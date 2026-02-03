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
                    string imageFile = GenerateAlertImage(alert, outputDir, i + 1, width, height, margin, imgConfig, language);
                    generatedFiles.Add(imageFile);

                    string audioFile = GenerateAlertAudio(alert, outputDir, i + 1, language);
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
                    // Default to 30 seconds if we can't determine audio duration
                    audioDuration = 30.0;
                    Logger.Log($"[EmergencyAlertGenerator] Could not determine audio duration, using default: {audioDuration}s", Logger.LogLevel.Warning);
                }
                else
                {
                    Logger.Log($"[EmergencyAlertGenerator] Audio duration: {audioDuration:F2}s", Logger.LogLevel.Debug);
                }

                // Build FFmpeg command to create video from single image + audio
                // -loop 1: loop the image indefinitely
                // -t: set duration to match audio
                // -shortest: finish when the shortest stream ends
                var codecArgs = BuildVideoCodecArgs(videoConfig);
                
                string ffmpegArgs = $"-y -loop 1 -i \"{alertImagePath}\" -i \"{alertAudioPath}\" " +
                                   $"-c:v {videoConfig.VideoCodec ?? "libx264"} {codecArgs} " +
                                   $"-c:a aac -b:a 192k " +
                                   $"-t {audioDuration:F2} " +
                                   $"-pix_fmt yuv420p " +
                                   $"-shortest \"{outputPath}\"";

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
                    Logger.Log($"[EmergencyAlertGenerator] ✓ Alert video generated successfully: {outputPath}", Logger.LogLevel.Info);
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
            int width, int height, float margin, ImageGenerationSettings imgConfig, string language)
        {
            string filename = $"EmergencyAlert_{index:D2}.png";
            string fullPath = Path.Combine(outputDir, filename);

            using (Bitmap bmp = new Bitmap(width, height))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                // Background color based on severity
                Color bgColor = GetSeverityBackgroundColor(alert.SeverityColor);
                using (var bgBrush = new SolidBrush(bgColor))
                {
                    g.FillRectangle(bgBrush, 0, 0, width, height);
                }

                // Draw border
                using (var borderPen = new Pen(Color.White, 8))
                {
                    g.DrawRectangle(borderPen, 20, 20, width - 40, height - 40);
                }

                // Alert Ready logo area (top)
                float currentY = 60;
                using (Font logoFont = new Font(imgConfig.FontFamily ?? "Arial", 48, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    string logoText = language == "en-CA" ? "ALERT READY" : "QUÉBEC EN ALERTE";
                    SizeF logoSize = g.MeasureString(logoText, logoFont);
                    float logoX = (width - logoSize.Width) / 2;
                    g.DrawString(logoText, logoFont, whiteBrush, logoX, currentY);
                }

                currentY += 100;

                // Alert icon/symbol
                DrawAlertSymbol(g, width / 2, currentY, 80);
                currentY += 140;

                // Title/Headline
                using (Font titleFont = new Font(imgConfig.FontFamily ?? "Arial", 42, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    string wrappedTitle = WrapText(alert.Title ?? "ALERTE D'URGENCE", titleFont, g, width - (margin * 4));
                    SizeF titleSize = g.MeasureString(wrappedTitle, titleFont);
                    float titleX = (width - titleSize.Width) / 2;
                    g.DrawString(wrappedTitle, titleFont, whiteBrush, new RectangleF(margin * 2, currentY, width - (margin * 4), titleSize.Height),
                        new StringFormat { Alignment = StringAlignment.Center });
                    currentY += titleSize.Height + 40;
                }

                // Location/Area
                if (!string.IsNullOrWhiteSpace(alert.City))
                {
                    using (Font areaFont = new Font(imgConfig.FontFamily ?? "Arial", 32, FontStyle.Bold))
                    using (Brush whiteBrush = new SolidBrush(Color.White))
                    {
                        string wrappedArea = WrapText(alert.City, areaFont, g, width - (margin * 4));
                        SizeF areaSize = g.MeasureString(wrappedArea, areaFont);
                        g.DrawString(wrappedArea, areaFont, whiteBrush, new RectangleF(margin * 2, currentY, width - (margin * 4), areaSize.Height),
                            new StringFormat { Alignment = StringAlignment.Center });
                        currentY += areaSize.Height + 30;
                    }
                }

                // Summary/Description
                if (!string.IsNullOrWhiteSpace(alert.Summary))
                {
                    using (Font summaryFont = new Font(imgConfig.FontFamily ?? "Arial", 24, FontStyle.Regular))
                    using (Brush whiteBrush = new SolidBrush(Color.White))
                    {
                        string wrappedSummary = WrapText(alert.Summary, summaryFont, g, width - (margin * 4));
                        SizeF summarySize = g.MeasureString(wrappedSummary, summaryFont);
                        float maxSummaryHeight = height - currentY - 150;
                        
                        if (summarySize.Height > maxSummaryHeight)
                        {
                            // Truncate if too long
                            wrappedSummary = TruncateText(wrappedSummary, summaryFont, g, width - (margin * 4), maxSummaryHeight);
                        }

                        g.DrawString(wrappedSummary, summaryFont, whiteBrush, new RectangleF(margin * 2, currentY, width - (margin * 4), maxSummaryHeight),
                            new StringFormat { Alignment = StringAlignment.Center });
                    }
                }

                // Footer instructions
                currentY = height - 100;
                using (Font footerFont = new Font(imgConfig.FontFamily ?? "Arial", 20, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    string footerText = language == "en-CA" 
                        ? "Follow instructions from local authorities • Stay informed"
                        : "Suivez les instructions des autorités locales • Restez informés";
                    SizeF footerSize = g.MeasureString(footerText, footerFont);
                    float footerX = (width - footerSize.Width) / 2;
                    g.DrawString(footerText, footerFont, whiteBrush, footerX, currentY);
                }

                bmp.Save(fullPath, ImageFormat.Png);
            }

            Logger.Log($"[EmergencyAlertGenerator] Generated image: {filename}", Logger.LogLevel.Info);
            return fullPath;
        }

        private static void DrawAlertSymbol(Graphics g, float centerX, float centerY, float size)
        {
            // Draw warning triangle symbol
            PointF[] triangle = new PointF[]
            {
                new PointF(centerX, centerY - size),              // Top
                new PointF(centerX - size, centerY + size * 0.6f), // Bottom left
                new PointF(centerX + size, centerY + size * 0.6f)  // Bottom right
            };

            using (var triangleBrush = new SolidBrush(Color.White))
            using (var borderPen = new Pen(Color.Black, 4))
            {
                g.FillPolygon(triangleBrush, triangle);
                g.DrawPolygon(borderPen, triangle);
            }

            // Draw exclamation mark
            using (var exclamationBrush = new SolidBrush(Color.Black))
            using (Font exclamationFont = new Font("Arial", size * 1.2f, FontStyle.Bold))
            {
                string exclamation = "!";
                SizeF textSize = g.MeasureString(exclamation, exclamationFont);
                g.DrawString(exclamation, exclamationFont, exclamationBrush, 
                    centerX - textSize.Width / 2, centerY - size * 0.3f);
            }
        }

        private static string GenerateAlertAudio(AlertEntry alert, string outputDir, int index, string language)
        {
            try
            {
                Logger.Log($"[EmergencyAlertGenerator] Starting audio generation for alert {index}...", Logger.LogLevel.Info);
                
                string filename = $"EmergencyAlert_{index:D2}.wav";
                string fullPath = Path.Combine(outputDir, filename);
                
                // First, ensure the Alert Ready attention signal is generated
                Logger.Log("[EmergencyAlertGenerator] Generating Alert Ready attention signal...", Logger.LogLevel.Debug);
                string? alertTonePath = AlertToneGenerator.GenerateToDirectory(outputDir);
                if (alertTonePath != null)
                {
                    Logger.Log($"[EmergencyAlertGenerator] Alert Ready tone available: {Path.GetFileName(alertTonePath)}", Logger.LogLevel.Debug);
                }
                else
                {
                    Logger.Log("[EmergencyAlertGenerator] Warning: Could not generate Alert Ready tone", Logger.LogLevel.Warning);
                }

                // Build alert text for TTS
                Logger.Log("[EmergencyAlertGenerator] Building TTS text...", Logger.LogLevel.Debug);
                StringBuilder audioText = new StringBuilder();
                
                if (language == "fr-CA")
                {
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
                    if (TryGenerateWithPiperTts(text, fullPath, language))
                    {
                        Logger.Log($"[EmergencyAlertGenerator] Generated audio with Piper TTS: {filename}", Logger.LogLevel.Info);
                        return fullPath;
                    }
                    Logger.Log("[EmergencyAlertGenerator] Piper TTS failed, trying Edge TTS...", Logger.LogLevel.Debug);
                }

                // Try EdgeTtsClient (high quality neural voices, requires internet)
                Logger.Log("[EmergencyAlertGenerator] Trying Edge TTS Client...", Logger.LogLevel.Info);
                if (TryGenerateWithEdgeTtsClient(text, fullPath, language))
                {
                    Logger.Log($"[EmergencyAlertGenerator] Generated audio with Edge TTS: {filename}", Logger.LogLevel.Info);
                    return fullPath;
                }
                Logger.Log("[EmergencyAlertGenerator] Edge TTS Client failed, trying CLI...", Logger.LogLevel.Debug);

                // Try edge-tts CLI as backup (if Python installed)
                Logger.Log("[EmergencyAlertGenerator] Trying Edge TTS CLI...", Logger.LogLevel.Info);
                if (TryGenerateWithEdgeTTS(text, fullPath, language))
                {
                    Logger.Log($"[EmergencyAlertGenerator] Generated audio with Edge TTS CLI: {filename}", Logger.LogLevel.Info);
                    return fullPath;
                }

                // Try Windows.Media.SpeechSynthesis (more voices than SAPI)
                if (TryGenerateWithWindowsMediaTTS(text, fullPath, language))
                {
                    Logger.Log($"[EmergencyAlertGenerator] Generated audio with Windows Media TTS: {filename}", Logger.LogLevel.Info);
                    return fullPath;
                }

                // Try espeak-ng (if available)
                if (TryGenerateWithEspeak(text, fullPath, language))
                {
                    Logger.Log($"[EmergencyAlertGenerator] Generated audio with espeak: {filename}", Logger.LogLevel.Info);
                    return fullPath;
                }

                // Try PowerShell SAPI as fallback
                if (TryGenerateWithSAPI(text, fullPath, language))
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
        private static bool TryGenerateWithPiperTts(string text, string outputPath, string language)
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

                    // Try to prepend the Alert Ready attention signal
                    Logger.Log("[PiperTTS] Attempting to prepend Alert Ready tone...", Logger.LogLevel.Debug);
                    string? alertTonePath = null;
                    try
                    {
                        alertTonePath = AlertToneGenerator.GetOrGenerateAlertTone();
                    }
                    catch (Exception toneEx)
                    {
                        Logger.Log($"[PiperTTS] Alert tone generation error: {toneEx.Message}", Logger.LogLevel.Warning);
                    }

                    if (alertTonePath != null && File.Exists(alertTonePath))
                    {
                        Logger.Log($"[PiperTTS] Concatenating alert tone with TTS audio...", Logger.LogLevel.Debug);
                        // Concatenate: AlertTone + TTS audio
                        try
                        {
                            if (AlertToneGenerator.ConcatenateAudioFiles(new[] { alertTonePath, ttsAudioPath }, outputPath))
                            {
                                Logger.Log("[EmergencyAlertGenerator] Successfully prepended Alert Ready tone to Piper TTS audio.", Logger.LogLevel.Debug);
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
                    Logger.Log("[EmergencyAlertGenerator] Using Piper TTS audio without Alert Ready tone prefix.", Logger.LogLevel.Debug);
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

        private static bool TryGenerateWithEdgeTtsClient(string text, string outputPath, string language)
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
                    // Try to prepend the Alert Ready attention signal
                    string? alertTonePath = AlertToneGenerator.GetOrGenerateAlertTone();
                    
                    if (alertTonePath != null && File.Exists(alertTonePath))
                    {
                        // Concatenate: AlertTone + TTS audio
                        if (AlertToneGenerator.ConcatenateAudioFiles(new[] { alertTonePath, tempTtsPath }, outputPath))
                        {
                            Logger.Log("[EmergencyAlertGenerator] Successfully prepended Alert Ready tone to TTS audio.", Logger.LogLevel.Debug);
                            try { File.Delete(tempTtsPath); } catch { }
                            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000;
                        }
                    }
                    
                    // Fallback: use TTS audio without alert tone
                    Logger.Log("[EmergencyAlertGenerator] Using TTS audio without Alert Ready tone prefix.", Logger.LogLevel.Debug);
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

        private static bool TryGenerateWithEspeak(string text, string outputPath, string language)
        {
            try
            {
                string voice = language == "fr-CA" ? "fr-ca" : "en-us";
                var startInfo = new ProcessStartInfo
                {
                    FileName = "espeak-ng",
                    Arguments = $"-v {voice} -w \"{outputPath}\" \"{text.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process?.WaitForExit(30000);
                    return process?.ExitCode == 0 && File.Exists(outputPath);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGenerateWithEdgeTTS(string text, string outputPath, string language)
        {
            try
            {
                // Use edge-tts via PowerShell/Python (if available)
                string voice = language == "fr-CA" ? "fr-CA-SylvieNeural" : "en-CA-LiamNeural";
                string mp3Path = Path.ChangeExtension(outputPath, ".mp3");
                
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
                        // Convert MP3 to WAV using ffmpeg if available, otherwise keep MP3
                        if (TryConvertMp3ToWav(mp3Path, outputPath))
                        {
                            try { File.Delete(mp3Path); } catch { }
                            return true;
                        }
                        // If conversion fails, rename mp3 to target (caller expects wav but mp3 works too)
                        try 
                        { 
                            if (File.Exists(outputPath)) File.Delete(outputPath);
                            File.Move(mp3Path, outputPath); 
                            return true;
                        } 
                        catch { return File.Exists(mp3Path); }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGenerateWithWindowsMediaTTS(string text, string outputPath, string language)
        {
            try
            {
                // Use Windows.Media.SpeechSynthesis via PowerShell - supports more voices
                string voiceLang = language == "fr-CA" ? "fr-CA" : (language == "fr-FR" ? "fr-FR" : "en-CA");
                
                // PowerShell script using Windows Runtime speech synthesis
                string psScript = $@"
$null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer,Windows.Media.SpeechSynthesis,ContentType=WindowsRuntime]
$synth = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer
$voices = [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices | Where-Object {{ $_.Language -like '{voiceLang}*' }}
if ($voices) {{ $synth.Voice = $voices[0] }}
$stream = $synth.SynthesizeTextToStreamAsync('{text.Replace("'", "''")}').GetAwaiter().GetResult()
$reader = New-Object System.IO.BinaryReader($stream.AsStreamForRead())
$bytes = $reader.ReadBytes($stream.Size)
[System.IO.File]::WriteAllBytes('{outputPath.Replace("'", "''")}', $bytes)
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
                    return process?.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000;
                }
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

        private static bool TryGenerateWithSAPI(string text, string outputPath, string language)
        {
            try
            {
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
$synth.SetOutputToWaveFile('{outputPath.Replace("'", "''")}');
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
                    return process?.ExitCode == 0 && File.Exists(outputPath);
                }
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
    }
}
