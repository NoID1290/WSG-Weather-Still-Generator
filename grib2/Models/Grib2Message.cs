#nullable enable
using System;

namespace Grib2.Models
{
    /// <summary>
    /// Represents a complete decoded GRIB2 message (one field/parameter within a GRIB2 file).
    /// A single GRIB2 file may contain multiple concatenated messages.
    /// </summary>
    public class Grib2Message
    {
        /// <summary>Section 0: Indicator — discipline, edition, total message length.</summary>
        public Grib2Metadata Metadata { get; set; } = new();

        /// <summary>Section 3: Grid definition — Ni×Nj, lat/lon bounds, template.</summary>
        public Grib2Grid Grid { get; set; } = new();

        /// <summary>Section 4+5+6+7: Decoded field data — parameter info, unpacked grid values.</summary>
        public Grib2Field Field { get; set; } = new();

        /// <summary>Raw Section 2 local-use bytes (ECCC internal metadata). May be empty.</summary>
        public ReadOnlyMemory<byte> LocalUseData { get; set; }

        /// <summary>Byte offset of this message within the source file/stream.</summary>
        public long FileOffset { get; set; }

        /// <summary>Total length of this GRIB2 message in bytes (from Section 0).</summary>
        public long TotalLength { get; set; }

        public override string ToString()
            => $"GRIB2 [{Metadata.Discipline}] {Field.ParameterName ?? Field.ParameterKey} " +
               $"@ {Field.SurfaceTypeName} {Field.SurfaceValue} | " +
               $"Grid {Grid.Ni}×{Grid.Nj} | ForecastHour={Field.ForecastHour}";
    }
}
