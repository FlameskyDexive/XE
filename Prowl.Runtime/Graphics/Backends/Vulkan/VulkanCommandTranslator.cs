// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Silk.NET.Vulkan;

using Prowl.Runtime.RHI.Shaders;
using Prowl.Runtime.RHI;

using CommandBuffer = Prowl.Runtime.CommandBuffer;
using VkCommandBuffer = Silk.NET.Vulkan.CommandBuffer;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace Prowl.Runtime.Backends.Vulkan;

/// <summary>
/// Translates engine <see cref="CommandBuffer"/> recordings into Vulkan commands.
/// Clears, viewport/scissor, and resource create/dispose are implemented; draws stay stubs.
/// </summary>
internal sealed unsafe class VulkanCommandTranslator
{
    private const ulong TransientUniformArenaSize = 64 * 1024;

    private readonly VulkanGraphicsDevice _device;
    private readonly HashSet<CommandOpcode> _warnedOpcodes = new();
    private bool _warnedDrawNoPso;
    private bool _inRenderPass;

    private GraphicsFrameBuffer? _pendingRenderTarget;
    private GraphicsFrameBuffer? _pendingReadTarget;
    private ShaderVariant? _currentShader;
    private RasterizerState _currentRaster = new();
    private readonly Dictionary<string, GraphicsBuffer> _uniformBuffers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VkTransientUniformBinding> _transientUniformBuffers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GraphicsTexture> _textures = new(StringComparer.Ordinal);
    private Rendering.ObjectUniformsData _objectUniforms;
    private bool _objectUniformsDirty;
    private Rendering.PropertyState? _materialProperties;
    private Resources.Shader? _materialShader;
    private bool _materialUniformsDirty;
    private DescriptorSet _currentDescriptorSet;
    private bool _descriptorDirty;
    private List<DescriptorSet>? _submissionDescriptorSets;
    private List<Sampler>? _submissionRetiredSamplers;
    private List<VkBufferResource>? _submissionTransientBuffers;
    private VkBufferResource? _transientUniformArena;
    private ulong _transientUniformArenaOffset;

    public VulkanCommandTranslator(VulkanGraphicsDevice device)
    {
        _device = device;
    }

    public void Translate(
        CommandBuffer commandBuffer,
        VkCommandBuffer vkCmd,
        List<DescriptorSet> descriptorSets,
        List<Sampler> retiredSamplers,
        List<VkBufferResource> transientBuffers)
    {
        var stream = commandBuffer._stream.AsSpan(0, commandBuffer._streamPos);
        var objects = commandBuffer._objects;
        var store = commandBuffer._store;
        int pos = 0;
        _inRenderPass = false;
        _uniformBuffers.Clear();
        _transientUniformBuffers.Clear();
        _textures.Clear();
        _objectUniforms = Rendering.ObjectUniformsData.Identity;
        _objectUniformsDirty = true;
        _materialProperties = null;
        _materialShader = null;
        _materialUniformsDirty = true;
        GraphicsBuffer? globalUniforms = Rendering.GlobalUniforms.GetBuffer();
        if (globalUniforms is { Handle: not 0 })
            _uniformBuffers["GlobalUniforms"] = globalUniforms;
        _currentDescriptorSet = default;
        _descriptorDirty = true;
        _submissionDescriptorSets = descriptorSets;
        _submissionRetiredSamplers = retiredSamplers;
        _submissionTransientBuffers = transientBuffers;
        _transientUniformArena = null;
        _transientUniformArenaOffset = 0;

        while (pos < stream.Length)
        {
            CommandOpcode op = ReadOpcode(stream, ref pos);
            switch (op)
            {
                case CommandOpcode.SetRenderTarget:
                {
                    EndRenderPassIfNeeded(vkCmd);
                    _pendingRenderTarget = (GraphicsFrameBuffer?)objects[ReadU16(stream, ref pos)];
                    _pendingReadTarget = _pendingRenderTarget;
                    break;
                }
                case CommandOpcode.SetRenderTargets:
                {
                    EndRenderPassIfNeeded(vkCmd);
                    _pendingRenderTarget = (GraphicsFrameBuffer?)objects[ReadU16(stream, ref pos)];
                    _pendingReadTarget = (GraphicsFrameBuffer?)objects[ReadU16(stream, ref pos)];
                    break;
                }
                case CommandOpcode.SetViewport:
                {
                    int x = ReadI32(stream, ref pos);
                    int y = ReadI32(stream, ref pos);
                    uint w = ReadU32(stream, ref pos);
                    uint h = ReadU32(stream, ref pos);
                    var vp = new Viewport
                    {
                        X = x,
                        Y = y,
                        Width = w,
                        Height = h,
                        MinDepth = 0f,
                        MaxDepth = 1f,
                    };
                    _device.Vk.CmdSetViewport(vkCmd, 0, 1, &vp);
                    break;
                }
                case CommandOpcode.SetScissor:
                {
                    int x = ReadI32(stream, ref pos);
                    int y = ReadI32(stream, ref pos);
                    uint w = ReadU32(stream, ref pos);
                    uint h = ReadU32(stream, ref pos);
                    var scissor = new Rect2D
                    {
                        Offset = new Offset2D(x, y),
                        Extent = new Extent2D(w, h),
                    };
                    _device.Vk.CmdSetScissor(vkCmd, 0, 1, &scissor);
                    break;
                }
                case CommandOpcode.DisableScissor:
                {
                    var scissor = new Rect2D
                    {
                        Offset = new Offset2D(0, 0),
                        Extent = new Extent2D(16384, 16384),
                    };
                    _device.Vk.CmdSetScissor(vkCmd, 0, 1, &scissor);
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
                    DoClear(vkCmd, flags, r, g, b, a, depth, stencil);
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
                    DoBlit(vkCmd, srcX, srcY, srcWidth, srcHeight, dstX, dstY, dstWidth, dstHeight, mask, filter);
                    break;
                }
                case CommandOpcode.SetShader:
                {
                    _currentShader = objects[ReadU16(stream, ref pos)] as ShaderVariant;
                    _descriptorDirty = true;
                    if (_currentShader?.Bytecode?.Format != ShaderBytecodeFormat.SpirV)
                        WarnOnce(CommandOpcode.SetShader, "Vulkan shader bind skipped: expected a SPIR-V ShaderVariant.");
                    else
                    {
                        _device.GetOrCreateShaderLayout(_currentShader);
                        _device.GetOrCreateShaderModules(_currentShader);
                    }
                    break;
                }
                case CommandOpcode.SetInstanceProperties:
                {
                    var properties = (Rendering.PropertyState?)objects[ReadU16(stream, ref pos)];
                    ApplyObjectProperties(properties);
                    break;
                }
                case CommandOpcode.SetProperties:
                {
                    _materialProperties = (Rendering.PropertyState?)objects[ReadU16(stream, ref pos)];
                    _materialShader = null;
                    _materialUniformsDirty = true;
                    _descriptorDirty = true;
                    ApplyMaterialTextureBindings();
                    break;
                }
                case CommandOpcode.SetMaterialProperties:
                {
                    _materialProperties = (Rendering.PropertyState?)objects[ReadU16(stream, ref pos)];
                    _materialShader = (Resources.Shader?)objects[ReadU16(stream, ref pos)];
                    _materialUniformsDirty = true;
                    _descriptorDirty = true;
                    ApplyMaterialTextureBindings();
                    break;
                }
                case CommandOpcode.ClearProperties:
                {
                    _materialProperties = null;
                    _materialShader = null;
                    _materialUniformsDirty = true;
                    _descriptorDirty = true;
                    _textures.Remove("_MainTex");
                    break;
                }
                case CommandOpcode.ClearInstanceProperties:
                {
                    _objectUniforms = Rendering.ObjectUniformsData.Identity;
                    _objectUniformsDirty = true;
                    _descriptorDirty = true;
                    break;
                }
                case CommandOpcode.SetUniformBuffer:
                {
                    string? name = (string?)objects[ReadU16(stream, ref pos)];
                    GraphicsBuffer? buffer = (GraphicsBuffer?)objects[ReadU16(stream, ref pos)];
                    _ = ReadU32(stream, ref pos);
                    if (name != null && buffer != null)
                        _uniformBuffers[name] = buffer;
                    _descriptorDirty = true;
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
                        _descriptorDirty = true;
                    }
                    else
                    {
                        WarnOnce(CommandOpcode.SetUniformInt, $"VulkanCommandTranslator: uniform '{name}' is not packable yet.");
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
                        WarnOnce(CommandOpcode.SetUniformMatrix, $"VulkanCommandTranslator: uniform '{name}' is not packable yet.");
                        break;
                    }
                    _objectUniformsDirty = true;
                    _descriptorDirty = true;
                    break;
                }
                case CommandOpcode.SetUniformTexture:
                {
                    string? name = (string?)objects[ReadU16(stream, ref pos)];
                    GraphicsTexture? texture = objects[ReadU16(stream, ref pos)] as GraphicsTexture;
                    if (name != null && texture != null)
                        _textures[name] = texture;
                    _descriptorDirty = true;
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
                    DrawIndexed(vkCmd, vao, topology, indexCount, startIndex, baseVertex, index32Bit);
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
                    DrawIndexedInstanced(vkCmd, vao, topology, indexCount, instanceCount, startIndex, baseVertex, index32Bit);
                    break;
                }
                case CommandOpcode.DrawArrays:
                {
                    var vao = (GraphicsVertexArray?)objects[ReadU16(stream, ref pos)];
                    Topology topology = (Topology)ReadU8(stream, ref pos);
                    int first = ReadI32(stream, ref pos);
                    uint count = ReadU32(stream, ref pos);
                    DrawArrays(vkCmd, vao, topology, first, count);
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
                    if (buf.Handle != 0 && _device.Buffers.Remove(buf.Handle, out VkBufferResource? res))
                    {
                        _device.DestroyBuffer(res);
                        buf.Handle = 0;
                    }
                    break;
                }
                case CommandOpcode.UpdateBuffer:
                {
                    var buf = (GraphicsBuffer?)objects[ReadU16(stream, ref pos)];
                    uint dstOffset = ReadU32(stream, ref pos);
                    ReadOnlySpan<byte> blob = ReadBlob<byte>(stream, ref pos, store);
                    if (buf != null && buf.Handle != 0 &&
                        _device.Buffers.TryGetValue(buf.Handle, out VkBufferResource? res))
                    {
                        UpdateBuffer(res, dstOffset, blob);
                    }
                    break;
                }
                case CommandOpcode.CreateTexture:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    uint handle = _device.AllocateHandle();
                    tex.Handle = handle;
                    _device.Images[handle] = new VkImageResource
                    {
                        EngineFormat = tex.ImageFormat,
                        Format = VulkanFormats.ToVkFormat(tex.ImageFormat),
                        Type = tex.Type,
                    };
                    break;
                }
                case CommandOpcode.DisposeTexture:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    if (tex.Handle != 0 && _device.Images.Remove(tex.Handle, out VkImageResource? res))
                    {
                        _device.DestroyImage(res);
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
                    _ = ReadI32(stream, ref pos);
                    ReadOnlySpan<byte> data = ReadBlob<byte>(stream, ref pos, store);
                    AllocateTexture2D(vkCmd, tex, mip, w, h, data);
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
                    AllocateTexture3D(vkCmd, tex, mip, w, h, d, data);
                    break;
                }
                case CommandOpcode.AllocateTextureCubeFace:
                {
                    var tex = (GraphicsTexture)objects[ReadU16(stream, ref pos)]!;
                    int face = ReadI32(stream, ref pos);
                    int mip = ReadI32(stream, ref pos);
                    uint size = ReadU32(stream, ref pos);
                    ReadOnlySpan<byte> data = ReadBlob<byte>(stream, ref pos, store);
                    AllocateTextureCubeFace(vkCmd, tex, face, mip, size, data);
                    break;
                }
                case CommandOpcode.GenerateMipmap:
                {
                    EndRenderPassIfNeeded(vkCmd);
                    GraphicsTexture? texture = objects[ReadU16(stream, ref pos)] as GraphicsTexture;
                    if (texture != null)
                        GenerateMipmaps(vkCmd, texture);
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
                    DisposeFramebuffer(framebuffer);
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
                case CommandOpcode.BeginSample:
                {
                    _ = objects[ReadU16(stream, ref pos)];
                    break;
                }
                case CommandOpcode.EndSample:
                    break;
                default:
                    SkipOpcode(op, stream, ref pos, objects, store);
                    break;
            }
        }

        EndRenderPassIfNeeded(vkCmd);
        _submissionDescriptorSets = null;
    }

    private void DoClear(
        VkCommandBuffer vkCmd,
        ClearFlags flags,
        float r, float g, float b, float a,
        float depth,
        int stencil)
    {
        if (_pendingRenderTarget != null)
        {
            VkFramebufferResource resource = GetPendingFramebuffer();
            EnsureDrawRenderPass(vkCmd);
            ClearAttachment* clearAttachments = stackalloc ClearAttachment[resource.ColorFormats.Count + (resource.DepthFormat == Format.Undefined ? 0 : 1)];
            int clearCount = 0;
            if ((flags & ClearFlags.Color) != 0)
            {
                for (int i = 0; i < resource.ColorFormats.Count; i++)
                {
                    clearAttachments[clearCount] = new ClearAttachment
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        ColorAttachment = checked((uint)i),
                        ClearValue = new ClearValue { Color = new ClearColorValue(r, g, b, a) },
                    };
                    clearCount++;
                }
            }
            ImageAspectFlags depthAspects = 0;
            if ((flags & ClearFlags.Depth) != 0)
                depthAspects |= ImageAspectFlags.DepthBit;
            if ((flags & ClearFlags.Stencil) != 0)
                depthAspects |= ImageAspectFlags.StencilBit;
            if (depthAspects != 0 && resource.DepthFormat != Format.Undefined)
            {
                clearAttachments[clearCount] = new ClearAttachment
                {
                    AspectMask = depthAspects,
                    ClearValue = new ClearValue
                    {
                        DepthStencil = new ClearDepthStencilValue(depth, checked((uint)stencil)),
                    },
                };
                clearCount++;
            }
            if (clearCount > 0)
            {
                var clearRect = new ClearRect
                {
                    Rect = new Rect2D(new Offset2D(0, 0), new Extent2D(resource.Width, resource.Height)),
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                };
                _device.Vk.CmdClearAttachments(vkCmd, checked((uint)clearCount), clearAttachments, 1, &clearRect);
            }
            return;
        }
        if (!_device.HasSwapchain)
            return;

        EnsureSwapchainRenderPass(vkCmd, flags, r, g, b, a, depth, stencil);
    }

    private void DoBlit(
        VkCommandBuffer vkCmd,
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
        EndRenderPassIfNeeded(vkCmd);
        if (mask == 0)
            return;

        VkFramebufferResource source = GetFramebuffer(_pendingReadTarget, "read");
        VkFramebufferResource destination = GetFramebuffer(_pendingRenderTarget, "draw");
        if (mask == ClearFlags.Depth)
        {
            DoDepthBlit(vkCmd, source, destination, srcX, srcY, srcWidth, srcHeight, dstX, dstY, dstWidth, dstHeight, filter);
            return;
        }
        if (mask != ClearFlags.Color)
            throw new NotSupportedException("Vulkan framebuffer blits currently support color-only or depth-only masks.");

        if (source.ColorHandles.Length != 1 || destination.ColorHandles.Length != 1)
            throw new NotSupportedException("Vulkan framebuffer blits currently require one color attachment on each framebuffer.");
        ValidateBlitRect(srcX, srcY, srcWidth, srcHeight, source.Width, source.Height, "source");
        ValidateBlitRect(dstX, dstY, dstWidth, dstHeight, destination.Width, destination.Height, "destination");

        VkImageResource sourceImage = _device.Images[source.ColorHandles[0]];
        VkImageResource destinationImage = _device.Images[destination.ColorHandles[0]];
        uint sourceMip = source.ColorMipLevels[0];
        uint destinationMip = destination.ColorMipLevels[0];
        uint sourceLayer = source.ColorArrayLayers[0];
        uint destinationLayer = destination.ColorArrayLayers[0];
        if (sourceImage.Image.Handle == destinationImage.Image.Handle &&
            sourceMip == destinationMip && sourceLayer == destinationLayer)
            throw new InvalidOperationException("Vulkan framebuffer blit source and destination subresources must differ.");
        if (sourceImage.Layout != ImageLayout.ShaderReadOnlyOptimal ||
            destinationImage.Layout != ImageLayout.ShaderReadOnlyOptimal)
            throw new InvalidOperationException("Vulkan framebuffer blit attachments must be in shader-read layout before transfer.");

        ImageMemoryBarrier* barriers = stackalloc ImageMemoryBarrier[2];
        barriers[0] = CreateMipBarrier(
            sourceImage.Image,
            sourceMip,
            sourceLayer,
            ImageLayout.ShaderReadOnlyOptimal,
            ImageLayout.TransferSrcOptimal,
            AccessFlags.ShaderReadBit | AccessFlags.ColorAttachmentWriteBit,
            AccessFlags.TransferReadBit);
        barriers[1] = CreateMipBarrier(
            destinationImage.Image,
            destinationMip,
            destinationLayer,
            ImageLayout.ShaderReadOnlyOptimal,
            ImageLayout.TransferDstOptimal,
            AccessFlags.ShaderReadBit | AccessFlags.ColorAttachmentWriteBit,
            AccessFlags.TransferWriteBit);
        _device.Vk.CmdPipelineBarrier(
            vkCmd,
            PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.TransferBit,
            0,
            0,
            null,
            0,
            null,
            2,
            barriers);

        var blit = new ImageBlit
        {
            SrcSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = sourceMip,
                BaseArrayLayer = sourceLayer,
                LayerCount = 1,
            },
            DstSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = destinationMip,
                BaseArrayLayer = destinationLayer,
                LayerCount = 1,
            },
        };
        blit.SrcOffsets[0] = new Offset3D(srcX, srcY, 0);
        blit.SrcOffsets[1] = new Offset3D(srcWidth, srcHeight, 1);
        blit.DstOffsets[0] = new Offset3D(dstX, dstY, 0);
        blit.DstOffsets[1] = new Offset3D(dstWidth, dstHeight, 1);
        _device.Vk.CmdBlitImage(
            vkCmd,
            sourceImage.Image,
            ImageLayout.TransferSrcOptimal,
            destinationImage.Image,
            ImageLayout.TransferDstOptimal,
            1,
            &blit,
            filter == BlitFilter.Linear ? Filter.Linear : Filter.Nearest);

        barriers[0] = CreateMipBarrier(
            sourceImage.Image,
            sourceMip,
            sourceLayer,
            ImageLayout.TransferSrcOptimal,
            ImageLayout.ShaderReadOnlyOptimal,
            AccessFlags.TransferReadBit,
            AccessFlags.ShaderReadBit);
        barriers[1] = CreateMipBarrier(
            destinationImage.Image,
            destinationMip,
            destinationLayer,
            ImageLayout.TransferDstOptimal,
            ImageLayout.ShaderReadOnlyOptimal,
            AccessFlags.TransferWriteBit,
            AccessFlags.ShaderReadBit);
        _device.Vk.CmdPipelineBarrier(
            vkCmd,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.FragmentShaderBit,
            0,
            0,
            null,
            0,
            null,
            2,
            barriers);
    }

    private void DoDepthBlit(
        VkCommandBuffer vkCmd,
        VkFramebufferResource source,
        VkFramebufferResource destination,
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
            throw new NotSupportedException("Vulkan depth framebuffer blits require nearest filtering.");
        if (source.DepthHandle == 0 || destination.DepthHandle == 0)
            throw new InvalidOperationException("Vulkan depth framebuffer blits require depth attachments on both framebuffers.");
        if (source.DepthFormat != destination.DepthFormat)
            throw new NotSupportedException("Vulkan depth framebuffer blits require matching depth formats.");
        ValidateBlitRect(srcX, srcY, srcWidth, srcHeight, source.Width, source.Height, "source");
        ValidateBlitRect(dstX, dstY, dstWidth, dstHeight, destination.Width, destination.Height, "destination");

        VkImageResource sourceImage = _device.Images[source.DepthHandle];
        VkImageResource destinationImage = _device.Images[destination.DepthHandle];
        if (sourceImage.Image.Handle == destinationImage.Image.Handle)
            throw new InvalidOperationException("Vulkan depth framebuffer blit source and destination textures must differ.");
        if (sourceImage.Layout != ImageLayout.DepthStencilAttachmentOptimal ||
            destinationImage.Layout != ImageLayout.DepthStencilAttachmentOptimal)
            throw new InvalidOperationException("Vulkan depth framebuffer blit attachments must be in depth-attachment layout before transfer.");

        ImageMemoryBarrier* barriers = stackalloc ImageMemoryBarrier[2];
        barriers[0] = CreateMipBarrier(
            sourceImage.Image,
            0,
            0,
            ImageLayout.DepthStencilAttachmentOptimal,
            ImageLayout.TransferSrcOptimal,
            AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit,
            AccessFlags.TransferReadBit,
            ImageAspectFlags.DepthBit);
        barriers[1] = CreateMipBarrier(
            destinationImage.Image,
            0,
            0,
            ImageLayout.DepthStencilAttachmentOptimal,
            ImageLayout.TransferDstOptimal,
            AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit,
            AccessFlags.TransferWriteBit,
            ImageAspectFlags.DepthBit);
        _device.Vk.CmdPipelineBarrier(
            vkCmd,
            PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
            PipelineStageFlags.TransferBit,
            0,
            0,
            null,
            0,
            null,
            2,
            barriers);

        var blit = new ImageBlit
        {
            SrcSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.DepthBit,
                LayerCount = 1,
            },
            DstSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.DepthBit,
                LayerCount = 1,
            },
        };
        blit.SrcOffsets[0] = new Offset3D(srcX, srcY, 0);
        blit.SrcOffsets[1] = new Offset3D(srcWidth, srcHeight, 1);
        blit.DstOffsets[0] = new Offset3D(dstX, dstY, 0);
        blit.DstOffsets[1] = new Offset3D(dstWidth, dstHeight, 1);
        _device.Vk.CmdBlitImage(
            vkCmd,
            sourceImage.Image,
            ImageLayout.TransferSrcOptimal,
            destinationImage.Image,
            ImageLayout.TransferDstOptimal,
            1,
            &blit,
            Filter.Nearest);

        barriers[0] = CreateMipBarrier(
            sourceImage.Image,
            0,
            0,
            ImageLayout.TransferSrcOptimal,
            ImageLayout.DepthStencilAttachmentOptimal,
            AccessFlags.TransferReadBit,
            AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit,
            ImageAspectFlags.DepthBit);
        barriers[1] = CreateMipBarrier(
            destinationImage.Image,
            0,
            0,
            ImageLayout.TransferDstOptimal,
            ImageLayout.DepthStencilAttachmentOptimal,
            AccessFlags.TransferWriteBit,
            AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit,
            ImageAspectFlags.DepthBit);
        _device.Vk.CmdPipelineBarrier(
            vkCmd,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
            0,
            0,
            null,
            0,
            null,
            2,
            barriers);
    }

    private VkFramebufferResource GetFramebuffer(GraphicsFrameBuffer? framebuffer, string role)
    {
        if (framebuffer == null || framebuffer.Handle == 0 ||
            !_device.Framebuffers.TryGetValue(framebuffer.Handle, out VkFramebufferResource? resource))
            throw new NotSupportedException($"Vulkan framebuffer blits currently require a custom {role} framebuffer.");
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
            x0 == x1 || y0 == y1)
            throw new ArgumentOutOfRangeException(role, $"Vulkan {role} blit rectangle must be non-empty and within the framebuffer extent.");
    }

    private void EnsureSwapchainRenderPass(
        VkCommandBuffer vkCmd,
        ClearFlags flags,
        float r, float g, float b, float a,
        float depth,
        int stencil)
    {
        EndRenderPassIfNeeded(vkCmd);

        ClearValue clearValue = default;
        clearValue.Color = new ClearColorValue(r, g, b, a);
        // Depth attachment is not part of the swapchain render pass yet.
        _ = flags;
        _ = depth;
        _ = stencil;

        Extent2D extent = _device.SwapchainExtent;
        var begin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _device.SwapchainRenderPass,
            Framebuffer = _device.CurrentSwapchainFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), extent),
            ClearValueCount = 1,
            PClearValues = &clearValue,
        };
        _device.Vk.CmdBeginRenderPass(vkCmd, &begin, SubpassContents.Inline);
        _inRenderPass = true;

        // Clear was applied via load-op; end so subsequent resource ops aren't inside the pass.
        EndRenderPassIfNeeded(vkCmd);
    }

    private void EndRenderPassIfNeeded(VkCommandBuffer vkCmd)
    {
        if (!_inRenderPass)
            return;
        _device.Vk.CmdEndRenderPass(vkCmd);
        _inRenderPass = false;
    }

    private void EnsureDrawRenderPass(VkCommandBuffer vkCmd)
    {
        if (_inRenderPass)
            return;
        RenderPass renderPass;
        Framebuffer framebuffer;
        Extent2D extent;
        if (_pendingRenderTarget != null)
        {
            if (_pendingRenderTarget.Handle == 0 ||
                !_device.Framebuffers.TryGetValue(_pendingRenderTarget.Handle, out VkFramebufferResource? resource))
                throw new InvalidOperationException("Vulkan custom framebuffer is not available.");
            renderPass = resource.RenderPass;
            framebuffer = resource.Framebuffer;
            extent = new Extent2D(resource.Width, resource.Height);
        }
        else
        {
            renderPass = _device.CurrentRenderPass;
            framebuffer = _device.CurrentFramebuffer;
            extent = _device.CurrentRenderExtent;
        }
        var begin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = renderPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), extent),
        };
        _device.Vk.CmdBeginRenderPass(vkCmd, &begin, SubpassContents.Inline);
        _inRenderPass = true;
    }

    private VkRenderTargetFormats GetCurrentTargetFormats()
    {
        if (_pendingRenderTarget == null)
            return new VkRenderTargetFormats(new VkColorAttachmentFormats(_device.CurrentColorFormat), Format.Undefined);
        VkFramebufferResource resource = GetPendingFramebuffer();
        return new VkRenderTargetFormats(resource.ColorFormats, resource.DepthFormat);
    }

    private VkFramebufferResource GetPendingFramebuffer()
    {
        if (_pendingRenderTarget == null || _pendingRenderTarget.Handle == 0 ||
            !_device.Framebuffers.TryGetValue(_pendingRenderTarget.Handle, out VkFramebufferResource? resource))
            throw new InvalidOperationException("Vulkan custom framebuffer is not available.");
        return resource;
    }

    private void CreateBuffer(GraphicsBuffer buf, bool dynamic, ReadOnlySpan<byte> data)
    {
        uint handle = _device.AllocateHandle();
        ulong size = data.Length > 0 ? (ulong)data.Length : Math.Max(1u, buf.SizeInBytes);

        BufferUsageFlags usage = buf.OriginalType switch
        {
            BufferType.VertexBuffer => BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
            BufferType.ElementsBuffer => BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
            BufferType.UniformBuffer => BufferUsageFlags.UniformBufferBit | BufferUsageFlags.TransferDstBit,
            BufferType.StructuredBuffer => BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
            _ => BufferUsageFlags.TransferDstBit,
        };

        MemoryPropertyFlags mem = dynamic
            ? MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
            : MemoryPropertyFlags.DeviceLocalBit;

        _device.CreateBuffer(size, usage, mem, out VkBuffer buffer, out DeviceMemory memory);

        if (data.Length > 0)
        {
            if (dynamic)
            {
                void* mapped;
                VulkanGraphicsDevice.Check(
                    _device.Vk.MapMemory(_device.Device, memory, 0, size, 0, &mapped),
                    "vkMapMemory");
                try
                {
                    fixed (byte* src = data)
                        System.Buffer.MemoryCopy(src, mapped, data.Length, data.Length);
                }
                finally
                {
                    _device.Vk.UnmapMemory(_device.Device, memory);
                }
            }
            else
            {
                // Staging upload
                _device.CreateBuffer(
                    size,
                    BufferUsageFlags.TransferSrcBit,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                    out VkBuffer staging,
                    out DeviceMemory stagingMem);

                void* mapped;
                VulkanGraphicsDevice.Check(
                    _device.Vk.MapMemory(_device.Device, stagingMem, 0, size, 0, &mapped),
                    "vkMapMemory");
                try
                {
                    fixed (byte* src = data)
                        System.Buffer.MemoryCopy(src, mapped, data.Length, data.Length);
                }
                finally
                {
                    _device.Vk.UnmapMemory(_device.Device, stagingMem);
                }

                // One-shot copy on a transient command buffer would be ideal; for MVP map device-local
                // via host-visible fallback if available. Re-create as host-visible when staging copy
                // can't run mid-translate safely without nested submits.
                _device.Vk.DestroyBuffer(_device.Device, buffer, null);
                _device.Vk.FreeMemory(_device.Device, memory, null);
                buffer = staging;
                memory = stagingMem;
            }
        }

        buf.Handle = handle;
        buf.SizeInBytes = (uint)size;
        _device.Buffers[handle] = new VkBufferResource
        {
            Buffer = buffer,
            Memory = memory,
            Size = size,
            Type = buf.OriginalType,
            Dynamic = dynamic,
        };
    }

    private void CreateFramebuffer(GraphicsFrameBuffer framebuffer)
    {
        GraphicsFrameBuffer.Attachment[] attachments = framebuffer.Attachments;
        if (attachments.Length is < 1 or > 9)
            throw new NotSupportedException("Vulkan custom framebuffers support one through eight color attachments and one depth attachment.");

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
            throw new NotSupportedException("Vulkan custom framebuffers require one through eight color attachments and at most one depth attachment.");

        var attachmentViews = new ImageView[colorCount + depthCount];
        var colorHandles = new uint[colorCount];
        var colorMipLevels = new uint[colorCount];
        var colorArrayLayers = new uint[colorCount];
        var colorFormats = new Format[colorCount];
        var descriptions = new AttachmentDescription[colorCount + depthCount];
        var colorReferences = new AttachmentReference[colorCount];
        uint depthHandle = 0;
        Format depthFormat = Format.Undefined;
        RenderPass renderPass = default;
        try
        {
            int colorIndex = 0;
            for (int i = 0; i < attachments.Length; i++)
            {
                GraphicsFrameBuffer.Attachment attachment = attachments[i];
                GraphicsTexture texture = attachment.Texture;
                if (texture.Handle == 0 || !_device.Images.TryGetValue(texture.Handle, out VkImageResource? image) ||
                    image.View.Handle == 0)
                    throw new InvalidOperationException("Vulkan framebuffer attachment is not ready.");
                bool isDepth = attachment.IsDepth;
                if (isDepth != VulkanFormats.IsDepth(image.EngineFormat))
                    throw new ArgumentException("Vulkan framebuffer attachment depth flag must match the texture format.");
                ImageLayout expectedLayout = isDepth
                    ? ImageLayout.DepthStencilAttachmentOptimal
                    : ImageLayout.ShaderReadOnlyOptimal;
                if (image.Layout != expectedLayout)
                    throw new InvalidOperationException("Vulkan framebuffer attachment layout is not ready.");
                if (attachment.MipLevel < 0 || (uint)attachment.MipLevel >= image.MipLevels)
                    throw new ArgumentOutOfRangeException(nameof(attachment.MipLevel));
                if (isDepth && attachment.IsCubeFace)
                    throw new NotSupportedException("Vulkan depth cubemap framebuffer attachments are not supported yet.");

                uint arrayLayer = 0;
                if (!isDepth && attachment.IsCubeFace)
                {
                    if (image.Type != TextureType.TextureCubeMap || (uint)attachment.CubeFace >= 6)
                        throw new ArgumentException("Vulkan cubemap framebuffer attachment requires a valid face 0 through 5.");
                    arrayLayer = checked((uint)attachment.CubeFace);
                }
                else if (image.Type != TextureType.Texture2D)
                {
                    throw new NotSupportedException("Vulkan non-cubemap framebuffer attachments must be Texture2D resources.");
                }

                uint expectedWidth = Math.Max(1u, image.Width >> attachment.MipLevel);
                uint expectedHeight = Math.Max(1u, image.Height >> attachment.MipLevel);
                if (framebuffer.Width != expectedWidth || framebuffer.Height != expectedHeight)
                    throw new ArgumentException($"Vulkan framebuffer extent must match attachment mip extent {expectedWidth}x{expectedHeight}.");

                int nativeIndex = isDepth ? colorCount : colorIndex;
                var viewInfo = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = image.Image,
                    ViewType = ImageViewType.Type2D,
                    Format = image.Format,
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = VulkanFormats.AspectFor(image.EngineFormat),
                        BaseMipLevel = checked((uint)attachment.MipLevel),
                        LevelCount = 1,
                        BaseArrayLayer = arrayLayer,
                        LayerCount = 1,
                    },
                };
                VulkanGraphicsDevice.Check(
                    _device.Vk.CreateImageView(_device.Device, &viewInfo, null, out attachmentViews[nativeIndex]),
                    "vkCreateImageView(framebuffer attachment)");

                descriptions[nativeIndex] = new AttachmentDescription
                {
                    Format = image.Format,
                    Samples = SampleCountFlags.Count1Bit,
                    LoadOp = AttachmentLoadOp.Load,
                    StoreOp = AttachmentStoreOp.Store,
                    StencilLoadOp = isDepth ? AttachmentLoadOp.Load : AttachmentLoadOp.DontCare,
                    StencilStoreOp = AttachmentStoreOp.DontCare,
                    InitialLayout = expectedLayout,
                    FinalLayout = expectedLayout,
                };
                if (isDepth)
                {
                    depthHandle = texture.Handle;
                    depthFormat = image.Format;
                }
                else
                {
                    colorHandles[colorIndex] = texture.Handle;
                    colorMipLevels[colorIndex] = checked((uint)attachment.MipLevel);
                    colorArrayLayers[colorIndex] = arrayLayer;
                    colorFormats[colorIndex] = image.Format;
                    colorReferences[colorIndex] = new AttachmentReference
                    {
                        Attachment = checked((uint)nativeIndex),
                        Layout = ImageLayout.ColorAttachmentOptimal,
                    };
                    colorIndex++;
                }
            }

            fixed (AttachmentDescription* descriptionPointer = descriptions)
            fixed (AttachmentReference* referencePointer = colorReferences)
            fixed (ImageView* viewPointer = attachmentViews)
            {
                AttachmentReference depthReference = new()
                {
                    Attachment = checked((uint)colorCount),
                    Layout = ImageLayout.DepthStencilAttachmentOptimal,
                };
                var subpass = new SubpassDescription
                {
                    PipelineBindPoint = PipelineBindPoint.Graphics,
                    ColorAttachmentCount = checked((uint)colorCount),
                    PColorAttachments = referencePointer,
                    PDepthStencilAttachment = depthCount == 0 ? null : &depthReference,
                };
                var renderPassInfo = new RenderPassCreateInfo
                {
                    SType = StructureType.RenderPassCreateInfo,
                    AttachmentCount = checked((uint)(colorCount + depthCount)),
                    PAttachments = descriptionPointer,
                    SubpassCount = 1,
                    PSubpasses = &subpass,
                };
                VulkanGraphicsDevice.Check(_device.Vk.CreateRenderPass(_device.Device, &renderPassInfo, null, out renderPass), "vkCreateRenderPass(custom)");

                var framebufferInfo = new FramebufferCreateInfo
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = renderPass,
                    AttachmentCount = checked((uint)(colorCount + depthCount)),
                    PAttachments = viewPointer,
                    Width = framebuffer.Width,
                    Height = framebuffer.Height,
                    Layers = 1,
                };
                VulkanGraphicsDevice.Check(_device.Vk.CreateFramebuffer(_device.Device, &framebufferInfo, null, out Framebuffer nativeFramebuffer), "vkCreateFramebuffer(custom)");
                uint handle = _device.AllocateHandle();
                framebuffer.Handle = handle;
                _device.Framebuffers[handle] = new VkFramebufferResource
                {
                    Framebuffer = nativeFramebuffer,
                    RenderPass = renderPass,
                    Width = framebuffer.Width,
                    Height = framebuffer.Height,
                    ColorFormat = colorFormats[0],
                    ColorFormats = new VkColorAttachmentFormats(colorFormats),
                    DepthFormat = depthFormat,
                    ColorHandles = colorHandles,
                    ColorMipLevels = colorMipLevels,
                    ColorArrayLayers = colorArrayLayers,
                    AttachmentViews = attachmentViews,
                    DepthHandle = depthHandle,
                };
            }
        }
        catch
        {
            if (renderPass.Handle != 0)
                _device.Vk.DestroyRenderPass(_device.Device, renderPass, null);
            for (int i = 0; i < attachmentViews.Length; i++)
            {
                if (attachmentViews[i].Handle != 0)
                    _device.Vk.DestroyImageView(_device.Device, attachmentViews[i], null);
            }
            throw;
        }
    }

    private void DisposeFramebuffer(GraphicsFrameBuffer framebuffer)
    {
        if (framebuffer.Handle == 0)
            return;
        if (_device.Framebuffers.Remove(framebuffer.Handle, out VkFramebufferResource? resource))
            _device.DestroyFramebuffer(resource);
        framebuffer.Handle = 0;
    }

    private void UpdateBuffer(VkBufferResource res, uint dstOffset, ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || res.Memory.Handle == 0)
            return;

        void* mapped;
        Result map = _device.Vk.MapMemory(_device.Device, res.Memory, dstOffset, (ulong)data.Length, 0, &mapped);
        if (map != Result.Success)
        {
            WarnOnce(CommandOpcode.UpdateBuffer, "Vulkan UpdateBuffer skipped: buffer memory is not host-visible.");
            return;
        }

        try
        {
            fixed (byte* src = data)
                System.Buffer.MemoryCopy(src, mapped, data.Length, data.Length);
        }
        finally
        {
            _device.Vk.UnmapMemory(_device.Device, res.Memory);
        }
    }

    private void AllocateTexture2D(VkCommandBuffer vkCmd, GraphicsTexture tex, int mip, uint width, uint height, ReadOnlySpan<byte> data)
    {
        if (tex.Handle == 0 || !_device.Images.TryGetValue(tex.Handle, out VkImageResource? res))
            return;
        if (mip != 0)
            return;

        if (res.Image.Handle != 0)
            _device.DestroyImage(res);

        Format format = VulkanFormats.ToVkFormat(tex.ImageFormat);
        ImageUsageFlags usage = VulkanFormats.IsDepth(tex.ImageFormat)
            ? ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit
            : ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit |
              ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit;

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D(width, height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        VulkanGraphicsDevice.Check(
            _device.Vk.CreateImage(_device.Device, &imageInfo, null, out Image image),
            "vkCreateImage");
        _device.Vk.GetImageMemoryRequirements(_device.Device, image, out MemoryRequirements reqs);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = reqs.Size,
            MemoryTypeIndex = _device.FindMemoryType(reqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        VulkanGraphicsDevice.Check(
            _device.Vk.AllocateMemory(_device.Device, &alloc, null, out DeviceMemory memory),
            "vkAllocateMemory");
        VulkanGraphicsDevice.Check(
            _device.Vk.BindImageMemory(_device.Device, image, memory, 0),
            "vkBindImageMemory");

        ImageAspectFlags aspect = VulkanFormats.AspectFor(tex.ImageFormat);
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = aspect,
                LevelCount = 1,
                LayerCount = 1,
            },
        };
        VulkanGraphicsDevice.Check(
            _device.Vk.CreateImageView(_device.Device, &viewInfo, null, out ImageView view),
            "vkCreateImageView");

        res.Image = image;
        res.Memory = memory;
        res.View = view;
        res.Format = format;
        res.Width = width;
        res.Height = height;
        res.Layout = ImageLayout.Undefined;

        res.Sampler = CreateSampler(res);

        if (data.Length > 0)
        {
            _device.UploadTexture2D(
                res,
                data,
                width,
                height,
                VulkanFormats.BytesPerPixel(tex.ImageFormat));
        }
        else
        {
            bool isDepth = VulkanFormats.IsDepth(tex.ImageFormat);
            ImageLayout targetLayout = isDepth
                ? ImageLayout.DepthStencilAttachmentOptimal
                : ImageLayout.ShaderReadOnlyOptimal;
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = targetLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = res.Image,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = aspect,
                    LevelCount = 1,
                    LayerCount = 1,
                },
                DstAccessMask = isDepth
                    ? AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit
                    : AccessFlags.ShaderReadBit,
            };
            _device.Vk.CmdPipelineBarrier(
                vkCmd,
                PipelineStageFlags.TopOfPipeBit,
                isDepth
                    ? PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit
                    : PipelineStageFlags.FragmentShaderBit,
                0,
                0,
                null,
                0,
                null,
                1,
                &barrier);
            res.Layout = targetLayout;
        }
    }

    private void SetTextureWrap(GraphicsTexture texture, byte axis, TextureWrap wrap)
    {
        if (texture.Handle == 0 || !_device.Images.TryGetValue(texture.Handle, out VkImageResource? resource))
            return;

        bool changed;
        switch (axis)
        {
            case 0:
                changed = resource.WrapS != wrap;
                resource.WrapS = wrap;
                break;
            case 1:
                changed = resource.WrapT != wrap;
                resource.WrapT = wrap;
                break;
            case 2:
                changed = resource.WrapR != wrap;
                resource.WrapR = wrap;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(axis), axis, "Vulkan texture wrap axis must be 0, 1, or 2.");
        }

        if (changed)
            ReplaceSamplerIfAllocated(resource);
    }

    private void AllocateTexture3D(
        VkCommandBuffer vkCmd,
        GraphicsTexture tex,
        int mip,
        uint width,
        uint height,
        uint depth,
        ReadOnlySpan<byte> data)
    {
        if (tex.Handle == 0 || !_device.Images.TryGetValue(tex.Handle, out VkImageResource? resource))
            return;
        if (mip != 0)
            return;
        if (tex.Type != TextureType.Texture3D)
            throw new InvalidOperationException("Vulkan AllocateTexture3D requires a Texture3D resource.");

        if (resource.Image.Handle != 0)
            _device.DestroyImage(resource);

        Format format = VulkanFormats.ToVkFormat(tex.ImageFormat);
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type3D,
            Format = format,
            Extent = new Extent3D(width, height, depth),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        VulkanGraphicsDevice.Check(
            _device.Vk.CreateImage(_device.Device, &imageInfo, null, out Image image),
            "vkCreateImage");
        _device.Vk.GetImageMemoryRequirements(_device.Device, image, out MemoryRequirements requirements);
        var allocation = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = _device.FindMemoryType(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        VulkanGraphicsDevice.Check(
            _device.Vk.AllocateMemory(_device.Device, &allocation, null, out DeviceMemory memory),
            "vkAllocateMemory");
        VulkanGraphicsDevice.Check(
            _device.Vk.BindImageMemory(_device.Device, image, memory, 0),
            "vkBindImageMemory");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type3D,
            Format = format,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                LevelCount = 1,
                LayerCount = 1,
            },
        };
        VulkanGraphicsDevice.Check(
            _device.Vk.CreateImageView(_device.Device, &viewInfo, null, out ImageView view),
            "vkCreateImageView");

        resource.Image = image;
        resource.Memory = memory;
        resource.View = view;
        resource.Format = format;
        resource.Width = width;
        resource.Height = height;
        resource.Depth = depth;
        resource.Layout = ImageLayout.Undefined;
        resource.Sampler = CreateSampler(resource);

        if (data.Length > 0)
        {
            _device.UploadTexture3D(
                resource,
                data,
                width,
                height,
                depth,
                VulkanFormats.BytesPerPixel(tex.ImageFormat));
            return;
        }

        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = resource.Image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                LevelCount = 1,
                LayerCount = 1,
            },
            DstAccessMask = AccessFlags.ShaderReadBit,
        };
        _device.Vk.CmdPipelineBarrier(
            vkCmd,
            PipelineStageFlags.TopOfPipeBit,
            PipelineStageFlags.FragmentShaderBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &barrier);
        resource.Layout = ImageLayout.ShaderReadOnlyOptimal;
    }

    private void AllocateTextureCubeFace(
        VkCommandBuffer vkCmd,
        GraphicsTexture tex,
        int face,
        int mip,
        uint size,
        ReadOnlySpan<byte> data)
    {
        if (tex.Handle == 0 || !_device.Images.TryGetValue(tex.Handle, out VkImageResource? resource))
            return;
        if ((uint)face >= 6)
            throw new ArgumentOutOfRangeException(nameof(face), face, "Vulkan cubemap face must be 0 through 5.");
        if (mip < 0)
            throw new ArgumentOutOfRangeException(nameof(mip));
        if (tex.Type != TextureType.TextureCubeMap)
            throw new InvalidOperationException("Vulkan AllocateTextureCubeFace requires a cubemap resource.");

        if (resource.Image.Handle == 0 && mip != 0)
            throw new InvalidOperationException("Vulkan cubemap mip 0 must be allocated before higher mip levels.");

        if (resource.Image.Handle == 0 || (mip == 0 && (resource.Width != size || resource.Height != size)))
        {
            if (resource.Image.Handle != 0)
                _device.DestroyImage(resource);

            Format format = VulkanFormats.ToVkFormat(tex.ImageFormat);
            uint mipLevels = CalculateMipLevels(size);
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                Flags = ImageCreateFlags.ImageCreateCubeCompatibleBit,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D(size, size, 1),
                MipLevels = mipLevels,
                ArrayLayers = 6,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.ColorAttachmentBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
            };
            VulkanGraphicsDevice.Check(_device.Vk.CreateImage(_device.Device, &imageInfo, null, out Image image), "vkCreateImage");
            _device.Vk.GetImageMemoryRequirements(_device.Device, image, out MemoryRequirements requirements);
            var allocation = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = _device.FindMemoryType(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
            };
            VulkanGraphicsDevice.Check(_device.Vk.AllocateMemory(_device.Device, &allocation, null, out DeviceMemory memory), "vkAllocateMemory");
            VulkanGraphicsDevice.Check(_device.Vk.BindImageMemory(_device.Device, image, memory, 0), "vkBindImageMemory");

            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.TypeCube,
                Format = format,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    LevelCount = mipLevels,
                    LayerCount = 6,
                },
            };
            VulkanGraphicsDevice.Check(_device.Vk.CreateImageView(_device.Device, &viewInfo, null, out ImageView view), "vkCreateImageView");

            resource.Image = image;
            resource.Memory = memory;
            resource.View = view;
            resource.Format = format;
            resource.Width = size;
            resource.Height = size;
            resource.Depth = 1;
            resource.MipLevels = mipLevels;
            resource.Layout = ImageLayout.ShaderReadOnlyOptimal;
            resource.CubeInitializedFaces = 0;
            resource.CubeInitializedFacesByMip = new byte[mipLevels];
            resource.AvailableMipLevels = 0;
            _device.InitializeImageForSampling(image, mipLevels, 6);
            resource.Sampler = CreateSampler(resource);
        }

        if ((uint)mip >= resource.MipLevels)
            throw new ArgumentOutOfRangeException(nameof(mip), mip, "Vulkan cubemap mip exceeds the allocated mip chain.");
        uint expectedSize = Math.Max(1u, resource.Width >> mip);
        if (size != expectedSize)
            throw new ArgumentException($"Vulkan cubemap mip {mip} expects size {expectedSize}, got {size}.", nameof(size));

        _ = vkCmd;
        byte faceBit = checked((byte)(1 << face));
        if (data.Length > 0)
        {
            _device.UploadTextureCubeFace(
                resource,
                data,
                size,
                checked((uint)face),
                checked((uint)mip),
                ImageLayout.ShaderReadOnlyOptimal,
                VulkanFormats.BytesPerPixel(tex.ImageFormat));
        }

        resource.CubeInitializedFacesByMip[mip] |= faceBit;
        resource.CubeInitializedFaces = resource.CubeInitializedFacesByMip[0];
        uint availableMipLevels = 0;
        while (availableMipLevels < resource.MipLevels &&
            resource.CubeInitializedFacesByMip[availableMipLevels] == 0b0011_1111)
        {
            availableMipLevels++;
        }
        uint oldMaxLod = resource.AvailableMipLevels > 0 ? resource.AvailableMipLevels - 1 : 0;
        uint newMaxLod = availableMipLevels > 0 ? availableMipLevels - 1 : 0;
        resource.AvailableMipLevels = availableMipLevels;
        if (newMaxLod != oldMaxLod)
            ReplaceSamplerIfAllocated(resource);
    }

    private void SetTextureFilters(GraphicsTexture texture, TextureMin min, TextureMag mag)
    {
        if (texture.Handle == 0 || !_device.Images.TryGetValue(texture.Handle, out VkImageResource? resource))
            return;
        if (resource.MinFilter == min && resource.MagFilter == mag)
            return;

        resource.MinFilter = min;
        resource.MagFilter = mag;
        ReplaceSamplerIfAllocated(resource);
    }

    private void GenerateMipmaps(VkCommandBuffer vkCmd, GraphicsTexture texture)
    {
        if (texture.Handle == 0 || !_device.Images.TryGetValue(texture.Handle, out VkImageResource? resource))
            return;
        if (resource.Type != TextureType.TextureCubeMap)
            throw new NotSupportedException("Vulkan mip generation currently supports cubemaps only.");
        if (resource.CubeInitializedFaces != 0b0011_1111)
            throw new InvalidOperationException("Vulkan cubemap mip generation requires all base-level faces.");
        if (resource.MipLevels <= 1)
            return;

        ImageMemoryBarrier* barriers = stackalloc ImageMemoryBarrier[2];
        for (uint mip = 1; mip < resource.MipLevels; mip++)
        {
            int sourceSize = checked((int)Math.Max(1u, resource.Width >> checked((int)(mip - 1))));
            int destinationSize = checked((int)Math.Max(1u, resource.Width >> checked((int)mip)));
            for (uint face = 0; face < 6; face++)
            {
                barriers[0] = CreateMipBarrier(
                    resource.Image,
                    mip - 1,
                    face,
                    ImageLayout.ShaderReadOnlyOptimal,
                    ImageLayout.TransferSrcOptimal,
                    AccessFlags.ShaderReadBit,
                    AccessFlags.TransferReadBit);
                barriers[1] = CreateMipBarrier(
                    resource.Image,
                    mip,
                    face,
                    ImageLayout.ShaderReadOnlyOptimal,
                    ImageLayout.TransferDstOptimal,
                    AccessFlags.ShaderReadBit,
                    AccessFlags.TransferWriteBit);
                _device.Vk.CmdPipelineBarrier(
                    vkCmd,
                    PipelineStageFlags.FragmentShaderBit,
                    PipelineStageFlags.TransferBit,
                    0,
                    0,
                    null,
                    0,
                    null,
                    2,
                    barriers);

                var blit = new ImageBlit
                {
                    SrcSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = mip - 1,
                        BaseArrayLayer = face,
                        LayerCount = 1,
                    },
                    DstSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = mip,
                        BaseArrayLayer = face,
                        LayerCount = 1,
                    },
                };
                blit.SrcOffsets[0] = new Offset3D(0, 0, 0);
                blit.SrcOffsets[1] = new Offset3D(sourceSize, sourceSize, 1);
                blit.DstOffsets[0] = new Offset3D(0, 0, 0);
                blit.DstOffsets[1] = new Offset3D(destinationSize, destinationSize, 1);
                _device.Vk.CmdBlitImage(
                    vkCmd,
                    resource.Image,
                    ImageLayout.TransferSrcOptimal,
                    resource.Image,
                    ImageLayout.TransferDstOptimal,
                    1,
                    &blit,
                    Filter.Linear);

                barriers[0] = CreateMipBarrier(
                    resource.Image,
                    mip - 1,
                    face,
                    ImageLayout.TransferSrcOptimal,
                    ImageLayout.ShaderReadOnlyOptimal,
                    AccessFlags.TransferReadBit,
                    AccessFlags.ShaderReadBit);
                barriers[1] = CreateMipBarrier(
                    resource.Image,
                    mip,
                    face,
                    ImageLayout.TransferDstOptimal,
                    ImageLayout.ShaderReadOnlyOptimal,
                    AccessFlags.TransferWriteBit,
                    AccessFlags.ShaderReadBit);
                _device.Vk.CmdPipelineBarrier(
                    vkCmd,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.FragmentShaderBit,
                    0,
                    0,
                    null,
                    0,
                    null,
                    2,
                    barriers);
            }
            resource.CubeInitializedFacesByMip[mip] = 0b0011_1111;
        }

        uint oldMaxLod = resource.AvailableMipLevels > 0 ? resource.AvailableMipLevels - 1 : 0;
        resource.AvailableMipLevels = resource.MipLevels;
        uint newMaxLod = resource.MipLevels - 1;
        if (newMaxLod != oldMaxLod)
            ReplaceSamplerIfAllocated(resource);
    }

    private static ImageMemoryBarrier CreateMipBarrier(
        Image image,
        uint mipLevel,
        uint arrayLayer,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        AccessFlags sourceAccess,
        AccessFlags destinationAccess,
        ImageAspectFlags aspectMask = ImageAspectFlags.ColorBit) => new()
    {
        SType = StructureType.ImageMemoryBarrier,
        OldLayout = oldLayout,
        NewLayout = newLayout,
        SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
        DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
        Image = image,
        SrcAccessMask = sourceAccess,
        DstAccessMask = destinationAccess,
        SubresourceRange = new ImageSubresourceRange
        {
            AspectMask = aspectMask,
            BaseMipLevel = mipLevel,
            LevelCount = 1,
            BaseArrayLayer = arrayLayer,
            LayerCount = 1,
        },
    };

    private void ReplaceSamplerIfAllocated(VkImageResource resource)
    {
        if (resource.Sampler.Handle == 0)
            return;

        Sampler previous = resource.Sampler;
        resource.Sampler = CreateSampler(resource);
        _submissionRetiredSamplers!.Add(previous);
        _descriptorDirty = true;
    }

    private Sampler CreateSampler(VkImageResource resource)
    {
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = VulkanFormats.ToFilter(resource.MagFilter),
            MinFilter = VulkanFormats.ToFilter(resource.MinFilter),
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = VulkanFormats.ToAddressMode(resource.WrapS),
            AddressModeV = VulkanFormats.ToAddressMode(resource.WrapT),
            AddressModeW = VulkanFormats.ToAddressMode(resource.WrapR),
            MinLod = 0,
            MaxLod = resource.AvailableMipLevels > 0 ? resource.AvailableMipLevels - 1 : 0,
            MaxAnisotropy = 1,
        };
        VulkanGraphicsDevice.Check(
            _device.Vk.CreateSampler(_device.Device, &samplerInfo, null, out Sampler sampler),
            "vkCreateSampler");
        return sampler;
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

    private void CreateVertexArray(GraphicsVertexArray vao)
    {
        uint handle = _device.AllocateHandle();
        vao.Handle = handle;
        _device.VertexArrays[handle] = new VkVertexArrayResource
        {
            VertexBuffer = vao.Vertices.Handle,
            IndexBuffer = vao.Indices?.Handle ?? 0,
            InstanceBuffer = vao.InstanceBuffer?.Handle ?? 0,
            Format = vao.Format,
            InstanceFormat = vao.InstanceFormat,
        };
    }

    private void DisposeVertexArray(GraphicsVertexArray vao)
    {
        if (vao.Handle == 0)
            return;

        _device.VertexArrays.Remove(vao.Handle);
        vao.Handle = 0;
    }

    private void DrawArrays(
        VkCommandBuffer vkCmd,
        GraphicsVertexArray? vao,
        Topology topology,
        int first,
        uint count)
    {
        if (_currentShader?.Bytecode?.Format != ShaderBytecodeFormat.SpirV || vao == null || vao.Handle == 0)
        {
            WarnDrawNoPso();
            return;
        }
        if (!_device.VertexArrays.TryGetValue(vao.Handle, out VkVertexArrayResource? vertexArray) ||
            !_device.Buffers.TryGetValue(vertexArray.VertexBuffer, out VkBufferResource? vertexBuffer) ||
            vertexBuffer.Buffer.Handle == 0)
        {
            WarnOnce(CommandOpcode.DrawArrays, "Vulkan DrawArrays skipped: vertex-array resources are incomplete.");
            return;
        }
        if (vertexArray.InstanceFormat != null)
            throw new NotSupportedException("Vulkan DrawArrays does not consume an instance stream; use an instanced draw opcode.");

        var key = new GraphicsPipelineKey(_currentShader, vao.Handle, topology, in _currentRaster, index32Bit: false);
        Pipeline pipeline = _device.GetOrCreateGraphicsPipeline(key, _currentShader, GetCurrentTargetFormats());
        VkShaderLayoutResource layout = _device.GetOrCreateShaderLayout(_currentShader);
        EnsureDrawRenderPass(vkCmd);
        _device.Vk.CmdBindPipeline(vkCmd, PipelineBindPoint.Graphics, pipeline);
        BindDescriptorSetIfNeeded(vkCmd, layout);
        VkBuffer buffer = vertexBuffer.Buffer;
        ulong offset = 0;
        _device.Vk.CmdBindVertexBuffers(vkCmd, 0, 1, &buffer, &offset);
        _device.Vk.CmdDraw(vkCmd, count, 1, checked((uint)first), 0);
        _ = layout;
    }

    private void DrawIndexed(
        VkCommandBuffer vkCmd,
        GraphicsVertexArray? vao,
        Topology topology,
        uint indexCount,
        uint startIndex,
        int baseVertex,
        bool index32Bit)
    {
        if (_currentShader?.Bytecode?.Format != ShaderBytecodeFormat.SpirV || vao == null || vao.Handle == 0)
        {
            WarnDrawNoPso();
            return;
        }
        if (!_device.VertexArrays.TryGetValue(vao.Handle, out VkVertexArrayResource? vertexArray) ||
            !_device.Buffers.TryGetValue(vertexArray.VertexBuffer, out VkBufferResource? vertexBuffer) ||
            !_device.Buffers.TryGetValue(vertexArray.IndexBuffer, out VkBufferResource? indexBuffer) ||
            vertexBuffer.Buffer.Handle == 0 || indexBuffer.Buffer.Handle == 0)
        {
            WarnOnce(CommandOpcode.DrawIndexed, "Vulkan DrawIndexed skipped: vertex-array resources are incomplete.");
            return;
        }
        if (vertexArray.InstanceFormat != null)
            throw new NotSupportedException("Vulkan DrawIndexed does not consume an instance stream; use DrawIndexedInstanced.");

        var key = new GraphicsPipelineKey(_currentShader, vao.Handle, topology, in _currentRaster, index32Bit);
        Pipeline pipeline = _device.GetOrCreateGraphicsPipeline(key, _currentShader, GetCurrentTargetFormats());
        VkShaderLayoutResource layout = _device.GetOrCreateShaderLayout(_currentShader);
        EnsureDrawRenderPass(vkCmd);
        _device.Vk.CmdBindPipeline(vkCmd, PipelineBindPoint.Graphics, pipeline);
        BindDescriptorSetIfNeeded(vkCmd, layout);
        VkBuffer buffer = vertexBuffer.Buffer;
        ulong offset = 0;
        _device.Vk.CmdBindVertexBuffers(vkCmd, 0, 1, &buffer, &offset);
        _device.Vk.CmdBindIndexBuffer(vkCmd, indexBuffer.Buffer, 0, index32Bit ? IndexType.Uint32 : IndexType.Uint16);
        _device.Vk.CmdDrawIndexed(vkCmd, indexCount, 1, startIndex, baseVertex, 0);
        _ = layout;
    }

    private void DrawIndexedInstanced(
        VkCommandBuffer vkCmd,
        GraphicsVertexArray? vao,
        Topology topology,
        uint indexCount,
        uint instanceCount,
        uint startIndex,
        int baseVertex,
        bool index32Bit)
    {
        if (_currentShader?.Bytecode?.Format != ShaderBytecodeFormat.SpirV || vao == null || vao.Handle == 0)
        {
            WarnDrawNoPso();
            return;
        }
        if (!_device.VertexArrays.TryGetValue(vao.Handle, out VkVertexArrayResource? vertexArray) ||
            vertexArray.InstanceFormat == null ||
            !_device.Buffers.TryGetValue(vertexArray.VertexBuffer, out VkBufferResource? vertexBuffer) ||
            !_device.Buffers.TryGetValue(vertexArray.InstanceBuffer, out VkBufferResource? instanceBuffer) ||
            !_device.Buffers.TryGetValue(vertexArray.IndexBuffer, out VkBufferResource? indexBuffer) ||
            vertexBuffer.Buffer.Handle == 0 || instanceBuffer.Buffer.Handle == 0 || indexBuffer.Buffer.Handle == 0)
        {
            WarnOnce(CommandOpcode.DrawIndexedInstanced, "Vulkan DrawIndexedInstanced skipped: vertex-array resources are incomplete.");
            return;
        }

        var key = new GraphicsPipelineKey(_currentShader, vao.Handle, topology, in _currentRaster, index32Bit);
        Pipeline pipeline = _device.GetOrCreateGraphicsPipeline(key, _currentShader, GetCurrentTargetFormats());
        VkShaderLayoutResource layout = _device.GetOrCreateShaderLayout(_currentShader);
        EnsureDrawRenderPass(vkCmd);
        _device.Vk.CmdBindPipeline(vkCmd, PipelineBindPoint.Graphics, pipeline);
        BindDescriptorSetIfNeeded(vkCmd, layout);
        VkBuffer* buffers = stackalloc VkBuffer[2] { vertexBuffer.Buffer, instanceBuffer.Buffer };
        ulong* offsets = stackalloc ulong[2];
        _device.Vk.CmdBindVertexBuffers(vkCmd, 0, 2, buffers, offsets);
        _device.Vk.CmdBindIndexBuffer(vkCmd, indexBuffer.Buffer, 0, index32Bit ? IndexType.Uint32 : IndexType.Uint16);
        _device.Vk.CmdDrawIndexed(vkCmd, indexCount, instanceCount, startIndex, baseVertex, 0);
        _ = layout;
    }

    private void BindDescriptorSetIfNeeded(VkCommandBuffer vkCmd, VkShaderLayoutResource layout)
    {
        ShaderBindingLayout bindingLayout = _currentShader?.Bytecode?.BindingLayout ?? new ShaderBindingLayout();
        EnsureObjectUniformsBuffer(bindingLayout.Buffers);
        EnsureMaterialUniformsBuffer(bindingLayout.Buffers);
        int descriptorCount = bindingLayout.Buffers.Length + bindingLayout.Textures.Length + bindingLayout.Samplers.Length;
        if (descriptorCount == 0)
            return;

        if (_descriptorDirty)
        {
            _currentDescriptorSet = _device.AllocateDescriptorSet(layout);
            _submissionDescriptorSets!.Add(_currentDescriptorSet);
            int bufferCount = bindingLayout.Buffers.Length;
            int textureCount = bindingLayout.Textures.Length;
            int samplerCount = bindingLayout.Samplers.Length;
            DescriptorBufferInfo* bufferInfos = stackalloc DescriptorBufferInfo[bufferCount];
            DescriptorImageInfo* imageInfos = stackalloc DescriptorImageInfo[textureCount + samplerCount];
            WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[descriptorCount];
            int writeIndex = 0;
            for (int i = 0; i < bufferCount; i++)
            {
                ShaderBindingSlot binding = bindingLayout.Buffers[i];
                if (_transientUniformBuffers.TryGetValue(binding.Name, out VkTransientUniformBinding transientBuffer))
                {
                    bufferInfos[i] = new DescriptorBufferInfo
                    {
                        Buffer = transientBuffer.Resource.Buffer,
                        Offset = transientBuffer.Offset,
                        Range = transientBuffer.Range,
                    };
                    writes[writeIndex++] = new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _currentDescriptorSet,
                        DstBinding = checked((uint)layout.Plan.GetPhysicalBinding(binding)),
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.UniformBuffer,
                        PBufferInfo = &bufferInfos[i],
                    };
                    continue;
                }
                if (!_uniformBuffers.TryGetValue(binding.Name, out GraphicsBuffer? uniformBuffer) || uniformBuffer.Handle == 0)
                    throw new InvalidOperationException($"Vulkan draw requires uniform buffer '{binding.Name}'.");
                if (!_device.Buffers.TryGetValue(uniformBuffer.Handle, out VkBufferResource? bufferResource) ||
                    bufferResource.Buffer.Handle == 0)
                    throw new InvalidOperationException($"Vulkan uniform buffer '{binding.Name}' is not available.");

                bufferInfos[i] = new DescriptorBufferInfo
                {
                    Buffer = bufferResource.Buffer,
                    Offset = 0,
                    Range = bufferResource.Size,
                };
                writes[writeIndex++] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = _currentDescriptorSet,
                    DstBinding = checked((uint)layout.Plan.GetPhysicalBinding(binding)),
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.UniformBuffer,
                    PBufferInfo = &bufferInfos[i],
                };
            }
            for (int i = 0; i < textureCount; i++)
            {
                ShaderBindingSlot binding = bindingLayout.Textures[i];
                VkImageResource textureResource = GetTextureResource(binding.Name);
                imageInfos[i] = new DescriptorImageInfo
                {
                    ImageView = textureResource.View,
                    ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                };
                writes[writeIndex++] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = _currentDescriptorSet,
                    DstBinding = checked((uint)layout.Plan.GetPhysicalBinding(binding)),
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.SampledImage,
                    PImageInfo = &imageInfos[i],
                };
            }
            for (int i = 0; i < samplerCount; i++)
            {
                ShaderBindingSlot binding = bindingLayout.Samplers[i];
                string textureName = FindTextureNameForSampler(bindingLayout.Textures, binding.Slot);
                VkImageResource textureResource = GetTextureResource(textureName);
                int imageIndex = textureCount + i;
                imageInfos[imageIndex] = new DescriptorImageInfo { Sampler = textureResource.Sampler };
                writes[writeIndex++] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = _currentDescriptorSet,
                    DstBinding = checked((uint)layout.Plan.GetPhysicalBinding(binding)),
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.Sampler,
                    PImageInfo = &imageInfos[imageIndex],
                };
            }
            _device.Vk.UpdateDescriptorSets(_device.Device, checked((uint)descriptorCount), writes, 0, null);
            _descriptorDirty = false;
        }

        DescriptorSet descriptorSet = _currentDescriptorSet;
        _device.Vk.CmdBindDescriptorSets(
            vkCmd,
            PipelineBindPoint.Graphics,
            layout.PipelineLayout,
            0,
            1,
            &descriptorSet,
            0,
            null);
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

        ulong dataSize = checked((ulong)Unsafe.SizeOf<Rendering.ObjectUniformsData>());
        ulong alignment = _device.UniformBufferOffsetAlignment;
        ulong alignedSize = (dataSize + alignment - 1) / alignment * alignment;
        if (_transientUniformArena == null || _transientUniformArenaOffset + alignedSize > TransientUniformArenaSize)
        {
            _device.CreateBuffer(
                TransientUniformArenaSize,
                BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                out VkBuffer buffer,
                out DeviceMemory memory);
            _transientUniformArena = new VkBufferResource
            {
                Buffer = buffer,
                Memory = memory,
                Size = TransientUniformArenaSize,
                Type = BufferType.UniformBuffer,
                Dynamic = true,
            };
            _submissionTransientBuffers!.Add(_transientUniformArena);
            _transientUniformArenaOffset = 0;
        }

        ulong offset = _transientUniformArenaOffset;
        void* mapped;
        VulkanGraphicsDevice.Check(
            _device.Vk.MapMemory(_device.Device, _transientUniformArena.Memory, 0, TransientUniformArenaSize, 0, &mapped),
            "vkMapMemory");
        try
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref _objectUniforms, 1));
            fixed (byte* source = bytes)
                System.Buffer.MemoryCopy(source, (byte*)mapped + offset, checked((long)alignedSize), bytes.Length);
        }
        finally
        {
            _device.Vk.UnmapMemory(_device.Device, _transientUniformArena.Memory);
        }

        _transientUniformBuffers["ObjectUniforms"] = new VkTransientUniformBinding(
            _transientUniformArena,
            offset,
            dataSize);
        _transientUniformArenaOffset += alignedSize;
        _objectUniformsDirty = false;
        _descriptorDirty = true;
    }

    private void ApplyObjectProperties(Rendering.PropertyState? properties)
    {
        _objectUniforms = Rendering.ObjectUniformsData.Identity;
        if (properties != null)
        {
            if (properties._matrices.TryGetValue("prowl_ObjectToWorld", out Vector.Float4x4 objectToWorld))
                _objectUniforms.prowl_ObjectToWorld = objectToWorld;
            if (properties._matrices.TryGetValue("prowl_WorldToObject", out Vector.Float4x4 worldToObject))
                _objectUniforms.prowl_WorldToObject = worldToObject;
            if (properties._matrices.TryGetValue("prowl_PrevObjectToWorld", out Vector.Float4x4 previousObjectToWorld))
                _objectUniforms.prowl_PrevObjectToWorld = previousObjectToWorld;
            if (properties._ints.TryGetValue("_ObjectID", out int objectId))
                _objectUniforms._ObjectID = objectId;
        }
        _objectUniformsDirty = true;
        _descriptorDirty = true;
    }

    private void EnsureMaterialUniformsBuffer(ShaderBindingSlot[] buffers)
    {
        bool required = false;
        for (int i = 0; i < buffers.Length; i++)
        {
            if (buffers[i].Name == "UnlitMaterial")
            {
                required = true;
                break;
            }
        }
        if (!required || !_materialUniformsDirty)
            return;

        Rendering.UnlitMaterialUniformsData data = BuildUnlitMaterialUniforms();
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref data, 1));
        _transientUniformBuffers["UnlitMaterial"] = AllocateTransientUniform(bytes);
        _materialUniformsDirty = false;
        _descriptorDirty = true;
    }

    private Rendering.UnlitMaterialUniformsData BuildUnlitMaterialUniforms()
    {
        Rendering.UnlitMaterialUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = _materialShader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "_Tiling")
                    data._Tiling = new Vector.Float2(property.Value.X, property.Value.Y);
                else if (property.Name == "_Offset")
                    data._Offset = new Vector.Float2(property.Value.X, property.Value.Y);
                else if (property.Name == "_MainColor")
                    data._MainColor = property.Value;
            }
        }

        if (_materialProperties != null)
        {
            if (_materialProperties._vectors2.TryGetValue("_Tiling", out Vector.Float2 tiling))
                data._Tiling = tiling;
            if (_materialProperties._vectors2.TryGetValue("_Offset", out Vector.Float2 offset))
                data._Offset = offset;
            if (_materialProperties._vectors4.TryGetValue("_MainColor", out Vector.Float4 vectorColor))
                data._MainColor = vectorColor;
            else if (_materialProperties._colors.TryGetValue("_MainColor", out Vector.Color color))
                data._MainColor = new Vector.Float4(color.R, color.G, color.B, color.A);
        }
        return data;
    }

    private void ApplyMaterialTextureBindings()
    {
        _textures.Remove("_MainTex");
        Resources.Texture2D? texture = null;
        if (_materialProperties != null && _materialProperties._textures.ContainsKey("_MainTex"))
            texture = CollectionsMarshal.GetValueRefOrNullRef(_materialProperties._textures, "_MainTex").Res;
        if (texture == null && _materialShader != null)
        {
            Rendering.Shaders.ShaderProperty[] defaults = _materialShader.PropertyArray;
            for (int i = 0; i < defaults.Length; i++)
            {
                if (defaults[i].Name == "_MainTex")
                {
                    texture = defaults[i].Texture2DValue;
                    break;
                }
            }
        }
        if (texture != null)
            _textures["_MainTex"] = texture.Handle;
    }

    private VkTransientUniformBinding AllocateTransientUniform(ReadOnlySpan<byte> bytes)
    {
        ulong alignment = _device.UniformBufferOffsetAlignment;
        ulong alignedSize = ((ulong)bytes.Length + alignment - 1) / alignment * alignment;
        if (_transientUniformArena == null || _transientUniformArenaOffset + alignedSize > TransientUniformArenaSize)
        {
            _device.CreateBuffer(
                TransientUniformArenaSize,
                BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                out VkBuffer buffer,
                out DeviceMemory memory);
            _transientUniformArena = new VkBufferResource
            {
                Buffer = buffer,
                Memory = memory,
                Size = TransientUniformArenaSize,
                Type = BufferType.UniformBuffer,
                Dynamic = true,
            };
            _submissionTransientBuffers!.Add(_transientUniformArena);
            _transientUniformArenaOffset = 0;
        }

        ulong offset = _transientUniformArenaOffset;
        void* mapped;
        VulkanGraphicsDevice.Check(
            _device.Vk.MapMemory(_device.Device, _transientUniformArena.Memory, 0, TransientUniformArenaSize, 0, &mapped),
            "vkMapMemory");
        try
        {
            fixed (byte* source = bytes)
                System.Buffer.MemoryCopy(source, (byte*)mapped + offset, checked((long)alignedSize), bytes.Length);
        }
        finally
        {
            _device.Vk.UnmapMemory(_device.Device, _transientUniformArena.Memory);
        }
        _transientUniformArenaOffset += alignedSize;
        return new VkTransientUniformBinding(_transientUniformArena, offset, checked((ulong)bytes.Length));
    }

    private VkImageResource GetTextureResource(string name)
    {
        if (!_textures.TryGetValue(name, out GraphicsTexture? texture) || texture.Handle == 0)
            throw new InvalidOperationException($"Vulkan draw requires texture '{name}'.");
        if (!_device.Images.TryGetValue(texture.Handle, out VkImageResource? resource) ||
            resource.View.Handle == 0 || resource.Sampler.Handle == 0 ||
            resource.Layout != ImageLayout.ShaderReadOnlyOptimal)
            throw new InvalidOperationException($"Vulkan texture '{name}' is not sample-ready.");
        return resource;
    }

    private static string FindTextureNameForSampler(ShaderBindingSlot[] textures, int samplerSlot)
    {
        for (int i = 0; i < textures.Length; i++)
        {
            if (textures[i].Slot == samplerSlot)
                return textures[i].Name;
        }
        throw new NotSupportedException($"Vulkan sampler slot s{samplerSlot} has no texture at matching t{samplerSlot}.");
    }

    private void WarnDrawNoPso()
    {
        if (_warnedDrawNoPso)
            return;
        _warnedDrawNoPso = true;
        Debug.LogWarning(_currentShader == null
            ? "Vulkan draw skipped: no backend-neutral shader variant is bound."
            : "Vulkan draw skipped: pipeline state objects are not ready yet.");
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
        WarnOnce(op, $"VulkanCommandTranslator: unhandled opcode {op} (skipped).");

        switch (op)
        {
            case CommandOpcode.BlitFramebuffer:
                pos += sizeof(int) * 8 + 2;
                break;
            case CommandOpcode.ClearGlobalTexture:
            case CommandOpcode.CompileShader:
            case CommandOpcode.DisposeShader:
                _ = objects[ReadU16(stream, ref pos)];
                break;
            case CommandOpcode.SetGlobalTexture:
            case CommandOpcode.SetGlobalTexture3D:
            case CommandOpcode.SetGlobalTextureCube:
            case CommandOpcode.SetGlobalMatrices:
                _ = objects[ReadU16(stream, ref pos)];
                _ = objects[ReadU16(stream, ref pos)];
                break;
            case CommandOpcode.SetGlobalInt:
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
                throw new InvalidOperationException($"VulkanCommandTranslator: cannot skip unknown opcode {op}.");
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

internal readonly record struct VkTransientUniformBinding(VkBufferResource Resource, ulong Offset, ulong Range);
