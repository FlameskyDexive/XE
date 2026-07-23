// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.RHI;

/// <summary>Creation description for a GPU buffer.</summary>
public sealed class BufferDescriptor
{
    public BufferType Type { get; init; }
    public int Size { get; init; }
    public bool Dynamic { get; init; }
}

/// <summary>Creation description for a GPU texture / image.</summary>
public sealed class TextureDescriptor
{
    public TextureType Type { get; init; }
    public TextureImageFormat Format { get; init; }
    public int Width { get; init; }
    public int Height { get; init; } = 1;
    public int Depth { get; init; } = 1;
    public int MipLevels { get; init; } = 1;
}

/// <summary>One color or depth attachment for a framebuffer.</summary>
public sealed class FramebufferAttachmentDescriptor
{
    public GpuHandle Texture { get; init; }
    public int AttachmentIndex { get; init; }
    public bool IsDepth { get; init; }
}

/// <summary>Creation description for a framebuffer / render pass target set.</summary>
public sealed class FramebufferDescriptor
{
    public FramebufferAttachmentDescriptor[] Attachments { get; init; } = Array.Empty<FramebufferAttachmentDescriptor>();
    public int Width { get; init; }
    public int Height { get; init; }
}

/// <summary>Vertex input layout plus bound buffer handles for a draw.</summary>
public sealed class VertexInputDescriptor
{
    public VertexFormat Format { get; init; } = null!;
    public GpuHandle[] VertexBuffers { get; init; } = Array.Empty<GpuHandle>();
    public GpuHandle IndexBuffer { get; init; }
}

/// <summary>Which programmable stage a <see cref="ShaderStageSource"/> targets.</summary>
public enum ShaderStage
{
    Vertex,
    Fragment,
    Geometry,
}

/// <summary>
/// One shader stage's source. Provide either <see cref="SourceText"/> (GLSL/HLSL)
/// or <see cref="SourceBytes"/> (SPIR-V / DXIL); backends pick the appropriate field.
/// Language values match <see cref="Shaders.ShaderLanguage"/> / bytecode formats.
/// </summary>
public sealed class ShaderStageSource
{
    public ShaderStage Stage { get; init; }
    public Shaders.ShaderLanguage Language { get; init; }
    public string? SourceText { get; init; }
    public byte[]? SourceBytes { get; init; }
    public string EntryPoint { get; init; } = "main";
}
