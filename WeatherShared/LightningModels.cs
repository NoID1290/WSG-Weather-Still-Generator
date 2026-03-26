#nullable enable
using System;

namespace WeatherShared
{
    /// <summary>
    /// Type of lightning discharge.
    /// </summary>
    public enum LightningStrikeType
    {
        /// <summary>Cloud-to-ground discharge (touches the ground)</summary>
        CloudToGround,
        /// <summary>In-cloud (cloud-to-cloud or intra-cloud) discharge</summary>
        InCloud,
        /// <summary>Unknown or unclassified discharge</summary>
        Unknown
    }

    /// <summary>
    /// A single lightning flash observation.
    /// </summary>
    public class LightningFlash
    {
        /// <summary>Geographic latitude of the discharge</summary>
        public double Latitude { get; set; }

        /// <summary>Geographic longitude of the discharge</summary>
        public double Longitude { get; set; }

        /// <summary>UTC time of the discharge</summary>
        public DateTime Time { get; set; }

        /// <summary>Type of discharge (CG, IC, or Unknown)</summary>
        public LightningStrikeType StrikeType { get; set; } = LightningStrikeType.Unknown;

        /// <summary>Optional peak current in kA (positive = positive polarity)</summary>
        public double? PeakCurrentKa { get; set; }

        /// <summary>Optional multiplicity (number of return strokes)</summary>
        public int? Multiplicity { get; set; }
    }
}
