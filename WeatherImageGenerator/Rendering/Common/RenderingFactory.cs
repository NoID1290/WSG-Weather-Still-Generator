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
            return api switch
            {
                RenderingApi.OpenGL => CreateOpenGLRenderer(),
                RenderingApi.Vulkan => CreateVulkanRenderer(),
                RenderingApi.DirectX11 => CreateDirectXRenderer(),
                _ => throw new NotSupportedException($"Unknown rendering API: {api}")
            };
        }

        /// <summary>
        /// Create an <see cref="IHudRenderer"/> for the specified rendering API.
        /// </summary>
        public static IHudRenderer CreateHudRenderer(RenderingApi api)
        {
            return api switch
            {
                RenderingApi.OpenGL => CreateOpenGLHudRenderer(),
                RenderingApi.Vulkan => CreateVulkanHudRenderer(),
                RenderingApi.DirectX11 => CreateDirectXHudRenderer(),
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

        private static unsafe bool CheckVulkanAvailability()
        {
            try
            {
                var vk = Silk.NET.Vulkan.Vk.GetApi();

                // Create a minimal Vulkan instance to probe for physical devices
                var appInfo = new Silk.NET.Vulkan.ApplicationInfo
                {
                    SType = Silk.NET.Vulkan.StructureType.ApplicationInfo,
                    ApiVersion = Silk.NET.Vulkan.Vk.Version11,
                };
                var createInfo = new Silk.NET.Vulkan.InstanceCreateInfo
                {
                    SType = Silk.NET.Vulkan.StructureType.InstanceCreateInfo,
                    PApplicationInfo = &appInfo,
                };

                Silk.NET.Vulkan.Instance instance = default;
                var result = vk.CreateInstance(&createInfo, null, &instance);
                if (result != Silk.NET.Vulkan.Result.Success)
                {
                    vk.Dispose();
                    return false;
                }

                uint count = 0;
                vk.EnumeratePhysicalDevices(instance, &count, null);

                vk.DestroyInstance(instance, null);
                vk.Dispose();

                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static unsafe bool CheckDirectXAvailability()
        {
            try
            {
                // Probe for D3D11 device creation to verify driver support
                var d3d11 = Silk.NET.Direct3D11.D3D11.GetApi();
                Silk.NET.Core.Native.D3DFeatureLevel level;
                var levels = new[] { Silk.NET.Core.Native.D3DFeatureLevel.Level110 };
                Silk.NET.Direct3D11.ID3D11Device* pDevice = null;
                Silk.NET.Direct3D11.ID3D11DeviceContext* pCtx = null;
                fixed (Silk.NET.Core.Native.D3DFeatureLevel* pLevels = levels)
                {
                    int hr = d3d11.CreateDevice(
                        (Silk.NET.DXGI.IDXGIAdapter*)null,
                        Silk.NET.Core.Native.D3DDriverType.Hardware,
                        0, 0,
                        pLevels, 1,
                        Silk.NET.Direct3D11.D3D11.SdkVersion,
                        &pDevice, &level, &pCtx);
                    if (hr >= 0)
                    {
                        if (pCtx != null) pCtx->Release();
                        if (pDevice != null) pDevice->Release();
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
