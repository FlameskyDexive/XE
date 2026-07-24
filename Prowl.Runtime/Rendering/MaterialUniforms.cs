// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
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

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct GradientSkyboxUniformsData
{
#pragma warning disable IDE1006
    public Float4 _TopColor;
    public Float4 _BottomColor;
    public float _Exponent;
#pragma warning restore IDE1006
    public Float3 Padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct ProceduralSkyboxUniformsData
{
    public Float2 Resolution;
    public float fogDensity;
    public float Padding0;
#pragma warning disable IDE1006
    public Float3 _SunDir;
#pragma warning restore IDE1006
    public float Padding1;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct TonemapperUniformsData
{
    public float Contrast;
    public float Saturation;
    public Float2 Padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct UIBlurUniformsData
{
#pragma warning disable IDE1006
    public float _Offset;
#pragma warning restore IDE1006
    public Float3 Padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct FXAAUniformsData
{
#pragma warning disable IDE1006
    public Float2 _Resolution;
    public float _EdgeThresholdMin;
    public float _EdgeThresholdMax;
    public float _SubpixelQuality;
#pragma warning restore IDE1006
    public Float3 Padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct BloomThresholdUniformsData
{
#pragma warning disable IDE1006
    public float _Threshold;
#pragma warning restore IDE1006
    public Float3 Padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct BloomCompositeUniformsData
{
#pragma warning disable IDE1006
    public float _Intensity;
#pragma warning restore IDE1006
    public Float3 Padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct MotionBlurUniformsData
{
#pragma warning disable IDE1006
    public Float2 _Resolution;
    public float _Intensity;
    public int _Samples;
    public float _MaxBlurRadius;
#pragma warning restore IDE1006
    public Float3 Padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct AutoExposureAdaptUniformsData
{
#pragma warning disable IDE1006
    public float _AdaptSpeedUp;
    public float _AdaptSpeedDown;
    public float _HistoryValid;
#pragma warning restore IDE1006
    public float Padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct AutoExposureApplyUniformsData
{
#pragma warning disable IDE1006
    public float _ExposureComp;
    public float _MinExposure;
    public float _MaxExposure;
#pragma warning restore IDE1006
    public float Padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct TAAUniformsData
{
#pragma warning disable IDE1006
    public Float2 _Resolution;
    public Float2 _Jitter;
    public float _HistoryValid;
    public float _BlendFactor;
    public float _MotionScale;
    public float _Sharpness;
#pragma warning restore IDE1006
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct GTAOCalculateUniformsData
{
#pragma warning disable IDE1006
    public int _Slices;
    public int _DirectionSamples;
    public float _Radius;
    public float _Intensity;
    public Float2 _NoiseScale;
    public Float2 _JitterOffset;
#pragma warning restore IDE1006
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct GridUniformsData
{
#pragma warning disable IDE1006
    public Float4 _GridColor;
    public float _PrimaryGridSize;
    public float _SecondaryGridSize;
    public float _LineWidth;
    public float _Falloff;
    public float _MaxDist;
#pragma warning restore IDE1006
    public Float3 Padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct UIVertexUniformsData
{
    public Float4x4 projection;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
internal struct UIFragmentUniformsData
{
    public Float4x4 scissorMat;
    public Float2 scissorExt;
    public Float2 Padding0;
    public Float4x4 brushMat;
    public int brushType;
    public Float3 Padding1;
    public Float4 brushColor1;
    public Float4 brushColor2;
    public Float4 brushParams;
    public Float2 brushParams2;
    public Float2 Padding2;
    public Float4x4 brushTextureMat;
    public float dpiScale;
    public Float2 viewportSize;
    public float backdropBlurAmount;
    public int backdropFlipY;
    public Float3 Padding3;
}

internal static class UIUniformPacking
{
    public static bool TrySetFloat(ref UIFragmentUniformsData data, string name, float value)
    {
        if (name == "dpiScale")
            data.dpiScale = value;
        else if (name == "backdropBlurAmount")
            data.backdropBlurAmount = value;
        else
            return false;
        return true;
    }

    public static bool TrySetInt(ref UIFragmentUniformsData data, string name, int value)
    {
        if (name == "brushType")
            data.brushType = value;
        else if (name == "backdropFlipY")
            data.backdropFlipY = value;
        else
            return false;
        return true;
    }

    public static bool TrySetVector2(ref UIFragmentUniformsData data, string name, Float2 value)
    {
        if (name == "scissorExt")
            data.scissorExt = value;
        else if (name == "brushParams2")
            data.brushParams2 = value;
        else if (name == "viewportSize")
            data.viewportSize = value;
        else
            return false;
        return true;
    }

    public static bool TrySetVector4(ref UIFragmentUniformsData data, string name, Float4 value)
    {
        if (name == "brushColor1")
            data.brushColor1 = value;
        else if (name == "brushColor2")
            data.brushColor2 = value;
        else if (name == "brushParams")
            data.brushParams = value;
        else
            return false;
        return true;
    }

    public static bool TrySetMatrix(ref UIFragmentUniformsData data, string name, Float4x4 value)
    {
        if (name == "scissorMat")
            data.scissorMat = value;
        else if (name == "brushMat")
            data.brushMat = value;
        else if (name == "brushTextureMat")
            data.brushTextureMat = value;
        else
            return false;
        return true;
    }
}

internal static class MaterialUniformPacking
{
    public static void ApplyTextureBindings(
        Dictionary<string, GraphicsTexture> bindings,
        PropertyState? properties,
        Resources.Shader? shader)
    {
        ClearTextureBindings(bindings);

        Resources.Texture2D? mainTexture = GetTextureOverride(properties, "_MainTex");
        Resources.Texture2D? normalTexture = GetTextureOverride(properties, "_NormalTex");
        Resources.Texture2D? surfaceTexture = GetTextureOverride(properties, "_SurfaceTex");
        Resources.Texture2D? emissionTexture = GetTextureOverride(properties, "_EmissionTex");
        Resources.Texture2D? bloomTexture = GetTextureOverride(properties, "_BloomTex");
        Resources.Texture2D? motionVectorsTexture = GetTextureOverride(properties, "_MotionVectorsTex");
        Resources.Texture2D? adaptedTexture = GetTextureOverride(properties, "_AdaptedTex");
        Resources.Texture2D? historyTexture = GetTextureOverride(properties, "_HistoryTex");
        Resources.Texture2D? noiseTexture = GetTextureOverride(properties, "_Noise");
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "_MainTex" && mainTexture == null)
                    mainTexture = property.Texture2DValue;
                else if (property.Name == "_NormalTex" && normalTexture == null)
                    normalTexture = property.Texture2DValue;
                else if (property.Name == "_SurfaceTex" && surfaceTexture == null)
                    surfaceTexture = property.Texture2DValue;
                else if (property.Name == "_EmissionTex" && emissionTexture == null)
                    emissionTexture = property.Texture2DValue;
                else if (property.Name == "_BloomTex" && bloomTexture == null)
                    bloomTexture = property.Texture2DValue;
                else if (property.Name == "_MotionVectorsTex" && motionVectorsTexture == null)
                    motionVectorsTexture = property.Texture2DValue;
                else if (property.Name == "_AdaptedTex" && adaptedTexture == null)
                    adaptedTexture = property.Texture2DValue;
                else if (property.Name == "_HistoryTex" && historyTexture == null)
                    historyTexture = property.Texture2DValue;
                else if (property.Name == "_Noise" && noiseTexture == null)
                    noiseTexture = property.Texture2DValue;
            }
        }

        AddTextureBinding(bindings, "_MainTex", mainTexture);
        AddTextureBinding(bindings, "_NormalTex", normalTexture);
        AddTextureBinding(bindings, "_SurfaceTex", surfaceTexture);
        AddTextureBinding(bindings, "_EmissionTex", emissionTexture);
        AddTextureBinding(bindings, "_BloomTex", bloomTexture);
        AddTextureBinding(bindings, "_MotionVectorsTex", motionVectorsTexture);
        AddTextureBinding(bindings, "_AdaptedTex", adaptedTexture);
        AddTextureBinding(bindings, "_HistoryTex", historyTexture);
        AddTextureBinding(bindings, "_Noise", noiseTexture);
    }

    public static void ClearTextureBindings(Dictionary<string, GraphicsTexture> bindings)
    {
        bindings.Remove("_MainTex");
        bindings.Remove("_NormalTex");
        bindings.Remove("_SurfaceTex");
        bindings.Remove("_EmissionTex");
        bindings.Remove("_BloomTex");
        bindings.Remove("_MotionVectorsTex");
        bindings.Remove("_AdaptedTex");
        bindings.Remove("_HistoryTex");
        bindings.Remove("_Noise");
    }

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

    public static GradientSkyboxUniformsData PackGradientSkybox(PropertyState? properties, Resources.Shader? shader)
    {
        GradientSkyboxUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "_TopColor")
                    data._TopColor = property.Value;
                else if (property.Name == "_BottomColor")
                    data._BottomColor = property.Value;
                else if (property.Name == "_Exponent")
                    data._Exponent = property.Value.X;
            }
        }

        if (properties != null)
        {
            ApplyColorOverride(properties, "_TopColor", ref data._TopColor);
            ApplyColorOverride(properties, "_BottomColor", ref data._BottomColor);
            if (properties._floats.TryGetValue("_Exponent", out float exponent))
                data._Exponent = exponent;
        }
        return data;
    }

    public static ProceduralSkyboxUniformsData PackProceduralSkybox(PropertyState? properties, Resources.Shader? shader)
    {
        ProceduralSkyboxUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "Resolution")
                    data.Resolution = new Float2(property.Value.X, property.Value.Y);
                else if (property.Name == "fogDensity")
                    data.fogDensity = property.Value.X;
                else if (property.Name == "_SunDir")
                    data._SunDir = new Float3(property.Value.X, property.Value.Y, property.Value.Z);
            }
        }

        if (properties != null)
        {
            if (properties._vectors2.TryGetValue("Resolution", out Float2 resolution))
                data.Resolution = resolution;
            if (properties._floats.TryGetValue("fogDensity", out float fogDensity))
                data.fogDensity = fogDensity;
            if (properties._vectors3.TryGetValue("_SunDir", out Float3 sunDirection))
                data._SunDir = sunDirection;
            else if (properties._vectors4.TryGetValue("_SunDir", out Float4 sunDirection4))
                data._SunDir = new Float3(sunDirection4.X, sunDirection4.Y, sunDirection4.Z);
        }
        return data;
    }

    public static TonemapperUniformsData PackTonemapper(PropertyState? properties, Resources.Shader? shader)
    {
        TonemapperUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "Contrast")
                    data.Contrast = property.Value.X;
                else if (property.Name == "Saturation")
                    data.Saturation = property.Value.X;
            }
        }

        if (properties != null)
        {
            if (properties._floats.TryGetValue("Contrast", out float contrast))
                data.Contrast = contrast;
            if (properties._floats.TryGetValue("Saturation", out float saturation))
                data.Saturation = saturation;
        }
        return data;
    }

    public static UIBlurUniformsData PackUIBlur(PropertyState? properties, Resources.Shader? shader)
    {
        UIBlurUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "_Offset")
                    data._Offset = property.Value.X;
            }
        }

        if (properties != null && properties._floats.TryGetValue("_Offset", out float offset))
            data._Offset = offset;
        return data;
    }

    public static FXAAUniformsData PackFXAA(PropertyState? properties, Resources.Shader? shader)
    {
        FXAAUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "_Resolution")
                    data._Resolution = new Float2(property.Value.X, property.Value.Y);
                else if (property.Name == "_EdgeThresholdMin")
                    data._EdgeThresholdMin = property.Value.X;
                else if (property.Name == "_EdgeThresholdMax")
                    data._EdgeThresholdMax = property.Value.X;
                else if (property.Name == "_SubpixelQuality")
                    data._SubpixelQuality = property.Value.X;
            }
        }

        if (properties != null)
        {
            if (properties._vectors2.TryGetValue("_Resolution", out Float2 resolution))
                data._Resolution = resolution;
            if (properties._floats.TryGetValue("_EdgeThresholdMin", out float thresholdMin))
                data._EdgeThresholdMin = thresholdMin;
            if (properties._floats.TryGetValue("_EdgeThresholdMax", out float thresholdMax))
                data._EdgeThresholdMax = thresholdMax;
            if (properties._floats.TryGetValue("_SubpixelQuality", out float subpixelQuality))
                data._SubpixelQuality = subpixelQuality;
        }
        return data;
    }

    public static BloomThresholdUniformsData PackBloomThreshold(PropertyState? properties, Resources.Shader? shader)
    {
        BloomThresholdUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "_Threshold")
                    data._Threshold = property.Value.X;
            }
        }

        if (properties != null && properties._floats.TryGetValue("_Threshold", out float threshold))
            data._Threshold = threshold;
        return data;
    }

    public static BloomCompositeUniformsData PackBloomComposite(PropertyState? properties, Resources.Shader? shader)
    {
        BloomCompositeUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "_Intensity")
                    data._Intensity = property.Value.X;
            }
        }

        if (properties != null && properties._floats.TryGetValue("_Intensity", out float intensity))
            data._Intensity = intensity;
        return data;
    }

    public static MotionBlurUniformsData PackMotionBlur(PropertyState? properties, Resources.Shader? shader)
    {
        MotionBlurUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "_Resolution")
                    data._Resolution = new Float2(property.Value.X, property.Value.Y);
                else if (property.Name == "_Intensity")
                    data._Intensity = property.Value.X;
                else if (property.Name == "_Samples")
                    data._Samples = (int)property.Value.X;
                else if (property.Name == "_MaxBlurRadius")
                    data._MaxBlurRadius = property.Value.X;
            }
        }

        if (properties != null)
        {
            if (properties._vectors2.TryGetValue("_Resolution", out Float2 resolution))
                data._Resolution = resolution;
            if (properties._floats.TryGetValue("_Intensity", out float intensity))
                data._Intensity = intensity;
            if (properties._ints.TryGetValue("_Samples", out int samples))
                data._Samples = samples;
            if (properties._floats.TryGetValue("_MaxBlurRadius", out float maxBlurRadius))
                data._MaxBlurRadius = maxBlurRadius;
        }
        return data;
    }

    public static AutoExposureAdaptUniformsData PackAutoExposureAdapt(PropertyState? properties, Resources.Shader? shader)
    {
        AutoExposureAdaptUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "_AdaptSpeedUp")
                    data._AdaptSpeedUp = property.Value.X;
                else if (property.Name == "_AdaptSpeedDown")
                    data._AdaptSpeedDown = property.Value.X;
                else if (property.Name == "_HistoryValid")
                    data._HistoryValid = property.Value.X;
            }
        }

        if (properties != null)
        {
            if (properties._floats.TryGetValue("_AdaptSpeedUp", out float speedUp))
                data._AdaptSpeedUp = speedUp;
            if (properties._floats.TryGetValue("_AdaptSpeedDown", out float speedDown))
                data._AdaptSpeedDown = speedDown;
            if (properties._floats.TryGetValue("_HistoryValid", out float historyValid))
                data._HistoryValid = historyValid;
        }
        return data;
    }

    public static AutoExposureApplyUniformsData PackAutoExposureApply(PropertyState? properties, Resources.Shader? shader)
    {
        AutoExposureApplyUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "_ExposureComp")
                    data._ExposureComp = property.Value.X;
                else if (property.Name == "_MinExposure")
                    data._MinExposure = property.Value.X;
                else if (property.Name == "_MaxExposure")
                    data._MaxExposure = property.Value.X;
            }
        }

        if (properties != null)
        {
            if (properties._floats.TryGetValue("_ExposureComp", out float exposureCompensation))
                data._ExposureComp = exposureCompensation;
            if (properties._floats.TryGetValue("_MinExposure", out float minExposure))
                data._MinExposure = minExposure;
            if (properties._floats.TryGetValue("_MaxExposure", out float maxExposure))
                data._MaxExposure = maxExposure;
        }
        return data;
    }

    public static TAAUniformsData PackTAA(PropertyState? properties, Resources.Shader? shader)
    {
        TAAUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "_Resolution")
                    data._Resolution = new Float2(property.Value.X, property.Value.Y);
                else if (property.Name == "_Jitter")
                    data._Jitter = new Float2(property.Value.X, property.Value.Y);
                else if (property.Name == "_HistoryValid")
                    data._HistoryValid = property.Value.X;
                else if (property.Name == "_BlendFactor")
                    data._BlendFactor = property.Value.X;
                else if (property.Name == "_MotionScale")
                    data._MotionScale = property.Value.X;
                else if (property.Name == "_Sharpness")
                    data._Sharpness = property.Value.X;
            }
        }

        if (properties != null)
        {
            if (properties._vectors2.TryGetValue("_Resolution", out Float2 resolution))
                data._Resolution = resolution;
            if (properties._vectors2.TryGetValue("_Jitter", out Float2 jitter))
                data._Jitter = jitter;
            if (properties._floats.TryGetValue("_HistoryValid", out float historyValid))
                data._HistoryValid = historyValid;
            if (properties._floats.TryGetValue("_BlendFactor", out float blendFactor))
                data._BlendFactor = blendFactor;
            if (properties._floats.TryGetValue("_MotionScale", out float motionScale))
                data._MotionScale = motionScale;
            if (properties._floats.TryGetValue("_Sharpness", out float sharpness))
                data._Sharpness = sharpness;
        }
        return data;
    }

    public static GTAOCalculateUniformsData PackGTAOCalculate(PropertyState? properties, Resources.Shader? shader)
    {
        GTAOCalculateUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "_Slices")
                    data._Slices = (int)property.Value.X;
                else if (property.Name == "_DirectionSamples")
                    data._DirectionSamples = (int)property.Value.X;
                else if (property.Name == "_Radius")
                    data._Radius = property.Value.X;
                else if (property.Name == "_Intensity")
                    data._Intensity = property.Value.X;
                else if (property.Name == "_NoiseScale")
                    data._NoiseScale = new Float2(property.Value.X, property.Value.Y);
                else if (property.Name == "_JitterOffset")
                    data._JitterOffset = new Float2(property.Value.X, property.Value.Y);
            }
        }

        if (properties != null)
        {
            if (properties._ints.TryGetValue("_Slices", out int slices))
                data._Slices = slices;
            if (properties._ints.TryGetValue("_DirectionSamples", out int directionSamples))
                data._DirectionSamples = directionSamples;
            if (properties._floats.TryGetValue("_Radius", out float radius))
                data._Radius = radius;
            if (properties._floats.TryGetValue("_Intensity", out float intensity))
                data._Intensity = intensity;
            if (properties._vectors2.TryGetValue("_NoiseScale", out Float2 noiseScale))
                data._NoiseScale = noiseScale;
            if (properties._vectors2.TryGetValue("_JitterOffset", out Float2 jitterOffset))
                data._JitterOffset = jitterOffset;
        }
        return data;
    }

    public static GridUniformsData PackGrid(PropertyState? properties, Resources.Shader? shader)
    {
        GridUniformsData data = default;
        Rendering.Shaders.ShaderProperty[]? defaults = shader?.PropertyArray;
        if (defaults != null)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                Rendering.Shaders.ShaderProperty property = defaults[i];
                if (property.Name == "_GridColor")
                    data._GridColor = property.Value;
                else if (property.Name == "_PrimaryGridSize")
                    data._PrimaryGridSize = property.Value.X;
                else if (property.Name == "_SecondaryGridSize")
                    data._SecondaryGridSize = property.Value.X;
                else if (property.Name == "_LineWidth")
                    data._LineWidth = property.Value.X;
                else if (property.Name == "_Falloff")
                    data._Falloff = property.Value.X;
                else if (property.Name == "_MaxDist")
                    data._MaxDist = property.Value.X;
            }
        }

        if (properties != null)
        {
            ApplyColorOverride(properties, "_GridColor", ref data._GridColor);
            if (properties._floats.TryGetValue("_PrimaryGridSize", out float primaryGridSize))
                data._PrimaryGridSize = primaryGridSize;
            if (properties._floats.TryGetValue("_SecondaryGridSize", out float secondaryGridSize))
                data._SecondaryGridSize = secondaryGridSize;
            if (properties._floats.TryGetValue("_LineWidth", out float lineWidth))
                data._LineWidth = lineWidth;
            if (properties._floats.TryGetValue("_Falloff", out float falloff))
                data._Falloff = falloff;
            if (properties._floats.TryGetValue("_MaxDist", out float maxDistance))
                data._MaxDist = maxDistance;
        }
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

    private static Resources.Texture2D? GetTextureOverride(PropertyState? properties, string name)
    {
        if (properties != null && properties._textures.TryGetValue(name, out var texture))
            return texture.Res;
        return null;
    }

    private static void AddTextureBinding(
        Dictionary<string, GraphicsTexture> bindings,
        string name,
        Resources.Texture2D? texture)
    {
        if (texture != null)
            bindings[name] = texture.Handle;
    }

    private static void ApplyColorOverride(PropertyState properties, string name, ref Float4 value)
    {
        if (properties._vectors4.TryGetValue(name, out Float4 vectorColor))
            value = vectorColor;
        else if (properties._colors.TryGetValue(name, out Color color))
            value = new Float4(color.R, color.G, color.B, color.A);
    }
}
