#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace EKCA.Models
{
    // ---------------------------------------------------------------------------
    // Enumerations
    // ---------------------------------------------------------------------------

    /// <summary>Activity level of a seismic station — used for marker color coding.</summary>
    public enum ActivityLevel
    {
        /// <summary>Station has no recent data or is decommissioned.</summary>
        Inactive = 0,
        /// <summary>Station is active and reporting; no recent significant event detected.</summary>
        Normal = 1,
        /// <summary>Station recorded a recent moderate event (M ≥ 2.0 within 24 h, or M ≥ 3.0 within 7 days).</summary>
        Elevated = 2,
        /// <summary>Station is at or very near an active or just-occurred significant event (M ≥ 3.0 within 24 h).</summary>
        Active = 3
    }

    // ---------------------------------------------------------------------------
    // Station model
    // ---------------------------------------------------------------------------

    /// <summary>A seismograph station in the Canadian National Seismograph Network (CNSN).</summary>
    public class SeismicStation
    {
        /// <summary>Network code — typically "CN".</summary>
        public string Network { get; set; } = "CN";

        /// <summary>Station code used in FDSN queries (e.g. "OTT", "GAC").</summary>
        public string StationCode { get; set; } = string.Empty;

        /// <summary>Geodetic latitude (WGS-84), degrees North.</summary>
        public double Latitude { get; set; }

        /// <summary>Geodetic longitude (WGS-84), degrees East.</summary>
        public double Longitude { get; set; }

        /// <summary>Elevation above sea level in metres.</summary>
        public double ElevationM { get; set; }

        /// <summary>Human-readable site name, e.g. "Ottawa, ON, CA".</summary>
        public string SiteName { get; set; } = string.Empty;

        /// <summary>UTC time the station epoch started.</summary>
        public DateTime StartTime { get; set; }

        /// <summary>UTC time the station epoch ended; null = still operational.</summary>
        public DateTime? EndTime { get; set; }

        /// <summary>True if this station is currently operational (EndTime null or future).</summary>
        public bool IsActive => !EndTime.HasValue || EndTime.Value > DateTime.UtcNow;

        /// <summary>Real-time activity level — updated by the viewer after event correlation.</summary>
        public ActivityLevel ActivityLevel { get; set; } = ActivityLevel.Normal;

        /// <summary>UTC time of the most recent detected event at this station (if any).</summary>
        public DateTime? LastEventTime { get; set; }

        /// <summary>Magnitude of the most recent event near this station (if any).</summary>
        public double? LastEventMagnitude { get; set; }

        /// <summary>Distance (km) from the most recent nearby event.</summary>
        public double? NearestEventDistanceKm { get; set; }

        /// <summary>Returns a display label: "OTT — Ottawa, ON".</summary>
        public string DisplayLabel => $"{StationCode} — {SiteName}";

        public override string ToString() => DisplayLabel;
    }

    // ---------------------------------------------------------------------------
    // Earthquake event model
    // ---------------------------------------------------------------------------

    /// <summary>A significant earthquake event parsed from the Earthquakes Canada Atom feed.</summary>
    public class EarthquakeEvent
    {
        /// <summary>Unique identifier from the Atom entry (typically the event URL).</summary>
        public string EventId { get; set; } = string.Empty;

        /// <summary>Raw HTML title from the Atom entry.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Richter / moment magnitude.</summary>
        public double Magnitude { get; set; }

        /// <summary>Geodetic latitude of the epicentre (WGS-84).</summary>
        public double Latitude { get; set; }

        /// <summary>Geodetic longitude of the epicentre (WGS-84).</summary>
        public double Longitude { get; set; }

        /// <summary>Focal depth in km.</summary>
        public double DepthKm { get; set; }

        /// <summary>UTC origin time of the earthquake.</summary>
        public DateTime OriginTime { get; set; }

        /// <summary>Human-readable location description (e.g. "100 km SW of Burwash Landing, YT").</summary>
        public string Location { get; set; } = string.Empty;

        /// <summary>Link to the earthquake detail page on earthquakescanada.nrcan.gc.ca.</summary>
        public string? DetailUrl { get; set; }

        /// <summary>Whether this is considered a significant event (M ≥ 3.0).</summary>
        public bool IsSignificant => Magnitude >= 3.0;

        /// <summary>Friendly display string like "M4.2 — Burwash Landing, YT (2h ago)".</summary>
        public string DisplayLabel
        {
            get
            {
                var age = DateTime.UtcNow - OriginTime;
                string ageStr = age.TotalDays >= 1
                    ? $"{(int)age.TotalDays}d ago"
                    : age.TotalHours >= 1
                        ? $"{(int)age.TotalHours}h ago"
                        : $"{(int)age.TotalMinutes}m ago";
                return $"M{Magnitude:F1} — {Location} ({ageStr})";
            }
        }

        public override string ToString() => DisplayLabel;
    }

    // ---------------------------------------------------------------------------
    // Seismogram / waveform data
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Decoded seismogram data for a single station/channel/time window,
    /// assembled from one or more MiniSEED records.
    /// </summary>
    public class SeismogramData
    {
        /// <summary>Station code this data belongs to.</summary>
        public string StationCode { get; set; } = string.Empty;

        /// <summary>Network code (e.g. "CN").</summary>
        public string Network { get; set; } = "CN";

        /// <summary>Channel code (e.g. "HHZ").</summary>
        public string Channel { get; set; } = string.Empty;

        /// <summary>Location code.</summary>
        public string Location { get; set; } = string.Empty;

        /// <summary>UTC start time of the first sample.</summary>
        public DateTime StartTime { get; set; }

        /// <summary>Nominal sample rate in Hz.</summary>
        public double SampleRateHz { get; set; }

        /// <summary>Raw integer or float amplitude samples in chronological order.</summary>
        public float[] Samples { get; set; } = Array.Empty<float>();

        /// <summary>Total duration of this data in seconds.</summary>
        public double DurationSeconds => SampleRateHz > 0 ? Samples.Length / SampleRateHz : 0;

        /// <summary>UTC end time of the last sample.</summary>
        public DateTime EndTime => StartTime.AddSeconds(DurationSeconds);

        /// <summary>True if there is at least one sample.</summary>
        public bool HasData => Samples.Length > 0;

        /// <summary>
        /// Returns samples normalized to the [-1, 1] range.
        /// If all samples are zero, returns a zero array.
        /// </summary>
        public float[] NormalizedSamples()
        {
            if (Samples.Length == 0) return Array.Empty<float>();
            float max = 0;
            foreach (var s in Samples)
            {
                var abs = MathF.Abs(s);
                if (abs > max) max = abs;
            }
            if (max == 0f) return new float[Samples.Length];
            var result = new float[Samples.Length];
            for (int i = 0; i < Samples.Length; i++)
                result[i] = Samples[i] / max;
            return result;
        }

        /// <summary>
        /// Returns a downsampled version of Samples with at most <paramref name="targetCount"/> points,
        /// using peak-preserving min/max decimation suitable for waveform display.
        /// </summary>
        public float[] Decimate(int targetCount)
        {
            if (Samples.Length == 0 || targetCount <= 0) return Array.Empty<float>();
            if (Samples.Length <= targetCount) return Samples;

            double step = (double)Samples.Length / targetCount;
            var output = new float[targetCount];
            for (int i = 0; i < targetCount; i++)
            {
                int lo = (int)(i * step);
                int hi = (int)((i + 1) * step);
                if (hi > Samples.Length) hi = Samples.Length;
                float peak = Samples[lo];
                for (int j = lo; j < hi; j++)
                    if (MathF.Abs(Samples[j]) > MathF.Abs(peak)) peak = Samples[j];
                output[i] = peak;
            }
            return output;
        }
    }

    // ---------------------------------------------------------------------------
    // Internal MiniSEED record (parsed intermediate)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Internal representation of a single parsed MiniSEED data record before
    /// conversion to <see cref="SeismogramData"/>.
    /// </summary>
    internal class MiniSeedRecord
    {
        public string Network { get; set; } = string.Empty;
        public string Station { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public double SampleRateHz { get; set; }
        public int SampleCount { get; set; }
        public float[] Samples { get; set; } = Array.Empty<float>();
        public int RecordLengthBytes { get; set; } = 512;
        public byte EncodingFormat { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Settings
    // ---------------------------------------------------------------------------

    /// <summary>Configuration for the EKCA API client.</summary>
    public class EKCASettings
    {
        /// <summary>Default seismograph network code.</summary>
        public string DefaultNetwork { get; set; } = "CN";

        /// <summary>Default preferred channel code for waveform retrieval.</summary>
        public string DefaultChannel { get; set; } = "HHZ";

        /// <summary>Fallback channel codes tried in order if preferred channel has no data.</summary>
        public string[] FallbackChannels { get; set; } = new[] { "BHZ", "EHZ", "SHZ" };

        /// <summary>Default waveform window duration in hours when not explicitly specified.</summary>
        public int DefaultWaveformWindowHours { get; set; } = 1;

        /// <summary>How often (seconds) to poll the Atom feed for new events.</summary>
        public int AtomRefreshIntervalSeconds { get; set; } = 60;

        /// <summary>HTTP request timeout in seconds.</summary>
        public int HttpTimeoutSeconds { get; set; } = 30;

        /// <summary>Minimum milliseconds to wait between consecutive HTTP requests.</summary>
        public int DelayBetweenRequestsMs { get; set; } = 250;

        /// <summary>Radius (km) within which a station is considered "near" an event for activity-level colouring.</summary>
        public double StationNearEventRadiusKm { get; set; } = 200.0;

        /// <summary>HTTP User-Agent header sent with all requests.</summary>
        public string UserAgent { get; set; } =
            "WeatherImageGenerator/1.0 (+https://github.com/NoID-Softwork/weather-still-api)";
    }
}
