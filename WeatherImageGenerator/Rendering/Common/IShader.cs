using System;

namespace WeatherImageGenerator.Rendering.Common
{
    /// <summary>
    /// Abstraction for a compiled GPU shader program.
    /// Each rendering backend provides its own implementation
    /// (GLSL for OpenGL, SPIR-V for Vulkan, HLSL for DirectX).
    /// </summary>
    public interface IShader : IDisposable
    {
        /// <summary>Activate this shader program for subsequent draw calls.</summary>
        void Use();

        /// <summary>Get the attribute location index by name (OpenGL-style; backends may translate).</summary>
        int GetAttribLocation(string name);

        /// <summary>Set an integer uniform value.</summary>
        void SetInt(string name, int value);

        /// <summary>Set a float uniform value.</summary>
        void SetFloat(string name, float value);

        /// <summary>Set a 3x3 matrix uniform (column-major float[9]).</summary>
        void SetMatrix3(string name, float[] mat3);
    }
}
