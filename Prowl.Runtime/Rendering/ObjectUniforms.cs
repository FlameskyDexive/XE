// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct ObjectUniformsData
{
#pragma warning disable IDE1006
    public Float4x4 prowl_ObjectToWorld;
    public Float4x4 prowl_WorldToObject;
    public Float4x4 prowl_PrevObjectToWorld;
    public int _ObjectID;
    public Float3 _objectPadding;
#pragma warning restore IDE1006

    public static ObjectUniformsData Identity => new()
    {
        prowl_ObjectToWorld = Float4x4.Identity,
        prowl_WorldToObject = Float4x4.Identity,
        prowl_PrevObjectToWorld = Float4x4.Identity,
    };
}
