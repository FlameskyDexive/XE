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
using Prowl.Runtime.RHI;

using CommandBuffer = Prowl.Runtime.CommandBuffer;

namespace Prowl.Runtime.Backends.D3D12;

/// <summary>
/// Translates engine <see cref="CommandBuffer"/> recordings into D3D12 commands.
/// Resource create/dispose and clears are implemented; draws no-op until PSOs exist.
/// </summary>
internal sealed unsafe class D3D12CommandTranslator
{
    private const ulong TransientUniformArenaSize = 64 * 1024;

    private readonly D3D12GraphicsDevice _device;
    private readonly ID3D12DescriptorHeap[] _descriptorHeaps;
    private readonly HashSet<CommandOpcode> _warnedOpcodes = new();
    private bool _warnedDrawNoPso;

    private GraphicsFrameBuffer? _pendingRenderTarget;
    private GraphicsFrameBuffer? _pendingReadTarget;
    private ShaderVariant? _currentShader;
    private RasterizerState _currentRaster = new();
    private readonly Dictionary<string, GraphicsBuffer> _uniformBuffers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, D3D12TransientUniformBinding> _transientUniformBuffers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GraphicsTexture> _textures = new(StringComparer.Ordinal);
    private Rendering.ObjectUniformsData _objectUniforms;
    private bool _objectUniformsDirty;
    private List<ID3D12Resource>? _submissionTransientResources;
    private ID3D12Resource? _transientUniformArena;
    private ulong _transientUniformArenaOffset;

    public D3D12CommandTranslator(D3D12GraphicsDevice device)
    {
        _device = device;
        _descriptorHeaps = [device.CbvSrvUavHeap, device.SamplerHeap];
    }

    public void Translate(
        CommandBuffer commandBuffer,
        ID3D12GraphicsCommandList list,
        List<ID3D12Resource> transientResources)
    {
        var stream = commandBuffer._stream.AsSpan(0, commandBuffer._streamPos);
        var objects = commandBuffer._objects;
        var store = commandBuffer._store;
        int pos = 0;
        _uniformBuffers.Clear();
        _transientUniformBuffers.Clear();
        _textures.Clear();
        _objectUniforms = Rendering.ObjectUniformsData.Identity;
        _objectUniformsDirty = true;
        _submissionTransientResources = transientResources;
        _transientUniformArena = null;
        _transientUniformArenaOffset = 0;
        GraphicsBuffer? globalUniforms = Rendering.GlobalUniforms.GetBuffer();
        if (globalUniforms is { Handle: not 0 })
            _uniformBuffers["GlobalUniforms"] = globalUniforms;

        while (pos < stream.Length)
        {
            CommandOpcode op = ReadOpcode(stream, ref pos);
            switch (op)
            {
                case CommandOpcode.SetRenderTarget:
                {
                    GraphicsFrameBuffer? framebuffer = (GraphicsFrameBuffer?)objects[ReadU16(stream, ref pos)];
                    SetRenderTarget(list, framebuffer);
                    _pendingReadTarget = framebuffer;
                    break;
                }
                case CommandOpcode.SetRenderTargets:
                {
                    SetRenderTarget(list, (GraphicsFrameBuffer?)objects[ReadU16(stream, ref pos)]);
                    _pendingReadTarget = (GraphicsFrameBuffer?)objects[ReadU16(stream, ref pos)];
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
                case CommandOpcode.BlitFramebuffer:
                {
                    int srcX = ReadI32(stream, ref pos);
                    int srcY = ReadI32(stream, ref pos);
                    int srcWidth = ReadI32(stream, ref pos);
                    int srcHeight = ReadI32(stream, ref pos);
                    int dstX = ReadI32(stream, ref pos);
                    int dstY = ReadI32(stream, ref pos);
                    int dstWidth = ReadI32(stream, ref pos);
                    int dstHeight = ReadI32(stream, ref pos);
                    ClearFlags mask = (ClearFlags)ReadU8(stream, ref pos);
                    BlitFilter filter = (BlitFilter)ReadU8(stream, ref pos);
                    DoBlit(list, srcX, srcY, srcWidth, srcHeight, dstX, dstY, dstWidth, dstHeight, mask, filter);
                    break;
                }
                case CommandOpcode.SetShader:
                {
                    _currentShader = objects[ReadU16(stream, ref pos)] as ShaderVariant;
                    if (_currentShader?.Bytecode?.Format != ShaderBytecodeFormat.Dxil)
                        WarnOnce(CommandOpcode.SetShader, "D3D12 shader bind skipped: expected a DXIL ShaderVariant.");
                    else
                        _device.GetOrCreateShaderLayout(_currentShader);
                    break;
                }
                case CommandOpcode.SetInstanceProperties:
                {
                    var properties = (Rendering.PropertyState?)objects[ReadU16(stream, ref pos)];
                    ApplyObjectProperties(properties);
                    break;
                }
                case CommandOpcode.ClearInstanceProperties:
                {
                    _objectUniforms = Rendering.ObjectUniformsData.Identity;
                    _objectUniformsDirty = true;
                    break;
                }
                case CommandOpcode.SetUniformBuffer:
                {
                    string? name = (string?)objects[ReadU16(stream, ref pos)];
                    GraphicsBuffer? buffer = (GraphicsBuffer?)objects[ReadU16(stream, ref pos)];
                    _ = ReadU32(stream, ref pos);
                    if (name != null && buffer != null)
                        _uniformBuffers[name] = buffer;
                    break;
                }
                case CommandOpcode.SetUniformInt:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    int value = ReadI32(stream, ref pos);
                    if (name == "_ObjectID")
                    {
                        _objectUniforms._ObjectID = value;
                        _objectUniformsDirty = true;
                    }
                    else
                    {
                        WarnOnce(CommandOpcode.SetUniformInt, $"D3D12CommandTranslator: uniform '{name}' is not packable yet.");
                    }
                    break;
                }
                case CommandOpcode.SetUniformMatrix:
                {
                    string name = (string)objects[ReadU16(stream, ref pos)]!;
                    Vector.Float4x4 value = ReadStruct<Vector.Float4x4>(stream, ref pos);
                    if (name == "prowl_ObjectToWorld")
                        _objectUniforms.prowl_ObjectToWorld = value;
                    else if (name == "prowl_WorldToObject")
                        _objectUniforms.prowl_WorldToObject = value;
                    else if (name == "prowl_PrevObjectToWorld")
                        _objectUniforms.prowl_PrevObjectToWorld = value;
                    else
                    {
                        WarnOnce(CommandOpcode.SetUniformMatrix, $"D3D12CommandTranslator: uniform '{name}' is not packable yet.");
                        break;
                    }
                    _objectUniformsDirty = true;
                    break;
                }
                case CommandOpcode.SetUniformTexture:
                {
                    string? name = (string?)objects[ReadU16(stream, ref pos)];
                    GraphicsTexture? texture = objects[ReadU16(stream, ref pos)] as GraphicsTexture;
                    if (name != null && texture != null)
                        _textures[name] = texture;
                    break;
                }
                case CommandOpcode.SetTextureWrap:
                {
                    var texture = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    byte axis = ReadU8(stream, ref pos);
                    var wrap = (TextureWrap)ReadU8(stream, ref pos);
                    SetTextureWrap(texture, axis, wrap);
                    break;
                }
                case CommandOpcode.SetTextureFiltersOp:
                {
                    var texture = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    var min = (TextureMin)ReadU8(stream, ref pos);
                    var mag = (TextureMag)ReadU8(stream, ref pos);
                    SetTextureFilters(texture, min, mag);
                    break;
                }
                case CommandOpcode.SetRasterState:
                {
                    _currentRaster = ReadStruct<RasterizerState>(stream, ref pos);
                    break;
                }
                case CommandOpcode.DrawIndexed:
                {
                    var vao = (GraphicsVertexArray?)objects[ReadU16(stream, ref pos)];
                    Topology topology = (Topology)ReadU8(stream, ref pos);
                    uint indexCount = ReadU32(stream, ref pos);
                    uint startIndex = ReadU32(stream, ref pos);
                    int baseVertex = ReadI32(stream, ref pos);
                    bool index32Bit = ReadU8(stream, ref pos) != 0;
                    DrawIndexed(list, vao, topology, indexCount, startIndex, baseVertex, index32Bit);
                    break;
                }
                case CommandOpcode.DrawIndexedInstanced:
                {
                    var vao = (GraphicsVertexArray?)objects[ReadU16(stream, ref pos)];
                    Topology topology = (Topology)ReadU8(stream, ref pos);
                    uint indexCount = ReadU32(stream, ref pos);
                    uint instanceCount = ReadU32(stream, ref pos);
                    uint startIndex = ReadU32(stream, ref pos);
                    int baseVertex = ReadI32(stream, ref pos);
                    bool index32Bit = ReadU8(stream, ref pos) != 0;
                    DrawIndexedInstanced(list, vao, topology, indexCount, instanceCount, startIndex, baseVertex, index32Bit);
                    break;
                }
                case CommandOpcode.DrawArrays:
                {
                    var vao = (GraphicsVertexArray?)objects[ReadU16(stream, ref pos)];
                    Topology topology = (Topology)ReadU8(stream, ref pos);
                    int first = ReadI32(stream, ref pos);
                    uint count = ReadU32(stream, ref pos);
                    DrawArrays(list, vao, topology, first, count);
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
                case CommandOpcode.AllocateTexture3D:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    int mip = ReadI32(stream, ref pos);
                    uint w = ReadU32(stream, ref pos);
                    uint h = ReadU32(stream, ref pos);
                    uint d = ReadU32(stream, ref pos);
                    ReadOnlySpan<byte> data = ReadBlob<byte>(stream, ref pos, store);
                    AllocateTexture3D(tex, mip, w, h, d, data);
                    break;
                }
                case CommandOpcode.AllocateTextureCubeFace:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    int face = ReadI32(stream, ref pos);
                    int mip = ReadI32(stream, ref pos);
                    uint size = ReadU32(stream, ref pos);
                    ReadOnlySpan<byte> data = ReadBlob<byte>(stream, ref pos, store);
                    AllocateTextureCubeFace(tex, face, mip, size, data);
                    break;
                }
                case CommandOpcode.GenerateMipmap:
                {
                    GraphicsTexture? texture = objects[ReadU16(stream, ref pos)] as GraphicsTexture;
                    if (texture != null)
                        GenerateMipmaps(list, texture);
                    break;
                }
                case CommandOpcode.CreateVertexArrayOp:
                {
                    var vao = (GraphicsVertexArray)objects[ReadU16(stream, ref pos)]!;
                    CreateVertexArray(vao);
                    break;
                }
                case CommandOpcode.DisposeVertexArray:
                {
                    var vao = (GraphicsVertexArray)objects[ReadU16(stream, ref pos)]!;
                    DisposeVertexArray(vao);
                    break;
                }
                case CommandOpcode.CreateFramebufferOp:
                {
                    var framebuffer = (GraphicsFrameBuffer)objects[ReadU16(stream, ref pos)]!;
                    CreateFramebuffer(framebuffer);
                    break;
                }
                case CommandOpcode.DisposeFramebuffer:
                {
                    var framebuffer = (GraphicsFrameBuffer)objects[ReadU16(stream, ref pos)]!;
                    DisposeFramebuffer(list, framebuffer);
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

        RestorePendingRenderTarget(list);
    }

    private void DoClear(
        ID3D12GraphicsCommandList list,
        ClearFlags flags,
        float r, float g, float b, float a,
        float depth,
        int stencil)
    {
        if ((flags & ClearFlags.Color) != 0)
        {
            var color = new Color4(r, g, b, a);
            D3D12FramebufferResource? framebuffer = GetPendingFramebuffer();
            if (framebuffer == null)
            {
                list.ClearRenderTargetView(_device.CurrentRtv, color);
            }
            else
            {
                TransitionFramebuffer(list, framebuffer, ResourceStates.RenderTarget);
                for (int i = 0; i < framebuffer.Rtvs.Length; i++)
                    list.ClearRenderTargetView(framebuffer.Rtvs[i], color);
            }
        }

        D3D12FramebufferResource? depthFramebuffer = GetPendingFramebuffer();
        if (depthFramebuffer != null && depthFramebuffer.DepthFormat != Format.Unknown &&
            (flags & (ClearFlags.Depth | ClearFlags.Stencil)) != 0)
        {
            Vortice.Direct3D12.ClearFlags clearFlags = Vortice.Direct3D12.ClearFlags.None;
            if ((flags & ClearFlags.Depth) != 0)
                clearFlags |= Vortice.Direct3D12.ClearFlags.Depth;
            if ((flags & ClearFlags.Stencil) != 0)
                clearFlags |= Vortice.Direct3D12.ClearFlags.Stencil;
            list.ClearDepthStencilView(depthFramebuffer.Dsv, clearFlags, depth, checked((byte)stencil), 0, null);
        }
    }

    private void DoBlit(
        ID3D12GraphicsCommandList list,
        int srcX,
        int srcY,
        int srcWidth,
        int srcHeight,
        int dstX,
        int dstY,
        int dstWidth,
        int dstHeight,
        ClearFlags mask,
        BlitFilter filter)
    {
        if (mask == 0)
            return;

        D3D12FramebufferResource source = GetFramebuffer(_pendingReadTarget, "read");
        D3D12FramebufferResource destination = GetFramebuffer(_pendingRenderTarget, "draw");
        if (mask == ClearFlags.Depth)
        {
            DoDepthBlit(list, source, destination, srcX, srcY, srcWidth, srcHeight, dstX, dstY, dstWidth, dstHeight, filter);
            return;
        }
        if (mask != ClearFlags.Color)
            throw new NotSupportedException("D3D12 framebuffer blits currently support color-only or depth-only masks.");

        if (source.ColorHandles.Length != 1 || destination.ColorHandles.Length != 1)
            throw new NotSupportedException("D3D12 framebuffer blits currently require one color attachment on each framebuffer.");
        if (source.SubresourceOnly || destination.SubresourceOnly || source.ColorSubresource != 0 || destination.ColorSubresource != 0)
            throw new NotSupportedException("D3D12 framebuffer blits currently support mip-0 Texture2D attachments only.");
        ValidateBlitRect(srcX, srcY, srcWidth, srcHeight, source.Width, source.Height, "source");
        ValidateBlitRect(dstX, dstY, dstWidth, dstHeight, destination.Width, destination.Height, "destination");
        if (source.ColorHandle == destination.ColorHandle)
            throw new InvalidOperationException("D3D12 framebuffer blit source and destination textures must differ.");

        if (!_device.Textures.TryGetValue(source.ColorHandle, out D3D12TextureResource? sourceTexture) ||
            sourceTexture.Resource == null || !sourceTexture.HasSrvDescriptor)
            throw new InvalidOperationException("D3D12 framebuffer blit source texture is not sample-ready.");
        if (!_device.Textures.TryGetValue(destination.ColorHandle, out D3D12TextureResource? destinationTexture) ||
            destinationTexture.Resource == null)
            throw new InvalidOperationException("D3D12 framebuffer blit destination texture is not available.");
        if (sourceTexture.Type != TextureType.Texture2D || destinationTexture.Type != TextureType.Texture2D)
            throw new NotSupportedException("D3D12 framebuffer blits currently support Texture2D resources only.");
        if (sourceTexture.State != ResourceStates.PixelShaderResource)
            throw new InvalidOperationException("D3D12 framebuffer blit source must be in pixel-shader resource state.");

        RestorePendingRenderTarget(list);
        if (destinationTexture.State != ResourceStates.PixelShaderResource)
            throw new InvalidOperationException("D3D12 framebuffer blit destination must be in pixel-shader resource state before transfer.");
        TransitionFramebuffer(list, destination, ResourceStates.RenderTarget);

        ID3D12PipelineState pipeline = _device.GetOrCreateFramebufferBlitPipeline(
            destination.ColorFormat,
            out ID3D12RootSignature rootSignature,
            out GpuDescriptorHandle pointSampler,
            out GpuDescriptorHandle linearSampler);
        list.SetDescriptorHeaps(_descriptorHeaps);
        list.SetPipelineState(pipeline);
        list.SetGraphicsRootSignature(rootSignature);
        list.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
        list.RSSetViewport(new Viewport(
            dstX,
            dstY,
            checked((uint)(dstWidth - dstX)),
            checked((uint)(dstHeight - dstY)),
            0f,
            1f));
        list.RSSetScissorRect(new RectI(dstX, dstY, dstWidth, dstHeight));
        list.OMSetRenderTargets(destination.Rtv, null);
        list.SetGraphicsRootDescriptorTable(0, sourceTexture.SrvDescriptor.Gpu);
        list.SetGraphicsRootDescriptorTable(1, filter == BlitFilter.Linear ? linearSampler : pointSampler);
        list.SetGraphicsRoot32BitConstant(2, BitConverter.SingleToUInt32Bits(srcX / (float)source.Width), 0);
        list.SetGraphicsRoot32BitConstant(2, BitConverter.SingleToUInt32Bits(srcY / (float)source.Height), 1);
        list.SetGraphicsRoot32BitConstant(2, BitConverter.SingleToUInt32Bits(srcWidth / (float)source.Width), 2);
        list.SetGraphicsRoot32BitConstant(2, BitConverter.SingleToUInt32Bits(srcHeight / (float)source.Height), 3);
        list.DrawInstanced(3, 1, 0, 0);
        TransitionFramebuffer(list, destination, ResourceStates.PixelShaderResource);
    }

    private void DoDepthBlit(
        ID3D12GraphicsCommandList list,
        D3D12FramebufferResource source,
        D3D12FramebufferResource destination,
        int srcX,
        int srcY,
        int srcWidth,
        int srcHeight,
        int dstX,
        int dstY,
        int dstWidth,
        int dstHeight,
        BlitFilter filter)
    {
        if (filter != BlitFilter.Nearest)
            throw new NotSupportedException("D3D12 depth framebuffer blits require nearest filtering.");
        if (source.DepthHandle == 0 || destination.DepthHandle == 0)
            throw new InvalidOperationException("D3D12 depth framebuffer blits require depth attachments on both framebuffers.");
        if (source.DepthFormat != destination.DepthFormat)
            throw new NotSupportedException("D3D12 depth framebuffer blits require matching depth formats.");
        if (source.Width != destination.Width || source.Height != destination.Height ||
            srcX != 0 || srcY != 0 || dstX != 0 || dstY != 0 ||
            srcWidth != checked((int)source.Width) || srcHeight != checked((int)source.Height) ||
            dstWidth != checked((int)destination.Width) || dstHeight != checked((int)destination.Height))
        {
            throw new NotSupportedException("D3D12 depth framebuffer blits currently require equal extents and full-frame rectangles.");
        }
        if (source.DepthHandle == destination.DepthHandle)
            throw new InvalidOperationException("D3D12 depth framebuffer blit source and destination textures must differ.");
        if (!_device.Textures.TryGetValue(source.DepthHandle, out D3D12TextureResource? sourceTexture) ||
            sourceTexture.Resource == null)
            throw new InvalidOperationException("D3D12 depth framebuffer blit source texture is not available.");
        if (!_device.Textures.TryGetValue(destination.DepthHandle, out D3D12TextureResource? destinationTexture) ||
            destinationTexture.Resource == null)
            throw new InvalidOperationException("D3D12 depth framebuffer blit destination texture is not available.");
        if (sourceTexture.State != ResourceStates.DepthWrite || destinationTexture.State != ResourceStates.DepthWrite)
            throw new InvalidOperationException("D3D12 depth framebuffer blit attachments must be in depth-write state before copy.");

        list.ResourceBarrierTransition(sourceTexture.Resource, ResourceStates.DepthWrite, ResourceStates.CopySource);
        list.ResourceBarrierTransition(destinationTexture.Resource, ResourceStates.DepthWrite, ResourceStates.CopyDest);
        list.CopyTextureRegion(
            new TextureCopyLocation(destinationTexture.Resource, 0),
            0,
            0,
            0,
            new TextureCopyLocation(sourceTexture.Resource, 0),
            null);
        list.ResourceBarrierTransition(sourceTexture.Resource, ResourceStates.CopySource, ResourceStates.DepthWrite);
        list.ResourceBarrierTransition(destinationTexture.Resource, ResourceStates.CopyDest, ResourceStates.DepthWrite);
    }

    private D3D12FramebufferResource GetFramebuffer(GraphicsFrameBuffer? framebuffer, string role)
    {
        if (framebuffer == null || framebuffer.Handle == 0 ||
            !_device.Framebuffers.TryGetValue(framebuffer.Handle, out D3D12FramebufferResource? resource))
            throw new NotSupportedException($"D3D12 framebuffer blits currently require a custom {role} framebuffer.");
        return resource;
    }

    private static void ValidateBlitRect(
        int x0,
        int y0,
        int x1,
        int y1,
        uint width,
        uint height,
        string role)
    {
        if (x0 < 0 || y0 < 0 || x1 < 0 || y1 < 0 ||
            x0 > width || x1 > width || y0 > height || y1 > height ||
            x0 >= x1 || y0 >= y1)
            throw new ArgumentOutOfRangeException(role, $"D3D12 {role} blit rectangle must be non-empty and within the framebuffer extent.");
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

    private void CreateFramebuffer(GraphicsFrameBuffer framebuffer)
    {
        GraphicsFrameBuffer.Attachment[] attachments = framebuffer.Attachments;
        if (attachments.Length is < 1 or > 9)
            throw new NotSupportedException("D3D12 custom framebuffers support one through eight color attachments and one depth attachment.");
        int colorCount = 0;
        int depthCount = 0;
        for (int i = 0; i < attachments.Length; i++)
        {
            if (attachments[i].IsDepth)
                depthCount++;
            else
                colorCount++;
        }
        if (colorCount is < 1 or > 8 || depthCount > 1)
            throw new NotSupportedException("D3D12 custom framebuffers require one through eight color attachments and at most one depth attachment.");

        var rtvs = new CpuDescriptorHandle[colorCount];
        var colorFormats = new Format[colorCount];
        var colorHandles = new uint[colorCount];
        var colorSubresources = new uint[colorCount];
        var subresourceOnly = new bool[colorCount];
        CpuDescriptorHandle dsv = default;
        Format depthFormat = Format.Unknown;
        uint depthHandle = 0;
        int colorIndex = 0;
        for (int i = 0; i < attachments.Length; i++)
        {
            GraphicsFrameBuffer.Attachment attachment = attachments[i];
            GraphicsTexture texture = attachment.Texture;
            if (texture.Handle == 0 ||
                !_device.Textures.TryGetValue(texture.Handle, out D3D12TextureResource? image) || image.Resource == null)
            {
                throw new InvalidOperationException("D3D12 framebuffer color attachment is not ready.");
            }
            if (attachment.MipLevel < 0 || (uint)attachment.MipLevel >= image.MipLevels)
                throw new ArgumentOutOfRangeException(nameof(attachment.MipLevel));
            bool isDepth = attachment.IsDepth;
            if (isDepth != D3D12Formats.IsDepth(image.EngineFormat))
                throw new ArgumentException("D3D12 framebuffer attachment depth flag must match the texture format.");
            if (isDepth && attachment.IsCubeFace)
                throw new NotSupportedException("D3D12 depth cubemap framebuffer attachments are not supported yet.");

            uint arraySlice = 0;
            if (!isDepth && attachment.IsCubeFace)
            {
                if (image.Type != TextureType.TextureCubeMap || (uint)attachment.CubeFace >= 6)
                    throw new ArgumentException("D3D12 cubemap framebuffer attachment requires a valid face 0 through 5.");
                arraySlice = checked((uint)attachment.CubeFace);
                subresourceOnly[i] = true;
            }
            else if (image.Type != TextureType.Texture2D)
            {
                throw new NotSupportedException("D3D12 non-cubemap framebuffer attachments must be Texture2D resources.");
            }

            uint expectedWidth = Math.Max(1u, image.Width >> attachment.MipLevel);
            uint expectedHeight = Math.Max(1u, image.Height >> attachment.MipLevel);
            if (framebuffer.Width != expectedWidth || framebuffer.Height != expectedHeight)
                throw new ArgumentException($"D3D12 framebuffer extent must match attachment mip extent {expectedWidth}x{expectedHeight}.");

            if (isDepth)
            {
                if (image.State != ResourceStates.DepthWrite)
                    throw new InvalidOperationException("D3D12 framebuffer depth attachment is not in depth-write state.");
                dsv = _device.AllocateDsvDescriptor();
                var dsvDescription = new DepthStencilViewDescription
                {
                    Format = image.Format,
                    ViewDimension = DepthStencilViewDimension.Texture2D,
                    Texture2D = new Texture2DDepthStencilView
                    {
                        MipSlice = checked((uint)attachment.MipLevel),
                    },
                };
                _device.Device.CreateDepthStencilView(image.Resource, dsvDescription, dsv);
                depthFormat = image.Format;
                depthHandle = texture.Handle;
                continue;
            }

            CpuDescriptorHandle rtv = _device.AllocateRtvDescriptor();
            uint colorSubresource = checked((uint)attachment.MipLevel + arraySlice * image.MipLevels);
            var rtvDescription = new RenderTargetViewDescription
            {
                Format = image.Format,
                ViewDimension = attachment.IsCubeFace
                    ? RenderTargetViewDimension.Texture2DArray
                    : RenderTargetViewDimension.Texture2D,
            };
            if (attachment.IsCubeFace)
            {
                rtvDescription.Texture2DArray = new Texture2DArrayRenderTargetView
                {
                    MipSlice = checked((uint)attachment.MipLevel),
                    FirstArraySlice = arraySlice,
                    ArraySize = 1,
                    PlaneSlice = 0,
                };
            }
            else
            {
                rtvDescription.Texture2D = new Texture2DRenderTargetView
                {
                    MipSlice = checked((uint)attachment.MipLevel),
                    PlaneSlice = 0,
                };
            }
            _device.Device.CreateRenderTargetView(image.Resource, rtvDescription, rtv);
            rtvs[colorIndex] = rtv;
            colorFormats[colorIndex] = image.Format;
            colorHandles[colorIndex] = texture.Handle;
            colorSubresources[colorIndex] = colorSubresource;
            subresourceOnly[colorIndex] = attachment.IsCubeFace;
            colorIndex++;
        }

        uint handle = _device.AllocateHandle();
        framebuffer.Handle = handle;
        _device.Framebuffers[handle] = new D3D12FramebufferResource
        {
            Rtv = rtvs[0],
            Rtvs = rtvs,
            Width = framebuffer.Width,
            Height = framebuffer.Height,
            ColorFormat = colorFormats[0],
            ColorFormats = new D3D12ColorAttachmentFormats(colorFormats),
            ColorHandle = colorHandles[0],
            ColorHandles = colorHandles,
            ColorSubresource = colorSubresources[0],
            ColorSubresources = colorSubresources,
            SubresourceOnly = subresourceOnly[0],
            SubresourceOnlyByAttachment = subresourceOnly,
            Dsv = dsv,
            DepthFormat = depthFormat,
            DepthHandle = depthHandle,
        };
    }

    private void DisposeFramebuffer(ID3D12GraphicsCommandList list, GraphicsFrameBuffer framebuffer)
    {
        if (framebuffer.Handle == 0)
            return;
        if (ReferenceEquals(_pendingRenderTarget, framebuffer))
        {
            RestorePendingRenderTarget(list);
            _pendingRenderTarget = null;
        }
        _device.Framebuffers.Remove(framebuffer.Handle);
        framebuffer.Handle = 0;
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

        if (!D3D12Formats.IsDepth(tex.ImageFormat))
        {
            if (!res.HasSrvDescriptor)
            {
                res.SrvDescriptor = _device.AllocateSrvDescriptor();
                res.HasSrvDescriptor = true;
            }

            if (!res.HasSamplerDescriptor)
            {
                res.SamplerDescriptor = _device.AllocateSamplerDescriptor();
                res.HasSamplerDescriptor = true;
            }

            var srv = new ShaderResourceViewDescription
            {
                Format = format,
                ViewDimension = ShaderResourceViewDimension.Texture2D,
                Shader4ComponentMapping = ShaderComponentMapping.Default,
                Texture2D = new Texture2DShaderResourceView
                {
                    MostDetailedMip = 0,
                    MipLevels = 1,
                    PlaneSlice = 0,
                    ResourceMinLODClamp = 0,
                },
            };
            _device.Device.CreateShaderResourceView(res.Resource, srv, res.SrvDescriptor.Cpu);

            WriteSamplerDescriptor(res);
        }

        if (data.Length > 0 && !D3D12Formats.IsDepth(tex.ImageFormat))
        {
            _device.UploadTexture2D(
                res.Resource,
                data,
                width,
                height,
                D3D12Formats.BytesPerPixel(tex.ImageFormat),
                res.State,
                out ResourceStates uploadedState);
            res.State = uploadedState;
        }
    }

    private void CreateVertexArray(GraphicsVertexArray vao)
    {
        uint handle = _device.AllocateHandle();
        vao.Handle = handle;
        _device.VertexArrays[handle] = new D3D12VertexArrayResource
        {
            VertexBuffer = vao.Vertices.Handle,
            IndexBuffer = vao.Indices?.Handle ?? 0,
            InstanceBuffer = vao.InstanceBuffer?.Handle ?? 0,
            Format = vao.Format,
            InstanceFormat = vao.InstanceFormat,
        };
    }

    private void AllocateTexture3D(
        GraphicsTexture tex,
        int mip,
        uint width,
        uint height,
        uint depth,
        ReadOnlySpan<byte> data)
    {
        if (tex.Handle == 0 || !_device.Textures.TryGetValue(tex.Handle, out D3D12TextureResource? resource))
            return;
        if (mip != 0)
            return;
        if (tex.Type != TextureType.Texture3D)
            throw new InvalidOperationException("D3D12 AllocateTexture3D requires a Texture3D resource.");
        if (D3D12Formats.IsDepth(tex.ImageFormat))
            throw new NotSupportedException("D3D12 depth Texture3D resources are not supported.");

        resource.Resource?.Dispose();
        Format format = D3D12Formats.ToDxgiFormat(tex.ImageFormat);
        resource.Resource = _device.CreateCommittedTexture3D(width, height, depth, format, ResourceStates.Common);
        resource.Width = width;
        resource.Height = height;
        resource.Depth = depth;
        resource.Format = format;
        resource.State = ResourceStates.Common;

        if (!resource.HasSrvDescriptor)
        {
            resource.SrvDescriptor = _device.AllocateSrvDescriptor();
            resource.HasSrvDescriptor = true;
        }
        if (!resource.HasSamplerDescriptor)
        {
            resource.SamplerDescriptor = _device.AllocateSamplerDescriptor();
            resource.HasSamplerDescriptor = true;
        }

        var srv = new ShaderResourceViewDescription
        {
            Format = format,
            ViewDimension = ShaderResourceViewDimension.Texture3D,
            Shader4ComponentMapping = ShaderComponentMapping.Default,
            Texture3D = new Texture3DShaderResourceView
            {
                MostDetailedMip = 0,
                MipLevels = 1,
                ResourceMinLODClamp = 0,
            },
        };
        _device.Device.CreateShaderResourceView(resource.Resource, srv, resource.SrvDescriptor.Cpu);
        WriteSamplerDescriptor(resource);

        if (data.Length > 0)
        {
            _device.UploadTexture3D(
                resource.Resource,
                data,
                width,
                height,
                depth,
                D3D12Formats.BytesPerPixel(tex.ImageFormat),
                resource.State,
                out ResourceStates uploadedState);
            resource.State = uploadedState;
        }
    }

    private void AllocateTextureCubeFace(
        GraphicsTexture tex,
        int face,
        int mip,
        uint size,
        ReadOnlySpan<byte> data)
    {
        if (tex.Handle == 0 || !_device.Textures.TryGetValue(tex.Handle, out D3D12TextureResource? resource))
            return;
        if ((uint)face >= 6)
            throw new ArgumentOutOfRangeException(nameof(face), face, "D3D12 cubemap face must be 0 through 5.");
        if (mip < 0)
            throw new ArgumentOutOfRangeException(nameof(mip));
        if (tex.Type != TextureType.TextureCubeMap)
            throw new InvalidOperationException("D3D12 AllocateTextureCubeFace requires a cubemap resource.");
        if (D3D12Formats.IsDepth(tex.ImageFormat))
            throw new NotSupportedException("D3D12 depth cubemaps are not supported.");

        if (resource.Resource == null && mip != 0)
            throw new InvalidOperationException("D3D12 cubemap mip 0 must be allocated before higher mip levels.");

        if (resource.Resource == null || (mip == 0 && (resource.Width != size || resource.Height != size)))
        {
            resource.Resource?.Dispose();
            Format format = D3D12Formats.ToDxgiFormat(tex.ImageFormat);
            uint mipLevels = CalculateMipLevels(size);
            resource.Resource = _device.CreateCommittedTextureCube(size, mipLevels, format, ResourceStates.Common);
            resource.Width = size;
            resource.Height = size;
            resource.Depth = 1;
            resource.MipLevels = mipLevels;
            resource.Format = format;
            resource.State = ResourceStates.Common;
            resource.CubeInitializedFaces = 0;
            resource.CubeInitializedFacesByMip = new byte[mipLevels];
            resource.AvailableMipLevels = 0;

            if (!resource.HasSrvDescriptor)
            {
                resource.SrvDescriptor = _device.AllocateSrvDescriptor();
                resource.HasSrvDescriptor = true;
            }
            if (!resource.HasSamplerDescriptor)
            {
                resource.SamplerDescriptor = _device.AllocateSamplerDescriptor();
                resource.HasSamplerDescriptor = true;
            }

            var srv = new ShaderResourceViewDescription
            {
                Format = format,
                ViewDimension = ShaderResourceViewDimension.TextureCube,
                Shader4ComponentMapping = ShaderComponentMapping.Default,
                TextureCube = new TextureCubeShaderResourceView
                {
                    MostDetailedMip = 0,
                    MipLevels = mipLevels,
                    ResourceMinLODClamp = 0,
                },
            };
            _device.Device.CreateShaderResourceView(resource.Resource, srv, resource.SrvDescriptor.Cpu);
            WriteSamplerDescriptor(resource);
            CreateCubemapMipGenerationViews(resource);
        }

        if ((uint)mip >= resource.MipLevels)
            throw new ArgumentOutOfRangeException(nameof(mip), mip, "D3D12 cubemap mip exceeds the allocated mip chain.");
        uint expectedSize = Math.Max(1u, resource.Width >> mip);
        if (size != expectedSize)
            throw new ArgumentException($"D3D12 cubemap mip {mip} expects size {expectedSize}, got {size}.", nameof(size));

        if (data.Length > 0)
        {
            uint destinationSubresource = checked((uint)mip + checked((uint)face) * resource.MipLevels);
            _device.UploadTexture2D(
                resource.Resource,
                data,
                size,
                size,
                D3D12Formats.BytesPerPixel(tex.ImageFormat),
                resource.State,
                out ResourceStates uploadedState,
                destinationSubresource);
            resource.State = uploadedState;
        }

        resource.CubeInitializedFacesByMip[mip] |= checked((byte)(1 << face));
        resource.CubeInitializedFaces = resource.CubeInitializedFacesByMip[0];
        uint availableMipLevels = 0;
        while (availableMipLevels < resource.MipLevels &&
            resource.CubeInitializedFacesByMip[availableMipLevels] == 0b0011_1111)
        {
            availableMipLevels++;
        }
        resource.AvailableMipLevels = availableMipLevels;
    }

    private void SetTextureWrap(GraphicsTexture texture, byte axis, TextureWrap wrap)
    {
        if (texture.Handle == 0 || !_device.Textures.TryGetValue(texture.Handle, out D3D12TextureResource? resource))
            return;

        switch (axis)
        {
            case 0: resource.WrapS = wrap; break;
            case 1: resource.WrapT = wrap; break;
            case 2: resource.WrapR = wrap; break;
            default: throw new ArgumentOutOfRangeException(nameof(axis), axis, "D3D12 texture wrap axis must be 0, 1, or 2.");
        }

        ReplaceSamplerDescriptorIfAllocated(resource);
    }

    private void CreateCubemapMipGenerationViews(D3D12TextureResource resource)
    {
        int generatedMipCount = checked((int)resource.MipLevels - 1);
        resource.MipGenerationSrvs = new D3D12DescriptorAllocation[generatedMipCount];
        resource.MipGenerationRtvs = new CpuDescriptorHandle[checked(generatedMipCount * 6)];
        for (int generatedMip = 0; generatedMip < generatedMipCount; generatedMip++)
        {
            uint sourceMip = checked((uint)generatedMip);
            D3D12DescriptorAllocation srvDescriptor = _device.AllocateSrvDescriptor();
            resource.MipGenerationSrvs[generatedMip] = srvDescriptor;
            var srv = new ShaderResourceViewDescription
            {
                Format = resource.Format,
                ViewDimension = ShaderResourceViewDimension.Texture2DArray,
                Shader4ComponentMapping = ShaderComponentMapping.Default,
                Texture2DArray = new Texture2DArrayShaderResourceView
                {
                    MostDetailedMip = sourceMip,
                    MipLevels = 1,
                    FirstArraySlice = 0,
                    ArraySize = 6,
                    PlaneSlice = 0,
                    ResourceMinLODClamp = 0,
                },
            };
            _device.Device.CreateShaderResourceView(resource.Resource, srv, srvDescriptor.Cpu);

            uint targetMip = sourceMip + 1;
            for (uint face = 0; face < 6; face++)
            {
                CpuDescriptorHandle rtv = _device.AllocateRtvDescriptor();
                resource.MipGenerationRtvs[generatedMip * 6 + face] = rtv;
                var rtvDescription = new RenderTargetViewDescription
                {
                    Format = resource.Format,
                    ViewDimension = RenderTargetViewDimension.Texture2DArray,
                    Texture2DArray = new Texture2DArrayRenderTargetView
                    {
                        MipSlice = targetMip,
                        FirstArraySlice = face,
                        ArraySize = 1,
                        PlaneSlice = 0,
                    },
                };
                _device.Device.CreateRenderTargetView(resource.Resource, rtvDescription, rtv);
            }
        }
    }

    private void GenerateMipmaps(ID3D12GraphicsCommandList list, GraphicsTexture texture)
    {
        if (texture.Handle == 0 || !_device.Textures.TryGetValue(texture.Handle, out D3D12TextureResource? resource) || resource.Resource == null)
            return;
        if (resource.Type != TextureType.TextureCubeMap)
            throw new NotSupportedException("D3D12 mip generation currently supports cubemaps only.");
        if (resource.CubeInitializedFaces != 0b0011_1111)
            throw new InvalidOperationException("D3D12 cubemap mip generation requires all base-level faces.");
        if (resource.MipLevels <= 1)
            return;
        if (resource.State != ResourceStates.PixelShaderResource)
            throw new InvalidOperationException("D3D12 cubemap mip generation requires pixel-shader resource state.");

        ID3D12PipelineState pipeline = _device.GetOrCreateCubemapMipPipeline(resource.Format, out ID3D12RootSignature rootSignature);
        list.SetDescriptorHeaps(_descriptorHeaps);
        list.SetPipelineState(pipeline);
        list.SetGraphicsRootSignature(rootSignature);
        list.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);

        for (uint mip = 1; mip < resource.MipLevels; mip++)
        {
            uint sourceWidth = Math.Max(1u, resource.Width >> checked((int)(mip - 1)));
            uint sourceHeight = Math.Max(1u, resource.Height >> checked((int)(mip - 1)));
            uint destinationWidth = Math.Max(1u, resource.Width >> checked((int)mip));
            uint destinationHeight = Math.Max(1u, resource.Height >> checked((int)mip));
            list.RSSetViewport(new Viewport(0, 0, destinationWidth, destinationHeight, 0f, 1f));
            list.RSSetScissorRect(new RectI(0, 0, checked((int)destinationWidth), checked((int)destinationHeight)));
            list.SetGraphicsRootDescriptorTable(0, resource.MipGenerationSrvs[mip - 1].Gpu);
            list.SetGraphicsRoot32BitConstant(1, sourceWidth, 1);
            list.SetGraphicsRoot32BitConstant(1, sourceHeight, 2);

            for (uint face = 0; face < 6; face++)
            {
                uint targetSubresource = mip + face * resource.MipLevels;
                list.ResourceBarrierTransition(
                    resource.Resource,
                    ResourceStates.PixelShaderResource,
                    ResourceStates.RenderTarget,
                    targetSubresource);
                CpuDescriptorHandle rtv = resource.MipGenerationRtvs[(mip - 1) * 6 + face];
                list.OMSetRenderTargets(rtv, null);
                list.SetGraphicsRoot32BitConstant(1, face, 0);
                list.DrawInstanced(3, 1, 0, 0);
                list.ResourceBarrierTransition(
                    resource.Resource,
                    ResourceStates.RenderTarget,
                    ResourceStates.PixelShaderResource,
                    targetSubresource);
            }
            resource.CubeInitializedFacesByMip[mip] = 0b0011_1111;
        }

        resource.AvailableMipLevels = resource.MipLevels;
    }

    private static uint CalculateMipLevels(uint size)
    {
        if (size == 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        uint levels = 1;
        while (size > 1)
        {
            size >>= 1;
            levels++;
        }
        return levels;
    }

    private void SetTextureFilters(GraphicsTexture texture, TextureMin min, TextureMag mag)
    {
        if (texture.Handle == 0 || !_device.Textures.TryGetValue(texture.Handle, out D3D12TextureResource? resource))
            return;

        resource.MinFilter = min;
        resource.MagFilter = mag;
        ReplaceSamplerDescriptorIfAllocated(resource);
    }

    private void ReplaceSamplerDescriptorIfAllocated(D3D12TextureResource resource)
    {
        if (!resource.HasSamplerDescriptor)
            return;

        resource.SamplerDescriptor = _device.AllocateSamplerDescriptor();
        WriteSamplerDescriptor(resource);
    }

    private void WriteSamplerDescriptor(D3D12TextureResource resource)
    {
        var sampler = new SamplerDescription(
            D3D12Formats.ToFilter(resource.MinFilter, resource.MagFilter),
            D3D12Formats.ToAddressMode(resource.WrapS),
            D3D12Formats.ToAddressMode(resource.WrapT),
            D3D12Formats.ToAddressMode(resource.WrapR),
            0,
            1,
            ComparisonFunction.Always,
            0,
            float.MaxValue);
        _device.Device.CreateSampler(ref sampler, resource.SamplerDescriptor.Cpu);
    }

    private void DisposeVertexArray(GraphicsVertexArray vao)
    {
        if (vao.Handle == 0)
            return;

        _device.VertexArrays.Remove(vao.Handle);
        vao.Handle = 0;
    }

    private void DrawArrays(
        ID3D12GraphicsCommandList list,
        GraphicsVertexArray? vao,
        Topology topology,
        int first,
        uint count)
    {
        if (_currentShader?.Bytecode?.Format != ShaderBytecodeFormat.Dxil || vao == null || vao.Handle == 0)
        {
            WarnDrawNoPso();
            return;
        }
        if (!_device.VertexArrays.TryGetValue(vao.Handle, out D3D12VertexArrayResource? vertexArray) ||
            !_device.Buffers.TryGetValue(vertexArray.VertexBuffer, out D3D12BufferResource? vertexBuffer) ||
            vertexBuffer.Resource == null)
        {
            WarnOnce(CommandOpcode.DrawArrays, "D3D12 DrawArrays skipped: vertex-array resources are incomplete.");
            return;
        }
        if (vertexArray.InstanceFormat != null)
            throw new NotSupportedException("D3D12 DrawArrays does not consume an instance stream; use an instanced draw opcode.");

        var key = new GraphicsPipelineKey(_currentShader, vao.Handle, topology, in _currentRaster, index32Bit: false);
        ID3D12PipelineState pipeline = _device.GetOrCreateGraphicsPipeline(key, _currentShader, GetCurrentTargetFormats());
        D3D12ShaderLayoutResource layout = _device.GetOrCreateShaderLayout(_currentShader);
        var vertexView = new VertexBufferView(
            vertexBuffer.Resource.GPUVirtualAddress,
            checked((uint)vertexBuffer.Size),
            checked((uint)vertexArray.Format.Size));

        BindCurrentRenderTargets(list);
        list.SetPipelineState(pipeline);
        list.SetGraphicsRootSignature(layout.RootSignature);
        BindShaderResources(list, layout);
        list.IASetPrimitiveTopology(D3D12Formats.ToTopology(topology));
        list.IASetVertexBuffers(0, 1, &vertexView);
        list.DrawInstanced(count, 1, checked((uint)first), 0);
    }

    private void DrawIndexed(
        ID3D12GraphicsCommandList list,
        GraphicsVertexArray? vao,
        Topology topology,
        uint indexCount,
        uint startIndex,
        int baseVertex,
        bool index32Bit)
    {
        if (_currentShader?.Bytecode?.Format != ShaderBytecodeFormat.Dxil || vao == null || vao.Handle == 0)
        {
            WarnDrawNoPso();
            return;
        }
        if (!_device.VertexArrays.TryGetValue(vao.Handle, out D3D12VertexArrayResource? vertexArray) ||
            !_device.Buffers.TryGetValue(vertexArray.VertexBuffer, out D3D12BufferResource? vertexBuffer) ||
            !_device.Buffers.TryGetValue(vertexArray.IndexBuffer, out D3D12BufferResource? indexBuffer) ||
            vertexBuffer.Resource == null || indexBuffer.Resource == null)
        {
            WarnOnce(CommandOpcode.DrawIndexed, "D3D12 DrawIndexed skipped: vertex-array resources are incomplete.");
            return;
        }
        if (vertexArray.InstanceFormat != null)
            throw new NotSupportedException("D3D12 DrawIndexed does not consume an instance stream; use DrawIndexedInstanced.");

        var key = new GraphicsPipelineKey(_currentShader, vao.Handle, topology, in _currentRaster, index32Bit);
        ID3D12PipelineState pipeline = _device.GetOrCreateGraphicsPipeline(key, _currentShader, GetCurrentTargetFormats());
        D3D12ShaderLayoutResource layout = _device.GetOrCreateShaderLayout(_currentShader);
        var vertexView = new VertexBufferView(
            vertexBuffer.Resource.GPUVirtualAddress,
            checked((uint)vertexBuffer.Size),
            checked((uint)vertexArray.Format.Size));
        var indexView = new IndexBufferView(
            indexBuffer.Resource.GPUVirtualAddress,
            checked((uint)indexBuffer.Size),
            index32Bit);

        BindCurrentRenderTargets(list);
        list.SetPipelineState(pipeline);
        list.SetGraphicsRootSignature(layout.RootSignature);
        BindShaderResources(list, layout);
        list.IASetPrimitiveTopology(D3D12Formats.ToTopology(topology));
        list.IASetVertexBuffers(0, 1, &vertexView);
        list.IASetIndexBuffer(&indexView);
        list.DrawIndexedInstanced(indexCount, 1, startIndex, baseVertex, 0);
    }

    private void DrawIndexedInstanced(
        ID3D12GraphicsCommandList list,
        GraphicsVertexArray? vao,
        Topology topology,
        uint indexCount,
        uint instanceCount,
        uint startIndex,
        int baseVertex,
        bool index32Bit)
    {
        if (_currentShader?.Bytecode?.Format != ShaderBytecodeFormat.Dxil || vao == null || vao.Handle == 0)
        {
            WarnDrawNoPso();
            return;
        }
        if (!_device.VertexArrays.TryGetValue(vao.Handle, out D3D12VertexArrayResource? vertexArray) ||
            vertexArray.InstanceFormat == null ||
            !_device.Buffers.TryGetValue(vertexArray.VertexBuffer, out D3D12BufferResource? vertexBuffer) ||
            !_device.Buffers.TryGetValue(vertexArray.InstanceBuffer, out D3D12BufferResource? instanceBuffer) ||
            !_device.Buffers.TryGetValue(vertexArray.IndexBuffer, out D3D12BufferResource? indexBuffer) ||
            vertexBuffer.Resource == null || instanceBuffer.Resource == null || indexBuffer.Resource == null)
        {
            WarnOnce(CommandOpcode.DrawIndexedInstanced, "D3D12 DrawIndexedInstanced skipped: vertex-array resources are incomplete.");
            return;
        }

        var key = new GraphicsPipelineKey(_currentShader, vao.Handle, topology, in _currentRaster, index32Bit);
        ID3D12PipelineState pipeline = _device.GetOrCreateGraphicsPipeline(key, _currentShader, GetCurrentTargetFormats());
        D3D12ShaderLayoutResource layout = _device.GetOrCreateShaderLayout(_currentShader);
        VertexBufferView* vertexViews = stackalloc VertexBufferView[2]
        {
            new(vertexBuffer.Resource.GPUVirtualAddress, checked((uint)vertexBuffer.Size), checked((uint)vertexArray.Format.Size)),
            new(instanceBuffer.Resource.GPUVirtualAddress, checked((uint)instanceBuffer.Size), checked((uint)vertexArray.InstanceFormat.Size)),
        };
        var indexView = new IndexBufferView(indexBuffer.Resource.GPUVirtualAddress, checked((uint)indexBuffer.Size), index32Bit);

        BindCurrentRenderTargets(list);
        list.SetPipelineState(pipeline);
        list.SetGraphicsRootSignature(layout.RootSignature);
        BindShaderResources(list, layout);
        list.IASetPrimitiveTopology(D3D12Formats.ToTopology(topology));
        list.IASetVertexBuffers(0, 2, vertexViews);
        list.IASetIndexBuffer(&indexView);
        list.DrawIndexedInstanced(indexCount, instanceCount, startIndex, baseVertex, 0);
    }

    private void BindShaderResources(ID3D12GraphicsCommandList list, D3D12ShaderLayoutResource layout)
    {
        ShaderBindingLayout bindingLayout = layout.BindingLayout;
        ShaderBindingSlot[] buffers = bindingLayout.Buffers;
        EnsureObjectUniformsBuffer(buffers);
        for (int i = 0; i < buffers.Length; i++)
        {
            ShaderBindingSlot binding = buffers[i];
            if (_transientUniformBuffers.TryGetValue(binding.Name, out D3D12TransientUniformBinding transientBuffer))
            {
                list.SetGraphicsRootConstantBufferView(
                    (uint)i,
                    transientBuffer.Resource.GPUVirtualAddress + transientBuffer.Offset);
                continue;
            }
            if (!_uniformBuffers.TryGetValue(binding.Name, out GraphicsBuffer? buffer) || buffer.Handle == 0)
                throw new InvalidOperationException($"D3D12 draw requires constant buffer '{binding.Name}'.");
            if (!_device.Buffers.TryGetValue(buffer.Handle, out D3D12BufferResource? resource) || resource.Resource == null)
                throw new InvalidOperationException($"D3D12 constant buffer '{binding.Name}' is not available.");
            if (resource.Type != BufferType.UniformBuffer)
                throw new InvalidOperationException($"D3D12 resource '{binding.Name}' is not a uniform buffer.");

            list.SetGraphicsRootConstantBufferView((uint)i, resource.Resource.GPUVirtualAddress);
        }

        int textureCount = bindingLayout.Textures.Length;
        int samplerCount = bindingLayout.Samplers.Length;
        if (textureCount == 0 && samplerCount == 0)
            return;

        list.SetDescriptorHeaps(2, _descriptorHeaps);

        for (int i = 0; i < textureCount; i++)
        {
            ShaderBindingSlot binding = bindingLayout.Textures[i];
            D3D12TextureResource resource = GetTextureResource(binding.Name);
            list.SetGraphicsRootDescriptorTable(
                checked((uint)(buffers.Length + i)),
                resource.SrvDescriptor.Gpu);
        }

        for (int i = 0; i < samplerCount; i++)
        {
            ShaderBindingSlot binding = bindingLayout.Samplers[i];
            string textureName = FindTextureNameForSampler(bindingLayout.Textures, binding.Slot);
            D3D12TextureResource resource = GetTextureResource(textureName);
            list.SetGraphicsRootDescriptorTable(
                checked((uint)(buffers.Length + textureCount + i)),
                resource.SamplerDescriptor.Gpu);
        }
    }

    private void EnsureObjectUniformsBuffer(ShaderBindingSlot[] buffers)
    {
        bool required = false;
        for (int i = 0; i < buffers.Length; i++)
        {
            if (buffers[i].Name == "ObjectUniforms")
            {
                required = true;
                break;
            }
        }
        if (!required || !_objectUniformsDirty)
            return;

        const ulong alignedSize = 256;
        if (_transientUniformArena == null || _transientUniformArenaOffset + alignedSize > TransientUniformArenaSize)
        {
            _transientUniformArena = _device.CreateCommittedBuffer(
                TransientUniformArenaSize,
                HeapType.Upload,
                ResourceStates.GenericRead);
            _submissionTransientResources!.Add(_transientUniformArena);
            _transientUniformArenaOffset = 0;
        }

        ulong offset = _transientUniformArenaOffset;
        byte* mapped = _transientUniformArena.Map<byte>(0);
        try
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref _objectUniforms, 1));
            fixed (byte* source = bytes)
                System.Buffer.MemoryCopy(source, mapped + offset, checked((long)alignedSize), bytes.Length);
        }
        finally
        {
            _transientUniformArena.Unmap(0);
        }

        _transientUniformBuffers["ObjectUniforms"] = new D3D12TransientUniformBinding(_transientUniformArena, offset);
        _transientUniformArenaOffset += alignedSize;
        _objectUniformsDirty = false;
    }

    private void ApplyObjectProperties(Rendering.PropertyState? properties)
    {
        _objectUniforms = Rendering.ObjectUniformsData.Identity;
        if (properties == null)
        {
            _objectUniformsDirty = true;
            return;
        }

        if (properties._matrices.TryGetValue("prowl_ObjectToWorld", out Vector.Float4x4 objectToWorld))
            _objectUniforms.prowl_ObjectToWorld = objectToWorld;
        if (properties._matrices.TryGetValue("prowl_WorldToObject", out Vector.Float4x4 worldToObject))
            _objectUniforms.prowl_WorldToObject = worldToObject;
        if (properties._matrices.TryGetValue("prowl_PrevObjectToWorld", out Vector.Float4x4 previousObjectToWorld))
            _objectUniforms.prowl_PrevObjectToWorld = previousObjectToWorld;
        if (properties._ints.TryGetValue("_ObjectID", out int objectId))
            _objectUniforms._ObjectID = objectId;
        _objectUniformsDirty = true;
    }

    private void SetRenderTarget(ID3D12GraphicsCommandList list, GraphicsFrameBuffer? framebuffer)
    {
        if (ReferenceEquals(_pendingRenderTarget, framebuffer))
            return;
        RestorePendingRenderTarget(list);
        _pendingRenderTarget = framebuffer;
    }

    private void BindCurrentRenderTargets(ID3D12GraphicsCommandList list)
    {
        D3D12FramebufferResource? framebuffer = GetPendingFramebuffer();
        if (framebuffer == null)
        {
            list.OMSetRenderTargets(_device.CurrentRtv, null);
            return;
        }

        TransitionFramebuffer(list, framebuffer, ResourceStates.RenderTarget);
        list.OMSetRenderTargets(
            framebuffer.Rtvs,
            framebuffer.DepthFormat == Format.Unknown ? null : framebuffer.Dsv);
        if (_currentRaster.StencilEnabled)
            list.OMSetStencilRef(checked((uint)_currentRaster.StencilRef));
    }

    private D3D12RenderTargetFormats GetCurrentTargetFormats()
    {
        D3D12FramebufferResource? framebuffer = GetPendingFramebuffer();
        return framebuffer == null
            ? new D3D12RenderTargetFormats(new D3D12ColorAttachmentFormats(Format.R8G8B8A8_UNorm), Format.Unknown)
            : new D3D12RenderTargetFormats(framebuffer.ColorFormats, framebuffer.DepthFormat);
    }

    private D3D12FramebufferResource? GetPendingFramebuffer()
    {
        if (_pendingRenderTarget == null)
            return null;
        if (_pendingRenderTarget.Handle == 0 ||
            !_device.Framebuffers.TryGetValue(_pendingRenderTarget.Handle, out D3D12FramebufferResource? framebuffer))
        {
            throw new InvalidOperationException("D3D12 custom framebuffer is not available.");
        }
        return framebuffer;
    }

    private void RestorePendingRenderTarget(ID3D12GraphicsCommandList list)
    {
        D3D12FramebufferResource? framebuffer = GetPendingFramebuffer();
        if (framebuffer != null)
            TransitionFramebuffer(list, framebuffer, ResourceStates.PixelShaderResource);
    }

    private void TransitionFramebuffer(
        ID3D12GraphicsCommandList list,
        D3D12FramebufferResource framebuffer,
        ResourceStates after)
    {
        for (int i = 0; i < framebuffer.ColorHandles.Length; i++)
        {
            if (!_device.Textures.TryGetValue(framebuffer.ColorHandles[i], out D3D12TextureResource? texture) || texture.Resource == null)
                throw new InvalidOperationException("D3D12 framebuffer color attachment is not available.");
            if (framebuffer.SubresourceOnlyByAttachment[i])
            {
                if (texture.State != ResourceStates.PixelShaderResource)
                    throw new InvalidOperationException("D3D12 cubemap framebuffer attachment requires pixel-shader resource state.");
                ResourceStates before = after == ResourceStates.RenderTarget
                    ? ResourceStates.PixelShaderResource
                    : ResourceStates.RenderTarget;
                list.ResourceBarrierTransition(texture.Resource, before, after, framebuffer.ColorSubresources[i]);
                continue;
            }
            if (texture.State == after)
                continue;

            list.ResourceBarrierTransition(texture.Resource, texture.State, after);
            texture.State = after;
        }
    }

    private D3D12TextureResource GetTextureResource(string name)
    {
        if (!_textures.TryGetValue(name, out GraphicsTexture? texture) || texture.Handle == 0)
            throw new InvalidOperationException($"D3D12 draw requires texture '{name}'.");
        if (!_device.Textures.TryGetValue(texture.Handle, out D3D12TextureResource? resource) ||
            resource.Resource == null || !resource.HasSrvDescriptor || !resource.HasSamplerDescriptor)
            throw new InvalidOperationException($"D3D12 texture '{name}' is not sample-ready.");
        if (resource.Type == TextureType.TextureCubeMap && resource.CubeInitializedFaces != 0b0011_1111)
            throw new InvalidOperationException($"D3D12 cubemap '{name}' is missing one or more faces.");
        return resource;
    }

    private static string FindTextureNameForSampler(ShaderBindingSlot[] textures, int samplerSlot)
    {
        for (int i = 0; i < textures.Length; i++)
        {
            if (textures[i].Slot == samplerSlot)
                return textures[i].Name;
        }
        throw new NotSupportedException($"D3D12 sampler slot s{samplerSlot} has no texture at matching t{samplerSlot}.");
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

    private void TouchPipelineKey(GraphicsVertexArray? vao, Topology topology, bool index32Bit)
    {
        if (_currentShader == null || vao == null)
            return;

        _ = new GraphicsPipelineKey(_currentShader, vao.Handle, topology, in _currentRaster, index32Bit);
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
            case CommandOpcode.SetProperties:
            case CommandOpcode.ClearProperties:
            case CommandOpcode.ClearGlobalTexture:
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
            case CommandOpcode.SetGlobalBuffer:
                _ = objects[ReadU16(stream, ref pos)];
                _ = objects[ReadU16(stream, ref pos)];
                _ = ReadU32(stream, ref pos);
                break;
            case CommandOpcode.ClearAllGlobals:
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

internal readonly record struct D3D12TransientUniformBinding(ID3D12Resource Resource, ulong Offset);
