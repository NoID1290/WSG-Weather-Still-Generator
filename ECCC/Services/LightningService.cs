#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using ECCC.Api;
using ECCC.Models;

namespace ECCC.Services
{
    /// <summary>
    /// Fetches real-time and historical lightning flash data from the ECCC OGC API
    /// (api.weather.gc.ca/collections) and WMS GetCapabilities (geo.weather.gc.ca/geomet).
    /// </summary>
    public class LightningService
    {
        private readonly HttpClient _httpClient;

        // Cached collection ID — discovered once per process lifetime
        private static string? _cachedCollectionId;

        public LightningService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        // ─────────────────────────────────────────────────────────────────
        // Collection discovery
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the OGC API collection ID for lightning flashes.
        /// Uses the known expected ID first; falls back to probing /collections if needed.
        /// Result is cached for the lifetime of the process.
        /// </summary>
        public async Task<string?> DiscoverLightningCollectionAsync()
        {
            if (_cachedCollectionId != null)
                return _cachedCollectionId;

            // Try the expected ID first
            try
            {
                var testUrl = $"{UrlBuilder.BaseApiUrl}/collections/{UrlBuilder.ExpectedLightningCollectionId}?f=json";
                var resp = await _httpClient.GetAsync(testUrl).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    _cachedCollectionId = UrlBuilder.ExpectedLightningCollectionId;
                    Console.WriteLine($"[LightningService] Using collection '{_cachedCollectionId}'");
                    return _cachedCollectionId;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LightningService] Expected collection probe failed: {ex.Message}");
            }

            // Probe /collections and find any entry whose ID or title contains "lightning"
            try
            {
                var url = UrlBuilder.BuildLightningCollectionsUrl();
                var json = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("collections", out var collections))
                {
                    foreach (var col in collections.EnumerateArray())
                    {
                        var id    = col.TryGetProperty("id",    out var idProp)    ? idProp.GetString()    ?? "" : "";
                        var title = col.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "";

                        if (id.Contains("lightning", StringComparison.OrdinalIgnoreCase)
                            || title.Contains("lightning", StringComparison.OrdinalIgnoreCase))
                        {
                            _cachedCollectionId = id;
                            Console.WriteLine($"[LightningService] Discovered lightning collection: '{id}' ({title})");
                            return _cachedCollectionId;
                        }
                    }
                }

                Console.WriteLine("[LightningService] No lightning collection found in /collections");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LightningService] Collection discovery failed: {ex.Message}");
            }

            return null;
        }

        // ─────────────────────────────────────────────────────────────────
        // Strike fetching
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fetches lightning flash features from ECCC OGC API for the given bounding box
        /// and time window. Returns an empty list if the collection is unavailable.
        /// </summary>
        /// <param name="bbox">Geographic bounding box (minLat, minLon, maxLat, maxLon)</param>
        /// <param name="from">Start of time window (UTC)</param>
        /// <param name="to">End of time window (UTC)</param>
        /// <param name="limit">Maximum features to return (ECCC caps at ~10 000)</param>
        public async Task<List<LightningFlash>> FetchLightningStrikesAsync(
            (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox,
            DateTime from,
            DateTime to,
            int limit = 5000)
        {
            var collectionId = await DiscoverLightningCollectionAsync().ConfigureAwait(false);
            if (collectionId == null)
            {
                Console.WriteLine("[LightningService] Cannot fetch strikes: no collection found.");
                return new List<LightningFlash>();
            }

            // OGC Features bbox is (minLon, minLat, maxLon, maxLat)
            var ogcBbox = (bbox.MinLon, bbox.MinLat, bbox.MaxLon, bbox.MaxLat);
            var url = UrlBuilder.BuildLightningStrikesUrl(collectionId, ogcBbox, from, to, limit);

            Console.WriteLine($"[LightningService] Fetching strikes: {from:u} → {to:u}");

            try
            {
                var json = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
                var flashes = ParseGeoJsonFeatures(json);
                Console.WriteLine($"[LightningService] Parsed {flashes.Count} lightning flashes");
                return flashes;
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode is 404 or 400)
            {
                // Collection exists but time range or bbox has no data
                Console.WriteLine($"[LightningService] No data returned (HTTP {ex.StatusCode}): {ex.Message}");
                return new List<LightningFlash>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LightningService] Error fetching lightning strikes: {ex.Message}");
                return new List<LightningFlash>();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Timestamp discovery (WMS-based)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Queries WMS GetCapabilities for the lightning density layer and returns
        /// available UTC timestamps in chronological order.
        /// Falls back to generating synthetic timestamps (every 5 minutes, last N frames) on failure.
        /// </summary>
        /// <param name="numFrames">Maximum number of timestamps to return</param>
        public async Task<List<DateTime>> FetchLightningTimestampsAsync(int numFrames = 12)
        {
            try
            {
                var capsUrl = UrlBuilder.BuildLightningCapabilitiesUrl();
                Console.WriteLine($"[LightningService] Fetching lightning timestamps...");
                var xml = await _httpClient.GetStringAsync(capsUrl).ConfigureAwait(false);
                var doc = XDocument.Parse(xml);
                var ns  = doc.Root?.GetDefaultNamespace();

                if (ns == null)
                    return GenerateFallbackTimestamps(numFrames);

                var dim = doc.Descendants(ns + "Dimension")
                             .FirstOrDefault(d => (string?)d.Attribute("name") == "time");

                if (dim != null)
                {
                    var content = dim.Value.Trim();
                    if (content.Contains('/') && content.Contains("PT"))
                    {
                        var parts = content.Split('/');
                        if (parts.Length >= 3 &&
                            DateTime.TryParse(parts[0], null, DateTimeStyles.RoundtripKind, out var start) &&
                            DateTime.TryParse(parts[1], null, DateTimeStyles.RoundtripKind, out var end))
                        {
                            var step = ParseIso8601Period(parts[2]);
                            if (step.TotalSeconds > 0)
                            {
                                var times = new List<DateTime>();
                                var t = end.ToUniversalTime();
                                for (int i = 0; i < numFrames; i++)
                                {
                                    times.Add(t);
                                    t = t.Subtract(step);
                                    if (t < start) break;
                                }
                                times.Reverse();
                                Console.WriteLine($"[LightningService] Found {times.Count} lightning timestamps");
                                return times;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LightningService] Failed to fetch lightning timestamps: {ex.Message}");
            }

            return GenerateFallbackTimestamps(numFrames);
        }

        // ─────────────────────────────────────────────────────────────────
        // GeoJSON parsing
        // ─────────────────────────────────────────────────────────────────

        private static List<LightningFlash> ParseGeoJsonFeatures(string json)
        {
            var result = new List<LightningFlash>();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var features))
                return result;

            foreach (var feature in features.EnumerateArray())
            {
                try
                {
                    var flash = ParseFeature(feature);
                    if (flash != null)
                        result.Add(flash);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LightningService] Skipping malformed feature: {ex.Message}");
                }
            }

            return result;
        }

        private static LightningFlash? ParseFeature(JsonElement feature)
        {
            // Geometry: {"type":"Point","coordinates":[lon,lat]}
            if (!feature.TryGetProperty("geometry", out var geom)) return null;
            if (!geom.TryGetProperty("coordinates", out var coords)) return null;

            if (coords.ValueKind != JsonValueKind.Array || coords.GetArrayLength() < 2) return null;
            double lon = coords[0].GetDouble();
            double lat = coords[1].GetDouble();

            // Properties
            var props = feature.TryGetProperty("properties", out var p) ? p : default;

            // Time — try common ECCC property names
            DateTime time = DateTime.UtcNow;
            foreach (var timeProp in new[] { "datetime", "time", "date_heure", "flash_time" })
            {
                if (props.ValueKind == JsonValueKind.Object
                    && props.TryGetProperty(timeProp, out var tp)
                    && tp.ValueKind == JsonValueKind.String)
                {
                    var str = tp.GetString();
                    if (str != null && DateTime.TryParse(str, null, DateTimeStyles.RoundtripKind, out var parsed))
                    {
                        time = parsed.ToUniversalTime();
                        break;
                    }
                }
            }

            // Strike type — ECCC typically uses "type" or "flash_type": "CG" / "IC"
            var strikeType = LightningStrikeType.Unknown;
            foreach (var typeProp in new[] { "type", "flash_type", "discharge_type" })
            {
                if (props.ValueKind == JsonValueKind.Object
                    && props.TryGetProperty(typeProp, out var tp)
                    && tp.ValueKind == JsonValueKind.String)
                {
                    var val = tp.GetString()?.ToUpperInvariant();
                    if (val == "CG" || val == "CLOUD_TO_GROUND") { strikeType = LightningStrikeType.CloudToGround; break; }
                    if (val == "IC" || val == "IN_CLOUD" || val == "CC")  { strikeType = LightningStrikeType.InCloud;     break; }
                }
            }

            // Peak current (optional)
            double? peakCurrent = null;
            foreach (var curProp in new[] { "peak_current", "current_kA", "amplitude" })
            {
                if (props.ValueKind == JsonValueKind.Object
                    && props.TryGetProperty(curProp, out var cp)
                    && (cp.ValueKind == JsonValueKind.Number))
                {
                    peakCurrent = cp.GetDouble();
                    break;
                }
            }

            // Feature ID
            string? featureId = null;
            if (feature.TryGetProperty("id", out var idProp))
                featureId = idProp.GetString() ?? idProp.GetRawText();

            return new LightningFlash
            {
                Latitude     = lat,
                Longitude    = lon,
                Time         = time,
                StrikeType   = strikeType,
                PeakCurrentKa = peakCurrent,
                FeatureId    = featureId
            };
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────

        private static List<DateTime> GenerateFallbackTimestamps(int numFrames, int stepMinutes = 5)
        {
            var now = DateTime.UtcNow;
            // Round down to the nearest step
            var minutes = (now.Minute / stepMinutes) * stepMinutes;
            var latest  = new DateTime(now.Year, now.Month, now.Day, now.Hour, minutes, 0, DateTimeKind.Utc);
            var step    = TimeSpan.FromMinutes(stepMinutes);
            var times   = new List<DateTime>();
            for (int i = 0; i < numFrames; i++)
                times.Add(latest.Subtract(TimeSpan.FromMinutes((numFrames - 1 - i) * stepMinutes)));
            return times;
        }

        /// <summary>
        /// Parses an ISO 8601 duration string (e.g., "PT5M", "PT6M", "PT1H") into a TimeSpan.
        /// </summary>
        internal static TimeSpan ParseIso8601Period(string period)
        {
            if (string.IsNullOrWhiteSpace(period)) return TimeSpan.Zero;

            double hours   = 0, minutes = 0;
            int i = 0;
            bool inTime = false;

            while (i < period.Length)
            {
                char c = period[i];
                if (c == 'P') { i++; continue; }
                if (c == 'T') { inTime = true; i++; continue; }

                int start = i;
                while (i < period.Length && (char.IsDigit(period[i]) || period[i] == '.')) i++;
                if (i == start) { i++; continue; }

                if (!double.TryParse(period.Substring(start, i - start),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out double val)) continue;

                if (i < period.Length)
                {
                    char unit = period[i++];
                    if (inTime)
                    {
                        if (unit == 'H') hours   = val;
                        if (unit == 'M') minutes = val;
                    }
                }
            }

            return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
        }
    }
}
