#nullable enable
using System;
using Grib2.Models;

namespace Grib2.Decoder.Sections
{
    /// <summary>
    /// Section 5 — Data Representation Section.
    /// Defines how the data in Section 7 is packed.
    /// Layout:
    ///   Octets 1–4:  Section length
    ///   Octet 5:     Section number (5)
    ///   Octets 6–9:  Number of data points (should match grid or bitmap count)
    ///   Octets 10–11: Data representation template number
    ///   Octets 12+:  Template data
    /// </summary>
    public static class DataRepresentationSection
    {
        /// <summary>Minimum header length before template data.</summary>
        public const int MinHeaderLength = 11;

        /// <summary>
        /// Parse Section 5 and populate the field with packing metadata.
        /// </summary>
        /// <param name="data">Section data span.</param>
        /// <param name="field">Field object to populate.</param>
        /// <param name="numberOfDataPoints">Output: number of data points.</param>
        /// <returns>Total section length in bytes.</returns>
        public static int Parse(ReadOnlySpan<byte> data, Grib2Field field, out int numberOfDataPoints)
        {
            if (data.Length < MinHeaderLength)
                throw new InvalidOperationException($"Section 5 requires at least {MinHeaderLength} bytes");

            int sectionLength = (int)data.ReadUInt32BE(0);
            byte sectionNumber = data[4];

            if (sectionNumber != 5)
                throw new InvalidOperationException($"Expected section 5, got section {sectionNumber}");

            numberOfDataPoints = (int)data.ReadUInt32BE(5);
            field.PackingTemplateNumber = data.ReadUInt16BE(9);

            // Parse packing template common fields
            // Most packing templates (5.0, 5.2, 5.3, 5.40, 5.41) share the same first 5 fields
            ReadOnlySpan<byte> templateData = data.Slice(11);
            if (templateData.Length >= 9)
            {
                ParseCommonPackingFields(templateData, field);
            }

            return sectionLength;
        }

        /// <summary>
        /// Parse the common packing fields shared by templates 5.0, 5.2, 5.3, 5.40, 5.41.
        /// Template layout (0-indexed from template start):
        ///   Octets 0–3:  Reference value R (IEEE 754 float)
        ///   Octets 4–5:  Binary scale factor E (signed 16-bit)
        ///   Octets 6–7:  Decimal scale factor D (signed 16-bit)
        ///   Octet 8:     Number of bits used for each packed value
        /// </summary>
        private static void ParseCommonPackingFields(ReadOnlySpan<byte> template, Grib2Field field)
        {
            field.ReferenceValue = template.ReadFloat32BE(0);
            field.BinaryScaleFactor = template.ReadInt16BE(4);
            field.DecimalScaleFactor = template.ReadInt16BE(6);
            field.BitsPerValue = template[8];
        }

        /// <summary>
        /// Get the raw template data for advanced packing template parsers.
        /// Returns the bytes after the common header (offset 11 from section start).
        /// </summary>
        public static ReadOnlySpan<byte> GetTemplateData(ReadOnlySpan<byte> sectionData)
        {
            if (sectionData.Length <= 11) return ReadOnlySpan<byte>.Empty;
            return sectionData.Slice(11);
        }

        /// <summary>
        /// Get the full template data including extra fields beyond the common 9-byte prefix.
        /// For Complex Packing (5.2/5.3) this includes group information.
        /// </summary>
        public static ReadOnlySpan<byte> GetFullTemplateData(ReadOnlySpan<byte> sectionData)
        {
            int sectionLength = (int)sectionData.ReadUInt32BE(0);
            int templateLength = sectionLength - 11;
            if (templateLength <= 0) return ReadOnlySpan<byte>.Empty;
            return sectionData.Slice(11, templateLength);
        }
    }
}
