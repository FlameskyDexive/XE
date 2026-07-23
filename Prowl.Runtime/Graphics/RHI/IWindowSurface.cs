// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.RHI;

/// <summary>
/// Minimal window / surface abstraction so devices do not depend on a specific
/// windowing library. NativeHandle is the platform HWND / NSView / X11 window /
/// Wayland surface pointer as required by the backend.
/// </summary>
public interface IWindowSurface
{
    /// <summary>Platform-native window or view handle.</summary>
    nint NativeHandle { get; }

    /// <summary>Current drawable framebuffer size in pixels.</summary>
    (int Width, int Height) FramebufferSize { get; }

    bool VSync { get; set; }

    /// <summary>Legacy GL swap interval (0 = immediate, 1 = vsync). Ignored by Vulkan/D3D12.</summary>
    int SwapInterval { get; set; }

    /// <summary>Make this surface's GL context current on the calling thread (OpenGL only).</summary>
    void MakeCurrent();

    /// <summary>Clear the current GL context on the calling thread (OpenGL only).</summary>
    void ClearCurrent();

    /// <summary>Present the backbuffer via the legacy GL path (OpenGL only).</summary>
    void SwapBuffers();

    /// <summary>
    /// Optional hook for Vulkan WSI. Default returns <see cref="nint.Zero"/>;
    /// concrete surfaces may override when Vulkan support is wired.
    /// </summary>
    nint CreateVulkanSurface(nint instance) => nint.Zero;
}
