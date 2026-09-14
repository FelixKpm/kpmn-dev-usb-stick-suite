using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AEGIS
{
    // Ein Release-Asset. Die Id ist entscheidend: bei privaten Repos funktioniert
    // browser_download_url nicht, der Download läuft ausschließlich über /releases/assets/{id}.
    public sealed record GitHubAsset(long Id, string Name, long Size);

    public sealed record GitHubRelease(string TagName, IReadOnlyList<GitHubAsset> Assets)
    {
        // Ein Suite-Release besteht aus genau einem ZIP-Asset mit dem fertigen Stick-Inhalt
        public GitHubAsset? ZipAsset =>
            Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    // Minimaler GitHub-API-Client für den Suite-Builder: neuestes Release abfragen,
    // Release-Asset laden und (für MABS) das komplette Repo als ZIP holen.
    //
    // Bewusst ohne jede Konfiguration durch den Anwender: Owner und Token stehen fest im Code.
    // Der Token ist ein fine-grained PAT mit ausschließlich "Contents: Read-only" auf genau die
    // drei Suite-Repos – aus einer verteilten EXE ist er zwar auslesbar, aber damit lässt sich
    // nichts anderes tun als das, was AEGIS ohnehin öffentlich für den Nutzer tut.
    //
    // Der eigentliche Token-Wert steht NICHT hier, sondern in GitHubReleaseService.Local.cs -
    // einer bewusst nicht versionierten Datei (siehe .gitignore), analog zum Signier-Zertifikat
    // in C:\Users\Koopm\Kpmn-Signing\. Grund: dieses Repo ist öffentlich, und GitHub scannt
    // Commits aktiv auf genau dieses Muster (Push Protection hat den ersten Versuch geblockt).
    // GitHubReleaseService.Local.cs.example zeigt die erwartete Form.
    public static partial class GitHubReleaseService
    {
        private const string Owner = "FelixKpm";

        private const string ApiVersion = "2022-11-28";
        private const string JsonAccept = "application/vnd.github+json";
        private const string BinaryAccept = "application/octet-stream";

        // GitHub beantwortet Requests ohne User-Agent grundsätzlich mit 403
        private const string UserAgent = "AEGIS-SuiteBuilder";

        // Eine wiederverwendete Instanz für alles Normale (kein new HttpClient() pro Aufruf).
        // Timeout großzügig, weil hierüber auch mehrere hundert MB große Stick-Images laufen.
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(60) };

        // Zweiter Client nur für den Repo-Archiv-Download: api.github.com antwortet dort mit 302 auf
        // codeload.github.com. Bei einem Host-Wechsel entfernt HttpClient den Authorization-Header
        // absichtlich – die codeload-URL ist aber ohnehin bereits signiert (?token=… im Query) und
        // braucht ihn nicht. Deshalb wird der Redirect hier von Hand ausgeführt: erst der
        // authentifizierte Request an die API, dann ein zweiter, bewusst unauthentifizierter an
        // die Location-Adresse. Das ist eindeutiger als sich auf das Redirect-Verhalten zu verlassen.
        private static readonly HttpClient NoRedirectHttp =
            new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(60) };

        public static async Task<GitHubRelease> GetLatestReleaseAsync(string repo, CancellationToken ct = default)
        {
            var url = $"https://api.github.com/repos/{Owner}/{repo}/releases/latest";

            using var request = CreateRequest(url, JsonAccept);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";

            var assets = new List<GitHubAsset>();
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";

                    long id = 0;
                    if (asset.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                        idProp.TryGetInt64(out id);

                    long size = 0;
                    if (asset.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number)
                        sizeProp.TryGetInt64(out size);

                    if (id != 0 && name.Length > 0)
                        assets.Add(new GitHubAsset(id, name, size));
                }
            }

            return new GitHubRelease(tag, assets);
        }

        // Lädt ein Release-Asset in eine Datei; meldet (gelesene Bytes, Gesamtbytes) über IProgress.
        // Entscheidend ist Accept: application/octet-stream – nur damit liefert die API die Datei
        // selbst statt der JSON-Beschreibung des Assets. Für private Repos ist das der einzige Weg.
        public static async Task DownloadAssetAsync(
            string repo,
            long assetId,
            string targetFile,
            IProgress<(long BytesRead, long TotalBytes)>? progress = null,
            CancellationToken ct = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

            var url = $"https://api.github.com/repos/{Owner}/{repo}/releases/assets/{assetId}";

            using var request = CreateRequest(url, BinaryAccept);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await StreamToFileAsync(response, targetFile, progress, ct).ConfigureAwait(false);
        }

        // Lädt den kompletten Repo-Inhalt als ZIP (Archiv des Standard-Branches).
        // Nötig für Repos ohne Release (z.B. MABS: nur Theme-Dateien im Repo, kein gebautes Stick-Image).
        public static async Task DownloadRepoArchiveAsync(
            string repo,
            string targetFile,
            IProgress<(long BytesRead, long TotalBytes)>? progress = null,
            CancellationToken ct = default)
        {
            var branch = await GetDefaultBranchAsync(repo, ct).ConfigureAwait(false);

            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

            var url = $"https://api.github.com/repos/{Owner}/{repo}/zipball/{Uri.EscapeDataString(branch)}";

            using var request = CreateRequest(url, JsonAccept);
            using var response = await NoRedirectHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (IsRedirect(response.StatusCode) && response.Headers.Location != null)
            {
                // zweiter Request ohne Token: die signierte codeload-URL braucht ihn nicht
                using var followUp = new HttpRequestMessage(HttpMethod.Get, response.Headers.Location);
                followUp.Headers.UserAgent.ParseAdd(UserAgent);

                using var archiveResponse = await NoRedirectHttp
                    .SendAsync(followUp, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                archiveResponse.EnsureSuccessStatusCode();

                await StreamToFileAsync(archiveResponse, targetFile, progress, ct).ConfigureAwait(false);
                return;
            }

            // Falls GitHub das Archiv doch einmal direkt ausliefert, funktioniert auch das
            response.EnsureSuccessStatusCode();
            await StreamToFileAsync(response, targetFile, progress, ct).ConfigureAwait(false);
        }

        private static bool IsRedirect(HttpStatusCode status) =>
            status == HttpStatusCode.Moved ||
            status == HttpStatusCode.Found ||
            status == HttpStatusCode.SeeOther ||
            status == HttpStatusCode.TemporaryRedirect ||
            status == HttpStatusCode.PermanentRedirect;

        private static async Task<string> GetDefaultBranchAsync(string repo, CancellationToken ct)
        {
            var url = $"https://api.github.com/repos/{Owner}/{repo}";

            using var request = CreateRequest(url, JsonAccept);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var branch = doc.RootElement.TryGetProperty("default_branch", out var branchProp) ? branchProp.GetString() ?? "" : "";
            return string.IsNullOrWhiteSpace(branch) ? "main" : branch;
        }

        // Gemeinsamer Download-Kern der beiden Download-Methoden: gestückeltes Kopieren mit
        // Fortschrittsmeldung. Bewusst hier und nicht in PackageDownloadService, weil dort nur
        // eine URL ohne Auth-Header geladen werden kann – der fertige Response muss übergeben werden.
        private static async Task StreamToFileAsync(
            HttpResponseMessage response,
            string targetFile,
            IProgress<(long BytesRead, long TotalBytes)>? progress,
            CancellationToken ct)
        {
            // Beim Repo-Archiv erzeugt GitHub das ZIP erst beim Ausliefern – dann fehlt Content-Length
            // und die Anzeige läuft über den Indeterminate-Zweig des Aufrufers (Gesamtgröße 0).
            var total = response.Content.Headers.ContentLength ?? 0;

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

        private static HttpRequestMessage CreateRequest(string url, string accept)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            return request;
        }
    }
}
