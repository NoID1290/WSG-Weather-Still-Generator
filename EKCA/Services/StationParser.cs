#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using EKCA.Models;

namespace EKCA.Services
{
    /// <summary>
    /// Parses the pipe-delimited text response from the FDSN Station web service.
    /// Format: #Network|Station|Latitude|Longitude|Elevation|SiteName|StartTime|EndTime
    /// </summary>
    internal static class StationParser
    {
        // ---------------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Parses the raw FDSN station query text and returns a de-duplicated list of
        /// <see cref="SeismicStation"/> objects, one per unique station code.
        /// </summary>
        public static List<SeismicStation> ParseStationText(string rawText)
        {
            var all = new List<SeismicStation>();

            foreach (var line in rawText.Split('\n'))
            {
                var trimmed = line.Trim();

                // Skip comment / header lines
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    continue;

                var station = ParseLine(trimmed);
                if (station != null)
                    all.Add(station);
            }

            return Deduplicate(all);
        }

        // ---------------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------------

        private static SeismicStation? ParseLine(string line)
        {
            // Network|Station|Latitude|Longitude|Elevation|SiteName|StartTime|EndTime
            var parts = line.Split('|');
            if (parts.Length < 7) return null;

            if (!double.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double lat)) return null;
            if (!double.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double lon)) return null;
            if (!double.TryParse(parts[4].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double elev))
                elev = 0;

            DateTime? endTime = null;
            if (parts.Length >= 8 && !string.IsNullOrWhiteSpace(parts[7].Trim()))
            {
                if (DateTime.TryParse(parts[7].Trim(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var parsedEnd))
                {
                    endTime = parsedEnd;
                }
            }

            DateTime startTime = DateTime.MinValue;
            if (DateTime.TryParse(parts[6].Trim(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsedStart))
            {
                startTime = parsedStart;
            }

            return new SeismicStation
            {
                Network = parts[0].Trim(),
                StationCode = parts[1].Trim(),
                Latitude = lat,
                Longitude = lon,
                ElevationM = elev,
                SiteName = parts[5].Trim().Replace("\r", "").Replace(",\n", ", "),
                StartTime = startTime,
                EndTime = endTime,
                ActivityLevel = ActivityLevel.Normal
            };
        }

        /// <summary>
        /// For each station code, keeps the entry that:
        ///   1. Has no EndTime (still active), preferring the most recent StartTime, OR
        ///   2. Has the latest EndTime (most recently decommissioned).
        /// This mirrors the pick-latest logic used by ECCC.Data.CityDatabase.
        /// </summary>
        private static List<SeismicStation> Deduplicate(List<SeismicStation> all)
        {
            var grouped = all.GroupBy(s => $"{s.Network}.{s.StationCode}");
            var result = new List<SeismicStation>(grouped.Count());

            foreach (var group in grouped)
            {
                // First preference: open-ended (active) entries, pick the latest StartTime
                var active = group.Where(s => !s.EndTime.HasValue)
                                  .OrderByDescending(s => s.StartTime)
                                  .FirstOrDefault();
                if (active != null)
                {
                    result.Add(active);
                    continue;
                }

                // Fallback: the epoch that ended most recently
                var latest = group.OrderByDescending(s => s.EndTime).FirstOrDefault();
                if (latest != null)
                    result.Add(latest);
            }

            return result.OrderBy(s => s.StationCode).ToList();
        }
    }
}
