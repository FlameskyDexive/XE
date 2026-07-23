// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct UnlitMaterialUniformsData
{
#pragma warning disable IDE1006
    public Float2 _Tiling;
    public Float2 _Offset;
    public Float4 _MainColor;
#pragma warning restore IDE1006
}
