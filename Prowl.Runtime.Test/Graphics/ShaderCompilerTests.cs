// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Reflection;

using Prowl.Runtime.AssetImporting;
using Prowl.Runtime.RHI;
using Prowl.Runtime.RHI.Shaders;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Stage C shader-pipeline: dual-language parsing, GLSL compile path, and DXC discovery.
/// </summary>
public class ShaderCompilerTests
{
    private const string DualLanguageShader = """
Shader "Test/Dual"

Pass "Main"
{
    GLSLPROGRAM
    Vertex
    {
        void main() { gl_Position = vec4(0.0); }
    }
    Fragment
    {
        layout(location = 0) out vec4 c;
        void main() { c = vec4(1.0); }
    }
    ENDGLSL

    HLSLPROGRAM
    Vertex
    {
        float4 main() : SV_Position { return float4(0, 0, 0, 1); }
    }
    Fragment
    {
        float4 main() : SV_Target { return float4(1, 1, 1, 1); }
    }
    ENDHLSL
}
""";

    [Fact]
    public void Parser_Accepts_HlslProgram_Alongside_Glsl()
    {
        bool ok = ShaderParser.ParseShader("test.shader", DualLanguageShader, out Shader? shader);
        Assert.True(ok);
        Assert.NotNull(shader);

        ShaderPass pass = shader!.GetPass(0);
        Assert.True(pass.HasGlsl);
        Assert.True(pass.HasHlsl);

        string hlslVert = (string)typeof(ShaderPass)
            .GetField("_hlslVertexSource", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(pass)!;
        Assert.Contains("SV_Position", hlslVert);
    }

    [Fact]
    public void Parser_StillParses_Blit_And_Standard_Glsl()
    {
        Shader blit = Shader.LoadDefault(DefaultShader.Blit);
        Assert.NotNull(blit);
        Assert.True(blit.GetPass(0).HasGlsl);
        Assert.True(blit.GetPass(0).HasHlsl);

        Shader standard = Shader.LoadDefault(DefaultShader.Standard);
        Assert.NotNull(standard);
        Assert.True(standard.GetPass(0).HasGlsl);
        Assert.True(standard.GetPass(0).HasHlsl);
        Assert.Contains("Standard", standard.GetPass(0).Name);
    }

    [Fact]
    public void GlslShaderCompiler_Injects_Version_And_Defines()
    {
        var compiler = new GlslShaderCompiler();
        ShaderCompileResult result = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.OpenGL,
            Language = ShaderLanguage.Glsl,
            VertexSource = "void main() {}",
            FragmentSource = "void main() {}",
            Keywords = new System.Collections.Generic.Dictionary<string, bool>
            {
                ["FOO"] = true,
                ["BAR"] = false,
            },
        });

        Assert.True(result.Success);
        Assert.Equal(ShaderBytecodeFormat.GlslSource, result.Format);
        Assert.Contains("#version 410", result.GlslVertexSource);
        Assert.Contains("#define FOO", result.GlslVertexSource);
        Assert.DoesNotContain("#define BAR", result.GlslVertexSource!);
    }

    [Fact]
    public void DxcShaderCompiler_Reports_NotFound_Or_Compiles_Tiny_Hlsl()
    {
        DxcShaderCompiler.ResetLocateCache();
        var compiler = new DxcShaderCompiler();

        string tinyVs = """
float4 main() : SV_Position
{
    return float4(0, 0, 0, 1);
}
""";
        string tinyPs = """
float4 main() : SV_Target
{
    return float4(1, 0, 1, 1);
}
""";

        ShaderCompileResult result = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = tinyVs,
            FragmentSource = tinyPs,
        });

        if (compiler.ResolvedDxcPath == null)
        {
            Assert.False(result.Success);
            Assert.Contains("dxc", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
            return;
        }

        // Prefer DXIL (always available on Windows Kits DXC). Also try Vulkan/SPIR-V when the
        // located DXC was built with ENABLE_SPIRV_CODEGEN.
        if (result.Success)
        {
            Assert.Equal(ShaderBytecodeFormat.Dxil, result.Format);
            Assert.NotNull(result.VertexBytecode);
            Assert.NotNull(result.FragmentBytecode);
            Assert.True(result.VertexBytecode!.Length > 0);
            Assert.True(result.FragmentBytecode!.Length > 0);
            return;
        }

        // DXC present but compile failed — must be a clear error, never an unhandled throw.
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));

        ShaderCompileResult spirv = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = tinyVs,
            FragmentSource = tinyPs,
        });

        if (spirv.Success)
        {
            Assert.Equal(ShaderBytecodeFormat.SpirV, spirv.Format);
            Assert.True(spirv.VertexBytecode!.Length > 0);
        }
        else
        {
            Assert.Contains("dxc", spirv.ErrorMessage! + result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ShaderCompilerFactory_Picks_By_Backend()
    {
        Assert.IsType<GlslShaderCompiler>(ShaderCompilerFactory.GetCompiler(GraphicsBackend.OpenGL));
        Assert.IsType<GlslShaderCompiler>(ShaderCompilerFactory.GetCompiler(GraphicsBackend.Null));
        Assert.IsType<DxcShaderCompiler>(ShaderCompilerFactory.GetCompiler(GraphicsBackend.Vulkan));
        Assert.IsType<DxcShaderCompiler>(ShaderCompilerFactory.GetCompiler(GraphicsBackend.Direct3D12));
        Assert.True(ShaderCompilerFactory.RequiresHlsl(GraphicsBackend.Vulkan));
        Assert.False(ShaderCompilerFactory.RequiresHlsl(GraphicsBackend.OpenGL));
    }

    [Fact]
    public void DxcBindingReflection_Merges_Stages_And_Uses_Explicit_Registers()
    {
        const string vertexSource = """
cbuffer GlobalUniforms : register(b0) { float4x4 ViewProjection; };
cbuffer ObjectUniforms : register(b2) { float4x4 Model; };
Texture2D SharedTexture : register(t3);
SamplerState SharedSampler : register(s3);
""";
        const string fragmentSource = """
cbuffer GlobalUniforms : register(b0) { float4 CameraPosition; };
Texture2D SharedTexture : register(t3);
TextureCube Environment : register(t1);
SamplerState SharedSampler : register(s3);
SamplerComparisonState ShadowSampler : register(s1);
""";

        ShaderBindingLayout layout = DxcShaderCompiler.ParseBindingLayout(vertexSource, fragmentSource);

        Assert.Collection(
            layout.Textures,
            slot => AssertBinding(slot, ShaderBindingKind.Texture, 1, "Environment"),
            slot => AssertBinding(slot, ShaderBindingKind.Texture, 3, "SharedTexture"));
        Assert.Collection(
            layout.Buffers,
            slot => AssertBinding(slot, ShaderBindingKind.Buffer, 0, "GlobalUniforms"),
            slot => AssertBinding(slot, ShaderBindingKind.Buffer, 2, "ObjectUniforms"));
        Assert.Collection(
            layout.Samplers,
            slot => AssertBinding(slot, ShaderBindingKind.Sampler, 1, "ShadowSampler"),
            slot => AssertBinding(slot, ShaderBindingKind.Sampler, 3, "SharedSampler"));
    }

    [Fact]
    public void DxcBindingReflection_Rejects_Conflicting_CrossStage_Slots()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DxcShaderCompiler.ParseBindingLayout(
                "Texture2D Shared : register(t0);",
                "Texture2D Shared : register(t1);"));

        Assert.Contains("conflicting", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DxcBindingReflection_Rejects_Different_Names_At_One_Slot()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DxcShaderCompiler.ParseBindingLayout(
                "cbuffer VertexMaterial : register(b2) { float4 Tint; };",
                "cbuffer FragmentMaterial : register(b2) { float4 Tint; };"));

        Assert.Contains("both", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VertexMaterial", exception.Message, StringComparison.Ordinal);
        Assert.Contains("FragmentMaterial", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Critical_Shaders_Have_Dual_Sources()
    {
        DefaultShader[] critical =
        [
            DefaultShader.Blit,
            DefaultShader.Unlit,
            DefaultShader.Invalid,
            DefaultShader.Standard,
            DefaultShader.Grid,
            DefaultShader.UI,
            DefaultShader.ProceduralSkybox,
            DefaultShader.GradientSkybox,
            DefaultShader.Tonemapper,
        ];

        foreach (DefaultShader id in critical)
        {
            Shader shader = Shader.LoadDefault(id);
            Assert.NotNull(shader);
            foreach (ShaderPass pass in shader.Passes)
            {
                Assert.True(pass.HasGlsl, $"{id}/{pass.Name} missing GLSL");
                Assert.True(pass.HasHlsl, $"{id}/{pass.Name} missing HLSL");
            }
        }
    }

    [Fact]
    public void Critical_Modern_Shader_Binding_Layouts_Are_Collision_Free()
    {
        DefaultShader[] critical =
        [
            DefaultShader.Blit,
            DefaultShader.Unlit,
            DefaultShader.Invalid,
            DefaultShader.Standard,
            DefaultShader.Grid,
            DefaultShader.UI,
            DefaultShader.ProceduralSkybox,
            DefaultShader.GradientSkybox,
            DefaultShader.Tonemapper,
        ];

        foreach (DefaultShader id in critical)
        {
            Shader shader = Shader.LoadDefault(id);
            foreach (ShaderPass pass in shader.Passes)
            {
                string vertexSource = (string)typeof(ShaderPass)
                    .GetField("_hlslVertexSource", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(pass)!;
                string fragmentSource = (string)typeof(ShaderPass)
                    .GetField("_hlslFragmentSource", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(pass)!;

                Exception? error = Record.Exception(() => DxcShaderCompiler.ParseBindingLayout(vertexSource, fragmentSource));
                Assert.True(error == null, $"{id}/{pass.Name}: {error?.Message}");
            }
        }
    }

    [Fact]
    public void Standard_Prepass_Hlsl_Uses_Shared_Material_Buffer_Layout()
    {
        Shader shader = Shader.LoadDefault(DefaultShader.Standard);
        ShaderPass pass = shader.GetPass("Prepass");
        string vertexSource = (string)typeof(ShaderPass)
            .GetField("_hlslVertexSource", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(pass)!;
        string fragmentSource = (string)typeof(ShaderPass)
            .GetField("_hlslFragmentSource", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(pass)!;

        ShaderBindingLayout layout = DxcShaderCompiler.ParseBindingLayout(vertexSource, fragmentSource);

        Assert.Collection(
            layout.Buffers,
            slot => AssertBinding(slot, ShaderBindingKind.Buffer, 0, "GlobalUniforms"),
            slot => AssertBinding(slot, ShaderBindingKind.Buffer, 1, "ObjectUniforms"),
            slot => AssertBinding(slot, ShaderBindingKind.Buffer, 2, "PrepassMaterial"));
        Assert.DoesNotContain(layout.Buffers, slot => slot.Name == "PrepassVS" || slot.Name == "PrepassPS");
        string[] materialFields = ["float2 _Tiling", "float2 _Offset", "float4 _MainColor", "float _AlphaCutoff", "float3 _PrepassMaterialPadding"];
        foreach (string field in materialFields)
        {
            Assert.Contains(field, vertexSource);
            Assert.Contains(field, fragmentSource);
        }
    }

    [Fact]
    public void Standard_Prepass_Hlsl_Compiles_For_Modern_Backends_When_Dxc_Is_Available()
    {
        var compiler = new DxcShaderCompiler();
        if (compiler.ResolvedDxcPath == null)
            return;

        Shader shader = Shader.LoadDefault(DefaultShader.Standard);
        ShaderPass pass = shader.GetPass("Prepass");
        string vertexSource = (string)typeof(ShaderPass)
            .GetField("_hlslVertexSource", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(pass)!;
        string fragmentSource = (string)typeof(ShaderPass)
            .GetField("_hlslFragmentSource", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(pass)!;

        ShaderCompileResult d3d12 = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Direct3D12,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = fragmentSource,
        });
        Assert.True(d3d12.Success, d3d12.ErrorMessage);
        Assert.Equal(ShaderBytecodeFormat.Dxil, d3d12.Format);

        ShaderCompileResult vulkan = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.Vulkan,
            Language = ShaderLanguage.Hlsl,
            VertexSource = vertexSource,
            FragmentSource = fragmentSource,
        });
        if (!vulkan.Success && vulkan.ErrorMessage?.Contains("SPIR-V CodeGen not available", StringComparison.OrdinalIgnoreCase) == true)
            return;

        Assert.True(vulkan.Success, vulkan.ErrorMessage);
        Assert.Equal(ShaderBytecodeFormat.SpirV, vulkan.Format);
    }

    [Fact]
    public void Modern_Default_Passes_Compile_For_Modern_Backends_When_Dxc_Is_Available()
    {
        var compiler = new DxcShaderCompiler();
        if (compiler.ResolvedDxcPath == null)
            return;

        (DefaultShader shader, string pass)[] targets =
        [
            (DefaultShader.Standard, "Standard"),
            (DefaultShader.Standard, "Prepass"),
            (DefaultShader.Standard, "StandardShadow"),
            (DefaultShader.StandardTransparent, "StandardTransparent"),
            (DefaultShader.CubemapSkybox, "CubemapSkybox"),
            (DefaultShader.Unlit, "Unlit"),
            (DefaultShader.Unlit, "UnlitPrepass"),
            (DefaultShader.Bloom, "Threshold"),
            (DefaultShader.Bloom, "Downsample"),
            (DefaultShader.Bloom, "Upsample"),
            (DefaultShader.Bloom, "Composite"),
            (DefaultShader.MotionBlur, "MotionBlur"),
            (DefaultShader.AutoExposure, "LuminanceExtract"),
            (DefaultShader.AutoExposure, "Downsample"),
            (DefaultShader.AutoExposure, "Adapt"),
            (DefaultShader.AutoExposure, "ApplyExposure"),
            (DefaultShader.TAA, "Resolve"),
            (DefaultShader.GTAO, "CalculateGTAO"),
            (DefaultShader.GTAO, "Blur"),
            (DefaultShader.GTAO, "Composite"),
            (DefaultShader.GTAO, "Temporal"),
        ];

        bool spirvUnavailable = false;
        foreach ((DefaultShader shaderId, string passName) in targets)
        {
            ShaderPass pass = Shader.LoadDefault(shaderId).GetPass(passName);
            string vertexSource = (string)typeof(ShaderPass)
                .GetField("_hlslVertexSource", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(pass)!;
            string fragmentSource = (string)typeof(ShaderPass)
                .GetField("_hlslFragmentSource", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(pass)!;

            ShaderCompileResult d3d12 = compiler.Compile(new ShaderCompileRequest
            {
                TargetBackend = GraphicsBackend.Direct3D12,
                Language = ShaderLanguage.Hlsl,
                VertexSource = vertexSource,
                FragmentSource = fragmentSource,
            });
            Assert.True(d3d12.Success, $"{shaderId}/{passName}: {d3d12.ErrorMessage}");
            Assert.Equal(ShaderBytecodeFormat.Dxil, d3d12.Format);

            if (spirvUnavailable)
                continue;

            ShaderCompileResult vulkan = compiler.Compile(new ShaderCompileRequest
            {
                TargetBackend = GraphicsBackend.Vulkan,
                Language = ShaderLanguage.Hlsl,
                VertexSource = vertexSource,
                FragmentSource = fragmentSource,
            });
            if (!vulkan.Success && vulkan.ErrorMessage?.Contains("SPIR-V CodeGen not available", StringComparison.OrdinalIgnoreCase) == true)
            {
                spirvUnavailable = true;
                continue;
            }

            Assert.True(vulkan.Success, $"{shaderId}/{passName}: {vulkan.ErrorMessage}");
            Assert.Equal(ShaderBytecodeFormat.SpirV, vulkan.Format);
        }
    }

    private static void AssertBinding(ShaderBindingSlot slot, ShaderBindingKind kind, int index, string name)
    {
        Assert.Equal(kind, slot.Kind);
        Assert.Equal(index, slot.Slot);
        Assert.Equal(name, slot.Name);
    }
}
