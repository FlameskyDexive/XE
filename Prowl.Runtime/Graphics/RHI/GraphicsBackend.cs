// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.RHI;

/// <summary>Selects which graphics API the device factory should create.</summary>
public enum GraphicsBackend
{
    /// <summary>Pick a suitable backend for the current platform (D3D12 → Vulkan → OpenGL).</summary>
    Auto = 0,

    /// <summary>No-op device for headless / dedicated-server runs.</summary>
    Null = 1,

    OpenGL = 2,
    Vulkan = 3,
    Direct3D12 = 4,
}
