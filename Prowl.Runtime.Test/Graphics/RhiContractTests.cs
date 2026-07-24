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
    public void Optional_D3D12_GlobalUniforms_AutoBind_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "cbuffer GlobalUniforms : register(b0) { float4 Tint; }; float4 main() : SV_Target { return Tint; }",
        });
        if (!compiled.Success)
            return;

        GraphicsBuffer? previousGlobalBuffer = GetGlobalUniformBuffer();
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            Float4 tint = new(0.25f, 0.5f, 0.75f, 1f);
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            ReadOnlySpan<byte> uniformBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref tint, 1));
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var uniformBuffer = new GraphicsBuffer(BufferType.UniformBuffer, uniformBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = color }],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-global-uniform-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateBuffer(uniformBuffer, dynamic: true, uniformBytes);
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }
            SetGlobalUniformBuffer(uniformBuffer);

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-global-uniform-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            byte[] readback = device.ReadTexture2D(device.Textures[color.Handle].Resource!, 1, 1, 4, device.Textures[color.Handle].State);
            Assert.InRange(readback[0], (byte)63, (byte)64);
            Assert.InRange(readback[1], (byte)127, (byte)128);
            Assert.InRange(readback[2], (byte)191, (byte)192);
            Assert.Equal((byte)255, readback[3]);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-global-uniform-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 global-uniform auto-bind failure: {ex.GetType().FullName}: {ex.Message}");
        }
        finally
        {
            SetGlobalUniformBuffer(previousGlobalBuffer);
        }
    }

    [Fact]
    public void Optional_D3D12_ObjectUniforms_Pack_Per_Draw_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "cbuffer ObjectUniforms : register(b1) { float4x4 prowl_ObjectToWorld; float4x4 prowl_WorldToObject; float4x4 prowl_PrevObjectToWorld; int _ObjectID; float3 padding; }; float4 main() : SV_Target { return float4(prowl_ObjectToWorld[0][0], prowl_WorldToObject[1][1], prowl_PrevObjectToWorld[2][2], _ObjectID / 255.0); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = color }],
                2,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-object-uniform-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-object-uniform-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);

                Float4x4 firstObject = Float4x4.CreateScale(0.25f, 1f, 1f);
                Float4x4 firstInverse = Float4x4.CreateScale(1f, 0.5f, 1f);
                Float4x4 firstPrevious = Float4x4.CreateScale(1f, 1f, 0.75f);
                var firstProperties = new Rendering.PropertyState();
                firstProperties.SetInt("_ObjectID", 255);
                draw.SetInstanceProperties(firstProperties);
                draw.SetMatrix("prowl_ObjectToWorld", in firstObject);
                draw.SetMatrix("prowl_WorldToObject", in firstInverse);
                draw.SetMatrix("prowl_PrevObjectToWorld", in firstPrevious);
                draw.SetViewport(0, 0, 1, 1);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);

                Float4x4 secondObject = Float4x4.CreateScale(0.75f, 1f, 1f);
                Float4x4 secondInverse = Float4x4.CreateScale(1f, 0.25f, 1f);
                Float4x4 secondPrevious = Float4x4.CreateScale(1f, 1f, 0.5f);
                var secondProperties = new Rendering.PropertyState();
                secondProperties.SetInt("_ObjectID", 128);
                draw.SetInstanceProperties(secondProperties);
                draw.SetMatrix("prowl_ObjectToWorld", in secondObject);
                draw.SetMatrix("prowl_WorldToObject", in secondInverse);
                draw.SetMatrix("prowl_PrevObjectToWorld", in secondPrevious);
                draw.SetViewport(1, 0, 1, 1);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            byte[] readback = device.ReadTexture2D(device.Textures[color.Handle].Resource!, 2, 1, 4, device.Textures[color.Handle].State);
            Assert.InRange(readback[0], (byte)63, (byte)64);
            Assert.InRange(readback[1], (byte)127, (byte)128);
            Assert.InRange(readback[2], (byte)191, (byte)192);
            Assert.Equal((byte)255, readback[3]);
            Assert.InRange(readback[4], (byte)191, (byte)192);
            Assert.InRange(readback[5], (byte)63, (byte)64);
            Assert.InRange(readback[6], (byte)127, (byte)128);
            Assert.InRange(readback[7], (byte)127, (byte)128);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-object-uniform-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 object-uniform packing failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_UnlitMaterial_Packs_Defaults_And_Overrides_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "cbuffer UnlitMaterial : register(b2) { float2 _Tiling; float2 _Offset; float4 _MainColor; }; float4 main() : SV_Target { return float4(_Tiling.x, _Offset.y, _MainColor.r, _MainColor.a); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            using var shader = CreateUnlitMaterialContractShader();
            using var defaultsMaterial = new Resources.Material(shader);
            using var overridesMaterial = new Resources.Material(shader);
            overridesMaterial.SetVector("_Tiling", new Float2(0.75f, 1f));
            overridesMaterial.SetVector("_Offset", new Float2(0f, 0.25f));
            overridesMaterial.SetColor("_MainColor", new Color(0.5f, 0f, 0f, 0.5f));

            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = color }],
                2,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-unlit-material-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-unlit-material-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetMaterialProperties(defaultsMaterial);
                draw.SetViewport(0, 0, 1, 1);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetMaterialProperties(overridesMaterial);
                draw.SetViewport(1, 0, 1, 1);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            byte[] readback = device.ReadTexture2D(device.Textures[color.Handle].Resource!, 2, 1, 4, device.Textures[color.Handle].State);
            Assert.InRange(readback[0], (byte)63, (byte)64);
            Assert.InRange(readback[1], (byte)127, (byte)128);
            Assert.InRange(readback[2], (byte)191, (byte)192);
            Assert.Equal((byte)255, readback[3]);
            Assert.InRange(readback[4], (byte)191, (byte)192);
            Assert.InRange(readback[5], (byte)63, (byte)64);
            Assert.InRange(readback[6], (byte)127, (byte)128);
            Assert.InRange(readback[7], (byte)127, (byte)128);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-unlit-material-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Unlit material packing failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_StandardMaterial_Blocks_Pack_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunStandardMaterialBlocksContract(
                device,
                GraphicsBackend.Direct3D12,
                ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 8, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Standard material packing failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_StandardMaterial_Textures_Bind_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunStandardMaterialTextureContract(
                device,
                GraphicsBackend.Direct3D12,
                ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 2, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Standard texture binding failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_StandardTransparent_DefaultShader_Blends_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunStandardTransparentContract(
                device,
                GraphicsBackend.Direct3D12,
                ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 2, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 StandardTransparent acceptance failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_GradientSkybox_Material_Packs_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunGradientSkyboxMaterialContract(
                device,
                GraphicsBackend.Direct3D12,
                ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 2, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Gradient skybox packing failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_CubemapSkybox_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunCubemapSkyboxMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 6, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Cubemap skybox failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_ProceduralSkybox_Material_Packs_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunProceduralSkyboxMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 2, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Procedural skybox packing failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_Tonemapper_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunTonemapperMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 2, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Tonemapper material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_FXAA_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunFXAAMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 4, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 FXAA material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_Bloom_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunBloomMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 6, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Bloom material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_MotionBlur_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunMotionBlurMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 4, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 MotionBlur material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_AutoExposure_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunAutoExposureMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 10, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 AutoExposure material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_TAA_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunTAAMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 4, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 TAA material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_GTAO_Calculate_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunGTAOCalculateContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 4, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 GTAO calculate failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_GTAO_Blur_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunGTAOBlurContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 4, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 GTAO blur failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_GTAO_Composite_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunGTAOCompositeContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 4, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 GTAO composite failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_GTAO_Temporal_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunGTAOTemporalContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 4, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 GTAO temporal failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_GizmoIcon_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunGizmoIconMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 4, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Gizmo icon failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_DefaultUI_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunDefaultUIMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 6, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 DefaultUI failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_DefaultText_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunDefaultTextMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 4, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 DefaultText failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_DefaultTextMesh_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunDefaultTextMeshMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 4, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 DefaultTextMesh failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_Sprite_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunSpriteMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 4, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Sprite failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_Line_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunLineMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 2, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Line failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_Gizmos_GlobalDepth_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunGizmosGlobalDepthContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 2, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Gizmos failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_Grid_Material_And_GlobalDepth_Bind_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunGridMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 4, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Grid material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_UIVertex_Projection_Packs_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunUIVertexProjectionContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 2, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 UI projection failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_UIFragment_State_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunUIFragmentStateContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 10, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 UI fragment state failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_UIBlur_Material_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunUIBlurMaterialContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 4, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 UI blur material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_UIBackdropBlur_Chain_Binds_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            RunUIBackdropBlurChainContract(device, GraphicsBackend.Direct3D12, ShaderBytecodeFormat.Dxil,
                texture => device.ReadTexture2D(device.Textures[texture.Handle].Resource!, 1, 1, 4, device.Textures[texture.Handle].State));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 UI backdrop blur chain failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_UnlitMaterial_Binds_Default_And_Override_Texture_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "Texture2D _MainTex : register(t0); SamplerState _MainTexSampler : register(s0); float4 main() : SV_Target { return _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); }",
        });
        if (!compiled.Success) return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            using var defaultTexture = new Resources.Texture2D();
            using var overrideTexture = new Resources.Texture2D();
            using var shader = CreateUnlitTextureContractShader(defaultTexture);
            using var defaultMaterial = new Resources.Material(shader);
            using var overrideMaterial = new Resources.Material(shader);
            overrideMaterial.SetTexture("_MainTex", overrideTexture);
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 2, 1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-unlit-texture-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
                create.EncodeCreateTexture(defaultTexture.Handle);
                create.EncodeAllocateTexture2D(defaultTexture.Handle, 0, 1, 1, 0, new byte[] { 64, 128, 192, 255 });
                create.EncodeCreateTexture(overrideTexture.Handle);
                create.EncodeAllocateTexture2D(overrideTexture.Handle, 0, 1, 1, 0, new byte[] { 192, 64, 128, 255 });
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, true);
            }
            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-unlit-texture-draw"))
            {
                draw.SetRenderTarget(framebuffer); draw.DisableScissor(); draw.SetShader(variant); draw.SetRasterState(in raster);
                draw.SetMaterialProperties(defaultMaterial); draw.SetViewport(0, 0, 1, 1); draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetMaterialProperties(overrideMaterial); draw.SetViewport(1, 0, 1, 1); draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, true);
            }
            byte[] readback = device.ReadTexture2D(device.Textures[color.Handle].Resource!, 2, 1, 4, device.Textures[color.Handle].State);
            Assert.Equal(new byte[] { 64, 128, 192, 255, 192, 64, 128, 255 }, readback);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Unlit texture binding failure: {ex.GetType().FullName}: {ex.Message}");
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
    public void Optional_D3D12_SingleColorFramebuffer_Draws_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(0.25, 0.5, 0.75, 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var colorTexture = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = colorTexture }],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-framebuffer-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(colorTexture);
                create.EncodeAllocateTexture2D(colorTexture, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Assert.NotEqual(0u, framebuffer.Handle);
            Backends.D3D12.D3D12FramebufferResource native = device.Framebuffers[framebuffer.Handle];
            Assert.NotEqual(0ul, native.Rtv.Ptr);
            Assert.Equal(1u, native.Width);
            Assert.Equal(1u, native.Height);
            Assert.Equal(Vortice.DXGI.Format.R8G8B8A8_UNorm, native.ColorFormat);
            Assert.Equal(colorTexture.Handle, native.ColorHandle);

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-framebuffer-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.ClearRenderTarget(ClearFlags.Color, Color.Black);
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.Equal(Vortice.Direct3D12.ResourceStates.PixelShaderResource, device.Textures[colorTexture.Handle].State);
            Assert.NotEqual(0ul, device.GetFenceValue());

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-framebuffer-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, framebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 custom framebuffer failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_ColorFramebuffer_Blit_Scales_And_Reads_Back_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            byte[] sourcePixels =
            [
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255,
            ];
            using var source = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var destination = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer sourceFramebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = source }],
                2,
                2);
            GraphicsFrameBuffer destinationFramebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = destination }],
                1,
                1);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-color-blit-create"))
            {
                create.EncodeCreateTexture(source);
                create.EncodeAllocateTexture2D(source, 0, 2, 2, 0, sourcePixels);
                create.EncodeCreateTexture(destination);
                create.EncodeAllocateTexture2D(destination, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(sourceFramebuffer);
                create.EncodeCreateFramebuffer(destinationFramebuffer);
                device.Execute(create, wait: true);
            }

            Backends.D3D12.D3D12FramebufferResource sourceNative = device.Framebuffers[sourceFramebuffer.Handle];
            Backends.D3D12.D3D12FramebufferResource destinationNative = device.Framebuffers[destinationFramebuffer.Handle];
            Assert.Equal(source.Handle, sourceNative.ColorHandle);
            Assert.Equal(destination.Handle, destinationNative.ColorHandle);
            Assert.Equal(0u, sourceNative.ColorSubresource);
            Assert.Equal(0u, destinationNative.ColorSubresource);

            using (CommandBuffer blit = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-color-blit"))
            {
                blit.SetRenderTargets(destinationFramebuffer, sourceFramebuffer);
                blit.BlitFramebuffer(0, 0, 2, 2, 0, 0, 1, 1, ClearFlags.Color, BlitFilter.Linear);
                device.Execute(blit, wait: true);
            }

            Backends.D3D12.D3D12TextureResource destinationResource = device.Textures[destination.Handle];
            byte[] readback = device.ReadTexture2D(destinationResource.Resource!, 1, 1, 4, destinationResource.State);
            Assert.InRange(readback[0], (byte)127, (byte)128);
            Assert.InRange(readback[1], (byte)127, (byte)128);
            Assert.InRange(readback[2], (byte)127, (byte)128);
            Assert.Equal((byte)255, readback[3]);
            Assert.Equal(Vortice.Direct3D12.ResourceStates.PixelShaderResource, device.Textures[source.Handle].State);
            Assert.Equal(Vortice.Direct3D12.ResourceStates.PixelShaderResource, destinationResource.State);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-color-blit-dispose");
            dispose.EncodeDisposeFramebuffer(sourceFramebuffer);
            dispose.EncodeDisposeFramebuffer(destinationFramebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, sourceFramebuffer.Handle);
            Assert.Equal(0u, destinationFramebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 color framebuffer blit failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_DefaultFramebuffer_Capture_Reads_Back_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var destination = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = destination }], 1, 1);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-default-capture-create"))
            {
                create.EncodeCreateTexture(destination);
                create.EncodeAllocateTexture2D(destination, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                device.Execute(create, wait: true);
            }

            using (CommandBuffer capture = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-default-capture"))
            {
                capture.SetRenderTarget(null);
                capture.ClearRenderTarget(ClearFlags.Color, new Color(0.25f, 0.5f, 0.75f, 1f));
                capture.SetRenderTargets(framebuffer, null);
                capture.BlitFramebuffer(0, 0, 1, 1, 0, 0, 1, 1, ClearFlags.Color, BlitFilter.Linear);
                device.Execute(capture, wait: true);
            }

            Backends.D3D12.D3D12TextureResource resource = device.Textures[destination.Handle];
            AssertMaterialPixel(device.ReadTexture2D(resource.Resource!, 1, 1, 4, resource.State), 0, 0.25f, 0.5f, 0.75f, 1f);
            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-default-capture-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 default framebuffer capture failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_DepthFramebuffer_Blit_Preserves_Occlusion_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        const string vertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        ShaderCompileResult red = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(1, 0, 0, 1); }",
        });
        ShaderCompileResult green = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 0, 1); }",
        });
        if (!red.Success || !green.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var redVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, red.VertexBytecode!, red.FragmentBytecode!, red.BindingLayout));
            using var greenVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, green.VertexBytecode!, green.FragmentBytecode!, green.BindingLayout));
            float[] vertices =
            [
                -1f, -1f, 0.2f, 0f, 1f, 0.2f, 1f, -1f, 0.2f,
                -1f, -1f, 0.8f, 0f, 1f, 0.8f, 1f, -1f, 0.8f,
            ];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var sourceColor = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var sourceDepth = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Depth32f);
            using var destinationColor = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var destinationDepth = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Depth32f);
            GraphicsFrameBuffer sourceFramebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = sourceColor },
                    new GraphicsFrameBuffer.Attachment { Texture = sourceDepth, IsDepth = true },
                ],
                1,
                1);
            GraphicsFrameBuffer destinationFramebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = destinationColor },
                    new GraphicsFrameBuffer.Attachment { Texture = destinationDepth, IsDepth = true },
                ],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-depth-blit-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(sourceColor);
                create.EncodeAllocateTexture2D(sourceColor, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(sourceDepth);
                create.EncodeAllocateTexture2D(sourceDepth, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(destinationColor);
                create.EncodeAllocateTexture2D(destinationColor, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(destinationDepth);
                create.EncodeAllocateTexture2D(destinationDepth, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(sourceFramebuffer);
                create.EncodeCreateFramebuffer(destinationFramebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new()
            {
                DepthTest = true,
                DepthWrite = true,
                Depth = RasterizerState.DepthMode.Less,
                CullFace = RasterizerState.PolyFace.None,
            };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-depth-blit-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetRenderTarget(destinationFramebuffer);
                draw.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, Color.Black, depth: 1f);
                draw.SetRenderTarget(sourceFramebuffer);
                draw.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, Color.Black, depth: 1f);
                draw.SetRasterState(in raster);
                draw.SetShader(redVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetRenderTargets(destinationFramebuffer, sourceFramebuffer);
                draw.BlitFramebuffer(0, 0, 1, 1, 0, 0, 1, 1, ClearFlags.Depth, BlitFilter.Nearest);
                draw.SetRenderTarget(destinationFramebuffer);
                draw.SetShader(greenVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 3, 3);
                device.Execute(draw, wait: true);
            }

            Backends.D3D12.D3D12TextureResource destinationColorResource = device.Textures[destinationColor.Handle];
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, device.ReadTexture2D(destinationColorResource.Resource!, 1, 1, 4, destinationColorResource.State));
            Assert.Equal(Vortice.Direct3D12.ResourceStates.DepthWrite, device.Textures[sourceDepth.Handle].State);
            Assert.Equal(Vortice.Direct3D12.ResourceStates.DepthWrite, device.Textures[destinationDepth.Handle].State);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-depth-blit-dispose");
            dispose.EncodeDisposeFramebuffer(sourceFramebuffer);
            dispose.EncodeDisposeFramebuffer(destinationFramebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, sourceFramebuffer.Handle);
            Assert.Equal(0u, destinationFramebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 depth framebuffer blit failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_CubemapMipFramebuffer_Draws_And_Reads_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(0.25, 0.5, 0.75, 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var cubemap = new GraphicsTexture(TextureType.TextureCubeMap, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = cubemap, IsCubeFace = true, CubeFace = 4, MipLevel = 1 }],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-cubemap-mip-framebuffer-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(cubemap);
                for (int face = 0; face < 6; face++)
                {
                    create.EncodeAllocateTextureCubeFace(cubemap, face, 0, 2, new byte[16]);
                    create.EncodeAllocateTextureCubeFace(cubemap, face, 1, 1, new byte[4]);
                }
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Assert.NotEqual(0u, framebuffer.Handle);
            Backends.D3D12.D3D12FramebufferResource native = device.Framebuffers[framebuffer.Handle];
            Assert.NotEqual(0ul, native.Rtv.Ptr);
            Assert.Equal(1u, native.Width);
            Assert.Equal(1u, native.Height);
            Assert.Equal(9u, native.ColorSubresource);
            Assert.True(native.SubresourceOnly);

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-cubemap-mip-framebuffer-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Backends.D3D12.D3D12TextureResource resource = device.Textures[cubemap.Handle];
            uint subresource = 1u + 4u * resource.MipLevels;
            byte[] pixels = device.ReadTexture2D(resource.Resource!, 1, 1, 4, resource.State, subresource);
            Assert.Equal(new byte[] { 64, 128, 191, 255 }, pixels);
            Assert.Equal(Vortice.Direct3D12.ResourceStates.PixelShaderResource, resource.State);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-cubemap-mip-framebuffer-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, framebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 cubemap mip framebuffer failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_MultipleRenderTargets_Draw_And_Read_Back_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "struct PSOutput { float4 color0 : SV_Target0; float4 color1 : SV_Target1; }; PSOutput main() { PSOutput o; o.color0 = float4(64.0 / 255.0, 128.0 / 255.0, 191.0 / 255.0, 1); o.color1 = float4(191.0 / 255.0, 64.0 / 255.0, 128.0 / 255.0, 1); return o; }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var color0 = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var color1 = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = color0 },
                    new GraphicsFrameBuffer.Attachment { Texture = color1 },
                ],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-mrt-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(color0);
                create.EncodeAllocateTexture2D(color0, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(color1);
                create.EncodeAllocateTexture2D(color1, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.D3D12.D3D12FramebufferResource native = device.Framebuffers[framebuffer.Handle];
            Assert.Equal(2, native.Rtvs.Length);
            Assert.Equal(2, native.ColorFormats.Count);
            Assert.Equal(color0.Handle, native.ColorHandles[0]);
            Assert.Equal(color1.Handle, native.ColorHandles[1]);

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-mrt-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.Equal(new byte[] { 64, 128, 191, 255 }, device.ReadTexture2D(device.Textures[color0.Handle].Resource!, 1, 1, 4, device.Textures[color0.Handle].State));
            Assert.Equal(new byte[] { 191, 64, 128, 255 }, device.ReadTexture2D(device.Textures[color1.Handle].Resource!, 1, 1, 4, device.Textures[color1.Handle].State));
            Assert.Equal(Vortice.Direct3D12.ResourceStates.PixelShaderResource, device.Textures[color0.Handle].State);
            Assert.Equal(Vortice.Direct3D12.ResourceStates.PixelShaderResource, device.Textures[color1.Handle].State);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-mrt-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, framebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 MRT framebuffer failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_Prepass_Mrt_Depth_Reused_By_Opaque_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        const string vertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        ShaderCompileResult prepass = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "struct PSOutput { float4 normal : SV_Target0; float4 motionRM : SV_Target1; }; PSOutput main() { PSOutput o; o.normal = float4(0.25, 0.5, 0.75, 1); o.motionRM = float4(0.5, -0.25, 0.75, 1); return o; }",
        });
        ShaderCompileResult opaque = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 0, 1); }",
        });
        if (!prepass.Success || !opaque.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var prepassVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, prepass.VertexBytecode!, prepass.FragmentBytecode!, prepass.BindingLayout));
            using var opaqueVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, opaque.VertexBytecode!, opaque.FragmentBytecode!, opaque.BindingLayout));
            float[] vertices =
            [
                -1f, -1f, 0.2f, 0f, 1f, 0.2f, 1f, -1f, 0.2f,
                -1f, -1f, 0.8f, 0f, 1f, 0.8f, 1f, -1f, 0.8f,
            ];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var normals = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var motionMaterial = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Short4);
            using var prepassDepth = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Depth32f);
            using var sceneColor = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var sceneDepth = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Depth32f);
            GraphicsFrameBuffer prepassFramebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = normals },
                    new GraphicsFrameBuffer.Attachment { Texture = motionMaterial },
                    new GraphicsFrameBuffer.Attachment { Texture = prepassDepth, IsDepth = true },
                ],
                1,
                1);
            GraphicsFrameBuffer sceneFramebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = sceneColor },
                    new GraphicsFrameBuffer.Attachment { Texture = sceneDepth, IsDepth = true },
                ],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-prepass-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(normals);
                create.EncodeAllocateTexture2D(normals, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(motionMaterial);
                create.EncodeAllocateTexture2D(motionMaterial, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(prepassDepth);
                create.EncodeAllocateTexture2D(prepassDepth, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(sceneColor);
                create.EncodeAllocateTexture2D(sceneColor, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(sceneDepth);
                create.EncodeAllocateTexture2D(sceneDepth, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(prepassFramebuffer);
                create.EncodeCreateFramebuffer(sceneFramebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.D3D12.D3D12FramebufferResource prepassNative = device.Framebuffers[prepassFramebuffer.Handle];
            Assert.Equal(
                [Vortice.DXGI.Format.R8G8B8A8_UNorm, Vortice.DXGI.Format.R16G16B16A16_Float],
                prepassNative.ColorFormats.ToArray());
            Assert.Equal(Vortice.DXGI.Format.D32_Float, prepassNative.DepthFormat);

            RasterizerState raster = new()
            {
                DepthTest = true,
                DepthWrite = true,
                Depth = RasterizerState.DepthMode.Less,
                CullFace = RasterizerState.PolyFace.None,
            };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-prepass-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetRenderTarget(sceneFramebuffer);
                draw.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, Color.Black, depth: 1f);
                draw.SetRenderTarget(prepassFramebuffer);
                draw.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, Color.Black, depth: 1f);
                draw.SetRasterState(in raster);
                draw.SetShader(prepassVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetRenderTargets(sceneFramebuffer, prepassFramebuffer);
                draw.BlitFramebuffer(0, 0, 1, 1, 0, 0, 1, 1, ClearFlags.Depth, BlitFilter.Nearest);
                draw.SetRenderTarget(sceneFramebuffer);
                draw.SetShader(opaqueVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 3, 3);
                device.Execute(draw, wait: true);
            }

            byte[] normalReadback = device.ReadTexture2D(device.Textures[normals.Handle].Resource!, 1, 1, 4, device.Textures[normals.Handle].State);
            Assert.InRange(normalReadback[0], (byte)63, (byte)64);
            Assert.InRange(normalReadback[1], (byte)127, (byte)128);
            Assert.InRange(normalReadback[2], (byte)191, (byte)192);
            Assert.Equal((byte)255, normalReadback[3]);
            Assert.Equal(PrepassHalfReadback(), device.ReadTexture2D(device.Textures[motionMaterial.Handle].Resource!, 1, 1, 8, device.Textures[motionMaterial.Handle].State));
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, device.ReadTexture2D(device.Textures[sceneColor.Handle].Resource!, 1, 1, 4, device.Textures[sceneColor.Handle].State));
            Assert.Equal(Vortice.Direct3D12.ResourceStates.DepthWrite, device.Textures[prepassDepth.Handle].State);
            Assert.Equal(Vortice.Direct3D12.ResourceStates.DepthWrite, device.Textures[sceneDepth.Handle].State);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-prepass-dispose");
            dispose.EncodeDisposeFramebuffer(prepassFramebuffer);
            dispose.EncodeDisposeFramebuffer(sceneFramebuffer);
            device.Execute(dispose, wait: true);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 prepass MRT failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_BlendState_Composites_Draws_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        const string vertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        ShaderCompileResult red = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(1, 0, 0, 1); }",
        });
        ShaderCompileResult green = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 0, 0.5); }",
        });
        if (!red.Success || !green.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var redVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, red.VertexBytecode!, red.FragmentBytecode!, red.BindingLayout));
            using var greenVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, green.VertexBytecode!, green.FragmentBytecode!, green.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = color }],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-blend-framebuffer-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState opaque = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            RasterizerState alphaBlend = opaque;
            alphaBlend.DoBlend = true;
            alphaBlend.BlendSrc = RasterizerState.Blending.SrcAlpha;
            alphaBlend.BlendDst = RasterizerState.Blending.OneMinusSrcAlpha;
            alphaBlend.Blend = RasterizerState.BlendMode.Add;
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-blend-framebuffer-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.ClearRenderTarget(ClearFlags.Color, Color.Black);
                draw.SetRasterState(in opaque);
                draw.SetShader(redVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetRasterState(in alphaBlend);
                draw.SetShader(greenVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Backends.D3D12.D3D12TextureResource resource = device.Textures[color.Handle];
            byte[] readback = device.ReadTexture2D(resource.Resource!, 1, 1, 4, resource.State);
            Assert.InRange(readback[0], (byte)127, (byte)128);
            Assert.InRange(readback[1], (byte)127, (byte)128);
            Assert.Equal((byte)0, readback[2]);
            Assert.Equal((byte)191, readback[3]);
            Assert.Equal(Vortice.Direct3D12.ResourceStates.PixelShaderResource, resource.State);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-blend-framebuffer-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, framebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 blend-state failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_DepthFramebuffer_Rejects_Farther_Draw_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        const string vertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        ShaderCompileResult red = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(1, 0, 0, 1); }",
        });
        ShaderCompileResult green = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 0, 1); }",
        });
        ShaderCompileResult blue = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 0, 1, 1); }",
        });
        if (!red.Success || !green.Success || !blue.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var redVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, red.VertexBytecode!, red.FragmentBytecode!, red.BindingLayout));
            using var greenVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, green.VertexBytecode!, green.FragmentBytecode!, green.BindingLayout));
            using var blueVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, blue.VertexBytecode!, blue.FragmentBytecode!, blue.BindingLayout));
            float[] vertices =
            [
                -1f, -1f, 0.8f, 0f, 1f, 0.8f, 1f, -1f, 0.8f,
                -1f, -1f, 0.2f, 0f, 1f, 0.2f, 1f, -1f, 0.2f,
                -1f, -1f, 0.8f, 0f, 1f, 0.8f, 1f, -1f, 0.8f,
            ];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var depth = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Depth32f);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = color },
                    new GraphicsFrameBuffer.Attachment { Texture = depth, IsDepth = true },
                ],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-depth-framebuffer-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(depth);
                create.EncodeAllocateTexture2D(depth, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.D3D12.D3D12FramebufferResource native = device.Framebuffers[framebuffer.Handle];
            Assert.Equal(Vortice.DXGI.Format.D32_Float, native.DepthFormat);
            Assert.Equal(depth.Handle, native.DepthHandle);
            Assert.NotEqual(0ul, native.Dsv.Ptr);
            Assert.Equal(Vortice.Direct3D12.ResourceStates.DepthWrite, device.Textures[depth.Handle].State);

            RasterizerState raster = new()
            {
                DepthTest = true,
                DepthWrite = true,
                Depth = RasterizerState.DepthMode.Less,
                CullFace = RasterizerState.PolyFace.None,
            };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-depth-framebuffer-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, Color.Black, depth: 1f);
                draw.SetRasterState(in raster);
                draw.SetShader(redVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetShader(greenVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 3, 3);
                draw.SetShader(blueVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 6, 3);
                device.Execute(draw, wait: true);
            }

            Backends.D3D12.D3D12TextureResource colorResource = device.Textures[color.Handle];
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, device.ReadTexture2D(colorResource.Resource!, 1, 1, 4, colorResource.State));
            Assert.Equal(Vortice.Direct3D12.ResourceStates.PixelShaderResource, colorResource.State);
            Assert.Equal(Vortice.Direct3D12.ResourceStates.DepthWrite, device.Textures[depth.Handle].State);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-depth-framebuffer-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, framebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 depth framebuffer failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_StencilFramebuffer_Controls_Draws_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        const string vertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        ShaderCompileResult red = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(1, 0, 0, 1); }",
        });
        ShaderCompileResult green = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 0, 1); }",
        });
        ShaderCompileResult blue = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 0, 1, 1); }",
        });
        if (!red.Success || !green.Success || !blue.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var redVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, red.VertexBytecode!, red.FragmentBytecode!, red.BindingLayout));
            using var greenVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, green.VertexBytecode!, green.FragmentBytecode!, green.BindingLayout));
            using var blueVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, blue.VertexBytecode!, blue.FragmentBytecode!, blue.BindingLayout));
            float[] vertices = [-1f, -1f, 0.5f, 0f, 1f, 0.5f, 1f, -1f, 0.5f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var depthStencil = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Depth24Stencil8);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = color },
                    new GraphicsFrameBuffer.Attachment { Texture = depthStencil, IsDepth = true },
                ],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-stencil-framebuffer-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(depthStencil);
                create.EncodeAllocateTexture2D(depthStencil, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.D3D12.D3D12FramebufferResource native = device.Framebuffers[framebuffer.Handle];
            Assert.Equal(Vortice.DXGI.Format.D24_UNorm_S8_UInt, native.DepthFormat);

            RasterizerState writeStencil = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
                StencilEnabled = true,
                StencilFunc = RasterizerState.StencilFunction.Always,
                StencilRef = 1,
                StencilReadMask = 255,
                StencilWriteMask = 255,
                StencilPassOp = RasterizerState.StencilOp.Replace,
            };
            RasterizerState passStencil = writeStencil;
            passStencil.StencilFunc = RasterizerState.StencilFunction.Equal;
            passStencil.StencilWriteMask = 0;
            passStencil.StencilPassOp = RasterizerState.StencilOp.Keep;
            RasterizerState rejectStencil = passStencil;
            rejectStencil.StencilRef = 2;
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-stencil-framebuffer-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth | ClearFlags.Stencil, Color.Black, depth: 1f, stencil: 0);
                draw.SetRasterState(in writeStencil);
                draw.SetShader(redVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetRasterState(in passStencil);
                draw.SetShader(greenVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetRasterState(in rejectStencil);
                draw.SetShader(blueVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Backends.D3D12.D3D12TextureResource colorResource = device.Textures[color.Handle];
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, device.ReadTexture2D(colorResource.Resource!, 1, 1, 4, colorResource.State));
            Assert.Equal(Vortice.Direct3D12.ResourceStates.DepthWrite, device.Textures[depthStencil.Handle].State);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-stencil-framebuffer-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, framebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 stencil framebuffer failure: {ex.GetType().FullName}: {ex.Message}");
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
    public void Optional_D3D12_Texture3D_Uploads_And_Draws_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; struct VSOutput { float4 position : SV_Position; float2 uv : TEXCOORD0; }; VSOutput main(VSInput input) { VSOutput o; o.position = float4(input.position, 1); o.uv = input.position.xy * 0.5 + 0.5; return o; }",
            FragmentSource = "Texture3D VolumeTexture : register(t0); SamplerState VolumeSampler : register(s0); float4 main(float2 uv : TEXCOORD0) : SV_Target { return VolumeTexture.Sample(VolumeSampler, float3(uv, 0.5)); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var texture = new GraphicsTexture(TextureType.Texture3D, TextureImageFormat.Color4b);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-texture3d-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(texture);
                create.EncodeAllocateTexture3D(texture, 0, 1, 1, 2, new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 });
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.D3D12.D3D12TextureResource resource = device.Textures[texture.Handle];
            Assert.Equal(TextureType.Texture3D, resource.Type);
            Assert.Equal(2u, resource.Depth);
            Assert.Equal(Vortice.Direct3D12.ResourceDimension.Texture3D, resource.Resource!.Description.Dimension);
            Assert.Equal(Vortice.Direct3D12.ResourceStates.PixelShaderResource, resource.State);
            Assert.True(resource.HasSrvDescriptor);
            Assert.True(resource.HasSamplerDescriptor);

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-texture3d-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetTexture("VolumeTexture", texture);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 Texture3D failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_Cubemap_Uploads_And_Draws_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "TextureCube SkyTexture : register(t0); SamplerState SkySampler : register(s0); float4 main() : SV_Target { return SkyTexture.Sample(SkySampler, normalize(float3(1, 0.25, 0.5))); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var texture = new GraphicsTexture(TextureType.TextureCubeMap, TextureImageFormat.Color4b);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-cubemap-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(texture);
                for (int face = 0; face < 6; face++)
                {
                    byte channel = checked((byte)(32 + face * 32));
                    create.EncodeAllocateTextureCubeFace(texture, face, 0, 1, new byte[] { channel, 0, checked((byte)(255 - channel)), 255 });
                }
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.D3D12.D3D12TextureResource resource = device.Textures[texture.Handle];
            Assert.Equal(TextureType.TextureCubeMap, resource.Type);
            Assert.Equal(0b0011_1111, resource.CubeInitializedFaces);
            Assert.Equal((ushort)6, resource.Resource!.Description.DepthOrArraySize);
            Assert.Equal(Vortice.Direct3D12.ResourceStates.PixelShaderResource, resource.State);
            Assert.True(resource.HasSrvDescriptor);
            Assert.True(resource.HasSamplerDescriptor);

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-cubemap-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetTexture("SkyTexture", texture);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 cubemap failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_CubemapMip_Uploads_Reads_And_Draws_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "TextureCube SkyTexture : register(t0); SamplerState SkySampler : register(s0); float4 main() : SV_Target { return SkyTexture.SampleLevel(SkySampler, normalize(float3(1, 0.25, 0.5)), 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var texture = new GraphicsTexture(TextureType.TextureCubeMap, TextureImageFormat.Color4b);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            byte[] expectedMipFace = [204, 17, 83, 255];
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-cubemap-mip-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(texture);
                for (int face = 0; face < 6; face++)
                {
                    byte channel = checked((byte)(24 + face * 24));
                    byte[] basePixels =
                    [
                        channel, 0, 0, 255,
                        channel, 32, 0, 255,
                        channel, 64, 0, 255,
                        channel, 96, 0, 255,
                    ];
                    create.EncodeAllocateTextureCubeFace(texture, face, 0, 2, basePixels);
                    byte[] mipPixel = face == 4 ? expectedMipFace : new byte[] { channel, 17, 83, 255 };
                    create.EncodeAllocateTextureCubeFace(texture, face, 1, 1, mipPixel);
                }
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.D3D12.D3D12TextureResource resource = device.Textures[texture.Handle];
            Assert.Equal(2u, resource.MipLevels);
            Assert.Equal(2u, resource.AvailableMipLevels);
            Assert.Equal(2, resource.CubeInitializedFacesByMip.Length);
            Assert.All(resource.CubeInitializedFacesByMip, mask => Assert.Equal(0b0011_1111, mask));
            uint sourceSubresource = 1u + 4u * resource.MipLevels;
            Assert.Equal(expectedMipFace, device.ReadTexture2D(resource.Resource!, 1, 1, 4, resource.State, sourceSubresource));

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-cubemap-mip-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetTexture("SkyTexture", texture);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 cubemap mip failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_D3D12_CubemapMipGeneration_Reads_And_Draws_Or_Skip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "TextureCube SkyTexture : register(t0); SamplerState SkySampler : register(s0); float4 main() : SV_Target { return SkyTexture.SampleLevel(SkySampler, normalize(float3(1, 0.25, 0.5)), 1); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.D3D12.D3D12GraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Direct3D12 });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.Dxil, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var texture = new GraphicsTexture(TextureType.TextureCubeMap, TextureImageFormat.Color4b);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            byte[] expectedMipFace = [144, 37, 91, 255];
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-cubemap-mip-generate"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(texture);
                for (int face = 0; face < 6; face++)
                {
                    byte[] pixel = face == 4 ? expectedMipFace : new byte[] { checked((byte)(32 + face * 24)), 37, 91, 255 };
                    byte[] basePixels = [.. pixel, .. pixel, .. pixel, .. pixel];
                    create.EncodeAllocateTextureCubeFace(texture, face, 0, 2, basePixels);
                }
                create.GenerateMipmap(texture);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.D3D12.D3D12TextureResource resource = device.Textures[texture.Handle];
            Assert.Equal(2u, resource.MipLevels);
            Assert.Equal(2u, resource.AvailableMipLevels);
            Assert.All(resource.CubeInitializedFacesByMip, mask => Assert.Equal(0b0011_1111, mask));
            uint sourceSubresource = 1u + 4u * resource.MipLevels;
            Assert.Equal(expectedMipFace, device.ReadTexture2D(resource.Resource!, 1, 1, 4, resource.State, sourceSubresource));

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("d3d12-cubemap-generated-mip-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetTexture("SkyTexture", texture);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected D3D12 cubemap mip generation failure: {ex.GetType().FullName}: {ex.Message}");
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
    public void Optional_Vulkan_GlobalUniforms_AutoBind_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "cbuffer GlobalUniforms : register(b0) { float4 Tint; }; float4 main() : SV_Target { return Tint; }",
        });
        if (!compiled.Success)
            return;

        GraphicsBuffer? previousGlobalBuffer = GetGlobalUniformBuffer();
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            Float4 tint = new(0.25f, 0.5f, 0.75f, 1f);
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            ReadOnlySpan<byte> uniformBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref tint, 1));
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var uniformBuffer = new GraphicsBuffer(BufferType.UniformBuffer, uniformBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = color }],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-global-uniform-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateBuffer(uniformBuffer, dynamic: true, uniformBytes);
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }
            SetGlobalUniformBuffer(uniformBuffer);

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-global-uniform-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            byte[] readback = device.ReadTexture2D(device.Images[color.Handle], 4);
            Assert.InRange(readback[0], (byte)63, (byte)64);
            Assert.InRange(readback[1], (byte)127, (byte)128);
            Assert.InRange(readback[2], (byte)191, (byte)192);
            Assert.Equal((byte)255, readback[3]);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-global-uniform-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan global-uniform auto-bind failure: {ex.GetType().FullName}: {ex.Message}");
        }
        finally
        {
            SetGlobalUniformBuffer(previousGlobalBuffer);
        }
    }

    [Fact]
    public void Optional_Vulkan_ObjectUniforms_Pack_Per_Draw_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "cbuffer ObjectUniforms : register(b1) { float4x4 prowl_ObjectToWorld; float4x4 prowl_WorldToObject; float4x4 prowl_PrevObjectToWorld; int _ObjectID; float3 padding; }; float4 main() : SV_Target { return float4(prowl_ObjectToWorld[0][0], prowl_WorldToObject[1][1], prowl_PrevObjectToWorld[2][2], _ObjectID / 255.0); }",
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
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = color }],
                2,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-object-uniform-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-object-uniform-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);

                Float4x4 firstObject = Float4x4.CreateScale(0.25f, 1f, 1f);
                Float4x4 firstInverse = Float4x4.CreateScale(1f, 0.5f, 1f);
                Float4x4 firstPrevious = Float4x4.CreateScale(1f, 1f, 0.75f);
                var firstProperties = new Rendering.PropertyState();
                firstProperties.SetInt("_ObjectID", 255);
                draw.SetInstanceProperties(firstProperties);
                draw.SetMatrix("prowl_ObjectToWorld", in firstObject);
                draw.SetMatrix("prowl_WorldToObject", in firstInverse);
                draw.SetMatrix("prowl_PrevObjectToWorld", in firstPrevious);
                draw.SetViewport(0, 0, 1, 1);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);

                Float4x4 secondObject = Float4x4.CreateScale(0.75f, 1f, 1f);
                Float4x4 secondInverse = Float4x4.CreateScale(1f, 0.25f, 1f);
                Float4x4 secondPrevious = Float4x4.CreateScale(1f, 1f, 0.5f);
                var secondProperties = new Rendering.PropertyState();
                secondProperties.SetInt("_ObjectID", 128);
                draw.SetInstanceProperties(secondProperties);
                draw.SetMatrix("prowl_ObjectToWorld", in secondObject);
                draw.SetMatrix("prowl_WorldToObject", in secondInverse);
                draw.SetMatrix("prowl_PrevObjectToWorld", in secondPrevious);
                draw.SetViewport(1, 0, 1, 1);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            byte[] readback = device.ReadTexture2D(device.Images[color.Handle], 4);
            Assert.InRange(readback[0], (byte)63, (byte)64);
            Assert.InRange(readback[1], (byte)127, (byte)128);
            Assert.InRange(readback[2], (byte)191, (byte)192);
            Assert.Equal((byte)255, readback[3]);
            Assert.InRange(readback[4], (byte)191, (byte)192);
            Assert.InRange(readback[5], (byte)63, (byte)64);
            Assert.InRange(readback[6], (byte)127, (byte)128);
            Assert.InRange(readback[7], (byte)127, (byte)128);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-object-uniform-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan object-uniform packing failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_UnlitMaterial_Packs_Defaults_And_Overrides_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "cbuffer UnlitMaterial : register(b2) { float2 _Tiling; float2 _Offset; float4 _MainColor; }; float4 main() : SV_Target { return float4(_Tiling.x, _Offset.y, _MainColor.r, _MainColor.a); }",
        });
        if (!compiled.Success)
            return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            using var shader = CreateUnlitMaterialContractShader();
            using var defaultsMaterial = new Resources.Material(shader);
            using var overridesMaterial = new Resources.Material(shader);
            overridesMaterial.SetVector("_Tiling", new Float2(0.75f, 1f));
            overridesMaterial.SetVector("_Offset", new Float2(0f, 0.25f));
            overridesMaterial.SetColor("_MainColor", new Color(0.5f, 0f, 0f, 0.5f));

            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = color }],
                2,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-unlit-material-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-unlit-material-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetMaterialProperties(defaultsMaterial);
                draw.SetViewport(0, 0, 1, 1);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetMaterialProperties(overridesMaterial);
                draw.SetViewport(1, 0, 1, 1);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            byte[] readback = device.ReadTexture2D(device.Images[color.Handle], 4);
            Assert.InRange(readback[0], (byte)63, (byte)64);
            Assert.InRange(readback[1], (byte)127, (byte)128);
            Assert.InRange(readback[2], (byte)191, (byte)192);
            Assert.Equal((byte)255, readback[3]);
            Assert.InRange(readback[4], (byte)191, (byte)192);
            Assert.InRange(readback[5], (byte)63, (byte)64);
            Assert.InRange(readback[6], (byte)127, (byte)128);
            Assert.InRange(readback[7], (byte)127, (byte)128);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-unlit-material-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Unlit material packing failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_StandardMaterial_Blocks_Pack_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunStandardMaterialBlocksContract(
                device,
                GraphicsBackend.Vulkan,
                ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Standard material packing failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_StandardMaterial_Textures_Bind_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunStandardMaterialTextureContract(
                device,
                GraphicsBackend.Vulkan,
                ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Standard texture binding failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_StandardTransparent_DefaultShader_Blends_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunStandardTransparentContract(
                device,
                GraphicsBackend.Vulkan,
                ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan StandardTransparent failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_GradientSkybox_Material_Packs_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunGradientSkyboxMaterialContract(
                device,
                GraphicsBackend.Vulkan,
                ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Gradient skybox packing failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_CubemapSkybox_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunCubemapSkyboxMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Cubemap skybox failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_ProceduralSkybox_Material_Packs_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunProceduralSkyboxMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Procedural skybox packing failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_Tonemapper_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunTonemapperMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Tonemapper material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_FXAA_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunFXAAMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan FXAA material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_Bloom_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunBloomMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Bloom material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_MotionBlur_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunMotionBlurMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan MotionBlur material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_AutoExposure_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunAutoExposureMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan AutoExposure material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_TAA_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunTAAMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan TAA material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_GTAO_Calculate_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunGTAOCalculateContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan GTAO calculate failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_GTAO_Blur_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunGTAOBlurContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan GTAO blur failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_GTAO_Composite_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunGTAOCompositeContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan GTAO composite failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_GTAO_Temporal_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunGTAOTemporalContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan GTAO temporal failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_GizmoIcon_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunGizmoIconMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Gizmo icon failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_DefaultUI_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunDefaultUIMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 6));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan DefaultUI failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_DefaultText_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunDefaultTextMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan DefaultText failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_DefaultTextMesh_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunDefaultTextMeshMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan DefaultTextMesh failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_Sprite_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunSpriteMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Sprite failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_Line_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunLineMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 2));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Line failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_Gizmos_GlobalDepth_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunGizmosGlobalDepthContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Gizmos failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_Grid_Material_And_GlobalDepth_Bind_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunGridMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Grid material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_UIVertex_Projection_Packs_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunUIVertexProjectionContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan UI projection failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_UIFragment_State_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunUIFragmentStateContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan UI fragment state failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_UIBlur_Material_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunUIBlurMaterialContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan UI blur material failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_UIBackdropBlur_Chain_Binds_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            RunUIBackdropBlurChainContract(device, GraphicsBackend.Vulkan, ShaderBytecodeFormat.SpirV,
                texture => device.ReadTexture2D(device.Images[texture.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan UI backdrop blur chain failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_UnlitMaterial_Binds_Default_And_Override_Texture_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "[[vk::binding(0)]] Texture2D _MainTex : register(t0); [[vk::binding(0)]] SamplerState _MainTexSampler : register(s0); float4 main() : SV_Target { return _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); }",
        });
        if (!compiled.Success) return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            using var variant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, compiled.VertexBytecode!, compiled.FragmentBytecode!, compiled.BindingLayout));
            using var defaultTexture = new Resources.Texture2D();
            using var overrideTexture = new Resources.Texture2D();
            using var shader = CreateUnlitTextureContractShader(defaultTexture);
            using var defaultMaterial = new Resources.Material(shader);
            using var overrideMaterial = new Resources.Material(shader);
            overrideMaterial.SetTexture("_MainTex", overrideTexture);
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 2, 1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-unlit-texture-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
                create.EncodeCreateTexture(defaultTexture.Handle);
                create.EncodeAllocateTexture2D(defaultTexture.Handle, 0, 1, 1, 0, new byte[] { 64, 128, 192, 255 });
                create.EncodeCreateTexture(overrideTexture.Handle);
                create.EncodeAllocateTexture2D(overrideTexture.Handle, 0, 1, 1, 0, new byte[] { 192, 64, 128, 255 });
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, true);
            }
            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-unlit-texture-draw"))
            {
                draw.SetRenderTarget(framebuffer); draw.DisableScissor(); draw.SetShader(variant); draw.SetRasterState(in raster);
                draw.SetMaterialProperties(defaultMaterial); draw.SetViewport(0, 0, 1, 1); draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetMaterialProperties(overrideMaterial); draw.SetViewport(1, 0, 1, 1); draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, true);
            }
            Assert.Equal(new byte[] { 64, 128, 192, 255, 192, 64, 128, 255 }, device.ReadTexture2D(device.Images[color.Handle], 4));
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Unlit texture binding failure: {ex.GetType().FullName}: {ex.Message}");
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
    public void Optional_Vulkan_Texture3D_Uploads_And_Draws_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; struct VSOutput { float4 position : SV_Position; [[vk::location(0)]] float2 uv : TEXCOORD0; }; VSOutput main(VSInput input) { VSOutput o; o.position = float4(input.position, 1); o.uv = input.position.xy * 0.5 + 0.5; return o; }",
            FragmentSource = "Texture3D VolumeTexture : register(t0); SamplerState VolumeSampler : register(s0); float4 main([[vk::location(0)]] float2 uv : TEXCOORD0) : SV_Target { return VolumeTexture.Sample(VolumeSampler, float3(uv, 0.5)); }",
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
            using var texture = new GraphicsTexture(TextureType.Texture3D, TextureImageFormat.Color4b);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-texture3d-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(texture);
                create.EncodeAllocateTexture3D(texture, 0, 1, 1, 2, new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 });
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.Vulkan.VkImageResource resource = device.Images[texture.Handle];
            Assert.Equal(TextureType.Texture3D, resource.Type);
            Assert.Equal(1u, resource.Width);
            Assert.Equal(1u, resource.Height);
            Assert.Equal(2u, resource.Depth);
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal, resource.Layout);
            Assert.NotEqual(0ul, resource.View.Handle);
            Assert.NotEqual(0ul, resource.Sampler.Handle);

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-texture3d-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetTexture("VolumeTexture", texture);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan Texture3D failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_Cubemap_Uploads_And_Draws_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "TextureCube SkyTexture : register(t0); SamplerState SkySampler : register(s0); float4 main() : SV_Target { return SkyTexture.Sample(SkySampler, normalize(float3(1, 0.25, 0.5))); }",
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
            using var texture = new GraphicsTexture(TextureType.TextureCubeMap, TextureImageFormat.Color4b);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-cubemap-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(texture);
                for (int face = 0; face < 6; face++)
                {
                    byte channel = checked((byte)(32 + face * 32));
                    create.EncodeAllocateTextureCubeFace(texture, face, 0, 1, new byte[] { channel, 0, checked((byte)(255 - channel)), 255 });
                }
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.Vulkan.VkImageResource resource = device.Images[texture.Handle];
            Assert.Equal(TextureType.TextureCubeMap, resource.Type);
            Assert.Equal(0b0011_1111, resource.CubeInitializedFaces);
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal, resource.Layout);
            Assert.NotEqual(0ul, resource.View.Handle);
            Assert.NotEqual(0ul, resource.Sampler.Handle);

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-cubemap-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetTexture("SkyTexture", texture);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan cubemap failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_CubemapMip_Uploads_Reads_And_Draws_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "TextureCube SkyTexture : register(t0); SamplerState SkySampler : register(s0); float4 main() : SV_Target { return SkyTexture.SampleLevel(SkySampler, normalize(float3(1, 0.25, 0.5)), 1); }",
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
            using var texture = new GraphicsTexture(TextureType.TextureCubeMap, TextureImageFormat.Color4b);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            byte[] expectedMipFace = [204, 17, 83, 255];
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-cubemap-mip-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(texture);
                for (int face = 0; face < 6; face++)
                {
                    byte channel = checked((byte)(24 + face * 24));
                    byte[] basePixels =
                    [
                        channel, 0, 0, 255,
                        channel, 32, 0, 255,
                        channel, 64, 0, 255,
                        channel, 96, 0, 255,
                    ];
                    create.EncodeAllocateTextureCubeFace(texture, face, 0, 2, basePixels);
                    byte[] mipPixel = face == 4 ? expectedMipFace : new byte[] { channel, 17, 83, 255 };
                    create.EncodeAllocateTextureCubeFace(texture, face, 1, 1, mipPixel);
                }
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.Vulkan.VkImageResource resource = device.Images[texture.Handle];
            Assert.Equal(2u, resource.MipLevels);
            Assert.Equal(2u, resource.AvailableMipLevels);
            Assert.Equal(2, resource.CubeInitializedFacesByMip.Length);
            Assert.All(resource.CubeInitializedFacesByMip, mask => Assert.Equal(0b0011_1111, mask));
            Assert.Equal(expectedMipFace, device.ReadTextureCubeFace(resource, 4, 1, 4));

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-cubemap-mip-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetTexture("SkyTexture", texture);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan cubemap mip failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_CubemapMipGeneration_Reads_And_Draws_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "TextureCube SkyTexture : register(t0); SamplerState SkySampler : register(s0); float4 main() : SV_Target { return SkyTexture.SampleLevel(SkySampler, normalize(float3(1, 0.25, 0.5)), 1); }",
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
            using var texture = new GraphicsTexture(TextureType.TextureCubeMap, TextureImageFormat.Color4b);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            byte[] expectedMipFace = [144, 37, 91, 255];
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-cubemap-mip-generate"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(texture);
                for (int face = 0; face < 6; face++)
                {
                    byte[] pixel = face == 4 ? expectedMipFace : new byte[] { checked((byte)(32 + face * 24)), 37, 91, 255 };
                    byte[] basePixels = [.. pixel, .. pixel, .. pixel, .. pixel];
                    create.EncodeAllocateTextureCubeFace(texture, face, 0, 2, basePixels);
                }
                create.GenerateMipmap(texture);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.Vulkan.VkImageResource resource = device.Images[texture.Handle];
            Assert.Equal(2u, resource.MipLevels);
            Assert.Equal(2u, resource.AvailableMipLevels);
            Assert.All(resource.CubeInitializedFacesByMip, mask => Assert.Equal(0b0011_1111, mask));
            Assert.Equal(expectedMipFace, device.ReadTextureCubeFace(resource, 4, 1, 4));

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-cubemap-generated-mip-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.SetTexture("SkyTexture", texture);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.NotEqual(0ul, device.GetFenceValue());
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan cubemap mip generation failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_SingleColorFramebuffer_Draws_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(0.25, 0.5, 0.75, 1); }",
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
            using var colorTexture = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = colorTexture }],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-framebuffer-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(colorTexture);
                create.EncodeAllocateTexture2D(colorTexture, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Assert.NotEqual(0u, framebuffer.Handle);
            Backends.Vulkan.VkFramebufferResource native = device.Framebuffers[framebuffer.Handle];
            Assert.NotEqual(0ul, native.Framebuffer.Handle);
            Assert.NotEqual(0ul, native.RenderPass.Handle);
            Assert.Equal(Silk.NET.Vulkan.Format.R8G8B8A8Unorm, native.ColorFormat);

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-framebuffer-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.Equal(Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal, device.Images[colorTexture.Handle].Layout);
            Assert.NotEqual(0ul, device.GetFenceValue());

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-framebuffer-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, framebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan custom framebuffer failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_ColorFramebuffer_Blit_Scales_And_Reads_Back_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            byte[] sourcePixels =
            [
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255,
            ];
            using var source = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var destination = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer sourceFramebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = source }],
                2,
                2);
            GraphicsFrameBuffer destinationFramebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = destination }],
                1,
                1);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-color-blit-create"))
            {
                create.EncodeCreateTexture(source);
                create.EncodeAllocateTexture2D(source, 0, 2, 2, 0, sourcePixels);
                create.EncodeCreateTexture(destination);
                create.EncodeAllocateTexture2D(destination, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(sourceFramebuffer);
                create.EncodeCreateFramebuffer(destinationFramebuffer);
                device.Execute(create, wait: true);
            }

            Backends.Vulkan.VkFramebufferResource sourceNative = device.Framebuffers[sourceFramebuffer.Handle];
            Backends.Vulkan.VkFramebufferResource destinationNative = device.Framebuffers[destinationFramebuffer.Handle];
            Assert.Equal(source.Handle, sourceNative.ColorHandles[0]);
            Assert.Equal(destination.Handle, destinationNative.ColorHandles[0]);
            Assert.Equal(0u, sourceNative.ColorMipLevels[0]);
            Assert.Equal(0u, destinationNative.ColorMipLevels[0]);

            using (CommandBuffer blit = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-color-blit"))
            {
                blit.SetRenderTargets(destinationFramebuffer, sourceFramebuffer);
                blit.BlitFramebuffer(0, 0, 2, 2, 0, 0, 1, 1, ClearFlags.Color, BlitFilter.Linear);
                device.Execute(blit, wait: true);
            }

            byte[] readback = device.ReadTexture2D(device.Images[destination.Handle], 4);
            Assert.InRange(readback[0], (byte)127, (byte)128);
            Assert.InRange(readback[1], (byte)127, (byte)128);
            Assert.InRange(readback[2], (byte)127, (byte)128);
            Assert.Equal((byte)255, readback[3]);
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal, device.Images[source.Handle].Layout);
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal, device.Images[destination.Handle].Layout);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-color-blit-dispose");
            dispose.EncodeDisposeFramebuffer(sourceFramebuffer);
            dispose.EncodeDisposeFramebuffer(destinationFramebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, sourceFramebuffer.Handle);
            Assert.Equal(0u, destinationFramebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan color framebuffer blit failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_DefaultFramebuffer_Capture_Reads_Back_Or_Skip()
    {
        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            using var destination = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = destination }], 1, 1);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-default-capture-create"))
            {
                create.EncodeCreateTexture(destination);
                create.EncodeAllocateTexture2D(destination, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                device.Execute(create, wait: true);
            }

            using (CommandBuffer capture = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-default-capture"))
            {
                capture.SetRenderTarget(null);
                capture.ClearRenderTarget(ClearFlags.Color, new Color(0.25f, 0.5f, 0.75f, 1f));
                capture.SetRenderTargets(framebuffer, null);
                capture.BlitFramebuffer(0, 0, 1, 1, 0, 0, 1, 1, ClearFlags.Color, BlitFilter.Linear);
                device.Execute(capture, wait: true);
            }

            AssertMaterialPixel(device.ReadTexture2D(device.Images[destination.Handle], 4), 0, 0.25f, 0.5f, 0.75f, 1f);
            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-default-capture-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan default framebuffer capture failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_DepthFramebuffer_Blit_Preserves_Occlusion_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        const string vertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        ShaderCompileResult red = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(1, 0, 0, 1); }",
        });
        ShaderCompileResult green = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 0, 1); }",
        });
        if (!red.Success || !green.Success)
            return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            using var redVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, red.VertexBytecode!, red.FragmentBytecode!, red.BindingLayout));
            using var greenVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, green.VertexBytecode!, green.FragmentBytecode!, green.BindingLayout));
            float[] vertices =
            [
                -1f, -1f, 0.2f, 0f, 1f, 0.2f, 1f, -1f, 0.2f,
                -1f, -1f, 0.8f, 0f, 1f, 0.8f, 1f, -1f, 0.8f,
            ];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var sourceColor = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var sourceDepth = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Depth32f);
            using var destinationColor = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var destinationDepth = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Depth32f);
            GraphicsFrameBuffer sourceFramebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = sourceColor },
                    new GraphicsFrameBuffer.Attachment { Texture = sourceDepth, IsDepth = true },
                ],
                1,
                1);
            GraphicsFrameBuffer destinationFramebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = destinationColor },
                    new GraphicsFrameBuffer.Attachment { Texture = destinationDepth, IsDepth = true },
                ],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-depth-blit-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(sourceColor);
                create.EncodeAllocateTexture2D(sourceColor, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(sourceDepth);
                create.EncodeAllocateTexture2D(sourceDepth, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(destinationColor);
                create.EncodeAllocateTexture2D(destinationColor, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(destinationDepth);
                create.EncodeAllocateTexture2D(destinationDepth, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(sourceFramebuffer);
                create.EncodeCreateFramebuffer(destinationFramebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState raster = new()
            {
                DepthTest = true,
                DepthWrite = true,
                Depth = RasterizerState.DepthMode.Less,
                CullFace = RasterizerState.PolyFace.None,
            };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-depth-blit-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetRenderTarget(destinationFramebuffer);
                draw.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, Color.Black, depth: 1f);
                draw.SetRenderTarget(sourceFramebuffer);
                draw.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, Color.Black, depth: 1f);
                draw.SetRasterState(in raster);
                draw.SetShader(redVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetRenderTargets(destinationFramebuffer, sourceFramebuffer);
                draw.BlitFramebuffer(0, 0, 1, 1, 0, 0, 1, 1, ClearFlags.Depth, BlitFilter.Nearest);
                draw.SetRenderTarget(destinationFramebuffer);
                draw.SetShader(greenVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 3, 3);
                device.Execute(draw, wait: true);
            }

            Assert.Equal(new byte[] { 0, 0, 0, 255 }, device.ReadTexture2D(device.Images[destinationColor.Handle], 4));
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.DepthStencilAttachmentOptimal, device.Images[sourceDepth.Handle].Layout);
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.DepthStencilAttachmentOptimal, device.Images[destinationDepth.Handle].Layout);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-depth-blit-dispose");
            dispose.EncodeDisposeFramebuffer(sourceFramebuffer);
            dispose.EncodeDisposeFramebuffer(destinationFramebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, sourceFramebuffer.Handle);
            Assert.Equal(0u, destinationFramebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan depth framebuffer blit failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_MultipleRenderTargets_Draw_And_Read_Back_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "struct PSOutput { float4 color0 : SV_Target0; float4 color1 : SV_Target1; }; PSOutput main() { PSOutput o; o.color0 = float4(0.25, 0.5, 0.75, 1); o.color1 = float4(0.75, 0.25, 0.5, 1); return o; }",
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
            using var color0 = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var color1 = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = color0 },
                    new GraphicsFrameBuffer.Attachment { Texture = color1 },
                ],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-mrt-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(color0);
                create.EncodeAllocateTexture2D(color0, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(color1);
                create.EncodeAllocateTexture2D(color1, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.Vulkan.VkFramebufferResource native = device.Framebuffers[framebuffer.Handle];
            Assert.Equal(2, native.AttachmentViews.Length);
            Assert.Equal(2, native.ColorFormats.Count);
            Assert.Equal(color0.Handle, native.ColorHandles[0]);
            Assert.Equal(color1.Handle, native.ColorHandles[1]);

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-mrt-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.Equal(new byte[] { 64, 128, 191, 255 }, device.ReadTexture2D(device.Images[color0.Handle], 4));
            Assert.Equal(new byte[] { 191, 64, 128, 255 }, device.ReadTexture2D(device.Images[color1.Handle], 4));
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal, device.Images[color0.Handle].Layout);
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal, device.Images[color1.Handle].Layout);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-mrt-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, framebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan MRT framebuffer failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_Prepass_Mrt_Depth_Reused_By_Opaque_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        const string vertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        ShaderCompileResult prepass = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "struct PSOutput { float4 normal : SV_Target0; float4 motionRM : SV_Target1; }; PSOutput main() { PSOutput o; o.normal = float4(0.25, 0.5, 0.75, 1); o.motionRM = float4(0.5, -0.25, 0.75, 1); return o; }",
        });
        ShaderCompileResult opaque = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 0, 1); }",
        });
        if (!prepass.Success || !opaque.Success)
            return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            using var prepassVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, prepass.VertexBytecode!, prepass.FragmentBytecode!, prepass.BindingLayout));
            using var opaqueVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, opaque.VertexBytecode!, opaque.FragmentBytecode!, opaque.BindingLayout));
            float[] vertices =
            [
                -1f, -1f, 0.2f, 0f, 1f, 0.2f, 1f, -1f, 0.2f,
                -1f, -1f, 0.8f, 0f, 1f, 0.8f, 1f, -1f, 0.8f,
            ];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var normals = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var motionMaterial = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Short4);
            using var prepassDepth = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Depth32f);
            using var sceneColor = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var sceneDepth = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Depth32f);
            GraphicsFrameBuffer prepassFramebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = normals },
                    new GraphicsFrameBuffer.Attachment { Texture = motionMaterial },
                    new GraphicsFrameBuffer.Attachment { Texture = prepassDepth, IsDepth = true },
                ],
                1,
                1);
            GraphicsFrameBuffer sceneFramebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = sceneColor },
                    new GraphicsFrameBuffer.Attachment { Texture = sceneDepth, IsDepth = true },
                ],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-prepass-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(normals);
                create.EncodeAllocateTexture2D(normals, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(motionMaterial);
                create.EncodeAllocateTexture2D(motionMaterial, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(prepassDepth);
                create.EncodeAllocateTexture2D(prepassDepth, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(sceneColor);
                create.EncodeAllocateTexture2D(sceneColor, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(sceneDepth);
                create.EncodeAllocateTexture2D(sceneDepth, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(prepassFramebuffer);
                create.EncodeCreateFramebuffer(sceneFramebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.Vulkan.VkFramebufferResource prepassNative = device.Framebuffers[prepassFramebuffer.Handle];
            Assert.Equal(Silk.NET.Vulkan.Format.R8G8B8A8Unorm, prepassNative.ColorFormats[0]);
            Assert.Equal(Silk.NET.Vulkan.Format.R16G16B16A16Sfloat, prepassNative.ColorFormats[1]);
            Assert.Equal(Silk.NET.Vulkan.Format.D32Sfloat, prepassNative.DepthFormat);

            RasterizerState raster = new()
            {
                DepthTest = true,
                DepthWrite = true,
                Depth = RasterizerState.DepthMode.Less,
                CullFace = RasterizerState.PolyFace.None,
            };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-prepass-draw"))
            {
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetRenderTarget(sceneFramebuffer);
                draw.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, Color.Black, depth: 1f);
                draw.SetRenderTarget(prepassFramebuffer);
                draw.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, Color.Black, depth: 1f);
                draw.SetRasterState(in raster);
                draw.SetShader(prepassVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetRenderTargets(sceneFramebuffer, prepassFramebuffer);
                draw.BlitFramebuffer(0, 0, 1, 1, 0, 0, 1, 1, ClearFlags.Depth, BlitFilter.Nearest);
                draw.SetRenderTarget(sceneFramebuffer);
                draw.SetShader(opaqueVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 3, 3);
                device.Execute(draw, wait: true);
            }

            byte[] normalReadback = device.ReadTexture2D(device.Images[normals.Handle], 4);
            Assert.InRange(normalReadback[0], (byte)63, (byte)64);
            Assert.InRange(normalReadback[1], (byte)127, (byte)128);
            Assert.InRange(normalReadback[2], (byte)191, (byte)192);
            Assert.Equal((byte)255, normalReadback[3]);
            Assert.Equal(PrepassHalfReadback(), device.ReadTexture2D(device.Images[motionMaterial.Handle], 8));
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, device.ReadTexture2D(device.Images[sceneColor.Handle], 4));
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.DepthStencilAttachmentOptimal, device.Images[prepassDepth.Handle].Layout);
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.DepthStencilAttachmentOptimal, device.Images[sceneDepth.Handle].Layout);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-prepass-dispose");
            dispose.EncodeDisposeFramebuffer(prepassFramebuffer);
            dispose.EncodeDisposeFramebuffer(sceneFramebuffer);
            device.Execute(dispose, wait: true);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan prepass MRT failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_BlendState_Composites_Draws_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        const string vertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        ShaderCompileResult red = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(1, 0, 0, 1); }",
        });
        ShaderCompileResult green = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 0, 0.5); }",
        });
        if (!red.Success || !green.Success)
            return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            using var redVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, red.VertexBytecode!, red.FragmentBytecode!, red.BindingLayout));
            using var greenVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, green.VertexBytecode!, green.FragmentBytecode!, green.BindingLayout));
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = color }],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-blend-framebuffer-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            RasterizerState opaque = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
            };
            RasterizerState alphaBlend = opaque;
            alphaBlend.DoBlend = true;
            alphaBlend.BlendSrc = RasterizerState.Blending.SrcAlpha;
            alphaBlend.BlendDst = RasterizerState.Blending.OneMinusSrcAlpha;
            alphaBlend.Blend = RasterizerState.BlendMode.Add;
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-blend-framebuffer-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.ClearRenderTarget(ClearFlags.Color, Color.Black);
                draw.SetRasterState(in opaque);
                draw.SetShader(redVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetRasterState(in alphaBlend);
                draw.SetShader(greenVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.Equal(new byte[] { 128, 128, 0, 191 }, device.ReadTexture2D(device.Images[color.Handle], 4));
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal, device.Images[color.Handle].Layout);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-blend-framebuffer-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, framebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan blend-state failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_DepthFramebuffer_Rejects_Farther_Draw_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        const string vertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        ShaderCompileResult red = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(1, 0, 0, 1); }",
        });
        ShaderCompileResult green = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 0, 1); }",
        });
        ShaderCompileResult blue = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 0, 1, 1); }",
        });
        if (!red.Success || !green.Success || !blue.Success)
            return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            using var redVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, red.VertexBytecode!, red.FragmentBytecode!, red.BindingLayout));
            using var greenVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, green.VertexBytecode!, green.FragmentBytecode!, green.BindingLayout));
            using var blueVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, blue.VertexBytecode!, blue.FragmentBytecode!, blue.BindingLayout));
            float[] vertices =
            [
                -1f, -1f, 0.8f, 0f, 1f, 0.8f, 1f, -1f, 0.8f,
                -1f, -1f, 0.2f, 0f, 1f, 0.2f, 1f, -1f, 0.2f,
                -1f, -1f, 0.8f, 0f, 1f, 0.8f, 1f, -1f, 0.8f,
            ];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var depth = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Depth32f);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = color },
                    new GraphicsFrameBuffer.Attachment { Texture = depth, IsDepth = true },
                ],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-depth-framebuffer-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(depth);
                create.EncodeAllocateTexture2D(depth, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.Vulkan.VkFramebufferResource native = device.Framebuffers[framebuffer.Handle];
            Assert.Equal(Silk.NET.Vulkan.Format.D32Sfloat, native.DepthFormat);
            Assert.Equal(depth.Handle, native.DepthHandle);
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.DepthStencilAttachmentOptimal, device.Images[depth.Handle].Layout);

            RasterizerState raster = new()
            {
                DepthTest = true,
                DepthWrite = true,
                Depth = RasterizerState.DepthMode.Less,
                CullFace = RasterizerState.PolyFace.None,
            };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-depth-framebuffer-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, Color.Black, depth: 1f);
                draw.SetRasterState(in raster);
                draw.SetShader(redVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetShader(greenVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 3, 3);
                draw.SetShader(blueVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 6, 3);
                device.Execute(draw, wait: true);
            }

            Assert.Equal(new byte[] { 0, 255, 0, 255 }, device.ReadTexture2D(device.Images[color.Handle], 4));
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal, device.Images[color.Handle].Layout);
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.DepthStencilAttachmentOptimal, device.Images[depth.Handle].Layout);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-depth-framebuffer-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, framebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan depth framebuffer failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_StencilFramebuffer_Controls_Draws_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        const string vertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        ShaderCompileResult red = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(1, 0, 0, 1); }",
        });
        ShaderCompileResult green = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 1, 0, 1); }",
        });
        ShaderCompileResult blue = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = "float4 main() : SV_Target { return float4(0, 0, 1, 1); }",
        });
        if (!red.Success || !green.Success || !blue.Success)
            return;

        try
        {
            using var device = new Backends.Vulkan.VulkanGraphicsDevice(new GraphicsDeviceOptions { Backend = GraphicsBackend.Vulkan });
            device.Initialize(null);
            using var redVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, red.VertexBytecode!, red.FragmentBytecode!, red.BindingLayout));
            using var greenVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, green.VertexBytecode!, green.FragmentBytecode!, green.BindingLayout));
            using var blueVariant = new ShaderVariant(new CompiledShaderBytecode(ShaderLanguage.Hlsl, ShaderBytecodeFormat.SpirV, blue.VertexBytecode!, blue.FragmentBytecode!, blue.BindingLayout));
            float[] vertices = [-1f, -1f, 0.5f, 0f, 1f, 0.5f, 1f, -1f, 0.5f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            using var depthStencil = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Depth24Stencil8);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [
                    new GraphicsFrameBuffer.Attachment { Texture = color },
                    new GraphicsFrameBuffer.Attachment { Texture = depthStencil, IsDepth = true },
                ],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-stencil-framebuffer-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateTexture(depthStencil);
                create.EncodeAllocateTexture2D(depthStencil, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.Vulkan.VkFramebufferResource native = device.Framebuffers[framebuffer.Handle];
            Assert.Equal(Silk.NET.Vulkan.Format.D24UnormS8Uint, native.DepthFormat);

            RasterizerState writeStencil = new()
            {
                DepthTest = false,
                DepthWrite = false,
                CullFace = RasterizerState.PolyFace.None,
                StencilEnabled = true,
                StencilFunc = RasterizerState.StencilFunction.Always,
                StencilRef = 1,
                StencilReadMask = 255,
                StencilWriteMask = 255,
                StencilPassOp = RasterizerState.StencilOp.Replace,
            };
            RasterizerState passStencil = writeStencil;
            passStencil.StencilFunc = RasterizerState.StencilFunction.Equal;
            passStencil.StencilWriteMask = 0;
            passStencil.StencilPassOp = RasterizerState.StencilOp.Keep;
            RasterizerState rejectStencil = passStencil;
            rejectStencil.StencilRef = 2;
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-stencil-framebuffer-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth | ClearFlags.Stencil, Color.Black, depth: 1f, stencil: 0);
                draw.SetRasterState(in writeStencil);
                draw.SetShader(redVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetRasterState(in passStencil);
                draw.SetShader(greenVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                draw.SetRasterState(in rejectStencil);
                draw.SetShader(blueVariant);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Assert.Equal(new byte[] { 0, 255, 0, 255 }, device.ReadTexture2D(device.Images[color.Handle], 4));
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.DepthStencilAttachmentOptimal, device.Images[depthStencil.Handle].Layout);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-stencil-framebuffer-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, framebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan stencil framebuffer failure: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    [Fact]
    public void Optional_Vulkan_CubemapMipFramebuffer_Draws_And_Reads_Or_Skip()
    {
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }",
            FragmentSource = "float4 main() : SV_Target { return float4(0.25, 0.5, 0.75, 1); }",
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
            using var cubemap = new GraphicsTexture(TextureType.TextureCubeMap, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
                [new GraphicsFrameBuffer.Attachment { Texture = cubemap, IsCubeFace = true, CubeFace = 4, MipLevel = 1 }],
                1,
                1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-cubemap-mip-framebuffer-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
                create.EncodeCreateTexture(cubemap);
                for (int face = 0; face < 6; face++)
                {
                    byte[] basePixels = new byte[16];
                    create.EncodeAllocateTextureCubeFace(cubemap, face, 0, 2, basePixels);
                    create.EncodeAllocateTextureCubeFace(cubemap, face, 1, 1, new byte[4]);
                }
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, wait: true);
            }

            Backends.Vulkan.VkFramebufferResource native = device.Framebuffers[framebuffer.Handle];
            Assert.Single(native.AttachmentViews);
            Assert.NotEqual(0ul, native.AttachmentViews[0].Handle);
            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-cubemap-mip-framebuffer-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.SetViewport(0, 0, 1, 1);
                draw.DisableScissor();
                draw.SetShader(variant);
                draw.SetRasterState(in raster);
                draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                device.Execute(draw, wait: true);
            }

            Backends.Vulkan.VkImageResource resource = device.Images[cubemap.Handle];
            Assert.Equal(new byte[] { 64, 128, 191, 255 }, device.ReadTextureCubeFace(resource, 4, 1, 4));
            Assert.Equal(Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal, resource.Layout);

            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("vulkan-cubemap-mip-framebuffer-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, wait: true);
            Assert.Equal(0u, framebuffer.Handle);
        }
        catch (Exception ex)
        {
            Assert.True(IsExpectedGpuUnavailable(ex), $"Unexpected Vulkan cubemap mip framebuffer failure: {ex.GetType().FullName}: {ex.Message}");
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

    private static byte[] PrepassHalfReadback()
    {
        Half[] values = [(Half)0.5f, (Half)(-0.25f), (Half)0.75f, (Half)1f];
        return System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
    }

    private static GraphicsBuffer? GetGlobalUniformBuffer() =>
        (GraphicsBuffer?)typeof(Rendering.GlobalUniforms)
            .GetField("s_uniformBuffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null);

    private static void SetGlobalUniformBuffer(GraphicsBuffer? buffer) =>
        typeof(Rendering.GlobalUniforms)
            .GetField("s_uniformBuffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, buffer);

    private static Resources.Shader CreateUnlitMaterialContractShader()
    {
        Rendering.Shaders.ShaderProperty tiling = new(new Float2(0.25f, 1f)) { Name = "_Tiling", DisplayName = "Tiling" };
        Rendering.Shaders.ShaderProperty offset = new(new Float2(0f, 0.5f)) { Name = "_Offset", DisplayName = "Offset" };
        Rendering.Shaders.ShaderProperty color = new(new Color(0.75f, 0f, 0f, 1f)) { Name = "_MainColor", DisplayName = "Tint" };
        return new Resources.Shader("Unlit Contract", [tiling, offset, color], []);
    }

    private static Resources.Shader CreateUnlitTextureContractShader(Resources.Texture2D texture)
    {
        Rendering.Shaders.ShaderProperty mainTexture = new(texture) { Name = "_MainTex", DisplayName = "Texture" };
        return new Resources.Shader("Unlit Texture Contract", [mainTexture], []);
    }

    private static void RunStandardMaterialBlocksContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string vertexSource = backend == GraphicsBackend.Vulkan
            ? "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }"
            : "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string standardBlock = "cbuffer StandardMaterial : register(b2) { float2 _Tiling; float2 _Offset; float4 _MainColor; float _EmissionIntensity; float _AlphaCutoff; float _Parallax; int _ParallaxSteps; float _TranslucencyStrength; float _ScatteringPower; float _ScatteringDistortion; float _ScatteringScale; }; ";
        const string cutoutFields = "float2 _Tiling; float2 _Offset; float4 _MainColor; float _AlphaCutoff; float3 Padding;";

        var compiler = new DxcShaderCompiler();
        ShaderCompileResult standardFront = CompileMaterialContractShader(compiler, backend, vertexSource, standardBlock + "float4 main() : SV_Target { return float4(_EmissionIntensity, _AlphaCutoff, _Parallax, _ParallaxSteps / 16.0); }");
        ShaderCompileResult standardTail = CompileMaterialContractShader(compiler, backend, vertexSource, standardBlock + "float4 main() : SV_Target { return float4(_TranslucencyStrength, _ScatteringPower, _ScatteringDistortion, _ScatteringScale); }");
        ShaderCompileResult prepass = CompileMaterialContractShader(compiler, backend, vertexSource, "cbuffer PrepassMaterial : register(b2) { " + cutoutFields + " }; float4 main() : SV_Target { return float4(_Tiling.x, _Offset.y, _MainColor.r, _AlphaCutoff); }");
        ShaderCompileResult shadow = CompileMaterialContractShader(compiler, backend, vertexSource, "cbuffer ShadowMaterial : register(b2) { " + cutoutFields + " }; float4 main() : SV_Target { return float4(_Tiling.x, _Offset.y, _MainColor.r, _AlphaCutoff); }");
        if (!standardFront.Success || !standardTail.Success || !prepass.Success || !shadow.Success)
            return;

        using var standardFrontVariant = CreateMaterialContractVariant(standardFront, bytecodeFormat);
        using var standardTailVariant = CreateMaterialContractVariant(standardTail, bytecodeFormat);
        using var prepassVariant = CreateMaterialContractVariant(prepass, bytecodeFormat);
        using var shadowVariant = CreateMaterialContractVariant(shadow, bytecodeFormat);
        using var shader = CreateStandardMaterialContractShader();
        using var defaultsMaterial = new Resources.Material(shader);
        using var standardOverrides = new Resources.Material(shader);
        standardOverrides.SetFloat("_EmissionIntensity", 0.5f);
        standardOverrides.SetFloat("_AlphaCutoff", 0.625f);
        standardOverrides.SetFloat("_Parallax", 0.75f);
        standardOverrides.SetInt("_ParallaxSteps", 14);
        standardOverrides.SetFloat("_TranslucencyStrength", 0.1f);
        standardOverrides.SetFloat("_ScatteringPower", 0.3f);
        standardOverrides.SetFloat("_ScatteringDistortion", 0.5f);
        standardOverrides.SetFloat("_ScatteringScale", 0.7f);
        using var prepassOverrides = new Resources.Material(shader);
        prepassOverrides.SetVector("_Tiling", new Float2(0.5f, 0f));
        prepassOverrides.SetVector("_Offset", new Float2(0f, 0.625f));
        prepassOverrides.SetColor("_MainColor", new Color(0.75f, 0f, 0f, 1f));
        prepassOverrides.SetFloat("_AlphaCutoff", 0.875f);
        using var shadowOverrides = new Resources.Material(shader);
        shadowOverrides.SetVector("_Tiling", new Float2(0.875f, 0f));
        shadowOverrides.SetVector("_Offset", new Float2(0f, 0.75f));
        shadowOverrides.SetVector("_MainColor", new Float4(0.625f, 0f, 0f, 1f));
        shadowOverrides.SetFloat("_AlphaCutoff", 0.5f);

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
            [new GraphicsFrameBuffer.Attachment { Texture = color }],
            8,
            1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("standard-material-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 8, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, wait: true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("standard-material-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, standardFrontVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, standardFrontVariant, standardOverrides, 1);
            DrawMaterialPixel(draw, vertexArray, standardTailVariant, defaultsMaterial, 2);
            DrawMaterialPixel(draw, vertexArray, standardTailVariant, standardOverrides, 3);
            DrawMaterialPixel(draw, vertexArray, prepassVariant, defaultsMaterial, 4);
            DrawMaterialPixel(draw, vertexArray, prepassVariant, prepassOverrides, 5);
            DrawMaterialPixel(draw, vertexArray, shadowVariant, defaultsMaterial, 6);
            DrawMaterialPixel(draw, vertexArray, shadowVariant, shadowOverrides, 7);
            device.Execute(draw, wait: true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.125f, 0.25f, 0.375f, 0.5f);
        AssertMaterialPixel(pixels, 1, 0.5f, 0.625f, 0.75f, 0.875f);
        AssertMaterialPixel(pixels, 2, 0.2f, 0.4f, 0.6f, 0.8f);
        AssertMaterialPixel(pixels, 3, 0.1f, 0.3f, 0.5f, 0.7f);
        AssertMaterialPixel(pixels, 4, 0.125f, 0.25f, 0.375f, 0.25f);
        AssertMaterialPixel(pixels, 5, 0.5f, 0.625f, 0.75f, 0.875f);
        AssertMaterialPixel(pixels, 6, 0.125f, 0.25f, 0.375f, 0.25f);
        AssertMaterialPixel(pixels, 7, 0.875f, 0.75f, 0.625f, 0.5f);

        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("standard-material-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, wait: true);
    }

    private static void RunStandardTransparentContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        GraphicsBuffer? previousGlobalBuffer = GetGlobalUniformBuffer();
        try
        {
            RunStandardTransparentContractCore(device, backend, bytecodeFormat, readback);
        }
        finally
        {
            SetGlobalUniformBuffer(previousGlobalBuffer);
        }
    }

    private static void RunStandardTransparentContractCore(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        Resources.Shader shader = Resources.Shader.LoadDefault(Resources.DefaultShader.StandardTransparent);
        Assert.NotNull(shader);
        Rendering.Shaders.ShaderPass pass = shader.GetPass("StandardTransparent");
        Assert.True(pass.HasHlsl);
        string vertexSource = (string)typeof(Rendering.Shaders.ShaderPass)
            .GetField("_hlslVertexSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(pass)!;
        string fragmentSource = (string)typeof(Rendering.Shaders.ShaderPass)
            .GetField("_hlslFragmentSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(pass)!;
        ShaderCompileResult compiled = CompileMaterialContractShader(new DxcShaderCompiler(), backend, vertexSource, fragmentSource);
        if (!compiled.Success &&
            backend == GraphicsBackend.Vulkan &&
            compiled.ErrorMessage?.Contains("SPIR-V CodeGen not available", StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        Assert.True(compiled.Success, compiled.ErrorMessage);
        ShaderBindingLayout bindingLayout = Assert.IsType<ShaderBindingLayout>(compiled.BindingLayout);
        Assert.Collection(
            bindingLayout.Buffers,
            slot => { Assert.Equal(ShaderBindingKind.Buffer, slot.Kind); Assert.Equal(0, slot.Slot); Assert.Equal("GlobalUniforms", slot.Name); },
            slot => { Assert.Equal(ShaderBindingKind.Buffer, slot.Kind); Assert.Equal(1, slot.Slot); Assert.Equal("ObjectUniforms", slot.Name); },
            slot => { Assert.Equal(ShaderBindingKind.Buffer, slot.Kind); Assert.Equal(2, slot.Slot); Assert.Equal("StandardMaterial", slot.Name); });
        using ShaderVariant variant = CreateMaterialContractVariant(compiled, bytecodeFormat);

        Assert.True(pass.State.DoBlend);
        Assert.Equal(RasterizerState.Blending.SrcAlpha, pass.State.BlendSrc);
        Assert.Equal(RasterizerState.Blending.OneMinusSrcAlpha, pass.State.BlendDst);
        Assert.Equal(RasterizerState.BlendMode.Add, pass.State.Blend);

        byte[] globalUniformBytes = new byte[Rendering.GlobalUniformsData.SizeInBytes];
        Span<float> globalUniformValues = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(globalUniformBytes.AsSpan());
        const int ViewProjectionOffset = 3 * 16;
        for (int diagonal = 0; diagonal < 4; diagonal++)
            globalUniformValues[ViewProjectionOffset + (diagonal * 5)] = 1f;
        using var globalUniformBuffer = new GraphicsBuffer(BufferType.UniformBuffer, globalUniformBytes, dynamic: true);
        using var mainTexture = new Resources.Texture2D();
        using var normalTexture = new Resources.Texture2D();
        using var surfaceTexture = new Resources.Texture2D();
        using var emissionTexture = new Resources.Texture2D();
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetTexture("_MainTex", mainTexture);
        overridesMaterial.SetTexture("_NormalTex", normalTexture);
        overridesMaterial.SetTexture("_SurfaceTex", surfaceTexture);
        overridesMaterial.SetTexture("_EmissionTex", emissionTexture);
        overridesMaterial.SetColor("_MainColor", new Color(1f, 1f, 1f, 0.25f));
        Rendering.StandardMaterialUniformsData packedMaterial = Rendering.MaterialUniformPacking.PackStandard(overridesMaterial._properties, shader);
        Assert.Equal(0.25f, packedMaterial._MainColor.W);

        float[] vertices =
        [
            -1f, -1f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 0f, 0f, 1f,
            -1f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 1f, 0f, 0f, 1f,
            1f, -1f, 0f, 1f, 0f, 0f, 0f, 1f, 1f, 0f, 0f, 1f,
            1f, -1f, 0f, 1f, 0f, 0f, 0f, 1f, 1f, 0f, 0f, 1f,
            -1f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 1f, 0f, 0f, 1f,
            1f, 1f, 0f, 1f, 1f, 0f, 0f, 1f, 1f, 0f, 0f, 1f,
        ];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        using var depth = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Depth32f);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
            [
                new GraphicsFrameBuffer.Attachment { Texture = color },
                new GraphicsFrameBuffer.Attachment { Texture = depth, IsDepth = true },
            ],
            2,
            1);
        var format = new VertexFormat([
            new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3),
            new(VertexFormat.VertexSemantic.TexCoord0, VertexFormat.VertexType.Float, 2),
            new(VertexFormat.VertexSemantic.Normal, VertexFormat.VertexType.Float, 3),
            new(VertexFormat.VertexSemantic.Tangent, VertexFormat.VertexType.Float, 4),
        ]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);

        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("standard-transparent-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, dynamic: true, vertexBytes);
            create.EncodeCreateBuffer(globalUniformBuffer, dynamic: true, globalUniformBytes);
            EncodeContractTexture(create, mainTexture, new byte[] { 255, 255, 255, 255 });
            EncodeContractTexture(create, normalTexture, new byte[] { 128, 128, 255, 255 });
            EncodeContractTexture(create, surfaceTexture, new byte[] { 255, 255, 0, 255 });
            EncodeContractTexture(create, emissionTexture, new byte[] { 0, 0, 0, 255 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateTexture(depth);
            create.EncodeAllocateTexture2D(depth, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, wait: true);
        }
        SetGlobalUniformBuffer(globalUniformBuffer);

        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("standard-transparent-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, new Color(0.2f, 0.3f, 0.4f, 1f));
            draw.DisableScissor();
            draw.SetBuffer("GlobalUniforms", globalUniformBuffer);
            draw.SetShader(variant);
            draw.SetRasterState(pass.State);
            draw.SetMaterialProperties(overridesMaterial);
            draw.SetViewport(0, 0, 2, 1);
            draw.DrawArrays(vertexArray, Topology.Triangles, 0, 6);
            device.Execute(draw, wait: true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.4f, 0.475f, 0.55f, 0.8125f);
        AssertMaterialPixel(pixels, 1, 0.4f, 0.475f, 0.55f, 0.8125f);

        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("standard-transparent-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, wait: true);
    }

    private static void RunStandardMaterialTextureContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string vertexSource = backend == GraphicsBackend.Vulkan
            ? "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }"
            : "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        string bindingPrefix0 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string bindingPrefix1 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(1)]] " : string.Empty;
        string bindingPrefix2 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(2)]] " : string.Empty;
        string bindingPrefix3 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(3)]] " : string.Empty;
        string fragmentSource =
            bindingPrefix0 + "Texture2D _MainTex : register(t0); " + bindingPrefix0 + "SamplerState _MainTexSampler : register(s0); " +
            bindingPrefix1 + "Texture2D _NormalTex : register(t1); " + bindingPrefix1 + "SamplerState _NormalTexSampler : register(s1); " +
            bindingPrefix2 + "Texture2D _SurfaceTex : register(t2); " + bindingPrefix2 + "SamplerState _SurfaceTexSampler : register(s2); " +
            bindingPrefix3 + "Texture2D _EmissionTex : register(t3); " + bindingPrefix3 + "SamplerState _EmissionTexSampler : register(s3); " +
            "float4 main() : SV_Target { float2 uv = float2(0.5, 0.5); return float4(_MainTex.Sample(_MainTexSampler, uv).r, _NormalTex.Sample(_NormalTexSampler, uv).g, _SurfaceTex.Sample(_SurfaceTexSampler, uv).b, _EmissionTex.Sample(_EmissionTexSampler, uv).a); }";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = CompileMaterialContractShader(compiler, backend, vertexSource, fragmentSource);
        if (!compiled.Success)
            return;

        using var variant = CreateMaterialContractVariant(compiled, bytecodeFormat);
        using var defaultMain = new Resources.Texture2D();
        using var defaultNormal = new Resources.Texture2D();
        using var defaultSurface = new Resources.Texture2D();
        using var defaultEmission = new Resources.Texture2D();
        using var overrideMain = new Resources.Texture2D();
        using var overrideNormal = new Resources.Texture2D();
        using var overrideSurface = new Resources.Texture2D();
        using var overrideEmission = new Resources.Texture2D();
        using var shader = CreateStandardTextureContractShader(defaultMain, defaultNormal, defaultSurface, defaultEmission);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetTexture("_MainTex", overrideMain);
        overridesMaterial.SetTexture("_NormalTex", overrideNormal);
        overridesMaterial.SetTexture("_SurfaceTex", overrideSurface);
        overridesMaterial.SetTexture("_EmissionTex", overrideEmission);

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 4, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("standard-texture-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, defaultMain, new byte[] { 64, 0, 0, 255 });
            EncodeContractTexture(create, defaultNormal, new byte[] { 0, 128, 0, 255 });
            EncodeContractTexture(create, defaultSurface, new byte[] { 0, 0, 192, 255 });
            EncodeContractTexture(create, defaultEmission, new byte[] { 0, 0, 0, 255 });
            EncodeContractTexture(create, overrideMain, new byte[] { 192, 0, 0, 255 });
            EncodeContractTexture(create, overrideNormal, new byte[] { 0, 64, 0, 255 });
            EncodeContractTexture(create, overrideSurface, new byte[] { 0, 0, 128, 255 });
            EncodeContractTexture(create, overrideEmission, new byte[] { 0, 0, 0, 128 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 4, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("standard-texture-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetShader(variant);
            draw.SetRasterState(in raster);
            draw.SetMaterialProperties(defaultsMaterial);
            draw.SetViewport(0, 0, 1, 1);
            draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
            draw.SetMaterialProperties(overridesMaterial);
            draw.SetViewport(1, 0, 1, 1);
            draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
            device.Execute(draw, true);
        }

        Assert.Equal(new byte[] { 64, 128, 192, 255, 192, 64, 128, 128 }, readback(color));
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("standard-texture-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunGradientSkyboxMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string vertexSource = backend == GraphicsBackend.Vulkan
            ? "struct VSInput { [[vk::location(0)]] float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }"
            : "struct VSInput { float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string fragmentSource = "cbuffer GradientPS : register(b2) { float4 _TopColor; float4 _BottomColor; float _Exponent; }; float4 main() : SV_Target { return float4(_TopColor.r, _BottomColor.g, _Exponent / 4.0, _TopColor.a); }";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = CompileMaterialContractShader(compiler, backend, vertexSource, fragmentSource);
        if (!compiled.Success)
            return;

        using var variant = CreateMaterialContractVariant(compiled, bytecodeFormat);
        using var shader = CreateGradientSkyboxContractShader();
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetColor("_TopColor", new Color(0.875f, 0f, 0f, 1f));
        overridesMaterial.SetVector("_BottomColor", new Float4(0f, 0.25f, 0f, 1f));
        overridesMaterial.SetFloat("_Exponent", 3f);

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 2, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("gradient-skybox-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("gradient-skybox-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, variant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, variant, overridesMaterial, 1);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.125f, 0.75f, 0.5f, 0.5f);
        AssertMaterialPixel(pixels, 1, 0.875f, 0.25f, 0.75f, 1f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("gradient-skybox-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunCubemapSkyboxMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string[] bindings = backend == GraphicsBackend.Vulkan
            ? ["[[vk::binding(0)]] ", "[[vk::binding(1)]] ", "[[vk::binding(2)]] ", "[[vk::binding(3)]] ", "[[vk::binding(4)]] ", "[[vk::binding(5)]] "]
            : [string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty];
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string block = "cbuffer CubemapSkyboxPS : register(b2) { float4 _Tint; float _Exposure; float3 _Padding; }; ";
        string resources = bindings[0] + "Texture2D _CubeRight : register(t0); " + bindings[0] + "SamplerState _CubeRightSampler : register(s0); "
            + bindings[1] + "Texture2D _CubeLeft : register(t1); " + bindings[1] + "SamplerState _CubeLeftSampler : register(s1); "
            + bindings[2] + "Texture2D _CubeTop : register(t2); " + bindings[2] + "SamplerState _CubeTopSampler : register(s2); "
            + bindings[3] + "Texture2D _CubeBottom : register(t3); " + bindings[3] + "SamplerState _CubeBottomSampler : register(s3); "
            + bindings[4] + "Texture2D _CubeFront : register(t4); " + bindings[4] + "SamplerState _CubeFrontSampler : register(s4); "
            + bindings[5] + "Texture2D _CubeBack : register(t5); " + bindings[5] + "SamplerState _CubeBackSampler : register(s5); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult constants = CompileMaterialContractShader(compiler, backend, vertexSource, block + "float4 main() : SV_Target { return float4(_Tint.rgb, _Exposure / 4.0); }");
        ShaderCompileResult firstFaces = CompileMaterialContractShader(compiler, backend, vertexSource, block + resources + "float4 main() : SV_Target { float2 uv = float2(0.5, 0.5); return float4(_CubeRight.Sample(_CubeRightSampler, uv).r, _CubeLeft.Sample(_CubeLeftSampler, uv).g, _CubeTop.Sample(_CubeTopSampler, uv).b, _CubeBottom.Sample(_CubeBottomSampler, uv).a); }");
        ShaderCompileResult lastFaces = CompileMaterialContractShader(compiler, backend, vertexSource, block + resources + "float4 main() : SV_Target { float2 uv = float2(0.5, 0.5); return float4(_CubeFront.Sample(_CubeFrontSampler, uv).r, _CubeBack.Sample(_CubeBackSampler, uv).g, _Tint.b, _Exposure / 4.0); }");
        if (!constants.Success || !firstFaces.Success || !lastFaces.Success)
            return;

        using var constantsVariant = CreateMaterialContractVariant(constants, bytecodeFormat);
        using var firstFacesVariant = CreateMaterialContractVariant(firstFaces, bytecodeFormat);
        using var lastFacesVariant = CreateMaterialContractVariant(lastFaces, bytecodeFormat);
        Resources.Texture2D[] defaultFaces = [new(), new(), new(), new(), new(), new()];
        Resources.Texture2D[] overrideFaces = [new(), new(), new(), new(), new(), new()];
        using var shader = CreateCubemapSkyboxContractShader(defaultFaces);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetColor("_Tint", new Color(0.25f, 0.5f, 0.75f, 1f));
        overridesMaterial.SetFloat("_Exposure", 2f);
        string[] names = ["_CubeRight", "_CubeLeft", "_CubeTop", "_CubeBottom", "_CubeFront", "_CubeBack"];
        for (int i = 0; i < names.Length; i++)
            overridesMaterial.SetTexture(names[i], overrideFaces[i]);

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 6, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("cubemap-skybox-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            byte[][] defaultPixels = [[32, 1, 1, 255], [1, 64, 1, 255], [1, 1, 96, 255], [1, 1, 1, 128], [160, 1, 1, 255], [1, 192, 1, 255]];
            byte[][] overridePixels = [[224, 1, 1, 255], [1, 160, 1, 255], [1, 1, 128, 255], [1, 1, 1, 64], [96, 1, 1, 255], [1, 80, 1, 255]];
            for (int i = 0; i < defaultFaces.Length; i++)
            {
                EncodeContractTexture(create, defaultFaces[i], defaultPixels[i]);
                EncodeContractTexture(create, overrideFaces[i], overridePixels[i]);
            }
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 6, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("cubemap-skybox-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, constantsVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, constantsVariant, overridesMaterial, 1);
            DrawMaterialPixel(draw, vertexArray, firstFacesVariant, defaultsMaterial, 2);
            DrawMaterialPixel(draw, vertexArray, firstFacesVariant, overridesMaterial, 3);
            DrawMaterialPixel(draw, vertexArray, lastFacesVariant, defaultsMaterial, 4);
            DrawMaterialPixel(draw, vertexArray, lastFacesVariant, overridesMaterial, 5);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 1f, 1f, 1f, 0.25f);
        AssertMaterialPixel(pixels, 1, 0.25f, 0.5f, 0.75f, 0.5f);
        AssertMaterialPixel(pixels, 2, 32f / 255f, 64f / 255f, 96f / 255f, 128f / 255f);
        AssertMaterialPixel(pixels, 3, 224f / 255f, 160f / 255f, 128f / 255f, 64f / 255f);
        AssertMaterialPixel(pixels, 4, 160f / 255f, 192f / 255f, 1f, 0.25f);
        AssertMaterialPixel(pixels, 5, 96f / 255f, 80f / 255f, 0.75f, 0.5f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("cubemap-skybox-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
        for (int i = 0; i < defaultFaces.Length; i++)
        {
            defaultFaces[i].Dispose();
            overrideFaces[i].Dispose();
        }
    }

    private static void RunProceduralSkyboxMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string vertexSource = "cbuffer SkyVS : register(b2) { float2 Resolution; float fogDensity; float3 _SunDir; }; struct VSInput { " + location + "float3 position : POSITION; }; struct VSOutput { float4 position : SV_Position; float4 data : TEXCOORD0; }; VSOutput main(VSInput input) { VSOutput output; output.position = float4(input.position, 1); output.data = float4(Resolution.x / 8.0, fogDensity, _SunDir.z, _SunDir.x); return output; }";
        const string fragmentSource = "struct PSInput { float4 position : SV_Position; float4 data : TEXCOORD0; }; float4 main(PSInput input) : SV_Target { return input.data; }";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = CompileMaterialContractShader(compiler, backend, vertexSource, fragmentSource);
        if (!compiled.Success)
            return;

        using var variant = CreateMaterialContractVariant(compiled, bytecodeFormat);
        using var shader = CreateProceduralSkyboxContractShader();
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetVector("Resolution", new Float2(6f, 1f));
        overridesMaterial.SetFloat("fogDensity", 0.25f);
        overridesMaterial.SetVector("_SunDir", new Float3(0.875f, 0f, 0.5f));

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 2, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("procedural-skybox-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("procedural-skybox-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, variant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, variant, overridesMaterial, 1);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.25f, 0.5f, 0.75f, 0.125f);
        AssertMaterialPixel(pixels, 1, 0.75f, 0.25f, 0.5f, 0.875f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("procedural-skybox-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunTonemapperMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        string fragmentSource = "cbuffer TonemapperPS : register(b2) { float Contrast; float Saturation; }; " + binding + "Texture2D _MainTex : register(t0); " + binding + "SamplerState _MainTexSampler : register(s0); float4 main() : SV_Target { float4 sampled = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); return float4(Contrast / 2.0, Saturation / 2.0, sampled.b, sampled.a); }";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = CompileMaterialContractShader(compiler, backend, vertexSource, fragmentSource);
        if (!compiled.Success)
            return;

        using var variant = CreateMaterialContractVariant(compiled, bytecodeFormat);
        using var defaultTexture = new Resources.Texture2D();
        using var overrideTexture = new Resources.Texture2D();
        using var shader = CreateTonemapperContractShader(defaultTexture);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetFloat("Contrast", 1.5f);
        overridesMaterial.SetFloat("Saturation", 0.5f);
        overridesMaterial.SetTexture("_MainTex", overrideTexture);

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 2, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("tonemapper-material-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, defaultTexture, new byte[] { 0, 0, 192, 255 });
            EncodeContractTexture(create, overrideTexture, new byte[] { 0, 0, 128, 128 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("tonemapper-material-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, variant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, variant, overridesMaterial, 1);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.25f, 0.5f, 0.75f, 1f);
        AssertMaterialPixel(pixels, 1, 0.75f, 0.25f, 0.5f, 0.5f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("tonemapper-material-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunGizmoIconMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding0 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string binding1 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(1)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string block = "cbuffer GizmoIconMaterial : register(b2) { float4 _IconColor; float3 _IconCenter; float _IconScale; }; ";
        string resources = binding0 + "Texture2D _MainTex : register(t0); " + binding0 + "SamplerState _MainTexSampler : register(s0); "
            + binding1 + "Texture2D _CameraDepthTexture : register(t1); " + binding1 + "SamplerState _CameraDepthSampler : register(s1); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult constants = CompileMaterialContractShader(compiler, backend, vertexSource, block + "float4 main() : SV_Target { return float4(_IconColor.r, _IconCenter.x, _IconScale / 4.0, _IconColor.a); }");
        ShaderCompileResult shaded = CompileMaterialContractShader(compiler, backend, vertexSource, block + resources + "float4 main() : SV_Target { float4 color = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)) * _IconColor; if (step(_CameraDepthTexture.Sample(_CameraDepthSampler, float2(0.5, 0.5)).r, 0.49999) > 0.5) { color.rgb *= 0.5; color.a *= 0.3; } return color; }");
        if (!constants.Success || !shaded.Success)
            return;

        using var constantsVariant = CreateMaterialContractVariant(constants, bytecodeFormat);
        using var shadedVariant = CreateMaterialContractVariant(shaded, bytecodeFormat);
        using var defaultMain = new Resources.Texture2D();
        using var overrideMain = new Resources.Texture2D();
        using var depthVisible = new Resources.Texture2D();
        using var depthOccluded = new Resources.Texture2D();
        using var shader = CreateGizmoIconContractShader(defaultMain);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetVector("_IconColor", new Float4(0.5f, 0.5f, 0.5f, 0.625f));
        overridesMaterial.SetVector("_IconCenter", new Float3(0.125f, 0.25f, 0.5f));
        overridesMaterial.SetFloat("_IconScale", 2f);
        overridesMaterial.SetTexture("_MainTex", overrideMain);

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 4, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("gizmo-icon-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, defaultMain, new byte[] { 64, 128, 192, 255 });
            EncodeContractTexture(create, overrideMain, new byte[] { 200, 120, 240, 128 });
            EncodeContractTexture(create, depthVisible, new byte[] { 224, 0, 0, 255 });
            EncodeContractTexture(create, depthOccluded, new byte[] { 32, 0, 0, 255 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 4, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("gizmo-icon-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, constantsVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, constantsVariant, overridesMaterial, 1);
            draw.SetGlobalTexture("_CameraDepthTexture", depthVisible);
            DrawMaterialPixel(draw, vertexArray, shadedVariant, defaultsMaterial, 2);
            draw.SetGlobalTexture("_CameraDepthTexture", depthOccluded);
            DrawMaterialPixel(draw, vertexArray, shadedVariant, overridesMaterial, 3);
            draw.ClearGlobalTexture("_CameraDepthTexture");
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 1f, 0f, 0.25f, 1f);
        AssertMaterialPixel(pixels, 1, 0.5f, 0.125f, 0.5f, 0.625f);
        AssertMaterialPixel(pixels, 2, 64f / 255f, 128f / 255f, 192f / 255f, 1f);
        AssertMaterialPixel(pixels, 3, 50f / 255f, 30f / 255f, 60f / 255f, 24f / 255f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("gizmo-icon-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunDefaultUIMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string block = "cbuffer DefaultUIMaterial : register(b2) { float2 _Tiling; float2 _Offset; float4 _MainColor; float4x4 _ClipToLocal; float4 _ClipRect; float _ClipRadius; float _ClipSoftness; float _ClipEnable; float _ClipPadding; }; ";
        string resources = binding + "Texture2D _MainTex : register(t0); " + binding + "SamplerState _MainTexSampler : register(s0); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult front = CompileMaterialContractShader(compiler, backend, vertexSource, block + "float4 main() : SV_Target { return float4(_Tiling.x, _Offset.y, _MainColor.r, _ClipEnable); }");
        ShaderCompileResult clip = CompileMaterialContractShader(compiler, backend, vertexSource, block + "float4 main() : SV_Target { return float4(_ClipToLocal._m00, _ClipRect.x, _ClipRadius, _ClipSoftness); }");
        ShaderCompileResult shaded = CompileMaterialContractShader(compiler, backend, vertexSource, block + resources + "float4 main() : SV_Target { return _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)) * _MainColor; }");
        if (!front.Success || !clip.Success || !shaded.Success)
            return;

        using var frontVariant = CreateMaterialContractVariant(front, bytecodeFormat);
        using var clipVariant = CreateMaterialContractVariant(clip, bytecodeFormat);
        using var shadedVariant = CreateMaterialContractVariant(shaded, bytecodeFormat);
        using var defaultMain = new Resources.Texture2D();
        using var overrideMain = new Resources.Texture2D();
        using var shader = CreateDefaultUIContractShader(defaultMain);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetVector("_Tiling", new Float2(0.5f, 0f));
        overridesMaterial.SetVector("_Offset", new Float2(0f, 0.25f));
        overridesMaterial.SetColor("_MainColor", new Color(0.75f, 0.5f, 0.5f, 0.625f));
        overridesMaterial.SetMatrix("_ClipToLocal", new Float4x4(
            0.5f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f));
        overridesMaterial.SetVector("_ClipRect", new Float4(0.125f, 0.25f, 0.75f, 0.875f));
        overridesMaterial.SetFloat("_ClipRadius", 0.25f);
        overridesMaterial.SetFloat("_ClipSoftness", 0.375f);
        overridesMaterial.SetFloat("_ClipEnable", 1f);
        overridesMaterial.SetTexture("_MainTex", overrideMain);

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 6, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("default-ui-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, defaultMain, new byte[] { 64, 128, 192, 255 });
            EncodeContractTexture(create, overrideMain, new byte[] { 200, 120, 240, 128 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 6, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("default-ui-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, frontVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, frontVariant, overridesMaterial, 1);
            DrawMaterialPixel(draw, vertexArray, clipVariant, defaultsMaterial, 2);
            DrawMaterialPixel(draw, vertexArray, clipVariant, overridesMaterial, 3);
            DrawMaterialPixel(draw, vertexArray, shadedVariant, defaultsMaterial, 4);
            DrawMaterialPixel(draw, vertexArray, shadedVariant, overridesMaterial, 5);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 1f, 0f, 1f, 0f);
        AssertMaterialPixel(pixels, 1, 0.5f, 0.25f, 0.75f, 1f);
        AssertMaterialPixel(pixels, 2, 0f, 0f, 0f, 0f);
        AssertMaterialPixel(pixels, 3, 0.5f, 0.125f, 0.25f, 0.375f);
        AssertMaterialPixel(pixels, 4, 64f / 255f, 128f / 255f, 192f / 255f, 1f);
        AssertMaterialPixel(pixels, 5, (200f / 255f) * 0.75f, (120f / 255f) * 0.5f, (240f / 255f) * 0.5f, (128f / 255f) * 0.625f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("default-ui-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunDefaultTextMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string block = "cbuffer DefaultUIMaterial : register(b2) { float2 _Tiling; float2 _Offset; float4 _MainColor; float4x4 _ClipToLocal; float4 _ClipRect; float _ClipRadius; float _ClipSoftness; float _ClipEnable; float _ClipPadding; }; ";
        string resources = binding + "Texture2D _MainTex : register(t0); " + binding + "SamplerState _MainTexSampler : register(s0); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult constants = CompileMaterialContractShader(compiler, backend, vertexSource, block + "float4 main() : SV_Target { return float4(_Tiling.x, _Offset.y, _MainColor.r, _ClipEnable); }");
        ShaderCompileResult shaded = CompileMaterialContractShader(compiler, backend, vertexSource, block + resources + "float4 main() : SV_Target { float sd = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)).r; float coverage = saturate((sd - 0.5) * 8.0 + 0.5); return _MainColor * coverage; }");
        if (!constants.Success || !shaded.Success)
            return;

        using var constantsVariant = CreateMaterialContractVariant(constants, bytecodeFormat);
        using var shadedVariant = CreateMaterialContractVariant(shaded, bytecodeFormat);
        using var inside = new Resources.Texture2D();
        using var outside = new Resources.Texture2D();
        using var shader = CreateDefaultUIContractShader(inside);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetVector("_Tiling", new Float2(0.5f, 0f));
        overridesMaterial.SetVector("_Offset", new Float2(0f, 0.25f));
        overridesMaterial.SetColor("_MainColor", new Color(0.75f, 0.5f, 0.25f, 0.625f));
        overridesMaterial.SetFloat("_ClipEnable", 1f);
        overridesMaterial.SetTexture("_MainTex", outside);

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 4, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("default-text-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, inside, new byte[] { 255, 0, 0, 255 });
            EncodeContractTexture(create, outside, new byte[] { 0, 0, 0, 255 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 4, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("default-text-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, constantsVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, constantsVariant, overridesMaterial, 1);
            DrawMaterialPixel(draw, vertexArray, shadedVariant, defaultsMaterial, 2);
            DrawMaterialPixel(draw, vertexArray, shadedVariant, overridesMaterial, 3);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 1f, 0f, 1f, 0f);
        AssertMaterialPixel(pixels, 1, 0.5f, 0.25f, 0.75f, 1f);
        AssertMaterialPixel(pixels, 2, 1f, 1f, 1f, 1f);
        AssertMaterialPixel(pixels, 3, 0f, 0f, 0f, 0f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("default-text-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunDefaultTextMeshMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string block = "cbuffer UnlitMaterial : register(b2) { float2 _Tiling; float2 _Offset; float4 _MainColor; }; ";
        string resources = binding + "Texture2D _MainTex : register(t0); " + binding + "SamplerState _MainTexSampler : register(s0); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult constants = CompileMaterialContractShader(compiler, backend, vertexSource, block + "float4 main() : SV_Target { return float4(_Tiling.x, _Offset.y, _MainColor.r, _MainColor.a); }");
        ShaderCompileResult shaded = CompileMaterialContractShader(compiler, backend, vertexSource, block + resources + "float4 main() : SV_Target { float sd = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)).r; float coverage = saturate((sd - 0.5) * 8.0 + 0.5); return _MainColor * coverage; }");
        if (!constants.Success || !shaded.Success)
            return;

        using var constantsVariant = CreateMaterialContractVariant(constants, bytecodeFormat);
        using var shadedVariant = CreateMaterialContractVariant(shaded, bytecodeFormat);
        using var inside = new Resources.Texture2D();
        using var outside = new Resources.Texture2D();
        using var shader = CreateDefaultTextMeshContractShader(inside);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetVector("_Tiling", new Float2(0.5f, 0f));
        overridesMaterial.SetVector("_Offset", new Float2(0f, 0.25f));
        overridesMaterial.SetColor("_MainColor", new Color(0.75f, 0.5f, 0.25f, 0.625f));
        overridesMaterial.SetTexture("_MainTex", outside);

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 4, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("default-textmesh-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, inside, new byte[] { 255, 0, 0, 255 });
            EncodeContractTexture(create, outside, new byte[] { 0, 0, 0, 255 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 4, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("default-textmesh-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, constantsVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, constantsVariant, overridesMaterial, 1);
            DrawMaterialPixel(draw, vertexArray, shadedVariant, defaultsMaterial, 2);
            DrawMaterialPixel(draw, vertexArray, shadedVariant, overridesMaterial, 3);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 1f, 0f, 1f, 1f);
        AssertMaterialPixel(pixels, 1, 0.5f, 0.25f, 0.75f, 0.625f);
        AssertMaterialPixel(pixels, 2, 1f, 1f, 1f, 1f);
        AssertMaterialPixel(pixels, 3, 0f, 0f, 0f, 0f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("default-textmesh-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunSpriteMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string block = "cbuffer UnlitMaterial : register(b2) { float2 _Tiling; float2 _Offset; float4 _MainColor; }; ";
        string resources = binding + "Texture2D _MainTex : register(t0); " + binding + "SamplerState _MainTexSampler : register(s0); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult constants = CompileMaterialContractShader(compiler, backend, vertexSource, block + "float4 main() : SV_Target { return float4(_Tiling.x, _Offset.y, _MainColor.r, _MainColor.a); }");
        ShaderCompileResult shaded = CompileMaterialContractShader(compiler, backend, vertexSource, block + resources + "float4 main() : SV_Target { return _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)) * _MainColor; }");
        if (!constants.Success || !shaded.Success)
            return;

        using var constantsVariant = CreateMaterialContractVariant(constants, bytecodeFormat);
        using var shadedVariant = CreateMaterialContractVariant(shaded, bytecodeFormat);
        using var defaultMain = new Resources.Texture2D();
        using var overrideMain = new Resources.Texture2D();
        using var shader = CreateDefaultTextMeshContractShader(defaultMain);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetVector("_Tiling", new Float2(0.5f, 0f));
        overridesMaterial.SetVector("_Offset", new Float2(0f, 0.25f));
        overridesMaterial.SetColor("_MainColor", new Color(0.75f, 0.5f, 0.25f, 0.625f));
        overridesMaterial.SetTexture("_MainTex", overrideMain);

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 4, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("sprite-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, defaultMain, new byte[] { 64, 128, 192, 255 });
            EncodeContractTexture(create, overrideMain, new byte[] { 200, 120, 240, 128 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 4, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("sprite-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, constantsVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, constantsVariant, overridesMaterial, 1);
            DrawMaterialPixel(draw, vertexArray, shadedVariant, defaultsMaterial, 2);
            DrawMaterialPixel(draw, vertexArray, shadedVariant, overridesMaterial, 3);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 1f, 0f, 1f, 1f);
        AssertMaterialPixel(pixels, 1, 0.5f, 0.25f, 0.75f, 0.625f);
        AssertMaterialPixel(pixels, 2, 64f / 255f, 128f / 255f, 192f / 255f, 1f);
        AssertMaterialPixel(pixels, 3, (200f / 255f) * 0.75f, (120f / 255f) * 0.5f, (240f / 255f) * 0.25f, (128f / 255f) * 0.625f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("sprite-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunLineMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        string resources = binding + "Texture2D _MainTex : register(t0); " + binding + "SamplerState _MainTexSampler : register(s0); ";
        string fragmentSource = resources + "struct PSOutput { float4 gAlbedo : SV_Target0; float4 gMotionVector : SV_Target1; float4 gNormal : SV_Target2; float4 gSurface : SV_Target3; }; PSOutput main() { PSOutput o; float4 albedo = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); o.gAlbedo = albedo; o.gMotionVector = float4(0.25, 0.5, 0, 1); o.gNormal = float4(0, 0, 1, 1); o.gSurface = float4(1, 0, 0, 1); return o; }";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = CompileMaterialContractShader(compiler, backend, vertexSource, fragmentSource);
        if (!compiled.Success)
            return;

        using var variant = CreateMaterialContractVariant(compiled, bytecodeFormat);
        using var defaultMain = new Resources.Texture2D();
        using var overrideMain = new Resources.Texture2D();
        using var shader = CreateUnlitTextureContractShader(defaultMain);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetTexture("_MainTex", overrideMain);

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var albedo = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        using var motion = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        using var normal = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        using var surface = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred(
            [
                new GraphicsFrameBuffer.Attachment { Texture = albedo },
                new GraphicsFrameBuffer.Attachment { Texture = motion },
                new GraphicsFrameBuffer.Attachment { Texture = normal },
                new GraphicsFrameBuffer.Attachment { Texture = surface },
            ],
            2,
            1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("line-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, defaultMain, new byte[] { 64, 128, 192, 255 });
            EncodeContractTexture(create, overrideMain, new byte[] { 200, 120, 240, 128 });
            create.EncodeCreateTexture(albedo);
            create.EncodeAllocateTexture2D(albedo, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateTexture(motion);
            create.EncodeAllocateTexture2D(motion, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateTexture(normal);
            create.EncodeAllocateTexture2D(normal, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateTexture(surface);
            create.EncodeAllocateTexture2D(surface, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("line-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, variant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, variant, overridesMaterial, 1);
            device.Execute(draw, true);
        }

        byte[] albedoPixels = readback(albedo);
        AssertMaterialPixel(albedoPixels, 0, 64f / 255f, 128f / 255f, 192f / 255f, 1f);
        AssertMaterialPixel(albedoPixels, 1, 200f / 255f, 120f / 255f, 240f / 255f, 128f / 255f);
        AssertMaterialPixel(readback(normal), 0, 0f, 0f, 1f, 1f);
        AssertMaterialPixel(readback(surface), 0, 1f, 0f, 0f, 1f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("line-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunGizmosGlobalDepthContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        string resources = binding + "Texture2D _CameraDepthTexture : register(t0); " + binding + "SamplerState _CameraDepthSampler : register(s0); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult fragment = CompileMaterialContractShader(compiler, backend, vertexSource, resources + "float4 main() : SV_Target { float4 color = float4(0.8, 0.4, 0.2, 1.0); if (step(_CameraDepthTexture.Sample(_CameraDepthSampler, float2(0.5, 0.5)).r, 0.49999) > 0.5) { color.rgb *= 0.5; color.a *= 0.3; } return color; }");
        if (!fragment.Success)
            return;

        using var variant = CreateMaterialContractVariant(fragment, bytecodeFormat);
        using var depthVisible = new Resources.Texture2D();
        using var depthOccluded = new Resources.Texture2D();
        using var shader = CreateGridContractShader();
        using var material = new Resources.Material(shader);
        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 2, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("gizmos-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, depthVisible, new byte[] { 224, 0, 0, 255 });
            EncodeContractTexture(create, depthOccluded, new byte[] { 32, 0, 0, 255 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("gizmos-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            draw.SetGlobalTexture("_CameraDepthTexture", depthVisible);
            DrawMaterialPixel(draw, vertexArray, variant, material, 0);
            draw.SetGlobalTexture("_CameraDepthTexture", depthOccluded);
            DrawMaterialPixel(draw, vertexArray, variant, material, 1);
            draw.ClearGlobalTexture("_CameraDepthTexture");
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.8f, 0.4f, 0.2f, 1f);
        AssertMaterialPixel(pixels, 1, 0.4f, 0.2f, 0.1f, 0.3f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("gizmos-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunGridMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string gridBlock = "cbuffer GridPS : register(b2) { float4 _GridColor; float _PrimaryGridSize; float _SecondaryGridSize; float _LineWidth; float _Falloff; float _MaxDist; }; ";
        string resources = binding + "Texture2D _CameraDepthTexture : register(t0); " + binding + "SamplerState _CameraDepthSampler : register(s0); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult front = CompileMaterialContractShader(compiler, backend, vertexSource, gridBlock + resources + "float4 main() : SV_Target { return float4(_GridColor.r, _GridColor.a, _PrimaryGridSize / 4.0, _SecondaryGridSize); }");
        ShaderCompileResult tail = CompileMaterialContractShader(compiler, backend, vertexSource, gridBlock + resources + "float4 main() : SV_Target { float depth = _CameraDepthTexture.Sample(_CameraDepthSampler, float2(0.5, 0.5)).r; return float4(_LineWidth, _Falloff / 2.0, _MaxDist / 8.0, depth); }");
        if (!front.Success || !tail.Success)
            return;

        using var frontVariant = CreateMaterialContractVariant(front, bytecodeFormat);
        using var tailVariant = CreateMaterialContractVariant(tail, bytecodeFormat);
        using var depthA = new Resources.Texture2D();
        using var depthB = new Resources.Texture2D();
        using var shader = CreateGridContractShader();
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetColor("_GridColor", new Color(0.75f, 0f, 0f, 0.625f));
        overridesMaterial.SetFloat("_PrimaryGridSize", 2f);
        overridesMaterial.SetFloat("_SecondaryGridSize", 0.25f);
        overridesMaterial.SetFloat("_LineWidth", 0.5f);
        overridesMaterial.SetFloat("_Falloff", 1.5f);
        overridesMaterial.SetFloat("_MaxDist", 2f);

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 4, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("grid-material-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, depthA, new byte[] { 224, 0, 0, 255 });
            EncodeContractTexture(create, depthB, new byte[] { 32, 0, 0, 255 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 4, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("grid-material-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            draw.SetGlobalTexture("_CameraDepthTexture", depthA);
            DrawMaterialPixel(draw, vertexArray, frontVariant, defaultsMaterial, 0);
            draw.SetGlobalTexture("_CameraDepthTexture", depthB);
            DrawMaterialPixel(draw, vertexArray, frontVariant, overridesMaterial, 1);
            draw.SetGlobalTexture("_CameraDepthTexture", depthA);
            DrawMaterialPixel(draw, vertexArray, tailVariant, defaultsMaterial, 2);
            draw.SetGlobalTexture("_CameraDepthTexture", depthB);
            DrawMaterialPixel(draw, vertexArray, tailVariant, overridesMaterial, 3);
            draw.ClearGlobalTexture("_CameraDepthTexture");
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.125f, 0.25f, 0.25f, 0.5f);
        AssertMaterialPixel(pixels, 1, 0.75f, 0.625f, 0.5f, 0.25f);
        AssertMaterialPixel(pixels, 2, 0.125f, 0.5f, 0.75f, 224f / 255f);
        AssertMaterialPixel(pixels, 3, 0.5f, 0.75f, 0.25f, 32f / 255f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("grid-material-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunFXAAMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string block = "cbuffer FXAAPS : register(b0) { float2 _Resolution; float _EdgeThresholdMin; float _EdgeThresholdMax; float _SubpixelQuality; }; ";
        string resources = binding + "Texture2D _MainTex : register(t0); " + binding + "SamplerState _MainTexSampler : register(s0); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult front = CompileMaterialContractShader(compiler, backend, vertexSource, block + "float4 main() : SV_Target { return float4(_Resolution.x / 8.0, _Resolution.y / 8.0, _EdgeThresholdMin * 4.0, _EdgeThresholdMax * 4.0); }");
        ShaderCompileResult tail = CompileMaterialContractShader(compiler, backend, vertexSource, block + resources + "float4 main() : SV_Target { float4 c = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); return float4(_SubpixelQuality, c.r, c.g, c.b); }");
        if (!front.Success || !tail.Success)
            return;

        using var frontVariant = CreateMaterialContractVariant(front, bytecodeFormat);
        using var tailVariant = CreateMaterialContractVariant(tail, bytecodeFormat);
        using var defaultTexture = new Resources.Texture2D();
        using var overrideTexture = new Resources.Texture2D();
        using var shader = CreateFXAAContractShader(defaultTexture);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetVector("_Resolution", new Float2(2f, 4f));
        overridesMaterial.SetFloat("_EdgeThresholdMin", 0.125f);
        overridesMaterial.SetFloat("_EdgeThresholdMax", 0.25f);
        overridesMaterial.SetFloat("_SubpixelQuality", 0.25f);
        overridesMaterial.SetTexture("_MainTex", overrideTexture);
        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 4, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("fxaa-material-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, defaultTexture, new byte[] { 32, 64, 128, 255 });
            EncodeContractTexture(create, overrideTexture, new byte[] { 192, 160, 96, 255 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 4, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }
        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("fxaa-material-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, frontVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, frontVariant, overridesMaterial, 1);
            DrawMaterialPixel(draw, vertexArray, tailVariant, defaultsMaterial, 2);
            DrawMaterialPixel(draw, vertexArray, tailVariant, overridesMaterial, 3);
            device.Execute(draw, true);
        }
        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.5f, 1f, 0.125f, 0.25f);
        AssertMaterialPixel(pixels, 1, 0.25f, 0.5f, 0.5f, 1f);
        AssertMaterialPixel(pixels, 2, 0.75f, 32f / 255f, 64f / 255f, 128f / 255f);
        AssertMaterialPixel(pixels, 3, 0.25f, 192f / 255f, 160f / 255f, 96f / 255f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("fxaa-material-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunBloomMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding0 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string binding1 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(1)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        string mainResource = binding0 + "Texture2D _MainTex : register(t0); " + binding0 + "SamplerState _MainTexSampler : register(s0); ";
        string bloomResource = binding1 + "Texture2D _BloomTex : register(t1); " + binding1 + "SamplerState _BloomTexSampler : register(s1); ";
        const string thresholdBlock = "cbuffer BloomThresholdPS : register(b0) { float _Threshold; float3 Padding; }; ";
        const string compositeBlock = "cbuffer BloomCompositePS : register(b0) { float _Intensity; float3 Padding; }; ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult threshold = CompileMaterialContractShader(compiler, backend, vertexSource, thresholdBlock + mainResource + "float4 main() : SV_Target { float4 c = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); return float4(_Threshold, c.r, c.g, c.b); }");
        ShaderCompileResult downsample = CompileMaterialContractShader(compiler, backend, vertexSource, mainResource + "float4 main() : SV_Target { return _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); }");
        ShaderCompileResult upsample = CompileMaterialContractShader(compiler, backend, vertexSource, mainResource + "float4 main() : SV_Target { return _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); }");
        ShaderCompileResult composite = CompileMaterialContractShader(compiler, backend, vertexSource, compositeBlock + mainResource + bloomResource + "float4 main() : SV_Target { float4 mainColor = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); float4 bloom = _BloomTex.Sample(_BloomTexSampler, float2(0.5, 0.5)); return float4(_Intensity, mainColor.r, bloom.g, bloom.b); }");
        if (!threshold.Success || !downsample.Success || !upsample.Success || !composite.Success)
            return;

        using var thresholdVariant = CreateMaterialContractVariant(threshold, bytecodeFormat);
        using var downsampleVariant = CreateMaterialContractVariant(downsample, bytecodeFormat);
        using var upsampleVariant = CreateMaterialContractVariant(upsample, bytecodeFormat);
        using var compositeVariant = CreateMaterialContractVariant(composite, bytecodeFormat);
        using var defaultMainTexture = new Resources.Texture2D();
        using var overrideMainTexture = new Resources.Texture2D();
        using var upsampleMainTexture = new Resources.Texture2D();
        using var defaultBloomTexture = new Resources.Texture2D();
        using var overrideBloomTexture = new Resources.Texture2D();
        using var shader = CreateBloomContractShader(defaultMainTexture, defaultBloomTexture);
        using var defaultsMaterial = new Resources.Material(shader);
        using var mutableMaterial = new Resources.Material(shader);
        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 6, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("bloom-material-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, defaultMainTexture, new byte[] { 32, 64, 96, 255 });
            EncodeContractTexture(create, overrideMainTexture, new byte[] { 192, 160, 128, 255 });
            EncodeContractTexture(create, upsampleMainTexture, new byte[] { 224, 48, 80, 255 });
            EncodeContractTexture(create, defaultBloomTexture, new byte[] { 16, 128, 240, 255 });
            EncodeContractTexture(create, overrideBloomTexture, new byte[] { 96, 224, 64, 255 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 6, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("bloom-material-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, thresholdVariant, defaultsMaterial, 0);
            mutableMaterial.SetFloat("_Threshold", 0.75f);
            mutableMaterial.SetTexture("_MainTex", overrideMainTexture);
            DrawMaterialPixel(draw, vertexArray, thresholdVariant, mutableMaterial, 1);
            DrawMaterialPixel(draw, vertexArray, downsampleVariant, defaultsMaterial, 2);
            mutableMaterial.SetTexture("_MainTex", upsampleMainTexture);
            DrawMaterialPixel(draw, vertexArray, upsampleVariant, mutableMaterial, 3);
            DrawMaterialPixel(draw, vertexArray, compositeVariant, defaultsMaterial, 4);
            mutableMaterial.SetFloat("_Intensity", 0.25f);
            mutableMaterial.SetTexture("_MainTex", overrideMainTexture);
            mutableMaterial.SetTexture("_BloomTex", overrideBloomTexture);
            DrawMaterialPixel(draw, vertexArray, compositeVariant, mutableMaterial, 5);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.25f, 32f / 255f, 64f / 255f, 96f / 255f);
        AssertMaterialPixel(pixels, 1, 0.75f, 192f / 255f, 160f / 255f, 128f / 255f);
        AssertMaterialPixel(pixels, 2, 32f / 255f, 64f / 255f, 96f / 255f, 1f);
        AssertMaterialPixel(pixels, 3, 224f / 255f, 48f / 255f, 80f / 255f, 1f);
        AssertMaterialPixel(pixels, 4, 0.5f, 32f / 255f, 128f / 255f, 240f / 255f);
        AssertMaterialPixel(pixels, 5, 0.25f, 192f / 255f, 224f / 255f, 64f / 255f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("bloom-material-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunMotionBlurMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding0 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string binding1 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(1)]] " : string.Empty;
        string binding2 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(2)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string block = "cbuffer MotionBlurPS : register(b2) { float2 _Resolution; float _Intensity; int _Samples; float _MaxBlurRadius; float3 Padding; }; ";
        string resources = binding0 + "Texture2D _MainTex : register(t0); " + binding0 + "SamplerState _MainTexSampler : register(s0); "
            + binding1 + "Texture2D _MotionVectorsTex : register(t1); " + binding1 + "SamplerState _MotionVectorsSampler : register(s1); "
            + binding2 + "Texture2D _CameraDepthTexture : register(t2); " + binding2 + "SamplerState _CameraDepthSampler : register(s2); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult front = CompileMaterialContractShader(compiler, backend, vertexSource, block + "float4 main() : SV_Target { return float4(_Resolution.x / 8.0, _Resolution.y / 8.0, _Intensity, float(_Samples) / 16.0); }");
        ShaderCompileResult tail = CompileMaterialContractShader(compiler, backend, vertexSource, block + resources + "float4 main() : SV_Target { float4 mainColor = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); float4 motion = _MotionVectorsTex.Sample(_MotionVectorsSampler, float2(0.5, 0.5)); float4 depth = _CameraDepthTexture.Sample(_CameraDepthSampler, float2(0.5, 0.5)); return float4(_MaxBlurRadius / 64.0, mainColor.r, motion.g, depth.b); }");
        if (!front.Success || !tail.Success)
            return;

        using var frontVariant = CreateMaterialContractVariant(front, bytecodeFormat);
        using var tailVariant = CreateMaterialContractVariant(tail, bytecodeFormat);
        using var defaultMainTexture = new Resources.Texture2D();
        using var overrideMainTexture = new Resources.Texture2D();
        using var defaultMotionTexture = new Resources.Texture2D();
        using var overrideMotionTexture = new Resources.Texture2D();
        using var depthTextureA = new Resources.Texture2D();
        using var depthTextureB = new Resources.Texture2D();
        using var shader = CreateMotionBlurContractShader(defaultMainTexture, defaultMotionTexture);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetVector("_Resolution", new Float2(2f, 4f));
        overridesMaterial.SetFloat("_Intensity", 0.25f);
        overridesMaterial.SetInt("_Samples", 4);
        overridesMaterial.SetFloat("_MaxBlurRadius", 16f);
        overridesMaterial.SetTexture("_MainTex", overrideMainTexture);
        overridesMaterial.SetTexture("_MotionVectorsTex", overrideMotionTexture);
        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 4, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("motion-blur-material-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, defaultMainTexture, new byte[] { 32, 64, 96, 255 });
            EncodeContractTexture(create, overrideMainTexture, new byte[] { 224, 192, 160, 255 });
            EncodeContractTexture(create, defaultMotionTexture, new byte[] { 64, 96, 128, 255 });
            EncodeContractTexture(create, overrideMotionTexture, new byte[] { 128, 160, 192, 255 });
            EncodeContractTexture(create, depthTextureA, new byte[] { 128, 160, 192, 255 });
            EncodeContractTexture(create, depthTextureB, new byte[] { 96, 80, 64, 255 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 4, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("motion-blur-material-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, frontVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, frontVariant, overridesMaterial, 1);
            draw.SetGlobalTexture("_CameraDepthTexture", depthTextureA);
            DrawMaterialPixel(draw, vertexArray, tailVariant, defaultsMaterial, 2);
            draw.SetGlobalTexture("_CameraDepthTexture", depthTextureB);
            DrawMaterialPixel(draw, vertexArray, tailVariant, overridesMaterial, 3);
            draw.ClearGlobalTexture("_CameraDepthTexture");
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.5f, 1f, 0.5f, 0.5f);
        AssertMaterialPixel(pixels, 1, 0.25f, 0.5f, 0.25f, 0.25f);
        AssertMaterialPixel(pixels, 2, 0.5f, 32f / 255f, 96f / 255f, 192f / 255f);
        AssertMaterialPixel(pixels, 3, 0.25f, 224f / 255f, 160f / 255f, 64f / 255f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("motion-blur-material-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunAutoExposureMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding0 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string binding1 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(1)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string adaptBlock = "cbuffer AutoExposureAdaptPS : register(b2) { float _AdaptSpeedUp; float _AdaptSpeedDown; float _HistoryValid; float Padding; }; ";
        const string applyBlock = "cbuffer AutoExposureApplyPS : register(b0) { float _ExposureComp; float _MinExposure; float _MaxExposure; float Padding; }; ";
        string resources = binding0 + "Texture2D _MainTex : register(t0); " + binding0 + "SamplerState _MainTexSampler : register(s0); "
            + binding1 + "Texture2D _AdaptedTex : register(t1); " + binding1 + "SamplerState _AdaptedTexSampler : register(s1); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult extract = CompileMaterialContractShader(compiler, backend, vertexSource, resources + "float4 main() : SV_Target { return _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); }");
        ShaderCompileResult downsample = CompileMaterialContractShader(compiler, backend, vertexSource, resources + "float4 main() : SV_Target { return _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); }");
        ShaderCompileResult adaptFront = CompileMaterialContractShader(compiler, backend, vertexSource, adaptBlock + "float4 main() : SV_Target { return float4(_AdaptSpeedUp / 4.0, _AdaptSpeedDown / 4.0, _HistoryValid, 1.0); }");
        ShaderCompileResult adaptTail = CompileMaterialContractShader(compiler, backend, vertexSource, adaptBlock + resources + "float4 main() : SV_Target { float4 current = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); float4 adapted = _AdaptedTex.Sample(_AdaptedTexSampler, float2(0.5, 0.5)); return float4(current.r, adapted.g, _AdaptSpeedUp / 4.0, _HistoryValid); }");
        ShaderCompileResult applyFront = CompileMaterialContractShader(compiler, backend, vertexSource, applyBlock + "float4 main() : SV_Target { return float4((_ExposureComp + 2.0) / 4.0, _MinExposure / 4.0, _MaxExposure / 8.0, 1.0); }");
        ShaderCompileResult applyTail = CompileMaterialContractShader(compiler, backend, vertexSource, applyBlock + resources + "float4 main() : SV_Target { float4 scene = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); float4 adapted = _AdaptedTex.Sample(_AdaptedTexSampler, float2(0.5, 0.5)); return float4(scene.r, adapted.g, scene.b, adapted.b); }");
        if (!extract.Success || !downsample.Success || !adaptFront.Success || !adaptTail.Success || !applyFront.Success || !applyTail.Success)
            return;

        using var extractVariant = CreateMaterialContractVariant(extract, bytecodeFormat);
        using var downsampleVariant = CreateMaterialContractVariant(downsample, bytecodeFormat);
        using var adaptFrontVariant = CreateMaterialContractVariant(adaptFront, bytecodeFormat);
        using var adaptTailVariant = CreateMaterialContractVariant(adaptTail, bytecodeFormat);
        using var applyFrontVariant = CreateMaterialContractVariant(applyFront, bytecodeFormat);
        using var applyTailVariant = CreateMaterialContractVariant(applyTail, bytecodeFormat);
        using var defaultMainTexture = new Resources.Texture2D();
        using var overrideMainTexture = new Resources.Texture2D();
        using var defaultAdaptedTexture = new Resources.Texture2D();
        using var overrideAdaptedTexture = new Resources.Texture2D();
        using var shader = CreateAutoExposureContractShader(defaultMainTexture, defaultAdaptedTexture);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetFloat("_AdaptSpeedUp", 4f);
        overridesMaterial.SetFloat("_AdaptSpeedDown", 2f);
        overridesMaterial.SetFloat("_HistoryValid", 1f);
        overridesMaterial.SetFloat("_ExposureComp", -1f);
        overridesMaterial.SetFloat("_MinExposure", 0.25f);
        overridesMaterial.SetFloat("_MaxExposure", 8f);
        overridesMaterial.SetTexture("_MainTex", overrideMainTexture);
        overridesMaterial.SetTexture("_AdaptedTex", overrideAdaptedTexture);
        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 10, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("auto-exposure-material-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, defaultMainTexture, new byte[] { 32, 64, 96, 255 });
            EncodeContractTexture(create, overrideMainTexture, new byte[] { 192, 160, 128, 255 });
            EncodeContractTexture(create, defaultAdaptedTexture, new byte[] { 64, 128, 160, 255 });
            EncodeContractTexture(create, overrideAdaptedTexture, new byte[] { 224, 192, 32, 255 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 10, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("auto-exposure-material-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, extractVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, downsampleVariant, overridesMaterial, 1);
            DrawMaterialPixel(draw, vertexArray, adaptFrontVariant, defaultsMaterial, 2);
            DrawMaterialPixel(draw, vertexArray, adaptFrontVariant, overridesMaterial, 3);
            DrawMaterialPixel(draw, vertexArray, adaptTailVariant, defaultsMaterial, 4);
            DrawMaterialPixel(draw, vertexArray, adaptTailVariant, overridesMaterial, 5);
            DrawMaterialPixel(draw, vertexArray, applyFrontVariant, defaultsMaterial, 6);
            DrawMaterialPixel(draw, vertexArray, applyFrontVariant, overridesMaterial, 7);
            DrawMaterialPixel(draw, vertexArray, applyTailVariant, defaultsMaterial, 8);
            DrawMaterialPixel(draw, vertexArray, applyTailVariant, overridesMaterial, 9);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 32f / 255f, 64f / 255f, 96f / 255f, 1f);
        AssertMaterialPixel(pixels, 1, 192f / 255f, 160f / 255f, 128f / 255f, 1f);
        AssertMaterialPixel(pixels, 2, 0.5f, 0.25f, 0f, 1f);
        AssertMaterialPixel(pixels, 3, 1f, 0.5f, 1f, 1f);
        AssertMaterialPixel(pixels, 4, 32f / 255f, 128f / 255f, 0.5f, 0f);
        AssertMaterialPixel(pixels, 5, 192f / 255f, 192f / 255f, 1f, 1f);
        AssertMaterialPixel(pixels, 6, 0.625f, 0.03125f, 0.5f, 1f);
        AssertMaterialPixel(pixels, 7, 0.25f, 0.0625f, 1f, 1f);
        AssertMaterialPixel(pixels, 8, 32f / 255f, 128f / 255f, 96f / 255f, 160f / 255f);
        AssertMaterialPixel(pixels, 9, 192f / 255f, 192f / 255f, 128f / 255f, 32f / 255f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("auto-exposure-material-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunTAAMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding0 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string binding1 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(1)]] " : string.Empty;
        string binding2 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(2)]] " : string.Empty;
        string binding3 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(3)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string block = "cbuffer TAAResolvePS : register(b2) { float2 _Resolution; float2 _Jitter; float _HistoryValid; float _BlendFactor; float _MotionScale; float _Sharpness; }; ";
        string resources = binding0 + "Texture2D _MainTex : register(t0); " + binding0 + "SamplerState _MainTexSampler : register(s0); "
            + binding1 + "Texture2D _HistoryTex : register(t1); " + binding1 + "SamplerState _HistoryTexSampler : register(s1); "
            + binding2 + "Texture2D _MotionVectorsTex : register(t2); " + binding2 + "SamplerState _MotionVectorsSampler : register(s2); "
            + binding3 + "Texture2D _CameraDepthTexture : register(t3); " + binding3 + "SamplerState _CameraDepthSampler : register(s3); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult front = CompileMaterialContractShader(compiler, backend, vertexSource, block + "float4 main() : SV_Target { return float4(_Resolution.x / 8.0, _Resolution.y / 8.0, _HistoryValid, _BlendFactor); }");
        ShaderCompileResult tail = CompileMaterialContractShader(compiler, backend, vertexSource, block + resources + "float4 main() : SV_Target { float4 current = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); float4 history = _HistoryTex.Sample(_HistoryTexSampler, float2(0.5, 0.5)); float4 motion = _MotionVectorsTex.Sample(_MotionVectorsSampler, float2(0.5, 0.5)); float4 depth = _CameraDepthTexture.Sample(_CameraDepthSampler, float2(0.5, 0.5)); return float4(current.r, history.g, motion.b, depth.a); }");
        if (!front.Success || !tail.Success)
            return;

        using var frontVariant = CreateMaterialContractVariant(front, bytecodeFormat);
        using var tailVariant = CreateMaterialContractVariant(tail, bytecodeFormat);
        using var mainDefault = new Resources.Texture2D();
        using var mainOverride = new Resources.Texture2D();
        using var historyDefault = new Resources.Texture2D();
        using var historyOverride = new Resources.Texture2D();
        using var motionDefault = new Resources.Texture2D();
        using var motionOverride = new Resources.Texture2D();
        using var depthDefault = new Resources.Texture2D();
        using var depthOverride = new Resources.Texture2D();
        using var shader = CreateTAAContractShader(mainDefault, historyDefault, motionDefault);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetVector("_Resolution", new Float2(2f, 4f));
        overridesMaterial.SetVector("_Jitter", new Float2(-0.25f, 0.5f));
        overridesMaterial.SetFloat("_HistoryValid", 1f);
        overridesMaterial.SetFloat("_BlendFactor", 0.75f);
        overridesMaterial.SetFloat("_MotionScale", 4f);
        overridesMaterial.SetFloat("_Sharpness", 0.5f);
        overridesMaterial.SetTexture("_MainTex", mainOverride);
        overridesMaterial.SetTexture("_HistoryTex", historyOverride);
        overridesMaterial.SetTexture("_MotionVectorsTex", motionOverride);
        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 4, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("taa-material-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, mainDefault, new byte[] { 32, 64, 96, 255 });
            EncodeContractTexture(create, mainOverride, new byte[] { 192, 160, 128, 255 });
            EncodeContractTexture(create, historyDefault, new byte[] { 64, 128, 160, 255 });
            EncodeContractTexture(create, historyOverride, new byte[] { 224, 192, 32, 255 });
            EncodeContractTexture(create, motionDefault, new byte[] { 16, 48, 80, 255 });
            EncodeContractTexture(create, motionOverride, new byte[] { 96, 128, 160, 255 });
            EncodeContractTexture(create, depthDefault, new byte[] { 32, 64, 96, 128 });
            EncodeContractTexture(create, depthOverride, new byte[] { 160, 192, 224, 64 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 4, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("taa-material-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, frontVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, frontVariant, overridesMaterial, 1);
            draw.SetGlobalTexture("_CameraDepthTexture", depthDefault);
            DrawMaterialPixel(draw, vertexArray, tailVariant, defaultsMaterial, 2);
            draw.SetGlobalTexture("_CameraDepthTexture", depthOverride);
            DrawMaterialPixel(draw, vertexArray, tailVariant, overridesMaterial, 3);
            draw.ClearGlobalTexture("_CameraDepthTexture");
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.5f, 1f, 0f, 0.9f);
        AssertMaterialPixel(pixels, 1, 0.25f, 0.5f, 1f, 0.75f);
        AssertMaterialPixel(pixels, 2, 32f / 255f, 128f / 255f, 80f / 255f, 128f / 255f);
        AssertMaterialPixel(pixels, 3, 192f / 255f, 192f / 255f, 160f / 255f, 64f / 255f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("taa-material-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunGTAOCalculateContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding0 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string binding1 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(1)]] " : string.Empty;
        string binding2 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(2)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string block = "cbuffer GTAOCalculatePS : register(b2) { int _Slices; int _DirectionSamples; float _Radius; float _Intensity; float2 _NoiseScale; float2 _JitterOffset; }; ";
        string resources = binding0 + "Texture2D _CameraDepthTexture : register(t0); " + binding0 + "SamplerState _CameraDepthSampler : register(s0); "
            + binding1 + "Texture2D _CameraNormalsTexture : register(t1); " + binding1 + "SamplerState _CameraNormalsSampler : register(s1); "
            + binding2 + "Texture2D _Noise : register(t2); " + binding2 + "SamplerState _NoiseSampler : register(s2); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult front = CompileMaterialContractShader(compiler, backend, vertexSource, block + "float4 main() : SV_Target { return float4(float(_Slices) / 16.0, float(_DirectionSamples) / 32.0, _Radius, _Intensity / 2.0); }");
        ShaderCompileResult tail = CompileMaterialContractShader(compiler, backend, vertexSource, block + resources + "float4 main() : SV_Target { float4 depth = _CameraDepthTexture.Sample(_CameraDepthSampler, float2(0.5, 0.5)); float4 normal = _CameraNormalsTexture.Sample(_CameraNormalsSampler, float2(0.5, 0.5)); float4 noise = _Noise.Sample(_NoiseSampler, float2(0.5, 0.5)); return float4(depth.r, normal.g, noise.b, _NoiseScale.x + _JitterOffset.x); }");
        if (!front.Success || !tail.Success)
            return;

        using var frontVariant = CreateMaterialContractVariant(front, bytecodeFormat);
        using var tailVariant = CreateMaterialContractVariant(tail, bytecodeFormat);
        using var depthDefault = new Resources.Texture2D();
        using var depthOverride = new Resources.Texture2D();
        using var normalsDefault = new Resources.Texture2D();
        using var normalsOverride = new Resources.Texture2D();
        using var noiseDefault = new Resources.Texture2D();
        using var noiseOverride = new Resources.Texture2D();
        using var shader = CreateGTAOContractShader(noiseDefault);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetInt("_Slices", 12);
        overridesMaterial.SetInt("_DirectionSamples", 16);
        overridesMaterial.SetFloat("_Radius", 1f);
        overridesMaterial.SetFloat("_Intensity", 2f);
        overridesMaterial.SetVector("_NoiseScale", new Float2(0.5f, 0.25f));
        overridesMaterial.SetVector("_JitterOffset", new Float2(0.25f, 0.125f));
        overridesMaterial.SetTexture("_Noise", noiseOverride);
        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 4, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("gtao-calculate-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, depthDefault, new byte[] { 32, 64, 96, 255 });
            EncodeContractTexture(create, depthOverride, new byte[] { 192, 160, 128, 255 });
            EncodeContractTexture(create, normalsDefault, new byte[] { 16, 48, 80, 255 });
            EncodeContractTexture(create, normalsOverride, new byte[] { 96, 128, 160, 255 });
            EncodeContractTexture(create, noiseDefault, new byte[] { 32, 64, 96, 255 });
            EncodeContractTexture(create, noiseOverride, new byte[] { 224, 192, 160, 255 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 4, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("gtao-calculate-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, frontVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, frontVariant, overridesMaterial, 1);
            draw.SetGlobalTexture("_CameraDepthTexture", depthDefault);
            draw.SetGlobalTexture("_CameraNormalsTexture", normalsDefault);
            DrawMaterialPixel(draw, vertexArray, tailVariant, defaultsMaterial, 2);
            draw.SetGlobalTexture("_CameraDepthTexture", depthOverride);
            draw.SetGlobalTexture("_CameraNormalsTexture", normalsOverride);
            DrawMaterialPixel(draw, vertexArray, tailVariant, overridesMaterial, 3);
            draw.ClearGlobalTexture("_CameraDepthTexture");
            draw.ClearGlobalTexture("_CameraNormalsTexture");
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 6f / 16f, 1f, 0.5f, 0.5f);
        AssertMaterialPixel(pixels, 1, 12f / 16f, 0.5f, 1f, 1f);
        AssertMaterialPixel(pixels, 2, 32f / 255f, 48f / 255f, 96f / 255f, 0.375f);
        AssertMaterialPixel(pixels, 3, 192f / 255f, 128f / 255f, 160f / 255f, 0.75f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("gtao-calculate-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunGTAOBlurContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding0 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string binding1 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(1)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string block = "cbuffer GTAOBlurPS : register(b2) { float2 _BlurDirection; float _BlurRadius; float _Padding; }; ";
        string resources = binding0 + "Texture2D _MainTex : register(t0); " + binding0 + "SamplerState _MainTexSampler : register(s0); "
            + binding1 + "Texture2D _CameraDepthTexture : register(t1); " + binding1 + "SamplerState _CameraDepthSampler : register(s1); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult front = CompileMaterialContractShader(compiler, backend, vertexSource, block + "float4 main() : SV_Target { return float4(_BlurDirection.x, _BlurDirection.y, _BlurRadius / 4.0, 1.0); }");
        ShaderCompileResult tail = CompileMaterialContractShader(compiler, backend, vertexSource, block + resources + "float4 main() : SV_Target { float4 color = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); float4 depth = _CameraDepthTexture.Sample(_CameraDepthSampler, float2(0.5, 0.5)); return float4(color.r, depth.g, _BlurDirection.x, _BlurRadius / 4.0); }");
        if (!front.Success || !tail.Success)
            return;

        using var frontVariant = CreateMaterialContractVariant(front, bytecodeFormat);
        using var tailVariant = CreateMaterialContractVariant(tail, bytecodeFormat);
        using var mainDefault = new Resources.Texture2D();
        using var mainOverride = new Resources.Texture2D();
        using var depthDefault = new Resources.Texture2D();
        using var depthOverride = new Resources.Texture2D();
        using var shader = CreateGTAOBlurContractShader(mainDefault);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetVector("_BlurDirection", new Float2(0f, 1f));
        overridesMaterial.SetFloat("_BlurRadius", 2f);
        overridesMaterial.SetTexture("_MainTex", mainOverride);
        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 4, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("gtao-blur-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, mainDefault, new byte[] { 32, 64, 96, 255 });
            EncodeContractTexture(create, mainOverride, new byte[] { 192, 160, 128, 255 });
            EncodeContractTexture(create, depthDefault, new byte[] { 16, 48, 80, 255 });
            EncodeContractTexture(create, depthOverride, new byte[] { 224, 192, 160, 255 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 4, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("gtao-blur-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, frontVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, frontVariant, overridesMaterial, 1);
            draw.SetGlobalTexture("_CameraDepthTexture", depthDefault);
            DrawMaterialPixel(draw, vertexArray, tailVariant, defaultsMaterial, 2);
            draw.SetGlobalTexture("_CameraDepthTexture", depthOverride);
            DrawMaterialPixel(draw, vertexArray, tailVariant, overridesMaterial, 3);
            draw.ClearGlobalTexture("_CameraDepthTexture");
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 1f, 0f, 0.25f, 1f);
        AssertMaterialPixel(pixels, 1, 0f, 1f, 0.5f, 1f);
        AssertMaterialPixel(pixels, 2, 32f / 255f, 48f / 255f, 1f, 0.25f);
        AssertMaterialPixel(pixels, 3, 192f / 255f, 192f / 255f, 0f, 0.5f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("gtao-blur-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunGTAOCompositeContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding0 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string binding1 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(1)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        string resources = binding0 + "Texture2D _MainTex : register(t0); " + binding0 + "SamplerState _MainTexSampler : register(s0); "
            + binding1 + "Texture2D _AOTex : register(t1); " + binding1 + "SamplerState _AOTexSampler : register(s1); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult composite = CompileMaterialContractShader(compiler, backend, vertexSource, resources + "float4 main() : SV_Target { float4 sceneColor = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); float ao = _AOTex.Sample(_AOTexSampler, float2(0.5, 0.5)).r; return float4(sceneColor.rgb * ao, sceneColor.a); }");
        if (!composite.Success)
            return;

        using var variant = CreateMaterialContractVariant(composite, bytecodeFormat);
        using var mainDefault = new Resources.Texture2D();
        using var mainOverride = new Resources.Texture2D();
        using var aoDefault = new Resources.Texture2D();
        using var aoOverride = new Resources.Texture2D();
        using var shader = CreateGTAOCompositeContractShader(mainDefault, aoDefault);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetTexture("_MainTex", mainOverride);
        overridesMaterial.SetTexture("_AOTex", aoOverride);
        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 2, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("gtao-composite-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, mainDefault, new byte[] { 255, 128, 64, 255 });
            EncodeContractTexture(create, mainOverride, new byte[] { 128, 255, 64, 128 });
            EncodeContractTexture(create, aoDefault, new byte[] { 255, 128, 255, 255 });
            EncodeContractTexture(create, aoOverride, new byte[] { 128, 64, 255, 255 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("gtao-composite-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, variant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, variant, overridesMaterial, 1);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 1f, 128f / 255f, 64f / 255f, 1f);
        AssertMaterialPixel(pixels, 1, 64f / 255f, 128f / 255f, 32f / 255f, 128f / 255f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("gtao-composite-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunGTAOTemporalContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding0 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string binding1 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(1)]] " : string.Empty;
        string binding2 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(2)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string block = "cbuffer GTAOTemporalPS : register(b2) { float _TResponse; float3 _Padding; }; ";
        string resources = binding0 + "Texture2D _MainTex : register(t0); " + binding0 + "SamplerState _MainTexSampler : register(s0); "
            + binding1 + "Texture2D _PreviousBuffer : register(t1); " + binding1 + "SamplerState _PreviousBufferSampler : register(s1); "
            + binding2 + "Texture2D _CameraMotionVectorsTexture : register(t2); " + binding2 + "SamplerState _CameraMotionVectorsTextureSampler : register(s2); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult constants = CompileMaterialContractShader(compiler, backend, vertexSource, block + "float4 main() : SV_Target { return float4(_TResponse, 0.0, 0.0, 1.0); }");
        ShaderCompileResult textures = CompileMaterialContractShader(compiler, backend, vertexSource, block + resources + "float4 main() : SV_Target { float2 uv = float2(0.5, 0.5); return float4(_MainTex.Sample(_MainTexSampler, uv).r, _PreviousBuffer.Sample(_PreviousBufferSampler, uv).g, _CameraMotionVectorsTexture.Sample(_CameraMotionVectorsTextureSampler, uv).b, _TResponse); }");
        if (!constants.Success || !textures.Success)
            return;

        using var constantsVariant = CreateMaterialContractVariant(constants, bytecodeFormat);
        using var texturesVariant = CreateMaterialContractVariant(textures, bytecodeFormat);
        using var mainDefault = new Resources.Texture2D();
        using var mainOverride = new Resources.Texture2D();
        using var previousDefault = new Resources.Texture2D();
        using var previousOverride = new Resources.Texture2D();
        using var motionDefault = new Resources.Texture2D();
        using var motionOverride = new Resources.Texture2D();
        using var shader = CreateGTAOTemporalContractShader(mainDefault, previousDefault, motionDefault);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetFloat("_TResponse", 0.25f);
        overridesMaterial.SetTexture("_MainTex", mainOverride);
        overridesMaterial.SetTexture("_PreviousBuffer", previousOverride);
        overridesMaterial.SetTexture("_CameraMotionVectorsTexture", motionOverride);
        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 4, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("gtao-temporal-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, mainDefault, [32, 64, 96, 255]);
            EncodeContractTexture(create, mainOverride, [192, 160, 128, 255]);
            EncodeContractTexture(create, previousDefault, [64, 128, 160, 255]);
            EncodeContractTexture(create, previousOverride, [224, 192, 32, 255]);
            EncodeContractTexture(create, motionDefault, [16, 48, 80, 255]);
            EncodeContractTexture(create, motionOverride, [96, 128, 160, 255]);
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 4, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("gtao-temporal-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, constantsVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, constantsVariant, overridesMaterial, 1);
            DrawMaterialPixel(draw, vertexArray, texturesVariant, defaultsMaterial, 2);
            DrawMaterialPixel(draw, vertexArray, texturesVariant, overridesMaterial, 3);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.75f, 0f, 0f, 1f);
        AssertMaterialPixel(pixels, 1, 0.25f, 0f, 0f, 1f);
        AssertMaterialPixel(pixels, 2, 32f / 255f, 128f / 255f, 80f / 255f, 0.75f);
        AssertMaterialPixel(pixels, 3, 192f / 255f, 192f / 255f, 160f / 255f, 0.25f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("gtao-temporal-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunUIBlurMaterialContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        string resources = binding + "Texture2D _MainTex : register(t0); " + binding + "SamplerState _MainTexSampler : register(s0); ";
        string shaderBody = resources + "float4 main() : SV_Target { float4 sampled = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); return float4(_Offset / 4.0, sampled.g, sampled.b, sampled.a); }";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult down = CompileMaterialContractShader(compiler, backend, vertexSource, "cbuffer BlurDownPS : register(b0) { float _Offset; }; " + shaderBody);
        ShaderCompileResult up = CompileMaterialContractShader(compiler, backend, vertexSource, "cbuffer BlurUpPS : register(b0) { float _Offset; }; " + shaderBody);
        if (!down.Success || !up.Success)
            return;

        using var downVariant = CreateMaterialContractVariant(down, bytecodeFormat);
        using var upVariant = CreateMaterialContractVariant(up, bytecodeFormat);
        using var defaultTexture = new Resources.Texture2D();
        using var overrideTexture = new Resources.Texture2D();
        using var shader = CreateUIBlurContractShader(defaultTexture);
        using var defaultsMaterial = new Resources.Material(shader);
        using var overridesMaterial = new Resources.Material(shader);
        overridesMaterial.SetFloat("_Offset", 3f);
        overridesMaterial.SetTexture("_MainTex", overrideTexture);

        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 4, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("ui-blur-material-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, defaultTexture, new byte[] { 0, 128, 192, 255 });
            EncodeContractTexture(create, overrideTexture, new byte[] { 0, 64, 128, 128 });
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 4, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("ui-blur-material-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, downVariant, defaultsMaterial, 0);
            DrawMaterialPixel(draw, vertexArray, downVariant, overridesMaterial, 1);
            DrawMaterialPixel(draw, vertexArray, upVariant, defaultsMaterial, 2);
            DrawMaterialPixel(draw, vertexArray, upVariant, overridesMaterial, 3);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.25f, 128f / 255f, 192f / 255f, 1f);
        AssertMaterialPixel(pixels, 1, 0.75f, 64f / 255f, 128f / 255f, 128f / 255f);
        AssertMaterialPixel(pixels, 2, 0.25f, 128f / 255f, 192f / 255f, 1f);
        AssertMaterialPixel(pixels, 3, 0.75f, 64f / 255f, 128f / 255f, 128f / 255f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("ui-blur-material-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunUIBackdropBlurChainContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string binding0 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string binding2 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(2)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        string mainTexture = binding0 + "Texture2D _MainTex : register(t0); " + binding0 + "SamplerState _MainTexSampler : register(s0); ";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult down = CompileMaterialContractShader(compiler, backend, vertexSource,
            "cbuffer BlurDownPS : register(b0) { float _Offset; }; " + mainTexture + "float4 main() : SV_Target { float4 c = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); return float4(c.r, _Offset / 4.0, c.b, c.a); }");
        ShaderCompileResult up = CompileMaterialContractShader(compiler, backend, vertexSource,
            "cbuffer BlurUpPS : register(b0) { float _Offset; }; " + mainTexture + "float4 main() : SV_Target { float4 c = _MainTex.Sample(_MainTexSampler, float2(0.5, 0.5)); return float4(c.r, c.g, _Offset / 4.0, c.a); }");
        ShaderCompileResult composite = CompileMaterialContractShader(compiler, backend, vertexSource,
            binding2 + "Texture2D backdropTexture : register(t2); " + binding2 + "SamplerState backdropSampler : register(s2); float4 main() : SV_Target { return backdropTexture.Sample(backdropSampler, float2(0.5, 0.5)); }");
        if (!down.Success || !up.Success || !composite.Success)
            return;

        using var downVariant = CreateMaterialContractVariant(down, bytecodeFormat);
        using var upVariant = CreateMaterialContractVariant(up, bytecodeFormat);
        using var compositeVariant = CreateMaterialContractVariant(composite, bytecodeFormat);
        using var capture = new Resources.Texture2D();
        using var downTexture = new Resources.Texture2D();
        using var upTexture = new Resources.Texture2D();
        using var output = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        using var shader = CreateUIBlurContractShader(capture);
        using var blurMaterial = new Resources.Material(shader);
        blurMaterial.SetFloat("_Offset", 1f);
        GraphicsFrameBuffer captureFramebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = capture.Handle }], 1, 1);
        GraphicsFrameBuffer downFramebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = downTexture.Handle }], 1, 1);
        GraphicsFrameBuffer upFramebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = upTexture.Handle }], 1, 1);
        GraphicsFrameBuffer outputFramebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = output }], 1, 1);
        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("ui-backdrop-chain-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            EncodeContractTexture(create, capture, new byte[] { 0, 0, 0, 0 });
            EncodeContractTexture(create, downTexture, new byte[] { 0, 0, 0, 0 });
            EncodeContractTexture(create, upTexture, new byte[] { 0, 0, 0, 0 });
            create.EncodeCreateTexture(output);
            create.EncodeAllocateTexture2D(output, 0, 1, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(captureFramebuffer);
            create.EncodeCreateFramebuffer(downFramebuffer);
            create.EncodeCreateFramebuffer(upFramebuffer);
            create.EncodeCreateFramebuffer(outputFramebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("ui-backdrop-chain-draw"))
        {
            draw.SetRenderTarget(null);
            draw.ClearRenderTarget(ClearFlags.Color, new Color(64f / 255f, 128f / 255f, 192f / 255f, 1f));
            draw.SetRenderTargets(captureFramebuffer, null);
            draw.BlitFramebuffer(0, 0, 1, 1, 0, 0, 1, 1, ClearFlags.Color, BlitFilter.Linear);
            draw.SetRenderTarget(downFramebuffer);
            draw.SetRasterState(in raster);
            DrawMaterialPixel(draw, vertexArray, downVariant, blurMaterial, 0);
            blurMaterial.SetTexture("_MainTex", downTexture);
            draw.SetRenderTarget(upFramebuffer);
            DrawMaterialPixel(draw, vertexArray, upVariant, blurMaterial, 0);
            draw.SetRenderTarget(outputFramebuffer);
            draw.SetShader(compositeVariant);
            draw.SetTexture("backdropTexture", upTexture);
            draw.SetViewport(0, 0, 1, 1);
            draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
            device.Execute(draw, true);
        }

        AssertMaterialPixel(readback(output), 0, 64f / 255f, 0.25f, 0.25f, 1f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("ui-backdrop-chain-dispose");
        dispose.EncodeDisposeFramebuffer(captureFramebuffer);
        dispose.EncodeDisposeFramebuffer(downFramebuffer);
        dispose.EncodeDisposeFramebuffer(upFramebuffer);
        dispose.EncodeDisposeFramebuffer(outputFramebuffer);
        device.Execute(dispose, true);
    }

    private static void RunUIVertexProjectionContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string vertexSource = "cbuffer UIVS : register(b0) { float4x4 projection; }; struct VSInput { " + location + "float3 position : POSITION; }; struct VSOutput { float4 position : SV_Position; float4 data : TEXCOORD0; }; VSOutput main(VSInput input) { VSOutput output; output.position = float4(input.position, 1); output.data = float4(projection._m00, projection._m11, projection._m22, projection._m33); return output; }";
        const string fragmentSource = "struct PSInput { float4 position : SV_Position; float4 data : TEXCOORD0; }; float4 main(PSInput input) : SV_Target { return input.data; }";
        var compiler = new DxcShaderCompiler();
        ShaderCompileResult compiled = CompileMaterialContractShader(compiler, backend, vertexSource, fragmentSource);
        if (!compiled.Success)
            return;

        using var variant = CreateMaterialContractVariant(compiled, bytecodeFormat);
        float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
        ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
        using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
        using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 2, 1);
        var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
        using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
        using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("ui-projection-create"))
        {
            create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
            create.EncodeCreateTexture(color);
            create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
            create.EncodeCreateFramebuffer(framebuffer);
            create.EncodeCreateVertexArray(vertexArray);
            device.Execute(create, true);
        }

        Float4x4 projectionA = new(
            0.125f, 0f, 0f, 0f,
            0f, 0.25f, 0f, 0f,
            0f, 0f, 0.375f, 0f,
            0f, 0f, 0f, 0.5f);
        Float4x4 projectionB = new(
            0.625f, 0f, 0f, 0f,
            0f, 0.75f, 0f, 0f,
            0f, 0f, 0.875f, 0f,
            0f, 0f, 0f, 1f);
        RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
        using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("ui-projection-draw"))
        {
            draw.SetRenderTarget(framebuffer);
            draw.DisableScissor();
            draw.SetShader(variant);
            draw.SetRasterState(in raster);
            draw.SetMatrix("projection", in projectionA);
            draw.SetViewport(0, 0, 1, 1);
            draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
            draw.SetMatrix("projection", in projectionB);
            draw.SetViewport(1, 0, 1, 1);
            draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
            device.Execute(draw, true);
        }

        byte[] pixels = readback(color);
        AssertMaterialPixel(pixels, 0, 0.125f, 0.25f, 0.375f, 0.5f);
        AssertMaterialPixel(pixels, 1, 0.625f, 0.75f, 0.875f, 1f);
        using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("ui-projection-dispose");
        dispose.EncodeDisposeFramebuffer(framebuffer);
        device.Execute(dispose, true);
    }

    private static void RunUIFragmentStateContract(
        IGraphicsDevice device,
        GraphicsBackend backend,
        ShaderBytecodeFormat bytecodeFormat,
        Func<GraphicsTexture, byte[]> readback)
    {
        string location = backend == GraphicsBackend.Vulkan ? "[[vk::location(0)]] " : string.Empty;
        string vertexSource = "struct VSInput { " + location + "float3 position : POSITION; }; float4 main(VSInput input) : SV_Position { return float4(input.position, 1); }";
        const string block = "cbuffer UIPS : register(b1) { float4x4 scissorMat; float2 scissorExt; float4x4 brushMat; int brushType; float4 brushColor1; float4 brushColor2; float4 brushParams; float2 brushParams2; float4x4 brushTextureMat; float dpiScale; float2 viewportSize; float backdropBlurAmount; int backdropFlipY; }; ";
        string binding0 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(0)]] " : string.Empty;
        string binding1 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(1)]] " : string.Empty;
        string binding2 = backend == GraphicsBackend.Vulkan ? "[[vk::binding(2)]] " : string.Empty;
        string textures = binding0 + "Texture2D texture0 : register(t0); " + binding0 + "SamplerState texture0Sampler : register(s0); " + binding1 + "Texture2D fontTexture : register(t1); " + binding1 + "SamplerState fontSampler : register(s1); " + binding2 + "Texture2D backdropTexture : register(t2); " + binding2 + "SamplerState backdropSampler : register(s2); ";
        string[] fragments =
        [
            block + "float4 main() : SV_Target { return float4(scissorMat._m00, scissorExt.x, scissorExt.y, brushMat._m11); }",
            block + "float4 main() : SV_Target { return float4(brushType / 4.0, brushColor1.r, brushColor2.g, brushParams.z); }",
            block + "float4 main() : SV_Target { return float4(brushParams2.x, brushTextureMat._m22, dpiScale / 4.0, viewportSize.x / 8.0); }",
            block + "float4 main() : SV_Target { return float4(viewportSize.y / 8.0, backdropBlurAmount, backdropFlipY, brushParams.w); }",
            block + textures + "float4 main() : SV_Target { float2 uv = float2(0.5, 0.5); return float4(texture0.Sample(texture0Sampler, uv).r, fontTexture.Sample(fontSampler, uv).g, backdropTexture.Sample(backdropSampler, uv).b, backdropTexture.Sample(backdropSampler, uv).a); }",
        ];
        var compiler = new DxcShaderCompiler();
        ShaderVariant[] variants = new ShaderVariant[fragments.Length];
        for (int i = 0; i < fragments.Length; i++)
        {
            ShaderCompileResult compiled = CompileMaterialContractShader(compiler, backend, vertexSource, fragments[i]);
            if (!compiled.Success)
            {
                for (int j = 0; j < i; j++)
                    variants[j].Dispose();
                return;
            }
            variants[i] = CreateMaterialContractVariant(compiled, bytecodeFormat);
        }

        try
        {
            using var textureA = new Resources.Texture2D();
            using var fontA = new Resources.Texture2D();
            using var backdropA = new Resources.Texture2D();
            using var textureB = new Resources.Texture2D();
            using var fontB = new Resources.Texture2D();
            using var backdropB = new Resources.Texture2D();
            float[] vertices = [-1f, -1f, 0f, 0f, 1f, 0f, 1f, -1f, 0f];
            ReadOnlySpan<byte> vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan());
            using var vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexBytes, dynamic: true);
            using var color = new GraphicsTexture(TextureType.Texture2D, TextureImageFormat.Color4b);
            GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 10, 1);
            var format = new VertexFormat([new(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, 3)]);
            using var vertexArray = new GraphicsVertexArray(format, vertexBuffer, null);
            using (CommandBuffer create = global::Prowl.Runtime.Graphics.GetCommandBuffer("ui-fragment-create"))
            {
                create.EncodeCreateBuffer(vertexBuffer, true, vertexBytes);
                EncodeContractTexture(create, textureA, new byte[] { 32, 0, 0, 255 });
                EncodeContractTexture(create, fontA, new byte[] { 0, 64, 0, 255 });
                EncodeContractTexture(create, backdropA, new byte[] { 0, 0, 96, 128 });
                EncodeContractTexture(create, textureB, new byte[] { 224, 0, 0, 255 });
                EncodeContractTexture(create, fontB, new byte[] { 0, 192, 0, 255 });
                EncodeContractTexture(create, backdropB, new byte[] { 0, 0, 160, 255 });
                create.EncodeCreateTexture(color);
                create.EncodeAllocateTexture2D(color, 0, 10, 1, 0, ReadOnlySpan<byte>.Empty);
                create.EncodeCreateFramebuffer(framebuffer);
                create.EncodeCreateVertexArray(vertexArray);
                device.Execute(create, true);
            }

            RasterizerState raster = new() { DepthTest = false, DepthWrite = false, CullFace = RasterizerState.PolyFace.None };
            using (CommandBuffer draw = global::Prowl.Runtime.Graphics.GetCommandBuffer("ui-fragment-draw"))
            {
                draw.SetRenderTarget(framebuffer);
                draw.DisableScissor();
                draw.SetRasterState(in raster);
                SetUIFragmentStateA(draw, textureA, fontA, backdropA);
                for (int i = 0; i < variants.Length; i++)
                {
                    draw.SetShader(variants[i]);
                    draw.SetViewport(i, 0, 1, 1);
                    draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                }
                SetUIFragmentStateB(draw, textureB, fontB, backdropB);
                for (int i = 0; i < variants.Length; i++)
                {
                    draw.SetShader(variants[i]);
                    draw.SetViewport(i + variants.Length, 0, 1, 1);
                    draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
                }
                device.Execute(draw, true);
            }

            byte[] pixels = readback(color);
            AssertMaterialPixel(pixels, 0, 0.125f, 0.25f, 0.375f, 0.5f);
            AssertMaterialPixel(pixels, 1, 0.25f, 0.625f, 0.75f, 0.875f);
            AssertMaterialPixel(pixels, 2, 0.125f, 0.25f, 0.25f, 0.25f);
            AssertMaterialPixel(pixels, 3, 0.5f, 0.625f, 1f, 0.75f);
            AssertMaterialPixel(pixels, 4, 32f / 255f, 64f / 255f, 96f / 255f, 128f / 255f);
            AssertMaterialPixel(pixels, 5, 0.875f, 0.75f, 0.625f, 0.125f);
            AssertMaterialPixel(pixels, 6, 0.75f, 0.25f, 0.375f, 0.5f);
            AssertMaterialPixel(pixels, 7, 0.75f, 0.625f, 0.75f, 0.75f);
            AssertMaterialPixel(pixels, 8, 0.25f, 0.125f, 0f, 0.875f);
            AssertMaterialPixel(pixels, 9, 224f / 255f, 192f / 255f, 160f / 255f, 1f);
            using CommandBuffer dispose = global::Prowl.Runtime.Graphics.GetCommandBuffer("ui-fragment-dispose");
            dispose.EncodeDisposeFramebuffer(framebuffer);
            device.Execute(dispose, true);
        }
        finally
        {
            for (int i = 0; i < variants.Length; i++)
                variants[i].Dispose();
        }
    }

    private static void SetUIFragmentStateA(CommandBuffer draw, Resources.Texture2D texture, Resources.Texture2D font, Resources.Texture2D backdrop)
    {
        Float4x4 scissor = CreateDiagonalMatrix(0.125f, 1f, 1f, 1f);
        Float4x4 brush = CreateDiagonalMatrix(1f, 0.5f, 1f, 1f);
        Float4x4 brushTexture = CreateDiagonalMatrix(1f, 1f, 0.25f, 1f);
        draw.SetMatrix("scissorMat", in scissor);
        draw.SetVector("scissorExt", new Float2(0.25f, 0.375f));
        draw.SetMatrix("brushMat", in brush);
        draw.SetInt("brushType", 1);
        draw.SetVector("brushColor1", new Float4(0.625f, 0f, 0f, 1f));
        draw.SetVector("brushColor2", new Float4(0f, 0.75f, 0f, 1f));
        draw.SetVector("brushParams", new Float4(0f, 0f, 0.875f, 0.75f));
        draw.SetVector("brushParams2", new Float2(0.125f, 0f));
        draw.SetMatrix("brushTextureMat", in brushTexture);
        draw.SetFloat("dpiScale", 1f);
        draw.SetVector("viewportSize", new Float2(2f, 4f));
        draw.SetFloat("backdropBlurAmount", 0.625f);
        draw.SetInt("backdropFlipY", 1);
        draw.SetTexture("texture0", texture);
        draw.SetTexture("fontTexture", font);
        draw.SetTexture("backdropTexture", backdrop);
    }

    private static void SetUIFragmentStateB(CommandBuffer draw, Resources.Texture2D texture, Resources.Texture2D font, Resources.Texture2D backdrop)
    {
        Float4x4 scissor = CreateDiagonalMatrix(0.875f, 1f, 1f, 1f);
        Float4x4 brush = CreateDiagonalMatrix(1f, 0.125f, 1f, 1f);
        Float4x4 brushTexture = CreateDiagonalMatrix(1f, 1f, 0.625f, 1f);
        draw.SetMatrix("scissorMat", in scissor);
        draw.SetVector("scissorExt", new Float2(0.75f, 0.625f));
        draw.SetMatrix("brushMat", in brush);
        draw.SetInt("brushType", 3);
        draw.SetVector("brushColor1", new Float4(0.25f, 0f, 0f, 1f));
        draw.SetVector("brushColor2", new Float4(0f, 0.375f, 0f, 1f));
        draw.SetVector("brushParams", new Float4(0f, 0f, 0.5f, 0.875f));
        draw.SetVector("brushParams2", new Float2(0.75f, 0f));
        draw.SetMatrix("brushTextureMat", in brushTexture);
        draw.SetFloat("dpiScale", 3f);
        draw.SetVector("viewportSize", new Float2(6f, 2f));
        draw.SetFloat("backdropBlurAmount", 0.125f);
        draw.SetInt("backdropFlipY", 0);
        draw.SetTexture("texture0", texture);
        draw.SetTexture("fontTexture", font);
        draw.SetTexture("backdropTexture", backdrop);
    }

    private static Float4x4 CreateDiagonalMatrix(float x, float y, float z, float w) => new(
        x, 0f, 0f, 0f,
        0f, y, 0f, 0f,
        0f, 0f, z, 0f,
        0f, 0f, 0f, w);

    private static ShaderCompileResult CompileMaterialContractShader(
        DxcShaderCompiler compiler,
        GraphicsBackend backend,
        string vertexSource,
        string fragmentSource) => compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = backend,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = fragmentSource,
        });

    private static ShaderVariant CreateMaterialContractVariant(ShaderCompileResult compiled, ShaderBytecodeFormat format) =>
        new(new CompiledShaderBytecode(
            ShaderLanguage.Hlsl,
            format,
            compiled.VertexBytecode!,
            compiled.FragmentBytecode!,
            compiled.BindingLayout));

    private static Resources.Shader CreateStandardMaterialContractShader()
    {
        Rendering.Shaders.ShaderProperty tiling = new(new Float2(0.125f, 1f)) { Name = "_Tiling", DisplayName = "Tiling" };
        Rendering.Shaders.ShaderProperty offset = new(new Float2(0f, 0.25f)) { Name = "_Offset", DisplayName = "Offset" };
        Rendering.Shaders.ShaderProperty color = new(new Color(0.375f, 0f, 0f, 1f)) { Name = "_MainColor", DisplayName = "Tint" };
        Rendering.Shaders.ShaderProperty emission = new(0.125f) { Name = "_EmissionIntensity", DisplayName = "Emission" };
        Rendering.Shaders.ShaderProperty alphaCutoff = new(0.25f) { Name = "_AlphaCutoff", DisplayName = "Alpha Cutoff" };
        Rendering.Shaders.ShaderProperty parallax = new(0.375f) { Name = "_Parallax", DisplayName = "Parallax" };
        Rendering.Shaders.ShaderProperty parallaxSteps = new(8) { Name = "_ParallaxSteps", DisplayName = "Parallax Steps" };
        Rendering.Shaders.ShaderProperty translucency = new(0.2f) { Name = "_TranslucencyStrength", DisplayName = "Translucency" };
        Rendering.Shaders.ShaderProperty scatteringPower = new(0.4f) { Name = "_ScatteringPower", DisplayName = "Scattering Power" };
        Rendering.Shaders.ShaderProperty scatteringDistortion = new(0.6f) { Name = "_ScatteringDistortion", DisplayName = "Scattering Distortion" };
        Rendering.Shaders.ShaderProperty scatteringScale = new(0.8f) { Name = "_ScatteringScale", DisplayName = "Scattering Scale" };
        return new Resources.Shader(
            "Standard Material Contract",
            [tiling, offset, color, emission, alphaCutoff, parallax, parallaxSteps, translucency, scatteringPower, scatteringDistortion, scatteringScale],
            []);
    }

    private static Resources.Shader CreateStandardTextureContractShader(
        Resources.Texture2D mainTexture,
        Resources.Texture2D normalTexture,
        Resources.Texture2D surfaceTexture,
        Resources.Texture2D emissionTexture)
    {
        Rendering.Shaders.ShaderProperty main = new(mainTexture) { Name = "_MainTex", DisplayName = "Main" };
        Rendering.Shaders.ShaderProperty normal = new(normalTexture) { Name = "_NormalTex", DisplayName = "Normal" };
        Rendering.Shaders.ShaderProperty surface = new(surfaceTexture) { Name = "_SurfaceTex", DisplayName = "Surface" };
        Rendering.Shaders.ShaderProperty emission = new(emissionTexture) { Name = "_EmissionTex", DisplayName = "Emission" };
        return new Resources.Shader("Standard Texture Contract", [main, normal, surface, emission], []);
    }

    private static Resources.Shader CreateGradientSkyboxContractShader()
    {
        Rendering.Shaders.ShaderProperty top = new(new Color(0.125f, 0.25f, 0.375f, 0.5f)) { Name = "_TopColor", DisplayName = "Top" };
        Rendering.Shaders.ShaderProperty bottom = new(new Color(0.625f, 0.75f, 0.875f, 1f)) { Name = "_BottomColor", DisplayName = "Bottom" };
        Rendering.Shaders.ShaderProperty exponent = new(2f) { Name = "_Exponent", DisplayName = "Exponent" };
        return new Resources.Shader("Gradient Skybox Contract", [top, bottom, exponent], []);
    }

    private static Resources.Shader CreateGizmoIconContractShader(Resources.Texture2D mainTexture)
    {
        Rendering.Shaders.ShaderProperty texture = new(mainTexture) { Name = "_MainTex", DisplayName = "Icon" };
        Rendering.Shaders.ShaderProperty color = new(new Float4(1f, 1f, 1f, 1f)) { Name = "_IconColor", DisplayName = "Color" };
        Rendering.Shaders.ShaderProperty center = new(new Float3(0f, 0f, 0f)) { Name = "_IconCenter", DisplayName = "Center" };
        Rendering.Shaders.ShaderProperty scale = new(1f) { Name = "_IconScale", DisplayName = "Scale" };
        return new Resources.Shader("Gizmo Icon Contract", [texture, color, center, scale], []);
    }

    private static Resources.Shader CreateDefaultUIContractShader(Resources.Texture2D mainTexture)
    {
        Rendering.Shaders.ShaderProperty texture = new(mainTexture) { Name = "_MainTex", DisplayName = "Texture" };
        Rendering.Shaders.ShaderProperty color = new(new Color(1f, 1f, 1f, 1f)) { Name = "_MainColor", DisplayName = "Tint" };
        Rendering.Shaders.ShaderProperty tiling = new(new Float2(1f, 1f)) { Name = "_Tiling", DisplayName = "Tiling" };
        Rendering.Shaders.ShaderProperty offset = new(new Float2(0f, 0f)) { Name = "_Offset", DisplayName = "Offset" };
        return new Resources.Shader("Default UI Contract", [texture, color, tiling, offset], []);
    }

    private static Resources.Shader CreateDefaultTextMeshContractShader(Resources.Texture2D mainTexture)
    {
        Rendering.Shaders.ShaderProperty texture = new(mainTexture) { Name = "_MainTex", DisplayName = "SDF Atlas" };
        Rendering.Shaders.ShaderProperty color = new(new Color(1f, 1f, 1f, 1f)) { Name = "_MainColor", DisplayName = "Tint" };
        Rendering.Shaders.ShaderProperty tiling = new(new Float2(1f, 1f)) { Name = "_Tiling", DisplayName = "Tiling" };
        Rendering.Shaders.ShaderProperty offset = new(new Float2(0f, 0f)) { Name = "_Offset", DisplayName = "Offset" };
        return new Resources.Shader("Default TextMesh Contract", [texture, color, tiling, offset], []);
    }

    private static Resources.Shader CreateCubemapSkyboxContractShader(Resources.Texture2D[] faces)
    {
        Rendering.Shaders.ShaderProperty tint = new(new Float4(1f, 1f, 1f, 1f)) { Name = "_Tint", DisplayName = "Tint" };
        Rendering.Shaders.ShaderProperty exposure = new(1f) { Name = "_Exposure", DisplayName = "Exposure" };
        string[] names = ["_CubeRight", "_CubeLeft", "_CubeTop", "_CubeBottom", "_CubeFront", "_CubeBack"];
        var properties = new Rendering.Shaders.ShaderProperty[8];
        properties[0] = tint;
        properties[1] = exposure;
        for (int i = 0; i < names.Length; i++)
            properties[i + 2] = new Rendering.Shaders.ShaderProperty(faces[i]) { Name = names[i], DisplayName = names[i] };
        return new Resources.Shader("Cubemap Skybox Contract", properties, []);
    }

    private static Resources.Shader CreateProceduralSkyboxContractShader()
    {
        Rendering.Shaders.ShaderProperty resolution = new(new Float2(2f, 1f)) { Name = "Resolution", DisplayName = "Resolution" };
        Rendering.Shaders.ShaderProperty fogDensity = new(0.5f) { Name = "fogDensity", DisplayName = "Fog Density" };
        Rendering.Shaders.ShaderProperty sunDirection = new(new Float3(0.125f, 0f, 0.75f)) { Name = "_SunDir", DisplayName = "Sun Direction" };
        return new Resources.Shader("Procedural Skybox Contract", [resolution, fogDensity, sunDirection], []);
    }

    private static Resources.Shader CreateTonemapperContractShader(Resources.Texture2D texture)
    {
        Rendering.Shaders.ShaderProperty contrast = new(0.5f) { Name = "Contrast", DisplayName = "Contrast" };
        Rendering.Shaders.ShaderProperty saturation = new(1f) { Name = "Saturation", DisplayName = "Saturation" };
        Rendering.Shaders.ShaderProperty mainTexture = new(texture) { Name = "_MainTex", DisplayName = "Main Texture" };
        return new Resources.Shader("Tonemapper Contract", [contrast, saturation, mainTexture], []);
    }

    private static Resources.Shader CreateUIBlurContractShader(Resources.Texture2D texture)
    {
        Rendering.Shaders.ShaderProperty offset = new(1f) { Name = "_Offset", DisplayName = "Offset" };
        Rendering.Shaders.ShaderProperty mainTexture = new(texture) { Name = "_MainTex", DisplayName = "Main Texture" };
        return new Resources.Shader("UI Blur Contract", [offset, mainTexture], []);
    }

    private static Resources.Shader CreateGridContractShader()
    {
        Rendering.Shaders.ShaderProperty color = new(new Color(0.125f, 0f, 0f, 0.25f)) { Name = "_GridColor", DisplayName = "Grid Color" };
        Rendering.Shaders.ShaderProperty primary = new(1f) { Name = "_PrimaryGridSize", DisplayName = "Primary" };
        Rendering.Shaders.ShaderProperty secondary = new(0.5f) { Name = "_SecondaryGridSize", DisplayName = "Secondary" };
        Rendering.Shaders.ShaderProperty lineWidth = new(0.125f) { Name = "_LineWidth", DisplayName = "Line Width" };
        Rendering.Shaders.ShaderProperty falloff = new(1f) { Name = "_Falloff", DisplayName = "Falloff" };
        Rendering.Shaders.ShaderProperty maxDistance = new(6f) { Name = "_MaxDist", DisplayName = "Max Distance" };
        return new Resources.Shader("Grid Contract", [color, primary, secondary, lineWidth, falloff, maxDistance], []);
    }

    private static Resources.Shader CreateFXAAContractShader(Resources.Texture2D texture)
    {
        Rendering.Shaders.ShaderProperty resolution = new(new Float2(4f, 8f)) { Name = "_Resolution", DisplayName = "Resolution" };
        Rendering.Shaders.ShaderProperty thresholdMin = new(0.03125f) { Name = "_EdgeThresholdMin", DisplayName = "Threshold Min" };
        Rendering.Shaders.ShaderProperty thresholdMax = new(0.0625f) { Name = "_EdgeThresholdMax", DisplayName = "Threshold Max" };
        Rendering.Shaders.ShaderProperty subpixel = new(0.75f) { Name = "_SubpixelQuality", DisplayName = "Subpixel" };
        Rendering.Shaders.ShaderProperty mainTexture = new(texture) { Name = "_MainTex", DisplayName = "Main Texture" };
        return new Resources.Shader("FXAA Contract", [resolution, thresholdMin, thresholdMax, subpixel, mainTexture], []);
    }

    private static Resources.Shader CreateBloomContractShader(Resources.Texture2D mainTextureValue, Resources.Texture2D bloomTextureValue)
    {
        Rendering.Shaders.ShaderProperty threshold = new(0.25f) { Name = "_Threshold", DisplayName = "Threshold" };
        Rendering.Shaders.ShaderProperty intensity = new(0.5f) { Name = "_Intensity", DisplayName = "Intensity" };
        Rendering.Shaders.ShaderProperty mainTexture = new(mainTextureValue) { Name = "_MainTex", DisplayName = "Main Texture" };
        Rendering.Shaders.ShaderProperty bloomTexture = new(bloomTextureValue) { Name = "_BloomTex", DisplayName = "Bloom Texture" };
        return new Resources.Shader("Bloom Contract", [threshold, intensity, mainTexture, bloomTexture], []);
    }

    private static Resources.Shader CreateMotionBlurContractShader(Resources.Texture2D mainTextureValue, Resources.Texture2D motionTextureValue)
    {
        Rendering.Shaders.ShaderProperty resolution = new(new Float2(4f, 8f)) { Name = "_Resolution", DisplayName = "Resolution" };
        Rendering.Shaders.ShaderProperty intensity = new(0.5f) { Name = "_Intensity", DisplayName = "Intensity" };
        Rendering.Shaders.ShaderProperty samples = new(8) { Name = "_Samples", DisplayName = "Samples" };
        Rendering.Shaders.ShaderProperty maxBlurRadius = new(32f) { Name = "_MaxBlurRadius", DisplayName = "Max Blur Radius" };
        Rendering.Shaders.ShaderProperty mainTexture = new(mainTextureValue) { Name = "_MainTex", DisplayName = "Main Texture" };
        Rendering.Shaders.ShaderProperty motionTexture = new(motionTextureValue) { Name = "_MotionVectorsTex", DisplayName = "Motion Vectors" };
        return new Resources.Shader("Motion Blur Contract", [resolution, intensity, samples, maxBlurRadius, mainTexture, motionTexture], []);
    }

    private static Resources.Shader CreateAutoExposureContractShader(Resources.Texture2D mainTextureValue, Resources.Texture2D adaptedTextureValue)
    {
        Rendering.Shaders.ShaderProperty adaptSpeedUp = new(2f) { Name = "_AdaptSpeedUp", DisplayName = "Adapt Up" };
        Rendering.Shaders.ShaderProperty adaptSpeedDown = new(1f) { Name = "_AdaptSpeedDown", DisplayName = "Adapt Down" };
        Rendering.Shaders.ShaderProperty historyValid = new(0f) { Name = "_HistoryValid", DisplayName = "History Valid" };
        Rendering.Shaders.ShaderProperty exposureComp = new(0.5f) { Name = "_ExposureComp", DisplayName = "Exposure Compensation" };
        Rendering.Shaders.ShaderProperty minExposure = new(0.125f) { Name = "_MinExposure", DisplayName = "Minimum Exposure" };
        Rendering.Shaders.ShaderProperty maxExposure = new(4f) { Name = "_MaxExposure", DisplayName = "Maximum Exposure" };
        Rendering.Shaders.ShaderProperty mainTexture = new(mainTextureValue) { Name = "_MainTex", DisplayName = "Main Texture" };
        Rendering.Shaders.ShaderProperty adaptedTexture = new(adaptedTextureValue) { Name = "_AdaptedTex", DisplayName = "Adapted Luminance" };
        return new Resources.Shader("Auto Exposure Contract", [adaptSpeedUp, adaptSpeedDown, historyValid, exposureComp, minExposure, maxExposure, mainTexture, adaptedTexture], []);
    }

    private static Resources.Shader CreateTAAContractShader(Resources.Texture2D mainTextureValue, Resources.Texture2D historyTextureValue, Resources.Texture2D motionTextureValue)
    {
        Rendering.Shaders.ShaderProperty resolution = new(new Float2(4f, 8f)) { Name = "_Resolution", DisplayName = "Resolution" };
        Rendering.Shaders.ShaderProperty jitter = new(new Float2(0.25f, -0.5f)) { Name = "_Jitter", DisplayName = "Jitter" };
        Rendering.Shaders.ShaderProperty historyValid = new(0f) { Name = "_HistoryValid", DisplayName = "History Valid" };
        Rendering.Shaders.ShaderProperty blendFactor = new(0.9f) { Name = "_BlendFactor", DisplayName = "Blend Factor" };
        Rendering.Shaders.ShaderProperty motionScale = new(2f) { Name = "_MotionScale", DisplayName = "Motion Scale" };
        Rendering.Shaders.ShaderProperty sharpness = new(0.125f) { Name = "_Sharpness", DisplayName = "Sharpness" };
        Rendering.Shaders.ShaderProperty mainTexture = new(mainTextureValue) { Name = "_MainTex", DisplayName = "Main Texture" };
        Rendering.Shaders.ShaderProperty historyTexture = new(historyTextureValue) { Name = "_HistoryTex", DisplayName = "History Texture" };
        Rendering.Shaders.ShaderProperty motionTexture = new(motionTextureValue) { Name = "_MotionVectorsTex", DisplayName = "Motion Vectors" };
        return new Resources.Shader("TAA Contract", [resolution, jitter, historyValid, blendFactor, motionScale, sharpness, mainTexture, historyTexture, motionTexture], []);
    }

    private static Resources.Shader CreateGTAOContractShader(Resources.Texture2D noiseTextureValue)
    {
        Rendering.Shaders.ShaderProperty slices = new(6) { Name = "_Slices", DisplayName = "Slices" };
        Rendering.Shaders.ShaderProperty directionSamples = new(32) { Name = "_DirectionSamples", DisplayName = "Direction Samples" };
        Rendering.Shaders.ShaderProperty radius = new(0.5f) { Name = "_Radius", DisplayName = "Radius" };
        Rendering.Shaders.ShaderProperty intensity = new(1f) { Name = "_Intensity", DisplayName = "Intensity" };
        Rendering.Shaders.ShaderProperty noiseScale = new(new Float2(0.25f, 0.5f)) { Name = "_NoiseScale", DisplayName = "Noise Scale" };
        Rendering.Shaders.ShaderProperty jitterOffset = new(new Float2(0.125f, 0.25f)) { Name = "_JitterOffset", DisplayName = "Jitter Offset" };
        Rendering.Shaders.ShaderProperty noise = new(noiseTextureValue) { Name = "_Noise", DisplayName = "Noise" };
        return new Resources.Shader("GTAO Calculate Contract", [slices, directionSamples, radius, intensity, noiseScale, jitterOffset, noise], []);
    }

    private static Resources.Shader CreateGTAOBlurContractShader(Resources.Texture2D mainTextureValue)
    {
        Rendering.Shaders.ShaderProperty direction = new(new Float2(1f, 0f)) { Name = "_BlurDirection", DisplayName = "Blur Direction" };
        Rendering.Shaders.ShaderProperty radius = new(1f) { Name = "_BlurRadius", DisplayName = "Blur Radius" };
        Rendering.Shaders.ShaderProperty mainTexture = new(mainTextureValue) { Name = "_MainTex", DisplayName = "Main Texture" };
        return new Resources.Shader("GTAO Blur Contract", [direction, radius, mainTexture], []);
    }

    private static Resources.Shader CreateGTAOCompositeContractShader(Resources.Texture2D mainTextureValue, Resources.Texture2D aoTextureValue)
    {
        Rendering.Shaders.ShaderProperty mainTexture = new(mainTextureValue) { Name = "_MainTex", DisplayName = "Main Texture" };
        Rendering.Shaders.ShaderProperty aoTexture = new(aoTextureValue) { Name = "_AOTex", DisplayName = "AO Texture" };
        return new Resources.Shader("GTAO Composite Contract", [mainTexture, aoTexture], []);
    }

    private static Resources.Shader CreateGTAOTemporalContractShader(Resources.Texture2D mainTextureValue, Resources.Texture2D previousTextureValue, Resources.Texture2D motionTextureValue)
    {
        Rendering.Shaders.ShaderProperty response = new(0.75f) { Name = "_TResponse", DisplayName = "Temporal Response" };
        Rendering.Shaders.ShaderProperty mainTexture = new(mainTextureValue) { Name = "_MainTex", DisplayName = "Current AO" };
        Rendering.Shaders.ShaderProperty previousTexture = new(previousTextureValue) { Name = "_PreviousBuffer", DisplayName = "Previous AO" };
        Rendering.Shaders.ShaderProperty motionTexture = new(motionTextureValue) { Name = "_CameraMotionVectorsTexture", DisplayName = "Motion Vectors" };
        return new Resources.Shader("GTAO Temporal Contract", [response, mainTexture, previousTexture, motionTexture], []);
    }

    private static void EncodeContractTexture(CommandBuffer commandBuffer, Resources.Texture2D texture, byte[] pixels)
    {
        commandBuffer.EncodeCreateTexture(texture.Handle);
        commandBuffer.EncodeAllocateTexture2D(texture.Handle, 0, 1, 1, 0, pixels);
    }

    private static void DrawMaterialPixel(
        CommandBuffer draw,
        GraphicsVertexArray vertexArray,
        ShaderVariant variant,
        Resources.Material material,
        int pixel)
    {
        draw.SetShader(variant);
        draw.SetMaterialProperties(material);
        draw.SetViewport(pixel, 0, 1, 1);
        draw.DrawArrays(vertexArray, Topology.Triangles, 0, 3);
    }

    private static void AssertMaterialPixel(byte[] pixels, int pixel, float red, float green, float blue, float alpha)
    {
        int offset = pixel * 4;
        AssertChannel(pixels[offset], red);
        AssertChannel(pixels[offset + 1], green);
        AssertChannel(pixels[offset + 2], blue);
        AssertChannel(pixels[offset + 3], alpha);
    }

    private static void AssertChannel(byte actual, float expected)
    {
        int expectedByte = (int)MathF.Round(expected * 255f);
        Assert.InRange((int)actual, Math.Max(0, expectedByte - 1), Math.Min(255, expectedByte + 1));
    }

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
