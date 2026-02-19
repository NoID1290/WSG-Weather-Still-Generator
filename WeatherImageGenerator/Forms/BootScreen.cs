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
    /// Dark-themed, compact, professional look with animations and expandable detail rows.
    /// </summary>
    public class BootScreen : Form
    {
        // ── Colors (matching MainForm dark theme) ──
        private static readonly Color BgDark       = Color.FromArgb(20, 26, 38);
        private static readonly Color BgPanel      = Color.FromArgb(30, 38, 52);
        private static readonly Color AccentBlue   = Color.FromArgb(41, 128, 185);
        private static readonly Color AccentGlow   = Color.FromArgb(52, 152, 219);
        private static readonly Color TextPrimary  = Color.FromArgb(220, 225, 235);
        private static readonly Color TextDim      = Color.FromArgb(140, 150, 170);
        private static readonly Color GreenOk      = Color.FromArgb(39, 174, 96);
        private static readonly Color YellowWarn   = Color.FromArgb(241, 196, 15);
        private static readonly Color RedFail      = Color.FromArgb(231, 76, 60);
        private static readonly Color CyanRepair   = Color.FromArgb(52, 152, 219);
        private static readonly Color GraySkip     = Color.FromArgb(127, 140, 141);
        private static readonly Color RowHover     = Color.FromArgb(35, 45, 62);
        private static readonly Color RowExpanded  = Color.FromArgb(28, 36, 50);

        // ── Controls ──
        private readonly Label _titleLabel;
        private readonly Label _subtitleLabel;
        private readonly Label _versionLabel;
        private readonly Label _statusLabel;
        private readonly Panel _progressPanel;
        private readonly Panel _checkListPanel;
        private readonly Button _continueBtn;
        private readonly List<CheckRow> _checkRows = new();
        private float _progressValue = 0f;
        private float _progressTarget = 0f;

        // ── Fade-in animation ──
        private readonly System.Windows.Forms.Timer _fadeTimer;
        private readonly System.Windows.Forms.Timer _progressAnimTimer;
        private double _targetOpacity = 1.0;

        // ── State ──
        private BootRunner? _runner;
        private bool _completed;
        private bool _hasFailures;
        private bool _hasFatalFailure;

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
            Size = new Size(560, 720);
            BackColor = BgDark;
            DoubleBuffered = true;
            ShowInTaskbar = true;
            TopMost = true;
            Opacity = 0;

            // ── Branded header ──
            var headerPanel = new Panel
            {
                Size = new Size(560, 100),
                Location = new Point(0, 0),
                BackColor = Color.Transparent
            };
            headerPanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Draw gradient header background
                using var headerBrush = new LinearGradientBrush(
                    new Rectangle(0, 0, headerPanel.Width, headerPanel.Height),
                    Color.FromArgb(30, 40, 60), BgDark, 90F);
                g.FillRectangle(headerBrush, 0, 0, headerPanel.Width, headerPanel.Height);

                // Draw accent line at bottom
                using var accentPen = new Pen(AccentBlue, 2);
                g.DrawLine(accentPen, 20, headerPanel.Height - 1, headerPanel.Width - 20, headerPanel.Height - 1);
            };

            _titleLabel = new Label
            {
                Text = "⛅  Weather Still Generator",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = TextPrimary,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(560, 42),
                Location = new Point(0, 16),
                BackColor = Color.Transparent
            };

            _subtitleLabel = new Label
            {
                Text = "WSG — Automated Weather Broadcast System",
                Font = new Font("Segoe UI", 9F, FontStyle.Italic),
                ForeColor = TextDim,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(560, 20),
                Location = new Point(0, 56),
                BackColor = Color.Transparent
            };

            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
            _versionLabel = new Label
            {
                Text = $"v{version}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 115, 140),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(560, 18),
                Location = new Point(0, 75),
                BackColor = Color.Transparent
            };

            headerPanel.Controls.Add(_titleLabel);
            headerPanel.Controls.Add(_subtitleLabel);
            headerPanel.Controls.Add(_versionLabel);

            // ── Status label ──
            _statusLabel = new Label
            {
                Text = "Initializing system checks...",
                Font = new Font("Segoe UI", 9F),
                ForeColor = AccentBlue,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(510, 22),
                Location = new Point(25, 110)
            };

            // ── Custom progress bar panel (painted manually) ──
            _progressPanel = new Panel
            {
                Size = new Size(510, 8),
                Location = new Point(25, 136),
                BackColor = Color.FromArgb(40, 50, 68)
            };
            _progressPanel.Paint += ProgressPanel_Paint;

            // ── Check list panel ──
            _checkListPanel = new Panel
            {
                Location = new Point(15, 152),
                Size = new Size(530, 480),
                AutoScroll = false,
                BackColor = BgDark
            };

            // ── Continue button (hidden until complete) ──
            _continueBtn = new Button
            {
                Text = "▶  Launch",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccentBlue,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(160, 40),
                Location = new Point(200, 655),
                Visible = false,
                Cursor = Cursors.Hand
            };
            _continueBtn.FlatAppearance.BorderSize = 0;
            _continueBtn.FlatAppearance.MouseOverBackColor = AccentGlow;
            _continueBtn.Click += (_, _) =>
            {
                if (_hasFatalFailure)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return;
                }
                DialogResult = _hasFailures ? DialogResult.Abort : DialogResult.OK;
                Close();
            };

            Controls.Add(headerPanel);
            Controls.Add(_statusLabel);
            Controls.Add(_progressPanel);
            Controls.Add(_checkListPanel);
            Controls.Add(_continueBtn);

            // Allow dragging the borderless window
            _titleLabel.MouseDown += OnDrag;
            headerPanel.MouseDown += OnDrag;
            this.MouseDown += OnDrag;

            // ── Fade-in timer ──
            _fadeTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps
            _fadeTimer.Tick += (_, _) =>
            {
                if (Opacity < _targetOpacity)
                {
                    Opacity = Math.Min(_targetOpacity, Opacity + 0.06);
                }
                else
                {
                    _fadeTimer.Stop();
                }
            };

            // ── Smooth progress animation timer ──
            _progressAnimTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _progressAnimTimer.Tick += (_, _) =>
            {
                if (Math.Abs(_progressValue - _progressTarget) > 0.5f)
                {
                    _progressValue += (_progressTarget - _progressValue) * 0.15f;
                    _progressPanel.Invalidate();
                }
                else
                {
                    _progressValue = _progressTarget;
                    _progressPanel.Invalidate();
                }
            };
            _progressAnimTimer.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _fadeTimer.Start();
        }

        private void ProgressPanel_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _progressPanel.Width;
            int h = _progressPanel.Height;

            // Background track (rounded)
            using var trackBrush = new SolidBrush(Color.FromArgb(40, 50, 68));
            using var trackPath = CreateRoundRect(0, 0, w, h, h / 2);
            g.FillPath(trackBrush, trackPath);

            // Fill bar (gradient, rounded)
            int fillW = Math.Max(0, (int)(_progressValue / 100f * w));
            if (fillW > 4)
            {
                using var fillPath = CreateRoundRect(0, 0, fillW, h, h / 2);
                using var fillBrush = new LinearGradientBrush(
                    new Rectangle(0, 0, fillW, h),
                    AccentGlow, AccentBlue, 0F);
                g.FillPath(fillBrush, fillPath);

                // Glow highlight on top half
                using var glowBrush = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
                using var glowPath = CreateRoundRect(0, 0, fillW, h / 2, h / 4);
                g.FillPath(glowBrush, glowPath);
            }
        }

        private static GraphicsPath CreateRoundRect(float x, float y, float w, float h, float r)
        {
            r = Math.Min(r, Math.Min(w, h) / 2f);
            var path = new GraphicsPath();
            if (r <= 0) { path.AddRectangle(new RectangleF(x, y, w, h)); return path; }
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
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

        // ── Rounded window with shadow ──
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Outer border with subtle glow
            using var borderPen = new Pen(Color.FromArgb(50, 65, 90), 1);
            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            // Inner accent line at top
            using var topAccent = new Pen(AccentBlue, 2);
            g.DrawLine(topAccent, 0, 0, Width, 0);
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
            _runner.Add(new SingleInstanceCheck());
            _runner.Add(new EnvironmentCheck());
            _runner.Add(new AppUpdateCheck());
            _runner.Add(settingsCheck);
            _runner.Add(new DependencyCheck());
            _runner.Add(new FFmpegCheck());
            _runner.Add(new OutputDirectoriesCheck());
            _runner.Add(new CacheCheck());
            _runner.Add(new OpenMeteoCheck());
            _runner.Add(new ECCCCheck());
            _runner.Add(new AlertReadyCheck());
            _runner.Add(new NwsNoaaCheck());
            _runner.Add(new NaadConnectionCheck());
            _runner.Add(new WebUICheck());
            _runner.Add(new WebUINetworkAccessCheck());

            // Create UI rows for each check
            _checkRows.Clear();
            _checkListPanel.Controls.Clear();
            int y = 0;
            var checks = new string[]
            {
                "Single Instance", "Environment", "App Update", "Configuration", "Dependencies",
                "FFmpeg", "Output Directories", "Cache",
                "Open Meteo API", "ECCC Weather", "Alert Ready (NAAD)",
                "NWS NOAA", "NAAD Connection", "Web UI", "Web UI Network"
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
            _progressTarget = Math.Min(100, (int)((idx / (double)total) * 100));

            if (idx < _checkRows.Count)
            {
                _checkRows[idx].SetStatus(BootCheckStatus.Running, "Checking...");
            }
        }

        private void OnCheckCompleted(int idx, int total, BootCheckResult result)
        {
            _progressTarget = Math.Min(100, (int)(((idx + 1) / (double)total) * 100));

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
            _progressTarget = 100;

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

            // Check if any failure is fatal (e.g. another instance running)
            _hasFatalFailure = false;
            foreach (var r in results)
            {
                if (r.IsFatal)
                {
                    _hasFatalFailure = true;
                    break;
                }
            }

            if (_hasFatalFailure)
            {
                _statusLabel.Text = "Cannot start — another instance is already running";
                _statusLabel.ForeColor = RedFail;
                _continueBtn.Text = "Exit";
                _continueBtn.BackColor = Color.FromArgb(192, 57, 43);
            }
            else if (failed > 0)
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
        //  Inner class: Expandable check row with animations
        // ═══════════════════════════════════════════════════════════════
        private class CheckRow : Panel
        {
            private readonly Label _icon;
            private readonly Label _nameLabel;
            private readonly Label _statusLabel;
            private readonly Label _expandIcon;
            private readonly Label _detailLabel;
            private bool _expanded = false;
            private bool _hasDetail = false;
            private string _detailText = "";
            private readonly int _collapsedHeight = 28;
            private readonly int _parentWidth;
            private readonly System.Windows.Forms.Timer _animTimer;
            private int _targetHeight;
            private Color _targetBg;
            private BootCheckStatus _currentStatus = BootCheckStatus.Skipped;

            public CheckRow(string name, int width)
            {
                _parentWidth = width;
                Size = new Size(width, _collapsedHeight);
                BackColor = BgDark;
                Cursor = Cursors.Default;
                DoubleBuffered = true;

                _icon = new Label
                {
                    Text = "○",
                    Font = new Font("Segoe UI", 10F),
                    ForeColor = TextDim,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Size = new Size(26, 26),
                    Location = new Point(4, 1),
                    BackColor = Color.Transparent
                };

                _nameLabel = new Label
                {
                    Text = name,
                    Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                    ForeColor = TextPrimary,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Size = new Size(180, 26),
                    Location = new Point(32, 1),
                    BackColor = Color.Transparent
                };

                _statusLabel = new Label
                {
                    Text = "Pending",
                    Font = new Font("Segoe UI", 8F),
                    ForeColor = TextDim,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Size = new Size(width - 250, 26),
                    Location = new Point(215, 1),
                    BackColor = Color.Transparent
                };

                _expandIcon = new Label
                {
                    Text = "",
                    Font = new Font("Segoe UI", 8F),
                    ForeColor = TextDim,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Size = new Size(22, 26),
                    Location = new Point(width - 28, 1),
                    BackColor = Color.Transparent,
                    Visible = false
                };

                _detailLabel = new Label
                {
                    Text = "",
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = Color.FromArgb(160, 175, 200),
                    AutoSize = false,
                    TextAlign = ContentAlignment.TopLeft,
                    Size = new Size(width - 60, 0),
                    Location = new Point(36, _collapsedHeight),
                    BackColor = Color.Transparent,
                    Visible = false
                };

                Controls.Add(_icon);
                Controls.Add(_nameLabel);
                Controls.Add(_statusLabel);
                Controls.Add(_expandIcon);
                Controls.Add(_detailLabel);

                _targetHeight = _collapsedHeight;
                _targetBg = BgDark;

                // Smooth height animation
                _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
                _animTimer.Tick += (_, _) =>
                {
                    bool changed = false;
                    if (Math.Abs(Height - _targetHeight) > 1)
                    {
                        int newH = Height + (int)((_targetHeight - Height) * 0.25);
                        if (newH == Height) newH = _targetHeight; // snap
                        Height = newH;
                        changed = true;
                    }
                    else if (Height != _targetHeight)
                    {
                        Height = _targetHeight;
                        changed = true;
                    }

                    if (changed)
                    {
                        // Reflow siblings
                        var parent = Parent;
                        if (parent != null)
                        {
                            int y = 0;
                            foreach (Control c in parent.Controls)
                            {
                                if (c is CheckRow row)
                                {
                                    row.Location = new Point(row.Location.X, y);
                                    y += row.Height + 2;
                                }
                            }
                        }
                    }
                    else
                    {
                        _animTimer.Stop();
                    }
                };

                // Click to expand/collapse
                this.Click += OnRowClick;
                _nameLabel.Click += OnRowClick;
                _statusLabel.Click += OnRowClick;
                _icon.Click += OnRowClick;
                _expandIcon.Click += OnRowClick;

                // Hover highlight
                void SetHover(bool hover)
                {
                    if (_hasDetail && !_expanded)
                        BackColor = hover ? RowHover : BgDark;
                }
                this.MouseEnter += (_, _) => SetHover(true);
                this.MouseLeave += (_, _) => SetHover(false);
                _nameLabel.MouseEnter += (_, _) => SetHover(true);
                _nameLabel.MouseLeave += (_, _) => SetHover(false);
                _statusLabel.MouseEnter += (_, _) => SetHover(true);
                _statusLabel.MouseLeave += (_, _) => SetHover(false);
            }

            private void OnRowClick(object? sender, EventArgs e)
            {
                if (!_hasDetail) return;

                _expanded = !_expanded;
                _expandIcon.Text = _expanded ? "▾" : "▸";

                if (_expanded)
                {
                    _detailLabel.Visible = true;
                    _detailLabel.Text = _detailText;
                    // Calculate needed height for detail text
                    using var g = CreateGraphics();
                    var textSize = g.MeasureString(_detailText, _detailLabel.Font,
                        _detailLabel.Width);
                    int detailH = Math.Max(20, (int)textSize.Height + 8);
                    _detailLabel.Height = detailH;
                    _targetHeight = _collapsedHeight + detailH + 6;
                    BackColor = RowExpanded;
                    Cursor = Cursors.Hand;
                }
                else
                {
                    _targetHeight = _collapsedHeight;
                    _detailLabel.Visible = false;
                    BackColor = BgDark;
                }

                _animTimer.Start();
            }

            public void SetStatus(BootCheckStatus status, string message)
            {
                _currentStatus = status;
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

                // Bold name for active/completed checks
                if (status == BootCheckStatus.Running)
                    _nameLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                else if (status != BootCheckStatus.Skipped)
                    _nameLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            }

            public void SetDetail(string detail)
            {
                _detailText = detail;
                _hasDetail = !string.IsNullOrWhiteSpace(detail);
                _expandIcon.Visible = _hasDetail;
                _expandIcon.Text = "▸";
                Cursor = _hasDetail ? Cursors.Hand : Cursors.Default;

                // Also keep tooltip for accessibility
                var tt = new ToolTip { AutoPopDelay = 10000 };
                tt.SetToolTip(_statusLabel, detail);
                tt.SetToolTip(_nameLabel, detail);
            }
        }
    }
}
