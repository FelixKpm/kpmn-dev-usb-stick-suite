using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AEGIS
{
    public enum AppLanguage { De, En }

    // Einfaches Dictionary-basiertes Übersetzungssystem (kein XAML-Databinding, da die UI komplett in C# gebaut wird).
    // Loc.T("Key") liefert den Text in der aktuell gewählten Sprache; SetLanguage löst LanguageChanged aus,
    // worauf MainWindow die statischen Topbar-Texte neu setzt und den aktuell offenen Tab neu aufbaut.
    public static class Loc
    {
        private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings", "settings.json");

        public static AppLanguage Current { get; private set; } = AppLanguage.De;

        public static event Action? LanguageChanged;

        public static void Initialize()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                    if (doc.RootElement.TryGetProperty("Language", out var langProp) && langProp.GetString() == "en")
                        Current = AppLanguage.En;
                }
            }
            catch
            {
                Current = AppLanguage.De;
            }
        }

        public static void SetLanguage(AppLanguage language)
        {
            if (Current == language)
                return;

            Current = language;
            Save();
            LanguageChanged?.Invoke();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { Language = Current == AppLanguage.En ? "en" : "de" }));
            }
            catch
            {
                // Einstellung konnte nicht gespeichert werden (z.B. schreibgeschützter Stick) – nicht kritisch
            }
        }

        public static string T(string key)
        {
            var dict = Current == AppLanguage.En ? EnStrings : DeStrings;
            if (dict.TryGetValue(key, out var value))
                return value;

            // Fallback: deutscher Text, falls der Schlüssel in der aktuellen Sprache fehlt; sonst der Schlüssel selbst
            return DeStrings.TryGetValue(key, out var de) ? de : key;
        }

        // Interne Ordnernamen (Desktop/Dokumente/Bilder/...) bleiben auf der Platte immer Deutsch,
        // damit sich Backup-Pfade beim Sprachwechsel nicht plötzlich ändern. Nur die Anzeige übersetzt.
        private static readonly Dictionary<string, string> FolderLocKeys = new()
        {
            ["Desktop"] = "Folder.Desktop",
            ["Dokumente"] = "Folder.Documents",
            ["Bilder"] = "Folder.Pictures",
            ["Videos"] = "Folder.Videos",
            ["Musik"] = "Folder.Music",
            ["Downloads"] = "Folder.Downloads",
        };

        public static string FolderDisplayName(string internalName) =>
            FolderLocKeys.TryGetValue(internalName, out var key) ? T(key) : internalName;

        private static readonly Dictionary<string, string> DeStrings = new()
        {
            // App / Topbar
            ["App.RecognizedSticks"] = "erkannte Sticks",
            ["App.BuildDate"] = "Build: {0}",
            ["Tab.Dokumente"] = "Dokumente",
            ["Tab.Templates"] = "Templates",
            ["Tab.Vault"] = "Vault",
            ["Tab.Backups"] = "Geräte Backups",
            ["Tab.SuiteBuilder"] = "Suite Builder",
            ["Status.DetectingSticks"] = "~/aegis $  Sticks werden erkannt...",
            ["Tab.ComingSoon"] = "{0}-Tab kommt hier hin",

            // Gemeinsam
            ["Common.Cancel"] = "Abbrechen",
            ["Common.Ok"] = "OK",
            ["Common.ConfirmRemoval"] = "Entfernen bestätigen",
            ["Common.ConfirmDeletion"] = "Löschen bestätigen",
            ["UsbNotDetected.Title"] = "USB-Stick nicht erkannt",
            ["UsbNotDetected.Message"] = "AEGIS muss von einem Wechseldatenträger gestartet werden,\num auf die Dokumentenstruktur zugreifen zu können.",
            ["Status.NoUsbStick"] = "~/aegis $  Kein USB-Stick erkannt",

            // Dokumente-Tab
            ["Dokumente.FolderHeader"] = "DOKUMENTE",
            ["Dokumente.SelectFolder"] = "WÄHLE EINEN ORDNER",
            ["Dokumente.SelectFileHint"] = "Wähle eine Datei aus um Details zu sehen.",
            ["Dokumente.Back"] = "⬆ Zurück",
            ["Dokumente.FolderNotFound"] = "~/aegis $  Ordner nicht gefunden: {0}",
            ["Dokumente.EntriesInFolder"] = "~/aegis $  {0} Einträge in {1}",
            ["Dokumente.ReadError"] = "~/aegis $  Fehler beim Lesen: {0}",
            ["Dokumente.StickNotDetectedTitle"] = "{0}-Stick nicht erkannt",
            ["Dokumente.StickNotDetectedMessage"] = "Bitte den {0}-Stick einstecken oder einen anderen USB-Port bzw. Stick verwenden.",
            ["Dokumente.StickNotDetectedStatus"] = "~/aegis $  {0}-Stick nicht erkannt",
            ["Dokumente.IsAFolder"] = "\"{0}\" ist ein Ordner.",
            ["Dokumente.PreviewError"] = "Fehler: {0}",
            ["Dokumente.ContentPreviewComingSoon"] = "Inhaltsvorschau folgt in einer späteren Version.",
            ["Dokumente.LastModified"] = "{0}  •  zuletzt geändert {1}",
            ["Dokumente.ErrorCreatingFolderStructure"] = "~/aegis $  Fehler beim Anlegen der Ordnerstruktur: {0}",

            // Vault-Tab
            ["Vault.SetupTitle"] = "Vault einrichten",
            ["Vault.SetupDescription"] = "Noch kein Vault vorhanden. Lege ein Master-Passwort fest, um verschlüsselte Dateien lokal auf diesem Stick zu speichern.\nOhne dieses Passwort sind die Daten unwiderruflich verloren – es gibt keine Wiederherstellung.",
            ["Vault.CreateButton"] = "Vault erstellen",
            ["Vault.LockedTitle"] = "Vault gesperrt",
            ["Vault.LockedDescription"] = "Gib dein Master-Passwort ein, um den Vault zu entsperren.",
            ["Vault.UnlockButton"] = "Entsperren",
            ["Vault.ErrorEnterPassword"] = "Bitte ein Passwort eingeben.",
            ["Vault.ErrorPasswordMismatch"] = "Die Passwörter stimmen nicht überein.",
            ["Vault.ErrorCreating"] = "Fehler beim Anlegen: {0}",
            ["Vault.WrongPassword"] = "Falsches Passwort.",
            ["Vault.ErrorUnlocking"] = "Fehler beim Entsperren: {0}",
            ["Vault.Header"] = "VAULT",
            ["Vault.AddFile"] = "+ Datei hinzufügen",
            ["Vault.Remove"] = "Entfernen",
            ["Vault.Lock"] = "🔒 Sperren",
            ["Vault.EntriesCount"] = "~/aegis $  {0} Datei(en) im Vault",
            ["Vault.ErrorReading"] = "~/aegis $  Fehler beim Lesen des Vaults: {0}",
            ["Vault.AddFileDialogTitle"] = "Dateien in den Vault aufnehmen",
            ["Vault.ErrorAdding"] = "~/aegis $  Fehler beim Hinzufügen: {0}",
            ["Vault.RemoveConfirmTitle"] = "Datei entfernen",
            ["Vault.RemoveConfirmMessage"] = "Durch Bestätigen wird \"{0}\" unwiderruflich aus dem Vault entfernt.",
            ["Vault.ErrorRemoving"] = "~/aegis $  Fehler beim Entfernen: {0}",
            ["Vault.OpenedTemporarily"] = "~/aegis $  \"{0}\" entschlüsselt geöffnet (temporär)",
            ["Vault.ErrorOpening"] = "~/aegis $  Fehler beim Öffnen: {0}",
            ["Vault.AutoLockedStatus"] = "~/aegis $  Vault automatisch gesperrt (Inaktivität)",
            ["Vault.InvalidFile"] = "Ungültige oder beschädigte Vault-Datei.",

            // Templates-Tab: Dokumentvorlagen
            ["Templates.DocsSubTab"] = "Dokumentvorlagen",
            ["Templates.SnippetsSubTab"] = "Text-Bausteine",
            ["Templates.AddDoc"] = "+ Vorlage hinzufügen",
            ["Templates.RemoveDoc"] = "Entfernen",
            ["Templates.UseDoc"] = "Neue Datei aus Vorlage…",
            ["Templates.DocsCount"] = "~/aegis $  {0} Vorlage(n)",
            ["Templates.ErrorReadingDocs"] = "~/aegis $  Fehler beim Lesen der Vorlagen: {0}",
            ["Templates.AddDialogTitle"] = "Dateien als Vorlage hinzufügen",
            ["Templates.ErrorAddingDoc"] = "~/aegis $  Fehler beim Hinzufügen: {0}",
            ["Templates.RemoveDocConfirmTitle"] = "Vorlage entfernen",
            ["Templates.RemoveDocConfirmMessage"] = "Durch Bestätigen wird die Vorlage \"{0}\" unwiderruflich entfernt.",
            ["Templates.ErrorRemovingDoc"] = "~/aegis $  Fehler beim Entfernen: {0}",
            ["Templates.SaveAsDialogTitle"] = "Neue Datei aus Vorlage speichern unter",
            ["Templates.AllFilesFilter"] = "Alle Dateien (*.*)|*.*",
            ["Templates.TemplateFileFilter"] = "Vorlagendatei (*{0})|*{0}|Alle Dateien (*.*)|*.*",
            ["Templates.NewFileCreated"] = "~/aegis $  Neue Datei aus \"{0}\" erstellt",
            ["Templates.ErrorCreatingDoc"] = "~/aegis $  Fehler beim Erstellen: {0}",

            // Templates-Tab: Text-Bausteine
            ["Snippets.New"] = "+ Neu",
            ["Snippets.Delete"] = "Löschen",
            ["Snippets.BulletList"] = "• Liste",
            ["Snippets.Save"] = "Speichern",
            ["Snippets.CopyToClipboard"] = "In Zwischenablage kopieren",
            ["Snippets.Count"] = "~/aegis $  {0} Text-Baustein(e)",
            ["Snippets.ErrorReading"] = "~/aegis $  Fehler beim Lesen der Bausteine: {0}",
            ["Snippets.ErrorLoading"] = "~/aegis $  Fehler beim Laden: {0}",
            ["Snippets.Saved"] = "~/aegis $  \"{0}\" gespeichert",
            ["Snippets.ErrorSaving"] = "~/aegis $  Fehler beim Speichern: {0}",
            ["Snippets.NewPromptTitle"] = "Neuer Text-Baustein",
            ["Snippets.NewPromptMessage"] = "Name des Bausteins:",
            ["Snippets.InvalidName"] = "~/aegis $  Ungültiger Name.",
            ["Snippets.AlreadyExists"] = "~/aegis $  Ein Baustein mit diesem Namen existiert bereits.",
            ["Snippets.ErrorCreating"] = "~/aegis $  Fehler beim Anlegen: {0}",
            ["Snippets.DeleteConfirmTitle"] = "Baustein löschen",
            ["Snippets.DeleteConfirmMessage"] = "Durch Bestätigen wird der Textbaustein \"{0}\" unwiderruflich gelöscht.",
            ["Snippets.ErrorDeleting"] = "~/aegis $  Fehler beim Löschen: {0}",
            ["Snippets.CopiedToClipboard"] = "~/aegis $  \"{0}\" in Zwischenablage kopiert",
            ["Snippets.ErrorCopying"] = "~/aegis $  Fehler beim Kopieren: {0}",

            // Backups-Tab
            ["Backups.Header"] = "GERÄTE BACKUPS",
            ["Backups.DeviceNameLabel"] = "Gerätename",
            ["Backups.OverviewTitle"] = "Vorhandene Backups",
            ["Backups.OverviewDescription"] = "Übersicht aller bisherigen Geräte-Backups auf diesem Stick (Treiber/Nutzerdaten). WLAN-Backups liegen verschlüsselt im Vault-Tab.",
            ["Backups.DeleteSelected"] = "Ausgewähltes Backup löschen",
            ["Backups.NoneYet"] = "Noch keine Backups vorhanden.",
            ["Backups.ErrorReading"] = "~/aegis $  Fehler beim Lesen der Backups: {0}",
            ["Backups.DeleteConfirmTitle"] = "Backup löschen",
            ["Backups.DeleteConfirmMessage"] = "Durch Bestätigen wird das Backup \"{0}\" unwiderruflich gelöscht.\n\nEnthält: {1}",
            ["Backups.Deleted"] = "~/aegis $  Backup \"{0}\" gelöscht",
            ["Backups.ErrorDeleting"] = "~/aegis $  Fehler beim Löschen: {0}",
            ["Backups.DriverCardTitle"] = "Treiber-Backup",
            ["Backups.DriverCardDescription"] = "Exportiert alle Gerätetreiber des aktuellen Geräts via DISM. Erfordert eine separate Admin-Bestätigung (UAC).",
            ["Backups.DriverButton"] = "Treiber sichern",
            ["Backups.DriverExporting"] = "Exportiere Treiber (Admin-Bestätigung erforderlich)…",
            ["Backups.DriverDone"] = "Treiber-Backup abgeschlossen: {0} Treiberpaket(e) exportiert nach \"{1}\\Drivers\".",
            ["Backups.DriverCancelled"] = "Treiber-Export abgebrochen (UAC verweigert oder DISM nicht gefunden).",
            ["Backups.DriverError"] = "Fehler beim Treiber-Export: {0}",
            ["Backups.UserDataCardTitle"] = "Nutzerdaten-Backup",
            ["Backups.UserDataCardDescription"] = "Kopiert ausgewählte Standardordner des aktuellen Geräts auf den Stick.",
            ["Backups.UserDataButton"] = "Ausgewählte Ordner sichern",
            ["Backups.UserDataSelectAtLeastOne"] = "Bitte mindestens einen Ordner auswählen.",
            ["Backups.UserDataCopying"] = "Kopiere {0}… {1} Dateien (aktuell: {2})",
            ["Backups.UserDataDone"] = "Nutzerdaten-Backup abgeschlossen ({0}).",
            ["Backups.UserDataError"] = "Fehler beim Nutzerdaten-Backup: {0}",
            ["Backups.NetworkCardTitle"] = "Netzwerkeinstellungen (WLAN)",
            ["Backups.NetworkCardDescription"] = "Exportiert gespeicherte WLAN-Profile inkl. Passwörter und speichert sie verschlüsselt im Vault (Vault muss entsperrt sein). Erfordert eine separate Admin-Bestätigung (UAC).",
            ["Backups.NetworkButton"] = "WLAN-Profile sichern",
            ["Backups.NetworkVaultLocked"] = "Bitte zuerst im Vault-Tab den Vault entsperren, dann hierher zurückkehren.",
            ["Backups.NetworkExporting"] = "Exportiere WLAN-Profile (Admin-Bestätigung erforderlich)…",
            ["Backups.NetworkNoneFound"] = "Keine WLAN-Profile gefunden.",
            ["Backups.NetworkDone"] = "{0} WLAN-Profil(e) verschlüsselt im Vault gespeichert (\"{1}\").",
            ["Backups.NetworkCancelled"] = "WLAN-Export abgebrochen (UAC verweigert).",
            ["Backups.NetworkError"] = "Fehler beim Netzwerk-Backup: {0}",

            // Suite-Builder-Tab
            ["SuiteBuilder.Header"] = "SUITE BUILDER",
            ["SuiteBuilder.Description"] = "Lädt die neueste Version eines Kpmn-Sticks vom Gitea-Server und schreibt sie auf einen angeschlossenen Wechseldatenträger.",
            ["SuiteBuilder.SetupTitle"] = "Gitea-Server einrichten",
            ["SuiteBuilder.SetupDescription"] = "Der Suite-Builder lädt die Stick-Releases von einem selbst gehosteten Gitea-Server.\nDie Zugangsdaten werden nur lokal in Settings\\gitea.json neben der EXE gespeichert.",
            ["SuiteBuilder.SetupServerLabel"] = "Server-URL",
            ["SuiteBuilder.SetupOrgLabel"] = "Organisation",
            ["SuiteBuilder.SetupTokenLabel"] = "Token (nur Lesezugriff)",
            ["SuiteBuilder.SetupSave"] = "Speichern",
            ["SuiteBuilder.SetupErrorIncomplete"] = "Bitte Server-URL, Organisation und Token ausfüllen.",
            ["SuiteBuilder.SetupErrorInvalidUrl"] = "Die Server-URL ist ungültig (erwartet z.B. http://host:3002).",
            ["SuiteBuilder.SetupErrorSaving"] = "Einstellungen konnten nicht gespeichert werden (schreibgeschützt?).",
            ["SuiteBuilder.SettingsButton"] = "Gitea-Einstellungen",
            ["SuiteBuilder.RefreshButton"] = "Aktualisieren",
            ["SuiteBuilder.CardTitle"] = "{0}-Stick bauen",
            ["SuiteBuilder.CardDescription"] = "Lädt das neueste {0}-Release herunter und entpackt es auf das gewählte Laufwerk.",
            ["SuiteBuilder.TargetDriveLabel"] = "Ziel-Laufwerk",
            ["SuiteBuilder.NoDrives"] = "Kein Wechseldatenträger verbunden",
            ["SuiteBuilder.NoVolumeLabel"] = "ohne Bezeichnung",
            ["SuiteBuilder.BuildButton"] = "Stick bauen",
            ["SuiteBuilder.VersionLoading"] = "Version wird abgerufen…",
            ["SuiteBuilder.VersionAvailable"] = "Neueste Version: {0}  •  {1} ({2})",
            ["SuiteBuilder.VersionNoZip"] = "Release {0} enthält kein ZIP-Asset.",
            ["SuiteBuilder.VersionError"] = "Version konnte nicht abgerufen werden: {0}",
            ["SuiteBuilder.NoReleaseYet"] = "Keine Version verfügbar – bitte zuerst aktualisieren.",
            ["SuiteBuilder.SelectDrive"] = "Bitte ein Ziel-Laufwerk auswählen.",
            ["SuiteBuilder.ConfirmNewTitle"] = "Neuen {0}-Stick erstellen",
            ["SuiteBuilder.ConfirmNewMessage"] = "Laufwerk {0} (\"{1}\") wird zu einem neuen {2}-Stick ({3}).\n\nAlle vorhandenen Dateien und Ordner auf diesem Laufwerk werden dabei unwiderruflich gelöscht.\n\nBitte prüfen, ob wirklich das richtige Laufwerk ausgewählt ist.",
            ["SuiteBuilder.ConfirmNewButton"] = "Löschen und {0}-Stick erstellen",
            ["SuiteBuilder.ConfirmUpdateTitle"] = "{0}-Stick aktualisieren",
            ["SuiteBuilder.ConfirmUpdateMessage"] = "Die {0}-Version auf Laufwerk {1} wird auf {2} aktualisiert.\n\nVorhandene {0}-Dateien werden überschrieben, andere Dateien auf dem Laufwerk bleiben unverändert.",
            ["SuiteBuilder.ConfirmUpdateButton"] = "Update bestätigen",
            ["SuiteBuilder.Wiping"] = "Lösche vorhandene Dateien auf {0}…",
            ["SuiteBuilder.Downloading"] = "Lade {0} {1} herunter… {2} % ({3} / {4})",
            ["SuiteBuilder.DownloadingUnknownSize"] = "Lade {0} {1} herunter… {2}",
            ["SuiteBuilder.Extracting"] = "Entpacke nach {0}…",
            ["SuiteBuilder.SettingLabel"] = "Setze Laufwerksbezeichnung…",
            ["SuiteBuilder.LabelFailed"] = "Laufwerksbezeichnung konnte nicht gesetzt werden: {0}",
            ["SuiteBuilder.Done"] = "Fertig. {0} {1} wurde auf {2} geschrieben.",
            ["SuiteBuilder.DoneWithWarning"] = "Fertig. {0} {1} wurde auf {2} geschrieben. Hinweis: {3}",
            ["SuiteBuilder.Error"] = "Fehler beim Bauen: {0}",
            ["SuiteBuilder.StatusBuilding"] = "~/aegis $  Baue {0}-Stick auf {1}…",
            ["SuiteBuilder.StatusDone"] = "~/aegis $  {0}-Stick fertig gebaut ({1})",
            ["SuiteBuilder.StatusError"] = "~/aegis $  Fehler beim Bauen des {0}-Sticks: {1}",

            // Suite-Builder: MABS-Stick (Ventoy)
            ["SuiteBuilder.Mabs.CardTitle"] = "MABS-Stick bauen (Ventoy)",
            ["SuiteBuilder.Mabs.CardDescription"] = "Installiert Ventoy auf dem gewählten Datenträger und kopiert anschließend das Kpmn-Theme darauf. Ventoy wird dabei direkt von seiner offiziellen GitHub-Seite geladen. ISO-Dateien sind nicht enthalten und müssen selbst hinzugefügt werden.",
            ["SuiteBuilder.Mabs.BuildButton"] = "Ventoy installieren",
            ["SuiteBuilder.Mabs.UnknownSize"] = "unbekannt",
            ["SuiteBuilder.Mabs.ConfirmTitle"] = "Datenträger komplett formatieren",
            ["SuiteBuilder.Mabs.ConfirmMessage"] = "Laufwerk {0} (\"{1}\", {2} gesamt, {3} frei) wird mit Ventoy zu einem MABS-Stick gemacht.\n\nACHTUNG: Dies ist keine einfache Dateilöschung, sondern eine komplette Neupartitionierung und Neuformatierung des gesamten Datenträgers. ALLE Daten auf {0} – auch alles, was AEGIS gar nicht anzeigt – gehen dabei endgültig und unwiederbringlich verloren. Es gibt danach keinen Weg zurück und keine Wiederherstellung.\n\nBitte ganz sicher sein, dass {0} (\"{1}\") wirklich der richtige Datenträger ist.",
            ["SuiteBuilder.Mabs.ConfirmButton"] = "Datenträger formatieren",
            ["SuiteBuilder.Mabs.StageCheckingVentoy"] = "Prüfe, ob Ventoy lokal vorhanden ist…",
            ["SuiteBuilder.Mabs.StageDownloadingVentoy"] = "Lade Ventoy von GitHub herunter… {0} % ({1} / {2})",
            ["SuiteBuilder.Mabs.StageDownloadingVentoyUnknownSize"] = "Lade Ventoy von GitHub herunter… {0}",
            ["SuiteBuilder.Mabs.StageInstalling"] = "Installiere Ventoy auf {0}… (Datenträger wird jetzt neu partitioniert)",
            ["SuiteBuilder.Mabs.StageInstallingPercent"] = "Installiere Ventoy auf {0}… {1} %",
            ["SuiteBuilder.Mabs.StageLocatingPartition"] = "Suche die neue Ventoy-Datenpartition…",
            ["SuiteBuilder.Mabs.StageDownloadingTheme"] = "Lade das MABS-Theme herunter… {0} % ({1} / {2})",
            ["SuiteBuilder.Mabs.StageDownloadingThemeUnknownSize"] = "Lade das MABS-Theme herunter… {0}",
            ["SuiteBuilder.Mabs.StageDeployingTheme"] = "Kopiere das Kpmn-Theme nach {0}…",
            ["SuiteBuilder.Mabs.Done"] = "Fertig. Ventoy wurde installiert und das Kpmn-Theme nach {0} kopiert.",
            ["SuiteBuilder.Mabs.DoneNoTheme"] = "Ventoy wurde erfolgreich auf {0} installiert, die neue Datenpartition war danach aber nicht auffindbar – das Kpmn-Theme konnte deshalb nicht automatisch kopiert werden. Das ist nur optisch: der Stick funktioniert auch ohne Theme. Die Theme-Dateien lassen sich jederzeit von Hand nachkopieren.",
            ["SuiteBuilder.Mabs.IsoHint"] = "Hinweis: ISO-Dateien sind bewusst nicht enthalten – Kpmn Development verteilt keine Betriebssystem-Images. Bitte die gewünschten ISOs selbst auf die Ventoy-Datenpartition kopieren.",
            ["SuiteBuilder.Mabs.UacCancelled"] = "Die Windows-Abfrage nach Administratorrechten wurde abgebrochen. Ventoy kann den Datenträger nur mit Administratorrechten beschreiben – es wurde nichts verändert.",
            ["SuiteBuilder.Mabs.InstallFailed"] = "Die Ventoy-Installation ist fehlgeschlagen: {0}",
            ["SuiteBuilder.Mabs.Error"] = "Fehler beim Bauen des MABS-Sticks: {0}",
            ["SuiteBuilder.Mabs.StatusBuilding"] = "~/aegis $  Installiere Ventoy auf {0}…",
            ["SuiteBuilder.Mabs.StatusDone"] = "~/aegis $  MABS-Stick fertig gebaut ({0})",
            ["SuiteBuilder.Mabs.StatusError"] = "~/aegis $  Fehler beim Bauen des MABS-Sticks: {0}",

            // Suite-Builder: Treiber-Datenbank nach einem AVAS-Build einrichten
            ["DriverSetup.Title"] = "Treiber-Datenbank wählen",
            ["DriverSetup.Intro"] = "Die AVAS-Installation auf diesem Stick kann Treiber mit SDI (Snappy Driver Installer) auf frisch aufgesetzten PCs installieren. Dafür muss auf dem Stick eine Treiber-Datenbank im Ordner \"Drivers\" eingerichtet werden.\n\nDieser Schritt ist optional – du kannst ihn auch später erledigen.",
            ["DriverSetup.OptionFullTitle"] = "Vollständige Offline-Datenbank",
            ["DriverSetup.OptionFullDescription"] = "Deckt die meiste Hardware ab und funktioniert komplett ohne Internet. Die vollständige Datenbank wird von SDI ausschließlich als Torrent (~50 GB) verteilt – AEGIS kann sie deshalb nicht automatisch herunterladen. AEGIS legt nur den Ordner \"Drivers\" an, die Dateien musst du selbst besorgen und dort ablegen.",
            ["DriverSetup.OptionFullButton"] = "Ordner anlegen und Anleitung anzeigen",
            ["DriverSetup.OptionLiteTitle"] = "Lite (empfohlen)",
            ["DriverSetup.OptionLiteDescription"] = "Stellt nur sicher, dass nach einer Neuinstallation wieder eine Netzwerkverbindung aufgebaut werden kann – alle weiteren Treiber lassen sich danach live nachladen. Das ist ausdrücklich keine vollständige Hardware-Abdeckung. AEGIS legt den Ordner \"Drivers\" an und kann das offizielle Intel-Ethernet-Treiberpaket direkt herunterladen.",
            ["DriverSetup.OptionLiteButton"] = "Lite einrichten",
            ["DriverSetup.Skip"] = "Später einrichten",
            ["DriverSetup.Close"] = "Schließen",
            ["DriverSetup.Back"] = "Zurück zur Auswahl",
            ["DriverSetup.FullHeading"] = "Vollständige Offline-Datenbank einrichten",
            ["DriverSetup.LiteHeading"] = "Lite-Treiber einrichten",
            ["DriverSetup.FolderCreated"] = "Ordner angelegt: {0}",
            ["DriverSetup.FolderError"] = "Ordner \"{0}\" konnte nicht angelegt werden: {1}",
            ["DriverSetup.FullSteps"] = "1. Lade die vollständige SDI-Datenbank (~50 GB, Torrent) selbst über die unten genannte Seite herunter.\n2. Lege SDI_R*.exe zusammen mit den zugehörigen Dateien und Ordnern (drivers, indexes, tools) in den Ordner \"Drivers\" auf dem Stick.\n3. Erst danach kann AVAS die Treiber direkt vom Stick installieren.",
            ["DriverSetup.FullSourceLabel"] = "Bezugsquelle (bitte selbst im Browser öffnen):",
            ["DriverSetup.IntelDescription"] = "\"Intel Ethernet Adapter Complete Driver Pack\" (~1,3 GB), direkt vom offiziellen Intel-Download-Server. Wird nach dem Herunterladen automatisch nach \"Drivers\\Intel-Ethernet\" entpackt.",
            ["DriverSetup.DownloadIntelButton"] = "Intel-Ethernet-Treiber jetzt herunterladen",
            ["DriverSetup.Downloading"] = "Lade Intel-Ethernet-Treiber… {0} % ({1} / {2})",
            ["DriverSetup.DownloadingUnknownSize"] = "Lade Intel-Ethernet-Treiber… {0}",
            ["DriverSetup.Extracting"] = "Entpacke nach {0}…",
            ["DriverSetup.DownloadDone"] = "Fertig. Die Intel-Ethernet-Treiber liegen jetzt in \"{0}\".",
            ["DriverSetup.DownloadError"] = "Fehler beim Herunterladen: {0}",
            ["DriverSetup.DownloadCancelled"] = "Download abgebrochen.",
            ["DriverSetup.ManualHint"] = "Empfehlung: Ergänze zusätzlich Treiber für weitere gängige Chipsätze von Hand. Lade sie bei den Herstellern herunter und lege sie ebenfalls im Ordner \"Drivers\" auf dem Stick ab:",
            ["DriverSetup.ManualRealtekLabel"] = "Realtek (LAN/WLAN):",
            ["DriverSetup.ManualIntelWlanLabel"] = "Intel Wireless/WLAN:",
            ["DriverSetup.ErrorOpeningLink"] = "~/aegis $  Link konnte nicht geöffnet werden: {0}",
            ["DriverSetup.StatusFolderCreated"] = "~/aegis $  Treiber-Ordner angelegt: {0}",
            ["DriverSetup.StatusDone"] = "~/aegis $  Intel-Ethernet-Treiber entpackt nach {0}",

            // Backup-Übersicht: Eintrags-Text
            ["BackupEntry.Drivers"] = "Treiber",
            ["BackupEntry.UserData"] = "Nutzerdaten ({0})",
            ["BackupEntry.Empty"] = "leer",
            ["BackupEntry.JustNow"] = "gerade eben",
            ["BackupEntry.MinutesAgo"] = "vor {0} Min.",
            ["BackupEntry.HoursAgo"] = "vor {0} Std.",
            ["BackupEntry.DaysAgo"] = "vor {0} Tag(en)",

            // AVAS-Wartungshinweis (Reminder-Passthrough)
            ["Avas.TooltipTitle"] = "AVAS – WARTUNG FÄLLIG",
            ["Avas.TooltipSdiRow"] = "SDI-Datenbank: {0} Tage alt — Update empfohlen",
            ["Avas.TooltipPackagesRow"] = "{0} von {1} Paketen veraltet (> 180 Tage)",
            ["Avas.TooltipFooter"] = "Klicken → AVAS-Ordner im Dokumente-Tab öffnen",
            ["Avas.NoticePrefix"] = "AVAS:",
            ["Avas.NoticeSdi"] = "SDI-Datenbank {0} Tage alt — Update empfohlen",
            ["Avas.NoticePackages"] = "{0} von {1} Paketen veraltet",
            ["Avas.NoticeDismiss"] = "Hinweis ausblenden",

            // Standardordner-Namen (Nutzerdaten-Backup Checkboxen)
            ["Folder.Desktop"] = "Desktop",
            ["Folder.Documents"] = "Dokumente",
            ["Folder.Pictures"] = "Bilder",
            ["Folder.Videos"] = "Videos",
            ["Folder.Music"] = "Musik",
            ["Folder.Downloads"] = "Downloads",
        };

        private static readonly Dictionary<string, string> EnStrings = new()
        {
            // App / Topbar
            ["App.RecognizedSticks"] = "detected sticks",
            ["App.BuildDate"] = "Built: {0}",
            ["Tab.Dokumente"] = "Documents",
            ["Tab.Templates"] = "Templates",
            ["Tab.Vault"] = "Vault",
            ["Tab.Backups"] = "Device Backups",
            ["Tab.SuiteBuilder"] = "Suite Builder",
            ["Status.DetectingSticks"] = "~/aegis $  Detecting sticks...",
            ["Tab.ComingSoon"] = "{0} tab coming soon",

            // Common
            ["Common.Cancel"] = "Cancel",
            ["Common.Ok"] = "OK",
            ["Common.ConfirmRemoval"] = "Confirm removal",
            ["Common.ConfirmDeletion"] = "Confirm deletion",
            ["UsbNotDetected.Title"] = "USB stick not detected",
            ["UsbNotDetected.Message"] = "AEGIS must be started from a removable drive\nto access the document structure.",
            ["Status.NoUsbStick"] = "~/aegis $  No USB stick detected",

            // Dokumente tab
            ["Dokumente.FolderHeader"] = "DOCUMENTS",
            ["Dokumente.SelectFolder"] = "SELECT A FOLDER",
            ["Dokumente.SelectFileHint"] = "Select a file to see details.",
            ["Dokumente.Back"] = "⬆ Back",
            ["Dokumente.FolderNotFound"] = "~/aegis $  Folder not found: {0}",
            ["Dokumente.EntriesInFolder"] = "~/aegis $  {0} entries in {1}",
            ["Dokumente.ReadError"] = "~/aegis $  Error while reading: {0}",
            ["Dokumente.StickNotDetectedTitle"] = "{0} stick not detected",
            ["Dokumente.StickNotDetectedMessage"] = "Please plug in the {0} stick or use a different USB port/stick.",
            ["Dokumente.StickNotDetectedStatus"] = "~/aegis $  {0} stick not detected",
            ["Dokumente.IsAFolder"] = "\"{0}\" is a folder.",
            ["Dokumente.PreviewError"] = "Error: {0}",
            ["Dokumente.ContentPreviewComingSoon"] = "Content preview coming in a later version.",
            ["Dokumente.LastModified"] = "{0}  •  last modified {1}",
            ["Dokumente.ErrorCreatingFolderStructure"] = "~/aegis $  Error creating folder structure: {0}",

            // Vault tab
            ["Vault.SetupTitle"] = "Set up vault",
            ["Vault.SetupDescription"] = "No vault exists yet. Set a master password to store encrypted files locally on this stick.\nWithout this password the data is permanently lost — there is no recovery.",
            ["Vault.CreateButton"] = "Create vault",
            ["Vault.LockedTitle"] = "Vault locked",
            ["Vault.LockedDescription"] = "Enter your master password to unlock the vault.",
            ["Vault.UnlockButton"] = "Unlock",
            ["Vault.ErrorEnterPassword"] = "Please enter a password.",
            ["Vault.ErrorPasswordMismatch"] = "The passwords don't match.",
            ["Vault.ErrorCreating"] = "Error while creating: {0}",
            ["Vault.WrongPassword"] = "Wrong password.",
            ["Vault.ErrorUnlocking"] = "Error while unlocking: {0}",
            ["Vault.Header"] = "VAULT",
            ["Vault.AddFile"] = "+ Add file",
            ["Vault.Remove"] = "Remove",
            ["Vault.Lock"] = "🔒 Lock",
            ["Vault.EntriesCount"] = "~/aegis $  {0} file(s) in vault",
            ["Vault.ErrorReading"] = "~/aegis $  Error reading vault: {0}",
            ["Vault.AddFileDialogTitle"] = "Add files to vault",
            ["Vault.ErrorAdding"] = "~/aegis $  Error adding: {0}",
            ["Vault.RemoveConfirmTitle"] = "Remove file",
            ["Vault.RemoveConfirmMessage"] = "Confirming will permanently remove \"{0}\" from the vault.",
            ["Vault.ErrorRemoving"] = "~/aegis $  Error removing: {0}",
            ["Vault.OpenedTemporarily"] = "~/aegis $  \"{0}\" decrypted and opened (temporary)",
            ["Vault.ErrorOpening"] = "~/aegis $  Error opening: {0}",
            ["Vault.AutoLockedStatus"] = "~/aegis $  Vault automatically locked (inactivity)",
            ["Vault.InvalidFile"] = "Invalid or corrupted vault file.",

            // Templates tab: document templates
            ["Templates.DocsSubTab"] = "Document templates",
            ["Templates.SnippetsSubTab"] = "Text snippets",
            ["Templates.AddDoc"] = "+ Add template",
            ["Templates.RemoveDoc"] = "Remove",
            ["Templates.UseDoc"] = "New file from template…",
            ["Templates.DocsCount"] = "~/aegis $  {0} template(s)",
            ["Templates.ErrorReadingDocs"] = "~/aegis $  Error reading templates: {0}",
            ["Templates.AddDialogTitle"] = "Add files as template",
            ["Templates.ErrorAddingDoc"] = "~/aegis $  Error adding: {0}",
            ["Templates.RemoveDocConfirmTitle"] = "Remove template",
            ["Templates.RemoveDocConfirmMessage"] = "Confirming will permanently remove the template \"{0}\".",
            ["Templates.ErrorRemovingDoc"] = "~/aegis $  Error removing: {0}",
            ["Templates.SaveAsDialogTitle"] = "Save new file from template as",
            ["Templates.AllFilesFilter"] = "All files (*.*)|*.*",
            ["Templates.TemplateFileFilter"] = "Template file (*{0})|*{0}|All files (*.*)|*.*",
            ["Templates.NewFileCreated"] = "~/aegis $  New file created from \"{0}\"",
            ["Templates.ErrorCreatingDoc"] = "~/aegis $  Error creating: {0}",

            // Templates tab: text snippets
            ["Snippets.New"] = "+ New",
            ["Snippets.Delete"] = "Delete",
            ["Snippets.BulletList"] = "• List",
            ["Snippets.Save"] = "Save",
            ["Snippets.CopyToClipboard"] = "Copy to clipboard",
            ["Snippets.Count"] = "~/aegis $  {0} text snippet(s)",
            ["Snippets.ErrorReading"] = "~/aegis $  Error reading snippets: {0}",
            ["Snippets.ErrorLoading"] = "~/aegis $  Error loading: {0}",
            ["Snippets.Saved"] = "~/aegis $  \"{0}\" saved",
            ["Snippets.ErrorSaving"] = "~/aegis $  Error saving: {0}",
            ["Snippets.NewPromptTitle"] = "New text snippet",
            ["Snippets.NewPromptMessage"] = "Snippet name:",
            ["Snippets.InvalidName"] = "~/aegis $  Invalid name.",
            ["Snippets.AlreadyExists"] = "~/aegis $  A snippet with this name already exists.",
            ["Snippets.ErrorCreating"] = "~/aegis $  Error creating: {0}",
            ["Snippets.DeleteConfirmTitle"] = "Delete snippet",
            ["Snippets.DeleteConfirmMessage"] = "Confirming will permanently delete the text snippet \"{0}\".",
            ["Snippets.ErrorDeleting"] = "~/aegis $  Error deleting: {0}",
            ["Snippets.CopiedToClipboard"] = "~/aegis $  \"{0}\" copied to clipboard",
            ["Snippets.ErrorCopying"] = "~/aegis $  Error copying: {0}",

            // Backups tab
            ["Backups.Header"] = "DEVICE BACKUPS",
            ["Backups.DeviceNameLabel"] = "Device name",
            ["Backups.OverviewTitle"] = "Existing backups",
            ["Backups.OverviewDescription"] = "Overview of all previous device backups on this stick (drivers/user data). WLAN backups are stored encrypted in the Vault tab.",
            ["Backups.DeleteSelected"] = "Delete selected backup",
            ["Backups.NoneYet"] = "No backups yet.",
            ["Backups.ErrorReading"] = "~/aegis $  Error reading backups: {0}",
            ["Backups.DeleteConfirmTitle"] = "Delete backup",
            ["Backups.DeleteConfirmMessage"] = "Confirming will permanently delete the backup \"{0}\".\n\nContains: {1}",
            ["Backups.Deleted"] = "~/aegis $  Backup \"{0}\" deleted",
            ["Backups.ErrorDeleting"] = "~/aegis $  Error deleting: {0}",
            ["Backups.DriverCardTitle"] = "Driver backup",
            ["Backups.DriverCardDescription"] = "Exports all device drivers of the current device via DISM. Requires a separate admin confirmation (UAC).",
            ["Backups.DriverButton"] = "Back up drivers",
            ["Backups.DriverExporting"] = "Exporting drivers (admin confirmation required)…",
            ["Backups.DriverDone"] = "Driver backup complete: {0} driver package(s) exported to \"{1}\\Drivers\".",
            ["Backups.DriverCancelled"] = "Driver export cancelled (UAC denied or DISM not found).",
            ["Backups.DriverError"] = "Error during driver export: {0}",
            ["Backups.UserDataCardTitle"] = "User data backup",
            ["Backups.UserDataCardDescription"] = "Copies selected standard folders of the current device to the stick.",
            ["Backups.UserDataButton"] = "Back up selected folders",
            ["Backups.UserDataSelectAtLeastOne"] = "Please select at least one folder.",
            ["Backups.UserDataCopying"] = "Copying {0}… {1} files (current: {2})",
            ["Backups.UserDataDone"] = "User data backup complete ({0}).",
            ["Backups.UserDataError"] = "Error during user data backup: {0}",
            ["Backups.NetworkCardTitle"] = "Network settings (WLAN)",
            ["Backups.NetworkCardDescription"] = "Exports saved WLAN profiles incl. passwords and stores them encrypted in the Vault (Vault must be unlocked). Requires a separate admin confirmation (UAC).",
            ["Backups.NetworkButton"] = "Back up WLAN profiles",
            ["Backups.NetworkVaultLocked"] = "Please unlock the vault in the Vault tab first, then come back here.",
            ["Backups.NetworkExporting"] = "Exporting WLAN profiles (admin confirmation required)…",
            ["Backups.NetworkNoneFound"] = "No WLAN profiles found.",
            ["Backups.NetworkDone"] = "{0} WLAN profile(s) stored encrypted in the vault (\"{1}\").",
            ["Backups.NetworkCancelled"] = "WLAN export cancelled (UAC denied).",
            ["Backups.NetworkError"] = "Error during network backup: {0}",

            // Suite Builder tab
            ["SuiteBuilder.Header"] = "SUITE BUILDER",
            ["SuiteBuilder.Description"] = "Downloads the latest version of a Kpmn stick from the Gitea server and writes it to a connected removable drive.",
            ["SuiteBuilder.SetupTitle"] = "Set up Gitea server",
            ["SuiteBuilder.SetupDescription"] = "The Suite Builder downloads the stick releases from a self-hosted Gitea server.\nThe credentials are stored locally only, in Settings\\gitea.json next to the EXE.",
            ["SuiteBuilder.SetupServerLabel"] = "Server URL",
            ["SuiteBuilder.SetupOrgLabel"] = "Organization",
            ["SuiteBuilder.SetupTokenLabel"] = "Token (read-only)",
            ["SuiteBuilder.SetupSave"] = "Save",
            ["SuiteBuilder.SetupErrorIncomplete"] = "Please fill in server URL, organization and token.",
            ["SuiteBuilder.SetupErrorInvalidUrl"] = "The server URL is invalid (expected e.g. http://host:3002).",
            ["SuiteBuilder.SetupErrorSaving"] = "Settings could not be saved (write-protected?).",
            ["SuiteBuilder.SettingsButton"] = "Gitea settings",
            ["SuiteBuilder.RefreshButton"] = "Refresh",
            ["SuiteBuilder.CardTitle"] = "Build {0} stick",
            ["SuiteBuilder.CardDescription"] = "Downloads the latest {0} release and extracts it onto the selected drive.",
            ["SuiteBuilder.TargetDriveLabel"] = "Target drive",
            ["SuiteBuilder.NoDrives"] = "No removable drive connected",
            ["SuiteBuilder.NoVolumeLabel"] = "no label",
            ["SuiteBuilder.BuildButton"] = "Build stick",
            ["SuiteBuilder.VersionLoading"] = "Fetching version…",
            ["SuiteBuilder.VersionAvailable"] = "Latest version: {0}  •  {1} ({2})",
            ["SuiteBuilder.VersionNoZip"] = "Release {0} contains no ZIP asset.",
            ["SuiteBuilder.VersionError"] = "Could not fetch version: {0}",
            ["SuiteBuilder.NoReleaseYet"] = "No version available — please refresh first.",
            ["SuiteBuilder.SelectDrive"] = "Please select a target drive.",
            ["SuiteBuilder.ConfirmNewTitle"] = "Create new {0} stick",
            ["SuiteBuilder.ConfirmNewMessage"] = "Drive {0} (\"{1}\") will be turned into a new {2} stick ({3}).\n\nAll existing files and folders on this drive will be permanently deleted.\n\nPlease double-check that you selected the right drive.",
            ["SuiteBuilder.ConfirmNewButton"] = "Erase and create {0} stick",
            ["SuiteBuilder.ConfirmUpdateTitle"] = "Update {0} stick",
            ["SuiteBuilder.ConfirmUpdateMessage"] = "The {0} version on drive {1} will be updated to {2}.\n\nExisting {0} files will be overwritten, other files on the drive remain untouched.",
            ["SuiteBuilder.ConfirmUpdateButton"] = "Confirm update",
            ["SuiteBuilder.Wiping"] = "Deleting existing files on {0}…",
            ["SuiteBuilder.Downloading"] = "Downloading {0} {1}… {2} % ({3} / {4})",
            ["SuiteBuilder.DownloadingUnknownSize"] = "Downloading {0} {1}… {2}",
            ["SuiteBuilder.Extracting"] = "Extracting to {0}…",
            ["SuiteBuilder.SettingLabel"] = "Setting volume label…",
            ["SuiteBuilder.LabelFailed"] = "Volume label could not be set: {0}",
            ["SuiteBuilder.Done"] = "Done. {0} {1} was written to {2}.",
            ["SuiteBuilder.DoneWithWarning"] = "Done. {0} {1} was written to {2}. Note: {3}",
            ["SuiteBuilder.Error"] = "Error while building: {0}",
            ["SuiteBuilder.StatusBuilding"] = "~/aegis $  Building {0} stick on {1}…",
            ["SuiteBuilder.StatusDone"] = "~/aegis $  {0} stick built successfully ({1})",
            ["SuiteBuilder.StatusError"] = "~/aegis $  Error building the {0} stick: {1}",

            // Suite Builder: MABS stick (Ventoy)
            ["SuiteBuilder.Mabs.CardTitle"] = "Build MABS stick (Ventoy)",
            ["SuiteBuilder.Mabs.CardDescription"] = "Installs Ventoy on the selected drive and then copies the Kpmn theme onto it. Ventoy is downloaded directly from its official GitHub page. ISO files are not included and have to be added yourself.",
            ["SuiteBuilder.Mabs.BuildButton"] = "Install Ventoy",
            ["SuiteBuilder.Mabs.UnknownSize"] = "unknown",
            ["SuiteBuilder.Mabs.ConfirmTitle"] = "Completely format drive",
            ["SuiteBuilder.Mabs.ConfirmMessage"] = "Drive {0} (\"{1}\", {2} total, {3} free) will be turned into a MABS stick using Ventoy.\n\nWARNING: This is not a simple file deletion but a complete repartitioning and reformatting of the entire disk. ALL data on {0} — including everything AEGIS does not even show — will be destroyed permanently and unrecoverably. There is no way back and no recovery afterwards.\n\nPlease be absolutely certain that {0} (\"{1}\") really is the correct drive.",
            ["SuiteBuilder.Mabs.ConfirmButton"] = "Format drive",
            ["SuiteBuilder.Mabs.StageCheckingVentoy"] = "Checking whether Ventoy is available locally…",
            ["SuiteBuilder.Mabs.StageDownloadingVentoy"] = "Downloading Ventoy from GitHub… {0} % ({1} / {2})",
            ["SuiteBuilder.Mabs.StageDownloadingVentoyUnknownSize"] = "Downloading Ventoy from GitHub… {0}",
            ["SuiteBuilder.Mabs.StageInstalling"] = "Installing Ventoy on {0}… (the drive is being repartitioned now)",
            ["SuiteBuilder.Mabs.StageInstallingPercent"] = "Installing Ventoy on {0}… {1} %",
            ["SuiteBuilder.Mabs.StageLocatingPartition"] = "Looking for the new Ventoy data partition…",
            ["SuiteBuilder.Mabs.StageDownloadingTheme"] = "Downloading the MABS theme… {0} % ({1} / {2})",
            ["SuiteBuilder.Mabs.StageDownloadingThemeUnknownSize"] = "Downloading the MABS theme… {0}",
            ["SuiteBuilder.Mabs.StageDeployingTheme"] = "Copying the Kpmn theme to {0}…",
            ["SuiteBuilder.Mabs.Done"] = "Done. Ventoy was installed and the Kpmn theme was copied to {0}.",
            ["SuiteBuilder.Mabs.DoneNoTheme"] = "Ventoy was installed successfully on {0}, but the new data partition could not be found afterwards, so the Kpmn theme could not be copied automatically. This is purely cosmetic — the stick works fine without the theme. The theme files can be copied over manually at any time.",
            ["SuiteBuilder.Mabs.IsoHint"] = "Note: ISO files are deliberately not included — Kpmn Development does not distribute operating system images. Please copy the ISOs you want onto the Ventoy data partition yourself.",
            ["SuiteBuilder.Mabs.UacCancelled"] = "The Windows prompt for administrator rights was cancelled. Ventoy can only write to the drive with administrator rights — nothing was changed.",
            ["SuiteBuilder.Mabs.InstallFailed"] = "The Ventoy installation failed: {0}",
            ["SuiteBuilder.Mabs.Error"] = "Error while building the MABS stick: {0}",
            ["SuiteBuilder.Mabs.StatusBuilding"] = "~/aegis $  Installing Ventoy on {0}…",
            ["SuiteBuilder.Mabs.StatusDone"] = "~/aegis $  MABS stick built successfully ({0})",
            ["SuiteBuilder.Mabs.StatusError"] = "~/aegis $  Error building the MABS stick: {0}",

            // Suite Builder: set up the driver database after an AVAS build
            ["DriverSetup.Title"] = "Choose driver database",
            ["DriverSetup.Intro"] = "The AVAS installation on this stick can install drivers on freshly imaged PCs using SDI (Snappy Driver Installer). For that, a driver database has to be set up in the \"Drivers\" folder on the stick.\n\nThis step is optional — you can also take care of it later.",
            ["DriverSetup.OptionFullTitle"] = "Complete offline database",
            ["DriverSetup.OptionFullDescription"] = "Covers the most hardware and works entirely without internet. The complete database is distributed by SDI exclusively as a torrent (~50 GB), so AEGIS cannot fetch it automatically. AEGIS only creates the \"Drivers\" folder; you have to obtain the files yourself and place them there.",
            ["DriverSetup.OptionFullButton"] = "Create folder and show instructions",
            ["DriverSetup.OptionLiteTitle"] = "Lite (recommended)",
            ["DriverSetup.OptionLiteDescription"] = "Only guarantees that network connectivity can be re-established after a fresh install — all other drivers can then be fetched live afterwards. This is explicitly not full hardware coverage. AEGIS creates the \"Drivers\" folder and can download the official Intel Ethernet driver pack directly.",
            ["DriverSetup.OptionLiteButton"] = "Set up lite",
            ["DriverSetup.Skip"] = "Set up later",
            ["DriverSetup.Close"] = "Close",
            ["DriverSetup.Back"] = "Back to selection",
            ["DriverSetup.FullHeading"] = "Set up complete offline database",
            ["DriverSetup.LiteHeading"] = "Set up lite drivers",
            ["DriverSetup.FolderCreated"] = "Folder created: {0}",
            ["DriverSetup.FolderError"] = "Folder \"{0}\" could not be created: {1}",
            ["DriverSetup.FullSteps"] = "1. Download the complete SDI database (~50 GB, torrent) yourself from the page listed below.\n2. Place SDI_R*.exe together with its related files and folders (drivers, indexes, tools) into the \"Drivers\" folder on the stick.\n3. Only then can AVAS install the drivers directly from the stick.",
            ["DriverSetup.FullSourceLabel"] = "Source (please open in your browser manually):",
            ["DriverSetup.IntelDescription"] = "\"Intel Ethernet Adapter Complete Driver Pack\" (~1.3 GB), straight from Intel's official download server. It is extracted to \"Drivers\\Intel-Ethernet\" automatically after downloading.",
            ["DriverSetup.DownloadIntelButton"] = "Download Intel Ethernet drivers now",
            ["DriverSetup.Downloading"] = "Downloading Intel Ethernet drivers… {0} % ({1} / {2})",
            ["DriverSetup.DownloadingUnknownSize"] = "Downloading Intel Ethernet drivers… {0}",
            ["DriverSetup.Extracting"] = "Extracting to {0}…",
            ["DriverSetup.DownloadDone"] = "Done. The Intel Ethernet drivers are now in \"{0}\".",
            ["DriverSetup.DownloadError"] = "Error while downloading: {0}",
            ["DriverSetup.DownloadCancelled"] = "Download cancelled.",
            ["DriverSetup.ManualHint"] = "Recommendation: additionally add drivers for other common chipsets manually. Download them from the vendors and place them into the same \"Drivers\" folder on the stick:",
            ["DriverSetup.ManualRealtekLabel"] = "Realtek (LAN/WLAN):",
            ["DriverSetup.ManualIntelWlanLabel"] = "Intel Wireless/WLAN:",
            ["DriverSetup.ErrorOpeningLink"] = "~/aegis $  Could not open link: {0}",
            ["DriverSetup.StatusFolderCreated"] = "~/aegis $  Driver folder created: {0}",
            ["DriverSetup.StatusDone"] = "~/aegis $  Intel Ethernet drivers extracted to {0}",

            // Backup overview: entry text
            ["BackupEntry.Drivers"] = "Drivers",
            ["BackupEntry.UserData"] = "User data ({0})",
            ["BackupEntry.Empty"] = "empty",
            ["BackupEntry.JustNow"] = "just now",
            ["BackupEntry.MinutesAgo"] = "{0} min. ago",
            ["BackupEntry.HoursAgo"] = "{0} hr. ago",
            ["BackupEntry.DaysAgo"] = "{0} day(s) ago",

            // AVAS maintenance reminder (passthrough)
            ["Avas.TooltipTitle"] = "AVAS – MAINTENANCE DUE",
            ["Avas.TooltipSdiRow"] = "SDI database: {0} days old — update recommended",
            ["Avas.TooltipPackagesRow"] = "{0} of {1} packages outdated (> 180 days)",
            ["Avas.TooltipFooter"] = "Click → open the AVAS folder in the Documents tab",
            ["Avas.NoticePrefix"] = "AVAS:",
            ["Avas.NoticeSdi"] = "SDI database {0} days old — update recommended",
            ["Avas.NoticePackages"] = "{0} of {1} packages outdated",
            ["Avas.NoticeDismiss"] = "Hide notice",

            // Standard folder names (user-data backup checkboxes)
            ["Folder.Desktop"] = "Desktop",
            ["Folder.Documents"] = "Documents",
            ["Folder.Pictures"] = "Pictures",
            ["Folder.Videos"] = "Videos",
            ["Folder.Music"] = "Music",
            ["Folder.Downloads"] = "Downloads",
        };
    }
}
