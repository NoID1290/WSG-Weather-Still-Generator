#nullable enable
using System;

namespace Grib2.Decoder.Sections
{
    /// <summary>
    /// Section 2 — Local Use Section.
    /// Optional section containing center-specific data (e.g., ECCC internal metadata).
    /// Layout:
    ///   Octets 1–4:  Section length
    ///   Octet 5:     Section number (2)
    ///   Octets 6–N:  Local use data (opaque bytes)
    /// </summary>
    public static class LocalUseSection
    {
        /// <summary>
        /// Parse Section 2 and return the local-use payload.
        /// </summary>
        /// <param name="data">Section data span.</param>
        /// <param name="localData">Output: local-use byte payload (may be empty).</param>
        /// <returns>Total section length in bytes.</returns>
        public static int Parse(ReadOnlySpan<byte> data, out ReadOnlyMemory<byte> localData)
        {
            int sectionLength = (int)data.ReadUInt32BE(0);
            byte sectionNumber = data[4];

            if (sectionNumber != 2)
                throw new InvalidOperationException($"Expected section 2, got section {sectionNumber}");

            // Local use data starts at octet 6 (index 5) and extends to end of section
            int payloadLength = sectionLength - 5;
            if (payloadLength > 0)
            {
                byte[] payload = new byte[payloadLength];
                data.Slice(5, payloadLength).CopyTo(payload);
                localData = payload;
            }
            else
            {
                localData = ReadOnlyMemory<byte>.Empty;
            }

            return sectionLength;
        }

        /// <summary>
        /// Check if the data at the current offset is a Section 2 (local use).
        /// Section 2 is optional — if not present, the next section will be 3, 4, etc.
        /// </summary>
        public static bool IsLocalUseSection(ReadOnlySpan<byte> data)
        {
            return data.Length >= 5 && data[4] == 2;
        }
    }
}
