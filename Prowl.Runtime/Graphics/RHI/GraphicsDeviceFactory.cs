// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Backends.D3D12;
using Prowl.Runtime.Backends.OpenGL;
using Prowl.Runtime.Backends.Vulkan;

namespace Prowl.Runtime.RHI;

/// <summary>Creates an <see cref="IGraphicsDevice"/> for the requested backend.</summary>
public static class GraphicsDeviceFactory
{
    /// <summary>
    /// Create a device for <paramref name="options"/>.
    /// <see cref="GraphicsBackend.Auto"/> tries Direct3D12 (Windows), then Vulkan, then OpenGL.
    /// </summary>
    public static IGraphicsDevice Create(GraphicsDeviceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Backend == GraphicsBackend.Auto)
        {
            foreach (GraphicsBackend candidate in AutoCandidates())
            {
                try
                {
                    return CreateExplicit(candidate, WithBackend(options, candidate));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Backend {candidate} unavailable: {ex.Message}");
                }
            }

            return new OpenGLGraphicsDevice(WithBackend(options, GraphicsBackend.OpenGL));
        }

        return CreateExplicit(options.Backend, options);
    }

    private static IGraphicsDevice CreateExplicit(GraphicsBackend backend, GraphicsDeviceOptions options) =>
        backend switch
        {
            GraphicsBackend.Null => new NullGraphicsDevice(),
            GraphicsBackend.OpenGL => new OpenGLGraphicsDevice(options),
            GraphicsBackend.Vulkan => new VulkanGraphicsDevice(options),
            GraphicsBackend.Direct3D12 => new D3D12GraphicsDevice(options),
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown graphics backend."),
        };

    private static GraphicsBackend[] AutoCandidates()
    {
        if (OperatingSystem.IsWindows())
            return [GraphicsBackend.Direct3D12, GraphicsBackend.Vulkan, GraphicsBackend.OpenGL];
        return [GraphicsBackend.Vulkan, GraphicsBackend.OpenGL];
    }

    private static GraphicsDeviceOptions WithBackend(GraphicsDeviceOptions options, GraphicsBackend backend) =>
        new()
        {
            Backend = backend,
            Debug = options.Debug,
            PreferredAdapterIndex = options.PreferredAdapterIndex,
            EnableValidation = options.EnableValidation,
            VSync = options.VSync,
        };
}
