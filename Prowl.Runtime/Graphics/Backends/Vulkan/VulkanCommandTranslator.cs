// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Silk.NET.Vulkan;

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

    public VulkanCommandTranslator(VulkanGraphicsDevice device)
    {
        _device = device;
    }

    public void Translate(CommandBuffer commandBuffer, VkCommandBuffer vkCmd)
    {
        var stream = commandBuffer._stream.AsSpan(0, commandBuffer._streamPos);
        var objects = commandBuffer._objects;
        var store = commandBuffer._store;
        int pos = 0;
        _inRenderPass = false;

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

    private void WarnDrawNoPso()
    {
        if (_warnedDrawNoPso)
            return;
        _warnedDrawNoPso = true;
        Debug.LogWarning("Vulkan draw skipped: pipeline state objects are not ready yet.");
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
            case CommandOpcode.SetRasterState:
                pos += Unsafe.SizeOf<RasterizerState>();
                break;
            case CommandOpcode.SetShader:
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
            case CommandOpcode.SetUniformBuffer:
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
