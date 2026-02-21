using System;

namespace WeatherImageGenerator.Rendering.Common
{
    /// <summary>
    /// Factory for creating rendering backend instances based on the selected API.
    /// </summary>
    public static class RenderingFactory
    {
        /// <summary>
        /// Create an <see cref="IMapRenderer"/> for the specified rendering API.
        /// </summary>
        /// <param name="api">The rendering backend to use.</param>
        /// <returns>A new map renderer instance.</returns>
        /// <exception cref="NotSupportedException">Thrown if the backend is not yet implemented.</exception>
        public static IMapRenderer CreateMapRenderer(RenderingApi api)
        {
            if (api == RenderingApi.Vulkan || api == RenderingApi.DirectX11)
            {
                Console.WriteLine($"[RenderingFactory] {api} backend is not yet implemented. Falling back to OpenGL.");
                return CreateOpenGLRenderer();
            }

            return api switch
            {
                RenderingApi.OpenGL => CreateOpenGLRenderer(),
                _ => throw new NotSupportedException($"Unknown rendering API: {api}")
            };
        }

        /// <summary>
        /// Create an <see cref="IHudRenderer"/> for the specified rendering API.
        /// </summary>
        public static IHudRenderer CreateHudRenderer(RenderingApi api)
        {
            if (api == RenderingApi.Vulkan || api == RenderingApi.DirectX11)
            {
                Console.WriteLine($"[RenderingFactory] {api} HUD renderer is not yet implemented. Falling back to OpenGL.");
                return CreateOpenGLHudRenderer();
            }

            return api switch
            {
                RenderingApi.OpenGL => CreateOpenGLHudRenderer(),
                _ => throw new NotSupportedException($"Unknown rendering API: {api}")
            };
        }

        /// <summary>
        /// Parse a rendering API from its display/config string.
        /// Returns OpenGL as default for unrecognized values.
        /// </summary>
        public static RenderingApi ParseFromString(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return RenderingApi.OpenGL;

            return value.Trim().ToLowerInvariant() switch
            {
                "opengl" => RenderingApi.OpenGL,
                "vulkan" => RenderingApi.Vulkan,
                "directx11" or "directx" or "dx11" or "d3d11" => RenderingApi.DirectX11,
                _ => RenderingApi.OpenGL
            };
        }

        /// <summary>
        /// Convert a rendering API enum to its config/display string.
        /// </summary>
        public static string ToConfigString(RenderingApi api)
        {
            return api switch
            {
                RenderingApi.OpenGL => "OpenGL",
                RenderingApi.Vulkan => "Vulkan",
                RenderingApi.DirectX11 => "DirectX11",
                _ => "OpenGL"
            };
        }

        /// <summary>
        /// Check whether a rendering API is available on the current system.
        /// </summary>
        public static bool IsAvailable(RenderingApi api)
        {
            return api switch
            {
                RenderingApi.OpenGL => true, // Always available on Windows with modern GPU
                RenderingApi.Vulkan => CheckVulkanAvailability(),
                RenderingApi.DirectX11 => CheckDirectXAvailability(),
                _ => false
            };
        }

        // ═══ Private factory methods ═══

        private static IMapRenderer CreateOpenGLRenderer()
        {
            return new OpenGL.GLRadarControl();
        }

        private static IMapRenderer CreateVulkanRenderer()
        {
            return new Vulkan.VulkanMapRenderer();
        }

        private static IMapRenderer CreateDirectXRenderer()
        {
            return new DirectX.DXMapRenderer();
        }

        private static IHudRenderer CreateOpenGLHudRenderer()
        {
            return new OpenGL.GLTextRenderer();
        }

        private static IHudRenderer CreateVulkanHudRenderer()
        {
            return new Vulkan.VulkanHudRenderer();
        }

        private static IHudRenderer CreateDirectXHudRenderer()
        {
            return new DirectX.DXHudRenderer();
        }

        // ═══ Availability checks ═══

        private static bool CheckVulkanAvailability()
        {
            // Vulkan backend is not yet implemented
            return false;
        }

        private static bool CheckDirectXAvailability()
        {
            // DirectX 11 backend is not yet implemented
            return false;
        }
    }
}
