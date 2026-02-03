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
    /// Connection status for NAAD TCP streams
    /// </summary>
    public enum NaadConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected
    }

    /// <summary>
    /// Event args for heartbeat received
    /// </summary>
    public class HeartbeatEventArgs : EventArgs
    {
        public DateTime Timestamp { get; set; }
        public int ReferencedAlertCount { get; set; }
    }

    /// <summary>
    /// Event args for connection status changes
    /// </summary>
    public class ConnectionStatusEventArgs : EventArgs
    {
        public NaadConnectionStatus Status { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Event args for alert received from stream
    /// </summary>
    public class AlertReceivedEventArgs : EventArgs
    {
        public AlertEntry? Alert { get; set; }
        public int TotalActiveAlerts { get; set; }
    }

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

        /// <summary>If true, exclude weather/meteorological alerts (handled by ECCC).</summary>
        [JsonPropertyName("ExcludeWeatherAlerts")]
        public bool ExcludeWeatherAlerts { get; set; } = true;

        /// <summary>TCP reconnection delay in seconds (default: 30)</summary>
        [JsonPropertyName("ReconnectDelaySeconds")]
        public int ReconnectDelaySeconds { get; set; } = 30;

        /// <summary>HTTP request timeout in seconds (default: 30)</summary>
        [JsonPropertyName("HttpTimeoutSeconds")]
        public int HttpTimeoutSeconds { get; set; } = 30;

        /// <summary>Maximum number of seen identifiers to cache (prevents memory bloat)</summary>
        [JsonPropertyName("MaxCachedIdentifiers")]
        public int MaxCachedIdentifiers { get; set; } = 10000;

        /// <summary>Enable automatic generation of alert tone for broadcast-immediate alerts</summary>
        [JsonPropertyName("GenerateAlertTone")]
        public bool GenerateAlertTone { get; set; } = true;

        /// <summary>
        /// Gets the default NAAD TCP stream URLs for Alert Ready Canada.
        /// Primary and backup servers for redundancy.
        /// </summary>
        public static List<string> GetDefaultNaadUrls()
        {
            return new List<string>
            {
                "tcp://streaming1.naad-adna.pelmorex.com:8080",
                "tcp://streaming2.naad-adna.pelmorex.com:8080"
            };
        }

        /// <summary>
        /// Gets the default HTTP API URLs for Alert Ready Canada historical/current alerts.
        /// </summary>
        public static List<string> GetDefaultHttpUrls()
        {
            return new List<string>
            {
                "http://capcp1.naad-adna.pelmorex.com",
                "http://capcp2.naad-adna.pelmorex.com"
            };
        }
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
        private bool _streamsStarted = false;

        // Connection tracking
        private NaadConnectionStatus _connectionStatus = NaadConnectionStatus.Disconnected;
        private DateTimeOffset _lastHeartbeat = DateTimeOffset.MinValue;
        private int _activeAlertCount = 0;

        public Action<string>? Log { get; set; }

        /// <summary>Event fired when a heartbeat is received from NAAD</summary>
        public event EventHandler<HeartbeatEventArgs>? HeartbeatReceived;

        /// <summary>Event fired when connection status changes</summary>
        public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;

        /// <summary>Event fired when an alert is received from the stream</summary>
        public event EventHandler<AlertReceivedEventArgs>? AlertReceived;

        /// <summary>Current connection status</summary>
        public NaadConnectionStatus ConnectionStatus => _connectionStatus;

        /// <summary>Last heartbeat received time</summary>
        public DateTimeOffset LastHeartbeat => _lastHeartbeat;

        /// <summary>Number of active alerts in queue</summary>
        public int ActiveAlertCount => _activeAlertCount;

        public AlertReadyClient(HttpClient httpClient, AlertReadyOptions? options = null)
        {
            _httpClient = httpClient;
            _options = options ?? new AlertReadyOptions();
            // Don't auto-start - let caller control when to start via StartTcpStreams()
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

            // Skip weather alerts if ECCC handles them
            if (_options.ExcludeWeatherAlerts)
            {
                var category = GetValue(info, "category");
                if (string.Equals(category, "Met", StringComparison.OrdinalIgnoreCase))
                    return null;
            }

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

        /// <summary>
        /// Starts listening to NAAD TCP stream feeds.
        /// </summary>
        public void StartTcpStreams()
        {
            if (_streamsStarted)
            {
                LogMessage("TCP streams already started, ignoring duplicate call.");
                return;
            }
            _streamsStarted = true;
            
            if (!_options.Enabled)
            {
                LogMessage("NAAD streaming disabled in options.");
                return;
            }

            var tcpUrls = _options.FeedUrls?
                .Where(u => u?.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            if (tcpUrls == null || tcpUrls.Count == 0)
            {
                LogMessage("No TCP URLs configured for NAAD streaming.");
                return;
            }

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

            OnConnectionStatusChanged(NaadConnectionStatus.Connecting, host, port, "Connecting...");

            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient? client = null;
                NetworkStream? stream = null;

                try
                {
                    client = new TcpClient();
                    // Configure TCP keep-alive to maintain connection
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    client.ReceiveTimeout = 0; // No timeout - wait indefinitely for data
                    client.SendTimeout = 30000;
                    
                    OnConnectionStatusChanged(NaadConnectionStatus.Connecting, host, port, "Connecting...");
                    await client.ConnectAsync(host, port, cancellationToken);
                    LogMessage($"Connected to {host}");
                    OnConnectionStatusChanged(NaadConnectionStatus.Connected, host, port, "Connected");

                    stream = client.GetStream();
                    var buffer = new byte[8192];
                    var xmlBuffer = new StringBuilder();

                    while (!cancellationToken.IsCancellationRequested && client.Connected)
                    {
                        // Check if data is available before reading
                        if (stream.DataAvailable || client.Connected)
                        {
                            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                            if (bytesRead == 0)
                            {
                                // Wait a bit before checking again - server might just be idle
                                await Task.Delay(100, cancellationToken);
                                continue;
                            }

                            var data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            xmlBuffer.Append(data);
                            ProcessBuffer(xmlBuffer);
                        }
                        else
                        {
                            await Task.Delay(100, cancellationToken);
                        }
                    }
                    
                    LogMessage($"Connection closed by {host}:{port}");
                    OnConnectionStatusChanged(NaadConnectionStatus.Disconnected, host, port, "Connection closed");
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        LogMessage($"TCP stream error for {host}:{port}: {ex.Message}");
                        OnConnectionStatusChanged(NaadConnectionStatus.Disconnected, host, port, ex.Message);
                    }
                }
                finally
                {
                    stream?.Dispose();
                    client?.Dispose();
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    var reconnectDelay = _options.ReconnectDelaySeconds > 0 ? _options.ReconnectDelaySeconds : 30;
                    LogMessage($"Reconnecting to {host}:{port} in {reconnectDelay} seconds...");
                    OnConnectionStatusChanged(NaadConnectionStatus.Disconnected, host, port, $"Reconnecting in {reconnectDelay}s...");
                    await Task.Delay(TimeSpan.FromSeconds(reconnectDelay), cancellationToken);
                }
            }
        }

        private void OnConnectionStatusChanged(NaadConnectionStatus status, string host, int port, string message)
        {
            _connectionStatus = status;
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs
            {
                Status = status,
                Host = host,
                Port = port,
                Message = message
            });
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

                    // Prevent memory bloat by clearing old identifiers if cache is too large
                    if (_seenIdentifiers.Count > _options.MaxCachedIdentifiers)
                    {
                        LogMessage($"Clearing identifier cache (exceeded {_options.MaxCachedIdentifiers} entries)");
                        _seenIdentifiers.Clear();
                        _seenIdentifiers.Add(identifier); // Re-add current one
                    }
                }

                // Handle NAADS heartbeat messages (no logging to avoid log spam)
                if (sender.Contains("NAADS-Heartbeat", StringComparison.OrdinalIgnoreCase))
                {
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
                    _activeAlertCount++;
                    LogMessage($"Queued alert from TCP stream: {alert.Title}");
                    
                    // Fire alert received event
                    AlertReceived?.Invoke(this, new AlertReceivedEventArgs
                    {
                        Alert = alert,
                        TotalActiveAlerts = _activeAlertCount
                    });
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
            
            // Count referenced alerts
            var refParts = string.IsNullOrWhiteSpace(references) 
                ? Array.Empty<string>() 
                : references.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Fire heartbeat event
            _lastHeartbeat = DateTimeOffset.UtcNow;
            HeartbeatReceived?.Invoke(this, new HeartbeatEventArgs
            {
                Timestamp = _lastHeartbeat.UtcDateTime,
                ReferencedAlertCount = refParts.Length
            });
            
            if (string.IsNullOrWhiteSpace(references)) return;

            // References format: "sender,identifier,sent sender2,identifier2,sent2 ..."
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
                        // Alert downloaded silently
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

        /// <summary>
        /// Gets connection health statistics for monitoring.
        /// </summary>
        public ConnectionHealthStats GetConnectionHealth()
        {
            var timeSinceLastHeartbeat = _lastHeartbeat != DateTimeOffset.MinValue
                ? DateTimeOffset.UtcNow - _lastHeartbeat
                : TimeSpan.MaxValue;

            return new ConnectionHealthStats
            {
                Status = _connectionStatus,
                LastHeartbeat = _lastHeartbeat == DateTimeOffset.MinValue ? null : _lastHeartbeat,
                TimeSinceLastHeartbeat = timeSinceLastHeartbeat,
                IsHealthy = _connectionStatus == NaadConnectionStatus.Connected && 
                           timeSinceLastHeartbeat < TimeSpan.FromMinutes(2),
                ActiveAlertCount = _activeAlertCount,
                CachedIdentifierCount = _seenIdentifiers.Count,
                StreamTasksRunning = _streamTasks.Count(t => !t.IsCompleted)
            };
        }

        /// <summary>
        /// Clears the seen identifiers cache (useful if you want to reprocess alerts).
        /// </summary>
        public void ClearSeenIdentifiers()
        {
            lock (_lockObj)
            {
                _seenIdentifiers.Clear();
            }
            LogMessage("Cleared seen identifiers cache.");
        }

        /// <summary>
        /// Gets all queued alerts without clearing the queue.
        /// </summary>
        public IReadOnlyList<AlertEntry> PeekAlerts()
        {
            return _streamAlerts.ToArray();
        }

        public void Dispose()
        {
            _streamCts.Cancel();
            Task.WaitAll(_streamTasks.ToArray(), TimeSpan.FromSeconds(5));
            _streamCts.Dispose();
        }
    }

    /// <summary>
    /// Connection health statistics for monitoring the NAAD stream.
    /// </summary>
    public class ConnectionHealthStats
    {
        public NaadConnectionStatus Status { get; set; }
        public DateTimeOffset? LastHeartbeat { get; set; }
        public TimeSpan TimeSinceLastHeartbeat { get; set; }
        public bool IsHealthy { get; set; }
        public int ActiveAlertCount { get; set; }
        public int CachedIdentifierCount { get; set; }
        public int StreamTasksRunning { get; set; }

        public override string ToString()
        {
            var heartbeatStr = LastHeartbeat.HasValue 
                ? $"{TimeSinceLastHeartbeat.TotalSeconds:F1}s ago" 
                : "Never";
            return $"Status: {Status}, Healthy: {IsHealthy}, Heartbeat: {heartbeatStr}, " +
                   $"Alerts: {ActiveAlertCount}, Cached IDs: {CachedIdentifierCount}, Streams: {StreamTasksRunning}";
        }
    }
}
