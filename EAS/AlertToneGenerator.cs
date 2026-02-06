#nullable enable
using System;
using System.IO;

namespace EAS
{
    /// <summary>
    /// Generates the official Canadian Alerting Attention Signal (Alert Ready tone).
    /// 
    /// The Canadian Alerting Attention Signal is an 8-second sequence of alternating 
    /// half-second duration complex tones:
    /// - Tone 1: 932.33 Hz + 1,046.5 Hz + 3,135.96 Hz
    /// - Tone 2: 440 Hz + 659.26 Hz + 3,135.96 Hz
    /// 
    /// This is the same signal used by Alberta Emergency Alert and the national Alert Ready system.
    /// Reference: https://en.wikipedia.org/wiki/Alert_Ready
    /// </summary>
    public static class AlertToneGenerator
    {
        // Tone 1 frequencies (Hz)
        private const double TONE1_FREQ1 = 932.33;   // B5 (slightly flat)
        private const double TONE1_FREQ2 = 1046.50;  // C6
        private const double TONE1_FREQ3 = 3135.96;  // G7 (approx)

        // Tone 2 frequencies (Hz)  
        private const double TONE2_FREQ1 = 440.00;   // A4 (concert pitch)
        private const double TONE2_FREQ2 = 659.26;   // E5
        private const double TONE2_FREQ3 = 3135.96;  // G7 (same as tone 1)

        // Audio parameters
        private const int SAMPLE_RATE = 44100;       // 44.1 kHz CD quality
        private const int BITS_PER_SAMPLE = 16;      // 16-bit audio
        private const int NUM_CHANNELS = 1;          // Mono
        private const double TONE_DURATION = 0.5;    // 500ms per tone
        private const int NUM_ALTERNATIONS = 16;     // 16 alternations × 0.5s = 8 seconds total
        private const double AMPLITUDE = 0.7;        // 70% max amplitude to prevent clipping

        /// <summary>
        /// Path to the cached alert tone file.
        /// </summary>
        public static string CachedTonePath => Path.Combine(
            Path.GetDirectoryName(typeof(AlertToneGenerator).Assembly.Location) ?? ".",
            "AlertReadyTone.wav");

        /// <summary>
        /// Generates the Canadian Alerting Attention Signal and saves it to a file.
        /// </summary>
        /// <param name="outputPath">Path for the output WAV file</param>
        /// <returns>True if generation succeeded</returns>
        public static bool GenerateAlertTone(string outputPath)
        {
            try
            {
                int samplesPerTone = (int)(SAMPLE_RATE * TONE_DURATION);
                int totalSamples = samplesPerTone * NUM_ALTERNATIONS;
                
                short[] samples = new short[totalSamples];
                int sampleIndex = 0;

                for (int i = 0; i < NUM_ALTERNATIONS; i++)
                {
                    bool isTone1 = (i % 2 == 0);
                    double freq1, freq2, freq3;

                    if (isTone1)
                    {
                        freq1 = TONE1_FREQ1;
                        freq2 = TONE1_FREQ2;
                        freq3 = TONE1_FREQ3;
                    }
                    else
                    {
                        freq1 = TONE2_FREQ1;
                        freq2 = TONE2_FREQ2;
                        freq3 = TONE2_FREQ3;
                    }

                    // Generate samples for this half-second tone
                    for (int j = 0; j < samplesPerTone; j++)
                    {
                        double t = (double)j / SAMPLE_RATE;
                        
                        // Combine three sine waves (equal amplitude for each frequency)
                        double value = 
                            Math.Sin(2 * Math.PI * freq1 * t) +
                            Math.Sin(2 * Math.PI * freq2 * t) +
                            Math.Sin(2 * Math.PI * freq3 * t);
                        
                        // Normalize (divide by 3 for equal mix) and apply amplitude
                        value = (value / 3.0) * AMPLITUDE;

                        // Apply attack/decay envelope to reduce clicking
                        double envelope = 1.0;
                        int fadeLength = (int)(SAMPLE_RATE * 0.01); // 10ms fade
                        
                        if (j < fadeLength)
                        {
                            envelope = (double)j / fadeLength; // Attack
                        }
                        else if (j > samplesPerTone - fadeLength)
                        {
                            envelope = (double)(samplesPerTone - j) / fadeLength; // Decay
                        }
                        
                        value *= envelope;

                        // Convert to 16-bit PCM
                        samples[sampleIndex++] = (short)(value * short.MaxValue);
                    }
                }

                // Write WAV file
                WriteWavFile(outputPath, samples, SAMPLE_RATE, BITS_PER_SAMPLE, NUM_CHANNELS);
                
                Console.WriteLine($"[AlertToneGenerator] Generated {NUM_ALTERNATIONS * TONE_DURATION}s alert tone: {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AlertToneGenerator] Error generating alert tone: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the path to the alert tone, generating it if necessary.
        /// </summary>
        /// <returns>Path to the WAV file, or null if generation failed</returns>
        public static string? GetOrGenerateAlertTone()
        {
            string tonePath = CachedTonePath;

            // Check if cached tone exists and is valid
            if (File.Exists(tonePath))
            {
                var fileInfo = new FileInfo(tonePath);
                // Should be approximately 705KB for 8 seconds of 16-bit 44.1kHz mono audio
                if (fileInfo.Length > 100000)
                {
                    return tonePath;
                }
            }

            // Generate the tone
            if (GenerateAlertTone(tonePath))
            {
                return tonePath;
            }

            return null;
        }

        /// <summary>
        /// Generates the alert tone to a specified output directory.
        /// </summary>
        public static string? GenerateToDirectory(string directory)
        {
            string tonePath = Path.Combine(directory, "AlertReadyTone.wav");
            
            if (File.Exists(tonePath))
            {
                var fileInfo = new FileInfo(tonePath);
                if (fileInfo.Length > 100000)
                {
                    return tonePath;
                }
            }

            if (GenerateAlertTone(tonePath))
            {
                return tonePath;
            }

            return null;
        }

        /// <summary>
        /// Writes PCM audio data to a WAV file.
        /// </summary>
        private static void WriteWavFile(string filePath, short[] samples, int sampleRate, int bitsPerSample, int numChannels)
        {
            using var stream = new FileStream(filePath, FileMode.Create);
            using var writer = new BinaryWriter(stream);

            int byteRate = sampleRate * numChannels * bitsPerSample / 8;
            short blockAlign = (short)(numChannels * bitsPerSample / 8);
            int dataSize = samples.Length * sizeof(short);
            int fileSize = 36 + dataSize;

            // RIFF header
            writer.Write(new char[] { 'R', 'I', 'F', 'F' });
            writer.Write(fileSize);
            writer.Write(new char[] { 'W', 'A', 'V', 'E' });

            // fmt subchunk
            writer.Write(new char[] { 'f', 'm', 't', ' ' });
            writer.Write(16);                       // Subchunk1Size (16 for PCM)
            writer.Write((short)1);                 // AudioFormat (1 = PCM)
            writer.Write((short)numChannels);       // NumChannels
            writer.Write(sampleRate);               // SampleRate
            writer.Write(byteRate);                 // ByteRate
            writer.Write(blockAlign);               // BlockAlign
            writer.Write((short)bitsPerSample);     // BitsPerSample

            // data subchunk
            writer.Write(new char[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);

            // Write audio samples
            foreach (short sample in samples)
            {
                writer.Write(sample);
            }
        }

        /// <summary>
        /// Concatenates the alert tone with TTS audio to create a complete emergency alert audio file.
        /// Uses FFmpeg for proper audio mixing if available, otherwise falls back to raw concatenation.
        /// </summary>
        /// <param name="ttsAudioPath">Path to the TTS-generated speech audio</param>
        /// <param name="outputPath">Path for the combined output audio</param>
        /// <returns>True if concatenation succeeded</returns>
        public static bool PrependAlertToneToAudio(string ttsAudioPath, string outputPath)
        {
            try
            {
                string? alertTonePath = GetOrGenerateAlertTone();
                if (alertTonePath == null || !File.Exists(alertTonePath))
                {
                    Console.WriteLine("[AlertToneGenerator] Could not get alert tone, copying TTS audio only.");
                    File.Copy(ttsAudioPath, outputPath, true);
                    return true;
                }

                // Try FFmpeg first for proper concatenation
                if (TryConcatenateWithFFmpeg(alertTonePath, ttsAudioPath, outputPath))
                {
                    return true;
                }

                // Fallback: just copy the TTS audio (alert tone will be missing)
                Console.WriteLine("[AlertToneGenerator] FFmpeg not available, using TTS audio without alert tone prefix.");
                File.Copy(ttsAudioPath, outputPath, true);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AlertToneGenerator] Error prepending alert tone: {ex.Message}");
                return false;
            }
        }

        private static bool TryConcatenateWithFFmpeg(string alertTonePath, string ttsAudioPath, string outputPath)
        {
            try
            {
                // Create a temp file list for FFmpeg concat demuxer
                string tempListFile = Path.GetTempFileName();
                File.WriteAllText(tempListFile, 
                    $"file '{alertTonePath.Replace("\\", "/")}'\nfile '{ttsAudioPath.Replace("\\", "/")}'\n");

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -f concat -safe 0 -i \"{tempListFile}\" -c:a pcm_s16le \"{outputPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null) return false;

                process.WaitForExit(60000);
                
                try { File.Delete(tempListFile); } catch { }

                if (process.ExitCode == 0 && File.Exists(outputPath))
                {
                    Console.WriteLine($"[AlertToneGenerator] Successfully prepended alert tone to audio.");
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a complete emergency alert audio file with alert tone, TTS speech, and optional trailing tone.
        /// </summary>
        /// <param name="text">Text to synthesize</param>
        /// <param name="outputPath">Output file path</param>
        /// <param name="language">Language code (en-CA or fr-CA)</param>
        /// <param name="addTrailingTone">Whether to add a second alert tone at the end</param>
        /// <returns>True if successful</returns>
        public static async System.Threading.Tasks.Task<bool> CreateCompleteAlertAudioAsync(
            string text, 
            string outputPath, 
            string language = "fr-CA",
            bool addTrailingTone = false)
        {
            try
            {
                // 1. Generate the alert tone
                string? alertTonePath = GetOrGenerateAlertTone();
                if (alertTonePath == null)
                {
                    Console.WriteLine("[AlertToneGenerator] Failed to generate alert tone.");
                    return false;
                }

                // 2. Generate TTS audio
                string tempTtsPath = Path.Combine(Path.GetTempPath(), $"tts_{Guid.NewGuid()}.mp3");
                
                using var ttsClient = new EdgeTtsClient();
                string voice = EdgeTtsClient.GetVoiceForLanguage(language);
                
                bool ttsSuccess = await ttsClient.SynthesizeToFileAsync(text, tempTtsPath, voice, "+5%", "+0Hz");
                if (!ttsSuccess || !File.Exists(tempTtsPath))
                {
                    Console.WriteLine("[AlertToneGenerator] Failed to generate TTS audio.");
                    return false;
                }

                // 3. Concatenate: AlertTone + TTS (+ optional AlertTone)
                string[] inputFiles = addTrailingTone 
                    ? new[] { alertTonePath, tempTtsPath, alertTonePath }
                    : new[] { alertTonePath, tempTtsPath };

                bool success = ConcatenateAudioFiles(inputFiles, outputPath);

                // Cleanup temp file
                try { File.Delete(tempTtsPath); } catch { }

                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AlertToneGenerator] Error creating complete alert audio: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Concatenates multiple audio files using FFmpeg filter_complex concat filter.
        /// This properly handles mixing different audio formats (WAV, MP3, etc.) by decoding each
        /// input independently before concatenation, unlike the concat demuxer which can fail
        /// silently when formats differ.
        /// </summary>
        public static bool ConcatenateAudioFiles(string[] inputPaths, string outputPath)
        {
            try
            {
                // Filter to only existing files
                var validPaths = new System.Collections.Generic.List<string>();
                foreach (string path in inputPaths)
                {
                    if (File.Exists(path) && new FileInfo(path).Length > 100)
                    {
                        validPaths.Add(path);
                    }
                    else
                    {
                        Console.WriteLine($"[AlertToneGenerator] Skipping missing/empty audio file: {path}");
                    }
                }

                if (validPaths.Count == 0)
                {
                    Console.WriteLine("[AlertToneGenerator] No valid audio files to concatenate.");
                    return false;
                }

                if (validPaths.Count == 1)
                {
                    // Single file - just copy it
                    File.Copy(validPaths[0], outputPath, true);
                    return File.Exists(outputPath);
                }

                // Build FFmpeg command using filter_complex concat filter
                // This properly decodes each input (regardless of format) and concatenates the decoded audio
                var args = new System.Text.StringBuilder();
                args.Append("-y ");

                // Add input files
                foreach (string path in validPaths)
                {
                    args.Append($"-i \"{path}\" ");
                }

                // Build filter_complex: [0:a][1:a][2:a]...concat=n=N:v=0:a=1[out]
                args.Append("-filter_complex \"");
                for (int i = 0; i < validPaths.Count; i++)
                {
                    args.Append($"[{i}:a]");
                }
                args.Append($"concat=n={validPaths.Count}:v=0:a=1[out]\" ");
                args.Append($"-map \"[out]\" -c:a pcm_s16le -ar 44100 \"{outputPath}\"");

                Console.WriteLine($"[AlertToneGenerator] Concatenating {validPaths.Count} audio files with filter_complex...");

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args.ToString(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null) 
                {
                    Console.WriteLine("[AlertToneGenerator] Failed to start FFmpeg.");
                    return false;
                }

                // Read output streams asynchronously to prevent deadlock
                string? errorOutput = null;
                var errorTask = System.Threading.Tasks.Task.Run(() => errorOutput = process.StandardError.ReadToEnd());
                var outputTask = System.Threading.Tasks.Task.Run(() => process.StandardOutput.ReadToEnd());

                bool exited = process.WaitForExit(60000); // 60 second timeout
                
                if (!exited)
                {
                    Console.WriteLine("[AlertToneGenerator] FFmpeg timed out, killing process.");
                    try { process.Kill(); } catch { }
                    return false;
                }

                // Wait for stream reading to complete
                System.Threading.Tasks.Task.WaitAll(new[] { errorTask, outputTask }, 5000);

                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"[AlertToneGenerator] FFmpeg filter_complex concat failed (exit code {process.ExitCode}): {errorOutput?.Substring(0, Math.Min(500, errorOutput?.Length ?? 0))}");
                    
                    // Fallback: try concat demuxer as backup (works when formats are the same)
                    Console.WriteLine("[AlertToneGenerator] Falling back to concat demuxer...");
                    return TryConcatenateDemuxer(validPaths.ToArray(), outputPath);
                }

                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000)
                {
                    Console.WriteLine($"[AlertToneGenerator] Successfully concatenated {validPaths.Count} audio files ({new FileInfo(outputPath).Length / 1024}KB).");
                    return true;
                }

                Console.WriteLine("[AlertToneGenerator] Output file missing or too small after concat.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AlertToneGenerator] FFmpeg concatenation error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Fallback concatenation using the concat demuxer (works best when all files have the same format).
        /// </summary>
        private static bool TryConcatenateDemuxer(string[] inputPaths, string outputPath)
        {
            try
            {
                string tempListFile = Path.GetTempFileName();
                var lines = new System.Text.StringBuilder();
                foreach (string path in inputPaths)
                {
                    lines.AppendLine($"file '{path.Replace("\\", "/").Replace("'", "'\\''")}'");
                }
                File.WriteAllText(tempListFile, lines.ToString());

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -f concat -safe 0 -i \"{tempListFile}\" -c:a pcm_s16le -ar 44100 \"{outputPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null) return false;

                var errorTask = System.Threading.Tasks.Task.Run(() => process.StandardError.ReadToEnd());
                var outputTask = System.Threading.Tasks.Task.Run(() => process.StandardOutput.ReadToEnd());
                
                bool exited = process.WaitForExit(60000);
                System.Threading.Tasks.Task.WaitAll(new[] { errorTask, outputTask }, 5000);
                
                try { File.Delete(tempListFile); } catch { }

                if (!exited) { try { process.Kill(); } catch { } return false; }
                
                return process.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000;
            }
            catch
            {
                return false;
            }
        }
    }
}
