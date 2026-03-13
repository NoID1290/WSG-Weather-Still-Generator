#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EKCA.Models;

namespace EKCA.Services
{
    /// <summary>
    /// Parses the Earthquakes Canada Atom feed into <see cref="EarthquakeEvent"/> objects.
    /// Feed URL: https://www.earthquakescanada.nrcan.gc.ca/cache/earthquakes/canada-en.atom
    /// </summary>
    internal static class AtomFeedParser
    {
        // Pre-compiled regexes
        // Handles: "M = 4.2", "M=4.2", "M4.2", "M 4.2", "Magnitude 4.2"
        private static readonly Regex MagnitudeRegex =
            new(@"(?:M(?:agnitude)?\s*=?\s*)([0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LocationFromTitleRegex =
            new(@"(?:M(?:agnitude)?\s*=?\s*[0-9.]+)\s*[-–—]\s*(.+)$", RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex DepthRegex =
            new(@"depth.*?([0-9]+(?:\.[0-9]+)?)\s*km", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // GeoRSS namespace
        private static readonly XNamespace GeoRssNs = "http://www.georss.org/georss";
        private static readonly XNamespace AtomNs = "http://www.w3.org/2005/Atom";

        // ---------------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Parses the XML content of the Earthquakes Canada Atom feed and returns a list of events
        /// sorted by origin time descending (most recent first).
        /// </summary>
        public static List<EarthquakeEvent> ParseAtomFeed(string xmlContent)
        {
            var events = new List<EarthquakeEvent>();

            XDocument doc;
            try
            {
                doc = XDocument.Parse(xmlContent);
            }
            catch
            {
                return events;
            }

            var root = doc.Root;
            if (root == null) return events;

            // Handle both namespaced and un-namespaced Atom documents
            var ns = root.Name.Namespace;

            foreach (var entry in root.Elements(ns + "entry"))
            {
                var ev = ParseEntry(entry, ns);
                if (ev != null)
                    events.Add(ev);
            }

            // Sort most recent first
            events.Sort((a, b) => b.OriginTime.CompareTo(a.OriginTime));
            return events;
        }

        // ---------------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------------

        private static EarthquakeEvent? ParseEntry(XElement entry, XNamespace ns)
        {
            var title = entry.Element(ns + "title")?.Value ?? string.Empty;
            var id = entry.Element(ns + "id")?.Value ?? string.Empty;
            var link = entry.Element(ns + "link")?.Attribute("href")?.Value;
            var updatedStr = entry.Element(ns + "updated")?.Value;
            var summary = entry.Element(ns + "summary")?.Value ?? string.Empty;

            // Parse magnitude from title; fall back to summary if missing
            double magnitude = 0;
            var magMatch = MagnitudeRegex.Match(title);
            if (magMatch.Success)
                double.TryParse(magMatch.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out magnitude);

            if (magnitude <= 0)
            {
                // Some entries embed magnitude only in the summary
                var magSummaryMatch = MagnitudeRegex.Match(summary);
                if (magSummaryMatch.Success)
                    double.TryParse(magSummaryMatch.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out magnitude);
            }

            if (magnitude <= 0) return null; // Truly malformed entry — skip

            // Parse location description from title
            string location = string.Empty;
            var locMatch = LocationFromTitleRegex.Match(title);
            if (locMatch.Success)
                location = locMatch.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(location))
                location = title;

            // Parse coordinates from <georss:point>LAT LON</georss:point>
            double lat = 0, lon = 0;
            var point = entry.Element(GeoRssNs + "point")?.Value
                     ?? entry.Element("point")?.Value;
            if (!string.IsNullOrWhiteSpace(point))
            {
                var parts = point.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out lat);
                    double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out lon);
                }
            }

            // Fallback: try to extract lat/lon from the summary text
            if (lat == 0 && lon == 0)
                TryExtractCoordsFromText(summary, out lat, out lon);

            // Parse depth from summary
            double depth = 0;
            var depthMatch = DepthRegex.Match(summary);
            if (depthMatch.Success)
                double.TryParse(depthMatch.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out depth);

            // Parse origin time from <updated> (best approximation since there is no separate <published>)
            DateTime originTime = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(updatedStr))
            {
                if (DateTime.TryParse(updatedStr,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var parsed))
                {
                    originTime = parsed;
                }
            }

            return new EarthquakeEvent
            {
                EventId = id,
                Title = title,
                Magnitude = magnitude,
                Latitude = lat,
                Longitude = lon,
                DepthKm = depth,
                OriginTime = originTime,
                Location = location,
                DetailUrl = link
            };
        }

        /// <summary>
        /// Attempts to extract lat/lon coordinates embedded in summary text.
        /// Looks for patterns like "Latitude: 60.753" and "Longitude: -137.518".
        /// </summary>
        private static void TryExtractCoordsFromText(string text, out double lat, out double lon)
        {
            lat = 0; lon = 0;
            var latMatch = Regex.Match(text, @"[Ll]at(?:itude)?[:\s]+(-?[0-9.]+)");
            var lonMatch = Regex.Match(text, @"[Ll]on(?:gitude)?[:\s]+(-?[0-9.]+)");
            if (latMatch.Success)
                double.TryParse(latMatch.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out lat);
            if (lonMatch.Success)
                double.TryParse(lonMatch.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out lon);
        }
    }
}
