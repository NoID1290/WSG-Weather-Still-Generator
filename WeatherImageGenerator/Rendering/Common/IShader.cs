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

        /// <summary>Set a boolean uniform value (passed as int 0/1 on GPU).</summary>
        void SetBool(string name, bool value);

        /// <summary>Set a float uniform value.</summary>
        void SetFloat(string name, float value);

        /// <summary>Set a vec2 uniform value.</summary>
        void SetVec2(string name, float x, float y);

        /// <summary>Set a vec3 uniform value.</summary>
        void SetVec3(string name, float x, float y, float z);

        /// <summary>Set a vec4 uniform value.</summary>
        void SetVec4(string name, float x, float y, float z, float w);

        /// <summary>Set a 3x3 matrix uniform (column-major float[9]).</summary>
        void SetMatrix3(string name, float[] mat3);

        /// <summary>Set a 4x4 matrix uniform (column-major float[16]).</summary>
        void SetMatrix4(string name, float[] mat4);
    }
}
