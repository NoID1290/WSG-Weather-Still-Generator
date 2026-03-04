#nullable enable
using System;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;

namespace Grib2.Compute.Kernels
{
    /// <summary>
    /// ILGPU kernels for computing derived meteorological fields.
    /// All kernels are element-wise operations over grid arrays.
    /// </summary>
    public static class DerivedFieldKernels
    {
        /// <summary>
        /// Compute wind speed from U and V components.
        /// speed = sqrt(u² + v²)
        /// </summary>
        public static void WindSpeedKernel(
            Index1D index,
            ArrayView<float> u,
            ArrayView<float> v,
            ArrayView<float> speed)
        {
            float ui = u[index];
            float vi = v[index];
            speed[index] = XMath.Sqrt(ui * ui + vi * vi);
        }

        /// <summary>
        /// Compute meteorological wind direction from U and V components.
        /// dir = (atan2(-u, -v) * 180/π) mod 360
        /// Result is direction wind is coming FROM in degrees (0=N, 90=E, 180=S, 270=W).
        /// </summary>
        public static void WindDirectionKernel(
            Index1D index,
            ArrayView<float> u,
            ArrayView<float> v,
            ArrayView<float> direction)
        {
            float ui = u[index];
            float vi = v[index];

            // atan2(-u, -v) gives direction wind is coming FROM
            float radians = XMath.Atan2(-ui, -vi);
            float degrees = radians * (180.0f / 3.14159265358979f);

            // Normalize to 0–360
            if (degrees < 0) degrees += 360.0f;
            direction[index] = degrees;
        }

        /// <summary>
        /// Compute wind chill using the Environment Canada / NWS formula.
        /// Wind Chill = 13.12 + 0.6215*T - 11.37*V^0.16 + 0.3965*T*V^0.16
        /// where T = temperature in °C, V = wind speed in km/h.
        /// Only valid for T ≤ 10°C and V ≥ 4.8 km/h.
        /// Input: temperature in °C, wind speed in km/h.
        /// </summary>
        public static void WindChillKernel(
            Index1D index,
            ArrayView<float> tempCelsius,
            ArrayView<float> windSpeedKmh,
            ArrayView<float> windChill)
        {
            float t = tempCelsius[index];
            float v = windSpeedKmh[index];

            if (t > 10.0f || v < 4.8f)
            {
                // Wind chill not applicable
                windChill[index] = t;
                return;
            }

            float vPow = XMath.Pow(v, 0.16f);
            windChill[index] = 13.12f + 0.6215f * t - 11.37f * vPow + 0.3965f * t * vPow;
        }

        /// <summary>
        /// Compute humidex using the Canadian formula.
        /// Humidex = T + 5/9 × (6.11 × exp(5417.7530 × (1/273.16 - 1/Td)) - 10)
        /// where T = temperature in °C, Td = dew point temperature in K.
        /// Input: temperature in °C, dew point in Kelvin.
        /// Only meaningful when humidex > T.
        /// </summary>
        public static void HumidexKernel(
            Index1D index,
            ArrayView<float> tempCelsius,
            ArrayView<float> dewPointKelvin,
            ArrayView<float> humidex)
        {
            float t = tempCelsius[index];
            float td = dewPointKelvin[index];

            // Compute vapor pressure from dew point
            float e = 6.11f * XMath.Exp(5417.7530f * (1.0f / 273.16f - 1.0f / td));
            float h = t + 0.5555556f * (e - 10.0f);

            // Humidex is only meaningful when > T and T > 20°C
            humidex[index] = (h > t && t > 20.0f) ? h : t;
        }

        /// <summary>
        /// Compute apparent temperature combining wind chill and humidex regimes.
        /// - T ≤ 10°C → wind chill
        /// - T ≥ 26°C → humidex
        /// - Otherwise → actual temperature
        /// Input: temperature in °C, wind chill in °C, humidex in °C.
        /// </summary>
        public static void ApparentTemperatureKernel(
            Index1D index,
            ArrayView<float> tempCelsius,
            ArrayView<float> windChillValues,
            ArrayView<float> humidexValues,
            ArrayView<float> apparentTemp)
        {
            float t = tempCelsius[index];

            if (t <= 10.0f)
                apparentTemp[index] = windChillValues[index];
            else if (t >= 26.0f)
                apparentTemp[index] = humidexValues[index];
            else
                apparentTemp[index] = t;
        }

        /// <summary>
        /// Compute dew point from temperature and relative humidity.
        /// Uses the Magnus formula:
        ///   γ = ln(RH/100) + (b × T)/(c + T)
        ///   Td = (c × γ)/(b − γ)
        /// where b = 17.67, c = 243.5°C, T in °C, RH in %.
        /// Input: temperature in °C, relative humidity in %.
        /// Output: dew point in °C.
        /// </summary>
        public static void DewPointKernel(
            Index1D index,
            ArrayView<float> tempCelsius,
            ArrayView<float> relativeHumidity,
            ArrayView<float> dewPoint)
        {
            const float b = 17.67f;
            const float c = 243.5f;

            float t = tempCelsius[index];
            float rh = relativeHumidity[index];

            // Clamp RH to avoid log(0)
            if (rh < 0.01f) rh = 0.01f;
            if (rh > 100.0f) rh = 100.0f;

            float gamma = XMath.Log(rh / 100.0f) + (b * t) / (c + t);
            dewPoint[index] = (c * gamma) / (b - gamma);
        }
    }
}
