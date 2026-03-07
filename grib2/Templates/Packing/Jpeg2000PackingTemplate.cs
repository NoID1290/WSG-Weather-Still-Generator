#nullable enable
using System;
using System.IO;
using CSJ2K;
using Grib2.Models;

namespace Grib2.Templates.Packing
{
    /// <summary>
    /// Data Representation Template 5.40 — JPEG2000 Code Stream Packing.
    /// Grid values are packed as a JPEG2000 code stream embedded in Section 7.
    /// The decoded pixel values are raw unsigned integers that must be
    /// decoded with the standard scale formula: Y = R + X × 2^E × 10^(-D).
    /// Uses CSJ2K for JPEG2000 decompression.
    /// </summary>
    public static class Jpeg2000PackingTemplate
    {
        /// <summary>
        /// Unpack Section 7 data using JPEG2000 Packing (Template 5.40).
        /// </summary>
        public static float[] Unpack(ReadOnlySpan<byte> packedData, Grib2Field field, int numberOfDataPoints)
        {
            if (packedData.Length == 0)
            {
                var empty = new float[numberOfDataPoints];
                Array.Fill(empty, float.NaN);
                return empty;
            }

            // Special case: 0 bits per value means constant field
            if (field.BitsPerValue == 0)
            {
                var constant = new float[numberOfDataPoints];
                Array.Fill(constant, field.ReferenceValue);
                return constant;
            }

            float R = field.ReferenceValue;
            float binaryScale = MathF.Pow(2.0f, field.BinaryScaleFactor);
            float decimalScale = MathF.Pow(10.0f, -field.DecimalScaleFactor);

            // Decode JPEG2000 code stream using CSJ2K
            var image = J2kImage.FromBytes(packedData.ToArray());
            if (image == null)
                throw new InvalidOperationException("Failed to decode JPEG2000 from Section 7 data");

            var values = new float[numberOfDataPoints];

            // GRIB2 J2K is single-component (grayscale); each pixel is a raw integer
            int numComponents = image.NumberOfComponents;
            var component0 = image.GetComponent(0);

            if (component0 == null || component0.Length == 0)
                throw new InvalidOperationException(
                    $"JPEG2000 component0 empty. Components={numComponents}, PackedLen={packedData.Length}, Expected={numberOfDataPoints}");

            // Validate decoded size matches expected; log mismatch
            if (component0.Length != numberOfDataPoints)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[J2K] Size mismatch: component0={component0.Length}, expected={numberOfDataPoints}, BPV={field.BitsPerValue}");
            }

            // Detect all-zero component (CSJ2K decode failure symptom)
            bool allZero = true;
            int checkCount = Math.Min(100, component0.Length);
            for (int i = 0; i < checkCount; i++)
            {
                if (component0[i] != 0) { allZero = false; break; }
            }
            if (allZero && component0.Length > 1)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[J2K] WARNING: All component0 values are 0 — possible decode failure. " +
                    $"R={R}, E={field.BinaryScaleFactor}, D={field.DecimalScaleFactor}, BPV={field.BitsPerValue}, " +
                    $"PackedBytes={packedData.Length}, Pixels={component0.Length}");
            }

            for (int i = 0; i < numberOfDataPoints; i++)
            {
                if (i >= component0.Length)
                {
                    values[i] = float.NaN;
                    continue;
                }

                uint raw = (uint)component0[i];
                values[i] = (R + raw * binaryScale) * decimalScale;
            }

            return values;
        }
    }
}
