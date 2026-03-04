#nullable enable
using System;

namespace Grib2.Models
{
    /// <summary>
    /// Grid definition from GRIB2 Section 3. Describes the spatial layout of grid points.
    /// </summary>
    public class Grib2Grid
    {
        /// <summary>Grid definition template number (e.g., 0 = lat/lon, 1 = rotated lat/lon).</summary>
        public int TemplateNumber { get; set; }

        /// <summary>Number of grid points along the parallel (longitude, columns).</summary>
        public int Ni { get; set; }

        /// <summary>Number of grid points along the meridian (latitude, rows).</summary>
        public int Nj { get; set; }

        /// <summary>Total number of data points in the grid.</summary>
        public int NumberOfDataPoints { get; set; }

        /// <summary>Latitude of the first grid point in degrees (scaled by 10^-6 in binary).</summary>
        public double FirstLatitude { get; set; }

        /// <summary>Longitude of the first grid point in degrees.</summary>
        public double FirstLongitude { get; set; }

        /// <summary>Latitude of the last grid point in degrees.</summary>
        public double LastLatitude { get; set; }

        /// <summary>Longitude of the last grid point in degrees.</summary>
        public double LastLongitude { get; set; }

        /// <summary>Longitude increment in degrees.</summary>
        public double DiDegrees { get; set; }

        /// <summary>Latitude increment in degrees.</summary>
        public double DjDegrees { get; set; }

        /// <summary>Scanning mode flags (octet 72 of template 3.0). Bits control row/column order.</summary>
        public byte ScanningMode { get; set; }

        /// <summary>Resolution and component flags (octet 55).</summary>
        public byte ResolutionFlags { get; set; }

        // --- Rotated grid fields (Template 3.1) ---

        /// <summary>Latitude of the southern pole of rotation (degrees). Only for template 3.1.</summary>
        public double? RotatedPoleLat { get; set; }

        /// <summary>Longitude of the southern pole of rotation (degrees). Only for template 3.1.</summary>
        public double? RotatedPoleLon { get; set; }

        /// <summary>Angle of rotation in degrees. Only for template 3.1.</summary>
        public double? RotationAngle { get; set; }

        // --- Scanning mode helpers ---

        /// <summary>True if points scan in +i (west→east) direction (bit 1 of scanning mode = 0).</summary>
        public bool IDirectionPositive => (ScanningMode & 0x80) == 0;

        /// <summary>True if points scan in +j (south→north) direction (bit 2 of scanning mode = 1).</summary>
        public bool JDirectionPositive => (ScanningMode & 0x40) != 0;

        /// <summary>True if adjacent points are in the i-direction (row-major). Bit 3 = 0.</summary>
        public bool RowMajor => (ScanningMode & 0x20) == 0;

        /// <summary>
        /// Get the (lat, lon) pair for a given linear grid index.
        /// Assumes a regular lat/lon grid (template 3.0).
        /// </summary>
        public (double Latitude, double Longitude) GetLatLon(int index)
        {
            int row, col;
            if (RowMajor)
            {
                row = index / Ni;
                col = index % Ni;
            }
            else
            {
                col = index / Nj;
                row = index % Nj;
            }

            double lat = JDirectionPositive
                ? FirstLatitude + row * DjDegrees
                : FirstLatitude - row * DjDegrees;

            double lon = IDirectionPositive
                ? FirstLongitude + col * DiDegrees
                : FirstLongitude - col * DiDegrees;

            return (lat, lon);
        }

        /// <summary>
        /// Get the nearest grid index for a given (lat, lon) pair.
        /// Returns -1 if the point is outside the grid bounds.
        /// </summary>
        public int GetNearestIndex(double latitude, double longitude)
        {
            if (Ni <= 0 || Nj <= 0 || DiDegrees <= 0 || DjDegrees <= 0)
                return -1;

            // Compute fractional row/col
            double fracCol = IDirectionPositive
                ? (longitude - FirstLongitude) / DiDegrees
                : (FirstLongitude - longitude) / DiDegrees;

            double fracRow = JDirectionPositive
                ? (latitude - FirstLatitude) / DjDegrees
                : (FirstLatitude - latitude) / DjDegrees;

            int col = (int)Math.Round(fracCol);
            int row = (int)Math.Round(fracRow);

            if (col < 0 || col >= Ni || row < 0 || row >= Nj)
                return -1;

            return RowMajor ? row * Ni + col : col * Nj + row;
        }

        public override string ToString()
            => $"Grid Template {TemplateNumber}: {Ni}×{Nj} " +
               $"({FirstLatitude:F3},{FirstLongitude:F3})→({LastLatitude:F3},{LastLongitude:F3}) " +
               $"Δi={DiDegrees:F4}° Δj={DjDegrees:F4}°";
    }
}
