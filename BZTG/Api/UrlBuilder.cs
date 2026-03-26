#nullable enable
using System;
using System.Globalization;

namespace BZTG.Api
{
    /// <summary>
    /// Provides URL builders for the Blitzortung.org lightning data archive.
    /// </summary>
    public static class UrlBuilder
    {
        /// <summary>Base URL for the Blitzortung data archive.</summary>
        public const string BaseUrl = "https://data.blitzortung.org";

        /// <summary>
        /// Region 1 covers the Americas (Canada, USA, Central and South America).
        /// This is the correct region for ECCC/Canadian coverage.
        /// </summary>
        public const int AmericasRegion = 1;

        /// <summary>
        /// Builds an archive URL for a specific UTC minute in the Americas region.
        /// Files are gzip-compressed NDJSON — one JSON object per line per strike.
        /// Returns HTTP 404 when no strikes were recorded for that minute.
        /// </summary>
        /// <param name="minute">The UTC minute to fetch (seconds/sub-seconds are ignored).</param>
        public static string BuildStrikeUrl(DateTime minute)
        {
            var utc = minute.ToUniversalTime();
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}/Data_{1}/archive/{2:yyyy}/{2:MM}/{2:dd}/{2:HH}/{2:mm}.json.gz",
                BaseUrl, AmericasRegion, utc);
        }
    }
}
