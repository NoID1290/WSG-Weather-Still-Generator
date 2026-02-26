using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using WeatherImageGenerator.Rendering.Common;
using VkImage = Silk.NET.Vulkan.Image;

namespace WeatherImageGenerator.Rendering.Vulkan
{
    /// <summary>
    /// Vulkan HUD text and rectangle renderer.
    /// Implements IHudRenderer with a font atlas texture, batched vertex buffer,
    /// and a dedicated UI graphics pipeline using push constants.
    ///
    /// Push constant layout (ui.vert/frag.glsl):
    ///   bytes 0-63:  mat4 uProjection
    ///   bytes 64-79: vec4 uColor
    ///   bytes 80-83: int uMode (0=glyph, 1=rect)
    ///   Total: 84 bytes
    /// </summary>
    public unsafe class VulkanHudRenderer : IHudRenderer, IDisposable
    {
        // Device references (set via SetDevice)
        private Vk? _vk;
        private Device _device;
        private PhysicalDevice _physicalDevice;
        private CommandBuffer _activeCmd;
        private RenderPass _renderPass;

        // Pipeline
        private VulkanShader? _uiShader;

        // Font atlas
        private VkImage _fontAtlasImage;
        private DeviceMemory _fontAtlasMemory;
        private ImageView _fontAtlasView;
        private Sampler _fontSampler;

        // Descriptor pool + set for font atlas
        private DescriptorPool _descriptorPool;
        private DescriptorSet _fontDescriptorSet;

        // Batched vertex buffer
        private Silk.NET.Vulkan.Buffer _vertexBuffer;
        private DeviceMemory _vertexMemory;
        private void* _vertexMapped;
        private const int MAX_VERTICES = 16384;
        private const int VERTEX_SIZE = 4 * sizeof(float); // pos(2) + tex(2)
        private int _vertexCount;

        // Font atlas metrics
        private readonly Dictionary<char, (int width, int col, int row)> _glyphMap = new();
        private int _cellW = 10, _cellH = 20;
        private int _atlasW = 160, _atlasH = 120;

        // Frame state
        private float[] _projection = new float[16];
        private int _vpWidth, _vpHeight;
        private bool _disposed;

        public bool IsInitialized { get; private set; }
        public float LineHeight => _cellH;

        /// <summary>
        /// Sets the Vulkan device handles. Must be called before Initialize().
        /// </summary>
        public void SetDevice(Vk vk, Device device, PhysicalDevice physDevice, RenderPass renderPass)
        {
            _vk = vk;
            _device = device;
            _physicalDevice = physDevice;
            _renderPass = renderPass;
        }

        /// <summary>
        /// Sets the active command buffer for the current frame. Call before BeginFrame.
        /// </summary>
        public void SetCommandBuffer(CommandBuffer cmd) => _activeCmd = cmd;

        public void Initialize()
        {
            if (_vk == null) throw new InvalidOperationException("SetDevice must be called before Initialize");

            BuildFontAtlas();
            CreateFontAtlasTexture();
            CreateSampler();
            CreateVertexBuffer();
            CreateDescriptorPool();
            CreateDescriptorSet();
            CreatePipeline();

            IsInitialized = true;
            Console.WriteLine("[VulkanHudRenderer] Initialized");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Font atlas generation (bitmap → glyph metrics)
        // ═══════════════════════════════════════════════════════════════════
        private Bitmap? _atlasBitmap;

        private void BuildFontAtlas()
        {
            var font = new Font("Consolas", 13f, FontStyle.Regular, GraphicsUnit.Pixel);

            // Build char list: ASCII 32-126 + extended UI symbols
            var charList = new List<char>();
            for (int i = 32; i <= 126; i++) charList.Add((char)i);
            charList.AddRange(new[] {
                '\u00A9', '\u00B0', '\u00B1', '\u00B2', '\u00B3', '\u00B5', '\u00B7',
                '\u00E9', '\u00E8', '\u00D7',
                '\u25B6', '\u25BC', '\u25B2', '\u25CE',
                '\u23EE', '\u23ED', '\u23F8',
                '\u2212', '\u2026', '\u25A0', '\u25CB',
                '\u2316', '\u2013', '\u2014', '\u21BB'
            });

            int cols = 16;
            int totalRows = (int)Math.Ceiling(charList.Count / (double)cols);

            using var measure = new Bitmap(1, 1);
            using var gm = Graphics.FromImage(measure);
            gm.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Measure widths for all chars
            _cellW = 10;
            foreach (char c in charList)
            {
                var sz = gm.MeasureString(c.ToString(), font, 0, System.Drawing.StringFormat.GenericTypographic);
                int w = Math.Max(1, (int)Math.Ceiling(sz.Width));
                _cellW = Math.Max(_cellW, w);
            }
            _cellW += 2;

            var fm = gm.MeasureString("M", font);
            _cellH = Math.Max(14, (int)Math.Ceiling(fm.Height));
            _atlasW = _cellW * cols;
            _atlasH = _cellH * totalRows;

            // Build glyph map with atlas positions
            _glyphMap.Clear();
            for (int i = 0; i < charList.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                var sz = gm.MeasureString(charList[i].ToString(), font, 0, System.Drawing.StringFormat.GenericTypographic);
                int gw = Math.Max(1, (int)Math.Ceiling(sz.Width));
                _glyphMap[charList[i]] = (gw, col, row);
            }

            _atlasBitmap = new Bitmap(_atlasW, _atlasH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(_atlasBitmap);
            g.Clear(Color.Transparent);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using var brush = new SolidBrush(Color.White);
            for (int i = 0; i < charList.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                g.DrawString(charList[i].ToString(), font, brush, col * _cellW, row * _cellH,
                    System.Drawing.StringFormat.GenericTypographic);
            }

            font.Dispose();
        }

        // ═══════════════════════════════════════════════════════════════════
        // Vulkan resource creation
        // ═══════════════════════════════════════════════════════════════════
        private void CreateFontAtlasTexture()
        {
            if (_atlasBitmap == null || _vk == null) return;

            int w = _atlasBitmap.Width, h = _atlasBitmap.Height;
            var bmpData = _atlasBitmap.LockBits(
                new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            // Convert BGRA → R8 (luminance from alpha channel for glyph atlas)
            byte[] pixels = new byte[w * h];
            byte* src = (byte*)bmpData.Scan0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int srcIdx = y * bmpData.Stride + x * 4;
                    // Use alpha channel of white-on-transparent rendering
                    pixels[y * w + x] = src[srcIdx + 3];
                }
            }
            _atlasBitmap.UnlockBits(bmpData);
            _atlasBitmap.Dispose();
            _atlasBitmap = null;

            // Create Vulkan image
            var imgInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = Format.R8Unorm,
                Extent = new Extent3D((uint)w, (uint)h, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Linear, // Linear for simple mapping
                Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Preinitialized,
            };

            fixed (VkImage* pImg = &_fontAtlasImage)
                CheckResult(_vk.CreateImage(_device, &imgInfo, null, pImg));

            MemoryRequirements memReq;
            _vk.GetImageMemoryRequirements(_device, _fontAtlasImage, &memReq);

            var allocInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memReq.Size,
                MemoryTypeIndex = FindMemoryType(memReq.MemoryTypeBits,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
            };

            fixed (DeviceMemory* pMem = &_fontAtlasMemory)
                CheckResult(_vk.AllocateMemory(_device, &allocInfo, null, pMem));

            _vk.BindImageMemory(_device, _fontAtlasImage, _fontAtlasMemory, 0);

            // Map and copy pixel data
            void* mapped;
            _vk.MapMemory(_device, _fontAtlasMemory, 0, memReq.Size, 0, &mapped);

            // Get image subresource layout for proper row pitch
            var subResource = new ImageSubresource { AspectMask = ImageAspectFlags.ColorBit, MipLevel = 0, ArrayLayer = 0 };
            SubresourceLayout layout;
            _vk.GetImageSubresourceLayout(_device, _fontAtlasImage, &subResource, &layout);

            for (int y = 0; y < h; y++)
            {
                    fixed (byte* pSrc = &pixels[y * w])
                        Unsafe.CopyBlock(
                            (byte*)mapped + (int)layout.Offset + y * (int)layout.RowPitch,
                            pSrc,
                            (uint)w);
            }
            _vk.UnmapMemory(_device, _fontAtlasMemory);

            // Create image view
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _fontAtlasImage,
                ViewType = ImageViewType.Type2D,
                Format = Format.R8Unorm,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0, LevelCount = 1,
                    BaseArrayLayer = 0, LayerCount = 1,
                },
            };
            fixed (ImageView* pView = &_fontAtlasView)
                CheckResult(_vk.CreateImageView(_device, &viewInfo, null, pView));
        }

        private void CreateSampler()
        {
            var info = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                AddressModeU = SamplerAddressMode.ClampToEdge,
                AddressModeV = SamplerAddressMode.ClampToEdge,
                AddressModeW = SamplerAddressMode.ClampToEdge,
                MipmapMode = SamplerMipmapMode.Nearest,
                MaxAnisotropy = 1.0f,
                MinLod = 0, MaxLod = 0,
            };
            fixed (Sampler* pSampler = &_fontSampler)
                CheckResult(_vk!.CreateSampler(_device, &info, null, pSampler));
        }

        private void CreateVertexBuffer()
        {
            uint bufSize = (uint)(MAX_VERTICES * VERTEX_SIZE);
            var bufInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = bufSize,
                Usage = BufferUsageFlags.VertexBufferBit,
                SharingMode = SharingMode.Exclusive,
            };

            fixed (Silk.NET.Vulkan.Buffer* pBuf = &_vertexBuffer)
                CheckResult(_vk!.CreateBuffer(_device, &bufInfo, null, pBuf));

            MemoryRequirements memReq;
            _vk!.GetBufferMemoryRequirements(_device, _vertexBuffer, &memReq);

            var allocInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memReq.Size,
                MemoryTypeIndex = FindMemoryType(memReq.MemoryTypeBits,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
            };

            fixed (DeviceMemory* pMem = &_vertexMemory)
                CheckResult(_vk.AllocateMemory(_device, &allocInfo, null, pMem));

            _vk.BindBufferMemory(_device, _vertexBuffer, _vertexMemory, 0);
            void* tmpMapped;
            _vk.MapMemory(_device, _vertexMemory, 0, bufSize, 0, &tmpMapped);
            _vertexMapped = tmpMapped;
        }

        private void CreateDescriptorPool()
        {
            var poolSize = new DescriptorPoolSize
            {
                Type = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
            };
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                MaxSets = 1,
                PoolSizeCount = 1,
                PPoolSizes = &poolSize,
            };
            fixed (DescriptorPool* pPool = &_descriptorPool)
                CheckResult(_vk!.CreateDescriptorPool(_device, &poolInfo, null, pPool));
        }

        private void CreateDescriptorSet()
        {
            if (_uiShader == null) return;

            var layout = _uiShader.DescriptorLayoutHandle;
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout,
            };
            fixed (DescriptorSet* pSet = &_fontDescriptorSet)
                CheckResult(_vk!.AllocateDescriptorSets(_device, &allocInfo, pSet));

            // Update descriptor with font atlas
            var imgInfo = new DescriptorImageInfo
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = _fontAtlasView,
                Sampler = _fontSampler,
            };
            var write = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _fontDescriptorSet,
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &imgInfo,
            };
            _vk!.UpdateDescriptorSets(_device, 1, &write, 0, null);
        }

        private void CreatePipeline()
        {
            if (_vk == null) return;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var vertPath = System.IO.Path.Combine(baseDir, "Rendering", "Vulkan", "shaders", "ui.vert.spv");
            var fragPath = System.IO.Path.Combine(baseDir, "Rendering", "Vulkan", "shaders", "ui.frag.spv");

            byte[] vertSpv, fragSpv;
            try
            {
                vertSpv = System.IO.File.ReadAllBytes(vertPath);
                fragSpv = System.IO.File.ReadAllBytes(fragPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VulkanHudRenderer] Failed to load UI shaders: {ex.Message}");
                return;
            }

            // Push constant uniforms: mat4(64) + vec4(16) + int(4) = 84 bytes
            // But int needs 4-byte alignment which it has at offset 80
            // Round up to multiple of 4: 84 bytes
            var uniforms = new Dictionary<string, (int, int)>
            {
                ["uProjection"] = (0, 64),
                ["uColor"] = (64, 16),
                ["uMode"] = (80, 4),
            };

            var attrs = new VertexInputAttributeDescription[]
            {
                new() { Binding = 0, Location = 0, Format = Format.R32G32Sfloat, Offset = 0 },        // aPos
                new() { Binding = 0, Location = 1, Format = Format.R32G32Sfloat, Offset = 8 },        // aTex
            };

            _uiShader = new VulkanShader(
                _vk, _device, _renderPass,
                vertSpv, fragSpv,
                84, uniforms,
                hasTexture: true,
                vertexStride: 16,
                vertexAttributes: attrs,
                extent: new Extent2D(1, 1)); // Dynamic viewport

            // Now that we have the pipeline, create the descriptor set
            CreateDescriptorSet();
        }

        // ═══════════════════════════════════════════════════════════════════
        // IHudRenderer implementation
        // ═══════════════════════════════════════════════════════════════════
        public void BeginFrame(int viewportWidth, int viewportHeight)
        {
            _vpWidth = viewportWidth;
            _vpHeight = viewportHeight;
            _vertexCount = 0;

            // Orthographic projection: NDC from (0,0) = top-left to (w,h) = bottom-right
            // Column-major mat4 for Vulkan clip space (Y down in NDC with viewport flip)
            float l = 0, r = viewportWidth, t = 0, b = viewportHeight;
            _projection = new float[16];
            _projection[0] = 2f / (r - l);
            _projection[5] = 2f / (b - t);    // Vulkan Y-down
            _projection[10] = -1f;
            _projection[12] = -(r + l) / (r - l);
            _projection[13] = -(t + b) / (b - t);
            _projection[15] = 1f;
        }

        public void DrawRect(float x, float y, float w, float h, float r, float g, float b, float a)
        {
            if (_vertexCount + 6 > MAX_VERTICES) return;

            float x2 = x + w, y2 = y + h;
            // 6 vertices, each 4 floats: pos.x, pos.y, tex.u, tex.v
            // For rects, tex coords are unused (uMode=1)
            EmitVertex(x, y, 0, 0);
            EmitVertex(x2, y, 0, 0);
            EmitVertex(x2, y2, 0, 0);
            EmitVertex(x, y, 0, 0);
            EmitVertex(x2, y2, 0, 0);
            EmitVertex(x, y2, 0, 0);

            // Flush this batch as a rect
            FlushBatch(mode: 1, r, g, b, a);
        }

        public void DrawText(string text, float x, float y, float r, float g, float b, float a)
        {
            if (string.IsNullOrEmpty(text)) return;

            float curX = x;

            foreach (char ch in text)
            {
                if (!_glyphMap.TryGetValue(ch, out var glyph))
                {
                    // Fallback to '?' if available, otherwise skip
                    if (!_glyphMap.TryGetValue('?', out glyph)) { curX += _cellW; continue; }
                }

                float u0 = (float)(glyph.col * _cellW) / _atlasW;
                float v0 = (float)(glyph.row * _cellH) / _atlasH;
                float u1 = u0 + (float)_cellW / _atlasW;
                float v1 = v0 + (float)_cellH / _atlasH;
                float x2 = curX + _cellW;
                float y2 = y + _cellH;

                if (_vertexCount + 6 > MAX_VERTICES) break;

                EmitVertex(curX, y, u0, v0);
                EmitVertex(x2, y, u1, v0);
                EmitVertex(x2, y2, u1, v1);
                EmitVertex(curX, y, u0, v0);
                EmitVertex(x2, y2, u1, v1);
                EmitVertex(curX, y2, u0, v1);

                curX += glyph.width + 1;
            }

            FlushBatch(mode: 0, r, g, b, a);
        }

        public float MeasureTextWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            float w = 0;
            foreach (char ch in text)
            {
                if (_glyphMap.TryGetValue(ch, out var glyph))
                    w += glyph.width + 1;
                else
                    w += _cellW;
            }
            return w;
        }

        public void EndFrame()
        {
            // All batches are flushed inline via FlushBatch.
        }

        // ═══════════════════════════════════════════════════════════════════
        // Internal helpers
        // ═══════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EmitVertex(float x, float y, float u, float v)
        {
            if (_vertexMapped == null) return;
            int offset = _vertexCount * 4; // 4 floats per vertex
            float* dst = (float*)_vertexMapped + offset;
            dst[0] = x;
            dst[1] = y;
            dst[2] = u;
            dst[3] = v;
            _vertexCount++;
        }

        private void FlushBatch(int mode, float r, float g, float b, float a)
        {
            if (_vertexCount == 0 || _uiShader == null || _activeCmd.Handle == 0 || _vk == null) return;

            _uiShader.SetMatrix4("uProjection", _projection);
            _uiShader.SetVec4("uColor", r, g, b, a);
            _uiShader.SetInt("uMode", mode);
            _uiShader.BindAndPush(_activeCmd);

            // Bind descriptor set (font atlas)
            fixed (DescriptorSet* pSet = &_fontDescriptorSet)
            {
                _vk.CmdBindDescriptorSets(_activeCmd, PipelineBindPoint.Graphics,
                    _uiShader.LayoutHandle, 0, 1, pSet, 0, null);
            }

            // Bind vertex buffer
            var vb = _vertexBuffer;
            ulong offset = 0;
            _vk.CmdBindVertexBuffers(_activeCmd, 0, 1, &vb, &offset);

            // Draw
            _vk.CmdDraw(_activeCmd, (uint)_vertexCount, 1, 0, 0);
            _vertexCount = 0;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Memory helpers
        // ═══════════════════════════════════════════════════════════════════
        private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
        {
            PhysicalDeviceMemoryProperties memProps;
            _vk!.GetPhysicalDeviceMemoryProperties(_physicalDevice, &memProps);

            for (uint i = 0; i < memProps.MemoryTypeCount; i++)
            {
                if ((typeFilter & (1u << (int)i)) != 0 &&
                    (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
                    return i;
            }

            throw new Exception($"[VulkanHudRenderer] No suitable memory type for flags {properties}");
        }

        private static void CheckResult(Result result)
        {
            if (result != Result.Success)
                throw new Exception($"Vulkan error: {result}");
        }

        public void Dispose()
        {
            if (_disposed || _vk == null) return;
            _disposed = true;

            _vk.DeviceWaitIdle(_device);

            _uiShader?.Dispose();

            if (_vertexMapped != null && _vertexMemory.Handle != 0)
            {
                _vk.UnmapMemory(_device, _vertexMemory);
                _vertexMapped = null;
            }
            if (_vertexBuffer.Handle != 0) _vk.DestroyBuffer(_device, _vertexBuffer, null);
            if (_vertexMemory.Handle != 0) _vk.FreeMemory(_device, _vertexMemory, null);

            if (_descriptorPool.Handle != 0) _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
            if (_fontSampler.Handle != 0) _vk.DestroySampler(_device, _fontSampler, null);
            if (_fontAtlasView.Handle != 0) _vk.DestroyImageView(_device, _fontAtlasView, null);
            if (_fontAtlasImage.Handle != 0) _vk.DestroyImage(_device, _fontAtlasImage, null);
            if (_fontAtlasMemory.Handle != 0) _vk.FreeMemory(_device, _fontAtlasMemory, null);
        }
    }
}
