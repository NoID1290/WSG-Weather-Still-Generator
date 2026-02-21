using System;
using WeatherImageGenerator.Rendering.Common;

namespace WeatherImageGenerator.Rendering.Vulkan
{
    /// <summary>
    /// Vulkan shader pipeline wrapper using Silk.NET.
    /// Loads SPIR-V shader modules and manages pipeline state.
    /// 
    /// Current status: Stub implementation — infrastructure scaffolding.
    /// TODO: Implement SPIR-V module loading, pipeline creation,
    ///       descriptor set binding, push constant management.
    /// </summary>
    public class VulkanShader : IShader
    {
        private bool _disposed;

        public VulkanShader(byte[] vertexSpirV, byte[] fragmentSpirV)
        {
            // TODO: Create Vulkan shader modules from SPIR-V bytecode
            Console.WriteLine("[VulkanShader] Created (stub)");
        }

        public void Use()
        {
            // TODO: Bind graphics pipeline
        }

        public int GetAttribLocation(string name)
        {
            // Vulkan uses explicit location bindings in SPIR-V; this is a no-op
            return -1;
        }

        public void SetInt(string name, int value)
        {
            // TODO: Update push constant or descriptor set
        }

        public void SetFloat(string name, float value)
        {
            // TODO: Update push constant or descriptor set
        }

        public void SetMatrix3(string name, float[] mat3)
        {
            // TODO: Update push constant or descriptor set
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                // TODO: Destroy Vulkan shader modules and pipeline
                Console.WriteLine("[VulkanShader] Disposed (stub)");
            }
        }
    }
}
