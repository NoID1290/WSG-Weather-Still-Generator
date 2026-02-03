using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EAS
{
    /// <summary>
    /// Free TTS client using Microsoft Edge's speech synthesis API.
    /// Supports high-quality neural voices including French Canadian.
    /// No API key required.
    /// </summary>
    public class EdgeTtsClient : IDisposable
    {
        private const string TRUSTED_CLIENT_TOKEN = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
        private const string CHROMIUM_FULL_VERSION = "133.0.6943.99";

        // French Canadian voices
        public const string VOICE_FR_CA_SYLVIE = "fr-CA-SylvieNeural";      // Female
        public const string VOICE_FR_CA_JEAN = "fr-CA-JeanNeural";          // Male
        public const string VOICE_FR_CA_ANTOINE = "fr-CA-AntoineNeural";    // Male
        public const string VOICE_FR_CA_THIERRY = "fr-CA-ThierryNeural";    // Male

        // French France voices  
        public const string VOICE_FR_FR_DENISE = "fr-FR-DeniseNeural";      // Female
        public const string VOICE_FR_FR_HENRI = "fr-FR-HenriNeural";        // Male

        // English Canadian voices
        public const string VOICE_EN_CA_CLARA = "en-CA-ClaraNeural";        // Female
        public const string VOICE_EN_CA_LIAM = "en-CA-LiamNeural";          // Male

        // English US voices
        public const string VOICE_EN_US_JENNY = "en-US-JennyNeural";        // Female
        public const string VOICE_EN_US_GUY = "en-US-GuyNeural";            // Male

        private ClientWebSocket? _webSocket;
        private bool _disposed;

        // Windows epoch offset (1601-01-01 to 1970-01-01) in seconds
        private const long WIN_EPOCH = 11644473600;
        private const long S_TO_NS = 10000000; // 100-nanosecond intervals per second

        /// <summary>
        /// Generate SEC-MS-GEC token (required by Edge TTS)
        /// Based on: https://github.com/rany2/edge-tts/blob/master/src/edge_tts/drm.py
        /// </summary>
        private static string GenerateSecMsGec()
        {
            // Get current Unix timestamp
            double unixTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

            // Convert to Windows file time epoch (add offset for 1601-01-01)
            double ticks = unixTime + WIN_EPOCH;

            // Round down to nearest 5 minutes (300 seconds)
            ticks -= ticks % 300;

            // Convert to 100-nanosecond intervals (Windows file time format)
            ticks *= S_TO_NS;

            // Create hash string: ticks + token (no space!)
            string strToHash = $"{ticks:F0}{TRUSTED_CLIENT_TOKEN}";

            // Compute SHA256 hash
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(strToHash));
            return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
        }

        private static string GetWssUrl()
        {
            string secMsGec = GenerateSecMsGec();
            string secMsGecVersion = $"1-{CHROMIUM_FULL_VERSION}";
            return $"wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken={TRUSTED_CLIENT_TOKEN}&Sec-MS-GEC={secMsGec}&Sec-MS-GEC-Version={secMsGecVersion}";
        }

        /// <summary>
        /// Synthesize text to audio file using Microsoft Edge TTS.
        /// </summary>
        public async Task<bool> SynthesizeToFileAsync(string text, string outputPath, string voice = VOICE_FR_CA_SYLVIE, string rate = "+0%", string pitch = "+0Hz")
        {
            try
            {
                var audioData = await SynthesizeAsync(text, voice, rate, pitch);
                if (audioData != null && audioData.Length > 0)
                {
                    await File.WriteAllBytesAsync(outputPath, audioData);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EdgeTTS] Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Synthesize text to audio bytes.
        /// </summary>
        public async Task<byte[]?> SynthesizeAsync(string text, string voice = VOICE_FR_CA_SYLVIE, string rate = "+0%", string pitch = "+0Hz")
        {
            try
            {
                _webSocket = new ClientWebSocket();
                _webSocket.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br, zstd");
                _webSocket.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
                _webSocket.Options.SetRequestHeader("Cache-Control", "no-cache");
                _webSocket.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
                _webSocket.Options.SetRequestHeader("Pragma", "no-cache");
                _webSocket.Options.SetRequestHeader("User-Agent", $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{CHROMIUM_FULL_VERSION} Safari/537.36 Edg/{CHROMIUM_FULL_VERSION}");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                string wssUrl = GetWssUrl();
                await _webSocket.ConnectAsync(new Uri(wssUrl), cts.Token);

                // Send configuration
                string requestId = Guid.NewGuid().ToString("N");
                string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                // Speech config message
                string configMessage = $"X-Timestamp:{timestamp}\r\n" +
                    "Content-Type:application/json; charset=utf-8\r\n" +
                    "Path:speech.config\r\n\r\n" +
                    "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":false,\"wordBoundaryEnabled\":false},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";

                await SendTextMessageAsync(configMessage, cts.Token);

                // SSML message
                string ssml = $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{GetLanguageFromVoice(voice)}'>" +
                    $"<voice name='{voice}'>" +
                    $"<prosody rate='{rate}' pitch='{pitch}'>" +
                    EscapeXml(text) +
                    "</prosody></voice></speak>";

                string ssmlMessage = $"X-RequestId:{requestId}\r\n" +
                    "Content-Type:application/ssml+xml\r\n" +
                    $"X-Timestamp:{timestamp}\r\n" +
                    "Path:ssml\r\n\r\n" + ssml;

                await SendTextMessageAsync(ssmlMessage, cts.Token);

                // Receive audio data
                var audioChunks = new List<byte[]>();
                var buffer = new byte[8192];
                bool turnEnd = false;

                while (!turnEnd && _webSocket.State == WebSocketState.Open)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        if (message.Contains("Path:turn.end"))
                            turnEnd = true;
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        // Binary messages contain audio data after a header
                        // Header format: 2 bytes length + "Path:audio\r\n" header
                        int headerLength = (buffer[0] << 8) | buffer[1];
                        if (result.Count > headerLength + 2)
                        {
                            int audioStart = headerLength + 2;
                            int audioLength = result.Count - audioStart;
                            var audioChunk = new byte[audioLength];
                            Array.Copy(buffer, audioStart, audioChunk, 0, audioLength);
                            audioChunks.Add(audioChunk);
                        }
                    }
                }

                // Combine all audio chunks
                int totalLength = 0;
                foreach (var chunk in audioChunks)
                    totalLength += chunk.Length;

                var audioData = new byte[totalLength];
                int offset = 0;
                foreach (var chunk in audioChunks)
                {
                    Array.Copy(chunk, 0, audioData, offset, chunk.Length);
                    offset += chunk.Length;
                }

                return audioData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EdgeTTS] Synthesis error: {ex.Message}");
                return null;
            }
            finally
            {
                if (_webSocket != null)
                {
                    try
                    {
                        if (_webSocket.State == WebSocketState.Open)
                            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }
                    catch { }
                    _webSocket.Dispose();
                    _webSocket = null;
                }
            }
        }

        private async Task SendTextMessageAsync(string message, CancellationToken cancellationToken)
        {
            if (_webSocket == null) return;
            var bytes = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }

        private static string GetLanguageFromVoice(string voice)
        {
            if (voice.StartsWith("fr-CA")) return "fr-CA";
            if (voice.StartsWith("fr-FR")) return "fr-FR";
            if (voice.StartsWith("en-CA")) return "en-CA";
            if (voice.StartsWith("en-US")) return "en-US";
            if (voice.StartsWith("en-GB")) return "en-GB";
            return "en-US";
        }

        private static string EscapeXml(string text)
        {
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        /// <summary>
        /// Get the recommended voice for a language code.
        /// </summary>
        public static string GetVoiceForLanguage(string language)
        {
            return language?.ToLowerInvariant() switch
            {
                "fr-ca" => VOICE_FR_CA_SYLVIE,
                "fr-fr" => VOICE_FR_FR_DENISE,
                "fr" => VOICE_FR_CA_SYLVIE,
                "en-ca" => VOICE_EN_CA_CLARA,
                "en-us" => VOICE_EN_US_JENNY,
                "en-gb" => "en-GB-SoniaNeural",
                _ => VOICE_EN_CA_CLARA
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _webSocket?.Dispose();
        }
    }
}
