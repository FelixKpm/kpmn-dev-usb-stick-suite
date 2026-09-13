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
            TabSuiteBuilder.Content = Loc.T("Tab.SuiteBuilder");

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
            else if (TabSuiteBuilder.IsChecked == true) LoadSuiteBuilderTab();

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
                case "TabSuiteBuilder":
                    LoadSuiteBuilderTab();
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

        // ===================== SUITE BUILDER TAB =====================

        // Repos der Kpmn-Suite, die der Builder aktuell bauen kann.
        // MABS fehlt bewusst: dieser Stick wird mit Ventoy formatiert und ist ein eigener, riskanterer Schritt.
        private static readonly string[] SuiteBuilderRepos = { "AVAS", "DART" };

        private readonly string _suiteBuilderTempDir = Path.Combine(Path.GetTempPath(), "AEGIS-SuiteBuilder");
        private GiteaSettings _giteaSettings = new();
        private readonly List<SuiteBuildCard> _suiteBuildCards = new();

        // Zählt Tab-Neuaufbauten mit, damit späte Antworten alter Release-Abfragen keine neue UI mehr überschreiben
        private int _suiteBuilderGeneration;

        // Ein Ziel-Laufwerk im "Ziel-Laufwerk"-Dropdown
        private sealed class SuiteDriveOption
        {
            public string Root { get; }

            // Echte Datenträgerbezeichnung (kann leer sein) – Grundlage für Update- vs. Neubau-Erkennung
            public string RawLabel { get; }

            // Anzeigename für Dropdown und Dialoge ("ohne Bezeichnung", wenn RawLabel leer ist)
            public string VolumeLabel { get; }

            public SuiteDriveOption(DriveInfo drive)
            {
                Root = drive.RootDirectory.FullName;
                RawLabel = ReadVolumeLabel(Root);
                VolumeLabel = RawLabel.Length == 0 ? Loc.T("SuiteBuilder.NoVolumeLabel") : RawLabel;
            }

            public override string ToString() => $"{Root} ({VolumeLabel})";
        }

        // Liest die aktuelle Datenträgerbezeichnung frisch vom Laufwerk; "" wenn nicht lesbar/leer
        private static string ReadVolumeLabel(string driveRoot)
        {
            try
            {
                var drive = new DriveInfo(driveRoot);
                if (drive.IsReady)
                    return (drive.VolumeLabel ?? "").Trim();
            }
            catch { }
            return "";
        }

        // Steuerelemente einer Repo-Karte; wird bei jedem Tab-Aufbau neu erzeugt
        private sealed class SuiteBuildCard
        {
            public string Repo { get; init; } = "";
            public TextBlock VersionText { get; init; } = null!;
            public ComboBox DriveBox { get; init; } = null!;
            public Button BuildButton { get; init; } = null!;
            public TextBlock StatusText { get; init; } = null!;
            public ProgressBar Progress { get; init; } = null!;
            public GiteaRelease? Release { get; set; }
        }

        private void LoadSuiteBuilderTab()
        {
            ContentArea.Children.Clear();
            _suiteBuildCards.Clear();
            _suiteBuilderGeneration++;

            _giteaSettings = GiteaSettings.Load();

            if (!_giteaSettings.IsConfigured)
                ShowGiteaSetupPrompt();
            else
                ShowSuiteBuilderUi();
        }

        // ----- Gitea-Einrichtung -----

        private void ShowGiteaSetupPrompt()
        {
            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 360
            };

            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("SuiteBuilder.SetupTitle"),
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            });

            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("SuiteBuilder.SetupDescription"),
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            });

            var urlBox = CreateSetupTextBox(panel, Loc.T("SuiteBuilder.SetupServerLabel"), _giteaSettings.BaseUrl);
            var orgBox = CreateSetupTextBox(panel, Loc.T("SuiteBuilder.SetupOrgLabel"), _giteaSettings.Org);
            var tokenBox = CreateSetupTextBox(panel, Loc.T("SuiteBuilder.SetupTokenLabel"), _giteaSettings.Token);

            var errorText = new TextBlock
            {
                Foreground = Brushes.IndianRed,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 8),
                Visibility = Visibility.Collapsed
            };
            panel.Children.Add(errorText);

            void ShowError(string message)
            {
                errorText.Text = message;
                errorText.Visibility = Visibility.Visible;
            }

            var saveButton = new Button
            {
                Content = Loc.T("SuiteBuilder.SetupSave"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            saveButton.Click += (_, _) =>
            {
                var url = urlBox.Text.Trim();
                var org = orgBox.Text.Trim();
                var token = tokenBox.Text.Trim();

                if (url.Length == 0 || org.Length == 0 || token.Length == 0)
                {
                    ShowError(Loc.T("SuiteBuilder.SetupErrorIncomplete"));
                    return;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                {
                    ShowError(Loc.T("SuiteBuilder.SetupErrorInvalidUrl"));
                    return;
                }

                var settings = new GiteaSettings { BaseUrl = url.TrimEnd('/'), Org = org, Token = token };
                if (!settings.Save())
                {
                    ShowError(Loc.T("SuiteBuilder.SetupErrorSaving"));
                    return;
                }

                _giteaSettings = settings;
                LoadSuiteBuilderTab();
            };

            panel.Children.Add(saveButton);

            ContentArea.Children.Add(panel);
        }

        private TextBox CreateSetupTextBox(StackPanel parent, string label, string value)
        {
            parent.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var box = new TextBox
            {
                Text = value,
                Background = (Brush)FindResource("BgSurfaceAlt"),
                Foreground = (Brush)FindResource("TextPrimary"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 10)
            };
            parent.Children.Add(box);
            return box;
        }

        // ----- Builder-Oberfläche -----

        private void ShowSuiteBuilderUi()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("SuiteBuilder.Header"),
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 8)
            });

            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("SuiteBuilder.Description"),
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };

            var refreshButton = new Button { Content = Loc.T("SuiteBuilder.RefreshButton"), Style = (Style)FindResource("ToolbarButtonStyle") };
            refreshButton.Click += (_, _) => LoadSuiteBuilderTab();
            toolbar.Children.Add(refreshButton);

            var settingsButton = new Button
            {
                Content = Loc.T("SuiteBuilder.SettingsButton"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                Margin = new Thickness(8, 0, 0, 0)
            };
            settingsButton.Click += (_, _) =>
            {
                ContentArea.Children.Clear();
                _suiteBuildCards.Clear();
                _suiteBuilderGeneration++;
                ShowGiteaSetupPrompt();
            };
            toolbar.Children.Add(settingsButton);

            panel.Children.Add(toolbar);

            foreach (var repo in SuiteBuilderRepos)
                panel.Children.Add(BuildSuiteRepoCard(repo));

            // MABS läuft bewusst getrennt: kein Release zum Entpacken, sondern eine Ventoy-Installation
            panel.Children.Add(BuildMabsCard());

            scroll.Content = panel;
            ContentArea.Children.Add(scroll);

            _ = RefreshSuiteReleasesAsync(_suiteBuilderGeneration);
        }

        private Border BuildSuiteRepoCard(string repo)
        {
            var card = CreateSectionCard(
                string.Format(Loc.T("SuiteBuilder.CardTitle"), repo),
                string.Format(Loc.T("SuiteBuilder.CardDescription"), repo),
                out var body);

            var versionText = new TextBlock
            {
                Text = Loc.T("SuiteBuilder.VersionLoading"),
                Foreground = (Brush)FindResource("AccentBlue"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            body.Children.Add(versionText);

            body.Children.Add(new TextBlock
            {
                Text = Loc.T("SuiteBuilder.TargetDriveLabel"),
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var driveBox = new ComboBox
            {
                Width = 280,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
                Style = (Style)FindResource("DarkComboBoxStyle")
            };
            PopulateRemovableDrives(driveBox);

            // Laufwerke + Bezeichnungen beim Aufklappen neu einlesen, damit Modus und Dialogtext
            // immer auf dem aktuellen Stand des Sticks basieren
            driveBox.DropDownOpened += (_, _) => PopulateRemovableDrives(driveBox, preserveSelection: true);

            body.Children.Add(driveBox);

            var buildButton = new Button
            {
                Content = Loc.T("SuiteBuilder.BuildButton"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            body.Children.Add(buildButton);

            var statusText = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(statusText);

            var progress = CreateProgressBar();
            body.Children.Add(progress);

            var cardState = new SuiteBuildCard
            {
                Repo = repo,
                VersionText = versionText,
                DriveBox = driveBox,
                BuildButton = buildButton,
                StatusText = statusText,
                Progress = progress
            };
            _suiteBuildCards.Add(cardState);

            buildButton.Click += (_, _) => _ = BuildSuiteStickAsync(cardState);

            return card;
        }

        // ----- MABS (Ventoy) -----

        private const string MabsSuiteRepo = "MABS";

        // Eigene Karte für den MABS-Stick: hier wird nicht entpackt, sondern der gesamte Datenträger
        // mit Ventoy neu partitioniert – deshalb ohne Versionszeile und mit eigenem Build-Pfad.
        private Border BuildMabsCard()
        {
            var card = CreateSectionCard(
                Loc.T("SuiteBuilder.Mabs.CardTitle"),
                Loc.T("SuiteBuilder.Mabs.CardDescription"),
                out var body);

            body.Children.Add(new TextBlock
            {
                Text = Loc.T("SuiteBuilder.TargetDriveLabel"),
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var driveBox = new ComboBox
            {
                Width = 280,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
                Style = (Style)FindResource("DarkComboBoxStyle")
            };
            PopulateRemovableDrives(driveBox);
            driveBox.DropDownOpened += (_, _) => PopulateRemovableDrives(driveBox, preserveSelection: true);
            body.Children.Add(driveBox);

            var buildButton = new Button
            {
                Content = Loc.T("SuiteBuilder.Mabs.BuildButton"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            body.Children.Add(buildButton);

            var statusText = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(statusText);

            var progress = CreateProgressBar();
            body.Children.Add(progress);

            buildButton.Click += (_, _) => _ = BuildMabsStickAsync(driveBox, buildButton, statusText, progress);

            return card;
        }

        // Laufwerk, von dem AEGIS gerade läuft (auf dem fertigen Stick der AEGIS-Datenträger selbst).
        // Bewusst über den Ausführungspfad und nicht über das Datenträger-Label ermittelt, damit die
        // Absicherung auch dann greift, wenn der Stick anders benannt ist.
        private static string? GetRunningDriveRoot()
        {
            try
            {
                var root = Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory);
                return string.IsNullOrWhiteSpace(root) ? null : root;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSameDriveRoot(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;

            return string.Equals(
                a.TrimEnd('\\', '/'),
                b.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }

        // Füllt ein Dropdown mit den aktuell verbundenen Wechseldatenträgern
        private static void PopulateRemovableDrives(ComboBox box, bool preserveSelection = false)
        {
            var previousRoot = preserveSelection && box.SelectedItem is SuiteDriveOption previous ? previous.Root : null;

            box.Items.Clear();

            // Das Laufwerk, von dem AEGIS gerade ausgeführt wird, darf nie als Ziel auftauchen:
            // ein Build/Format darauf würde die laufende Anwendung zerstören.
            var runningRoot = GetRunningDriveRoot();

            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                    .Where(d => !IsSameDriveRoot(d.RootDirectory.FullName, runningRoot))
                    .ToArray();
            }
            catch
            {
                drives = Array.Empty<DriveInfo>();
            }

            foreach (var drive in drives)
                box.Items.Add(new SuiteDriveOption(drive));

            if (box.Items.Count == 0)
            {
                box.Items.Add(Loc.T("SuiteBuilder.NoDrives"));
                box.IsEnabled = false;
            }
            else
            {
                box.IsEnabled = true;
            }

            var restoredIndex = -1;
            if (previousRoot != null)
            {
                for (var i = 0; i < box.Items.Count; i++)
                {
                    if (box.Items[i] is SuiteDriveOption option &&
                        string.Equals(option.Root, previousRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        restoredIndex = i;
                        break;
                    }
                }
            }

            box.SelectedIndex = restoredIndex >= 0 ? restoredIndex : 0;
        }

        // Holt für alle Karten parallel das jeweils neueste Release vom Gitea-Server
        private async Task RefreshSuiteReleasesAsync(int generation)
        {
            var settings = _giteaSettings;

            foreach (var card in _suiteBuildCards.ToList())
            {
                var repo = card.Repo;

                try
                {
                    var release = await GiteaService.GetLatestReleaseAsync(settings, repo);

                    // Tab wurde zwischenzeitlich neu aufgebaut -> Ergebnis verwerfen
                    if (generation != _suiteBuilderGeneration)
                        return;

                    card.Release = release;

                    var zip = release.ZipAsset;
                    if (zip == null)
                    {
                        card.VersionText.Text = string.Format(Loc.T("SuiteBuilder.VersionNoZip"), release.TagName);
                        card.BuildButton.IsEnabled = false;
                    }
                    else
                    {
                        card.VersionText.Text = string.Format(
                            Loc.T("SuiteBuilder.VersionAvailable"), release.TagName, zip.Name, FormatBytes(zip.Size));
                    }
                }
                catch (Exception ex)
                {
                    if (generation != _suiteBuilderGeneration)
                        return;

                    card.Release = null;
                    card.VersionText.Text = string.Format(Loc.T("SuiteBuilder.VersionError"), ex.Message);
                }
            }
        }

        // ----- Stick bauen -----

        private async Task BuildSuiteStickAsync(SuiteBuildCard card)
        {
            if (card.Release?.ZipAsset is not GiteaAsset asset)
            {
                card.StatusText.Text = Loc.T("SuiteBuilder.NoReleaseYet");
                return;
            }

            if (card.DriveBox.SelectedItem is not SuiteDriveOption drive)
            {
                card.StatusText.Text = Loc.T("SuiteBuilder.SelectDrive");
                return;
            }

            var version = card.Release.TagName;

            // Modus live entscheiden: Label wird direkt vor dem Dialog erneut gelesen,
            // falls sich der Stick seit dem Befüllen des Dropdowns geändert hat.
            var currentLabel = ReadVolumeLabel(drive.Root);
            var isUpdate = string.Equals(currentLabel, card.Repo, StringComparison.OrdinalIgnoreCase);
            var currentLabelDisplay = currentLabel.Length == 0 ? Loc.T("SuiteBuilder.NoVolumeLabel") : currentLabel;

            bool confirmed;
            if (isUpdate)
            {
                // Update: nur die Stick-Software erneuern, restliche Dateien bleiben liegen
                confirmed = ShowConfirmDialog(
                    string.Format(Loc.T("SuiteBuilder.ConfirmUpdateTitle"), card.Repo),
                    string.Format(Loc.T("SuiteBuilder.ConfirmUpdateMessage"), card.Repo, drive.Root, version),
                    Loc.T("SuiteBuilder.ConfirmUpdateButton"));
            }
            else
            {
                // Neubau: das Laufwerk wird komplett geleert – entsprechend deutlich nachfragen
                confirmed = ShowConfirmDialog(
                    string.Format(Loc.T("SuiteBuilder.ConfirmNewTitle"), card.Repo),
                    string.Format(Loc.T("SuiteBuilder.ConfirmNewMessage"), drive.Root, currentLabelDisplay, card.Repo, version),
                    string.Format(Loc.T("SuiteBuilder.ConfirmNewButton"), card.Repo));
            }

            if (!confirmed)
                return;

            var tempFile = Path.Combine(_suiteBuilderTempDir, $"{card.Repo}-{DateTime.Now:yyyyMMddHHmmss}.zip");

            card.BuildButton.IsEnabled = false;
            card.DriveBox.IsEnabled = false;
            card.Progress.IsIndeterminate = false;
            card.Progress.Minimum = 0;
            card.Progress.Maximum = 100;
            card.Progress.Value = 0;
            card.Progress.Visibility = Visibility.Visible;

            StatusLeft.Text = string.Format(Loc.T("SuiteBuilder.StatusBuilding"), card.Repo, drive.Root);

            IProgress<(long BytesRead, long TotalBytes)> downloadProgress = new Progress<(long BytesRead, long TotalBytes)>(p =>
            {
                if (p.TotalBytes > 0)
                {
                    var percent = (int)(p.BytesRead * 100 / p.TotalBytes);
                    card.Progress.Value = Math.Min(100, percent);
                    card.StatusText.Text = string.Format(
                        Loc.T("SuiteBuilder.Downloading"), card.Repo, version, percent, FormatBytes(p.BytesRead), FormatBytes(p.TotalBytes));
                }
                else
                {
                    card.Progress.IsIndeterminate = true;
                    card.StatusText.Text = string.Format(
                        Loc.T("SuiteBuilder.DownloadingUnknownSize"), card.Repo, version, FormatBytes(p.BytesRead));
                }
            });

            // Nach einem erfolgreichen AVAS-Build folgen die optionalen Schritte Treiber-Datenbank und Software-Pakete,
            // nach einem DART-Build stattdessen der Download der portablen Diagnose-Tools.
            // Die Dialoge werden erst nach dem finally-Block gezeigt, damit die Karte vorher wieder freigegeben ist.
            var offerDriverSetup = false;
            var offerDartToolSetup = false;

            try
            {
                card.StatusText.Text = string.Format(Loc.T("SuiteBuilder.Downloading"), card.Repo, version, 0, FormatBytes(0), FormatBytes(asset.Size));
                await GiteaService.DownloadAssetAsync(_giteaSettings, asset, tempFile, downloadProgress);

                card.Progress.IsIndeterminate = true;

                if (!isUpdate)
                {
                    // Neubau: Laufwerk vor dem Entpacken komplett leeren
                    card.StatusText.Text = string.Format(Loc.T("SuiteBuilder.Wiping"), drive.Root);
                    await Task.Run(() => WipeDriveRoot(drive.Root));
                }

                card.StatusText.Text = string.Format(Loc.T("SuiteBuilder.Extracting"), drive.Root);
                await Task.Run(() => ZipFile.ExtractToDirectory(tempFile, drive.Root, overwriteFiles: true));

                card.StatusText.Text = Loc.T("SuiteBuilder.SettingLabel");
                var labelError = await Task.Run(() => TrySetVolumeLabel(drive.Root, card.Repo));

                if (labelError == null)
                {
                    card.StatusText.Text = string.Format(Loc.T("SuiteBuilder.Done"), card.Repo, version, drive.Root);
                }
                else
                {
                    card.StatusText.Text = string.Format(
                        Loc.T("SuiteBuilder.DoneWithWarning"), card.Repo, version, drive.Root,
                        string.Format(Loc.T("SuiteBuilder.LabelFailed"), labelError));
                }

                StatusLeft.Text = string.Format(Loc.T("SuiteBuilder.StatusDone"), card.Repo, version);

                // nur AVAS hat die SDI-Treiberinstallation – DART bekommt diesen Schritt nicht
                offerDriverSetup = string.Equals(card.Repo, AvasSuiteRepo, StringComparison.OrdinalIgnoreCase);

                // DART ist umgekehrt ohne seine portablen Tools nur eine leere Hülle: die Buttons zeigen
                // auf Ordner, die das Repository selbst nicht mitliefert.
                offerDartToolSetup = string.Equals(card.Repo, DartSuiteRepo, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                card.StatusText.Text = string.Format(Loc.T("SuiteBuilder.Error"), ex.Message);
                StatusLeft.Text = string.Format(Loc.T("SuiteBuilder.StatusError"), card.Repo, ex.Message);
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }

                card.Progress.Visibility = Visibility.Collapsed;
                card.Progress.IsIndeterminate = true;
                card.BuildButton.IsEnabled = true;

                // Laufwerksbezeichnung hat sich gerade geändert -> Dropdown-Einträge neu aufbauen
                // (setzt auch IsEnabled), dabei aber beim selben Laufwerk bleiben
                PopulateRemovableDrives(card.DriveBox, preserveSelection: true);
            }

            if (offerDriverSetup)
            {
                ShowDriverDatabaseSetupDialog(drive.Root);

                // zweiter, davon unabhängiger Schritt: die Installer für AVAS' Ordner "Files" holen
                ShowPackageDownloadSetupDialog(drive.Root);
            }
            else if (offerDartToolSetup)
            {
                ShowDartToolsSetupDialog(drive.Root);
            }
        }

        // ----- MABS-Stick bauen (Ventoy-Installation + Kpmn-Theme) -----

        private async Task BuildMabsStickAsync(ComboBox driveBox, Button buildButton, TextBlock statusText, ProgressBar progress)
        {
            if (driveBox.SelectedItem is not SuiteDriveOption drive)
            {
                statusText.Text = Loc.T("SuiteBuilder.SelectDrive");
                return;
            }

            // Label und Größe unmittelbar vor dem Dialog frisch lesen, damit der Warntext wirklich
            // den Datenträger beschreibt, der gleich formatiert wird
            var currentLabel = ReadVolumeLabel(drive.Root);
            var currentLabelDisplay = currentLabel.Length == 0 ? Loc.T("SuiteBuilder.NoVolumeLabel") : currentLabel;

            var totalDisplay = Loc.T("SuiteBuilder.Mabs.UnknownSize");
            var freeDisplay = Loc.T("SuiteBuilder.Mabs.UnknownSize");
            try
            {
                var info = new DriveInfo(drive.Root);
                if (info.IsReady)
                {
                    totalDisplay = FormatBytes(info.TotalSize);
                    freeDisplay = FormatBytes(info.TotalFreeSpace);
                }
            }
            catch { }

            var confirmed = ShowConfirmDialog(
                Loc.T("SuiteBuilder.Mabs.ConfirmTitle"),
                string.Format(Loc.T("SuiteBuilder.Mabs.ConfirmMessage"), drive.Root, currentLabelDisplay, totalDisplay, freeDisplay),
                Loc.T("SuiteBuilder.Mabs.ConfirmButton"));

            if (!confirmed)
                return;

            var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var tempZip = Path.Combine(_suiteBuilderTempDir, $"MABS-{stamp}.zip");
            var tempExtractDir = Path.Combine(_suiteBuilderTempDir, $"MABS-{stamp}");

            driveBox.IsEnabled = false;
            buildButton.IsEnabled = false;
            progress.Minimum = 0;
            progress.Maximum = 100;
            progress.Value = 0;
            progress.IsIndeterminate = true;
            progress.Visibility = Visibility.Visible;

            StatusLeft.Text = string.Format(Loc.T("SuiteBuilder.Mabs.StatusBuilding"), drive.Root);

            IProgress<(long BytesRead, long TotalBytes)> ventoyDownloadProgress = new Progress<(long BytesRead, long TotalBytes)>(p =>
            {
                if (p.TotalBytes > 0)
                {
                    var percent = (int)(p.BytesRead * 100 / p.TotalBytes);
                    progress.IsIndeterminate = false;
                    progress.Value = Math.Min(100, percent);
                    statusText.Text = string.Format(
                        Loc.T("SuiteBuilder.Mabs.StageDownloadingVentoy"), percent, FormatBytes(p.BytesRead), FormatBytes(p.TotalBytes));
                }
                else
                {
                    progress.IsIndeterminate = true;
                    statusText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.StageDownloadingVentoyUnknownSize"), FormatBytes(p.BytesRead));
                }
            });

            IProgress<int> installProgress = new Progress<int>(p =>
            {
                // sobald echte Prozentwerte aus Ventoys cli_percent.txt kommen, aus dem Indeterminate-Modus raus
                progress.IsIndeterminate = false;
                progress.Value = Math.Clamp(p, 0, 100);
                statusText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.StageInstallingPercent"), drive.Root, p);
            });

            IProgress<(long BytesRead, long TotalBytes)> themeDownloadProgress = new Progress<(long BytesRead, long TotalBytes)>(p =>
            {
                if (p.TotalBytes > 0)
                {
                    var percent = (int)(p.BytesRead * 100 / p.TotalBytes);
                    progress.IsIndeterminate = false;
                    progress.Value = Math.Min(100, percent);
                    statusText.Text = string.Format(
                        Loc.T("SuiteBuilder.Mabs.StageDownloadingTheme"), percent, FormatBytes(p.BytesRead), FormatBytes(p.TotalBytes));
                }
                else
                {
                    progress.IsIndeterminate = true;
                    statusText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.StageDownloadingThemeUnknownSize"), FormatBytes(p.BytesRead));
                }
            });

            // Wurzel der Ventoy-Datenpartition, sobald der Build durch ist – erst dann darf der
            // optionale ISO-Dialog angeboten werden (vorher weiß niemand, wohin die Images sollen).
            string? isoTarget = null;

            try
            {
                statusText.Text = Loc.T("SuiteBuilder.Mabs.StageCheckingVentoy");
                var ventoyExe = await VentoyService.EnsureVentoyAsync(ventoyDownloadProgress);

                progress.IsIndeterminate = true;
                progress.Value = 0;
                statusText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.StageInstalling"), drive.Root);

                var install = await VentoyService.RunInstallAsync(
                    ventoyExe, drive.Root, installProgress, System.Threading.CancellationToken.None);

                if (!install.Success)
                {
                    statusText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.InstallFailed"), install.ErrorMessage ?? "");
                    StatusLeft.Text = string.Format(Loc.T("SuiteBuilder.Mabs.StatusError"), install.ErrorMessage ?? "");
                    return;
                }

                progress.IsIndeterminate = true;
                statusText.Text = Loc.T("SuiteBuilder.Mabs.StageLocatingPartition");

                var themeTarget = await FindVentoyDataDriveAsync(drive.Root);
                if (themeTarget == null)
                {
                    // Ventoy selbst ist durch – nur das Theme fehlt, und das ist rein kosmetisch.
                    // Die Bezeichnung trotzdem versuchen: Windows behält meist denselben Laufwerksbuchstaben,
                    // also trifft drive.Root oft auch nach dem Partitionieren noch die Datenpartition.
                    var fallbackLabelError = await Task.Run(() => TrySetVolumeLabel(drive.Root, MabsSuiteRepo));

                    statusText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.DoneNoTheme"), drive.Root) + " " + Loc.T("SuiteBuilder.Mabs.IsoHint");
                    if (fallbackLabelError != null)
                        statusText.Text += " " + string.Format(Loc.T("SuiteBuilder.Mabs.LabelFailed"), fallbackLabelError);

                    StatusLeft.Text = string.Format(Loc.T("SuiteBuilder.Mabs.StatusDone"), drive.Root);
                    return;
                }

                // Bezeichnung vor dem Theme setzen: ohne "MABS" erkennt AEGIS den Stick später nicht,
                // das Theme ist dagegen nur Kosmetik und darf die Umbenennung nicht blockieren
                statusText.Text = Loc.T("SuiteBuilder.Mabs.StageSettingLabel");
                var labelError = await Task.Run(() => TrySetVolumeLabel(themeTarget, MabsSuiteRepo));

                statusText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.StageDownloadingTheme"), 0, FormatBytes(0), FormatBytes(0));
                await GiteaService.DownloadRepoArchiveAsync(_giteaSettings, MabsSuiteRepo, tempZip, themeDownloadProgress);

                progress.IsIndeterminate = true;
                statusText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.StageDeployingTheme"), themeTarget);
                await Task.Run(() => DeployMabsTheme(tempZip, tempExtractDir, themeTarget));

                statusText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Done"), themeTarget) + " " + Loc.T("SuiteBuilder.Mabs.IsoHint");
                if (labelError != null)
                    statusText.Text += " " + string.Format(Loc.T("SuiteBuilder.Mabs.LabelFailed"), labelError);

                StatusLeft.Text = string.Format(Loc.T("SuiteBuilder.Mabs.StatusDone"), themeTarget);

                isoTarget = themeTarget;
            }
            catch (Exception ex)
            {
                statusText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Error"), ex.Message);
                StatusLeft.Text = string.Format(Loc.T("SuiteBuilder.Mabs.StatusError"), ex.Message);
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, recursive: true); } catch { }

                progress.Visibility = Visibility.Collapsed;
                progress.IsIndeterminate = true;
                progress.Value = 0;
                buildButton.IsEnabled = true;

                // Partitionierung und Bezeichnung haben sich geändert -> Dropdown neu aufbauen (setzt auch IsEnabled)
                PopulateRemovableDrives(driveBox, preserveSelection: true);
            }

            // Optionaler Folgeschritt: Betriebssystem-Images direkt auf die frische Datenpartition laden.
            // Bewusst erst nach dem finally-Block, damit der Stick-Build selbst zu diesem Zeitpunkt
            // vollständig abgeschlossen ist und der Dialog nichts davon blockieren kann.
            if (isoTarget != null)
                ShowMabsIsoSetupDialog(isoTarget);
        }

        // Ventoy gibt den Pfad der neu angelegten Datenpartition nicht zurück, deshalb diese Heuristik:
        // Windows braucht nach dem Partitionieren ein paar Sekunden, bis beide Partitionen gemountet sind
        // (daher alle 500 ms, bis zu ~10 s). Gesucht wird ein Wechseldatenträger, der nicht "VTOYEFI" heißt –
        // das ist Ventoys 32-MB-EFI-Partition mit festem Label. Bevorzugt wird derselbe Laufwerksbuchstabe
        // wie vorher (Windows behält ihn meist bei), dann ein Laufwerk mit Ventoys Standardbezeichnung
        // "Ventoy", und erst zuletzt schlicht der größte verbliebene Wechseldatenträger.
        private static async Task<string?> FindVentoyDataDriveAsync(string previousRoot)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    var candidates = DriveInfo.GetDrives()
                        .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                        .Where(d => !string.Equals(ReadVolumeLabel(d.RootDirectory.FullName), "VTOYEFI", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var sameLetter = candidates.FirstOrDefault(d =>
                        string.Equals(d.RootDirectory.FullName, previousRoot, StringComparison.OrdinalIgnoreCase));
                    if (sameLetter != null)
                        return sameLetter.RootDirectory.FullName;

                    var ventoyLabelled = candidates.FirstOrDefault(d =>
                        string.Equals(ReadVolumeLabel(d.RootDirectory.FullName), "Ventoy", StringComparison.OrdinalIgnoreCase));
                    if (ventoyLabelled != null)
                        return ventoyLabelled.RootDirectory.FullName;

                    var largest = candidates.OrderByDescending(d => d.TotalSize).FirstOrDefault();
                    if (largest != null)
                        return largest.RootDirectory.FullName;
                }
                catch
                {
                    // Laufwerk gerade nicht abfragbar (wird noch gemountet) – einfach erneut versuchen
                }

                await Task.Delay(500);
            }

            return null;
        }

        // Entpackt das MABS-Repo-Archiv und kopiert den ventoy-Ordner auf die Datenpartition.
        // Ventoy erwartet seine Konfiguration genau unter X:\ventoy\ventoy.json, deshalb landet der
        // Ordner als Ganzes im Wurzelverzeichnis und nicht sein Inhalt.
        private static void DeployMabsTheme(string archiveFile, string extractDir, string targetRoot)
        {
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, recursive: true);
            Directory.CreateDirectory(extractDir);

            ZipFile.ExtractToDirectory(archiveFile, extractDir, overwriteFiles: true);

            // Gitea packt das Archiv in einen Repo-Unterordner, deshalb rekursiv nach "ventoy" suchen
            var themeSource = Directory.EnumerateDirectories(extractDir, "ventoy", SearchOption.AllDirectories).FirstOrDefault();
            if (themeSource == null)
                throw new DirectoryNotFoundException("ventoy");

            CopyDirectoryContents(themeSource, Path.Combine(targetRoot, "ventoy"));
        }

        private static void CopyDirectoryContents(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);

            foreach (var dir in Directory.GetDirectories(sourceDir))
                CopyDirectoryContents(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
        }

        // ----- MABS: Betriebssystem-Images auf die Ventoy-Datenpartition laden -----

        // Microsofts offizielle Windows-11-Downloadseite. Bewusst nur ein Link und kein Download:
        // Microsoft erzeugt die eigentliche ISO-Adresse erst nach einer Auswahl im Browser, bindet sie
        // an die Sitzung und lässt sie nach 24 Stunden ablaufen – das ist nicht automatisierbar.
        private const string WindowsIsoDownloadUrl = "https://www.microsoft.com/software-download/windows11";

        // Folgeschritt nach einem erfolgreichen MABS-Build: Ventoy bootet jede .iso-Datei, die in der
        // Wurzel der Datenpartition liegt, ganz von allein – ein frisch gebauter Stick ist ohne Images
        // aber erst mal ein leeres Bootmenü. Ubuntu und Kali holt AEGIS auf Wunsch direkt von den
        // offiziellen Servern. Optional und jederzeit abbrechbar (Fenster schließen bricht ab).
        private void ShowMabsIsoSetupDialog(string dataDriveRoot)
        {
            var isos = MabsIsoDownloadService.AllIsos;

            var win = new Window
            {
                Title = Loc.T("SuiteBuilder.Mabs.Iso.Title"),
                Width = 640,
                MaxHeight = 720,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (Brush)FindResource("BgPrimary"),
                FontFamily = FontFamily
            };

            // Beim Schließen des Fensters einen laufenden Download abbrechen, statt ihn ins Leere weiterlaufen zu lassen
            var cts = new System.Threading.CancellationTokenSource();
            win.Closed += (_, _) => { try { cts.Cancel(); } catch { } };

            var panel = new StackPanel { Margin = new Thickness(20) };
            win.Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            };

            panel.Children.Add(CreateDialogTextBlock(Loc.T("SuiteBuilder.Mabs.Iso.Title"), "TextPrimary", 15, new Thickness(0, 0, 0, 10), bold: true));
            panel.Children.Add(CreateDialogTextBlock(Loc.T("SuiteBuilder.Mabs.Iso.Intro"), "TextMuted", 12, new Thickness(0, 0, 0, 12)));

            // Der freie Platz gehört hier direkt danebengeschrieben: zwei Images sind zusammen gut 11 GB,
            // auf einem 16-GB-Stick wird das knapp und der Fehler käme sonst erst nach Stunden Download.
            var freeDisplay = Loc.T("SuiteBuilder.Mabs.UnknownSize");
            try
            {
                var info = new DriveInfo(dataDriveRoot);
                if (info.IsReady)
                    freeDisplay = FormatBytes(info.TotalFreeSpace);
            }
            catch { }

            panel.Children.Add(CreateDialogTextBlock(
                string.Format(Loc.T("SuiteBuilder.Mabs.Iso.TargetRoot"), dataDriveRoot, freeDisplay),
                "TextSecondary", 12, new Thickness(0, 0, 0, 14)));

            var statusText = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var progress = CreateProgressBar();

            var summaryText = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };

            // Anders als bei den AVAS-Paketen und den DART-Tools bekommt hier jede Zeile ihre eigene
            // Schaltfläche: es sind nur zwei Einträge, aber jeder davon mehrere Gigabyte – "alles oder
            // nichts" wäre bei der Größe die falsche Vorgabe. Während ein Download läuft, sind beide
            // Schaltflächen gesperrt (ein Server nach dem anderen, wie in den anderen Dialogen auch).
            var listStack = new StackPanel();
            var downloadButtons = new List<Button>();

            foreach (var iso in isos)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameText = CreateDialogTextBlock(iso.Name, "TextPrimary", 12, new Thickness(0, 0, 10, 0));
                nameText.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(nameText, 0);
                row.Children.Add(nameText);

                var descriptionText = CreateDialogTextBlock(Loc.T(iso.DescriptionKey), "TextMuted", 11, new Thickness(0, 0, 10, 0));
                descriptionText.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(descriptionText, 1);
                row.Children.Add(descriptionText);

                var stateText = CreateDialogTextBlock(Loc.T("SuiteBuilder.Mabs.Iso.StatePending"), "TextMuted", 11, new Thickness(0, 0, 10, 0));
                stateText.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(stateText, 2);
                row.Children.Add(stateText);

                var downloadButton = new Button
                {
                    Content = Loc.T("SuiteBuilder.Mabs.Iso.DownloadButton"),
                    Style = (Style)FindResource("ToolbarButtonStyle"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(downloadButton, 3);
                row.Children.Add(downloadButton);

                downloadButtons.Add(downloadButton);

                var spec = iso;
                var state = stateText;
                downloadButton.Click += (_, _) => _ = DownloadMabsIsoAsync(
                    spec, dataDriveRoot, downloadButtons, progress, statusText, summaryText, state, cts.Token);

                listStack.Children.Add(row);
            }

            panel.Children.Add(new Border
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = listStack
            });

            panel.Children.Add(CreateDialogTextBlock(Loc.T("SuiteBuilder.Mabs.Iso.SourcesNote"), "TextMuted", 11, new Thickness(0, 0, 0, 4)));

            panel.Children.Add(statusText);
            panel.Children.Add(progress);
            panel.Children.Add(summaryText);

            // Zweiter, deutlich abgesetzter Block: Windows, das AEGIS nicht automatisch holen kann.
            panel.Children.Add(BuildMabsManualWindowsSection());

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            var closeButton = new Button
            {
                Content = Loc.T("SuiteBuilder.Mabs.Iso.Close"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                IsCancel = true
            };
            closeButton.Click += (_, _) => win.Close();
            footer.Children.Add(closeButton);
            panel.Children.Add(footer);

            win.ShowDialog();
        }

        // Windows lässt sich nicht automatisieren: Microsoft erzeugt die ISO-Adresse erst nach einer
        // Auswahl im Browser, bindet sie an die Sitzung und lässt sie nach 24 Stunden ablaufen. Statt
        // eines Downloads gibt es hier denselben klickbaren Link wie bei den manuellen Einträgen im
        // Treiber- und im DART-Tools-Dialog – plus den Zielordner auf dem Stick.
        private Border BuildMabsManualWindowsSection()
        {
            var stack = new StackPanel();

            stack.Children.Add(CreateDialogTextBlock(Loc.T("SuiteBuilder.Mabs.Iso.ManualHeading"), "TextPrimary", 13, new Thickness(0, 0, 0, 6), bold: true));
            stack.Children.Add(CreateDialogTextBlock(Loc.T("SuiteBuilder.Mabs.Iso.ManualHint"), "TextMuted", 11, new Thickness(0, 0, 0, 10)));
            stack.Children.Add(CreateDialogHyperlink(WindowsIsoDownloadUrl, 12, new Thickness(0, 0, 0, 0)));

            return new Border
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 22, 0, 0),
                Child = stack
            };
        }

        // Lädt genau ein Image in die Wurzel der Datenpartition. Ein Fehlschlag bleibt folgenlos:
        // der MABS-Stick ist an dieser Stelle längst fertig, die Images sind reine Zugabe.
        private async Task DownloadMabsIsoAsync(
            MabsIsoSpec spec,
            string dataDriveRoot,
            IReadOnlyList<Button> downloadButtons,
            ProgressBar progress,
            TextBlock statusText,
            TextBlock summaryText,
            TextBlock state,
            System.Threading.CancellationToken ct)
        {
            foreach (var button in downloadButtons)
                button.IsEnabled = false;

            progress.IsIndeterminate = false;
            progress.Minimum = 0;
            progress.Maximum = 100;
            progress.Value = 0;
            progress.Visibility = Visibility.Visible;

            summaryText.Text = "";
            summaryText.Foreground = (Brush)FindResource("TextSecondary");

            state.Foreground = (Brush)FindResource("TextSecondary");
            state.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Iso.StateDownloading"), 0);
            statusText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Iso.Progress"), spec.Name, 0, FormatBytes(0), FormatBytes(0));

            StatusLeft.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Iso.StatusDownloading"), spec.Name, dataDriveRoot);

            IProgress<(long BytesRead, long TotalBytes)> isoProgress = new Progress<(long BytesRead, long TotalBytes)>(p =>
            {
                if (p.TotalBytes > 0)
                {
                    var percent = (int)Math.Min(100, p.BytesRead * 100 / p.TotalBytes);
                    state.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Iso.StateDownloading"), percent);
                    statusText.Text = string.Format(
                        Loc.T("SuiteBuilder.Mabs.Iso.Progress"), spec.Name, percent, FormatBytes(p.BytesRead), FormatBytes(p.TotalBytes));

                    progress.IsIndeterminate = false;
                    progress.Value = percent;
                }
                else
                {
                    // manche Spiegelserver liefern keine Content-Length (gechunkte Antwort)
                    state.Text = FormatBytes(p.BytesRead);
                    statusText.Text = string.Format(
                        Loc.T("SuiteBuilder.Mabs.Iso.ProgressUnknownSize"), spec.Name, FormatBytes(p.BytesRead));
                    progress.IsIndeterminate = true;
                }
            });

            try
            {
                var result = await MabsIsoDownloadService.DownloadIsoAsync(spec, dataDriveRoot, isoProgress, ct);

                if (result.Success && result.Skipped)
                {
                    state.Text = Loc.T("SuiteBuilder.Mabs.Iso.StateAlreadyPresent");
                    summaryText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Iso.SummaryAlreadyPresent"), result.FileName, dataDriveRoot);
                    StatusLeft.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Iso.StatusAlreadyPresent"), result.FileName);
                }
                else if (result.Success)
                {
                    state.Text = Loc.T("SuiteBuilder.Mabs.Iso.StateDone");
                    summaryText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Iso.SummaryOk"), result.FileName, dataDriveRoot);
                    StatusLeft.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Iso.StatusDone"), result.FileName, dataDriveRoot);
                }
                else
                {
                    var error = result.Error ?? "";
                    state.Foreground = Brushes.IndianRed;
                    state.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Iso.StateFailed"), error);
                    summaryText.Foreground = Brushes.IndianRed;
                    summaryText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Iso.SummaryFailed"), spec.Name, error);
                    StatusLeft.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Iso.StatusFailed"), spec.Name, error);
                }
            }
            catch (OperationCanceledException)
            {
                state.Foreground = (Brush)FindResource("TextMuted");
                state.Text = Loc.T("SuiteBuilder.Mabs.Iso.StateCancelled");
                summaryText.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Iso.SummaryCancelled"), spec.Name);
                StatusLeft.Text = string.Format(Loc.T("SuiteBuilder.Mabs.Iso.StatusCancelled"), spec.Name);
            }
            finally
            {
                progress.IsIndeterminate = false;
                progress.Value = 100;
                progress.Visibility = Visibility.Collapsed;
                progress.IsIndeterminate = true;
                statusText.Text = "";

                foreach (var button in downloadButtons)
                    button.IsEnabled = true;
            }
        }

        // ----- AVAS: Treiber-Datenbank einrichten (Folgeschritt nach dem Bauen) -----

        // Offizielles "Intel Ethernet Adapter Complete Driver Pack" (~1,3 GB), direkt von Intels eigenem Server.
        // HINWEIS: URL und Version sind fest verdrahtet und können veralten, sobald Intel ein neues Release
        // veröffentlicht – dann muss diese Konstante hier von Hand aktualisiert werden. Bewusst akzeptierte
        // Einschränkung: es gibt keine stabile, automatisierbare "immer neueste Version"-URL bei Intel.
        private const string IntelEthernetPackUrl = "https://downloadmirror.intel.com/923522/Release_31.2.2.zip";

        // Reine Referenz-Adressen, die der Nutzer selbst im Browser öffnet (keine automatischen Downloads,
        // da nichts Drittanbieter-Software gehostet oder umverteilt wird).
        private const string SdiFullDatabaseUrl = "https://sdi-tool.org/download/";
        private const string RealtekDownloadUrl = "https://www.realtek.com/Download/";
        private const string IntelWirelessDownloadUrl =
            "https://www.intel.com/content/www/us/en/download/19351/intel-wireless-wi-fi-drivers-for-windows-10-and-windows-11.html";

        // Dasselbe für die beiden DART-Tools, die AEGIS nicht automatisch holen kann (Cloudflare-Bot-Prüfung)
        private const string HwInfoDownloadUrl = "https://www.hwinfo.com/download/";
        private const string CrystalDiskInfoDownloadUrl = "https://crystalmark.info/en/software/crystaldiskinfo/";

        // Der eigentliche Treiber-Scanner, den AVAS in "Drivers" als SDI*.exe sucht und startet.
        // Das ursprüngliche "Snappy Driver Installer" ist tot (seit 2017), gepflegt wird heute der Fork
        // SDIO (Snappy Driver Installer Origin) von Glenn Delahoy. Es gibt keine feste "latest"-URL,
        // deshalb wird der aktuelle ZIP-Link live aus der offiziellen Seite gefischt.
        private const string SdioPageUrl = "https://www.glenn.delahoy.com/snappy-driver-installer-origin/";
        private const string SdioBaseUrl = "https://www.glenn.delahoy.com";

        // Der erste Treffer auf der Seite ist die aktuelle Version (ältere Versionen stehen weiter unten).
        private static readonly Regex SdioZipLinkRegex =
            new(@"/downloads/sdio/SDIO_[0-9.]+\.zip", RegexOptions.IgnoreCase);

        // Aus dem ZIP wird ausschließlich das 64-Bit-Hauptprogramm übernommen (z. B. SDIO_x64_R887.exe).
        // Bewusst streng: SDIO-XP_x64_R*.exe, SDIO_R*.exe (32 Bit) und SDIOTranslationTool.exe fallen hier raus.
        private static readonly Regex SdioMainExeRegex =
            new(@"^SDIO_x64_R\d+\.exe$", RegexOptions.IgnoreCase);

        private const string AvasSuiteRepo = "AVAS";
        private const string DartSuiteRepo = "DART";
        private const string DriversFolderName = "Drivers";
        private const string IntelEthernetFolderName = "Intel-Ethernet";
        private const string FilesFolderName = "Files";

        // Themed Folgedialog nach einem erfolgreichen AVAS-Build: vollständige Offline-Datenbank vs. Lite.
        // Jederzeit abbrechbar – das ist eine optionale Hilfestellung, keine Pflichtentscheidung.
        private void ShowDriverDatabaseSetupDialog(string driveRoot)
        {
            var driversFolder = Path.Combine(driveRoot, DriversFolderName);

            var win = new Window
            {
                Title = Loc.T("DriverSetup.Title"),
                Width = 560,
                MaxHeight = 720,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (Brush)FindResource("BgPrimary"),
                FontFamily = FontFamily
            };

            // Beim Schließen des Fensters einen laufenden Download abbrechen, statt ihn ins Leere weiterlaufen zu lassen
            var cts = new System.Threading.CancellationTokenSource();
            win.Closed += (_, _) => { try { cts.Cancel(); } catch { } };

            var host = new ContentControl { Margin = new Thickness(20) };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = host
            };
            win.Content = scroll;

            host.Content = BuildDriverSetupChoiceView(win, host, driversFolder, cts.Token);

            win.ShowDialog();
        }

        // Schritt 1: Auswahl zwischen vollständiger Datenbank und Lite (oder später einrichten)
        private StackPanel BuildDriverSetupChoiceView(
            Window win, ContentControl host, string driversFolder, System.Threading.CancellationToken ct)
        {
            var panel = new StackPanel();

            panel.Children.Add(CreateDialogTextBlock(Loc.T("DriverSetup.Title"), "TextPrimary", 15, new Thickness(0, 0, 0, 10), bold: true));
            panel.Children.Add(CreateDialogTextBlock(Loc.T("DriverSetup.Intro"), "TextMuted", 12, new Thickness(0, 0, 0, 16)));

            panel.Children.Add(BuildDriverSetupOptionCard(
                Loc.T("DriverSetup.OptionFullTitle"),
                Loc.T("DriverSetup.OptionFullDescription"),
                Loc.T("DriverSetup.OptionFullButton"),
                () => host.Content = BuildDriverSetupFullView(win, host, driversFolder, ct)));

            panel.Children.Add(BuildDriverSetupOptionCard(
                Loc.T("DriverSetup.OptionLiteTitle"),
                Loc.T("DriverSetup.OptionLiteDescription"),
                Loc.T("DriverSetup.OptionLiteButton"),
                () => host.Content = BuildDriverSetupLiteView(win, host, driversFolder, ct)));

            var skipRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var skipButton = new Button
            {
                Content = Loc.T("DriverSetup.Skip"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                IsCancel = true
            };
            skipButton.Click += (_, _) => win.Close();
            skipRow.Children.Add(skipButton);
            panel.Children.Add(skipRow);

            return panel;
        }

        // Schritt 2a: vollständige Offline-Datenbank – nur Ordner anlegen + Anleitung
        private StackPanel BuildDriverSetupFullView(
            Window win, ContentControl host, string driversFolder, System.Threading.CancellationToken ct)
        {
            var panel = new StackPanel();

            panel.Children.Add(CreateDialogTextBlock(Loc.T("DriverSetup.FullHeading"), "TextPrimary", 15, new Thickness(0, 0, 0, 10), bold: true));
            panel.Children.Add(CreateDriverFolderResultText(driversFolder));

            // Das Scanner-Programm selbst wird unabhängig von der Datenbank-Variante immer geholt –
            // ohne SDI*.exe im Ordner "Drivers" überspringt AVAS den Treiberschritt kommentarlos.
            panel.Children.Add(BuildSdiToolSection(driversFolder, ct));

            panel.Children.Add(CreateDialogTextBlock(Loc.T("DriverSetup.FullSteps"), "TextMuted", 12, new Thickness(0, 0, 0, 14)));
            panel.Children.Add(CreateDialogTextBlock(Loc.T("DriverSetup.FullSourceLabel"), "TextMuted", 11, new Thickness(0, 0, 0, 4)));
            panel.Children.Add(CreateDialogHyperlink(SdiFullDatabaseUrl, 12, new Thickness(0, 0, 0, 18)));

            panel.Children.Add(BuildDriverSetupFooter(win, host, driversFolder, ct));

            return panel;
        }

        // Schritt 2b: Lite – Ordner anlegen, Intel-Ethernet-Pack herunterladen, Hinweise auf weitere Hersteller
        private StackPanel BuildDriverSetupLiteView(
            Window win, ContentControl host, string driversFolder, System.Threading.CancellationToken ct)
        {
            var panel = new StackPanel();

            panel.Children.Add(CreateDialogTextBlock(Loc.T("DriverSetup.LiteHeading"), "TextPrimary", 15, new Thickness(0, 0, 0, 10), bold: true));
            panel.Children.Add(CreateDriverFolderResultText(driversFolder));

            // Siehe Full-Ansicht: der Scanner gehört in beide Varianten, die Offline-Datenbank ist davon unabhängig.
            panel.Children.Add(BuildSdiToolSection(driversFolder, ct));

            panel.Children.Add(CreateDialogTextBlock(Loc.T("DriverSetup.IntelDescription"), "TextMuted", 12, new Thickness(0, 0, 0, 12)));

            var downloadButton = new Button
            {
                Content = Loc.T("DriverSetup.DownloadIntelButton"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            panel.Children.Add(downloadButton);

            var statusText = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            panel.Children.Add(statusText);

            var progress = CreateProgressBar();
            panel.Children.Add(progress);

            downloadButton.Click += (_, _) =>
                _ = DownloadIntelEthernetPackAsync(driversFolder, downloadButton, progress, statusText, ct);

            panel.Children.Add(CreateDialogTextBlock(Loc.T("DriverSetup.ManualHint"), "TextMuted", 12, new Thickness(0, 18, 0, 10)));

            panel.Children.Add(CreateDialogTextBlock(Loc.T("DriverSetup.ManualRealtekLabel"), "TextSecondary", 11, new Thickness(0, 0, 0, 2)));
            panel.Children.Add(CreateDialogHyperlink(RealtekDownloadUrl, 12, new Thickness(0, 0, 0, 10)));

            panel.Children.Add(CreateDialogTextBlock(Loc.T("DriverSetup.ManualIntelWlanLabel"), "TextSecondary", 11, new Thickness(0, 0, 0, 2)));
            panel.Children.Add(CreateDialogHyperlink(IntelWirelessDownloadUrl, 12, new Thickness(0, 0, 0, 18)));

            panel.Children.Add(BuildDriverSetupFooter(win, host, driversFolder, ct));

            return panel;
        }

        // Gemeinsame Fußzeile der beiden Detailansichten: zurück zur Auswahl oder Dialog schließen
        private StackPanel BuildDriverSetupFooter(
            Window win, ContentControl host, string driversFolder, System.Threading.CancellationToken ct)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var backButton = new Button
            {
                Content = Loc.T("DriverSetup.Back"),
                Style = (Style)FindResource("ToolbarButtonStyle")
            };
            backButton.Click += (_, _) => host.Content = BuildDriverSetupChoiceView(win, host, driversFolder, ct);
            row.Children.Add(backButton);

            var closeButton = new Button
            {
                Content = Loc.T("DriverSetup.Close"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                Margin = new Thickness(6, 0, 0, 0),
                IsCancel = true
            };
            closeButton.Click += (_, _) => win.Close();
            row.Children.Add(closeButton);

            return row;
        }

        // Karte für eine der beiden Optionen im Auswahlschritt
        private Border BuildDriverSetupOptionCard(string title, string description, string buttonText, Action onChosen)
        {
            var border = new Border
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 14)
            };

            var stack = new StackPanel();
            stack.Children.Add(CreateDialogTextBlock(title, "TextPrimary", 13, new Thickness(0, 0, 0, 6), bold: true));
            stack.Children.Add(CreateDialogTextBlock(description, "TextMuted", 11, new Thickness(0, 0, 0, 12)));

            var button = new Button
            {
                Content = buttonText,
                Style = (Style)FindResource("ToolbarButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            button.Click += (_, _) => onChosen();
            stack.Children.Add(button);

            border.Child = stack;
            return border;
        }

        // Legt Drivers\ auf dem Stick an und meldet Erfolg bzw. Fehler als Text zurück
        private TextBlock CreateDriverFolderResultText(string driversFolder)
        {
            try
            {
                Directory.CreateDirectory(driversFolder);
                StatusLeft.Text = string.Format(Loc.T("DriverSetup.StatusFolderCreated"), driversFolder);
                return CreateDialogTextBlock(
                    string.Format(Loc.T("DriverSetup.FolderCreated"), driversFolder),
                    "TextSecondary", 12, new Thickness(0, 0, 0, 12));
            }
            catch (Exception ex)
            {
                var block = CreateDialogTextBlock(
                    string.Format(Loc.T("DriverSetup.FolderError"), driversFolder, ex.Message),
                    "TextSecondary", 12, new Thickness(0, 0, 0, 12));
                block.Foreground = Brushes.IndianRed;
                return block;
            }
        }

        private TextBlock CreateDialogTextBlock(string text, string brushResourceKey, double fontSize, Thickness margin, bool bold = false)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = (Brush)FindResource(brushResourceKey),
                FontSize = fontSize,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = margin
            };
        }

        // Klickbarer Link, der die URL im Standardbrowser öffnet (Fehler beim Öffnen wird nur im Statusbereich vermerkt, kein Absturz)
        private TextBlock CreateDialogHyperlink(string url, double fontSize, Thickness margin)
        {
            var link = new Hyperlink(new Run(url))
            {
                NavigateUri = new Uri(url),
                Foreground = (Brush)FindResource("AccentBlue")
            };
            link.RequestNavigate += (_, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    StatusLeft.Text = string.Format(Loc.T("DriverSetup.ErrorOpeningLink"), ex.Message);
                }
                e.Handled = true;
            };

            var textBlock = new TextBlock
            {
                FontSize = fontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = margin,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            textBlock.Inlines.Add(link);
            return textBlock;
        }

        // Eigener Block für den Treiber-Scanner (SDIO) in beiden Detailansichten des Treiber-Dialogs.
        // Startet den Download sofort, wenn im Ordner "Drivers" noch kein passendes Programm liegt –
        // er ist klein (~12 MB) und ohne ihn ist der ganze Treiberschritt auf dem Stick wirkungslos.
        private Border BuildSdiToolSection(string driversFolder, System.Threading.CancellationToken ct)
        {
            var stack = new StackPanel();
            stack.Children.Add(CreateDialogTextBlock(Loc.T("DriverSetup.SdiToolHeading"), "TextPrimary", 13, new Thickness(0, 0, 0, 6), bold: true));
            stack.Children.Add(CreateDialogTextBlock(Loc.T("DriverSetup.SdiToolDescription"), "TextMuted", 11, new Thickness(0, 0, 0, 8)));

            var statusText = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(statusText);

            var progress = CreateProgressBar();
            stack.Children.Add(progress);

            var retryButton = new Button
            {
                Content = Loc.T("DriverSetup.SdiToolRetryButton"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };
            retryButton.Click += (_, _) => _ = DownloadSdiToolAsync(driversFolder, retryButton, progress, statusText, ct);
            stack.Children.Add(retryButton);

            var existing = FindExistingSdiTool(driversFolder);
            if (existing != null)
            {
                statusText.Text = string.Format(Loc.T("DriverSetup.SdiToolAlreadyPresent"), Path.GetFileName(existing));
                retryButton.Visibility = Visibility.Visible;
            }
            else
            {
                _ = DownloadSdiToolAsync(driversFolder, retryButton, progress, statusText, ct);
            }

            return new Border
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 14),
                Child = stack
            };
        }

        // Liegt im Ordner "Drivers" schon ein SDIO-Hauptprogramm? Dann nicht erneut laden.
        private static string? FindExistingSdiTool(string driversFolder)
        {
            try
            {
                if (!Directory.Exists(driversFolder))
                    return null;

                return Directory.EnumerateFiles(driversFolder, "*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => SdioMainExeRegex.IsMatch(Path.GetFileName(f)));
            }
            catch { return null; }
        }

        // Holt SDIO von der offiziellen Seite, entpackt es im Temp-Ordner und kopiert nur das
        // 64-Bit-Hauptprogramm nach Drivers\. Der Dateiname bleibt unverändert – er beginnt mit "SDI",
        // damit passt er auf AVAS' bestehende Suche nach SDI*.exe ohne Umbenennen.
        // Best effort: schlägt das fehl, blockiert es den restlichen Treiber-Dialog nicht.
        private async Task DownloadSdiToolAsync(
            string driversFolder, Button retryButton, ProgressBar progress, TextBlock statusText, System.Threading.CancellationToken ct)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "AEGIS-SDIO-" + Guid.NewGuid().ToString("N"));
            var zipFile = Path.Combine(tempRoot, "SDIO.zip");
            var extractFolder = Path.Combine(tempRoot, "extract");

            retryButton.IsEnabled = false;
            retryButton.Visibility = Visibility.Collapsed;
            statusText.Foreground = (Brush)FindResource("TextSecondary");

            progress.IsIndeterminate = true;
            progress.Minimum = 0;
            progress.Maximum = 100;
            progress.Value = 0;
            progress.Visibility = Visibility.Visible;

            IProgress<(long BytesRead, long TotalBytes)> downloadProgress = new Progress<(long BytesRead, long TotalBytes)>(p =>
            {
                if (p.TotalBytes > 0)
                {
                    var percent = (int)Math.Min(100, p.BytesRead * 100 / p.TotalBytes);
                    progress.IsIndeterminate = false;
                    progress.Value = percent;
                    statusText.Text = string.Format(
                        Loc.T("DriverSetup.SdiToolDownloading"), percent, FormatBytes(p.BytesRead), FormatBytes(p.TotalBytes));
                }
                else
                {
                    progress.IsIndeterminate = true;
                    statusText.Text = string.Format(Loc.T("DriverSetup.SdiToolDownloadingUnknownSize"), FormatBytes(p.BytesRead));
                }
            });

            try
            {
                Directory.CreateDirectory(driversFolder);

                statusText.Text = Loc.T("DriverSetup.SdiToolResolving");
                var html = await PackageDownloadService.Http.GetStringAsync(SdioPageUrl, ct);

                var link = SdioZipLinkRegex.Match(html);
                if (!link.Success)
                    throw new InvalidOperationException(Loc.T("DriverSetup.SdiToolNoLinkFound"));

                Directory.CreateDirectory(tempRoot);
                await DownloadFileWithProgressAsync(SdioBaseUrl + link.Value, zipFile, downloadProgress, ct);

                progress.IsIndeterminate = true;
                statusText.Text = Loc.T("DriverSetup.SdiToolExtracting");
                await Task.Run(() => ZipFile.ExtractToDirectory(zipFile, extractFolder, overwriteFiles: true), ct);

                var sourceExe = Directory.EnumerateFiles(extractFolder, "*.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(f => SdioMainExeRegex.IsMatch(Path.GetFileName(f)));

                if (sourceExe == null)
                    throw new InvalidOperationException(Loc.T("DriverSetup.SdiToolNoExeFound"));

                var fileName = Path.GetFileName(sourceExe);
                var targetExe = Path.Combine(driversFolder, fileName);
                await Task.Run(() => File.Copy(sourceExe, targetExe, overwrite: true), ct);

                statusText.Text = string.Format(Loc.T("DriverSetup.SdiToolDone"), fileName, driversFolder);
                StatusLeft.Text = string.Format(Loc.T("DriverSetup.StatusSdiTool"), fileName);
            }
            catch (OperationCanceledException)
            {
                statusText.Text = Loc.T("DriverSetup.SdiToolCancelled");
            }
            catch (Exception ex)
            {
                statusText.Foreground = Brushes.IndianRed;
                statusText.Text = string.Format(Loc.T("DriverSetup.SdiToolError"), ex.Message);
            }
            finally
            {
                TryDeleteTempFolder(tempRoot);
                progress.Visibility = Visibility.Collapsed;
                progress.IsIndeterminate = true;
                retryButton.IsEnabled = true;
                retryButton.Visibility = Visibility.Visible;
            }
        }

        // Temporären Entpack-Ordner samt ZIP wegräumen (Fehler dabei sind egal – Windows räumt Temp ohnehin auf)
        private static void TryDeleteTempFolder(string folder)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch { }
        }

        // Lädt das Intel-Ethernet-Paket nach Drivers\Intel-Ethernet\ und entpackt es dort.
        // Fortschrittsanzeige wie beim Release-Download im Suite-Builder.
        private async Task DownloadIntelEthernetPackAsync(
            string driversFolder, Button downloadButton, ProgressBar progress, TextBlock statusText, System.Threading.CancellationToken ct)
        {
            var targetFolder = Path.Combine(driversFolder, IntelEthernetFolderName);

            var fileName = "Intel-Ethernet-Pack.zip";
            try { fileName = Path.GetFileName(new Uri(IntelEthernetPackUrl).LocalPath); } catch { }
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "Intel-Ethernet-Pack.zip";

            var targetFile = Path.Combine(targetFolder, fileName);

            downloadButton.IsEnabled = false;
            progress.IsIndeterminate = false;
            progress.Minimum = 0;
            progress.Maximum = 100;
            progress.Value = 0;
            progress.Visibility = Visibility.Visible;

            IProgress<(long BytesRead, long TotalBytes)> downloadProgress = new Progress<(long BytesRead, long TotalBytes)>(p =>
            {
                if (p.TotalBytes > 0)
                {
                    var percent = (int)(p.BytesRead * 100 / p.TotalBytes);
                    progress.Value = Math.Min(100, percent);
                    statusText.Text = string.Format(
                        Loc.T("DriverSetup.Downloading"), percent, FormatBytes(p.BytesRead), FormatBytes(p.TotalBytes));
                }
                else
                {
                    progress.IsIndeterminate = true;
                    statusText.Text = string.Format(Loc.T("DriverSetup.DownloadingUnknownSize"), FormatBytes(p.BytesRead));
                }
            });

            try
            {
                Directory.CreateDirectory(targetFolder);

                statusText.Text = string.Format(Loc.T("DriverSetup.Downloading"), 0, FormatBytes(0), FormatBytes(0));
                await DownloadFileWithProgressAsync(IntelEthernetPackUrl, targetFile, downloadProgress, ct);

                progress.IsIndeterminate = true;
                statusText.Text = string.Format(Loc.T("DriverSetup.Extracting"), targetFolder);
                await Task.Run(() => ZipFile.ExtractToDirectory(targetFile, targetFolder, overwriteFiles: true), ct);

                // ZIP nach dem Entpacken entfernen – sonst liegt das Paket doppelt (~2,6 GB) auf dem Stick
                try { File.Delete(targetFile); } catch { }

                statusText.Text = string.Format(Loc.T("DriverSetup.DownloadDone"), targetFolder);
                StatusLeft.Text = string.Format(Loc.T("DriverSetup.StatusDone"), targetFolder);
            }
            catch (OperationCanceledException)
            {
                TryDeletePartialDownload(targetFile);
                statusText.Text = Loc.T("DriverSetup.DownloadCancelled");
            }
            catch (Exception ex)
            {
                TryDeletePartialDownload(targetFile);
                statusText.Text = string.Format(Loc.T("DriverSetup.DownloadError"), ex.Message);
            }
            finally
            {
                progress.Visibility = Visibility.Collapsed;
                progress.IsIndeterminate = true;
                downloadButton.IsEnabled = true;
            }
        }

        // Halb heruntergeladene ZIPs nicht auf dem Stick liegen lassen
        private static void TryDeletePartialDownload(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }

        // Wie GiteaService.DownloadAssetAsync, nur ohne Token/Gitea-spezifische URL-Korrektur.
        // Die Schleife selbst liegt in PackageDownloadService, damit es sie im Projekt nur einmal gibt –
        // dort steckt auch der HttpClient mit User-Agent (Intel antwortet ohne ihn mit 403, GitHub ebenso).
        private static Task DownloadFileWithProgressAsync(
            string url,
            string targetFile,
            IProgress<(long BytesRead, long TotalBytes)>? progress,
            System.Threading.CancellationToken ct)
            => PackageDownloadService.DownloadToFileAsync(url, targetFile, progress, ct);

        // ----- Software-Pakete nach einem AVAS-Build -----

        // Zweiter, vom Treiber-Dialog unabhängiger Folgeschritt: die Installer, die packages.json auf dem
        // Stick im Ordner "Files" erwartet, direkt beim jeweiligen Hersteller holen. Auch das ist optional –
        // AVAS meldet fehlende Dateien beim Installieren nur als überspringbaren Fehler.
        private void ShowPackageDownloadSetupDialog(string driveRoot)
        {
            var filesFolder = Path.Combine(driveRoot, FilesFolderName);
            var packages = PackageDownloadService.AllPackages;

            var win = new Window
            {
                Title = Loc.T("PackageSetup.Title"),
                Width = 620,
                MaxHeight = 720,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (Brush)FindResource("BgPrimary"),
                FontFamily = FontFamily
            };

            // Beim Schließen des Fensters laufende Downloads abbrechen, statt sie ins Leere weiterlaufen zu lassen
            var cts = new System.Threading.CancellationTokenSource();
            win.Closed += (_, _) => { try { cts.Cancel(); } catch { } };

            var panel = new StackPanel { Margin = new Thickness(20) };
            win.Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            };

            panel.Children.Add(CreateDialogTextBlock(Loc.T("PackageSetup.Title"), "TextPrimary", 15, new Thickness(0, 0, 0, 10), bold: true));
            panel.Children.Add(CreateDialogTextBlock(Loc.T("PackageSetup.Intro"), "TextMuted", 12, new Thickness(0, 0, 0, 12)));
            panel.Children.Add(CreatePackageFolderResultText(filesFolder));

            // Liste der Pakete: Name, Zieldateiname und ein Statusfeld, das der Download-Lauf fortschreibt
            var listStack = new StackPanel();
            var packageStates = new List<TextBlock>();

            foreach (var package in packages)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var nameText = CreateDialogTextBlock(package.Name, "TextPrimary", 12, new Thickness(0));

                // Brave ist als Einziges ein Online-Stub statt eines vollständigen Offline-Installers –
                // das steht als Info-Symbol mit Tooltip direkt neben dem Namen, nicht als Fließtext.
                if (package.TargetFileName.Equals("brave_setup.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
                    nameRow.Children.Add(nameText);
                    nameRow.Children.Add(CreatePackageInfoIcon(Loc.T("PackageSetup.BraveStubHint")));
                    Grid.SetColumn(nameRow, 0);
                    row.Children.Add(nameRow);
                }
                else
                {
                    Grid.SetColumn(nameText, 0);
                    row.Children.Add(nameText);
                }

                var fileText = CreateDialogTextBlock(package.TargetFileName, "TextMuted", 11, new Thickness(0));
                Grid.SetColumn(fileText, 1);
                row.Children.Add(fileText);

                var stateText = CreateDialogTextBlock(Loc.T("PackageSetup.StatePending"), "TextMuted", 11, new Thickness(0));
                Grid.SetColumn(stateText, 2);
                row.Children.Add(stateText);

                packageStates.Add(stateText);
                listStack.Children.Add(row);
            }

            panel.Children.Add(new Border
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = listStack
            });

            panel.Children.Add(CreateDialogTextBlock(Loc.T("PackageSetup.ChromeNote"), "TextMuted", 11, new Thickness(0, 0, 0, 14)));

            var downloadButton = new Button
            {
                Content = Loc.T("PackageSetup.DownloadAllButton"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            panel.Children.Add(downloadButton);

            var statusText = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            panel.Children.Add(statusText);

            var progress = CreateProgressBar();
            panel.Children.Add(progress);

            var summaryText = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            panel.Children.Add(summaryText);

            downloadButton.Click += (_, _) => _ = DownloadAllPackagesAsync(
                filesFolder, downloadButton, progress, statusText, summaryText, packageStates, cts.Token);

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            var closeButton = new Button
            {
                Content = Loc.T("PackageSetup.Close"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                IsCancel = true
            };
            closeButton.Click += (_, _) => win.Close();
            footer.Children.Add(closeButton);
            panel.Children.Add(footer);

            win.ShowDialog();
        }

        // Legt Files\ auf dem Stick an und meldet Erfolg bzw. Fehler als Text zurück
        private TextBlock CreatePackageFolderResultText(string filesFolder)
        {
            try
            {
                Directory.CreateDirectory(filesFolder);
                StatusLeft.Text = string.Format(Loc.T("PackageSetup.StatusFolderCreated"), filesFolder);
                return CreateDialogTextBlock(
                    string.Format(Loc.T("PackageSetup.FolderCreated"), filesFolder),
                    "TextSecondary", 12, new Thickness(0, 0, 0, 14));
            }
            catch (Exception ex)
            {
                var block = CreateDialogTextBlock(
                    string.Format(Loc.T("PackageSetup.FolderError"), filesFolder, ex.Message),
                    "TextSecondary", 12, new Thickness(0, 0, 0, 14));
                block.Foreground = Brushes.IndianRed;
                return block;
            }
        }

        // Kleines "ⓘ" neben einem Paketnamen, das den übergebenen Hinweis beim Hovern als Tooltip zeigt
        private TextBlock CreatePackageInfoIcon(string hint)
        {
            var icon = new TextBlock
            {
                Text = "ⓘ",
                Foreground = (Brush)FindResource("AccentBlue"),
                FontSize = 11,
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Help
            };

            var hintText = new TextBlock
            {
                Text = hint,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };

            var callout = new Border
            {
                Background = (Brush)FindResource("BgSurface"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Child = hintText
            };

            icon.ToolTip = new ToolTip
            {
                Content = callout,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HasDropShadow = true,
                MaxWidth = 340,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                VerticalOffset = 6,
                FontFamily = FontFamily
            };

            return icon;
        }

        // Lädt alle Pakete nacheinander (nicht parallel – ein Hersteller-CDN nach dem anderen reicht völlig
        // und hält die Fortschrittsanzeige einfach). Ein Fehlschlag stoppt den Durchlauf nicht.
        private async Task DownloadAllPackagesAsync(
            string filesFolder,
            Button downloadButton,
            ProgressBar progress,
            TextBlock statusText,
            TextBlock summaryText,
            IReadOnlyList<TextBlock> packageStates,
            System.Threading.CancellationToken ct)
        {
            var packages = PackageDownloadService.AllPackages;

            downloadButton.IsEnabled = false;
            progress.IsIndeterminate = false;
            progress.Minimum = 0;
            progress.Maximum = 100;
            progress.Value = 0;
            progress.Visibility = Visibility.Visible;

            summaryText.Text = "";
            summaryText.Foreground = (Brush)FindResource("TextSecondary");

            StatusLeft.Text = string.Format(Loc.T("PackageSetup.StatusDownloading"), filesFolder);

            var failures = new List<string>();
            var succeeded = 0;
            var cancelled = false;

            for (var i = 0; i < packages.Count; i++)
            {
                var package = packages[i];
                var position = i + 1;
                var state = packageStates[i];

                state.Foreground = (Brush)FindResource("TextSecondary");
                state.Text = string.Format(Loc.T("PackageSetup.StateDownloading"), 0);
                statusText.Text = string.Format(
                    Loc.T("PackageSetup.Progress"), package.Name, position, packages.Count, 0, FormatBytes(0), FormatBytes(0));

                IProgress<(long BytesRead, long TotalBytes)> packageProgress = new Progress<(long BytesRead, long TotalBytes)>(p =>
                {
                    if (p.TotalBytes > 0)
                    {
                        var percent = (int)Math.Min(100, p.BytesRead * 100 / p.TotalBytes);
                        state.Text = string.Format(Loc.T("PackageSetup.StateDownloading"), percent);
                        statusText.Text = string.Format(
                            Loc.T("PackageSetup.Progress"), package.Name, position, packages.Count, percent,
                            FormatBytes(p.BytesRead), FormatBytes(p.TotalBytes));

                        // Gesamtbalken: abgeschlossene Pakete plus Fortschritt im aktuellen Paket
                        progress.IsIndeterminate = false;
                        progress.Value = Math.Min(100, (i * 100 + percent) / (double)packages.Count);
                    }
                    else
                    {
                        // manche Hersteller-CDNs liefern keine Content-Length (gechunkte Antwort)
                        state.Text = FormatBytes(p.BytesRead);
                        statusText.Text = string.Format(
                            Loc.T("PackageSetup.ProgressUnknownSize"), package.Name, position, packages.Count, FormatBytes(p.BytesRead));
                        progress.IsIndeterminate = true;
                    }
                });

                try
                {
                    var result = await PackageDownloadService.DownloadPackageAsync(package, filesFolder, packageProgress, ct);

                    if (result.Success)
                    {
                        succeeded++;
                        state.Foreground = (Brush)FindResource("TextSecondary");
                        state.Text = Loc.T("PackageSetup.StateDone");
                    }
                    else
                    {
                        var error = result.Error ?? "";
                        failures.Add(string.Format(Loc.T("PackageSetup.FailureItem"), package.Name, error));
                        state.Foreground = Brushes.IndianRed;
                        state.Text = string.Format(Loc.T("PackageSetup.StateFailed"), error);
                    }
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    state.Foreground = (Brush)FindResource("TextMuted");
                    state.Text = Loc.T("PackageSetup.StateCancelled");
                    break;
                }
            }

            progress.IsIndeterminate = false;
            progress.Value = 100;
            progress.Visibility = Visibility.Collapsed;
            progress.IsIndeterminate = true;
            statusText.Text = "";
            downloadButton.IsEnabled = true;

            if (cancelled)
            {
                summaryText.Text = string.Format(Loc.T("PackageSetup.SummaryCancelled"), succeeded, packages.Count);
                StatusLeft.Text = string.Format(Loc.T("PackageSetup.StatusCancelled"), succeeded, packages.Count);
                return;
            }

            if (failures.Count == 0)
            {
                summaryText.Text = string.Format(Loc.T("PackageSetup.SummaryAllOk"), packages.Count, filesFolder);
            }
            else
            {
                summaryText.Foreground = Brushes.IndianRed;
                summaryText.Text = string.Format(
                    Loc.T("PackageSetup.SummaryPartial"), succeeded, packages.Count, string.Join("; ", failures));
            }

            StatusLeft.Text = string.Format(Loc.T("PackageSetup.StatusDone"), succeeded, packages.Count, filesFolder);
        }

        // ----- Diagnose-Tools nach einem DART-Build -----

        // Folgeschritt nach einem erfolgreichen DART-Build: DARTs Buttons zeigen auf feste, versionslose
        // Ordner (z. B. "01_Hardware\CPU-Z"), die das Repository selbst nicht enthält – ohne diesen Schritt
        // meldet DART beim Klick nur "Datei nicht gefunden". Optional und jederzeit abbrechbar.
        private void ShowDartToolsSetupDialog(string driveRoot)
        {
            var tools = DartToolDownloadService.AllTools;

            var win = new Window
            {
                Title = Loc.T("DartToolSetup.Title"),
                Width = 620,
                MaxHeight = 720,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (Brush)FindResource("BgPrimary"),
                FontFamily = FontFamily
            };

            // Beim Schließen des Fensters laufende Downloads abbrechen, statt sie ins Leere weiterlaufen zu lassen
            var cts = new System.Threading.CancellationTokenSource();
            win.Closed += (_, _) => { try { cts.Cancel(); } catch { } };

            var panel = new StackPanel { Margin = new Thickness(20) };
            win.Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            };

            panel.Children.Add(CreateDialogTextBlock(Loc.T("DartToolSetup.Title"), "TextPrimary", 15, new Thickness(0, 0, 0, 10), bold: true));
            panel.Children.Add(CreateDialogTextBlock(Loc.T("DartToolSetup.Intro"), "TextMuted", 12, new Thickness(0, 0, 0, 12)));
            panel.Children.Add(CreateDialogTextBlock(
                string.Format(Loc.T("DartToolSetup.TargetRoot"), driveRoot), "TextSecondary", 12, new Thickness(0, 0, 0, 14)));

            // Liste der Tools: Name, Zielordner und ein Statusfeld, das der Download-Lauf fortschreibt
            var listStack = new StackPanel();
            var toolStates = new List<TextBlock>();

            foreach (var tool in tools)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var nameText = CreateDialogTextBlock(tool.Name, "TextPrimary", 12, new Thickness(0));
                Grid.SetColumn(nameText, 0);
                row.Children.Add(nameText);

                var folderText = CreateDialogTextBlock(tool.TargetRelativeFolder, "TextMuted", 11, new Thickness(0));
                Grid.SetColumn(folderText, 1);
                row.Children.Add(folderText);

                var stateText = CreateDialogTextBlock(Loc.T("DartToolSetup.StatePending"), "TextMuted", 11, new Thickness(0));
                Grid.SetColumn(stateText, 2);
                row.Children.Add(stateText);

                toolStates.Add(stateText);
                listStack.Children.Add(row);
            }

            panel.Children.Add(new Border
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = listStack
            });

            panel.Children.Add(CreateDialogTextBlock(Loc.T("DartToolSetup.SourcesNote"), "TextMuted", 11, new Thickness(0, 0, 0, 14)));

            var downloadButton = new Button
            {
                Content = Loc.T("DartToolSetup.DownloadAllButton"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            panel.Children.Add(downloadButton);

            var statusText = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            panel.Children.Add(statusText);

            var progress = CreateProgressBar();
            panel.Children.Add(progress);

            var summaryText = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            panel.Children.Add(summaryText);

            downloadButton.Click += (_, _) => _ = DownloadAllDartToolsAsync(
                driveRoot, downloadButton, progress, statusText, summaryText, toolStates, cts.Token);

            // Zweiter, deutlich abgesetzter Block: die beiden Tools, die AEGIS nicht automatisch holen kann.
            panel.Children.Add(BuildDartManualToolsSection(driveRoot));

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            var closeButton = new Button
            {
                Content = Loc.T("DartToolSetup.Close"),
                Style = (Style)FindResource("ToolbarButtonStyle"),
                IsCancel = true
            };
            closeButton.Click += (_, _) => win.Close();
            footer.Children.Add(closeButton);
            panel.Children.Add(footer);

            win.ShowDialog();
        }

        // HWiNFO und CrystalDiskInfo lassen sich nicht automatisieren: Ihre Downloads laufen über
        // SourceForge bzw. die eigene Herstellerseite, und beide antworten HttpClient-Anfragen mit einer
        // Cloudflare-Bot-Prüfung (403). Die greift an der TLS-Signatur des Clients an, kein User-Agent
        // hilft dagegen. Statt eines Downloads gibt es hier dieselben klickbaren Links wie im
        // Treiber-Dialog für Realtek und Intel-WLAN – plus den exakten Zielordner auf dem Stick.
        private Border BuildDartManualToolsSection(string driveRoot)
        {
            var stack = new StackPanel();

            stack.Children.Add(CreateDialogTextBlock(Loc.T("DartToolSetup.ManualHeading"), "TextPrimary", 13, new Thickness(0, 0, 0, 6), bold: true));
            stack.Children.Add(CreateDialogTextBlock(Loc.T("DartToolSetup.ManualHint"), "TextMuted", 11, new Thickness(0, 0, 0, 12)));

            stack.Children.Add(CreateDialogTextBlock(
                string.Format(Loc.T("DartToolSetup.ManualHwInfoLabel"), Path.Combine(driveRoot, @"01_Hardware\HWiNFO")),
                "TextSecondary", 11, new Thickness(0, 0, 0, 2)));
            stack.Children.Add(CreateDialogHyperlink(HwInfoDownloadUrl, 12, new Thickness(0, 0, 0, 12)));

            stack.Children.Add(CreateDialogTextBlock(
                string.Format(Loc.T("DartToolSetup.ManualCrystalDiskInfoLabel"), Path.Combine(driveRoot, @"02_Disk\CrystalDiskInfo")),
                "TextSecondary", 11, new Thickness(0, 0, 0, 2)));
            stack.Children.Add(CreateDialogHyperlink(CrystalDiskInfoDownloadUrl, 12, new Thickness(0, 0, 0, 0)));

            return new Border
            {
                Background = (Brush)FindResource("BgSurfaceAlt"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 22, 0, 0),
                Child = stack
            };
        }

        // Lädt alle Tools nacheinander (nicht parallel – ein Anbieter nach dem anderen reicht völlig
        // und hält die Fortschrittsanzeige einfach). Ein Fehlschlag stoppt den Durchlauf nicht.
        private async Task DownloadAllDartToolsAsync(
            string driveRoot,
            Button downloadButton,
            ProgressBar progress,
            TextBlock statusText,
            TextBlock summaryText,
            IReadOnlyList<TextBlock> toolStates,
            System.Threading.CancellationToken ct)
        {
            var tools = DartToolDownloadService.AllTools;

            downloadButton.IsEnabled = false;
            progress.IsIndeterminate = false;
            progress.Minimum = 0;
            progress.Maximum = 100;
            progress.Value = 0;
            progress.Visibility = Visibility.Visible;

            summaryText.Text = "";
            summaryText.Foreground = (Brush)FindResource("TextSecondary");

            StatusLeft.Text = string.Format(Loc.T("DartToolSetup.StatusDownloading"), driveRoot);

            var failures = new List<string>();
            var succeeded = 0;
            var cancelled = false;

            for (var i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];
                var position = i + 1;
                var state = toolStates[i];

                state.Foreground = (Brush)FindResource("TextSecondary");
                state.Text = string.Format(Loc.T("DartToolSetup.StateDownloading"), 0);
                statusText.Text = string.Format(
                    Loc.T("DartToolSetup.Progress"), tool.Name, position, tools.Count, 0, FormatBytes(0), FormatBytes(0));

                IProgress<(long BytesRead, long TotalBytes)> toolProgress = new Progress<(long BytesRead, long TotalBytes)>(p =>
                {
                    if (p.TotalBytes > 0)
                    {
                        var percent = (int)Math.Min(100, p.BytesRead * 100 / p.TotalBytes);
                        state.Text = string.Format(Loc.T("DartToolSetup.StateDownloading"), percent);
                        statusText.Text = string.Format(
                            Loc.T("DartToolSetup.Progress"), tool.Name, position, tools.Count, percent,
                            FormatBytes(p.BytesRead), FormatBytes(p.TotalBytes));

                        // Gesamtbalken: abgeschlossene Tools plus Fortschritt im aktuellen Tool
                        progress.IsIndeterminate = false;
                        progress.Value = Math.Min(100, (i * 100 + percent) / (double)tools.Count);
                    }
                    else
                    {
                        // manche Anbieter liefern keine Content-Length (gechunkte Antwort)
                        state.Text = FormatBytes(p.BytesRead);
                        statusText.Text = string.Format(
                            Loc.T("DartToolSetup.ProgressUnknownSize"), tool.Name, position, tools.Count, FormatBytes(p.BytesRead));
                        progress.IsIndeterminate = true;
                    }
                });

                try
                {
                    var result = await DartToolDownloadService.DownloadToolAsync(tool, driveRoot, toolProgress, ct);

                    if (result.Success)
                    {
                        succeeded++;
                        state.Foreground = (Brush)FindResource("TextSecondary");
                        state.Text = Loc.T("DartToolSetup.StateDone");
                    }
                    else
                    {
                        var error = result.Error ?? "";
                        failures.Add(string.Format(Loc.T("DartToolSetup.FailureItem"), tool.Name, error));
                        state.Foreground = Brushes.IndianRed;
                        state.Text = string.Format(Loc.T("DartToolSetup.StateFailed"), error);
                    }
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    state.Foreground = (Brush)FindResource("TextMuted");
                    state.Text = Loc.T("DartToolSetup.StateCancelled");
                    break;
                }
            }

            progress.IsIndeterminate = false;
            progress.Value = 100;
            progress.Visibility = Visibility.Collapsed;
            progress.IsIndeterminate = true;
            statusText.Text = "";
            downloadButton.IsEnabled = true;

            if (cancelled)
            {
                summaryText.Text = string.Format(Loc.T("DartToolSetup.SummaryCancelled"), succeeded, tools.Count);
                StatusLeft.Text = string.Format(Loc.T("DartToolSetup.StatusCancelled"), succeeded, tools.Count);
                return;
            }

            if (failures.Count == 0)
            {
                summaryText.Text = string.Format(Loc.T("DartToolSetup.SummaryAllOk"), tools.Count, driveRoot);
            }
            else
            {
                summaryText.Foreground = Brushes.IndianRed;
                summaryText.Text = string.Format(
                    Loc.T("DartToolSetup.SummaryPartial"), succeeded, tools.Count, string.Join("; ", failures));
            }

            StatusLeft.Text = string.Format(Loc.T("DartToolSetup.StatusDone"), succeeded, tools.Count, driveRoot);
        }

        // Löscht alle Dateien und Ordner im Wurzelverzeichnis des Laufwerks.
        // Einzelne gesperrte oder geschützte Einträge (z. B. "System Volume Information")
        // werden übersprungen, damit der Rest trotzdem entfernt wird.
        private static void WipeDriveRoot(string driveRoot)
        {
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(driveRoot);
            }
            catch
            {
                return;
            }

            foreach (var entry in entries)
            {
                try
                {
                    if (Directory.Exists(entry))
                    {
                        Directory.Delete(entry, recursive: true);
                    }
                    else if (File.Exists(entry))
                    {
                        // Schreibschutz/Versteckt-Attribute vorher entfernen, sonst schlägt File.Delete fehl
                        try { File.SetAttributes(entry, FileAttributes.Normal); } catch { }
                        File.Delete(entry);
                    }
                }
                catch
                {
                    // Eintrag gesperrt oder System – überspringen und weitermachen
                }
            }
        }

        // Setzt die Datenträgerbezeichnung über WMI (Win32_Volume).
        // Gibt null bei Erfolg zurück, sonst die Fehlermeldung – ein Fehlschlag ist nicht kritisch,
        // die Dateien liegen dann trotzdem schon auf dem Stick.
        private static string? TrySetVolumeLabel(string driveRoot, string label)
        {
            try
            {
                // "E:\" -> "E:" (Win32_Volume.DriveLetter ist ohne Backslash)
                var driveLetter = driveRoot.TrimEnd('\\', '/');

                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Volume WHERE DriveLetter = '{driveLetter}'");

                foreach (var item in searcher.Get())
                {
                    using var volume = (System.Management.ManagementObject)item;
                    volume["Label"] = label;
                    volume.Put();
                    return null;
                }

                return $"Win32_Volume {driveLetter} not found";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
            if (bytes >= 1024L * 1024)
                return $"{bytes / (1024.0 * 1024):0.0} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes} B";
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
