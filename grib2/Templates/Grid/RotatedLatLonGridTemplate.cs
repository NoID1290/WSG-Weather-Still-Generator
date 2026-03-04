#nullable enable
using System;
using Grib2.Models;

namespace Grib2.Templates.Grid
{
    /// <summary>
    /// Grid Definition Template 3.1 — Rotated Latitude/Longitude.
    /// Used when a model runs on a rotated coordinate system (e.g., some regional models).
    /// Extends Template 3.0 with a rotated pole definition.
    /// </summary>
    public static class RotatedLatLonGridTemplate
    {
        /// <summary>Template number for this grid type.</summary>
        public const int TemplateNumber = 1;

        /// <summary>
        /// Convert rotated coordinates to geographic (true) coordinates.
        /// </summary>
        /// <param name="rotLat">Latitude in the rotated grid (degrees).</param>
        /// <param name="rotLon">Longitude in the rotated grid (degrees).</param>
        /// <param name="southPoleLat">Latitude of the south pole of the rotated grid (degrees).</param>
        /// <param name="southPoleLon">Longitude of the south pole of the rotated grid (degrees).</param>
        /// <param name="rotationAngle">Angle of rotation (degrees). Often 0.</param>
        /// <returns>Geographic (lat, lon) in degrees.</returns>
        public static (double Latitude, double Longitude) RotatedToGeographic(
            double rotLat, double rotLon,
            double southPoleLat, double southPoleLon,
            double rotationAngle = 0.0)
        {
            const double deg2rad = Math.PI / 180.0;
            const double rad2deg = 180.0 / Math.PI;

            double sinPoleLat = Math.Sin(southPoleLat * deg2rad);
            double cosPoleLat = Math.Cos(southPoleLat * deg2rad);

            // Apply rotation angle first
            double adjustedLon = rotLon - rotationAngle;

            double sinRotLat = Math.Sin(rotLat * deg2rad);
            double cosRotLat = Math.Cos(rotLat * deg2rad);
            double sinRotLon = Math.Sin(adjustedLon * deg2rad);
            double cosRotLon = Math.Cos(adjustedLon * deg2rad);

            // True latitude
            double sinTrueLat = cosPoleLat * sinRotLat - sinPoleLat * cosRotLat * cosRotLon;
            sinTrueLat = Math.Clamp(sinTrueLat, -1.0, 1.0);
            double trueLat = Math.Asin(sinTrueLat) * rad2deg;

            // True longitude
            double cosNum = cosRotLat * cosRotLon * cosPoleLat + sinRotLat * sinPoleLat;
            double sinNum = cosRotLat * sinRotLon;
            double trueLon = Math.Atan2(sinNum, cosNum) * rad2deg + southPoleLon;

            // Normalize longitude to [-180, 180]
            if (trueLon > 180.0) trueLon -= 360.0;
            if (trueLon < -180.0) trueLon += 360.0;

            return (trueLat, trueLon);
        }

        /// <summary>
        /// Convert geographic coordinates to rotated coordinates.
        /// </summary>
        public static (double RotatedLat, double RotatedLon) GeographicToRotated(
            double trueLat, double trueLon,
            double southPoleLat, double southPoleLon,
            double rotationAngle = 0.0)
        {
            const double deg2rad = Math.PI / 180.0;
            const double rad2deg = 180.0 / Math.PI;

            double sinPoleLat = Math.Sin(southPoleLat * deg2rad);
            double cosPoleLat = Math.Cos(southPoleLat * deg2rad);

            double lonDiff = (trueLon - southPoleLon) * deg2rad;
            double sinTrueLat = Math.Sin(trueLat * deg2rad);
            double cosTrueLat = Math.Cos(trueLat * deg2rad);
            double sinLonDiff = Math.Sin(lonDiff);
            double cosLonDiff = Math.Cos(lonDiff);

            // Rotated latitude
            double sinRotLat = cosPoleLat * sinTrueLat + sinPoleLat * cosTrueLat * cosLonDiff;
            sinRotLat = Math.Clamp(sinRotLat, -1.0, 1.0);
            double rotLat = Math.Asin(sinRotLat) * rad2deg;

            // Rotated longitude
            double cosNum = cosTrueLat * cosLonDiff * cosPoleLat - sinTrueLat * sinPoleLat;
            double sinNum = cosTrueLat * sinLonDiff;
            double rotLon = Math.Atan2(sinNum, cosNum) * rad2deg + rotationAngle;

            if (rotLon > 180.0) rotLon -= 360.0;
            if (rotLon < -180.0) rotLon += 360.0;

            return (rotLat, rotLon);
        }

        /// <summary>
        /// Compute all grid point coordinates in geographic (true) coordinates.
        /// </summary>
        public static (double[] Latitudes, double[] Longitudes) ComputeGeographicCoordinates(Grib2Grid grid)
        {
            if (!grid.RotatedPoleLat.HasValue || !grid.RotatedPoleLon.HasValue)
                throw new InvalidOperationException("Grid does not have rotation parameters (not Template 3.1)");

            int count = grid.Ni * grid.Nj;
            var lats = new double[count];
            var lons = new double[count];

            double poleLat = grid.RotatedPoleLat.Value;
            double poleLon = grid.RotatedPoleLon.Value;
            double rotAngle = grid.RotationAngle ?? 0.0;

            for (int i = 0; i < count; i++)
            {
                var (rotLat, rotLon) = grid.GetLatLon(i);
                var (geoLat, geoLon) = RotatedToGeographic(rotLat, rotLon, poleLat, poleLon, rotAngle);
                lats[i] = geoLat;
                lons[i] = geoLon;
            }

            return (lats, lons);
        }
    }
}
