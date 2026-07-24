Shader "Default/CubemapSkybox"

Properties
{
    _CubeRight ("Right (+X)", Texture2D) = "white"
    _CubeLeft ("Left (-X)", Texture2D) = "white"
    _CubeTop ("Top (+Y)", Texture2D) = "white"
    _CubeBottom ("Bottom (-Y)", Texture2D) = "white"
    _CubeFront ("Front (+Z)", Texture2D) = "white"
    _CubeBack ("Back (-Z)", Texture2D) = "white"
    _Tint ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    _Exposure ("Exposure", Float) = 1.0
}

Pass "CubemapSkybox"
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
                mat4 viewNoTranslation = PROWL_MATRIX_V;
                viewNoTranslation[3][0] = 0.0;
                viewNoTranslation[3][1] = 0.0;
                viewNoTranslation[3][2] = 0.0;

                vec4 pos = PROWL_MATRIX_P * viewNoTranslation * vec4(vertexPosition, 1.0);
                gl_Position = pos.xyww;

                vDirection = vertexPosition;
            }
        }

        Fragment
        {
            #include "ProwlCG"

            layout (location = 0) out vec4 fragColor;

            in vec3 vDirection;

            uniform sampler2D _CubeRight;
            uniform sampler2D _CubeLeft;
            uniform sampler2D _CubeTop;
            uniform sampler2D _CubeBottom;
            uniform sampler2D _CubeFront;
            uniform sampler2D _CubeBack;
            uniform vec4 _Tint;
            uniform float _Exposure;

            // Sample a cubemap face from 6 separate 2D textures
            vec4 sampleCubemap(vec3 dir)
            {
                vec3 absDir = abs(dir);
                vec2 uv;
                vec4 color;

                if (absDir.x >= absDir.y && absDir.x >= absDir.z)
                {
                    // X dominant
                    if (dir.x > 0.0)
                    {
                        uv = vec2(-dir.z, -dir.y) / absDir.x * 0.5 + 0.5;
                        color = texture(_CubeRight, uv);
                    }
                    else
                    {
                        uv = vec2(dir.z, -dir.y) / absDir.x * 0.5 + 0.5;
                        color = texture(_CubeLeft, uv);
                    }
                }
                else if (absDir.y >= absDir.x && absDir.y >= absDir.z)
                {
                    // Y dominant
                    if (dir.y > 0.0)
                    {
                        uv = vec2(dir.x, dir.z) / absDir.y * 0.5 + 0.5;
                        color = texture(_CubeTop, uv);
                    }
                    else
                    {
                        uv = vec2(dir.x, -dir.z) / absDir.y * 0.5 + 0.5;
                        color = texture(_CubeBottom, uv);
                    }
                }
                else
                {
                    // Z dominant
                    if (dir.z > 0.0)
                    {
                        uv = vec2(dir.x, -dir.y) / absDir.z * 0.5 + 0.5;
                        color = texture(_CubeFront, uv);
                    }
                    else
                    {
                        uv = vec2(-dir.x, -dir.y) / absDir.z * 0.5 + 0.5;
                        color = texture(_CubeBack, uv);
                    }
                }

                return color;
            }

            void main()
            {
                vec3 dir = normalize(vDirection);
                vec4 color = sampleCubemap(dir);
                color.rgb *= _Tint.rgb * _Exposure;

                fragColor = vec4(color.rgb, 1.0);
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
                VSOutput output;
                float4x4 viewNoTranslation = PROWL_MATRIX_V;
                viewNoTranslation._m30 = 0.0;
                viewNoTranslation._m31 = 0.0;
                viewNoTranslation._m32 = 0.0;
                float4 position = mul(PROWL_MATRIX_P, mul(viewNoTranslation, float4(input.vertexPosition, 1.0)));
                output.position = float4(position.xy, position.w, position.w);
                output.vDirection = input.vertexPosition;
                return output;
            }
        }

        Fragment
        {
            cbuffer CubemapSkyboxPS : register(b2)
            {
                float4 _Tint;
                float _Exposure;
                float3 _CubemapSkyboxPadding;
            };

            [[vk::binding(0)]] Texture2D _CubeRight : register(t0);
            [[vk::binding(0)]] SamplerState _CubeRightSampler : register(s0);
            [[vk::binding(1)]] Texture2D _CubeLeft : register(t1);
            [[vk::binding(1)]] SamplerState _CubeLeftSampler : register(s1);
            [[vk::binding(2)]] Texture2D _CubeTop : register(t2);
            [[vk::binding(2)]] SamplerState _CubeTopSampler : register(s2);
            [[vk::binding(3)]] Texture2D _CubeBottom : register(t3);
            [[vk::binding(3)]] SamplerState _CubeBottomSampler : register(s3);
            [[vk::binding(4)]] Texture2D _CubeFront : register(t4);
            [[vk::binding(4)]] SamplerState _CubeFrontSampler : register(s4);
            [[vk::binding(5)]] Texture2D _CubeBack : register(t5);
            [[vk::binding(5)]] SamplerState _CubeBackSampler : register(s5);

            struct PSInput
            {
                float4 position : SV_Position;
                float3 vDirection : TEXCOORD0;
            };

            float4 SampleCubemap(float3 direction)
            {
                float3 absoluteDirection = abs(direction);
                float2 uv;
                if (absoluteDirection.x >= absoluteDirection.y && absoluteDirection.x >= absoluteDirection.z)
                {
                    if (direction.x > 0.0)
                    {
                        uv = float2(-direction.z, -direction.y) / absoluteDirection.x * 0.5 + 0.5;
                        return _CubeRight.Sample(_CubeRightSampler, uv);
                    }
                    uv = float2(direction.z, -direction.y) / absoluteDirection.x * 0.5 + 0.5;
                    return _CubeLeft.Sample(_CubeLeftSampler, uv);
                }
                if (absoluteDirection.y >= absoluteDirection.x && absoluteDirection.y >= absoluteDirection.z)
                {
                    if (direction.y > 0.0)
                    {
                        uv = float2(direction.x, direction.z) / absoluteDirection.y * 0.5 + 0.5;
                        return _CubeTop.Sample(_CubeTopSampler, uv);
                    }
                    uv = float2(direction.x, -direction.z) / absoluteDirection.y * 0.5 + 0.5;
                    return _CubeBottom.Sample(_CubeBottomSampler, uv);
                }
                if (direction.z > 0.0)
                {
                    uv = float2(direction.x, -direction.y) / absoluteDirection.z * 0.5 + 0.5;
                    return _CubeFront.Sample(_CubeFrontSampler, uv);
                }
                uv = float2(-direction.x, -direction.y) / absoluteDirection.z * 0.5 + 0.5;
                return _CubeBack.Sample(_CubeBackSampler, uv);
            }

            float4 main(PSInput input) : SV_Target
            {
                float3 color = SampleCubemap(normalize(input.vDirection)).rgb;
                color *= _Tint.rgb * _Exposure;
                return float4(color, 1.0);
            }
        }
    ENDHLSL
}
