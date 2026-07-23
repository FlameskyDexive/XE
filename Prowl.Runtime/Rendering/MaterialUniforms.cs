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

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct StandardMaterialUniformsData
{
#pragma warning disable IDE1006
    public Float2 _Tiling;
    public Float2 _Offset;
    public Float4 _MainColor;
    public float _EmissionIntensity;
    public float _AlphaCutoff;
    public float _Parallax;
    public int _ParallaxSteps;
    public float _TranslucencyStrength;
    public float _ScatteringPower;
    public float _ScatteringDistortion;
    public float _ScatteringScale;
#pragma warning restore IDE1006
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct CutoutMaterialUniformsData
{
#pragma warning disable IDE1006
    public Float2 _Tiling;
    public Float2 _Offset;
    public Float4 _MainColor;
    public float _AlphaCutoff;
#pragma warning restore IDE1006
    public Float3 Padding;
}

internal static class MaterialUniformPacking
{
    public static UnlitMaterialUniformsData PackUnlit(PropertyState? properties, Resources.Shader? shader)
    {
        UnlitMaterialUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
                ApplyCommonDefault(defaults[i], ref data._Tiling, ref data._Offset, ref data._MainColor);
        }

        ApplyCommonOverrides(properties, ref data._Tiling, ref data._Offset, ref data._MainColor);
        return data;
    }

    public static StandardMaterialUniformsData PackStandard(PropertyState? properties, Resources.Shader? shader)
    {
        StandardMaterialUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                ApplyCommonDefault(property, ref data._Tiling, ref data._Offset, ref data._MainColor);
                if (property.Name == "_EmissionIntensity")
                    data._EmissionIntensity = property.Value.X;
                else if (property.Name == "_AlphaCutoff")
                    data._AlphaCutoff = property.Value.X;
                else if (property.Name == "_Parallax")
                    data._Parallax = property.Value.X;
                else if (property.Name == "_ParallaxSteps")
                    data._ParallaxSteps = (int)property.Value.X;
                else if (property.Name == "_TranslucencyStrength")
                    data._TranslucencyStrength = property.Value.X;
                else if (property.Name == "_ScatteringPower")
                    data._ScatteringPower = property.Value.X;
                else if (property.Name == "_ScatteringDistortion")
                    data._ScatteringDistortion = property.Value.X;
                else if (property.Name == "_ScatteringScale")
                    data._ScatteringScale = property.Value.X;
            }
        }

        ApplyCommonOverrides(properties, ref data._Tiling, ref data._Offset, ref data._MainColor);
        if (properties != null)
        {
            if (properties._floats.TryGetValue("_EmissionIntensity", out float emissionIntensity))
                data._EmissionIntensity = emissionIntensity;
            if (properties._floats.TryGetValue("_AlphaCutoff", out float alphaCutoff))
                data._AlphaCutoff = alphaCutoff;
            if (properties._floats.TryGetValue("_Parallax", out float parallax))
                data._Parallax = parallax;
            if (properties._ints.TryGetValue("_ParallaxSteps", out int parallaxSteps))
                data._ParallaxSteps = parallaxSteps;
            if (properties._floats.TryGetValue("_TranslucencyStrength", out float translucencyStrength))
                data._TranslucencyStrength = translucencyStrength;
            if (properties._floats.TryGetValue("_ScatteringPower", out float scatteringPower))
                data._ScatteringPower = scatteringPower;
            if (properties._floats.TryGetValue("_ScatteringDistortion", out float scatteringDistortion))
                data._ScatteringDistortion = scatteringDistortion;
            if (properties._floats.TryGetValue("_ScatteringScale", out float scatteringScale))
                data._ScatteringScale = scatteringScale;
        }
        return data;
    }

    public static CutoutMaterialUniformsData PackCutout(PropertyState? properties, Resources.Shader? shader)
    {
        CutoutMaterialUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                ApplyCommonDefault(property, ref data._Tiling, ref data._Offset, ref data._MainColor);
                if (property.Name == "_AlphaCutoff")
                    data._AlphaCutoff = property.Value.X;
            }
        }

        ApplyCommonOverrides(properties, ref data._Tiling, ref data._Offset, ref data._MainColor);
        if (properties != null && properties._floats.TryGetValue("_AlphaCutoff", out float alphaCutoff))
            data._AlphaCutoff = alphaCutoff;
        return data;
    }

    private static void ApplyCommonDefault(
        Rendering.Shaders.ShaderProperty property,
        ref Float2 tiling,
        ref Float2 offset,
        ref Float4 mainColor)
    {
        if (property.Name == "_Tiling")
            tiling = new Float2(property.Value.X, property.Value.Y);
        else if (property.Name == "_Offset")
            offset = new Float2(property.Value.X, property.Value.Y);
        else if (property.Name == "_MainColor")
            mainColor = property.Value;
    }

    private static void ApplyCommonOverrides(
        PropertyState? properties,
        ref Float2 tiling,
        ref Float2 offset,
        ref Float4 mainColor)
    {
        if (properties == null)
            return;

        if (properties._vectors2.TryGetValue("_Tiling", out Float2 tilingOverride))
            tiling = tilingOverride;
        if (properties._vectors2.TryGetValue("_Offset", out Float2 offsetOverride))
            offset = offsetOverride;
        if (properties._vectors4.TryGetValue("_MainColor", out Float4 vectorColor))
            mainColor = vectorColor;
        else if (properties._colors.TryGetValue("_MainColor", out Color color))
            mainColor = new Float4(color.R, color.G, color.B, color.A);
    }
}
