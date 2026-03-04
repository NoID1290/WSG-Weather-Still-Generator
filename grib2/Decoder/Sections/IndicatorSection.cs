#nullable enable
using System;

namespace Grib2.Decoder.Sections
{
    /// <summary>
    /// Section 0 — Indicator Section.
    /// Fixed 16-byte header at the start of every GRIB2 message.
    /// Layout:
    ///   Octets 1–4:  "GRIB" (magic bytes 0x47524942)
    ///   Octets 5–6:  Reserved
    ///   Octet 7:     Discipline (WMO Code Table 0.0)
    ///   Octet 8:     GRIB edition number (always 2)
    ///   Octets 9–16: Total length of GRIB message in bytes (64-bit)
    /// </summary>
    public static class IndicatorSection
    {
        /// <summary>Fixed size of Section 0 in bytes.</summary>
        public const int SectionLength = 16;

        /// <summary>GRIB2 magic bytes: 'G','R','I','B' = 0x47, 0x52, 0x49, 0x42.</summary>
        public static readonly byte[] MagicBytes = { 0x47, 0x52, 0x49, 0x42 };

        /// <summary>
        /// Parse Section 0 from spanning data starting at offset.
        /// </summary>
        /// <param name="data">Source data span.</param>
        /// <param name="discipline">Output: WMO discipline code.</param>
        /// <param name="edition">Output: GRIB edition number (should be 2).</param>
        /// <param name="totalLength">Output: Total message length in bytes.</param>
        /// <exception cref="InvalidOperationException">If magic bytes do not match or edition is not 2.</exception>
        public static void Parse(ReadOnlySpan<byte> data,
            out byte discipline, out byte edition, out long totalLength)
        {
            if (data.Length < SectionLength)
                throw new InvalidOperationException($"Section 0 requires {SectionLength} bytes, got {data.Length}");

            // Validate magic bytes
            if (data[0] != 0x47 || data[1] != 0x52 || data[2] != 0x49 || data[3] != 0x42)
                throw new InvalidOperationException("Invalid GRIB2 magic bytes — expected 'GRIB'");

            // Octets 5–6: Reserved (skip)
            discipline = data[6];  // Octet 7
            edition = data[7];     // Octet 8

            if (edition != 2)
                throw new InvalidOperationException($"Unsupported GRIB edition: {edition} (only GRIB2 is supported)");

            // Octets 9–16: Total message length as 64-bit big-endian unsigned integer
            totalLength = (long)data.ReadUInt64BE(8);
        }

        /// <summary>
        /// Scan for the next "GRIB" magic bytes in a data span, starting at the given offset.
        /// Returns the offset of the first byte of "GRIB", or -1 if not found.
        /// </summary>
        public static long FindMagic(ReadOnlySpan<byte> data, int startOffset = 0)
        {
            for (int i = startOffset; i <= data.Length - 4; i++)
            {
                if (data[i] == 0x47 && data[i + 1] == 0x52 && data[i + 2] == 0x49 && data[i + 3] == 0x42)
                    return i;
            }
            return -1;
        }
    }
}
