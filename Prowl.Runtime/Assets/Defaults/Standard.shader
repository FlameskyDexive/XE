Shader "Default/Standard"

Properties
{
    _MainTex ("Albedo", Texture2D) = "grid"
    _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    _Tiling ("Tiling", Vector2) = (1.0, 1.0)
    _Offset ("Offset", Vector2) = (0.0, 0.0)

    _NormalTex ("Normal", Texture2D) = "normal"

    _SurfaceTex ("Surface (AO, Roughness, Metallicness)", Texture2D) = "surface"

    _EmissionTex ("Emission", Texture2D) = "emission"
    _EmissionIntensity ("Emission Intensity", Float) = 1.0

    _AlphaCutoff ("Alpha Cutoff", Float) = 0.5

    _ParallaxMap ("Height Map (G)", Texture2D) = "black"
    _Parallax ("Height Scale", Float) = 0.0
    _ParallaxSteps ("POM Steps", Int) = 16

    _TranslucencyMap ("Translucency (B) Occlusion (G)", Texture2D) = "white"
    _TranslucencyStrength ("Translucency Strength", Float) = 0.0
    _ScatteringPower ("Scattering Power", Float) = 0.0
    _ScatteringDistortion ("Scattering Distortion", Float) = 0.5
    _ScatteringScale ("Scattering Scale", Float) = 1.0
}

Pass "Standard"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull Back

	GLSLPROGRAM

		Vertex
		{
            #include "ProwlCG"
            #include "VertexAttributes"

			out vec2 texCoord0;
			out vec3 worldPos;
			out vec4 vColor;
			out vec3 vNormal;
			out vec3 vTangent;
			out vec3 vBitangent;
			out vec2 vLightmapUV2;

			uniform vec2 _Tiling;
			uniform vec2 _Offset;

			void main()
			{
				gl_Position = TransformClip(vertexPosition);
				texCoord0 = vertexTexCoord0 * _Tiling + _Offset;
				vLightmapUV2 = vertexTexCoord1; // raw UV2; scale/offset applied in the fragment
				worldPos = TransformPosition(vertexPosition);
				vColor = GetInstanceColor();
				vNormal = TransformDirection(GetMorphedNormal(vertexNormal));
#ifdef HAS_TANGENTS
				vTangent = TransformDirection(GetMorphedTangent(vertexTangent.xyz));
				vBitangent = cross(vTangent, vNormal) * vertexTangent.w;
				// Guard against degenerate tangent frames (parallel normal/tangent)
				if (dot(vBitangent, vBitangent) < 0.000001) {
					vTangent = abs(vNormal.y) < 0.999 ? normalize(cross(vNormal, vec3(0,1,0))) : normalize(cross(vNormal, vec3(1,0,0)));
					vBitangent = cross(vTangent, vNormal) * vertexTangent.w;
				}
#endif
			}
		}

		Fragment
		{
            #include "StandardSurface"

			layout (location = 0) out vec4 fragColor;

			in vec2 texCoord0;
			in vec3 worldPos;
			in vec4 vColor;
			in vec3 vNormal;
			in vec3 vTangent;
			in vec3 vBitangent;
			in vec2 vLightmapUV2;

			uniform sampler2D _MainTex;
			uniform sampler2D _NormalTex;
			uniform sampler2D _SurfaceTex;
			uniform sampler2D _EmissionTex;
			uniform float _EmissionIntensity;
			uniform vec4 _MainColor;
			uniform float _AlphaCutoff;

			uniform sampler2D _ParallaxMap;
			uniform float _Parallax;
			uniform int _ParallaxSteps;

			uniform sampler2D _TranslucencyMap;
			uniform float _TranslucencyStrength;
			uniform float _ScatteringPower;
			uniform float _ScatteringDistortion;
			uniform float _ScatteringScale;

			void main()
			{
				vec4 result = StandardSurface(texCoord0, worldPos, vColor,
				    vNormal, vTangent, vBitangent,
				    _MainTex, _NormalTex, _SurfaceTex, _EmissionTex,
				    _EmissionIntensity, _MainColor,
				    _ParallaxMap, _Parallax, _ParallaxSteps,
				    _TranslucencyMap, _TranslucencyStrength,
				    _ScatteringPower, _ScatteringDistortion, _ScatteringScale,
				    vLightmapUV2);

				// Alpha cutout discard below threshold, output fully opaque
				if (result.a < _AlphaCutoff)
				    discard;

				fragColor = vec4(result.rgb, 1.0);
			}
		}
	ENDGLSL

	HLSLPROGRAM
		Vertex
		{
			#include "ProwlCG"

			cbuffer StandardMaterial : register(b2)
			{
				float2 _Tiling;
				float2 _Offset;
				float4 _MainColor;
				float _EmissionIntensity;
				float _AlphaCutoff;
				float _Parallax;
				int _ParallaxSteps;
				float _TranslucencyStrength;
				float _ScatteringPower;
				float _ScatteringDistortion;
				float _ScatteringScale;
			};

			struct VSInput
			{
				float3 vertexPosition : POSITION;
				float2 vertexTexCoord0 : TEXCOORD0;
				float2 vertexTexCoord1 : TEXCOORD1;
				float3 vertexNormal : NORMAL;
				float4 vertexTangent : TANGENT;
			};

			struct VSOutput
			{
				float4 position : SV_Position;
				float2 texCoord0 : TEXCOORD0;
				float3 worldPos : TEXCOORD1;
				float4 vColor : COLOR0;
				float3 vNormal : TEXCOORD2;
				float3 vTangent : TEXCOORD3;
				float3 vBitangent : TEXCOORD4;
				float2 vLightmapUV2 : TEXCOORD5;
			};

			VSOutput main(VSInput input)
			{
				VSOutput o;
				o.position = TransformClip(input.vertexPosition);
				o.texCoord0 = input.vertexTexCoord0 * _Tiling + _Offset;
				o.vLightmapUV2 = input.vertexTexCoord1;
				o.worldPos = TransformPosition(input.vertexPosition);
				o.vColor = GetInstanceColor();
				o.vNormal = TransformDirection(GetMorphedNormal(input.vertexNormal));
				o.vTangent = TransformDirection(GetMorphedTangent(input.vertexTangent.xyz));
				o.vBitangent = cross(o.vTangent, o.vNormal) * input.vertexTangent.w;
				return o;
			}
		}

		Fragment
		{
			#include "ProwlCG"

			cbuffer StandardMaterial : register(b2)
			{
				float2 _Tiling;
				float2 _Offset;
				float4 _MainColor;
				float _EmissionIntensity;
				float _AlphaCutoff;
				float _Parallax;
				int _ParallaxSteps;
				float _TranslucencyStrength;
				float _ScatteringPower;
				float _ScatteringDistortion;
				float _ScatteringScale;
			};

			[[vk::binding(0)]] Texture2D _MainTex : register(t0);
			[[vk::binding(0)]] SamplerState _MainTexSampler : register(s0);
			[[vk::binding(1)]] Texture2D _NormalTex : register(t1);
			[[vk::binding(1)]] SamplerState _NormalTexSampler : register(s1);
			[[vk::binding(2)]] Texture2D _SurfaceTex : register(t2);
			[[vk::binding(2)]] SamplerState _SurfaceTexSampler : register(s2);
			[[vk::binding(3)]] Texture2D _EmissionTex : register(t3);
			[[vk::binding(3)]] SamplerState _EmissionTexSampler : register(s3);

			struct PSInput
			{
				float4 position : SV_Position;
				float2 texCoord0 : TEXCOORD0;
				float3 worldPos : TEXCOORD1;
				float4 vColor : COLOR0;
				float3 vNormal : TEXCOORD2;
				float3 vTangent : TEXCOORD3;
				float3 vBitangent : TEXCOORD4;
				float2 vLightmapUV2 : TEXCOORD5;
			};

			float4 main(PSInput input) : SV_Target
			{
				// Stage-C dual-source stub: albedo * tint until StandardSurface.hlsl lands.
				float4 albedo = _MainTex.Sample(_MainTexSampler, input.texCoord0) * input.vColor * _MainColor;
				if (albedo.a < _AlphaCutoff)
					discard;
				float3 emission = _EmissionTex.Sample(_EmissionTexSampler, input.texCoord0).rgb * _EmissionIntensity;
				float3 color = gammaToLinearSpace(albedo.rgb) + emission;
				color = ApplyFog(color, input.worldPos);
				return float4(color, 1.0);
			}
		}
	ENDHLSL
}

Pass "Prepass"
{
    Tags { "LightMode" = "Prepass" }
    Cull Back
    ZWrite On

	GLSLPROGRAM

		Vertex
		{
            #include "ProwlCG"
            #include "VertexAttributes"

			out vec3 vNormal;
			out vec3 vTangent;
			out vec3 vBitangent;
			out vec2 texCoord0;
			out vec4 vCurrClipNJ;
			out vec4 vPrevClip;

			uniform vec2 _Tiling;
			uniform vec2 _Offset;

			void main()
			{
				gl_Position = TransformClip(vertexPosition); // jittered, for raster + depth
				vNormal = TransformDirection(GetMorphedNormal(vertexNormal));
#ifdef HAS_TANGENTS
				vTangent = TransformDirection(GetMorphedTangent(vertexTangent.xyz));
				vBitangent = cross(vTangent, vNormal) * vertexTangent.w;
				if (dot(vBitangent, vBitangent) < 0.000001) {
					vTangent = abs(vNormal.y) < 0.999 ? normalize(cross(vNormal, vec3(0,1,0))) : normalize(cross(vNormal, vec3(1,0,0)));
					vBitangent = cross(vTangent, vNormal) * vertexTangent.w;
				}
#endif
				texCoord0 = vertexTexCoord0 * _Tiling + _Offset;

				// Jitter-free current + previous clip positions for motion vectors.
				vec4 worldPos = GetModelMatrix() * vec4(vertexPosition, 1.0);
				vCurrClipNJ = PROWL_MATRIX_VP_NONJITTERED * worldPos;
				vec4 prevWorldPos = PROWL_MATRIX_M_PREVIOUS * vec4(vertexPosition, 1.0);
				vPrevClip = PROWL_MATRIX_VP_PREVIOUS * prevWorldPos;
			}
		}

		Fragment
		{
            #include "ProwlCG"

			layout (location = 0) out vec4 normalOut;
			layout (location = 1) out vec4 motionRM;

			in vec3 vNormal;
			in vec3 vTangent;
			in vec3 vBitangent;
			in vec2 texCoord0;
			in vec4 vCurrClipNJ;
			in vec4 vPrevClip;

			uniform sampler2D _NormalTex;
			uniform sampler2D _MainTex;
			uniform sampler2D _SurfaceTex;
			uniform vec4 _MainColor;
			uniform float _AlphaCutoff;

			void main()
			{
				// Alpha cutoff for cutout mode
				if (_AlphaCutoff > 0.0)
				{
				    float alpha = texture(_MainTex, texCoord0).a * _MainColor.a;
				    if (alpha < _AlphaCutoff) discard;
				}

                vec3 worldNormal = ApplyNormalMap(_NormalTex, texCoord0, vNormal, vTangent, vBitangent);
				normalOut = EncodeViewNormal(worldNormal);

				// Motion vectors (jitter-free) + packed roughness/metallic (_SurfaceTex G/B).
				vec2 currNDC = (vCurrClipNJ.xy / vCurrClipNJ.w) * 0.5 + 0.5;
				vec2 prevNDC = (vPrevClip.xy / vPrevClip.w) * 0.5 + 0.5;
				vec4 surface = texture(_SurfaceTex, texCoord0);
				motionRM = vec4(currNDC - prevNDC, surface.g, surface.b);
			}
		}
	ENDGLSL

	HLSLPROGRAM
		Vertex
		{
			#include "ProwlCG"

			cbuffer PrepassMaterial : register(b2)
			{
				float2 _Tiling;
				float2 _Offset;
				float4 _MainColor;
				float _AlphaCutoff;
				float3 _PrepassMaterialPadding;
			};

			struct VSInput
			{
				float3 vertexPosition : POSITION;
				float2 vertexTexCoord0 : TEXCOORD0;
				float3 vertexNormal : NORMAL;
				float4 vertexTangent : TANGENT;
			};

			struct VSOutput
			{
				float4 position : SV_Position;
				float3 vNormal : TEXCOORD0;
				float3 vTangent : TEXCOORD1;
				float3 vBitangent : TEXCOORD2;
				float2 texCoord0 : TEXCOORD3;
				float4 vCurrClipNJ : TEXCOORD4;
				float4 vPrevClip : TEXCOORD5;
			};

			VSOutput main(VSInput input)
			{
				VSOutput o;
				o.position = TransformClip(input.vertexPosition);
				o.vNormal = TransformDirection(GetMorphedNormal(input.vertexNormal));
				o.vTangent = TransformDirection(GetMorphedTangent(input.vertexTangent.xyz));
				o.vBitangent = cross(o.vTangent, o.vNormal) * input.vertexTangent.w;
				o.texCoord0 = input.vertexTexCoord0 * _Tiling + _Offset;
				float4 worldPos = mul(GetModelMatrix(), float4(input.vertexPosition, 1.0));
				o.vCurrClipNJ = mul(PROWL_MATRIX_VP_NONJITTERED, worldPos);
				float4 prevWorldPos = mul(PROWL_MATRIX_M_PREVIOUS, float4(input.vertexPosition, 1.0));
				o.vPrevClip = mul(PROWL_MATRIX_VP_PREVIOUS, prevWorldPos);
				return o;
			}
		}

		Fragment
		{
			#include "ProwlCG"

			cbuffer PrepassMaterial : register(b2)
			{
				float2 _Tiling;
				float2 _Offset;
				float4 _MainColor;
				float _AlphaCutoff;
				float3 _PrepassMaterialPadding;
			};

			[[vk::binding(0)]] Texture2D _MainTex : register(t0);
			[[vk::binding(0)]] SamplerState _MainTexSampler : register(s0);
			[[vk::binding(1)]] Texture2D _NormalTex : register(t1);
			[[vk::binding(1)]] SamplerState _NormalTexSampler : register(s1);
			[[vk::binding(2)]] Texture2D _SurfaceTex : register(t2);
			[[vk::binding(2)]] SamplerState _SurfaceTexSampler : register(s2);

			struct PSInput
			{
				float4 position : SV_Position;
				float3 vNormal : TEXCOORD0;
				float3 vTangent : TEXCOORD1;
				float3 vBitangent : TEXCOORD2;
				float2 texCoord0 : TEXCOORD3;
				float4 vCurrClipNJ : TEXCOORD4;
				float4 vPrevClip : TEXCOORD5;
			};

			struct PSOutput
			{
				float4 normalOut : SV_Target0;
				float4 motionRM : SV_Target1;
			};

			PSOutput main(PSInput input)
			{
				PSOutput o;
				if (_AlphaCutoff > 0.0)
				{
					float alpha = _MainTex.Sample(_MainTexSampler, input.texCoord0).a * _MainColor.a;
					if (alpha < _AlphaCutoff)
						discard;
				}
				o.normalOut = EncodeViewNormal(normalize(input.vNormal));
				float2 currNDC = (input.vCurrClipNJ.xy / input.vCurrClipNJ.w) * 0.5 + 0.5;
				float2 prevNDC = (input.vPrevClip.xy / input.vPrevClip.w) * 0.5 + 0.5;
				float4 surface = _SurfaceTex.Sample(_SurfaceTexSampler, input.texCoord0);
				o.motionRM = float4(currNDC - prevNDC, surface.g, surface.b);
				return o;
			}
		}
	ENDHLSL
}

Pass "StandardShadow"
{
    Tags { "LightMode" = "ShadowCaster" }
    Cull Back

	GLSLPROGRAM

		Vertex
		{
            #include "ProwlCG"
            #include "VertexAttributes"

			out vec2 texCoord0;

			uniform vec2 _Tiling;
			uniform vec2 _Offset;

			void main()
			{
				gl_Position = TransformClip(vertexPosition);
				texCoord0 = vertexTexCoord0 * _Tiling + _Offset;
			}
		}

		Fragment
		{
            #include "ProwlCG"

			in vec2 texCoord0;
			uniform sampler2D _MainTex;
			uniform vec4 _MainColor;
			uniform float _AlphaCutoff;

			void main()
			{
				if (_AlphaCutoff > 0.0)
				{
				    float alpha = texture(_MainTex, texCoord0).a * _MainColor.a;
				    if (alpha < _AlphaCutoff) discard;
				}
                gl_FragDepth = gl_FragCoord.z;
			}
		}
	ENDGLSL

	HLSLPROGRAM
		Vertex
		{
			#include "ProwlCG"

			cbuffer ShadowMaterial : register(b2)
			{
				float2 _Tiling;
				float2 _Offset;
				float4 _MainColor;
				float _AlphaCutoff;
				float3 _ShadowMaterialPadding;
			};

			struct VSInput
			{
				float3 vertexPosition : POSITION;
				float2 vertexTexCoord0 : TEXCOORD0;
			};

			struct VSOutput
			{
				float4 position : SV_Position;
				float2 texCoord0 : TEXCOORD0;
			};

			VSOutput main(VSInput input)
			{
				VSOutput o;
				o.position = TransformClip(input.vertexPosition);
				o.texCoord0 = input.vertexTexCoord0 * _Tiling + _Offset;
				return o;
			}
		}

		Fragment
		{
			cbuffer ShadowMaterial : register(b2)
			{
				float2 _Tiling;
				float2 _Offset;
				float4 _MainColor;
				float _AlphaCutoff;
				float3 _ShadowMaterialPadding;
			};

			[[vk::binding(0)]] Texture2D _MainTex : register(t0);
			[[vk::binding(0)]] SamplerState _MainTexSampler : register(s0);

			struct PSInput
			{
				float4 position : SV_Position;
				float2 texCoord0 : TEXCOORD0;
			};

			void main(PSInput input)
			{
				if (_AlphaCutoff > 0.0)
				{
					float alpha = _MainTex.Sample(_MainTexSampler, input.texCoord0).a * _MainColor.a;
					if (alpha < _AlphaCutoff)
						discard;
				}
			}
		}
	ENDHLSL
}
