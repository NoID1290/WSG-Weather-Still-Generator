using System;

namespace WeatherImageGenerator.Utilities
{
    /// <summary>
    /// Computes solar position for day/night terminator rendering.
    /// Uses standard astronomical algorithms for solar declination and hour angle.
    /// </summary>
    public static class SolarPosition
    {
        /// <summary>
        /// Calculates solar declination and Greenwich hour angle for the given UTC time.
        /// These values are passed as shader uniforms for the day/night terminator effect.
        /// </summary>
        /// <param name="utcNow">Current time in UTC</param>
        /// <returns>
        /// Declination in degrees (-23.45 to +23.45),
        /// SubsolarLon in degrees (-180 to +180) — the longitude where the sun is directly overhead
        /// </returns>
        public static (double DeclinationDeg, double SubsolarLonDeg) Calculate(DateTime utcNow)
        {
            // Day of year (fractional)
            int dayOfYear = utcNow.DayOfYear;
            double fractionalDay = dayOfYear + (utcNow.Hour + utcNow.Minute / 60.0 + utcNow.Second / 3600.0) / 24.0;

            // Solar declination (Spencer formula, simplified)
            double gamma = 2.0 * Math.PI * (fractionalDay - 1) / 365.0;
            double declination = 0.006918
                - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
                - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
                - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);
            double decDeg = declination * 180.0 / Math.PI;

            // Subsolar longitude — based on UTC time
            // At 12:00 UTC, the sun is over 0° longitude
            double hoursSinceNoon = (utcNow.Hour - 12) + utcNow.Minute / 60.0 + utcNow.Second / 3600.0;
            // Equation of time correction (in hours)
            double eqTime = EquationOfTime(gamma) / 60.0; // minutes → hours
            double subsolarLon = -(hoursSinceNoon + eqTime) * 15.0; // 15°/hour

            // Normalize to [-180, 180]
            while (subsolarLon > 180) subsolarLon -= 360;
            while (subsolarLon < -180) subsolarLon += 360;

            return (decDeg, subsolarLon);
        }

        /// <summary>
        /// Equation of time in minutes (corrects for Earth's orbital eccentricity and axial tilt).
        /// </summary>
        private static double EquationOfTime(double gamma)
        {
            return 229.18 * (0.000075
                + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                - 0.014615 * Math.Cos(2 * gamma) - 0.04089 * Math.Sin(2 * gamma));
        }

        /// <summary>
        /// Computes the solar elevation angle at a given lat/lon.
        /// Positive = sun above horizon, negative = below.
        /// </summary>
        public static double SolarElevation(double latDeg, double lonDeg, DateTime utcNow)
        {
            var (decDeg, subsolarLon) = Calculate(utcNow);
            double decRad = decDeg * Math.PI / 180.0;
            double latRad = latDeg * Math.PI / 180.0;
            double hourAngleRad = (lonDeg - subsolarLon) * Math.PI / 180.0;

            double sinElev = Math.Sin(latRad) * Math.Sin(decRad)
                           + Math.Cos(latRad) * Math.Cos(decRad) * Math.Cos(hourAngleRad);

            return Math.Asin(Math.Clamp(sinElev, -1, 1)) * 180.0 / Math.PI;
        }
    }
}
