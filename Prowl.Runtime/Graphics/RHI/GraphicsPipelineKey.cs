// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.RHI.Shaders;

namespace Prowl.Runtime.RHI;

/// <summary>
/// Exact backend pipeline-cache key. Keeps the full raster state instead of relying on
/// a hash-only lookup, so collisions cannot select an incompatible PSO.
/// </summary>
internal readonly struct GraphicsPipelineKey : IEquatable<GraphicsPipelineKey>
{
    public int ShaderVariantId { get; }
    public uint VertexArrayHandle { get; }
    public Topology Topology { get; }
    public RasterizerState RasterState { get; }
    public bool Index32Bit { get; }

    public GraphicsPipelineKey(
        ShaderVariant shaderVariant,
        uint vertexArrayHandle,
        Topology topology,
        in RasterizerState rasterState,
        bool index32Bit)
    {
        ArgumentNullException.ThrowIfNull(shaderVariant);

        ShaderVariantId = shaderVariant.Id;
        VertexArrayHandle = vertexArrayHandle;
        Topology = topology;
        RasterState = rasterState;
        Index32Bit = index32Bit;
    }

    public bool Equals(GraphicsPipelineKey other)
    {
        RasterizerState rasterState = RasterState;
        RasterizerState otherRasterState = other.RasterState;
        return ShaderVariantId == other.ShaderVariantId &&
            VertexArrayHandle == other.VertexArrayHandle &&
            Topology == other.Topology &&
            Index32Bit == other.Index32Bit &&
            RasterEquals(in rasterState, in otherRasterState);
    }

    public override bool Equals(object? obj) => obj is GraphicsPipelineKey other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(ShaderVariantId);
        hash.Add(VertexArrayHandle);
        hash.Add(Topology);
        hash.Add(Index32Bit);
        RasterizerState rasterState = RasterState;
        AddRasterHash(ref hash, in rasterState);
        return hash.ToHashCode();
    }

    public static bool operator ==(GraphicsPipelineKey left, GraphicsPipelineKey right) => left.Equals(right);
    public static bool operator !=(GraphicsPipelineKey left, GraphicsPipelineKey right) => !left.Equals(right);

    private static bool RasterEquals(in RasterizerState left, in RasterizerState right) =>
        left.DepthTest == right.DepthTest &&
        left.DepthWrite == right.DepthWrite &&
        left.Depth == right.Depth &&
        left.DoBlend == right.DoBlend &&
        left.BlendSrc == right.BlendSrc &&
        left.BlendDst == right.BlendDst &&
        left.Blend == right.Blend &&
        left.CullFace == right.CullFace &&
        left.Winding == right.Winding &&
        left.StencilEnabled == right.StencilEnabled &&
        left.StencilFunc == right.StencilFunc &&
        left.StencilRef == right.StencilRef &&
        left.StencilReadMask == right.StencilReadMask &&
        left.StencilWriteMask == right.StencilWriteMask &&
        left.StencilPassOp == right.StencilPassOp &&
        left.StencilFailOp == right.StencilFailOp &&
        left.StencilZFailOp == right.StencilZFailOp;

    private static void AddRasterHash(ref HashCode hash, in RasterizerState state)
    {
        hash.Add(state.DepthTest);
        hash.Add(state.DepthWrite);
        hash.Add(state.Depth);
        hash.Add(state.DoBlend);
        hash.Add(state.BlendSrc);
        hash.Add(state.BlendDst);
        hash.Add(state.Blend);
        hash.Add(state.CullFace);
        hash.Add(state.Winding);
        hash.Add(state.StencilEnabled);
        hash.Add(state.StencilFunc);
        hash.Add(state.StencilRef);
        hash.Add(state.StencilReadMask);
        hash.Add(state.StencilWriteMask);
        hash.Add(state.StencilPassOp);
        hash.Add(state.StencilFailOp);
        hash.Add(state.StencilZFailOp);
    }
}
