#nullable enable
using System;
using Grib2.Models;

namespace Grib2.Decoder.Sections
{
    /// <summary>
    /// Section 3 — Grid Definition Section.
    /// Defines the spatial grid (template, dimensions, coordinates, scanning mode).
    /// Layout:
    ///   Octets 1–4:   Section length
    ///   Octet 5:      Section number (3)
    ///   Octet 6:      Source of grid definition (0 = specified in this section)
    ///   Octets 7–10:  Number of data points
    ///   Octet 11:     Number of octets for optional list of numbers defining groups
    ///   Octet 12:     Interpretation of optional list
    ///   Octets 13–14: Grid definition template number
    ///   Octets 15+:   Grid template data (varies by template)
    /// </summary>
    public static class GridDefinitionSection
    {
        /// <summary>Minimum section header length before template data.</summary>
        public const int MinHeaderLength = 14;

        /// <summary>
        /// Parse Section 3 and populate the grid object.
        /// </summary>
        /// <param name="data">Section data span.</param>
        /// <param name="grid">Grid object to populate.</param>
        /// <returns>Total section length in bytes.</returns>
        public static int Parse(ReadOnlySpan<byte> data, Grib2Grid grid)
        {
            if (data.Length < MinHeaderLength)
                throw new InvalidOperationException($"Section 3 requires at least {MinHeaderLength} bytes");

            int sectionLength = (int)data.ReadUInt32BE(0);
            byte sectionNumber = data[4];

            if (sectionNumber != 3)
                throw new InvalidOperationException($"Expected section 3, got section {sectionNumber}");

            // Octet 6: Source of grid definition
            // byte gridSource = data[5];

            grid.NumberOfDataPoints = (int)data.ReadUInt32BE(6);

            // Octets 11–12: optional list info (skip)
            grid.TemplateNumber = data.ReadUInt16BE(12);

            // Delegate to specific grid template parser
            ReadOnlySpan<byte> templateData = data.Slice(14);
            switch (grid.TemplateNumber)
            {
                case 0: // Latitude/longitude (equidistant cylindrical)
                    ParseLatLonTemplate(templateData, grid);
                    break;
                case 1: // Rotated latitude/longitude
                    ParseLatLonTemplate(templateData, grid);
                    ParseRotatedExtension(templateData, grid);
                    break;
                default:
                    // Unknown template — parse what we can
                    TryParseBasicGrid(templateData, grid);
                    break;
            }

            return sectionLength;
        }

        /// <summary>
        /// Parse Template 3.0 (and base of 3.1) — latitude/longitude grid.
        /// Template 3.0 layout (relative to template data start, 0-indexed):
        ///   Octets 0:     Shape of the earth
        ///   Octets 1:     Scale factor of radius
        ///   Octets 2–5:   Scaled value of radius
        ///   Octets 6:     Scale factor of major axis
        ///   Octets 7–10:  Scaled value of major axis
        ///   Octets 11:    Scale factor of minor axis
        ///   Octets 12–15: Scaled value of minor axis
        ///   Octets 16–19: Ni — number of points along parallel
        ///   Octets 20–23: Nj — number of points along meridian
        ///   Octets 24–27: Basic angle
        ///   Octets 28–31: Subdivisions of basic angle
        ///   Octets 32–35: Latitude of first grid point (signed, 10^-6 degrees)
        ///   Octets 36–39: Longitude of first grid point (signed, 10^-6 degrees)
        ///   Octet 40:     Resolution and component flags
        ///   Octets 41–44: Latitude of last grid point
        ///   Octets 45–48: Longitude of last grid point
        ///   Octets 49–52: Di — i-direction increment (10^-6 degrees)
        ///   Octets 53–56: Dj — j-direction increment (10^-6 degrees)
        ///   Octet 57:     Scanning mode
        /// </summary>
        private static void ParseLatLonTemplate(ReadOnlySpan<byte> template, Grib2Grid grid)
        {
            if (template.Length < 58)
                return;

            grid.Ni = (int)template.ReadUInt32BE(16);
            grid.Nj = (int)template.ReadUInt32BE(20);

            // Basic angle and subdivisions for sub-degree precision
            uint basicAngle = template.ReadUInt32BE(24);
            uint subdivisions = template.ReadUInt32BE(28);

            // Scale factor for lat/lon values
            double scale;
            if (basicAngle == 0 || basicAngle == uint.MaxValue)
                scale = 1e-6; // Default: values are in 10^-6 degrees
            else if (subdivisions == 0 || subdivisions == uint.MaxValue)
                scale = (double)basicAngle / 1e6;
            else
                scale = (double)basicAngle / subdivisions;

            grid.FirstLatitude = template.ReadSignedMagnitude32BE(32) * scale;
            grid.FirstLongitude = template.ReadSignedMagnitude32BE(36) * scale;

            grid.ResolutionFlags = template[40];

            grid.LastLatitude = template.ReadSignedMagnitude32BE(41) * scale;
            grid.LastLongitude = template.ReadSignedMagnitude32BE(45) * scale;

            grid.DiDegrees = template.ReadUInt32BE(49) * scale;
            grid.DjDegrees = template.ReadUInt32BE(53) * scale;

            grid.ScanningMode = template[57];
        }

        /// <summary>
        /// Parse the rotation extension for Template 3.1 (Rotated Latitude/Longitude).
        /// Extension data starts after the basic lat/lon template (octet 58+).
        ///   Octets 58–61: Latitude of southern pole (10^-6 degrees, signed)
        ///   Octets 62–65: Longitude of southern pole (10^-6 degrees, signed)
        ///   Octets 66–69: Angle of rotation (floating point)
        /// </summary>
        private static void ParseRotatedExtension(ReadOnlySpan<byte> template, Grib2Grid grid)
        {
            if (template.Length < 70)
                return;

            grid.RotatedPoleLat = template.ReadSignedMagnitude32BE(58) * 1e-6;
            grid.RotatedPoleLon = template.ReadSignedMagnitude32BE(62) * 1e-6;
            grid.RotationAngle = template.ReadFloat32BE(66);
        }

        /// <summary>
        /// Attempt to parse basic grid dimensions from an unknown template.
        /// Many templates share the same initial layout for Ni/Nj.
        /// </summary>
        private static void TryParseBasicGrid(ReadOnlySpan<byte> template, Grib2Grid grid)
        {
            if (template.Length >= 24)
            {
                grid.Ni = (int)template.ReadUInt32BE(16);
                grid.Nj = (int)template.ReadUInt32BE(20);
            }
        }
    }
}
