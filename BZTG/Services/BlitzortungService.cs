#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WeatherShared;

namespace BZTG.Services
{
    /// <summary>
    /// Fetches real-time lightning strike data from Blitzortung websocket servers.
    /// Keeps a rolling in-memory cache and returns filtered snapshots for the caller.
    /// </summary>
    public class BlitzortungService
    {
        private static readonly object _cacheLock = new();
        private static readonly List<LightningFlash> _rollingCache = new();
        private static readonly Random _random = new();
        private static readonly string[] _wsServers =
        {
            "ws1.blitzortung.org",
            "ws2.blitzortung.org",
            "ws7.blitzortung.org",
            "ws8.blitzortung.org"
        };
        private static Task? _listenerTask;
        private static CancellationTokenSource? _listenerCts;

        private const int HandshakeKey = 111;
        private const int MaxCacheMinutes = 120;

        public BlitzortungService(HttpClient httpClient)
        {
            _ = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        // ─────────────────────────────────────────────────────────────────
        // Public fetch entry point
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fetches lightning strikes within the given bounding box and time window
        /// from the rolling websocket-backed cache.
        /// </summary>
        public async Task<List<LightningFlash>> FetchLightningStrikesAsync(
            (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox,
            DateTime from,
            DateTime to,
            int limit = 5000)
        {
            EnsureListenerStarted();

            // Give the listener a brief warm-up on first call.
            if (GetCacheCount() == 0)
                await Task.Delay(1200).ConfigureAwait(false);

            var fromUtc = from.ToUniversalTime();
            var toUtc = to.ToUniversalTime();
            var snapshot = SnapshotCache();
            var filtered = new List<LightningFlash>(snapshot.Count);

            foreach (var f in snapshot)
            {
                if (f.Time < fromUtc || f.Time > toUtc) continue;
                if (f.Latitude < bbox.MinLat || f.Latitude > bbox.MaxLat) continue;
                if (f.Longitude < bbox.MinLon || f.Longitude > bbox.MaxLon) continue;
                filtered.Add(f);
            }

            filtered.Sort((a, b) => b.Time.CompareTo(a.Time));
            if (filtered.Count > limit)
                filtered.RemoveRange(limit, filtered.Count - limit);

            Console.WriteLine($"[BZTG] {filtered.Count} strikes in viewport from websocket cache ({snapshot.Count} cached)");
            return filtered;
        }

        // ─────────────────────────────────────────────────────────────────
        // WebSocket listener and parsing
        // ─────────────────────────────────────────────────────────────────

        private static void EnsureListenerStarted()
        {
            lock (_cacheLock)
            {
                if (_listenerTask != null && !_listenerTask.IsCompleted)
                    return;

                _listenerCts?.Cancel();
                _listenerCts = new CancellationTokenSource();
                _listenerTask = Task.Run(() => RunWebSocketLoopAsync(_listenerCts.Token));
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────

        private static async Task RunWebSocketLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var server = _wsServers[_random.Next(_wsServers.Length)];
                try
                {
                    await ConnectAndReceiveAsync(server, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BZTG] websocket error on {server}: {ex.Message}");
                }

                try
                {
                    await Task.Delay(1500, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private static async Task ConnectAndReceiveAsync(string server, CancellationToken ct)
        {
            using var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

            var uri = new Uri($"wss://{server}");
            await ws.ConnectAsync(uri, ct).ConfigureAwait(false);
            Console.WriteLine($"[BZTG] websocket connected: {server}");

            var hello = $"{{\"a\":{HandshakeKey}}}";
            var helloBytes = System.Text.Encoding.UTF8.GetBytes(hello);
            await ws.SendAsync(new ArraySegment<byte>(helloBytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

            var buffer = new byte[8192];
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var chunk = new ArraySegment<byte>(buffer);
                using var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(chunk, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        try
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None).ConfigureAwait(false);
                        }
                        catch
                        {
                        }
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (ms.Length == 0) continue;
                var payload = System.Text.Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                ProcessEncodedMessage(payload);
            }
        }

        private static void ProcessEncodedMessage(string encoded)
        {
            string decoded;
            try
            {
                decoded = DecodeLzw(encoded);
            }
            catch
            {
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(decoded);
                var root = doc.RootElement;

                if (!root.TryGetProperty("lat", out var latProp) || latProp.ValueKind != JsonValueKind.Number)
                    return;
                if (!root.TryGetProperty("lon", out var lonProp) || lonProp.ValueKind != JsonValueKind.Number)
                    return;
                if (!root.TryGetProperty("time", out var timeProp) || timeProp.ValueKind != JsonValueKind.Number)
                    return;

                double lat = latProp.GetDouble();
                double lon = lonProp.GetDouble();
                if (root.TryGetProperty("latc", out var latcProp) && latcProp.ValueKind == JsonValueKind.Number)
                    lat += latcProp.GetDouble();
                if (root.TryGetProperty("lonc", out var loncProp) && loncProp.ValueKind == JsonValueKind.Number)
                    lon += loncProp.GetDouble();

                var eventTime = ParseUnixFlexible(timeProp);
                int? multiplicity = null;
                LightningStrikeType strikeType = LightningStrikeType.Unknown;
                if (root.TryGetProperty("mds", out var mdsProp) && mdsProp.ValueKind == JsonValueKind.Number)
                {
                    if (mdsProp.TryGetInt32(out var mds))
                        multiplicity = mds;
                }

                if (root.TryGetProperty("mcg", out var mcgProp) && mcgProp.ValueKind == JsonValueKind.Number)
                {
                    if (mcgProp.TryGetInt32(out var mcg))
                        strikeType = mcg > 0 ? LightningStrikeType.CloudToGround : LightningStrikeType.InCloud;
                }

                var flash = new LightningFlash
                {
                    Latitude = lat,
                    Longitude = lon,
                    Time = eventTime,
                    StrikeType = strikeType,
                    Multiplicity = multiplicity
                };

                lock (_cacheLock)
                {
                    if (!ExistsNearDuplicate_NoLock(flash))
                        _rollingCache.Add(flash);
                    PruneOld_NoLock();
                }
            }
            catch
            {
            }
        }

        private static DateTime ParseUnixFlexible(JsonElement value)
        {
            double n;
            if (value.TryGetInt64(out var i64))
                n = i64;
            else
                n = value.GetDouble();

            // Accept seconds, milliseconds, microseconds, or nanoseconds.
            // Current websocket payloads use nanoseconds since Unix epoch.
            if (n > 1_000_000_000_000_000_000d)
                return DateTimeOffset.FromUnixTimeMilliseconds((long)(n / 1_000_000d)).UtcDateTime;
            if (n > 100_000_000_000_000d)
                return DateTimeOffset.FromUnixTimeMilliseconds((long)(n / 1000d)).UtcDateTime;
            if (n > 100_000_000_000d)
                return DateTimeOffset.FromUnixTimeMilliseconds((long)n).UtcDateTime;
            return DateTimeOffset.FromUnixTimeSeconds((long)n).UtcDateTime;
        }

        private static bool ExistsNearDuplicate_NoLock(LightningFlash flash)
        {
            var start = _rollingCache.Count > 500 ? _rollingCache.Count - 500 : 0;
            for (int i = _rollingCache.Count - 1; i >= start; i--)
            {
                var x = _rollingCache[i];
                if (Math.Abs((x.Time - flash.Time).TotalMilliseconds) > 250) continue;
                if (Math.Abs(x.Latitude - flash.Latitude) > 0.0002) continue;
                if (Math.Abs(x.Longitude - flash.Longitude) > 0.0002) continue;
                return true;
            }
            return false;
        }

        private static void PruneOld_NoLock()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-MaxCacheMinutes);
            _rollingCache.RemoveAll(x => x.Time < cutoff);
            if (_rollingCache.Count > 50000)
                _rollingCache.RemoveRange(0, _rollingCache.Count - 50000);
        }

        private static List<LightningFlash> SnapshotCache()
        {
            lock (_cacheLock)
            {
                PruneOld_NoLock();
                return new List<LightningFlash>(_rollingCache);
            }
        }

        private static int GetCacheCount()
        {
            lock (_cacheLock)
                return _rollingCache.Count;
        }

        // Port of Blitzortung live map JS `decode()`.
        private static string DecodeLzw(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var dict = new Dictionary<int, string>();
            var chars = input.ToCharArray();
            var c = chars[0].ToString();
            var f = c;
            var output = new List<string> { c };
            var nextCode = 256;

            for (int i = 1; i < chars.Length; i++)
            {
                int code = chars[i];
                string a;
                if (code < 256)
                {
                    a = chars[i].ToString();
                }
                else if (dict.TryGetValue(code, out var entry))
                {
                    a = entry;
                }
                else
                {
                    a = f + c;
                }

                output.Add(a);
                c = a.Substring(0, 1);
                dict[nextCode] = f + c;
                nextCode++;
                f = a;
            }

            return string.Concat(output);
        }
    }
}
