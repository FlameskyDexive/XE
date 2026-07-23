// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.RHI;

/// <summary>
/// Backend-neutral graphics device. Owns presentation, command execution, and
/// GPU-work retirement. Resource wrappers (<c>GraphicsBuffer</c>, etc.) may call
/// into the active device; this interface does not mandate a full create/destroy API yet.
/// </summary>
public interface IGraphicsDevice : IDisposable
{
    GraphicsBackend Backend { get; }
    GraphicsDeviceCapabilities Capabilities { get; }
    bool IsInitialized { get; }

    /// <summary>
    /// Bind to an optional window surface and finish device setup.
    /// Pass <c>null</c> for headless / Null backends.
    /// </summary>
    void Initialize(IWindowSurface? surface);

    void Shutdown();

    /// <summary>Begin a new frame (acquire image / arm frame fence).</summary>
    void BeginFrame();

    /// <summary>End the frame and present when a surface is attached.</summary>
    void EndFrame();

    /// <summary>
    /// Execute a recorded <see cref="CommandBuffer"/> on the device's GPU queue / executor.
    /// When <paramref name="wait"/> is true, blocks until the buffer has finished (and
    /// rethrows render-thread exceptions on the caller), matching historical SubmitAndWait.
    /// </summary>
    void Execute(CommandBuffer commandBuffer, bool wait);

    /// <summary>Block until all submitted GPU work has completed.</summary>
    void WaitIdle();

    /// <summary>Monotonically increasing fence value reflecting completed GPU work.</summary>
    ulong GetFenceValue();

    /// <summary>Block until the device fence reaches at least <paramref name="fenceValue"/>.</summary>
    void WaitFence(ulong fenceValue);
}
