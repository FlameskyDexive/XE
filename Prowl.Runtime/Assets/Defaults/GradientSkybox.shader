Shader "Default/GradientSkybox"

Properties
{
    _TopColor ("Top Color", Color) = (0.4, 0.6, 0.9, 1.0)
    _BottomColor ("Bottom Color", Color) = (0.8, 0.8, 0.7, 1.0)
    _Exponent ("Exponent", Float) = 1.0
}

Pass "GradientSkybox"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull Front
    ZWrite Off
    ZTest LEqual

    GLSLPROGRAM

        Vertex
        {
            #include "ProwlCG"
            #include "VertexAttributes"

            out vec3 vDirection;

            void main()
            {
                // Strip translation from view matrix so skybox stays centered
                mat4 viewNoTranslation = PROWL_MATRIX_V;
                viewNoTranslation[3][0] = 0.0;
                viewNoTranslation[3][1] = 0.0;
                viewNoTranslation[3][2] = 0.0;

                vec4 pos = PROWL_MATRIX_P * viewNoTranslation * vec4(vertexPosition, 1.0);
                gl_Position = pos.xyww; // depth = 1.0 (far plane)

                vDirection = normalize(vertexPosition);
            }
        }

        Fragment
        {
            #include "ProwlCG"

            layout (location = 0) out vec4 fragColor;

            in vec3 vDirection;

            uniform vec4 _TopColor;
            uniform vec4 _BottomColor;
            uniform float _Exponent;

            void main()
            {
                float t = pow(clamp(vDirection.y * 0.5 + 0.5, 0.0, 1.0), _Exponent);
                vec3 color = mix(_BottomColor.rgb, _TopColor.rgb, t);
                fragColor = vec4(color, 1.0);
            }
        }
    ENDGLSL

    HLSLPROGRAM
        Vertex
        {
            #include "ProwlCG"

            struct VSInput
            {
                float3 vertexPosition : POSITION;
            };

            struct VSOutput
            {
                float4 position : SV_Position;
                float3 vDirection : TEXCOORD0;
            };

            VSOutput main(VSInput input)
            {
                VSOutput o;
                float4x4 viewNoTranslation = PROWL_MATRIX_V;
                viewNoTranslation._m30 = 0.0;
                viewNoTranslation._m31 = 0.0;
                viewNoTranslation._m32 = 0.0;
                float4 pos = mul(PROWL_MATRIX_P, mul(viewNoTranslation, float4(input.vertexPosition, 1.0)));
                o.position = float4(pos.xy, pos.w, pos.w);
                o.vDirection = normalize(input.vertexPosition);
                return o;
            }
        }

        Fragment
        {
            cbuffer GradientPS : register(b2)
            {
                float4 _TopColor;
                float4 _BottomColor;
                float _Exponent;
            };

            struct PSInput
            {
                float4 position : SV_Position;
                float3 vDirection : TEXCOORD0;
            };

            float4 main(PSInput input) : SV_Target
            {
                float t = pow(saturate(input.vDirection.y * 0.5 + 0.5), _Exponent);
                float3 color = lerp(_BottomColor.rgb, _TopColor.rgb, t);
                return float4(color, 1.0);
            }
        }
    ENDHLSL
}
