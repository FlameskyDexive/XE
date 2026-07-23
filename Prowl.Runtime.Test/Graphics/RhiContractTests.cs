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
    public void GraphicsPipelineKey_Distinguishes_Exact_Pipeline_State()
    {
        var bytecode = new CompiledShaderBytecode(
            ShaderLanguage.Hlsl,
            ShaderBytecodeFormat.SpirV,
            [0x03, 0x02, 0x23, 0x07],
            [0x03, 0x02, 0x23, 0x07]);
        using var firstVariant = new ShaderVariant(bytecode);
        using var secondVariant = new ShaderVariant(bytecode);
        RasterizerState raster = new();

        var baseline = new GraphicsPipelineKey(firstVariant, 7, Topology.Triangles, in raster, index32Bit: false);
        var same = new GraphicsPipelineKey(firstVariant, 7, Topology.Triangles, in raster, index32Bit: false);
        Assert.Equal(baseline, same);
        Assert.Equal(baseline.GetHashCode(), same.GetHashCode());

        raster.DepthWrite = false;
        var differentRaster = new GraphicsPipelineKey(firstVariant, 7, Topology.Triangles, in raster, index32Bit: false);
        Assert.NotEqual(baseline, differentRaster);
        Assert.NotEqual(baseline, new GraphicsPipelineKey(secondVariant, 7, Topology.Triangles, in raster, index32Bit: false));
        Assert.NotEqual(differentRaster, new GraphicsPipelineKey(firstVariant, 8, Topology.Triangles, in raster, index32Bit: false));
        Assert.NotEqual(differentRaster, new GraphicsPipelineKey(firstVariant, 7, Topology.Lines, in raster, index32Bit: false));
        Assert.NotEqual(differentRaster, new GraphicsPipelineKey(firstVariant, 7, Topology.Triangles, in raster, index32Bit: true));
    }

    [Fact]
    public void ShaderDescriptorLayoutPlan_Assigns_CollisionFree_Physical_Bindings()
    {
        var layout = new ShaderBindingLayout
        {
            Buffers =
            [
                new ShaderBindingSlot(ShaderBindingKind.Buffer, 0, "GlobalUniforms"),
                new ShaderBindingSlot(ShaderBindingKind.Buffer, 2, "MaterialUniforms"),
            ],
            Textures =
            [
                new ShaderBindingSlot(ShaderBindingKind.Texture, 0, "MainTexture"),
                new ShaderBindingSlot(ShaderBindingKind.Texture, 3, "ShadowTexture"),
            ],
            Samplers =
            [
                new ShaderBindingSlot(ShaderBindingKind.Sampler, 0, "MainSampler"),
                new ShaderBindingSlot(ShaderBindingKind.Sampler, 3, "ShadowSampler"),
            ],
        };

        ShaderDescriptorLayoutPlan plan = ShaderDescriptorLayoutPlan.Create(layout);

        Assert.Equal(0, plan.BufferBindingBase);
        Assert.Equal(3, plan.TextureBindingBase);
        Assert.Equal(7, plan.SamplerBindingBase);
        int[] physicalBindings = Array.ConvertAll(plan.Bindings, binding => binding.PhysicalBinding);
        Assert.Collection(
            physicalBindings,
            binding => Assert.Equal(0, binding),
            binding => Assert.Equal(2, binding),
            binding => Assert.Equal(3, binding),
            binding => Assert.Equal(6, binding),
            binding => Assert.Equal(7, binding),
            binding => Assert.Equal(10, binding));
        Assert.Equal(plan.Bindings.Length, new System.Collections.Generic.HashSet<int>(physicalBindings).Count);
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
    public void Optional_Vulkan_ShaderLayout_Creates_Or_Skips()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Vulkan,
                Debug = false,
            });
            device.Initialize(null);
            var bytecode = new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.SpirV,
                [0x03, 0x02, 0x23, 0x07],
                [0x03, 0x02, 0x23, 0x07],
                new ShaderBindingLayout
                {
                    Buffers = [new ShaderBindingSlot(ShaderBindingKind.Buffer, 0, "GlobalUniforms")],
                    Textures = [new ShaderBindingSlot(ShaderBindingKind.Texture, 0, "MainTexture")],
                    Samplers = [new ShaderBindingSlot(ShaderBindingKind.Sampler, 0, "MainSampler")],
                });
            using var variant = new ShaderVariant(bytecode);

            Backends.Vulkan.VkShaderLayoutResource first = device.GetOrCreateShaderLayout(variant);
            Backends.Vulkan.VkShaderLayoutResource second = device.GetOrCreateShaderLayout(variant);

            Assert.Same(first, second);
            Assert.NotEqual(0ul, first.DescriptorSetLayout.Handle);
            Assert.NotEqual(0ul, first.PipelineLayout.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected Vulkan layout failure: {ex.GetType().FullName}: {ex.Message}");
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
