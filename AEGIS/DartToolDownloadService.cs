using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AEGIS
{
    // Ein portables Diagnose-Tool, das DART auf dem Stick in einem festen, versionslosen Ordner erwartet
    // (z. B. "01_Hardware\CPU-Z"). Die Ordnernamen in DARTs Buttons sind bewusst ohne Versionsnummer,
    // weil AEGIS hier immer die aktuelle Version hineinlegt.
    //
    // EntryFilter == null  -> das komplette Archiv wird in den Zielordner entpackt (ganze Tool-Sammlungen).
    // EntryFilter != null  -> nur die passenden Archiv-Einträge werden flach in den Zielordner kopiert.
    // Der Filter bekommt den Pfad innerhalb des Archivs mit Backslashes (z. B. "amd64\SystemInformer.exe"),
    // nicht nur den Dateinamen: System Informer liefert dieselbe EXE mehrfach in verschiedenen
    // Plattform-Unterordnern, ein reiner Dateinamensvergleich würde die falsche Variante überschreiben.
    public sealed record DartToolSpec(
        string Name,
        string TargetRelativeFolder,
        Func<HttpClient, CancellationToken, Task<string>> ResolveDownloadUrlAsync,
        Func<string, bool>? EntryFilter = null);

    // Holt die automatisierbaren portablen Diagnose-Tools für einen frisch gebauten DART-Stick direkt
    // beim jeweiligen Anbieter (HWiNFO und CrystalDiskInfo sind es nicht – siehe Kommentar unten).
    // Wie beim AVAS-Paketdownload hostet Kpmn Development selbst keine Fremdsoftware – es wird immer live
    // von der offiziellen Quelle geladen. HttpClient und Download-Schleife kommen aus PackageDownloadService,
    // damit es die Streaming-Logik (und den zwingenden User-Agent) im Projekt nur einmal gibt.
    public static class DartToolDownloadService
    {
        private const string CpuZPageUrl = "https://www.cpuid.com/softwares/cpu-z.html";

        // www.cpuid.com liefert auch für eine .zip-Adresse eine HTML-Zwischenseite ("your download will start…"),
        // die echte Datei liegt auf dem separaten Downloadhost.
        private const string CpuZDownloadHost = "https://download.cpuid.com/cpu-z/";

        // Die Ziffern müssen unmittelbar von "-en.zip" gefolgt werden – damit fallen die Varianten
        // -cn, -win9x, -rog-en und -msi-en von allein raus.
        private static readonly Regex CpuZZipRegex = new(@"cpu-z_[\d.]+-en\.zip", RegexOptions.IgnoreCase);

        // HWiNFO und CrystalDiskInfo fehlen hier bewusst: Beide sind weder über ihre eigene Seite noch
        // über ihren SourceForge-Spiegel automatisierbar. SourceForge antwortet HttpClient-Anfragen mit
        // einer Cloudflare-Bot-Prüfung (403 "Just a moment…"), und die greift an der TLS-Signatur des
        // Clients an – ein anderer User-Agent ändert daran nichts. Beide Tools stehen deshalb als
        // manueller Schritt mit Link im Dialog (siehe ShowDartToolsSetupDialog).
        private const string WifiChannelMonitorUrl = "https://www.nirsoft.net/utils/wifichannelmonitor-x64.zip";
        private const string SysinternalsSuiteUrl = "https://download.sysinternals.com/files/SysinternalsSuite.zip";
        private const string TreeSizeFreeUrl = "https://downloads.jam-software.de/treesize_free/TreeSizeFree-Portable.zip?language=EN";

        // Reihenfolge = Anzeigereihenfolge im Dialog und Download-Reihenfolge (bewusst sequenziell).
        public static readonly IReadOnlyList<DartToolSpec> AllTools = new[]
        {
            new DartToolSpec("CPU-Z", @"01_Hardware\CPU-Z", ResolveCpuZAsync,
                entry => IsFileNamed(entry, "cpuz_x64.exe") || IsFileNamed(entry, "cpuz_x32.exe")),

            // Winzig: EXE, Readme und CHM-Hilfedatei – alles übernehmen.
            new DartToolSpec("WifiChannelMonitor", @"03_Netzwerk\WifiChannelMonitor",
                (_, _) => Task.FromResult(WifiChannelMonitorUrl)),

            // Nachfolger des eingestellten Process Hacker (gleiche Entwickler). Das "-bin"-Asset ist die
            // portable Version; "-release-setup.exe" wäre der Installer und damit für den Stick unbrauchbar.
            new DartToolSpec("System Informer", @"05_Prozesse\SystemInformer",
                (http, ct) => PackageDownloadService.ResolveGitHubAssetAsync(http, "winsiderss/systeminformer",
                    name => name.EndsWith("-bin.zip", StringComparison.OrdinalIgnoreCase), ct),
                // Nur die 64-Bit-Hauptdatei direkt unter "amd64\" – nicht die aus "amd64\x86\",
                // "i386\" oder "arm64\", und auch nicht die mitgelieferte peview.exe.
                entry => entry.Equals(@"amd64\SystemInformer.exe", StringComparison.OrdinalIgnoreCase)),

            // Ganze Werkzeugsammlung – DARTs Button öffnet hier bewusst nur den Ordner.
            new DartToolSpec("Sysinternals Suite", @"05_Prozesse\SysinternalsSuite",
                (_, _) => Task.FromResult(SysinternalsSuiteUrl)),

            // Enthält genau eine Datei (TreeSizeFree.exe), wird aber generisch wie die anderen behandelt.
            new DartToolSpec("TreeSize Free", @"06_Sonstiges\TreeSize",
                (_, _) => Task.FromResult(TreeSizeFreeUrl)),
        };

        // Ermittelt die Adresse, lädt das Archiv in den Temp-Ordner und legt das Ergebnis unter
        // <driveRoot>\<TargetRelativeFolder> ab. Fehler werden pro Tool abgefangen, damit ein toter
        // Anbieter-Server nicht den ganzen Durchlauf killt; ein Abbruch beendet dagegen den Durchlauf.
        public static async Task<(string Name, bool Success, string? Error)> DownloadToolAsync(
            DartToolSpec spec,
            string driveRoot,
            IProgress<(long BytesRead, long TotalBytes)>? progress,
            CancellationToken ct)
        {
            var targetFolder = Path.Combine(driveRoot, spec.TargetRelativeFolder);
            var tempRoot = Path.Combine(Path.GetTempPath(), "AEGIS-DART-" + Guid.NewGuid().ToString("N"));
            var zipFile = Path.Combine(tempRoot, "tool.zip");
            var extractFolder = Path.Combine(tempRoot, "extract");

            try
            {
                Directory.CreateDirectory(tempRoot);
                Directory.CreateDirectory(targetFolder);

                var url = await spec.ResolveDownloadUrlAsync(PackageDownloadService.Http, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(url))
                    throw new InvalidOperationException(Loc.T("DartToolSetup.ErrorNoUrl"));

                await PackageDownloadService.DownloadToFileAsync(url, zipFile, progress, ct).ConfigureAwait(false);

                if (spec.EntryFilter == null)
                {
                    // Ganzes Archiv direkt an den Zielort – spart bei den großen Sammlungen
                    // (Sysinternals) das doppelte Schreiben über einen Zwischenordner.
                    await Task.Run(() => ZipFile.ExtractToDirectory(zipFile, targetFolder, overwriteFiles: true), ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    await Task.Run(() => ExtractSelectedEntries(zipFile, extractFolder, targetFolder, spec.EntryFilter), ct)
                        .ConfigureAwait(false);
                }

                return (spec.Name, true, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (spec.Name, false, ex.Message);
            }
            finally
            {
                TryDeleteFolder(tempRoot);
            }
        }

        // Entpackt das Archiv in den Temp-Ordner und kopiert nur die passenden Einträge flach
        // in den Zielordner (die Unterordner-Struktur des Archivs interessiert DART nicht).
        private static void ExtractSelectedEntries(
            string zipFile, string extractFolder, string targetFolder, Func<string, bool> entryFilter)
        {
            ZipFile.ExtractToDirectory(zipFile, extractFolder, overwriteFiles: true);

            var copied = 0;

            foreach (var file in Directory.EnumerateFiles(extractFolder, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(extractFolder, file).Replace('/', '\\');
                if (!entryFilter(relative))
                    continue;

                File.Copy(file, Path.Combine(targetFolder, Path.GetFileName(file)), overwrite: true);
                copied++;
            }

            if (copied == 0)
                throw new InvalidOperationException(Loc.T("DartToolSetup.ErrorNoMatchingFiles"));
        }

        // CPUID veröffentlicht keine feste "latest"-Adresse: der Dateiname enthält die Version und muss
        // aus dem HTML der Produktseite gefischt werden. Der erste Treffer ist die aktuelle Version.
        private static async Task<string> ResolveCpuZAsync(HttpClient http, CancellationToken ct)
        {
            var html = await http.GetStringAsync(CpuZPageUrl, ct).ConfigureAwait(false);

            var match = CpuZZipRegex.Match(html);
            if (!match.Success)
                throw new InvalidOperationException(Loc.T("DartToolSetup.ErrorCpuZNoZip"));

            return CpuZDownloadHost + match.Value;
        }

        // Vergleich nur über den Dateinamen – egal, in welchem Unterordner des Archivs die Datei liegt.
        private static bool IsFileNamed(string archiveRelativePath, string fileName)
            => Path.GetFileName(archiveRelativePath).Equals(fileName, StringComparison.OrdinalIgnoreCase);

        private static void TryDeleteFolder(string folder)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch { }
        }
    }
}
