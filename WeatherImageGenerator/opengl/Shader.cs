using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;

namespace WeatherImageGenerator.OpenGL
{
    public class Shader : IDisposable
    {
        public int Handle { get; private set; }

        public Shader(string vertexSource, string fragmentSource)
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
