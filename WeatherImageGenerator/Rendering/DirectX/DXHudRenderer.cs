using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using WeatherImageGenerator.Rendering.Common;

using DXBlend = Silk.NET.Direct3D11.Blend;

namespace WeatherImageGenerator.Rendering.DirectX
{
    /// <summary>
    /// DirectX 11 HUD text and rectangle renderer using Silk.NET.
    /// Mirrors GLTextRenderer: builds a GDI+ font atlas, uploads as D3D11 texture,
    /// and renders batched glyph/rect quads via the UI shader with orthographic projection.
    /// </summary>
    public unsafe class DXHudRenderer : IHudRenderer
    {
        private bool _disposed;

        // D3D11 resources (borrowed from DXMapRenderer – caller owns lifetime)
        private ID3D11Device* _device;
        private ID3D11DeviceContext* _context;

        // Font atlas texture
        private ID3D11Texture2D* _atlasTexture;
        private ID3D11ShaderResourceView* _atlasSrv;
        private ID3D11SamplerState* _sampler;
        private int _atlasWidth, _atlasHeight;

        // UI shader
        private DXShader? _uiShader;

        // Dynamic vertex buffer for batched quads
        private ID3D11Buffer* _vertexBuffer;
        private const int MAX_CHARS = 2048;
        private const int FLOATS_PER_VERT = 4; // x, y, u, v
        private const int VERTS_PER_CHAR = 6;
        private readonly float[] _cpuVertexBuffer = new float[MAX_CHARS * VERTS_PER_CHAR * FLOATS_PER_VERT];
        private int _vertexCount;

        // Glyph metrics
        private readonly Dictionary<char, GlyphInfo> _glyphs = new();
        private struct GlyphInfo
        {
            public float U0, V0, U1, V1;
            public int Width, Height;
            public int AdvanceX;
            public int BearingY;
        }

        // Blend state for alpha blending
        private ID3D11BlendState* _blendState;

        // Current projection matrix and frame state
        private float[] _projection = new float[16];
        private int _vpWidth, _vpHeight;

        public bool IsInitialized { get; private set; }
        public float LineHeight { get; private set; } = 20f;

        /// <summary>
        /// Set the D3D11 device and context. Must be called before Initialize().
        /// </summary>
        public void SetDevice(ID3D11Device* device, ID3D11DeviceContext* context)
        {
            _device = device;
            _context = context;
        }

        public void Initialize()
        {
            if (IsInitialized) return;
            if (_device == null)
            {
                Console.WriteLine("[DXHudRenderer] Cannot initialize — no D3D11 device set.");
                return;
            }
            try
            {
                BuildFontAtlas("Segoe UI", 13f);
                CreateUIShader();
                CreateVertexBuffer();
                CreateBlendState();
                CreateSampler();
                IsInitialized = true;
                Console.WriteLine("[DXHudRenderer] Initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DXHudRenderer] Init failed: {ex.Message}");
            }
        }

        private void BuildFontAtlas(string fontFamily, float fontSize)
        {
            var charList = new List<char>();
            for (int i = 32; i <= 126; i++) charList.Add((char)i);
            charList.AddRange(new[] {
                '©', '°', '±', '²', '³', 'µ', '·', 'é', 'è',
                '\u25B6', '\u25BC', '\u25B2', '\u25CE', '\u23EE', '\u23ED',
                '\u23F8', '\u2212', '\u2026', '\u25A0', '\u25CB',
                '\u2316', '\u2013', '\u2014'
            });

            int cellW = (int)(fontSize * 1.5f);
            int cellH = (int)(fontSize * 1.9f);
            LineHeight = cellH;
            int cols = 16;
            int rows = (int)Math.Ceiling(charList.Count / (double)cols);
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

            for (int i = 0; i < charList.Count; i++)
            {
                char c = charList[i];
                int col = i % cols;
                int row = i / cols;
                float x = col * cellW;
                float y = row * cellH;

                var size = g.MeasureString(c.ToString(), font, 1000, sf);
                int advance = Math.Max(1, (int)Math.Ceiling(size.Width));
                g.DrawString(c.ToString(), font, brush, x, y, sf);

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

            // Extract alpha to R8 and upload as D3D11 texture
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int pixels = bmp.Width * bmp.Height;
                byte[] rgba = new byte[pixels * 4];
                Marshal.Copy(data.Scan0, rgba, 0, rgba.Length);
                byte[] r8 = new byte[pixels];
                for (int i = 0; i < pixels; i++)
                    r8[i] = rgba[i * 4 + 3]; // Alpha channel

                var texDesc = new Texture2DDesc
                {
                    Width = (uint)_atlasWidth,
                    Height = (uint)_atlasHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.FormatR8Unorm,
                    SampleDesc = new SampleDesc(1, 0),
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.ShaderResource,
                    CPUAccessFlags = 0,
                    MiscFlags = 0
                };

                fixed (byte* pData = r8)
                {
                    var initData = new SubresourceData
                    {
                        PSysMem = pData,
                        SysMemPitch = (uint)_atlasWidth,
                        SysMemSlicePitch = 0
                    };
                    ID3D11Texture2D* tex = null;
                    SilkMarshal.ThrowHResult(_device->CreateTexture2D(ref texDesc, ref initData, ref tex));
                    _atlasTexture = tex;
                }

                // Create SRV
                var srvDesc = new ShaderResourceViewDesc
                {
                    Format = Format.FormatR8Unorm,
                    ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2D,
                };
                srvDesc.Texture2D.MostDetailedMip = 0;
                srvDesc.Texture2D.MipLevels = 1;

                ID3D11ShaderResourceView* srv = null;
                SilkMarshal.ThrowHResult(
                    _device->CreateShaderResourceView(
                        (ID3D11Resource*)_atlasTexture, ref srvDesc, ref srv));
                _atlasSrv = srv;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private void CreateUIShader()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var vsPath = Path.Combine(baseDir, "Rendering", "DirectX", "shaders", "ui.vs.hlsl");
            var psPath = Path.Combine(baseDir, "Rendering", "DirectX", "shaders", "ui.ps.hlsl");

            string vsSrc, psSrc;
            try { vsSrc = File.ReadAllText(vsPath); }
            catch { vsSrc = GetFallbackUIVS(); }
            try { psSrc = File.ReadAllText(psPath); }
            catch { psSrc = GetFallbackUIPS(); }

            // Input layout: POSITION (float2), TEXCOORD0 (float2)
            var inputElements = new InputElementDesc[]
            {
                new InputElementDesc
                {
                    SemanticName = (byte*)SilkMarshal.StringToPtr("POSITION", NativeStringEncoding.UTF8),
                    SemanticIndex = 0,
                    Format = Format.FormatR32G32Float,
                    InputSlot = 0,
                    AlignedByteOffset = 0,
                    InputSlotClass = InputClassification.PerVertexData,
                    InstanceDataStepRate = 0
                },
                new InputElementDesc
                {
                    SemanticName = (byte*)SilkMarshal.StringToPtr("TEXCOORD", NativeStringEncoding.UTF8),
                    SemanticIndex = 0,
                    Format = Format.FormatR32G32Float,
                    InputSlot = 0,
                    AlignedByteOffset = 8,
                    InputSlotClass = InputClassification.PerVertexData,
                    InstanceDataStepRate = 0
                }
            };

            // Uniform layout: VS has projection matrix, PS has color + mode
            var uniforms = new Dictionary<string, (bool, int, int)>
            {
                ["uProjection"] = (true, 0, 64),   // float4x4 = 64 bytes in VS cbuffer
                ["uColor"] = (false, 0, 16),        // float4 = 16 bytes in PS cbuffer
                ["uMode"] = (false, 16, 4),          // int = 4 bytes
            };

            _uiShader = new DXShader(_device, _context, vsSrc, psSrc, "main", "main", inputElements, uniforms);
        }

        private void CreateVertexBuffer()
        {
            var desc = new BufferDesc
            {
                ByteWidth = (uint)(_cpuVertexBuffer.Length * sizeof(float)),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.VertexBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
                MiscFlags = 0,
                StructureByteStride = 0
            };
            ID3D11Buffer* buf = null;
            SilkMarshal.ThrowHResult(_device->CreateBuffer(ref desc, (SubresourceData*)null, ref buf));
            _vertexBuffer = buf;
        }

        private void CreateBlendState()
        {
            var desc = new BlendDesc();
            desc.RenderTarget[0] = new RenderTargetBlendDesc
            {
                BlendEnable = 1,
                SrcBlend = DXBlend.SrcAlpha,
                DestBlend = DXBlend.InvSrcAlpha,
                BlendOp = BlendOp.Add,
                SrcBlendAlpha = DXBlend.One,
                DestBlendAlpha = DXBlend.InvSrcAlpha,
                BlendOpAlpha = BlendOp.Add,
                RenderTargetWriteMask = (byte)ColorWriteEnable.All,
            };
            ID3D11BlendState* bs = null;
            SilkMarshal.ThrowHResult(_device->CreateBlendState(ref desc, ref bs));
            _blendState = bs;
        }

        private void CreateSampler()
        {
            var desc = new SamplerDesc
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                MipLODBias = 0f,
                MaxAnisotropy = 1,
                ComparisonFunc = ComparisonFunc.Never,
                MinLOD = 0,
                MaxLOD = float.MaxValue,
            };
            ID3D11SamplerState* ss = null;
            SilkMarshal.ThrowHResult(_device->CreateSamplerState(ref desc, ref ss));
            _sampler = ss;
        }

        public void BeginFrame(int viewportWidth, int viewportHeight)
        {
            _vpWidth = viewportWidth;
            _vpHeight = viewportHeight;
            _vertexCount = 0;

            // Orthographic projection: pixel coords → clip space
            // Maps (0, 0) at top-left, (w, h) at bottom-right to (-1,1)...(1,-1)
            float L = 0, R = viewportWidth, T = 0, B = viewportHeight;
            _projection = new float[]
            {
                2f/(R-L),      0f,            0f, 0f,
                0f,            2f/(T-B),      0f, 0f,
                0f,            0f,            0.5f, 0f,
                (R+L)/(L-R),  (T+B)/(B-T),  0.5f, 1f
            };
        }

        public void DrawRect(float x, float y, float w, float h, float r, float g, float b, float a)
        {
            // Emit 6 vertices for a solid-color quad (no texture)
            if (_vertexCount + 6 > MAX_CHARS * VERTS_PER_CHAR) return;

            int i = _vertexCount * FLOATS_PER_VERT;
            float x1 = x, y1 = y, x2 = x + w, y2 = y + h;

            // Triangle 1
            _cpuVertexBuffer[i + 0] = x1; _cpuVertexBuffer[i + 1] = y1; _cpuVertexBuffer[i + 2] = 0f; _cpuVertexBuffer[i + 3] = 0f;
            _cpuVertexBuffer[i + 4] = x2; _cpuVertexBuffer[i + 5] = y1; _cpuVertexBuffer[i + 6] = 1f; _cpuVertexBuffer[i + 7] = 0f;
            _cpuVertexBuffer[i + 8] = x2; _cpuVertexBuffer[i + 9] = y2; _cpuVertexBuffer[i + 10] = 1f; _cpuVertexBuffer[i + 11] = 1f;
            // Triangle 2
            _cpuVertexBuffer[i + 12] = x1; _cpuVertexBuffer[i + 13] = y1; _cpuVertexBuffer[i + 14] = 0f; _cpuVertexBuffer[i + 15] = 0f;
            _cpuVertexBuffer[i + 16] = x2; _cpuVertexBuffer[i + 17] = y2; _cpuVertexBuffer[i + 18] = 1f; _cpuVertexBuffer[i + 19] = 1f;
            _cpuVertexBuffer[i + 20] = x1; _cpuVertexBuffer[i + 21] = y2; _cpuVertexBuffer[i + 22] = 0f; _cpuVertexBuffer[i + 23] = 1f;

            // Flush with color and mode=1 (flat rect)
            _vertexCount += 6;
            FlushBatch(r, g, b, a, 1);
            _vertexCount = 0; // Reset after flush
        }

        public void DrawText(string text, float x, float y, float r, float g, float b, float a)
        {
            if (string.IsNullOrEmpty(text)) return;

            float cursorX = x;
            int startVert = 0;

            foreach (char c in text)
            {
                if (!_glyphs.TryGetValue(c, out var glyph))
                {
                    if (_glyphs.TryGetValue('?', out var fallback))
                        glyph = fallback;
                    else
                    {
                        cursorX += 8;
                        continue;
                    }
                }

                if (startVert + 6 > MAX_CHARS * VERTS_PER_CHAR) break;

                int i = startVert * FLOATS_PER_VERT;
                float x1 = cursorX, y1 = y;
                float x2 = cursorX + glyph.Width, y2 = y + glyph.Height;

                // Triangle 1
                _cpuVertexBuffer[i + 0] = x1; _cpuVertexBuffer[i + 1] = y1; _cpuVertexBuffer[i + 2] = glyph.U0; _cpuVertexBuffer[i + 3] = glyph.V0;
                _cpuVertexBuffer[i + 4] = x2; _cpuVertexBuffer[i + 5] = y1; _cpuVertexBuffer[i + 6] = glyph.U1; _cpuVertexBuffer[i + 7] = glyph.V0;
                _cpuVertexBuffer[i + 8] = x2; _cpuVertexBuffer[i + 9] = y2; _cpuVertexBuffer[i + 10] = glyph.U1; _cpuVertexBuffer[i + 11] = glyph.V1;
                // Triangle 2
                _cpuVertexBuffer[i + 12] = x1; _cpuVertexBuffer[i + 13] = y1; _cpuVertexBuffer[i + 14] = glyph.U0; _cpuVertexBuffer[i + 15] = glyph.V0;
                _cpuVertexBuffer[i + 16] = x2; _cpuVertexBuffer[i + 17] = y2; _cpuVertexBuffer[i + 18] = glyph.U1; _cpuVertexBuffer[i + 19] = glyph.V1;
                _cpuVertexBuffer[i + 20] = x1; _cpuVertexBuffer[i + 21] = y2; _cpuVertexBuffer[i + 22] = glyph.U0; _cpuVertexBuffer[i + 23] = glyph.V1;

                startVert += 6;
                cursorX += glyph.AdvanceX;
            }

            _vertexCount = startVert;
            if (_vertexCount > 0)
            {
                FlushBatch(r, g, b, a, 0); // mode=0 = textured glyph
                _vertexCount = 0;
            }
        }

        private void FlushBatch(float r, float g, float b, float a, int mode)
        {
            if (_uiShader == null || _vertexCount == 0) return;

            // Upload vertex data
            MappedSubresource mapped;
            SilkMarshal.ThrowHResult(
                _context->Map((ID3D11Resource*)_vertexBuffer, 0, Map.WriteDiscard, 0, &mapped));
            int bytesToCopy = _vertexCount * FLOATS_PER_VERT * sizeof(float);
            fixed (float* pData = _cpuVertexBuffer)
                Unsafe.CopyBlock(mapped.PData, pData, (uint)bytesToCopy);
            _context->Unmap((ID3D11Resource*)_vertexBuffer, 0);

            // Set pipeline state
            _uiShader.SetMatrix4("uProjection", _projection);
            _uiShader.SetVec4("uColor", r, g, b, a);
            _uiShader.SetInt("uMode", mode);
            _uiShader.Use();

            // Set blend state
            float* blendFactor = stackalloc float[4] { 0, 0, 0, 0 };
            _context->OMSetBlendState(_blendState, blendFactor, 0xffffffff);

            // Bind vertex buffer
            uint stride = (uint)(FLOATS_PER_VERT * sizeof(float));
            uint offset = 0;
            var vb = _vertexBuffer;
            _context->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
            _context->IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);

            // Bind font atlas texture (for text mode)
            if (mode == 0)
            {
                var srv = _atlasSrv;
                _context->PSSetShaderResources(0, 1, &srv);
                var samp = _sampler;
                _context->PSSetSamplers(0, 1, &samp);
            }

            _context->Draw((uint)_vertexCount, 0);
        }

        public float MeasureTextWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            float w = 0;
            foreach (char c in text)
            {
                if (_glyphs.TryGetValue(c, out var gInfo))
                    w += gInfo.AdvanceX;
                else
                    w += 8;
            }
            return w;
        }

        public void EndFrame()
        {
            // Final flush if needed
            if (_vertexCount > 0)
            {
                FlushBatch(1f, 1f, 1f, 1f, 0);
                _vertexCount = 0;
            }
        }

        private static string GetFallbackUIVS() => @"
cbuffer ProjectionCB : register(b0) { row_major float4x4 uProjection; };
struct VS_INPUT { float2 aPos : POSITION; float2 aTex : TEXCOORD0; };
struct VS_OUTPUT { float4 Position : SV_POSITION; float2 vTex : TEXCOORD0; };
VS_OUTPUT main(VS_INPUT input) {
    VS_OUTPUT output;
    output.Position = mul(float4(input.aPos, 0.0, 1.0), uProjection);
    output.vTex = input.aTex;
    return output;
}";

        private static string GetFallbackUIPS() => @"
Texture2D uFontAtlas : register(t0);
SamplerState uSampler : register(s0);
cbuffer UIParams : register(b0) { float4 uColor; int uMode; float3 _pad; };
struct PS_INPUT { float4 Position : SV_POSITION; float2 vTex : TEXCOORD0; };
float4 main(PS_INPUT input) : SV_TARGET {
    if (uMode == 0) { float a = uFontAtlas.Sample(uSampler, input.vTex).r; return float4(uColor.rgb, uColor.a * a); }
    else { return uColor; }
}";

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _uiShader?.Dispose();
                if (_atlasSrv != null) { _atlasSrv->Release(); _atlasSrv = null; }
                if (_atlasTexture != null) { _atlasTexture->Release(); _atlasTexture = null; }
                if (_sampler != null) { _sampler->Release(); _sampler = null; }
                if (_vertexBuffer != null) { _vertexBuffer->Release(); _vertexBuffer = null; }
                if (_blendState != null) { _blendState->Release(); _blendState = null; }
            }
        }
    }
}
