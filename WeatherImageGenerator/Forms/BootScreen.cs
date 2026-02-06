#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Services.BootChecks;

namespace WeatherImageGenerator.Forms
{
    /// <summary>
    /// Splash / boot screen shown at application startup while system checks run.
    /// Dark-themed, compact, professional look matching the main UI.
    /// </summary>
    public class BootScreen : Form
    {
        // ── Colors (matching MainForm dark theme) ──
        private static readonly Color BgDark       = Color.FromArgb(25, 32, 45);
        private static readonly Color BgPanel      = Color.FromArgb(35, 45, 60);
        private static readonly Color AccentBlue   = Color.FromArgb(41, 128, 185);
        private static readonly Color TextPrimary  = Color.FromArgb(220, 225, 235);
        private static readonly Color TextDim      = Color.FromArgb(140, 150, 170);
        private static readonly Color GreenOk      = Color.FromArgb(39, 174, 96);
        private static readonly Color YellowWarn   = Color.FromArgb(241, 196, 15);
        private static readonly Color RedFail      = Color.FromArgb(231, 76, 60);
        private static readonly Color CyanRepair   = Color.FromArgb(52, 152, 219);
        private static readonly Color GraySkip     = Color.FromArgb(127, 140, 141);

        // ── Controls ──
        private readonly Label _titleLabel;
        private readonly Label _versionLabel;
        private readonly Label _statusLabel;
        private readonly ProgressBar _progressBar;
        private readonly Panel _checkListPanel;
        private readonly Button _continueBtn;
        private readonly List<CheckRow> _checkRows = new();

        // ── State ──
        private BootRunner? _runner;
        private bool _completed;
        private bool _hasFailures;

        /// <summary>
        /// The validated AppSettings loaded by the configuration check (available after boot completes).
        /// </summary>
        public AppSettings? LoadedSettings { get; private set; }

        /// <summary>
        /// Whether the boot completed successfully (no hard failures).
        /// </summary>
        public bool BootSucceeded => _completed && !_hasFailures;

        public BootScreen()
        {
            // ── Form setup ──
            Text = "WSG — Starting Up";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(520, 520);
            BackColor = BgDark;
            DoubleBuffered = true;
            ShowInTaskbar = true;
            TopMost = true;

            // ── Title ──
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
            _titleLabel = new Label
            {
                Text = "⛅  WSG — Weather Still Generator",
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                ForeColor = TextPrimary,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(520, 40),
                Location = new Point(0, 18)
            };

            _versionLabel = new Label
            {
                Text = $"v{version}",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextDim,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(520, 20),
                Location = new Point(0, 55)
            };

            // ── Status label ──
            _statusLabel = new Label
            {
                Text = "Initializing...",
                Font = new Font("Segoe UI", 9F),
                ForeColor = AccentBlue,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(490, 22),
                Location = new Point(15, 85)
            };

            // ── Progress bar ──
            _progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Continuous,
                Size = new Size(490, 6),
                Location = new Point(15, 110),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            // ── Check list panel ──
            _checkListPanel = new Panel
            {
                Location = new Point(15, 125),
                Size = new Size(490, 320),
                AutoScroll = true,
                BackColor = BgDark
            };

            // ── Continue button (hidden until complete) ──
            _continueBtn = new Button
            {
                Text = "Continue",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccentBlue,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(140, 36),
                Location = new Point(190, 460),
                Visible = false,
                Cursor = Cursors.Hand
            };
            _continueBtn.FlatAppearance.BorderSize = 0;
            _continueBtn.Click += (_, _) =>
            {
                DialogResult = _hasFailures ? DialogResult.Abort : DialogResult.OK;
                Close();
            };

            Controls.Add(_titleLabel);
            Controls.Add(_versionLabel);
            Controls.Add(_statusLabel);
            Controls.Add(_progressBar);
            Controls.Add(_checkListPanel);
            Controls.Add(_continueBtn);

            // Allow dragging the borderless window
            _titleLabel.MouseDown += OnDrag;
            this.MouseDown += OnDrag;
        }

        // ── Dragging support for borderless form ──
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private void OnDrag(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        // ── Rounded window ──
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Draw a subtle border
            using var pen = new Pen(Color.FromArgb(60, 75, 95), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            // Draw separator line under title
            using var sepPen = new Pen(Color.FromArgb(50, 65, 85), 1);
            e.Graphics.DrawLine(sepPen, 15, 80, Width - 15, 80);
        }

        /// <summary>
        /// Builds and runs all boot checks, updating the UI live.
        /// Call this from Shown event or after showing the form.
        /// </summary>
        public async Task RunBootSequenceAsync(CancellationToken ct = default)
        {
            _runner = new BootRunner();

            // Register all checks in order
            var settingsCheck = new AppSettingsCheck();
            _runner.Add(new EnvironmentCheck());
            _runner.Add(settingsCheck);
            _runner.Add(new FFmpegCheck());
            _runner.Add(new OutputDirectoriesCheck());
            _runner.Add(new MapTileCacheCheck());
            _runner.Add(new OpenMeteoCheck());
            _runner.Add(new ECCCCheck());
            _runner.Add(new AlertReadyCheck());
            _runner.Add(new WebUICheck());

            // Create UI rows for each check
            _checkRows.Clear();
            _checkListPanel.Controls.Clear();
            int y = 0;
            var checks = new string[]
            {
                "Environment", "Configuration", "FFmpeg", "Output Directories",
                "Map Tile Cache", "Open Meteo API", "ECCC Weather",
                "Alert Ready (NAAD)", "Web UI"
            };
            foreach (var name in checks)
            {
                var row = new CheckRow(name, _checkListPanel.Width - 10);
                row.Location = new Point(0, y);
                _checkListPanel.Controls.Add(row);
                _checkRows.Add(row);
                y += row.Height + 2;
            }

            // Wire events
            _runner.CheckStarted += (idx, tot, name) =>
            {
                if (InvokeRequired) { BeginInvoke(() => OnCheckStarted(idx, tot, name)); return; }
                OnCheckStarted(idx, tot, name);
            };

            _runner.CheckCompleted += (idx, tot, result) =>
            {
                if (InvokeRequired) { BeginInvoke(() => OnCheckCompleted(idx, tot, result)); return; }
                OnCheckCompleted(idx, tot, result);
            };

            _runner.AllCompleted += (results) =>
            {
                if (InvokeRequired) { BeginInvoke(() => OnAllCompleted(results)); return; }
                OnAllCompleted(results);
            };

            // Run
            await _runner.RunAllAsync(ct);

            // Grab the validated settings
            LoadedSettings = settingsCheck.LoadedSettings;
        }

        private void OnCheckStarted(int idx, int total, string name)
        {
            _statusLabel.Text = $"Checking {name}...";
            _progressBar.Value = Math.Min(100, (int)((idx / (double)total) * 100));

            if (idx < _checkRows.Count)
            {
                _checkRows[idx].SetStatus(BootCheckStatus.Running, "Checking...");
            }
        }

        private void OnCheckCompleted(int idx, int total, BootCheckResult result)
        {
            _progressBar.Value = Math.Min(100, (int)(((idx + 1) / (double)total) * 100));

            if (idx < _checkRows.Count)
            {
                _checkRows[idx].SetStatus(result.Status, result.StatusMessage);
                if (!string.IsNullOrWhiteSpace(result.Detail))
                {
                    _checkRows[idx].SetDetail(result.Detail);
                }
            }
        }

        private void OnAllCompleted(List<BootCheckResult> results)
        {
            _completed = true;
            _progressBar.Value = 100;

            int passed = 0, repaired = 0, warnings = 0, failed = 0, skipped = 0;
            foreach (var r in results)
            {
                switch (r.Status)
                {
                    case BootCheckStatus.Passed: passed++; break;
                    case BootCheckStatus.Repaired: repaired++; break;
                    case BootCheckStatus.Warning: warnings++; break;
                    case BootCheckStatus.Failed: failed++; break;
                    case BootCheckStatus.Skipped: skipped++; break;
                }
            }

            _hasFailures = failed > 0;

            if (failed > 0)
            {
                _statusLabel.Text = $"Boot completed with {failed} failure(s) — review below";
                _statusLabel.ForeColor = RedFail;
                _continueBtn.Text = "Continue Anyway";
                _continueBtn.BackColor = Color.FromArgb(192, 57, 43);
            }
            else if (warnings > 0 || repaired > 0)
            {
                _statusLabel.Text = $"Ready — {passed} passed, {repaired} repaired, {warnings} warning(s)";
                _statusLabel.ForeColor = GreenOk;
            }
            else
            {
                _statusLabel.Text = $"All {passed} checks passed — ready to launch";
                _statusLabel.ForeColor = GreenOk;
            }

            _continueBtn.Visible = true;

            // Auto-continue after 2s if no failures and no repairs
            if (!_hasFailures && repaired == 0)
            {
                var timer = new System.Windows.Forms.Timer { Interval = 1500 };
                int countdown = 1500;
                timer.Tick += (_, _) =>
                {
                    countdown -= timer.Interval;
                    if (countdown <= 0)
                    {
                        timer.Stop();
                        timer.Dispose();
                        if (!IsDisposed && Visible)
                        {
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                    }
                };
                timer.Start();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Inner class: a single row in the check list
        // ═══════════════════════════════════════════════════════════════
        private class CheckRow : Panel
        {
            private readonly Label _icon;
            private readonly Label _nameLabel;
            private readonly Label _statusLabel;

            public CheckRow(string name, int width)
            {
                Size = new Size(width, 30);
                BackColor = BgDark;

                _icon = new Label
                {
                    Text = "○",
                    Font = new Font("Segoe UI", 10F),
                    ForeColor = TextDim,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Size = new Size(28, 28),
                    Location = new Point(2, 1)
                };

                _nameLabel = new Label
                {
                    Text = name,
                    Font = new Font("Segoe UI", 9.5F),
                    ForeColor = TextPrimary,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Size = new Size(180, 28),
                    Location = new Point(32, 1)
                };

                _statusLabel = new Label
                {
                    Text = "Pending",
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = TextDim,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Size = new Size(width - 220, 28),
                    Location = new Point(215, 1)
                };

                Controls.Add(_icon);
                Controls.Add(_nameLabel);
                Controls.Add(_statusLabel);
            }

            public void SetStatus(BootCheckStatus status, string message)
            {
                var (icon, color) = status switch
                {
                    BootCheckStatus.Running  => ("⟳", AccentBlue),
                    BootCheckStatus.Passed   => ("✓", GreenOk),
                    BootCheckStatus.Repaired => ("🔧", CyanRepair),
                    BootCheckStatus.Warning  => ("⚠", YellowWarn),
                    BootCheckStatus.Failed   => ("✗", RedFail),
                    BootCheckStatus.Skipped  => ("⊘", GraySkip),
                    _                        => ("○", TextDim)
                };

                _icon.Text = icon;
                _icon.ForeColor = color;
                _statusLabel.Text = message;
                _statusLabel.ForeColor = color;
            }

            public void SetDetail(string detail)
            {
                // Show detail as tooltip
                var tt = new ToolTip { AutoPopDelay = 10000 };
                tt.SetToolTip(_statusLabel, detail);
                tt.SetToolTip(_nameLabel, detail);
            }
        }
    }
}
