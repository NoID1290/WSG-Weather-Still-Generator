using System.Numerics;

namespace WeatherImageGenerator.Rendering.Common
{
    /// <summary>
    /// Centralized procedural cloud tuning values shared by OpenGL and DirectX renderers.
    /// Edit these values to tune cloud visuals without touching shader code.
    /// </summary>
    public static class ProceduralCloudSettings
    {
        // Core cloud shape/light parameters
        public static float Density { get; set; } = 1.45f;
        public static float Contrast { get; set; } = 1.35f;
        public static float Brightness { get; set; } = 5.60f;
        public static float RaymarchSteps { get; set; } = 14.0f;
        public static float OpacityMultiplier { get; set; } = 1.00f;

        // Daylight direction animation (screen-space sun vector)
        public static float SunSpeed { get; set; } = 0.035f;
        public static float SunYScale { get; set; } = 0.45f;
        public static float SunYOffset { get; set; } = 0.40f;

        // Radar gating controls for cloud placement
        public static float RadarThreshold { get; set; } = 0.020f;
        public static float RadarMaskUpper { get; set; } = 0.120f;
        public static float RadarSpreadStep { get; set; } = 0.030f;
        public static float RadarSpreadInfluence { get; set; } = 0.55f;
        public static float StormDarkening { get; set; } = 0.35f;

        // Cloud color ramp (dark -> bright)
        public static Vector3 DarkCloudColor { get; set; } = new Vector3(0.45f, 0.48f, 0.55f);
        public static Vector3 BrightCloudColor { get; set; } = new Vector3(0.92f, 0.95f, 0.98f);
    }
}