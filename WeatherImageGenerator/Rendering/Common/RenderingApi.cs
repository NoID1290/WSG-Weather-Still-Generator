namespace WeatherImageGenerator.Rendering.Common
{
    /// <summary>
    /// Available rendering API backends for the Weather Interactive Map.
    /// </summary>
    public enum RenderingApi
    {
        /// <summary>OpenGL 3.3+ via OpenTK (default, most compatible).</summary>
        OpenGL = 0,

        /// <summary>Vulkan via Silk.NET (modern, high-performance).</summary>
        Vulkan = 1,

        /// <summary>DirectX 11 via Silk.NET (Windows-native).</summary>
        DirectX11 = 2
    }
}
