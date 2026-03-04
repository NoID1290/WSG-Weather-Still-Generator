#nullable enable
using System;
using System.IO;
using Grib2.Models;
using SkiaSharp;

namespace Grib2.Templates.Packing
{
    /// <summary>
    /// Data Representation Template 5.40 — PNG Packing.
    /// Grid values are packed as a PNG image embedded in Section 7.
    /// The PNG pixel values are raw unsigned integers that must be
    /// decoded with the standard scale formula: Y = R + X × 2^E × 10^(-D).
    /// Uses SkiaSharp for PNG decompression (available via OpenMap project reference).
    /// </summary>
    public static class PngPackingTemplate
    {
        /// <summary>
        /// Unpack Section 7 data using PNG Packing (Template 5.40).
        /// </summary>
        /// <param name="packedData">Raw packed bytes from Section 7 (PNG image).</param>
        /// <param name="field">Field with packing metadata (R, E, D).</param>
        /// <param name="numberOfDataPoints">Expected number of data points.</param>
        /// <returns>Unpacked float array.</returns>
        public static float[] Unpack(ReadOnlySpan<byte> packedData, Grib2Field field, int numberOfDataPoints)
        {
            if (packedData.Length == 0)
            {
                var empty = new float[numberOfDataPoints];
                Array.Fill(empty, float.NaN);
                return empty;
            }

            // Special case: 0 bits per value
            if (field.BitsPerValue == 0)
            {
                var constant = new float[numberOfDataPoints];
                Array.Fill(constant, field.ReferenceValue);
                return constant;
            }

            float R = field.ReferenceValue;
            float binaryScale = MathF.Pow(2.0f, field.BinaryScaleFactor);
            float decimalScale = MathF.Pow(10.0f, -field.DecimalScaleFactor);

            // Decode PNG using SkiaSharp
            using var bitmap = SKBitmap.Decode(packedData);
            if (bitmap == null)
                throw new InvalidOperationException("Failed to decode PNG from Section 7 data");

            var values = new float[numberOfDataPoints];
            int pixelCount = bitmap.Width * bitmap.Height;

            for (int i = 0; i < numberOfDataPoints; i++)
            {
                if (i >= pixelCount)
                {
                    values[i] = float.NaN;
                    continue;
                }

                int x = i % bitmap.Width;
                int y = i / bitmap.Width;
                var pixel = bitmap.GetPixel(x, y);

                // Extract raw integer value from pixel
                // For 8-bit: use single channel (grayscale or red channel)
                // For 16-bit: combine channels
                uint raw;
                if (field.BitsPerValue <= 8)
                {
                    raw = pixel.Red;
                }
                else if (field.BitsPerValue <= 16)
                {
                    raw = ((uint)pixel.Red << 8) | pixel.Green;
                }
                else if (field.BitsPerValue <= 24)
                {
                    raw = ((uint)pixel.Red << 16) | ((uint)pixel.Green << 8) | pixel.Blue;
                }
                else
                {
                    raw = ((uint)pixel.Alpha << 24) | ((uint)pixel.Red << 16) |
                          ((uint)pixel.Green << 8) | pixel.Blue;
                }

                values[i] = (R + raw * binaryScale) * decimalScale;
            }

            return values;
        }
    }
}
