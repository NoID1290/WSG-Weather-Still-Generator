using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.DXGI;
using WeatherImageGenerator.Rendering.Common;

namespace WeatherImageGenerator.Rendering.DirectX
{
    /// <summary>
    /// DirectX 11 shader wrapper using Silk.NET.
    /// Compiles HLSL at runtime via D3DCompiler and manages vertex/pixel shader + constant buffer state.
    /// Constant buffers are updated per-uniform-set and uploaded before draw calls.
    /// </summary>
    public unsafe class DXShader : IShader
    {
        private bool _disposed;

        // Silk.NET D3D11 device/context (borrowed – caller owns lifetime)
        private ID3D11Device* _device;
        private ID3D11DeviceContext* _context;

        // Compiled shaders
        private ID3D11VertexShader* _vertexShader;
        private ID3D11PixelShader* _pixelShader;
        private ID3D11InputLayout* _inputLayout;

        // Constant buffers: slot 0 for VS, slot 0 for PS
        private ID3D11Buffer* _vsCBuffer;
        private ID3D11Buffer* _psCBuffer;

        // CPU-side constant buffer data (sized to match shader cbuffer)
        private byte[] _vsData;
        private byte[] _psData;
        private bool _vsDirty = true;
        private bool _psDirty = true;

        // Compiled bytecode for input layout creation
        private ID3D10Blob* _vsBytecode;

        // Uniform name → (isVS, byteOffset, sizeInBytes) for constant buffer updates
        private readonly Dictionary<string, (bool isVS, int offset, int size)> _uniformMap = new();

        // Keep D3DCompiler API alive for the lifetime of the process.
        // Disposing it unloads the native DLL, which invalidates the COM vtable
        // of any ID3D10Blob objects still in use (causing AccessViolationException).
        private static readonly D3DCompiler s_compiler = D3DCompiler.GetApi();

        /// <summary>
        /// Create a DXShader from separate VS and PS HLSL source strings.
        /// </summary>
        public DXShader(
            ID3D11Device* device,
            ID3D11DeviceContext* context,
            string vsSource, string psSource,
            string vsEntry = "main", string psEntry = "main",
            InputElementDesc[]? inputElements = null,
            Dictionary<string, (bool isVS, int offset, int size)>? uniformLayout = null)
        {
            _device = device;
            _context = context;

            // Compile vertex shader
            _vsBytecode = CompileShader(vsSource, vsEntry, "vs_5_0");
            ID3D11VertexShader* vs = null;
            SilkMarshal.ThrowHResult(
                _device->CreateVertexShader(
                    _vsBytecode->GetBufferPointer(),
                    _vsBytecode->GetBufferSize(),
                    (ID3D11ClassLinkage*)null, ref vs));
            _vertexShader = vs;

            // Compile pixel shader
            ID3D10Blob* psBytecode = CompileShader(psSource, psEntry, "ps_5_0");
            try
            {
                ID3D11PixelShader* ps = null;
                SilkMarshal.ThrowHResult(
                    _device->CreatePixelShader(
                        psBytecode->GetBufferPointer(),
                        psBytecode->GetBufferSize(),
                        (ID3D11ClassLinkage*)null, ref ps));
                _pixelShader = ps;
            }
            finally
            {
                psBytecode->Release();
            }

            // Create input layout
            if (inputElements != null && inputElements.Length > 0)
            {
                fixed (InputElementDesc* pElem = inputElements)
                {
                    ID3D11InputLayout* il = null;
                    SilkMarshal.ThrowHResult(
                        _device->CreateInputLayout(
                            pElem, (uint)inputElements.Length,
                            _vsBytecode->GetBufferPointer(),
                            _vsBytecode->GetBufferSize(),
                            ref il));
                    _inputLayout = il;
                }
            }

            // Store uniform layout
            if (uniformLayout != null)
            {
                foreach (var kv in uniformLayout)
                    _uniformMap[kv.Key] = kv.Value;
            }

            // Create constant buffers (256 bytes each, enough for most cbuffers)
            _vsData = new byte[256];
            _psData = new byte[256];
            _vsCBuffer = CreateConstantBuffer(256);
            _psCBuffer = CreateConstantBuffer(256);
        }

        private ID3D11Buffer* CreateConstantBuffer(int sizeBytes)
        {
            // Constant buffer size must be multiple of 16
            int aligned = (sizeBytes + 15) & ~15;
            var desc = new BufferDesc
            {
                ByteWidth = (uint)aligned,
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.ConstantBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
                MiscFlags = 0,
                StructureByteStride = 0
            };
            ID3D11Buffer* buf = null;
            SilkMarshal.ThrowHResult(_device->CreateBuffer(ref desc, (SubresourceData*)null, ref buf));
            return buf;
        }

        private static ID3D10Blob* CompileShader(string source, string entryPoint, string target)
        {
            ID3D10Blob* code = null;
            ID3D10Blob* errors = null;

            var sourceBytes = System.Text.Encoding.UTF8.GetBytes(source + "\0");
            var entryBytes = System.Text.Encoding.UTF8.GetBytes(entryPoint + "\0");
            var targetBytes = System.Text.Encoding.UTF8.GetBytes(target + "\0");

            int hr;
            fixed (byte* pSource = sourceBytes)
            fixed (byte* pEntry = entryBytes)
            fixed (byte* pTarget = targetBytes)
            {
                hr = s_compiler.Compile(
                    pSource, (nuint)sourceBytes.Length,
                    (byte*)null, // source name
                    (D3DShaderMacro*)null, // defines
                    (ID3DInclude*)null, // includes
                    pEntry, pTarget,
                    0, 0, // flags
                    &code, &errors);
            }

            if (hr < 0)
            {
                string errorMsg = "Unknown error";
                if (errors != null)
                {
                    errorMsg = SilkMarshal.PtrToString(
                        (nint)errors->GetBufferPointer(), NativeStringEncoding.UTF8) ?? errorMsg;
                }
                if (errors != null) errors->Release();
                if (code != null) { code->Release(); code = null; }
                throw new Exception($"HLSL compile failed ({target}:{entryPoint}): {errorMsg}");
            }

            if (errors != null) errors->Release();
            return code;
        }

        /// <summary>Activate this shader pipeline on the device context.</summary>
        public void Use()
        {
            _context->VSSetShader(_vertexShader, null, 0);
            _context->PSSetShader(_pixelShader, null, 0);

            if (_inputLayout != null)
                _context->IASetInputLayout(_inputLayout);

            // Upload dirty constant buffers
            FlushConstants();
        }

        /// <summary>Upload constant buffer data if dirty.</summary>
        public void FlushConstants()
        {
            if (_vsDirty)
            {
                UpdateBuffer(_vsCBuffer, _vsData);
                _vsDirty = false;
            }
            if (_psDirty)
            {
                UpdateBuffer(_psCBuffer, _psData);
                _psDirty = false;
            }

            // Bind constant buffers
            var vsBuf = _vsCBuffer;
            _context->VSSetConstantBuffers(0, 1, &vsBuf);
            var psBuf = _psCBuffer;
            _context->PSSetConstantBuffers(0, 1, &psBuf);
        }

        private void UpdateBuffer(ID3D11Buffer* buffer, byte[] data)
        {
            MappedSubresource mapped;
            SilkMarshal.ThrowHResult(
                _context->Map((ID3D11Resource*)buffer, 0, Map.WriteDiscard, 0, &mapped));
            fixed (byte* pData = data)
            {
                Unsafe.CopyBlock(mapped.PData, pData, (uint)data.Length);
            }
            _context->Unmap((ID3D11Resource*)buffer, 0);
        }

        #region IShader uniform setters

        public int GetAttribLocation(string name) => -1; // DX uses input layouts

        public void SetInt(string name, int value)
        {
            if (_uniformMap.TryGetValue(name, out var info))
                WriteUniform(info, BitConverter.GetBytes(value));
        }

        public void SetBool(string name, bool value)
        {
            // HLSL bool in cbuffers is 4 bytes (uint)
            if (_uniformMap.TryGetValue(name, out var info))
                WriteUniform(info, BitConverter.GetBytes(value ? 1u : 0u));
        }

        public void SetFloat(string name, float value)
        {
            if (_uniformMap.TryGetValue(name, out var info))
                WriteUniform(info, BitConverter.GetBytes(value));
        }

        public void SetVec2(string name, float x, float y)
        {
            if (_uniformMap.TryGetValue(name, out var info))
            {
                var bytes = new byte[8];
                BitConverter.TryWriteBytes(bytes.AsSpan(0), x);
                BitConverter.TryWriteBytes(bytes.AsSpan(4), y);
                WriteUniform(info, bytes);
            }
        }

        public void SetVec3(string name, float x, float y, float z)
        {
            if (_uniformMap.TryGetValue(name, out var info))
            {
                var bytes = new byte[12];
                BitConverter.TryWriteBytes(bytes.AsSpan(0), x);
                BitConverter.TryWriteBytes(bytes.AsSpan(4), y);
                BitConverter.TryWriteBytes(bytes.AsSpan(8), z);
                WriteUniform(info, bytes);
            }
        }

        public void SetVec4(string name, float x, float y, float z, float w)
        {
            if (_uniformMap.TryGetValue(name, out var info))
            {
                var bytes = new byte[16];
                BitConverter.TryWriteBytes(bytes.AsSpan(0), x);
                BitConverter.TryWriteBytes(bytes.AsSpan(4), y);
                BitConverter.TryWriteBytes(bytes.AsSpan(8), z);
                BitConverter.TryWriteBytes(bytes.AsSpan(12), w);
                WriteUniform(info, bytes);
            }
        }

        public void SetMatrix3(string name, float[] mat3)
        {
            if (_uniformMap.TryGetValue(name, out var info))
            {
                // HLSL float3x3 is stored as 3 float4 rows (with padding) = 48 bytes
                var bytes = new byte[48];
                // Row 0
                BitConverter.TryWriteBytes(bytes.AsSpan(0), mat3[0]);
                BitConverter.TryWriteBytes(bytes.AsSpan(4), mat3[1]);
                BitConverter.TryWriteBytes(bytes.AsSpan(8), mat3[2]);
                // Row 1  (16-byte aligned)
                BitConverter.TryWriteBytes(bytes.AsSpan(16), mat3[3]);
                BitConverter.TryWriteBytes(bytes.AsSpan(20), mat3[4]);
                BitConverter.TryWriteBytes(bytes.AsSpan(24), mat3[5]);
                // Row 2
                BitConverter.TryWriteBytes(bytes.AsSpan(32), mat3[6]);
                BitConverter.TryWriteBytes(bytes.AsSpan(36), mat3[7]);
                BitConverter.TryWriteBytes(bytes.AsSpan(40), mat3[8]);
                WriteUniform(info, bytes);
            }
        }

        public void SetMatrix4(string name, float[] mat4)
        {
            if (_uniformMap.TryGetValue(name, out var info))
            {
                var bytes = new byte[64];
                System.Buffer.BlockCopy(mat4, 0, bytes, 0, 64);
                WriteUniform(info, bytes);
            }
        }

        private void WriteUniform((bool isVS, int offset, int size) info, byte[] data)
        {
            int len = Math.Min(data.Length, info.size);
            var target = info.isVS ? _vsData : _psData;
            System.Buffer.BlockCopy(data, 0, target, info.offset, len);
            if (info.isVS) _vsDirty = true; else _psDirty = true;
        }

        #endregion

        /// <summary>Get the compiled VS bytecode pointer for input layout creation.</summary>
        public (nint ptr, nuint size) GetVertexBytecodeInfo()
            => ((nint)_vsBytecode->GetBufferPointer(), _vsBytecode->GetBufferSize());

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_vertexShader != null) { _vertexShader->Release(); _vertexShader = null; }
                if (_pixelShader != null) { _pixelShader->Release(); _pixelShader = null; }
                if (_inputLayout != null) { _inputLayout->Release(); _inputLayout = null; }
                if (_vsCBuffer != null) { _vsCBuffer->Release(); _vsCBuffer = null; }
                if (_psCBuffer != null) { _psCBuffer->Release(); _psCBuffer = null; }
                if (_vsBytecode != null) { _vsBytecode->Release(); _vsBytecode = null; }
            }
        }
    }
}
