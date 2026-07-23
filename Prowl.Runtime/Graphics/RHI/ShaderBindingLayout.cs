// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.RHI;

/// <summary>Kind of resource bound at a shader binding slot.</summary>
public enum ShaderBindingKind
{
    Texture,
    Buffer,
    Sampler,
}

/// <summary>
/// One named binding slot. Names match the current string-keyed uniform / texture /
/// buffer lookups so OpenGL reflection and modern descriptor sets stay compatible.
/// </summary>
public readonly struct ShaderBindingSlot
{
    public ShaderBindingKind Kind { get; }
    public int Slot { get; }
    public string Name { get; }

    public ShaderBindingSlot(ShaderBindingKind kind, int slot, string name)
    {
        Kind = kind;
        Slot = slot;
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }
}

/// <summary>
/// Declares texture, buffer, and sampler binding slots for a shader / pipeline.
/// Names are retained for reflection compatibility with the existing name-based
/// uniform system (<c>PropertyState</c> / <c>GraphicsProgram.uniformLocations</c>).
/// </summary>
public sealed class ShaderBindingLayout
{
    public ShaderBindingSlot[] Textures { get; init; } = Array.Empty<ShaderBindingSlot>();
    public ShaderBindingSlot[] Buffers { get; init; } = Array.Empty<ShaderBindingSlot>();
    public ShaderBindingSlot[] Samplers { get; init; } = Array.Empty<ShaderBindingSlot>();

    /// <summary>Find a binding by name across all kinds, or null if missing.</summary>
    public ShaderBindingSlot? FindByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        for (int i = 0; i < Textures.Length; i++)
        {
            if (Textures[i].Name == name)
                return Textures[i];
        }

        for (int i = 0; i < Buffers.Length; i++)
        {
            if (Buffers[i].Name == name)
                return Buffers[i];
        }

        for (int i = 0; i < Samplers.Length; i++)
        {
            if (Samplers[i].Name == name)
                return Samplers[i];
        }

        return null;
    }
}
