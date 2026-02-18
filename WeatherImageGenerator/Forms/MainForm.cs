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
using System.Runtime.InteropServices;
using System.Xml.Linq;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Utilities;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Forms;

using EAS;
using EAS.AlertReady;
using EAS.NWS;

namespace WeatherImageGenerator.Forms
{
    public class MainForm : Form
    {
        // P/Invoke for RichTextBox paragraph spacing
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref PARAFORMAT2 lParam);
        private const int EM_SETPARAFORMAT = 0x0447;
        private const int EM_GETPARAFORMAT = 0x043D;
        private const uint PFM_SPACEAFTER  = 0x00000080;
        private const uint PFM_SPACEBEFORE = 0x00000040;
        private const uint PFM_LINESPACING = 0x00000100;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PARAFORMAT2
        {
            public int cbSize;
            public uint dwMask;
            public short wNumbering;
            public short wReserved;
            public int dxStartIndent;
            public int dxRightIndent;
            public int dxOffset;
            public short wAlignment;
            public short cTabCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public int[] rgxTabs;
            public int dySpaceBefore;
            public int dySpaceAfter;
            public int dyLineSpacing;
            public short sStyle;
            public byte bLineSpacingRule;
            public byte bOutlineLevel;
            public short wShadingWeight;
            public short wShadingStyle;
            public short wNumberingStart;
            public short wNumberingStyle;
            public short wNumberingTab;
            public short wBorderSpace;
            public short wBorderWidth;
            public short wBorders;
        }

        /// <summary>Set exact line spacing on all text in a RichTextBox using PARAFORMAT2 rule 4 (exact twips).</summary>
        private static void SetTightLineSpacing(RichTextBox rtb, int spacingTwips)
        {
            rtb.SelectAll();
            var pf = new PARAFORMAT2();
            pf.cbSize = Marshal.SizeOf(pf);
            pf.rgxTabs = new int[32];
            pf.dwMask = PFM_SPACEAFTER | PFM_SPACEBEFORE | PFM_LINESPACING;
            pf.dySpaceBefore = 0;
            pf.dySpaceAfter = 0;
            pf.dyLineSpacing = spacingTwips;   // exact line height in twips
            pf.bLineSpacingRule = 4;            // 4 = exact spacing in twips
            SendMessage(rtb.Handle, EM_SETPARAFORMAT, IntPtr.Zero, ref pf);
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
        }

        /// <summary>Apply exact spacing to the current paragraph (call after appending a line).</summary>
        private static void ApplyTightSpacingToCurrentParagraph(RichTextBox rtb, int spacingTwips)
        {
            // Find the start of the current line/paragraph
            int currentPos = rtb.SelectionStart;
            int lineIndex = rtb.GetLineFromCharIndex(currentPos);
            int lineStart = rtb.GetFirstCharIndexFromLine(lineIndex);
            int lineLength = (lineIndex < rtb.Lines.Length - 1) 
                ? rtb.GetFirstCharIndexFromLine(lineIndex + 1) - lineStart
                : rtb.TextLength - lineStart;
            
            // Select the entire paragraph to apply format
            rtb.Select(lineStart, lineLength);
            
            var pf = new PARAFORMAT2();
            pf.cbSize = Marshal.SizeOf(pf);
            pf.rgxTabs = new int[32];
            pf.dwMask = PFM_SPACEAFTER | PFM_SPACEBEFORE | PFM_LINESPACING;
            pf.dySpaceBefore = 0;
            pf.dySpaceAfter = 0;
            pf.dyLineSpacing = spacingTwips;   // exact line height in twips
            pf.bLineSpacingRule = 4;            // 4 = exact spacing in twips
            SendMessage(rtb.Handle, EM_SETPARAFORMAT, IntPtr.Zero, ref pf);
            
            // Restore position
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
        }
        private CancellationTokenSource? _cts;
        private NotifyIcon? _notifyIcon;
        private bool _isMinimizedToTray = false;
        // Store text together with the explicit LogLevel so rendering is deterministic
        private readonly System.Collections.Generic.List<(string Text, Logger.LogLevel Level)> _logBuffer = new System.Collections.Generic.List<(string Text, Logger.LogLevel Level)>();
        private int _savedSplitterDistance = 0;  // Store splitter distance when logs are collapsed
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
        private Button? _startBtn, _stopBtn, _fetchBtn, _stillBtn, _videoBtn, _openOutputBtn, _clearDirBtn, _locationsBtn, _musicBtn, _settingsBtn, _aboutBtn, _clearLogBtn, _cancelBtn, _galleryBtn, _testAlertBtn, _toggleLogsBtn;
        
        // UI controls for log line spacing (user adjustable)
        private ComboBox? _cmbLineSpacing;
        private Label? _lblLineSpacing;
        private int _logLineSpacingDy = 220; // line height in twips (rule 4). 220 = Relaxed for 9pt Consolas
        private CancellationTokenSource? _operationCts;
        private Services.VideoGenerator? _runningVideoGenerator; 
        private Label? _groupLabel1, _groupLabel2, _groupLabel3, _groupLabel4, _progressLabel, _statusLabel2, _lblLog;
        private System.Threading.Timer? _logArchiveTimer;
        private Button? _openWebUIBtn;
        private Button? _weatherMapBtn;
        // NAAD Status Panel
        private Panel? _naadPanel;
        private Label? _naadTitleLabel;
        private Label? _naadConnectionLabel;
        private Label? _naadHeartbeatLabel;
        private Label? _naadAlertLabel;
        private AlertReadyClient? _naadClient;
        private CancellationTokenSource? _naadCts;
        private System.Net.Http.HttpClient? _naadHttpClient;

        // NWS Status Panel & Polling
        private Panel? _nwsPanel;
        private Label? _nwsTitleLabel;
        private Label? _nwsStatusLabel;
        private Label? _nwsLastFetchLabel;
        private Label? _nwsAlertLabel;
        private CancellationTokenSource? _nwsPollCts;
        private System.Net.Http.HttpClient? _nwsHttpClient;
        private HashSet<string> _nwsSeenAlertIds = new HashSet<string>();

        // Cross-cycle alert deduplication: tracks fingerprints of alerts already processed (video + form shown)
        private HashSet<string> _cycleProcessedAlertFingerprints = new HashSet<string>();
        private bool _forceFetchAlertGeneration = false;

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
        private TextBox? _txtWebUIUrl;

        public MainForm()
        {
            this.Text = "WSG - WeatherStillGenerator";
            this.Width = 1220;
            this.Height = 700;
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            _logBox = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Name = "logBox", Font = new System.Drawing.Font("Consolas", 9F), DetectUrls = true, HideSelection = false, ScrollBars = RichTextBoxScrollBars.Vertical, BorderStyle = BorderStyle.None, Padding = new Padding(4) };
            // Note: RichTextBox with Dock=Fill doesn't support Region properly, so we skip rounding for it
            // Force tight paragraph spacing so log lines aren't spread apart
            _logBox.HandleCreated += (s, e) => SetTightLineSpacing(_logBox, _logLineSpacingDy);

            // Start a background timer that will periodically archive older logs to disk to avoid UI growth/crashes
            _logArchiveTimer = new System.Threading.Timer(_ => {
                try { TryArchiveLogsIfNeededSafe(); } catch { }
            }, null, 30000, 30000); // every 30s
            
            // === CONTROL GROUPS - Professional Aligned Layout ===
            // Constants for consistent spacing and sizing
            int btnHeight = 32;
            int btnSpacing = 6;
            int groupSpacing = 20;
            int row1Top = 22;
            int row2Top = 58;
            int labelTop = 6;
            
            // Group 1: Control Cycle (Start/Stop)
            int g1Left = 15;
            int g1BtnWidth = 70;
            _groupLabel1 = new Label { Text = "CONTROL CYCLE", Left = g1Left, Top = labelTop, AutoSize = true, Font = new Font("Segoe UI", 7F, FontStyle.Bold), ForeColor = Color.FromArgb(180, 180, 180) };
            _startBtn = CreateStyledButton("Start", g1Left, row1Top, g1BtnWidth, btnHeight, Color.FromArgb(39, 174, 96), Color.White);
            _stopBtn = CreateStyledButton("Stop", g1Left + g1BtnWidth + btnSpacing, row1Top, g1BtnWidth, btnHeight, Color.FromArgb(192, 57, 43), Color.White);
            SetButtonEnabled(_stopBtn, false);
            
            // Group 2: Generate (Fetch, Still, Video, Cancel, Test Alert)
            int g2Left = g1Left + (g1BtnWidth * 2) + btnSpacing + groupSpacing;
            int g2BtnWidth = 75;
            _groupLabel2 = new Label { Text = "GENERATE", Left = g2Left, Top = labelTop, AutoSize = true, Font = new Font("Segoe UI", 7F, FontStyle.Bold), ForeColor = Color.FromArgb(180, 180, 180) };
            _fetchBtn = CreateStyledButton("Fetch", g2Left, row1Top, g2BtnWidth, btnHeight, Color.FromArgb(41, 128, 185), Color.White);
            _stillBtn = CreateStyledButton("Still", g2Left + (g2BtnWidth + btnSpacing), row1Top, g2BtnWidth, btnHeight, Color.FromArgb(41, 128, 185), Color.White);
            _videoBtn = CreateStyledButton("Video", g2Left + (g2BtnWidth + btnSpacing) * 2, row1Top, g2BtnWidth, btnHeight, Color.FromArgb(41, 128, 185), Color.White);
            _cancelBtn = CreateStyledButton("Cancel", g2Left + (g2BtnWidth + btnSpacing) * 3, row1Top, g2BtnWidth, btnHeight, Color.FromArgb(192, 57, 43), Color.White);
            SetButtonEnabled(_cancelBtn, false);
            _testAlertBtn = CreateStyledButton("Test Alert", g2Left, row2Top, (g2BtnWidth * 2) + btnSpacing, btnHeight, Color.FromArgb(230, 126, 34), Color.White);
            
            // Group 3: Files (Open, Clear, Gallery, WebUI)
            int g3Left = g2Left + (g2BtnWidth + btnSpacing) * 4 + groupSpacing;
            int g3BtnWidth = 70;
            _groupLabel3 = new Label { Text = "FILES", Left = g3Left, Top = labelTop, AutoSize = true, Font = new Font("Segoe UI", 7F, FontStyle.Bold), ForeColor = Color.FromArgb(180, 180, 180) };
            _openOutputBtn = CreateStyledButton("Open", g3Left, row1Top, g3BtnWidth, btnHeight, Color.FromArgb(52, 73, 94), Color.White);
            _clearDirBtn = CreateStyledButton("Clear", g3Left + g3BtnWidth + btnSpacing, row1Top, g3BtnWidth, btnHeight, Color.FromArgb(127, 140, 141), Color.White);
            _galleryBtn = CreateStyledButton("Gallery", g3Left, row2Top, g3BtnWidth, btnHeight, Color.FromArgb(52, 73, 94), Color.White);
            _openWebUIBtn = CreateStyledButton("WebUI", g3Left + g3BtnWidth + btnSpacing, row2Top, g3BtnWidth, btnHeight, Color.FromArgb(155, 89, 182), Color.White);
            _weatherMapBtn = CreateStyledButton("Map", g3Left + (g3BtnWidth + btnSpacing) * 2, row2Top, g3BtnWidth, btnHeight, Color.FromArgb(39, 174, 96), Color.White);
            
            // Group 4: Settings (Locations, Music, Settings, About)
            int g4Left = g3Left + (g3BtnWidth + btnSpacing) * 2 + groupSpacing;
            int g4BtnWidth = 90;
            _groupLabel4 = new Label { Text = "SETTINGS", Left = g4Left, Top = labelTop, AutoSize = true, Font = new Font("Segoe UI", 7F, FontStyle.Bold), ForeColor = Color.FromArgb(180, 180, 180) };
            _locationsBtn = CreateStyledButton("Locations", g4Left, row1Top, g4BtnWidth, btnHeight, Color.FromArgb(52, 73, 94), Color.White);
            _musicBtn = CreateStyledButton("Music", g4Left + g4BtnWidth + btnSpacing, row1Top, g4BtnWidth, btnHeight, Color.FromArgb(52, 73, 94), Color.White);
            _settingsBtn = CreateStyledButton("Settings", g4Left + (g4BtnWidth + btnSpacing) * 2, row1Top, g4BtnWidth, btnHeight, Color.FromArgb(52, 73, 94), Color.White);
            _aboutBtn = CreateStyledButton("About", g4Left + (g4BtnWidth + btnSpacing) * 3, row1Top, g4BtnWidth, btnHeight, Color.FromArgb(52, 73, 94), Color.White);
            _toggleLogsBtn = CreateStyledButton("▼ Logs", g4Left + (g4BtnWidth + btnSpacing) * 4 + 5, row1Top, 80, btnHeight, Color.FromArgb(155, 89, 182), Color.White);
            _toggleLogsBtn.Click += (s, e) => ToggleLogsVisibility();


            // Progress & Status Section (Below buttons - properly spaced below row2)
            // Row 2 buttons end at: row2Top(58) + btnHeight(32) = 90, add 8px padding
            int statusSectionTop = row2Top + btnHeight + 10; // 100
            int statusRowTop = statusSectionTop + 14; // 114 - for progress bar
            int statusRow2Top = statusRowTop + 26; // 140 - for NAAD panel
            int progressWidth = 520;
            int statusLeft = progressWidth + 40;
            
            _progressLabel = new Label { Text = "PROGRESS", Left = 15, Top = statusSectionTop, AutoSize = true, Font = new Font("Segoe UI", 7F, FontStyle.Bold), ForeColor = Color.FromArgb(140, 150, 170) };
            _progress = new TextProgressBar { Left = 15, Top = statusRowTop, Width = progressWidth, Height = 28, Style = ProgressBarStyle.Continuous, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };

            // NAAD Status Panel - Modern professional status bar design
            int statusPanelWidth = (progressWidth - 8) / 2;  // Half width for side-by-side layout with NWS
            _naadPanel = new Panel { 
                Left = 15, 
                Top = statusRow2Top, 
                Width = statusPanelWidth, 
                Height = 28, 
                BackColor = Color.FromArgb(35, 45, 60), 
                Padding = new Padding(8, 0, 8, 0) 
            };
            _naadPanel.Paint += (s, e) => {
                // Draw rounded rectangle background
                var rect = _naadPanel.ClientRectangle;
                rect.Width -= 1; rect.Height -= 1;
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    int r = 6;
                    path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
                    path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
                    path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
                    path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
                    path.CloseFigure();
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(rect, 
                        Color.FromArgb(40, 52, 70), Color.FromArgb(30, 40, 55), 
                        System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                    using (var pen = new Pen(Color.FromArgb(55, 70, 90), 1f))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };
            
            // Title with modern styling
            _naadTitleLabel = new Label { 
                Text = "◈ NAAD", 
                Left = 10, 
                Top = 5, 
                AutoSize = true, 
                Font = new Font("Segoe UI", 9F, FontStyle.Bold), 
                ForeColor = Color.FromArgb(200, 210, 225),
                BackColor = Color.Transparent
            };
            
            // Connection status with colored indicator
            _naadConnectionLabel = new Label { 
                Text = "○ Offline", 
                Left = 85, 
                Top = 5, 
                AutoSize = true, 
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 130, 150),
                BackColor = Color.Transparent
            };
            
            // Heartbeat with subtle styling
            _naadHeartbeatLabel = new Label { 
                Text = "♡ --:--:--", 
                Left = 175, 
                Top = 5, 
                AutoSize = true, 
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(140, 150, 170),
                BackColor = Color.Transparent
            };
            
            // Alert counter with emphasis styling
            _naadAlertLabel = new Label { 
                Text = "△ 0 alerts", 
                Left = statusPanelWidth - 90, 
                Top = 5, 
                AutoSize = true, 
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(140, 150, 170),
                BackColor = Color.Transparent
            };
            
            _naadPanel.Controls.Add(_naadTitleLabel);
            _naadPanel.Controls.Add(_naadConnectionLabel);
            _naadPanel.Controls.Add(_naadHeartbeatLabel);
            _naadPanel.Controls.Add(_naadAlertLabel);

            // NWS Status Panel - matching NAAD style, side by side with NAAD panel
            _nwsPanel = new Panel {
                Left = 15 + statusPanelWidth + 8,
                Top = statusRow2Top,
                Width = statusPanelWidth,
                Height = 28,
                BackColor = Color.FromArgb(35, 45, 60),
                Padding = new Padding(8, 0, 8, 0),
                Visible = false  // hidden until NWS is enabled
            };
            _nwsPanel.Paint += (s, e) => {
                var rect = _nwsPanel.ClientRectangle;
                rect.Width -= 1; rect.Height -= 1;
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    int r = 6;
                    path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
                    path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
                    path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
                    path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
                    path.CloseFigure();
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(rect,
                        Color.FromArgb(40, 42, 70), Color.FromArgb(30, 30, 55),
                        System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                    using (var pen = new Pen(Color.FromArgb(55, 60, 90), 1f))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };

            _nwsTitleLabel = new Label {
                Text = "\u25c8 NWS",
                Left = 10,
                Top = 5,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 149, 237),  // Cornflower blue for NWS
                BackColor = Color.Transparent
            };

            _nwsStatusLabel = new Label {
                Text = "\u25cb Idle",
                Left = 75,
                Top = 5,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 130, 150),
                BackColor = Color.Transparent
            };

            _nwsLastFetchLabel = new Label {
                Text = "\u231a --:--:--",
                Left = 175,
                Top = 5,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(140, 150, 170),
                BackColor = Color.Transparent
            };

            _nwsAlertLabel = new Label {
                Text = "\u25b3 0 alerts",
                Left = statusPanelWidth - 90,
                Top = 5,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(140, 150, 170),
                BackColor = Color.Transparent
            };

            _nwsPanel.Controls.Add(_nwsTitleLabel);
            _nwsPanel.Controls.Add(_nwsStatusLabel);
            _nwsPanel.Controls.Add(_nwsLastFetchLabel);
            _nwsPanel.Controls.Add(_nwsAlertLabel);

            _statusLabel2 = new Label { Text = "STATUS", Left = statusLeft, Top = statusSectionTop, AutoSize = true, Font = new Font("Segoe UI", 7F, FontStyle.Bold), ForeColor = Color.FromArgb(140, 150, 170) };
            _statusLabel = new Label { Left = statusLeft, Top = statusRowTop, Width = 400, Height = 28, Text = "✦ Idle — Ready to process", AutoSize = false, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold), BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };
            _sleepLabel = new Label { Left = statusLeft, Top = statusRow2Top, Width = 350, Height = 20, Text = string.Empty, AutoSize = false, Font = new Font("Segoe UI", 9F, FontStyle.Regular), BackColor = Color.Transparent };
            _lastFetchLabel = new Label { Dock = DockStyle.Top, Height = 26, Text = "📡 Last fetch: Never", Font = new Font("Segoe UI", 9F, FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 4, 0, 0) };

            // --- Log Controls Panel (Professional Layout) ---
            _logPanel = new Panel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(10, 6, 10, 6) };
            
            _lblLog = new Label { Text = "📋 LOGS", Left = 10, Top = 12, AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };

            // When the form is first shown, optionally auto-start the update cycle based on configuration
            this.Shown += (s, e) =>
            {
                try
                {
                    var cfg = ConfigManager.LoadConfig();
                    
                    // Check if app should start minimized to tray (when launched from Windows startup)
                    if (cfg.StartWithWindows && cfg.StartMinimizedToTray)
                    {
                        Logger.Log("Starting minimized to system tray (Windows startup).");
                        MinimizeToTray();
                    }
                    
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

                    // Start NWS polling if NWS is enabled
                    if (cfg.Nws?.Enabled == true)
                    {
                        StartNwsPolling(cfg);
                    }
                    else
                    {
                        Logger.Log("NWS alerts are not enabled in config.", Logger.LogLevel.Info);
                    }
                    
                    // Check for updates on startup if enabled
                    if (cfg.CheckForUpdatesOnStartup)
                    {
                        _ = CheckForUpdatesOnStartupAsync();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to auto-start: {ex.Message}", Logger.LogLevel.Error);
                }
            };
            
            _cmbFilter = new ComboBox { Left = 80, Top = 9, Width = 95, Height = 26, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9F, FontStyle.Regular) };
            _cmbFilter.Items.AddRange(new object[] { "All", "Errors", "Warnings", "Info" });
            _cmbFilter.SelectedIndex = 0;
            _cmbFilter.SelectedIndexChanged += (s, e) => RefreshLogView();

            _cmbVerbosity = new ComboBox { Left = 182, Top = 9, Width = 88, Height = 26, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9F, FontStyle.Regular) };
            _cmbVerbosity.Items.AddRange(new object[] { "Verbose", "Normal", "Minimal" });
            _cmbVerbosity.SelectedIndex = 1; // Normal
            _cmbVerbosity.SelectedIndexChanged += (s, e) => RefreshLogView();

            // Initialize line spacing from config
            try
            {
                var cfgLine = ConfigManager.LoadConfig();
                var rawSpacing = cfgLine.LogLineSpacing;
                // Migrate old small values (14/16/20/24) to new twip values
                _logLineSpacingDy = rawSpacing switch
                {
                    <= 0 => 180,
                    14 => 140,
                    16 => 180,
                    20 => 180,
                    24 => 220,
                    < 100 => 180,   // any other legacy small value → Normal
                    _ => rawSpacing // already in twips
                };

                // Map to combo index (only if combo exists)
                if (_cmbLineSpacing != null)
                {
                    _cmbLineSpacing.SelectedIndex = _logLineSpacingDy switch { 140 => 0, 180 => 1, 220 => 2, 300 => 3, _ => 1 };
                }
                // Apply initial spacing immediately if handle exists
                try { if (_logBox != null) SetTightLineSpacing(_logBox, _logLineSpacingDy); } catch { }
            }
            catch { if (_cmbLineSpacing != null) _cmbLineSpacing.SelectedIndex = 1; }

            _chkCompact = new CheckBox { Left = 280, Top = 11, Width = 78, Text = "Compact", Font = new Font("Segoe UI", 9F, FontStyle.Regular), FlatStyle = FlatStyle.Flat };
            _chkCompact.CheckedChanged += (s, e) => RefreshLogView();

            _txtSearch = new TextBox { Left = 365, Top = 9, Width = 240, Height = 26, Font = new Font("Segoe UI", 9F), BorderStyle = BorderStyle.FixedSingle };
            _txtSearch.PlaceholderText = "🔍 Search logs...";
            _txtSearch.TextChanged += (s, e) => RefreshLogView();

            _clearLogBtn = CreateStyledButton("Clear", 615, 7, 70, 28, Color.FromArgb(127, 140, 141), Color.White);
            _clearLogBtn.Click += (s, e) => 
            {
                lock (_logBuffer)
                {
                    _logBuffer.Clear();
                }
                RefreshLogView();
            };

            // --- Line spacing control for logs ---
            // Compute position dynamically so it stays visible next to the Clear button even on narrow windows
            int lblSpacingTop = 12;
            int comboTop = 9;
            int lblWidth = 90;
            int comboWidth = 150;
            int spacingLeft = 700;
            if (_clearLogBtn != null) spacingLeft = _clearLogBtn.Left + _clearLogBtn.Width + 8;

            _lblLineSpacing = new Label { Left = spacingLeft, Top = lblSpacingTop, Width = lblWidth, AutoSize = false, Text = "Line spacing", Font = new Font("Segoe UI", 9F), TextAlign = ContentAlignment.MiddleLeft };
            _cmbLineSpacing = new ComboBox { Left = spacingLeft + lblWidth + 8, Top = comboTop, Width = comboWidth, Height = 26, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9F) };
            _cmbLineSpacing.Items.AddRange(new object[] { "Tight", "Normal", "Relaxed", "Spacious" });
            _cmbLineSpacing.SelectedIndexChanged += (s, e) => 
            {
                if (_cmbLineSpacing?.SelectedItem != null)
                {
                    var sel = _cmbLineSpacing.SelectedItem.ToString() ?? "";
                    _logLineSpacingDy = sel switch
                    {
                        "Tight" => 140,
                        "Normal" => 180,
                        "Relaxed" => 220,
                        "Spacious" => 300,
                        _ => 180
                    };

                    try
                    {
                        var cfg = ConfigManager.LoadConfig();
                        cfg.LogLineSpacing = _logLineSpacingDy;
                        ConfigManager.SaveConfig(cfg, silent: true);
                    }
                    catch (Exception ex) { Logger.Log($"Failed to save LogLineSpacing: {ex.Message}", Logger.LogLevel.Warning); }

                    // Apply immediately to existing control and refresh view. Force a second apply after refresh to ensure paragraphs get updated.
                    try
                    {
                        if (_logBox != null)
                        {
                            SetTightLineSpacing(_logBox, _logLineSpacingDy);
                            RefreshLogView();
                            // Ensure formatting applied after refresh (some RTF updates require a second pass)
                            SetTightLineSpacing(_logBox, _logLineSpacingDy);
                            _logBox.Invalidate();
                        }
                    }
                    catch (Exception ex) { Logger.Log($"Failed to apply log spacing: {ex.Message}", Logger.LogLevel.Warning); }
                }
            };
            _logPanel.Controls.Add(_lblLog);
            _logPanel.Controls.Add(_cmbFilter);
            _logPanel.Controls.Add(_cmbVerbosity);
            _logPanel.Controls.Add(_chkCompact);
            _logPanel.Controls.Add(_txtSearch);
            _logPanel.Controls.Add(_clearLogBtn);
            _logPanel.Controls.Add(_lblLineSpacing);
            _logPanel.Controls.Add(_cmbLineSpacing);
            _startBtn.Click += (s, e) => StartClicked(_startBtn, _stopBtn);
            _stopBtn.Click += (s, e) => StopClicked(_startBtn, _stopBtn);
            _openOutputBtn.Click += (s, e) => OpenOutputDirectory();
            _clearDirBtn.Click += (s, e) => ClearOutputDirectory();
            _videoBtn.Click += (s, e) => VideoClicked();
            _fetchBtn.Click += (s, e) => FetchClicked(_fetchBtn);
            _stillBtn.Click += (s, e) => StillClicked(_stillBtn);
            _openWebUIBtn.Click += (s, e) => OpenWebUIInBrowser();
            _weatherMapBtn.Click += (s, e) =>
            {
                try
                {
                    var f = new WeatherMapForm();
                    f.Show();
                    Logger.Log("Opened Weather Interactive Map.", Logger.LogLevel.Info);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open weather map: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            
            // Subscribe to WebUI events if the service is running
            SubscribeToWebUIEvents();
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
                    if (f.ShowDialog(this) == DialogResult.OK)
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
                    if (f.ShowDialog(this) == DialogResult.OK)
                    {
                        Logger.Log("Locations updated.");
                    }
                }
            };

            _musicBtn.Click += (s, e) =>
            {
                using (var f = new MusicForm())
                {
                    if (f.ShowDialog(this) == DialogResult.OK)
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
            


            _topPanel = new Panel { Dock = DockStyle.Top, Height = 178, Padding = new Padding(8, 4, 8, 4) };
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
            _topPanel.Controls.Add(_openWebUIBtn);
            _topPanel.Controls.Add(_weatherMapBtn);
            _topPanel.Controls.Add(_toggleLogsBtn);
            _topPanel.Controls.Add(_txtWebUIUrl);
            // Add progress and status
            _topPanel.Controls.Add(_progressLabel);
            _topPanel.Controls.Add(_statusLabel2);
            _topPanel.Controls.Add(_progress);
            _topPanel.Controls.Add(_statusLabel);
            _topPanel.Controls.Add(_sleepLabel);
            _topPanel.Controls.Add(_naadPanel);
            _topPanel.Controls.Add(_nwsPanel);
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
            _logBox!.BringToFront(); 

            _tabControl.TabPages.Add(_logTab);

            // Save selected tab when user changes tabs (silent to avoid log spam)
            _tabControl.SelectedIndexChanged += (s, e) =>
            {
                try
                {
                    var config = ConfigManager.LoadConfig();
                    config.SelectedTabIndex = _tabControl.SelectedIndex;
                    ConfigManager.SaveConfig(config, silent: true);
                }
                catch { /* Ignore save errors */ }
            };

            _splitContainer.Panel2.Controls.Add(_tabControl);
            
            // Configure splitter (distance will be set in Load event when form has proper size)
            _splitContainer.SplitterWidth = 6;
            _splitContainer.Panel2MinSize = 100;
            
            // Save splitter position when user moves it (silent to avoid log spam)
            _splitContainer.SplitterMoved += (s, e) =>
            {
                try
                {
                    var config = ConfigManager.LoadConfig();
                    config.SplitterDistance = _splitContainer.SplitterDistance;
                    ConfigManager.SaveConfig(config, silent: true);
                }
                catch { /* Ignore save errors */ }
            };

            this.Controls.Add(_splitContainer);
            this.Controls.Add(_topPanel);
            
            // Set splitter distance after form is loaded to avoid size constraint issues
            this.Load += (s, e) =>
            {
                // Restore saved splitter distance, or use default
                var config = ConfigManager.LoadConfig();
                int targetDistance;
                
                if (config.SplitterDistance > 0)
                {
                    targetDistance = config.SplitterDistance;
                    _savedSplitterDistance = targetDistance;  // Save for collapse/restore
                }
                else
                {
                    // Default: logs panel takes about 35% of the split container height
                    targetDistance = (int)(_splitContainer.Height * 0.35);
                    _savedSplitterDistance = targetDistance;
                }
                
                if (targetDistance >= _splitContainer.Panel1MinSize && 
                    targetDistance <= _splitContainer.Height - _splitContainer.Panel2MinSize)
                {
                    _splitContainer.SplitterDistance = targetDistance;
                }
                
                // Restore saved tab selection
                if (config.SelectedTabIndex >= 0 && config.SelectedTabIndex < _tabControl.TabCount)
                {
                    _tabControl.SelectedIndex = config.SelectedTabIndex;
                }
                
                // Restore saved window size
                if (config.WindowWidth > 0 && config.WindowHeight > 0)
                {
                    this.Width = config.WindowWidth;
                    this.Height = config.WindowHeight;
                }
                
                // Restore logs collapsed state (do this last after splitter distance is set)
                if (config.LogsCollapsed && _splitContainer != null)
                {
                    SetLogsCollapsed(true);
                }
            };
            
            // Save window size when resized (silent to avoid log spam)
            this.ResizeEnd += (s, e) =>
            {
                if (this.WindowState == FormWindowState.Normal)
                {
                    try
                    {
                        var config = ConfigManager.LoadConfig();
                        config.WindowWidth = this.Width;
                        config.WindowHeight = this.Height;
                        ConfigManager.SaveConfig(config, silent: true);
                    }
                    catch { /* Ignore save errors */ }
                }
            };

            Program.WeatherDataFetched += OnWeatherDataFetched;
            Program.AlertsFetched += OnAlertsFetched;

            var cfg = ConfigManager.LoadConfig();
            ApplyTheme(cfg.Theme);
            
            // Setup NotifyIcon for minimize to tray
            InitializeNotifyIcon();

            // Keyboard shortcut: Ctrl+Shift+M opens Weather Interactive Map (always available)
            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                try
                {
                    if (e.Control && e.Shift && e.KeyCode == Keys.M)
                    {
                        var weatherMapForm = new WeatherMapForm();
                        weatherMapForm.Show();
                        Logger.Log("Opened Weather Interactive Map via Ctrl+Shift+M.", Logger.LogLevel.Info);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to open weather map via shortcut: {ex.Message}", Logger.LogLevel.Error);
                }
            };

            this.Resize += MainForm_Resize;
            this.FormClosing += MainForm_FormClosing;
        }

        public void OpenWebUIInBrowser()
        {
            try
            {
                var url = _txtWebUIUrl?.Text ?? string.Empty;
                if (string.IsNullOrEmpty(url))
                {
                    url = "http://localhost:5000"; // Default URL
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open browser: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

            // Buttons - update stored colors and apply only if enabled
            void SetBtn(Button? b, Color bg, Color fg) { 
                if (b != null) { 
                    // Update the stored original colors in Tag
                    b.Tag = new Color[] { bg, fg };
                    
                    // Only apply colors if the button is enabled; disabled buttons stay gray
                    if (b.Enabled)
                    {
                        b.BackColor = bg; 
                        b.ForeColor = fg; 
                        b.FlatAppearance.BorderColor = ControlPaint.Light(bg, 0.2f);
                        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
                        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(bg, 0.15f);
                    }
                    // Disabled buttons keep their gray appearance
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
            SetBtn(_weatherMapBtn, successColor, coloredBtnText);
            SetBtn(_clearLogBtn, dangerColor, coloredBtnText);

            // Combos & Inputs
            void SetCombo(ComboBox? c) { if (c != null) { c.BackColor = buttonColor; c.ForeColor = neutralBtnText; } }
            SetCombo(_cmbFilter);
            SetCombo(_cmbVerbosity);
            SetCombo(_cmbLineSpacing);
            if (_chkCompact != null) _chkCompact.ForeColor = headerTextColor;
            if (_txtSearch != null) { _txtSearch.BackColor = buttonColor; _txtSearch.ForeColor = neutralBtnText; }
            if (_txtWebUIUrl != null) { _txtWebUIUrl.BackColor = buttonColor; _txtWebUIUrl.ForeColor = neutralBtnText; }
            if (_progress != null) _progress.ForeColor = headerTextColor;
            if (_lblLineSpacing != null) _lblLineSpacing.ForeColor = headerTextColor;

            // NAAD Panel theme colors
            if (_naadTitleLabel != null) _naadTitleLabel.ForeColor = headerTextColor;
            if (_naadHeartbeatLabel != null) _naadHeartbeatLabel.ForeColor = labelTextColor;
            // Connection and alert labels are updated dynamically based on status, but set default colors
            if (_naadConnectionLabel != null && _naadClient == null) _naadConnectionLabel.ForeColor = labelTextColor;
            if (_naadAlertLabel != null) _naadAlertLabel.ForeColor = _naadAlertLabel.Text.Contains("0 alert") ? labelTextColor : warningColor;
        
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
            
            // Store original colors in Tag for restoration when re-enabled
            btn.Tag = new Color[] { backColor, foreColor };
            
            // Handle enabled/disabled state visual changes
            btn.EnabledChanged += (s, e) =>
            {
                if (s is Button b && b.Tag is Color[] colors && colors.Length == 2)
                {
                    Color origBack = colors[0];
                    Color origFore = colors[1];
                    
                    if (b.Enabled)
                    {
                        // Restore original colors
                        b.BackColor = origBack;
                        b.ForeColor = origFore;
                        b.Cursor = Cursors.Hand;
                        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(origBack, 0.15f);
                        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(origBack, 0.15f);
                    }
                    else
                    {
                        // Gray out the button
                        b.BackColor = Color.FromArgb(100, 100, 100);
                        b.ForeColor = Color.FromArgb(160, 160, 160);
                        b.Cursor = Cursors.Default;
                        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(100, 100, 100);
                        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(100, 100, 100);
                    }
                }
            };
            
            return btn;
        }

        /// <summary>
        /// Sets a styled button's enabled state and applies the appropriate visual styling immediately.
        /// </summary>
        private void SetButtonEnabled(Button btn, bool enabled)
        {
            btn.Enabled = enabled;
            
            // Apply visual styling immediately (in case EnabledChanged doesn't fire)
            if (btn.Tag is Color[] colors && colors.Length == 2)
            {
                if (enabled)
                {
                    btn.BackColor = colors[0];
                    btn.ForeColor = colors[1];
                    btn.Cursor = Cursors.Hand;
                    btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(colors[0], 0.15f);
                    btn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(colors[0], 0.15f);
                }
                else
                {
                    btn.BackColor = Color.FromArgb(100, 100, 100);
                    btn.ForeColor = Color.FromArgb(160, 160, 160);
                    btn.Cursor = Cursors.Default;
                    btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(100, 100, 100);
                    btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(100, 100, 100);
                }
            }
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
                // Cancel all running operations
                try
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _cts = null;
                }
                catch { }

                try
                {
                    _operationCts?.Cancel();
                    _operationCts?.Dispose();
                    _operationCts = null;
                }
                catch { }

                try
                {
                    _naadCts?.Cancel();
                    _naadCts?.Dispose();
                    _naadCts = null;
                }
                catch { }

                // Stop NAAD listener
                StopNaadListener();

                // Stop NWS polling
                StopNwsPolling();

                // Dispose timer
                try
                {
                    _logArchiveTimer?.Dispose();
                    _logArchiveTimer = null;
                }
                catch { }

                // Dispose NotifyIcon
                try
                {
                    _notifyIcon?.Dispose();
                    _notifyIcon = null;
                }
                catch { }

                // Dispose running video generator if any
                try
                {
                    _runningVideoGenerator = null;
                }
                catch { }
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

            // --- Cross-cycle alert deduplication: generate video + show form only for new/changed alerts ---
            bool forceAll = _forceFetchAlertGeneration;
            _forceFetchAlertGeneration = false;

            var currentFingerprints = new HashSet<string>();
            var cfg = ConfigManager.LoadConfig();
            string alertLanguage = cfg.AlertReady?.PreferredLanguage ?? "fr-CA";

            foreach (var alert in alerts)
            {
                string fp = ComputeAlertFingerprint(alert);
                currentFingerprints.Add(fp);

                if (forceAll || !_cycleProcessedAlertFingerprints.Contains(fp))
                {
                    _cycleProcessedAlertFingerprints.Add(fp);

                    // Only NAAD/AlertReady and NWS alerts trigger emergency media generation
                    if (alert.Provider == "Canada_AlertReady" || alert.Provider == "USA_NWS")
                    {
                        Logger.Log($"[Cycle Alert] New/changed alert detected — generating media: {alert.Title}", Logger.LogLevel.Info);
                        _ = Task.Run(() => GenerateAlertMediaAsync(alert));
                    }
                    else
                    {
                        Logger.Log($"[Cycle Alert] Skipping media generation for non-emergency provider '{alert.Provider}': {alert.Title}", Logger.LogLevel.Debug);
                    }
                }
                else
                {
                    Logger.Log($"[Cycle Alert] Skipping already-processed alert: {alert.Title}", Logger.LogLevel.Debug);
                }
            }

            // Prune fingerprints for alerts that are no longer active (expired/removed)
            _cycleProcessedAlertFingerprints.IntersectWith(currentFingerprints);

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

        /// <summary>
        /// Computes a stable fingerprint for an alert based on its key fields.
        /// The fingerprint changes when the alert content actually changes but stays
        /// the same for identical re-fetches across cycles.
        /// </summary>
        private static string ComputeAlertFingerprint(AlertEntry alert)
        {
            var summary = alert.Summary ?? "";
            if (summary.Length > 500) summary = summary.Substring(0, 500);
            return $"{alert.Identifier}|{alert.Title}|{alert.Severity}|{alert.City}|{summary}|{alert.IssuedAt}";
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

        /// <summary>
        /// Subscribe to Web UI action events
        /// </summary>
        private void SubscribeToWebUIEvents()
        {
            var webUIService = Program.WebUIService;
            if (webUIService != null)
            {
                webUIService.StartCycleRequested += (s, e) =>
                {
                    Logger.Log("Web UI: Start Cycle requested", Logger.LogLevel.Info);
                    if (_startBtn != null && _stopBtn != null)
                    {
                        this.Invoke(() => StartClicked(_startBtn, _stopBtn));
                    }
                };

                webUIService.StopCycleRequested += (s, e) =>
                {
                    Logger.Log("Web UI: Stop Cycle requested", Logger.LogLevel.Info);
                    if (_startBtn != null && _stopBtn != null)
                    {
                        this.Invoke(() => StopClicked(_startBtn, _stopBtn));
                    }
                };

                webUIService.GenerateStillRequested += (s, e) =>
                {
                    Logger.Log("Web UI: Generate Still requested", Logger.LogLevel.Info);
                    if (_stillBtn != null)
                    {
                        this.Invoke(() => StillClicked(_stillBtn));
                    }
                };

                webUIService.GenerateVideoRequested += (s, e) =>
                {
                    Logger.Log("Web UI: Generate Video requested", Logger.LogLevel.Info);
                    if (_videoBtn != null)
                    {
                        this.Invoke(() => VideoClicked());
                    }
                };

                Logger.Log("Web UI action handlers registered", Logger.LogLevel.Info);
            }
        }

        private void StartClicked(Button startBtn, Button stopBtn)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            SetButtonEnabled(startBtn, false);
            SetButtonEnabled(stopBtn, true);
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
            SetButtonEnabled(startBtn, true);
            SetButtonEnabled(stopBtn, false);
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
            // Force regeneration of alert media regardless of deduplication
            _forceFetchAlertGeneration = true;

            SetButtonEnabled(fetchBtn, false);
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
                        fetchBtn.Invoke(new Action(() => SetButtonEnabled(fetchBtn, true)));
                    else
                        SetButtonEnabled(fetchBtn, true);

                    _operationCts?.Dispose();
                    _operationCts = null;
                    SetCancelState(false);
                }
            });
        }

        private void StillClicked(Button stillBtn)
        {
            SetButtonEnabled(stillBtn, false);
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
                        stillBtn.Invoke(new Action(() => SetButtonEnabled(stillBtn, true)));
                    else
                        SetButtonEnabled(stillBtn, true);

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
            SetButtonEnabled(_cancelBtn, enabled);
        }

        private void OnMessageLogged(string text, Logger.LogLevel level)
        {
            // Keep a copy of everything with explicit level, then reapply filters for the view.
            // Buffer add happens ONCE here, regardless of which thread we're on.
            lock (_logBuffer)
            {
                _logBuffer.Add((text, level));
                // Keep the buffer bounded to avoid runaway memory usage
                if (_logBuffer.Count > 5000) _logBuffer.RemoveRange(0, _logBuffer.Count - 5000);
            }

            // UI updates must be on UI thread — call the UI-only helper, not ourselves (avoids double buffer add)
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => UpdateLogUI(text, level)));
                return;
            }

            UpdateLogUI(text, level);
        }

        /// <summary>UI-thread-only helper: updates status indicators and appends/refreshes the log view.</summary>
        private void UpdateLogUI(string text, Logger.LogLevel level)
        {
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

        private void ToggleLogsVisibility()
        {
            if (_tabControl == null) return;
            
            bool areLogsVisible = _tabControl.Visible;
            SetLogsCollapsed(areLogsVisible);
            
            // Save state to config
            try
            {
                var config = ConfigManager.LoadConfig();
                config.LogsCollapsed = areLogsVisible;
                ConfigManager.SaveConfig(config, silent: true);
            }
            catch { /* Ignore save errors */ }
        }

        private void SetLogsCollapsed(bool collapsed)
        {
            if (_tabControl == null || _toggleLogsBtn == null || _splitContainer == null) return;
            
            if (collapsed)
            {
                // Save current splitter distance before collapsing
                _savedSplitterDistance = _splitContainer.SplitterDistance;
                
                // Use the built-in Panel2Collapsed property — the correct way to hide a SplitterPanel
                _splitContainer.Panel2Collapsed = true;
                _toggleLogsBtn.Text = "▲ Logs";
            }
            else
            {
                // Restore Panel2 using the built-in property
                _splitContainer.Panel2Collapsed = false;
                
                // Restore previous splitter position
                if (_savedSplitterDistance > 0)
                {
                    _splitContainer.SplitterDistance = _savedSplitterDistance;
                }
                else
                {
                    // Default if no saved distance (35% for logs)
                    _splitContainer.SplitterDistance = (int)(_splitContainer.Height * 0.35);
                }
                _toggleLogsBtn.Text = "▼ Logs";
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
            SetTightLineSpacing(rtb, _logLineSpacingDy); // Reapply tight spacing after clear resets RTF formatting
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

            // Apply line spacing to ALL text AFTER everything has been appended
            SetTightLineSpacing(rtb, _logLineSpacingDy);
            rtb.ScrollToCaret();
        }

        private bool PassesFilter(string line, string filter, string search, Logger.LogLevel level, string verbosity)
        {
            if (!string.IsNullOrEmpty(search) && !line.Contains(search, StringComparison.OrdinalIgnoreCase)) return false;

            var lower = line.ToLowerInvariant();

            // ── Step 1: Verbosity controls overall noise level ──────────
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
            // Verbose: show everything (no filtering by verbosity)

            // ── Step 2: Apply category filter on the remaining messages ─
            return filter switch
            {
                "All" => true,
                "Errors" => level == Logger.LogLevel.Error,
                "Warnings" => level == Logger.LogLevel.Warning || level == Logger.LogLevel.Error,
                "Info" => level == Logger.LogLevel.Info,
                _ => true
            };
        }

        // Format log line with appropriate category icon for better visual scanning
        // ── UI Log Rendering ─────────────────────────────────────────────────
        // Category → (icon, color) mapping for the RichTextBox UI log
        private static readonly Dictionary<string, (string Icon, Color Color)> _uiCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            { "OpenMeteo",       ("🌡", Color.FromArgb(77, 208, 225)) },
            { "ECCC",            ("🍁", Color.FromArgb(77, 208, 225)) },
            { "ECCC API",        ("🍁", Color.FromArgb(77, 208, 225)) },
            { "ECCC Fallback",   ("🍁", Color.FromArgb(255, 183, 77)) },
            { "ECCC+OpenMeteo",  ("🔗", Color.FromArgb(77, 208, 225)) },
            { "Hybrid",          ("🔗", Color.FromArgb(77, 208, 225)) },
            { "OpenMeteo Retry", ("🔄", Color.FromArgb(255, 183, 77)) },
            { "ECCC CAP",        ("📋", Color.FromArgb(255, 183, 77)) },
            { "Alerts",          ("🔔", Color.FromArgb(255, 183, 77)) },
            { "AlertReady",      ("🚨", Color.FromArgb(255, 82, 82)) },
            { "NAAD",            ("📡", Color.FromArgb(255, 183, 77)) },
            { "RadarAnimation",  ("📡", Color.FromArgb(186, 104, 200)) },
            { "Radar",           ("📡", Color.FromArgb(186, 104, 200)) },
            { "Radar Animation", ("📡", Color.FromArgb(186, 104, 200)) },
            { "MapCache",        ("🗺", Color.FromArgb(77, 182, 172)) },
            { "GlobalWeatherMap",("🌍", Color.FromArgb(77, 182, 172)) },
            { "OpenMap",         ("🗺", Color.FromArgb(77, 182, 172)) },
            { "Weather Map",     ("🗺", Color.FromArgb(77, 182, 172)) },
            { "FFmpeg",          ("🎬", Color.FromArgb(100, 181, 246)) },
            { "MUSIC",           ("🎵", Color.FromArgb(186, 104, 200)) },
            { "OVERLAY",         ("🎞", Color.FromArgb(100, 181, 246)) },
            { "AUDIO",           ("🔊", Color.FromArgb(100, 181, 246)) },
            { "RUNNING",         ("▶", Color.FromArgb(100, 181, 246)) },
            { "DONE",            ("■", Color.FromArgb(129, 199, 132)) },
            { "FAIL",            ("✖", Color.FromArgb(255, 82, 82)) },
            { "CLEANUP",         ("🧹", Color.FromArgb(120, 120, 120)) },
            { "MEMORY",          ("💾", Color.FromArgb(120, 120, 120)) },
            { "PiperTTS",        ("🗣", Color.FromArgb(186, 104, 200)) },
            { "EdgeTTS",         ("🗣", Color.FromArgb(186, 104, 200)) },
            { "SAPI",            ("🗣", Color.FromArgb(186, 104, 200)) },
            { "WebUI",           ("🌐", Color.FromArgb(129, 199, 132)) },
            { "Boot",            ("⚡", Color.FromArgb(224, 224, 224)) },
            { "INFO",            ("ℹ", Color.FromArgb(144, 202, 249)) },
        };

        private static readonly System.Text.RegularExpressions.Regex _uiTagRegex = 
            new(@"^\[([^\]]+)\]", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Returns true if the line is a section/banner header (── or ═══).
        /// </summary>
        private static bool IsUiSectionLine(string trimmed)
        {
            return trimmed.StartsWith("──") || trimmed.StartsWith("═══");
        }

        /// <summary>
        /// Extract [Tag] from a log line, returning (tag, bodyAfterTag).
        /// Returns (null, original) if no tag found.
        /// </summary>
        private static (string? Tag, string Body) UiExtractTag(string text)
        {
            // Strip timestamp prefix like "[14:37:08] " first
            var work = text;
            if (work.Length > 10 && work[0] == '[' && work[9] == ']' && work[3] == ':' && work[6] == ':')
                work = work.Substring(11).TrimStart();

            var m = _uiTagRegex.Match(work);
            if (m.Success)
            {
                var tag = m.Groups[1].Value;
                var body = work.Substring(m.Length).TrimStart();
                return (tag, body);
            }
            return (null, work);
        }

        // Append a single line to the RichTextBox with professional formatting

        // (RTF fallback methods removed — EM_SETPARAFORMAT with rule 4 handles spacing reliably)

        private void AppendColoredLine(RichTextBox rtb, string line, string search, Logger.LogLevel level)
        {
            if (rtb == null || string.IsNullOrEmpty(line)) return;

            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                rtb.AppendText(Environment.NewLine);
                ApplyTightSpacingToCurrentParagraph(rtb, _logLineSpacingDy);
                rtb.SelectionStart = rtb.TextLength;
                rtb.ScrollToCaret();
                return;
            }

            var baseFont = rtb.Font; // Consolas 9.5
            var boldFont = new Font(baseFont, FontStyle.Bold);
            var italicFont = new Font(baseFont, FontStyle.Italic);

            // ── Section / Banner headers ────────────────────────────────────
            if (IsUiSectionLine(trimmed))
            {
                // Add a blank line before section for spacing
                rtb.AppendText(Environment.NewLine);

                // Section title — extract text between ── markers
                var title = trimmed.Trim('─', '═', ' ', '—');

                // Build: "  ▸ SECTION TITLE" with accent color and bold
                var sectionLine = $"  ▸ {title.ToUpperInvariant()}" + Environment.NewLine;
                int start = rtb.TextLength;
                rtb.AppendText(sectionLine);
                rtb.Select(start, sectionLine.Length);
                rtb.SelectionColor = Color.FromArgb(100, 181, 246); // Accent blue
                rtb.SelectionFont = boldFont;

                // Thin separator line under it
                var sepLine = "  " + new string('─', Math.Min(70, Math.Max(30, title.Length + 8))) + Environment.NewLine;
                int sepStart = rtb.TextLength;
                rtb.AppendText(sepLine);
                rtb.Select(sepStart, sepLine.Length);
                rtb.SelectionColor = Color.FromArgb(60, 70, 80);
                rtb.SelectionFont = baseFont;

                rtb.SelectionStart = rtb.TextLength;
                rtb.ScrollToCaret();
                return;
            }

            // ── Extract parts from the line ─────────────────────────────────
            var (tag, body) = UiExtractTag(line);

            // Extract timestamp
            string timestamp = "";
            var rawLine = line.TrimStart();
            if (rawLine.Length > 10 && rawLine[0] == '[' && rawLine[9] == ']' && rawLine[3] == ':' && rawLine[6] == ':')
            {
                timestamp = rawLine.Substring(0, 10); // "[HH:mm:ss]"
                // body is already stripped of timestamp by UiExtractTag
            }

            // Detect status
            bool isSuccess = body.StartsWith("✓") || body.StartsWith("✔") || trimmed.Contains("[DONE]");
            bool isError = body.StartsWith("✗") || body.StartsWith("✖") || level == Logger.LogLevel.Error || trimmed.Contains("[FAIL]");
            bool isWarning = body.StartsWith("⚠") || level == Logger.LogLevel.Warning;
            bool isDebug = level == Logger.LogLevel.Debug;

            // Clean body of redundant status symbols (we show them as colored indicators)
            var displayBody = body;
            if (displayBody.StartsWith("✓ ") || displayBody.StartsWith("✔ ")) displayBody = displayBody.Substring(2);
            if (displayBody.StartsWith("✗ ") || displayBody.StartsWith("✖ ")) displayBody = displayBody.Substring(2);
            if (displayBody.StartsWith("⚠ ")) displayBody = displayBody.Substring(2);
            // Also strip duplicate [Tag] from body
            if (tag != null && displayBody.StartsWith($"[{tag}]", StringComparison.OrdinalIgnoreCase))
                displayBody = displayBody.Substring(tag.Length + 2).TrimStart();

            // ── Build the formatted line ────────────────────────────────────
            int lineStart = rtb.TextLength;

            // 1) Timestamp (dimmed)
            if (!string.IsNullOrEmpty(timestamp))
            {
                int tsStart = rtb.TextLength;
                rtb.AppendText(timestamp + " ");
                rtb.Select(tsStart, timestamp.Length + 1);
                rtb.SelectionColor = Color.FromArgb(90, 100, 110);
                rtb.SelectionFont = baseFont;
            }

            // 2) Status indicator dot/icon
            string statusIcon;
            Color statusColor;
            if (isError) { statusIcon = "● "; statusColor = Color.FromArgb(255, 82, 82); }
            else if (isWarning) { statusIcon = "● "; statusColor = Color.FromArgb(255, 213, 79); }
            else if (isSuccess) { statusIcon = "● "; statusColor = Color.FromArgb(129, 199, 132); }
            else if (isDebug) { statusIcon = "· "; statusColor = Color.FromArgb(90, 100, 110); }
            else { statusIcon = "  "; statusColor = Color.FromArgb(60, 70, 80); }

            int dotStart = rtb.TextLength;
            rtb.AppendText(statusIcon);
            rtb.Select(dotStart, statusIcon.Length);
            rtb.SelectionColor = statusColor;
            rtb.SelectionFont = baseFont;

            // 3) Category tag (colored badge)
            if (tag != null)
            {
                Color tagColor;
                string tagIcon;
                if (_uiCategories.TryGetValue(tag, out var catInfo))
                {
                    tagIcon = catInfo.Icon;
                    tagColor = catInfo.Color;
                }
                else
                {
                    tagIcon = "›";
                    tagColor = Color.FromArgb(158, 158, 158);
                }

                var tagText = $"{tagIcon} {tag}  ";
                int tagStart = rtb.TextLength;
                rtb.AppendText(tagText);
                rtb.Select(tagStart, tagText.Length);
                rtb.SelectionColor = tagColor;
                rtb.SelectionFont = boldFont;
            }

            // 4) Message body
            Color bodyColor;
            FontStyle bodyStyle = FontStyle.Regular;
            if (isSuccess) { bodyColor = Color.FromArgb(129, 199, 132); }
            else if (isError) { bodyColor = Color.FromArgb(255, 82, 82); bodyStyle = FontStyle.Bold; }
            else if (isWarning) { bodyColor = Color.FromArgb(255, 213, 79); }
            else if (isDebug) { bodyColor = Color.FromArgb(120, 130, 140); }
            else if (tag != null && _uiCategories.TryGetValue(tag, out var bodyTagInfo))
            {
                // Slightly desaturated version of category color for body text
                bodyColor = Color.FromArgb(
                    Math.Min(255, bodyTagInfo.Color.R + 40),
                    Math.Min(255, bodyTagInfo.Color.G + 40),
                    Math.Min(255, bodyTagInfo.Color.B + 40));
            }
            else { bodyColor = Color.FromArgb(200, 210, 220); }

            int bodyStart = rtb.TextLength;
            rtb.AppendText(displayBody + Environment.NewLine);
            rtb.Select(bodyStart, displayBody.Length);
            rtb.SelectionColor = bodyColor;
            rtb.SelectionFont = new Font(baseFont, bodyStyle);

            // ── Highlight search matches ────────────────────────────────────
            if (!string.IsNullOrEmpty(search))
            {
                int offset = 0;
                var fullText = line;
                while (true)
                {
                    int pos = fullText.IndexOf(search, offset, StringComparison.OrdinalIgnoreCase);
                    if (pos < 0) break;
                    // Map position relative to what we appended
                    int absPos = lineStart + pos;
                    if (absPos >= 0 && absPos + search.Length <= rtb.TextLength)
                    {
                        rtb.Select(absPos, search.Length);
                        rtb.SelectionBackColor = Color.FromArgb(255, 235, 59);
                        rtb.SelectionColor = Color.Black;
                    }
                    offset = pos + search.Length;
                }
            }

            // Reset
            ApplyTightSpacingToCurrentParagraph(rtb, _logLineSpacingDy);
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionBackColor = rtb.BackColor;
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
            var locations = config.Locations?.GetLocationsArray() ?? Array.Empty<string>();
            
            string locationName = (selectedIndex < locations.Length) ? (locations[selectedIndex] ?? $"Location {selectedIndex}") : $"Location {selectedIndex}";
            
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

            SetOverallProgress(pct, status ?? string.Empty);
        }

        // Called when ffmpeg/video reports a fine-grained percent (0-100)
        private void OnVideoProgress(double pct, string status)
        {
            // If a video phase mapping exists, map ffmpeg percent into overall percent
            if (_videoActive)
            {
                var overall = _videoBase + (pct / 100.0) * _videoRange;
                SetOverallProgress(overall, status ?? string.Empty);
            }
            else
            {
                // No mapping known; show raw percent
                SetOverallProgress(pct, status ?? string.Empty);
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

            // Enhanced status display with professional icons and color coding
            if (status == null) status = string.Empty;
            var lower = status.ToLowerInvariant();
            string icon = "✦";  // Default icon
            Color statusColor = _themeTextColor;
            
            // Determine status type and apply appropriate styling
            if (lower.Contains("error") || lower.Contains("failed") || lower.Contains("fail"))
            {
                icon = "✖";
                statusColor = _themeDangerColor;
            }
            else if (lower.Contains("complete") || lower.Contains("success") || lower.Contains("done") || lower.Contains("finished"))
            {
                icon = "✓";
                statusColor = _themeSuccessColor;
            }
            else if (lower.Contains("encoding") || lower.Contains("video") || lower.Contains("generating"))
            {
                icon = "◉";
                statusColor = Color.FromArgb(155, 89, 182);  // Purple for media processing
            }
            else if (lower.Contains("running") || lower.Contains("processing") || lower.Contains("working"))
            {
                icon = "◈";
                statusColor = _themeAccentColor;
            }
            else if (lower.Contains("fetch") || lower.Contains("download") || lower.Contains("loading"))
            {
                icon = "↻";
                statusColor = Color.FromArgb(52, 152, 219);  // Blue for network operations
            }
            else if (lower.Contains("wait") || lower.Contains("sleep") || lower.Contains("idle"))
            {
                icon = "◇";
                statusColor = Color.FromArgb(149, 165, 166);  // Gray for idle/waiting
            }
            else if (lower.Contains("start") || lower.Contains("begin") || lower.Contains("init"))
            {
                icon = "▶";
                statusColor = _themeSuccessColor;
            }
            else if (lower.Contains("stop") || lower.Contains("cancel") || lower.Contains("abort"))
            {
                icon = "■";
                statusColor = _themeWarningColor;
            }
            
            _statusLabel.ForeColor = statusColor;
            
            // Format status with icon - ensure proper spacing
            string formattedStatus = $"{icon} {status}";
            _statusLabel.Text = formattedStatus;
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
            SetButtonEnabled(_testAlertBtn!, false);
            _testAlertBtn!.Text = "⏳ Selecting alert type...";

            try
            {
                // Show selection dialog
                using var selectionForm = new TestAlertSelectionForm();
                if (selectionForm.ShowDialog(this) != DialogResult.OK)
                {
                    Logger.Log("Test alert generation cancelled.", Logger.LogLevel.Info);
                    return;
                }

                _testAlertBtn!.Text = "⏳ Generating...";

                string selectedCountry = selectionForm.SelectedCountry;
                string selectedAlertType = selectionForm.SelectedAlertType;

                await Task.Run(() =>
                {
                    Logger.Log($"Generating test alert: {selectedAlertType} ({selectedCountry})...", Logger.LogLevel.Info);

                    // Create output directory
                    string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestAlerts");
                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    // Get current config for language
                    var cfg = ConfigManager.LoadConfig();
                    string language = cfg.AlertReady?.PreferredLanguage ?? "fr-CA";

                    string testAlertXml;
                    List<AlertEntry> alerts = new();

                    // Generate appropriate alert based on selection
                    if (selectedCountry.StartsWith("Canada"))
                    {
                        testAlertXml = GenerateAlertReadyTestAlert(selectedAlertType, language);
                        alerts = ParseCapAlert(testAlertXml);
                        // Set provider for all alerts
                        foreach (var a in alerts)
                        {
                            a.Provider = "Canada_AlertReady";
                        }
                    }
                    else
                    {
                        testAlertXml = GenerateNwsTestAlert(selectedAlertType);
                        alerts = ParseCapAlert(testAlertXml);
                        // Set provider for all alerts
                        foreach (var a in alerts)
                        {
                            a.Provider = "USA_NWS";
                            // For NWS, set issued/expires times
                            a.IssuedAt = DateTimeOffset.Now;
                            a.ExpiresAt = DateTimeOffset.Now.AddHours(1);
                        }
                    }

                    if (alerts.Count > 0)
                    {
                        Logger.Log($"Parsed {alerts.Count} test alert(s). Generating media and video...", Logger.LogLevel.Info);
                        
                        // Log alert details for debugging
                        for (int i = 0; i < alerts.Count; i++)
                        {
                            var alert = alerts[i];
                            Logger.Log($"  Alert {i + 1}: Title='{alert.Title}', City='{alert.City}', Type='{alert.Type}', Summary length={alert.Summary?.Length ?? 0}", Logger.LogLevel.Debug);
                        }

                        try
                        {
                            // Show the first alert in a live Windows Form (exact replica of the video frame)
                            if (alerts.Count > 0)
                            {
                                AlertDisplayForm.ShowAlert(alerts[0], language, autoCloseSeconds: 120);
                            }

                            // Use the new method that generates both media AND video automatically
                            var (generatedFiles, videoPath) = EmergencyAlertGenerator.GenerateEmergencyAlertsWithVideo(
                                alerts,
                                outputDir,
                                language
                            );

                            Logger.Log($"Generated {generatedFiles.Count} file(s) in TestAlerts folder.", Logger.LogLevel.Info);

                            if (!string.IsNullOrEmpty(videoPath))
                            {
                                Logger.Log($"✓ Alert video generated: {videoPath}", Logger.LogLevel.Info);
                            }

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
                        catch (Exception genEx)
                        {
                            Logger.Log($"Error during media generation: {genEx.Message}", Logger.LogLevel.Error);
                            Logger.Log($"Stack trace: {genEx.StackTrace}", Logger.LogLevel.Debug);
                            throw; // Re-throw to be caught by outer handler
                        }
                    }
                    else
                    {
                        Logger.Log("Failed to parse test alert.", Logger.LogLevel.Warning);
                    }
                }).ConfigureAwait(true);  // Ensure we return to UI thread

                Logger.Log("Test alert generation completed.", Logger.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"Test alert generation failed: {ex.Message}", Logger.LogLevel.Error);
                if (ex.InnerException != null)
                {
                    Logger.Log($"Inner exception: {ex.InnerException.Message}", Logger.LogLevel.Debug);
                }
                try
                {
                    MessageBox.Show($"Failed to generate test alert: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { /* If MessageBox fails too */ }
            }
            finally
            {
                // Ensure we're on the UI thread for button updates
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() =>
                    {
                        SetButtonEnabled(_testAlertBtn!, true);
                        _testAlertBtn!.Text = "🧪 Emergency Test Alert";
                    }));
                }
                else
                {
                    SetButtonEnabled(_testAlertBtn!, true);
                    _testAlertBtn!.Text = "🧪 Emergency Test Alert";
                }
            }
        }

        /// <summary>
        /// Generates AlertReady test alert XML based on alert type
        /// </summary>
        private string GenerateAlertReadyTestAlert(string alertType, string language)
        {
            return alertType switch
            {
                "AMBER Alert - Missing Child" => TestAlertGenerator.GenerateAmberAlert(language),
                "Civil Emergency - Public Safety" => TestAlertGenerator.GenerateCivilEmergencyAlert(language),
                "Public Safety Advisory" => TestAlertGenerator.GeneratePublicSafetyAlert(language),
                _ => TestAlertGenerator.GenerateAmberAlert(language)
            };
        }

        /// <summary>
        /// Generates NWS test alert XML based on alert type
        /// </summary>
        private string GenerateNwsTestAlert(string alertType)
        {
            return alertType switch
            {
                "Tornado Warning" => EAS.NWS.TestNwsAlerts.GenerateTornadoWarning(),
                "Severe Thunderstorm Warning" => EAS.NWS.TestNwsAlerts.GenerateSevereThunderstormWarning(),
                "Winter Weather Advisory" => EAS.NWS.TestNwsAlerts.GenerateWinterWeatherAdvisory(),
                "Flash Flood Warning" => EAS.NWS.TestNwsAlerts.GenerateFloodWarning(),
                "Heat Advisory" => EAS.NWS.TestNwsAlerts.GenerateHeatAdvisory(),
                _ => EAS.NWS.TestNwsAlerts.GenerateTornadoWarning()
            };
        }

        /// <summary>
        /// Parses CAP (Common Alerting Protocol) XML into AlertEntry objects
        /// </summary>
        private List<AlertEntry> ParseCapAlert(string xml)
        {
            var results = new List<AlertEntry>();

            try
            {
                if (string.IsNullOrWhiteSpace(xml)) return results;

                var doc = XDocument.Parse(xml);
                var root = doc.Root;

                if (root?.Name.LocalName != "alert") return results;

                // Extract basic alert information
                var identifier = GetCapValue(root, "identifier");
                var status = GetCapValue(root, "status");
                var scope = GetCapValue(root, "scope");

                // Only process Actual and Public alerts
                if (!string.Equals(status, "Actual", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(status, "Test", StringComparison.OrdinalIgnoreCase))
                {
                    return results;
                }

                if (!string.Equals(scope, "Public", StringComparison.OrdinalIgnoreCase))
                {
                    return results;
                }

                // Get info element (can have multiple for different languages)
                var infoElement = root.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("info", StringComparison.OrdinalIgnoreCase));

                if (infoElement == null) return results;

                var eventName = GetCapValue(infoElement, "event") ?? "Emergency Alert";
                var headline = GetCapValue(infoElement, "headline") ?? eventName;
                var description = GetCapValue(infoElement, "description") ?? "An emergency alert has been issued for your area.";
                var instruction = GetCapValue(infoElement, "instruction") ?? "";
                var severity = GetCapValue(infoElement, "severity") ?? "Unknown";
                var urgency = GetCapValue(infoElement, "urgency") ?? "Unknown";
                var category = GetCapValue(infoElement, "category") ?? "Safety";

                // Get area information
                var areaElement = infoElement.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("area", StringComparison.OrdinalIgnoreCase));

                var areaDesc = GetCapValue(areaElement, "areaDesc") ?? "Alert Area";

                // Build summary - ensure we have content
                var summaryParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(description))
                    summaryParts.Add(description.Trim());
                if (!string.IsNullOrWhiteSpace(instruction))
                    summaryParts.Add(instruction.Trim());

                var summary = summaryParts.Count > 0
                    ? string.Join("  ", summaryParts)
                    : "An emergency alert has been issued for your area. Please remain alert and follow instructions from local authorities.";

                // Determine color based on severity
                var color = MapSeverityToColor(severity);

                var alert = new AlertEntry
                {
                    City = !string.IsNullOrWhiteSpace(areaDesc) ? areaDesc : "Alert Area",
                    Type = !string.IsNullOrWhiteSpace(eventName) ? eventName : "Emergency Alert",
                    Title = !string.IsNullOrWhiteSpace(headline) ? headline : "Emergency Alert",
                    Summary = !string.IsNullOrWhiteSpace(summary) ? summary : "An emergency alert has been issued.",
                    SeverityColor = !string.IsNullOrWhiteSpace(color) ? color : "Red",
                    Confidence = urgency,
                    Impact = urgency
                };

                results.Add(alert);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error parsing CAP alert: {ex.Message}", Logger.LogLevel.Warning);
            }

            return results;
        }

        /// <summary>
        /// Gets value from CAP XML element by local name
        /// </summary>
        private static string? GetCapValue(XElement? parent, string localName)
        {
            if (parent == null) return null;

            var child = parent.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

            return child?.Value?.Trim();
        }

        /// <summary>
        /// Maps CAP alert severity to display color
        /// </summary>
        private static string MapSeverityToColor(string? severity)
        {
            if (string.IsNullOrWhiteSpace(severity)) return "Gray";

            var sev = severity.Trim().ToLowerInvariant();
            return sev switch
            {
                "extreme" or "severe" => "Red",
                "moderate" or "minor" => "Yellow",
                _ => "Gray"
            };
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

                _naadHttpClient = new System.Net.Http.HttpClient();
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

                _naadClient = new AlertReadyClient(_naadHttpClient, options);
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
                _naadCts?.Dispose();
                _naadCts = null;
                
                _naadClient.Dispose();
                _naadClient = null;
                
                _naadHttpClient?.Dispose();
                _naadHttpClient = null;
                
                UpdateNaadConnectionStatus(NaadConnectionStatus.Disconnected, "Stopped");
                Logger.Log("NAAD listener stopped.", Logger.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error stopping NAAD listener: {ex.Message}", Logger.LogLevel.Warning);
            }
        }

        #region NWS Background Polling

        private void StartNwsPolling(AppSettings cfg)
        {
            if (_nwsPollCts != null) return; // already polling

            try
            {
                var nwsOptions = cfg.Nws ?? new EAS.NWS.NwsOptions();
                int intervalMinutes = Math.Max(1, nwsOptions.PollingIntervalMinutes);

                Logger.Log($"Starting NWS background polling (every {intervalMinutes} min)...", Logger.LogLevel.Info);
                Logger.Log($"NWS config: States={string.Join(",", nwsOptions.States ?? new())}, Zones={string.Join(",", nwsOptions.Zones ?? new())}, Point={nwsOptions.Point ?? "none"}", Logger.LogLevel.Info);

                _nwsHttpClient = new System.Net.Http.HttpClient();
                _nwsHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(nwsOptions.UserAgent);
                _nwsPollCts = new CancellationTokenSource();
                _nwsSeenAlertIds.Clear();

                // Show the NWS panel
                if (_nwsPanel != null)
                {
                    _nwsPanel.Visible = true;
                }

                UpdateNwsStatus("● Polling", Color.FromArgb(46, 204, 113));

                var token = _nwsPollCts.Token;
                _ = Task.Run(async () => await NwsPollingLoopAsync(nwsOptions, token), token);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to start NWS polling: {ex.Message}", Logger.LogLevel.Error);
                UpdateNwsStatus("✗ Error", Color.FromArgb(231, 76, 60));
            }
        }

        private void StopNwsPolling()
        {
            try
            {
                _nwsPollCts?.Cancel();
                _nwsPollCts?.Dispose();
                _nwsPollCts = null;

                _nwsHttpClient?.Dispose();
                _nwsHttpClient = null;

                _nwsSeenAlertIds.Clear();

                UpdateNwsStatus("○ Stopped", Color.FromArgb(120, 130, 150));
                Logger.Log("NWS polling stopped.", Logger.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error stopping NWS polling: {ex.Message}", Logger.LogLevel.Warning);
            }
        }

        private async Task NwsPollingLoopAsync(EAS.NWS.NwsOptions options, CancellationToken token)
        {
            int intervalMs = Math.Max(60000, options.PollingIntervalMinutes * 60 * 1000);

            // Initial fetch immediately
            await NwsPollOnceAsync(options, token);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(intervalMs, token);
                }
                catch (TaskCanceledException) { break; }

                if (token.IsCancellationRequested) break;
                await NwsPollOnceAsync(options, token);
            }
        }

        private async Task NwsPollOnceAsync(EAS.NWS.NwsOptions options, CancellationToken token)
        {
            try
            {
                if (_nwsHttpClient == null) return;

                // Update status to "Polling..."
                SafeInvoke(() =>
                {
                    if (_nwsStatusLabel != null)
                    {
                        _nwsStatusLabel.Text = "◐ Polling...";
                        _nwsStatusLabel.ForeColor = Color.FromArgb(241, 196, 15); // Yellow
                    }
                });

                var nwsClient = new EAS.NWS.NwsClient(_nwsHttpClient, options)
                {
                    Log = msg => Logger.Log($"[NWS] {msg}", Logger.LogLevel.Info)
                };

                var cfg = ConfigManager.LoadConfig();
                var locations = cfg.Locations?.GetLocationsArray()?.Where(l => l != null).Select(l => l!).ToArray() ?? Array.Empty<string>();

                var alerts = await nwsClient.FetchAlertsAsync(locations);

                var now = DateTime.Now;

                // Check for new alerts not seen before
                var newAlerts = new List<AlertEntry>();
                foreach (var alert in alerts)
                {
                    string alertId = alert.Identifier ?? $"{alert.Title}_{alert.IssuedAt}";
                    if (_nwsSeenAlertIds.Add(alertId))
                    {
                        newAlerts.Add(alert);
                    }
                }

                // Update UI
                SafeInvoke(() =>
                {
                    if (_nwsStatusLabel != null)
                    {
                        _nwsStatusLabel.Text = "● Active";
                        _nwsStatusLabel.ForeColor = Color.FromArgb(46, 204, 113); // Green
                    }
                    if (_nwsLastFetchLabel != null)
                    {
                        _nwsLastFetchLabel.Text = $"⏰ {now:HH:mm:ss}";
                        _nwsLastFetchLabel.ForeColor = Color.FromArgb(155, 170, 190);
                    }
                    if (_nwsAlertLabel != null)
                    {
                        string icon = alerts.Count > 0 ? "▲" : "△";
                        _nwsAlertLabel.Text = $"{icon} {alerts.Count} alert{(alerts.Count != 1 ? "s" : "")}";
                        _nwsAlertLabel.ForeColor = alerts.Count > 0
                            ? Color.FromArgb(241, 196, 15)    // Warning yellow
                            : Color.FromArgb(140, 150, 170);  // Muted
                    }
                });

                Logger.Log($"[NWS] Polled: {alerts.Count} active alert(s), {newAlerts.Count} new.", Logger.LogLevel.Info);

                // Always show alert forms for new alerts; only generate video when cycle is running
                foreach (var alert in newAlerts)
                {
                    if (token.IsCancellationRequested) break;
                    Logger.Log($"[NWS] New alert detected: {alert.Title}", Logger.LogLevel.Info);

                    var nwsCfg = ConfigManager.LoadConfig();
                    string nwsLang = nwsCfg.AlertReady?.PreferredLanguage ?? "fr-CA";
                    AlertDisplayForm.ShowAlert(alert, nwsLang, autoCloseSeconds: 120);

                    if (_cts != null)
                    {
                        await GenerateAlertMediaAsync(alert, skipFormDisplay: true);
                    }
                    else
                    {
                        Logger.Log($"[NWS] Alert shown: {alert.Title} (video skipped \u2014 cycle not running)", Logger.LogLevel.Info);
                    }
                }

                // Prune old seen IDs to prevent unbounded growth (keep last 5000)
                if (_nwsSeenAlertIds.Count > 5000)
                {
                    _nwsSeenAlertIds.Clear();
                    foreach (var a in alerts)
                    {
                        string id = a.Identifier ?? $"{a.Title}_{a.IssuedAt}";
                        _nwsSeenAlertIds.Add(id);
                    }
                }
            }
            catch (TaskCanceledException) { /* expected on shutdown */ }
            catch (Exception ex)
            {
                Logger.Log($"[NWS] Polling error: {ex.Message}", Logger.LogLevel.Error);
                SafeInvoke(() =>
                {
                    if (_nwsStatusLabel != null)
                    {
                        _nwsStatusLabel.Text = "✗ Error";
                        _nwsStatusLabel.ForeColor = Color.FromArgb(231, 76, 60); // Red
                    }
                });
            }
        }

        private void UpdateNwsStatus(string text, Color color)
        {
            SafeInvoke(() =>
            {
                if (_nwsStatusLabel != null)
                {
                    _nwsStatusLabel.Text = text;
                    _nwsStatusLabel.ForeColor = color;
                }
            });
        }

        private void SafeInvoke(Action action)
        {
            try
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    if (this.InvokeRequired)
                        this.BeginInvoke(action);
                    else
                        action();
                }
            }
            catch (ObjectDisposedException) { }
        }

        #endregion

        private void OnNaadConnectionChanged(object? sender, ConnectionStatusEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnNaadConnectionChanged(sender, e));
                return;
            }

            UpdateNaadConnectionStatus(e.Status, e.Message ?? string.Empty);
        }

        private void OnNaadHeartbeat(object? sender, HeartbeatEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnNaadHeartbeat(sender, e));
                return;
            }

            var localTime = e.Timestamp.ToLocalTime();
            _naadHeartbeatLabel!.Text = $"♡ {localTime:HH:mm:ss}";
            _naadHeartbeatLabel.ForeColor = Color.FromArgb(155, 170, 190);
        }

        private void OnNaadAlertReceived(object? sender, AlertReceivedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnNaadAlertReceived(sender, e));
                return;
            }

            // Enhanced alert display with animated feel
            string alertIcon = e.TotalActiveAlerts > 0 ? "▲" : "△";
            _naadAlertLabel!.Text = $"{alertIcon} {e.TotalActiveAlerts} alert{(e.TotalActiveAlerts != 1 ? "s" : "")}";
            _naadAlertLabel.ForeColor = e.TotalActiveAlerts > 0 
                ? Color.FromArgb(241, 196, 15)   // Bright warning yellow for active alerts
                : Color.FromArgb(140, 150, 170);  // Muted for no alerts

            // Log the alert
            Logger.Log($"[NAAD] Alert received: {e.Alert?.Title}", Logger.LogLevel.Info);

            // Always show the alert form; only generate video when cycle is running
            if (e.Alert != null)
            {
                var naadCfg = ConfigManager.LoadConfig();
                string naadLang = naadCfg.AlertReady?.PreferredLanguage ?? "fr-CA";
                AlertDisplayForm.ShowAlert(e.Alert, naadLang, autoCloseSeconds: 120);

                if (_cts != null)
                {
                    _ = Task.Run(() => GenerateAlertMediaAsync(e.Alert, skipFormDisplay: true));
                }
                else
                {
                    Logger.Log($"[NAAD] Alert shown: {e.Alert.Title} (video skipped \u2014 cycle not running)", Logger.LogLevel.Info);
                }
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
                    _naadConnectionLabel!.Text = "● Connected";
                    _naadConnectionLabel.ForeColor = Color.FromArgb(46, 204, 113);  // Bright green
                    break;
                case NaadConnectionStatus.Connecting:
                    _naadConnectionLabel!.Text = "◐ Connecting...";
                    _naadConnectionLabel.ForeColor = Color.FromArgb(241, 196, 15);  // Yellow
                    break;
                case NaadConnectionStatus.Disconnected:
                    _naadConnectionLabel!.Text = "○ Offline";
                    _naadConnectionLabel.ForeColor = Color.FromArgb(231, 76, 60);   // Red
                    break;
            }
        }

        private async Task GenerateAlertMediaAsync(AlertEntry alert, bool skipFormDisplay = false)
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

                Logger.Log($"Generating media and video for alert: {alert.Title}", Logger.LogLevel.Info);

                // Show the alert in a live Windows Form (exact replica of the video frame)
                if (!skipFormDisplay)
                {
                    AlertDisplayForm.ShowAlert(alert, language, autoCloseSeconds: 120);
                }

                var alerts = new List<AlertEntry> { alert };
                
                // Use the new method that generates both media AND video automatically
                var (generatedFiles, videoPath) = EmergencyAlertGenerator.GenerateEmergencyAlertsWithVideo(alerts, outputDir, language);

                Logger.Log($"Generated {generatedFiles.Count} file(s) for alert: {alert.Title}", Logger.LogLevel.Info);

                if (!string.IsNullOrEmpty(videoPath))
                {
                    Logger.Log($"✓ Alert video generated: {videoPath}", Logger.LogLevel.Info);
                }
                else
                {
                    Logger.Log("Alert video generation was skipped or failed.", Logger.LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to generate alert media: {ex.Message}", Logger.LogLevel.Error);
            }
        }

        // Custom progress bar that paints a centered overlay text (percentage) and supports a simple marquee animation.
        // Enhanced with modern professional styling: rounded corners, gradients, glow effects, and segment animation.
        internal class TextProgressBar : ProgressBar
        {
            private readonly System.Windows.Forms.Timer _marqueeTimer;
            private readonly System.Windows.Forms.Timer _pulseTimer;
            private int _marqueeOffset = 0;
            private int _marqueeWidth = 120;
            private float _pulsePhase = 0f;
            private int _cornerRadius = 6;
            
            // Modern color scheme
            private Color _progressStartColor = Color.FromArgb(0, 180, 120);   // Teal green
            private Color _progressEndColor = Color.FromArgb(0, 220, 160);     // Lighter teal
            private Color _progressGlowColor = Color.FromArgb(80, 0, 220, 160); // Glow overlay
            private Color _trackColor = Color.FromArgb(45, 55, 72);            // Dark slate
            private Color _trackBorderColor = Color.FromArgb(60, 70, 90);      // Subtle border
            private Color _segmentLineColor = Color.FromArgb(35, 255, 255, 255); // Subtle segments

            public TextProgressBar()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
                
                _marqueeTimer = new System.Windows.Forms.Timer { Interval = 35 };
                _marqueeTimer.Tick += (s, e) =>
                {
                    _marqueeOffset = (_marqueeOffset + 6) % (this.Width + _marqueeWidth);
                    this.Invalidate();
                };

                _pulseTimer = new System.Windows.Forms.Timer { Interval = 50 };
                _pulseTimer.Tick += (s, e) =>
                {
                    _pulsePhase = (_pulsePhase + 0.12f) % (float)(Math.PI * 2);
                    this.Invalidate();
                };
                _pulseTimer.Start();

                this.ForeColor = Color.White;
                this.BackColor = _trackColor;
                this.Height = 28;
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

            private System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
            {
                var path = new System.Drawing.Drawing2D.GraphicsPath();
                int d = radius * 2;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                
                var rect = this.ClientRectangle;
                rect.Width -= 1; rect.Height -= 1;

                // Draw track background with rounded corners
                using (var trackPath = CreateRoundedRectPath(rect, _cornerRadius))
                {
                    using (var trackBrush = new System.Drawing.Drawing2D.LinearGradientBrush(rect, 
                        Color.FromArgb(35, 45, 60), Color.FromArgb(50, 60, 75), 
                        System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                    {
                        e.Graphics.FillPath(trackBrush, trackPath);
                    }
                    // Inner shadow effect
                    using (var innerShadow = new Pen(Color.FromArgb(30, 0, 0, 0), 2f))
                    {
                        e.Graphics.DrawPath(innerShadow, trackPath);
                    }
                }

                if (this.Style == ProgressBarStyle.Marquee)
                {
                    // Animated marquee with glow
                    int w = Math.Min(_marqueeWidth, rect.Width - 4);
                    int x = _marqueeOffset - w;
                    var marRect = new Rectangle(x + 2, 3, w, rect.Height - 6);
                    if (marRect.Width > 0 && marRect.Right > 2)
                    {
                        using (var marPath = CreateRoundedRectPath(marRect, _cornerRadius - 2))
                        {
                            // Gradient fill
                            using (var marBrush = new System.Drawing.Drawing2D.LinearGradientBrush(marRect,
                                Color.FromArgb(60, 180, 255), Color.FromArgb(100, 200, 255),
                                System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                            {
                                e.Graphics.FillPath(marBrush, marPath);
                            }
                            // Glow overlay
                            using (var glowBrush = new SolidBrush(Color.FromArgb((int)(40 + 20 * Math.Sin(_pulsePhase)), 150, 220, 255)))
                            {
                                e.Graphics.FillPath(glowBrush, marPath);
                            }
                        }
                    }
                }
                else
                {
                    double pct = (this.Maximum > this.Minimum) ? (this.Value - this.Minimum) / (double)(this.Maximum - this.Minimum) : 0.0;
                    int progressWidth = (int)Math.Round((rect.Width - 4) * pct);
                    
                    if (progressWidth > 2)
                    {
                        var fillRect = new Rectangle(2, 3, progressWidth, rect.Height - 6);
                        using (var fillPath = CreateRoundedRectPath(fillRect, Math.Min(_cornerRadius - 2, progressWidth / 2)))
                        {
                            // Main gradient fill
                            using (var fillBrush = new System.Drawing.Drawing2D.LinearGradientBrush(fillRect,
                                _progressStartColor, _progressEndColor,
                                System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                            {
                                e.Graphics.FillPath(fillBrush, fillPath);
                            }

                            // Animated glow/shine overlay
                            float pulseIntensity = (float)(0.15 + 0.1 * Math.Sin(_pulsePhase));
                            using (var shineBrush = new System.Drawing.Drawing2D.LinearGradientBrush(fillRect,
                                Color.FromArgb((int)(255 * pulseIntensity), 255, 255, 255),
                                Color.FromArgb(0, 255, 255, 255),
                                System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                            {
                                e.Graphics.FillPath(shineBrush, fillPath);
                            }

                            // Draw subtle segment lines for professional look
                            int segmentSpacing = 30;
                            using (var segPen = new Pen(_segmentLineColor, 1f))
                            {
                                for (int sx = segmentSpacing; sx < progressWidth; sx += segmentSpacing)
                                {
                                    e.Graphics.DrawLine(segPen, 2 + sx, 5, 2 + sx, rect.Height - 5);
                                }
                            }
                        }
                    }
                }

                // Outer border
                using (var borderPath = CreateRoundedRectPath(rect, _cornerRadius))
                using (var borderPen = new Pen(_trackBorderColor, 1.5f))
                {
                    e.Graphics.DrawPath(borderPen, borderPath);
                }

                // Draw centered text with shadow for depth
                double displayPct = (this.Maximum > 0) ? (this.Value / (double)this.Maximum) * 100.0 : 0.0;
                string text = string.IsNullOrEmpty(this.Text) ? $"{(int)Math.Round(displayPct)}%" : this.Text;
                
                var textFont = new Font(this.Font.FontFamily, this.Font.Size, FontStyle.Bold);
                
                // Text shadow
                var shadowRect = rect;
                shadowRect.Offset(1, 1);
                TextRenderer.DrawText(e.Graphics, text, textFont, shadowRect, Color.FromArgb(100, 0, 0, 0),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                
                // Main text
                TextRenderer.DrawText(e.Graphics, text, textFont, rect, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                
                textFont.Dispose();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _marqueeTimer?.Stop();
                    _marqueeTimer?.Dispose();
                    _pulseTimer?.Stop();
                    _pulseTimer?.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Checks for updates on application startup and prompts the user if one is available
        /// </summary>
        private async Task CheckForUpdatesOnStartupAsync()
        {
            try
            {
                // Small delay to let the UI finish loading
                await Task.Delay(2000);
                
                var updateInfo = await UpdateService.CheckForUpdatesAsync();
                
                if (updateInfo.IsUpdateAvailable && !string.IsNullOrEmpty(updateInfo.DownloadUrl))
                {
                    var message = $"A new version of WSG is available!\n\n" +
                                  $"Current version: {updateInfo.CurrentVersion}\n" +
                                  $"Latest version: {updateInfo.LatestVersion}\n";
                    
                    if (updateInfo.PublishedAt.HasValue)
                    {
                        message += $"Released: {updateInfo.PublishedAt.Value:MMMM dd, yyyy}\n";
                    }
                    
                    message += "\nWould you like to download and install the update now?\n\n" +
                               "(You can disable this check in the About dialog)";
                    
                    var result = MessageBox.Show(
                        this,
                        message,
                        "Update Available",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);
                    
                    if (result == DialogResult.Yes)
                    {
                        // Show a simple progress form
                        await DownloadAndInstallUpdateWithProgressAsync(updateInfo.DownloadUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Startup update check failed: {ex.Message}", Logger.LogLevel.Warning);
                // Don't show error to user for startup check - silent fail
            }
        }

        /// <summary>
        /// Downloads and installs update with a progress dialog
        /// </summary>
        private async Task DownloadAndInstallUpdateWithProgressAsync(string downloadUrl)
        {
            using var progressForm = new Form
            {
                Text = "Downloading Update",
                Width = 450,
                Height = 150,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var progressBar = new ProgressBar
            {
                Left = 20,
                Top = 30,
                Width = 390,
                Height = 25,
                Style = ProgressBarStyle.Continuous
            };

            var statusLabel = new Label
            {
                Left = 20,
                Top = 65,
                Width = 390,
                Height = 25,
                Text = "Starting download..."
            };

            progressForm.Controls.Add(progressBar);
            progressForm.Controls.Add(statusLabel);

            var progress = new Progress<(int Percent, string Status)>(p =>
            {
                progressBar.Value = Math.Min(100, p.Percent);
                statusLabel.Text = p.Status;
            });

            progressForm.Show(this);
            progressForm.Refresh();

            try
            {
                var (success, message) = await UpdateService.DownloadAndInstallUpdateAsync(downloadUrl, progress);

                progressForm.Hide();

                if (success)
                {
                    var restart = MessageBox.Show(
                        this,
                        message + "\n\nRestart now?",
                        "Update Complete",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (restart == DialogResult.Yes)
                    {
                        UpdateService.RestartApplication();
                    }
                }
                else
                {
                    MessageBox.Show(this, message, "Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                progressForm.Hide();
                MessageBox.Show(this, $"Update failed: {ex.Message}", "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                this.Width = 700;
                this.Height = 600;
                this.MaximizeBox = false;
                this.MinimizeBox = false;
                this.BackColor = Color.WhiteSmoke;

                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var product = asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product
                              ?? asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title
                              ?? "Weather Image Generator";
                var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                              ?? asm.GetName().Version?.ToString() ?? "Unknown";
                var copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

                // Get theme accent color
                var aboutCfg = ConfigManager.LoadConfig();
                var aboutTheme = aboutCfg.Theme ?? "Blue";
                Color accentColor = aboutTheme.ToLowerInvariant() switch
                {
                    "light" => ColorTranslator.FromHtml("#1976D2"),
                    "dark" => ColorTranslator.FromHtml("#61AFEF"),
                    "green" => ColorTranslator.FromHtml("#27AE60"),
                    _ => ColorTranslator.FromHtml("#3498DB")
                };

                // --- Tab Control Setup ---
                var tabControl = new TabControl
                {
                    Dock = DockStyle.Top,
                    Height = 520
                };

                // --- Tab 1: General Info ---
                var tabGeneral = new TabPage("General") { BackColor = Color.White };
                
                var lblProduct = new Label 
                { 
                    Text = product, 
                    Font = new Font("Segoe UI", 16F, FontStyle.Bold), 
                    Left = 25, Top = 20, Width = 620, Height = 35,
                    ForeColor = accentColor
                };
                var lblVersion = new Label 
                { 
                    Text = $"Version: {version}", 
                    Left = 25, Top = 58, Width = 300, 
                    Font = new Font("Segoe UI", 10.5F) 
                };
                var lblCopyright = new Label 
                { 
                    Text = copyright, 
                    Left = 25, Top = 82, Width = 620, 
                    Font = new Font("Segoe UI", 9.5F),
                    ForeColor = Color.DimGray
                };

                var githubUrl = "https://github.com/NoID1290/WSG-Weather-Still-Generator";
                var linkGithub = new LinkLabel 
                { 
                    Text = "🔗 GitHub Repository", 
                    Left = 25, Top = 108, Width = 620, 
                    LinkColor = accentColor,
                    ActiveLinkColor = Color.FromArgb(Math.Max(0, accentColor.R - 40), Math.Max(0, accentColor.G - 40), Math.Max(0, accentColor.B - 40)),
                    Font = new Font("Segoe UI", 10.5F, FontStyle.Underline)
                };
                linkGithub.LinkClicked += (s, e) => OpenUrl(githubUrl);

                // --- Updates Section ---
                var updateGroup = new GroupBox
                {
                    Text = "🔄 Updates",
                    Left = 25, Top = 140, Width = 620, Height = 145,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    ForeColor = accentColor
                };

                var lblCurrentVersion = new Label
                {
                    Text = $"Current Version: {version}",
                    Left = 15, Top = 28, Width = 280, Height = 22,
                    Font = new Font("Segoe UI", 10F),
                    ForeColor = Color.FromArgb(64, 64, 64)
                };

                var lblLatestVersion = new Label
                {
                    Text = "Latest Version: Checking...",
                    Left = 15, Top = 52, Width = 280, Height = 22,
                    Font = new Font("Segoe UI", 10F),
                    ForeColor = Color.FromArgb(64, 64, 64)
                };

                var lblUpdateStatus = new Label
                {
                    Text = "",
                    Left = 15, Top = 76, Width = 400, Height = 22,
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Italic),
                    ForeColor = Color.DimGray
                };

                var btnCheckUpdate = new Button
                {
                    Text = "🔍 Check for Updates",
                    Left = 310, Top = 25, Width = 150, Height = 32,
                    Font = new Font("Segoe UI", 9.5F),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = accentColor,
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand
                };
                btnCheckUpdate.FlatAppearance.BorderSize = 0;

                var btnDownloadUpdate = new Button
                {
                    Text = "⬇ Download & Install",
                    Left = 470, Top = 25, Width = 135, Height = 32,
                    Font = new Font("Segoe UI", 9.5F),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.ForestGreen,
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand,
                    Enabled = false,
                    Visible = false
                };
                btnDownloadUpdate.FlatAppearance.BorderSize = 0;

                var updateProgress = new ProgressBar
                {
                    Left = 310, Top = 62, Width = 295, Height = 20,
                    Visible = false,
                    Style = ProgressBarStyle.Continuous
                };

                var lblUpdateProgress = new Label
                {
                    Text = "",
                    Left = 310, Top = 85, Width = 295, Height = 20,
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = Color.DimGray,
                    Visible = false
                };

                var chkAutoUpdate = new CheckBox
                {
                    Text = "Check for updates on startup",
                    Left = 15, Top = 110, Width = 250, Height = 22,
                    Font = new Font("Segoe UI", 9.5F),
                    ForeColor = Color.FromArgb(64, 64, 64),
                    Checked = aboutCfg.CheckForUpdatesOnStartup
                };
                chkAutoUpdate.CheckedChanged += (s, e) =>
                {
                    try
                    {
                        var cfg = ConfigManager.LoadConfig();
                        cfg.CheckForUpdatesOnStartup = chkAutoUpdate.Checked;
                        ConfigManager.SaveConfig(cfg);
                    }
                    catch { /* Ignore save errors */ }
                };

                string? pendingDownloadUrl = null;

                // Check for updates action
                btnCheckUpdate.Click += async (s, e) =>
                {
                    btnCheckUpdate.Enabled = false;
                    btnCheckUpdate.Text = "Checking...";
                    lblUpdateStatus.Text = "";
                    lblLatestVersion.Text = "Latest Version: Checking...";
                    btnDownloadUpdate.Visible = false;

                    try
                    {
                        var updateInfo = await UpdateService.CheckForUpdatesAsync();
                        
                        if (!string.IsNullOrEmpty(updateInfo.Error))
                        {
                            lblLatestVersion.Text = "Latest Version: Unknown";
                            lblUpdateStatus.Text = updateInfo.Error;
                            lblUpdateStatus.ForeColor = Color.OrangeRed;
                        }
                        else
                        {
                            lblLatestVersion.Text = $"Latest Version: {updateInfo.LatestVersion}";
                            
                            if (updateInfo.IsUpdateAvailable)
                            {
                                lblUpdateStatus.Text = $"🎉 New version available! ({updateInfo.PublishedAt?.ToString("MMM dd, yyyy") ?? "Recent"})";
                                lblUpdateStatus.ForeColor = Color.ForestGreen;
                                pendingDownloadUrl = updateInfo.DownloadUrl;
                                btnDownloadUpdate.Visible = true;
                                btnDownloadUpdate.Enabled = !string.IsNullOrEmpty(pendingDownloadUrl);
                            }
                            else
                            {
                                lblUpdateStatus.Text = "✓ You are running the latest version!";
                                lblUpdateStatus.ForeColor = Color.ForestGreen;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lblUpdateStatus.Text = $"Error: {ex.Message}";
                        lblUpdateStatus.ForeColor = Color.OrangeRed;
                    }
                    finally
                    {
                        btnCheckUpdate.Enabled = true;
                        btnCheckUpdate.Text = "🔍 Check for Updates";
                    }
                };

                // Download and install update action
                btnDownloadUpdate.Click += async (s, e) =>
                {
                    if (string.IsNullOrEmpty(pendingDownloadUrl))
                    {
                        MessageBox.Show("No download URL available. Please try checking for updates again.",
                            "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    var confirm = MessageBox.Show(
                        "This will download and install the latest version.\n\n" +
                        "The application will need to restart after the update.\n" +
                        "Your settings will be preserved.\n\n" +
                        "Continue?",
                        "Confirm Update",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (confirm != DialogResult.Yes) return;

                    btnDownloadUpdate.Enabled = false;
                    btnCheckUpdate.Enabled = false;
                    updateProgress.Visible = true;
                    lblUpdateProgress.Visible = true;
                    updateProgress.Value = 0;

                    var progress = new Progress<(int Percent, string Status)>(p =>
                    {
                        updateProgress.Value = Math.Min(100, p.Percent);
                        lblUpdateProgress.Text = p.Status;
                    });

                    try
                    {
                        var (success, message) = await UpdateService.DownloadAndInstallUpdateAsync(pendingDownloadUrl, progress);

                        if (success)
                        {
                            var restart = MessageBox.Show(
                                message + "\n\nRestart now?",
                                "Update Complete",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Information);

                            if (restart == DialogResult.Yes)
                            {
                                UpdateService.RestartApplication();
                            }
                        }
                        else
                        {
                            MessageBox.Show(message, "Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Update failed: {ex.Message}", "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        updateProgress.Visible = false;
                        lblUpdateProgress.Visible = false;
                        btnCheckUpdate.Enabled = true;
                        btnDownloadUpdate.Enabled = true;
                    }
                };

                updateGroup.Controls.AddRange(new Control[] 
                { 
                    lblCurrentVersion, lblLatestVersion, lblUpdateStatus, 
                    btnCheckUpdate, btnDownloadUpdate, 
                    updateProgress, lblUpdateProgress, chkAutoUpdate 
                });

                var lblDesc = new Label 
                { 
                    Text = "A comprehensive tool to generate beautiful weather forecast images and videos with support for:\n\n" +
                           "• Real-time weather data from Open-Meteo and Environment Canada\n" +
                           "• Emergency alerts from Alert Ready (NAAD)\n" +
                           "• High-quality text-to-speech for Canadian French and English\n" +
                           "• Video generation with background music and transitions\n" +
                           "• Multiple themes and customizable layouts",
                    Left = 25, Top = 295, Width = 620, Height = 200,
                    Font = new Font("Segoe UI", 10F)
                };

                tabGeneral.Controls.AddRange(new Control[] { lblProduct, lblVersion, lblCopyright, linkGithub, updateGroup, lblDesc });

                // --- Tab 2: Credits & Attribution ---
                var tabCredits = new TabPage("Credits & Licenses") { BackColor = Color.White };
                var flowCredits = new FlowLayoutPanel 
                { 
                    Dock = DockStyle.Fill, 
                    AutoScroll = true, 
                    Padding = new Padding(20), 
                    FlowDirection = FlowDirection.TopDown, 
                    WrapContents = false, 
                    BackColor = Color.White 
                };

                // Local helpers for UI construction
                GroupBox CreateGroup(string title, params Control[] ctrls)
                {
                    var gb = new GroupBox 
                    { 
                        Text = title, 
                        Font = new Font("Segoe UI", 11F, FontStyle.Bold), 
                        Width = 620, 
                        AutoSize = true, 
                        AutoSizeMode = AutoSizeMode.GrowAndShrink, 
                        Margin = new Padding(0, 0, 0, 18),
                        ForeColor = accentColor
                    };
                    var pnl = new FlowLayoutPanel 
                    { 
                        Dock = DockStyle.Fill, 
                        FlowDirection = FlowDirection.TopDown, 
                        AutoSize = true, 
                        AutoSizeMode = AutoSizeMode.GrowAndShrink, 
                        Padding = new Padding(15, 30, 15, 15) 
                    };
                    foreach (var c in ctrls) { c.Margin = new Padding(0, 0, 0, 6); pnl.Controls.Add(c); }
                    gb.Controls.Add(pnl);
                    return gb;
                }
                
                LinkLabel MkLink(string txt, string url) 
                {
                    var l = new LinkLabel 
                    { 
                        Text = txt, 
                        AutoSize = true, 
                        Font = new Font("Segoe UI", 9.5F), 
                        LinkColor = accentColor,
                        ActiveLinkColor = Color.FromArgb(Math.Max(0, accentColor.R - 40), Math.Max(0, accentColor.G - 40), Math.Max(0, accentColor.B - 40))
                    };
                    l.LinkClicked += (s, e) => OpenUrl(url);
                    return l;
                }
                
                Label MkLbl(string txt, bool bold = false, bool italic = false) 
                {
                    var style = FontStyle.Regular;
                    if (bold) style |= FontStyle.Bold;
                    if (italic) style |= FontStyle.Italic;
                    return new Label 
                    { 
                        Text = txt, 
                        AutoSize = true, 
                        MaximumSize = new Size(580, 0),
                        Font = new Font("Segoe UI", 9.5F, style), 
                        ForeColor = italic ? Color.Gray : Color.FromArgb(64, 64, 64)
                    };
                }

                Label Spacer() => new Label { Height = 8 };

                // 1. Development
                var grpDev = CreateGroup("Development",
                    MkLbl("Created by: NoID Softwork", true),
                    MkLink("🔗 GitHub: https://github.com/NoID1290/WSG-Weather-Still-Generator", "https://github.com/NoID1290/WSG-Weather-Still-Generator"),
                    MkLbl("License: MIT License (see License tab)", false, true),
                    MkLbl("Framework: .NET 10.0", false, true)
                );

                // 2. Weather Data APIs
                var grpData = CreateGroup("Weather Data APIs",
                    MkLbl("Open-Meteo", true),
                    MkLink("https://open-meteo.com/", "https://open-meteo.com/"),
                    MkLbl("Free weather forecast API with global coverage", false),
                    MkLbl("License: Creative Commons Attribution 4.0 International (CC BY 4.0)", false, true),
                    Spacer(),
                    MkLbl("Environment and Climate Change Canada (ECCC)", true),
                    MkLink("https://weather.gc.ca/", "https://weather.gc.ca/"),
                    MkLbl("Official Canadian weather data and forecasts", false),
                    MkLbl("License: Open Government Licence - Canada", false, true),
                    Spacer(),
                    MkLbl("Alert Ready / NAAD", true),
                    MkLink("https://alerts.pelmorex.com/", "https://alerts.pelmorex.com/"),
                    MkLbl("National Alert Aggregation & Dissemination system for emergency alerts", false),
                    MkLbl("License: Public emergency alert data", false, true)
                );

                // 3. Libraries & Components
                var grpLibs = CreateGroup("Third-Party Libraries",
                    MkLbl("Open-Meteo .NET Library by AlienDwarf", true),
                    MkLink("https://github.com/AlienDwarf/open-meteo-dotnet", "https://github.com/AlienDwarf/open-meteo-dotnet"),
                    MkLbl("License: MIT License", false, true),
                    Spacer(),
                    MkLbl("Xabe.FFmpeg.Downloader", true),
                    MkLbl("Automatic FFmpeg binary downloader for .NET", false),
                    MkLbl("License: MIT License", false, true)
                );

                // 4. Multimedia & Services
                var grpMedia = CreateGroup("Multimedia & Services",
                    MkLbl("FFmpeg Project", true),
                    MkLink("https://ffmpeg.org/", "https://ffmpeg.org/"),
                    MkLbl("Complete, cross-platform solution for video and audio processing", false),
                    MkLbl("License: LGPL v2.1+ (or GPL v2+ with specific builds)", false, true),
                    MkLbl("FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project.", false, true),
                    Spacer(),
                    MkLbl("Piper TTS (Text-to-Speech)", true),
                    MkLink("https://github.com/rhasspy/piper", "https://github.com/rhasspy/piper"),
                    MkLbl("High-quality, offline, open-source neural text-to-speech", false),
                    MkLbl("License: MIT License", false, true),
                    MkLbl("Voice models from Hugging Face (various licenses)", false, true),
                    Spacer(),
                    MkLbl("Microsoft Edge TTS (Text-to-Speech)", true),
                    MkLbl("Neural text-to-speech API for high-quality voice synthesis", false),
                    MkLbl("License: Microsoft Azure Cognitive Services", false, true),
                    Spacer(),
                    MkLbl("Background Music by Kevin MacLeod", true),
                    MkLink("https://incompetech.com/", "https://incompetech.com/"),
                    MkLbl("Royalty-free music for video backgrounds", false),
                    MkLbl("License: Creative Commons Attribution 3.0 Unported (CC BY 3.0)", false, true),
                    MkLink("http://creativecommons.org/licenses/by/3.0/", "http://creativecommons.org/licenses/by/3.0/")
                );

                // 5. Map Data & Tiles
                var grpMaps = CreateGroup("Map Data & Tiles",
                    MkLbl("OpenStreetMap (OSM)", true),
                    MkLink("https://www.openstreetmap.org/copyright", "https://www.openstreetmap.org/copyright"),
                    MkLbl("© OpenStreetMap contributors", true),
                    MkLbl("Free and open geographic data used for map backgrounds and overlays", false),
                    MkLbl("Data License: Open Database License (ODbL) 1.0", false, true),
                    MkLbl("Tile Usage Policy: https://operations.osmfoundation.org/policies/tiles/", false, true),
                    Spacer(),
                    MkLbl("OpenTopoMap", true),
                    MkLink("https://opentopomap.org/", "https://opentopomap.org/"),
                    MkLbl("© OpenStreetMap contributors, SRTM | Style: © OpenTopoMap (CC BY-SA)", true),
                    MkLbl("Topographic map tiles for terrain visualization", false),
                    MkLbl("Data License: ODbL (data), CC BY-SA (style)", false, true),
                    Spacer(),
                    MkLbl("ESRI Satellite Imagery", true),
                    MkLbl("Esri, Maxar, Earthstar Geographics", true),
                    MkLbl("Satellite imagery tiles for weather radar overlays", false),
                    MkLbl("Usage subject to ESRI terms and conditions", false, true),
                    Spacer(),
                    MkLbl("⚠️ Map Attribution Requirements:", true),
                    MkLbl("When displaying maps in this application, attribution must be shown:", false),
                    MkLbl("• Standard/Terrain maps: \"© OpenStreetMap contributors\"", false),
                    MkLbl("• Topographic maps: \"© OpenStreetMap contributors, SRTM | Style: © OpenTopoMap\"", false),
                    MkLbl("• Satellite maps: \"Esri, Maxar, Earthstar Geographics\"", false),
                    MkLbl("See OpenMap\\LEGAL.md for complete legal requirements.", false, true)
                );

                flowCredits.Controls.AddRange(new Control[] { grpDev, grpData, grpLibs, grpMedia, grpMaps });
                tabCredits.Controls.Add(flowCredits);

                // --- Tab 3: License ---
                var tabLicense = new TabPage("License") { BackColor = Color.White };
                var txtLicense = new TextBox 
                { 
                    Dock = DockStyle.Fill, 
                    Multiline = true, 
                    ReadOnly = true, 
                    ScrollBars = ScrollBars.Vertical, 
                    Font = new Font("Consolas", 9.5F),
                    BorderStyle = BorderStyle.None,
                    BackColor = Color.White,
                    Padding = new Padding(10)
                };
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
                var tabDisclaimer = new TabPage("Disclaimer") { BackColor = Color.White };
                var txtDisclaimer = new TextBox 
                { 
                    Dock = DockStyle.Fill, 
                    Multiline = true, 
                    ReadOnly = true, 
                    ScrollBars = ScrollBars.Vertical, 
                    Font = new Font("Segoe UI", 10.5F),
                    BorderStyle = BorderStyle.None,
                    BackColor = Color.White,
                    Padding = new Padding(15)
                };
                txtDisclaimer.Text = @"⚠️ IMPORTANT DISCLAIMER

1. Not for Safety-Critical Use
   This application is for informational and educational purposes only. 
   It should NOT be used for safety-critical decisions, navigation, emergency 
   response, or protection of life and property.

2. Data Accuracy
   Weather data is retrieved from third-party sources (Open-Meteo, Environment 
   and Climate Change Canada) and may contain errors, delays, or inaccuracies. 
   The generated images and videos may not reflect the most current meteorological 
   conditions.

3. Emergency Alerts
   While this application provides access to Alert Ready emergency alerts, 
   users should always verify critical information through official government 
   channels and local emergency management authorities.

4. Official Sources
   Always consult official sources for severe weather warnings and emergency 
   information:
   • Environment and Climate Change Canada (weather.gc.ca)
   • National Weather Service (weather.gov)
   • Local emergency management authorities
   • Provincial/territorial emergency alert systems

5. Text-to-Speech
   The quality and accuracy of synthesized speech may vary. Do not rely solely 
   on audio output for critical weather information.

6. No Warranty
   The authors and contributors provide this software ""AS IS"" without warranty 
   of any kind, express or implied. We are not responsible for any damages, 
   losses, or consequences resulting from the use of this software.

7. Third-Party Services
   This application relies on external APIs and services which may experience 
   downtime, rate limiting, or changes without notice.

By using this software, you acknowledge and accept these limitations.";
                tabDisclaimer.Controls.Add(txtDisclaimer);

                tabControl.TabPages.Add(tabGeneral);
                tabControl.TabPages.Add(tabCredits);
                tabControl.TabPages.Add(tabLicense);
                tabControl.TabPages.Add(tabDisclaimer);

                // Bottom button panel with better styling
                var btnPanel = new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 60,
                    BackColor = Color.WhiteSmoke
                };

                var ok = new Button 
                { 
                    Text = "OK", 
                    Left = 590, 
                    Top = 15, 
                    Width = 90, 
                    Height = 35,
                    Font = new Font("Segoe UI", 10F),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = accentColor,
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand
                };
                ok.FlatAppearance.BorderSize = 0;
                ok.Click += (s, e) => this.Close();
                btnPanel.Controls.Add(ok);

                this.Controls.Add(tabControl);
                this.Controls.Add(btnPanel);

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
    }
}