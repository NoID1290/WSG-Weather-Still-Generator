using System;
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

        // ── HLS Alert Injection (writes to Tunarr's stream cache on disk) ──

        /// <summary>
        /// When enabled, EAS alerts are also injected directly into Tunarr's HLS stream
        /// cache on disk by appending #EXT-X-DISCONTINUITY + alert segments to stream.m3u8.
        /// This is a secondary delivery mode — clients reading Tunarr's HLS endpoint natively
        /// will see the alert without going through the TCP byte pipe.
        /// </summary>
        [JsonPropertyName("HlsInjectionEnabled")]
        public bool HlsInjectionEnabled { get; set; } = false;

        /// <summary>
        /// Root path of Tunarr's stream cache directory (e.g. "R:\stream-cache").
        /// Each channel's HLS output lives at {path}\stream_{TunarrChannelId}\.
        /// </summary>
        [JsonPropertyName("TunarrStreamCachePath")]
        public string TunarrStreamCachePath { get; set; } = "";

        /// <summary>
        /// HLS segment duration in seconds. Must match Tunarr's -hls_time setting.
        /// Used to split the alert .ts into correctly-sized HLS segments.
        /// Default: 4 (Tunarr 0.20.x typical default).
        /// </summary>
        [JsonPropertyName("HlsSegmentDurationSeconds")]
        public int HlsSegmentDurationSeconds { get; set; } = 4;

        // ── HLS Relay (read-only Tunarr cache relay with EAS injection) ──

        /// <summary>
        /// When enabled, starts an HTTP HLS relay server that reads Tunarr's stream cache
        /// read-only and serves modified manifests with EAS segments injected at segment
        /// boundaries. Tunarr's cache files are never modified.
        /// Point TvMate's M3U source at: http://server:{HlsRelayPort}/m3u
        /// </summary>
        [JsonPropertyName("HlsRelayEnabled")]
        public bool HlsRelayEnabled { get; set; } = false;

        /// <summary>
        /// Port for the HLS relay HTTP server. Default: 8090.
        /// Must be different from TunarrPublicPort and TunarrInternalPort.
        /// </summary>
        [JsonPropertyName("HlsRelayPort")]
        public int HlsRelayPort { get; set; } = 8090;

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

    /// <summary>
    /// Stream parameters discovered by probing a live Tunarr channel via ffprobe.
    /// Used to generate splice-compatible alert .ts files that match the upstream
    /// codec, resolution, framerate, bitrate, and PID structure exactly.
    /// Not serialized — populated at runtime by StreamPipeService.
    /// </summary>
    public class ChannelStreamProfile
    {
        // ── Codec parameters ───────────────────────────────────────────
        public string VideoCodec { get; set; } = "h264";
        public string AudioCodec { get; set; } = "aac";
        public int Width { get; set; }
        public int Height { get; set; }
        public string FrameRate { get; set; } = "24/1";
        public string? VideoProfile { get; set; }
        public string? VideoLevel { get; set; }
        public int VideoBitrateBps { get; set; }
        public int AudioSampleRate { get; set; } = 48000;
        public int AudioChannels { get; set; } = 2;

        // ── MPEG-TS PID structure (learned from UpstreamPidMap) ────────
        public int PmtPid { get; set; } = -1;
        public int PcrPid { get; set; } = -1;
        public int VideoPid { get; set; } = -1;
        public int AudioPid { get; set; } = -1;
        public int ServiceId { get; set; } = 1;

        // ── Probe metadata ─────────────────────────────────────────────
        public DateTime LastProbed { get; set; } = DateTime.MinValue;

        /// <summary>True when we have enough data to generate a matched alert .ts.</summary>
        public bool IsComplete => Width > 0 && Height > 0 && VideoPid > 0;
    }
}
