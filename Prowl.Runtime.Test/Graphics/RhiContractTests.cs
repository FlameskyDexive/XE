// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.RHI;
using Prowl.Runtime.RHI.Shaders;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test.Rhi;

/// <summary>
/// CPU-only Stage C contract tests: command recording, null device, factory.
/// Optional GPU smoke tests skip (assert expected exception type) when a backend
/// cannot be created on the machine.
/// </summary>
public class RhiContractTests
{
    [Fact]
    public void CommandBuffer_Records_Clear_And_Viewport_Opcodes()
    {
        CommandBuffer cmd = global::Prowl.Runtime.Graphics.GetCommandBuffer("rhi-contract");
        cmd.SetViewport(0, 0, 128, 64);
        cmd.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, new Color(1f, 0f, 0f, 1f));

        Assert.True(cmd._streamPos > 0);

        ReadOnlySpan<byte> stream = cmd._stream.AsSpan(0, cmd._streamPos);
        int pos = 0;
        Assert.Equal(CommandOpcode.SetViewport, ReadOpcode(stream, ref pos));
        Assert.Equal(0, ReadI32(stream, ref pos));
        Assert.Equal(0, ReadI32(stream, ref pos));
        Assert.Equal(128u, ReadU32(stream, ref pos));
        Assert.Equal(64u, ReadU32(stream, ref pos));
        Assert.Equal(CommandOpcode.ClearRenderTarget, ReadOpcode(stream, ref pos));

        cmd._ownerReleased = true;
        CommandBufferPool.Return(cmd);
    }

    [Fact]
    public void CommandBuffer_Records_BackendNeutral_ShaderVariant()
    {
        var bytecode = new CompiledShaderBytecode(
            ShaderLanguage.Hlsl,
            ShaderBytecodeFormat.SpirV,
            [0x03, 0x02, 0x23, 0x07],
            [0x03, 0x02, 0x23, 0x07]);
        using var variant = new ShaderVariant(bytecode);
        CommandBuffer cmd = global::Prowl.Runtime.Graphics.GetCommandBuffer("rhi-shader-variant");

        cmd.SetShader(variant);

        ReadOnlySpan<byte> stream = cmd._stream.AsSpan(0, cmd._streamPos);
        int pos = 0;
        Assert.Equal(CommandOpcode.SetShader, ReadOpcode(stream, ref pos));
        ushort objectIndex = ReadU16(stream, ref pos);
        Assert.Same(variant, cmd._objects[objectIndex]);
        Assert.Equal(stream.Length, pos);

        cmd._ownerReleased = true;
        CommandBufferPool.Return(cmd);
    }

    [Fact]
    public void Factory_Creates_Null_And_OpenGL_Devices()
    {
        using IGraphicsDevice nullDevice = GraphicsDeviceFactory.Create(new GraphicsDeviceOptions
        {
            Backend = GraphicsBackend.Null,
        });
        Assert.Equal(GraphicsBackend.Null, nullDevice.Backend);
        nullDevice.Initialize(null);
        Assert.True(nullDevice.IsInitialized);
        nullDevice.BeginFrame();
        nullDevice.EndFrame();

        using IGraphicsDevice gl = GraphicsDeviceFactory.Create(new GraphicsDeviceOptions
        {
            Backend = GraphicsBackend.OpenGL,
        });
        Assert.Equal(GraphicsBackend.OpenGL, gl.Backend);
    }

    [Fact]
    public void TopologyUtilities_Portable_Vs_Legacy()
    {
        Assert.True(TopologyUtilities.IsPortable(Topology.Triangles));
        Assert.True(TopologyUtilities.IsPortable(Topology.Lines));
        Assert.False(TopologyUtilities.IsPortable(Topology.Quads));
        Assert.False(TopologyUtilities.IsPortable(Topology.LineLoop));
        Assert.False(TopologyUtilities.IsPortable(Topology.TriangleFan));
    }

    [Fact]
    public void ModernBackends_Map_Common_VertexFormats()
    {
        Assert.Equal(
            Silk.NET.Vulkan.Format.R32G32B32Sfloat,
            Backends.Vulkan.VulkanFormats.ToVertexFormat(VertexFormat.VertexType.Float, 3, normalized: false));
        Assert.Equal(
            Silk.NET.Vulkan.Format.R8G8B8A8Unorm,
            Backends.Vulkan.VulkanFormats.ToVertexFormat(VertexFormat.VertexType.UnsignedByte, 4, normalized: true));
        Assert.Equal(
            Vortice.DXGI.Format.R32G32B32_Float,
            Backends.D3D12.D3D12Formats.ToVertexFormat(VertexFormat.VertexType.Float, 3, normalized: false));
        Assert.Equal(
            Vortice.DXGI.Format.R8G8B8A8_UNorm,
            Backends.D3D12.D3D12Formats.ToVertexFormat(VertexFormat.VertexType.UnsignedByte, 4, normalized: true));
    }

    [Fact]
    public void Optional_D3D12_Device_Creates_Or_Skips()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.ThrowsAny<Exception>(() =>
                GraphicsDeviceFactory.Create(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 }));
            return;
        }

        try
        {
            using IGraphicsDevice device = GraphicsDeviceFactory.Create(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Direct3D12,
                Debug = false,
            });
            Assert.Equal(GraphicsBackend.Direct3D12, device.Backend);
            Assert.Contains("Direct3D", device.Capabilities.BackendName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected D3D12 failure type: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_Device_Creates_Or_Skips()
    {
        try
        {
            using IGraphicsDevice device = GraphicsDeviceFactory.Create(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Vulkan,
                Debug = false,
            });
            Assert.Equal(GraphicsBackend.Vulkan, device.Backend);
            Assert.Contains("Vulkan", device.Capabilities.BackendName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected Vulkan failure type: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void BackendDisplayName_Works_For_Null()
    {
        using var device = new NullGraphicsDevice();
        device.Initialize(null);
        string name = GraphicsBackendSelection.GetDisplayName(device);
        Assert.Contains("Null", name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpectedGpuUnavailable(Exception ex) =>
        ex is InvalidOperationException
            or NotSupportedException
            or PlatformNotSupportedException
            or DllNotFoundException
            || ex.GetType().FullName?.Contains("SharpGen", StringComparison.Ordinal) == true
            || ex.InnerException is DllNotFoundException;

    private static CommandOpcode ReadOpcode(ReadOnlySpan<byte> s, ref int pos)
    {
        ushort v = (ushort)(s[pos] | (s[pos + 1] << 8));
        pos += 2;
        return (CommandOpcode)v;
    }

    private static int ReadI32(ReadOnlySpan<byte> s, ref int pos)
    {
        int v = BitConverter.ToInt32(s.Slice(pos, 4));
        pos += 4;
        return v;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> s, ref int pos)
    {
        ushort v = BitConverter.ToUInt16(s.Slice(pos, 2));
        pos += 2;
        return v;
    }

    private static uint ReadU32(ReadOnlySpan<byte> s, ref int pos)
    {
        uint v = BitConverter.ToUInt32(s.Slice(pos, 4));
        pos += 4;
        return v;
    }
}
