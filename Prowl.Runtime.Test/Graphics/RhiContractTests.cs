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
        GraphicsFrameBuffer framebuffer = GraphicsFrameBuffer.CreateDeferred([new GraphicsFrameBuffer.Attachment { Texture = color }], 2, 1);
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
            create.EncodeAllocateTexture2D(color, 0, 2, 1, 0, ReadOnlySpan<byte>.Empty);
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
