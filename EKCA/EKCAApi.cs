#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EKCA.Models;
using EKCA.Services;

namespace EKCA
{
    /// <summary>
    /// Public static API for Earthquakes Canada (EKCA) seismic data.
    ///
    /// Provides access to:
    ///  - CNSN station list (locations, activity levels)
    ///  - Recent significant earthquake events (Earthquakes Canada Atom feed)
    ///  - Real-time and near-real-time waveform data via FDSN Dataselect (MiniSEED)
    ///
    /// Usage example:
    /// <code>
    ///   var stations = await EKCAApi.GetStationsAsync(httpClient);
    ///   var events   = await EKCAApi.GetRecentEventsAsync(httpClient);
    ///   var waveform = await EKCAApi.GetRecentWaveformAsync(httpClient, "OTT", lastHours: 1);
    /// </code>
    /// </summary>
    public static class EKCAApi
    {
        // ---------------------------------------------------------------------------
        // Diagnostics callback
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Optional logging callback. Set this to receive diagnostic messages.
        /// </summary>
        public static Action<string>? Log { get; set; }

        private static void LogMessage(string message) => Log?.Invoke($"[EKCA] {message}");

        // ---------------------------------------------------------------------------
        // Station list
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Returns all currently active stations of the Canadian National Seismograph
        /// Network (CNSN), with lat/lon, elevation, and site name.
        /// </summary>
        /// <param name="httpClient">Shared <see cref="HttpClient"/> instance.</param>
        /// <param name="settings">Optional settings override.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task<List<SeismicStation>> GetStationsAsync(
            HttpClient httpClient,
            EKCASettings? settings = null,
            CancellationToken ct = default)
        {
            LogMessage("Fetching CNSN station list...");
            try
            {
                var client = new EKCAClient(httpClient, settings);
                var stations = await client.FetchStationsAsync(ct).ConfigureAwait(false);
                LogMessage($"Retrieved {stations.Count} active stations.");
                return stations;
            }
            catch (Exception ex)
            {
                LogMessage($"GetStationsAsync failed: {ex.Message}");
                return new List<SeismicStation>();
            }
        }

        // ---------------------------------------------------------------------------
        // Earthquake events
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Returns recent significant earthquake events parsed from the
        /// Earthquakes Canada Atom feed (approximately last 30 days),
        /// sorted by origin time descending.
        /// </summary>
        /// <param name="httpClient">Shared <see cref="HttpClient"/> instance.</param>
        /// <param name="settings">Optional settings override.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task<List<EarthquakeEvent>> GetRecentEventsAsync(
            HttpClient httpClient,
            EKCASettings? settings = null,
            CancellationToken ct = default)
        {
            LogMessage("Fetching recent earthquake events...");
            try
            {
                var client = new EKCAClient(httpClient, settings);
                var events = await client.FetchRecentEventsAsync(ct).ConfigureAwait(false);
                LogMessage($"Retrieved {events.Count} earthquake events.");
                return events;
            }
            catch (Exception ex)
            {
                LogMessage($"GetRecentEventsAsync failed: {ex.Message}");
                return new List<EarthquakeEvent>();
            }
        }

        // ---------------------------------------------------------------------------
        // Waveform data
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Fetches MiniSEED waveform data for the specified station and time window,
        /// decoding it into a <see cref="SeismogramData"/> object.
        /// Tries the preferred <paramref name="channel"/> first, then falls back through
        /// HHZ → BHZ → EHZ → SHZ until data is obtained.
        /// </summary>
        /// <param name="httpClient">Shared <see cref="HttpClient"/> instance.</param>
        /// <param name="stationCode">FDSN station code, e.g. "OTT", "GAC", "DAWY".</param>
        /// <param name="startUtc">UTC start of the requested time window.</param>
        /// <param name="endUtc">UTC end of the requested time window.</param>
        /// <param name="channel">Preferred channel (e.g. "HHZ"). Null = use settings default.</param>
        /// <param name="settings">Optional settings override.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task<SeismogramData?> GetWaveformAsync(
            HttpClient httpClient,
            string stationCode,
            DateTime startUtc,
            DateTime endUtc,
            string? channel = null,
            EKCASettings? settings = null,
            CancellationToken ct = default)
        {
            LogMessage($"Fetching waveform for {stationCode} [{startUtc:u} — {endUtc:u}]...");
            try
            {
                var client = new EKCAClient(httpClient, settings);
                var data = await client.FetchWaveformAsync(stationCode, startUtc, endUtc, channel, ct)
                    .ConfigureAwait(false);
                if (data != null && data.HasData)
                    LogMessage($"Waveform: {data.Samples.Length} samples @ {data.SampleRateHz:F1} Hz ({data.Channel})");
                else
                    LogMessage($"No waveform data for {stationCode}.");
                return data;
            }
            catch (Exception ex)
            {
                LogMessage($"GetWaveformAsync failed: {ex.Message}");
                return new SeismogramData { StationCode = stationCode };
            }
        }

        /// <summary>
        /// Convenience method that fetches the most recent <paramref name="lastHours"/>
        /// hours of waveform data for the given station.
        /// </summary>
        /// <param name="httpClient">Shared <see cref="HttpClient"/> instance.</param>
        /// <param name="stationCode">FDSN station code, e.g. "OTT".</param>
        /// <param name="lastHours">Number of hours back from now (default 1).</param>
        /// <param name="channel">Preferred channel code. Null = use settings default.</param>
        /// <param name="settings">Optional settings override.</param>
        /// <param name="ct">Cancellation token.</param>
        public static Task<SeismogramData?> GetRecentWaveformAsync(
            HttpClient httpClient,
            string stationCode,
            int lastHours = 1,
            string? channel = null,
            EKCASettings? settings = null,
            CancellationToken ct = default)
        {
            var end = DateTime.UtcNow;
            var start = end.AddHours(-Math.Max(1, lastHours));
            return GetWaveformAsync(httpClient, stationCode, start, end, channel, settings, ct);
        }

        // ---------------------------------------------------------------------------
        // Utility helpers
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Correlates a list of stations with nearby events and updates each station's
        /// <see cref="SeismicStation.ActivityLevel"/>, <see cref="SeismicStation.LastEventTime"/>
        /// and <see cref="SeismicStation.LastEventMagnitude"/> properties.
        /// </summary>
        /// <param name="stations">Stations to update (mutated in-place).</param>
        /// <param name="events">Earthquake events to correlate against.</param>
        /// <param name="radiusKm">Radius within which a station is considered "near" an event.</param>
        public static void CorrelateEventsToStations(
            IList<SeismicStation> stations,
            IList<EarthquakeEvent> events,
            double radiusKm = 200.0)
        {
            foreach (var station in stations)
            {
                station.ActivityLevel = ActivityLevel.Normal;
                station.LastEventTime = null;
                station.LastEventMagnitude = null;
                station.NearestEventDistanceKm = null;

                foreach (var ev in events)
                {
                    double dist = CalculateDistanceKm(
                        station.Latitude, station.Longitude,
                        ev.Latitude, ev.Longitude);

                    if (dist > radiusKm) continue;

                    var age = DateTime.UtcNow - ev.OriginTime;

                    // Track nearest event
                    if (station.NearestEventDistanceKm == null || dist < station.NearestEventDistanceKm)
                    {
                        station.NearestEventDistanceKm = dist;
                        station.LastEventTime = ev.OriginTime;
                        station.LastEventMagnitude = ev.Magnitude;
                    }

                    // Determine activity level
                    ActivityLevel level;
                    if (ev.Magnitude >= 3.0 && age.TotalHours <= 24)
                        level = ActivityLevel.Active;
                    else if (ev.Magnitude >= 2.0 && age.TotalHours <= 24)
                        level = ActivityLevel.Elevated;
                    else if (ev.Magnitude >= 3.0 && age.TotalDays <= 7)
                        level = ActivityLevel.Elevated;
                    else
                        level = ActivityLevel.Normal;

                    if (level > station.ActivityLevel)
                        station.ActivityLevel = level;
                }
            }
        }

        /// <summary>
        /// Calculates the great-circle distance between two geographic points using the Haversine formula.
        /// </summary>
        public static double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            double dLat = ToRad(lat2 - lat1);
            double dLon = ToRad(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static double ToRad(double deg) => deg * Math.PI / 180.0;
    }
}
