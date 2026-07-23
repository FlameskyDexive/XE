// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.RHI.Shaders;

/// <summary>Source language for a shader compile request.</summary>
public enum ShaderLanguage
{
    Glsl = 0,
    Hlsl = 1,
}

/// <summary>Bytecode / IR format produced by a compiler for a given backend.</summary>
public enum ShaderBytecodeFormat
{
    /// <summary>OpenGL: GLSL source strings (not true bytecode).</summary>
    GlslSource = 0,

    /// <summary>Vulkan: SPIR-V module bytes.</summary>
    SpirV = 1,

    /// <summary>Direct3D 12: DXIL container bytes.</summary>
    Dxil = 2,
}
