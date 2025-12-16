using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WeatherImageGenerator
{
    public class MainForm : Form
    {
        private CancellationTokenSource? _cts;
        private readonly System.Collections.Generic.List<string> _logBuffer = new System.Collections.Generic.List<string>();
        private ComboBox? _cmbFilter;
        private TextBox? _txtSearch;
        private ProgressBar? _progress;
        private Label? _statusLabel;
        private Label? _sleepLabel;

        public MainForm()
        {

            this.Text = "Weather Image Generator — GUI Console";
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

            _progress = new ProgressBar { Left = 10, Top = 46, Width = 300, Height = 18, Style = ProgressBarStyle.Blocks };
            // Make room for the sleep countdown label to the right of status
            _statusLabel = new Label { Left = 320, Top = 46, Width = 300, Text = "Idle" };
            _sleepLabel = new Label { Left = 630, Top = 46, Width = 220, Text = string.Empty };

            startBtn.Click += (s, e) => StartClicked(startBtn, stopBtn);
            stopBtn.Click += (s, e) => StopClicked(startBtn, stopBtn);
            videoBtn.Click += (s, e) => VideoClicked();

            // Subscribe to only the leveled event to avoid duplicate entries (we previously subscribed to both events)
            Logger.MessageLoggedWithLevel += (text, level) => OnMessageLogged(text);

            // Subscribe to sleep updates from the background worker so we can show a countdown
            Program.SleepRemainingUpdated += (ts) => SetSleepRemaining(ts);

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

            var panel = new Panel { Dock = DockStyle.Top, Height = 90 };
            panel.Controls.Add(videoBtn);
            panel.Controls.Add(stopBtn);
            panel.Controls.Add(startBtn);
            panel.Controls.Add(settingsBtn);
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
                SetStatus("Running...");
                SetProgressMarquee(true);
            }
            else if (TryExtractProgress(trimmed, out var pct))
            {
                SetProgressValue(pct);
                SetStatus($"Encoding... {pct:0}%");
            }
            else if (trimmed.Contains("[DONE]") || trimmed.Contains("[FAIL]") || trimmed.Contains("Video saved") || trimmed.Contains("Video generation completed"))
            {
                SetStatus("Idle");
                SetProgressMarquee(false);
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

            _progress.Style = marquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!marquee) _progress.Value = 0;
        }

        private void SetProgressValue(double pct)
        {
            if (_progress == null) return;
            if (_progress.InvokeRequired)
            {
                _progress.BeginInvoke(new Action(() => SetProgressValue(pct)));
                return;
            }

            _progress.Style = ProgressBarStyle.Blocks;
            var clamped = (int)Math.Max(0, Math.Min(100, Math.Round(pct)));
            _progress.Value = clamped;
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

        private void SetStatus(string status)
        {
            if (_statusLabel == null) return;
            if (_statusLabel.InvokeRequired)
            {
                _statusLabel.BeginInvoke(new Action(() => SetStatus(status)));
                return;
            }
            _statusLabel.Text = status;
        }
    }
}

/* Note: No Designer file is required for this simple form — controls are created at runtime */