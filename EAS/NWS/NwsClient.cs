using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using WeatherImageGenerator.Models;

namespace EAS.NWS
{
    /// <summary>
    /// NWS (United States / National Weather Service) alert provider.
    /// Implements the <see cref="IAlertProvider"/> interface so it can be swapped with AlertReady.
    /// Supports the modern api.weather.gov JSON API as well as legacy CAP/Atom XML feeds.
    /// Parses SAME (Specific Area Message Encoding) headers when present.
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

        /// <summary>
        /// Fetches active NWS alerts from the modern api.weather.gov API and/or legacy CAP feeds.
        /// </summary>
        public async Task<List<AlertEntry>> FetchAlertsAsync(IEnumerable<string>? filterAreas = null)
        {
            var results = new List<AlertEntry>();

            if (!_options.Enabled)
            {
                LogMessage("NWS provider disabled; skipping.");
                return results;
            }

            var filters = NormalizeList(filterAreas);

            // 1. Try the modern api.weather.gov JSON API first
            if (!string.IsNullOrWhiteSpace(_options.ApiBaseUrl))
            {
                var apiAlerts = await FetchFromApiAsync(filters);
                results.AddRange(apiAlerts);
            }

            // 2. Fallback / additional: legacy CAP XML feeds
            var feeds = _options.FeedUrls?.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (feeds != null && feeds.Count > 0)
            {
                foreach (var feed in feeds)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.HttpTimeoutSeconds));
                        var xml = await _httpClient.GetStringAsync(feed, cts.Token);
                        var alerts = ParseCapAlerts(xml, filters);
                        results.AddRange(alerts);

                        if (alerts.Count > 0)
                            LogMessage($"CAP feed {feed}: fetched {alerts.Count} alerts");
                    }
                    catch (TaskCanceledException)
                    {
                        LogMessage($"CAP feed {feed}: timed out after {_options.HttpTimeoutSeconds}s");
                    }
                    catch (HttpRequestException ex)
                    {
                        LogMessage($"CAP feed {feed}: HTTP error: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"CAP feed {feed}: parse error: {ex.Message}");
                    }
                }
            }

            var deduped = Deduplicate(results);
            if (deduped.Count > 0)
                LogMessage($"Total: {deduped.Count} active NWS alert(s) after deduplication.");
            return deduped;
        }

        public void StartTcpStreams()
        {
            // NWS uses HTTP polling; no TCP streaming.
        }

        public void Dispose()
        {
            // nothing to dispose
        }

        // ─── Modern api.weather.gov JSON API ───────────────────────────────

        /// <summary>
        /// Fetches active alerts from api.weather.gov/alerts/active.
        /// Constructs query parameters from configured States, Zones, and Point filters.
        /// </summary>
        private async Task<List<AlertEntry>> FetchFromApiAsync(List<string> filters)
        {
            var results = new List<AlertEntry>();
            var urls = BuildApiUrls();

            foreach (var url in urls)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.HttpTimeoutSeconds));
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd(_options.UserAgent);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/geo+json"));

                    using var response = await _httpClient.SendAsync(request, cts.Token);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync(cts.Token);
                    var parsed = ParseApiJson(json, filters);
                    results.AddRange(parsed);

                    LogMessage($"API {url}: fetched {parsed.Count} alert(s)");
                }
                catch (TaskCanceledException)
                {
                    LogMessage($"API {url}: timed out after {_options.HttpTimeoutSeconds}s");
                }
                catch (HttpRequestException ex)
                {
                    LogMessage($"API {url}: HTTP error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    LogMessage($"API {url}: error: {ex.Message}");
                }
            }

            return results;
        }

        /// <summary>
        /// Builds one or more api.weather.gov URLs from configuration.
        /// </summary>
        private List<string> BuildApiUrls()
        {
            var urls = new List<string>();
            var baseUrl = _options.ApiBaseUrl.TrimEnd('/');

            // Point-based query (highest specificity)
            if (!string.IsNullOrWhiteSpace(_options.Point))
            {
                urls.Add($"{baseUrl}/alerts/active?status=actual&point={Uri.EscapeDataString(_options.Point.Trim())}");
                return urls;
            }

            // Zone-based queries
            if (_options.Zones?.Any(z => !string.IsNullOrWhiteSpace(z)) == true)
            {
                var zones = string.Join(",", _options.Zones.Where(z => !string.IsNullOrWhiteSpace(z)).Select(z => z.Trim().ToUpperInvariant()));
                urls.Add($"{baseUrl}/alerts/active?status=actual&zone={Uri.EscapeDataString(zones)}");
                return urls;
            }

            // State-based queries
            if (_options.States?.Any(s => !string.IsNullOrWhiteSpace(s)) == true)
            {
                var areas = string.Join(",", _options.States.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()));
                urls.Add($"{baseUrl}/alerts/active?status=actual&area={Uri.EscapeDataString(areas)}");
                return urls;
            }

            // Fallback: all active alerts
            urls.Add($"{baseUrl}/alerts/active?status=actual");
            return urls;
        }

        /// <summary>
        /// Parses the GeoJSON response from api.weather.gov/alerts/active.
        /// </summary>
        private List<AlertEntry> ParseApiJson(string json, List<string> filters)
        {
            var results = new List<AlertEntry>();
            if (string.IsNullOrWhiteSpace(json)) return results;

            var now = DateTimeOffset.UtcNow;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("properties", out var props))
                    continue;

                var alert = ConvertApiAlert(props, filters, now);
                if (alert != null)
                    results.Add(alert);
            }

            return results;
        }

        /// <summary>
        /// Converts a single GeoJSON feature properties object to an AlertEntry.
        /// </summary>
        private AlertEntry? ConvertApiAlert(JsonElement props, List<string> filters, DateTimeOffset now)
        {
            var status = GetJsonString(props, "status");
            if (!string.Equals(status, "Actual", StringComparison.OrdinalIgnoreCase))
                return null;

            var messageType = GetJsonString(props, "messageType");
            if (string.Equals(messageType, "Cancel", StringComparison.OrdinalIgnoreCase))
                return null;

            var id = GetJsonString(props, "id");
            lock (_lockObj)
            {
                if (!string.IsNullOrWhiteSpace(id) && !_seenIdentifiers.Add(id))
                    return null;
            }

            // Check expiry
            var expiresStr = GetJsonString(props, "expires");
            if (!string.IsNullOrWhiteSpace(expiresStr) && DateTimeOffset.TryParse(expiresStr, null, DateTimeStyles.AssumeUniversal, out var expiresAt))
            {
                if (expiresAt < now) return null;
            }

            // Check age
            var sentStr = GetJsonString(props, "sent");
            if (_options.MaxAgeHours > 0 && DateTimeOffset.TryParse(sentStr, null, DateTimeStyles.AssumeUniversal, out var sentTime))
            {
                if (now - sentTime > TimeSpan.FromHours(_options.MaxAgeHours)) return null;
            }

            var severity = GetJsonString(props, "severity");
            var urgency = GetJsonString(props, "urgency");
            var certainty = GetJsonString(props, "certainty");

            // Severity filter
            if (_options.SeverityFilter?.Any() == true)
            {
                if (!_options.SeverityFilter.Any(s => string.Equals(s, severity, StringComparison.OrdinalIgnoreCase)))
                    return null;
            }

            // High-risk only filter
            if (_options.HighRiskOnly && !IsHighRisk(severity, urgency, certainty))
                return null;

            var eventName = GetJsonString(props, "event");
            var headline = GetJsonString(props, "headline");
            var description = GetJsonString(props, "description");
            var instruction = GetJsonString(props, "instruction");
            var senderName = GetJsonString(props, "senderName");
            var areaDesc = GetJsonString(props, "areaDesc");
            var detailUrl = GetJsonString(props, "id"); // The @id field is the detail URL

            if (string.IsNullOrWhiteSpace(areaDesc)) areaDesc = "NWS Alert";

            // Area text filter
            if (filters.Count > 0 && !filters.Any(f => areaDesc.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                return null;

            // Extract SAME codes from parameters if present
            string? sameHeader = null;
            if (props.TryGetProperty("parameters", out var parameters))
            {
                sameHeader = ExtractSameFromParameters(parameters);
            }

            var summaryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(description)) summaryParts.Add(description.Trim());
            if (!string.IsNullOrWhiteSpace(sameHeader)) summaryParts.Add($"[SAME: {sameHeader}]");
            if (!string.IsNullOrWhiteSpace(instruction)) summaryParts.Add(instruction.Trim());
            var summary = summaryParts.Count > 0 ? string.Join("  ", summaryParts) : headline ?? eventName ?? "NWS Alert";

            DateTimeOffset? issuedAt = null;
            if (!string.IsNullOrWhiteSpace(sentStr) && DateTimeOffset.TryParse(sentStr, null, DateTimeStyles.AssumeUniversal, out var issuedParsed))
                issuedAt = issuedParsed;

            DateTimeOffset? expiresAtFinal = null;
            if (!string.IsNullOrWhiteSpace(expiresStr) && DateTimeOffset.TryParse(expiresStr, null, DateTimeStyles.AssumeUniversal, out var expParsed))
                expiresAtFinal = expParsed;

            var alert = new AlertEntry
            {
                City = areaDesc,
                Type = eventName ?? "Alert",
                Title = !string.IsNullOrWhiteSpace(headline) ? headline : eventName ?? "NWS Alert",
                Summary = summary,
                SeverityColor = MapSeverity(severity),
                Provider = "USA_NWS",
                IssuedAt = issuedAt,
                ExpiresAt = expiresAtFinal,
                Description = description,
                Instructions = instruction,
                DetailUrl = detailUrl,
                Region = areaDesc
            };

            AlertReceived?.Invoke(this, new EAS.AlertReceivedEventArgs { Alert = alert });
            return alert;
        }

        /// <summary>
        /// Extracts SAME codes from the NWS API parameters object.
        /// </summary>
        private static string? ExtractSameFromParameters(JsonElement parameters)
        {
            try
            {
                if (parameters.TryGetProperty("SAME", out var sameArray) && sameArray.ValueKind == JsonValueKind.Array)
                {
                    var codes = new List<string>();
                    foreach (var item in sameArray.EnumerateArray())
                    {
                        var val = item.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            codes.Add(val);
                    }
                    return codes.Count > 0 ? string.Join(", ", codes) : null;
                }
            }
            catch { }
            return null;
        }

        private static string GetJsonString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString()?.Trim() ?? string.Empty;
            return string.Empty;
        }

        // ─── Legacy CAP XML parsing ────────────────────────────────────────

        private List<AlertEntry> ParseCapAlerts(string xml, List<string> filters)
        {
            var results = new List<AlertEntry>();
            if (string.IsNullOrWhiteSpace(xml)) return results;

            var doc = XDocument.Parse(xml);
            var capElements = doc.Root != null ? new[] { doc.Root }.Concat(doc.Root.Descendants()) : Enumerable.Empty<XElement>();
            var now = DateTimeOffset.UtcNow;

            foreach (var el in capElements.Where(IsAlertElement))
            {
                var alert = ConvertCapAlert(el, filters, now);
                if (alert != null) results.Add(alert);
            }

            return results;
        }

        private AlertEntry? ConvertCapAlert(XElement alertElement, List<string> filters, DateTimeOffset now)
        {
            var status = GetXmlValue(alertElement, "status");
            if (!string.Equals(status, "Actual", StringComparison.OrdinalIgnoreCase))
                return null;

            var scope = GetXmlValue(alertElement, "scope");
            if (!string.Equals(scope, "Public", StringComparison.OrdinalIgnoreCase)) return null;

            var msgType = GetXmlValue(alertElement, "msgType");
            if (string.Equals(msgType, "Cancel", StringComparison.OrdinalIgnoreCase)) return null;

            var identifier = GetXmlValue(alertElement, "identifier");
            lock (_lockObj)
            {
                if (!string.IsNullOrWhiteSpace(identifier) && !_seenIdentifiers.Add(identifier))
                    return null;
            }

            // Check age
            var sentStr = GetXmlValue(alertElement, "sent");
            if (_options.MaxAgeHours > 0 && DateTimeOffset.TryParse(sentStr, null, DateTimeStyles.AssumeUniversal, out var sentTime))
            {
                if (now - sentTime > TimeSpan.FromHours(_options.MaxAgeHours)) return null;
            }

            var info = alertElement.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("info", StringComparison.OrdinalIgnoreCase));
            if (info == null) return null;

            // Check expiry
            var expiresStr = GetXmlValue(info, "expires");
            if (!string.IsNullOrWhiteSpace(expiresStr) && DateTimeOffset.TryParse(expiresStr, null, DateTimeStyles.AssumeUniversal, out var expiresAt))
            {
                if (expiresAt < now) return null;
            }

            var eventName = GetXmlValue(info, "event");
            var headline = GetXmlValue(info, "headline");
            var description = GetXmlValue(info, "description");
            var instruction = GetXmlValue(info, "instruction");
            var severity = GetXmlValue(info, "severity");
            var urgency = GetXmlValue(info, "urgency");
            var certainty = GetXmlValue(info, "certainty");

            // Severity filter
            if (_options.SeverityFilter?.Any() == true)
            {
                if (!_options.SeverityFilter.Any(s => string.Equals(s, severity, StringComparison.OrdinalIgnoreCase)))
                    return null;
            }

            if (_options.HighRiskOnly && !IsHighRisk(severity, urgency, certainty))
                return null;

            var sameHeader = ExtractSameHeaderFromXml(info);

            var areaElement = info.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("area", StringComparison.OrdinalIgnoreCase));
            var areaDesc = GetXmlValue(areaElement, "areaDesc");
            if (string.IsNullOrWhiteSpace(areaDesc)) areaDesc = "NWS Alert";

            if (filters.Count > 0 && !filters.Any(f => areaDesc.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                return null;

            var summaryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(description)) summaryParts.Add(description.Trim());
            if (!string.IsNullOrWhiteSpace(sameHeader)) summaryParts.Add($"[SAME: {sameHeader}]");
            if (!string.IsNullOrWhiteSpace(instruction)) summaryParts.Add(instruction.Trim());
            var summary = summaryParts.Count > 0 ? string.Join("  ", summaryParts) : headline ?? eventName ?? "NWS Alert";

            DateTimeOffset? issuedAt = null;
            if (!string.IsNullOrWhiteSpace(sentStr) && DateTimeOffset.TryParse(sentStr, null, DateTimeStyles.AssumeUniversal, out var issuedParsed))
                issuedAt = issuedParsed;

            DateTimeOffset? expiresAtFinal = null;
            if (!string.IsNullOrWhiteSpace(expiresStr) && DateTimeOffset.TryParse(expiresStr, null, DateTimeStyles.AssumeUniversal, out var expParsed))
                expiresAtFinal = expParsed;

            var alert = new AlertEntry
            {
                City = areaDesc,
                Type = eventName ?? "Alert",
                Title = !string.IsNullOrWhiteSpace(headline) ? headline : eventName ?? "NWS Alert",
                Summary = summary,
                SeverityColor = MapSeverity(severity),
                Provider = "USA_NWS",
                IssuedAt = issuedAt,
                ExpiresAt = expiresAtFinal,
                Description = description,
                Instructions = instruction,
                Region = areaDesc
            };

            AlertReceived?.Invoke(this, new EAS.AlertReceivedEventArgs { Alert = alert });
            return alert;
        }

        /// <summary>
        /// Extracts the SAME header from a CAP XML info element.
        /// </summary>
        private static string? ExtractSameHeaderFromXml(XElement info)
        {
            try
            {
                var paramElement = info.Elements()
                    .Where(e => e.Name.LocalName.Equals("parameter", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(e => e.Elements().Any(c =>
                        c.Name.LocalName.Equals("valueName", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(c.Value?.Trim(), "SAME", StringComparison.OrdinalIgnoreCase)));

                if (paramElement != null)
                {
                    var valueElement = paramElement.Elements()
                        .FirstOrDefault(e => e.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase));
                    return valueElement?.Value?.Trim();
                }
            }
            catch { }
            return null;
        }

        // ─── Shared helpers ────────────────────────────────────────────────

        private static bool IsHighRisk(string? severity, string? urgency, string? certainty)
        {
            var sev = (severity ?? "").Trim().ToLowerInvariant();
            var urg = (urgency ?? "").Trim().ToLowerInvariant();
            return (sev == "extreme" || sev == "severe") && (urg == "immediate" || urg == "expected");
        }

        private static string GetXmlValue(XElement? parent, string localName)
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
            return severity.Trim().ToLowerInvariant() switch
            {
                "extreme" or "severe" => "Red",
                "moderate" or "minor" => "Yellow",
                _ => "Gray"
            };
        }

        private static List<string> NormalizeList(IEnumerable<string>? values)
        {
            return values?.Where(v => !string.IsNullOrWhiteSpace(v))
                          .Select(v => v.Trim())
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToList() ?? new List<string>();
        }

        private static List<AlertEntry> Deduplicate(IEnumerable<AlertEntry> alerts)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<AlertEntry>();

            foreach (var a in alerts)
            {
                var key = $"{a.City}|{a.Title}|{a.Type}";
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
