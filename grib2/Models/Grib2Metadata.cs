#nullable enable
using System;

namespace Grib2.Models
{
    /// <summary>
    /// GRIB2 message metadata from Sections 0 (Indicator) and 1 (Identification).
    /// </summary>
    public class Grib2Metadata
    {
        // --- Section 0: Indicator ---

        /// <summary>GRIB2 discipline code. 0 = Meteorological, 1 = Hydrological, 2 = Land Surface, 10 = Oceanographic.</summary>
        public byte Discipline { get; set; }

        /// <summary>GRIB edition number (always 2 for GRIB2).</summary>
        public byte Edition { get; set; } = 2;

        /// <summary>Total length of the GRIB2 message in bytes.</summary>
        public long TotalLength { get; set; }

        // --- Section 1: Identification ---

        /// <summary>Identification of originating/generating center (WMO Common Code Table C-11).
        /// 54 = CMC (Canadian Meteorological Centre / ECCC), 7 = NCEP.</summary>
        public ushort OriginatingCenter { get; set; }

        /// <summary>Identification of originating/generating sub-center.</summary>
        public ushort OriginatingSubCenter { get; set; }

        /// <summary>GRIB master tables version number.</summary>
        public byte MasterTablesVersion { get; set; }

        /// <summary>Version number of local tables used.</summary>
        public byte LocalTablesVersion { get; set; }

        /// <summary>Significance of reference time (0 = analysis, 1 = start of forecast, 2 = verifying time).</summary>
        public byte SignificanceOfReferenceTime { get; set; }

        /// <summary>Reference time (analysis or forecast base time) as UTC DateTime.</summary>
        public DateTime ReferenceTime { get; set; }

        /// <summary>Production status (0 = operational, 1 = test, 2 = research).</summary>
        public byte ProductionStatus { get; set; }

        /// <summary>Type of processed data (0 = analysis, 1 = forecast, 2 = analysis + forecast).</summary>
        public byte TypeOfData { get; set; }

        /// <summary>
        /// Human-readable name of the originating center.
        /// </summary>
        public string CenterName => OriginatingCenter switch
        {
            7 => "NCEP (US National Weather Service)",
            54 => "CMC (Canadian Meteorological Centre)",
            74 => "UKMO (UK Met Office)",
            78 => "DWD (Deutscher Wetterdienst)",
            85 => "Météo-France",
            98 => "ECMWF",
            _ => $"Center {OriginatingCenter}"
        };

        /// <summary>Human-readable discipline name.</summary>
        public string DisciplineName => Discipline switch
        {
            0 => "Meteorological",
            1 => "Hydrological",
            2 => "Land Surface",
            3 => "Satellite Remote Sensing",
            4 => "Space Weather",
            10 => "Oceanographic",
            _ => $"Discipline {Discipline}"
        };

        public override string ToString()
            => $"GRIB2 Ed{Edition} | {CenterName} | {DisciplineName} | " +
               $"RefTime={ReferenceTime:yyyy-MM-dd HH:mm}Z | Length={TotalLength}";
    }
}
