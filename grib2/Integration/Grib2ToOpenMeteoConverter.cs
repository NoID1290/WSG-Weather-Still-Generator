#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Grib2.Models;
using OpenMeteo;

namespace Grib2.Integration
{
    /// <summary>
    /// Maps decoded GRIB2 fields to the <see cref="OpenMeteo.WeatherForecast"/> model.
    /// Populates <see cref="Hourly"/> surface and pressure-level arrays from GRIB2 messages
    /// decoded from ECCC Datamart (GDPS, RDPS, HRDPS) or other GRIB2 sources.
    /// </summary>
    public class Grib2ToOpenMeteoConverter
    {
        /// <summary>
        /// Known pressure levels in the OpenMeteo Hourly model (hPa).
        /// </summary>
        private static readonly int[] PressureLevels =
        [
            1000, 975, 950, 925, 900, 850, 800, 700, 600, 500,
            400, 300, 250, 200, 150, 100, 70, 50, 30
        ];

        /// <summary>
        /// Convert a collection of GRIB2 messages from a single model run
        /// into a <see cref="WeatherForecast"/> with populated <see cref="Hourly"/> data.
        /// </summary>
        /// <param name="messages">All decoded GRIB2 messages from the model run.</param>
        /// <param name="lat">Station/grid latitude for metadata.</param>
        /// <param name="lon">Station/grid longitude for metadata.</param>
        /// <param name="elevation">Station elevation in meters (optional).</param>
        /// <returns>A WeatherForecast with hourly data populated from GRIB2 fields.</returns>
        public WeatherForecast Convert(
            IEnumerable<Grib2Message> messages, float lat, float lon, float elevation = 0f)
        {
            var msgList = messages.ToList();
            if (msgList.Count == 0)
                return CreateEmptyForecast(lat, lon, elevation);

            // Group messages by forecast hour
            var byHour = GroupByForecastHour(msgList);
            int hourCount = byHour.Count;
            var sortedHours = byHour.Keys.OrderBy(h => h).ToList();

            // Find reference time from first message
            var refTime = msgList.First().Metadata.ReferenceTime;

            // Build time array
            var times = new string[hourCount];
            for (int i = 0; i < sortedHours.Count; i++)
            {
                var forecastTime = refTime.AddHours(sortedHours[i]);
                times[i] = forecastTime.ToString("yyyy-MM-ddTHH:mm");
            }

            var hourly = new Hourly { Time = times };

            // Populate surface fields
            PopulateSurfaceFields(hourly, byHour, sortedHours);

            // Populate pressure-level fields
            PopulatePressureLevelFields(hourly, byHour, sortedHours);

            return new WeatherForecast
            {
                Latitude = lat,
                Longitude = lon,
                Elevation = elevation,
                GenerationTime = 0,
                Timezone = "UTC",
                TimezoneAbbreviation = "UTC",
                UtcOffset = 0,
                Hourly = hourly
            };
        }

        /// <summary>
        /// Convert a single GRIB2 field value from native GRIB2 units to OpenMeteo units.
        /// </summary>
        private static float ConvertUnit(float value, int discipline, int category, int number)
        {
            // Temperature: K → °C
            if (discipline == 0 && category == 0 && (number == 0 || number == 4 || number == 5 || number == 6))
                return value - 273.15f;

            // Pressure: Pa → hPa
            if (discipline == 0 && category == 3 && (number == 0 || number == 1))
                return value * 0.01f;

            // No conversion needed for: RH (%), wind (m/s stays as m/s for OpenMeteo),
            // precipitation (kg/m² = mm), cloud cover (%)
            return value;
        }

        /// <summary>
        /// Convert wind speed from m/s to km/h for OpenMeteo compatibility.
        /// </summary>
        private static float WindMsToKmh(float ms) => ms * 3.6f;

        /// <summary>
        /// Group messages by forecast hour, extracting hour from each field.
        /// </summary>
        private static SortedDictionary<int, List<(Grib2Field Field, Grib2Grid Grid, Grib2Metadata Meta)>> 
            GroupByForecastHour(List<Grib2Message> messages)
        {
            var result = new SortedDictionary<int, List<(Grib2Field, Grib2Grid, Grib2Metadata)>>();

            foreach (var msg in messages)
            {
                int hour = msg.Field.ForecastHour;
                if (!result.TryGetValue(hour, out var list))
                {
                    list = [];
                    result[hour] = list;
                }
                list.Add((msg.Field, msg.Grid, msg.Metadata));
            }

            return result;
        }

        /// <summary>
        /// Find a field value at a specific forecast hour.
        /// Returns the first value of the grid (index 0) — for point forecasts,
        /// caller should pre-interpolate or use index nearest to target.
        /// </summary>
        private static float? GetFieldValue(
            List<(Grib2Field Field, Grib2Grid Grid, Grib2Metadata Meta)> hourFields,
            int category, int number, int surfaceType = 103, int discipline = 0)
        {
            var match = hourFields.FirstOrDefault(f =>
                f.Field.Discipline == discipline &&
                f.Field.ParameterCategory == category &&
                f.Field.ParameterNumber == number &&
                (surfaceType < 0 || f.Field.SurfaceType == surfaceType));

            if (match.Field?.Values == null || match.Field.Values.Length == 0)
                return null;

            // Return first grid point value (caller should handle interpolation)
            return match.Field.Values[0];
        }

        /// <summary>
        /// Find a field for a specific pressure level.
        /// Surface type 100 = isobaric surface, value in Pa.
        /// </summary>
        private static float? GetPressureLevelValue(
            List<(Grib2Field Field, Grib2Grid Grid, Grib2Metadata Meta)> hourFields,
            int category, int number, int pressureHpa, int discipline = 0)
        {
            int pressurePa = pressureHpa * 100;

            var match = hourFields.FirstOrDefault(f =>
                f.Field.Discipline == discipline &&
                f.Field.ParameterCategory == category &&
                f.Field.ParameterNumber == number &&
                f.Field.SurfaceType == 100 &&
                Math.Abs(f.Field.SurfaceValue - pressurePa) < 50);

            if (match.Field?.Values == null || match.Field.Values.Length == 0)
                return null;

            return match.Field.Values[0];
        }

        /// <summary>
        /// Populate surface-level Hourly fields.
        /// </summary>
        private void PopulateSurfaceFields(
            Hourly hourly,
            SortedDictionary<int, List<(Grib2Field Field, Grib2Grid Grid, Grib2Metadata Meta)>> byHour,
            List<int> sortedHours)
        {
            int n = sortedHours.Count;

            var temp2m = new float?[n];
            var rh2m = new int?[n];
            var dewpoint2m = new float?[n];
            var apparent = new float?[n];
            var precip = new float?[n];
            var rain = new float?[n];
            var snowfall = new float?[n];
            var pressure = new float?[n];
            var surfacePressure = new float?[n];
            var cloudcover = new int?[n];
            var cloudLow = new int?[n];
            var cloudMid = new int?[n];
            var cloudHigh = new int?[n];
            var windSpeed10m = new float?[n];
            var windDir10m = new int?[n];
            var windGusts = new float?[n];
            var visibility = new float?[n];
            var cape = new float?[n];

            for (int i = 0; i < n; i++)
            {
                var fields = byHour[sortedHours[i]];

                // Temperature 2m: cat=0, num=0, surface type 103 (height above ground), value ~2
                var tempVal = GetFieldValue(fields, 0, 0, 103);
                temp2m[i] = tempVal.HasValue ? tempVal.Value - 273.15f : null;

                // Relative humidity 2m: cat=1, num=1
                var rhVal = GetFieldValue(fields, 1, 1, 103);
                rh2m[i] = rhVal.HasValue ? (int)MathF.Round(rhVal.Value) : null;

                // Dew point 2m: cat=0, num=6
                var dpVal = GetFieldValue(fields, 0, 6, 103);
                dewpoint2m[i] = dpVal.HasValue ? dpVal.Value - 273.15f : null;

                // Total precipitation: cat=1, num=8
                var precipVal = GetFieldValue(fields, 1, 8, -1);
                precip[i] = precipVal;

                // Rain: cat=1, num=65
                var rainVal = GetFieldValue(fields, 1, 65, -1);
                rain[i] = rainVal;

                // Snowfall: cat=1, num=60
                var snowVal = GetFieldValue(fields, 1, 60, -1);
                snowfall[i] = snowVal;

                // Pressure MSL: cat=3, num=0 (surface type 101 = mean sea level) or num=1
                var pressVal = GetFieldValue(fields, 3, 0, 101);
                pressVal ??= GetFieldValue(fields, 3, 1, 101);
                pressure[i] = pressVal.HasValue ? pressVal.Value * 0.01f : null;

                // Surface pressure: cat=3, num=0, surface type 1 (ground)
                var spVal = GetFieldValue(fields, 3, 0, 1);
                surfacePressure[i] = spVal.HasValue ? spVal.Value * 0.01f : null;

                // Cloud cover: cat=6, num=1
                var ccVal = GetFieldValue(fields, 6, 1, -1);
                cloudcover[i] = ccVal.HasValue ? (int)MathF.Round(ccVal.Value) : null;

                // Cloud cover low: cat=6, num=3 (surface type 214 = low cloud layer)
                var ccLowVal = GetFieldValue(fields, 6, 3, -1);
                cloudLow[i] = ccLowVal.HasValue ? (int)MathF.Round(ccLowVal.Value) : null;

                // Cloud cover mid: cat=6, num=4
                var ccMidVal = GetFieldValue(fields, 6, 4, -1);
                cloudMid[i] = ccMidVal.HasValue ? (int)MathF.Round(ccMidVal.Value) : null;

                // Cloud cover high: cat=6, num=5
                var ccHighVal = GetFieldValue(fields, 6, 5, -1);
                cloudHigh[i] = ccHighVal.HasValue ? (int)MathF.Round(ccHighVal.Value) : null;

                // Wind: need U (cat=2, num=2) and V (cat=2, num=3) at 10m
                var uVal = GetFieldValue(fields, 2, 2, 103);
                var vVal = GetFieldValue(fields, 2, 3, 103);
                if (uVal.HasValue && vVal.HasValue)
                {
                    float u = uVal.Value;
                    float v = vVal.Value;
                    float speed = MathF.Sqrt(u * u + v * v);
                    windSpeed10m[i] = speed * 3.6f; // km/h

                    float dirRad = MathF.Atan2(-u, -v);
                    float dirDeg = dirRad * (180f / MathF.PI);
                    if (dirDeg < 0) dirDeg += 360f;
                    windDir10m[i] = (int)MathF.Round(dirDeg);
                }

                // Wind gusts: cat=2, num=22 (gusts)
                var gustVal = GetFieldValue(fields, 2, 22, 103);
                windGusts[i] = gustVal.HasValue ? gustVal.Value * 3.6f : null;

                // Visibility: cat=19, num=0
                var visVal = GetFieldValue(fields, 19, 0, -1);
                visibility[i] = visVal;

                // CAPE: cat=7, num=6
                var capeVal = GetFieldValue(fields, 7, 6, -1);
                cape[i] = capeVal;

                // Apparent temperature (derived)
                if (temp2m[i].HasValue && windSpeed10m[i].HasValue)
                {
                    float t = temp2m[i]!.Value;
                    float ws = windSpeed10m[i]!.Value;
                    if (t <= 10f && ws >= 4.8f)
                    {
                        float vPow = MathF.Pow(ws, 0.16f);
                        apparent[i] = 13.12f + 0.6215f * t - 11.37f * vPow + 0.3965f * t * vPow;
                    }
                    else
                    {
                        apparent[i] = t;
                    }
                }
            }

            hourly.Temperature_2m = temp2m;
            hourly.Relativehumidity_2m = rh2m;
            hourly.Dewpoint_2m = dewpoint2m;
            hourly.Apparent_temperature = apparent;
            hourly.Precipitation = precip;
            hourly.Rain = rain;
            hourly.Snowfall = snowfall;
            hourly.Pressure_msl = pressure;
            hourly.Surface_pressure = surfacePressure;
            hourly.Cloudcover = cloudcover;
            hourly.Cloudcover_low = cloudLow;
            hourly.Cloudcover_mid = cloudMid;
            hourly.Cloudcover_high = cloudHigh;
            hourly.Windspeed_10m = windSpeed10m;
            hourly.Winddirection_10m = windDir10m;
            hourly.Windgusts_10m = windGusts;
            hourly.Visibility = visibility;
            hourly.Cape = cape;
        }

        /// <summary>
        /// Populate pressure-level Hourly fields via reflection-like property mapping.
        /// Each pressure level maps to specific Hourly properties like Temperature_500hPa, etc.
        /// </summary>
        private void PopulatePressureLevelFields(
            Hourly hourly,
            SortedDictionary<int, List<(Grib2Field Field, Grib2Grid Grid, Grib2Metadata Meta)>> byHour,
            List<int> sortedHours)
        {
            int n = sortedHours.Count;

            foreach (int level in PressureLevels)
            {
                var tempArr = new float?[n];
                var dpArr = new float?[n];
                var rhArr = new int?[n];
                var wsArr = new float?[n];
                var wdArr = new int?[n];
                var ghArr = new float?[n];
                var ccArr = new int?[n];

                bool hasTemp = false, hasDp = false, hasRh = false;
                bool hasWs = false, hasGh = false, hasCc = false;

                for (int i = 0; i < n; i++)
                {
                    var fields = byHour[sortedHours[i]];

                    // Temperature at pressure level
                    var tempVal = GetPressureLevelValue(fields, 0, 0, level);
                    if (tempVal.HasValue)
                    {
                        tempArr[i] = tempVal.Value - 273.15f;
                        hasTemp = true;
                    }

                    // Dew point at pressure level
                    var dpVal = GetPressureLevelValue(fields, 0, 6, level);
                    if (dpVal.HasValue)
                    {
                        dpArr[i] = dpVal.Value - 273.15f;
                        hasDp = true;
                    }

                    // Relative humidity at pressure level
                    var rhVal = GetPressureLevelValue(fields, 1, 1, level);
                    if (rhVal.HasValue)
                    {
                        rhArr[i] = (int)MathF.Round(rhVal.Value);
                        hasRh = true;
                    }

                    // Wind U/V at pressure level → speed + direction
                    var uVal = GetPressureLevelValue(fields, 2, 2, level);
                    var vVal = GetPressureLevelValue(fields, 2, 3, level);
                    if (uVal.HasValue && vVal.HasValue)
                    {
                        float u = uVal.Value;
                        float v = vVal.Value;
                        wsArr[i] = MathF.Sqrt(u * u + v * v) * 3.6f;
                        float dirRad = MathF.Atan2(-u, -v);
                        float dirDeg = dirRad * (180f / MathF.PI);
                        if (dirDeg < 0) dirDeg += 360f;
                        wdArr[i] = (int)MathF.Round(dirDeg);
                        hasWs = true;
                    }

                    // Geopotential height: cat=3, num=5
                    var ghVal = GetPressureLevelValue(fields, 3, 5, level);
                    if (ghVal.HasValue)
                    {
                        ghArr[i] = ghVal.Value;
                        hasGh = true;
                    }

                    // Cloud cover at pressure level: cat=6, num=22
                    var ccVal = GetPressureLevelValue(fields, 6, 22, level);
                    ccVal ??= GetPressureLevelValue(fields, 6, 1, level);
                    if (ccVal.HasValue)
                    {
                        ccArr[i] = (int)MathF.Round(ccVal.Value);
                        hasCc = true;
                    }
                }

                // Assign to Hourly properties by pressure level
                if (hasTemp) SetPressureLevelArray(hourly, "Temperature", level, tempArr);
                if (hasDp) SetPressureLevelArray(hourly, "Dewpoint", level, dpArr);
                if (hasRh) SetPressureLevelIntArray(hourly, "Relativehumidity", level, rhArr);
                if (hasWs)
                {
                    SetPressureLevelArray(hourly, "Windspeed", level, wsArr);
                    SetPressureLevelIntArray(hourly, "Winddirection", level, wdArr);
                }
                if (hasGh) SetPressureLevelArray(hourly, "Geopotential_height", level, ghArr);
                if (hasCc) SetPressureLevelIntArray(hourly, "Cloudcover", level, ccArr);
            }
        }

        /// <summary>
        /// Set a float?[] pressure-level property on Hourly via reflection.
        /// Property names follow pattern: {prefix}_{level}hPa (e.g., Temperature_500hPa).
        /// </summary>
        private static void SetPressureLevelArray(Hourly hourly, string prefix, int level, float?[] values)
        {
            string propName = $"{prefix}_{level}hPa";
            var prop = typeof(Hourly).GetProperty(propName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (prop != null)
            {
                try
                {
                    if (prop.PropertyType == typeof(float?[]))
                        prop.SetValue(hourly, values);
                    else if (prop.PropertyType == typeof(object?[]))
                    {
                        // Handle the oddball Dewpoint_30hPa which is object?[]
                        var objArr = new object?[values.Length];
                        for (int i = 0; i < values.Length; i++)
                            objArr[i] = values[i];
                        prop.SetValue(hourly, objArr);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Grib2Converter] Failed to set {propName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Set an int?[] pressure-level property on Hourly via reflection.
        /// </summary>
        private static void SetPressureLevelIntArray(Hourly hourly, string prefix, int level, int?[] values)
        {
            string propName = $"{prefix}_{level}hPa";
            var prop = typeof(Hourly).GetProperty(propName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (prop != null && prop.PropertyType == typeof(int?[]))
            {
                try
                {
                    prop.SetValue(hourly, values);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Grib2Converter] Failed to set {propName}: {ex.Message}");
                }
            }
        }

        private static WeatherForecast CreateEmptyForecast(float lat, float lon, float elevation)
        {
            return new WeatherForecast
            {
                Latitude = lat,
                Longitude = lon,
                Elevation = elevation,
                GenerationTime = 0,
                Timezone = "UTC",
                TimezoneAbbreviation = "UTC",
                UtcOffset = 0,
                Hourly = new Hourly { Time = [] }
            };
        }
    }
}
