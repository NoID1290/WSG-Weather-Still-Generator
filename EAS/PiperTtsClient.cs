using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace EAS
{
    /// <summary>
    /// Open-source TTS client using Piper (https://github.com/rhasspy/piper).
    /// High-quality neural TTS that runs 100% offline.
    /// Supports English and French with natural-sounding voices.
    /// </summary>
    public class PiperTtsClient : IDisposable
    {
        // Voice model URLs from Hugging Face (official Piper models)
        private static readonly Dictionary<string, VoiceModel> AvailableVoices = new()
        {
            // French voices
            ["fr_FR-siwis-medium"] = new VoiceModel(
                "fr_FR-siwis-medium",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/fr/fr_FR/siwis/medium/fr_FR-siwis-medium.onnx",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/fr/fr_FR/siwis/medium/fr_FR-siwis-medium.onnx.json",
                "French (France) - Siwis - Medium quality female voice"
            ),
            ["fr_FR-upmc-medium"] = new VoiceModel(
                "fr_FR-upmc-medium",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/fr/fr_FR/upmc/medium/fr_FR-upmc-medium.onnx",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/fr/fr_FR/upmc/medium/fr_FR-upmc-medium.onnx.json",
                "French (France) - UPMC - Medium quality male voice"
            ),
            ["fr_FR-tom-medium"] = new VoiceModel(
                "fr_FR-tom-medium",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/fr/fr_FR/tom/medium/fr_FR-tom-medium.onnx",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/fr/fr_FR/tom/medium/fr_FR-tom-medium.onnx.json",
                "French (France) - Tom - Medium quality male voice"
            ),
            
            // English US voices
            ["en_US-amy-medium"] = new VoiceModel(
                "en_US-amy-medium",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/amy/medium/en_US-amy-medium.onnx",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/amy/medium/en_US-amy-medium.onnx.json",
                "English (US) - Amy - Medium quality female voice"
            ),
            ["en_US-ryan-medium"] = new VoiceModel(
                "en_US-ryan-medium",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/ryan/medium/en_US-ryan-medium.onnx",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/ryan/medium/en_US-ryan-medium.onnx.json",
                "English (US) - Ryan - Medium quality male voice"
            ),
            ["en_US-lessac-medium"] = new VoiceModel(
                "en_US-lessac-medium",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx.json",
                "English (US) - Lessac - Medium quality female voice (high quality)"
            ),
            
            // English GB voices
            ["en_GB-alan-medium"] = new VoiceModel(
                "en_GB-alan-medium",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_GB/alan/medium/en_GB-alan-medium.onnx",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_GB/alan/medium/en_GB-alan-medium.onnx.json",
                "English (UK) - Alan - Medium quality male voice"
            ),
            ["en_GB-alba-medium"] = new VoiceModel(
                "en_GB-alba-medium",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_GB/alba/medium/en_GB-alba-medium.onnx",
                "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_GB/alba/medium/en_GB-alba-medium.onnx.json",
                "English (UK) - Alba - Medium quality female voice"
            ),
        };

        // Default voices per language
        public const string DEFAULT_VOICE_FR = "fr_FR-siwis-medium";
        public const string DEFAULT_VOICE_EN = "en_US-lessac-medium";

        private readonly string _piperPath;
        private readonly string _modelsPath;
        private bool _disposed;
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

        /// <summary>
        /// Creates a new PiperTtsClient.
        /// </summary>
        /// <param name="basePath">Base path for Piper installation. If null, uses app directory.</param>
        public PiperTtsClient(string? basePath = null)
        {
            basePath ??= Path.Combine(AppContext.BaseDirectory, "piper");
            _piperPath = Path.Combine(basePath, "piper");
            _modelsPath = Path.Combine(basePath, "models");

            Directory.CreateDirectory(_modelsPath);
        }

        /// <summary>
        /// Gets the appropriate voice for the given language.
        /// </summary>
        public static string GetVoiceForLanguage(string language)
        {
            return language?.ToLowerInvariant() switch
            {
                "fr-ca" or "fr-fr" or "fr" => DEFAULT_VOICE_FR,
                "en-ca" or "en-us" or "en-gb" or "en" => DEFAULT_VOICE_EN,
                _ => DEFAULT_VOICE_EN
            };
        }

        /// <summary>
        /// Gets all available voice names.
        /// </summary>
        public static IReadOnlyList<string> GetAvailableVoices() => AvailableVoices.Keys.ToList();

        /// <summary>
        /// Gets voice description.
        /// </summary>
        public static string GetVoiceDescription(string voiceName)
        {
            return AvailableVoices.TryGetValue(voiceName, out var model) 
                ? model.Description 
                : "Unknown voice";
        }

        /// <summary>
        /// Checks if Piper is installed and ready.
        /// </summary>
        public bool IsPiperInstalled()
        {
            string exePath = GetPiperExecutablePath();
            return File.Exists(exePath);
        }

        /// <summary>
        /// Checks if a specific voice model is downloaded.
        /// </summary>
        public bool IsVoiceDownloaded(string voiceName)
        {
            if (!AvailableVoices.TryGetValue(voiceName, out var model))
                return false;

            string onnxPath = Path.Combine(_modelsPath, $"{voiceName}.onnx");
            string jsonPath = Path.Combine(_modelsPath, $"{voiceName}.onnx.json");
            return File.Exists(onnxPath) && File.Exists(jsonPath);
        }

        /// <summary>
        /// Downloads and installs Piper TTS.
        /// </summary>
        public async Task<bool> InstallPiperAsync(IProgress<string>? progress = null)
        {
            try
            {
                if (IsPiperInstalled())
                {
                    progress?.Report("Piper is already installed.");
                    return true;
                }

                string platform = GetPlatformIdentifier();
                string downloadUrl = GetPiperDownloadUrl(platform);

                progress?.Report($"Downloading Piper for {platform}...");

                bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                string extension = isWindows ? ".zip" : ".tar.gz";
                string tempArchive = Path.Combine(Path.GetTempPath(), $"piper_{platform}{extension}");
                
                // Download Piper
                progress?.Report($"Downloading from: {downloadUrl}");
                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    progress?.Report($"Download size: {totalBytes / 1024 / 1024} MB");
                    using var fileStream = File.Create(tempArchive);
                    await response.Content.CopyToAsync(fileStream);
                }

                progress?.Report("Extracting Piper...");

                string extractPath = Path.GetDirectoryName(_piperPath)!;
                Directory.CreateDirectory(extractPath);

                if (isWindows)
                {
                    // Use .NET's ZipFile for Windows
                    ZipFile.ExtractToDirectory(tempArchive, extractPath, overwriteFiles: true);
                }
                else
                {
                    // Use tar for Linux/Mac
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "tar",
                        Arguments = $"-xzf \"{tempArchive}\" -C \"{extractPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                    }
                }

                // Cleanup
                try { File.Delete(tempArchive); } catch { }

                if (IsPiperInstalled())
                {
                    progress?.Report("Piper installed successfully!");
                    return true;
                }

                progress?.Report($"Failed to install Piper. Executable not found at: {GetPiperExecutablePath()}");
                return false;
            }
            catch (Exception ex)
            {
                progress?.Report($"Error installing Piper: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Downloads a voice model.
        /// </summary>
        public async Task<bool> DownloadVoiceAsync(string voiceName, IProgress<string>? progress = null)
        {
            try
            {
                if (!AvailableVoices.TryGetValue(voiceName, out var model))
                {
                    progress?.Report($"Unknown voice: {voiceName}");
                    return false;
                }

                if (IsVoiceDownloaded(voiceName))
                {
                    progress?.Report($"Voice {voiceName} is already downloaded.");
                    return true;
                }

                string onnxPath = Path.Combine(_modelsPath, $"{voiceName}.onnx");
                string jsonPath = Path.Combine(_modelsPath, $"{voiceName}.onnx.json");

                // Download ONNX model
                progress?.Report($"Downloading voice model: {voiceName}...");
                using (var response = await _httpClient.GetAsync(model.OnnxUrl))
                {
                    response.EnsureSuccessStatusCode();
                    using var fileStream = File.Create(onnxPath);
                    await response.Content.CopyToAsync(fileStream);
                }

                // Download JSON config
                progress?.Report("Downloading voice configuration...");
                using (var response = await _httpClient.GetAsync(model.JsonUrl))
                {
                    response.EnsureSuccessStatusCode();
                    using var fileStream = File.Create(jsonPath);
                    await response.Content.CopyToAsync(fileStream);
                }

                progress?.Report($"Voice {voiceName} downloaded successfully!");
                return true;
            }
            catch (Exception ex)
            {
                progress?.Report($"Error downloading voice: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Synthesize text to audio file using Piper TTS.
        /// </summary>
        /// <param name="text">Text to synthesize</param>
        /// <param name="outputPath">Output WAV file path</param>
        /// <param name="voiceName">Voice model name (e.g., "fr_FR-siwis-medium")</param>
        /// <param name="speakerId">Speaker ID for multi-speaker models (optional)</param>
        /// <param name="lengthScale">Speech speed (1.0 = normal, lower = faster)</param>
        /// <returns>True if synthesis succeeded</returns>
        public async Task<bool> SynthesizeToFileAsync(
            string text, 
            string outputPath, 
            string? voiceName = null, 
            int? speakerId = null,
            float lengthScale = 1.0f)
        {
            try
            {
                voiceName ??= DEFAULT_VOICE_EN;

                // Ensure Piper is installed
                if (!IsPiperInstalled())
                {
                    Console.WriteLine("[PiperTTS] Piper not installed. Attempting to install...");
                    if (!await InstallPiperAsync(new Progress<string>(msg => Console.WriteLine($"[PiperTTS] {msg}"))))
                    {
                        Console.WriteLine("[PiperTTS] Failed to install Piper.");
                        return false;
                    }
                }

                // Ensure voice is downloaded
                if (!IsVoiceDownloaded(voiceName))
                {
                    Console.WriteLine($"[PiperTTS] Voice {voiceName} not downloaded. Downloading...");
                    if (!await DownloadVoiceAsync(voiceName, new Progress<string>(msg => Console.WriteLine($"[PiperTTS] {msg}"))))
                    {
                        Console.WriteLine($"[PiperTTS] Failed to download voice {voiceName}.");
                        return false;
                    }
                }

                string piperExe = GetPiperExecutablePath();
                string modelPath = Path.Combine(_modelsPath, $"{voiceName}.onnx");

                // Build arguments
                var args = new StringBuilder();
                args.Append($"--model \"{modelPath}\"");
                args.Append($" --output_file \"{outputPath}\"");
                
                if (speakerId.HasValue)
                    args.Append($" --speaker {speakerId.Value}");
                
                if (Math.Abs(lengthScale - 1.0f) > 0.01f)
                    args.Append($" --length_scale {lengthScale:F2}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = piperExe,
                    Arguments = args.ToString(),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardInputEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                // Write text to stdin
                await process.StandardInput.WriteLineAsync(text);
                process.StandardInput.Close();

                // Read any errors
                string errors = await process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"[PiperTTS] Synthesis failed with exit code {process.ExitCode}: {errors}");
                    return false;
                }

                // Verify output file exists and has content
                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 100)
                {
                    Console.WriteLine($"[PiperTTS] Successfully synthesized audio to {outputPath}");
                    return true;
                }

                Console.WriteLine("[PiperTTS] Output file not created or empty.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PiperTTS] Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Synthesize text to raw audio bytes.
        /// </summary>
        public async Task<byte[]?> SynthesizeAsync(
            string text,
            string? voiceName = null,
            int? speakerId = null,
            float lengthScale = 1.0f)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"piper_tts_{Guid.NewGuid()}.wav");
            try
            {
                if (await SynthesizeToFileAsync(text, tempPath, voiceName, speakerId, lengthScale))
                {
                    return await File.ReadAllBytesAsync(tempPath);
                }
                return null;
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }
        }

        /// <summary>
        /// Converts WAV to MP3 using ffmpeg (if available).
        /// </summary>
        public static async Task<bool> ConvertWavToMp3Async(string wavPath, string mp3Path)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -i \"{wavPath}\" -codec:a libmp3lame -qscale:a 2 \"{mp3Path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    return process.ExitCode == 0 && File.Exists(mp3Path);
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private string GetPiperExecutablePath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Path.Combine(_piperPath, "piper.exe");
            else
                return Path.Combine(_piperPath, "piper");
        }

        private static string GetPlatformIdentifier()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 
                    ? "windows_arm64" 
                    : "windows_amd64";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 
                    ? "linux_aarch64" 
                    : "linux_x86_64";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 
                    ? "macos_arm64" 
                    : "macos_x64";
            }
            throw new PlatformNotSupportedException("Unsupported platform for Piper TTS");
        }

        private static string GetPiperDownloadUrl(string platform)
        {
            // Piper releases are at: https://github.com/rhasspy/piper/releases
            const string version = "2023.11.14-2";
            
            // Windows uses .zip, Linux/Mac use .tar.gz
            if (platform.StartsWith("windows"))
                return $"https://github.com/rhasspy/piper/releases/download/{version}/piper_{platform}.zip";
            else
                return $"https://github.com/rhasspy/piper/releases/download/{version}/piper_{platform}.tar.gz";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private record VoiceModel(string Name, string OnnxUrl, string JsonUrl, string Description);
    }
}
