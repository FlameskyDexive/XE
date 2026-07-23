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
    private DescriptorSet _currentDescriptorSet;
    private bool _descriptorDirty;
    private List<DescriptorSet>? _submissionDescriptorSets;

    public VulkanCommandTranslator(VulkanGraphicsDevice device)
    {
        _device = device;
    }

    public void Translate(CommandBuffer commandBuffer, VkCommandBuffer vkCmd, List<DescriptorSet> descriptorSets)
    {
        var stream = commandBuffer._stream.AsSpan(0, commandBuffer._streamPos);
        var objects = commandBuffer._objects;
        var store = commandBuffer._store;
        int pos = 0;
        _inRenderPass = false;
        _uniformBuffers.Clear();
        _currentDescriptorSet = default;
        _descriptorDirty = true;
        _submissionDescriptorSets = descriptorSets;

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
                    AllocateTexture2D(tex, mip, w, h, data);
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

    private void AllocateTexture2D(GraphicsTexture tex, int mip, uint width, uint height, ReadOnlySpan<byte> data)
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
            : ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit;

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

        if (data.Length > 0)
            WarnOnce(CommandOpcode.AllocateTexture2D, "Vulkan AllocateTexture2D initial data upload is not implemented yet.");
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
        if (bindingLayout.Buffers.Length == 0)
            return;
        if (bindingLayout.Textures.Length != 0 || bindingLayout.Samplers.Length != 0)
            throw new NotSupportedException("Vulkan descriptor binding does not support texture or sampler bindings yet.");

        if (_descriptorDirty)
        {
            _currentDescriptorSet = _device.AllocateDescriptorSet(layout);
            _submissionDescriptorSets!.Add(_currentDescriptorSet);
            int bufferCount = bindingLayout.Buffers.Length;
            DescriptorBufferInfo* bufferInfos = stackalloc DescriptorBufferInfo[bufferCount];
            WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[bufferCount];
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
                writes[i] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = _currentDescriptorSet,
                    DstBinding = checked((uint)layout.Plan.GetPhysicalBinding(binding)),
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.UniformBuffer,
                    PBufferInfo = &bufferInfos[i],
                };
            }
            _device.Vk.UpdateDescriptorSets(_device.Device, checked((uint)bufferCount), writes, 0, null);
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
            case CommandOpcode.SetUniformTexture:
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
