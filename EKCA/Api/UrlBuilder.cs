#nullable enable
using System;
using System.Globalization;

namespace EKCA.Api
{
    /// <summary>
    /// Builds URLs for Earthquakes Canada FDSN web services and public data feeds.
    /// </summary>
    public static class UrlBuilder
    {
        // ---------------------------------------------------------------------------
        // Base URLs
        // ---------------------------------------------------------------------------

        /// <summary>Base URL for Earthquakes Canada FDSN web services.</summary>
        public const string FdsnBaseUrl = "https://www.earthquakescanada.nrcan.gc.ca/fdsnws";

        /// <summary>Atom feed of significant earthquakes in Canada — updated frequently.</summary>
        public const string AtomFeedUrl = "https://www.earthquakescanada.nrcan.gc.ca/cache/earthquakes/canada-en.atom";

        /// <summary>All earthquakes (broader combined feed).</summary>
        public const string AllQuakesFeedUrl = "https://www.earthquakescanada.nrcan.gc.ca/cache/earthquakes/canada-en.atom";

        // ---------------------------------------------------------------------------
        // FDSN Station service
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Builds a FDSN station query URL that returns pipe-delimited text.
        /// </summary>
        /// <param name="network">Seismograph network code (default "CN" = Canadian National Seismograph Network).</param>
        /// <param name="format">Response format — "text" (pipe-delimited), "xml" (StationXML), "json".</param>
        /// <param name="level">Detail level — "network", "station", "channel", "response".</param>
        /// <param name="endTimeAfter">Only include stations active after this UTC time (optional).</param>
        public static string BuildStationQueryUrl(
            string network = "CN",
            string format = "text",
            string level = "station",
            DateTime? endTimeAfter = null)
        {
            var url = $"{FdsnBaseUrl}/station/1/query?network={network}&format={format}&level={level}";
            if (endTimeAfter.HasValue)
                url += $"&endbefore={endTimeAfter.Value.AddSeconds(-1).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}";
            return url;
        }

        /// <summary>
        /// Builds a FDSN station query URL limited to currently active stations only.
        /// </summary>
        public static string BuildActiveStationsUrl(string network = "CN", string format = "text")
            => $"{FdsnBaseUrl}/station/1/query?network={network}&format={format}&level=station&endafter={DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}";

        // ---------------------------------------------------------------------------
        // FDSN Dataselect (waveform) service
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Builds a FDSN Dataselect URL to retrieve MiniSEED waveform data.
        /// </summary>
        /// <param name="network">Network code (e.g. "CN").</param>
        /// <param name="station">Station code (e.g. "OTT").</param>
        /// <param name="location">Location code ("*" = any, "--" = blank).</param>
        /// <param name="channel">Channel code (e.g. "HHZ", "BHZ", "EHZ").</param>
        /// <param name="startTime">Start of requested time window (UTC).</param>
        /// <param name="endTime">End of requested time window (UTC).</param>
        public static string BuildWaveformUrl(
            string network,
            string station,
            string location,
            string channel,
            DateTime startTime,
            DateTime endTime)
        {
            var start = startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            var end = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            return $"{FdsnBaseUrl}/dataselect/1/query" +
                   $"?network={network}&station={station}&location={location}&channel={channel}" +
                   $"&starttime={start}&endtime={end}";
        }

        /// <summary>
        /// Convenience overload using wildcard location ("*") and a duration offset.
        /// </summary>
        public static string BuildWaveformUrl(
            string network,
            string station,
            string channel,
            DateTime startTime,
            DateTime endTime)
            => BuildWaveformUrl(network, station, "*", channel, startTime, endTime);

        // ---------------------------------------------------------------------------
        // Atom feed
        // ---------------------------------------------------------------------------

        /// <summary>Returns the Earthquakes Canada Atom feed URL.</summary>
        public static string BuildAtomFeedUrl() => AtomFeedUrl;

        /// <summary>
        /// Builds the URL of a specific earthquake event detail page.
        /// </summary>
        /// <param name="yearFolder">e.g. "2026"</param>
        /// <param name="eventFolder">e.g. "20260214.0403"</param>
        public static string BuildEventDetailUrl(string yearFolder, string eventFolder)
            => $"https://www.earthquakescanada.nrcan.gc.ca/recent/{yearFolder}/{eventFolder}/index-en.php";
    }
}
