#nullable enable
using System;

namespace Grib2.Decoder.Sections
{
    /// <summary>
    /// Section 6 — Bitmap Section.
    /// Indicates which grid points have valid data values.
    /// Layout:
    ///   Octets 1–4:  Section length
    ///   Octet 5:     Section number (6)
    ///   Octet 6:     Bitmap indicator
    ///                  0 = bitmap follows (in this section)
    ///                  254 = use previously defined bitmap
    ///                  255 = all grid points are present (no bitmap)
    ///   Octets 7+:   Bitmap data (if indicator = 0)
    /// </summary>
    public static class BitmapSection
    {
        /// <summary>
        /// Parse Section 6.
        /// </summary>
        /// <param name="data">Section data span.</param>
        /// <param name="numberOfDataPoints">Total number of grid points (from Section 3).</param>
        /// <param name="hasBitmap">Output: true if a bitmap is present.</param>
        /// <param name="bitmap">Output: boolean array (true = data present) or null.</param>
        /// <returns>Total section length in bytes.</returns>
        public static int Parse(ReadOnlySpan<byte> data, int numberOfDataPoints,
            out bool hasBitmap, out bool[]? bitmap)
        {
            if (data.Length < 6)
                throw new InvalidOperationException("Section 6 requires at least 6 bytes");

            int sectionLength = (int)data.ReadUInt32BE(0);
            byte sectionNumber = data[4];

            if (sectionNumber != 6)
                throw new InvalidOperationException($"Expected section 6, got section {sectionNumber}");

            byte indicator = data[5];

            if (indicator == 255)
            {
                // All grid points present — no bitmap
                hasBitmap = false;
                bitmap = null;
            }
            else if (indicator == 0)
            {
                // Bitmap follows in this section
                hasBitmap = true;
                bitmap = new bool[numberOfDataPoints];
                int bitmapDataOffset = 6;
                int bitmapBytes = sectionLength - 6;

                for (int i = 0; i < numberOfDataPoints && (i >> 3) < bitmapBytes; i++)
                {
                    int byteIndex = bitmapDataOffset + (i >> 3);
                    int bitIndex = 7 - (i & 7); // MSB first
                    bitmap[i] = byteIndex < data.Length && ((data[byteIndex] >> bitIndex) & 1) == 1;
                }
            }
            else if (indicator == 254)
            {
                // Use previously defined bitmap — caller must handle this
                hasBitmap = true;
                bitmap = null; // Caller should reuse the previous bitmap
            }
            else
            {
                // Pre-determined bitmap (indicator 1–253)
                hasBitmap = false;
                bitmap = null;
            }

            return sectionLength;
        }

        /// <summary>
        /// Count the number of true (present) values in a bitmap.
        /// </summary>
        public static int CountPresentPoints(bool[] bitmap)
        {
            int count = 0;
            for (int i = 0; i < bitmap.Length; i++)
                if (bitmap[i])
                    count++;
            return count;
        }
    }
}
