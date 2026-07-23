// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
using System;

namespace Prowl.Runtime;

public enum TextureWrap { Repeat, ClampToBorder, ClampToEdge, MirroredRepeat }
public enum TextureType { Texture2D, Texture3D, TextureCubeMap, }
public enum TextureParameter { WrapS, WrapT, WrapR, MinFilter, MagFilter }
public enum TextureMin { Nearest, Linear, NearestMipmapNearest, LinearMipmapNearest, NearestMipmapLinear, LinearMipmapLinear }
public enum TextureMag { Nearest, Linear }
public enum TextureImageFormat
{
    Color4b,
    Byte,

    Short,
    Short2,
    Short3,
    Short4,

    Float,
    Float2,
    Float3,
    Float4,
    Depth16f,
    Depth24f,
    Depth32f,

    Int,
    Int2,
    Int3,
    Int4,

    UnsignedShort,
    UnsignedShort2,
    UnsignedShort3,
    UnsignedShort4,

    UnsignedInt,
    UnsignedInt2,
    UnsignedInt3,
    UnsignedInt4,

    Depth24Stencil8,
}

/// <summary>
/// Primitive topology for draw calls.
/// <para>
/// <see cref="LineLoop"/>, <see cref="TriangleFan"/>, and <see cref="Quads"/> are
/// OpenGL-legacy topologies. Vulkan and Direct3D12 do not support them natively;
/// backends may emulate them (index expansion) or reject draws that use them.
/// Prefer <see cref="Lines"/>, <see cref="LineStrip"/>, <see cref="Triangles"/>,
/// or <see cref="TriangleStrip"/> for portable content.
/// </para>
/// </summary>
public enum Topology
{
    Points,
    Lines,
    /// <summary>OpenGL-legacy; may be emulated or rejected on Vulkan/D3D12.</summary>
    LineLoop,
    LineStrip,
    Triangles,
    TriangleStrip,
    /// <summary>OpenGL-legacy; may be emulated or rejected on Vulkan/D3D12.</summary>
    TriangleFan,
    /// <summary>OpenGL-legacy; may be emulated or rejected on Vulkan/D3D12.</summary>
    Quads,
}

/// <summary>Helpers for portable (cross-backend) topology selection.</summary>
public static class TopologyUtilities
{
    /// <summary>
    /// Returns true when <paramref name="topology"/> is supported without emulation
    /// on Vulkan and Direct3D12 (as well as OpenGL).
    /// </summary>
    public static bool IsPortable(Topology topology) =>
        topology is Topology.Points
            or Topology.Lines
            or Topology.LineStrip
            or Topology.Triangles
            or Topology.TriangleStrip;
}

public enum FBOTarget { Read, Draw, Framebuffer, }

[Flags]
public enum ClearFlags
{
    Color = 1 << 1,
    Depth = 1 << 2,
    Stencil = 1 << 3,
}

public enum BlitFilter { Nearest, Linear }

public enum BufferType { VertexBuffer, ElementsBuffer, UniformBuffer, StructuredBuffer, Count }

public struct RasterizerState
{
    public enum DepthMode { Never, Less, Equal, Lequal, Greater, Notequal, Gequal, Always }
    public enum Blending { Zero, One, SrcColor, OneMinusSrcColor, DstColor, OneMinusDstColor, SrcAlpha, OneMinusSrcAlpha, DstAlpha, OneMinusDstAlpha, ConstantColor, OneMinusConstantColor, ConstantAlpha, OneMinusConstantAlpha, SrcAlphaSaturate, Src1Color, OneMinusSrc1Color, Src1Alpha, OneMinusSrc1Alpha }
    public enum BlendMode { Add, Subtract, ReverseSubtract, Min, Max }
    public enum PolyFace { None, Front, Back, FrontAndBack }
    public enum WindingOrder { CW, CCW }
    public enum StencilFunction { Never, Less, Equal, Lequal, Greater, Notequal, Gequal, Always }
    public enum StencilOp { Keep, Zero, Replace, Incr, IncrWrap, Decr, DecrWrap, Invert }

    public bool DepthTest = true;
    public bool DepthWrite = true;
    public DepthMode Depth = DepthMode.Lequal;

    public bool DoBlend = false;
    public Blending BlendSrc = Blending.SrcAlpha;
    public Blending BlendDst = Blending.OneMinusSrcAlpha;
    public BlendMode Blend = BlendMode.Add;

    public PolyFace CullFace = PolyFace.Back;

    public WindingOrder Winding = WindingOrder.CW;

    public bool StencilEnabled = false;
    public StencilFunction StencilFunc = StencilFunction.Always;
    public int StencilRef = 0;
    public int StencilReadMask = 255;
    public int StencilWriteMask = 255;
    public StencilOp StencilPassOp = StencilOp.Keep;
    public StencilOp StencilFailOp = StencilOp.Keep;
    public StencilOp StencilZFailOp = StencilOp.Keep;

    public RasterizerState()
    {
        // Default constructor
    }
}
