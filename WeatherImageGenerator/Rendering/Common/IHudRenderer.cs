using System;

namespace WeatherImageGenerator.Rendering.Common
{
    /// <summary>
    /// Abstraction for a GPU-accelerated text and rectangle renderer used by the HUD system.
    /// Each rendering backend (OpenGL, Vulkan, DirectX) must provide an implementation.
    /// </summary>
    public interface IHudRenderer : IDisposable
    {
        /// <summary>Whether the renderer has been initialized and is ready for draw calls.</summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Initialize GPU resources (font atlas texture, shaders, buffers).
        /// Must be called on the render thread after the graphics context is current.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Begin a new frame of HUD rendering. Call once per paint before any Draw calls.
        /// Sets up orthographic projection for pixel-coordinate rendering.
        /// </summary>
        void BeginFrame(int viewportWidth, int viewportHeight);

        /// <summary>
        /// Draw a filled rectangle at pixel coordinates.
        /// </summary>
        void DrawRect(float x, float y, float w, float h, float r, float g, float b, float a);

        /// <summary>
        /// Render text at the given pixel position with the specified color.
        /// </summary>
        void DrawText(string text, float x, float y, float r, float g, float b, float a);

        /// <summary>
        /// Measure the pixel width of a string without drawing it.
        /// </summary>
        float MeasureTextWidth(string text);

        /// <summary>
        /// Get the line height in pixels.
        /// </summary>
        float LineHeight { get; }

        /// <summary>
        /// End the frame — flush any remaining vertices.
        /// </summary>
        void EndFrame();
    }
}
