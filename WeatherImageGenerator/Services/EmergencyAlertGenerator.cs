using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EAS;
using EAS.AlertReady;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Generates Alert Ready / NAAD emergency alert visuals and audio following CAP-CP standards.
    /// Handles non-weather emergency alerts (AMBER, civil emergencies, public safety).
    /// </summary>
    public static class EmergencyAlertGenerator
    {
        /// <summary>
        /// Generates emergency alert images and audio files for NAAD/Alert Ready alerts.
        /// </summary>
        /// <param name="alerts">List of non-weather emergency alerts</param>
        /// <param name="outputDir">Output directory for generated files</param>
        /// <param name="language">Language for TTS audio (en-CA or fr-CA)</param>
        /// <returns>List of generated file paths (images and audio)</returns>
        public static List<string> GenerateEmergencyAlerts(List<AlertEntry> alerts, string outputDir, string language = "fr-CA")
        {
            var generatedFiles = new List<string>();

            if (alerts == null || alerts.Count == 0)
            {
                Logger.Log("[EmergencyAlertGenerator] No emergency alerts to generate.", Logger.LogLevel.Info);
                return generatedFiles;
            }

            Logger.Log($"[EmergencyAlertGenerator] Generating {alerts.Count} emergency alert(s)...", Logger.LogLevel.Info);

            var config = ConfigManager.LoadConfig();
            var imgConfig = config.ImageGeneration ?? new ImageGenerationSettings();

            int width = imgConfig.ImageWidth;
            int height = imgConfig.ImageHeight;
            float margin = imgConfig.MarginPixels;

            // Cleanup old emergency alert files
            CleanupOldAlerts(outputDir);

            // Generate each alert
            for (int i = 0; i < alerts.Count; i++)
            {
                var alert = alerts[i];
                try
                {
                    // Determine provider
                    string provider = alert.Provider ?? "Canada_AlertReady";
                    
                    string imageFile = GenerateAlertImage(alert, outputDir, i + 1, width, height, margin, imgConfig, language, provider);
                    generatedFiles.Add(imageFile);

                    string audioFile = GenerateAlertAudio(alert, outputDir, i + 1, language, provider);
                    if (!string.IsNullOrEmpty(audioFile))
                    {
                        generatedFiles.Add(audioFile);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[EmergencyAlertGenerator] Failed to generate alert {i + 1}: {ex.Message}", Logger.LogLevel.Error);
                }
            }

            return generatedFiles;
        }

        /// <summary>
        /// Generates emergency alert video using the alert image and audio.
        /// This creates a video that displays the alert image for the duration of the alert audio.
        /// </summary>
        /// <param name="alertImagePath">Path to the alert image file</param>
        /// <param name="alertAudioPath">Path to the alert audio file (WAV or MP3)</param>
        /// <param name="outputDir">Output directory for the video</param>
        /// <returns>Path to the generated video, or null if generation failed</returns>
        public static string? GenerateAlertVideo(string alertImagePath, string alertAudioPath, string outputDir)
        {
            try
            {
                if (!File.Exists(alertImagePath))
                {
                    Logger.Log($"[EmergencyAlertGenerator] Alert image not found: {alertImagePath}", Logger.LogLevel.Error);
                    return null;
                }

                if (!File.Exists(alertAudioPath))
                {
                    Logger.Log($"[EmergencyAlertGenerator] Alert audio not found: {alertAudioPath}", Logger.LogLevel.Error);
                    return null;
                }

                var config = ConfigManager.LoadConfig();
                var videoConfig = config.Video ?? new VideoSettings();

                // Use configurable alert display duration, falling back to 30s minimum
                double minVideoDuration = videoConfig.AlertDisplayDurationSeconds > 0 
                    ? videoConfig.AlertDisplayDurationSeconds 
                    : 30.0;

                // Determine output file path
                string container = (videoConfig.Container ?? "mp4").Trim().Trim('.');
                string outputPath = Path.Combine(outputDir, $"alert_video.{container}");

                Logger.Log($"[EmergencyAlertGenerator] Generating alert video...", Logger.LogLevel.Info);
                Logger.Log($"  Image: {Path.GetFileName(alertImagePath)}", Logger.LogLevel.Debug);
                Logger.Log($"  Audio: {Path.GetFileName(alertAudioPath)}", Logger.LogLevel.Debug);
                Logger.Log($"  Output: {outputPath}", Logger.LogLevel.Debug);

                // Get audio duration to determine video length
                double? audioDuration = GetAudioDuration(alertAudioPath);
                if (!audioDuration.HasValue || audioDuration.Value <= 0)
                {
                    audioDuration = minVideoDuration;
                    Logger.Log($"[EmergencyAlertGenerator] Could not determine audio duration, using default: {audioDuration}s", Logger.LogLevel.Warning);
                }
                else
                {
                    Logger.Log($"[EmergencyAlertGenerator] Audio duration: {audioDuration:F2}s", Logger.LogLevel.Debug);
                }

                // Enforce minimum video duration (from AlertDisplayDurationSeconds config, default 30s)
                // The image will display for the full duration; audio plays then silence follows
                double videoDuration = Math.Max(audioDuration.Value, minVideoDuration);
                Logger.Log($"[EmergencyAlertGenerator] Video duration: {videoDuration:F2}s (min {minVideoDuration:F0}s)", Logger.LogLevel.Info);

                // Build FFmpeg command to create video from single image + audio
                // -loop 1: loop the image indefinitely
                // -t: set total video duration (at least 30s)
                // Use apad filter to pad audio with silence to match video duration
                var codecArgs = BuildVideoCodecArgs(videoConfig);
                
                string ffmpegArgs = $"-y -loop 1 -i \"{alertImagePath}\" -i \"{alertAudioPath}\" " +
                                   $"-c:v {videoConfig.VideoCodec ?? "libx264"} {codecArgs} " +
                                   $"-af \"apad=whole_dur={videoDuration:F2}\" " +
                                   $"-c:a aac -b:a 192k " +
                                   $"-t {videoDuration:F2} " +
                                   $"-pix_fmt yuv420p " +
                                   $"\"{outputPath}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = ffmpegArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                Logger.Log($"[EmergencyAlertGenerator] Running FFmpeg for alert video...", Logger.LogLevel.Debug);

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    Logger.Log("[EmergencyAlertGenerator] Failed to start FFmpeg process.", Logger.LogLevel.Error);
                    return null;
                }

                // Read stderr asynchronously to avoid deadlocks
                var stderrTask = process.StandardError.ReadToEndAsync();
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                bool exited = process.WaitForExit(300000); // 5 minute timeout
                
                string stderr = "";
                if (stderrTask.Wait(5000))
                {
                    stderr = stderrTask.Result;
                }

                if (!exited)
                {
                    Logger.Log("[EmergencyAlertGenerator] FFmpeg timed out, killing process.", Logger.LogLevel.Warning);
                    try { process.Kill(); } catch { }
                    return null;
                }

                if (process.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000)
                {
                    var outSize = new FileInfo(outputPath).Length;
                    Logger.Log($"[EmergencyAlertGenerator] ✓ Alert video generated: {outputPath} ({outSize / 1024}KB, {videoDuration:F0}s)", Logger.LogLevel.Info);
                    return outputPath;
                }
                else
                {
                    Logger.Log($"[EmergencyAlertGenerator] ✗ FFmpeg failed with exit code {process.ExitCode}", Logger.LogLevel.Error);
                    if (!string.IsNullOrEmpty(stderr))
                    {
                        Logger.Log($"[EmergencyAlertGenerator] FFmpeg error: {stderr.Substring(0, Math.Min(500, stderr.Length))}", Logger.LogLevel.Error);
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[EmergencyAlertGenerator] Error generating alert video: {ex.Message}", Logger.LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// Generates alerts and automatically creates a video from them.
        /// </summary>
        /// <param name="alerts">List of emergency alerts</param>
        /// <param name="outputDir">Output directory</param>
        /// <param name="language">Language for TTS</param>
        /// <returns>Tuple containing list of generated files and the video path (if successful)</returns>
        public static (List<string> GeneratedFiles, string? VideoPath) GenerateEmergencyAlertsWithVideo(
            List<AlertEntry> alerts, string outputDir, string language = "fr-CA")
        {
            // Generate the alert media (images and audio)
            var generatedFiles = GenerateEmergencyAlerts(alerts, outputDir, language);

            if (generatedFiles.Count == 0)
            {
                Logger.Log("[EmergencyAlertGenerator] No alert files generated, skipping video creation.", Logger.LogLevel.Warning);
                return (generatedFiles, null);
            }

            // Find the first image and audio pair
            string? imagePath = generatedFiles.FirstOrDefault(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
            string? audioPath = generatedFiles.FirstOrDefault(f => 
                f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || 
                f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(imagePath) || string.IsNullOrEmpty(audioPath))
            {
                Logger.Log("[EmergencyAlertGenerator] Missing image or audio file, skipping video creation.", Logger.LogLevel.Warning);
                return (generatedFiles, null);
            }

            // Generate the video
            string? videoPath = GenerateAlertVideo(imagePath, audioPath, outputDir);

            if (!string.IsNullOrEmpty(videoPath))
            {
                generatedFiles.Add(videoPath);
            }

            return (generatedFiles, videoPath);
        }

        /// <summary>
        /// Gets the duration of an audio file using FFprobe.
        /// </summary>
        private static double? GetAudioDuration(string audioPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{audioPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return null;

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(10000);

                if (double.TryParse(output, System.Globalization.NumberStyles.Any, 
                    System.Globalization.CultureInfo.InvariantCulture, out double duration))
                {
                    return duration;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmergencyAlertGenerator] Error getting audio duration: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Builds FFmpeg video codec arguments based on configuration.
        /// </summary>
        private static string BuildVideoCodecArgs(VideoSettings config)
        {
            var args = new StringBuilder();

            // Use CRF encoding for quality-based encoding
            if (config.UseCrfEncoding)
            {
                args.Append($"-crf {config.CrfValue} ");
            }
            else
            {
                args.Append($"-b:v {config.VideoBitrate ?? "4M"} ");
            }

            // Add encoder preset
            if (!string.IsNullOrEmpty(config.EncoderPreset))
            {
                args.Append($"-preset {config.EncoderPreset} ");
            }

            // Add maxrate and bufsize if specified
            if (!string.IsNullOrEmpty(config.MaxBitrate))
            {
                args.Append($"-maxrate {config.MaxBitrate} ");
            }
            if (!string.IsNullOrEmpty(config.BufferSize))
            {
                args.Append($"-bufsize {config.BufferSize} ");
            }

            return args.ToString().Trim();
        }

        private static void CleanupOldAlerts(string outputDir)
        {
            try
            {
                var patterns = new[] { "EmergencyAlert_*.png", "EmergencyAlert_*.wav", "EmergencyAlert_*.mp3" };
                foreach (var pattern in patterns)
                {
                    var files = Directory.GetFiles(outputDir, pattern);
                    foreach (var file in files)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmergencyAlertGenerator] Cleanup error: {ex.Message}");
            }
        }

        private static string GenerateAlertImage(AlertEntry alert, string outputDir, int index, 
            int width, int height, float margin, ImageGenerationSettings imgConfig, string language, string provider)
        {
            string filename = $"EmergencyAlert_{index:D2}.png";
            string fullPath = Path.Combine(outputDir, filename);

            try
            {
                using (Bitmap bmp = new Bitmap(width, height))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    // Check provider for different layouts
                    if (provider == "USA_NWS")
                    {
                        DrawNwsAlert(g, alert, width, height, imgConfig, index);
                    }
                    else
                    {
                        DrawAlertReadyAlert(g, alert, width, height, margin, imgConfig, language);
                    }

                    bmp.Save(fullPath, ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[EmergencyAlertGenerator] Error generating image: {ex.Message}", Logger.LogLevel.Error);
                Logger.Log($"  Alert: Title='{alert?.Title}', City='{alert?.City}'", Logger.LogLevel.Debug);
                Logger.Log($"  Stack: {ex.StackTrace}", Logger.LogLevel.Debug);
                throw;
            }

            Logger.Log($"[EmergencyAlertGenerator] Generated image: {filename}", Logger.LogLevel.Info);
            return fullPath;
        }

        private static void DrawNwsAlert(Graphics g, AlertEntry alert, int width, int height, ImageGenerationSettings imgConfig, int pageNum)
        {
            // USA NWS style: Blue background with severity-colored accent bar
            using (var bgBrush = new SolidBrush(Color.FromArgb(0, 51, 153)))  // NWS Blue
            {
                g.FillRectangle(bgBrush, 0, 0, width, height);
            }

            // Severity-based accent bar at top
            Color accentColor = GetNwsSeverityColor(alert.Severity);
            using (var accentBrush = new SolidBrush(accentColor))
            {
                g.FillRectangle(accentBrush, 0, 0, width, 12);
            }

            // Thin white border
            using (var borderPen = new Pen(Color.FromArgb(180, Color.White), 3))
            {
                g.DrawRectangle(borderPen, 8, 8, width - 16, height - 16);
            }

            float margin = 60;
            float currentY = margin + 10;
            using (Brush whiteBrush = new SolidBrush(Color.White))
            using (Brush accentBrushText = new SolidBrush(accentColor))
            using (Brush dimBrush = new SolidBrush(Color.FromArgb(180, 200, 220)))
            using (Font headerFont = new Font(imgConfig.FontFamily ?? "Arial", 34, FontStyle.Bold))
            using (Font typeFont = new Font(imgConfig.FontFamily ?? "Arial", 40, FontStyle.Bold))
            using (Font bodyFont = new Font(imgConfig.FontFamily ?? "Arial", 26, FontStyle.Regular))
            using (Font smallFont = new Font(imgConfig.FontFamily ?? "Arial", 22, FontStyle.Regular))
            using (Font tinyFont = new Font(imgConfig.FontFamily ?? "Arial", 18, FontStyle.Regular))
            using (Font badgeFont = new Font(imgConfig.FontFamily ?? "Arial", 16, FontStyle.Bold))
            {
                // NWS header line
                string nwsHeader = "NATIONAL WEATHER SERVICE";
                SizeF nwsSize = g.MeasureString(nwsHeader, smallFont);
                g.DrawString(nwsHeader, smallFont, dimBrush, (width - nwsSize.Width) / 2, currentY);
                currentY += nwsSize.Height + 5;

                // Severity/Urgency/Certainty badge bar
                float badgeX = margin;
                if (!string.IsNullOrWhiteSpace(alert.Severity))
                {
                    badgeX = DrawBadge(g, $"SEVERITY: {alert.Severity.ToUpperInvariant()}", badgeFont, accentColor, badgeX, currentY, 8);
                    badgeX += 12;
                }
                if (!string.IsNullOrWhiteSpace(alert.Urgency))
                {
                    badgeX = DrawBadge(g, $"URGENCY: {alert.Urgency.ToUpperInvariant()}", badgeFont, Color.FromArgb(52, 73, 94), badgeX, currentY, 8);
                    badgeX += 12;
                }
                if (!string.IsNullOrWhiteSpace(alert.Certainty))
                {
                    badgeX = DrawBadge(g, $"CERTAINTY: {alert.Certainty.ToUpperInvariant()}", badgeFont, Color.FromArgb(44, 62, 80), badgeX, currentY, 8);
                }
                currentY += 35;

                // Separator line
                using (var linePen = new Pen(Color.FromArgb(80, Color.White), 1f))
                {
                    g.DrawLine(linePen, margin, currentY, width - margin, currentY);
                }
                currentY += 15;

                // Alert type in accent color
                string alertType = (alert.Type ?? "ALERT").ToUpperInvariant();
                SizeF typeSize = g.MeasureString(alertType, typeFont, (int)(width - margin * 2));
                g.DrawString(alertType, typeFont, accentBrushText, margin, currentY);
                currentY += typeSize.Height + 10;

                // Title: "The National Weather Service has issued..."
                string headerText = $"The National Weather Service has issued a {alert.Type} for the following counties or areas:";
                var headerRect = new RectangleF(margin, currentY, width - margin * 2, height);
                SizeF headerSize = g.MeasureString(headerText, bodyFont, (int)(width - margin * 2));
                g.DrawString(headerText, bodyFont, whiteBrush, headerRect);
                currentY += headerSize.Height + 20;

                // Counties/Areas
                if (!string.IsNullOrWhiteSpace(alert.City))
                {
                    var areaRect = new RectangleF(margin, currentY, width - margin * 2, height - currentY - 120);
                    SizeF areaSize = g.MeasureString(alert.City, bodyFont, (int)(width - margin * 2));
                    g.DrawString(alert.City, bodyFont, whiteBrush, areaRect);
                    currentY += Math.Min(areaSize.Height, 80) + 20;
                }

                // Time information
                if (alert.IssuedAt.HasValue)
                {
                    string timeText = $"Issued at {alert.IssuedAt.Value.ToLocalTime():h:mm tt} on {alert.IssuedAt.Value.ToLocalTime():MMM d, yyyy}";
                    g.DrawString(timeText, smallFont, dimBrush, margin, currentY);
                    currentY += 30;
                }

                if (alert.ExpiresAt.HasValue)
                {
                    string expiresText = $"Effective until {alert.ExpiresAt.Value.ToLocalTime():h:mm tt} {alert.ExpiresAt.Value.ToLocalTime():MMM d, yyyy}";
                    g.DrawString(expiresText, smallFont, dimBrush, margin, currentY);
                    currentY += 35;
                }

                // Description text
                float footerReserveNws = 60; // space for station message + page number at bottom
                if (!string.IsNullOrWhiteSpace(alert.Description))
                {
                    currentY += 10;
                    float availableDescHeight = height - currentY - footerReserveNws;
                    // If we also have Instructions, split the remaining space
                    bool hasInstructions = !string.IsNullOrWhiteSpace(alert.Instructions);
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
                        string descText = alert.Description.Length > 2000
                            ? alert.Description.Substring(0, 1997) + "..."
                            : alert.Description;
                        var descRect = new RectangleF(margin, currentY, width - margin * 2, descHeight);
                        g.DrawString(descText, tinyFont, whiteBrush, descRect);
                        currentY += descHeight + 15;
                    }
                }

                // Instructions text (if available)
                if (!string.IsNullOrWhiteSpace(alert.Instructions))
                {
                    float instrAvailable = height - currentY - footerReserveNws;
                    if (instrAvailable > 50)
                    {
                        string instrText = alert.Instructions.Length > 1500
                            ? alert.Instructions.Substring(0, 1497) + "..."
                            : alert.Instructions;
                        var instrRect = new RectangleF(margin, currentY, width - margin * 2, instrAvailable);
                        g.DrawString(instrText, tinyFont, dimBrush, instrRect);
                        currentY += instrAvailable + 10;
                    }
                }

                // Message from station
                string station = "NWS";
                if (!string.IsNullOrWhiteSpace(alert.Region))
                {
                    // Measure how much width is available for the station text
                    float stationMaxWidth = width - margin * 2 - g.MeasureString("Message from ", smallFont).Width;
                    string regionText = alert.Region;
                    SizeF regionSize = g.MeasureString(regionText, smallFont);
                    if (regionSize.Width > stationMaxWidth && regionText.Length > 3)
                    {
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
                g.DrawString($"Message from {station}.", smallFont, whiteBrush, margin, height - margin - 30);

                // Page number at bottom right
                string pageInfo = $"{pageNum}/1";
                SizeF pageSize = g.MeasureString(pageInfo, smallFont);
                g.DrawString(pageInfo, smallFont, dimBrush, width - margin - pageSize.Width, height - margin - 30);
            }
        }

        /// <summary>
        /// Draws a colored badge/pill with text and returns the X position after the badge.
        /// </summary>
        private static float DrawBadge(Graphics g, string text, Font font, Color bgColor, float x, float y, float padding)
        {
            SizeF textSize = g.MeasureString(text, font);
            float badgeWidth = textSize.Width + padding * 2;
            float badgeHeight = textSize.Height + 4;

            using (var bgBrush = new SolidBrush(bgColor))
            {
                var rect = new RectangleF(x, y, badgeWidth, badgeHeight);
                // Draw rounded rectangle
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
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

        /// <summary>
        /// Maps NWS severity level to a display color.
        /// </summary>
        private static Color GetNwsSeverityColor(string? severity)
        {
            if (string.IsNullOrWhiteSpace(severity)) return Color.FromArgb(52, 73, 94); // dark gray-blue
            return severity.Trim().ToLowerInvariant() switch
            {
                "extreme" => Color.FromArgb(192, 57, 43),   // Deep red
                "severe"  => Color.FromArgb(231, 76, 60),   // Red
                "moderate" => Color.FromArgb(230, 126, 34),  // Orange
                "minor"   => Color.FromArgb(241, 196, 15),  // Yellow
                _         => Color.FromArgb(52, 73, 94)     // Dark gray-blue
            };
        }

        private static void DrawAlertReadyAlert(Graphics g, AlertEntry alert, int width, int height, float margin, ImageGenerationSettings imgConfig, string language)
        {
            // Canada Alert Ready style: Colored background, warning symbol, bilingual
            // Background color based on severity
            Color bgColor = GetSeverityBackgroundColor(alert.SeverityColor);
                    using (var bgBrush = new SolidBrush(bgColor))
                    {
                        g.FillRectangle(bgBrush, 0, 0, width, height);
                    }

                    // Draw outer border
                    float borderInset = 16;
                    using (var borderPen = new Pen(Color.White, 8))
                    {
                        g.DrawRectangle(borderPen, borderInset, borderInset, width - borderInset * 2, height - borderInset * 2);
                    }

                    // Draw inner border for professional look
                    float innerInset = 28;
                    using (var innerPen = new Pen(Color.FromArgb(120, Color.White), 2))
                    {
                        g.DrawRectangle(innerPen, innerInset, innerInset, width - innerInset * 2, height - innerInset * 2);
                    }

                    float contentLeft = margin * 2.5f;
                    float contentWidth = width - (contentLeft * 2);
                    var centerFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };

                    // === HEADER: Alert Ready logo text ===
                    float currentY = 50;
                    using (Font logoFont = new Font(imgConfig.FontFamily ?? "Arial", 42, FontStyle.Bold))
                    using (Brush whiteBrush = new SolidBrush(Color.White))
                    {
                        try
                        {
                            string logoText = language == "en-CA" ? "ALERT READY" : "QUÉBEC EN ALERTE";
                            SizeF logoSize = g.MeasureString(logoText, logoFont);
                            float logoX = (width - logoSize.Width) / 2;
                            g.DrawString(logoText, logoFont, whiteBrush, logoX, currentY);
                            currentY += logoSize.Height + 10;
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"[GenerateAlertImage] Error drawing logo: {ex.Message}", Logger.LogLevel.Error);
                        }
                    }

                // === WARNING TRIANGLE ICON (properly centered) ===
                float triangleSize = 70;
                float triangleCenterY = currentY + triangleSize + 5;
                DrawAlertSymbol(g, width / 2f, triangleCenterY, triangleSize);
                currentY = triangleCenterY + triangleSize * 0.6f + 25;

                // === TITLE / HEADLINE (larger, bold) ===
                using (Font titleFont = new Font(imgConfig.FontFamily ?? "Arial", 38, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    try
                    {
                        string titleText = alert.Title ?? (language == "en-CA" ? "EMERGENCY ALERT" : "ALERTE D'URGENCE");
                        if (string.IsNullOrWhiteSpace(titleText))
                            titleText = language == "en-CA" ? "EMERGENCY ALERT" : "ALERTE D'URGENCE";
                        
                        string wrappedTitle = WrapText(titleText, titleFont, g, contentWidth);
                        if (string.IsNullOrWhiteSpace(wrappedTitle))
                            wrappedTitle = titleText;
                            
                        SizeF titleSize = g.MeasureString(wrappedTitle, titleFont, (int)contentWidth);
                        g.DrawString(wrappedTitle, titleFont, whiteBrush,
                            new RectangleF(contentLeft, currentY, contentWidth, titleSize.Height + 10), centerFormat);
                        currentY += titleSize.Height + 20;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[GenerateAlertImage] Error drawing title: {ex.Message}", Logger.LogLevel.Error);
                        Logger.Log($"  Title text: '{alert.Title}'", Logger.LogLevel.Debug);
                    }
                }

                // === LOCATION / AREA ===
                if (!string.IsNullOrWhiteSpace(alert.City))
                {
                    using (Font areaFont = new Font(imgConfig.FontFamily ?? "Arial", 30, FontStyle.Bold))
                    using (Brush whiteBrush = new SolidBrush(Color.White))
                    {
                        try
                        {
                            string wrappedArea = WrapText(alert.City, areaFont, g, contentWidth);
                            if (string.IsNullOrWhiteSpace(wrappedArea))
                                wrappedArea = alert.City;
                                
                            SizeF areaSize = g.MeasureString(wrappedArea, areaFont, (int)contentWidth);
                            g.DrawString(wrappedArea, areaFont, whiteBrush,
                                new RectangleF(contentLeft, currentY, contentWidth, areaSize.Height + 10), centerFormat);
                            currentY += areaSize.Height + 20;
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"[GenerateAlertImage] Error drawing area: {ex.Message}", Logger.LogLevel.Error);
                            Logger.Log($"  Area text: '{alert.City}'", Logger.LogLevel.Debug);
                        }
                    }
                }

                // === SUMMARY / DESCRIPTION (in a semi-transparent box) ===
                if (!string.IsNullOrWhiteSpace(alert.Summary))
                {
                    float summaryBoxTop = currentY;
                    float summaryBoxPadding = 20;
                    float footerReserve = 120;
                    float maxSummaryHeight = height - summaryBoxTop - footerReserve - summaryBoxPadding * 2;

                    using (Font summaryFont = new Font(imgConfig.FontFamily ?? "Arial", 22, FontStyle.Regular))
                    using (Brush whiteBrush = new SolidBrush(Color.White))
                    {
                        try
                        {
                            // Build summary text including certainty/urgency if available
                            string fullSummary = alert.Summary ?? "";
                            var metaParts = new List<string>();
                            if (!string.IsNullOrWhiteSpace(alert.Confidence))
                                metaParts.Add($"{(language == "en-CA" ? "Certainty" : "Certitude")}: {alert.Confidence}");
                            if (!string.IsNullOrWhiteSpace(alert.Impact))
                                metaParts.Add($"{(language == "en-CA" ? "Urgency" : "Urgence")}: {alert.Impact}");
                            if (metaParts.Count > 0)
                                fullSummary += "  " + string.Join("  ", metaParts);

                            Logger.Log($"[GenerateAlertImage] Summary before wrap: length={fullSummary?.Length ?? 0}", Logger.LogLevel.Debug);
                            
                            string wrappedSummary = WrapText(fullSummary, summaryFont, g, contentWidth - summaryBoxPadding * 2);
                            Logger.Log($"[GenerateAlertImage] Summary after wrap: length={wrappedSummary?.Length ?? 0}, lines={wrappedSummary?.Split('\n').Length ?? 0}", Logger.LogLevel.Debug);
                            
                            if (string.IsNullOrWhiteSpace(wrappedSummary))
                                wrappedSummary = fullSummary ?? "Alert issued for your area.";

                            SizeF summarySize = g.MeasureString(wrappedSummary, summaryFont, (int)(contentWidth - summaryBoxPadding * 2));
                            Logger.Log($"[GenerateAlertImage] Summary size: width={summarySize.Width}, height={summarySize.Height}", Logger.LogLevel.Debug);

                            float actualSummaryH = Math.Min(summarySize.Height, maxSummaryHeight);
                            if (summarySize.Height > maxSummaryHeight)
                            {
                                wrappedSummary = TruncateText(wrappedSummary, summaryFont, g, contentWidth - summaryBoxPadding * 2, maxSummaryHeight);
                            }

                            // Draw semi-transparent background box for readability
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
                        catch (Exception ex)
                        {
                            Logger.Log($"[GenerateAlertImage] Error drawing summary: {ex.Message}", Logger.LogLevel.Error);
                            Logger.Log($"  Summary length: {alert.Summary?.Length ?? 0}", Logger.LogLevel.Debug);
                            Logger.Log($"  Stack: {ex.StackTrace}", Logger.LogLevel.Debug);
                        }
                    }
                }

                // === FOOTER ===
                using (Font footerFont = new Font(imgConfig.FontFamily ?? "Arial", 18, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    try
                    {
                        string footerText = language == "en-CA"
                            ? "Follow instructions from local authorities \u2022 Stay informed"
                            : "Suivez les instructions des autorités locales \u2022 Restez informés";
                        SizeF footerSize = g.MeasureString(footerText, footerFont);
                        float footerX = (width - footerSize.Width) / 2;
                        float footerY = height - 55 - footerSize.Height / 2;
                        g.DrawString(footerText, footerFont, whiteBrush, footerX, footerY);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[GenerateAlertImage] Error drawing footer: {ex.Message}", Logger.LogLevel.Error);
                    }
                }

            // Footer
            using (Font footerFont = new Font(imgConfig.FontFamily ?? "Arial", 18, FontStyle.Bold))
            using (Brush whiteBrush = new SolidBrush(Color.White))
            {
                try
                {
                    string footerText = language == "en-CA"
                        ? "Follow instructions from local authorities • Stay informed"
                        : "Suivez les instructions des autorités locales • Restez informés";
                    SizeF footerSize = g.MeasureString(footerText, footerFont);
                    float footerX = (width - footerSize.Width) / 2;
                    float footerY = height - 55 - footerSize.Height / 2;
                    g.DrawString(footerText, footerFont, whiteBrush, footerX, footerY);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[GenerateAlertImage] Error drawing footer: {ex.Message}", Logger.LogLevel.Error);
                }
            }
        }

        private static void DrawAlertSymbol(Graphics g, float centerX, float centerY, float size)
        {
            // Draw warning triangle symbol with proper proportions
            // Triangle: equilateral-ish, top vertex at center-size, base at center + size*0.55
            float topY = centerY - size;
            float baseY = centerY + size * 0.55f;
            float halfBase = size * 0.95f;

            PointF[] triangle = new PointF[]
            {
                new PointF(centerX, topY),                   // Top vertex
                new PointF(centerX - halfBase, baseY),       // Bottom left
                new PointF(centerX + halfBase, baseY)        // Bottom right
            };

            // Shadow for depth
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

            // Fill triangle white
            using (var triangleBrush = new SolidBrush(Color.White))
            {
                g.FillPolygon(triangleBrush, triangle);
            }

            // Triangle border
            using (var borderPen = new Pen(Color.FromArgb(60, 60, 60), 3))
            {
                borderPen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                g.DrawPolygon(borderPen, triangle);
            }

            // Exclamation mark - position at centroid of triangle for perfect centering
            // Centroid Y = (topY + baseY + baseY) / 3
            float centroidY = (topY + baseY + baseY) / 3f;
            float exclamFontSize = size * 1.0f;

            using (var exclamationBrush = new SolidBrush(Color.FromArgb(40, 40, 40)))
            using (Font exclamationFont = new Font("Arial", exclamFontSize, FontStyle.Bold))
            {
                string exclamation = "!";
                SizeF textSize = g.MeasureString(exclamation, exclamationFont);
                // Center horizontally on centerX, vertically on the centroid
                // Shift up slightly since the "!" glyph has a dot at the bottom with visual weight
                float textX = centerX - textSize.Width / 2f;
                float textY = centroidY - textSize.Height / 2f - size * 0.03f;
                g.DrawString(exclamation, exclamationFont, exclamationBrush, textX, textY);
            }
        }

        /// <summary>
        /// Public wrapper for audio generation — used by AlertDisplayForm to generate
        /// alert tone + TTS audio independently of video/image generation.
        /// </summary>
        /// <param name="alert">The alert entry</param>
        /// <param name="outputDir">Output directory for the audio file</param>
        /// <param name="index">Alert index (for filename)</param>
        /// <param name="language">Language code (en-CA, fr-CA)</param>
        /// <param name="provider">Provider: USA_NWS or Canada_AlertReady</param>
        /// <returns>Path to the generated audio file, or empty string on failure</returns>
        public static string GenerateAlertAudioPublic(AlertEntry alert, string outputDir, int index, string language, string provider)
        {
            return GenerateAlertAudio(alert, outputDir, index, language, provider);
        }

        private static string GenerateAlertAudio(AlertEntry alert, string outputDir, int index, string language, string provider)
        {
            try
            {
                Logger.Log($"[EmergencyAlertGenerator] Starting audio generation for alert {index}...", Logger.LogLevel.Info);
                
                string filename = $"EmergencyAlert_{index:D2}.wav";
                string fullPath = Path.Combine(outputDir, filename);
                
                // Generate appropriate attention signal based on provider
                string? alertTonePath = null;
                if (provider == "USA_NWS")
                {
                    Logger.Log("[EmergencyAlertGenerator] Generating complete NWS SAME alert (SAME header + attention signal)...", Logger.LogLevel.Debug);
                    alertTonePath = EAS.NWS.NwsSameToneGenerator.GetOrGenerateCompleteSameAlert();
                    if (alertTonePath != null)
                    {
                        Logger.Log($"[EmergencyAlertGenerator] USA NWS SAME alert (with header + 853+960 Hz attention): {alertTonePath}", Logger.LogLevel.Info);
                        Logger.Log($"[EmergencyAlertGenerator] Alert file size: {new FileInfo(alertTonePath).Length} bytes", Logger.LogLevel.Debug);
                    }
                }
                else
                {
                    Logger.Log("[EmergencyAlertGenerator] Generating Alert Ready attention signal (Canadian 3-tone)...", Logger.LogLevel.Debug);
                    alertTonePath = AlertToneGenerator.GetOrGenerateAlertTone();
                    if (alertTonePath != null)
                    {
                        Logger.Log($"[EmergencyAlertGenerator] Canada Alert Ready tone: {alertTonePath}", Logger.LogLevel.Info);
                        Logger.Log($"[EmergencyAlertGenerator] Tone file size: {new FileInfo(alertTonePath).Length} bytes", Logger.LogLevel.Debug);
                    }
                }
                if (alertTonePath == null)
                {
                    Logger.Log("[EmergencyAlertGenerator] Warning: Could not generate alert tone", Logger.LogLevel.Warning);
                }

                // Build alert text for TTS
                Logger.Log("[EmergencyAlertGenerator] Building TTS text...", Logger.LogLevel.Debug);
                StringBuilder audioText = new StringBuilder();
                
                if (provider == "USA_NWS")
                {
                    // USA Emergency Alert System (EAS) format — enhanced with full detail
                    audioText.AppendLine("The Emergency Alert System has been activated.");
                    audioText.AppendLine();
                    audioText.AppendLine($"The National Weather Service has issued a {alert.Type ?? "weather alert"}.");
                    audioText.AppendLine();
                    audioText.AppendLine(alert.Title ?? "Emergency Alert");
                    if (!string.IsNullOrWhiteSpace(alert.City))
                        audioText.AppendLine($"Affected areas: {alert.City}.");
                    if (alert.IssuedAt.HasValue)
                        audioText.AppendLine($"Issued at {alert.IssuedAt.Value.ToLocalTime():h:mm tt} on {alert.IssuedAt.Value.ToLocalTime():MMMM d, yyyy}.");
                    if (alert.ExpiresAt.HasValue)
                        audioText.AppendLine($"Effective until {alert.ExpiresAt.Value.ToLocalTime():h:mm tt}.");
                    audioText.AppendLine();
                    // Include alert description for full detail narration
                    if (!string.IsNullOrWhiteSpace(alert.Description))
                    {
                        string descForTts = CleanTextForTTS(alert.Description);
                        // Limit to ~800 chars for reasonable TTS length
                        if (descForTts.Length > 800)
                            descForTts = descForTts.Substring(0, 797) + "...";
                        audioText.AppendLine(descForTts);
                        audioText.AppendLine();
                    }
                    // Include safety instructions if available
                    if (!string.IsNullOrWhiteSpace(alert.Instructions))
                    {
                        string instrForTts = CleanTextForTTS(alert.Instructions);
                        if (instrForTts.Length > 400)
                            instrForTts = instrForTts.Substring(0, 397) + "...";
                        audioText.AppendLine(instrForTts);
                        audioText.AppendLine();
                    }
                    audioText.AppendLine("Follow instructions from local authorities.");
                    audioText.AppendLine("This concludes this Emergency Alert System message.");
                }
                else if (language == "fr-CA")
                {
                    // Canadian Alert Ready (French)
                    audioText.AppendLine("Alerte d'urgence. Québec en alerte.");
                    audioText.AppendLine(alert.Title ?? "Alerte d'urgence");
                    if (!string.IsNullOrWhiteSpace(alert.City))
                        audioText.AppendLine($"Zone touchée: {alert.City}");
                    if (!string.IsNullOrWhiteSpace(alert.Summary))
                        audioText.AppendLine(CleanTextForTTS(alert.Summary));
                    audioText.AppendLine("Suivez les instructions des autorités locales.");
                }
                else
                {
                    // Canadian Alert Ready (English)
                    audioText.AppendLine("Emergency alert. Alert Ready.");
                    audioText.AppendLine(alert.Title ?? "Emergency Alert");
                    if (!string.IsNullOrWhiteSpace(alert.City))
                        audioText.AppendLine($"Affected area: {alert.City}");
                    if (!string.IsNullOrWhiteSpace(alert.Summary))
                        audioText.AppendLine(CleanTextForTTS(alert.Summary));
                    audioText.AppendLine("Follow instructions from local authorities.");
                }

                string text = audioText.ToString();
                Logger.Log($"[EmergencyAlertGenerator] TTS text length: {text.Length} chars", Logger.LogLevel.Debug);

                // Load TTS settings to check preferred engine
                var config = ConfigManager.LoadConfig();
                var ttsSettings = config.TTS ?? new TTSSettings();
                string preferredEngine = ttsSettings.Engine?.ToLowerInvariant() ?? "piper";

                // Try Piper TTS first (open-source, offline, high-quality)
                if (preferredEngine != "edge")
                {
                    Logger.Log("[EmergencyAlertGenerator] Trying Piper TTS (open-source, offline)...", Logger.LogLevel.Info);
                    if (TryGenerateWithPiperTts(text, fullPath, language, alertTonePath))
                    {
                        Logger.Log($"[EmergencyAlertGenerator] Generated audio with Piper TTS: {filename}", Logger.LogLevel.Info);
                        return fullPath;
                    }
                    Logger.Log("[EmergencyAlertGenerator] Piper TTS failed, trying Edge TTS...", Logger.LogLevel.Debug);
                }

                // Try EdgeTtsClient (high quality neural voices, requires internet)
                Logger.Log("[EmergencyAlertGenerator] Trying Edge TTS Client...", Logger.LogLevel.Info);
                if (TryGenerateWithEdgeTtsClient(text, fullPath, language, alertTonePath))
                {
                    Logger.Log($"[EmergencyAlertGenerator] Generated audio with Edge TTS: {filename}", Logger.LogLevel.Info);
                    return fullPath;
                }
                Logger.Log("[EmergencyAlertGenerator] Edge TTS Client failed, trying CLI...", Logger.LogLevel.Debug);

                // Try edge-tts CLI as backup (if Python installed)
                Logger.Log("[EmergencyAlertGenerator] Trying Edge TTS CLI...", Logger.LogLevel.Info);
                if (TryGenerateWithEdgeTTS(text, fullPath, language, alertTonePath))
                {
                    Logger.Log($"[EmergencyAlertGenerator] Generated audio with Edge TTS CLI: {filename}", Logger.LogLevel.Info);
                    return fullPath;
                }

                // Try Windows.Media.SpeechSynthesis (more voices than SAPI)
                if (TryGenerateWithWindowsMediaTTS(text, fullPath, language, alertTonePath))
                {
                    Logger.Log($"[EmergencyAlertGenerator] Generated audio with Windows Media TTS: {filename}", Logger.LogLevel.Info);
                    return fullPath;
                }

                // Try espeak-ng (if available)
                if (TryGenerateWithEspeak(text, fullPath, language, alertTonePath))
                {
                    Logger.Log($"[EmergencyAlertGenerator] Generated audio with espeak: {filename}", Logger.LogLevel.Info);
                    return fullPath;
                }

                // Try PowerShell SAPI as fallback
                if (TryGenerateWithSAPI(text, fullPath, language, alertTonePath))
                {
                    Logger.Log($"[EmergencyAlertGenerator] Generated audio with SAPI: {filename}", Logger.LogLevel.Info);
                    return fullPath;
                }

                Logger.Log($"[EmergencyAlertGenerator] No TTS engine available for audio generation.", Logger.LogLevel.Error);
                return string.Empty;
            }
            catch (Exception ex)
            {
                Logger.Log($"[EmergencyAlertGenerator] Audio generation error: {ex.Message}", Logger.LogLevel.Error);
                return string.Empty;
            }
        }

        /// <summary>
        /// Try to generate TTS audio using Piper (open-source, offline, high-quality neural TTS).
        /// </summary>
        private static bool TryGenerateWithPiperTts(string text, string outputPath, string language, string? alertTonePath)
        {
            try
            {
                // Load TTS settings from config
                var config = ConfigManager.LoadConfig();
                var ttsSettings = config.TTS ?? new TTSSettings();

                using var client = new PiperTtsClient();

                // Use configured voice or default based on language
                string voice = !string.IsNullOrEmpty(ttsSettings.PiperVoice)
                    ? ttsSettings.PiperVoice
                    : PiperTtsClient.GetVoiceForLanguage(language);

                float lengthScale = ttsSettings.PiperLengthScale ?? 1.0f;

                // Generate TTS to a temp WAV file first (Piper outputs WAV)
                string tempWavPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", $"temp_piper_{Guid.NewGuid()}.wav");
                string tempMp3Path = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", $"temp_piper_{Guid.NewGuid()}.mp3");

                Logger.Log($"[PiperTTS] Attempting synthesis with voice: {voice}, lengthScale: {lengthScale}", Logger.LogLevel.Info);

                bool ttsResult = false;
                try
                {
                    var synthesisTask = Task.Run(async () =>
                    {
                        return await client.SynthesizeToFileAsync(text, tempWavPath, voice, null, lengthScale);
                    });

                    if (synthesisTask.Wait(TimeSpan.FromSeconds(60)))
                    {
                        ttsResult = synthesisTask.Result;
                        Logger.Log($"[PiperTTS] Synthesis completed, result: {ttsResult}", Logger.LogLevel.Debug);
                    }
                    else
                    {
                        Logger.Log("[PiperTTS] TTS synthesis timed out after 60 seconds.", Logger.LogLevel.Warning);
                        return false;
                    }
                }
                catch (AggregateException ex)
                {
                    Logger.Log($"[PiperTTS] TTS synthesis failed: {ex.InnerException?.Message ?? ex.Message}", Logger.LogLevel.Error);
                    return false;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[PiperTTS] TTS synthesis error: {ex.Message}", Logger.LogLevel.Error);
                    return false;
                }

                if (ttsResult && File.Exists(tempWavPath))
                {
                    // Convert WAV to MP3 if ffmpeg is available
                    string ttsAudioPath = tempWavPath;
                    try
                    {
                        var convertTask = Task.Run(async () => await PiperTtsClient.ConvertWavToMp3Async(tempWavPath, tempMp3Path));
                        if (convertTask.Wait(TimeSpan.FromSeconds(30)) && convertTask.Result)
                        {
                            ttsAudioPath = tempMp3Path;
                            try { File.Delete(tempWavPath); } catch { }
                            Logger.Log("[PiperTTS] Converted WAV to MP3.", Logger.LogLevel.Debug);
                        }
                        else
                        {
                            Logger.Log("[PiperTTS] WAV to MP3 conversion failed or timed out, using WAV file.", Logger.LogLevel.Debug);
                        }
                    }
                    catch (Exception convEx)
                    {
                        Logger.Log($"[PiperTTS] WAV to MP3 conversion error: {convEx.Message}, using WAV file.", Logger.LogLevel.Debug);
                    }

                    // Try to prepend the alert attention signal (NWS SAME or Alert Ready)
                    // Per SAME standard: [1s silence] + [Alert Tone] + [1s silence] + [Message]
                    Logger.Log("[PiperTTS] Attempting to prepend alert tone with SAME standard spacing...", Logger.LogLevel.Debug);

                    if (alertTonePath != null && File.Exists(alertTonePath))
                    {
                        Logger.Log($"[PiperTTS] Building SAME-compliant audio structure...", Logger.LogLevel.Debug);
                        // Generate 1-second silences per SAME protocol
                        string? silence1 = GenerateSilence(Path.GetDirectoryName(outputPath) ?? ".");
                        string? silence2 = GenerateSilence(Path.GetDirectoryName(outputPath) ?? ".");
                        
                        try
                        {
                            // Concatenate: [1s silence] + [AlertTone] + [1s silence] + [TTS audio]
                            var audioFiles = new System.Collections.Generic.List<string>();
                            if (silence1 != null) audioFiles.Add(silence1);
                            audioFiles.Add(alertTonePath);
                            if (silence2 != null) audioFiles.Add(silence2);
                            audioFiles.Add(ttsAudioPath);
                            
                            if (AlertToneGenerator.ConcatenateAudioFiles(audioFiles.ToArray(), outputPath))
                            {
                                Logger.Log("[EmergencyAlertGenerator] Successfully created SAME-compliant alert audio.", Logger.LogLevel.Debug);
                                try { File.Delete(ttsAudioPath); } catch { }
                                return File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000;
                            }
                        }
                        catch (Exception concatEx)
                        {
                            Logger.Log($"[PiperTTS] Concatenation error: {concatEx.Message}", Logger.LogLevel.Warning);
                        }
                    }

                    // Fallback: use TTS audio without alert tone
                    Logger.Log("[EmergencyAlertGenerator] Using Piper TTS audio without alert tone prefix.", Logger.LogLevel.Debug);
                    if (outputPath != ttsAudioPath)
                    {
                        if (File.Exists(outputPath)) File.Delete(outputPath);
                        File.Move(ttsAudioPath, outputPath);
                    }
                    return File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"[EmergencyAlertGenerator] PiperTtsClient error: {ex.Message}", Logger.LogLevel.Error);
                if (ex.InnerException != null)
                    Logger.Log($"[EmergencyAlertGenerator] Inner exception: {ex.InnerException.Message}", Logger.LogLevel.Debug);
                return false;
            }
        }

        private static bool TryGenerateWithEdgeTtsClient(string text, string outputPath, string language, string? alertTonePath)
        {
            try
            {
                // Load TTS settings from config
                var config = ConfigManager.LoadConfig();
                var ttsSettings = config.TTS ?? new TTSSettings();
                
                using var client = new EdgeTtsClient();
                
                // Use configured voice or default based on language
                string voice = !string.IsNullOrEmpty(ttsSettings.Voice) 
                    ? ttsSettings.Voice 
                    : EdgeTtsClient.GetVoiceForLanguage(language);
                    
                string rate = ttsSettings.Rate ?? "+0%";
                string pitch = ttsSettings.Pitch ?? "+0Hz";
                
                // Generate TTS to a temp file first
                string tempTtsPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", $"temp_tts_{Guid.NewGuid()}.mp3");
                
                Logger.Log($"[EdgeTTS] Attempting synthesis with voice: {voice}, rate: {rate}, pitch: {pitch}", Logger.LogLevel.Info);
                
                // Run async method with proper timeout handling - wrap in Task.Run to avoid deadlocks
                Logger.Log("[EdgeTTS] Starting TTS synthesis (timeout: 30s)...", Logger.LogLevel.Info);
                
                bool ttsResult = false;
                try
                {
                    // Use Task.Run to run on thread pool and avoid synchronization context deadlock
                    var synthesisTask = Task.Run(async () => 
                    {
                        return await client.SynthesizeToFileAsync(text, tempTtsPath, voice, rate, pitch);
                    });
                    
                    // Wait with a shorter timeout
                    if (synthesisTask.Wait(TimeSpan.FromSeconds(30)))
                    {
                        ttsResult = synthesisTask.Result;
                        Logger.Log($"[EdgeTTS] Synthesis completed, result: {ttsResult}", Logger.LogLevel.Debug);
                    }
                    else
                    {
                        Logger.Log("[EdgeTTS] TTS synthesis timed out after 30 seconds.", Logger.LogLevel.Warning);
                        return false;
                    }
                }
                catch (AggregateException ex)
                {
                    Logger.Log($"[EdgeTTS] TTS synthesis failed: {ex.InnerException?.Message ?? ex.Message}", Logger.LogLevel.Error);
                    return false;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[EdgeTTS] TTS synthesis error: {ex.Message}", Logger.LogLevel.Error);
                    return false;
                }
                
                if (ttsResult && File.Exists(tempTtsPath))
                {
                    // Build SAME-compliant structure: [1s silence] + [Alert Tone] + [1s silence] + [TTS]
                    if (alertTonePath != null && File.Exists(alertTonePath))
                    {
                        string? silence1 = GenerateSilence(Path.GetDirectoryName(outputPath) ?? ".");
                        string? silence2 = GenerateSilence(Path.GetDirectoryName(outputPath) ?? ".");
                        
                        var audioFiles = new System.Collections.Generic.List<string>();
                        if (silence1 != null) audioFiles.Add(silence1);
                        audioFiles.Add(alertTonePath);
                        if (silence2 != null) audioFiles.Add(silence2);
                        audioFiles.Add(tempTtsPath);
                        
                        if (AlertToneGenerator.ConcatenateAudioFiles(audioFiles.ToArray(), outputPath))
                        {
                            Logger.Log("[EmergencyAlertGenerator] Successfully created SAME-compliant alert audio.", Logger.LogLevel.Debug);
                            try { File.Delete(tempTtsPath); } catch { }
                            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000;
                        }
                    }
                    
                    // Fallback: use TTS audio without alert tone
                    Logger.Log("[EmergencyAlertGenerator] Using TTS audio without alert tone prefix.", Logger.LogLevel.Debug);
                    if (outputPath != tempTtsPath)
                    {
                        if (File.Exists(outputPath)) File.Delete(outputPath);
                        File.Move(tempTtsPath, outputPath);
                    }
                    return File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"[EmergencyAlertGenerator] EdgeTtsClient error: {ex.Message}", Logger.LogLevel.Error);
                if (ex.InnerException != null)
                    Logger.Log($"[EmergencyAlertGenerator] Inner exception: {ex.InnerException.Message}", Logger.LogLevel.Debug);
                return false;
            }
        }

        private static bool TryGenerateWithEspeak(string text, string outputPath, string language, string? alertTonePath)
        {
            try
            {
                string voice = language == "fr-CA" ? "fr-ca" : "en-us";
                string tempTtsPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", $"temp_tts_{Guid.NewGuid()}.wav");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "espeak-ng",
                    Arguments = $"-v {voice} -w \"{tempTtsPath}\" \"{text.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process?.WaitForExit(30000);
                    if (process?.ExitCode == 0 && File.Exists(tempTtsPath))
                    {
                        // Build SAME-compliant structure with silences
                        if (alertTonePath != null && File.Exists(alertTonePath))
                        {
                            string? silence1 = GenerateSilence(Path.GetDirectoryName(outputPath) ?? ".");
                            string? silence2 = GenerateSilence(Path.GetDirectoryName(outputPath) ?? ".");
                            
                            var audioFiles = new System.Collections.Generic.List<string>();
                            if (silence1 != null) audioFiles.Add(silence1);
                            audioFiles.Add(alertTonePath);
                            if (silence2 != null) audioFiles.Add(silence2);
                            audioFiles.Add(tempTtsPath);
                            
                            if (AlertToneGenerator.ConcatenateAudioFiles(audioFiles.ToArray(), outputPath))
                            {
                                try { File.Delete(tempTtsPath); } catch { }
                                return File.Exists(outputPath);
                            }
                        }
                        
                        // Fallback: use TTS audio without alert tone
                        if (outputPath != tempTtsPath)
                        {
                            if (File.Exists(outputPath)) File.Delete(outputPath);
                            File.Move(tempTtsPath, outputPath);
                        }
                        return File.Exists(outputPath);
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGenerateWithEdgeTTS(string text, string outputPath, string language, string? alertTonePath)
        {
            try
            {
                // Use edge-tts via PowerShell/Python (if available)
                string voice = language == "fr-CA" ? "fr-CA-SylvieNeural" : "en-CA-LiamNeural";
                string tempTtsPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", $"temp_tts_{Guid.NewGuid()}.wav");
                string mp3Path = Path.ChangeExtension(tempTtsPath, ".mp3");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "edge-tts",
                    Arguments = $"--voice \"{voice}\" --text \"{text.Replace("\"", "\\\"")}\" --write-media \"{mp3Path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process?.WaitForExit(60000);
                    if (process?.ExitCode == 0 && File.Exists(mp3Path))
                    {
                        // Convert MP3 to WAV using ffmpeg if available
                        if (!TryConvertMp3ToWav(mp3Path, tempTtsPath))
                        {
                            // If conversion fails, use MP3 directly
                            tempTtsPath = mp3Path;
                        }
                        else
                        {
                            try { File.Delete(mp3Path); } catch { }
                        }
                        
                        // Build SAME-compliant structure with silences
                        if (alertTonePath != null && File.Exists(alertTonePath))
                        {
                            string? silence1 = GenerateSilence(Path.GetDirectoryName(outputPath) ?? ".");
                            string? silence2 = GenerateSilence(Path.GetDirectoryName(outputPath) ?? ".");
                            
                            var audioFiles = new System.Collections.Generic.List<string>();
                            if (silence1 != null) audioFiles.Add(silence1);
                            audioFiles.Add(alertTonePath);
                            if (silence2 != null) audioFiles.Add(silence2);
                            audioFiles.Add(tempTtsPath);
                            
                            if (AlertToneGenerator.ConcatenateAudioFiles(audioFiles.ToArray(), outputPath))
                            {
                                try { File.Delete(tempTtsPath); } catch { }
                                return File.Exists(outputPath);
                            }
                        }
                        
                        // Fallback: use TTS audio without alert tone
                        if (outputPath != tempTtsPath)
                        {
                            if (File.Exists(outputPath)) File.Delete(outputPath);
                            File.Move(tempTtsPath, outputPath);
                        }
                        return File.Exists(outputPath);
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGenerateWithWindowsMediaTTS(string text, string outputPath, string language, string? alertTonePath)
        {
            try
            {
                // Use Windows.Media.SpeechSynthesis via PowerShell - supports more voices
                string voiceLang = language == "fr-CA" ? "fr-CA" : (language == "fr-FR" ? "fr-FR" : "en-CA");
                string tempTtsPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", $"temp_tts_{Guid.NewGuid()}.wav");
                
                // PowerShell script using Windows Runtime speech synthesis
                string psScript = $@"
$null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer,Windows.Media.SpeechSynthesis,ContentType=WindowsRuntime]
$synth = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer
$voices = [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices | Where-Object {{ $_.Language -like '{voiceLang}*' }}
if ($voices) {{ $synth.Voice = $voices[0] }}
$stream = $synth.SynthesizeTextToStreamAsync('{text.Replace("'", "''")}').GetAwaiter().GetResult()
$reader = New-Object System.IO.BinaryReader($stream.AsStreamForRead())
$bytes = $reader.ReadBytes($stream.Size)
[System.IO.File]::WriteAllBytes('{tempTtsPath.Replace("'", "''")}', $bytes)
$reader.Dispose()
$stream.Dispose()
$synth.Dispose()
";
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript.Replace("\"", "`\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process?.WaitForExit(30000);
                    if (process?.ExitCode == 0 && File.Exists(tempTtsPath) && new FileInfo(tempTtsPath).Length > 1000)
                    {
                        // Build SAME-compliant structure with silences
                        if (alertTonePath != null && File.Exists(alertTonePath))
                        {
                            string? silence1 = GenerateSilence(Path.GetDirectoryName(outputPath) ?? ".");
                            string? silence2 = GenerateSilence(Path.GetDirectoryName(outputPath) ?? ".");
                            
                            var audioFiles = new System.Collections.Generic.List<string>();
                            if (silence1 != null) audioFiles.Add(silence1);
                            audioFiles.Add(alertTonePath);
                            if (silence2 != null) audioFiles.Add(silence2);
                            audioFiles.Add(tempTtsPath);
                            
                            if (AlertToneGenerator.ConcatenateAudioFiles(audioFiles.ToArray(), outputPath))
                            {
                                try { File.Delete(tempTtsPath); } catch { }
                                return File.Exists(outputPath);
                            }
                        }
                        
                        // Fallback: use TTS audio without alert tone
                        if (outputPath != tempTtsPath)
                        {
                            if (File.Exists(outputPath)) File.Delete(outputPath);
                            File.Move(tempTtsPath, outputPath);
                        }
                        return File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConvertMp3ToWav(string mp3Path, string wavPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -i \"{mp3Path}\" -acodec pcm_s16le -ar 44100 \"{wavPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process?.WaitForExit(30000);
                    return process?.ExitCode == 0 && File.Exists(wavPath);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGenerateWithSAPI(string text, string outputPath, string language, string? alertTonePath)
        {
            try
            {
                string tempTtsPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", $"temp_tts_{Guid.NewGuid()}.wav");
                
                // First, list all available voices for debugging
                string listVoicesScript = @"
Add-Type -AssemblyName System.Speech;
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer;
$voices = $synth.GetInstalledVoices();
Write-Host ""Available voices:"";
foreach ($v in $voices) {
    Write-Host ""  $($v.VoiceInfo.Name) [$($v.VoiceInfo.Culture.Name)] Gender: $($v.VoiceInfo.Gender) Enabled: $($v.Enabled)""
}
$synth.Dispose();
";
                
                var listInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{listVoicesScript.Replace("\"", "`\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using (var listProcess = Process.Start(listInfo))
                {
                    if (listProcess != null)
                    {
                        string output = listProcess.StandardOutput.ReadToEnd();
                        Console.WriteLine($"[SAPI] {output}");
                        listProcess.WaitForExit(5000);
                    }
                }
                
                // Use PowerShell SAPI (Windows Speech API) as fallback
                // Try to select the best French voice for fr-CA language
                string voiceSelection = language == "fr-CA" 
                    ? @"
# List all voices first
Write-Host ""Searching for French voices...""
$allVoices = $synth.GetInstalledVoices()
Write-Host ""Total voices found: $($allVoices.Count)""

# Try to find French Canadian voice first, then any French voice
$voices = $allVoices | Where-Object { 
    $_.Enabled -and (
        $_.VoiceInfo.Culture.Name -eq 'fr-CA' -or 
        $_.VoiceInfo.Culture.Name -eq 'fr-FR' -or
        $_.VoiceInfo.Name -like '*French*' -or
        $_.VoiceInfo.Name -like '*Hortense*' -or
        $_.VoiceInfo.Name -like '*Julie*' -or
        $_.VoiceInfo.Name -like '*Pauline*'
    )
}

Write-Host ""French voices found: $($voices.Count)""

if ($voices) { 
    # Sort to prefer fr-CA first
    $sorted = $voices | Sort-Object { 
        if ($_.VoiceInfo.Culture.Name -eq 'fr-CA') { 0 } 
        elseif ($_.VoiceInfo.Culture.Name -eq 'fr-FR') { 1 }
        else { 2 }
    }
    $selectedVoice = $sorted[0]
    Write-Host ""Selected voice: $($selectedVoice.VoiceInfo.Name) [$($selectedVoice.VoiceInfo.Culture.Name)]""
    $synth.SelectVoice($selectedVoice.VoiceInfo.Name)
} else {
    Write-Host ""WARNING: No French voice found! Using default voice.""
}
"
                    : "";
                
                string psScript = $@"
Add-Type -AssemblyName System.Speech;
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer;
{voiceSelection}
$synth.SetOutputToWaveFile('{tempTtsPath.Replace("'", "''")}');
$synth.Speak('{text.Replace("'", "''")}');
$synth.Dispose();
";
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript.Replace("\"", "`\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process?.WaitForExit(30000);
                    if (process?.ExitCode == 0 && File.Exists(tempTtsPath))
                    {
                        // Build SAME-compliant structure with silences
                        if (alertTonePath != null && File.Exists(alertTonePath))
                        {
                            string? silence1 = GenerateSilence(Path.GetDirectoryName(outputPath) ?? ".");
                            string? silence2 = GenerateSilence(Path.GetDirectoryName(outputPath) ?? ".");
                            
                            var audioFiles = new System.Collections.Generic.List<string>();
                            if (silence1 != null) audioFiles.Add(silence1);
                            audioFiles.Add(alertTonePath);
                            if (silence2 != null) audioFiles.Add(silence2);
                            audioFiles.Add(tempTtsPath);
                            
                            if (AlertToneGenerator.ConcatenateAudioFiles(audioFiles.ToArray(), outputPath))
                            {
                                try { File.Delete(tempTtsPath); } catch { }
                                return File.Exists(outputPath);
                            }
                        }
                        
                        // Fallback: use TTS audio without alert tone
                        if (outputPath != tempTtsPath)
                        {
                            if (File.Exists(outputPath)) File.Delete(outputPath);
                            File.Move(tempTtsPath, outputPath);
                        }
                        return File.Exists(outputPath);
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string CleanTextForTTS(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            
            // Remove URLs
            text = System.Text.RegularExpressions.Regex.Replace(text, @"http[s]?://\S+", "");
            
            // Limit length
            if (text.Length > 500)
                text = text.Substring(0, 497) + "...";
            
            return text.Trim();
        }

        private static Color GetSeverityBackgroundColor(string severity)
        {
            return severity?.ToLowerInvariant() switch
            {
                "red" => Color.FromArgb(200, 0, 0),      // Extreme/Severe - Dark Red
                "yellow" => Color.FromArgb(200, 150, 0), // Moderate/Minor - Dark Orange
                _ => Color.FromArgb(80, 80, 80)          // Unknown - Dark Gray
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

        /// <summary>
        /// Generates a 1-second silence audio file (per SAME standard requirement).
        /// SAME protocol requires 1 second of silence between each section.
        /// </summary>
        private static string? GenerateSilence(string outputDir, double durationSeconds = 1.0)
        {
            try
            {
                string silencePath = Path.Combine(outputDir, $"silence_{durationSeconds}s.wav");
                
                // Check if already exists
                if (File.Exists(silencePath))
                {
                    return silencePath;
                }

                // Generate silence using ffmpeg
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -f lavfi -i anullsrc=r=44100:cl=mono -t {durationSeconds:F1} -acodec pcm_s16le \"{silencePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process?.WaitForExit(5000);
                    if (process?.ExitCode == 0 && File.Exists(silencePath))
                    {
                        return silencePath;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmergencyAlertGenerator] Error generating silence: {ex.Message}");
            }
            return null;
        }
    }
}
