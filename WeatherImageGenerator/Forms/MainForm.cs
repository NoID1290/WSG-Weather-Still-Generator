using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Forms;
using System.Reflection;
using System.Drawing;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Utilities;
using WeatherImageGenerator.Models;

namespace WeatherImageGenerator.Forms
{
    public class MainForm : Form
    {
        private CancellationTokenSource? _cts;
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

        // When Minimal verbosity is selected, show only the last N important lines
        private const int MinimalVisibleCount = 5;    // reduced for casual users (show only 5 lines)


        // Video phase mapping (when ffmpeg reports 0-100 we map it into [videoBase, 100])
        private double _videoBase = 80.0;
        private double _videoRange = 20.0;
        private bool _videoActive = false;

        public MainForm()
        {

            this.Text = "WSG - WeatherStillGenerator";
            this.Width = 950;
            this.Height = 600;

            var rich = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Name = "logBox", BackColor = System.Drawing.Color.Black, ForeColor = System.Drawing.Color.LightGray, Font = new System.Drawing.Font("Segoe UI", 10F), DetectUrls = true, HideSelection = false, ScrollBars = RichTextBoxScrollBars.Vertical };
            
            // --- Top Panel Controls ---
            var startBtn = new Button { Text = "Start", Left = 10, Top = 10, Width = 70, Height = 30 };
            var stopBtn = new Button { Text = "Stop", Left = 85, Top = 10, Width = 70, Height = 30, Enabled = false };
            var fetchBtn = new Button { Text = "Fetch", Left = 160, Top = 10, Width = 70, Height = 30 };
            var stillBtn = new Button { Text = "Still", Left = 235, Top = 10, Width = 70, Height = 30 };
            var videoBtn = new Button { Text = "Video", Left = 310, Top = 10, Width = 70, Height = 30 };
            
            var openOutputBtn = new Button { Text = "Open Dir", Left = 385, Top = 10, Width = 70, Height = 30 };
            var clearDirBtn = new Button { Text = "Clear Dir", Left = 460, Top = 10, Width = 70, Height = 30 };
            var locationsBtn = new Button { Text = "Locations", Left = 535, Top = 10, Width = 80, Height = 30 };
            var settingsBtn = new Button { Text = "Settings", Left = 620, Top = 10, Width = 70, Height = 30 };
            var aboutBtn = new Button { Text = "About", Left = 695, Top = 10, Width = 70, Height = 30 };

            // Progress & Status (Row 2)
            _progress = new TextProgressBar { Left = 10, Top = 50, Width = 370, Height = 24, Style = ProgressBarStyle.Continuous, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            _statusLabel = new Label { Left = 400, Top = 50, Width = 200, Text = "Idle", AutoSize = true };
            _sleepLabel = new Label { Left = 400, Top = 70, Width = 200, Text = string.Empty, AutoSize = true };
            _lastFetchLabel = new Label { Dock = DockStyle.Top, Height = 20, Text = "Last fetch: Never", Font = new Font("Segoe UI", 8F, FontStyle.Italic), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

            // --- Log Controls (Separate Panel) ---
            var logPanel = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = SystemColors.Control };
            
            var lblLog = new Label { Text = "Logs:", Left = 10, Top = 12, AutoSize = true };
            
            _cmbFilter = new ComboBox { Left = 60, Top = 8, Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbFilter.Items.AddRange(new object[] { "All", "Errors", "Warnings", "Info" });
            _cmbFilter.SelectedIndex = 0;
            _cmbFilter.SelectedIndexChanged += (s, e) => RefreshLogView();

            _cmbVerbosity = new ComboBox { Left = 170, Top = 8, Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbVerbosity.Items.AddRange(new object[] { "Verbose", "Normal", "Minimal" });
            _cmbVerbosity.SelectedIndex = 1; // Normal
            _cmbVerbosity.SelectedIndexChanged += (s, e) => RefreshLogView();

            _chkCompact = new CheckBox { Left = 270, Top = 10, Width = 80, Text = "Compact" };
            _chkCompact.CheckedChanged += (s, e) => RefreshLogView();

            _txtSearch = new TextBox { Left = 360, Top = 8, Width = 200 };
            _txtSearch.PlaceholderText = "Search logs...";
            _txtSearch.TextChanged += (s, e) => RefreshLogView();

            var clearBtn = new Button { Text = "Clear", Left = 570, Top = 7, Width = 60, Height = 25 };
            clearBtn.Click += (s, e) => 
            {
                lock (_logBuffer)
                {
                    _logBuffer.Clear();
                }
                RefreshLogView();
            };

            logPanel.Controls.Add(lblLog);
            logPanel.Controls.Add(_cmbFilter);
            logPanel.Controls.Add(_cmbVerbosity);
            logPanel.Controls.Add(_chkCompact);
            logPanel.Controls.Add(_txtSearch);
            logPanel.Controls.Add(clearBtn);

            startBtn.Click += (s, e) => StartClicked(startBtn, stopBtn);
            stopBtn.Click += (s, e) => StopClicked(startBtn, stopBtn);
            openOutputBtn.Click += (s, e) => OpenOutputDirectory();
            clearDirBtn.Click += (s, e) => ClearOutputDirectory();
            videoBtn.Click += (s, e) => VideoClicked();
            fetchBtn.Click += (s, e) => FetchClicked(fetchBtn);
            stillBtn.Click += (s, e) => StillClicked(stillBtn);

            // Subscribe to only the leveled event and receive the explicit LogLevel (fixes coloring detection)
            Logger.MessageLoggedWithLevel += (text, level) => OnMessageLogged(text, level);

            // Subscribe to sleep updates from the background worker so we can show a countdown
            Program.SleepRemainingUpdated += (ts) => SetSleepRemaining(ts);

            // Subscribe to overall progress and video-specific progress
            Program.ProgressUpdated += (pct, msg) => OnProgramProgress(pct, msg);
            VideoGenerator.VideoProgressUpdated += (pct, msg) => OnVideoProgress(pct, msg);

            settingsBtn.Click += (s, e) =>
            {
                using (var f = new SettingsForm())
                {
                    if (f.ShowDialog() == DialogResult.OK)
                    {
                        Logger.Log("Settings saved.");
                    }
                }
            };

            locationsBtn.Click += (s, e) =>
            {
                using (var f = new LocationsForm())
                {
                    if (f.ShowDialog() == DialogResult.OK)
                    {
                        Logger.Log("Locations updated.");
                    }
                }
            };

            aboutBtn.Click += (s, e) =>
            {
                using (var f = new AboutDialog())
                {
                    f.ShowDialog();
                }
            };

            var panel = new Panel { Dock = DockStyle.Top, Height = 90 };
            panel.Controls.Add(videoBtn);
            panel.Controls.Add(stillBtn);
            panel.Controls.Add(fetchBtn);
            panel.Controls.Add(stopBtn);
            panel.Controls.Add(openOutputBtn);
            panel.Controls.Add(clearDirBtn);
            panel.Controls.Add(startBtn);
            panel.Controls.Add(locationsBtn);
            panel.Controls.Add(settingsBtn);
            panel.Controls.Add(aboutBtn);
            panel.Controls.Add(_progress);
            panel.Controls.Add(_statusLabel);
            panel.Controls.Add(_sleepLabel);
            // _lastFetchLabel moved to splitContainer.Panel1

            var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            _weatherList = new ListView { Dock = DockStyle.Fill, View = View.Details, GridLines = true, FullRowSelect = true };
            _weatherList.Columns.Add("Location", 200);
            _weatherList.Columns.Add("Temp", 80);
            _weatherList.Columns.Add("Feels Like", 80);
            _weatherList.Columns.Add("Condition", 150);
            _weatherList.Columns.Add("Wind", 150);
            _weatherList.Columns.Add("Alerts", 200);

            splitContainer.Panel1.Controls.Add(_weatherList);
            splitContainer.Panel1.Controls.Add(_lastFetchLabel);
            // Docking order is reverse of Z-order. Send label to back so it is docked first (Top), 
            // then list fills the remaining space.
            _lastFetchLabel.SendToBack();
            _weatherList.BringToFront();
            
            // Add log controls panel first (Dock=Top) then rich text box (Dock=Fill)
            splitContainer.Panel2.Controls.Add(logPanel);
            splitContainer.Panel2.Controls.Add(rich);
            rich.BringToFront(); // Ensure rich text box fills remaining space correctly if needed, but usually Dock order is reverse of Add order?
            // Actually: "Controls added to a container are docked in the reverse order of the z-order."
            // So the last added control is at the top of the z-order and gets docked first? No.
            // "The control at the top of the Z-order is docked last."
            // So if I want logPanel at Top, and rich at Fill.
            // I should add rich first, then logPanel?
            // Let's try adding logPanel (Top) then rich (Fill).
            // If logPanel is Top, it takes the top strip. rich (Fill) takes the rest.
            // But if rich is added first, it fills everything. Then logPanel is added... where?
            // Let's stick to: Add logPanel, Add rich. And set logPanel.SendToBack() (bottom of Z-order -> docked first).
            // Or just rely on the fact that Dock=Top usually takes precedence over Dock=Fill if added in correct order.
            // Let's just add them and see. Usually adding Top then Fill works.
            
            // Make console smaller (Panel2 is bottom)
            splitContainer.SplitterDistance = 350;

            this.Controls.Add(splitContainer);
            this.Controls.Add(panel);

            Program.WeatherDataFetched += OnWeatherDataFetched;
            Program.AlertsFetched += OnAlertsFetched;
        }

        private void OnAlertsFetched(System.Collections.Generic.List<AlertEntry> alerts)
        {
            if (_weatherList == null) return;
            if (_weatherList.InvokeRequired)
            {
                _weatherList.BeginInvoke(new Action(() => OnAlertsFetched(alerts)));
                return;
            }

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
                    // Simple case-insensitive match
                    if (string.Equals(item.Text, alert.City, StringComparison.OrdinalIgnoreCase))
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
                        EnableHardwareEncoding = config.Video?.EnableHardwareEncoding ?? false
                    };

                    videoGenerator.GenerateVideo();
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ERROR] Manual video generation error: {ex.Message}", ConsoleColor.Red);
                }
            });
        }

        private void FetchClicked(Button fetchBtn)
        {
            fetchBtn.Enabled = false;
            Task.Run(async () => 
            {
                try
                {
                    await Program.FetchDataOnlyAsync();
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
                }
            });
        }

        private void StillClicked(Button stillBtn)
        {
            stillBtn.Enabled = false;
            Task.Run(async () => 
            {
                try
                {
                    await Program.GenerateStillsOnlyAsync();
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
                }
            });
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
                        // Periodically refresh to prune old lines from the view if it gets too long
                        if (rtb.Lines.Length > 2000) 
                        {
                            RefreshLogView();
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
            if (lower.Contains("error") || lower.Contains("failed") || lower.Contains("fail")) _statusLabel.ForeColor = Color.Red;
            else if (lower.Contains("encoding") || lower.Contains("video") || lower.Contains("running")) _statusLabel.ForeColor = Color.Cyan;
            else _statusLabel.ForeColor = Color.Black;

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
                var linkGithub = new LinkLabel { Text = "GitHub Repository", Left = 20, Top = 120, Width = 520, LinkColor = System.Drawing.Color.Blue, Font = new Font("Segoe UI", 10F) };
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
                    MkLbl("FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project.", false, true)
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