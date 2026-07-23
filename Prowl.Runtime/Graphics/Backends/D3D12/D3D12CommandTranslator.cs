// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Vortice;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

using Prowl.Runtime.RHI.Shaders;

using CommandBuffer = Prowl.Runtime.CommandBuffer;

namespace Prowl.Runtime.Backends.D3D12;

/// <summary>
/// Translates engine <see cref="CommandBuffer"/> recordings into D3D12 commands.
/// Resource create/dispose and clears are implemented; draws no-op until PSOs exist.
/// </summary>
internal sealed class D3D12CommandTranslator
{
    private readonly D3D12GraphicsDevice _device;
    private readonly HashSet<CommandOpcode> _warnedOpcodes = new();
    private bool _warnedDrawNoPso;

    private GraphicsFrameBuffer? _pendingRenderTarget;
    private ShaderVariant? _currentShader;

    public D3D12CommandTranslator(D3D12GraphicsDevice device)
    {
        _device = device;
    }

    public void Translate(CommandBuffer commandBuffer, ID3D12GraphicsCommandList list)
    {
        var stream = commandBuffer._stream.AsSpan(0, commandBuffer._streamPos);
        var objects = commandBuffer._objects;
        var store = commandBuffer._store;
        int pos = 0;

        while (pos < stream.Length)
        {
            CommandOpcode op = ReadOpcode(stream, ref pos);
            switch (op)
            {
                case CommandOpcode.SetRenderTarget:
                {
                    _pendingRenderTarget = (GraphicsFrameBuffer?)objects[ReadU16(stream, ref pos)];
                    break;
                }
                case CommandOpcode.SetRenderTargets:
                {
                    _pendingRenderTarget = (GraphicsFrameBuffer?)objects[ReadU16(stream, ref pos)];
                    _ = ReadU16(stream, ref pos); // read FB unused in MVP
                    break;
                }
                case CommandOpcode.SetViewport:
                {
                    int x = ReadI32(stream, ref pos);
                    int y = ReadI32(stream, ref pos);
                    uint w = ReadU32(stream, ref pos);
                    uint h = ReadU32(stream, ref pos);
                    list.RSSetViewport(new Viewport(x, y, w, h, 0f, 1f));
                    break;
                }
                case CommandOpcode.SetScissor:
                {
                    int x = ReadI32(stream, ref pos);
                    int y = ReadI32(stream, ref pos);
                    uint w = ReadU32(stream, ref pos);
                    uint h = ReadU32(stream, ref pos);
                    list.RSSetScissorRect(new RectI(x, y, (int)w, (int)h));
                    break;
                }
                case CommandOpcode.DisableScissor:
                {
                    // D3D12 always scissors; widen to a large rect.
                    list.RSSetScissorRect(new RectI(0, 0, 16384, 16384));
                    break;
                }
                case CommandOpcode.ClearRenderTarget:
                {
                    ClearFlags flags = (ClearFlags)ReadU8(stream, ref pos);
                    float r = ReadF32(stream, ref pos);
                    float g = ReadF32(stream, ref pos);
                    float b = ReadF32(stream, ref pos);
                    float a = ReadF32(stream, ref pos);
                    float depth = ReadF32(stream, ref pos);
                    int stencil = ReadI32(stream, ref pos);
                    DoClear(list, flags, r, g, b, a, depth, stencil);
                    break;
                }
                case CommandOpcode.SetShader:
                {
                    _currentShader = objects[ReadU16(stream, ref pos)] as ShaderVariant;
                    if (_currentShader?.Bytecode?.Format != ShaderBytecodeFormat.Dxil)
                        WarnOnce(CommandOpcode.SetShader, "D3D12 shader bind skipped: expected a DXIL ShaderVariant.");
                    break;
                }
                case CommandOpcode.DrawIndexed:
                {
                    _ = objects[ReadU16(stream, ref pos)];
                    _ = ReadU8(stream, ref pos);
                    _ = ReadU32(stream, ref pos);
                    _ = ReadU32(stream, ref pos);
                    _ = ReadI32(stream, ref pos);
                    _ = ReadU8(stream, ref pos);
                    WarnDrawNoPso();
                    break;
                }
                case CommandOpcode.DrawIndexedInstanced:
                {
                    _ = objects[ReadU16(stream, ref pos)];
                    _ = ReadU8(stream, ref pos);
                    _ = ReadU32(stream, ref pos);
                    _ = ReadU32(stream, ref pos);
                    _ = ReadU32(stream, ref pos);
                    _ = ReadI32(stream, ref pos);
                    _ = ReadU8(stream, ref pos);
                    WarnDrawNoPso();
                    break;
                }
                case CommandOpcode.DrawArrays:
                {
                    _ = objects[ReadU16(stream, ref pos)];
                    _ = ReadU8(stream, ref pos);
                    _ = ReadI32(stream, ref pos);
                    _ = ReadU32(stream, ref pos);
                    WarnDrawNoPso();
                    break;
                }
                case CommandOpcode.CreateBuffer:
                {
                    var buf = (GraphicsBuffer)objects[ReadU16(stream, ref pos)]!;
                    bool dynamic = ReadU8(stream, ref pos) != 0;
                    ReadOnlySpan<byte> data = ReadBlob<byte>(stream, ref pos, store);
                    CreateBuffer(buf, dynamic, data);
                    break;
                }
                case CommandOpcode.DisposeBuffer:
                {
                    var buf = (GraphicsBuffer)objects[ReadU16(stream, ref pos)]!;
                    DisposeBuffer(buf);
                    break;
                }
                case CommandOpcode.UpdateBuffer:
                {
                    var buf = (GraphicsBuffer?)objects[ReadU16(stream, ref pos)];
                    uint dstOffset = ReadU32(stream, ref pos);
                    ReadOnlySpan<byte> blob = ReadBlob<byte>(stream, ref pos, store);
                    if (buf != null && buf.Handle != 0 &&
                        _device.Buffers.TryGetValue(buf.Handle, out D3D12BufferResource? res) &&
                        res.Resource != null)
                    {
                        _device.UploadToBuffer(res.Resource, blob, dstOffset);
                    }
                    break;
                }
                case CommandOpcode.CreateTexture:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    uint handle = _device.AllocateHandle();
                    tex.Handle = handle;
                    _device.Textures[handle] = new D3D12TextureResource
                    {
                        EngineFormat = tex.ImageFormat,
                        Format = D3D12Formats.ToDxgiFormat(tex.ImageFormat),
                        Type = tex.Type,
                    };
                    break;
                }
                case CommandOpcode.DisposeTexture:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    if (tex.Handle != 0 && _device.Textures.Remove(tex.Handle, out D3D12TextureResource? res))
                    {
                        res.Resource?.Dispose();
                        tex.Handle = 0;
                    }
                    break;
                }
                case CommandOpcode.AllocateTexture2D:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    int mip = ReadI32(stream, ref pos);
                    uint w = ReadU32(stream, ref pos);
                    uint h = ReadU32(stream, ref pos);
                    _ = ReadI32(stream, ref pos); // border
                    ReadOnlySpan<byte> data = ReadBlob<byte>(stream, ref pos, store);
                    AllocateTexture2D(tex, mip, w, h, data);
                    break;
                }
                case CommandOpcode.BeginSample:
                {
                    _ = objects[ReadU16(stream, ref pos)];
                    break;
                }
                case CommandOpcode.EndSample:
                    break;

                // Remaining opcodes: consume payloads so the stream stays aligned.
                default:
                    SkipOpcode(op, stream, ref pos, objects, store);
                    break;
            }
        }
    }

    private void DoClear(
        ID3D12GraphicsCommandList list,
        ClearFlags flags,
        float r, float g, float b, float a,
        float depth,
        int stencil)
    {
        _ = _pendingRenderTarget; // custom FB clears land in a later pass
        if ((flags & ClearFlags.Color) != 0 && _device.HasSwapchain)
        {
            var color = new Color4(r, g, b, a);
            list.ClearRenderTargetView(_device.CurrentRtv, color);
        }

        // Swapchain MVP has no default DSV; depth/stencil clears are ignored until FBO path lands.
        _ = depth;
        _ = stencil;
    }

    private void CreateBuffer(GraphicsBuffer buf, bool dynamic, ReadOnlySpan<byte> data)
    {
        uint handle = _device.AllocateHandle();
        ulong size = data.Length > 0 ? (ulong)data.Length : Math.Max(1u, buf.SizeInBytes);
        HeapType heap = dynamic ? HeapType.Upload : HeapType.Default;
        ResourceStates state = dynamic ? ResourceStates.GenericRead : ResourceStates.Common;
        ID3D12Resource resource = _device.CreateCommittedBuffer(size, heap, state);

        if (data.Length > 0)
        {
            if (heap == HeapType.Upload)
            {
                unsafe
                {
                    byte* mapped = resource.Map<byte>(0);
                    try
                    {
                        fixed (byte* src = data)
                            System.Buffer.MemoryCopy(src, mapped, data.Length, data.Length);
                    }
                    finally
                    {
                        resource.Unmap(0);
                    }
                }
            }
            else
            {
                _device.UploadToBuffer(resource, data, 0);
            }
        }

        buf.Handle = handle;
        buf.SizeInBytes = (uint)size;
        _device.Buffers[handle] = new D3D12BufferResource
        {
            Resource = resource,
            Size = size,
            Type = buf.OriginalType,
            Dynamic = dynamic,
            HeapType = heap,
        };
    }

    private void DisposeBuffer(GraphicsBuffer buf)
    {
        if (buf.Handle == 0)
            return;
        if (_device.Buffers.Remove(buf.Handle, out D3D12BufferResource? res))
            res.Resource?.Dispose();
        buf.Handle = 0;
    }

    private void AllocateTexture2D(GraphicsTexture tex, int mip, uint width, uint height, ReadOnlySpan<byte> data)
    {
        if (tex.Handle == 0 || !_device.Textures.TryGetValue(tex.Handle, out D3D12TextureResource? res))
            return;

        // Only allocate storage at mip 0 for MVP; higher mips ignored.
        if (mip != 0)
            return;

        res.Resource?.Dispose();
        Format format = D3D12Formats.ToDxgiFormat(tex.ImageFormat);
        ResourceFlags flags = D3D12Formats.IsDepth(tex.ImageFormat)
            ? ResourceFlags.AllowDepthStencil
            : ResourceFlags.AllowRenderTarget;
        ResourceStates initial = D3D12Formats.IsDepth(tex.ImageFormat)
            ? ResourceStates.DepthWrite
            : ResourceStates.Common;

        res.Resource = _device.CreateCommittedTexture2D(width, height, format, flags, initial);
        res.Width = width;
        res.Height = height;
        res.Format = format;
        res.State = initial;

        if (data.Length > 0 && !D3D12Formats.IsDepth(tex.ImageFormat))
        {
            // Staging upload via intermediate buffer + CopyTextureRegion is Stage C follow-up.
            WarnOnce(CommandOpcode.AllocateTexture2D, "D3D12 AllocateTexture2D initial data upload is not implemented yet.");
        }
    }

    private void WarnDrawNoPso()
    {
        if (_warnedDrawNoPso)
            return;
        _warnedDrawNoPso = true;
        Debug.LogWarning(_currentShader == null
            ? "D3D12 draw skipped: no backend-neutral shader variant is bound."
            : "D3D12 draw skipped: pipeline state objects are not ready yet.");
    }

    private void WarnOnce(CommandOpcode op, string message)
    {
        if (!_warnedOpcodes.Add(op))
            return;
        Debug.LogWarning(message);
    }

    private void SkipOpcode(
        CommandOpcode op,
        ReadOnlySpan<byte> stream,
        ref int pos,
        List<object?> objects,
        TransientStore store)
    {
        WarnOnce(op, $"D3D12CommandTranslator: unhandled opcode {op} (skipped).");

        switch (op)
        {
            case CommandOpcode.BlitFramebuffer:
                pos += sizeof(int) * 8 + 2; // 8 ints + mask + filter
                break;
            case CommandOpcode.SetRasterState:
                pos += Unsafe.SizeOf<RasterizerState>();
                break;
            case CommandOpcode.SetProperties:
            case CommandOpcode.ClearProperties:
            case CommandOpcode.SetInstanceProperties:
            case CommandOpcode.ClearInstanceProperties:
            case CommandOpcode.ClearGlobalTexture:
            case CommandOpcode.GenerateMipmap:
            case CommandOpcode.CreateVertexArrayOp:
            case CommandOpcode.DisposeVertexArray:
            case CommandOpcode.CreateFramebufferOp:
            case CommandOpcode.DisposeFramebuffer:
            case CommandOpcode.CompileShader:
            case CommandOpcode.DisposeShader:
                _ = objects[ReadU16(stream, ref pos)];
                break;
            case CommandOpcode.SetMaterialProperties:
            case CommandOpcode.SetGlobalTexture:
            case CommandOpcode.SetGlobalTexture3D:
            case CommandOpcode.SetGlobalTextureCube:
            case CommandOpcode.SetGlobalMatrices:
            case CommandOpcode.SetUniformTexture:
                _ = objects[ReadU16(stream, ref pos)];
                _ = objects[ReadU16(stream, ref pos)];
                break;
            case CommandOpcode.SetGlobalInt:
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadI32(stream, ref pos);
                break;
            case CommandOpcode.SetGlobalFloat:
            case CommandOpcode.SetUniformFloat:
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadF32(stream, ref pos);
                break;
            case CommandOpcode.SetGlobalVec2:
            case CommandOpcode.SetUniformVec2:
                _ = objects[ReadU16(stream, ref pos)];
                pos += Unsafe.SizeOf<Vector.Float2>();
                break;
            case CommandOpcode.SetGlobalVec3:
            case CommandOpcode.SetUniformVec3:
                _ = objects[ReadU16(stream, ref pos)];
                pos += Unsafe.SizeOf<Vector.Float3>();
                break;
            case CommandOpcode.SetGlobalVec4:
            case CommandOpcode.SetUniformVec4:
            case CommandOpcode.SetGlobalColor:
                _ = objects[ReadU16(stream, ref pos)];
                pos += Unsafe.SizeOf<Vector.Float4>();
                break;
            case CommandOpcode.SetGlobalMatrix:
            case CommandOpcode.SetUniformMatrix:
                _ = objects[ReadU16(stream, ref pos)];
                pos += Unsafe.SizeOf<Vector.Float4x4>();
                break;
            case CommandOpcode.SetGlobalBuffer:
            case CommandOpcode.SetUniformBuffer:
                _ = objects[ReadU16(stream, ref pos)];
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadU32(stream, ref pos);
                break;
            case CommandOpcode.ClearAllGlobals:
                break;
            case CommandOpcode.SetUniformInt:
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadI32(stream, ref pos);
                break;
            case CommandOpcode.SetUniformMatrixArray:
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadBlob<Vector.Float4x4>(stream, ref pos, store);
                break;
            case CommandOpcode.UpdateTexture:
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadI32(stream, ref pos);
                _ = ReadI32(stream, ref pos);
                _ = ReadU32(stream, ref pos);
                _ = ReadU32(stream, ref pos);
                _ = ReadI32(stream, ref pos);
                _ = ReadBlob<byte>(stream, ref pos, store);
                break;
            case CommandOpcode.AllocateTextureCubeFace:
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadI32(stream, ref pos);
                _ = ReadI32(stream, ref pos);
                _ = ReadU32(stream, ref pos);
                _ = ReadBlob<byte>(stream, ref pos, store);
                break;
            case CommandOpcode.AllocateTexture3D:
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadI32(stream, ref pos);
                _ = ReadU32(stream, ref pos);
                _ = ReadU32(stream, ref pos);
                _ = ReadU32(stream, ref pos);
                _ = ReadBlob<byte>(stream, ref pos, store);
                break;
            case CommandOpcode.UpdateTexture3D:
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadI32(stream, ref pos);
                _ = ReadI32(stream, ref pos);
                _ = ReadI32(stream, ref pos);
                _ = ReadI32(stream, ref pos);
                _ = ReadU32(stream, ref pos);
                _ = ReadU32(stream, ref pos);
                _ = ReadU32(stream, ref pos);
                _ = ReadBlob<byte>(stream, ref pos, store);
                break;
            case CommandOpcode.SetTextureWrap:
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadU8(stream, ref pos);
                _ = ReadU8(stream, ref pos);
                break;
            case CommandOpcode.SetTextureFiltersOp:
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadU8(stream, ref pos);
                _ = ReadU8(stream, ref pos);
                break;
            case CommandOpcode.SetTextureCompareMode:
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadU8(stream, ref pos);
                break;
            case CommandOpcode.GetTextureData:
            case CommandOpcode.GetTextureDataPtr:
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadI32(stream, ref pos);
                if (op == CommandOpcode.GetTextureData)
                    _ = objects[ReadU16(stream, ref pos)];
                else
                    pos += sizeof(long);
                break;
            case CommandOpcode.GetTextureCubeFaceData:
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadI32(stream, ref pos);
                _ = ReadI32(stream, ref pos);
                _ = objects[ReadU16(stream, ref pos)];
                break;
            default:
                throw new InvalidOperationException($"D3D12CommandTranslator: cannot skip unknown opcode {op}.");
        }
    }

    private static CommandOpcode ReadOpcode(ReadOnlySpan<byte> s, ref int pos)
    {
        var v = MemoryMarshal.Read<CommandOpcode>(s.Slice(pos));
        pos += sizeof(ushort);
        return v;
    }

    private static byte ReadU8(ReadOnlySpan<byte> s, ref int pos) { byte v = s[pos]; pos += 1; return v; }
    private static ushort ReadU16(ReadOnlySpan<byte> s, ref int pos) { var v = MemoryMarshal.Read<ushort>(s.Slice(pos)); pos += sizeof(ushort); return v; }
    private static int ReadI32(ReadOnlySpan<byte> s, ref int pos) { var v = MemoryMarshal.Read<int>(s.Slice(pos)); pos += sizeof(int); return v; }
    private static uint ReadU32(ReadOnlySpan<byte> s, ref int pos) { var v = MemoryMarshal.Read<uint>(s.Slice(pos)); pos += sizeof(uint); return v; }
    private static float ReadF32(ReadOnlySpan<byte> s, ref int pos) { var v = MemoryMarshal.Read<float>(s.Slice(pos)); pos += sizeof(float); return v; }

    private static T ReadStruct<T>(ReadOnlySpan<byte> s, ref int pos) where T : unmanaged
    {
        var v = MemoryMarshal.Read<T>(s.Slice(pos));
        pos += Unsafe.SizeOf<T>();
        return v;
    }

    private static ReadOnlySpan<T> ReadBlob<T>(ReadOnlySpan<byte> s, ref int pos, TransientStore store) where T : unmanaged
    {
        var r = ReadStruct<TransientStore.Ref>(s, ref pos);
        return store.Read<T>(r);
    }
}
