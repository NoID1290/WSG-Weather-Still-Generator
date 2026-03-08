using System;
using OpenTK.Graphics.OpenGL4;
using WeatherImageGenerator.Rendering.Common;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Rendering.OpenGL
{
    /// <summary>
    /// Manages the GPU GRIB2 shader pipeline for OpenGL.
    /// Handles shader compilation, texture upload/update, and rendering for
    /// weather data visualization (data map, contour lines, clouds, atmosphere).
    /// </summary>
    public sealed class Grib2GpuPipelineGL : IDisposable
    {
        // Shader programs
        private GLShader? _dataShader;
        private GLShader? _contourShader;

        // GPU textures
        private int _dataTexture;      // R32F raw data grid
        private int _paletteTexture;   // 1D RGBA palette (256 texels)
        private int _dataWidth;
        private int _dataHeight;

        // Cached uniform locations for _dataShader
        private int _dsTransformLoc = -1;
        private int _dsDataTexLoc = -1;
        private int _dsPaletteTexLoc = -1;
        private int _dsOpacityLoc = -1;
        private int _dsTimeLoc = -1;
        private int _dsGlowLoc = -1;
        private int _dsFieldTypeLoc = -1;
        private int _dsDataMinLoc = -1;
        private int _dsDataMaxLoc = -1;

        // Cached uniform locations for _contourShader
        private int _csTransformLoc = -1;
        private int _csDataTexLoc = -1;
        private int _csDataMinLoc = -1;
        private int _csDataMaxLoc = -1;
        private int _csIntervalLoc = -1;
        private int _csWidthLoc = -1;
        private int _csOpacityLoc = -1;
        private int _csColorLoc = -1;

        // Current data state
        private Grib2GpuRenderData? _currentData;
        private bool _initialized;

        public bool IsActive => _currentData != null && _initialized;

        /// <summary>
        /// Initialize shader programs. Call once after GL context is available.
        /// </summary>
        public bool Initialize()
        {
            if (_initialized) return true;

            try
            {
                // Load grib2_data shaders
                if (EmbeddedResourceLoader.TryReadText("Rendering/OpenGL/shaders/grib2_data.vert.glsl", out var dataVert) &&
                    EmbeddedResourceLoader.TryReadText("Rendering/OpenGL/shaders/grib2_data.frag.glsl", out var dataFrag))
                {
                    _dataShader = new GLShader(dataVert, dataFrag);
                    CacheDataShaderUniforms();
                }
                else
                {
                    Console.WriteLine("[Grib2GPU-GL] Failed to load grib2_data shaders");
                    return false;
                }

                // Load grib2_contour shaders
                if (EmbeddedResourceLoader.TryReadText("Rendering/OpenGL/shaders/grib2_contour.vert.glsl", out var contVert) &&
                    EmbeddedResourceLoader.TryReadText("Rendering/OpenGL/shaders/grib2_contour.frag.glsl", out var contFrag))
                {
                    _contourShader = new GLShader(contVert, contFrag);
                    CacheContourShaderUniforms();
                }

                _initialized = true;
                Console.WriteLine("[Grib2GPU-GL] Pipeline initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Grib2GPU-GL] Init failed: {ex.Message}");
                return false;
            }
        }

        private void CacheDataShaderUniforms()
        {
            if (_dataShader == null) return;
            int h = _dataShader.Handle;
            _dsTransformLoc = GL.GetUniformLocation(h, "uTransform");
            _dsDataTexLoc = GL.GetUniformLocation(h, "uDataTex");
            _dsPaletteTexLoc = GL.GetUniformLocation(h, "uPaletteTex");
            _dsOpacityLoc = GL.GetUniformLocation(h, "uOpacity");
            _dsTimeLoc = GL.GetUniformLocation(h, "uTime");
            _dsGlowLoc = GL.GetUniformLocation(h, "uEnableGlow");
            _dsFieldTypeLoc = GL.GetUniformLocation(h, "uFieldType");
            _dsDataMinLoc = GL.GetUniformLocation(h, "uDataMin");
            _dsDataMaxLoc = GL.GetUniformLocation(h, "uDataMax");
        }

        private void CacheContourShaderUniforms()
        {
            if (_contourShader == null) return;
            int h = _contourShader.Handle;
            _csTransformLoc = GL.GetUniformLocation(h, "uTransform");
            _csDataTexLoc = GL.GetUniformLocation(h, "uDataTex");
            _csDataMinLoc = GL.GetUniformLocation(h, "uDataMin");
            _csDataMaxLoc = GL.GetUniformLocation(h, "uDataMax");
            _csIntervalLoc = GL.GetUniformLocation(h, "uContourInterval");
            _csWidthLoc = GL.GetUniformLocation(h, "uContourWidth");
            _csOpacityLoc = GL.GetUniformLocation(h, "uOpacity");
            _csColorLoc = GL.GetUniformLocation(h, "uContourColor");
        }

        /// <summary>
        /// Upload new GRIB2 data to the GPU. Creates or updates textures as needed.
        /// </summary>
        public void UploadData(Grib2GpuRenderData data)
        {
            if (!_initialized && !Initialize()) return;

            // Upload R32F data texture
            if (_dataTexture == 0)
                _dataTexture = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, _dataTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f,
                data.GridWidth, data.GridHeight, 0,
                PixelFormat.Red, PixelType.Float, data.GridData);

            _dataWidth = data.GridWidth;
            _dataHeight = data.GridHeight;
            GL.BindTexture(TextureTarget.Texture2D, 0);

            // Upload 1D palette texture
            if (_paletteTexture == 0)
                _paletteTexture = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture1D, _paletteTexture);
            GL.TexParameter(TextureTarget.Texture1D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture1D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture1D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            GL.TexImage1D(TextureTarget.Texture1D, 0, PixelInternalFormat.Rgba,
                256, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data.PaletteData);

            GL.BindTexture(TextureTarget.Texture1D, 0);

            _currentData = data;

            var err = GL.GetError();
            if (err != ErrorCode.NoError)
                Console.WriteLine($"[Grib2GPU-GL] GL error after upload: {err}");
        }

        /// <summary>
        /// Render the GRIB2 data overlay using the data shader.
        /// Call this during the main render loop with blending enabled.
        /// </summary>
        /// <param name="transformMatrix">3x3 geo-to-screen transform (column-major float[9]).</param>
        /// <param name="time">Elapsed time in seconds for animation effects.</param>
        /// <param name="vaoHandle">VAO handle for the fullscreen quad.</param>
        public void Render(float[] transformMatrix, float time, int vaoHandle)
        {
            if (!IsActive || _dataShader == null) return;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // ── Data visualization pass ──
            _dataShader.Use();

            // Set transform
            if (_dsTransformLoc >= 0)
                GL.UniformMatrix3(_dsTransformLoc, 1, false, transformMatrix);

            // Bind data texture to unit 0
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _dataTexture);
            if (_dsDataTexLoc >= 0)
                GL.Uniform1(_dsDataTexLoc, 0);

            // Bind palette to unit 1
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture1D, _paletteTexture);
            if (_dsPaletteTexLoc >= 0)
                GL.Uniform1(_dsPaletteTexLoc, 1);

            // Set uniforms
            if (_dsOpacityLoc >= 0)
                GL.Uniform1(_dsOpacityLoc, _currentData!.Opacity);
            if (_dsTimeLoc >= 0)
                GL.Uniform1(_dsTimeLoc, time);
            if (_dsGlowLoc >= 0)
                GL.Uniform1(_dsGlowLoc, _currentData!.EnableGlow ? 1.0f : 0.0f);
            if (_dsFieldTypeLoc >= 0)
                GL.Uniform1(_dsFieldTypeLoc, GetFieldTypeIndex(_currentData!.FieldType));
            if (_dsDataMinLoc >= 0)
                GL.Uniform1(_dsDataMinLoc, _currentData!.DataMin);
            if (_dsDataMaxLoc >= 0)
                GL.Uniform1(_dsDataMaxLoc, _currentData!.DataMax);

            // Draw
            GL.BindVertexArray(vaoHandle);
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

            // ── Contour lines pass ──
            if (_currentData!.EnableContours && _contourShader != null)
            {
                _contourShader.Use();

                if (_csTransformLoc >= 0)
                    GL.UniformMatrix3(_csTransformLoc, 1, false, transformMatrix);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, _dataTexture);
                if (_csDataTexLoc >= 0)
                    GL.Uniform1(_csDataTexLoc, 0);

                if (_csDataMinLoc >= 0)
                    GL.Uniform1(_csDataMinLoc, _currentData.DataMin);
                if (_csDataMaxLoc >= 0)
                    GL.Uniform1(_csDataMaxLoc, _currentData.DataMax);
                if (_csIntervalLoc >= 0)
                    GL.Uniform1(_csIntervalLoc, _currentData.ContourInterval);
                if (_csWidthLoc >= 0)
                    GL.Uniform1(_csWidthLoc, 1.5f);
                if (_csOpacityLoc >= 0)
                    GL.Uniform1(_csOpacityLoc, _currentData.Opacity * 0.8f);
                if (_csColorLoc >= 0)
                    GL.Uniform4(_csColorLoc, 0.15f, 0.15f, 0.15f, 0.85f);

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            }

            // Reset state
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture1D, 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Clear GPU resources and deactivate the pipeline.
        /// </summary>
        public void Clear()
        {
            _currentData = null;

            if (_dataTexture != 0)
            {
                GL.DeleteTexture(_dataTexture);
                _dataTexture = 0;
            }
            if (_paletteTexture != 0)
            {
                GL.DeleteTexture(_paletteTexture);
                _paletteTexture = 0;
            }
        }

        private static int GetFieldTypeIndex(Grib2FieldType ft) => ft switch
        {
            Grib2FieldType.Temperature => 0,
            Grib2FieldType.Wind => 1,
            Grib2FieldType.Precipitation => 2,
            Grib2FieldType.CloudCover => 3,
            Grib2FieldType.Pressure => 4,
            Grib2FieldType.CAPE => 5,
            _ => 0
        };

        public void Dispose()
        {
            Clear();
            _dataShader?.Dispose();
            _contourShader?.Dispose();
            _dataShader = null;
            _contourShader = null;
            _initialized = false;
        }
    }
}
