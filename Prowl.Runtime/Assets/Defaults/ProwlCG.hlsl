#ifndef PROWL_CG_HLSL
#define PROWL_CG_HLSL

// Minimal HLSL counterpart of ShaderVariables / ProwlCG for dual-backend shaders.
// Layout mirrors the GLSL GlobalUniforms UBO where practical.

cbuffer GlobalUniforms : register(b0)
{
    float4x4 prowl_MatV;
    float4x4 prowl_MatIV;
    float4x4 prowl_MatP;
    float4x4 prowl_MatVP;
    float4x4 prowl_PrevViewProj;
    float4x4 prowl_MatIP;
    float4x4 prowl_MatIVP;
    float4x4 prowl_MatVP_NonJittered;

    float3 _WorldSpaceCameraPos;
    float _padding0;

    float4 _ProjectionParams;
    float4 _ScreenParams;
    float2 _CameraJitter;
    float2 _CameraPreviousJitter;

    float4 _Time;
    float4 _SinTime;
    float4 _CosTime;
    float4 prowl_DeltaTime;
};

cbuffer ObjectUniforms : register(b1)
{
    float4x4 prowl_ObjectToWorld;
    float4x4 prowl_WorldToObject;
    float4x4 prowl_PrevObjectToWorld;
    int _ObjectID;
    float3 _objectPadding;
};

#define PROWL_MATRIX_V prowl_MatV
#define PROWL_MATRIX_VP_PREVIOUS prowl_PrevViewProj
#define PROWL_MATRIX_I_V prowl_MatIV
#define PROWL_MATRIX_P prowl_MatP
#define PROWL_MATRIX_VP prowl_MatVP
#define PROWL_MATRIX_I_P prowl_MatIP
#define PROWL_MATRIX_I_VP prowl_MatIVP
#define PROWL_MATRIX_VP_NONJITTERED prowl_MatVP_NonJittered
#define PROWL_MATRIX_M prowl_ObjectToWorld
#define PROWL_MATRIX_M_PREVIOUS prowl_PrevObjectToWorld

float4x4 GetModelMatrix() { return prowl_ObjectToWorld; }

float4 TransformClip(float3 objectPos)
{
    return mul(PROWL_MATRIX_VP, mul(PROWL_MATRIX_M, float4(objectPos, 1.0)));
}

float3 TransformPosition(float3 objectPos)
{
    return mul(PROWL_MATRIX_M, float4(objectPos, 1.0)).xyz;
}

float3 TransformDirection(float3 dir)
{
    return normalize(mul((float3x3)PROWL_MATRIX_M, dir));
}

float3 linearToGammaSpace(float3 lin)
{
    return max(1.055 * pow(max(lin, 0.0.xxx), 0.416666667.xxx) - 0.055.xxx, 0.0.xxx);
}

float3 gammaToLinearSpace(float3 gamma)
{
    return gamma * (gamma * (gamma * 0.305306011 + 0.682171111) + 0.012522878);
}

float linearizeDepth(float depth, float nearZ, float farZ)
{
    float z = depth * 2.0 - 1.0;
    return (2.0 * nearZ * farZ) / (farZ + nearZ - z * (farZ - nearZ));
}

float linearizeDepthFromProjection(float depth)
{
    return linearizeDepth(depth, _ProjectionParams.y, _ProjectionParams.z);
}

float4 EncodeViewNormal(float3 worldNormal)
{
    float3 viewN = normalize(mul((float3x3)PROWL_MATRIX_V, worldNormal));
    return float4(viewN * 0.5 + 0.5, 1.0);
}

float3 ApplyFog(float3 color, float3 worldPos)
{
    // Stub: full fog lives in Lighting.glsl; keep identity until HLSL lighting is ported.
    return color;
}

float4 GetInstanceColor()
{
    return float4(1, 1, 1, 1);
}

float3 GetMorphedNormal(float3 n) { return n; }
float3 GetMorphedTangent(float3 t) { return t; }

#endif
