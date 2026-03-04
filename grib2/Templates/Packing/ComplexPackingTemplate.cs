#nullable enable
using System;
using Grib2.Decoder;
using Grib2.Models;

namespace Grib2.Templates.Packing
{
    /// <summary>
    /// Data Representation Template 5.2 — Complex Packing.
    /// Data is divided into groups, each with its own reference value, width, and length.
    /// Section 5 template layout (after common 9-byte header):
    ///   Octet 9:      Group splitting method (Code Table 5.4)
    ///   Octet 10:     Missing value management (Code Table 5.5)
    ///   Octets 11–14: Primary missing value substitute (IEEE 754)
    ///   Octets 15–18: Secondary missing value substitute (IEEE 754)
    ///   Octets 19–22: Number of groups (NG)
    ///   Octet 23:     Reference for group widths
    ///   Octet 24:     Number of bits for group widths
    ///   Octets 25–28: Reference for group lengths
    ///   Octet 29:     Length increment for group lengths
    ///   Octets 30–33: True length of last group
    ///   Octet 34:     Number of bits used for scaled group lengths
    /// </summary>
    public static class ComplexPackingTemplate
    {
        /// <summary>
        /// Unpack Section 7 data using Complex Packing (Template 5.2).
        /// </summary>
        /// <param name="packedData">Raw packed bytes from Section 7.</param>
        /// <param name="sec5Template">Full Section 5 template data (after the 11-byte section header).</param>
        /// <param name="field">Field with common packing metadata.</param>
        /// <param name="numberOfDataPoints">Expected number of data points.</param>
        /// <returns>Unpacked float array.</returns>
        public static float[] Unpack(ReadOnlySpan<byte> packedData, ReadOnlySpan<byte> sec5Template,
            Grib2Field field, int numberOfDataPoints)
        {
            if (sec5Template.Length < 25) // Need at least through "number of bits for group widths"
                throw new InvalidOperationException("Section 5 template too short for Complex Packing");

            float R = field.ReferenceValue;
            float binaryScale = MathF.Pow(2.0f, field.BinaryScaleFactor);
            float decimalScale = MathF.Pow(10.0f, -field.DecimalScaleFactor);
            int bitsPerValue = field.BitsPerValue;

            // Parse complex packing parameters from Section 5 template
            // Offsets are relative to template data start (after common 9-byte header)
            byte groupSplittingMethod = sec5Template[9];
            byte missingValueMgmt = sec5Template[10];

            float primaryMissing = sec5Template.ReadFloat32BE(11);
            float secondaryMissing = sec5Template.ReadFloat32BE(15);

            int numberOfGroups = (int)sec5Template.ReadUInt32BE(19);
            byte refGroupWidths = sec5Template[23];
            byte bitsGroupWidths = sec5Template[24];

            uint refGroupLength = 0;
            byte lengthIncrement = 0;
            int lastGroupLength = 0;
            byte bitsGroupLengths = 0;

            if (sec5Template.Length >= 35)
            {
                refGroupLength = sec5Template.ReadUInt32BE(25);
                lengthIncrement = sec5Template[29];
                lastGroupLength = (int)sec5Template.ReadUInt32BE(30);
                bitsGroupLengths = sec5Template[34];
            }

            if (numberOfGroups == 0)
            {
                var empty = new float[numberOfDataPoints];
                Array.Fill(empty, R * decimalScale);
                return empty;
            }

            // Read group reference values, widths, and lengths from packed data
            var reader = new BitReader(packedData);

            // Group reference values (bitsPerValue bits each)
            var groupRefs = new uint[numberOfGroups];
            for (int g = 0; g < numberOfGroups; g++)
                groupRefs[g] = reader.ReadUInt(bitsPerValue);

            // Group widths (bitsGroupWidths bits each, add refGroupWidths)
            var groupWidths = new int[numberOfGroups];
            for (int g = 0; g < numberOfGroups; g++)
                groupWidths[g] = (int)reader.ReadUInt(bitsGroupWidths) + refGroupWidths;

            // Group lengths (bitsGroupLengths bits each, apply length formula)
            var groupLengths = new int[numberOfGroups];
            for (int g = 0; g < numberOfGroups - 1; g++)
                groupLengths[g] = (int)(reader.ReadUInt(bitsGroupLengths) * lengthIncrement + refGroupLength);

            // Last group length is stored explicitly
            groupLengths[numberOfGroups - 1] = lastGroupLength;

            // Now read the actual data values group by group
            var values = new float[numberOfDataPoints];
            int valueIndex = 0;

            for (int g = 0; g < numberOfGroups; g++)
            {
                int width = groupWidths[g];
                int length = groupLengths[g];
                uint groupRef = groupRefs[g];

                for (int j = 0; j < length && valueIndex < numberOfDataPoints; j++)
                {
                    uint raw;
                    if (width == 0)
                    {
                        raw = groupRef;
                    }
                    else
                    {
                        raw = reader.ReadUInt(width) + groupRef;
                    }

                    // Check for missing values
                    if (missingValueMgmt != 0)
                    {
                        bool isMissing = false;
                        if (width == 0 && groupRef == 0)
                            isMissing = true;
                        else if (width > 0 && raw == ((1u << width) - 1) + groupRef)
                            isMissing = true;

                        if (isMissing)
                        {
                            values[valueIndex++] = float.NaN;
                            continue;
                        }
                    }

                    values[valueIndex++] = (R + raw * binaryScale) * decimalScale;
                }
            }

            // Fill any remaining points
            for (int i = valueIndex; i < numberOfDataPoints; i++)
                values[i] = float.NaN;

            return values;
        }
    }
}
