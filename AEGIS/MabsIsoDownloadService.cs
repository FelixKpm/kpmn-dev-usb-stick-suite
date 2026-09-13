using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AEGIS
{
    // Ein Betriebssystem-Image, das AEGIS auf Wunsch direkt in die Wurzel der Ventoy-Datenpartition legt.
    // Ventoy bootet jede .iso-Datei, die dort liegt, von allein – es gibt kein Namensschema und keine
    // Konfiguration pro Image. Deshalb reicht hier die Download-Adresse; der Dateiname kommt aus der URL,
    // damit die Versionsnummer im Ventoy-Menü sichtbar bleibt.
    //
    // DescriptionKey/SizeHintKey sind Loc-Schlüssel, keine fertigen Texte: Der Dialog wird bei jedem
    // Öffnen neu aufgebaut, die Sprache kann sich zwischendurch geändert haben.
    public sealed record MabsIsoSpec(
        string Name,
        string DescriptionKey,
        Func<HttpClient, CancellationToken, Task<string>> ResolveDownloadUrlAsync);

    // Ergebnis eines einzelnen ISO-Downloads. Skipped = die Datei lag schon auf der Datenpartition
    // (ein zweiter Durchlauf soll keine 6 GB noch einmal ziehen).
    public sealed record MabsIsoResult(string Name, bool Success, bool Skipped, string? FileName, string? Error);

    // Holt Ubuntu und Kali live von den offiziellen Servern der jeweiligen Distribution.
    // Wie beim AVAS-Paketdownload und den DART-Tools hostet Kpmn Development selbst nichts weiter –
    // HttpClient und Download-Schleife kommen aus PackageDownloadService, damit es die Streaming-Logik
    // (und den zwingenden User-Agent) im Projekt nur einmal gibt.
    //
    // Windows fehlt hier bewusst: Microsofts ISO-Download ist sitzungsgebunden, die erzeugten Links
    // laufen nach 24 Stunden ab und lassen sich nicht automatisieren. Windows bleibt deshalb ein
    // manueller Schritt mit Link im Dialog (siehe ShowMabsIsoSetupDialog).
    public static class MabsIsoDownloadService
    {
        // Übersicht aller Ubuntu-Veröffentlichungen. Enthält je ein Verzeichnis pro Version
        // (z. B. "26.04/", "25.10/"); die LTS-Versionen sind immer die .04-Ausgaben gerader Jahre.
        private const string UbuntuReleasesUrl = "https://releases.ubuntu.com/";

        // Kali pflegt "current/" als Verweis auf die jeweils aktuelle Veröffentlichung –
        // damit bleibt die Adresse über Versionswechsel hinweg stabil.
        private const string KaliCurrentUrl = "https://cdimage.kali.org/current/";

        // Nur die reinen LTS-Verzeichnisse ("24.04/", "26.04/"), nicht die Punktversionen ("26.04.1/"):
        // das LTS-Verzeichnis enthält immer auch die aktuellste Punktversion.
        private static readonly Regex UbuntuLtsDirRegex =
            new(@"href=""(?<year>\d{2})\.04/""", RegexOptions.IgnoreCase);

        // Im Versionsverzeichnis liegen Desktop- und Server-Image nebeneinander; gebraucht wird das Desktop-Image.
        private static readonly Regex UbuntuDesktopIsoRegex =
            new(@"href=""(?<file>ubuntu-(?<version>[\d.]+)-desktop-amd64\.iso)""", RegexOptions.IgnoreCase);

        // Das vollständige Installer-Image (~5 GB). Das schließende Anführungszeichen direkt nach ".iso"
        // hält die .torrent-Einträge draußen, das feste "-installer-amd64" die Varianten
        // netinst, purple, everything und arm64.
        private static readonly Regex KaliInstallerIsoRegex =
            new(@"href=""(?<file>kali-linux-[\d.]+-installer-amd64\.iso)""", RegexOptions.IgnoreCase);

        // Reihenfolge = Anzeigereihenfolge im Dialog.
        public static readonly IReadOnlyList<MabsIsoSpec> AllIsos = new[]
        {
            new MabsIsoSpec("Ubuntu Desktop (LTS)", "SuiteBuilder.Mabs.Iso.UbuntuDescription", ResolveUbuntuAsync),
            new MabsIsoSpec("Kali Linux (Installer)", "SuiteBuilder.Mabs.Iso.KaliDescription", ResolveKaliAsync),
        };

        // Ermittelt die Adresse und lädt das Image in die Wurzel der Ventoy-Datenpartition.
        // Fehler werden abgefangen und zurückgemeldet, damit ein voller Stick oder ein toter Spiegelserver
        // weder den Dialog noch den fertigen MABS-Build kaputt macht; ein Abbruch fliegt dagegen nach oben.
        public static async Task<MabsIsoResult> DownloadIsoAsync(
            MabsIsoSpec spec,
            string dataDriveRoot,
            IProgress<(long BytesRead, long TotalBytes)>? progress,
            CancellationToken ct)
        {
            string? targetFile = null;

            try
            {
                var url = await spec.ResolveDownloadUrlAsync(PackageDownloadService.Http, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(url))
                    throw new InvalidOperationException(Loc.T("SuiteBuilder.Mabs.Iso.ErrorNoUrl"));

                var fileName = Path.GetFileName(new Uri(url).LocalPath);
                if (fileName.Length == 0)
                    throw new InvalidOperationException(Loc.T("SuiteBuilder.Mabs.Iso.ErrorNoUrl"));

                targetFile = Path.Combine(dataDriveRoot, fileName);

                // Schon vorhanden = vollständig: abgebrochene oder fehlgeschlagene Downloads löscht
                // dieser Service selbst wieder, es kann also keine halbe Datei stehen bleiben.
                if (File.Exists(targetFile))
                    return new MabsIsoResult(spec.Name, true, true, fileName, null);

                await PackageDownloadService.DownloadToFileAsync(url, targetFile, progress, ct).ConfigureAwait(false);

                return new MabsIsoResult(spec.Name, true, false, fileName, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // kein halbes Image auf dem Stick liegen lassen – das würde Ventoy im Menü anzeigen
                TryDeletePartialFile(targetFile);
                throw;
            }
            catch (Exception ex)
            {
                TryDeletePartialFile(targetFile);
                return new MabsIsoResult(spec.Name, false, false, null, ex.Message);
            }
        }

        // Canonical, ohne feste Version: erst das höchste LTS-Verzeichnis auf releases.ubuntu.com suchen
        // (LTS = .04 in geraden Jahren), darin dann das aktuellste Desktop-Image. Damit bleibt der
        // Download auch nach einem neuen LTS-Release ohne Codeänderung korrekt.
        private static async Task<string> ResolveUbuntuAsync(HttpClient http, CancellationToken ct)
        {
            var indexHtml = await http.GetStringAsync(UbuntuReleasesUrl, ct).ConfigureAwait(false);

            var latestLtsYear = UbuntuLtsDirRegex.Matches(indexHtml)
                .Select(m => int.TryParse(m.Groups["year"].Value, out var year) ? year : -1)
                .Where(year => year > 0 && year % 2 == 0)
                .DefaultIfEmpty(-1)
                .Max();

            if (latestLtsYear < 0)
                throw new InvalidOperationException(Loc.T("SuiteBuilder.Mabs.Iso.ErrorUbuntuNoRelease"));

            var releaseUrl = $"{UbuntuReleasesUrl}{latestLtsYear:00}.04/";
            var releaseHtml = await http.GetStringAsync(releaseUrl, ct).ConfigureAwait(false);

            // Im LTS-Verzeichnis stehen mehrere Punktversionen nebeneinander (z. B. 26.04 und 26.04.1)
            var newest = UbuntuDesktopIsoRegex.Matches(releaseHtml)
                .Select(m => new
                {
                    File = m.Groups["file"].Value,
                    Version = Version.TryParse(m.Groups["version"].Value, out var v) ? v : null
                })
                .Where(x => x.Version != null)
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();

            if (newest == null)
                throw new InvalidOperationException(Loc.T("SuiteBuilder.Mabs.Iso.ErrorUbuntuNoIso"));

            return releaseUrl + newest.File;
        }

        // Kali veröffentlicht keine versionslose Dateiadresse: der Dateiname enthält die Version und wird
        // deshalb aus dem Verzeichnislisting von "current/" gefischt. Die Datei selbst liegt hinter einer
        // Weiterleitung auf kali.download – die läuft mit AllowAutoRedirect von allein durch.
        private static async Task<string> ResolveKaliAsync(HttpClient http, CancellationToken ct)
        {
            var html = await http.GetStringAsync(KaliCurrentUrl, ct).ConfigureAwait(false);

            var match = KaliInstallerIsoRegex.Match(html);
            if (!match.Success)
                throw new InvalidOperationException(Loc.T("SuiteBuilder.Mabs.Iso.ErrorKaliNoIso"));

            return KaliCurrentUrl + match.Groups["file"].Value;
        }

        private static void TryDeletePartialFile(string? file)
        {
            try { if (file != null && File.Exists(file)) File.Delete(file); } catch { }
        }
    }
}
