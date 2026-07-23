// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.RHI;
using Prowl.Runtime.RHI.Shaders;

namespace Prowl.Runtime.Rendering.Shaders;

public sealed class ShaderPass
{
    [SerializeField] private string _name;

    [SerializeField] private Dictionary<string, string> _tags;
    [SerializeField] private Dictionary<string, int> _tagSortOffsets;
    [SerializeField] private RasterizerState _rasterizerState;

    [SerializeField] private string _vertexSource;
    [SerializeField] private string _fragmentSource;
    [SerializeField] private string _hlslVertexSource;
    [SerializeField] private string _hlslFragmentSource;
    [SerializeField] private string _fallbackAsset;

    [SerializeField] private string _grabTextureName; // If not empty, captures screen before rendering
    [SerializeField] private string _grabDepthTextureName; // If not empty, also captures the depth buffer

    [SerializeIgnore]
    private Dictionary<string, ShaderVariant> _variants = [];


    /// <summary>
    /// The name to identify this <see cref="ShaderPass"/>
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// The tags to identify this <see cref="ShaderPass"/>
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>> Tags => _tags;

    /// <summary>
    /// The sort offsets for tags (e.g., "Transparent+1000" has offset 1000)
    /// </summary>
    public IReadOnlyDictionary<string, int> TagSortOffsets => _tagSortOffsets;

    /// <summary>
    /// The blending options to use when rendering this <see cref="ShaderPass"/>
    /// </summary>
    public RasterizerState State => _rasterizerState;

    /// <summary>
    /// The name of the texture uniform to bind the grabbed colour texture to. Empty if this pass doesn't grab.
    /// </summary>
    public string GrabTextureName => _grabTextureName;

    /// <summary>
    /// The name of the texture uniform to bind the grabbed depth texture to. Empty if depth isn't grabbed.
    /// </summary>
    public string GrabDepthTextureName => _grabDepthTextureName;

    /// <summary>
    /// Whether this pass captures the screen colour before rendering
    /// </summary>
    public bool HasGrabTexture => !string.IsNullOrEmpty(_grabTextureName);

    /// <summary>
    /// Whether this pass also captures the depth buffer alongside colour. Implies <see cref="HasGrabTexture"/>.
    /// </summary>
    public bool HasGrabDepth => !string.IsNullOrEmpty(_grabDepthTextureName);

    /// <summary>True when this pass has GLSL vertex/fragment sources.</summary>
    public bool HasGlsl => !string.IsNullOrEmpty(_vertexSource) && !string.IsNullOrEmpty(_fragmentSource);

    /// <summary>True when this pass has HLSL vertex/fragment sources.</summary>
    public bool HasHlsl => !string.IsNullOrEmpty(_hlslVertexSource) && !string.IsNullOrEmpty(_hlslFragmentSource);

    public IEnumerable<KeyValuePair<string, ShaderVariant>> Variants => _variants;


    private ShaderPass() { }

    public ShaderPass(
        string name,
        Dictionary<string, string>? tags,
        Dictionary<string, int>? tagSortOffsets,
        RasterizerState state,
        string vertexSource,
        string fragmentSource,
        string fallbackAsset,
        string grabTextureName = "",
        string grabDepthTextureName = "",
        string hlslVertexSource = "",
        string hlslFragmentSource = "")
    {
        _name = name;

        _tags = tags ?? [];
        _tagSortOffsets = tagSortOffsets ?? [];
        _rasterizerState = state;

        _vertexSource = vertexSource ?? "";
        _fragmentSource = fragmentSource ?? "";
        _hlslVertexSource = hlslVertexSource ?? "";
        _hlslFragmentSource = hlslFragmentSource ?? "";
        _fallbackAsset = fallbackAsset;

        _grabTextureName = grabTextureName;
        _grabDepthTextureName = grabDepthTextureName;

        _variants = [];
    }

    /// <summary>
    /// OpenGL-compatible API: returns the linked <see cref="GraphicsProgram"/> for the
    /// variant. On Vulkan/D3D12 this fails if only bytecode is available — prefer
    /// <see cref="TryGetVariant"/>.
    /// </summary>
    public bool TryGetVariantProgram(Dictionary<string, bool>? keywordID, out GraphicsProgram variant)
    {
        if (!TryGetVariant(keywordID, out ShaderVariant? shaderVariant) || shaderVariant == null)
        {
            variant = null!;
            return false;
        }

        if (shaderVariant.GlProgram != null)
        {
            variant = shaderVariant.GlProgram;
            return true;
        }

        Debug.LogWarning(
            $"Shader pass '{Name}' compiled to bytecode for the active backend; GraphicsProgram is unavailable. Use TryGetVariant.");
        variant = null!;
        return false;
    }

    /// <summary>
    /// Compile (or fetch cached) a <see cref="ShaderVariant"/> for the active graphics backend.
    /// OpenGL / Null: GLSL → <see cref="GraphicsProgram"/>.
    /// Vulkan / D3D12: HLSL → <see cref="CompiledShaderBytecode"/> via DXC.
    /// </summary>
    public bool TryGetVariant(Dictionary<string, bool>? keywordID, out ShaderVariant variant)
    {
        string keywords = BuildKeywordKey(keywordID);

        if (_variants.TryGetValue(keywords, out variant!))
            return true;

        GraphicsBackend backend = Graphics.Device?.Backend ?? GraphicsBackend.OpenGL;

        try
        {
            if (ShaderCompilerFactory.RequiresHlsl(backend))
            {
                variant = CompileHlslVariant(backend, keywordID, keywords);
            }
            else
            {
                variant = CompileGlslVariant(keywordID, keywords);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to compile shader pass of {Name}. Exception: {e.Message}");

            if (string.Equals(Name, "Invalid", StringComparison.Ordinal))
                throw;

            var fallbackShader = Resources.Shader.LoadDefault(Resources.DefaultShader.Invalid);
            if (fallbackShader.IsValid())
            {
                if (!fallbackShader.GetPass(0).TryGetVariant(null, out variant!))
                    throw new Exception($"Failed to compile shader pass of {Name}. Fallback shader also failed to compile.");
            }
            else
            {
                throw new Exception($"Failed to compile shader pass of {Name}. Fallback shader is null.");
            }
        }

        _variants[keywords] = variant;
        return true;
    }

    private ShaderVariant CompileGlslVariant(Dictionary<string, bool>? keywordID, string keywords)
    {
        if (!HasGlsl)
            throw new Exception($"Failed to compile shader pass of {Name}. GLSL Vertex/Fragment sources are missing.");

        IShaderCompiler compiler = ShaderCompilerFactory.GetCompiler(GraphicsBackend.OpenGL);
        ShaderCompileResult result = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = GraphicsBackend.OpenGL,
            Language = ShaderLanguage.Glsl,
            VertexSource = _vertexSource,
            FragmentSource = _fragmentSource,
            Keywords = keywordID,
        });

        if (!result.Success)
            throw new Exception(result.ErrorMessage ?? "GLSL compile failed.");

        Debug.Log("Compiling shader pass " + Name + " with keywords: " + keywords);

        GraphicsProgram program = Graphics.CompileProgram(
            result.GlslFragmentSource!,
            result.GlslVertexSource!,
            result.GlslGeometrySource);

        return new ShaderVariant(program);
    }

    private ShaderVariant CompileHlslVariant(GraphicsBackend backend, Dictionary<string, bool>? keywordID, string keywords)
    {
        if (!HasHlsl)
        {
            Debug.LogWarning(
                $"Shader pass '{Name}' has no HLSLPROGRAM for backend {backend}. " +
                "Falling back to Invalid shader. Add an HLSLPROGRAM block for Vulkan/D3D12.");

            var fallbackShader = Resources.Shader.LoadDefault(Resources.DefaultShader.Invalid);
            if (fallbackShader.IsValid() && fallbackShader.GetPass(0).HasHlsl)
            {
                if (fallbackShader.GetPass(0).TryGetVariant(null, out ShaderVariant? fb) && fb != null)
                    return fb;
            }

            throw new Exception(
                $"Failed to compile shader pass of {Name}: HLSL source missing for {backend} and Invalid fallback has no HLSL.");
        }

        IShaderCompiler compiler = ShaderCompilerFactory.GetCompiler(backend);
        ShaderCompileResult result = compiler.Compile(new ShaderCompileRequest
        {
            TargetBackend = backend,
            Language = ShaderLanguage.Hlsl,
            VertexSource = _hlslVertexSource,
            FragmentSource = _hlslFragmentSource,
            Keywords = keywordID,
        });

        if (!result.Success)
            throw new Exception(result.ErrorMessage ?? "DXC compile failed.");

        Debug.Log("Compiling HLSL shader pass " + Name + " for " + backend + " with keywords: " + keywords);

        var bytecode = new CompiledShaderBytecode(
            ShaderLanguage.Hlsl,
            result.Format,
            result.VertexBytecode!,
            result.FragmentBytecode!,
            result.BindingLayout);

        return new ShaderVariant(bytecode);
    }

    private static string BuildKeywordKey(Dictionary<string, bool>? keywordID)
    {
        string keywords = string.Empty;
        if (keywordID != null)
        {
            foreach (KeyValuePair<string, bool> kvp in keywordID)
            {
                if (kvp.Value)
                    keywords += $"{kvp.Key};";
            }
        }
        return keywords;
    }

    public bool HasTag(string tag, string? tagValue = null)
    {
        if (_tags.TryGetValue(tag, out string value))
            return tagValue == null || value == tagValue;

        return false;
    }

    /// <summary>
    /// Gets the sort offset for a given tag, or 0 if no offset is specified
    /// </summary>
    public int GetTagSortOffset(string tag)
    {
        return _tagSortOffsets.TryGetValue(tag, out int offset) ? offset : 0;
    }

    /// <summary>Disposes every compiled variant program. The pass itself keeps its source/tags so it
    /// can still recompile fresh variants on next use if the owning Shader isn't actually disposed.</summary>
    public void Dispose()
    {
        foreach (var variant in _variants.Values)
            variant.Dispose();
        _variants.Clear();
    }
}
