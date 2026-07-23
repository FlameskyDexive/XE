// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Prowl.Runtime.Backends.D3D12;

/// <summary>Maps engine enums to DXGI / D3D12 format and state enums.</summary>
internal static class D3D12Formats
{
    public static Format ToDxgiFormat(TextureImageFormat format) => format switch
    {
        TextureImageFormat.Color4b => Format.R8G8B8A8_UNorm,
        TextureImageFormat.Byte => Format.R8_UInt,
        TextureImageFormat.Float => Format.R32_Float,
        TextureImageFormat.Float2 => Format.R32G32_Float,
        TextureImageFormat.Float3 => Format.R32G32B32_Float,
        TextureImageFormat.Float4 => Format.R32G32B32A32_Float,
        TextureImageFormat.Short => Format.R16_Float,
        TextureImageFormat.Short2 => Format.R16G16_Float,
        TextureImageFormat.Short3 => Format.R16G16B16A16_Float, // no RGB16F in DXGI
        TextureImageFormat.Short4 => Format.R16G16B16A16_Float,
        TextureImageFormat.Int => Format.R32_SInt,
        TextureImageFormat.Int2 => Format.R32G32_SInt,
        TextureImageFormat.Int3 => Format.R32G32B32_SInt,
        TextureImageFormat.Int4 => Format.R32G32B32A32_SInt,
        TextureImageFormat.UnsignedShort => Format.R16_UInt,
        TextureImageFormat.UnsignedShort2 => Format.R16G16_UInt,
        TextureImageFormat.UnsignedShort3 => Format.R16G16B16A16_UInt,
        TextureImageFormat.UnsignedShort4 => Format.R16G16B16A16_UInt,
        TextureImageFormat.UnsignedInt => Format.R32_UInt,
        TextureImageFormat.UnsignedInt2 => Format.R32G32_UInt,
        TextureImageFormat.UnsignedInt3 => Format.R32G32B32_UInt,
        TextureImageFormat.UnsignedInt4 => Format.R32G32B32A32_UInt,
        TextureImageFormat.Depth16f => Format.D16_UNorm,
        TextureImageFormat.Depth24f => Format.D24_UNorm_S8_UInt,
        TextureImageFormat.Depth32f => Format.D32_Float,
        TextureImageFormat.Depth24Stencil8 => Format.D24_UNorm_S8_UInt,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public static bool IsDepth(TextureImageFormat format) =>
        format is TextureImageFormat.Depth16f
            or TextureImageFormat.Depth24f
            or TextureImageFormat.Depth32f
            or TextureImageFormat.Depth24Stencil8;

    public static PrimitiveTopologyType ToTopologyType(Topology topology) => topology switch
    {
        Topology.Points => PrimitiveTopologyType.Point,
        Topology.Lines or Topology.LineStrip or Topology.LineLoop => PrimitiveTopologyType.Line,
        _ => PrimitiveTopologyType.Triangle,
    };

    public static PrimitiveTopology ToTopology(Topology topology) => topology switch
    {
        Topology.Points => PrimitiveTopology.PointList,
        Topology.Lines => PrimitiveTopology.LineList,
        Topology.LineStrip => PrimitiveTopology.LineStrip,
        Topology.Triangles => PrimitiveTopology.TriangleList,
        Topology.TriangleStrip => PrimitiveTopology.TriangleStrip,
        Topology.LineLoop => PrimitiveTopology.LineStrip,
        Topology.TriangleFan => PrimitiveTopology.TriangleList,
        Topology.Quads => PrimitiveTopology.TriangleList,
        _ => PrimitiveTopology.TriangleList,
    };

    public static Format ToVertexFormat(VertexFormat.VertexType type, byte count, bool normalized)
    {
        return (type, count, normalized) switch
        {
            (VertexFormat.VertexType.Float, 1, _) => Format.R32_Float,
            (VertexFormat.VertexType.Float, 2, _) => Format.R32G32_Float,
            (VertexFormat.VertexType.Float, 3, _) => Format.R32G32B32_Float,
            (VertexFormat.VertexType.Float, 4, _) => Format.R32G32B32A32_Float,
            (VertexFormat.VertexType.Byte, 4, true) => Format.R8G8B8A8_SNorm,
            (VertexFormat.VertexType.Byte, 4, false) => Format.R8G8B8A8_SInt,
            (VertexFormat.VertexType.UnsignedByte, 4, true) => Format.R8G8B8A8_UNorm,
            (VertexFormat.VertexType.UnsignedByte, 4, false) => Format.R8G8B8A8_UInt,
            (VertexFormat.VertexType.Short, 2, false) => Format.R16G16_SInt,
            (VertexFormat.VertexType.Short, 4, false) => Format.R16G16B16A16_SInt,
            (VertexFormat.VertexType.Int, 1, _) => Format.R32_SInt,
            (VertexFormat.VertexType.Int, 2, _) => Format.R32G32_SInt,
            (VertexFormat.VertexType.Int, 4, _) => Format.R32G32B32A32_SInt,
            _ => Format.R32G32B32A32_Float,
        };
    }

    public static ComparisonFunction ToComparison(RasterizerState.DepthMode mode) => mode switch
    {
        RasterizerState.DepthMode.Never => ComparisonFunction.Never,
        RasterizerState.DepthMode.Less => ComparisonFunction.Less,
        RasterizerState.DepthMode.Equal => ComparisonFunction.Equal,
        RasterizerState.DepthMode.Lequal => ComparisonFunction.LessEqual,
        RasterizerState.DepthMode.Greater => ComparisonFunction.Greater,
        RasterizerState.DepthMode.Notequal => ComparisonFunction.NotEqual,
        RasterizerState.DepthMode.Gequal => ComparisonFunction.GreaterEqual,
        RasterizerState.DepthMode.Always => ComparisonFunction.Always,
        _ => ComparisonFunction.LessEqual,
    };

    public static void GetVertexSemantic(uint semantic, out string name, out uint index)
    {
        switch ((VertexFormat.VertexSemantic)semantic)
        {
            case VertexFormat.VertexSemantic.Position:
                name = "POSITION";
                index = 0;
                return;
            case VertexFormat.VertexSemantic.TexCoord0:
                name = "TEXCOORD";
                index = 0;
                return;
            case VertexFormat.VertexSemantic.TexCoord1:
                name = "TEXCOORD";
                index = 1;
                return;
            case VertexFormat.VertexSemantic.Normal:
                name = "NORMAL";
                index = 0;
                return;
            case VertexFormat.VertexSemantic.Color:
                name = "COLOR";
                index = 0;
                return;
            case VertexFormat.VertexSemantic.Tangent:
                name = "TANGENT";
                index = 0;
                return;
            case VertexFormat.VertexSemantic.BoneIndex:
                name = "BLENDINDICES";
                index = 0;
                return;
            case VertexFormat.VertexSemantic.BoneWeight:
                name = "BLENDWEIGHT";
                index = 0;
                return;
            default:
                name = "TEXCOORD";
                index = semantic;
                return;
        }
    }

    public static CullMode ToCullMode(RasterizerState.PolyFace face) => face switch
    {
        RasterizerState.PolyFace.None => CullMode.None,
        RasterizerState.PolyFace.Front => CullMode.Front,
        RasterizerState.PolyFace.Back => CullMode.Back,
        _ => CullMode.Back,
    };

    public static Blend ToBlend(RasterizerState.Blending b) => b switch
    {
        RasterizerState.Blending.Zero => Blend.Zero,
        RasterizerState.Blending.One => Blend.One,
        RasterizerState.Blending.SrcColor => Blend.SourceColor,
        RasterizerState.Blending.OneMinusSrcColor => Blend.InverseSourceColor,
        RasterizerState.Blending.DstColor => Blend.DestinationColor,
        RasterizerState.Blending.OneMinusDstColor => Blend.InverseDestinationColor,
        RasterizerState.Blending.SrcAlpha => Blend.SourceAlpha,
        RasterizerState.Blending.OneMinusSrcAlpha => Blend.InverseSourceAlpha,
        RasterizerState.Blending.DstAlpha => Blend.DestinationAlpha,
        RasterizerState.Blending.OneMinusDstAlpha => Blend.InverseDestinationAlpha,
        RasterizerState.Blending.ConstantColor => Blend.BlendFactor,
        RasterizerState.Blending.OneMinusConstantColor => Blend.InverseBlendFactor,
        RasterizerState.Blending.SrcAlphaSaturate => Blend.SourceAlphaSaturate,
        _ => Blend.One,
    };

    public static BlendOperation ToBlendOp(RasterizerState.BlendMode mode) => mode switch
    {
        RasterizerState.BlendMode.Add => BlendOperation.Add,
        RasterizerState.BlendMode.Subtract => BlendOperation.Subtract,
        RasterizerState.BlendMode.ReverseSubtract => BlendOperation.RevSubtract,
        RasterizerState.BlendMode.Min => BlendOperation.Min,
        RasterizerState.BlendMode.Max => BlendOperation.Max,
        _ => BlendOperation.Add,
    };

    public static Filter ToFilter(TextureMin min, TextureMag mag)
    {
        bool nearestMin = min is TextureMin.Nearest or TextureMin.NearestMipmapNearest or TextureMin.NearestMipmapLinear;
        bool nearestMag = mag == TextureMag.Nearest;
        if (nearestMin && nearestMag) return Filter.MinMagMipPoint;
        if (!nearestMin && !nearestMag) return Filter.MinMagMipLinear;
        return Filter.MinMagMipLinear;
    }

    public static TextureAddressMode ToAddressMode(TextureWrap wrap) => wrap switch
    {
        TextureWrap.Repeat => TextureAddressMode.Wrap,
        TextureWrap.MirroredRepeat => TextureAddressMode.Mirror,
        TextureWrap.ClampToEdge => TextureAddressMode.Clamp,
        TextureWrap.ClampToBorder => TextureAddressMode.Border,
        _ => TextureAddressMode.Wrap,
    };
}
