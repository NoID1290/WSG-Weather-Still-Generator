using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WeatherImageGenerator.Models
{
    /// <summary>
    /// Configuration for the MPEG-TS stream proxy (same-port takeover mode).
    /// The proxy listens on Tunarr's original public port so ALL existing clients
    /// (Plex, Jellyfin, Emby, VLC, STBs) keep working with zero URL changes.
    /// Tunarr is moved to an internal port that only the proxy connects to.
    /// </summary>
    public class StreamProxySettings
    {
        /// <summary>Whether the stream proxy is enabled.</summary>
        [JsonPropertyName("Enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// The public port that clients already connect to (Tunarr's original port).
        /// The proxy takes over this port. Default: 8000 (Tunarr's default).
        /// </summary>
        [JsonPropertyName("TunarrPublicPort")]
        public int TunarrPublicPort { get; set; } = 8000;

        /// <summary>
        /// The internal port where Tunarr is moved to after enabling the proxy.
        /// Only the proxy connects here. Default: 8001.
        /// </summary>
        [JsonPropertyName("TunarrInternalPort")]
        public int TunarrInternalPort { get; set; } = 8001;

        /// <summary>Allow remote access (bind to all interfaces) or localhost only.</summary>
        [JsonPropertyName("AllowRemoteAccess")]
        public bool AllowRemoteAccess { get; set; } = true;

        /// <summary>HDHR device friendly name advertised to Plex/Jellyfin/Emby.</summary>
        [JsonPropertyName("DeviceFriendlyName")]
        public string DeviceFriendlyName { get; set; } = "WSG EAS Proxy";

        /// <summary>HDHR device ID (hex string). Must be unique on the network.</summary>
        [JsonPropertyName("DeviceId")]
        public string DeviceId { get; set; } = "12345680";

        /// <summary>
        /// Milliseconds to buffer while searching for a clean keyframe splice point.
        /// Higher values give cleaner splices but add latency to alert display.
        /// </summary>
        [JsonPropertyName("SpliceBufferMs")]
        public int SpliceBufferMs { get; set; } = 500;

        /// <summary>
        /// Maximum number of consecutive upstream reconnection attempts before dropping a client.
        /// Default: 10.
        /// </summary>
        [JsonPropertyName("MaxReconnectRetries")]
        public int MaxReconnectRetries { get; set; } = 10;

        /// <summary>
        /// Base reconnection delay in milliseconds. Doubles each retry (exponential backoff),
        /// capped at 15 seconds. Default: 1000 (1 second).
        /// </summary>
        [JsonPropertyName("ReconnectBaseMs")]
        public int ReconnectBaseMs { get; set; } = 1000;

        /// <summary>Channels to proxy. Each maps a Tunarr channel to a local channel number.</summary>
        [JsonPropertyName("Channels")]
        public List<ProxyChannelConfig> Channels { get; set; } = new();

        // ── Computed helpers (not serialized) ──────────────────────────

        /// <summary>The internal Tunarr base URL (derived from TunarrInternalPort).</summary>
        [JsonIgnore]
        public string TunarrBaseUrl => $"http://localhost:{TunarrInternalPort}";

        /// <summary>The public-facing listen port (same as TunarrPublicPort).</summary>
        [JsonIgnore]
        public int ListenPort => TunarrPublicPort;
    }

    /// <summary>
    /// Configuration for a single proxied channel.
    /// </summary>
    public class ProxyChannelConfig
    {
        /// <summary>Tunarr channel UUID or guide number.</summary>
        [JsonPropertyName("TunarrChannelId")]
        public string TunarrChannelId { get; set; } = "";

        /// <summary>Channel number presented to downstream clients (e.g. in HDHR lineup).</summary>
        [JsonPropertyName("ProxyChannelNumber")]
        public int ProxyChannelNumber { get; set; } = 1;

        /// <summary>Display name for the channel.</summary>
        [JsonPropertyName("DisplayName")]
        public string DisplayName { get; set; } = "Weather Channel";

        /// <summary>Whether EAS alert interruption is enabled for this channel.</summary>
        [JsonPropertyName("AlertInterruptEnabled")]
        public bool AlertInterruptEnabled { get; set; } = true;
    }
}
