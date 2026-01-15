#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Linq;
using WeatherImageGenerator.Models;

namespace EAS
{
    /// <summary>
    /// Settings for the Alert Ready (NAAD) CAP-CP feeds.
    /// </summary>
    public class AlertReadyOptions
    {
        [JsonPropertyName("Enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>List of CAP-CP feed URLs (Atom or raw CAP documents)</summary>
        [JsonPropertyName("FeedUrls")]
        public List<string>? FeedUrls { get; set; }

        [JsonPropertyName("IncludeTests")]
        public bool IncludeTests { get; set; } = false;

        /// <summary>Ignore alerts older than this many hours (0 disables filtering)</summary>
        [JsonPropertyName("MaxAgeHours")]
        public int MaxAgeHours { get; set; } = 24;

        /// <summary>Preferred CAP language code (e.g., en-CA, fr-CA)</summary>
        [JsonPropertyName("PreferredLanguage")]
        public string PreferredLanguage { get; set; } = "en-CA";

        /// <summary>Optional list of area names to keep (case-insensitive substring match)</summary>
        [JsonPropertyName("AreaFilters")]
        public List<string>? AreaFilters { get; set; }
    }

    /// <summary>
    /// Lightweight CAP-CP client for Alert Ready / NAAD public feeds.
    /// Produces the shared AlertEntry model for reuse in the UI.
    /// </summary>
    public class AlertReadyClient
    {
        private readonly HttpClient _httpClient;
        private readonly AlertReadyOptions _options;

        public Action<string>? Log { get; set; }

        public AlertReadyClient(HttpClient httpClient, AlertReadyOptions? options = null)
        {
            _httpClient = httpClient;
            _options = options ?? new AlertReadyOptions();
        }

        /// <summary>
        /// Fetches and parses all configured Alert Ready feeds.
        /// </summary>
        /// <param name="filterAreas">Optional list of location names to keep (matches areaDesc)</param>
        public async Task<List<AlertEntry>> FetchAlertsAsync(IEnumerable<string>? filterAreas = null)
        {
            var alerts = new List<AlertEntry>();

            if (!_options.Enabled)
            {
                LogMessage("Alert Ready disabled; skipping.");
                return alerts;
            }

            var feeds = _options.FeedUrls?
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (feeds == null || feeds.Count == 0)
            {
                LogMessage("No Alert Ready feed URLs configured.");
                return alerts;
            }

            var normalizedFilters = NormalizeList(filterAreas);
            if (_options.AreaFilters?.Count > 0)
            {
                normalizedFilters.AddRange(NormalizeList(_options.AreaFilters));
            }

            foreach (var feed in feeds)
            {
                try
                {
                    var xml = await _httpClient.GetStringAsync(feed);
                    var parsed = ParseAlerts(xml, normalizedFilters);
                    alerts.AddRange(parsed);
                    LogMessage($"Fetched {parsed.Count} alert(s) from {feed}");
                }
                catch (Exception ex)
                {
                    LogMessage($"Failed to fetch {feed}: {ex.Message}");
                }
            }

            return Deduplicate(alerts);
        }

        private List<AlertEntry> ParseAlerts(string xml, List<string> normalizedFilters)
        {
            var results = new List<AlertEntry>();
            var now = DateTimeOffset.UtcNow;
            var doc = XDocument.Parse(xml);

            var capElements = doc.Root != null
                ? new[] { doc.Root }.Concat(doc.Root.Descendants())
                : Enumerable.Empty<XElement>();

            foreach (var alertElement in capElements.Where(IsAlertElement))
            {
                var alert = ConvertAlert(alertElement, normalizedFilters, now);
                if (alert != null)
                {
                    results.Add(alert);
                }
            }

            return results;
        }

        private AlertEntry? ConvertAlert(XElement alertElement, List<string> filters, DateTimeOffset now)
        {
            var status = GetValue(alertElement, "status");
            if (!string.Equals(status, "Actual", StringComparison.OrdinalIgnoreCase))
            {
                if (!_options.IncludeTests && string.Equals(status, "Test", StringComparison.OrdinalIgnoreCase)) return null;
                if (string.Equals(status, "Exercise", StringComparison.OrdinalIgnoreCase)) return null;
            }

            var msgType = GetValue(alertElement, "msgType");
            if (string.Equals(msgType, "Cancel", StringComparison.OrdinalIgnoreCase)) return null;

            var sentStr = GetValue(alertElement, "sent");
            if (_options.MaxAgeHours > 0 && DateTimeOffset.TryParse(sentStr, null, DateTimeStyles.AssumeUniversal, out var sentTime))
            {
                if (now - sentTime > TimeSpan.FromHours(_options.MaxAgeHours)) return null;
            }

            var info = SelectInfo(alertElement);
            if (info == null) return null;

            var eventName = GetValue(info, "event");
            var headline = GetValue(info, "headline");
            var description = GetValue(info, "description");
            var instruction = GetValue(info, "instruction");
            var severity = GetValue(info, "severity");
            var certainty = GetValue(info, "certainty");
            var urgency = GetValue(info, "urgency");

            var areaElement = info.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("area", StringComparison.OrdinalIgnoreCase));
            var areaDesc = GetValue(areaElement, "areaDesc");
            if (string.IsNullOrWhiteSpace(areaDesc))
            {
                var geocode = areaElement?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("geocode", StringComparison.OrdinalIgnoreCase));
                var codeName = GetValue(geocode, "valueName");
                var code = GetValue(geocode, "value");
                areaDesc = !string.IsNullOrWhiteSpace(codeName) ? $"{codeName}:{code}" : code;
            }

            areaDesc = string.IsNullOrWhiteSpace(areaDesc) ? null : areaDesc.Trim();
            if (filters.Count > 0 && !AreaMatches(areaDesc, filters)) return null;

            var summaryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(description)) summaryParts.Add(description.Trim());
            if (!string.IsNullOrWhiteSpace(instruction)) summaryParts.Add(instruction.Trim());
            if (!string.IsNullOrWhiteSpace(certainty)) summaryParts.Add($"Certainty: {certainty}");
            if (!string.IsNullOrWhiteSpace(urgency)) summaryParts.Add($"Urgency: {urgency}");

            var summary = summaryParts.Count > 0 ? string.Join("  ", summaryParts) : "No additional details provided.";

            return new AlertEntry
            {
                City = areaDesc ?? "Alert Ready",
                Type = !string.IsNullOrWhiteSpace(eventName) ? eventName : severity ?? "Alert",
                Title = !string.IsNullOrWhiteSpace(headline) ? headline : eventName ?? "Alert Ready Notification",
                Summary = summary,
                SeverityColor = MapSeverity(severity)
            };
        }

        private XElement? SelectInfo(XElement alertElement)
        {
            var infos = alertElement.Elements().Where(e => e.Name.LocalName.Equals("info", StringComparison.OrdinalIgnoreCase)).ToList();
            if (infos.Count == 0) return null;

            if (!string.IsNullOrWhiteSpace(_options.PreferredLanguage))
            {
                var preferred = infos.FirstOrDefault(i => GetValue(i, "language")
                    .StartsWith(_options.PreferredLanguage, StringComparison.OrdinalIgnoreCase));
                if (preferred != null) return preferred;
            }

            return infos.First();
        }

        private static string GetValue(XElement? parent, string localName)
        {
            if (parent == null) return string.Empty;
            var child = parent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
            return child?.Value?.Trim() ?? string.Empty;
        }

        private static bool IsAlertElement(XElement element)
            => element.Name.LocalName.Equals("alert", StringComparison.OrdinalIgnoreCase);

        private static string MapSeverity(string? severity)
        {
            return severity?.ToLowerInvariant() switch
            {
                "extreme" or "severe" => "Red",
                "moderate" or "minor" => "Yellow",
                _ => "Gray"
            };
        }

        private static bool AreaMatches(string? areaDesc, List<string> filters)
        {
            if (filters.Count == 0) return true;
            if (string.IsNullOrWhiteSpace(areaDesc)) return false;
            var norm = Normalize(areaDesc);
            return filters.Any(f => norm.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> NormalizeList(IEnumerable<string>? values)
        {
            return values?
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }

        private static string Normalize(string value)
        {
            return value.Trim().ToLowerInvariant();
        }

        private static List<AlertEntry> Deduplicate(IEnumerable<AlertEntry> alerts)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<AlertEntry>();

            foreach (var alert in alerts)
            {
                var key = $"{alert.City}|{alert.Title}|{alert.Summary}";
                if (seen.Add(key))
                {
                    deduped.Add(alert);
                }
            }

            return deduped;
        }

        private void LogMessage(string message)
        {
            Log?.Invoke(message);
        }
    }
}
