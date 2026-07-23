// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.RHI.Shaders;

/// <summary>Selects an <see cref="IShaderCompiler"/> for a <see cref="GraphicsBackend"/>.</summary>
public static class ShaderCompilerFactory
{
    private static readonly GlslShaderCompiler s_glsl = new();
    private static readonly DxcShaderCompiler s_dxc = new();

    /// <summary>
    /// OpenGL / Null / Auto → GLSL injector. Vulkan / Direct3D12 → DXC (HLSL → SPIR-V / DXIL).
    /// </summary>
    public static IShaderCompiler GetCompiler(GraphicsBackend backend)
    {
        return backend switch
        {
            GraphicsBackend.Vulkan or GraphicsBackend.Direct3D12 => s_dxc,
            GraphicsBackend.OpenGL or GraphicsBackend.Null or GraphicsBackend.Auto => s_glsl,
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown graphics backend."),
        };
    }

    /// <summary>Whether the backend expects HLSL bytecode rather than GLSL source.</summary>
    public static bool RequiresHlsl(GraphicsBackend backend) =>
        backend is GraphicsBackend.Vulkan or GraphicsBackend.Direct3D12;
}
