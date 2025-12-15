using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace WeatherImageGenerator
{
    /// <summary>
    /// Generates MP4 videos from a sequence of images with fade transitions using FFmpeg.
    /// Converted from make_video.ps1 PowerShell script.
    /// </summary>
    public class VideoGenerator
    {
        private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

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

        private int _width;
        private int _height;
        private readonly string[] _extensions = { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp" };

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
            if (images.Count < 2)
            {
                Logger.Log($"[ERROR] Found {images.Count} images.", System.ConsoleColor.Red);
                return false;
            }

            Logger.Log($"[INFO] Found {images.Count} images. Processing...", System.ConsoleColor.Green);

            // Calculate resolution
            CalculateResolution();

            // Build filter complex
            var filterComplex = BuildFilterComplex(images);

            // Build and execute FFmpeg command
            var ffmpegCmd = BuildFFmpegCommand(images, filterComplex);
            return ExecuteFFmpeg(ffmpegCmd);
        }

        private List<FileInfo> LoadImages()
        {
            var images = new List<FileInfo>();
            var dir = new DirectoryInfo(ImageFolder);

            foreach (var extension in _extensions)
            {
                var files = dir.GetFiles(extension);
                images.AddRange(files);
            }

            return images.OrderBy(f => f.Name).ToList();
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

        private string BuildFilterComplex(List<FileInfo> images)
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

            // Build input stream filters
            foreach (var img in images)
            {
                // FIX 2: USE 'fps' INSTEAD OF 'framerate'
                // We replaced 'framerate=$FPS' with 'fps=$FPS' which is more robust for static loops
                filterParts.Add($"[{index}:v]{preScale},fps={FrameRate},setpts=PTS-STARTPTS[v{index}];");
                index++;
            }

            // Build transitions
            var lastLabel = "[v0]";
            var currentOffset = 0.0;
            var fadeStr = FadeDuration.ToString(_culture);

            for (int i = 0; i < images.Count - 1; i++)
            {
                var nextInput = $"[v{i + 1}]";
                var outputLabel = $"[f{i}]";

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
                    // If fades are disabled, use a simple concat instead
                    var concatString = $"{lastLabel}{nextInput}concat=n=2:v=1:a=0{outputLabel};";
                    filterParts.Add(concatString);
                }

                lastLabel = outputLabel;
            }

            // Final output
            var finalMap = images.Count == 1 ? "[v0]" : lastLabel;
            filterParts.Add($"{finalMap}format=yuv420p[outv]");

            return string.Concat(filterParts);
        }

        private string BuildFFmpegCommand(List<FileInfo> images, string filterComplex)
        {
            var sb = new StringBuilder();
            sb.Append("ffmpeg -y");

            // Add input files
            var clipDuration = StaticDuration + FadeDuration;
            var clipDurStr = clipDuration.ToString(_culture);

            foreach (var img in images)
            {
                sb.Append($" -framerate {FrameRate} -loop 1 -t {clipDurStr} -i \"{img.FullName}\"");
            }

            // Add filter complex
            sb.Append($" -filter_complex \"{filterComplex}\" -map \"[outv]\"");

            // Add audio if available
            if (File.Exists(MusicFile))
            {
                Logger.Log($"[AUDIO] Adding Audio: {MusicFile}", System.ConsoleColor.Magenta);
                sb.Append($" -i \"{MusicFile}\" -map \"{images.Count}:a\" -shortest");
            }

            // Video encoding settings
            sb.Append(" -c:v libx264 -preset medium -crf 23");
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
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) Logger.Log(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) Logger.Log(e.Data, System.ConsoleColor.Yellow); };

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
                            cmdProc.OutputDataReceived += (s, e) => { if (e.Data != null) Logger.Log(e.Data); };
                            cmdProc.ErrorDataReceived += (s, e) => { if (e.Data != null) Logger.Log(e.Data, System.ConsoleColor.Yellow); };

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
}
