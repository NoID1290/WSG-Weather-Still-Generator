using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;
using System.Drawing;

namespace WeatherImageGenerator
{
    public class MainForm : Form
    {
        private CancellationTokenSource? _cts;
        private readonly System.Collections.Generic.List<string> _logBuffer = new System.Collections.Generic.List<string>();
        private ComboBox? _cmbFilter;
        private TextBox? _txtSearch;
        private TextProgressBar? _progress;
        private Label? _statusLabel;
        private Label? _sleepLabel;

        // Video phase mapping (when ffmpeg reports 0-100 we map it into [videoBase, 100])
        private double _videoBase = 80.0;
        private double _videoRange = 20.0;
        private bool _videoActive = false;

        public MainForm()
        {

            this.Text = "WSG - WeatherStillGenerator";
            this.Width = 900;
            this.Height = 600;

            var rich = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Name = "logBox", BackColor = System.Drawing.Color.Black, ForeColor = System.Drawing.Color.LightGreen, Font = new System.Drawing.Font("Consolas", 10) };
            var startBtn = new Button { Text = "Start", Left = 10, Top = 10, Width = 80, Height = 30 };
            var stopBtn = new Button { Text = "Stop", Left = 100, Top = 10, Width = 80, Height = 30, Enabled = false };
            var videoBtn = new Button { Text = "Make Video Now", Left = 190, Top = 10, Width = 120, Height = 30 };
            var settingsBtn = new Button { Text = "Settings", Left = 320, Top = 10, Width = 80, Height = 30 };

            _cmbFilter = new ComboBox { Left = 410, Top = 12, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbFilter.Items.AddRange(new object[] { "All", "Errors", "Info" });
            _cmbFilter.SelectedIndex = 0;
            _cmbFilter.SelectedIndexChanged += (s, e) => RefreshLogView();

            _txtSearch = new TextBox { Left = 540, Top = 12, Width = 200 };
            _txtSearch.PlaceholderText = "Search logs...";
            _txtSearch.TextChanged += (s, e) => RefreshLogView();

            // Core progress bar (used as the visual fill) with embedded percentage text
            _progress = new TextProgressBar { Left = 10, Top = 46, Width = 300, Height = 24, Style = ProgressBarStyle.Continuous, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };

            // Make room for the activity/status label to the right
            _statusLabel = new Label { Left = 320, Top = 46, Width = 300, Text = "Idle" };
            _sleepLabel = new Label { Left = 630, Top = 46, Width = 220, Text = string.Empty };

            startBtn.Click += (s, e) => StartClicked(startBtn, stopBtn);
            stopBtn.Click += (s, e) => StopClicked(startBtn, stopBtn);
            videoBtn.Click += (s, e) => VideoClicked();

            // Subscribe to only the leveled event to avoid duplicate entries (we previously subscribed to both events)
            Logger.MessageLoggedWithLevel += (text, level) => OnMessageLogged(text);

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

            var aboutBtn = new Button { Text = "About", Left = 760, Top = 10, Width = 80, Height = 30 };
            aboutBtn.Click += (s, e) =>
            {
                using (var f = new AboutDialog())
                {
                    f.ShowDialog();
                }
            };

            var panel = new Panel { Dock = DockStyle.Top, Height = 90 };
            panel.Controls.Add(videoBtn);
            panel.Controls.Add(stopBtn);
            panel.Controls.Add(startBtn);
            panel.Controls.Add(settingsBtn);
            panel.Controls.Add(aboutBtn);
            panel.Controls.Add(_cmbFilter);
            panel.Controls.Add(_txtSearch);
            panel.Controls.Add(_progress);
            panel.Controls.Add(_statusLabel);
            panel.Controls.Add(_sleepLabel);

            this.Controls.Add(rich);
            this.Controls.Add(panel);
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

                rtb.AppendText(text);
                rtb.ScrollToCaret();
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
                        ShowFfmpegOutputInGui = config.Video?.ShowFfmpegOutputInGui ?? true
                    };

                    videoGenerator.GenerateVideo();
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ERROR] Manual video generation error: {ex.Message}", ConsoleColor.Red);
                }
            });
        }

        private void OnMessageLogged(string text)
        {
            // Keep a copy of everything, then reapply filters for the view
            lock (_logBuffer)
            {
                _logBuffer.Add(text);
            }

            var trimmed = text.Trim();

            // If messages indicate ffmpeg running/done, update status/progress
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

            RefreshLogView();
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

            rtb.Clear();
            lock (_logBuffer)
            {
                foreach (var line in _logBuffer)
                {
                    if (!PassesFilter(line, filter, search)) continue;
                    rtb.AppendText(line);
                }
            }

            rtb.ScrollToCaret();
        }

        private bool PassesFilter(string line, string filter, string search)
        {
            if (!string.IsNullOrEmpty(search) && !line.Contains(search, StringComparison.OrdinalIgnoreCase)) return false;

            return filter switch
            {
                "All" => true,
                "Errors" => line.Contains("[ERROR]") || line.Contains("Failed") || line.Contains("✗") || line.Contains("X "),
                "Info" => !line.Contains("[ERROR]") && !line.Contains("✗") && !line.Contains("X "),
                _ => true
            };
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
                this.Text = "About";
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.StartPosition = FormStartPosition.CenterParent;
                this.Width = 560;
                this.Height = 360;
                this.MaximizeBox = false;
                this.MinimizeBox = false;

                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var product = asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product
                              ?? asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title
                              ?? "Weather Image Generator";
                var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                              ?? asm.GetName().Version?.ToString() ?? "Unknown";
                var copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

                var lblProduct = new Label { Text = product, Font = new Font("Segoe UI", 12F, FontStyle.Bold), Left = 16, Top = 14, Width = 520, Height = 26 };
                var lblVersion = new Label { Text = $"Version: {version}", Left = 16, Top = 44, Width = 520 };
                var lblCopyright = new Label { Text = copyright, Left = 16, Top = 64, Width = 520 }; 

                var githubUrl = "https://github.com/NoID1290/WSG-Weather-Still-Generator";
                var linkGithub = new LinkLabel { Text = githubUrl, Left = 16, Top = 90, Width = 520, LinkColor = System.Drawing.Color.Blue };
                linkGithub.LinkClicked += (s, e) => OpenUrl(githubUrl);

                var licenseUrl = "https://github.com/NoID1290/WSG-Weather-Still-Generator/blob/main/LICENSE";
                var linkLicense = new LinkLabel { Text = "MIT License", Left = 16, Top = 116, Width = 520, LinkColor = System.Drawing.Color.Blue };
                linkLicense.LinkClicked += (s, e) => OpenUrl(licenseUrl);

                var txtCredits = new TextBox { Left = 16, Top = 140, Width = 510, Height = 120, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
                txtCredits.Text = "Credits:\r\n - NoID Softwork (Author)\r\n - OpenMeteo API (data)\r\n - Environment Canada (ECCC) alerts\r\n\r\nBuilt with .NET 8.0\r\n";

                var ok = new Button { Text = "OK", Left = 450, Top = 270, Width = 80, Height = 28 };
                ok.Click += (s, e) => this.Close();

                this.Controls.AddRange(new Control[] { lblProduct, lblVersion, lblCopyright, linkGithub, linkLicense, txtCredits, ok });

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