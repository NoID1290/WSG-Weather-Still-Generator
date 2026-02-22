using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using WeatherImageGenerator.Rendering.Common;

namespace WeatherImageGenerator.Rendering.OpenGL
{
    public class GLShader : IShader
    {
        public int Handle { get; private set; }

        public GLShader(string vertexSource, string fragmentSource)
        {
            var vertex = CompileShader(ShaderType.VertexShader, vertexSource);
            var fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vertex);
            GL.AttachShader(Handle, fragment);
            GL.LinkProgram(Handle);

            GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int status);
            if (status == 0)
            {
                var info = GL.GetProgramInfoLog(Handle);
                throw new Exception($"Shader link error: {info}");
            }

            // shaders can be deleted after linking
            GL.DetachShader(Handle, vertex);
            GL.DetachShader(Handle, fragment);
            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);
        }

        private static int CompileShader(ShaderType type, string source)
        {
            var handle = GL.CreateShader(type);
            GL.ShaderSource(handle, source);
            GL.CompileShader(handle);
            GL.GetShader(handle, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                var info = GL.GetShaderInfoLog(handle);
                throw new Exception($"Failed to compile {type}: {info}\nSource:\n{source}");
            }
            return handle;
        }

        public void Use() => GL.UseProgram(Handle);

        public int GetAttribLocation(string name) => GL.GetAttribLocation(Handle, name);

        public void SetInt(string name, int value)
        {
            var loc = GL.GetUniformLocation(Handle, name);
            GL.Uniform1(loc, value);
        }

        public void SetBool(string name, bool value)
        {
            var loc = GL.GetUniformLocation(Handle, name);
            GL.Uniform1(loc, value ? 1 : 0);
        }

        public void SetMatrix3(string name, float[] mat3)
        {
            var loc = GL.GetUniformLocation(Handle, name);
            GL.UniformMatrix3(loc, 1, false, mat3);
        }

        public void SetFloat(string name, float value)
        {
            var loc = GL.GetUniformLocation(Handle, name);
            GL.Uniform1(loc, value);
        }

        public void SetVec2(string name, float x, float y)
        {
            var loc = GL.GetUniformLocation(Handle, name);
            GL.Uniform2(loc, x, y);
        }

        public void SetVec3(string name, float x, float y, float z)
        {
            var loc = GL.GetUniformLocation(Handle, name);
            GL.Uniform3(loc, x, y, z);
        }

        public void SetVec4(string name, float x, float y, float z, float w)
        {
            var loc = GL.GetUniformLocation(Handle, name);
            GL.Uniform4(loc, x, y, z, w);
        }

        public void SetMatrix4(string name, float[] mat4)
        {
            var loc = GL.GetUniformLocation(Handle, name);
            GL.UniformMatrix4(loc, 1, false, mat4);
        }

        public void Dispose()
        {
            if (Handle != 0)
            {
                GL.DeleteProgram(Handle);
                Handle = 0;
            }
        }
    }
}
