// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime.RHI.Shaders;

/// <summary>Input to <see cref="IShaderCompiler.Compile"/>.</summary>
public sealed class ShaderCompileRequest
{
    public required GraphicsBackend TargetBackend { get; init; }
    public required ShaderLanguage Language { get; init; }

    /// <summary>Vertex-stage source (GLSL or HLSL text).</summary>
    public required string VertexSource { get; init; }

    /// <summary>Fragment / pixel-stage source (GLSL or HLSL text).</summary>
    public required string FragmentSource { get; init; }

    /// <summary>Optional geometry-stage source.</summary>
    public string? GeometrySource { get; init; }

    /// <summary>Vertex entry point name (HLSL / SPIR-V). Defaults to <c>main</c>.</summary>
    public string VertexEntryPoint { get; init; } = "main";

    /// <summary>Fragment entry point name. Defaults to <c>main</c>.</summary>
    public string FragmentEntryPoint { get; init; } = "main";

    /// <summary>Preprocessor keywords / defines (name → enabled).</summary>
    public IReadOnlyDictionary<string, bool>? Keywords { get; init; }

    /// <summary>Extra <c>#define</c> names injected before stage sources.</summary>
    public IReadOnlyList<string>? ExtraDefines { get; init; }
}
