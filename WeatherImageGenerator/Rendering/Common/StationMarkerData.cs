using System.Drawing;

namespace WeatherImageGenerator.Rendering.Common
{
    /// <summary>Type of GPU marker to render.</summary>
    public enum MarkerType { Triangle, Dot }

    /// <summary>
    /// Per-station marker entry passed to the GPU renderer.
    /// Lat/Lon are geographic coordinates. ColorArgb is ARGB packed int.
    /// </summary>
    public readonly record struct StationMarkerEntry(
        float Lat,
        float Lon,
        int   ColorArgb,
        bool  Selected)
    {
        public static int PackColor(Color c) => c.ToArgb();
    }

    /// <summary>
    /// Per-epicenter marker entry passed to the GPU renderer.
    /// IsMostRecent drives which animation phase clock to use (faster ring).
    /// </summary>
    public readonly record struct EpicenterMarkerEntry(
        float  Lat,
        float  Lon,
        int    ColorArgb,
        float  Magnitude,
        bool   IsMostRecent);

    /// <summary>
    /// Per-lightning-strike entry passed to the GPU renderer.
    /// Age is normalised 0.0 (just occurred) → 1.0 (oldest in the window).
    /// IsCG distinguishes cloud-to-ground (yellow) from in-cloud (blue) strikes.
    /// </summary>
    public readonly record struct LightningStrikeEntry(
        float Lat,
        float Lon,
        float Age,
        bool  IsCG);
}
