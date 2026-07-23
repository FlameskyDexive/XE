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
    private readonly VulkanGraphicsDevice _device;
    private readonly HashSet<CommandOpcode> _warnedOpcodes = new();
    private bool _warnedDrawNoPso;
    private bool _inRenderPass;

    private GraphicsFrameBuffer? _pendingRenderTarget;
    private ShaderVariant? _currentShader;
    private RasterizerState _currentRaster = new();
    private readonly Dictionary<string, GraphicsBuffer> _uniformBuffers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GraphicsTexture> _textures = new(StringComparer.Ordinal);
    private DescriptorSet _currentDescriptorSet;
    private bool _descriptorDirty;
    private List<DescriptorSet>? _submissionDescriptorSets;
    private List<Sampler>? _submissionRetiredSamplers;

    public VulkanCommandTranslator(VulkanGraphicsDevice device)
    {
        _device = device;
    }

    public void Translate(
        CommandBuffer commandBuffer,
        VkCommandBuffer vkCmd,
        List<DescriptorSet> descriptorSets,
        List<Sampler> retiredSamplers)
    {
        var stream = commandBuffer._stream.AsSpan(0, commandBuffer._streamPos);
        var objects = commandBuffer._objects;
        var store = commandBuffer._store;
        int pos = 0;
        _inRenderPass = false;
        _uniformBuffers.Clear();
        _textures.Clear();
        _currentDescriptorSet = default;
        _descriptorDirty = true;
        _submissionDescriptorSets = descriptorSets;
        _submissionRetiredSamplers = retiredSamplers;

        while (pos < stream.Length)
        {
            CommandOpcode op = ReadOpcode(stream, ref pos);
            switch (op)
            {
                case CommandOpcode.SetRenderTarget:
                {
                    EndRenderPassIfNeeded(vkCmd);
                    _pendingRenderTarget = (GraphicsFrameBuffer?)objects[ReadU16(stream, ref pos)];
                    break;
                }
                case CommandOpcode.SetRenderTargets:
                {
                    EndRenderPassIfNeeded(vkCmd);
                    _pendingRenderTarget = (GraphicsFrameBuffer?)objects[ReadU16(stream, ref pos)];
                    _ = ReadU16(stream, ref pos);
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
        _ = _pendingRenderTarget;
        if (!_device.HasSwapchain)
            return;

        EnsureSwapchainRenderPass(vkCmd, flags, r, g, b, a, depth, stencil);
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
        if (_pendingRenderTarget != null)
            throw new NotSupportedException("Vulkan custom framebuffer draw execution is not implemented yet.");

        Extent2D extent = _device.CurrentRenderExtent;
        var begin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _device.CurrentRenderPass,
            Framebuffer = _device.CurrentFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), extent),
        };
        _device.Vk.CmdBeginRenderPass(vkCmd, &begin, SubpassContents.Inline);
        _inRenderPass = true;
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
            ? ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.TransferDstBit
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
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.ShaderReadOnlyOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = res.Image,
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
            res.Layout = ImageLayout.ShaderReadOnlyOptimal;
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
        if (mip != 0)
            return;
        if (tex.Type != TextureType.TextureCubeMap)
            throw new InvalidOperationException("Vulkan AllocateTextureCubeFace requires a cubemap resource.");

        if (resource.Image.Handle == 0 || resource.Width != size || resource.Height != size)
        {
            if (resource.Image.Handle != 0)
                _device.DestroyImage(resource);

            Format format = VulkanFormats.ToVkFormat(tex.ImageFormat);
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                Flags = ImageCreateFlags.ImageCreateCubeCompatibleBit,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D(size, size, 1),
                MipLevels = 1,
                ArrayLayers = 6,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit,
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
                    LevelCount = 1,
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
            resource.Layout = ImageLayout.Undefined;
            resource.CubeInitializedFaces = 0;
            resource.Sampler = CreateSampler(resource);
        }

        byte faceBit = checked((byte)(1 << face));
        ImageLayout oldLayout = (resource.CubeInitializedFaces & faceBit) != 0
            ? ImageLayout.ShaderReadOnlyOptimal
            : ImageLayout.Undefined;
        if (data.Length > 0)
        {
            _device.UploadTextureCubeFace(resource, data, size, checked((uint)face), oldLayout, VulkanFormats.BytesPerPixel(tex.ImageFormat));
        }
        else
        {
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = ImageLayout.ShaderReadOnlyOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = resource.Image,
                SrcAccessMask = oldLayout == ImageLayout.ShaderReadOnlyOptimal ? AccessFlags.ShaderReadBit : 0,
                DstAccessMask = AccessFlags.ShaderReadBit,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseArrayLayer = checked((uint)face),
                    LevelCount = 1,
                    LayerCount = 1,
                },
            };
            PipelineStageFlags sourceStage = oldLayout == ImageLayout.ShaderReadOnlyOptimal
                ? PipelineStageFlags.FragmentShaderBit
                : PipelineStageFlags.TopOfPipeBit;
            _device.Vk.CmdPipelineBarrier(vkCmd, sourceStage, PipelineStageFlags.FragmentShaderBit, 0, 0, null, 0, null, 1, &barrier);
        }

        resource.CubeInitializedFaces |= faceBit;
        if (resource.CubeInitializedFaces == 0b0011_1111)
            resource.Layout = ImageLayout.ShaderReadOnlyOptimal;
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
            MaxLod = 0,
            MaxAnisotropy = 1,
        };
        VulkanGraphicsDevice.Check(
            _device.Vk.CreateSampler(_device.Device, &samplerInfo, null, out Sampler sampler),
            "vkCreateSampler");
        return sampler;
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
        Pipeline pipeline = _device.GetOrCreateGraphicsPipeline(key, _currentShader, _device.CurrentColorFormat);
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
        Pipeline pipeline = _device.GetOrCreateGraphicsPipeline(key, _currentShader, _device.CurrentColorFormat);
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
        Pipeline pipeline = _device.GetOrCreateGraphicsPipeline(key, _currentShader, _device.CurrentColorFormat);
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
            case CommandOpcode.SetProperties:
            case CommandOpcode.ClearProperties:
            case CommandOpcode.SetInstanceProperties:
            case CommandOpcode.ClearInstanceProperties:
            case CommandOpcode.ClearGlobalTexture:
            case CommandOpcode.GenerateMipmap:
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
                _ = objects[ReadU16(stream, ref pos)];
                _ = objects[ReadU16(stream, ref pos)];
                break;
            case CommandOpcode.SetGlobalInt:
            case CommandOpcode.SetUniformInt:
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
