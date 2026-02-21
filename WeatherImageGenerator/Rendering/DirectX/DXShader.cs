using System;
using WeatherImageGenerator.Rendering.Common;

namespace WeatherImageGenerator.Rendering.DirectX
{
    /// <summary>
    /// DirectX 11 shader wrapper using Silk.NET.
    /// Compiles HLSL at runtime via D3DCompiler and manages shader/constant buffer state.
    /// 
    /// Current status: Stub implementation — infrastructure scaffolding.
    /// TODO: Implement HLSL compilation, vertex/pixel shader creation,
    ///       input layout, constant buffer management.
    /// </summary>
    public class DXShader : IShader
    {
        private bool _disposed;

        public DXShader(string hlslSource, string vertexEntryPoint = "VSMain", string pixelEntryPoint = "PSMain")
        {
            // TODO: Compile HLSL source with D3DCompiler
            Console.WriteLine("[DXShader] Created (stub)");
        }

        public void Use()
        {
            // TODO: Set vertex/pixel shaders on the device context
        }

        public int GetAttribLocation(string name)
        {
            // DirectX uses input layouts rather than attribute locations
            return -1;
        }

        public void SetInt(string name, int value)
        {
            // TODO: Update constant buffer field
        }

        public void SetFloat(string name, float value)
        {
            // TODO: Update constant buffer field
        }

        public void SetMatrix3(string name, float[] mat3)
        {
            // TODO: Update constant buffer field
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Console.WriteLine("[DXShader] Disposed (stub)");
            }
        }
    }
}
