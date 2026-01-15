#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
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

        /// <summary>Jurisdictions to include (e.g., ["QC", "CA"]). Matches areaDesc/geocode/sender.</summary>
        [JsonPropertyName("Jurisdictions")]
        public List<string>? Jurisdictions { get; set; } = new List<string> { "QC", "CA" };

        /// <summary>If true, keep only high-risk alerts (Severe/Extreme).</summary>
        [JsonPropertyName("HighRiskOnly")]
        public bool HighRiskOnly { get; set; } = true;
    }

    /// <summary>
    /// Lightweight CAP-CP client for Alert Ready / NAAD public feeds.
    /// Produces the shared AlertEntry model for reuse in the UI.
    /// </summary>
    public class AlertReadyClient
    {
        private readonly HttpClient _httpClient;
        private readonly AlertReadyOptions _options;
        private readonly ConcurrentQueue<AlertEntry> _streamAlerts = new();
        private readonly List<Task> _streamTasks = new();
        private readonly CancellationTokenSource _streamCts = new();
        private readonly HashSet<string> _seenIdentifiers = new();
        private readonly object _lockObj = new();

        public Action<string>? Log { get; set; }

        public AlertReadyClient(HttpClient httpClient, AlertReadyOptions? options = null)
        {
            _httpClient = httpClient;
            _options = options ?? new AlertReadyOptions();
            StartTcpStreams();
        }

        /// <summary>
        /// Fetches and parses all configured Alert Ready feeds (HTTP) and drains TCP stream alerts.
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

            // Drain TCP stream alerts
            while (_streamAlerts.TryDequeue(out var streamAlert))
            {
                alerts.Add(streamAlert);
            }

            var feeds = _options.FeedUrls?
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (feeds == null || feeds.Count == 0)
            {
                if (alerts.Count > 0)
                    LogMessage($"Using {alerts.Count} alerts from TCP streams.");
                else
                    LogMessage("No HTTP Alert Ready feed URLs configured; using TCP streams only.");
                return Deduplicate(alerts);
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

            // Scope must be Public to mirror broadcasted alerts (TV/radio/wireless)
            var scope = GetValue(alertElement, "scope");
            if (!string.Equals(scope, "Public", StringComparison.OrdinalIgnoreCase)) return null;

            var sentStr = GetValue(alertElement, "sent");
            if (_options.MaxAgeHours > 0 && DateTimeOffset.TryParse(sentStr, null, DateTimeStyles.AssumeUniversal, out var sentTime))
            {
                if (now - sentTime > TimeSpan.FromHours(_options.MaxAgeHours)) return null;
            }

            // If an expires time is provided and already passed, drop the alert
            var anyInfo = alertElement.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("info", StringComparison.OrdinalIgnoreCase));
            var expiresStr = GetValue(anyInfo, "expires");
            if (!string.IsNullOrWhiteSpace(expiresStr) && DateTimeOffset.TryParse(expiresStr, null, DateTimeStyles.AssumeUniversal, out var expiresAt))
            {
                if (expiresAt < now) return null;
            }

            var info = SelectInfo(alertElement);
            if (info == null) return null;

            var senderName = GetValue(alertElement, "senderName");
            var eventName = GetValue(info, "event");
            var headline = GetValue(info, "headline");
            var description = GetValue(info, "description");
            var instruction = GetValue(info, "instruction");
            var severity = GetValue(info, "severity");
            var certainty = GetValue(info, "certainty");
            var urgency = GetValue(info, "urgency");

            if (_options.HighRiskOnly && !IsHighRisk(severity, urgency, certainty))
                return null;

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
            // Jurisdiction filter (QC/CA) and optional free-text filters
            if (!MatchesJurisdiction(senderName, areaElement, _options.Jurisdictions)) return null;
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

        private static bool IsHighRisk(string? severity, string? urgency, string? certainty)
        {
            var sev = (severity ?? string.Empty).Trim().ToLowerInvariant();
            var urg = (urgency ?? string.Empty).Trim().ToLowerInvariant();
            var cer = (certainty ?? string.Empty).Trim().ToLowerInvariant();

            var sevOk = sev == "extreme" || sev == "severe";
            var urgOk = urg == "immediate" || urg == "expected";
            var cerOk = cer == "observed" || cer == "likely";

            return sevOk && urgOk && cerOk;
        }

        private static bool MatchesJurisdiction(string senderName, XElement? areaElement, List<string>? allowed)
        {
            if (allowed == null || allowed.Count == 0) return true;
            var allowedSet = new HashSet<string>(allowed.Select(a => a.Trim().ToLowerInvariant()));

            // Check senderName
            var sender = (senderName ?? string.Empty);
            if (allowedSet.Contains("qc") && (ContainsQuebec(sender) || sender.IndexOf("gouvernement du québec", StringComparison.OrdinalIgnoreCase) >= 0 || sender.IndexOf("gouvernement du quebec", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
            if (allowedSet.Contains("ca") && (ContainsCanada(sender) || sender.IndexOf("government of canada", StringComparison.OrdinalIgnoreCase) >= 0 || sender.IndexOf("gouvernement du canada", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;

            // Check areaDesc
            var areaDesc = GetValue(areaElement, "areaDesc");
            var areaDescLower = areaDesc.ToLowerInvariant();
            if (allowedSet.Contains("qc") && (ContainsQuebec(areaDesc) || areaDescLower.Contains("qc")))
                return true;
            if (allowedSet.Contains("ca") && (ContainsCanada(areaDesc) || areaDescLower.Contains("pan-canada") || areaDescLower.Contains("national")))
                return true;

            // Check geocodes
            foreach (var ge in areaElement?.Elements().Where(e => e.Name.LocalName.Equals("geocode", StringComparison.OrdinalIgnoreCase)) ?? Enumerable.Empty<XElement>())
            {
                var vnameRaw = GetValue(ge, "valueName");
                var valRaw = GetValue(ge, "value");
                var vname = vnameRaw.ToLowerInvariant();
                var val = valRaw.ToLowerInvariant();
                // Any token equals one of the allowed (split by non-alnum)
                var tokens = val.Split(new[] { ',', ';', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Any(t => allowedSet.Contains(t))) return true;

                // Prefer CAP-CP Location references; Québec SGC codes start with 24
                if (vname.Contains("cap-cp") || vname.Contains("location") || vname.Contains("canadianlocation"))
                {
                    if (allowedSet.Contains("qc"))
                    {
                        var digits = new string(valRaw.Where(char.IsDigit).ToArray());
                        if (digits == "24" || digits.StartsWith("24")) return true;
                    }
                    if (allowedSet.Contains("ca") && (vname.Contains("canada") || val.Contains("canada")))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ContainsQuebec(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value.IndexOf("québec", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("quebec", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsCanada(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value.IndexOf("canada", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("canadien", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("canadienne", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private void StartTcpStreams()
        {
            if (!_options.Enabled) return;

            var tcpUrls = _options.FeedUrls?
                .Where(u => u?.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            if (tcpUrls == null || tcpUrls.Count == 0) return;

            foreach (var url in tcpUrls)
            {
                var task = Task.Run(async () => await ListenToTcpStreamAsync(url, _streamCts.Token));
                _streamTasks.Add(task);
            }
        }

        private async Task ListenToTcpStreamAsync(string tcpUrl, CancellationToken cancellationToken)
        {
            var uri = new Uri(tcpUrl);
            var host = uri.Host;
            var port = uri.Port;

            LogMessage($"Starting TCP stream listener for {host}:{port}");

            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient? client = null;
                Stream? stream = null;

                try
                {
                    client = new TcpClient();
                    await client.ConnectAsync(host, port, cancellationToken);
                    LogMessage($"Connected to {host}:{port}");

                    stream = client.GetStream();
                    var reader = new StreamReader(stream, Encoding.UTF8);
                    var buffer = new StringBuilder();

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var data = new char[4096];
                        var bytesRead = await reader.ReadAsync(data, 0, data.Length);

                        if (bytesRead == 0)
                        {
                            LogMessage($"Connection closed by {host}:{port}");
                            break;
                        }

                        buffer.Append(data, 0, bytesRead);
                        ProcessBuffer(buffer);
                    }
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        LogMessage($"TCP stream error for {host}:{port}: {ex.Message}");
                    }
                }
                finally
                {
                    stream?.Dispose();
                    client?.Dispose();
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    LogMessage($"Reconnecting to {host}:{port} in 30 seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
        }

        private void ProcessBuffer(StringBuilder buffer)
        {
            var content = buffer.ToString();
            var alertEndTag = "</alert>";
            int endIndex;

            while ((endIndex = content.IndexOf(alertEndTag, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var alertXml = content.Substring(0, endIndex + alertEndTag.Length);
                buffer.Remove(0, endIndex + alertEndTag.Length);
                content = buffer.ToString();

                ProcessAlertXml(alertXml);
            }
        }

        private void ProcessAlertXml(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                var alertElement = doc.Root;

                if (alertElement == null || !IsAlertElement(alertElement))
                    return;

                var identifier = GetValue(alertElement, "identifier");
                var sender = GetValue(alertElement, "sender");
                var source = GetValue(alertElement, "source");

                // Skip duplicate alerts
                lock (_lockObj)
                {
                    if (!_seenIdentifiers.Add(identifier))
                    {
                        return;
                    }
                }

                // Handle NAADS heartbeat messages
                if (sender.Contains("NAADS-Heartbeat", StringComparison.OrdinalIgnoreCase))
                {
                    LogMessage("Received NAADS heartbeat");
                    ProcessHeartbeat(alertElement);
                    return;
                }

                // Process regular CAP alert
                var normalizedFilters = new List<string>();
                if (_options.AreaFilters?.Count > 0)
                {
                    normalizedFilters.AddRange(NormalizeList(_options.AreaFilters));
                }

                var alert = ConvertAlert(alertElement, normalizedFilters, DateTimeOffset.UtcNow);
                if (alert != null)
                {
                    _streamAlerts.Enqueue(alert);
                    LogMessage($"Queued alert from TCP stream: {alert.Title}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to process TCP alert XML: {ex.Message}");
            }
        }

        private void ProcessHeartbeat(XElement alertElement)
        {
            // NAADS heartbeat contains references to recent alerts that can be downloaded via HTTP
            var references = GetValue(alertElement, "references");
            if (string.IsNullOrWhiteSpace(references)) return;

            // References format: "sender,identifier,sent sender2,identifier2,sent2 ..."
            var refParts = references.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var refPart in refParts)
            {
                var parts = refPart.Split(',');
                if (parts.Length < 3) continue;

                var refIdentifier = parts[1];
                var refSent = parts[2];

                // Check if we've already seen this alert
                lock (_lockObj)
                {
                    if (!_seenIdentifiers.Add(refIdentifier))
                        continue;
                }

                // Download alert from NAAD HTTP servers
                _ = Task.Run(async () => await DownloadHeartbeatAlertAsync(refIdentifier, refSent));
            }
        }

        private async Task DownloadHeartbeatAlertAsync(string identifier, string sent)
        {
            try
            {
                // Parse sent date to get folder path: 2026-01-15T23:43:26-00:00 -> 2026-01-15
                var sentDate = sent.Split('T')[0];
                var filename = $"{sent.Replace("-", "_").Replace("+", "p").Replace(":", "_")}I{identifier.Replace("-", "_").Replace("+", "p").Replace(":", "_")}.xml";

                var urls = new[]
                {
                    $"http://capcp1.naad-adna.pelmorex.com/{sentDate}/{filename}",
                    $"http://capcp2.naad-adna.pelmorex.com/{sentDate}/{filename}"
                };

                foreach (var url in urls)
                {
                    try
                    {
                        var xml = await _httpClient.GetStringAsync(url);
                        ProcessAlertXml(xml);
                        LogMessage($"Downloaded heartbeat alert: {identifier}");
                        return;
                    }
                    catch
                    {
                        // Try next server
                    }
                }

                LogMessage($"Failed to download heartbeat alert: {identifier}");
            }
            catch (Exception ex)
            {
                LogMessage($"Error downloading heartbeat alert: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _streamCts.Cancel();
            Task.WaitAll(_streamTasks.ToArray(), TimeSpan.FromSeconds(5));
            _streamCts.Dispose();
        }
    }
}
