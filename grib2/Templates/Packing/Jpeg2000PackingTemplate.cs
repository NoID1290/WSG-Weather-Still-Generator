#nullable enable
using System;
using Grib2.Models;

namespace Grib2.Templates.Packing
{
    /// <summary>
    /// Data Representation Template 5.41 — JPEG2000 Packing.
    /// Grid values are packed as a JPEG2000 code stream embedded in Section 7.
    /// 
    /// STUB IMPLEMENTATION: Throws NotSupportedException.
    /// 
    /// Rationale: Most ECCC GRIB2 files from the Datamart use Simple Packing (5.0) or 
    /// Complex Packing (5.2/5.3). JPEG2000 support requires a managed J2K decoder
    /// (e.g., CSJ2K), which is not yet available for net10.0. This can be revisited
    /// when a compatible library becomes available.
    /// </summary>
    public static class Jpeg2000PackingTemplate
    {
        /// <summary>
        /// Unpack Section 7 data using JPEG2000 Packing (Template 5.41).
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown — JPEG2000 is not yet implemented.</exception>
        public static float[] Unpack(ReadOnlySpan<byte> packedData, Grib2Field field, int numberOfDataPoints)
        {
            throw new NotSupportedException(
                "JPEG2000 packing template 5.41 is not yet implemented. " +
                "Most ECCC Datamart GRIB2 files use Simple (5.0) or Complex (5.2/5.3) packing. " +
                "A managed J2K decoder compatible with net10.0 is required to support this template.");
        }
    }
}
