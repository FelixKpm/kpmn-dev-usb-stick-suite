using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AEGIS
{
    // Zugangsdaten für den selbst gehosteten Gitea-Server (Suite-Builder).
    // Liegen bewusst NICHT im Quellcode, sondern werden zur Laufzeit unter Settings\gitea.json
    // neben der EXE gespeichert (also unterhalb von bin\ bzw. auf dem Stick) – analog zu Loc.Initialize()/Save().
    public sealed class GiteaSettings
    {
        private static readonly string SettingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings", "gitea.json");

        public string BaseUrl { get; set; } = "";
        public string Org { get; set; } = "";
        public string Token { get; set; } = "";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(BaseUrl) &&
            !string.IsNullOrWhiteSpace(Org) &&
            !string.IsNullOrWhiteSpace(Token);

        public static GiteaSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var loaded = JsonSerializer.Deserialize<GiteaSettings>(json);
                    if (loaded != null)
                    {
                        loaded.BaseUrl = (loaded.BaseUrl ?? "").Trim();
                        loaded.Org = (loaded.Org ?? "").Trim();
                        loaded.Token = (loaded.Token ?? "").Trim();
                        return loaded;
                    }
                }
            }
            catch
            {
                // Datei fehlt, ist beschädigt oder nicht lesbar – dann eben unkonfiguriert starten
            }

            return new GiteaSettings();
        }

        public bool Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
                    new { BaseUrl, Org, Token },
                    new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            catch
            {
                // z.B. schreibgeschützter Stick – der Aufrufer zeigt dann einen Hinweis an
                return false;
            }
        }
    }

    public sealed record GiteaAsset(string Name, string DownloadUrl, long Size);

    public sealed record GiteaRelease(string TagName, IReadOnlyList<GiteaAsset> Assets)
    {
        // Ein Suite-Release besteht aus genau einem ZIP-Asset mit dem fertigen Stick-Inhalt
        public GiteaAsset? ZipAsset =>
            Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    // Minimaler Gitea-API-Client: neuestes Release abfragen + Release-Asset herunterladen.
    // Bewusst eine einzige, wiederverwendete HttpClient-Instanz (kein new HttpClient() pro Aufruf).
    public static class GiteaService
    {
        private static readonly HttpClient Http = new()
        {
            // großzügig, weil hierüber auch mehrere hundert MB große Stick-Images laufen
            Timeout = TimeSpan.FromMinutes(60)
        };

        public static async Task<GiteaRelease> GetLatestReleaseAsync(GiteaSettings settings, string repo, CancellationToken ct = default)
        {
            var url = $"{settings.BaseUrl.TrimEnd('/')}/api/v1/repos/{settings.Org}/{repo}/releases/latest";

            using var request = CreateRequest(url, settings);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";

            var assets = new List<GiteaAsset>();
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    var downloadUrl = asset.TryGetProperty("browser_download_url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                    long size = 0;
                    if (asset.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number)
                        sizeProp.TryGetInt64(out size);

                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(downloadUrl))
                        assets.Add(new GiteaAsset(name, downloadUrl, size));
                }
            }

            return new GiteaRelease(tag, assets);
        }

        // Lädt ein Release-Asset in eine Datei; meldet (gelesene Bytes, Gesamtbytes) über IProgress.
        // Private Repos verlangen den Token auch beim Anhang-Download, deshalb derselbe Header wie bei der API.
        public static async Task DownloadAssetAsync(
            GiteaSettings settings,
            GiteaAsset asset,
            string targetFile,
            IProgress<(long BytesRead, long TotalBytes)>? progress = null,
            CancellationToken ct = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

            using var request = CreateRequest(ResolveDownloadUrl(settings, asset.DownloadUrl), settings);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? asset.Size;

            using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var target = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long readTotal = 0;
            long lastReported = 0;
            int read;

            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                readTotal += read;

                // nicht bei jedem Chunk melden, sonst flutet der Dispatcher (alle ~256 KB reicht für die Anzeige)
                if (progress != null && readTotal - lastReported >= 262144)
                {
                    lastReported = readTotal;
                    progress.Report((readTotal, total));
                }
            }

            progress?.Report((readTotal, total));
        }

        private static HttpRequestMessage CreateRequest(string url, GiteaSettings settings)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("token", settings.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        // Gitea baut browser_download_url aus seiner konfigurierten ROOT_URL. Weicht die vom eingetragenen
        // Server-Host ab (typisch bei Docker/Reverse-Proxy-Setups), wäre der Download von außen nicht erreichbar –
        // deshalb Host/Port der URL auf den konfigurierten Server umbiegen, Pfad bleibt unverändert.
        private static string ResolveDownloadUrl(GiteaSettings settings, string downloadUrl)
        {
            try
            {
                if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var assetUri))
                    return $"{settings.BaseUrl.TrimEnd('/')}/{downloadUrl.TrimStart('/')}";

                if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri))
                    return downloadUrl;

                if (string.Equals(assetUri.Authority, baseUri.Authority, StringComparison.OrdinalIgnoreCase))
                    return downloadUrl;

                var rebuilt = new UriBuilder(assetUri)
                {
                    Scheme = baseUri.Scheme,
                    Host = baseUri.Host,
                    Port = baseUri.IsDefaultPort ? -1 : baseUri.Port
                };
                return rebuilt.Uri.ToString();
            }
            catch
            {
                return downloadUrl;
            }
        }
    }
}
