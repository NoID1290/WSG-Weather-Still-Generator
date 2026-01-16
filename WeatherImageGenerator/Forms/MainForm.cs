using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Drawing;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Utilities;
using WeatherImageGenerator.Models;
using EAS;

namespace WeatherImageGenerator.Forms
{
    public class MainForm : Form
    {
        private CancellationTokenSource? _cts;
        private NotifyIcon? _notifyIcon;
        private bool _isMinimizedToTray = false;
        // Store text together with the explicit LogLevel so rendering is deterministic
        private readonly System.Collections.Generic.List<(string Text, Logger.LogLevel Level)> _logBuffer = new System.Collections.Generic.List<(string Text, Logger.LogLevel Level)>();
        private ComboBox? _cmbFilter;
        private ComboBox? _cmbVerbosity;
        private TextBox? _txtSearch;
        private CheckBox? _chkCompact;
        private TextProgressBar? _progress;
        private Label? _statusLabel;
        private Label? _sleepLabel;
        private Label? _lastFetchLabel;
        private ListView? _weatherList;
        private RichTextBox? _logBox;
        private SplitContainer? _splitContainer;
        private TabControl? _tabControl;
        private TabPage? _logTab;
        private Panel? _topPanel;
        private Panel? _logPanel;
        private Button? _startBtn, _stopBtn, _fetchBtn, _stillBtn, _videoBtn, _openOutputBtn, _clearDirBtn, _locationsBtn, _musicBtn, _settingsBtn, _aboutBtn, _clearLogBtn, _cancelBtn, _galleryBtn, _testAlertBtn;
        private CancellationTokenSource? _operationCts;
        private Services.VideoGenerator? _runningVideoGenerator; 
        private Label? _groupLabel1, _groupLabel2, _groupLabel3, _groupLabel4, _progressLabel, _statusLabel2, _lblLog;
        private System.Threading.Timer? _logArchiveTimer;

        // NAAD Status Panel
        private Panel? _naadPanel;
        private Label? _naadTitleLabel;
        private Label? _naadConnectionLabel;
        private Label? _naadHeartbeatLabel;
        private Label? _naadAlertLabel;
        private AlertReadyClient? _naadClient;
        private CancellationTokenSource? _naadCts;

        // Theme colors for dynamic updates
        private Color _themeSuccessColor = Color.Green;
        private Color _themeDangerColor = Color.Red;
        private Color _themeWarningColor = Color.Orange;
        private Color _themeTextColor = Color.Black;
        private Color _themeAccentColor = Color.Blue;

        // When Minimal verbosity is selected, show only the last N important lines
        private const int MinimalVisibleCount = 5;    // reduced for casual users (show only 5 lines)

        // Log archival settings: when the on-screen log grows past this many lines
        private const int LogArchiveThreshold = 2000;      // lines in RichTextBox before archiving triggers
        private const int LogArchiveKeepRecent = 200;      // keep these many most-recent lines on-screen after archiving
        private readonly string LogArchiveFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private readonly string LogArchiveFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "archived_logs.b64");

        // Video phase mapping (when ffmpeg reports 0-100 we map it into [videoBase, 100])
        private double _videoBase = 80.0;
        private double _videoRange = 20.0;
        private bool _videoActive = false;

        // Store fetched weather data for detail views
        private OpenMeteo.WeatherForecast?[]? _cachedForecasts;
        private System.Collections.Generic.List<AlertEntry>? _cachedAlerts;

        public MainForm()
        {
            this.Text = "WSG - WeatherStillGenerator";
            this.Width = 1220;
            this.Height = 700;
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            _logBox = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Name = "logBox", Font = new System.Drawing.Font("Consolas", 9.5F), DetectUrls = true, HideSelection = false, ScrollBars = RichTextBoxScrollBars.Vertical, BorderStyle = BorderStyle.None, Padding = new Padding(8) };
            // Note: RichTextBox with Dock=Fill doesn't support Region properly, so we skip rounding for it

            // Start a background timer that will periodically archive older logs to disk to avoid UI growth/crashes
            _logArchiveTimer = new System.Threading.Timer(_ => {
                try { TryArchiveLogsIfNeededSafe(); } catch { }
            }, null, 30000, 30000); // every 30s
            
            // === CONTROL GROUPS - Reorganized for Better Layout ===
            // Row 1: Main Operations
            // Group 1: Control Operations
            _groupLabel1 = new Label { Text = "CONTROL", Left = 15, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 8F, FontStyle.Bold) };
            _startBtn = CreateStyledButton("▶ Start", 15, 28, 100, 38, Color.Gray, Color.White);
            _stopBtn = CreateStyledButton("⏹ Stop", 125, 28, 100, 38, Color.Gray, Color.White);
            _stopBtn.Enabled = false;
            
            // Group 2: Generation Operations
            _groupLabel2 = new Label { Text = "GENERATE", Left = 245, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 8F, FontStyle.Bold) };
            _fetchBtn = CreateStyledButton("🔄 Fetch", 245, 28, 100, 38, Color.Gray, Color.White);
            _stillBtn = CreateStyledButton("📷 Still", 355, 28, 100, 38, Color.Gray, Color.White);
            _videoBtn = CreateStyledButton("🎬 Video", 465, 28, 100, 38, Color.Gray, Color.White);
            _cancelBtn = CreateStyledButton("✖ Cancel", 575, 28, 100, 38, Color.DarkRed, Color.White);
            _cancelBtn.Enabled = false;
            _testAlertBtn = CreateStyledButton("🧪 Emergency Test Alert", 245, 76, 210, 38, Color.DarkOrange, Color.White);
            
            // Group 3: File Operations
            _groupLabel3 = new Label { Text = "FILES", Left = 695, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 8F, FontStyle.Bold) };
            _openOutputBtn = CreateStyledButton("📁 Open", 695, 28, 100, 38, Color.Gray, Color.White);
            _clearDirBtn = CreateStyledButton("🗑 Clear", 805, 28, 100, 38, Color.Gray, Color.White);
            _galleryBtn = CreateStyledButton("🖼 Gallery", 695, 76, 100, 38, Color.Gray, Color.White);
            
            // Row 2: Settings & Configuration (2 rows)
            _groupLabel4 = new Label { Text = "SETTINGS", Left = 925, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 8F, FontStyle.Bold) };
            _locationsBtn = CreateStyledButton("📍 Locations", 925, 28, 120, 38, Color.Gray, Color.White);
            _musicBtn = CreateStyledButton("🎵 Music", 1055, 28, 120, 38, Color.Gray, Color.White);
            _settingsBtn = CreateStyledButton("⚙ Settings", 925, 76, 120, 38, Color.Gray, Color.White);
            _aboutBtn = CreateStyledButton("ℹ About", 1055, 76, 120, 38, Color.Gray, Color.White);

            // Progress & Status (Below buttons - adjusted for 2-row settings)
            _progressLabel = new Label { Text = "PROGRESS", Left = 15, Top = 124, AutoSize = true, Font = new Font("Segoe UI", 8F, FontStyle.Bold) };
            _progress = new TextProgressBar { Left = 15, Top = 144, Width = 600, Height = 28, Style = ProgressBarStyle.Continuous, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };

            // NAAD Status Panel (next to status area)
            _naadPanel = new Panel { Left = 15, Top = 178, Width = 600, Height = 18, BackColor = Color.Transparent };
            _naadTitleLabel = new Label { Text = "📡 NAAD:", Left = 0, Top = 0, AutoSize = true, Font = new Font("Segoe UI", 8F, FontStyle.Bold) };
            _naadConnectionLabel = new Label { Text = "⚪ Offline", Left = 55, Top = 0, AutoSize = true, Font = new Font("Segoe UI", 8F, FontStyle.Regular) };
            _naadHeartbeatLabel = new Label { Text = "💓 --:--:--", Left = 160, Top = 0, AutoSize = true, Font = new Font("Segoe UI", 8F, FontStyle.Regular) };
            _naadAlertLabel = new Label { Text = "⚠ 0 alerts", Left = 280, Top = 0, AutoSize = true, Font = new Font("Segoe UI", 8F, FontStyle.Regular) };
            _naadPanel.Controls.Add(_naadTitleLabel);
            _naadPanel.Controls.Add(_naadConnectionLabel);
            _naadPanel.Controls.Add(_naadHeartbeatLabel);
            _naadPanel.Controls.Add(_naadAlertLabel);

            _statusLabel2 = new Label { Text = "STATUS", Left = 635, Top = 124, AutoSize = true, Font = new Font("Segoe UI", 8F, FontStyle.Bold) };
            _statusLabel = new Label { Left = 635, Top = 144, Width = 440, Height = 28, Text = "● Idle", AutoSize = false, Font = new Font("Segoe UI", 10F, FontStyle.Bold), BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
            _sleepLabel = new Label { Left = 635, Top = 174, Width = 440, Height = 20, Text = string.Empty, AutoSize = false, Font = new Font("Segoe UI", 9.5F, FontStyle.Regular), BackColor = Color.Transparent };
            _lastFetchLabel = new Label { Dock = DockStyle.Top, Height = 30, Text = "📡 Last fetch: Never", Font = new Font("Segoe UI", 9.5F, FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 6, 0, 0) };

            // --- Log Controls Panel with Enhanced Readability ---
            _logPanel = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(10, 8, 10, 8) };
            
            _lblLog = new Label { Text = "📋 LOGS", Left = 10, Top = 14, AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };

            // When the form is first shown, optionally auto-start the update cycle based on configuration
            this.Shown += (s, e) =>
            {
                try
                {
                    var cfg = ConfigManager.LoadConfig();
                    if (cfg.AutoStartCycle)
                    {
                        Logger.Log("AutoStartCycle enabled in config; starting update cycle.");
                        StartClicked(_startBtn, _stopBtn);
                    }
                    
                    // Start NAAD listener if AlertReady is enabled
                    Logger.Log($"AlertReady config: Enabled={cfg.AlertReady?.Enabled}, FeedUrls={cfg.AlertReady?.FeedUrls?.Count ?? 0}", Logger.LogLevel.Info);
                    if (cfg.AlertReady?.Enabled == true)
                    {
                        StartNaadListener(cfg);
                    }
                    else
                    {
                        Logger.Log("AlertReady is not enabled in config.", Logger.LogLevel.Info);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to auto-start: {ex.Message}", Logger.LogLevel.Error);
                }
            };
            
            _cmbFilter = new ComboBox { Left = 90, Top = 11, Width = 110, Height = 28, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5F, FontStyle.Regular) };
            _cmbFilter.Items.AddRange(new object[] { "All", "Errors", "Warnings", "Info" });
            _cmbFilter.SelectedIndex = 0;
            _cmbFilter.SelectedIndexChanged += (s, e) => RefreshLogView();

            _cmbVerbosity = new ComboBox { Left = 210, Top = 11, Width = 105, Height = 28, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5F, FontStyle.Regular) };
            _cmbVerbosity.Items.AddRange(new object[] { "Verbose", "Normal", "Minimal" });
            _cmbVerbosity.SelectedIndex = 1; // Normal
            _cmbVerbosity.SelectedIndexChanged += (s, e) => RefreshLogView();

            _chkCompact = new CheckBox { Left = 325, Top = 13, Width = 95, Text = "Compact", Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), FlatStyle = FlatStyle.Flat };
            _chkCompact.CheckedChanged += (s, e) => RefreshLogView();

            _txtSearch = new TextBox { Left = 430, Top = 12, Width = 280, Height = 26, Font = new Font("Segoe UI", 9.5F), BorderStyle = BorderStyle.FixedSingle };
            _txtSearch.PlaceholderText = "🔍 Search logs...";
            _txtSearch.TextChanged += (s, e) => RefreshLogView();

            _clearLogBtn = CreateStyledButton("Clear", 720, 10, 75, 30, Color.Gray, Color.White);
            _clearLogBtn.Click += (s, e) => 
            {
                lock (_logBuffer)
                {
                    _logBuffer.Clear();
                }
                RefreshLogView();
            };

            _logPanel.Controls.Add(_lblLog);
            _logPanel.Controls.Add(_cmbFilter);
            _logPanel.Controls.Add(_cmbVerbosity);
            _logPanel.Controls.Add(_chkCompact);
            _logPanel.Controls.Add(_txtSearch);
            _logPanel.Controls.Add(_clearLogBtn);

            _startBtn.Click += (s, e) => StartClicked(_startBtn, _stopBtn);
            _stopBtn.Click += (s, e) => StopClicked(_startBtn, _stopBtn);
            _openOutputBtn.Click += (s, e) => OpenOutputDirectory();
            _clearDirBtn.Click += (s, e) => ClearOutputDirectory();
            _videoBtn.Click += (s, e) => VideoClicked();
            _fetchBtn.Click += (s, e) => FetchClicked(_fetchBtn);
            _stillBtn.Click += (s, e) => StillClicked(_stillBtn);
            _cancelBtn.Click += (s, e) => CancelOperationsClicked();

            // Subscribe to only the leveled event and receive the explicit LogLevel (fixes coloring detection)
            Logger.MessageLoggedWithLevel += (text, level) => OnMessageLogged(text, level);
            // Allow external requests to trigger a log archival (keeps UI responsive)
            Logger.ArchiveRequested += () => TryArchiveLogsIfNeededSafe();

            // Subscribe to sleep updates from the background worker so we can show a countdown
            Program.SleepRemainingUpdated += (ts) => SetSleepRemaining(ts);

            // Subscribe to overall progress and video-specific progress
            Program.ProgressUpdated += (pct, msg) => OnProgramProgress(pct, msg);
            VideoGenerator.VideoProgressUpdated += (pct, msg) => OnVideoProgress(pct, msg);

            _settingsBtn.Click += (s, e) =>
            {
                using (var f = new SettingsForm())
                {
                    if (f.ShowDialog() == DialogResult.OK)
                    {
                        Logger.Log("Settings saved.");
                        var newConfig = ConfigManager.LoadConfig();
                        ApplyTheme(newConfig.Theme);
                    }
                }
            };

            _locationsBtn.Click += (s, e) =>
            {
                using (var f = new LocationsForm())
                {
                    if (f.ShowDialog() == DialogResult.OK)
                    {
                        Logger.Log("Locations updated.");
                    }
                }
            };

            _musicBtn.Click += (s, e) =>
            {
                using (var f = new MusicForm())
                {
                    if (f.ShowDialog() == DialogResult.OK)
                    {
                        Logger.Log("Music settings updated.");
                    }
                }
            };

            _aboutBtn.Click += (s, e) =>
            {
                using (var f = new AboutDialog())
                {
                    f.ShowDialog();
                }
            };

            _galleryBtn.Click += (s, e) =>
            {
                var galleryForm = new GalleryForm();
                galleryForm.Show();
            };

            _testAlertBtn.Click += async (s, e) =>
            {
                await GenerateTestAlertAsync();
            };

            _topPanel = new Panel { Dock = DockStyle.Top, Height = 200, Padding = new Padding(5) };
            // Add group labels
            _topPanel.Controls.Add(_groupLabel1);
            _topPanel.Controls.Add(_groupLabel2);
            _topPanel.Controls.Add(_groupLabel3);
            _topPanel.Controls.Add(_groupLabel4);
            // Add buttons
            _topPanel.Controls.Add(_videoBtn);
            _topPanel.Controls.Add(_stillBtn);
            _topPanel.Controls.Add(_fetchBtn);
            _topPanel.Controls.Add(_stopBtn);
            _topPanel.Controls.Add(_cancelBtn);
            _topPanel.Controls.Add(_testAlertBtn);
            _topPanel.Controls.Add(_openOutputBtn);
            _topPanel.Controls.Add(_clearDirBtn);
            _topPanel.Controls.Add(_startBtn);
            _topPanel.Controls.Add(_locationsBtn);
            _topPanel.Controls.Add(_musicBtn);
            _topPanel.Controls.Add(_galleryBtn);
            _topPanel.Controls.Add(_settingsBtn);
            _topPanel.Controls.Add(_aboutBtn);
            // Add progress and status
            _topPanel.Controls.Add(_progressLabel);
            _topPanel.Controls.Add(_statusLabel2);
            _topPanel.Controls.Add(_progress);
            _topPanel.Controls.Add(_statusLabel);
            _topPanel.Controls.Add(_sleepLabel);
            _topPanel.Controls.Add(_naadPanel);
            // _lastFetchLabel moved to splitContainer.Panel1

            _splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            _weatherList = new ListView { Dock = DockStyle.Fill, View = View.Details, GridLines = true, FullRowSelect = true, Font = new Font("Segoe UI", 10F, FontStyle.Regular), BorderStyle = BorderStyle.None };
            _weatherList.Columns.Add("📍 Location", 300);
            _weatherList.Columns.Add("🌡 Temp", 90);
            _weatherList.Columns.Add("🤔 Feels Like", 100);
            _weatherList.Columns.Add("☁ Condition", 200);
            _weatherList.Columns.Add("💨 Wind", 160);
            _weatherList.Columns.Add("⚠ Alerts", 400);
            _weatherList.DoubleClick += WeatherList_DoubleClick;

            _splitContainer.Panel1.Controls.Add(_weatherList);
            _splitContainer.Panel1.Controls.Add(_lastFetchLabel);
            // Docking order is reverse of Z-order. Send label to back so it is docked first (Top), 
            // then list fills the remaining space.
            _lastFetchLabel.SendToBack();
            _weatherList.BringToFront();
            
            // Initialize TabControl
            _tabControl = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9F) };

            // Logs Tab
            _logTab = new TabPage("📋 Logs");
            _logTab.Controls.Add(_logPanel);
            _logTab.Controls.Add(_logBox);
            _logBox.BringToFront(); 

            _tabControl.TabPages.Add(_logTab);

            _splitContainer.Panel2.Controls.Add(_tabControl);
            
            // Make console smaller (Panel2 is bottom)
            _splitContainer.SplitterDistance = 280;
            _splitContainer.SplitterWidth = 6;

            this.Controls.Add(_splitContainer);
            this.Controls.Add(_topPanel);

            Program.WeatherDataFetched += OnWeatherDataFetched;
            Program.AlertsFetched += OnAlertsFetched;

            var cfg = ConfigManager.LoadConfig();
            ApplyTheme(cfg.Theme);
            
            // Setup NotifyIcon for minimize to tray
            InitializeNotifyIcon();
            this.Resize += MainForm_Resize;
            this.FormClosing += MainForm_FormClosing;
        }

        private void ApplyTheme(string? themeName)
        {
            themeName ??= "Blue";
            Color primaryColor, secondaryColor, accentColor, successColor, warningColor, dangerColor;
            Color lightTextColor, headerTextColor, labelTextColor, bgColor, buttonColor;
            Color coloredBtnText, neutralBtnText, warningBtnText;

            switch (themeName.ToLowerInvariant())
            {
                case "light":
                    primaryColor = ColorTranslator.FromHtml("#F8F9FA");
                    secondaryColor = ColorTranslator.FromHtml("#FFFFFF");
                    accentColor = ColorTranslator.FromHtml("#0D6EFD"); // Bootstrap Primary Blue
                    successColor = ColorTranslator.FromHtml("#198754"); // Bootstrap Success Green
                    warningColor = ColorTranslator.FromHtml("#FFC107"); // Bootstrap Warning Yellow
                    dangerColor = ColorTranslator.FromHtml("#DC3545"); // Bootstrap Danger Red
                    
                    lightTextColor = ColorTranslator.FromHtml("#212529"); // Dark text for light bg
                    headerTextColor = ColorTranslator.FromHtml("#212529");
                    labelTextColor = ColorTranslator.FromHtml("#495057");
                    bgColor = ColorTranslator.FromHtml("#FFFFFF"); // Log box bg
                    buttonColor = ColorTranslator.FromHtml("#DEE2E6"); // Light gray button
                    
                    coloredBtnText = Color.White;
                    neutralBtnText = Color.Black;
                    warningBtnText = Color.Black; // Black on Yellow
                    break;

                case "dark":
                    primaryColor = ColorTranslator.FromHtml("#121212");
                    secondaryColor = ColorTranslator.FromHtml("#1E1E1E");
                    accentColor = ColorTranslator.FromHtml("#3498DB");
                    successColor = ColorTranslator.FromHtml("#27AE60");
                    warningColor = ColorTranslator.FromHtml("#F39C12");
                    dangerColor = ColorTranslator.FromHtml("#E74C3C");
                    
                    lightTextColor = ColorTranslator.FromHtml("#E0E0E0");
                    headerTextColor = ColorTranslator.FromHtml("#FFFFFF");
                    labelTextColor = ColorTranslator.FromHtml("#B0B0B0");
                    bgColor = ColorTranslator.FromHtml("#1E1E1E");
                    buttonColor = ColorTranslator.FromHtml("#333333"); // Lighter than bg
                    
                    coloredBtnText = Color.White;
                    neutralBtnText = Color.White;
                    warningBtnText = Color.Black;
                    break;

                case "green":
                    primaryColor = ColorTranslator.FromHtml("#102018"); // Very dark green
                    secondaryColor = ColorTranslator.FromHtml("#1B3A28");
                    accentColor = ColorTranslator.FromHtml("#2E8B57"); // SeaGreen
                    successColor = ColorTranslator.FromHtml("#27AE60");
                    warningColor = ColorTranslator.FromHtml("#F39C12");
                    dangerColor = ColorTranslator.FromHtml("#E74C3C");
                    
                    lightTextColor = ColorTranslator.FromHtml("#E8F8F1");
                    headerTextColor = ColorTranslator.FromHtml("#FFFFFF");
                    labelTextColor = ColorTranslator.FromHtml("#A0C0B0");
                    bgColor = ColorTranslator.FromHtml("#0A1510");
                    buttonColor = ColorTranslator.FromHtml("#2D5A40"); // Distinct from bg
                    
                    coloredBtnText = Color.White;
                    neutralBtnText = Color.White;
                    warningBtnText = Color.Black;
                    break;

                default: // blue
                    primaryColor = ColorTranslator.FromHtml("#2C3E50");
                    secondaryColor = ColorTranslator.FromHtml("#34495E");
                    accentColor = ColorTranslator.FromHtml("#3498DB");
                    successColor = ColorTranslator.FromHtml("#27AE60");
                    warningColor = ColorTranslator.FromHtml("#F39C12");
                    dangerColor = ColorTranslator.FromHtml("#E74C3C");
                    
                    lightTextColor = ColorTranslator.FromHtml("#ECF0F1");
                    headerTextColor = ColorTranslator.FromHtml("#FFFFFF");
                    labelTextColor = ColorTranslator.FromHtml("#BDC3C7");
                    bgColor = ColorTranslator.FromHtml("#233140"); // Darker blue for logs
                    buttonColor = ColorTranslator.FromHtml("#4E6781"); // Lighter blue-grey
                    
                    coloredBtnText = Color.White;
                    neutralBtnText = Color.White;
                    warningBtnText = Color.Black;
                    break;
            }

            // Update class-level theme colors
            _themeSuccessColor = successColor;
            _themeDangerColor = dangerColor;
            _themeWarningColor = warningColor;
            _themeAccentColor = accentColor;
            _themeTextColor = headerTextColor;

            this.BackColor = primaryColor;
            if (_logBox != null) { _logBox.BackColor = bgColor; _logBox.ForeColor = lightTextColor; }
            if (_topPanel != null) _topPanel.BackColor = primaryColor;
            if (_logPanel != null) _logPanel.BackColor = primaryColor;
            if (_splitContainer != null) { _splitContainer.BackColor = primaryColor; _splitContainer.Panel1.BackColor = secondaryColor; _splitContainer.Panel2.BackColor = primaryColor; }
            if (_weatherList != null) { _weatherList.BackColor = secondaryColor; _weatherList.ForeColor = headerTextColor; }
            if (_lastFetchLabel != null) { _lastFetchLabel.BackColor = secondaryColor; _lastFetchLabel.ForeColor = headerTextColor; }
            
            // TabControl & Tabs
            if (_tabControl != null)
            {
                // Note: Standard TabControl doesn't support full coloring without OwnerDraw, 
                // but we can set the background of the pages.
                foreach (TabPage page in _tabControl.TabPages)
                {
                    page.BackColor = primaryColor;
                    page.ForeColor = headerTextColor;
                }
            }

            // Labels
            void SetLabel(Label? l, Color c) { if (l != null) l.ForeColor = c; }
            SetLabel(_groupLabel1, labelTextColor);
            SetLabel(_groupLabel2, labelTextColor);
            SetLabel(_groupLabel3, labelTextColor);
            SetLabel(_groupLabel4, labelTextColor);
            SetLabel(_progressLabel, labelTextColor);
            SetLabel(_statusLabel2, labelTextColor);
            SetLabel(_lblLog, headerTextColor);
            SetLabel(_sleepLabel, labelTextColor);
            if (_statusLabel != null) _statusLabel.ForeColor = successColor; // Idle is success color usually

            // Buttons
            void SetBtn(Button? b, Color bg, Color fg) { 
                if (b != null) { 
                    b.BackColor = bg; 
                    b.ForeColor = fg; 
                    b.FlatAppearance.BorderColor = ControlPaint.Light(bg, 0.2f);
                    b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
                    b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(bg, 0.15f);
                } 
            }
            SetBtn(_startBtn, successColor, coloredBtnText);
            SetBtn(_stopBtn, dangerColor, coloredBtnText);
            SetBtn(_fetchBtn, accentColor, coloredBtnText);
            SetBtn(_stillBtn, accentColor, coloredBtnText);
            SetBtn(_videoBtn, accentColor, coloredBtnText);
            SetBtn(_openOutputBtn, buttonColor, neutralBtnText);
            SetBtn(_clearDirBtn, warningColor, warningBtnText);
            SetBtn(_locationsBtn, buttonColor, neutralBtnText);
            SetBtn(_musicBtn, buttonColor, neutralBtnText);
            SetBtn(_galleryBtn, buttonColor, neutralBtnText);
            SetBtn(_settingsBtn, buttonColor, neutralBtnText);
            SetBtn(_aboutBtn, buttonColor, neutralBtnText);
            SetBtn(_clearLogBtn, dangerColor, coloredBtnText);

            // Combos & Inputs
            void SetCombo(ComboBox? c) { if (c != null) { c.BackColor = buttonColor; c.ForeColor = neutralBtnText; } }
            SetCombo(_cmbFilter);
            SetCombo(_cmbVerbosity);
            if (_chkCompact != null) _chkCompact.ForeColor = headerTextColor;
            if (_txtSearch != null) { _txtSearch.BackColor = buttonColor; _txtSearch.ForeColor = neutralBtnText; }
            if (_progress != null) _progress.ForeColor = headerTextColor;
        
        }

        private Button CreateStyledButton(string text, int left, int top, int width, int height, Color backColor, Color foreColor)
        {
            var btn = new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                BackColor = backColor,
                ForeColor = foreColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = false
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.BorderColor = ControlPaint.Light(backColor, 0.2f);
            btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor, 0.15f);
            btn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor, 0.15f);
            return btn;
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Text = "WSG - WeatherStillGenerator",
                Visible = false
            };

            // Use application icon or a default system icon
            try
            {
                _notifyIcon.Icon = this.Icon ?? SystemIcons.Application;
            }
            catch
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }

            // Create context menu for the tray icon
            var contextMenu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("Open", null, (s, e) => RestoreFromTray());
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => Application.Exit());
            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);
            _notifyIcon.ContextMenuStrip = contextMenu;

            // Double-click to restore
            _notifyIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private void MainForm_Resize(object? sender, EventArgs e)
        {
            var cfg = ConfigManager.LoadConfig();
            if (cfg.MinimizeToTray && this.WindowState == FormWindowState.Minimized)
            {
                MinimizeToTray();
            }
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            var cfg = ConfigManager.LoadConfig();
            // If MinimizeToTrayOnClose is enabled and the user clicked the X button (not from Application.Exit)
            if (cfg.MinimizeToTrayOnClose && e.CloseReason == CloseReason.UserClosing && !_isMinimizedToTray)
            {
                e.Cancel = true;
                MinimizeToTray();
            }
        }

        private void MinimizeToTray()
        {
            this.Hide();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = true;
                _isMinimizedToTray = true;
            }
        }

        private void RestoreFromTray()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _isMinimizedToTray = false;
            }
            this.Activate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopNaadListener();
                _notifyIcon?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void OnAlertsFetched(System.Collections.Generic.List<AlertEntry> alerts)
        {
            if (_weatherList == null) return;
            if (_weatherList.InvokeRequired)
            {
                _weatherList.BeginInvoke(new Action(() => OnAlertsFetched(alerts)));
                return;
            }

            // Cache the alerts for detail view
            _cachedAlerts = alerts;

            // Clear previous alerts in the list
            foreach (ListViewItem item in _weatherList.Items)
            {
                // Ensure we have enough subitems
                while (item.SubItems.Count < 6) item.SubItems.Add("");
                item.SubItems[5].Text = "No alert";
                item.SubItems[5].BackColor = Color.Transparent;
                item.SubItems[5].ForeColor = Color.Black;
            }

            // Map alerts to locations
            foreach (var alert in alerts)
            {
                foreach (ListViewItem item in _weatherList.Items)
                {
                    // Accent-insensitive and case-insensitive match
                    // Also check if either string contains the other (for "Quebec" vs "Quebec City")
                    string normalizedItem = NormalizeForComparison(item.Text);
                    string normalizedAlert = NormalizeForComparison(alert.City);

                    // Don't attempt substring matches if either side is empty - empty string is contained in all strings.
                    if (string.IsNullOrEmpty(normalizedItem) || string.IsNullOrEmpty(normalizedAlert))
                    {
                        continue;
                    }
                    
                    bool isMatch = normalizedItem == normalizedAlert ||
                                   normalizedItem.Contains(normalizedAlert) ||
                                   normalizedAlert.Contains(normalizedItem);
                    
                    if (isMatch)
                    {
                        string existing = item.SubItems[5].Text;
                        string newAlert = $"{alert.Type}: {alert.Title}";
                        
                        if (existing == "No alert")
                        {
                            item.SubItems[5].Text = newAlert;
                        }
                        else
                        {
                            item.SubItems[5].Text = existing + "; " + newAlert;
                        }
                        
                        // Color coding based on severity
                        if (alert.SeverityColor.Equals("Red", StringComparison.OrdinalIgnoreCase))
                        {
                            item.SubItems[5].BackColor = Color.Red;
                            item.SubItems[5].ForeColor = Color.White;
                        }
                        else if (alert.SeverityColor.Equals("Yellow", StringComparison.OrdinalIgnoreCase) && item.SubItems[5].BackColor != Color.Red)
                        {
                            item.SubItems[5].BackColor = Color.Yellow;
                            item.SubItems[5].ForeColor = Color.Black;
                        }
                    }
                }
            }
        }

        // Helper method to normalize strings for accent-insensitive comparison
        private static string NormalizeForComparison(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            
            // Remove accents and convert to lowercase
            var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var c in normalized)
            {
                var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();
        }

        private void SetSleepRemaining(TimeSpan ts)
        {
            if (_sleepLabel == null) return;
            if (_sleepLabel.InvokeRequired)
            {
                _sleepLabel.BeginInvoke(new Action(() => SetSleepRemaining(ts)));
                return;
            }

            if (ts == TimeSpan.Zero)
                _sleepLabel.Text = string.Empty;
            else
                _sleepLabel.Text = $"Next run in {ts.ToString(@"hh\:mm\:ss")}";
        }

        private void AppendLog(string text)
        {
            var rich = this.Controls.Find("logBox", true);
            if (rich.Length == 1 && rich[0] is RichTextBox rtb)
            {
                if (rtb.InvokeRequired)
                {
                    rtb.BeginInvoke(new Action(() => AppendLog(text)));
                    return;
                }

                // Use colorized append to improve readability and highlight search matches (assume Info-level for legacy/unnamed messages)
                AppendColoredLine(rtb, text, _txtSearch?.Text ?? string.Empty, Logger.LogLevel.Info);
            }
        }

        private void StartClicked(Button startBtn, Button stopBtn)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            startBtn.Enabled = false;
            stopBtn.Enabled = true;
            // Clear any previous sleep indicator when starting
            SetSleepRemaining(TimeSpan.Zero);

            Task.Run(() => Program.RunAsync(_cts.Token));
            Logger.Log("Started background worker.");
        }

        private void StopClicked(Button startBtn, Button stopBtn)
        {
            if (_cts == null) return;
            _cts.Cancel();
            _cts = null;
            startBtn.Enabled = true;
            stopBtn.Enabled = false;
            // Clear any sleep countdown when stopped
            SetSleepRemaining(TimeSpan.Zero);
            Logger.Log("Stop requested. Background worker will exit shortly.");
        }

        private void VideoClicked()
        {
            Task.Run(() =>
            {
                try
                {
                    var config = ConfigManager.LoadConfig();
                    var imageDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), config.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                    var videoDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), config.Video?.OutputDirectory ?? config.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                    if (!System.IO.Directory.Exists(videoDir)) System.IO.Directory.CreateDirectory(videoDir);
                    if (!System.IO.Directory.Exists(imageDir)) System.IO.Directory.CreateDirectory(imageDir);
                    var outputName = config.Video?.OutputFileName ?? "slideshow_v3.mp4";
                    var container = (config.Video?.Container ?? "mp4").Trim().Trim('.');
                    var outputPath = System.IO.Path.Combine(videoDir, System.IO.Path.ChangeExtension(outputName, container));

                    var videoGenerator = new VideoGenerator(imageDir)
                    {
                        WorkingDirectory = videoDir,
                        ImageFolder = imageDir,
                        OutputFile = outputPath,
                        StaticDuration = config.Video?.StaticDurationSeconds ?? 8,
                        FadeDuration = config.Video?.FadeDurationSeconds ?? 0.5,
                        FrameRate = config.Video?.FrameRate ?? 30,
                        ResolutionMode = Enum.Parse<ResolutionMode>(config.Video?.ResolutionMode ?? "Mode1080p"),
                        EnableFadeTransitions = config.Video?.EnableFadeTransitions ?? false,
                        VideoCodec = config.Video?.VideoCodec ?? "libx264",
                        VideoBitrate = config.Video?.VideoBitrate ?? "4M",
                        Container = container,
                        FfmpegVerbose = config.Video?.VerboseFfmpeg ?? false,
                        ShowFfmpegOutputInGui = config.Video?.ShowFfmpegOutputInGui ?? true,
                        EnableHardwareEncoding = config.Video?.EnableHardwareEncoding ?? false,
                        UseCrfEncoding = config.Video?.UseCrfEncoding ?? true,
                        CrfValue = config.Video?.CrfValue ?? 23,
                        MaxBitrate = config.Video?.MaxBitrate,
                        BufferSize = config.Video?.BufferSize,
                        EncoderPreset = config.Video?.EncoderPreset ?? "medium",
                        UseTotalDuration = config.Video?.UseTotalDuration ?? false,
                        TotalDurationSeconds = config.Video?.TotalDurationSeconds ?? 60
                    };

                    // Load music from configuration (handles random/specific selection)
                    videoGenerator.LoadMusicFromConfig();

                    _operationCts?.Dispose();
                    _operationCts = new CancellationTokenSource();
                    _runningVideoGenerator = videoGenerator;
                    SetCancelState(true);

                    videoGenerator.GenerateVideo();
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ERROR] Manual video generation error: {ex.Message}", ConsoleColor.Red);
                }
                finally
                {
                    _runningVideoGenerator = null;
                    _operationCts?.Dispose();
                    _operationCts = null;
                    SetCancelState(false);
                }
            });
        }

        private void FetchClicked(Button fetchBtn)
        {
            fetchBtn.Enabled = false;
            _operationCts?.Dispose();
            _operationCts = new CancellationTokenSource();
            SetCancelState(true);
            Task.Run(async () => 
            {
                try
                {
                    await Program.FetchDataOnlyAsync(_operationCts.Token);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ERROR] Manual fetch error: {ex.Message}", ConsoleColor.Red);
                }
                finally
                {
                    if (fetchBtn.InvokeRequired)
                        fetchBtn.Invoke(new Action(() => fetchBtn.Enabled = true));
                    else
                        fetchBtn.Enabled = true;

                    _operationCts?.Dispose();
                    _operationCts = null;
                    SetCancelState(false);
                }
            });
        }

        private void StillClicked(Button stillBtn)
        {
            stillBtn.Enabled = false;
            _operationCts?.Dispose();
            _operationCts = new CancellationTokenSource();
            SetCancelState(true);
            Task.Run(async () => 
            {
                try
                {
                    await Program.GenerateStillsOnlyAsync(_operationCts.Token);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ERROR] Manual still generation error: {ex.Message}", ConsoleColor.Red);
                }
                finally
                {
                    if (stillBtn.InvokeRequired)
                        stillBtn.Invoke(new Action(() => stillBtn.Enabled = true));
                    else
                        stillBtn.Enabled = true;

                    _operationCts?.Dispose();
                    _operationCts = null;
                    SetCancelState(false);
                }
            });
        }

        private void CancelOperationsClicked()
        {
            Logger.Log("Cancel requested by user.");

            try { _operationCts?.Cancel(); } catch { }
            try { _runningVideoGenerator?.Cancel(); } catch { }

            try { Services.ExternalProcessManager.CancelAll(); } catch { }

            SetCancelState(false);
        }

        private void SetCancelState(bool enabled)
        {
            if (_cancelBtn == null) return;
            if (_cancelBtn.InvokeRequired)
            {
                _cancelBtn.BeginInvoke(new Action(() => SetCancelState(enabled)));
                return;
            }
            _cancelBtn.Enabled = enabled;
        }

        private void OnMessageLogged(string text, Logger.LogLevel level)
        {
            // Keep a copy of everything with explicit level, then reapply filters for the view
            lock (_logBuffer)
            {
                _logBuffer.Add((text, level));
                // Keep the buffer bounded to avoid runaway memory usage
                if (_logBuffer.Count > 5000) _logBuffer.RemoveRange(0, _logBuffer.Count - 5000);
            }

            // UI updates must be on UI thread
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => OnMessageLogged(text, level)));
                return;
            }

            var trimmed = text.Trim();

            // If messages indicate ffmpeg running/done, update status/progress (content-based features remain)
            if (trimmed.Contains("[RUNNING]"))
            {
                // Video/FFmpeg started
                _videoActive = true;
                SetStatus("Running...");
                SetProgressMarquee(true);
            }
            else if (TryExtractProgress(trimmed, out var pct))
            {
                // Legacy ffmpeg condensed progress lines - map to video progress range
                OnVideoProgress(pct, $"Encoding... {pct:0}%");
            }
            else if (trimmed.Contains("[DONE]") || trimmed.Contains("[FAIL]") || trimmed.Contains("Video saved") || trimmed.Contains("Video generation completed"))
            {
                // Video finished (success or fail)
                _videoActive = false;
                SetStatus("Idle");
                SetProgressMarquee(false);

                // If video save is present, push progress to 100
                if (trimmed.Contains("Video saved") || trimmed.Contains("Video generation completed") || trimmed.Contains("[DONE]"))
                {
                    SetOverallProgress(100.0, "Video complete");
                }
            }

            // Optimization: If compact mode is OFF, append directly instead of full refresh
            if (_chkCompact?.Checked == false)
            {
                var richArr = this.Controls.Find("logBox", true);
                if (richArr.Length == 1 && richArr[0] is RichTextBox rtb)
                {
                    string filter = _cmbFilter?.SelectedItem as string ?? "All";
                    string search = _txtSearch?.Text ?? string.Empty;
                    string verbosity = _cmbVerbosity?.SelectedItem as string ?? "Normal";

                    if (PassesFilter(text, filter, search, level, verbosity))
                    {
                        AppendColoredLine(rtb, text, search, level);
                        // Periodically refresh / archive to prune old lines from the view if it gets too long
                        if (rtb.Lines.Length > LogArchiveThreshold)
                        {
                            TryArchiveLogsIfNeeded(rtb);
                        }
                    }
                }
            }
            else
            {
                RefreshLogView();
            }
        }

        private void RefreshLogView()
        {
            var richArr = this.Controls.Find("logBox", true);
            if (richArr.Length != 1 || !(richArr[0] is RichTextBox rtb)) return;

            if (rtb.InvokeRequired)
            {
                rtb.BeginInvoke(new Action(RefreshLogView));
                return;
            }

            string filter = _cmbFilter?.SelectedItem as string ?? "All";
            string search = _txtSearch?.Text ?? string.Empty;
            string verbosity = _cmbVerbosity?.SelectedItem as string ?? "Normal";

            rtb.Clear();
            lock (_logBuffer)
            {
                if (verbosity == "Minimal")
                {
                    // Gather only important entries and show only the most recent MinimalVisibleCount to keep view friendly
                    var important = _logBuffer.Where(e => IsImportantForMinimal(e.Text, e.Level)).ToList();
                    int totalHidden = _logBuffer.Count - important.Count;

                    var toShow = important.Skip(Math.Max(0, important.Count - MinimalVisibleCount)).ToList();

                    // If compact mode is enabled, collapse consecutive duplicates within the shown subset
                    if (_chkCompact?.Checked == true)
                    {
                        (string Text, Logger.LogLevel Level)? prev = null;
                        int count = 0;
                        foreach (var entry in toShow)
                        {
                            if (prev == null || prev.Value.Text != entry.Text || prev.Value.Level != entry.Level)
                            {
                                if (prev != null) AppendCollapsedLine(rtb, prev.Value.Text, search, prev.Value.Level, count);
                                prev = entry;
                                count = 1;
                            }
                            else count++;
                        }

                        if (prev != null) AppendCollapsedLine(rtb, prev.Value.Text, search, prev.Value.Level, count);
                    }
                    else
                    {
                        foreach (var entry in toShow)
                        {
                            AppendColoredLine(rtb, entry.Text, search, entry.Level);
                        }
                    }

                    if (totalHidden > 0) AppendHiddenSummary(rtb, totalHidden);
                }
                else if (_chkCompact?.Checked == true)
                {
                    // Collapse consecutive identical entries - show "(Nx)" for repeats
                    (string Text, Logger.LogLevel Level)? prev = null;
                    int count = 0;
                    int hiddenCount = 0;
                    foreach (var entry in _logBuffer)
                    {
                        if (!PassesFilter(entry.Text, filter, search, entry.Level, verbosity))
                        {
                            hiddenCount++;
                            continue;
                        }

                        // If we had hidden lines immediately before a visible one, emit a summary
                        if (hiddenCount > 0)
                        {
                            AppendHiddenSummary(rtb, hiddenCount);
                            hiddenCount = 0;
                        }

                        if (prev == null || prev.Value.Text != entry.Text || prev.Value.Level != entry.Level)
                        {
                            if (prev != null)
                            {
                                AppendCollapsedLine(rtb, prev.Value.Text, search, prev.Value.Level, count);
                            }

                            prev = entry;
                            count = 1;
                        }
                        else
                        {
                            count++;
                        }
                    }

                    if (hiddenCount > 0) AppendHiddenSummary(rtb, hiddenCount);
                    if (prev != null) AppendCollapsedLine(rtb, prev.Value.Text, search, prev.Value.Level, count);
                }
                else
                {
                    int hiddenCount = 0;
                    foreach (var entry in _logBuffer)
                    {
                        if (!PassesFilter(entry.Text, filter, search, entry.Level, verbosity))
                        {
                            hiddenCount++;
                            continue;
                        }

                        if (hiddenCount > 0)
                        {
                            AppendHiddenSummary(rtb, hiddenCount);
                            hiddenCount = 0;
                        }

                        AppendColoredLine(rtb, entry.Text, search, entry.Level);
                    }

                    if (hiddenCount > 0) AppendHiddenSummary(rtb, hiddenCount);
                }
            }

            rtb.ScrollToCaret();
        }

        private bool PassesFilter(string line, string filter, string search, Logger.LogLevel level, string verbosity)
        {
            if (!string.IsNullOrEmpty(search) && !line.Contains(search, StringComparison.OrdinalIgnoreCase)) return false;

            var lower = line.ToLowerInvariant();

            // Verbosity controls overall noise
            if (verbosity == "Minimal")
            {
                // Minimal: only errors, warnings, and important status lines
                if (level == Logger.LogLevel.Error || level == Logger.LogLevel.Warning) return true;
                if (lower.Contains("[running]") || lower.Contains("video") || lower.Contains("encoding") || lower.Contains("[done]") || lower.Contains("saved") || lower.Contains("completed") || lower.Contains("fail")) return true;
                return false;
            }

            if (verbosity == "Normal")
            {
                // Normal: hide Debug-level messages
                if (level == Logger.LogLevel.Debug) return false;
            }

            // Apply filter on the remaining messages
            return filter switch
            {
                "All" => true,
                "Errors" => level == Logger.LogLevel.Error || lower.Contains("[error]") || lower.Contains("failed") || lower.Contains("✗") || lower.Contains(" x "),
                "Warnings" => level == Logger.LogLevel.Warning || lower.Contains("[warn]") || lower.Contains("warning"),
                "Info" => level == Logger.LogLevel.Info || level == Logger.LogLevel.Debug || (!lower.Contains("[error]") && !lower.Contains("✗") && !lower.Contains(" x ")),
                _ => true
            };
        }

        // Append a single line to the RichTextBox with color, optional bolding for level tokens, and search highlighting
        private void AppendColoredLine(RichTextBox rtb, string line, string search, Logger.LogLevel level)
        {
            if (rtb == null) return;

            int start = rtb.TextLength;
            rtb.AppendText(line);
            int length = line.Length;

            var lower = line.ToLowerInvariant();
            Color color;
            FontStyle style = FontStyle.Regular;

            // Prefer explicit level when deciding color; allow content-based overrides for video/running and success
            switch (level)
            {
                case Logger.LogLevel.Error: color = Color.FromArgb(255, 100, 100); style = FontStyle.Bold; break;
                case Logger.LogLevel.Warning: color = Color.FromArgb(255, 215, 0); break;
                case Logger.LogLevel.Debug: color = Color.Gray; break;
                default: color = Color.WhiteSmoke; break;
            }

            if (lower.Contains("saved") || lower.Contains("completed") || lower.Contains("success")) { color = Color.LightGreen; style = FontStyle.Bold; }
            else if (lower.Contains("[running]") || lower.Contains("video") || lower.Contains("encoding") || lower.Contains("[done]")) color = Color.LightSkyBlue;

            // Apply color and font
            rtb.Select(start, length);
            rtb.SelectionColor = color;
            rtb.SelectionFont = new Font(rtb.Font, style);

            // Bold level token when present
            var levelTokens = new string[] { "[error]", "[warn]", "[running]", "[done]", "[fail]" };
            foreach (var token in levelTokens)
            {
                int idx = lower.IndexOf(token);
                if (idx >= 0)
                {
                    rtb.Select(start + idx, token.Length);
                    rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
                }
            }

            // Highlight search matches
            if (!string.IsNullOrEmpty(search))
            {
                int offset = 0;
                var comparison = StringComparison.OrdinalIgnoreCase;
                while (true)
                {
                    int pos = line.IndexOf(search, offset, comparison);
                    if (pos < 0) break;
                    rtb.Select(start + pos, search.Length);
                    rtb.SelectionBackColor = Color.Yellow;
                    rtb.SelectionColor = Color.Black;
                    offset = pos + search.Length;
                }
            }

            // Reset selection and scroll
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.ScrollToCaret();
        }

        // Helper to append a collapsed entry (with repeat count)
        private void AppendCollapsedLine(RichTextBox rtb, string line, string search, Logger.LogLevel level, int count)
        {
            if (count <= 1)
            {
                AppendColoredLine(rtb, line, search, level);
                return;
            }

            var display = line.TrimEnd();
            display += $"  ({count}x)" + Environment.NewLine;
            AppendColoredLine(rtb, display, search, level);
        }

        // Small summary line used when many lines are hidden in Minimal verbosity
        private void AppendHiddenSummary(RichTextBox rtb, int hiddenCount)
        {
            if (hiddenCount <= 0) return;
            var msg = $"... {hiddenCount} lines hidden ..." + Environment.NewLine;
            int start = rtb.TextLength;
            rtb.AppendText(msg);
            rtb.Select(start, msg.Length);
            rtb.SelectionColor = Color.DarkGray;
            rtb.SelectionFont = new Font(rtb.Font, FontStyle.Italic);
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.ScrollToCaret();
        }

        // Minimal-mode importance test: only errors/warnings and a small set of high-level status lines
        private bool IsImportantForMinimal(string line, Logger.LogLevel level)
        {
            if (level == Logger.LogLevel.Error || level == Logger.LogLevel.Warning) return true;

            var lower = line.ToLowerInvariant();

            // High-level indicators we consider important for casual users
            if (lower.Contains("[done]") || lower.Contains("video saved") || lower.Contains("video generation completed") || lower.Contains("completed") || lower.Contains("saved")) return true;
            if (lower.Contains("started background worker") || lower.Contains("stop requested") || lower.Contains("settings saved")) return true;

            // Exclude noisy progress lines (percentages and ffmpeg '[MAIN]' lines)
            if (lower.Contains("%") || lower.StartsWith("[main]")) return false;

            return false;
        }

        // Safely invoked by the background timer to attempt archival when needed (switch to UI thread)
        private void TryArchiveLogsIfNeededSafe()
        {
            if (this.IsDisposed) return;
            var richArr = this.Controls.Find("logBox", true);
            if (richArr.Length != 1 || !(richArr[0] is RichTextBox rtb)) return;
            if (rtb.InvokeRequired)
            {
                rtb.BeginInvoke(new Action(() => TryArchiveLogsIfNeeded(rtb)));
            }
            else TryArchiveLogsIfNeeded(rtb);
        }

        // Check and archive older log entries (UI thread) if the RichTextBox exceeds threshold
        private void TryArchiveLogsIfNeeded(RichTextBox rtb)
        {
            try
            {
                if (rtb == null) return;
                if (rtb.Lines.Length <= LogArchiveThreshold) return;

                // Snapshot and steal older entries from buffer while holding the lock
                List<(string Text, Logger.LogLevel Level)> toArchive;
                lock (_logBuffer)
                {
                    if (_logBuffer.Count <= LogArchiveKeepRecent) return; // nothing to archive
                    int removeCount = Math.Max(0, _logBuffer.Count - LogArchiveKeepRecent);
                    toArchive = _logBuffer.Take(removeCount).ToList();
                    _logBuffer.RemoveRange(0, removeCount);
                }

                // Prepare the content to archive (concatenate text entries)
                var content = string.Concat(toArchive.Select(e => e.Text));
                int archivedLines = toArchive.Count;

                // Refresh the visible view to show only remaining recent entries
                RefreshLogView();

                // Add a short info entry to the live log and then background-archive the content
                Logger.Log($"[INFO] Archived {archivedLines} log lines to disk (logs/archived_logs.b64)", Logger.LogLevel.Info);
                Task.Run(() => ArchiveToFile(content, archivedLines));
            }
            catch (Exception ex)
            {
                // Don't throw on archival failures - just log a warning and continue
                Logger.Log($"[WARN] Log archive failed: {ex.Message}", Logger.LogLevel.Warning);
            }
        }

        // Append content directly to the archive file (plain text, human-readable)
        private void ArchiveToFile(string content, int count)
        {
            try
            {
                if (!Directory.Exists(LogArchiveFolder)) Directory.CreateDirectory(LogArchiveFolder);
                var header = $"=== ARCHIVE {DateTime.UtcNow:O} lines={count} ==={Environment.NewLine}";
                File.AppendAllText(LogArchiveFile, header + content + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Logger.Log($"[WARN] Failed to write log archive: {ex.Message}", Logger.LogLevel.Warning);
            }
        }


        private void SetProgressMarquee(bool marquee)
        {
            if (_progress == null) return;
            if (_progress.InvokeRequired)
            {
                _progress.BeginInvoke(new Action(() => SetProgressMarquee(marquee)));
                return;
            }

            if (marquee)
                _progress.StartMarquee();
            else
            {
                _progress.StopMarquee();
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = 0;
                _progress.Text = string.Empty;
            }
        }

        private void SetProgressValue(double pct)
        {
            if (_progress == null) return;
            if (_progress.InvokeRequired)
            {
                _progress.BeginInvoke(new Action(() => SetProgressValue(pct)));
                return;
            }

            _progress.Style = ProgressBarStyle.Continuous;
            var clamped = (int)Math.Max(0, Math.Min(100, Math.Round(pct)));
            _progress.Value = clamped;

            // Update overlay text inside the bar
            _progress.Text = $"{clamped}%";
            _progress.Invalidate();
        }

        private bool TryExtractProgress(string line, out double percent)
        {
            percent = 0;

            // We only care about the condensed ffmpeg progress lines like "[MAIN] [#####] 42%"
            if (!line.StartsWith("[MAIN]", StringComparison.OrdinalIgnoreCase)) return false;

            var percentIdx = line.LastIndexOf('%');
            if (percentIdx < 0 || percentIdx == 0) return false;

            var start = line.LastIndexOf(' ', percentIdx);
            if (start < 0 || start >= percentIdx) return false;

            var numberSpan = line.Substring(start + 1, percentIdx - start - 1);
            if (double.TryParse(numberSpan, out var value))
            {
                percent = Math.Max(0, Math.Min(100, value));
                return true;
            }

            return false;
        }

        // Called when Program reports broad progress (fetch/images/video start/complete)
        private void OnWeatherDataFetched(OpenMeteo.WeatherForecast?[] forecasts)
        {
            if (_weatherList == null) return;
            if (_weatherList.InvokeRequired)
            {
                _weatherList.BeginInvoke(new Action(() => OnWeatherDataFetched(forecasts)));
                return;
            }

            // Cache the forecasts for detail view
            _cachedForecasts = forecasts;

            if (_lastFetchLabel != null)
            {
                _lastFetchLabel.Text = $"Last fetch: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            }

            _weatherList.Items.Clear();
            var config = ConfigManager.LoadConfig();
            var locations = config.Locations?.GetLocationsArray() ?? new string[0];
            var client = new OpenMeteo.OpenMeteoClient();

            for (int i = 0; i < forecasts.Length; i++)
            {
                var f = forecasts[i];
                var locName = (i < locations.Length) ? locations[i] : $"Location {i}";

                if (f?.Current == null)
                {
                    var item = new ListViewItem(locName);
                    item.SubItems.Add("N/A");
                    item.SubItems.Add("N/A");
                    item.SubItems.Add("N/A");
                    item.SubItems.Add("N/A");
                    _weatherList.Items.Add(item);
                }
                else
                {
                    var item = new ListViewItem(locName);
                    item.SubItems.Add($"{f.Current.Temperature}{f.CurrentUnits?.Temperature ?? "°C"}");
                    item.SubItems.Add($"{f.Current.Apparent_temperature}{f.CurrentUnits?.Apparent_temperature ?? "°C"}");
                    
                    string condition = f.Current.Weathercode.HasValue 
                        ? client.WeathercodeToString(f.Current.Weathercode.Value) 
                        : "Unknown";
                    item.SubItems.Add(condition);

                    item.SubItems.Add($"{f.Current.Windspeed_10m}{f.CurrentUnits?.Windspeed_10m ?? "km/h"} {DegreesToCardinal(f.Current.Winddirection_10m)}");
                    _weatherList.Items.Add(item);
                }
            }
        }

        private void WeatherList_DoubleClick(object? sender, EventArgs e)
        {
            if (_weatherList == null || _weatherList.SelectedItems.Count == 0) return;
            
            int selectedIndex = _weatherList.SelectedIndices[0];
            var config = ConfigManager.LoadConfig();
            var locations = config.Locations?.GetLocationsArray() ?? new string[0];
            
            string locationName = (selectedIndex < locations.Length) ? locations[selectedIndex] : $"Location {selectedIndex}";
            
            // Get forecast for this location
            OpenMeteo.WeatherForecast? forecast = null;
            if (_cachedForecasts != null && selectedIndex < _cachedForecasts.Length)
            {
                forecast = _cachedForecasts[selectedIndex];
            }
            
            // Show the details form
            var detailsForm = new WeatherDetailsForm(locationName, forecast, _cachedAlerts ?? new System.Collections.Generic.List<AlertEntry>());
            detailsForm.ShowDialog(this);
        }

        private string DegreesToCardinal(double? degrees)
        {
            if (!degrees.HasValue) return "";
            string[] cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW", "N" };
            return cardinals[(int)Math.Round(((double)degrees % 360) / 45)];
        }

        private void OnProgramProgress(double pct, string status)
        {
            // If program reports a video start value, record mapping
            if (status != null && status.ToLowerInvariant().Contains("video"))
            {
                _videoActive = true;
                _videoBase = pct;
                _videoRange = Math.Max(0.0, 100.0 - _videoBase);
            }

            SetOverallProgress(pct, status);
        }

        // Called when ffmpeg/video reports a fine-grained percent (0-100)
        private void OnVideoProgress(double pct, string status)
        {
            // If a video phase mapping exists, map ffmpeg percent into overall percent
            if (_videoActive)
            {
                var overall = _videoBase + (pct / 100.0) * _videoRange;
                SetOverallProgress(overall, status);
            }
            else
            {
                // No mapping known; show raw percent
                SetOverallProgress(pct, status);
            }
        }

        private void SetOverallProgress(double pct, string status)
        {
            // Normalize
            var clamped = Math.Max(0.0, Math.Min(100.0, pct));
            SetProgressMarquee(false);
            SetProgressValue(clamped);
            SetStatus(status ?? string.Empty);

            // If we've reached 100 and not in video phase, clear video flag
            if (clamped >= 100.0)
            {
                _videoActive = false;
            }
        }

        private void SetStatus(string status)
        {
            if (_statusLabel == null) return;
            if (_statusLabel.InvokeRequired)
            {
                _statusLabel.BeginInvoke(new Action(() => SetStatus(status)));
                return;
            }

            // Gentle color coding for statuses
            if (status == null) status = string.Empty;
            var lower = status.ToLowerInvariant();
            if (lower.Contains("error") || lower.Contains("failed") || lower.Contains("fail")) _statusLabel.ForeColor = _themeDangerColor;
            else if (lower.Contains("encoding") || lower.Contains("video") || lower.Contains("running")) _statusLabel.ForeColor = _themeAccentColor;
            else _statusLabel.ForeColor = _themeTextColor;

            _statusLabel.Text = status;
        }

        private void OpenOutputDirectory()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                string path = config.ImageGeneration?.OutputDirectory ?? "WeatherImages";
                if (!System.IO.Path.IsPathRooted(path))
                {
                    path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path);
                }

                if (!System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(path);
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"Error opening output directory: {ex.Message}", Logger.LogLevel.Error);
                MessageBox.Show($"Could not open output directory: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ClearOutputDirectory()
        {
            try
            {
                var result = MessageBox.Show(
                    "This will delete all generated images and videos in the output directory. Are you sure?",
                    "Clear Output Directory",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.Yes)
                    return;

                var config = ConfigManager.LoadConfig();
                string imageDir = config.ImageGeneration?.OutputDirectory ?? "WeatherImages";
                if (!System.IO.Path.IsPathRooted(imageDir))
                {
                    imageDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), imageDir);
                }

                string videoDir = config.Video?.OutputDirectory ?? imageDir;
                if (!System.IO.Path.IsPathRooted(videoDir))
                {
                    videoDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), videoDir);
                }

                int deletedCount = 0;

                // Delete image files
                if (System.IO.Directory.Exists(imageDir))
                {
                    var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
                    foreach (var ext in imageExtensions)
                    {
                        foreach (var file in System.IO.Directory.GetFiles(imageDir, $"*{ext}"))
                        {
                            System.IO.File.Delete(file);
                            deletedCount++;
                        }
                    }
                }

                // Delete video files (if video directory is different from image directory)
                if (System.IO.Directory.Exists(videoDir) && !string.Equals(videoDir, imageDir, StringComparison.OrdinalIgnoreCase))
                {
                    var videoExtensions = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };
                    foreach (var ext in videoExtensions)
                    {
                        foreach (var file in System.IO.Directory.GetFiles(videoDir, $"*{ext}"))
                        {
                            System.IO.File.Delete(file);
                            deletedCount++;
                        }
                    }
                }
                else if (string.Equals(videoDir, imageDir, StringComparison.OrdinalIgnoreCase))
                {
                    // Same directory, delete video files
                    var videoExtensions = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };
                    foreach (var ext in videoExtensions)
                    {
                        foreach (var file in System.IO.Directory.GetFiles(videoDir, $"*{ext}"))
                        {
                            System.IO.File.Delete(file);
                            deletedCount++;
                        }
                    }
                }

                Logger.Log($"Deleted {deletedCount} file(s) from output directory.", Logger.LogLevel.Info);
                MessageBox.Show($"Successfully deleted {deletedCount} file(s).", "Clear Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error clearing output directory: {ex.Message}", Logger.LogLevel.Error);
                MessageBox.Show($"Could not clear output directory: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Generates a test emergency alert (AMBER Alert) for testing the EAS system.
        /// </summary>
        private async Task GenerateTestAlertAsync()
        {
            _testAlertBtn!.Enabled = false;
            _testAlertBtn.Text = "⏳ Generating...";

            try
            {
                await Task.Run(() =>
                {
                    Logger.Log("Generating test emergency alert...", Logger.LogLevel.Info);

                    // Create output directory
                    string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestAlerts");
                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    // Get current config for language
                    var cfg = ConfigManager.LoadConfig();
                    string language = cfg.AlertReady?.PreferredLanguage ?? "fr-CA";

                    // Generate test alert XML
                    string testAlertXml = EAS.TestAlertGenerator.GenerateAmberAlert(language);

                    // Parse the alert
                    var httpClient = new System.Net.Http.HttpClient();
                    var options = new EAS.AlertReadyOptions
                    {
                        Enabled = true,
                        ExcludeWeatherAlerts = true,
                        PreferredLanguage = language,
                        Jurisdictions = new List<string> { "QC", "ON", "CA" },
                        HighRiskOnly = false
                    };

                    var client = new EAS.AlertReadyClient(httpClient, options);
                    client.Log = (msg) => Logger.Log($"[AlertReady] {msg}", Logger.LogLevel.Debug);

                    // Use reflection to call the private ParseAlerts method
                    var parseMethod = client.GetType().GetMethod("ParseAlerts", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    var alerts = parseMethod?.Invoke(client, new object[] { testAlertXml, new List<string>() }) 
                        as List<AlertEntry>;

                    if (alerts != null && alerts.Count > 0)
                    {
                        Logger.Log($"Parsed {alerts.Count} test alert(s). Generating media...", Logger.LogLevel.Info);

                        var generatedFiles = EmergencyAlertGenerator.GenerateEmergencyAlerts(
                            alerts,
                            outputDir,
                            language
                        );

                        Logger.Log($"Generated {generatedFiles.Count} file(s) in TestAlerts folder.", Logger.LogLevel.Info);

                        // Open the output folder
                        if (generatedFiles.Count > 0)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = outputDir,
                                    UseShellExecute = true
                                });
                            }
                            catch { /* best-effort */ }
                        }
                    }
                    else
                    {
                        Logger.Log("Failed to parse test alert.", Logger.LogLevel.Warning);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"Test alert generation failed: {ex.Message}", Logger.LogLevel.Error);
                MessageBox.Show($"Failed to generate test alert: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _testAlertBtn!.Enabled = true;
                _testAlertBtn.Text = "🧪 Test Alert";
            }
        }

        private void StartNaadListener(AppSettings cfg)
        {
            if (_naadClient != null) return;

            try
            {
                Logger.Log("Starting NAAD/Alert Ready listener...", Logger.LogLevel.Info);

                var feedUrls = cfg.AlertReady?.FeedUrls ?? new List<string>
                {
                    "tcp://streaming1.naad-adna.pelmorex.com:8080",
                    "tcp://streaming2.naad-adna.pelmorex.com:8080"
                };

                Logger.Log($"NAAD Feed URLs: {string.Join(", ", feedUrls)}", Logger.LogLevel.Info);

                var httpClient = new System.Net.Http.HttpClient();
                var options = new AlertReadyOptions
                {
                    Enabled = true,
                    FeedUrls = feedUrls,
                    ExcludeWeatherAlerts = cfg.AlertReady?.ExcludeWeatherAlerts ?? true,
                    PreferredLanguage = cfg.AlertReady?.PreferredLanguage ?? "fr-CA",
                    Jurisdictions = cfg.AlertReady?.Jurisdictions ?? new List<string> { "QC" },
                    HighRiskOnly = cfg.AlertReady?.HighRiskOnly ?? false,
                    AreaFilters = cfg.AlertReady?.AreaFilters
                };

                _naadClient = new AlertReadyClient(httpClient, options);
                _naadClient.Log = (msg) => Logger.Log($"[NAAD] {msg}", Logger.LogLevel.Info);

                // Subscribe to events
                _naadClient.ConnectionStatusChanged += OnNaadConnectionChanged;
                _naadClient.HeartbeatReceived += OnNaadHeartbeat;
                _naadClient.AlertReceived += OnNaadAlertReceived;

                UpdateNaadConnectionStatus(NaadConnectionStatus.Connecting, "Connecting...");

                // Start TCP stream listeners
                _naadCts = new CancellationTokenSource();
                _naadClient.StartTcpStreams();

                Logger.Log("NAAD TCP stream listeners started.", Logger.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to start NAAD listener: {ex.Message}", Logger.LogLevel.Error);
                UpdateNaadConnectionStatus(NaadConnectionStatus.Disconnected, ex.Message);
            }
        }

        private void StopNaadListener()
        {
            if (_naadClient == null) return;

            try
            {
                _naadCts?.Cancel();
                _naadClient.Dispose();
                _naadClient = null;
                UpdateNaadConnectionStatus(NaadConnectionStatus.Disconnected, "Stopped");
                Logger.Log("NAAD listener stopped.", Logger.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error stopping NAAD listener: {ex.Message}", Logger.LogLevel.Warning);
            }
        }

        private void OnNaadConnectionChanged(object? sender, ConnectionStatusEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnNaadConnectionChanged(sender, e));
                return;
            }

            UpdateNaadConnectionStatus(e.Status, e.Message);
        }

        private void OnNaadHeartbeat(object? sender, HeartbeatEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnNaadHeartbeat(sender, e));
                return;
            }

            var localTime = e.Timestamp.ToLocalTime();
            _naadHeartbeatLabel!.Text = $"💓 {localTime:HH:mm:ss}";
            _naadHeartbeatLabel.ForeColor = _themeTextColor;
        }

        private void OnNaadAlertReceived(object? sender, AlertReceivedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnNaadAlertReceived(sender, e));
                return;
            }

            _naadAlertLabel!.Text = $"⚠ {e.TotalActiveAlerts} alert{(e.TotalActiveAlerts != 1 ? "s" : "")}";
            _naadAlertLabel.ForeColor = e.TotalActiveAlerts > 0 ? Color.OrangeRed : _themeTextColor;

            // Log the alert
            Logger.Log($"[NAAD] Alert received: {e.Alert?.Title}", Logger.LogLevel.Info);

            // Generate alert media automatically
            if (e.Alert != null)
            {
                _ = Task.Run(() => GenerateAlertMediaAsync(e.Alert));
            }
        }

        private void UpdateNaadConnectionStatus(NaadConnectionStatus status, string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => UpdateNaadConnectionStatus(status, message));
                return;
            }

            switch (status)
            {
                case NaadConnectionStatus.Connected:
                    _naadConnectionLabel!.Text = "🟢 Connected";
                    _naadConnectionLabel.ForeColor = _themeSuccessColor;
                    break;
                case NaadConnectionStatus.Connecting:
                    _naadConnectionLabel!.Text = "🟡 Connecting...";
                    _naadConnectionLabel.ForeColor = _themeWarningColor;
                    break;
                case NaadConnectionStatus.Disconnected:
                    _naadConnectionLabel!.Text = "🔴 Offline";
                    _naadConnectionLabel.ForeColor = _themeDangerColor;
                    break;
            }
        }

        private async Task GenerateAlertMediaAsync(AlertEntry alert)
        {
            try
            {
                var cfg = ConfigManager.LoadConfig();
                string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AlertOutput");
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                string language = cfg.AlertReady?.PreferredLanguage ?? "fr-CA";

                Logger.Log($"Generating media for alert: {alert.Title}", Logger.LogLevel.Info);

                var alerts = new List<AlertEntry> { alert };
                var generatedFiles = EmergencyAlertGenerator.GenerateEmergencyAlerts(alerts, outputDir, language);

                Logger.Log($"Generated {generatedFiles.Count} file(s) for alert: {alert.Title}", Logger.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to generate alert media: {ex.Message}", Logger.LogLevel.Error);
            }
        }
    }
}

/* Note: No Designer file is required for this simple form — controls are created at runtime */

        // Custom progress bar that paints a centered overlay text (percentage) and supports a simple marquee animation.
        internal class TextProgressBar : ProgressBar
        {
            private readonly System.Windows.Forms.Timer _marqueeTimer;
            private int _marqueeOffset = 0;
            private int _marqueeWidth = 80;

            public TextProgressBar()
            {
                // We handle painting ourselves
                SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                _marqueeTimer = new System.Windows.Forms.Timer { Interval = 60 };
                _marqueeTimer.Tick += (s, e) =>
                {
                    _marqueeOffset = (_marqueeOffset + 8) % (this.Width + _marqueeWidth);
                    this.Invalidate();
                };

                // Provide a pleasant default look
                this.ForeColor = Color.Black;
                this.BackColor = Color.FromArgb(240, 240, 240);
            }

            public void StartMarquee()
            {
                this.Style = ProgressBarStyle.Marquee;
                _marqueeTimer.Start();
                this.Invalidate();
            }

            public void StopMarquee()
            {
                _marqueeTimer.Stop();
                _marqueeOffset = 0;
                this.Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var rect = this.ClientRectangle;

                // Background gradient
                using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(rect, Color.FromArgb(240, 240, 240), Color.FromArgb(220, 220, 220), System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                    e.Graphics.FillRectangle(bg, rect);

                // Draw progress or marquee
                if (this.Style == ProgressBarStyle.Marquee)
                {
                    int w = Math.Min(_marqueeWidth, rect.Width);
                    int x = _marqueeOffset - w;
                    var mar = new Rectangle(x, 2, w, rect.Height - 4);
                    if (mar.Width > 0)
                    {
                        using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(mar, Color.FromArgb(150, 200, 255), Color.FromArgb(100, 160, 255), 0f))
                            e.Graphics.FillRectangle(b, mar);
                    }
                }
                else
                {
                    double pct = (this.Maximum > this.Minimum) ? (this.Value - this.Minimum) / (double)(this.Maximum - this.Minimum) : 0.0;
                    int width = (int)Math.Round(rect.Width * pct);
                    if (width > 0)
                    {
                        var fill = new Rectangle(0, 2, width, rect.Height - 4);
                        using (var g = new System.Drawing.Drawing2D.LinearGradientBrush(fill, Color.FromArgb(105, 180, 255), Color.FromArgb(40, 120, 255), System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                            e.Graphics.FillRectangle(g, fill);
                    }
                }

                // Border
                using (var p = new Pen(Color.FromArgb(170, 170, 170)))
                    e.Graphics.DrawRectangle(p, 0, 0, rect.Width - 1, rect.Height - 1);

                // Centered text (use Text property if set, otherwise default to percent)
                string text = string.IsNullOrEmpty(this.Text) ? (this.Maximum > 0 ? $"{(int)Math.Round((this.Value / (double)this.Maximum) * 100.0)}%" : "0%") : this.Text;
                Color textColor = Color.Black;
                try
                {
                    if (this.Value / (double)Math.Max(1, this.Maximum) > 0.5) textColor = Color.White;
                }
                catch { }

                TextRenderer.DrawText(e.Graphics, text, this.Font, rect, textColor, System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter);
            }
        }

        // A compact About dialog kept in the same file to avoid namespace collisions.
        internal class AboutDialog : Form
        {
            public AboutDialog()
            {
                this.Text = "About Weather Image Generator";
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.StartPosition = FormStartPosition.CenterParent;
                this.Width = 600;
                this.Height = 500;
                this.MaximizeBox = false;
                this.MinimizeBox = false;

                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var product = asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product
                              ?? asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title
                              ?? "Weather Image Generator";
                var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                              ?? asm.GetName().Version?.ToString() ?? "Unknown";
                var copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

                // --- Tab Control Setup ---
                var tabControl = new TabControl
                {
                    Dock = DockStyle.Top,
                    Height = 400
                };

                // --- Tab 1: General Info ---
                var tabGeneral = new TabPage("General");
                
                var lblProduct = new Label { Text = product, Font = new Font("Segoe UI", 14F, FontStyle.Bold), Left = 20, Top = 20, Width = 520, Height = 30 };
                var lblVersion = new Label { Text = $"Version: {version}", Left = 20, Top = 60, Width = 520, Font = new Font("Segoe UI", 10F) };
                var lblCopyright = new Label { Text = copyright, Left = 20, Top = 85, Width = 520, Font = new Font("Segoe UI", 10F) };

                var githubUrl = "https://github.com/NoID1290/WSG-Weather-Still-Generator";
                var aboutCfg = ConfigManager.LoadConfig();
                var aboutTheme = aboutCfg.Theme ?? "Blue";
                Color aboutAccent = aboutTheme.ToLowerInvariant() switch
                {
                    "light" => ColorTranslator.FromHtml("#1976D2"),
                    "dark" => ColorTranslator.FromHtml("#61AFEF"),
                    "green" => ColorTranslator.FromHtml("#27AE60"),
                    _ => ColorTranslator.FromHtml("#3498DB")
                };
                var linkGithub = new LinkLabel { Text = "GitHub Repository", Left = 20, Top = 120, Width = 520, LinkColor = aboutAccent, Font = new Font("Segoe UI", 10F) };
                linkGithub.LinkClicked += (s, e) => OpenUrl(githubUrl);

                var lblDesc = new Label 
                { 
                    Text = "A tool to generate weather forecast images and videos using data from Open-Meteo and alerts from Environment Canada.",
                    Left = 20, Top = 160, Width = 520, Height = 60,
                    Font = new Font("Segoe UI", 10F)
                };

                tabGeneral.Controls.AddRange(new Control[] { lblProduct, lblVersion, lblCopyright, linkGithub, lblDesc });

                // --- Tab 2: Credits & Attribution ---
                var tabCredits = new TabPage("Credits");
                var flowCredits = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(15), FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.White };

                // Local helpers for UI construction
                GroupBox CreateGroup(string title, params Control[] ctrls)
                {
                    var gb = new GroupBox { Text = title, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Width = 540, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 15) };
                    var pnl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 25, 10, 10) };
                    foreach (var c in ctrls) { c.Margin = new Padding(0, 0, 0, 5); pnl.Controls.Add(c); }
                    gb.Controls.Add(pnl);
                    return gb;
                }
                
                LinkLabel MkLink(string txt, string url) {
                    var l = new LinkLabel { Text = txt, AutoSize = true, Font = new Font("Segoe UI", 9.5F), LinkColor = Color.FromArgb(0, 102, 204) };
                    l.LinkClicked += (s, e) => OpenUrl(url);
                    return l;
                }
                
                Label MkLbl(string txt, bool bold = false, bool italic = false) {
                    var style = FontStyle.Regular;
                    if (bold) style |= FontStyle.Bold;
                    if (italic) style |= FontStyle.Italic;
                    return new Label { Text = txt, AutoSize = true, Font = new Font("Segoe UI", 9F, style), ForeColor = italic ? Color.DimGray : Color.Black };
                }

                // 1. Development
                var grpDev = CreateGroup("Development",
                    MkLbl("Author: NoID Softwork", true),
                    MkLink("GitHub Repository", "https://github.com/NoID1290/WSG-Weather-Still-Generator"),
                    MkLbl("License: MIT License", false, true),
                    MkLbl("Built with .NET 8.0")
                );

                // 2. Weather Data
                var grpData = CreateGroup("Weather Data Sources",
                    MkLbl("Open-Meteo.com", true),
                    MkLink("https://open-meteo.com/", "https://open-meteo.com/"),
                    MkLbl("License: Creative Commons Attribution 4.0 (CC-BY 4.0)", false, true),
                    new Label { Height = 10 }, // spacer
                    MkLbl("Environment and Climate Change Canada", true),
                    MkLink("https://weather.gc.ca/", "https://weather.gc.ca/"),
                    MkLbl("License: Open Government Licence - Canada", false, true)
                );

                // 3. Multimedia
                var grpMedia = CreateGroup("Multimedia",
                    MkLbl("FFmpeg Project", true),
                    MkLink("https://ffmpeg.org/", "https://ffmpeg.org/"),
                    MkLbl("License: LGPL v2.1", false, true),
                    MkLbl("FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project.", false, true),
                    new Label { Height = 10 }, // spacer
                    MkLbl("Music", true),
                    MkLbl("Kevin MacLeod (incompetech.com)", false),
                    MkLink("https://incompetech.com/", "https://incompetech.com/"),
                    MkLbl("Licensed under Creative Commons: By Attribution 3.0", false, true),
                    MkLink("http://creativecommons.org/licenses/by/3.0/", "http://creativecommons.org/licenses/by/3.0/")
                );

                flowCredits.Controls.AddRange(new Control[] { grpDev, grpData, grpMedia });
                tabCredits.Controls.Add(flowCredits);

                // --- Tab 3: License ---
                var tabLicense = new TabPage("License");
                var txtLicense = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F) };
                txtLicense.Text = @"MIT License

Copyright (c) 2020-2026 NoID Softwork 

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.";
                tabLicense.Controls.Add(txtLicense);

                // --- Tab 4: Disclaimer ---
                var tabDisclaimer = new TabPage("Disclaimer");
                var txtDisclaimer = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10F) };
                txtDisclaimer.Text = @"IMPORTANT DISCLAIMER:

1. Not for Safety-Critical Use:
This application is for informational and educational purposes only. It should NOT be used for safety-critical decisions, navigation, or protection of life and property.

2. Data Accuracy:
Weather data is retrieved from third-party sources (Open-Meteo, ECCC) and may contain errors, delays, or inaccuracies. The generated images and videos may not reflect the most current conditions.

3. Official Sources:
Always consult official sources (e.g., Environment Canada, National Weather Service, local authorities) for severe weather alerts and emergency information.

4. No Warranty:
The authors and contributors of this software provide it ""AS IS"" without any warranty of any kind. We are not responsible for any damages or losses resulting from the use of this software.";
                tabDisclaimer.Controls.Add(txtDisclaimer);

                tabControl.TabPages.Add(tabGeneral);
                tabControl.TabPages.Add(tabCredits);
                tabControl.TabPages.Add(tabLicense);
                tabControl.TabPages.Add(tabDisclaimer);

                var ok = new Button { Text = "OK", Left = 490, Top = 420, Width = 80, Height = 30 };
                ok.Click += (s, e) => this.Close();

                this.Controls.Add(tabControl);
                this.Controls.Add(ok);

                this.KeyPreview = true;
                this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) this.Close(); };
            }

            private void OpenUrl(string url)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch { /* best-effort only */ }
            }
        }