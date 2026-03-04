#nullable enable
using System;
using Grib2.Decoder;
using Grib2.Models;

namespace Grib2.Templates.Packing
{
    /// <summary>
    /// Data Representation Template 5.0 — Simple Packing.
    /// Each grid point value X is packed as an N-bit unsigned integer.
    /// Unpacking formula: Y = R + X × 2^E × 10^(-D)
    ///   where R = reference value, E = binary scale factor, D = decimal scale factor.
    /// </summary>
    public static class SimplePackingTemplate
    {
        /// <summary>
        /// Unpack Section 7 data using Simple Packing (Template 5.0).
        /// </summary>
        /// <param name="packedData">Raw packed bytes from Section 7.</param>
        /// <param name="field">Field with packing metadata (R, E, D, bits per value).</param>
        /// <param name="numberOfDataPoints">Expected number of data points.</param>
        /// <returns>Unpacked float array.</returns>
        public static float[] Unpack(ReadOnlySpan<byte> packedData, Grib2Field field, int numberOfDataPoints)
        {
            int bitsPerValue = field.BitsPerValue;

            // Special case: 0 bits per value means all values equal the reference value
            if (bitsPerValue == 0)
            {
                var constant = new float[numberOfDataPoints];
                Array.Fill(constant, field.ReferenceValue);
                return constant;
            }

            float R = field.ReferenceValue;
            float binaryScale = MathF.Pow(2.0f, field.BinaryScaleFactor);
            float decimalScale = MathF.Pow(10.0f, -field.DecimalScaleFactor);

            // Determine how many points we can actually read
            int availableBits = packedData.Length * 8;
            int pointsFromData = availableBits / bitsPerValue;
            int pointsToRead = Math.Min(numberOfDataPoints, pointsFromData);

            var values = new float[numberOfDataPoints];
            var reader = new BitReader(packedData);

            for (int i = 0; i < pointsToRead; i++)
            {
                uint raw = reader.ReadUInt(bitsPerValue);
                values[i] = (R + raw * binaryScale) * decimalScale;
            }

            // Fill remaining points with NaN if data is truncated
            for (int i = pointsToRead; i < numberOfDataPoints; i++)
            {
                values[i] = float.NaN;
            }

            return values;
        }
    }
}
