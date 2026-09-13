using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AEGIS
{
    // Ein Installer, den AVAS in seinem Ordner "Files" erwartet (Dateinamen laut packages.json).
    // Die eigentliche Download-Adresse wird erst zur Laufzeit ermittelt, weil kein Hersteller eine
    // dauerhaft gültige "immer aktuelle Version"-Datei-URL anbietet: mal ist es ein 302-Redirect,
    // mal eine Release-API, mal ein HTML-Verzeichnislisting.
    public sealed record PackageSpec(
        string Name,
        string TargetFileName,
        Func<HttpClient, CancellationToken, Task<string>> ResolveDownloadUrlAsync);

    // Lädt die Standard-Programme für einen frisch gebauten AVAS-Stick direkt beim jeweiligen Hersteller.
    // Kpmn Development hostet oder verteilt bewusst keine fremden Installer – deshalb wird immer live
    // von der offiziellen Quelle geladen, genau wie beim Intel-Treiberpaket im Treiber-Dialog.
    public static class PackageDownloadService
    {
        // Eine wiederverwendete Instanz für alle Hersteller-Downloads. Der User-Agent ist Pflicht:
        // GitHubs API und mehrere CDNs antworten anonymen Requests ohne User-Agent mit 403.
        // AllowAutoRedirect ist Standard – die 302-Ketten von Mozilla, Brave und Microsoft laufen damit von allein durch.
        internal static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            return client;
        }

        private const string VlcDirectoryUrl = "https://get.videolan.org/vlc/last/win64/";

        // Reihenfolge = Anzeigereihenfolge im Dialog und Download-Reihenfolge (bewusst sequenziell).
        public static readonly IReadOnlyList<PackageSpec> AllPackages = new[]
        {
            new PackageSpec("Mozilla Firefox", "firefox_setup.exe",
                (_, _) => Task.FromResult("https://download.mozilla.org/?product=firefox-latest&os=win64&lang=de")),

            // Google liefert die offizielle Silent-Install-Variante nur als MSI, nicht als EXE –
            // deshalb weicht hier als Einziges die Dateiendung von packages.json ab.
            new PackageSpec("Google Chrome", "chrome_setup.msi",
                (_, _) => Task.FromResult("https://dl.google.com/edgedl/chrome/install/GoogleChromeStandaloneEnterprise64.msi")),

            new PackageSpec("Brave Browser", "brave_setup.exe",
                (_, _) => Task.FromResult("https://laptop-updates.brave.com/latest/winx64")),

            new PackageSpec("7-Zip", "7zip_setup.exe",
                (http, ct) => ResolveGitHubAssetAsync(http, "ip7z/7zip",
                    name => name.EndsWith("-x64.exe", StringComparison.OrdinalIgnoreCase), ct)),

            new PackageSpec("VLC Media Player", "vlc_setup.exe", ResolveVlcAsync),

            new PackageSpec("Notepad++", "npp_setup.exe",
                (http, ct) => ResolveGitHubAssetAsync(http, "notepad-plus-plus/notepad-plus-plus",
                    name => name.EndsWith("Installer.x64.exe", StringComparison.OrdinalIgnoreCase), ct)),

            new PackageSpec("Visual Studio Code", "vscode_setup.exe",
                (_, _) => Task.FromResult("https://code.visualstudio.com/sha/download?build=stable&os=win32-x64")),
        };

        // Ermittelt die Adresse und lädt genau eine Datei nach <targetDirectory>\<TargetFileName>.
        // Fehler werden pro Paket abgefangen, damit ein toter Hersteller-Server nicht den ganzen Durchlauf killt.
        public static async Task<(string Name, bool Success, string? Error)> DownloadPackageAsync(
            PackageSpec spec,
            string targetDirectory,
            IProgress<(long BytesRead, long TotalBytes)>? progress,
            CancellationToken ct)
        {
            var targetFile = Path.Combine(targetDirectory, spec.TargetFileName);

            try
            {
                Directory.CreateDirectory(targetDirectory);

                var url = await spec.ResolveDownloadUrlAsync(Http, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(url))
                    throw new InvalidOperationException("Keine Download-Adresse gefunden.");

                await DownloadToFileAsync(url, targetFile, progress, ct).ConfigureAwait(false);
                return (spec.Name, true, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // halb geladene Datei nicht auf dem Stick liegen lassen
                TryDeletePartialFile(targetFile);
                throw;
            }
            catch (Exception ex)
            {
                TryDeletePartialFile(targetFile);
                return (spec.Name, false, ex.Message);
            }
        }

        // Gestückeltes Laden mit Fortschrittsmeldung – gemeinsamer Kern aller Nicht-Gitea-Downloads
        // (auch das Intel-Treiberpaket im Treiber-Dialog läuft hierüber).
        internal static async Task DownloadToFileAsync(
            string url,
            string targetFile,
            IProgress<(long BytesRead, long TotalBytes)>? progress,
            CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

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

        // 7-Zip und Notepad++ veröffentlichen nur über GitHub-Releases – dort steht die aktuelle
        // Version immer unter .../releases/latest, das passende Asset wird über den Dateinamen gesucht.
        // Auch DartToolDownloadService (System Informer) nutzt diesen Resolver, damit es die
        // GitHub-Sonderbehandlung (User-Agent, Accept-Header) im Projekt nur einmal gibt.
        internal static async Task<string> ResolveGitHubAssetAsync(
            HttpClient http, string repo, Func<string, bool> assetMatches, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"GitHub-Release von {repo} enthält keine Dateien.");

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                var url = asset.TryGetProperty("browser_download_url", out var urlProp) ? urlProp.GetString() ?? "" : "";

                if (name.Length > 0 && url.Length > 0 && assetMatches(name))
                    return url;
            }

            throw new InvalidOperationException($"Im aktuellen GitHub-Release von {repo} wurde keine passende Installationsdatei gefunden.");
        }

        // VideoLAN hat keine API, aber ein stabiles Verzeichnislisting: unter .../vlc/last/win64/ liegt
        // immer genau die aktuelle Version, der Dateiname enthält die Versionsnummer und muss daher
        // aus dem HTML gefischt werden (die .asc/.sha256/.msi-Varianten dabei ignorieren).
        private static async Task<string> ResolveVlcAsync(HttpClient http, CancellationToken ct)
        {
            var html = await http.GetStringAsync(VlcDirectoryUrl, ct).ConfigureAwait(false);

            var matches = Regex.Matches(html, @"href=""(?<file>vlc-[0-9][^""/]*-win64\.exe)""", RegexOptions.IgnoreCase);
            var fileName = matches
                .Select(m => m.Groups["file"].Value)
                .FirstOrDefault(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(fileName))
                throw new InvalidOperationException("Im VLC-Verzeichnis wurde keine win64-Installationsdatei gefunden.");

            return VlcDirectoryUrl + fileName;
        }

        private static void TryDeletePartialFile(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
    }
}
