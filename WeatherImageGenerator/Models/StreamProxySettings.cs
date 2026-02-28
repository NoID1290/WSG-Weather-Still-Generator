using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WeatherImageGenerator.Models
{
    /// <summary>
    /// Configuration for the MPEG-TS stream proxy that sits between Tunarr and downstream clients.
    /// Enables transparent EAS alert injection into live streams.
    /// </summary>
    public class StreamProxySettings
    {
        /// <summary>Whether the stream proxy is enabled.</summary>
        [JsonPropertyName("Enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>Port the proxy listens on for incoming client connections.</summary>
        [JsonPropertyName("ListenPort")]
        public int ListenPort { get; set; } = 6077;

        /// <summary>Base URL of the Tunarr server (e.g. "http://localhost:8000").</summary>
        [JsonPropertyName("TunarrBaseUrl")]
        public string TunarrBaseUrl { get; set; } = "http://localhost:8000";

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

        /// <summary>Channels to proxy. Each maps a Tunarr channel to a local channel number.</summary>
        [JsonPropertyName("Channels")]
        public List<ProxyChannelConfig> Channels { get; set; } = new();
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
