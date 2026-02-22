using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using WeatherImageGenerator.Rendering.Common;

namespace WeatherImageGenerator.Rendering.Vulkan
{
    /// <summary>
    /// Vulkan shader pipeline wrapper using Silk.NET.
    /// Manages SPIR-V shader modules, graphics pipeline, push constants, and
    /// descriptor set layout for texture bindings.
    ///
    /// Push constant layout is defined per-pipeline and updated via SetFloat/SetMatrix3 etc.
    /// Textures are bound via a single descriptor set with a combined image sampler at binding 0.
    /// </summary>
    public unsafe class VulkanShader : IShader
    {
        private readonly Vk _vk;
        private readonly Device _device;
        private ShaderModule _vertModule;
        private ShaderModule _fragModule;
        private PipelineLayout _pipelineLayout;
        private Pipeline _pipeline;
        private DescriptorSetLayout _descriptorSetLayout;

        // Push constant data — CPU shadow buffer
        private readonly byte[] _pushData;
        private readonly uint _pushSize;
        private readonly Dictionary<string, (int offset, int size)> _uniforms;

        private bool _disposed;

        /// <summary>Vulkan pipeline handle for binding.</summary>
        public Pipeline PipelineHandle => _pipeline;

        /// <summary>Pipeline layout for push constants and descriptor binding.</summary>
        public PipelineLayout LayoutHandle => _pipelineLayout;

        /// <summary>Descriptor set layout describing texture bindings.</summary>
        public DescriptorSetLayout DescriptorLayoutHandle => _descriptorSetLayout;

        /// <summary>
        /// Creates a Vulkan graphics pipeline from precompiled SPIR-V bytecode.
        /// </summary>
        /// <param name="vk">Vulkan API instance.</param>
        /// <param name="device">Logical device.</param>
        /// <param name="renderPass">Render pass this pipeline will be used with.</param>
        /// <param name="vertSpirV">Vertex shader SPIR-V bytecode.</param>
        /// <param name="fragSpirV">Fragment shader SPIR-V bytecode.</param>
        /// <param name="pushConstantSize">Total size of push constant block in bytes.</param>
        /// <param name="uniforms">Map of uniform name → (byte offset, byte size) within push constants.</param>
        /// <param name="hasTexture">Whether this pipeline uses a texture sampler at set 0 binding 0.</param>
        /// <param name="vertexStride">Vertex stride in bytes.</param>
        /// <param name="vertexAttributes">Vertex attribute descriptions.</param>
        /// <param name="extent">Swapchain extent for viewport/scissor.</param>
        public VulkanShader(
            Vk vk, Device device, RenderPass renderPass,
            byte[] vertSpirV, byte[] fragSpirV,
            uint pushConstantSize,
            Dictionary<string, (int offset, int size)> uniforms,
            bool hasTexture,
            uint vertexStride,
            VertexInputAttributeDescription[] vertexAttributes,
            Extent2D extent)
        {
            _vk = vk;
            _device = device;
            _pushSize = pushConstantSize;
            _pushData = new byte[pushConstantSize];
            _uniforms = uniforms;

            // Create shader modules
            _vertModule = CreateShaderModule(vertSpirV);
            _fragModule = CreateShaderModule(fragSpirV);

            // Descriptor set layout: optional texture sampler at binding 0
            if (hasTexture)
            {
                var binding = new DescriptorSetLayoutBinding
                {
                    Binding = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    StageFlags = ShaderStageFlags.FragmentBit,
                    PImmutableSamplers = null,
                };
                var layoutInfo = new DescriptorSetLayoutCreateInfo
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = 1,
                    PBindings = &binding,
                };
                fixed (DescriptorSetLayout* pLayout = &_descriptorSetLayout)
                {
                    CheckResult(_vk.CreateDescriptorSetLayout(_device, &layoutInfo, null, pLayout));
                }
            }
            else
            {
                var layoutInfo = new DescriptorSetLayoutCreateInfo
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = 0,
                    PBindings = null,
                };
                fixed (DescriptorSetLayout* pLayout = &_descriptorSetLayout)
                {
                    CheckResult(_vk.CreateDescriptorSetLayout(_device, &layoutInfo, null, pLayout));
                }
            }

            // Pipeline layout: push constants + descriptor set
            var pushRange = new PushConstantRange
            {
                StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                Offset = 0,
                Size = pushConstantSize,
            };

            fixed (DescriptorSetLayout* pDescLayout = &_descriptorSetLayout)
            {
                var pipeLayoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PSetLayouts = pDescLayout,
                    PushConstantRangeCount = 1,
                    PPushConstantRanges = &pushRange,
                };
                fixed (PipelineLayout* pPipeLayout = &_pipelineLayout)
                {
                    CheckResult(_vk.CreatePipelineLayout(_device, &pipeLayoutInfo, null, pPipeLayout));
                }
            }

            // Create graphics pipeline
            CreateGraphicsPipeline(renderPass, vertexStride, vertexAttributes, extent);
        }

        private ShaderModule CreateShaderModule(byte[] spirV)
        {
            fixed (byte* pCode = spirV)
            {
                var info = new ShaderModuleCreateInfo
                {
                    SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)spirV.Length,
                    PCode = (uint*)pCode,
                };
                ShaderModule module;
                CheckResult(_vk.CreateShaderModule(_device, &info, null, &module));
                return module;
            }
        }

        private void CreateGraphicsPipeline(
            RenderPass renderPass,
            uint vertexStride,
            VertexInputAttributeDescription[] attributes,
            Extent2D extent)
        {
            // Shader stage info
            var entryName = (byte*)SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);
            var vertStageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _vertModule,
                PName = entryName,
            };
            var fragStageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _fragModule,
                PName = entryName,
            };
            var stages = stackalloc PipelineShaderStageCreateInfo[] { vertStageInfo, fragStageInfo };

            // Vertex input
            var bindingDesc = new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = vertexStride,
                InputRate = VertexInputRate.Vertex,
            };

            fixed (VertexInputAttributeDescription* pAttrs = attributes)
            {
                var vertexInputInfo = new PipelineVertexInputStateCreateInfo
                {
                    SType = StructureType.PipelineVertexInputStateCreateInfo,
                    VertexBindingDescriptionCount = 1,
                    PVertexBindingDescriptions = &bindingDesc,
                    VertexAttributeDescriptionCount = (uint)attributes.Length,
                    PVertexAttributeDescriptions = pAttrs,
                };

                var inputAssembly = new PipelineInputAssemblyStateCreateInfo
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = PrimitiveTopology.TriangleList,
                    PrimitiveRestartEnable = false,
                };

                // Dynamic viewport and scissor
                var dynStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
                var dynamicState = new PipelineDynamicStateCreateInfo
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    DynamicStateCount = 2,
                    PDynamicStates = dynStates,
                };

                var viewportState = new PipelineViewportStateCreateInfo
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1,
                    ScissorCount = 1,
                    // Viewport/scissor are dynamic, pointers can be null
                };

                var rasterizer = new PipelineRasterizationStateCreateInfo
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    DepthClampEnable = false,
                    RasterizerDiscardEnable = false,
                    PolygonMode = PolygonMode.Fill,
                    LineWidth = 1.0f,
                    CullMode = CullModeFlags.None,
                    FrontFace = FrontFace.CounterClockwise,
                    DepthBiasEnable = false,
                };

                var multisampling = new PipelineMultisampleStateCreateInfo
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    SampleShadingEnable = false,
                    RasterizationSamples = SampleCountFlags.Count1Bit,
                };

                // Alpha blending: srcAlpha, oneMinusSrcAlpha
                var colorBlendAttach = new PipelineColorBlendAttachmentState
                {
                    ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                     ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                    BlendEnable = true,
                    SrcColorBlendFactor = BlendFactor.SrcAlpha,
                    DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                    ColorBlendOp = BlendOp.Add,
                    SrcAlphaBlendFactor = BlendFactor.One,
                    DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                    AlphaBlendOp = BlendOp.Add,
                };

                var colorBlending = new PipelineColorBlendStateCreateInfo
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    LogicOpEnable = false,
                    AttachmentCount = 1,
                    PAttachments = &colorBlendAttach,
                };

                var pipelineInfo = new GraphicsPipelineCreateInfo
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = stages,
                    PVertexInputState = &vertexInputInfo,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterizer,
                    PMultisampleState = &multisampling,
                    PColorBlendState = &colorBlending,
                    PDynamicState = &dynamicState,
                    Layout = _pipelineLayout,
                    RenderPass = renderPass,
                    Subpass = 0,
                };

                fixed (Pipeline* pPipeline = &_pipeline)
                {
                    CheckResult(_vk.CreateGraphicsPipelines(
                        _device, default, 1, &pipelineInfo, null, pPipeline));
                }
            }

            SilkMarshal.FreeString((nint)entryName);
        }

        // ═══════════════════════════════════════════════════════════════════
        // IShader interface — push constant updates
        // ═══════════════════════════════════════════════════════════════════
        public void Use()
        {
            // In Vulkan, pipeline binding is done via command buffer, not here.
            // The VulkanMapRenderer calls BindAndPush() directly.
        }

        /// <summary>
        /// Binds this pipeline and pushes the current push constant data to the command buffer.
        /// </summary>
        public void BindAndPush(CommandBuffer cmd)
        {
            _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);
            fixed (byte* pData = _pushData)
            {
                _vk.CmdPushConstants(cmd, _pipelineLayout,
                    ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                    0, _pushSize, pData);
            }
        }

        public int GetAttribLocation(string name) => -1; // N/A in Vulkan

        public void SetInt(string name, int value)
        {
            if (_uniforms.TryGetValue(name, out var info))
                SetPushBytes(info.offset, BitConverter.GetBytes(value), info.size);
        }

        public void SetFloat(string name, float value)
        {
            if (_uniforms.TryGetValue(name, out var info))
                SetPushBytes(info.offset, BitConverter.GetBytes(value), info.size);
        }

        public void SetBool(string name, bool value) =>
            SetFloat(name, value ? 1.0f : 0.0f);

        public void SetVec2(string name, float x, float y)
        {
            if (_uniforms.TryGetValue(name, out var info) && info.size >= 8)
            {
                Unsafe.WriteUnaligned(ref _pushData[info.offset], x);
                Unsafe.WriteUnaligned(ref _pushData[info.offset + 4], y);
            }
        }

        public void SetVec3(string name, float x, float y, float z)
        {
            if (_uniforms.TryGetValue(name, out var info) && info.size >= 12)
            {
                Unsafe.WriteUnaligned(ref _pushData[info.offset], x);
                Unsafe.WriteUnaligned(ref _pushData[info.offset + 4], y);
                Unsafe.WriteUnaligned(ref _pushData[info.offset + 8], z);
            }
        }

        public void SetVec4(string name, float x, float y, float z, float w)
        {
            if (_uniforms.TryGetValue(name, out var info) && info.size >= 16)
            {
                Unsafe.WriteUnaligned(ref _pushData[info.offset], x);
                Unsafe.WriteUnaligned(ref _pushData[info.offset + 4], y);
                Unsafe.WriteUnaligned(ref _pushData[info.offset + 8], z);
                Unsafe.WriteUnaligned(ref _pushData[info.offset + 12], w);
            }
        }

        /// <summary>
        /// Sets a 3x3 matrix as three vec4 rows (xyz + pad) at the named offset.
        /// Layout: 3 × vec4 = 48 bytes. mat3[0..2] → row0.xyz, row1.xyz, row2.xyz with w=0 pad.
        /// </summary>
        public void SetMatrix3(string name, float[] mat3)
        {
            if (mat3.Length < 9) return;
            if (!_uniforms.TryGetValue(name, out var info) || info.size < 48) return;

            int off = info.offset;
            // Row 0 → vec4 (m00, m01, m02, 0)
            Unsafe.WriteUnaligned(ref _pushData[off +  0], mat3[0]);
            Unsafe.WriteUnaligned(ref _pushData[off +  4], mat3[1]);
            Unsafe.WriteUnaligned(ref _pushData[off +  8], mat3[2]);
            Unsafe.WriteUnaligned(ref _pushData[off + 12], 0f);
            // Row 1 → vec4 (m10, m11, m12, 0)
            Unsafe.WriteUnaligned(ref _pushData[off + 16], mat3[3]);
            Unsafe.WriteUnaligned(ref _pushData[off + 20], mat3[4]);
            Unsafe.WriteUnaligned(ref _pushData[off + 24], mat3[5]);
            Unsafe.WriteUnaligned(ref _pushData[off + 28], 0f);
            // Row 2 → vec4 (m20, m21, m22, 0)
            Unsafe.WriteUnaligned(ref _pushData[off + 32], mat3[6]);
            Unsafe.WriteUnaligned(ref _pushData[off + 36], mat3[7]);
            Unsafe.WriteUnaligned(ref _pushData[off + 40], mat3[8]);
            Unsafe.WriteUnaligned(ref _pushData[off + 44], 0f);
        }

        public void SetMatrix4(string name, float[] mat4)
        {
            if (mat4.Length < 16) return;
            if (!_uniforms.TryGetValue(name, out var info) || info.size < 64) return;

            fixed (float* src = mat4)
            {
                System.Buffer.BlockCopy(mat4, 0, _pushData, info.offset, 64);
            }
        }

        private void SetPushBytes(int offset, byte[] bytes, int expectedSize)
        {
            int count = Math.Min(bytes.Length, expectedSize);
            System.Buffer.BlockCopy(bytes, 0, _pushData, offset, count);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Helpers
        // ═══════════════════════════════════════════════════════════════════
        private static void CheckResult(Result result)
        {
            if (result != Result.Success)
                throw new Exception($"Vulkan error: {result}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_pipeline.Handle != 0)
                _vk.DestroyPipeline(_device, _pipeline, null);
            if (_pipelineLayout.Handle != 0)
                _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
            if (_descriptorSetLayout.Handle != 0)
                _vk.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);
            if (_vertModule.Handle != 0)
                _vk.DestroyShaderModule(_device, _vertModule, null);
            if (_fragModule.Handle != 0)
                _vk.DestroyShaderModule(_device, _fragModule, null);
        }
    }
}
