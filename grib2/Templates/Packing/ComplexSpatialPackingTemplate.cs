#nullable enable
using System;
using Grib2.Decoder;
using Grib2.Models;

namespace Grib2.Templates.Packing
{
    /// <summary>
    /// Data Representation Template 5.3 — Complex Packing with Spatial Differencing.
    /// Extends Template 5.2 by applying first-order or second-order spatial differencing
    /// before group packing, which significantly improves compression for smooth fields.
    /// 
    /// Section 5 template extends 5.2 with:
    ///   Octet 35: Order of spatial differencing (1 or 2)
    ///   Octet 36: Number of extra descriptors
    /// 
    /// Section 7 data prepends spatial differencing descriptors before the group data:
    ///   For 1st order: first_value (nbits), minimum_value (nbits)
    ///   For 2nd order: first_value (nbits), second_value (nbits), minimum_value (nbits)
    /// </summary>
    public static class ComplexSpatialPackingTemplate
    {
        /// <summary>
        /// Unpack Section 7 data using Complex Packing with Spatial Differencing (Template 5.3).
        /// </summary>
        public static float[] Unpack(ReadOnlySpan<byte> packedData, ReadOnlySpan<byte> sec5Template,
            Grib2Field field, int numberOfDataPoints)
        {
            if (sec5Template.Length < 38)
                throw new InvalidOperationException("Section 5 template too short for Complex Spatial Packing");

            // Parse spatial differencing parameters
            // Template byte [9] = Type of original field values, so spatial fields start at [36]/[37]
            byte spatialOrder = sec5Template[36];
            byte extraDescriptors = sec5Template[37];

            if (spatialOrder != 1 && spatialOrder != 2)
                throw new InvalidOperationException($"Unsupported spatial differencing order: {spatialOrder}");

            // Determine bit width for extra descriptors (octets × 8)
            int descriptorBits = extraDescriptors * 8;

            var reader = new BitReader(packedData);

            // Read spatial differencing descriptors
            int firstValue = reader.ReadSignMagnitude(descriptorBits);
            int secondValue = 0;
            if (spatialOrder == 2)
                secondValue = reader.ReadSignMagnitude(descriptorBits);
            int minimumValue = reader.ReadSignMagnitude(descriptorBits);

            // Remaining data is standard complex packing (Template 5.2)
            // We need to reconstruct the packed data without the descriptors
            int descriptorBytes = reader.BitOffset / 8;
            if (reader.BitOffset % 8 != 0)
                descriptorBytes++; // Round up to byte boundary

            ReadOnlySpan<byte> groupData = packedData.Slice(descriptorBytes);

            // Unpack using complex packing (5.2) — get raw integer values before scale formula
            float R = field.ReferenceValue;
            float binaryScale = MathF.Pow(2.0f, field.BinaryScaleFactor);
            float decimalScale = MathF.Pow(10.0f, -field.DecimalScaleFactor);
            int bitsPerValue = field.BitsPerValue;

            // Parse group parameters (same as Template 5.2)
            // Template byte [9] = Type of original field values, complex fields start at [10]+1
            byte missingValueMgmt = sec5Template[11];
            int numberOfGroups = (int)sec5Template.ReadUInt32BE(20);
            byte refGroupWidths = sec5Template[24];
            byte bitsGroupWidths = sec5Template[25];
            uint refGroupLength = sec5Template.ReadUInt32BE(26);
            byte lengthIncrement = sec5Template[30];
            int lastGroupLength = (int)sec5Template.ReadUInt32BE(31);
            byte bitsGroupLengths = sec5Template[35];

            if (numberOfGroups == 0)
            {
                var empty = new float[numberOfDataPoints];
                Array.Fill(empty, float.NaN);
                return empty;
            }

            var groupReader = new BitReader(groupData);

            // Read group reference values
            var groupRefs = new uint[numberOfGroups];
            for (int g = 0; g < numberOfGroups; g++)
                groupRefs[g] = groupReader.ReadUInt(bitsPerValue);

            // Read group widths
            var groupWidths = new int[numberOfGroups];
            for (int g = 0; g < numberOfGroups; g++)
                groupWidths[g] = (int)groupReader.ReadUInt(bitsGroupWidths) + refGroupWidths;

            // Read group lengths
            var groupLengths = new int[numberOfGroups];
            for (int g = 0; g < numberOfGroups - 1; g++)
                groupLengths[g] = (int)(groupReader.ReadUInt(bitsGroupLengths) * lengthIncrement + refGroupLength);
            groupLengths[numberOfGroups - 1] = lastGroupLength;

            // Unpack raw integer values
            var rawValues = new long[numberOfDataPoints];
            int valueIndex = 0;

            for (int g = 0; g < numberOfGroups; g++)
            {
                int width = groupWidths[g];
                int length = groupLengths[g];
                uint groupRef = groupRefs[g];

                for (int j = 0; j < length && valueIndex < numberOfDataPoints; j++)
                {
                    uint raw = width == 0 ? groupRef : groupReader.ReadUInt(width) + groupRef;
                    rawValues[valueIndex++] = raw;
                }
            }

            // Apply spatial differencing (reverse: cumulative sum)
            // Add back the minimum value
            for (int i = 0; i < valueIndex; i++)
                rawValues[i] += minimumValue;

            if (spatialOrder == 1)
            {
                // First-order: values[0] = firstValue, values[i] = values[i-1] + diff[i]
                rawValues[0] = firstValue;
                for (int i = 1; i < valueIndex; i++)
                    rawValues[i] += rawValues[i - 1];
            }
            else if (spatialOrder == 2)
            {
                // Second-order: values[0] = firstValue, values[1] = secondValue
                // values[i] = 2*values[i-1] - values[i-2] + diff[i]
                rawValues[0] = firstValue;
                rawValues[1] = secondValue;
                for (int i = 2; i < valueIndex; i++)
                    rawValues[i] += 2 * rawValues[i - 1] - rawValues[i - 2];
            }

            // Apply scale formula: Y = R + X × 2^E × 10^(-D)
            var values = new float[numberOfDataPoints];
            for (int i = 0; i < valueIndex; i++)
                values[i] = (R + rawValues[i] * binaryScale) * decimalScale;

            for (int i = valueIndex; i < numberOfDataPoints; i++)
                values[i] = float.NaN;

            return values;
        }
    }
}
