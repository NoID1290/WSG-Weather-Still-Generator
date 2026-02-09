using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using WeatherImageGenerator.Models;

namespace EAS.NWS
{
    /// <summary>
    /// NWS (United States / National Weather Service) CAP feed provider.
    /// Implements the <see cref="IAlertProvider"/> interface so it can be swapped with AlertReady.
    /// Supports parsing CAP alerts with SAME (Specific Area Message Encoding) headers.
    /// </summary>
    public class NwsClient : IAlertProvider
    {
        private readonly HttpClient _httpClient;
        private readonly NwsOptions _options;
        private readonly HashSet<string> _seenIdentifiers = new();
        private readonly object _lockObj = new();

        public Action<string>? Log { get; set; }

        public event EventHandler<EAS.AlertReceivedEventArgs>? AlertReceived;

        public NwsClient(HttpClient httpClient, NwsOptions? options = null)
        {
            _httpClient = httpClient;
            _options = options ?? new NwsOptions();
        }

        public async Task<List<AlertEntry>> FetchAlertsAsync(IEnumerable<string>? filterAreas = null)
        {
            var results = new List<AlertEntry>();

            if (!_options.Enabled)
            {
                LogMessage("NWS provider disabled; skipping.");
                return results;
            }

            var feeds = _options.FeedUrls?.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (feeds == null || feeds.Count == 0)
            {
                LogMessage("No NWS feed URLs configured for NwsClient.");
                return results;
            }

            foreach (var feed in feeds)
            {
                try
                {
                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(_options.HttpTimeoutSeconds)))
                    {
                        var xml = await _httpClient.GetStringAsync(feed, cts.Token);
                        var alerts = ParseAlerts(xml, filterAreas);
                        results.AddRange(alerts);

                        if (alerts.Count > 0)
                        {
                            LogMessage($"NWS feed {feed}: fetched {alerts.Count} alerts");
                        }
                    }
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    LogMessage($"NWS feed {feed}: request timed out after {_options.HttpTimeoutSeconds}s");
                }
                catch (HttpRequestException ex)
                {
                    LogMessage($"NWS feed {feed}: HTTP error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    LogMessage($"NWS feed {feed}: failed to parse: {ex.Message}");
                }
            }

            return Deduplicate(results);
        }

        public void StartTcpStreams()
        {
            // NWS typically uses HTTP CAP feeds; no TCP streaming required by default.
        }

        public void Dispose()
        {
            // nothing to dispose yet
        }

        private List<AlertEntry> ParseAlerts(string xml, IEnumerable<string>? filterAreas)
        {
            var results = new List<AlertEntry>();
            if (string.IsNullOrWhiteSpace(xml)) return results;

            var doc = XDocument.Parse(xml);
            var capElements = doc.Root != null ? new[] { doc.Root }.Concat(doc.Root.Descendants()) : Enumerable.Empty<XElement>();
            var now = DateTimeOffset.UtcNow;
            var filters = NormalizeList(filterAreas);

            foreach (var el in capElements.Where(IsAlertElement))
            {
                var alert = ConvertAlert(el, filters, now);
                if (alert != null) results.Add(alert);
            }

            return results;
        }

        private AlertEntry? ConvertAlert(XElement alertElement, List<string> filters, DateTimeOffset now)
        {
            var status = GetValue(alertElement, "status");
            if (!string.Equals(status, "Actual", StringComparison.OrdinalIgnoreCase))
            {
                // Skip Test/Exercise alerts
                return null;
            }

            var scope = GetValue(alertElement, "scope");
            if (!string.Equals(scope, "Public", StringComparison.OrdinalIgnoreCase)) return null;

            var identifier = GetValue(alertElement, "identifier");
            
            // Check if we've already seen this alert to avoid duplicates
            lock (_lockObj)
            {
                if (!string.IsNullOrWhiteSpace(identifier) && !_seenIdentifiers.Add(identifier))
                {
                    return null; // Already processed
                }
            }

            var info = alertElement.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("info", StringComparison.OrdinalIgnoreCase));
            if (info == null) return null;

            var eventName = GetValue(info, "event");
            var headline = GetValue(info, "headline");
            var description = GetValue(info, "description");
            var instruction = GetValue(info, "instruction");
            var severity = GetValue(info, "severity");
            var urgency = GetValue(info, "urgency");
            var certainty = GetValue(info, "certainty");

            // Extract SAME header if present
            var sameHeader = ExtractSameHeader(info, eventName);

            var areaElement = info.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("area", StringComparison.OrdinalIgnoreCase));
            var areaDesc = GetValue(areaElement, "areaDesc");
            if (string.IsNullOrWhiteSpace(areaDesc)) areaDesc = "NWS Alert";

            if (filters.Count > 0 && !filters.Any(f => areaDesc.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)) return null;

            var summaryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(description)) summaryParts.Add(description.Trim());
            if (!string.IsNullOrWhiteSpace(sameHeader)) summaryParts.Add($"[SAME: {sameHeader}]");
            if (!string.IsNullOrWhiteSpace(instruction)) summaryParts.Add(instruction.Trim());
            var summary = summaryParts.Count > 0 ? string.Join("  ", summaryParts) : headline ?? eventName ?? "NWS Alert";

            var alert = new AlertEntry
            {
                City = areaDesc,
                Type = eventName ?? "Alert",
                Title = !string.IsNullOrWhiteSpace(headline) ? headline : eventName ?? "NWS Alert",
                Summary = summary,
                SeverityColor = MapSeverity(severity)
            };

            // Fire the AlertReceived event
            AlertReceived?.Invoke(this, new EAS.AlertReceivedEventArgs { Alert = alert });

            return alert;
        }

        /// <summary>
        /// Extracts the SAME (Specific Area Message Encoding) header from the alert if present.
        /// SAME headers contain coded information about the alert target area and event type.
        /// </summary>
        private string? ExtractSameHeader(XElement info, string? eventName)
        {
            try
            {
                // Look for parameter element with valueName='SAME'
                var sameParam = info.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("parameter", StringComparison.OrdinalIgnoreCase))?
                    .Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("valueName", StringComparison.OrdinalIgnoreCase))?
                    .Value;

                if (string.Equals(sameParam, "SAME", StringComparison.OrdinalIgnoreCase))
                {
                    var parentParam = info.Elements()
                        .FirstOrDefault(e => e.Name.LocalName.Equals("parameter", StringComparison.OrdinalIgnoreCase) &&
                                            e.Elements().Any(c => c.Name.LocalName.Equals("valueName", StringComparison.OrdinalIgnoreCase) && 
                                                                   string.Equals(c.Value, "SAME", StringComparison.OrdinalIgnoreCase)));

                    var valueElement = parentParam?.Elements()
                        .FirstOrDefault(e => e.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase));

                    return valueElement?.Value?.Trim();
                }
            }
            catch
            {
                // Silently ignore parse errors
            }

            return null;
        }

        private static string GetValue(XElement? parent, string localName)
        {
            if (parent == null) return string.Empty;
            var child = parent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
            return child?.Value?.Trim() ?? string.Empty;
        }

        private static bool IsAlertElement(XElement el)
            => el.Name.LocalName.Equals("alert", StringComparison.OrdinalIgnoreCase);

        private static string MapSeverity(string? severity)
        {
            if (string.IsNullOrWhiteSpace(severity)) return "Gray";
            var sev = severity.Trim().ToLowerInvariant();
            return sev switch
            {
                "extreme" or "severe" => "Red",
                "moderate" or "minor" => "Yellow",
                _ => "Gray"
            };
        }

        private static List<string> NormalizeList(IEnumerable<string>? values)
        {
            return values?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim().ToLowerInvariant()).Distinct().ToList() ?? new List<string>();
        }

        private static List<AlertEntry> Deduplicate(IEnumerable<AlertEntry> alerts)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<AlertEntry>();

            foreach (var a in alerts)
            {
                var key = $"{a.City}|{a.Title}|{a.Summary}";
                if (seen.Add(key)) deduped.Add(a);
            }

            return deduped;
        }

        private void LogMessage(string message)
        {
            Log?.Invoke($"[NwsClient] {message}");
        }
    }
}
