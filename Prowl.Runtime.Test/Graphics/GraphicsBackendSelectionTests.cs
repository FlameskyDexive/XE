// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.RHI;

using Xunit;

namespace Prowl.Runtime.Test.Rhi;

public class GraphicsBackendSelectionTests
{
    [Theory]
    [InlineData("vulkan", GraphicsBackend.Vulkan)]
    [InlineData("vk", GraphicsBackend.Vulkan)]
    [InlineData("d3d12", GraphicsBackend.Direct3D12)]
    [InlineData("dx12", GraphicsBackend.Direct3D12)]
    [InlineData("opengl", GraphicsBackend.OpenGL)]
    [InlineData("null", GraphicsBackend.Null)]
    [InlineData("auto", GraphicsBackend.Auto)]
    public void TryParseName_RecognizesAliases(string name, GraphicsBackend expected)
    {
        Assert.True(GraphicsBackendSelection.TryParseName(name, out GraphicsBackend backend));
        Assert.Equal(expected, backend);
    }

    [Fact]
    public void Parse_ReadsEqualsForm()
    {
        GraphicsBackend previous = GraphicsBackendSelection.Preferred ?? GraphicsBackend.Auto;
        try
        {
            GraphicsBackendSelection.Preferred = null;
            GraphicsBackend backend = GraphicsBackendSelection.Parse(["--graphics=vulkan"]);
            Assert.Equal(GraphicsBackend.Vulkan, backend);
        }
        finally
        {
            GraphicsBackendSelection.Preferred = previous == GraphicsBackend.Auto ? null : previous;
        }
    }

    [Fact]
    public void NullDevice_RecyclesCommandBuffers()
    {
        var device = new NullGraphicsDevice();
        device.Initialize(null);
        CommandBuffer cmd = global::Prowl.Runtime.Graphics.GetCommandBuffer("test");
        // Execute takes ownership and returns to pool.
        device.Execute(cmd, wait: false);
        Assert.True(device.GetFenceValue() > 0);
        device.Dispose();
    }

    [Fact]
    public void TopologyUtilities_MarksLegacyTopologies()
    {
        Assert.True(TopologyUtilities.IsPortable(Topology.Triangles));
        Assert.False(TopologyUtilities.IsPortable(Topology.Quads));
        Assert.False(TopologyUtilities.IsPortable(Topology.LineLoop));
        Assert.False(TopologyUtilities.IsPortable(Topology.TriangleFan));
    }
}
