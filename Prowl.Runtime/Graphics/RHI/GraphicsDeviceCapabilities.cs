// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.RHI;

/// <summary>Device limits and feature flags queried after <see cref="IGraphicsDevice.Initialize"/>.</summary>
public sealed class GraphicsDeviceCapabilities
{
    public int MaxTextureSize { get; init; } = 16384;
    public int MaxCubeMapTextureSize { get; init; } = 16384;
    public int MaxArrayTextureLayers { get; init; } = 2048;
    public int MaxFramebufferColorAttachments { get; init; } = 8;

    /// <summary>Maximum frames the backend may keep in flight before retiring GPU work.</summary>
    public int MaxFramesInFlight { get; init; } = 2;

    public bool SupportsCompute { get; init; }
    public bool SupportsGeometryShader { get; init; }

    /// <summary>Human-readable backend name (e.g. "Null", "OpenGL 4.1", "Vulkan").</summary>
    public string BackendName { get; init; } = "Unknown";
}
