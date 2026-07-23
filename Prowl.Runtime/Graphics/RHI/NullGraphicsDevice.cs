// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.RHI;

/// <summary>
/// No-op <see cref="IGraphicsDevice"/> for headless / dedicated-server runs.
/// Validates and recycles command buffers without performing GPU work.
/// </summary>
public sealed class NullGraphicsDevice : IGraphicsDevice
{
    private bool _initialized;
    private ulong _fenceValue;

    public GraphicsBackend Backend => GraphicsBackend.Null;

    public GraphicsDeviceCapabilities Capabilities { get; } = new()
    {
        MaxTextureSize = 16384,
        MaxCubeMapTextureSize = 16384,
        MaxArrayTextureLayers = 2048,
        MaxFramebufferColorAttachments = 8,
        MaxFramesInFlight = 1,
        SupportsCompute = false,
        SupportsGeometryShader = false,
        BackendName = "Null",
    };

    public bool IsInitialized => _initialized;

    public void Initialize(IWindowSurface? surface)
    {
        // Surface is ignored; Null is always headless-compatible.
        _initialized = true;
        _fenceValue = 0;
    }

    public void Shutdown()
    {
        _initialized = false;
    }

    public void BeginFrame()
    {
        EnsureInitialized();
    }

    public void EndFrame()
    {
        EnsureInitialized();
        _fenceValue++;
    }

    public void Execute(CommandBuffer commandBuffer, bool wait)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        EnsureInitialized();

        if (commandBuffer._inPool)
            throw new InvalidOperationException("CommandBuffer has already been submitted (it's in the pool).");

        // Mirror Graphics.Submit / SubmitAndWait headless path: drop GPU work and recycle.
        // wait is ignored — Null completes synchronously.
        _ = wait;
        commandBuffer._ownerReleased = true;
        CommandBufferPool.Return(commandBuffer);
        _fenceValue++;
    }

    public void WaitIdle()
    {
        // Nothing pending.
    }

    public ulong GetFenceValue() => _fenceValue;

    public void WaitFence(ulong fenceValue)
    {
        // Work completes synchronously in Execute / EndFrame.
        if (fenceValue > _fenceValue)
            _fenceValue = fenceValue;
    }

    public void Dispose() => Shutdown();

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("NullGraphicsDevice has not been initialized.");
    }
}
