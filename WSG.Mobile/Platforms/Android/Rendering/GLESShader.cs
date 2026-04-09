using Android.Opengl;
using System.Collections.Generic;

namespace WSG.Mobile.Platforms.Android.Rendering;

/// <summary>
/// Wraps a GLES 3.0 program. Mirrors Windows GLShader.cs — same API, different backend.
/// </summary>
internal sealed class GLESShader : IDisposable
{
    public int Handle { get; private set; }

    // Uniform location cache — avoids repeated GlGetUniformLocation calls
    private readonly Dictionary<string, int> _locs = new();

    public GLESShader(string vertexSource, string fragmentSource)
    {
        int vert = CompileShader(GLES30.GlVertexShader, vertexSource);
        int frag = CompileShader(GLES30.GlFragmentShader, fragmentSource);

        Handle = GLES30.GlCreateProgram();
        GLES30.GlAttachShader(Handle, vert);
        GLES30.GlAttachShader(Handle, frag);
        GLES30.GlLinkProgram(Handle);

        int[] status = new int[1];
        GLES30.GlGetProgramiv(Handle, GLES30.GlLinkStatus, status, 0);
        if (status[0] == 0)
        {
            string info = GLES30.GlGetProgramInfoLog(Handle) ?? "(no log)";
            GLES30.GlDeleteProgram(Handle);
            Handle = 0;
            throw new Exception($"Shader link error: {info}");
        }

        // Shaders can be freed after linking
        GLES30.GlDetachShader(Handle, vert);
        GLES30.GlDetachShader(Handle, frag);
        GLES30.GlDeleteShader(vert);
        GLES30.GlDeleteShader(frag);
    }

    private static int CompileShader(int type, string source)
    {
        int shader = GLES30.GlCreateShader(type);
        GLES30.GlShaderSource(shader, source);
        GLES30.GlCompileShader(shader);

        int[] status = new int[1];
        GLES30.GlGetShaderiv(shader, GLES30.GlCompileStatus, status, 0);
        if (status[0] == 0)
        {
            string info = GLES30.GlGetShaderInfoLog(shader) ?? "(no log)";
            GLES30.GlDeleteShader(shader);
            throw new Exception($"Shader compile error ({(type == GLES30.GlVertexShader ? "vert" : "frag")}): {info}");
        }
        return shader;
    }

    public void Use()
    {
        if (Handle != 0) GLES30.GlUseProgram(Handle);
    }

    // ── Uniform setters ──────────────────────────────────────────────────

    private int Loc(string name)
    {
        if (!_locs.TryGetValue(name, out int loc))
        {
            loc = GLES30.GlGetUniformLocation(Handle, name);
            _locs[name] = loc;
        }
        return loc;
    }

    public void SetInt(string name, int value)
    {
        int loc = Loc(name);
        if (loc >= 0) GLES30.GlUniform1i(loc, value);
    }

    public void SetBool(string name, bool value)
    {
        int loc = Loc(name);
        if (loc >= 0) GLES30.GlUniform1i(loc, value ? 1 : 0);
    }

    public void SetFloat(string name, float value)
    {
        int loc = Loc(name);
        if (loc >= 0) GLES30.GlUniform1f(loc, value);
    }

    public void SetVec2(string name, float x, float y)
    {
        int loc = Loc(name);
        if (loc >= 0) GLES30.GlUniform2f(loc, x, y);
    }

    public void SetVec3(string name, float x, float y, float z)
    {
        int loc = Loc(name);
        if (loc >= 0) GLES30.GlUniform3f(loc, x, y, z);
    }

    public void SetVec4(string name, float x, float y, float z, float w)
    {
        int loc = Loc(name);
        if (loc >= 0) GLES30.GlUniform4f(loc, x, y, z, w);
    }

    /// <summary>
    /// Upload a column-major 3×3 matrix.
    /// Layout matches Windows GLShader.SetMatrix3 — same float[9] order.
    /// </summary>
    public void SetMatrix3(string name, float[] mat3)
    {
        int loc = Loc(name);
        if (loc >= 0) GLES30.GlUniformMatrix3fv(loc, 1, false, mat3, 0);
    }

    // ── Cached location helpers (for hot-path uniforms) ──────────────────

    /// <summary>Pre-cache the location for a uniform to avoid dictionary lookup per frame.</summary>
    public int CacheLoc(string name)
    {
        int loc = GLES30.GlGetUniformLocation(Handle, name);
        _locs[name] = loc;
        return loc;
    }

    /// <summary>Set a float by pre-cached location (no dictionary lookup).</summary>
    public static void SetFloatAt(int loc, float value)
    {
        if (loc >= 0) GLES30.GlUniform1f(loc, value);
    }

    public static void SetBoolAt(int loc, bool value)
    {
        if (loc >= 0) GLES30.GlUniform1i(loc, value ? 1 : 0);
    }

    public static void SetMatrix3At(int loc, float[] mat3)
    {
        if (loc >= 0) GLES30.GlUniformMatrix3fv(loc, 1, false, mat3, 0);
    }

    public static void SetIntAt(int loc, int value)
    {
        if (loc >= 0) GLES30.GlUniform1i(loc, value);
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            GLES30.GlDeleteProgram(Handle);
            Handle = 0;
        }
    }
}
