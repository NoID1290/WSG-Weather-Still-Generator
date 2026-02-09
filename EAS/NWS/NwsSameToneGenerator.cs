#nullable enable
using System;
using System.IO;

namespace EAS.NWS
{
    /// <summary>
    /// Generates the official US Emergency Alert System (EAS) attention signal and SAME alert tones.
    /// 
    /// The EAS Attention Signal is approximately 8 seconds of alternating dual-tone signals:
    /// - Primary tone: 853 Hz + 960 Hz (combined)
    /// - Alternate tone: 853 Hz (lower frequency warning)
    /// 
    /// SAME (Specific Area Message Encoding) headers use the EAS tones for alert origination.
    /// This is the standard alert tone used by the National Weather Service (NWS) and
    /// Emergency Management Agency (FEMA) across the United States.
    /// Reference: FCC 47 CFR Part 11 - Emergency Alert System
    /// </summary>
    public static class NwsSameToneGenerator
    {
        // EAS Attention Signal frequencies (Hz)
        private const double EAS_TONE1_FREQ = 853.0;    // Primary frequency
        private const double EAS_TONE2_FREQ = 960.0;    // Secondary frequency (creates beat pattern)

        // Audio parameters (matching professional EAS standards)
        private const int SAMPLE_RATE = 44100;          // 44.1 kHz CD quality
        private const int BITS_PER_SAMPLE = 16;         // 16-bit audio
        private const int NUM_CHANNELS = 1;             // Mono
        private const double TONE_DURATION = 0.5;       // 500ms per tone segment
        private const int NUM_ALTERNATIONS = 16;        // 16 alternations x 0.5s = 8 seconds total
        private const double AMPLITUDE = 0.7;           // 70% max amplitude to prevent clipping

        /// <summary>
        /// Path to the cached SAME tone file.
        /// </summary>
        public static string CachedTonePath => Path.Combine(
            Path.GetDirectoryName(typeof(NwsSameToneGenerator).Assembly.Location) ?? ".",
            "NWSSameTone.wav");

        /// <summary>
        /// Generates the EAS Attention Signal and saves it to a file.
        /// Uses the standard dual-tone pattern with beat frequency modulation.
        /// </summary>
        /// <param name="outputPath">Path for the output WAV file</param>
        /// <returns>True if generation succeeded</returns>
        public static bool GenerateSameTone(string outputPath)
        {
            try
            {
                int samplesPerTone = (int)(SAMPLE_RATE * TONE_DURATION);
                int totalSamples = samplesPerTone * NUM_ALTERNATIONS;

                short[] samples = new short[totalSamples];
                int sampleIndex = 0;

                for (int i = 0; i < NUM_ALTERNATIONS; i++)
                {
                    bool isPrimaryTone = (i % 2 == 0);

                    // Generate samples for this half-second tone
                    for (int j = 0; j < samplesPerTone; j++)
                    {
                        double t = (double)j / SAMPLE_RATE;

                        double value;
                        if (isPrimaryTone)
                        {
                            // Primary tone: Both frequencies combined (beat pattern)
                            value = Math.Sin(2 * Math.PI * EAS_TONE1_FREQ * t) +
                                    Math.Sin(2 * Math.PI * EAS_TONE2_FREQ * t);
                            value = (value / 2.0) * AMPLITUDE;
                        }
                        else
                        {
                            // Alternate tone: Lower frequency only (more sparse)
                            value = Math.Sin(2 * Math.PI * EAS_TONE1_FREQ * t) * AMPLITUDE * 0.8;
                        }

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

                Console.WriteLine($"[NwsSameToneGenerator] Generated {NUM_ALTERNATIONS * TONE_DURATION}s EAS SAME tone: {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NwsSameToneGenerator] Error generating SAME tone: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the path to the SAME alert tone, generating it if necessary.
        /// </summary>
        /// <returns>Path to the WAV file, or null if generation failed</returns>
        public static string? GetOrGenerateSameTone()
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
            if (GenerateSameTone(tonePath))
            {
                return tonePath;
            }

            return null;
        }

        /// <summary>
        /// Writes a WAV file with the specified audio parameters.
        /// </summary>
        private static void WriteWavFile(string path, short[] samples, int sampleRate, int bitsPerSample, int numChannels)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                int byteRate = sampleRate * numChannels * bitsPerSample / 8;
                int blockAlign = numChannels * bitsPerSample / 8;
                int dataSize = samples.Length * blockAlign;

                // RIFF chunk
                bw.Write(new char[] { 'R', 'I', 'F', 'F' });
                bw.Write(36 + dataSize);
                bw.Write(new char[] { 'W', 'A', 'V', 'E' });

                // fmt subchunk
                bw.Write(new char[] { 'f', 'm', 't', ' ' });
                bw.Write(16); // Subchunk1Size (for PCM)
                bw.Write((short)1); // AudioFormat (1 = PCM)
                bw.Write((short)numChannels);
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write((short)blockAlign);
                bw.Write((short)bitsPerSample);

                // data subchunk
                bw.Write(new char[] { 'd', 'a', 't', 'a' });
                bw.Write(dataSize);
                foreach (var sample in samples)
                {
                    bw.Write(sample);
                }
            }
        }
    }
}
