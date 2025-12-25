using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WeatherImageGenerator
{
    /// <summary>
    /// Generates MP4 videos from a sequence of images with fade transitions using FFmpeg.
    /// Converted from make_video.ps1 PowerShell script.
    /// </summary>
    public class VideoGenerator
    {
        private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

        // Event to report ffmpeg progress (0-100) and a short status/detail string
        public static event Action<double, string>? VideoProgressUpdated;

        // Settings
        public string WorkingDirectory { get; set; }
        public string ImageFolder { get; set; }
        public string OutputFile { get; set; }
        public string MusicFile { get; set; }

        // Video Timing Settings
        public double StaticDuration { get; set; } = 8;          // Time image is STATIC
        public double FadeDuration { get; set; } = 0.5;          // Time spent TRANSITIONING
        public int FrameRate { get; set; } = 30;                 // Output framerate
        public ResolutionMode ResolutionMode { get; set; } = ResolutionMode.Mode1080p;
        public bool EnableFadeTransitions { get; set; } = false;
        public bool UseOverlayMode { get; set; } = false;       // When true, overlay frames on static background
        public string StaticMapPath { get; set; } = "";         // Path to static background map
        // When set, the radar overlay will only be enabled for the base image whose filename contains this string (case-insensitive)
        public string OverlayTargetFilename { get; set; } = "";
        public string VideoCodec { get; set; } = "libx264";     // FFmpeg video codec
        public string VideoBitrate { get; set; } = "4M";        // Target bitrate (e.g., 4M)
        public string Container { get; set; } = "mp4";          // Output container/extension
        public bool ShowFfmpegOutputInGui { get; set; } = true;  // Controls whether to emit ffmpeg logs to Logger
        
        // When true, attempt hardware accelerated encoding (NVENC) if available
        public bool EnableHardwareEncoding { get; set; } = false;

        // Audio handling
        public bool TrimToAudio { get; set; } = false;          // When true, end output when audio ends (-shortest)
        public string AudioCodec { get; set; } = "aac";         // FFmpeg audio codec
        public string AudioBitrate { get; set; } = "192k";      // Audio bitrate for output

        private int _width;
        private int _height;
        private readonly string[] _extensions = { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.gif" };

        // When in overlay mode, radar frames are stored here
        private List<FileInfo> _radarFrames = new List<FileInfo>();
        // Temporary directory for composed stills (static map underlay)
        private string? _compositeTempDir = null;

        // FFmpeg output simplification
        private string _lastFfmpegProgress = "";
        private DateTime _lastFfmpegProgressLog = DateTime.MinValue;

        // Estimated totals for progress calculation
        private double _expectedTotalSeconds = 0;
        private double _expectedTotalFrames = 1;

        private readonly object _progressLock = new object();

        /// <summary>
        /// When true, show raw ffmpeg output. Default is false (simplified output).
        /// </summary>
        public bool FfmpegVerbose { get; set; } = false;

        public VideoGenerator(string workingDirectory)
        {
            WorkingDirectory = workingDirectory;
            ImageFolder = workingDirectory;
            OutputFile = Path.Combine(workingDirectory, "slideshow_v3.mp4");
            MusicFile = Path.Combine(workingDirectory, "music.mp3");
        }

        /// <summary>
        /// Generates the video from images in the working directory.
        /// </summary>
        public bool GenerateVideo()
        {
            Logger.Log(new string('-', 48));
            Logger.Log($"WORKING DIRECTORY: {WorkingDirectory}");
            Logger.Log(new string('-', 48));

            // Load images
            var images = LoadImages();
            Logger.Log($"[INFO] Images to include ({images.Count}):");
            for (int i = 0; i < images.Count; i++)
            {
                Logger.Log($"  [{i}] {images[i].FullName}");
            }

            if (images.Count < 2)
            {
                Logger.Log($"[ERROR] Found {images.Count} images.", System.ConsoleColor.Red);
                return false;
            }

            Logger.Log($"[INFO] Found {images.Count} images. Processing...", System.ConsoleColor.Green);

            // Calculate resolution
            CalculateResolution();

            // If overlay mode requested, load radar frames and pre-compose stills on top of the static map
            List<FileInfo> baseImages = images;
            List<FileInfo> radarFrames = new List<FileInfo>();
            int overlayBaseIndex = -1;

            if (UseOverlayMode)
            {
                radarFrames = LoadRadarFrames();

                if (!string.IsNullOrEmpty(StaticMapPath) && File.Exists(StaticMapPath) && radarFrames.Count > 0)
                {
                    // Pre-compose each still with the static map beneath it
                    _compositeTempDir = Path.Combine(WorkingDirectory, "_composited_stills");
                    if (!Directory.Exists(_compositeTempDir)) Directory.CreateDirectory(_compositeTempDir);

                    var composed = new List<FileInfo>();
                    try
                    {
                        foreach (var st in images)
                        {
                            var outPath = Path.Combine(_compositeTempDir, Path.GetFileNameWithoutExtension(st.Name) + ".png");
                            ComposeStillWithStaticMap(st.FullName, StaticMapPath, outPath);
                            composed.Add(new FileInfo(outPath));
                        }

                        baseImages = composed.OrderBy(f => f.Name).ToList();

                        // If a target filename substring is provided, find its index in the base image list
                        if (!string.IsNullOrEmpty(OverlayTargetFilename))
                        {
                            var match = baseImages.Select((f, idx) => new { f, idx })
                                                   .FirstOrDefault(x => x.f.Name.IndexOf(OverlayTargetFilename, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (match != null)
                            {
                                overlayBaseIndex = match.idx;
                                Logger.Log($"[OVERLAY] Radar overlay will be enabled for base image index {overlayBaseIndex} ({baseImages[overlayBaseIndex].Name})", ConsoleColor.Cyan);
                            }
                            else
                            {
                                Logger.Log($"[OVERLAY] Target filename '{OverlayTargetFilename}' not found among base images; radar overlay will be disabled.", ConsoleColor.Yellow);
                                UseOverlayMode = false; // disable overlay to avoid overlaying everywhere
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[OVERLAY] Failed to compose images: {ex.Message}", ConsoleColor.Yellow);
                        // Fall back to original stills if composition fails
                        baseImages = images;
                    }
                }
                else
                {
                    Logger.Log("[OVERLAY] Static map not found or no radar frames; overlay mode will be ignored.", ConsoleColor.Yellow);
                    UseOverlayMode = false;
                }
            }

            // Build filter complex
            var filterComplex = BuildFilterComplex(baseImages, radarFrames, overlayBaseIndex);

            // Estimate expected total duration and frames for progress calculation
            var imageCount = UseOverlayMode ? (/* base images */ (int?)null) : (int?)null;
            // We already have _expectedTotalSeconds and frames calculated from inputs; compute from baseImages if overlay
            // (calculate based on number of base images so the progress is accurate when overlay mode is used)
            int baseCount = (UseOverlayMode && filterComplex != null) ? 0 : 0; // placeholder to keep flow; will override below

            if (EnableFadeTransitions)
            {
                // If fades overlap, total duration = static durations + fades between images
                _expectedTotalSeconds = StaticDuration * (UseOverlayMode ? 0 : 0); // replaced below
            }
            // We'll replace the above shortly by setting correct expected times based on the number of base images

            // Set the expected totals properly now (baseImages used when overlay mode is on)
            var effectiveCount = UseOverlayMode ? (baseImages?.Count ?? 0) : images.Count;
            if (EnableFadeTransitions)
            {
                _expectedTotalSeconds = StaticDuration * effectiveCount + FadeDuration * Math.Max(0, effectiveCount - 1);
            }
            else
            {
                _expectedTotalSeconds = (StaticDuration + FadeDuration) * effectiveCount;
            }
            _expectedTotalFrames = Math.Max(1.0, _expectedTotalSeconds * FrameRate);

            if (!string.IsNullOrWhiteSpace(Container))
            {
                var ext = Container.Trim().TrimStart('.') ;
                OutputFile = Path.ChangeExtension(OutputFile, ext);
            }

            // Build and execute FFmpeg command
            var ffmpegCmd = BuildFFmpegCommand(baseImages, radarFrames, filterComplex);
            if (FfmpegVerbose) Logger.Log($"[CMD] {ffmpegCmd}");
            try
            {
                return ExecuteFFmpeg(ffmpegCmd);
            }
            finally
            {
                // Cleanup any temporary composed images
                if (!string.IsNullOrEmpty(_compositeTempDir) && Directory.Exists(_compositeTempDir))
                {
                    try { Directory.Delete(_compositeTempDir, true); } catch { }
                    _compositeTempDir = null;
                }
            }
        }

        private List<FileInfo> LoadImages()
        {
            var images = new List<FileInfo>();
            var dir = new DirectoryInfo(ImageFolder);

            // Always load still images from the main folder (do not switch to province_frames)
            foreach (var extension in _extensions)
            {
                var files = dir.GetFiles(extension);
                foreach (var file in files)
                {
                    if (IsValidImage(file))
                    {
                        images.Add(file);
                    }
                    else
                    {
                        Logger.Log($"[WARN] Skipping invalid or corrupt image: {file.Name}", ConsoleColor.Yellow);
                    }
                }
            }

            return images.OrderBy(f => f.Name).ToList();
        }

        /// <summary>
        /// Loads radar frames from the province_frames subdirectory (if present).
        /// </summary>
        private List<FileInfo> LoadRadarFrames()
        {
            var frames = new List<FileInfo>();
            var provinceFramesDir = Path.Combine(ImageFolder, "province_frames");
            if (Directory.Exists(provinceFramesDir))
            {
                Logger.Log($"[OVERLAY MODE] Loading radar frames from: {provinceFramesDir}", System.ConsoleColor.Cyan);
                var d = new DirectoryInfo(provinceFramesDir);
                foreach (var extension in _extensions)
                {
                    var files = d.GetFiles(extension);
                    foreach (var file in files)
                    {
                        if (IsValidImage(file))
                        {
                            frames.Add(file);
                        }
                        else
                        {
                            Logger.Log($"[WARN] Skipping invalid or corrupt radar frame: {file.Name}", ConsoleColor.Yellow);
                        }
                    }
                }
            }
            else
            {
                Logger.Log("[OVERLAY MODE] province_frames directory not found, no radar overlay will be applied.", System.ConsoleColor.Yellow);
            }

            _radarFrames = frames.OrderBy(f => f.Name).ToList();
            return _radarFrames;
        }

        /// <summary>
        /// Checks if an image file is valid by attempting to load it.
        /// </summary>
        private bool IsValidImage(FileInfo file)
        {
            try
            {
                using (var img = Image.FromFile(file.FullName))
                {
                    return img.Width > 0 && img.Height > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private void CalculateResolution()
        {
            switch (ResolutionMode)
            {
                case ResolutionMode.Mode4K:
                    _width = 3840;
                    _height = 2160;
                    break;
                case ResolutionMode.ModeVertical:
                    _width = 1080;
                    _height = 1920;
                    break;
                case ResolutionMode.Mode1080p:
                default:
                    _width = 1920;
                    _height = 1080;
                    break;
            }
        }

        private string BuildFilterComplex(List<FileInfo> baseImages, List<FileInfo> radarFrames, int overlayBaseIndex = -1)
        {
            var clipDuration = StaticDuration + FadeDuration;
            var clipDurStr = clipDuration.ToString(_culture);

            // FIX 1: FORCE PIXEL FORMAT EARLY
            // We added "format=yuv420p" to ensure every image has the same color space immediately
            var preScale = $"scale={_width}:{_height}:force_original_aspect_ratio=decrease," +
                          $"pad={_width}:{_height}:(ow-iw)/2:(oh-ih)/2,setsar=1,format=yuv420p";

            // Build filter parts
            var filterParts = new List<string>();
            var index = 0;

            // We'll add inputs in BuildFFmpegCommand in the following order when overlay mode is active:
            // [baseImages..., radarFrames...]

            // Build input stream filters for base images
            foreach (var img in baseImages)
            {
                filterParts.Add($"[{index}:v]{preScale},fps={FrameRate},setpts=PTS-STARTPTS[v{index}];");
                index++;
            }

            var baseCount = baseImages.Count;

            // Build input stream filters for radar frames
            for (int r = 0; r < radarFrames.Count; r++)
            {
                filterParts.Add($"[{index}:v]{preScale},fps={FrameRate},setpts=PTS-STARTPTS[r{r+1}];");
                index++;
            }

            // Build base sequence (concatenate or xfades between base images)
            var lastLabel = baseCount > 0 ? "[v0]" : "[v0]";
            var currentOffset = 0.0;
            var fadeStr = FadeDuration.ToString(_culture);

            if (baseCount > 0)
            {
                for (int i = 0; i < baseCount - 1; i++)
                {
                    var nextInput = $"[v{i + 1}]";
                    var outputLabel = $"[b{i}]";

                    currentOffset += StaticDuration;
                    var offStr = currentOffset.ToString(_culture);

                    if (EnableFadeTransitions)
                    {
                        var xfadeString = $"{lastLabel}{nextInput}xfade=transition=fade:" +
                                        $"duration={fadeStr}:offset={offStr}{outputLabel};";
                        filterParts.Add(xfadeString);
                    }
                    else
                    {
                        var concatString = $"{lastLabel}{nextInput}concat=n=2:v=1:a=0{outputLabel};";
                        filterParts.Add(concatString);
                    }

                    lastLabel = outputLabel;
                }
            }

            var baseFinal = baseCount == 1 ? "[v0]" : lastLabel;

            // Build radar animation sequence from radar frames (if any)
            string? radarFinal = null;
            if (radarFrames.Count > 0)
            {
                var radarLast = radarFrames.Count == 1 ? "[r1]" : "[r1]";
                var radarLabel = radarFrames.Count == 1 ? "[r1]" : null;

                // If more than one radar frame, xfade/concat them
                if (radarFrames.Count > 1)
                {
                    var rLastLabel = "[r1]";
                    for (int i = 0; i < radarFrames.Count - 1; i++)
                    {
                        var cur = $"[r{i + 1}]";
                        var nxt = $"[r{i + 2}]";
                        var outLbl = $"[ra{i}]";

                        currentOffset += StaticDuration; // reuse offset to keep consistent timing for transitions
                        var offStr = currentOffset.ToString(_culture);

                        if (EnableFadeTransitions)
                        {
                            filterParts.Add($"{rLastLabel}{nxt}xfade=transition=fade:duration={fadeStr}:offset={offStr}{outLbl};");
                        }
                        else
                        {
                            filterParts.Add($"{rLastLabel}{nxt}concat=n=2:v=1:a=0{outLbl};");
                        }

                        rLastLabel = outLbl;
                    }

                    radarFinal = rLastLabel;
                }
                else
                {
                    radarFinal = "[r1]";
                }
            }

            // If overlay mode (radar frames present) overlay radarFinal onto baseFinal
            if (radarFinal != null && baseCount > 0)
            {
                if (overlayBaseIndex >= 0)
                {
                    // Compute the start and end time (seconds) to enable the overlay only during that base image clip
                    double startSec = overlayBaseIndex * StaticDuration;
                    double endSec = startSec + clipDuration; // show overlay for the duration of the clip
                    var startStr = startSec.ToString(_culture);
                    var endStr = endSec.ToString(_culture);
                    Logger.Log($"[OVERLAY] Enabling radar overlay between {startStr}s and {endStr}s (for base index {overlayBaseIndex})", ConsoleColor.Cyan);
                    filterParts.Add($"{baseFinal}{radarFinal}overlay=0:0:enable='between(t,{startStr},{endStr})'[overlaid];");
                    filterParts.Add($"[overlaid]format=yuv420p[outv]");
                }
                else
                {
                    filterParts.Add($"{baseFinal}{radarFinal}overlay=0:0[overlaid];");
                    filterParts.Add($"[overlaid]format=yuv420p[outv]");
                }
            }
            else if (baseCount > 0)
            {
                // No radar frames; just output baseFinal
                filterParts.Add($"{baseFinal}format=yuv420p[outv]");
            }
            else
            {
                // Fallback - empty
                filterParts.Add("nullsrc=size=1920x1080,format=yuv420p[outv]");
            }

            return string.Concat(filterParts);
        }

        private string BuildFFmpegCommand(List<FileInfo> baseImages, List<FileInfo> radarFrames, string filterComplex)
        {
            var sb = new StringBuilder();
            sb.Append("ffmpeg -y");

            // Add input files in this order: baseImages..., radarFrames..., [audio]
            var clipDuration = StaticDuration + FadeDuration;
            var clipDurStr = clipDuration.ToString(_culture);

            foreach (var img in baseImages)
            {
                sb.Append($" -framerate {FrameRate} -loop 1 -t {clipDurStr} -i \"{img.FullName}\"");
            }

            foreach (var rf in radarFrames)
            {
                sb.Append($" -framerate {FrameRate} -loop 1 -t {clipDurStr} -i \"{rf.FullName}\"");
            }

            // Add audio input if available (must be added before filter_complex)
            var hasAudio = File.Exists(MusicFile);
            if (hasAudio)
            {
                Logger.Log($"[AUDIO] Adding Audio: {MusicFile}", System.ConsoleColor.Magenta);
                sb.Append($" -i \"{MusicFile}\"");
            }

            // Add filter complex and map the filtered video output
            sb.Append($" -filter_complex \"{filterComplex}\" -map \"[outv]\"");

            // Map audio if present (after filter_complex and video mapping) and set codec/bitrate
            if (hasAudio)
            {
                // Audio index needs to account for the total number of video inputs
                var audioIndex = baseImages.Count + radarFrames.Count;
                sb.Append($" -map \"{audioIndex}:a\"");
                if (!string.IsNullOrWhiteSpace(AudioCodec))
                {
                    sb.Append($" -c:a {AudioCodec}");
                }
                if (!string.IsNullOrWhiteSpace(AudioBitrate))
                {
                    sb.Append($" -b:a {AudioBitrate}");
                }
                if (TrimToAudio)
                {
                    sb.Append(" -shortest");
                }
            }

            // Video encoding settings
            var codec = string.IsNullOrWhiteSpace(VideoCodec) ? "libx264" : VideoCodec;

            // If hardware encoding is requested, pick a hardware codec that matches the desired family and add friendly flags.
            if (EnableHardwareEncoding)
            {
                var hwType = GetHardwareEncoderType(out _);
                var lower = codec.ToLowerInvariant();
                bool isHevc = lower.Contains("hevc") || lower.Contains("x265") || lower.Contains("libx265");

                if (hwType == HardwareEncoderType.Nvenc)
                {
                    codec = isHevc ? "hevc_nvenc" : "h264_nvenc";
                    sb.Append($" -c:v {codec} -preset p1 -rc vbr_hq");
                }
                else if (hwType == HardwareEncoderType.Amf)
                {
                    codec = isHevc ? "hevc_amf" : "h264_amf";
                    // AMF specific flags can be added here if needed, e.g. -usage transcoding
                    sb.Append($" -c:v {codec}");
                }
                else if (hwType == HardwareEncoderType.Qsv)
                {
                    codec = isHevc ? "hevc_qsv" : "h264_qsv";
                    sb.Append($" -c:v {codec} -preset medium");
                }
                else
                {
                    // Fallback to software if no hardware encoder found but requested
                    Logger.Log("[WARN] Hardware encoding requested but no supported hardware encoder found. Falling back to software encoding.", ConsoleColor.Yellow);
                    sb.Append($" -c:v {codec}");
                }

                // Hardware encoders typically use bitrate targets rather than CRF — ensure we have a sensible default bitrate if none specified.
                if (hwType != HardwareEncoderType.None)
                {
                    if (!string.IsNullOrWhiteSpace(VideoBitrate))
                    {
                        sb.Append($" -b:v {VideoBitrate}");
                    }
                    else
                    {
                        sb.Append(" -b:v 8M");
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(VideoBitrate))
                    {
                        sb.Append($" -b:v {VideoBitrate}");
                    }
                    else
                    {
                        sb.Append(" -crf 23");
                    }
                }
            }
            else
            {
                sb.Append($" -c:v {codec}");

                if (!string.IsNullOrWhiteSpace(VideoBitrate))
                {
                    sb.Append($" -b:v {VideoBitrate}");
                }
                else
                {
                    sb.Append(" -crf 23");
                }
            }

            if (string.Equals(Container, "mp4", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(" -movflags +faststart");
            }
            sb.Append($" \"{OutputFile}\"");

            return sb.ToString();
        }

        private bool ExecuteFFmpeg(string ffmpegCmd)
        {
            Logger.Log("[RUNNING] Starting FFmpeg...", System.ConsoleColor.Green);

            try
            {
                // Try to run ffmpeg directly and capture output
                string args = ffmpegCmd.StartsWith("ffmpeg ") ? ffmpegCmd.Substring(7) : ffmpegCmd;

                var processInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = WorkingDirectory
                };

                using (var process = new Process { StartInfo = processInfo, EnableRaisingEvents = true })
                {
                    // Simplify ffmpeg output unless verbose mode is enabled
                    if (!FfmpegVerbose)
                    {
                        Logger.Log("[INFO] Simplified FFmpeg output enabled (set VideoGenerator.FfmpegVerbose = true for raw output).", ConsoleColor.DarkGray);
                    }

                    process.OutputDataReceived += (s, e) => { if (e.Data != null) HandleFfmpegLine(e.Data, false); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) HandleFfmpegLine(e.Data, true); };

                    try
                    { 
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();
                    }
                    catch (System.ComponentModel.Win32Exception wex)
                    {
                        // ffmpeg not found in PATH
                        Logger.Log($"[ERROR] ffmpeg process start failed: {wex.Message}", System.ConsoleColor.Red);
                        Logger.Log("Attempting fallback via cmd.exe (if available)...");

                        var cmdInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c {ffmpegCmd}",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = WorkingDirectory
                        };

                        using (var cmdProc = new Process { StartInfo = cmdInfo, EnableRaisingEvents = true })
                        {
                            cmdProc.OutputDataReceived += (s, e) => { if (e.Data != null) HandleFfmpegLine(e.Data, false); };
                            cmdProc.ErrorDataReceived += (s, e) => { if (e.Data != null) HandleFfmpegLine(e.Data, true); };

                            cmdProc.Start();
                            cmdProc.BeginOutputReadLine();
                            cmdProc.BeginErrorReadLine();
                            cmdProc.WaitForExit();
                        }
                    }
                }

                Logger.Log("");

                if (File.Exists(OutputFile))
                {
                    Logger.Log($"[DONE] Video saved to: {OutputFile}", System.ConsoleColor.Cyan);
                    return true;
                }
                else
                {
                    Logger.Log("[FAIL] FFmpeg failed to create the file.", System.ConsoleColor.Red);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] Exception occurred: {ex.Message}", System.ConsoleColor.Red);
                return false;
            }
        }

        /// <summary>
        /// Condenses ffmpeg output into concise progress updates and hides noisy lines unless verbose mode is enabled.
        /// </summary>
        private void HandleFfmpegLine(string data, bool isError)
        {
            if (string.IsNullOrWhiteSpace(data)) return;
            var d = data.Trim();

            // Parse common progress tokens emitted by ffmpeg (frame=, fps=, time=, bitrate=, speed=)
            string frame = null, fps = null, time = null, speed = null, bitrate = null;
            var parts = d.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var kv = part.Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;
                var key = kv[0].Trim().ToLowerInvariant();
                var val = kv[1].Trim();
                switch (key)
                {
                    case "frame": frame = val; break;
                    case "fps": fps = val; break;
                    case "time": time = val; break;
                    case "speed": speed = val; break;
                    case "bitrate": bitrate = val; break;
                }
            }

            if (frame != null || fps != null || time != null || speed != null)
            {
                // Convert tokens
                double parsedTimeSeconds = -1;
                if (time != null)
                {
                    // time usually appears as HH:MM:SS.xx
                    if (TimeSpan.TryParse(time, out var ts)) parsedTimeSeconds = ts.TotalSeconds;
                }

                int parsedFrame = -1;
                if (frame != null) int.TryParse(frame, out parsedFrame);

                double mainPercent = 0;
                if (parsedTimeSeconds > 0 && _expectedTotalSeconds > 0)
                {
                    mainPercent = Math.Min(100.0, (parsedTimeSeconds / _expectedTotalSeconds) * 100.0);
                }

                double framePercent = 0;
                if (parsedFrame > 0 && _expectedTotalFrames > 0)
                {
                    framePercent = Math.Min(100.0, (parsedFrame / _expectedTotalFrames) * 100.0);
                }

                var stats = new List<string>();
                if (!string.IsNullOrEmpty(frame)) stats.Add($"Frame: {frame}");
                if (!string.IsNullOrEmpty(fps)) stats.Add($"FPS: {fps}");
                if (!string.IsNullOrEmpty(time)) stats.Add($"Time: {time}");
                if (!string.IsNullOrEmpty(speed)) stats.Add($"Speed: {speed}");
                if (!string.IsNullOrEmpty(bitrate)) stats.Add($"Bitrate: {bitrate}");

                var bottomBar = $"Rendering: " + string.Join(" | ", stats);

                var now = DateTime.UtcNow;
                if ((bottomBar != _lastFfmpegProgress) || (now - _lastFfmpegProgressLog).TotalMilliseconds > 800)
                {
                    lock (_progressLock)
                    {
                        // Emit concise progress block
                        if (ShowFfmpegOutputInGui)
                        {
                            Logger.Log(bottomBar, ConsoleColor.DarkCyan);
                        }

                        // Also notify any GUI listeners specifically about the ffmpeg progress
                        try { VideoProgressUpdated?.Invoke(mainPercent, bottomBar); } catch { }

                        _lastFfmpegProgress = bottomBar;
                        _lastFfmpegProgressLog = now;
                    }
                }

                return;
            }

            // Treat anything with 'error' as an error
            if (d.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || d.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
            {
                if (ShowFfmpegOutputInGui) Logger.Log($"[FFMPEG] {d}", System.ConsoleColor.Red);
                return;
            }

            // If verbose mode enabled, show raw lines; otherwise keep console simple
            if (FfmpegVerbose && ShowFfmpegOutputInGui)
            {
                if (isError) Logger.Log(d, System.ConsoleColor.Yellow); else Logger.Log(d);
            }
        }

        private void ComposeStillWithStaticMap(string stillPath, string staticMapPath, string outPath)
        {
            // Compose static map (below) and still image (above) into a new PNG sized to target resolution
            using (var bgImg = Image.FromFile(staticMapPath))
            using (var stImg = Image.FromFile(stillPath))
            using (var bmp = new Bitmap(_width, _height))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // Draw background scaled to fill while preserving aspect
                var bgRect = GetAspectFitRect(bgImg.Width, bgImg.Height, _width, _height);
                g.DrawImage(bgImg, bgRect);

                // Draw still centered and aspect-fit
                var stRect = GetAspectFitRect(stImg.Width, stImg.Height, _width, _height);
                g.DrawImage(stImg, stRect);

                try
                {
                    bmp.Save(outPath, ImageFormat.Png);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[COMPOSE] Failed to save composed image {outPath}: {ex.Message}", ConsoleColor.Yellow);
                    throw;
                }
            }
        }

        private Rectangle GetAspectFitRect(int srcW, int srcH, int dstW, int dstH)
        {
            double scale = Math.Min((double)dstW / srcW, (double)dstH / srcH);
            int w = (int)Math.Round(srcW * scale);
            int h = (int)Math.Round(srcH * scale);
            int x = (dstW - w) / 2;
            int y = (dstH - h) / 2;
            return new Rectangle(x, y, w, h);
        }

        /// <summary>
        /// Checks if ffmpeg is installed and available in the system PATH.
        /// </summary>
        public static bool IsFfmpegInstalled(out string version, string ffmpegExe = "ffmpeg", int timeoutMs = 5000)
        {
            version = "Unknown";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegExe,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        return false;
                    }

                    var outText = p.StandardOutput.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(outText)) outText = p.StandardError.ReadToEnd();
                    
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }

                    if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(outText))
                    {
                        // Parse version from first line, e.g. "ffmpeg version 4.4.1-..."
                        var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0)
                        {
                            var match = Regex.Match(lines[0], @"ffmpeg version\s+([^\s]+)", RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                version = match.Groups[1].Value;
                            }
                            else
                            {
                                // Just take the first part of the string if regex fails, up to a reasonable length
                                version = lines[0].Length > 30 ? lines[0].Substring(0, 30) + "..." : lines[0];
                            }
                        }
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore exceptions (file not found etc)
            }
            return false;
        }

        /// <summary>
        /// Probes ffmpeg to see whether hardware encoders are available (NVENC, AMF, QSV).
        /// Returns true if an encoder name is present in the `ffmpeg -encoders` output. Provides a short message describing the result.
        /// </summary>
        public static bool IsHardwareEncodingSupported(out string message, string ffmpegExe = "ffmpeg", int timeoutMs = 5000)
        {
            var type = GetHardwareEncoderType(out message, ffmpegExe, timeoutMs);
            return type != HardwareEncoderType.None;
        }

        public static HardwareEncoderType GetHardwareEncoderType(out string message, string ffmpegExe = "ffmpeg", int timeoutMs = 5000)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegExe,
                    Arguments = "-hide_banner -encoders",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        message = "Failed to start ffmpeg";
                        return HardwareEncoderType.None;
                    }

                    var outText = p.StandardOutput.ReadToEnd();
                    // If nothing on stdout, check stderr as some ffmpeg builds write here
                    if (string.IsNullOrWhiteSpace(outText)) outText = p.StandardError.ReadToEnd();
                    p.WaitForExit(timeoutMs);

                    // Check for NVENC
                    if ((outText.IndexOf("h264_nvenc", StringComparison.OrdinalIgnoreCase) >= 0 || outText.IndexOf("hevc_nvenc", StringComparison.OrdinalIgnoreCase) >= 0) &&
                        ProbeEncoder("h264_nvenc", ffmpegExe, timeoutMs))
                    {
                        message = "NVENC (NVIDIA) encoder found";
                        return HardwareEncoderType.Nvenc;
                    }
                    // Check for AMF
                    else if ((outText.IndexOf("h264_amf", StringComparison.OrdinalIgnoreCase) >= 0 || outText.IndexOf("hevc_amf", StringComparison.OrdinalIgnoreCase) >= 0) &&
                             ProbeEncoder("h264_amf", ffmpegExe, timeoutMs))
                    {
                        message = "AMF (AMD) encoder found";
                        return HardwareEncoderType.Amf;
                    }
                    // Check for QSV
                    else if ((outText.IndexOf("h264_qsv", StringComparison.OrdinalIgnoreCase) >= 0 || outText.IndexOf("hevc_qsv", StringComparison.OrdinalIgnoreCase) >= 0) &&
                             ProbeEncoder("h264_qsv", ffmpegExe, timeoutMs))
                    {
                        message = "QSV (Intel) encoder found";
                        return HardwareEncoderType.Qsv;
                    }
                    else
                    {
                        message = "No working hardware encoders (NVENC/AMF/QSV) found";
                        return HardwareEncoderType.None;
                    }
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return HardwareEncoderType.None;
            }
        }

        private static bool ProbeEncoder(string encoderName, string ffmpegExe, int timeoutMs)
        {
            try
            {
                // Try to encode a tiny dummy video to see if the hardware encoder actually initializes
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegExe,
                    Arguments = $"-hide_banner -y -f lavfi -i color=c=black:s=64x64:d=0.1 -c:v {encoderName} -f null -",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Resolution modes for video generation.
    /// </summary>
    public enum ResolutionMode
    {
        Mode1080p,
        Mode4K,
        ModeVertical
    }

    public enum HardwareEncoderType
    {
        None,
        Nvenc,
        Amf,
        Qsv
    }
}
