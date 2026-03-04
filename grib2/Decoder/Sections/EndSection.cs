#nullable enable
using System;

namespace Grib2.Decoder.Sections
{
    /// <summary>
    /// Section 8 — End Section.
    /// Fixed 4-byte marker "7777" (0x37373737) signaling the end of a GRIB2 message.
    /// </summary>
    public static class EndSection
    {
        /// <summary>Fixed size of Section 8.</summary>
        public const int SectionLength = 4;

        /// <summary>Expected end marker bytes: '7','7','7','7'.</summary>
        public static readonly byte[] EndMarker = { 0x37, 0x37, 0x37, 0x37 };

        /// <summary>
        /// Validate the end section marker.
        /// </summary>
        /// <param name="data">Data span at the expected end section position.</param>
        /// <returns>True if the "7777" marker is present.</returns>
        public static bool Validate(ReadOnlySpan<byte> data)
        {
            if (data.Length < SectionLength)
                return false;

            return data[0] == 0x37 && data[1] == 0x37 && data[2] == 0x37 && data[3] == 0x37;
        }

        /// <summary>
        /// Validate and throw if the end marker is missing.
        /// </summary>
        public static void ValidateOrThrow(ReadOnlySpan<byte> data)
        {
            if (!Validate(data))
                throw new InvalidOperationException("Missing GRIB2 end section marker '7777'");
        }
    }
}
