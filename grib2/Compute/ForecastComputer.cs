#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ILGPU;
using ILGPU.Runtime;
using Grib2.Compute.Kernels;
using Grib2.Models;

namespace Grib2.Compute
{
    /// <summary>
    /// Orchestrates GPU kernel dispatch for GRIB2 forecast computation.
    /// Takes decoded fields from <see cref="Grib2Field"/>, uploads grid data to GPU,
    /// dispatches kernel sequences (unit conversion → derived fields → interpolation),
    /// and downloads results.
    /// </summary>
    public sealed class ForecastComputer : IDisposable
    {
        private readonly GpuContext _gpu;
        private bool _disposed;

        // Compiled kernel delegates
        private readonly Action<Index1D, ArrayView<float>, ArrayView<float>> _kelvinToCelsius;
        private readonly Action<Index1D, ArrayView<float>, ArrayView<float>> _pascalToHpa;
        private readonly Action<Index1D, ArrayView<float>, ArrayView<float>> _msToKmh;
        private readonly Action<Index1D, ArrayView<float>, ArrayView<float>> _geopotentialToHeight;
        private readonly Action<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>> _windSpeed;
        private readonly Action<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>> _windDirection;
        private readonly Action<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>> _windChill;
        private readonly Action<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>> _humidex;
        private readonly Action<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>> _dewPoint;
        private readonly Action<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>> _apparentTemp;
        private readonly Action<Index1D, ArrayView<float>, ArrayView<float>, int, int, ArrayView<float>, ArrayView<float>, float, float, float, float> _bilinear;

        /// <summary>The underlying GPU context.</summary>
        public GpuContext Gpu => _gpu;

        public ForecastComputer(GpuContext? gpu = null)
        {
            _gpu = gpu ?? GpuContext.Instance;

            var acc = _gpu.Accelerator;

            // Compile all kernels up front
            _kelvinToCelsius = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                UnitConversionKernels.KelvinToCelsiusKernel);
            _pascalToHpa = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                UnitConversionKernels.PascalToHpaKernel);
            _msToKmh = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                UnitConversionKernels.MsToKmhKernel);
            _geopotentialToHeight = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                UnitConversionKernels.GeopotentialToHeightKernel);
            _windSpeed = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(
                DerivedFieldKernels.WindSpeedKernel);
            _windDirection = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(
                DerivedFieldKernels.WindDirectionKernel);
            _windChill = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(
                DerivedFieldKernels.WindChillKernel);
            _humidex = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(
                DerivedFieldKernels.HumidexKernel);
            _dewPoint = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(
                DerivedFieldKernels.DewPointKernel);
            _apparentTemp = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>>(
                DerivedFieldKernels.ApparentTemperatureKernel);
            _bilinear = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, int, int, ArrayView<float>, ArrayView<float>, float, float, float, float>(
                InterpolationKernels.BilinearKernel);

            Debug.WriteLine($"[ForecastComputer] Compiled {12} kernels on {_gpu.DeviceName}");
        }

        #region Unit Conversions

        /// <summary>Convert Kelvin array to Celsius on GPU.</summary>
        public float[] KelvinToCelsius(float[] kelvin)
        {
            int n = kelvin.Length;
            using var src = _gpu.AllocateAndUpload(kelvin);
            using var dst = _gpu.Allocate<float>(n);
            _kelvinToCelsius(n, src.View, dst.View);
            _gpu.Synchronize();
            return _gpu.Download(dst);
        }

        /// <summary>Convert Pascal array to hPa on GPU.</summary>
        public float[] PascalToHpa(float[] pascal)
        {
            int n = pascal.Length;
            using var src = _gpu.AllocateAndUpload(pascal);
            using var dst = _gpu.Allocate<float>(n);
            _pascalToHpa(n, src.View, dst.View);
            _gpu.Synchronize();
            return _gpu.Download(dst);
        }

        /// <summary>Convert m/s array to km/h on GPU.</summary>
        public float[] MsToKmh(float[] ms)
        {
            int n = ms.Length;
            using var src = _gpu.AllocateAndUpload(ms);
            using var dst = _gpu.Allocate<float>(n);
            _msToKmh(n, src.View, dst.View);
            _gpu.Synchronize();
            return _gpu.Download(dst);
        }

        /// <summary>Convert geopotential (m²/s²) to geopotential height (gpm) on GPU.</summary>
        public float[] GeopotentialToHeight(float[] geopotential)
        {
            int n = geopotential.Length;
            using var src = _gpu.AllocateAndUpload(geopotential);
            using var dst = _gpu.Allocate<float>(n);
            _geopotentialToHeight(n, src.View, dst.View);
            _gpu.Synchronize();
            return _gpu.Download(dst);
        }

        #endregion

        #region Derived Fields

        /// <summary>Compute wind speed from U/V components on GPU.</summary>
        public float[] ComputeWindSpeed(float[] u, float[] v)
        {
            if (u.Length != v.Length)
                throw new ArgumentException("U and V arrays must have same length");

            int n = u.Length;
            using var uBuf = _gpu.AllocateAndUpload(u);
            using var vBuf = _gpu.AllocateAndUpload(v);
            using var dst = _gpu.Allocate<float>(n);
            _windSpeed(n, uBuf.View, vBuf.View, dst.View);
            _gpu.Synchronize();
            return _gpu.Download(dst);
        }

        /// <summary>Compute wind direction from U/V components on GPU. Returns degrees (0=N, 90=E).</summary>
        public float[] ComputeWindDirection(float[] u, float[] v)
        {
            if (u.Length != v.Length)
                throw new ArgumentException("U and V arrays must have same length");

            int n = u.Length;
            using var uBuf = _gpu.AllocateAndUpload(u);
            using var vBuf = _gpu.AllocateAndUpload(v);
            using var dst = _gpu.Allocate<float>(n);
            _windDirection(n, uBuf.View, vBuf.View, dst.View);
            _gpu.Synchronize();
            return _gpu.Download(dst);
        }

        /// <summary>Compute wind chill on GPU. Input: temp in °C, wind in km/h.</summary>
        public float[] ComputeWindChill(float[] tempCelsius, float[] windKmh)
        {
            if (tempCelsius.Length != windKmh.Length)
                throw new ArgumentException("Arrays must have same length");

            int n = tempCelsius.Length;
            using var tBuf = _gpu.AllocateAndUpload(tempCelsius);
            using var wBuf = _gpu.AllocateAndUpload(windKmh);
            using var dst = _gpu.Allocate<float>(n);
            _windChill(n, tBuf.View, wBuf.View, dst.View);
            _gpu.Synchronize();
            return _gpu.Download(dst);
        }

        /// <summary>Compute humidex on GPU. Input: temp in °C, dew point in K.</summary>
        public float[] ComputeHumidex(float[] tempCelsius, float[] dewPointKelvin)
        {
            if (tempCelsius.Length != dewPointKelvin.Length)
                throw new ArgumentException("Arrays must have same length");

            int n = tempCelsius.Length;
            using var tBuf = _gpu.AllocateAndUpload(tempCelsius);
            using var dBuf = _gpu.AllocateAndUpload(dewPointKelvin);
            using var dst = _gpu.Allocate<float>(n);
            _humidex(n, tBuf.View, dBuf.View, dst.View);
            _gpu.Synchronize();
            return _gpu.Download(dst);
        }

        /// <summary>Compute dew point on GPU. Input: temp in °C, RH in %.</summary>
        public float[] ComputeDewPoint(float[] tempCelsius, float[] relativeHumidity)
        {
            if (tempCelsius.Length != relativeHumidity.Length)
                throw new ArgumentException("Arrays must have same length");

            int n = tempCelsius.Length;
            using var tBuf = _gpu.AllocateAndUpload(tempCelsius);
            using var rhBuf = _gpu.AllocateAndUpload(relativeHumidity);
            using var dst = _gpu.Allocate<float>(n);
            _dewPoint(n, tBuf.View, rhBuf.View, dst.View);
            _gpu.Synchronize();
            return _gpu.Download(dst);
        }

        /// <summary>Compute apparent temperature on GPU combining wind chill & humidex.</summary>
        public float[] ComputeApparentTemperature(float[] tempCelsius, float[] windChill, float[] humidex)
        {
            if (tempCelsius.Length != windChill.Length || tempCelsius.Length != humidex.Length)
                throw new ArgumentException("Arrays must have same length");

            int n = tempCelsius.Length;
            using var tBuf = _gpu.AllocateAndUpload(tempCelsius);
            using var wcBuf = _gpu.AllocateAndUpload(windChill);
            using var hBuf = _gpu.AllocateAndUpload(humidex);
            using var dst = _gpu.Allocate<float>(n);
            _apparentTemp(n, tBuf.View, wcBuf.View, hBuf.View, dst.View);
            _gpu.Synchronize();
            return _gpu.Download(dst);
        }

        #endregion

        #region Interpolation

        /// <summary>
        /// Bilinear interpolation — extract values at target lat/lon points from a regular grid.
        /// </summary>
        /// <param name="gridValues">Source grid values (row-major, Ni×Nj).</param>
        /// <param name="ni">Number of columns in source grid.</param>
        /// <param name="nj">Number of rows in source grid.</param>
        /// <param name="lat0">Latitude of first grid point (degrees).</param>
        /// <param name="lon0">Longitude of first grid point (degrees).</param>
        /// <param name="dLat">Latitude increment (degrees).</param>
        /// <param name="dLon">Longitude increment (degrees).</param>
        /// <param name="targetLats">Latitudes to interpolate to.</param>
        /// <param name="targetLons">Longitudes to interpolate to.</param>
        public float[] Interpolate(float[] gridValues, int ni, int nj,
            float lat0, float lon0, float dLat, float dLon,
            float[] targetLats, float[] targetLons)
        {
            if (targetLats.Length != targetLons.Length)
                throw new ArgumentException("Target lat/lon arrays must have same length");

            int n = targetLats.Length;
            using var srcBuf = _gpu.AllocateAndUpload(gridValues);
            using var outBuf = _gpu.Allocate<float>(n);
            using var latBuf = _gpu.AllocateAndUpload(targetLats);
            using var lonBuf = _gpu.AllocateAndUpload(targetLons);

            _bilinear(n, srcBuf.View, outBuf.View, ni, nj,
                latBuf.View, lonBuf.View, lat0, lon0, dLat, dLon);
            _gpu.Synchronize();
            return _gpu.Download(outBuf);
        }

        /// <summary>
        /// Extract a single point forecast from a grid field using bilinear interpolation.
        /// </summary>
        public float InterpolatePoint(Grib2Field field, Grib2Grid grid, float lat, float lon)
        {
            if (field.Values == null || grid.Ni == 0 || grid.Nj == 0)
                return float.NaN;

            var result = Interpolate(
                field.Values, grid.Ni, grid.Nj,
                (float)grid.FirstLatitude, (float)grid.FirstLongitude,
                (float)grid.DjDegrees, (float)grid.DiDegrees,
                [lat], [lon]);

            return result[0];
        }

        #endregion

        #region High-level Operations

        /// <summary>
        /// Compute a full point forecast from multiple GRIB2 fields.
        /// Finds matching fields by parameter, converts units, and computes derived values.
        /// </summary>
        /// <param name="fields">Decoded GRIB2 fields from the same model run.</param>
        /// <param name="grid">The grid definition shared by these fields.</param>
        /// <param name="lat">Target latitude.</param>
        /// <param name="lon">Target longitude.</param>
        /// <returns>Dictionary of variable name → value at the target point.</returns>
        public Dictionary<string, float> ComputePointForecast(
            IEnumerable<Grib2Field> fields, Grib2Grid grid, float lat, float lon)
        {
            var result = new Dictionary<string, float>();
            var fieldList = fields.ToList();

            // Find key fields by WMO parameter codes
            var tempField = FindField(fieldList, 0, 0, 0);     // Temperature [K]
            var uWindField = FindField(fieldList, 0, 2, 2);    // U-wind [m/s]
            var vWindField = FindField(fieldList, 0, 2, 3);    // V-wind [m/s]
            var rhField = FindField(fieldList, 0, 1, 1);       // Relative humidity [%]
            var pressField = FindField(fieldList, 0, 3, 0);    // Pressure [Pa]
            var precipField = FindField(fieldList, 0, 1, 8);   // Total precipitation [kg/m²]
            var cloudField = FindField(fieldList, 0, 6, 1);    // Total cloud cover [%]

            // Temperature → Celsius
            if (tempField != null)
            {
                float tempK = InterpolatePoint(tempField, grid, lat, lon);
                result["temperature_2m"] = tempK - 273.15f;
            }

            // Wind (U, V → speed + direction)
            if (uWindField != null && vWindField != null)
            {
                float u = InterpolatePoint(uWindField, grid, lat, lon);
                float v = InterpolatePoint(vWindField, grid, lat, lon);
                float speed = MathF.Sqrt(u * u + v * v);
                float dirRad = MathF.Atan2(-u, -v);
                float dirDeg = dirRad * (180f / MathF.PI);
                if (dirDeg < 0) dirDeg += 360f;

                result["windspeed_10m"] = speed * 3.6f; // km/h
                result["winddirection_10m"] = dirDeg;
            }

            // Relative humidity
            if (rhField != null)
                result["relativehumidity_2m"] = InterpolatePoint(rhField, grid, lat, lon);

            // Pressure → hPa
            if (pressField != null)
                result["pressure_msl"] = InterpolatePoint(pressField, grid, lat, lon) * 0.01f;

            // Precipitation
            if (precipField != null)
                result["precipitation"] = InterpolatePoint(precipField, grid, lat, lon);

            // Cloud cover
            if (cloudField != null)
                result["cloudcover"] = InterpolatePoint(cloudField, grid, lat, lon);

            // Derived: apparent temperature
            if (result.ContainsKey("temperature_2m") && result.ContainsKey("windspeed_10m"))
            {
                float t = result["temperature_2m"];
                float ws = result["windspeed_10m"];

                if (t <= 10f && ws >= 4.8f)
                {
                    float vPow = MathF.Pow(ws, 0.16f);
                    result["apparent_temperature"] = 13.12f + 0.6215f * t - 11.37f * vPow + 0.3965f * t * vPow;
                }
                else
                {
                    result["apparent_temperature"] = t;
                }
            }

            return result;
        }

        /// <summary>
        /// Compute derived fields over the entire grid (wind speed, direction, etc.)
        /// and return as new Grib2Field instances.
        /// </summary>
        public List<Grib2Field> ComputeGridDerivedFields(IEnumerable<Grib2Field> fields)
        {
            var derived = new List<Grib2Field>();
            var fieldList = fields.ToList();

            var uWind = FindField(fieldList, 0, 2, 2);
            var vWind = FindField(fieldList, 0, 2, 3);

            if (uWind?.Values != null && vWind?.Values != null &&
                uWind.Values.Length == vWind.Values.Length)
            {
                // Wind speed
                var speed = ComputeWindSpeed(uWind.Values, vWind.Values);
                derived.Add(new Grib2Field
                {
                    Discipline = 0,
                    ParameterCategory = 2,
                    ParameterNumber = 1, // Wind speed
                    ParameterName = "Wind Speed",
                    ParameterUnit = "m/s",
                    ForecastHour = uWind.ForecastHour,
                    SurfaceType = uWind.SurfaceType,
                    SurfaceValue = uWind.SurfaceValue,
                    Values = speed
                });

                // Wind direction
                var dir = ComputeWindDirection(uWind.Values, vWind.Values);
                derived.Add(new Grib2Field
                {
                    Discipline = 0,
                    ParameterCategory = 2,
                    ParameterNumber = 0, // Wind direction
                    ParameterName = "Wind Direction",
                    ParameterUnit = "°",
                    ForecastHour = uWind.ForecastHour,
                    SurfaceType = uWind.SurfaceType,
                    SurfaceValue = uWind.SurfaceValue,
                    Values = dir
                });
            }

            return derived;
        }

        #endregion

        #region Helpers

        /// <summary>Find a field by WMO parameter code (discipline, category, number).</summary>
        private static Grib2Field? FindField(List<Grib2Field> fields,
            int discipline, int category, int number)
        {
            return fields.FirstOrDefault(f =>
                f.Discipline == discipline &&
                f.ParameterCategory == category &&
                f.ParameterNumber == number);
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                // Note: we don't dispose _gpu here if using the singleton
            }
        }
    }
}
