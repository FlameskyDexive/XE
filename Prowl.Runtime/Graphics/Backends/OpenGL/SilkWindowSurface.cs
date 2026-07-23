// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.Windowing;

using Prowl.Runtime.RHI;

namespace Prowl.Runtime.Backends.OpenGL;

/// <summary>
/// <see cref="IWindowSurface"/> wrapping a Silk.NET <see cref="IWindow"/> and its GL context.
/// </summary>
public sealed class SilkWindowSurface : IWindowSurface
{
    private readonly IWindow _window;
    private int _swapInterval;

    public SilkWindowSurface(IWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _swapInterval = _window.VSync ? 1 : 0;
    }

    /// <summary>Underlying Silk window (used for <c>GL.GetApi</c>).</summary>
    public IWindow Window => _window;

    public nint NativeHandle => _window.Handle;

    public (int Width, int Height) FramebufferSize
    {
        get
        {
            var size = _window.FramebufferSize;
            return (size.X, size.Y);
        }
    }

    public bool VSync
    {
        get => _window.VSync;
        set => _window.VSync = value;
    }

    public int SwapInterval
    {
        get => _swapInterval;
        set
        {
            _swapInterval = value;
            _window.GLContext!.SwapInterval(value);
        }
    }

    public void MakeCurrent() => _window.GLContext!.MakeCurrent();

    public void ClearCurrent() => _window.GLContext!.Clear();

    public void SwapBuffers() => _window.GLContext!.SwapBuffers();

    /// <inheritdoc />
    public unsafe nint CreateVulkanSurface(nint instance)
    {
        // Silk.NET IVkSurface.Create signature varies by package version; surface creation
        // for the Vulkan backend will be wired when the window/Vulkan path is finalized.
        _ = instance;
        return nint.Zero;
    }
}
