#nullable enable
using System;

namespace Grib2.Decoder.Sections
{
    /// <summary>
    /// Section 7 — Data Section.
    /// Contains the packed binary data values. Unpacking is delegated to the
    /// appropriate packing template based on the template number from Section 5.
    /// Layout:
    ///   Octets 1–4: Section length
    ///   Octet 5:    Section number (7)
    ///   Octets 6+:  Packed data bytes
    /// </summary>
    public static class DataSection
    {
        /// <summary>
        /// Parse the Section 7 header and extract the raw packed data bytes.
        /// Actual unpacking is performed by the packing template classes.
        /// </summary>
        /// <param name="data">Section data span.</param>
        /// <param name="packedData">Output: raw packed data bytes (excluding section header).</param>
        /// <returns>Total section length in bytes.</returns>
        public static int Parse(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> packedData)
        {
            if (data.Length < 5)
                throw new InvalidOperationException("Section 7 requires at least 5 bytes");

            int sectionLength = (int)data.ReadUInt32BE(0);
            byte sectionNumber = data[4];

            if (sectionNumber != 7)
                throw new InvalidOperationException($"Expected section 7, got section {sectionNumber}");

            int dataLength = sectionLength - 5;
            packedData = dataLength > 0 ? data.Slice(5, dataLength) : ReadOnlySpan<byte>.Empty;

            return sectionLength;
        }

        /// <summary>
        /// Get the length of the packed data payload (excluding the 5-byte header).
        /// </summary>
        public static int GetDataLength(ReadOnlySpan<byte> data)
        {
            int sectionLength = (int)data.ReadUInt32BE(0);
            return Math.Max(0, sectionLength - 5);
        }
    }
}
