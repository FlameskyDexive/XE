// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Threading;

namespace Prowl.Runtime.RHI.Shaders;

/// <summary>
/// A compiled shader variant for the active backend: either a linked OpenGL
/// <see cref="GraphicsProgram"/> or Vulkan/D3D12 <see cref="CompiledShaderBytecode"/>.
/// </summary>
public sealed class ShaderVariant : IDisposable
{
    private static int s_nextId;

    public int Id { get; } = Interlocked.Increment(ref s_nextId);
    public GraphicsProgram? GlProgram { get; }
    public CompiledShaderBytecode? Bytecode { get; }

    public bool IsOpenGl => GlProgram != null;
    public bool IsBytecode => Bytecode != null;

    public ShaderVariant(GraphicsProgram program)
    {
        GlProgram = program ?? throw new ArgumentNullException(nameof(program));
    }

    public ShaderVariant(CompiledShaderBytecode bytecode)
    {
        Bytecode = bytecode ?? throw new ArgumentNullException(nameof(bytecode));
    }

    public void Dispose()
    {
        GlProgram?.Dispose();
    }
}
