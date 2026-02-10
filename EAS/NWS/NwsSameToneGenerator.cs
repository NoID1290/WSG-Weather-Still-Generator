#nullable enable
using System;
using System.IO;

namespace EAS.NWS
{
    /// <summary>
    /// Generates the official US Emergency Alert System (EAS) attention signal and SAME alert tones.
    /// 
    /// The EAS Attention Signal is 8 seconds of continuous dual-tone (853 Hz + 960 Hz combined).
    /// This creates a distinctive, dissonant sound that approximates a major second interval.
    /// The two frequencies are played SIMULTANEOUSLY for the entire duration, NOT alternated.
    /// 
    /// SAME (Specific Area Message Encoding) headers use AFSK (Audio Frequency-Shift Keying) to encode
    /// alert metadata as a digital burst before the attention signal.
    /// This is the standard alert tone used by the National Weather Service (NWS) and
    /// Emergency Management Agency (FEMA) across the United States.
    /// Reference: FCC 47 CFR Part 11 - Emergency Alert System
    /// Wikipedia: "On commercial broadcast stations, a 'two-tone' attention signal of 853 Hz 
    /// and 960 Hz sine waves is used instead"
    /// </summary>
    public static class NwsSameToneGenerator
    {
        // EAS Attention Signal frequencies (Hz) - played simultaneously
        private const double EAS_TONE1_FREQ = 853.0;    // Lower frequency
        private const double EAS_TONE2_FREQ = 960.0;    // Upper frequency (creates dissonant major second)

        // SAME AFSK (Audio Frequency-Shift Keying) parameters
        private const double SAME_MARK_FREQ = 2083.3;   // Binary 1 (mark frequency)
        private const double SAME_SPACE_FREQ = 1562.5;  // Binary 0 (space frequency)
        private const double SAME_BAUD_RATE = 520.83;   // Bits per second
        private const byte SAME_PREAMBLE = 0xAB;        // 10101011 calibration byte

        // Audio parameters (matching professional EAS standards)
        private const int SAMPLE_RATE = 44100;          // 44.1 kHz CD quality
        private const int BITS_PER_SAMPLE = 16;         // 16-bit audio
        private const int NUM_CHANNELS = 1;             // Mono
        private const double TOTAL_DURATION = 8.0;      // 8 seconds continuous tone
        private const double AMPLITUDE = 0.7;           // 70% max amplitude to prevent clipping

        /// <summary>
        /// Path to the cached SAME tone file (attention signal only).
        /// </summary>
        public static string CachedTonePath => Path.Combine(
            Path.GetDirectoryName(typeof(NwsSameToneGenerator).Assembly.Location) ?? ".",
            "NWSSameTone_Continuous.wav");

        /// <summary>
        /// Path to the cached complete SAME alert (header + attention signal).
        /// </summary>
        public static string CachedCompleteAlertPath => Path.Combine(
            Path.GetDirectoryName(typeof(NwsSameToneGenerator).Assembly.Location) ?? ".",
            "NWSSameAlert_Complete.wav");

        /// <summary>
        /// Generates the EAS Attention Signal and saves it to a file.
        /// Uses the standard continuous dual-tone (853+960 Hz) for 8 seconds.
        /// The two frequencies are played simultaneously to create a dissonant, attention-grabbing sound.
        /// </summary>
        /// <param name="outputPath">Path for the output WAV file</param>
        /// <returns>True if generation succeeded</returns>
        public static bool GenerateSameTone(string outputPath)
        {
            try
            {
                int totalSamples = (int)(SAMPLE_RATE * TOTAL_DURATION);
                short[] samples = new short[totalSamples];

                // Generate 8 seconds of continuous dual-tone (853 Hz + 960 Hz)
                for (int i = 0; i < totalSamples; i++)
                {
                    double t = (double)i / SAMPLE_RATE;

                    // Both frequencies combined simultaneously (creates dissonant major second)
                    double value = Math.Sin(2 * Math.PI * EAS_TONE1_FREQ * t) +
                                   Math.Sin(2 * Math.PI * EAS_TONE2_FREQ * t);
                    
                    // Average the two sine waves and apply amplitude
                    value = (value / 2.0) * AMPLITUDE;

                    // Apply short attack/decay envelope at start and end to reduce clicking
                    double envelope = 1.0;
                    int fadeLength = (int)(SAMPLE_RATE * 0.01); // 10ms fade

                    if (i < fadeLength)
                    {
                        // Attack at beginning
                        envelope = (double)i / fadeLength;
                    }
                    else if (i > totalSamples - fadeLength)
                    {
                        // Decay at end
                        envelope = (double)(totalSamples - i) / fadeLength;
                    }

                    value *= envelope;

                    // Convert to 16-bit PCM
                    samples[i] = (short)(value * short.MaxValue);
                }

                // Write WAV file
                WriteWavFile(outputPath, samples, SAMPLE_RATE, BITS_PER_SAMPLE, NUM_CHANNELS);

                Console.WriteLine($"[NwsSameToneGenerator] Generated {TOTAL_DURATION}s EAS SAME continuous dual-tone (853+960 Hz): {outputPath}");
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
        /// Generates a complete SAME alert with header bursts and attention signal.
        /// Format: [SAME Header x3 with 1s gaps] + [1s silence] + [Attention Signal 8s]
        /// </summary>
        /// <param name="outputPath">Path for the output WAV file</param>
        /// <returns>True if generation succeeded</returns>
        public static bool GenerateCompleteSameAlert(string outputPath)
        {
            try
            {
                // Test SAME header: ZCZC-WXR-TOR-012345+0030-0010130-TESTTEST-
                // ZCZC: Preamble
                // WXR: National Weather Service
                // TOR: Tornado Warning
                // 012345: Location code (fake test location)
                // 0030: 30 minutes duration
                // 0010130: Julian day 001, 01:30 UTC
                // TESTTEST: Test station callsign
                string sameMessage = "ZCZC-WXR-TOR-012345+0030-0010130-TESTTEST-";

                var allSamples = new System.Collections.Generic.List<short>();

                // Generate SAME header (transmitted 3 times with 1 second gaps)
                for (int burst = 0; burst < 3; burst++)
                {
                    // Generate preamble (16 bytes of 0xAB = 10101011)
                    for (int i = 0; i < 16; i++)
                    {
                        allSamples.AddRange(GenerateSameByteAFSK(SAME_PREAMBLE));
                    }

                    // Generate message bytes
                    foreach (char c in sameMessage)
                    {
                        allSamples.AddRange(GenerateSameByteAFSK((byte)c));
                    }

                    // Add 1 second silence between bursts (except after the last)
                    if (burst < 2)
                    {
                        int silenceSamples = SAMPLE_RATE; // 1 second
                        for (int i = 0; i < silenceSamples; i++)
                        {
                            allSamples.Add(0);
                        }
                    }
                }

                // Add 1 second silence before attention signal
                int gapSamples = SAMPLE_RATE;
                for (int i = 0; i < gapSamples; i++)
                {
                    allSamples.Add(0);
                }

                // Generate 8 seconds of attention signal (853 Hz + 960 Hz continuous dual-tone)
                int attentionSamples = (int)(SAMPLE_RATE * TOTAL_DURATION);
                for (int i = 0; i < attentionSamples; i++)
                {
                    double t = (double)i / SAMPLE_RATE;

                    // Both frequencies combined simultaneously
                    double value = Math.Sin(2 * Math.PI * EAS_TONE1_FREQ * t) +
                                   Math.Sin(2 * Math.PI * EAS_TONE2_FREQ * t);
                    
                    value = (value / 2.0) * AMPLITUDE;

                    // Apply fade envelope at start and end
                    double envelope = 1.0;
                    int fadeLength = (int)(SAMPLE_RATE * 0.01);

                    if (i < fadeLength)
                    {
                        envelope = (double)i / fadeLength;
                    }
                    else if (i > attentionSamples - fadeLength)
                    {
                        envelope = (double)(attentionSamples - i) / fadeLength;
                    }

                    value *= envelope;
                    allSamples.Add((short)(value * short.MaxValue));
                }

                // Write to WAV file
                WriteWavFile(outputPath, allSamples.ToArray(), SAMPLE_RATE, BITS_PER_SAMPLE, NUM_CHANNELS);

                Console.WriteLine($"[NwsSameToneGenerator] Generated complete SAME alert: {allSamples.Count / SAMPLE_RATE:F1}s total ({sameMessage}): {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NwsSameToneGenerator] Error generating complete SAME alert: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the path to the complete SAME alert, generating it if necessary.
        /// </summary>
        /// <returns>Path to the WAV file with SAME header + attention signal, or null if generation failed</returns>
        public static string? GetOrGenerateCompleteSameAlert()
        {
            string alertPath = CachedCompleteAlertPath;

            // Check if cached alert exists and is valid
            if (File.Exists(alertPath))
            {
                var fileInfo = new FileInfo(alertPath);
                // Should be larger than attention signal alone (includes SAME header bursts)
                if (fileInfo.Length > 200000)
                {
                    return alertPath;
                }
            }

            // Generate the complete alert
            if (GenerateCompleteSameAlert(alertPath))
            {
                return alertPath;
            }

            return null;
        }

        /// <summary>
        /// Generates AFSK audio samples for a single byte using SAME encoding.
        /// Uses mark frequency (2083.3 Hz) for binary 1 and space frequency (1562.5 Hz) for binary 0.
        /// LSB (Least Significant Bit) is transmitted first.
        /// </summary>
        /// <param name="data">The byte to encode</param>
        /// <returns>Array of audio samples representing the byte</returns>
        private static short[] GenerateSameByteAFSK(byte data)
        {
            int samplesPerBit = (int)(SAMPLE_RATE / SAME_BAUD_RATE);
            short[] samples = new short[samplesPerBit * 8]; // 8 bits per byte
            int sampleIndex = 0;

            // Transmit LSB first
            for (int bit = 0; bit < 8; bit++)
            {
                bool bitValue = ((data >> bit) & 1) == 1;
                double frequency = bitValue ? SAME_MARK_FREQ : SAME_SPACE_FREQ;

                // Generate samples for this bit
                for (int i = 0; i < samplesPerBit; i++)
                {
                    double t = (double)i / SAMPLE_RATE;
                    double value = Math.Sin(2 * Math.PI * frequency * t) * AMPLITUDE;
                    samples[sampleIndex++] = (short)(value * short.MaxValue);
                }
            }

            return samples;
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
