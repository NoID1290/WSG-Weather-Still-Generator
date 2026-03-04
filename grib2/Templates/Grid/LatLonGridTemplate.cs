#nullable enable
using System;
using Grib2.Models;

namespace Grib2.Templates.Grid
{
    /// <summary>
    /// Grid Definition Template 3.0 — Latitude/Longitude (equidistant cylindrical, or Plate Carrée).
    /// The most common grid template, used by ECCC GDPS, RDPS, and many other NWP models.
    /// </summary>
    public static class LatLonGridTemplate
    {
        /// <summary>Template number for this grid type.</summary>
        public const int TemplateNumber = 0;

        /// <summary>
        /// Compute latitude and longitude for every grid point.
        /// Returns parallel arrays of lat[] and lon[] in degrees.
        /// </summary>
        public static (double[] Latitudes, double[] Longitudes) ComputeCoordinates(Grib2Grid grid)
        {
            int count = grid.Ni * grid.Nj;
            var lats = new double[count];
            var lons = new double[count];

            for (int i = 0; i < count; i++)
            {
                var (lat, lon) = grid.GetLatLon(i);
                lats[i] = lat;
                lons[i] = lon;
            }

            return (lats, lons);
        }

        /// <summary>
        /// Perform bilinear interpolation on grid data at a target (lat, lon).
        /// Returns NaN if the point is outside the grid.
        /// </summary>
        public static float Interpolate(Grib2Grid grid, float[] values, double targetLat, double targetLon)
        {
            if (grid.Ni <= 1 || grid.Nj <= 1 || grid.DiDegrees <= 0 || grid.DjDegrees <= 0)
                return float.NaN;

            // Compute fractional grid position
            double fracCol = grid.IDirectionPositive
                ? (targetLon - grid.FirstLongitude) / grid.DiDegrees
                : (grid.FirstLongitude - targetLon) / grid.DiDegrees;

            double fracRow = grid.JDirectionPositive
                ? (targetLat - grid.FirstLatitude) / grid.DjDegrees
                : (grid.FirstLatitude - targetLat) / grid.DjDegrees;

            // Check bounds
            if (fracCol < 0 || fracCol >= grid.Ni - 1 || fracRow < 0 || fracRow >= grid.Nj - 1)
                return float.NaN;

            int col0 = (int)fracCol;
            int row0 = (int)fracRow;
            int col1 = col0 + 1;
            int row1 = row0 + 1;

            double dx = fracCol - col0;
            double dy = fracRow - row0;

            // 4 corner indices
            int i00 = grid.RowMajor ? row0 * grid.Ni + col0 : col0 * grid.Nj + row0;
            int i10 = grid.RowMajor ? row0 * grid.Ni + col1 : col1 * grid.Nj + row0;
            int i01 = grid.RowMajor ? row1 * grid.Ni + col0 : col0 * grid.Nj + row1;
            int i11 = grid.RowMajor ? row1 * grid.Ni + col1 : col1 * grid.Nj + row1;

            // Check that indices are valid and values are not NaN
            if (i00 >= values.Length || i10 >= values.Length || i01 >= values.Length || i11 >= values.Length)
                return float.NaN;

            float v00 = values[i00];
            float v10 = values[i10];
            float v01 = values[i01];
            float v11 = values[i11];

            if (float.IsNaN(v00) || float.IsNaN(v10) || float.IsNaN(v01) || float.IsNaN(v11))
                return float.NaN;

            // Bilinear interpolation
            float result = (float)(
                v00 * (1 - dx) * (1 - dy) +
                v10 * dx * (1 - dy) +
                v01 * (1 - dx) * dy +
                v11 * dx * dy);

            return result;
        }
    }
}
