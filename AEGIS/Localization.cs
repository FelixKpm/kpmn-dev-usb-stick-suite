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
