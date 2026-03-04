#nullable enable
using System;
using ILGPU;
using ILGPU.Runtime;

namespace Grib2.Compute.Kernels
{
    /// <summary>
    /// ILGPU kernels for bulk unit conversions.
    /// Element-wise conversions that benefit from GPU parallelism on large grids
    /// (e.g., GDPS = 1800×901 = 1.6M points).
    /// </summary>
    public static class UnitConversionKernels
    {
        /// <summary>
        /// Convert Kelvin to Celsius: °C = K - 273.15
        /// </summary>
        public static void KelvinToCelsiusKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> destination)
        {
            destination[index] = source[index] - 273.15f;
        }

        /// <summary>
        /// Convert Celsius to Kelvin: K = °C + 273.15
        /// </summary>
        public static void CelsiusToKelvinKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> destination)
        {
            destination[index] = source[index] + 273.15f;
        }

        /// <summary>
        /// Convert Pascal to hectoPascal (millibar): hPa = Pa × 0.01
        /// </summary>
        public static void PascalToHpaKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> destination)
        {
            destination[index] = source[index] * 0.01f;
        }

        /// <summary>
        /// Convert hectoPascal to Pascal: Pa = hPa × 100
        /// </summary>
        public static void HpaToPascalKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> destination)
        {
            destination[index] = source[index] * 100.0f;
        }

        /// <summary>
        /// Convert meters per second to kilometers per hour: km/h = m/s × 3.6
        /// </summary>
        public static void MsToKmhKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> destination)
        {
            destination[index] = source[index] * 3.6f;
        }

        /// <summary>
        /// Convert kilometers per hour to meters per second: m/s = km/h / 3.6
        /// </summary>
        public static void KmhToMsKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> destination)
        {
            destination[index] = source[index] / 3.6f;
        }

        /// <summary>
        /// Convert m/s to knots: kn = m/s × 1.94384
        /// </summary>
        public static void MsToKnotsKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> destination)
        {
            destination[index] = source[index] * 1.94384f;
        }

        /// <summary>
        /// Convert kg/m² (precipitation total) to mm.
        /// For liquid water: 1 kg/m² = 1 mm. This is an identity transform,
        /// but provided for clarity in the pipeline.
        /// </summary>
        public static void KgPerM2ToMmKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> destination)
        {
            destination[index] = source[index];
        }

        /// <summary>
        /// General linear transform: destination = source × scale + offset.
        /// </summary>
        public static void LinearTransformKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> destination,
            float scale,
            float offset)
        {
            destination[index] = source[index] * scale + offset;
        }

        /// <summary>
        /// Convert geopotential (m²/s²) to geopotential height (gpm = m²/s² / 9.80665).
        /// </summary>
        public static void GeopotentialToHeightKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> destination)
        {
            destination[index] = source[index] / 9.80665f;
        }
    }
}
