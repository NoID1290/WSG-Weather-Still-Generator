using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using WeatherImageGenerator.Rendering.Common;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Rendering.OpenGL
{
    /// <summary>
    /// GPU-accelerated text and rectangle renderer for in-viewport HUD elements.
    /// Builds a font texture atlas from GDI+ at startup, then renders glyphs and
    /// rectangles via a dedicated UI shader using an orthographic projection.
    /// </summary>
    public class GLTextRenderer : IHudRenderer
    {
        private int _atlasTexture;
        private int _vao;
        private int _vbo;
        private GLShader? _shader;
        private int _projectionLoc;
        private int _colorLoc;
        private int _modeLoc;
        private int _atlasWidth;
        private int _atlasHeight;

        // Glyph metrics: per-character UV rect + advance
        private readonly Dictionary<char, GlyphInfo> _glyphs = new Dictionary<char, GlyphInfo>();

        private struct GlyphInfo
        {
            public float U0, V0, U1, V1; // UV coords in atlas
            public int Width, Height;     // pixel size of glyph cell
            public int AdvanceX;          // horizontal advance in pixels
            public int BearingY;          // vertical offset from baseline
        }

        // Dynamic VBO capacity
        private const int MAX_CHARS = 2048;   // up to 2K characters per frame
        private const int FLOATS_PER_VERT = 4; // x, y, u, v
        private const int VERTS_PER_CHAR = 6;  // 2 triangles = 6 vertices
        private readonly float[] _vertexBuffer = new float[MAX_CHARS * VERTS_PER_CHAR * FLOATS_PER_VERT];
        private int _vertexCount;

        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Initialize the text renderer. Must be called on the GL thread after MakeCurrent().
        /// </summary>
        public void Initialize()
        {
            if (IsInitialized) return;
            try
            {
                BuildFontAtlas("Segoe UI", 13f);
                LoadShader();
                SetupBuffers();
                IsInitialized = true;
                Console.WriteLine("[GLTextRenderer] Initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GLTextRenderer] Init failed: {ex.Message}");
            }
        }

        private void BuildFontAtlas(string fontFamily, float fontSize)
        {
            // Rasterize ASCII printable range (32-126) + extended chars (©, °, etc.)
            var charList = new List<char>();
            for (int i = 32; i <= 126; i++) charList.Add((char)i);
            // Add commonly needed extended characters
            charList.Add('©'); // U+00A9 copyright
            charList.Add('°'); // U+00B0 degree
            charList.Add('±'); // U+00B1 plus-minus
            charList.Add('²'); // U+00B2 superscript 2
            charList.Add('³'); // U+00B3 superscript 3
            charList.Add('µ'); // U+00B5 micro
            charList.Add('·'); // U+00B7 middle dot
            charList.Add('é'); // U+00E9 e-acute
            charList.Add('è'); // U+00E8 e-grave
            // HUD UI symbols (prevents '?' fallback)
            charList.Add('\u25B6'); // ▶ play
            charList.Add('\u25BC'); // ▼ down arrow
            charList.Add('\u25B2'); // ▲ up arrow
            charList.Add('\u25CE'); // ◎ center
            charList.Add('\u23EE'); // ⏮ skip back
            charList.Add('\u23ED'); // ⏭ skip forward
            charList.Add('\u23F8'); // ⏸ pause
            charList.Add('\u2212'); // − minus sign
            charList.Add('\u2026'); // … ellipsis
            charList.Add('\u25A0'); // ■ filled square
            charList.Add('\u25CB'); // ○ circle
            charList.Add('\u2316'); // ⌖ position indicator
            charList.Add('\u2013'); // – en dash
            charList.Add('\u2014'); // — em dash
            charList.Add('\u21BB'); // ↻ anticlockwise arrow (loop)
            charList.Add('\u00D7'); // × multiplication sign (close)

            int totalChars = charList.Count;
            int cellW = (int)(fontSize * 1.5f);
            int cellH = (int)(fontSize * 1.9f);
            int cols = 16;
            int rows = (int)Math.Ceiling(totalChars / (double)cols);
            _atlasWidth = cols * cellW;
            _atlasHeight = rows * cellH;

            using var bmp = new Bitmap(_atlasWidth, _atlasHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using var font = new Font(fontFamily, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.White);
            var sf = new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap | StringFormatFlags.NoClip
            };

            for (int i = 0; i < totalChars; i++)
            {
                char c = charList[i];
                int col = i % cols;
                int row = i / cols;
                float x = col * cellW;
                float y = row * cellH;

                // Measure character advance
                var charStr = c.ToString();
                var size = g.MeasureString(charStr, font, 1000, sf);
                int advance = Math.Max(1, (int)Math.Ceiling(size.Width));

                // Draw the glyph
                g.DrawString(charStr, font, brush, x, y, sf);

                // Store glyph info with UV coordinates
                _glyphs[c] = new GlyphInfo
                {
                    U0 = x / _atlasWidth,
                    V0 = y / _atlasHeight,
                    U1 = (x + advance) / _atlasWidth,
                    V1 = (y + cellH) / _atlasHeight,
                    Width = advance,
                    Height = cellH,
                    AdvanceX = advance,
                    BearingY = 0
                };
            }

            // Upload to GL texture (single-channel: extract alpha into red)
            _atlasTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _atlasTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            // Convert ARGB bitmap to single-channel R8 (use alpha as the value)
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int pixels = bmp.Width * bmp.Height;
                byte[] rgba = new byte[pixels * 4];
                Marshal.Copy(data.Scan0, rgba, 0, rgba.Length);

                // Extract alpha channel to R8
                byte[] r8 = new byte[pixels];
                for (int i = 0; i < pixels; i++)
                {
                    // BGRA layout: index*4+3 = alpha
                    r8[i] = rgba[i * 4 + 3];
                }

                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8,
                    bmp.Width, bmp.Height, 0,
                    OpenTK.Graphics.OpenGL4.PixelFormat.Red, PixelType.UnsignedByte, r8);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        private void LoadShader()
        {
            string vSrc, fSrc;
            if (!EmbeddedResourceLoader.TryReadText("Rendering/OpenGL/shaders/ui.vert.glsl", out vSrc))
            {
                vSrc = @"#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;
uniform mat4 uProjection;
out vec2 vTex;
void main() {
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
    vTex = aTex;
}";
            }

            if (!EmbeddedResourceLoader.TryReadText("Rendering/OpenGL/shaders/ui.frag.glsl", out fSrc))
            {
                fSrc = @"#version 330 core
in vec2 vTex;
out vec4 FragColor;
uniform sampler2D uFontAtlas;
uniform vec4 uColor;
uniform int uMode;
void main() {
    if (uMode == 0) {
        float a = texture(uFontAtlas, vTex).r;
        FragColor = vec4(uColor.rgb, uColor.a * a);
    } else {
        FragColor = uColor;
    }
}";
            }

            try
            {
                _shader = new GLShader(vSrc, fSrc);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GLTextRenderer] UI shader compile failed: {ex.Message}");
                return;
            }

            _projectionLoc = GL.GetUniformLocation(_shader.Handle, "uProjection");
            _colorLoc = GL.GetUniformLocation(_shader.Handle, "uColor");
            _modeLoc = GL.GetUniformLocation(_shader.Handle, "uMode");
        }

        private void SetupBuffers()
        {
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertexBuffer.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);

            // aPos (location=0): vec2
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, FLOATS_PER_VERT * sizeof(float), 0);
            // aTex (location=1): vec2
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, FLOATS_PER_VERT * sizeof(float), 2 * sizeof(float));

            GL.BindVertexArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        /// <summary>
        /// Begin a new frame of HUD rendering. Call once per paint before any Draw calls.
        /// </summary>
        public void BeginFrame(int viewportWidth, int viewportHeight)
        {
            _vertexCount = 0;
            if (_shader == null) return;

            _shader.Use();

            // Orthographic projection: pixel coords → NDC (origin = top-left)
            float[] ortho = MakeOrtho(0, viewportWidth, viewportHeight, 0, -1, 1);
            GL.UniformMatrix4(_projectionLoc, 1, false, ortho);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _atlasTexture);
        }

        /// <summary>
        /// Draw a filled rectangle at pixel coordinates.
        /// </summary>
        public void DrawRect(float x, float y, float w, float h, float r, float g, float b, float a)
        {
            if (_shader == null) return;

            // Flush any pending text first
            FlushText();

            GL.Uniform1(_modeLoc, 1); // rect mode
            GL.Uniform4(_colorLoc, r, g, b, a);

            // Emit 6 vertices for a quad (2 triangles)
            float[] verts = new float[]
            {
                x,     y,     0, 0,
                x + w, y,     0, 0,
                x + w, y + h, 0, 0,
                x,     y,     0, 0,
                x + w, y + h, 0, 0,
                x,     y + h, 0, 0
            };

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, verts.Length * sizeof(float), verts);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Queue text for rendering at the given pixel position.
        /// </summary>
        public void DrawText(string text, float x, float y, float r, float g, float b, float a)
        {
            if (_shader == null || string.IsNullOrEmpty(text)) return;

            // Flush any pending rect draws, switch to text mode
            GL.Uniform1(_modeLoc, 0); // text mode
            GL.Uniform4(_colorLoc, r, g, b, a);
            GL.BindTexture(TextureTarget.Texture2D, _atlasTexture);

            float cursorX = x;
            float cursorY = y;

            foreach (char c in text)
            {
                if (c == '\n')
                {
                    cursorX = x;
                    cursorY += (_glyphs.TryGetValue('A', out var aInfo) ? aInfo.Height : 20);
                    continue;
                }

                if (!_glyphs.TryGetValue(c, out var glyph))
                {
                    if (_glyphs.TryGetValue('?', out glyph)) { } else continue;
                }

                if (_vertexCount + VERTS_PER_CHAR * FLOATS_PER_VERT > _vertexBuffer.Length)
                {
                    FlushText();
                }

                float gx = cursorX;
                float gy = cursorY + glyph.BearingY;
                float gw = glyph.Width;
                float gh = glyph.Height;

                // Tri 1: TL, TR, BR
                int idx = _vertexCount;
                _vertexBuffer[idx++] = gx;      _vertexBuffer[idx++] = gy;      _vertexBuffer[idx++] = glyph.U0; _vertexBuffer[idx++] = glyph.V0;
                _vertexBuffer[idx++] = gx + gw;  _vertexBuffer[idx++] = gy;      _vertexBuffer[idx++] = glyph.U1; _vertexBuffer[idx++] = glyph.V0;
                _vertexBuffer[idx++] = gx + gw;  _vertexBuffer[idx++] = gy + gh;  _vertexBuffer[idx++] = glyph.U1; _vertexBuffer[idx++] = glyph.V1;
                // Tri 2: TL, BR, BL
                _vertexBuffer[idx++] = gx;      _vertexBuffer[idx++] = gy;      _vertexBuffer[idx++] = glyph.U0; _vertexBuffer[idx++] = glyph.V0;
                _vertexBuffer[idx++] = gx + gw;  _vertexBuffer[idx++] = gy + gh;  _vertexBuffer[idx++] = glyph.U1; _vertexBuffer[idx++] = glyph.V1;
                _vertexBuffer[idx++] = gx;      _vertexBuffer[idx++] = gy + gh;  _vertexBuffer[idx++] = glyph.U0; _vertexBuffer[idx++] = glyph.V1;
                _vertexCount = idx;

                cursorX += glyph.AdvanceX;
            }

            FlushText();
        }

        /// <summary>
        /// Measure the pixel width of a string without drawing it.
        /// </summary>
        public float MeasureTextWidth(string text)
        {
            float w = 0;
            if (text == null) return 0;
            foreach (char c in text)
            {
                if (c == '\n') continue;
                if (_glyphs.TryGetValue(c, out var g))
                    w += g.AdvanceX;
            }
            return w;
        }

        /// <summary>
        /// Get the line height in pixels.
        /// </summary>
        public float LineHeight => _glyphs.TryGetValue('A', out var g) ? g.Height : 20;

        /// <summary>
        /// Upload queued glyph vertices and draw them.
        /// </summary>
        private void FlushText()
        {
            if (_vertexCount == 0) return;

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, _vertexCount * sizeof(float), _vertexBuffer);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount / FLOATS_PER_VERT);
            GL.BindVertexArray(0);

            _vertexCount = 0;
        }

        /// <summary>
        /// End the frame — flush any remaining vertices.
        /// </summary>
        public void EndFrame()
        {
            FlushText();
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        private static float[] MakeOrtho(float left, float right, float bottom, float top, float near, float far)
        {
            float[] m = new float[16];
            m[0]  = 2f / (right - left);
            m[5]  = 2f / (top - bottom);
            m[10] = -2f / (far - near);
            m[12] = -(right + left) / (right - left);
            m[13] = -(top + bottom) / (top - bottom);
            m[14] = -(far + near) / (far - near);
            m[15] = 1f;
            return m;
        }

        public void Dispose()
        {
            if (_atlasTexture != 0) { try { GL.DeleteTexture(_atlasTexture); } catch { } _atlasTexture = 0; }
            if (_vbo != 0) { try { GL.DeleteBuffer(_vbo); } catch { } _vbo = 0; }
            if (_vao != 0) { try { GL.DeleteVertexArray(_vao); } catch { } _vao = 0; }
            _shader?.Dispose();
        }
    }
}
