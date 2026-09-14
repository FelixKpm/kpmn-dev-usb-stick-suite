using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AEGIS
{
    // Ergebnis eines Ventoy-Installationslaufs; ErrorMessage ist nur im Fehlerfall gesetzt
    public sealed record InstallResult(bool Success, string? ErrorMessage);

    // Holt Ventoy zur Laufzeit von Ventoys eigenem GitHub-Release und installiert es auf einen Stick.
    // Bewusst nichts vorgehalten oder mitgeliefert: Kpmn Development verteilt keine Fremdsoftware weiter.
    public static class VentoyService
    {
        private const string VentoyLatestReleaseApi = "https://api.github.com/repos/ventoy/Ventoy/releases/latest";
        private const string Ventoy2DiskExeName = "Ventoy2Disk.exe";

        // ERROR_CANCELLED: der Nutzer hat die UAC-Abfrage weggeklickt
        private const int ErrorCancelled = 1223;

        // Cache liegt neben der EXE (also auf dem Stick) – wie alle anderen persistenten AEGIS-Daten auch
        private static readonly string CacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "Ventoy");

        // GitHubs API antwortet ohne User-Agent mit 403 – derselbe Stolperstein wie bei Intels Downloadserver
        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AEGIS-SuiteBuilder/1.0 (+Kpmn-Development)");
            return client;
        }

        // Liefert den Pfad zur lokal entpackten Ventoy2Disk.exe; lädt Ventoy nur beim ersten Mal herunter.
        public static async Task<string> EnsureVentoyAsync(
            IProgress<(long BytesRead, long TotalBytes)>? progress = null,
            CancellationToken ct = default)
        {
            Directory.CreateDirectory(CacheDir);

            var cached = FindVentoy2Disk(CacheDir);
            if (cached != null)
                return cached;

            var (downloadUrl, assetSize) = await GetWindowsPackageUrlAsync(ct).ConfigureAwait(false);

            var tempZip = Path.Combine(CacheDir, $"ventoy-download-{DateTime.Now:yyyyMMddHHmmss}.zip");

            try
            {
                using (var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    var total = response.Content.Headers.ContentLength ?? assetSize;

                    using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var target = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                    var buffer = new byte[81920];
                    long readTotal = 0;
                    long lastReported = 0;
                    int read;

                    while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        readTotal += read;

                        // wie bei den übrigen Downloads nur alle ~256 KB melden, sonst flutet der Dispatcher
                        if (progress != null && readTotal - lastReported >= 262144)
                        {
                            lastReported = readTotal;
                            progress.Report((readTotal, total));
                        }
                    }

                    progress?.Report((readTotal, total));
                }

                await Task.Run(() => ZipFile.ExtractToDirectory(tempZip, CacheDir, overwriteFiles: true), ct).ConfigureAwait(false);
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            }

            // Ventoy packt alles unter einen versionsbenannten Unterordner, daher rekursiv suchen
            var extracted = FindVentoy2Disk(CacheDir);
            if (extracted == null)
                throw new InvalidOperationException($"{Ventoy2DiskExeName} was not found in the downloaded Ventoy package.");

            return extracted;
        }

        // Führt Ventoy2Disk im CLI-Modus aus und meldet den Fortschritt aus dessen Statusdateien.
        // Wirft bewusst nie nach oben – die UI bekommt Fehler immer als InstallResult zurück.
        public static async Task<InstallResult> RunInstallAsync(
            string ventoy2DiskExePath,
            string driveRoot,
            IProgress<int>? percentProgress,
            CancellationToken ct)
        {
            try
            {
                var workingDir = Path.GetDirectoryName(ventoy2DiskExePath)!;

                // "F:\" -> "F:" (Ventoy erwartet den Laufwerksbuchstaben mit Doppelpunkt, ohne Backslash)
                var driveLetter = driveRoot.TrimEnd('\\', '/');

                var percentFile = Path.Combine(workingDir, "cli_percent.txt");
                var doneFile = Path.Combine(workingDir, "cli_done.txt");
                var logFile = Path.Combine(workingDir, "cli_log.txt");

                // Reste eines früheren Laufs entfernen, sonst wird sofort ein altes Ergebnis gelesen
                foreach (var stale in new[] { percentFile, doneFile })
                {
                    try { if (File.Exists(stale)) File.Delete(stale); } catch { }
                }

                // Ventoy schreibt direkt auf den physischen Datenträger und braucht dafür Adminrechte,
                // deshalb per ShellExecute + "runas" starten (UAC-Abfrage). Damit sind CreateNoWindow und
                // die Umleitung von stdout/stderr nicht mehr möglich – beides wird auch nicht gebraucht,
                // Fortschritt und Ergebnis kommen ausschließlich aus cli_percent.txt / cli_done.txt / cli_log.txt.
                var startInfo = new ProcessStartInfo
                {
                    FileName = ventoy2DiskExePath,
                    Arguments = $"VTOYCLI /I /Drive:{driveLetter} /GPT /NOUSBCheck",
                    WorkingDirectory = workingDir,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return new InstallResult(false, $"Could not start {Path.GetFileName(ventoy2DiskExePath)}.");

                var graceRounds = 0;

                while (true)
                {
                    var percent = ReadPercent(percentFile);
                    if (percent >= 0)
                        percentProgress?.Report(percent);

                    if (process.HasExited)
                    {
                        // cli_done.txt wird teils minimal nach dem Prozessende geschrieben – kurz darauf warten,
                        // danach trotzdem abbrechen (dann eben ohne verlässliches Erfolgssignal aus der Datei)
                        if (File.Exists(doneFile) || graceRounds >= 6)
                            break;

                        graceRounds++;
                    }

                    await Task.Delay(500, ct).ConfigureAwait(false);
                }

                // cli_done.txt enthält einen reinen Ergebniscode: 0 = Erfolg, alles andere ein Fehlercode.
                // Fehlt die Datei ganz, bleibt nur der Exitcode des Prozesses als Ersatzsignal.
                var doneText = ReadTextSafe(doneFile).Trim();

                bool success;
                string? failureReason = null;

                if (doneText.Length == 0)
                {
                    success = process.ExitCode == 0;
                    if (!success)
                        failureReason = $"Ventoy2Disk exited with code {process.ExitCode} and wrote no result file.";
                }
                else if (int.TryParse(doneText, out var resultCode))
                {
                    success = resultCode == 0;
                    if (!success)
                        failureReason = $"Ventoy2Disk reported error code {resultCode}.";
                }
                else
                {
                    success = false;
                    failureReason = $"Ventoy2Disk wrote an unexpected result to cli_done.txt: \"{doneText}\".";
                }

                if (!success)
                {
                    var log = ReadTextSafe(logFile).Trim();
                    if (log.Length > 2000)
                        log = log[^2000..];

                    var message = failureReason ?? $"Ventoy2Disk exited with code {process.ExitCode}.";
                    if (log.Length > 0)
                        message = $"{message}\n\n{log}";

                    return new InstallResult(false, message);
                }

                percentProgress?.Report(100);
                return new InstallResult(true, null);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                // Nutzer hat die UAC-Abfrage abgebrochen – kein echter Fehler, aber ohne Adminrechte geht nichts
                return new InstallResult(false, Loc.T("SuiteBuilder.Mabs.UacCancelled"));
            }
            catch (Exception ex)
            {
                return new InstallResult(false, ex.Message);
            }
        }

        // Sucht Ventoy2Disk.exe irgendwo unterhalb des Cache-Ordners
        private static string? FindVentoy2Disk(string root)
        {
            try
            {
                return Directory.EnumerateFiles(root, Ventoy2DiskExeName, SearchOption.AllDirectories).FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        // Fragt das aktuelle Ventoy-Release bei GitHub ab und sucht das Windows-ZIP heraus
        private static async Task<(string Url, long Size)> GetWindowsPackageUrlAsync(CancellationToken ct)
        {
            var json = await Http.GetStringAsync(VentoyLatestReleaseApi, ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("The Ventoy release information contains no assets.");

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                var url = asset.TryGetProperty("browser_download_url", out var urlProp) ? urlProp.GetString() ?? "" : "";

                if (name.Length == 0 || url.Length == 0)
                    continue;

                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    !name.Contains("windows", StringComparison.OrdinalIgnoreCase))
                    continue;

                long size = 0;
                if (asset.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number)
                    sizeProp.TryGetInt64(out size);

                return (url, size);
            }

            throw new InvalidOperationException("The latest Ventoy release contains no Windows ZIP package.");
        }

        // -1, wenn die Datei (noch) nicht existiert oder keine Zahl enthält
        private static int ReadPercent(string percentFile)
        {
            var text = ReadTextSafe(percentFile).Trim();
            return int.TryParse(text, out var percent) ? Math.Clamp(percent, 0, 100) : -1;
        }

        // Ventoy schreibt seine Statusdateien parallel weiter, daher lesend mit FileShare.ReadWrite öffnen
        private static string ReadTextSafe(string file)
        {
            try
            {
                if (!File.Exists(file))
                    return "";

                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch
            {
                return "";
            }
        }
    }
}
