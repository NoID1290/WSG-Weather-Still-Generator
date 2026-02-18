using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Forms
{
    /// <summary>
    /// Full-screen Windows Form that displays an emergency alert using the exact same
    /// visual rendering as the generated alert images/video frames (Alert Ready &amp; NWS styles).
    /// </summary>
    public class AlertDisplayForm : Form
    {
        private readonly AlertEntry _alert;
        private readonly string _language;
        private readonly string _fontFamily;
        private readonly System.Windows.Forms.Timer _autoCloseTimer;
        private readonly System.Windows.Forms.Timer _flashTimer;
        private bool _flashState = true;
        private int _autoCloseSeconds;
        private readonly Label _countdownLabel;

        // Audio playback
        private Process? _audioProcess;
        private string? _generatedAudioPath;
        private CancellationTokenSource? _audioCts;

        /// <summary>
        /// Creates a new AlertDisplayForm that renders the alert exactly as it appears in video.
        /// </summary>
        /// <param name="alert">The alert to display</param>
        /// <param name="language">Language code (en-CA, fr-CA)</param>
        /// <param name="autoCloseSeconds">Auto-close after N seconds (0 = manual close only)</param>
        public AlertDisplayForm(AlertEntry alert, string language = "fr-CA", int autoCloseSeconds = 60)
        {
            _alert = alert ?? throw new ArgumentNullException(nameof(alert));
            _language = language;
            _autoCloseSeconds = autoCloseSeconds;

            // Load font family from config
            try
            {
                var config = ConfigManager.LoadConfig();
                _fontFamily = config.ImageGeneration?.FontFamily ?? "Arial";
            }
            catch
            {
                _fontFamily = "Arial";
            }

            // Form setup — fullscreen, no borders, topmost
            this.Text = "Emergency Alert";
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.DoubleBuffered = true;
            this.KeyPreview = true;
            this.BackColor = Color.Black;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            // ESC or click to close
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                {
                    this.Close();
                }
            };
            this.Click += (s, e) => this.Close();

            // Close button (top-right)
            var closeBtn = new Label
            {
                Text = "✕",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            closeBtn.Click += (s, e) => this.Close();
            this.Controls.Add(closeBtn);
            this.Resize += (s, e) =>
            {
                closeBtn.Location = new Point(this.ClientSize.Width - closeBtn.Width - 20, 20);
            };

            // Countdown label (bottom-right)
            _countdownLabel = new Label
            {
                Font = new Font("Segoe UI", 12, FontStyle.Regular),
                ForeColor = Color.FromArgb(180, 200, 220),
                BackColor = Color.Transparent,
                AutoSize = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            this.Controls.Add(_countdownLabel);
            this.Resize += (s, e) =>
            {
                _countdownLabel.Location = new Point(this.ClientSize.Width - _countdownLabel.Width - 30,
                    this.ClientSize.Height - _countdownLabel.Height - 20);
            };

            // Flash timer for border pulse effect
            _flashTimer = new System.Windows.Forms.Timer { Interval = 800 };
            _flashTimer.Tick += (s, e) =>
            {
                _flashState = !_flashState;
                this.Invalidate();
            };
            _flashTimer.Start();

            // Auto-close timer
            _autoCloseTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _autoCloseTimer.Tick += (s, e) =>
            {
                _autoCloseSeconds--;
                if (_autoCloseSeconds <= 0)
                {
                    _autoCloseTimer.Stop();
                    this.Close();
                }
                else
                {
                    _countdownLabel.Text = $"Closes in {_autoCloseSeconds}s  |  Press ESC or click to dismiss";
                }
            };
            if (autoCloseSeconds > 0)
            {
                _countdownLabel.Text = $"Closes in {_autoCloseSeconds}s  |  Press ESC or click to dismiss";
                _autoCloseTimer.Start();
            }
            else
            {
                _countdownLabel.Text = "Press ESC or click to dismiss";
            }

            // Start audio generation + playback on background thread
            _audioCts = new CancellationTokenSource();
            _ = Task.Run(() => GenerateAndPlayAudioAsync(_audioCts.Token));
        }

        /// <summary>
        /// Generates the alert audio (tone + TTS) on a background thread and plays it.
        /// </summary>
        private async Task GenerateAndPlayAudioAsync(CancellationToken token)
        {
            try
            {
                string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AlertFormAudio");
                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                string provider = _alert.Provider ?? "";

                // Only generate emergency audio for AlertReady (NAAD) and NWS alerts
                if (provider != "Canada_AlertReady" && provider != "USA_NWS")
                {
                    Logger.Log($"[AlertDisplayForm] Skipping audio generation for non-emergency provider '{provider}'", Logger.LogLevel.Info);
                    return;
                }

                Logger.Log("[AlertDisplayForm] Generating alert audio (tone + TTS)...", Logger.LogLevel.Info);

                // Use EmergencyAlertGenerator to produce the audio file (tone + TTS combined)
                string audioPath = EmergencyAlertGenerator.GenerateAlertAudioPublic(_alert, outputDir, 1, _language, provider);

                if (token.IsCancellationRequested) return;

                if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
                {
                    _generatedAudioPath = audioPath;
                    Logger.Log($"[AlertDisplayForm] Playing alert audio: {audioPath}", Logger.LogLevel.Info);
                    await PlayAudioAsync(audioPath, token);
                }
                else
                {
                    Logger.Log("[AlertDisplayForm] No audio file generated, skipping playback.", Logger.LogLevel.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on form close
            }
            catch (Exception ex)
            {
                Logger.Log($"[AlertDisplayForm] Audio playback error: {ex.Message}", Logger.LogLevel.Error);
            }
        }

        /// <summary>
        /// Plays a WAV/MP3 audio file using ffplay (hidden window, no video).
        /// </summary>
        private async Task PlayAudioAsync(string audioPath, CancellationToken token)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffplay",
                    Arguments = $"-nodisp -autoexit -loglevel quiet \"{audioPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                _audioProcess = Process.Start(psi);
                if (_audioProcess != null)
                {
                    Logger.Log("[AlertDisplayForm] ffplay started for alert audio.", Logger.LogLevel.Debug);
                    await Task.Run(() =>
                    {
                        try { _audioProcess.WaitForExit(); }
                        catch { /* process killed */ }
                    }, token);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[AlertDisplayForm] ffplay failed: {ex.Message}, trying System.Media.SoundPlayer...", Logger.LogLevel.Warning);

                // Fallback: System.Media.SoundPlayer (WAV only)
                if (audioPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var player = new System.Media.SoundPlayer(audioPath);
                        player.PlaySync();
                    }
                    catch (Exception ex2)
                    {
                        Logger.Log($"[AlertDisplayForm] SoundPlayer fallback also failed: {ex2.Message}", Logger.LogLevel.Error);
                    }
                }
            }
        }

        private void StopAudio()
        {
            try
            {
                _audioCts?.Cancel();
                if (_audioProcess != null && !_audioProcess.HasExited)
                {
                    _audioProcess.Kill();
                    _audioProcess.Dispose();
                    _audioProcess = null;
                    Logger.Log("[AlertDisplayForm] Audio playback stopped.", Logger.LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[AlertDisplayForm] Error stopping audio: {ex.Message}", Logger.LogLevel.Warning);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            int width = this.ClientSize.Width;
            int height = this.ClientSize.Height;

            string provider = _alert.Provider ?? "";

            if (provider == "USA_NWS")
            {
                PaintNwsAlert(g, width, height);
            }
            else if (provider == "Canada_AlertReady")
            {
                PaintAlertReadyAlert(g, width, height);
            }
            else
            {
                // ECCC / unknown provider — use a simple generic rendering
                PaintAlertReadyAlert(g, width, height);
            }
        }

        #region Alert Ready (Canada) Rendering

        private void PaintAlertReadyAlert(Graphics g, int width, int height)
        {
            float margin = 50f;

            // Background color based on severity
            Color bgColor = GetSeverityBackgroundColor(_alert.SeverityColor);
            using (var bgBrush = new SolidBrush(bgColor))
            {
                g.FillRectangle(bgBrush, 0, 0, width, height);
            }

            // Outer border (pulse effect)
            float borderInset = 16;
            Color borderColor = _flashState ? Color.White : Color.FromArgb(200, 255, 255, 255);
            float borderWidth = _flashState ? 8 : 6;
            using (var borderPen = new Pen(borderColor, borderWidth))
            {
                g.DrawRectangle(borderPen, borderInset, borderInset,
                    width - borderInset * 2, height - borderInset * 2);
            }

            // Inner border
            float innerInset = 28;
            using (var innerPen = new Pen(Color.FromArgb(120, Color.White), 2))
            {
                g.DrawRectangle(innerPen, innerInset, innerInset,
                    width - innerInset * 2, height - innerInset * 2);
            }

            float contentLeft = margin * 2.5f;
            float contentWidth = width - (contentLeft * 2);
            var centerFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };

            // === HEADER: Alert Ready logo text ===
            float currentY = 50;
            using (Font logoFont = new Font(_fontFamily, 42, FontStyle.Bold))
            using (Brush whiteBrush = new SolidBrush(Color.White))
            {
                string logoText = _language == "en-CA" ? "ALERT READY" : "QUÉBEC EN ALERTE";
                SizeF logoSize = g.MeasureString(logoText, logoFont);
                float logoX = (width - logoSize.Width) / 2;
                g.DrawString(logoText, logoFont, whiteBrush, logoX, currentY);
                currentY += logoSize.Height + 10;
            }

            // === WARNING TRIANGLE ICON ===
            float triangleSize = 70;
            float triangleCenterY = currentY + triangleSize + 5;
            DrawAlertSymbol(g, width / 2f, triangleCenterY, triangleSize);
            currentY = triangleCenterY + triangleSize * 0.6f + 25;

            // === TITLE / HEADLINE ===
            using (Font titleFont = new Font(_fontFamily, 38, FontStyle.Bold))
            using (Brush whiteBrush = new SolidBrush(Color.White))
            {
                string titleText = _alert.Title ?? (_language == "en-CA" ? "EMERGENCY ALERT" : "ALERTE D'URGENCE");
                if (string.IsNullOrWhiteSpace(titleText))
                    titleText = _language == "en-CA" ? "EMERGENCY ALERT" : "ALERTE D'URGENCE";

                string wrappedTitle = WrapText(titleText, titleFont, g, contentWidth);
                if (string.IsNullOrWhiteSpace(wrappedTitle))
                    wrappedTitle = titleText;

                SizeF titleSize = g.MeasureString(wrappedTitle, titleFont, (int)contentWidth);
                g.DrawString(wrappedTitle, titleFont, whiteBrush,
                    new RectangleF(contentLeft, currentY, contentWidth, titleSize.Height + 10), centerFormat);
                currentY += titleSize.Height + 20;
            }

            // === LOCATION / AREA ===
            if (!string.IsNullOrWhiteSpace(_alert.City))
            {
                using (Font areaFont = new Font(_fontFamily, 30, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    string wrappedArea = WrapText(_alert.City, areaFont, g, contentWidth);
                    if (string.IsNullOrWhiteSpace(wrappedArea))
                        wrappedArea = _alert.City;

                    SizeF areaSize = g.MeasureString(wrappedArea, areaFont, (int)contentWidth);
                    g.DrawString(wrappedArea, areaFont, whiteBrush,
                        new RectangleF(contentLeft, currentY, contentWidth, areaSize.Height + 10), centerFormat);
                    currentY += areaSize.Height + 20;
                }
            }

            // === SUMMARY / DESCRIPTION (semi-transparent box) ===
            if (!string.IsNullOrWhiteSpace(_alert.Summary))
            {
                float summaryBoxTop = currentY;
                float summaryBoxPadding = 20;
                float footerReserve = 120;
                float maxSummaryHeight = height - summaryBoxTop - footerReserve - summaryBoxPadding * 2;

                using (Font summaryFont = new Font(_fontFamily, 22, FontStyle.Regular))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    // Build summary text including certainty/urgency if available
                    string fullSummary = _alert.Summary ?? "";
                    var metaParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(_alert.Confidence))
                        metaParts.Add($"{(_language == "en-CA" ? "Certainty" : "Certitude")}: {_alert.Confidence}");
                    if (!string.IsNullOrWhiteSpace(_alert.Impact))
                        metaParts.Add($"{(_language == "en-CA" ? "Urgency" : "Urgence")}: {_alert.Impact}");
                    if (metaParts.Count > 0)
                        fullSummary += "  " + string.Join("  ", metaParts);

                    string wrappedSummary = WrapText(fullSummary, summaryFont, g, contentWidth - summaryBoxPadding * 2);
                    if (string.IsNullOrWhiteSpace(wrappedSummary))
                        wrappedSummary = fullSummary ?? "Alert issued for your area.";

                    SizeF summarySize = g.MeasureString(wrappedSummary, summaryFont, (int)(contentWidth - summaryBoxPadding * 2));
                    float actualSummaryH = Math.Min(summarySize.Height, maxSummaryHeight);
                    if (summarySize.Height > maxSummaryHeight)
                    {
                        wrappedSummary = TruncateText(wrappedSummary, summaryFont, g, contentWidth - summaryBoxPadding * 2, maxSummaryHeight);
                    }

                    // Semi-transparent background box
                    float boxHeight = actualSummaryH + summaryBoxPadding * 2;
                    using (var boxBrush = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                    {
                        float boxLeft = contentLeft - 5;
                        g.FillRectangle(boxBrush, boxLeft, summaryBoxTop, contentWidth + 10, boxHeight);
                    }

                    g.DrawString(wrappedSummary, summaryFont, whiteBrush,
                        new RectangleF(contentLeft + summaryBoxPadding, summaryBoxTop + summaryBoxPadding,
                            contentWidth - summaryBoxPadding * 2, actualSummaryH),
                        centerFormat);
                }
            }

            // === FOOTER ===
            using (Font footerFont = new Font(_fontFamily, 18, FontStyle.Bold))
            using (Brush whiteBrush = new SolidBrush(Color.White))
            {
                string footerText = _language == "en-CA"
                    ? "Follow instructions from local authorities \u2022 Stay informed"
                    : "Suivez les instructions des autorités locales \u2022 Restez informés";
                SizeF footerSize = g.MeasureString(footerText, footerFont);
                float footerX = (width - footerSize.Width) / 2;
                float footerY = height - 80 - footerSize.Height / 2;
                g.DrawString(footerText, footerFont, whiteBrush, footerX, footerY);
            }
        }

        #endregion

        #region NWS (USA) Rendering

        private void PaintNwsAlert(Graphics g, int width, int height)
        {
            float margin = 60;

            // NWS Blue background
            using (var bgBrush = new SolidBrush(Color.FromArgb(0, 51, 153)))
            {
                g.FillRectangle(bgBrush, 0, 0, width, height);
            }

            // Severity-based accent bar at top
            Color accentColor = GetNwsSeverityColor(_alert.Severity);
            using (var accentBrush = new SolidBrush(accentColor))
            {
                g.FillRectangle(accentBrush, 0, 0, width, 12);
            }

            // Border (pulse effect)
            Color borderColor = _flashState ? Color.FromArgb(180, Color.White) : Color.FromArgb(140, Color.White);
            using (var borderPen = new Pen(borderColor, 3))
            {
                g.DrawRectangle(borderPen, 8, 8, width - 16, height - 16);
            }

            float currentY = margin + 10;
            using (Brush whiteBrush = new SolidBrush(Color.White))
            using (Brush accentBrushText = new SolidBrush(accentColor))
            using (Brush dimBrush = new SolidBrush(Color.FromArgb(180, 200, 220)))
            using (Font headerFont = new Font(_fontFamily, 34, FontStyle.Bold))
            using (Font typeFont = new Font(_fontFamily, 40, FontStyle.Bold))
            using (Font bodyFont = new Font(_fontFamily, 26, FontStyle.Regular))
            using (Font smallFont = new Font(_fontFamily, 22, FontStyle.Regular))
            using (Font tinyFont = new Font(_fontFamily, 18, FontStyle.Regular))
            using (Font badgeFont = new Font(_fontFamily, 16, FontStyle.Bold))
            {
                // NWS header line
                string nwsHeader = "NATIONAL WEATHER SERVICE";
                SizeF nwsSize = g.MeasureString(nwsHeader, smallFont);
                g.DrawString(nwsHeader, smallFont, dimBrush, (width - nwsSize.Width) / 2, currentY);
                currentY += nwsSize.Height + 5;

                // Severity/Urgency/Certainty badge bar
                float badgeX = margin;
                if (!string.IsNullOrWhiteSpace(_alert.Severity))
                {
                    badgeX = DrawBadge(g, $"SEVERITY: {_alert.Severity.ToUpperInvariant()}", badgeFont, accentColor, badgeX, currentY, 8);
                    badgeX += 12;
                }
                if (!string.IsNullOrWhiteSpace(_alert.Urgency))
                {
                    badgeX = DrawBadge(g, $"URGENCY: {_alert.Urgency.ToUpperInvariant()}", badgeFont, Color.FromArgb(52, 73, 94), badgeX, currentY, 8);
                    badgeX += 12;
                }
                if (!string.IsNullOrWhiteSpace(_alert.Certainty))
                {
                    DrawBadge(g, $"CERTAINTY: {_alert.Certainty.ToUpperInvariant()}", badgeFont, Color.FromArgb(44, 62, 80), badgeX, currentY, 8);
                }
                currentY += 35;

                // Separator line
                using (var linePen = new Pen(Color.FromArgb(80, Color.White), 1f))
                {
                    g.DrawLine(linePen, margin, currentY, width - margin, currentY);
                }
                currentY += 15;

                // Alert type in accent color
                string alertType = (_alert.Type ?? "ALERT").ToUpperInvariant();
                SizeF typeSize = g.MeasureString(alertType, typeFont, (int)(width - margin * 2));
                g.DrawString(alertType, typeFont, accentBrushText, margin, currentY);
                currentY += typeSize.Height + 10;

                // "The National Weather Service has issued..."
                string headerText = $"The National Weather Service has issued a {_alert.Type} for the following counties or areas:";
                var headerRect = new RectangleF(margin, currentY, width - margin * 2, height);
                SizeF headerSize = g.MeasureString(headerText, bodyFont, (int)(width - margin * 2));
                g.DrawString(headerText, bodyFont, whiteBrush, headerRect);
                currentY += headerSize.Height + 20;

                // Counties/Areas
                if (!string.IsNullOrWhiteSpace(_alert.City))
                {
                    var areaRect = new RectangleF(margin, currentY, width - margin * 2, height - currentY - 120);
                    SizeF areaSize = g.MeasureString(_alert.City, bodyFont, (int)(width - margin * 2));
                    g.DrawString(_alert.City, bodyFont, whiteBrush, areaRect);
                    currentY += Math.Min(areaSize.Height, 80) + 20;
                }

                // Time information
                if (_alert.IssuedAt.HasValue)
                {
                    string timeText = $"Issued at {_alert.IssuedAt.Value.ToLocalTime():h:mm tt} on {_alert.IssuedAt.Value.ToLocalTime():MMM d, yyyy}";
                    g.DrawString(timeText, smallFont, dimBrush, margin, currentY);
                    currentY += 30;
                }

                if (_alert.ExpiresAt.HasValue)
                {
                    string expiresText = $"Effective until {_alert.ExpiresAt.Value.ToLocalTime():h:mm tt} {_alert.ExpiresAt.Value.ToLocalTime():MMM d, yyyy}";
                    g.DrawString(expiresText, smallFont, dimBrush, margin, currentY);
                    currentY += 35;
                }

                // Description text
                float footerReserveNws = 80; // space for station message at bottom
                if (!string.IsNullOrWhiteSpace(_alert.Description))
                {
                    currentY += 10;
                    float availableDescHeight = height - currentY - footerReserveNws;
                    // If we also have Instructions, split the remaining space
                    bool hasInstructions = !string.IsNullOrWhiteSpace(_alert.Instructions);
                    float descHeight = hasInstructions
                        ? Math.Max(availableDescHeight * 0.55f, 100)
                        : Math.Max(availableDescHeight, 100);

                    if (descHeight > 60)
                    {
                        using (var descBg = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                        {
                            g.FillRectangle(descBg, margin - 10, currentY - 5, width - margin * 2 + 20, descHeight + 10);
                        }

                        // Use the full description — let the rect clip naturally rather than truncating prematurely
                        string descText = _alert.Description.Length > 2000
                            ? _alert.Description.Substring(0, 1997) + "..."
                            : _alert.Description;
                        var descRect = new RectangleF(margin, currentY, width - margin * 2, descHeight);
                        g.DrawString(descText, tinyFont, whiteBrush, descRect);
                        currentY += descHeight + 15;
                    }
                }

                // Instructions text (if available)
                if (!string.IsNullOrWhiteSpace(_alert.Instructions))
                {
                    float instrAvailable = height - currentY - footerReserveNws;
                    if (instrAvailable > 50)
                    {
                        string instrText = _alert.Instructions.Length > 1500
                            ? _alert.Instructions.Substring(0, 1497) + "..."
                            : _alert.Instructions;
                        var instrRect = new RectangleF(margin, currentY, width - margin * 2, instrAvailable);
                        g.DrawString(instrText, tinyFont, dimBrush, instrRect);
                        currentY += instrAvailable + 10;
                    }
                }

                // Station message
                string station = "NWS";
                if (!string.IsNullOrWhiteSpace(_alert.Region))
                {
                    // Measure how much width is available for the station text
                    float stationMaxWidth = width - margin * 2 - g.MeasureString("Message from ", smallFont).Width;
                    string regionText = _alert.Region;
                    SizeF regionSize = g.MeasureString(regionText, smallFont);
                    if (regionSize.Width > stationMaxWidth && regionText.Length > 3)
                    {
                        // Binary-search for a fitting length
                        int maxLen = regionText.Length;
                        while (maxLen > 10)
                        {
                            maxLen -= 5;
                            string candidate = regionText.Substring(0, maxLen) + "...";
                            if (g.MeasureString(candidate, smallFont).Width <= stationMaxWidth)
                            {
                                regionText = candidate;
                                break;
                            }
                        }
                    }
                    station = regionText;
                }
                g.DrawString($"Message from {station}.", smallFont, whiteBrush, margin, height - margin - 50);
            }
        }

        #endregion

        #region Drawing Helpers

        private void DrawAlertSymbol(Graphics g, float centerX, float centerY, float size)
        {
            float topY = centerY - size;
            float baseY = centerY + size * 0.55f;
            float halfBase = size * 0.95f;

            PointF[] triangle = new PointF[]
            {
                new PointF(centerX, topY),
                new PointF(centerX - halfBase, baseY),
                new PointF(centerX + halfBase, baseY)
            };

            // Shadow
            PointF[] shadowTri = new PointF[]
            {
                new PointF(centerX + 3, topY + 3),
                new PointF(centerX - halfBase + 3, baseY + 3),
                new PointF(centerX + halfBase + 3, baseY + 3)
            };
            using (var shadowBrush = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            {
                g.FillPolygon(shadowBrush, shadowTri);
            }

            // White triangle fill
            using (var triangleBrush = new SolidBrush(Color.White))
            {
                g.FillPolygon(triangleBrush, triangle);
            }

            // Border
            using (var borderPen = new Pen(Color.FromArgb(60, 60, 60), 3))
            {
                borderPen.LineJoin = LineJoin.Round;
                g.DrawPolygon(borderPen, triangle);
            }

            // Exclamation mark
            float centroidY = (topY + baseY + baseY) / 3f;
            float exclamFontSize = size * 1.0f;
            using (var exclamationBrush = new SolidBrush(Color.FromArgb(40, 40, 40)))
            using (Font exclamationFont = new Font("Arial", exclamFontSize, FontStyle.Bold))
            {
                string exclamation = "!";
                SizeF textSize = g.MeasureString(exclamation, exclamationFont);
                float textX = centerX - textSize.Width / 2f;
                float textY = centroidY - textSize.Height / 2f - size * 0.03f;
                g.DrawString(exclamation, exclamationFont, exclamationBrush, textX, textY);
            }
        }

        private float DrawBadge(Graphics g, string text, Font font, Color bgColor, float x, float y, float padding)
        {
            SizeF textSize = g.MeasureString(text, font);
            float badgeWidth = textSize.Width + padding * 2;
            float badgeHeight = textSize.Height + 4;

            using (var bgBrush = new SolidBrush(bgColor))
            {
                var rect = new RectangleF(x, y, badgeWidth, badgeHeight);
                using (var path = new GraphicsPath())
                {
                    float r = 6;
                    path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
                    path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
                    path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
                    path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
                    path.CloseFigure();
                    g.FillPath(bgBrush, path);
                }
            }
            using (var textBrush = new SolidBrush(Color.White))
            {
                g.DrawString(text, font, textBrush, x + padding, y + 2);
            }
            return x + badgeWidth;
        }

        private static Color GetSeverityBackgroundColor(string severity)
        {
            return severity?.ToLowerInvariant() switch
            {
                "red" => Color.FromArgb(200, 0, 0),
                "yellow" => Color.FromArgb(200, 150, 0),
                _ => Color.FromArgb(80, 80, 80)
            };
        }

        private static Color GetNwsSeverityColor(string? severity)
        {
            if (string.IsNullOrWhiteSpace(severity)) return Color.FromArgb(52, 73, 94);
            return severity.Trim().ToLowerInvariant() switch
            {
                "extreme" => Color.FromArgb(192, 57, 43),
                "severe" => Color.FromArgb(231, 76, 60),
                "moderate" => Color.FromArgb(230, 126, 34),
                "minor" => Color.FromArgb(241, 196, 15),
                _ => Color.FromArgb(52, 73, 94)
            };
        }

        private static string WrapText(string text, Font font, Graphics g, float maxWidth)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var words = text.Split(' ');
            var lines = new List<string>();
            var currentLine = new StringBuilder();

            foreach (var word in words)
            {
                string testLine = currentLine.Length > 0 ? $"{currentLine} {word}" : word;
                SizeF size = g.MeasureString(testLine, font);

                if (size.Width > maxWidth && currentLine.Length > 0)
                {
                    lines.Add(currentLine.ToString());
                    currentLine.Clear();
                    currentLine.Append(word);
                }
                else
                {
                    if (currentLine.Length > 0) currentLine.Append(" ");
                    currentLine.Append(word);
                }
            }

            if (currentLine.Length > 0)
            {
                lines.Add(currentLine.ToString());
            }

            return string.Join("\n", lines);
        }

        private static string TruncateText(string text, Font font, Graphics g, float maxWidth, float maxHeight)
        {
            var lines = text.Split('\n');
            var result = new StringBuilder();
            float currentHeight = 0;

            foreach (var line in lines)
            {
                SizeF lineSize = g.MeasureString(line, font);
                if (currentHeight + lineSize.Height > maxHeight)
                {
                    result.Append("...");
                    break;
                }
                result.AppendLine(line);
                currentHeight += lineSize.Height;
            }

            return result.ToString().TrimEnd();
        }

        #endregion

        #region Static Show Helpers

        /// <summary>
        /// Shows the alert display form on the UI thread.
        /// Safe to call from any thread.
        /// </summary>
        public static void ShowAlert(AlertEntry alert, string language = "fr-CA", int autoCloseSeconds = 60)
        {
            try
            {
                if (Application.OpenForms.Count > 0)
                {
                    var mainForm = Application.OpenForms[0];
                    if (mainForm.InvokeRequired)
                    {
                        mainForm.BeginInvoke(new Action(() => ShowAlertOnUiThread(alert, language, autoCloseSeconds)));
                    }
                    else
                    {
                        ShowAlertOnUiThread(alert, language, autoCloseSeconds);
                    }
                }
                else
                {
                    ShowAlertOnUiThread(alert, language, autoCloseSeconds);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[AlertDisplayForm] Error showing alert form: {ex.Message}", Logger.LogLevel.Error);
            }
        }

        private static void ShowAlertOnUiThread(AlertEntry alert, string language, int autoCloseSeconds)
        {
            try
            {
                var form = new AlertDisplayForm(alert, language, autoCloseSeconds);
                form.Show();
                Logger.Log($"[AlertDisplayForm] Displaying alert: {alert.Title}", Logger.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"[AlertDisplayForm] Error creating alert form: {ex.Message}", Logger.LogLevel.Error);
            }
        }

        #endregion

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopAudio();
            _audioCts?.Dispose();
            _autoCloseTimer?.Stop();
            _autoCloseTimer?.Dispose();
            _flashTimer?.Stop();
            _flashTimer?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
