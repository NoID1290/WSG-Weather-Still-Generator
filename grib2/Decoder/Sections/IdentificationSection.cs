#nullable enable
using System;
using Grib2.Models;

namespace Grib2.Decoder.Sections
{
    /// <summary>
    /// Section 1 — Identification Section.
    /// Contains originating center, reference time, production status, and data type.
    /// Layout:
    ///   Octets 1–4:  Section length
    ///   Octet 5:     Section number (1)
    ///   Octets 6–7:  Originating center (Code Table C-11)
    ///   Octets 8–9:  Originating sub-center
    ///   Octet 10:    Master tables version
    ///   Octet 11:    Local tables version
    ///   Octet 12:    Significance of reference time
    ///   Octets 13–14: Year
    ///   Octet 15:    Month
    ///   Octet 16:    Day
    ///   Octet 17:    Hour
    ///   Octet 18:    Minute
    ///   Octet 19:    Second
    ///   Octet 20:    Production status
    ///   Octet 21:    Type of processed data
    /// </summary>
    public static class IdentificationSection
    {
        /// <summary>Minimum section length in bytes.</summary>
        public const int MinLength = 21;

        /// <summary>
        /// Parse Section 1 and populate the metadata object.
        /// </summary>
        /// <param name="data">Section 1 data span (starting at octet 1 of section).</param>
        /// <param name="metadata">Metadata object to populate.</param>
        /// <returns>Total section length in bytes.</returns>
        public static int Parse(ReadOnlySpan<byte> data, Grib2Metadata metadata)
        {
            if (data.Length < MinLength)
                throw new InvalidOperationException($"Section 1 requires at least {MinLength} bytes, got {data.Length}");

            int sectionLength = (int)data.ReadUInt32BE(0);
            byte sectionNumber = data[4];

            if (sectionNumber != 1)
                throw new InvalidOperationException($"Expected section 1, got section {sectionNumber}");

            metadata.OriginatingCenter = data.ReadUInt16BE(5);
            metadata.OriginatingSubCenter = data.ReadUInt16BE(7);
            metadata.MasterTablesVersion = data[9];
            metadata.LocalTablesVersion = data[10];
            metadata.SignificanceOfReferenceTime = data[11];

            // Reference time: Year (2 bytes), Month, Day, Hour, Minute, Second
            int year = data.ReadUInt16BE(12);
            int month = data[14];
            int day = data[15];
            int hour = data[16];
            int minute = data[17];
            int second = data[18];

            try
            {
                metadata.ReferenceTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Fallback for invalid dates
                metadata.ReferenceTime = DateTime.MinValue;
            }

            metadata.ProductionStatus = data[19];
            metadata.TypeOfData = data[20];

            return sectionLength;
        }
    }
}
