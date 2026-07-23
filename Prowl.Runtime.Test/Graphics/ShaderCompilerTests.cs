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
}
