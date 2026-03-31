using System;

namespace WeatherImageGenerator.Rendering.Common
{
    /// <summary>
    /// Normalized weather-driving signals extracted from live overlays.
    /// Values are in the range [0,1] and intended to drive procedural GPU effects.
    /// </summary>
    public sealed class ProceduralWeatherData
    {
        public bool HasPrecip { get; set; }
        public float RainCoverage01 { get; set; }
        public float RainIntensity01 { get; set; }
        public float SnowCoverage01 { get; set; }
        public float ConvectiveSignal01 { get; set; }
        public float CloudCoverage01 { get; set; }
        public float LightningSignal01 { get; set; }
        public DateTime SourceTimeUtc { get; set; } = DateTime.UtcNow;
        public string SourceLayer { get; set; } = "RADAR_1KM_RRAI";

        public static ProceduralWeatherData Blend(ProceduralWeatherData prev, ProceduralWeatherData next, float alpha)
        {
            alpha = Math.Clamp(alpha, 0f, 1f);
            float inv = 1f - alpha;
            return new ProceduralWeatherData
            {
                HasPrecip = next.HasPrecip,
                RainCoverage01 = prev.RainCoverage01 * inv + next.RainCoverage01 * alpha,
                RainIntensity01 = prev.RainIntensity01 * inv + next.RainIntensity01 * alpha,
                SnowCoverage01 = prev.SnowCoverage01 * inv + next.SnowCoverage01 * alpha,
                ConvectiveSignal01 = prev.ConvectiveSignal01 * inv + next.ConvectiveSignal01 * alpha,
                CloudCoverage01 = prev.CloudCoverage01 * inv + next.CloudCoverage01 * alpha,
                LightningSignal01 = prev.LightningSignal01 * inv + next.LightningSignal01 * alpha,
                SourceTimeUtc = next.SourceTimeUtc,
                SourceLayer = next.SourceLayer
            };
        }
    }
}