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
    public void Optional_D3D12_ShaderLayout_Creates_Or_Skips()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Direct3D12,
                Debug = false,
            });
            device.Initialize(null);
            var bytecode = new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.Dxil,
                [0x44, 0x58, 0x42, 0x43],
                [0x44, 0x58, 0x42, 0x43],
                new ShaderBindingLayout
                {
                    Buffers = [new ShaderBindingSlot(ShaderBindingKind.Buffer, 0, "GlobalUniforms")],
                    Textures = [new ShaderBindingSlot(ShaderBindingKind.Texture, 0, "MainTexture")],
                    Samplers = [new ShaderBindingSlot(ShaderBindingKind.Sampler, 0, "MainSampler")],
                });
            using var variant = new ShaderVariant(bytecode);

            Backends.D3D12.D3D12ShaderLayoutResource first = device.GetOrCreateShaderLayout(variant);
            Backends.D3D12.D3D12ShaderLayoutResource second = device.GetOrCreateShaderLayout(variant);

            Assert.Same(first, second);
            Assert.NotEqual(nint.Zero, first.RootSignature.NativePointer);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected D3D12 layout failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_FullscreenPso_Creates_Or_Skips()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "float4 main(uint id : SV_VertexID) : SV_Position { float2 p = float2((id << 1) & 2, id & 2); return float4(p * 2 - 1, 0, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(1, 0, 1, 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Direct3D12,
                Debug = false,
            });
            device.Initialize(null);
            var bytecode = new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.Dxil,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout);
            using var variant = new ShaderVariant(bytecode);
            RasterizerState raster = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            var key = new GraphicsPipelineKey(variant, 0, Topology.Triangles, in raster, index32Bit: false);

            Vortice.Direct3D12.ID3D12PipelineState first = device.GetOrCreateGraphicsPipeline(key, variant);
            Vortice.Direct3D12.ID3D12PipelineState second = device.GetOrCreateGraphicsPipeline(key, variant);

            Assert.Same(first, second);
            Assert.NotEqual(nint.Zero, first.NativePointer);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected D3D12 PSO failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_VertexInputPso_Creates_Or_Skips()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; float2 uv : TEXCOORD0; }; float4 main(VSInput input) : SV_Position { return float4(input.position.xy + input.uv * 0.0, input.position.z, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 1, 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Direct3D12,
                Debug = false,
            });
            device.Initialize(null);
            var bytecode = new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.Dxil,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout);
            using var variant = new ShaderVariant(bytecode);
            var format = new VertexFormat(
            [
                new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3),
                new(VertexFormat.VertexSemantic.TexCoord0, VertexFormat.VertexType.Float, 2),
            ]);
            const uint vertexArrayHandle = 41;
            device.VertexArrays[vertexArrayHandle] = new Backends.D3D12.D3D12VertexArrayResource
            {
                Format = format,
            };
            RasterizerState raster = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            var key = new GraphicsPipelineKey(variant, vertexArrayHandle, Topology.Triangles, in raster, index32Bit: false);

            Vortice.Direct3D12.ID3D12PipelineState first = device.GetOrCreateGraphicsPipeline(key, variant);
            Vortice.Direct3D12.ID3D12PipelineState second = device.GetOrCreateGraphicsPipeline(key, variant);

            Assert.Same(first, second);
            Assert.NotEqual(nint.Zero, first.NativePointer);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected D3D12 vertex-input PSO failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_InstancedInputPso_Creates_Or_Skips()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; float4 modelRow0 : TEXCOORD8; float4 color : TEXCOORD12; }; float4 main(VSInput input) : SV_Position { return float4(input.position + input.modelRow0.xyz * 0.0 + input.color.xyz * 0.0, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(1, 1, 0, 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Direct3D12,
                Debug = false,
            });
            device.Initialize(null);
            var bytecode = new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.Dxil,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout);
            using var variant = new ShaderVariant(bytecode);
            var format = new VertexFormat(
            [
                new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3),
            ]);
            var instanceFormat = new VertexFormat(
            [
                new((VertexFormat.VertexSemantic)8, VertexFormat.VertexType.Float, 4, divisor: 1),
                new((VertexFormat.VertexSemantic)12, VertexFormat.VertexType.Float, 4, divisor: 1),
            ]);
            const uint vertexArrayHandle = 42;
            device.VertexArrays[vertexArrayHandle] = new Backends.D3D12.D3D12VertexArrayResource
            {
                Format = format,
                InstanceFormat = instanceFormat,
            };
            RasterizerState raster = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            var key = new GraphicsPipelineKey(variant, vertexArrayHandle, Topology.Triangles, in raster, index32Bit: false);

            Vortice.Direct3D12.ID3D12PipelineState first = device.GetOrCreateGraphicsPipeline(key, variant);
            Vortice.Direct3D12.ID3D12PipelineState second = device.GetOrCreateGraphicsPipeline(key, variant);

            Assert.Same(first, second);
            Assert.NotEqual(nint.Zero, first.NativePointer);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected D3D12 instanced-input PSO failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_DrawArrays_Executes_Or_Skips()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 0, 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Direct3D12,
                Debug = false,
            });
            device.Initialize(null);
            var bytecode = new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.Dxil,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout);
            using var variant = new ShaderVariant(bytecode);
            float[] vertices =
            [
                -1f, -1f, 0f,
                0f, 1f, 0f,
                1f, -1f, 0f,
            ];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            var format = new VertexFormat(
            [
                new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3),
            ]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-draw-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-draw-arrays"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected D3D12 DrawArrays failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_UniformBufferRootDescriptor_Binds_Or_Skips()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "cbuffer DrawData : register(b0) { float4 offset; }; struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position + offset.xyz, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 1, 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.Dxil,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            float[] drawData = [0f, 0f, 0f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            ReadOnlySpan<byte> uniformBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(drawData.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var uniformBuffer = new GraphicsBuffer(BufferType.UniformBuffer, uniformBytes, dynamic: true);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-cbv-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateBuffer(uniformBuffer, dynamic: true, uniformBytes);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-cbv-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetBuffer("DrawData", uniformBuffer);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 CBV bind failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_DescriptorHeapSlots_Allocate_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Direct3D12,
            });
            device.Initialize(null);

            Backends.D3D12.D3D12DescriptorAllocation firstSrv = device.AllocateSrvDescriptor();
            Backends.D3D12.D3D12DescriptorAllocation secondSrv = device.AllocateSrvDescriptor();
            Backends.D3D12.D3D12DescriptorAllocation firstSampler = device.AllocateSamplerDescriptor();
            Backends.D3D12.D3D12DescriptorAllocation secondSampler = device.AllocateSamplerDescriptor();

            Assert.Equal(0, firstSrv.Index);
            Assert.Equal(1, secondSrv.Index);
            Assert.NotEqual(0ul, firstSrv.Cpu.Ptr);
            Assert.NotEqual(0ul, firstSrv.Gpu.Ptr);
            Assert.NotEqual(firstSrv.Cpu.Ptr, secondSrv.Cpu.Ptr);
            Assert.NotEqual(firstSrv.Gpu.Ptr, secondSrv.Gpu.Ptr);

            Assert.Equal(0, firstSampler.Index);
            Assert.Equal(1, secondSampler.Index);
            Assert.NotEqual(0ul, firstSampler.Cpu.Ptr);
            Assert.NotEqual(0ul, firstSampler.Gpu.Ptr);
            Assert.NotEqual(firstSampler.Cpu.Ptr, secondSampler.Cpu.Ptr);
            Assert.NotEqual(firstSampler.Gpu.Ptr, secondSampler.Gpu.Ptr);

            for (int i = 2; i < 1024; i++)
                device.AllocateSrvDescriptor();
            InvalidOperationException srvFull = Assert.Throws<InvalidOperationException>(() => device.AllocateSrvDescriptor());
            Assert.Contains("CBV/SRV/UAV", srvFull.Message, StringComparison.Ordinal);

            for (int i = 2; i < 64; i++)
                device.AllocateSamplerDescriptor();
            InvalidOperationException samplerFull = Assert.Throws<InvalidOperationException>(() => device.AllocateSamplerDescriptor());
            Assert.Contains("sampler", samplerFull.Message, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected D3D12 descriptor heap allocation failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_TextureSrvSampler_Creates_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Direct3D12,
            });
            device.Initialize(null);
            using var texture = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);

            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-texture-descriptor-create"))
            {
                create.EncodeCreateTexture(texture);
                create.EncodeAllocateTexture2D(texture, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                device.Execute(create, wait: true);
            }

            Backends.D3D12.D3D12TextureResource resource = device.Textures[texture.Handle];
            Assert.NotNull(resource.Resource);
            Assert.True(resource.HasSrvDescriptor);
            Assert.True(resource.HasSamplerDescriptor);
            Assert.NotEqual(0ul, resource.SrvDescriptor.Cpu.Ptr);
            Assert.NotEqual(0ul, resource.SrvDescriptor.Gpu.Ptr);
            Assert.NotEqual(0ul, resource.SamplerDescriptor.Cpu.Ptr);
            Assert.NotEqual(0ul, resource.SamplerDescriptor.Gpu.Ptr);
            int srvIndex = resource.SrvDescriptor.Index;
            int samplerIndex = resource.SamplerDescriptor.Index;

            using (CommandBuffer reallocate = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-texture-descriptor-reallocate"))
            {
                reallocate.EncodeAllocateTexture2D(texture, 0, 2, 2, 0, ReadOnlySpan<byte>.Empty);
                device.Execute(reallocate, wait: true);
            }

            resource = device.Textures[texture.Handle];
            Assert.Equal(2u, resource.Width);
            Assert.Equal(2u, resource.Height);
            Assert.Equal(srvIndex, resource.SrvDescriptor.Index);
            Assert.Equal(samplerIndex, resource.SamplerDescriptor.Index);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected D3D12 texture descriptor creation failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_TextureDescriptorTables_Bind_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; struct VSOutput { float4 position : SV_Position; float2 uv : TEXCOORD0; }; VSOutput main(VSInput input) { VSOutput o; o.position = float4(input.position, 1); o.uv = input.position.xy * 0.5 + 0.5; return o; }",
            FragmentSource = "Texture2D MainTexture : register(t0); SamplerState MainSampler : register(s0); float4 main(float2 uv : TEXCOORD0) : SV_Target { return MainTexture.Sample(MainSampler, uv); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Direct3D12,
            });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.Dxil,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var texture = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);

            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-texture-table-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(texture);
                create.EncodeAllocateTexture2D(texture, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-texture-table-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetTexture("MainTexture", texture);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected D3D12 texture descriptor table bind failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_InitialTextureUpload_RoundTrips_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Direct3D12,
            });
            device.Initialize(null);
            using var texture = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            byte[] pixels =
            [
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255,
            ];

            using (CommandBuffer upload = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-texture-upload"))
            {
                upload.EncodeCreateTexture(texture);
                upload.EncodeAllocateTexture2D(texture, 0, 2, 2, 0, pixels);
                device.Execute(upload, wait: true);
            }

            Backends.D3D12.D3D12TextureResource resource = device.Textures[texture.Handle];
            Assert.NotNull(resource.Resource);
            Assert.Equal(Vortice.Direct3D12.ResourceStates.PixelShaderResource, resource.State);
            byte[] readback = device.ReadTexture2D(resource.Resource!, 2, 2, 4, resource.State);
            Assert.Equal(pixels, readback);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected D3D12 initial texture upload failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_SamplerState_Updates_And_Draws_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; struct VSOutput { float4 position : SV_Position; float2 uv : TEXCOORD0; }; VSOutput main(VSInput input) { VSOutput o; o.position = float4(input.position, 1); o.uv = input.position.xy * 0.5 + 0.5; return o; }",
            FragmentSource = "Texture2D MainTexture : register(t0); SamplerState MainSampler : register(s0); float4 main(float2 uv : TEXCOORD0) : SV_Target { return MainTexture.Sample(MainSampler, uv); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Direct3D12,
            });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.Dxil,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var texture = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);

            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-sampler-state-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(texture);
                create.EncodeAllocateTexture2D(texture, 0, 1, 1, 0, new byte[] { 255, 255, 255, 255 });
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            int originalSamplerIndex = device.Textures[texture.Handle].SamplerDescriptor.Index;
            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-sampler-state-draw"))
            {
                draw.EncodeSetTextureWrap(texture, 0, TextureWrap.ClampToEdge);
                draw.EncodeSetTextureWrap(texture, 1, TextureWrap.MirroredRepeat);
                draw.EncodeSetTextureFilters(texture, TextureMin.Nearest, TextureMag.Nearest);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetTexture("MainTexture", texture);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Backends.D3D12.D3D12TextureResource resource = device.Textures[texture.Handle];
            Assert.Equal(TextureWrap.ClampToEdge, resource.WrapS);
            Assert.Equal(TextureWrap.MirroredRepeat, resource.WrapT);
            Assert.Equal(TextureMin.Nearest, resource.MinFilter);
            Assert.Equal(TextureMag.Nearest, resource.MagFilter);
            Assert.True(resource.SamplerDescriptor.Index > originalSamplerIndex);
            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected D3D12 sampler state update failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_DrawIndexed_Executes_Or_Skips()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(0, 0, 1, 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Direct3D12,
                Debug = false,
            });
            device.Initialize(null);
            var bytecode = new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.Dxil,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout);
            using var variant = new ShaderVariant(bytecode);
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ushort[] indices = [0, 1, 2];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            ReadOnlySpan<byte> indexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(indices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var indexBuffer = new GraphicsBuffer(BufferType.ElementsBuffer, indexBytes, dynamic: true);
            var format = new VertexFormat(
            [
                new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3),
            ]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, indexBuffer);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-indexed-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateBuffer(indexBuffer, dynamic: true, indexBytes);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-draw-indexed"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawIndexed(vertexArray, Topology.Triangles, 3, index32bit: false);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected D3D12 DrawIndexed failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_DrawIndexedInstanced_Executes_Or_Skips()
    {
        if (!OperatingSystem.IsWindows()) return;
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; float4 offset : TEXCOORD8; }; float4 main(VSInput input) : SV_Position { return float4(input.position + input.offset.xyz, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(1, 1, 0, 1); }",
        });
        if (!compiled.Success) return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ushort[] indices = [0, 1, 2];
            float[] instances = [0f, 0f, 0f, 0f];
            ReadOnlySpan<byte> vb = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            ReadOnlySpan<byte> ib = System.Runtime.InteropServices.MemoryMarshal.AsBytes(indices.AsSpan());
            ReadOnlySpan<byte> inst = System.Runtime.InteropServices.MemoryMarshal.AsBytes(instances.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vb, true);
            using var indexBuffer = new GraphicsBuffer(BufferType.ElementsBuffer, ib, true);
            using var instanceBuffer = new GraphicsBuffer(BufferType.VertexBuffer, inst, true);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            var instanceFormat = new VertexFormat([new((VertexFormat.VertexSemantic)8, VertexFormat.VertexType.Float, 4, divisor: 1)]);
            using var vao = new GraphicsVertexArray(format, vertexBuffer, indexBuffer, instanceFormat, instanceBuffer);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-instanced-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, true, vb);
                create.EncodeCreateBuffer(indexBuffer, true, ib);
                create.EncodeCreateBuffer(instanceBuffer, true, inst);
                create.EncodeCreateVertexArray(vao);
                device.Execute(create, true);
            }
            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-instanced-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawIndexedInstanced(vao, Topology.Triangles, 3, 1);
                device.Execute(draw, true);
            }
            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 instanced draw failure: {ex.GetType().FullName}: {ex.Message}");
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
    public void Optional_Vulkan_DescriptorSets_Allocate_Independently_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Vulkan,
                Debug = false,
            });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.SpirV,
                [0x03, 0x02, 0x23, 0x07],
                [0x03, 0x02, 0x23, 0x07],
                new ShaderBindingLayout
                {
                    Buffers = [new ShaderBindingSlot(ShaderBindingKind.Buffer, 0, "GlobalUniforms")],
                    Textures = [new ShaderBindingSlot(ShaderBindingKind.Texture, 0, "MainTexture")],
                    Samplers = [new ShaderBindingSlot(ShaderBindingKind.Sampler, 0, "MainSampler")],
                }));
            Backends.Vulkan.VkShaderLayoutResource layout = device.GetOrCreateShaderLayout(variant);

            Silk.NET.Vulkan.DescriptorSet first = device.AllocateDescriptorSet(layout);
            Silk.NET.Vulkan.DescriptorSet second = device.AllocateDescriptorSet(layout);
            try
            {
                Assert.NotEqual(0ul, first.Handle);
                Assert.NotEqual(0ul, second.Handle);
                Assert.NotEqual(first.Handle, second.Handle);
            }
            finally
            {
                device.FreeDescriptorSet(first);
                device.FreeDescriptorSet(second);
            }
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected Vulkan descriptor allocation failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_ShaderModules_Create_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "float4 main() : SV_Position { return float4(0, 0, 0, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(1, 0, 1, 1); }",
        });
        if (!compiled.Success)
            return;

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
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout);
            using var variant = new ShaderVariant(bytecode);

            Backends.Vulkan.VkShaderModuleResource first = device.GetOrCreateShaderModules(variant);
            Backends.Vulkan.VkShaderModuleResource second = device.GetOrCreateShaderModules(variant);

            Assert.Same(first, second);
            Assert.NotEqual(0ul, first.VertexModule.Handle);
            Assert.NotEqual(0ul, first.FragmentModule.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected Vulkan shader-module failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_FullscreenPipeline_Creates_Or_Skips()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "float4 main(uint id : SV_VertexID) : SV_Position { float2 p = float2((id << 1) & 2, id & 2); return float4(p * 2 - 1, 0, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(1, 0, 1, 1); }",
        });
        if (!compiled.Success)
            return;

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
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout);
            using var variant = new ShaderVariant(bytecode);
            RasterizerState raster = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            var key = new GraphicsPipelineKey(variant, 0, Topology.Triangles, in raster, index32Bit: false);

            Silk.NET.Vulkan.Pipeline first = device.GetOrCreateGraphicsPipeline(
                key,
                variant,
                Silk.NET.Vulkan.Format.R8G8B8A8Unorm);
            Silk.NET.Vulkan.Pipeline second = device.GetOrCreateGraphicsPipeline(
                key,
                variant,
                Silk.NET.Vulkan.Format.R8G8B8A8Unorm);

            Assert.Equal(first.Handle, second.Handle);
            Assert.NotEqual(0ul, first.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected Vulkan pipeline failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_VertexInputPipeline_Creates_Or_Skips()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; [[vk::location(1)]] float2 uv : TEXCOORD0; }; float4 main(VSInput input) : SV_Position { return float4(input.position.xy + input.uv * 0.0, input.position.z, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 1, 1); }",
        });
        if (!compiled.Success)
            return;

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
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout);
            using var variant = new ShaderVariant(bytecode);
            var format = new VertexFormat(
            [
                new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3),
                new(VertexFormat.VertexSemantic.TexCoord0, VertexFormat.VertexType.Float, 2),
            ]);
            const uint vertexArrayHandle = 51;
            device.VertexArrays[vertexArrayHandle] = new Backends.Vulkan.VkVertexArrayResource
            {
                Format = format,
            };
            RasterizerState raster = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            var key = new GraphicsPipelineKey(variant, vertexArrayHandle, Topology.Triangles, in raster, index32Bit: false);

            Silk.NET.Vulkan.Pipeline first = device.GetOrCreateGraphicsPipeline(
                key,
                variant,
                Silk.NET.Vulkan.Format.R8G8B8A8Unorm);
            Silk.NET.Vulkan.Pipeline second = device.GetOrCreateGraphicsPipeline(
                key,
                variant,
                Silk.NET.Vulkan.Format.R8G8B8A8Unorm);

            Assert.Equal(first.Handle, second.Handle);
            Assert.NotEqual(0ul, first.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected Vulkan vertex-input pipeline failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_InstancedInputPipeline_Creates_Or_Skips()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; [[vk::location(8)]] float4 modelRow0 : TEXCOORD8; [[vk::location(12)]] float4 color : TEXCOORD12; }; float4 main(VSInput input) : SV_Position { return float4(input.position + input.modelRow0.xyz * 0.0 + input.color.xyz * 0.0, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(1, 1, 0, 1); }",
        });
        if (!compiled.Success)
            return;

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
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout);
            using var variant = new ShaderVariant(bytecode);
            var format = new VertexFormat(
            [
                new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3),
            ]);
            var instanceFormat = new VertexFormat(
            [
                new((VertexFormat.VertexSemantic)8, VertexFormat.VertexType.Float, 4, divisor: 1),
                new((VertexFormat.VertexSemantic)12, VertexFormat.VertexType.Float, 4, divisor: 1),
            ]);
            const uint vertexArrayHandle = 52;
            device.VertexArrays[vertexArrayHandle] = new Backends.Vulkan.VkVertexArrayResource
            {
                Format = format,
                InstanceFormat = instanceFormat,
            };
            RasterizerState raster = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            var key = new GraphicsPipelineKey(variant, vertexArrayHandle, Topology.Triangles, in raster, index32Bit: false);

            Silk.NET.Vulkan.Pipeline first = device.GetOrCreateGraphicsPipeline(
                key,
                variant,
                Silk.NET.Vulkan.Format.R8G8B8A8Unorm);
            Silk.NET.Vulkan.Pipeline second = device.GetOrCreateGraphicsPipeline(
                key,
                variant,
                Silk.NET.Vulkan.Format.R8G8B8A8Unorm);

            Assert.Equal(first.Handle, second.Handle);
            Assert.NotEqual(0ul, first.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected Vulkan instanced-input pipeline failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_DrawArrays_Executes_Or_Skips()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 0, 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Vulkan,
                Debug = false,
            });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.SpirV,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            var format = new VertexFormat(
            [
                new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3),
            ]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-draw-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-draw-arrays"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected Vulkan DrawArrays failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_UniformBufferDescriptor_Binds_Or_Skips()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "cbuffer DrawData : register(b0) { float4 offset; }; struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position + offset.xyz, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 1, 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.SpirV,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            float[] drawData = [0f, 0f, 0f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            ReadOnlySpan<byte> uniformBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(drawData.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var uniformBuffer = new GraphicsBuffer(BufferType.UniformBuffer, uniformBytes, dynamic: true);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-descriptor-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateBuffer(uniformBuffer, dynamic: true, uniformBytes);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-descriptor-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetBuffer("DrawData", uniformBuffer);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan descriptor bind failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_MultipleUniformBufferDescriptors_Bind_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "cbuffer FrameData : register(b0) { float4 frameOffset; }; cbuffer DrawData : register(b1) { float4 drawOffset; }; struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position + frameOffset.xyz + drawOffset.xyz, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(1, 0, 1, 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.SpirV,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            float[] frameData = [0f, 0f, 0f, 0f];
            float[] drawData = [0f, 0f, 0f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            ReadOnlySpan<byte> frameBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(frameData.AsSpan());
            ReadOnlySpan<byte> drawBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(drawData.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var frameBuffer = new GraphicsBuffer(BufferType.UniformBuffer, frameBytes, dynamic: true);
            using var drawBuffer = new GraphicsBuffer(BufferType.UniformBuffer, drawBytes, dynamic: true);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-multi-descriptor-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateBuffer(frameBuffer, dynamic: true, frameBytes);
                create.EncodeCreateBuffer(drawBuffer, dynamic: true, drawBytes);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-multi-descriptor-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetBuffer("FrameData", frameBuffer);
                draw.SetBuffer("DrawData", drawBuffer);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan multi-descriptor bind failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_TextureSamplerDescriptors_Bind_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; struct VSOutput { float4 position : SV_Position; [[vk::location(0)]] float2 uv : TEXCOORD0; }; VSOutput main(VSInput input) { VSOutput o; o.position = float4(input.position, 1); o.uv = input.position.xy * 0.5 + 0.5; return o; }",
            FragmentSource = "Texture2D MainTexture : register(t0); SamplerState MainSampler : register(s0); float4 main([[vk::location(0)]] float2 uv : TEXCOORD0) : SV_Target { return MainTexture.Sample(MainSampler, uv); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var texture = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-texture-descriptor-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(texture);
                create.EncodeAllocateTexture2D(texture, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-texture-descriptor-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetTexture("MainTexture", texture);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan texture descriptor bind failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_MultipleTextureSamplerDescriptors_Bind_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; struct VSOutput { float4 position : SV_Position; [[vk::location(0)]] float2 uv : TEXCOORD0; }; VSOutput main(VSInput input) { VSOutput o; o.position = float4(input.position, 1); o.uv = input.position.xy * 0.5 + 0.5; return o; }",
            FragmentSource = "Texture2D BaseTexture : register(t0); SamplerState BaseSampler : register(s0); Texture2D DetailTexture : register(t3); SamplerState DetailSampler : register(s3); float4 main([[vk::location(0)]] float2 uv : TEXCOORD0) : SV_Target { return (BaseTexture.Sample(BaseSampler, uv) + DetailTexture.Sample(DetailSampler, uv)) * 0.5; }",
        });
        if (!compiled.Success)
            return;

        ShaderBindingLayout bindingLayout = Assert.IsType<ShaderBindingLayout>(compiled.BindingLayout);
        Assert.Equal(2, bindingLayout.Textures.Length);
        Assert.Equal(0, bindingLayout.Textures[0].Slot);
        Assert.Equal(3, bindingLayout.Textures[1].Slot);
        Assert.Equal(2, bindingLayout.Samplers.Length);
        Assert.Equal(0, bindingLayout.Samplers[0].Slot);
        Assert.Equal(3, bindingLayout.Samplers[1].Slot);

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Vulkan,
                Debug = false,
            });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.SpirV,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                bindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var baseTexture = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var detailTexture = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-multi-texture-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(baseTexture);
                create.EncodeAllocateTexture2D(baseTexture, 0, 1, 1, 0, new byte[] { 255, 0, 0, 255 });
                create.EncodeCreateTexture(detailTexture);
                create.EncodeAllocateTexture2D(detailTexture, 0, 1, 1, 0, new byte[] { 0, 0, 255, 255 });
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-multi-texture-draw"))
            {
                draw.EncodeSetTextureWrap(detailTexture, 0, TextureWrap.ClampToEdge);
                draw.EncodeSetTextureFilters(detailTexture, TextureMin.Nearest, TextureMag.Nearest);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetTexture("BaseTexture", baseTexture);
                draw.SetTexture("DetailTexture", detailTexture);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Backends.Vulkan.VkImageResource detailResource = device.Images[detailTexture.Handle];
            Assert.Equal(TextureWrap.ClampToEdge, detailResource.WrapS);
            Assert.Equal(TextureMin.Nearest, detailResource.MinFilter);
            Assert.Equal(TextureMag.Nearest, detailResource.MagFilter);
            Assert.Equal(0, device.PendingSamplerRetirementCount);
            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected Vulkan multiple texture descriptor bind failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_InitialTextureUpload_RoundTrips_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Vulkan,
            });
            device.Initialize(null);
            using var texture = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            byte[] pixels =
            [
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255,
            ];

            using (CommandBuffer upload = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-texture-upload"))
            {
                upload.EncodeCreateTexture(texture);
                upload.EncodeAllocateTexture2D(texture, 0, 2, 2, 0, pixels);
                device.Execute(upload, wait: true);
            }

            Backends.Vulkan.VkImageResource resource = device.Images[texture.Handle];
            Assert.NotEqual(0ul, resource.Image.Handle);
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal, resource.Layout);
            byte[] readback = device.ReadTexture2D(resource, 4);
            Assert.Equal(pixels, readback);
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected Vulkan initial texture upload failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_SamplerState_Updates_And_Draws_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; struct VSOutput { float4 position : SV_Position; [[vk::location(0)]] float2 uv : TEXCOORD0; }; VSOutput main(VSInput input) { VSOutput o; o.position = float4(input.position, 1); o.uv = input.position.xy * 0.5 + 0.5; return o; }",
            FragmentSource = "Texture2D MainTexture : register(t0); SamplerState MainSampler : register(s0); float4 main([[vk::location(0)]] float2 uv : TEXCOORD0) : SV_Target { return MainTexture.Sample(MainSampler, uv); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Vulkan,
                Debug = false,
            });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.SpirV,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var texture = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-sampler-state-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(texture);
                create.EncodeAllocateTexture2D(texture, 0, 1, 1, 0, new byte[] { 255, 255, 255, 255 });
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            ulong originalSampler = device.Images[texture.Handle].Sampler.Handle;
            RasterizerState raster = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-sampler-state-draw"))
            {
                draw.EncodeSetTextureWrap(texture, 0, TextureWrap.ClampToEdge);
                draw.EncodeSetTextureWrap(texture, 1, TextureWrap.MirroredRepeat);
                draw.EncodeSetTextureFilters(texture, TextureMin.Nearest, TextureMag.Nearest);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetTexture("MainTexture", texture);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: false);
            }

            Backends.Vulkan.VkImageResource resource = device.Images[texture.Handle];
            Assert.Equal(TextureWrap.ClampToEdge, resource.WrapS);
            Assert.Equal(TextureWrap.MirroredRepeat, resource.WrapT);
            Assert.Equal(TextureMin.Nearest, resource.MinFilter);
            Assert.Equal(TextureMag.Nearest, resource.MagFilter);
            Assert.NotEqual(originalSampler, resource.Sampler.Handle);
            Assert.True(device.PendingSamplerRetirementCount >= 3);

            device.WaitIdle();
            Assert.Equal(0, device.PendingSamplerRetirementCount);
            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected Vulkan sampler state update failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_DrawIndexed_Executes_Or_Skips()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(0, 0, 1, 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Vulkan,
                Debug = false,
            });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.SpirV,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ushort[] indices = [0, 1, 2];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            ReadOnlySpan<byte> indexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(indices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var indexBuffer = new GraphicsBuffer(BufferType.ElementsBuffer, indexBytes, dynamic: true);
            var format = new VertexFormat(
            [
                new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3),
            ]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, indexBuffer);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-indexed-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateBuffer(indexBuffer, dynamic: true, indexBytes);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-draw-indexed"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawIndexed(vertexArray, Topology.Triangles, 3, index32bit: false);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected Vulkan DrawIndexed failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_DrawIndexedInstanced_Executes_Or_Skips()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; [[vk::location(8)]] float4 offset : TEXCOORD8; }; float4 main(VSInput input) : SV_Position { return float4(input.position + input.offset.xyz, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(1, 1, 0, 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions
            {
                Backend = GraphicsBackend.Vulkan,
                Debug = false,
            });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(
                ShaderLanguage.Hlsl,
                ShaderBytecodeFormat.SpirV,
                compiled.VertexBytecode!,
                compiled.FragmentBytecode!,
                compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ushort[] indices = [0, 1, 2];
            float[] instances = [0f, 0f, 0f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            ReadOnlySpan<byte> indexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(indices.AsSpan());
            ReadOnlySpan<byte> instanceBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(instances.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var indexBuffer = new GraphicsBuffer(BufferType.ElementsBuffer, indexBytes, dynamic: true);
            using var instanceBuffer = new GraphicsBuffer(BufferType.VertexBuffer, instanceBytes, dynamic: true);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            var instanceFormat = new VertexFormat([new((VertexFormat.VertexSemantic)8, VertexFormat.VertexType.Float, 4, divisor: 1)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, indexBuffer, instanceFormat, instanceBuffer);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-instanced-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateBuffer(indexBuffer, dynamic: true, indexBytes);
                create.EncodeCreateBuffer(instanceBuffer, dynamic: true, instanceBytes);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-draw-indexed-instanced"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawIndexedInstanced(vertexArray, Topology.Triangles, 3, 1);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(
                IsExpectedGpuUnavailable(ex),
                $"Unexpected Vulkan DrawIndexedInstanced failure: {ex.GetType().FullName}: {ex.Message}");
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
