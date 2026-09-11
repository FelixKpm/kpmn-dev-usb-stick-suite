using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AEGIS
{
    public partial class MainWindow : Window
    {
        // Feste Ordnerstruktur für den Dokumente-Tab
        private static readonly string[] FixedDocumentFolders = new[]
        {
            "DART",
            "AVAS",
            "MABS",
            "Portable System",
            "Persönliche Dokumente"
        };

        // Ordner, die einem physischen Kpmn-Stick zugeordnet sind: Ordnername -> (erwartetes Datenträger-Label, Anzeigename)
        // "PORTABLE" ist ein Platzhalter, solange Stick 5 (portables Ubuntu-System) kein finales Label hat.
        private static readonly Dictionary<string, (string VolumeLabel, string DisplayName)> StickFolderMap = new()
        {
            ["DART"] = ("DART", "DART"),
            ["AVAS"] = ("AVAS", "AVAS"),
            ["MABS"] = ("MABS", "MABS"),
            ["Portable System"] = ("PORTABLE", "Portable System"),
        };

        // Sticks, die als Pille in der Topbar angezeigt werden: Name, erwartetes Datenträger-Label, Akzentfarbe
        // DART nutzt hier #ffcf21 (heller als die Programm-Akzentfarbe #a16207) für bessere Lesbarkeit in der Pille.
        private static readonly (string Name, string VolumeLabel, string AccentHex)[] TopbarSticks = new[]
        {
            ("DART", "DART", "#ffcf21"),
            ("AVAS", "AVAS", "#A8FF3E"),
            ("MABS", "MABS", "#FF6B00"),
        };

        private static readonly Brush StickOnlineNameBrush = CreateFrozenBrush("#A8CCE8");

        // ---------- AVAS-Wartungshinweis (Reminder-Passthrough) ----------
        // AVAS selbst berechnet seine Veraltungs-Warnungen rein aus Datei-Zeitstempeln auf dem AVAS-Stick.
        // AEGIS spiegelt diese Logik, damit der Hinweis auch ohne Start von AVAS sichtbar ist.
        private const string AvasVolumeLabel = "AVAS";
        private const string AvasDocumentFolder = "AVAS";
        private const int AvasSdiStaleDays = 150;
        private const int AvasPackageStaleDays = 180;

        private static readonly Brush AvasWarnBorderBrush = CreateFrozenBrush("#C98A1E");
        private static readonly Brush AvasWarnAccentBrush = CreateFrozenBrush("#FFB020");
        private static readonly Brush AvasWarnValueBrush = CreateFrozenBrush("#F0D8A8");
        private static readonly Brush AvasWarnPillBackgroundBrush = CreateFrozenBrush("#23305C");
        private static readonly Brush AvasTooltipBackgroundBrush = CreateFrozenBrush("#152540");
        private static readonly Brush AvasTooltipTextBrush = CreateFrozenBrush("#A8CCE8");
        private static readonly Brush AvasNoticeBackgroundBrush = CreateFrozenBrush("#12FFB020");

        private static Brush CreateFrozenBrush(string hex)
        {
            var brush = (Brush)new BrushConverter().ConvertFromString(hex)!;
            brush.Freeze();
            return brush;
        }

        private readonly record struct StickIndicator(
            Ellipse Dot,
            TextBlock Label,
            string VolumeLabel,
            Brush AccentBrush,
            Border Pill,
            TextBlock WarnMark);

        private readonly List<StickIndicator> _stickIndicators = new();
        private DispatcherTimer _stickRefreshTimer = null!;

        // Basisverzeichnis für Dokumente (liegt relativ zur EXE, z.B. auf dem Stick)
        private readonly string _documentsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Documents");

        // ===================== VAULT =====================
        private static readonly TimeSpan VaultAutoLockTimeout = TimeSpan.FromMinutes(5);
        private readonly string _vaultTempDir = Path.Combine(Path.GetTempPath(), "AEGIS-Vault");
        private readonly VaultService _vaultService = new(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Vault", "vault.kvault"));
        private DispatcherTimer _vaultAutoLockTimer = null!;
        private DateTime _vaultLastActivity = DateTime.Now;
        private ListBox _vaultList = null!;

        // ===================== TEMPLATES =====================
        private readonly string _templateDocsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates", "Documents");
        private readonly string _templateSnippetsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates", "Snippets");
        private Grid _templatesContentHost = null!;
        private ListBox _templateDocsList = null!;
        private ListBox _snippetList = null!;
        private RichTextBox _snippetEditor = null!;
        private string? _currentSnippetName;

        // ===================== BACKUPS =====================
        private readonly string _backupsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");
        private string? _currentBackupFolder;
        private TextBox _deviceNameBox = null!;
        private TextBlock _driverBackupStatus = null!;
        private TextBlock _userDataBackupStatus = null!;
        private TextBlock _networkBackupStatus = null!;
        private ProgressBar _driverProgressBar = null!;
        private ProgressBar _userDataProgressBar = null!;
        private ProgressBar _networkProgressBar = null!;
        private Button _driverBackupButton = null!;
        private Button _userDataBackupButton = null!;
        private Button _networkBackupButton = null!;
        private ListBox _backupsOverviewList = null!;
        private readonly Dictionary<string, CheckBox> _userDataFolderChecks = new();

        private static readonly string[] UserDataFolderLabels = { "Desktop", "Dokumente", "Bilder", "Videos", "Musik", "Downloads" };

        // True, wenn AEGIS von einem Wechseldatenträger (USB-Stick) gestartet wurde
        private bool _isRunningFromUsbStick;

        // Unlokalisierter Basistext der Statusleiste unten rechts (Build-Datum wird angehängt)
        private const string AppVersionBaseText = "AEGIS v1.0  •  Kpmn Development";

        private TextBlock _langDeText = null!;
        private TextBlock _langEnText = null!;

        public MainWindow()
        {
            Loc.Initialize();

            InitializeComponent();
            CheckUsbStick();

            if (_isRunningFromUsbStick)
            {
                EnsureDocumentFolders();
            }

            BuildLanguageSwitcher();
            ApplyStaticUiText();
            Loc.LanguageChanged += OnLanguageChanged;

            LoadDokumenteTab();

            BuildStickIndicators();
            RefreshStickIndicators();
            _stickRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _stickRefreshTimer.Tick += (_, _) => RefreshStickIndicators();
            _stickRefreshTimer.Start();

            _vaultAutoLockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _vaultAutoLockTimer.Tick += (_, _) => CheckVaultAutoLock();
            _vaultAutoLockTimer.Start();

            Closing += (_, _) => LockVault();
        }

        // ===================== SPRACHUMSCHALTER =====================

        private void BuildLanguageSwitcher()
        {
            _langDeText = new TextBlock { Text = "DE", FontSize = 10, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 0, 6, 0) };
            _langDeText.MouseLeftButtonUp += (_, _) => Loc.SetLanguage(AppLanguage.De);

            var separator = new TextBlock { Text = "/", FontSize = 10, Foreground = (Brush)FindResource("TextMuted"), Margin = new Thickness(0, 0, 6, 0) };

            _langEnText = new TextBlock { Text = "EN", FontSize = 10, Cursor = System.Windows.Input.Cursors.Hand };
            _langEnText.MouseLeftButtonUp += (_, _) => Loc.SetLanguage(AppLanguage.En);

            LanguagePanel.Children.Add(_langDeText);
            LanguagePanel.Children.Add(separator);
            LanguagePanel.Children.Add(_langEnText);

            RefreshLanguageSwitcher();
        }

        private void RefreshLanguageSwitcher()
        {
            _langDeText.Foreground = Loc.Current == AppLanguage.De ? (Brush)FindResource("TextPrimary") : (Brush)FindResource("TextMuted");
            _langDeText.FontWeight = Loc.Current == AppLanguage.De ? FontWeights.Bold : FontWeights.Normal;
            _langEnText.Foreground = Loc.Current == AppLanguage.En ? (Brush)FindResource("TextPrimary") : (Brush)FindResource("TextMuted");
            _langEnText.FontWeight = Loc.Current == AppLanguage.En ? FontWeights.Bold : FontWeights.Normal;
        }

        // Setzt alle Texte, die außerhalb der pro-Tab-Load-Methoden liegen (Topbar, Tab-Beschriftungen)
        private void ApplyStaticUiText()
        {
            StickLabelText.Text = Loc.T("App.RecognizedSticks");
            TabDokumente.Content = Loc.T("Tab.Dokumente");
            TabTemplates.Content = Loc.T("Tab.Templates");
            TabVault.Content = Loc.T("Tab.Vault");
            TabBackups.Content = Loc.T("Tab.Backups");

            // Immer vom festen Basistext ausgehen, damit sich das Build-Datum bei Sprachwechseln nicht mehrfach anhängt
            var versionText = AppVersionBaseText;
            var buildDate = GetBuildDate();
            if (buildDate != null)
            {
                var formatted = buildDate.Value.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
                versionText += "  •  " + string.Format(Loc.T("App.BuildDate"), formatted);
            }

            AppVersionText.Text = versionText;
        }

        // Letztes Änderungsdatum der AEGIS-EXE; null, wenn die Datei nicht ermittelbar ist
        private static DateTime? GetBuildDate()
        {
            try
            {
                var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(location) || !File.Exists(location))
                {
                    // Fallback für Single-File-Publish, bei dem Location leer ist
                    location = Path.Combine(AppContext.BaseDirectory, "AEGIS.exe");
                }

                return File.Exists(location) ? File.GetLastWriteTime(location) : null;
            }
            catch
            {
                return null;
            }
        }

        // Wird bei jedem Sprachwechsel ausgelöst: statische Texte neu setzen und den aktuell offenen Tab neu aufbauen
        private void OnLanguageChanged()
        {
            RefreshLanguageSwitcher();
            ApplyStaticUiText();

            if (TabDokumente.IsChecked == true) LoadDokumenteTab();
            else if (TabVault.IsChecked == true) LoadVaultTab();
            else if (TabTemplates.IsChecked == true) LoadTemplatesTab();
            else if (TabBackups.IsChecked == true) LoadBackupsTab();

            // Tooltip/Hinweiszeile der AVAS-Pille sofort in der neuen Sprache neu aufbauen
            if (_stickIndicators.Count > 0)
                RefreshStickIndicators();
        }

        // ===================== STICK-INDIKATOREN (TOPBAR) =====================

        // Baut die Pillen für die bekannten Kpmn-Sticks einmalig in die Topbar
        private void BuildStickIndicators()
        {
            foreach (var stick in TopbarSticks)
            {
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Margin = new Thickness(0, 0, 5, 0)
                };

                var label = new TextBlock
                {
                    Text = stick.Name,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Warnzeichen hinter dem Namen – nur sichtbar, wenn der AVAS-Stick eine Wartung braucht
                var warnMark = new TextBlock
                {
                    Text = "⚠",
                    FontSize = 9,
                    Foreground = AvasWarnAccentBrush,
                    Margin = new Thickness(2, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed
                };

                var pillContent = new StackPanel { Orientation = Orientation.Horizontal };
                pillContent.Children.Add(dot);
                pillContent.Children.Add(label);
                pillContent.Children.Add(warnMark);

                var pill = new Border
                {
                    Background = (Brush)FindResource("BgDeep"),
                    BorderBrush = (Brush)FindResource("BorderColor"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(6, 0, 0, 0),
                    Child = pillContent
                };

                if (string.Equals(stick.VolumeLabel, AvasVolumeLabel, StringComparison.OrdinalIgnoreCase))
                {
                    pill.MouseLeftButtonUp += AvasPill_Click;
                }

                StickPanel.Children.Add(pill);
                _stickIndicators.Add(new StickIndicator(dot, label, stick.VolumeLabel, CreateFrozenBrush(stick.AccentHex), pill, warnMark));
            }
        }

        // Aktualisiert Farbe/Text der Stick-Pillen anhand der aktuell eingesteckten Wechseldatenträger
        private void RefreshStickIndicators()
        {
            var avasStatus = GetAvasStatus();

            foreach (var indicator in _stickIndicators)
            {
                var online = FindConnectedStick(indicator.VolumeLabel) != null;
                indicator.Dot.Fill = online ? indicator.AccentBrush : (Brush)FindResource("BorderColor");
                indicator.Label.Foreground = online ? StickOnlineNameBrush : (Brush)FindResource("TextMuted");

                if (!string.Equals(indicator.VolumeLabel, AvasVolumeLabel, StringComparison.OrdinalIgnoreCase))
                    continue;

                ApplyAvasWarnLook(indicator, online && avasStatus is { IsStale: true } ? avasStatus : null);
            }

            UpdateAvasNotice(avasStatus);
        }

        // ===================== AVAS-WARTUNGSHINWEIS (REMINDER-PASSTHROUGH) =====================

        // Ergebnis der Veraltungs-Prüfung des AVAS-Sticks (gleiche Schwellwerte wie AVAS selbst)
        private sealed record AvasStatus(int? SdiAgeDays, int StaleCount, int TotalCount)
        {
            public bool IsStale => SdiAgeDays > AvasSdiStaleDays || StaleCount > 0;
        }

        private AvasStatus? _avasStatusCache;
        private string? _avasStatusCacheRoot;
        private DateTime _avasStatusCacheTime = DateTime.MinValue;
        private static readonly TimeSpan AvasStatusCacheLifetime = TimeSpan.FromSeconds(30);

        // Aktueller AVAS-Status; das Ergebnis wird kurz zwischengespeichert, damit der 2-Sekunden-Timer
        // nicht permanent über den Stick liest. null = AVAS-Stick nicht eingesteckt oder nicht lesbar.
        private AvasStatus? GetAvasStatus()
        {
            var drive = FindConnectedStick(AvasVolumeLabel);
            if (drive == null)
            {
                _avasStatusCache = null;
                _avasStatusCacheRoot = null;
                return null;
            }

            var root = drive.RootDirectory.FullName;
            if (_avasStatusCache != null
                && string.Equals(_avasStatusCacheRoot, root, StringComparison.OrdinalIgnoreCase)
                && DateTime.Now - _avasStatusCacheTime < AvasStatusCacheLifetime)
            {
                return _avasStatusCache;
            }

            _avasStatusCache = CheckAvasStatus(drive);
            _avasStatusCacheRoot = root;
            _avasStatusCacheTime = DateTime.Now;
            return _avasStatusCache;
        }

        // Spiegelt die AVAS-eigene Logik: neueste Drivers\SDI_R*.exe (> 150 Tage) und jede Files\*.exe (> 180 Tage).
        // Fehlende Ordner werden übersprungen – der Stick kann in jedem Zustand sein.
        private static AvasStatus? CheckAvasStatus(DriveInfo avasDrive)
        {
            try
            {
                var root = avasDrive.RootDirectory.FullName;
                var now = DateTime.Now;

                int? sdiAgeDays = null;
                var driversDir = Path.Combine(root, "Drivers");
                if (Directory.Exists(driversDir))
                {
                    var newestSdi = new DirectoryInfo(driversDir)
                        .GetFiles("SDI_R*.exe")
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();

                    if (newestSdi != null)
                        sdiAgeDays = (int)(now - newestSdi.LastWriteTime).TotalDays;
                }

                var staleCount = 0;
                var totalCount = 0;
                var filesDir = Path.Combine(root, "Files");
                if (Directory.Exists(filesDir))
                {
                    foreach (var file in new DirectoryInfo(filesDir).GetFiles("*.exe"))
                    {
                        totalCount++;
                        if ((now - file.LastWriteTime).TotalDays > AvasPackageStaleDays)
                            staleCount++;
                    }
                }

                return new AvasStatus(sdiAgeDays, staleCount, totalCount);
            }
            catch
            {
                // Stick abgezogen / nicht lesbar – kein Hinweis statt Absturz
                return null;
            }
        }

        private string? _avasTooltipKey;

        // Setzt bzw. entfernt den Warn-Look an der AVAS-Pille (status == null => normaler Zustand)
        private void ApplyAvasWarnLook(StickIndicator indicator, AvasStatus? status)
        {
            if (status == null)
            {
                indicator.Pill.Background = (Brush)FindResource("BgDeep");
                indicator.Pill.BorderBrush = (Brush)FindResource("BorderColor");
                indicator.Pill.Cursor = null;
                indicator.Pill.ToolTip = null;
                indicator.WarnMark.Visibility = Visibility.Collapsed;
                indicator.Dot.Effect = null;
                _avasTooltipKey = null;
                return;
            }

            indicator.Pill.Background = AvasWarnPillBackgroundBrush;
            indicator.Pill.BorderBrush = AvasWarnBorderBrush;
            indicator.Pill.Cursor = System.Windows.Input.Cursors.Hand;
            indicator.Label.Foreground = AvasWarnValueBrush;
            indicator.WarnMark.Visibility = Visibility.Visible;

            // weicher Amber-Glow um den Status-Punkt (Entsprechung zu box-shadow 0 0 0 3px rgba(255,176,32,.3))
            indicator.Dot.Effect ??= new DropShadowEffect
            {
                Color = Color.FromRgb(0xFF, 0xB0, 0x20),
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.85
            };

            var key = $"{status.SdiAgeDays}|{status.StaleCount}|{status.TotalCount}|{Loc.Current}";
            if (_avasTooltipKey != key || indicator.Pill.ToolTip == null)
            {
                indicator.Pill.ToolTip = BuildAvasTooltip(status);
                _avasTooltipKey = key;
            }
        }

        // Dunkler Tooltip im Kpmn-Stil, der unter der AVAS-Pille erscheint
        private ToolTip BuildAvasTooltip(AvasStatus status)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = "⚠ " + Loc.T("Avas.TooltipTitle"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = AvasWarnAccentBrush,
                Margin = new Thickness(0, 0, 0, 7)
            });

            if (status.SdiAgeDays.HasValue)
            {
                panel.Children.Add(BuildAvasTooltipRow(
                    Loc.T("Avas.TooltipSdiRow"),
                    status.SdiAgeDays.Value.ToString()));
            }

            if (status.StaleCount > 0)
            {
                panel.Children.Add(BuildAvasTooltipRow(
                    Loc.T("Avas.TooltipPackagesRow"),
                    status.StaleCount.ToString(),
                    status.TotalCount.ToString()));
            }

            var footer = new TextBlock
            {
                Text = Loc.T("Avas.TooltipFooter"),
                FontSize = 10,
                Foreground = (Brush)FindResource("TextMuted"),
                TextWrapping = TextWrapping.Wrap
            };

            var footerBorder = new Border
            {
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(0, 7, 0, 0),
                Child = footer
            };
            panel.Children.Add(footerBorder);

            var callout = new Border
            {
                Background = AvasTooltipBackgroundBrush,
                BorderBrush = AvasWarnBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Child = panel
            };

            return new ToolTip
            {
                Content = callout,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HasDropShadow = true,
                MaxWidth = 360,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                VerticalOffset = 6,
                FontFamily = FontFamily
            };
        }

        private TextBlock BuildAvasTooltipRow(string format, params string[] values)
        {
            var text = new TextBlock
            {
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2)
            };

            text.Inlines.Add(new Run("● ") { Foreground = AvasWarnAccentBrush });
            AddHighlightedInlines(text, format, AvasTooltipTextBrush, AvasWarnValueBrush, values);
            return text;
        }

        // Baut einen Format-String ({0}, {1}, ...) als Inlines auf, wobei die eingesetzten Werte hervorgehoben werden
        private static void AddHighlightedInlines(TextBlock target, string format, Brush normalBrush, Brush valueBrush, params string[] values)
        {
            foreach (var part in Regex.Split(format, @"(\{\d+\})"))
            {
                if (part.Length == 0)
                    continue;

                var match = Regex.Match(part, @"^\{(\d+)\}$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var index) && index < values.Length)
                {
                    target.Inlines.Add(new Run(values[index]) { Foreground = valueBrush, FontWeight = FontWeights.Bold });
                }
                else
                {
                    target.Inlines.Add(new Run(part) { Foreground = normalBrush });
                }
            }
        }

        // Klick auf die AVAS-Pille: nur im Warnzustand aktiv -> Dokumente-Tab mit AVAS-Ordner öffnen
        private void AvasPill_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (GetAvasStatus() is not { IsStale: true })
                return;

            if (TabDokumente.IsChecked != true)
                TabDokumente.IsChecked = true;

            SelectAvasDocumentFolder();
        }

        // Wählt den AVAS-Ordner in der Dokumente-Sidebar aus (falls die Sidebar gebaut ist)
        private void SelectAvasDocumentFolder()
        {
            if (_folderList == null || !_folderList.Items.Contains(AvasDocumentFolder))
                return;

            _folderList.SelectedItem = AvasDocumentFolder;
        }

        // ---------- Notice-Bar im Dokumente-Tab ----------

        private Border? _avasNoticeBar;
        private TextBlock? _avasNoticeText;
        private bool _avasNoticeDismissed;
        private string? _avasNoticeKey;

        // Kompakte Hinweiszeile über der Dateiliste (Aufbau einmal pro LoadDokumenteTab)
        private Border BuildAvasNoticeBar()
        {
            var grid = new Grid { Margin = new Thickness(9, 7, 9, 7) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = "⚠",
                FontSize = 12,
                Foreground = AvasWarnAccentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            _avasNoticeText = new TextBlock
            {
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_avasNoticeText, 1);
            grid.Children.Add(_avasNoticeText);

            var dismiss = new TextBlock
            {
                Text = "✕",
                FontSize = 11,
                Foreground = (Brush)FindResource("TextMuted"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = Loc.T("Avas.NoticeDismiss")
            };
            dismiss.MouseLeftButtonUp += (_, _) =>
            {
                _avasNoticeDismissed = true;
                if (_avasNoticeBar != null)
                    _avasNoticeBar.Visibility = Visibility.Collapsed;
            };
            Grid.SetColumn(dismiss, 2);
            grid.Children.Add(dismiss);

            // linker Akzentstreifen (#FFB020) innerhalb des amber getönten Rahmens
            var inner = new DockPanel();
            var accent = new Border { Width = 3, Background = AvasWarnAccentBrush };
            DockPanel.SetDock(accent, Dock.Left);
            inner.Children.Add(accent);
            inner.Children.Add(grid);

            return new Border
            {
                Background = AvasNoticeBackgroundBrush,
                BorderBrush = AvasWarnBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(14, 0, 14, 8),
                Visibility = Visibility.Collapsed,
                Child = inner
            };
        }

        private void UpdateAvasNotice() => UpdateAvasNotice(GetAvasStatus());

        // Zeigt die Hinweiszeile nur im Dokumente-Tab, im AVAS-Ordner und solange sie nicht weggeklickt wurde
        private void UpdateAvasNotice(AvasStatus? status)
        {
            if (_avasNoticeBar == null || _avasNoticeText == null)
                return;

            var show = !_avasNoticeDismissed
                && TabDokumente.IsChecked == true
                && string.Equals(_currentFolder, AvasDocumentFolder, StringComparison.OrdinalIgnoreCase)
                && status is { IsStale: true };

            if (!show)
            {
                _avasNoticeBar.Visibility = Visibility.Collapsed;
                return;
            }

            var key = $"{status!.SdiAgeDays}|{status.StaleCount}|{status.TotalCount}|{Loc.Current}";
            if (_avasNoticeKey != key || _avasNoticeText.Inlines.Count == 0)
            {
                _avasNoticeText.Inlines.Clear();
                _avasNoticeText.Inlines.Add(new Run(Loc.T("Avas.NoticePrefix") + " ")
                {
                    Foreground = AvasWarnAccentBrush,
                    FontWeight = FontWeights.Bold
                });

                var first = true;
                if (status.SdiAgeDays.HasValue)
                {
                    AddHighlightedInlines(_avasNoticeText, Loc.T("Avas.NoticeSdi"), AvasTooltipTextBrush, AvasWarnValueBrush,
                        status.SdiAgeDays.Value.ToString());
                    first = false;
                }

                if (status.StaleCount > 0)
                {
                    if (!first)
                        _avasNoticeText.Inlines.Add(new Run(" · ") { Foreground = AvasTooltipTextBrush });

                    AddHighlightedInlines(_avasNoticeText, Loc.T("Avas.NoticePackages"), AvasTooltipTextBrush, AvasWarnValueBrush,
                        status.StaleCount.ToString(), status.TotalCount.ToString());
                }

                _avasNoticeKey = key;
            }

            _avasNoticeBar.Visibility = Visibility.Visible;
        }

        // Prüft ob die EXE von einem Wechseldatenträger ausgeführt wird
        private void CheckUsbStick()
        {
            try
            {
                var rootPath = Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory);

                if (string.IsNullOrEmpty(rootPath))
                {
                    _isRunningFromUsbStick = false;
                    return;
                }

                var drive = new DriveInfo(rootPath);
                _isRunningFromUsbStick = drive.IsReady && drive.DriveType == DriveType.Removable;
            }
            catch
            {
                _isRunningFromUsbStick = false;
            }
        }

        // Legt die feste Ordnerstruktur an, falls sie noch nicht existiert
        private void EnsureDocumentFolders()
        {
            try
            {
                Directory.CreateDirectory(_documentsRoot);

                foreach (var folder in FixedDocumentFolders)
                {
                    Directory.CreateDirectory(Path.Combine(_documentsRoot, folder));
                }
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Dokumente.ErrorCreatingFolderStructure"), ex.Message);
            }
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton clickedTab || ContentArea == null)
                return;

            if (clickedTab.Name != "TabVault" && _vaultService.IsUnlocked)
            {
                LockVault();
            }

            if (clickedTab.Name != "TabTemplates")
            {
                SaveCurrentSnippet(showStatus: false);
            }

            ContentArea.Children.Clear();

            switch (clickedTab.Name)
            {
                case "TabDokumente":
                    LoadDokumenteTab();
                    break;
                case "TabVault":
                    LoadVaultTab();
                    break;
                case "TabTemplates":
                    LoadTemplatesTab();
                    break;
                case "TabBackups":
                    LoadBackupsTab();
                    break;
                default:
                    var placeholder = new TextBlock
                    {
                        Text = string.Format(Loc.T("Tab.ComingSoon"), clickedTab.Content),
                        Foreground = (Brush)FindResource("TextMuted"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 13
                    };
                    ContentArea.Children.Add(placeholder);
                    break;
            }
        }

        private void ShowUsbNotDetected()
        {
            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var icon = new TextBlock
            {
                Text = Loc.T("UsbNotDetected.Title"),
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(icon);

            var subtext = new TextBlock
            {
                Text = Loc.T("UsbNotDetected.Message"),
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(subtext);

            ContentArea.Children.Add(panel);

            StatusLeft.Text = Loc.T("Status.NoUsbStick");
        }

        // ===================== DOKUMENTE TAB =====================

        private ListBox _folderList = null!;
        private ListBox _fileList = null!;
        private StackPanel _fileToolbar = null!;
        private Button _upButton = null!;
        private StackPanel _stickNotDetectedPanel = null!;
        private StackPanel _previewPanel = null!;

        // Wurzelverzeichnis des aktuell gewählten Sidebar-Ordners (Stick-Root oder lokaler Ordner) und aktuell angezeigter Unterpfad
        private string _currentBrowseRoot = "";
        private string _currentBrowsePath = "";

        private void LoadDokumenteTab()
        {
            ContentArea.Children.Clear();
            _avasNoticeBar = null;
            _avasNoticeText = null;
            _avasNoticeKey = null;

            if (!_isRunningFromUsbStick)
            {
                ShowUsbNotDetected();
                return;
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ---------- Spalte 1: Ordnerliste ----------
            var folderBorder = new Border
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(0, 0, 1, 0)
            };

            var folderStack = new DockPanel();

            var folderHeader = new TextBlock
            {
                Text = Loc.T("Dokumente.FolderHeader"),
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 10,
                Padding = new Thickness(14, 10, 14, 10)
            };
            DockPanel.SetDock(folderHeader, Dock.Top);
            folderStack.Children.Add(folderHeader);

            _folderList = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ItemContainerStyle = (Style)FindResource("FolderItemStyle")
            };

            foreach (var folder in FixedDocumentFolders)
                _folderList.Items.Add(folder);

            _folderList.SelectionChanged += FolderList_SelectionChanged;
            folderStack.Children.Add(_folderList);

            folderBorder.Child = folderStack;
            Grid.SetColumn(folderBorder, 0);
            grid.Children.Add(folderBorder);

            // ---------- Spalte 2: Dateiliste ----------
            var fileBorder = new Border
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(0, 0, 1, 0)
            };

            var fileStack = new DockPanel();

            var fileHeaderText = new TextBlock
            {
                Text = Loc.T("Dokumente.SelectFolder"),
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 10,
                Padding = new Thickness(14, 10, 14, 10)
            };
            fileHeaderText.Name = "FileListHeader";
            DockPanel.SetDock(fileHeaderText, Dock.Top);
            fileStack.Children.Add(fileHeaderText);
            _fileListHeader = fileHeaderText;

            _fileToolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(14, 0, 14, 8)
            };
            _upButton = new Button
            {
                Content = Loc.T("Dokumente.Back"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                IsEnabled = false
            };
            _upButton.Click += UpButton_Click;
            _fileToolbar.Children.Add(_upButton);
            DockPanel.SetDock(_fileToolbar, Dock.Top);
            fileStack.Children.Add(_fileToolbar);

            // AVAS-Wartungshinweis direkt über der Dateiliste (nur sichtbar im AVAS-Ordner bei veraltetem Stick)
            _avasNoticeBar = BuildAvasNoticeBar();
            DockPanel.SetDock(_avasNoticeBar, Dock.Top);
            fileStack.Children.Add(_avasNoticeBar);

            _fileList = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ItemContainerStyle = (Style)FindResource("FileItemStyle")
            };
            _fileList.SelectionChanged += FileList_SelectionChanged;
            _fileList.MouseDoubleClick += FileList_MouseDoubleClick;
            fileStack.Children.Add(_fileList);

            _stickNotDetectedPanel = new StackPanel
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(14, 12, 14, 0)
            };
            fileStack.Children.Add(_stickNotDetectedPanel);

            fileBorder.Child = fileStack;
            Grid.SetColumn(fileBorder, 1);
            grid.Children.Add(fileBorder);

            // ---------- Spalte 3: Vorschau ----------
            _previewPanel = new StackPanel
            {
                Margin = new Thickness(20),
                VerticalAlignment = VerticalAlignment.Top
            };

            ShowEmptyPreview(Loc.T("Dokumente.SelectFileHint"));

            Grid.SetColumn(_previewPanel, 2);
            grid.Children.Add(_previewPanel);

            ContentArea.Children.Add(grid);

            // Ersten Ordner direkt vorauswählen
            if (_folderList.Items.Count > 0)
                _folderList.SelectedIndex = 0;
        }

        private TextBlock _fileListHeader = null!;
        private string _currentFolder = "";

        private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_folderList.SelectedItem is not string folderName)
                return;

            _currentFolder = folderName;
            ShowEmptyPreview(Loc.T("Dokumente.SelectFileHint"));
            UpdateAvasNotice();

            if (StickFolderMap.TryGetValue(folderName, out var stick))
            {
                var drive = FindConnectedStick(stick.VolumeLabel);
                if (drive == null)
                {
                    ShowStickNotDetected(stick.DisplayName);
                    return;
                }

                SetFileListVisible(true);
                _currentBrowseRoot = drive.RootDirectory.FullName;
                NavigateTo(_currentBrowseRoot);
                return;
            }

            SetFileListVisible(true);
            var folderPath = Path.Combine(_documentsRoot, folderName);

            if (!Directory.Exists(folderPath))
            {
                StatusLeft.Text = string.Format(Loc.T("Dokumente.FolderNotFound"), folderName);
                _fileList.Items.Clear();
                _fileListHeader.Text = folderName.ToUpper();
                return;
            }

            _currentBrowseRoot = folderPath;
            NavigateTo(_currentBrowseRoot);
        }

        // Navigiert die Dateiliste zu einem Unterpfad innerhalb des aktuellen Sidebar-Ordners
        private void NavigateTo(string path)
        {
            _currentBrowsePath = path;
            PopulateFileList(path, _currentFolder);

            var relative = Path.GetRelativePath(_currentBrowseRoot, path);
            _fileListHeader.Text = relative == "."
                ? _currentFolder.ToUpper()
                : $"{_currentFolder.ToUpper()} / {relative.Replace('\\', '/')}";

            _upButton.IsEnabled = !string.Equals(path, _currentBrowseRoot, StringComparison.OrdinalIgnoreCase);
            ShowEmptyPreview(Loc.T("Dokumente.SelectFileHint"));
        }

        private void UpButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.Equals(_currentBrowsePath, _currentBrowseRoot, StringComparison.OrdinalIgnoreCase))
                return;

            var parent = Directory.GetParent(_currentBrowsePath);
            if (parent == null)
                return;

            NavigateTo(parent.FullName);
        }

        private void FileList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_fileList.SelectedItem is FileEntry entry && entry.IsDirectory)
            {
                NavigateTo(entry.FullPath);
            }
        }

        // Sucht einen eingesteckten Wechseldatenträger mit dem angegebenen Datenträger-Label
        private static DriveInfo? FindConnectedStick(string volumeLabel)
        {
            return DriveInfo.GetDrives().FirstOrDefault(d =>
                d.IsReady
                && d.DriveType == DriveType.Removable
                && string.Equals(d.VolumeLabel, volumeLabel, StringComparison.OrdinalIgnoreCase));
        }

        // Listet Unterordner und Dateien eines Verzeichnisses in die Dateiliste
        private void PopulateFileList(string path, string folderName)
        {
            _fileList.Items.Clear();

            try
            {
                // Unterordner zuerst
                foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
                {
                    _fileList.Items.Add(new FileEntry
                    {
                        Name = Path.GetFileName(dir),
                        FullPath = dir,
                        IsDirectory = true
                    });
                }

                // Dann Dateien
                foreach (var file in Directory.GetFiles(path).OrderBy(f => f))
                {
                    _fileList.Items.Add(new FileEntry
                    {
                        Name = Path.GetFileName(file),
                        FullPath = file,
                        IsDirectory = false
                    });
                }

                StatusLeft.Text = string.Format(Loc.T("Dokumente.EntriesInFolder"), _fileList.Items.Count, folderName);
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Dokumente.ReadError"), ex.Message);
            }
        }

        // Blendet zwischen Dateiliste und "Stick nicht erkannt"-Hinweis um
        private void SetFileListVisible(bool visible)
        {
            _fileList.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _fileToolbar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _stickNotDetectedPanel.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        }

        // Zeigt in der Dateiliste-Spalte an, dass der zugehörige Stick nicht eingesteckt ist
        private void ShowStickNotDetected(string displayName)
        {
            SetFileListVisible(false);

            _stickNotDetectedPanel.Children.Clear();
            _stickNotDetectedPanel.Children.Add(new TextBlock
            {
                Text = string.Format(Loc.T("Dokumente.StickNotDetectedTitle"), displayName),
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            _stickNotDetectedPanel.Children.Add(new TextBlock
            {
                Text = string.Format(Loc.T("Dokumente.StickNotDetectedMessage"), displayName),
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });

            StatusLeft.Text = string.Format(Loc.T("Dokumente.StickNotDetectedStatus"), displayName);
        }

        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fileList.SelectedItem is not FileEntry entry)
                return;

            if (entry.IsDirectory)
            {
                ShowEmptyPreview(string.Format(Loc.T("Dokumente.IsAFolder"), entry.Name));
                return;
            }

            try
            {
                var info = new FileInfo(entry.FullPath);
                ShowFilePreview(info);
            }
            catch (Exception ex)
            {
                ShowEmptyPreview(string.Format(Loc.T("Dokumente.PreviewError"), ex.Message));
            }
        }

        private void ShowEmptyPreview(string message)
        {
            _previewPanel.Children.Clear();
            _previewPanel.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }

        private void ShowFilePreview(FileInfo info)
        {
            _previewPanel.Children.Clear();

            var name = new TextBlock
            {
                Text = info.Name,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _previewPanel.Children.Add(name);

            var sizeKb = info.Length / 1024.0;
            var sizeText = sizeKb < 1024
                ? $"{sizeKb:0.#} KB"
                : $"{sizeKb / 1024:0.#} MB";

            var meta = new TextBlock
            {
                Text = string.Format(Loc.T("Dokumente.LastModified"), sizeText, info.LastWriteTime.ToString("dd.MM.yyyy")),
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 16)
            };
            _previewPanel.Children.Add(meta);

            var divider = new Border
            {
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 0, 0, 0)
            };
            _previewPanel.Children.Add(divider);

            var note = new TextBlock
            {
                Text = Loc.T("Dokumente.ContentPreviewComingSoon"),
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 11,
                Margin = new Thickness(0, 16, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            _previewPanel.Children.Add(note);
        }

        // ===================== VAULT TAB =====================

        private void LoadVaultTab()
        {
            ContentArea.Children.Clear();

            if (_vaultService.IsUnlocked)
                ShowVaultBrowser();
            else if (_vaultService.VaultExists())
                ShowVaultUnlockPrompt();
            else
                ShowVaultSetupPrompt();
        }

        private void ShowVaultSetupPrompt()
        {
            var panel = BuildVaultPromptPanel(
                Loc.T("Vault.SetupTitle"),
                Loc.T("Vault.SetupDescription"),
                confirmVisible: true,
                Loc.T("Vault.CreateButton"),
                out var passwordBox,
                out var confirmBox,
                out var errorText,
                out var button);

            button.Click += (_, _) =>
            {
                if (string.IsNullOrEmpty(passwordBox.Password))
                {
                    ShowVaultError(errorText, Loc.T("Vault.ErrorEnterPassword"));
                    return;
                }

                if (passwordBox.Password != confirmBox.Password)
                {
                    ShowVaultError(errorText, Loc.T("Vault.ErrorPasswordMismatch"));
                    return;
                }

                try
                {
                    _vaultService.CreateNew(passwordBox.Password);
                    ShowVaultBrowser();
                }
                catch (Exception ex)
                {
                    ShowVaultError(errorText, string.Format(Loc.T("Vault.ErrorCreating"), ex.Message));
                }
            };

            ContentArea.Children.Add(panel);
        }

        private void ShowVaultUnlockPrompt()
        {
            var panel = BuildVaultPromptPanel(
                Loc.T("Vault.LockedTitle"),
                Loc.T("Vault.LockedDescription"),
                confirmVisible: false,
                Loc.T("Vault.UnlockButton"),
                out var passwordBox,
                out _,
                out var errorText,
                out var button);

            void TryUnlock()
            {
                try
                {
                    _vaultService.Unlock(passwordBox.Password);
                    ShowVaultBrowser();
                }
                catch (CryptographicException)
                {
                    ShowVaultError(errorText, Loc.T("Vault.WrongPassword"));
                }
                catch (Exception ex)
                {
                    ShowVaultError(errorText, string.Format(Loc.T("Vault.ErrorUnlocking"), ex.Message));
                }
            }

            button.Click += (_, _) => TryUnlock();
            passwordBox.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                    TryUnlock();
            };

            ContentArea.Children.Add(panel);
        }

        private static void ShowVaultError(TextBlock errorText, string message)
        {
            errorText.Text = message;
            errorText.Visibility = Visibility.Visible;
        }

        // Baut das zentrierte Formular für Vault-Einrichtung/-Entsperrung
        private StackPanel BuildVaultPromptPanel(
            string title,
            string description,
            bool confirmVisible,
            string buttonText,
            out PasswordBox passwordBox,
            out PasswordBox confirmBox,
            out TextBlock errorText,
            out Button button)
        {
            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 320
            };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            });

            panel.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            });

            passwordBox = new PasswordBox
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                Foreground = (Brush)FindResource("TextPrimary"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(passwordBox);

            confirmBox = new PasswordBox
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                Foreground = (Brush)FindResource("TextPrimary"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = confirmVisible ? Visibility.Visible : Visibility.Collapsed
            };
            if (confirmVisible)
                panel.Children.Add(confirmBox);

            errorText = new TextBlock
            {
                Foreground = Brushes.IndianRed,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            panel.Children.Add(errorText);

            button = new Button
            {
                Content = buttonText,
                Style = (Style)FindResource("ToolbarButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(button);

            return panel;
        }

        private void ShowVaultBrowser()
        {
            RegisterVaultActivity();
            ContentArea.Children.Clear();

            var root = new DockPanel();

            var header = new TextBlock
            {
                Text = Loc.T("Vault.Header"),
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 10,
                Padding = new Thickness(14, 10, 14, 10)
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 14, 8) };

            var addButton = new Button { Content = Loc.T("Vault.AddFile"), Style = (Style)FindResource("ToolbarButtonStyle") };
            addButton.Click += VaultAddButton_Click;
            toolbar.Children.Add(addButton);

            var removeButton = new Button { Content = Loc.T("Vault.Remove"), Style = (Style)FindResource("ToolbarButtonStyle") };
            removeButton.Click += VaultRemoveButton_Click;
            toolbar.Children.Add(removeButton);

            var lockButton = new Button { Content = Loc.T("Vault.Lock"), Style = (Style)FindResource("ToolbarButtonStyle") };
            lockButton.Click += (_, _) => { LockVault(); LoadVaultTab(); };
            toolbar.Children.Add(lockButton);

            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            _vaultList = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ItemContainerStyle = (Style)FindResource("FileItemStyle")
            };
            _vaultList.PreviewMouseDown += (_, _) => RegisterVaultActivity();
            _vaultList.MouseDoubleClick += VaultList_MouseDoubleClick;
            root.Children.Add(_vaultList);

            ContentArea.Children.Add(root);

            RefreshVaultList();
        }

        private void RefreshVaultList()
        {
            _vaultList.Items.Clear();

            try
            {
                foreach (var entry in _vaultService.ListEntries())
                    _vaultList.Items.Add(entry);

                StatusLeft.Text = string.Format(Loc.T("Vault.EntriesCount"), _vaultList.Items.Count);
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Vault.ErrorReading"), ex.Message);
            }
        }

        private void VaultAddButton_Click(object sender, RoutedEventArgs e)
        {
            RegisterVaultActivity();

            var dialog = new OpenFileDialog { Multiselect = true, Title = Loc.T("Vault.AddFileDialogTitle") };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                foreach (var file in dialog.FileNames)
                    _vaultService.AddFile(file, Path.GetFileName(file));

                RefreshVaultList();
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Vault.ErrorAdding"), ex.Message);
            }
        }

        private void VaultRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            RegisterVaultActivity();

            if (_vaultList.SelectedItem is not VaultListEntry entry)
                return;

            var confirmed = ShowConfirmDialog(
                Loc.T("Vault.RemoveConfirmTitle"),
                string.Format(Loc.T("Vault.RemoveConfirmMessage"), entry.Name),
                Loc.T("Common.ConfirmRemoval"));

            if (!confirmed)
                return;

            try
            {
                _vaultService.RemoveEntry(entry.Name);
                RefreshVaultList();
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Vault.ErrorRemoving"), ex.Message);
            }
        }

        private void VaultList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            RegisterVaultActivity();

            if (_vaultList.SelectedItem is not VaultListEntry entry)
                return;

            try
            {
                var bytes = _vaultService.ExtractEntry(entry.Name);
                Directory.CreateDirectory(_vaultTempDir);
                var tempPath = Path.Combine(_vaultTempDir, entry.Name);
                File.WriteAllBytes(tempPath, bytes);

                Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
                StatusLeft.Text = string.Format(Loc.T("Vault.OpenedTemporarily"), entry.Name);
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Vault.ErrorOpening"), ex.Message);
            }
        }

        private void RegisterVaultActivity()
        {
            _vaultLastActivity = DateTime.Now;
        }

        private void CheckVaultAutoLock()
        {
            if (!_vaultService.IsUnlocked)
                return;

            if (DateTime.Now - _vaultLastActivity < VaultAutoLockTimeout)
                return;

            LockVault();

            if (TabVault.IsChecked == true)
            {
                LoadVaultTab();
                StatusLeft.Text = Loc.T("Vault.AutoLockedStatus");
            }
        }

        private void LockVault()
        {
            _vaultService.Lock();
            CleanupVaultTempFiles();
        }

        // Löscht temporär entschlüsselte Dateien; schlägt harmlos fehl, falls eine Datei noch von einem externen Programm geöffnet ist
        private void CleanupVaultTempFiles()
        {
            try
            {
                if (Directory.Exists(_vaultTempDir))
                    Directory.Delete(_vaultTempDir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        // ===================== TEMPLATES TAB =====================

        private void LoadTemplatesTab()
        {
            ContentArea.Children.Clear();

            if (!_isRunningFromUsbStick)
            {
                ShowUsbNotDetected();
                return;
            }

            Directory.CreateDirectory(_templateDocsRoot);
            Directory.CreateDirectory(_templateSnippetsRoot);

            var root = new DockPanel();

            var subTabBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 0, 0) };

            var docsSubTab = new RadioButton
            {
                Content = Loc.T("Templates.DocsSubTab"),
                GroupName = "TemplatesSubTabs",
                Style = (Style)FindResource("TabButtonStyle"),
                IsChecked = true
            };
            var snippetsSubTab = new RadioButton
            {
                Content = Loc.T("Templates.SnippetsSubTab"),
                GroupName = "TemplatesSubTabs",
                Style = (Style)FindResource("TabButtonStyle")
            };
            docsSubTab.Checked += (_, _) => ShowTemplateDocs();
            snippetsSubTab.Checked += (_, _) => ShowTemplateSnippets();
            subTabBar.Children.Add(docsSubTab);
            subTabBar.Children.Add(snippetsSubTab);

            DockPanel.SetDock(subTabBar, Dock.Top);
            root.Children.Add(subTabBar);

            _templatesContentHost = new Grid();
            root.Children.Add(_templatesContentHost);

            ContentArea.Children.Add(root);

            ShowTemplateDocs();
        }

        // ----- Dokumentvorlagen -----

        private void ShowTemplateDocs()
        {
            if (_templatesContentHost == null)
                return;

            _templatesContentHost.Children.Clear();

            var dock = new DockPanel();

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 14, 8) };

            var addBtn = new Button { Content = Loc.T("Templates.AddDoc"), Style = (Style)FindResource("ToolbarButtonStyle") };
            addBtn.Click += TemplateDocsAddButton_Click;
            var removeBtn = new Button { Content = Loc.T("Templates.RemoveDoc"), Style = (Style)FindResource("ToolbarButtonStyle") };
            removeBtn.Click += TemplateDocsRemoveButton_Click;
            var useBtn = new Button { Content = Loc.T("Templates.UseDoc"), Style = (Style)FindResource("ToolbarButtonStyle") };
            useBtn.Click += (_, _) => UseSelectedTemplateDoc();

            toolbar.Children.Add(addBtn);
            toolbar.Children.Add(removeBtn);
            toolbar.Children.Add(useBtn);
            DockPanel.SetDock(toolbar, Dock.Top);
            dock.Children.Add(toolbar);

            _templateDocsList = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ItemContainerStyle = (Style)FindResource("FileItemStyle"),
                Margin = new Thickness(14, 0, 14, 14)
            };
            _templateDocsList.MouseDoubleClick += (_, _) => UseSelectedTemplateDoc();
            dock.Children.Add(_templateDocsList);

            _templatesContentHost.Children.Add(dock);

            RefreshTemplateDocsList();
        }

        private void RefreshTemplateDocsList()
        {
            _templateDocsList.Items.Clear();

            try
            {
                foreach (var file in Directory.GetFiles(_templateDocsRoot).OrderBy(f => f))
                {
                    _templateDocsList.Items.Add(new FileEntry
                    {
                        Name = Path.GetFileName(file),
                        FullPath = file,
                        IsDirectory = false
                    });
                }

                StatusLeft.Text = string.Format(Loc.T("Templates.DocsCount"), _templateDocsList.Items.Count);
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Templates.ErrorReadingDocs"), ex.Message);
            }
        }

        private void TemplateDocsAddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Multiselect = true, Title = Loc.T("Templates.AddDialogTitle") };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                foreach (var file in dialog.FileNames)
                {
                    var dest = Path.Combine(_templateDocsRoot, Path.GetFileName(file));
                    File.Copy(file, dest, overwrite: true);
                }

                RefreshTemplateDocsList();
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Templates.ErrorAddingDoc"), ex.Message);
            }
        }

        private void TemplateDocsRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_templateDocsList.SelectedItem is not FileEntry entry)
                return;

            var confirmed = ShowConfirmDialog(
                Loc.T("Templates.RemoveDocConfirmTitle"),
                string.Format(Loc.T("Templates.RemoveDocConfirmMessage"), entry.Name),
                Loc.T("Common.ConfirmRemoval"));

            if (!confirmed)
                return;

            try
            {
                File.Delete(entry.FullPath);
                RefreshTemplateDocsList();
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Templates.ErrorRemovingDoc"), ex.Message);
            }
        }

        // Kopiert die gewählte Vorlage an einen selbst gewählten Zielort und öffnet die neue Datei zur Bearbeitung
        private void UseSelectedTemplateDoc()
        {
            if (_templateDocsList.SelectedItem is not FileEntry entry)
                return;

            var extension = Path.GetExtension(entry.Name);
            var dialog = new SaveFileDialog
            {
                Title = Loc.T("Templates.SaveAsDialogTitle"),
                FileName = entry.Name,
                Filter = string.IsNullOrEmpty(extension)
                    ? Loc.T("Templates.AllFilesFilter")
                    : string.Format(Loc.T("Templates.TemplateFileFilter"), extension)
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                File.Copy(entry.FullPath, dialog.FileName, overwrite: true);
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                StatusLeft.Text = string.Format(Loc.T("Templates.NewFileCreated"), entry.Name);
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Templates.ErrorCreatingDoc"), ex.Message);
            }
        }

        // ----- Text-Bausteine -----

        private void ShowTemplateSnippets()
        {
            if (_templatesContentHost == null)
                return;

            _templatesContentHost.Children.Clear();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Spalte 1: Liste der Bausteine
            var listBorder = new Border
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            var listDock = new DockPanel();

            var listToolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 10, 10, 8) };
            var newBtn = new Button { Content = Loc.T("Snippets.New"), Style = (Style)FindResource("ToolbarButtonStyle") };
            newBtn.Click += SnippetNewButton_Click;
            var deleteBtn = new Button { Content = Loc.T("Snippets.Delete"), Style = (Style)FindResource("ToolbarButtonStyle") };
            deleteBtn.Click += SnippetDeleteButton_Click;
            listToolbar.Children.Add(newBtn);
            listToolbar.Children.Add(deleteBtn);
            DockPanel.SetDock(listToolbar, Dock.Top);
            listDock.Children.Add(listToolbar);

            _snippetList = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ItemContainerStyle = (Style)FindResource("FolderItemStyle")
            };
            _snippetList.SelectionChanged += SnippetList_SelectionChanged;
            listDock.Children.Add(_snippetList);

            listBorder.Child = listDock;
            Grid.SetColumn(listBorder, 0);
            grid.Children.Add(listBorder);

            // Spalte 2: Editor
            var editorDock = new DockPanel { Margin = new Thickness(16) };

            var formatToolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var boldBtn = new Button { Content = "B", FontWeight = FontWeights.Bold, Width = 32, Style = (Style)FindResource("ToolbarButtonStyle") };
            boldBtn.Click += (_, _) => EditingCommands.ToggleBold.Execute(null, _snippetEditor);
            var italicBtn = new Button { Content = "I", FontStyle = FontStyles.Italic, Width = 32, Style = (Style)FindResource("ToolbarButtonStyle") };
            italicBtn.Click += (_, _) => EditingCommands.ToggleItalic.Execute(null, _snippetEditor);
            var underlineBtn = new Button { Content = "U", Width = 32, Style = (Style)FindResource("ToolbarButtonStyle") };
            underlineBtn.Click += (_, _) => EditingCommands.ToggleUnderline.Execute(null, _snippetEditor);
            var bulletBtn = new Button { Content = Loc.T("Snippets.BulletList"), Style = (Style)FindResource("ToolbarButtonStyle") };
            bulletBtn.Click += (_, _) => EditingCommands.ToggleBullets.Execute(null, _snippetEditor);
            var saveBtn = new Button { Content = Loc.T("Snippets.Save"), Style = (Style)FindResource("ToolbarButtonStyle") };
            saveBtn.Click += (_, _) => SaveCurrentSnippet(showStatus: true);
            var copyBtn = new Button { Content = Loc.T("Snippets.CopyToClipboard"), Style = (Style)FindResource("ToolbarButtonStyle") };
            copyBtn.Click += SnippetCopyButton_Click;

            formatToolbar.Children.Add(boldBtn);
            formatToolbar.Children.Add(italicBtn);
            formatToolbar.Children.Add(underlineBtn);
            formatToolbar.Children.Add(bulletBtn);
            formatToolbar.Children.Add(saveBtn);
            formatToolbar.Children.Add(copyBtn);
            DockPanel.SetDock(formatToolbar, Dock.Top);
            editorDock.Children.Add(formatToolbar);

            _snippetEditor = new RichTextBox
            {
                Background = (Brush)FindResource("BgSurface"),
                Foreground = (Brush)FindResource("TextPrimary"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                IsEnabled = false
            };
            editorDock.Children.Add(_snippetEditor);

            Grid.SetColumn(editorDock, 1);
            grid.Children.Add(editorDock);

            _templatesContentHost.Children.Add(grid);

            RefreshSnippetList();
        }

        private void RefreshSnippetList()
        {
            _currentSnippetName = null;
            _snippetList.Items.Clear();

            try
            {
                foreach (var file in Directory.GetFiles(_templateSnippetsRoot, "*.rtf").OrderBy(f => f))
                    _snippetList.Items.Add(Path.GetFileNameWithoutExtension(file));

                StatusLeft.Text = string.Format(Loc.T("Snippets.Count"), _snippetList.Items.Count);
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Snippets.ErrorReading"), ex.Message);
            }

            _snippetEditor.Document = new FlowDocument();
            _snippetEditor.IsEnabled = false;
        }

        private void SnippetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveCurrentSnippet(showStatus: false);

            if (_snippetList.SelectedItem is not string name)
            {
                _currentSnippetName = null;
                _snippetEditor.Document = new FlowDocument();
                _snippetEditor.IsEnabled = false;
                return;
            }

            _currentSnippetName = name;

            var doc = new FlowDocument();
            var path = Path.Combine(_templateSnippetsRoot, name + ".rtf");

            try
            {
                if (File.Exists(path))
                {
                    var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                    using var fs = File.OpenRead(path);
                    range.Load(fs, DataFormats.Rtf);
                }

                _snippetEditor.Document = doc;
                _snippetEditor.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Snippets.ErrorLoading"), ex.Message);
            }
        }

        private void SaveCurrentSnippet(bool showStatus)
        {
            if (_currentSnippetName == null)
                return;

            try
            {
                var path = Path.Combine(_templateSnippetsRoot, _currentSnippetName + ".rtf");
                var range = new TextRange(_snippetEditor.Document.ContentStart, _snippetEditor.Document.ContentEnd);
                using var fs = File.Create(path);
                range.Save(fs, DataFormats.Rtf);

                if (showStatus)
                    StatusLeft.Text = string.Format(Loc.T("Snippets.Saved"), _currentSnippetName);
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Snippets.ErrorSaving"), ex.Message);
            }
        }

        private void SnippetNewButton_Click(object sender, RoutedEventArgs e)
        {
            var name = PromptForText(Loc.T("Snippets.NewPromptTitle"), Loc.T("Snippets.NewPromptMessage"));
            if (string.IsNullOrWhiteSpace(name))
                return;

            var invalidChars = Path.GetInvalidFileNameChars();
            var safeName = new string(name.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
            if (string.IsNullOrEmpty(safeName))
            {
                StatusLeft.Text = Loc.T("Snippets.InvalidName");
                return;
            }

            var path = Path.Combine(_templateSnippetsRoot, safeName + ".rtf");
            if (File.Exists(path))
            {
                StatusLeft.Text = Loc.T("Snippets.AlreadyExists");
                return;
            }

            try
            {
                var emptyDoc = new FlowDocument(new Paragraph());
                var range = new TextRange(emptyDoc.ContentStart, emptyDoc.ContentEnd);
                using (var fs = File.Create(path))
                    range.Save(fs, DataFormats.Rtf);

                RefreshSnippetList();
                _snippetList.SelectedItem = safeName;
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Snippets.ErrorCreating"), ex.Message);
            }
        }

        private void SnippetDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_snippetList.SelectedItem is not string name)
                return;

            var confirmed = ShowConfirmDialog(
                Loc.T("Snippets.DeleteConfirmTitle"),
                string.Format(Loc.T("Snippets.DeleteConfirmMessage"), name),
                Loc.T("Common.ConfirmDeletion"));

            if (!confirmed)
                return;

            try
            {
                File.Delete(Path.Combine(_templateSnippetsRoot, name + ".rtf"));
                _currentSnippetName = null;
                RefreshSnippetList();
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Snippets.ErrorDeleting"), ex.Message);
            }
        }

        private void SnippetCopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSnippetName == null)
                return;

            try
            {
                var range = new TextRange(_snippetEditor.Document.ContentStart, _snippetEditor.Document.ContentEnd);
                using var ms = new MemoryStream();
                range.Save(ms, DataFormats.Rtf);
                var rtfText = Encoding.UTF8.GetString(ms.ToArray());

                var dataObject = new DataObject();
                dataObject.SetData(DataFormats.Rtf, rtfText);
                dataObject.SetData(DataFormats.Text, range.Text);
                Clipboard.SetDataObject(dataObject, true);

                StatusLeft.Text = string.Format(Loc.T("Snippets.CopiedToClipboard"), _currentSnippetName);
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Snippets.ErrorCopying"), ex.Message);
            }
        }

        // Kleiner modaler Eingabedialog im AEGIS-Look, da WPF keine eingebaute InputBox mitbringt
        private string? PromptForText(string title, string message)
        {
            var win = new Window
            {
                Title = title,
                Width = 360,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (Brush)FindResource("BgPrimary"),
                FontFamily = FontFamily
            };

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var input = new TextBox
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                Foreground = (Brush)FindResource("TextPrimary"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                Padding = new Thickness(6),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 14)
            };
            panel.Children.Add(input);

            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okBtn = new Button { Content = Loc.T("Common.Ok"), Style = (Style)FindResource("ToolbarButtonStyle"), IsDefault = true };
            var cancelBtn = new Button { Content = Loc.T("Common.Cancel"), Style = (Style)FindResource("ToolbarButtonStyle"), IsCancel = true, Margin = new Thickness(6, 0, 0, 0) };

            string? result = null;
            okBtn.Click += (_, _) => { result = input.Text; win.DialogResult = true; };
            cancelBtn.Click += (_, _) => { win.DialogResult = false; };

            buttonRow.Children.Add(okBtn);
            buttonRow.Children.Add(cancelBtn);
            panel.Children.Add(buttonRow);

            win.Content = panel;
            win.Loaded += (_, _) => input.Focus();

            return win.ShowDialog() == true ? result : null;
        }

        // Eigener dunkel gestylter Bestätigungsdialog im AEGIS-Look statt des Windows-Standard-MessageBox
        private bool ShowConfirmDialog(string title, string message, string confirmText, string? cancelText = null)
        {
            cancelText ??= Loc.T("Common.Cancel");

            var win = new Window
            {
                Title = title,
                Width = 380,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (Brush)FindResource("BgPrimary"),
                FontFamily = FontFamily
            };

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });
            panel.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });

            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = new Button { Content = cancelText, Style = (Style)FindResource("ToolbarButtonStyle"), IsCancel = true };
            var confirmBtn = new Button
            {
                Content = confirmText,
                Style = (Style)FindResource("ToolbarButtonStyle"),
                Foreground = Brushes.IndianRed,
                Margin = new Thickness(6, 0, 0, 0),
                IsDefault = true
            };

            confirmBtn.Click += (_, _) => win.DialogResult = true;
            cancelBtn.Click += (_, _) => win.DialogResult = false;

            buttonRow.Children.Add(cancelBtn);
            buttonRow.Children.Add(confirmBtn);
            panel.Children.Add(buttonRow);

            win.Content = panel;

            return win.ShowDialog() == true;
        }

        // ===================== BACKUPS TAB =====================

        private void LoadBackupsTab()
        {
            ContentArea.Children.Clear();

            if (!_isRunningFromUsbStick)
            {
                ShowUsbNotDetected();
                return;
            }

            Directory.CreateDirectory(_backupsRoot);
            _currentBackupFolder = null;

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Backups.Header"),
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 12)
            });

            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Backups.DeviceNameLabel"),
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });

            _deviceNameBox = new TextBox
            {
                Text = Environment.MachineName,
                Background = (Brush)FindResource("BgSurfaceAlt"),
                Foreground = (Brush)FindResource("TextPrimary"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12,
                Width = 280,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 20)
            };
            _deviceNameBox.TextChanged += (_, _) => _currentBackupFolder = null;
            panel.Children.Add(_deviceNameBox);

            panel.Children.Add(BuildBackupsOverviewCard());
            panel.Children.Add(BuildDriverBackupCard());
            panel.Children.Add(BuildUserDataBackupCard());
            panel.Children.Add(BuildNetworkBackupCard());

            scroll.Content = panel;
            ContentArea.Children.Add(scroll);

            RefreshBackupsOverview();
        }

        // ----- Übersicht vorhandener Backups -----

        private Border BuildBackupsOverviewCard()
        {
            var card = CreateSectionCard(
                Loc.T("Backups.OverviewTitle"),
                Loc.T("Backups.OverviewDescription"),
                out var body);

            _backupsOverviewList = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ItemContainerStyle = (Style)FindResource("FileItemStyle"),
                MaxHeight = 220
            };
            body.Children.Add(_backupsOverviewList);

            var deleteBtn = new Button
            {
                Content = Loc.T("Backups.DeleteSelected"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0)
            };
            deleteBtn.Click += DeleteBackupButton_Click;
            body.Children.Add(deleteBtn);

            return card;
        }

        private void RefreshBackupsOverview()
        {
            _backupsOverviewList.Items.Clear();

            try
            {
                foreach (var dir in Directory.GetDirectories(_backupsRoot).OrderByDescending(Directory.GetCreationTime))
                    _backupsOverviewList.Items.Add(new BackupOverviewEntry(dir));

                if (_backupsOverviewList.Items.Count == 0)
                {
                    _backupsOverviewList.Items.Add(Loc.T("Backups.NoneYet"));
                    _backupsOverviewList.IsEnabled = false;
                }
                else
                {
                    _backupsOverviewList.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Backups.ErrorReading"), ex.Message);
            }
        }

        private void DeleteBackupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_backupsOverviewList.SelectedItem is not BackupOverviewEntry entry)
                return;

            var confirmed = ShowConfirmDialog(
                Loc.T("Backups.DeleteConfirmTitle"),
                string.Format(Loc.T("Backups.DeleteConfirmMessage"), entry.Name, entry.ContentsSummary),
                Loc.T("Common.ConfirmDeletion"));

            if (!confirmed)
                return;

            try
            {
                Directory.Delete(entry.FolderPath, recursive: true);

                if (_currentBackupFolder != null && string.Equals(_currentBackupFolder, entry.FolderPath, StringComparison.OrdinalIgnoreCase))
                    _currentBackupFolder = null;

                RefreshBackupsOverview();
                StatusLeft.Text = string.Format(Loc.T("Backups.Deleted"), entry.Name);
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(Loc.T("Backups.ErrorDeleting"), ex.Message);
            }
        }

        // Baut eine einheitliche Karte (Titel + Beschreibung + Inhalt) für einen Backup-Abschnitt
        private Border CreateSectionCard(string title, string description, out StackPanel body)
        {
            var card = new Border
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 16)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            stack.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            body = new StackPanel();
            stack.Children.Add(body);

            card.Child = stack;
            return card;
        }

        // Unbestimmte Fortschrittsanzeige (läuft/läuft nicht), standardmäßig ausgeblendet
        private ProgressBar CreateProgressBar()
        {
            return new ProgressBar
            {
                IsIndeterminate = true,
                Height = 4,
                Background = (Brush)FindResource("BgSurface"),
                Foreground = (Brush)FindResource("AccentBlue"),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };
        }

        // Legt bei Bedarf einen gemeinsamen Zielordner für alle Backup-Arten dieser Sitzung an: Backups\<Gerät>_<Zeitstempel>\
        private string GetOrCreateBackupFolder()
        {
            if (_currentBackupFolder != null && Directory.Exists(_currentBackupFolder))
                return _currentBackupFolder;

            var deviceName = string.IsNullOrWhiteSpace(_deviceNameBox.Text) ? Environment.MachineName : _deviceNameBox.Text.Trim();
            var invalidChars = Path.GetInvalidFileNameChars();
            var safeName = new string(deviceName.Where(c => !invalidChars.Contains(c)).ToArray());

            _currentBackupFolder = Path.Combine(_backupsRoot, $"{safeName}_{DateTime.Now:yyyy-MM-dd_HHmm}");
            Directory.CreateDirectory(_currentBackupFolder);
            return _currentBackupFolder;
        }

        // ----- Treiber-Backup -----

        private Border BuildDriverBackupCard()
        {
            var card = CreateSectionCard(
                Loc.T("Backups.DriverCardTitle"),
                Loc.T("Backups.DriverCardDescription"),
                out var body);

            _driverBackupButton = new Button { Content = Loc.T("Backups.DriverButton"), Style = (Style)FindResource("ToolbarButtonStyle"), HorizontalAlignment = HorizontalAlignment.Left };
            _driverBackupButton.Click += DriverBackupButton_Click;
            body.Children.Add(_driverBackupButton);

            _driverBackupStatus = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_driverBackupStatus);

            _driverProgressBar = CreateProgressBar();
            body.Children.Add(_driverProgressBar);

            return card;
        }

        private async void DriverBackupButton_Click(object sender, RoutedEventArgs e)
        {
            var folder = GetOrCreateBackupFolder();
            var driversPath = Path.Combine(folder, "Drivers");
            Directory.CreateDirectory(driversPath);

            _driverBackupButton.IsEnabled = false;
            _driverProgressBar.Visibility = Visibility.Visible;
            _driverBackupStatus.Text = Loc.T("Backups.DriverExporting");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dism.exe",
                    Arguments = $"/online /export-driver /destination:\"{driversPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Normal
                };

                using var process = Process.Start(psi);
                if (process != null)
                    await process.WaitForExitAsync();

                var count = Directory.Exists(driversPath) ? Directory.GetDirectories(driversPath).Length : 0;
                _driverBackupStatus.Text = string.Format(Loc.T("Backups.DriverDone"), count, Path.GetFileName(folder));
            }
            catch (System.ComponentModel.Win32Exception)
            {
                _driverBackupStatus.Text = Loc.T("Backups.DriverCancelled");
            }
            catch (Exception ex)
            {
                _driverBackupStatus.Text = string.Format(Loc.T("Backups.DriverError"), ex.Message);
            }
            finally
            {
                _driverProgressBar.Visibility = Visibility.Collapsed;
                _driverBackupButton.IsEnabled = true;
                RefreshBackupsOverview();
            }
        }

        // ----- Nutzerdaten-Backup -----

        private Border BuildUserDataBackupCard()
        {
            var card = CreateSectionCard(
                Loc.T("Backups.UserDataCardTitle"),
                Loc.T("Backups.UserDataCardDescription"),
                out var body);

            _userDataFolderChecks.Clear();
            var checksPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var label in UserDataFolderLabels)
            {
                var cb = new CheckBox
                {
                    Content = Loc.FolderDisplayName(label),
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 18, 6)
                };
                _userDataFolderChecks[label] = cb;
                checksPanel.Children.Add(cb);
            }
            body.Children.Add(checksPanel);

            _userDataBackupButton = new Button { Content = Loc.T("Backups.UserDataButton"), Style = (Style)FindResource("ToolbarButtonStyle"), HorizontalAlignment = HorizontalAlignment.Left };
            _userDataBackupButton.Click += UserDataBackupButton_Click;
            body.Children.Add(_userDataBackupButton);

            _userDataBackupStatus = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_userDataBackupStatus);

            _userDataProgressBar = CreateProgressBar();
            body.Children.Add(_userDataProgressBar);

            return card;
        }

        private static string ResolveUserDataFolderPath(string label) => label switch
        {
            "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Dokumente" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Bilder" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "Musik" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "Downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            _ => throw new ArgumentException($"Unbekannter Ordner: {label}")
        };

        private async void UserDataBackupButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = _userDataFolderChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
            if (selected.Count == 0)
            {
                _userDataBackupStatus.Text = Loc.T("Backups.UserDataSelectAtLeastOne");
                return;
            }

            var folder = GetOrCreateBackupFolder();
            var userDataRoot = Path.Combine(folder, "UserData");
            IProgress<string> progress = new Progress<string>(msg => _userDataBackupStatus.Text = msg);

            _userDataBackupButton.IsEnabled = false;
            _userDataProgressBar.Visibility = Visibility.Visible;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var label in selected)
                    {
                        var sourcePath = ResolveUserDataFolderPath(label);
                        if (!Directory.Exists(sourcePath))
                            continue;

                        var destPath = Path.Combine(userDataRoot, label);
                        var count = 0;

                        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                        {
                            var relative = Path.GetRelativePath(sourcePath, file);
                            var destFile = Path.Combine(destPath, relative);

                            try
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                                File.Copy(file, destFile, overwrite: true);
                            }
                            catch
                            {
                                // einzelne gesperrte/unzugängliche Datei überspringen, restliches Backup fortsetzen
                            }

                            count++;
                            progress.Report(string.Format(Loc.T("Backups.UserDataCopying"), Loc.FolderDisplayName(label), count, Path.GetFileName(file)));
                        }
                    }
                });

                _userDataBackupStatus.Text = string.Format(Loc.T("Backups.UserDataDone"), string.Join(", ", selected.Select(Loc.FolderDisplayName)));
            }
            catch (Exception ex)
            {
                _userDataBackupStatus.Text = string.Format(Loc.T("Backups.UserDataError"), ex.Message);
            }
            finally
            {
                _userDataProgressBar.Visibility = Visibility.Collapsed;
                _userDataBackupButton.IsEnabled = true;
                RefreshBackupsOverview();
            }
        }

        // ----- Netzwerkeinstellungen (WLAN) -----

        private Border BuildNetworkBackupCard()
        {
            var card = CreateSectionCard(
                Loc.T("Backups.NetworkCardTitle"),
                Loc.T("Backups.NetworkCardDescription"),
                out var body);

            _networkBackupButton = new Button { Content = Loc.T("Backups.NetworkButton"), Style = (Style)FindResource("ToolbarButtonStyle"), HorizontalAlignment = HorizontalAlignment.Left };
            _networkBackupButton.Click += NetworkBackupButton_Click;
            body.Children.Add(_networkBackupButton);

            _networkBackupStatus = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_networkBackupStatus);

            _networkProgressBar = CreateProgressBar();
            body.Children.Add(_networkProgressBar);

            return card;
        }

        private async void NetworkBackupButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_vaultService.IsUnlocked)
            {
                _networkBackupStatus.Text = Loc.T("Backups.NetworkVaultLocked");
                return;
            }

            var folder = GetOrCreateBackupFolder();
            var deviceLabel = Path.GetFileName(folder);
            var tempExportDir = Path.Combine(Path.GetTempPath(), "AEGIS-NetBackup-" + Guid.NewGuid().ToString("N"));
            var zipPath = Path.Combine(Path.GetTempPath(), $"WLAN-Profile-{Guid.NewGuid():N}.zip");

            _networkBackupButton.IsEnabled = false;
            _networkProgressBar.Visibility = Visibility.Visible;
            _networkBackupStatus.Text = Loc.T("Backups.NetworkExporting");

            try
            {
                Directory.CreateDirectory(tempExportDir);

                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"wlan export profile key=clear folder=\"{tempExportDir}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var process = Process.Start(psi))
                {
                    if (process != null)
                        await process.WaitForExitAsync();
                }

                var exportedFiles = Directory.GetFiles(tempExportDir, "*.xml");
                if (exportedFiles.Length == 0)
                {
                    _networkBackupStatus.Text = Loc.T("Backups.NetworkNoneFound");
                    return;
                }

                if (File.Exists(zipPath))
                    File.Delete(zipPath);
                ZipFile.CreateFromDirectory(tempExportDir, zipPath);

                var vaultEntryName = $"Backups/{deviceLabel}/WLAN-Profile.zip";
                _vaultService.AddFile(zipPath, vaultEntryName);

                _networkBackupStatus.Text = string.Format(Loc.T("Backups.NetworkDone"), exportedFiles.Length, vaultEntryName);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                _networkBackupStatus.Text = Loc.T("Backups.NetworkCancelled");
            }
            catch (Exception ex)
            {
                _networkBackupStatus.Text = string.Format(Loc.T("Backups.NetworkError"), ex.Message);
            }
            finally
            {
                try { if (Directory.Exists(tempExportDir)) Directory.Delete(tempExportDir, recursive: true); } catch { }
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }

                _networkProgressBar.Visibility = Visibility.Collapsed;
                _networkBackupButton.IsEnabled = true;
                RefreshBackupsOverview();
            }
        }
    }

    // Hilfsklasse für Datei-/Ordnereinträge in der Liste
    public class FileEntry
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }

        public override string ToString() => IsDirectory ? $"📁 {Name}" : $"📄 {Name}";
    }

    // Ein Eintrag in der Backups-Übersicht: ein Ordner Backups\<Gerät>_<Zeitstempel>\
    public sealed class BackupOverviewEntry
    {
        public string FolderPath { get; }
        public string Name { get; }
        public DateTime Created { get; }
        public string ContentsSummary { get; }

        public BackupOverviewEntry(string folderPath)
        {
            FolderPath = folderPath;
            Name = Path.GetFileName(folderPath);
            Created = Directory.GetCreationTime(folderPath);

            var parts = new List<string>();

            var driversPath = Path.Combine(folderPath, "Drivers");
            if (Directory.Exists(driversPath) && Directory.GetFileSystemEntries(driversPath).Length > 0)
                parts.Add(Loc.T("BackupEntry.Drivers"));

            var userDataPath = Path.Combine(folderPath, "UserData");
            if (Directory.Exists(userDataPath))
            {
                var subfolders = Directory.GetDirectories(userDataPath).Select(d => Loc.FolderDisplayName(Path.GetFileName(d)!)).ToArray();
                if (subfolders.Length > 0)
                    parts.Add(string.Format(Loc.T("BackupEntry.UserData"), string.Join(", ", subfolders)));
            }

            ContentsSummary = parts.Count > 0 ? string.Join(" · ", parts) : Loc.T("BackupEntry.Empty");
        }

        public override string ToString() => $"🗄 {Name}  —  {FormatAge(Created)}  —  {ContentsSummary}";

        private static string FormatAge(DateTime timestamp)
        {
            var span = DateTime.Now - timestamp;
            if (span.TotalMinutes < 1) return Loc.T("BackupEntry.JustNow");
            if (span.TotalMinutes < 60) return string.Format(Loc.T("BackupEntry.MinutesAgo"), (int)span.TotalMinutes);
            if (span.TotalHours < 24) return string.Format(Loc.T("BackupEntry.HoursAgo"), (int)span.TotalHours);
            if (span.TotalDays < 30) return string.Format(Loc.T("BackupEntry.DaysAgo"), (int)span.TotalDays);
            return timestamp.ToString("dd.MM.yyyy");
        }
    }
}
