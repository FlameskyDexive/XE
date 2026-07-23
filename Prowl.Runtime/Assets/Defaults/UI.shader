Shader "Paper/UI"

Properties
{
}

Pass "UI"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend {
        Src One
        Dst OneMinusSrcAlpha
        Mode Add
    }
    ZTest Off
    ZWrite Off
    Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec2 aPosition;
        layout (location = 1) in vec2 aTexCoord;
        layout (location = 2) in vec4 aColor;

        uniform mat4 projection;

        out vec2 fragTexCoord;
        out vec4 fragColor;
        out vec2 fragPos;

        void main()
        {
            fragTexCoord = aTexCoord;
            fragColor = aColor;
            fragPos = aPosition;
            gl_Position = projection * vec4(aPosition, 0.0, 1.0);
        }
    }

    Fragment
    {
        in vec2 fragTexCoord;
        in vec4 fragColor;
        in vec2 fragPos;

        out vec4 finalColor;

        uniform sampler2D texture0;
        uniform sampler2D fontTexture;     // dedicated font-atlas sampler, so text batches with shapes
        uniform mat4 scissorMat;
        uniform vec2 scissorExt;

        uniform mat4 brushMat;
        uniform int brushType;
        uniform vec4 brushColor1;
        uniform vec4 brushColor2;
        uniform vec4 brushParams;
        uniform vec2 brushParams2;

        uniform mat4 brushTextureMat;
        uniform float dpiScale;

        // Backdrop blur
        uniform sampler2D backdropTexture; // blurred copy of the scene behind the shape
        uniform vec2 viewportSize;         // framebuffer size in pixels
        uniform float backdropBlurAmount;  // > 0 when this fill is frosted glass
        uniform int backdropFlipY;         // 1 to flip the backdrop sample vertically

        // ============== Canvas functions ==============

        float calculateBrushFactor() {
            vec2 logicalPos = fragPos / max(dpiScale, 0.001);
            vec2 transformedPoint = (brushMat * vec4(logicalPos, 0.0, 1.0)).xy;

            if (brushType == 1) {
                vec2 startPoint = brushParams.xy; vec2 endPoint = brushParams.zw;
                vec2 line = endPoint - startPoint; float lineLength = length(line);
                if (lineLength < 0.001) return 0.0;
                return clamp(dot(transformedPoint - startPoint, line) / (lineLength * lineLength), 0.0, 1.0);
            }
            if (brushType == 2) {
                vec2 center = brushParams.xy;
                return clamp(smoothstep(brushParams.z, brushParams.w, length(transformedPoint - center)), 0.0, 1.0);
            }
            if (brushType == 3) {
                vec2 center = brushParams.xy; vec2 halfSize = brushParams.zw;
                float radius = brushParams2.x; float feather = brushParams2.y;
                if (halfSize.x < 0.001 || halfSize.y < 0.001) return 0.0;
                vec2 q = abs(transformedPoint - center) - (halfSize - vec2(radius));
                float dist = min(max(q.x,q.y),0.0) + length(max(q,0.0)) - radius;
                return smoothstep(-feather * 0.5, feather * 0.5, dist);
            }
            return 0.0;
        }

        float scissorMask(vec2 p) {
            if(scissorExt.x < 0.0 || scissorExt.y < 0.0) return 1.0;
            float dpi = max(dpiScale, 0.001);
            vec2 logicalP = p / dpi;
            vec2 transformedPoint = (scissorMat * vec4(logicalP, 0.0, 1.0)).xy;
            vec2 logicalExt = scissorExt / dpi;
            vec2 distanceFromEdges = abs(transformedPoint) - logicalExt;
            float halfPixelLogical = 0.5 / dpi;
            vec2 smoothEdges = vec2(halfPixelLogical) - distanceFromEdges;
            return clamp(smoothEdges.x, 0.0, 1.0) * clamp(smoothEdges.y, 0.0, 1.0);
        }

        // Single-channel SDF text: width of the distance range in atlas texels (matches Scribe's
        // FontSystem.DistanceRange), and the screen-space span of one unit at this fragment.
        const float sdfPxRange = 4.0;
        float sdfScreenPxRange(vec2 uv) {
            vec2 unitRange = vec2(sdfPxRange) / vec2(textureSize(fontTexture, 0));
            vec2 screenTexSize = vec2(1.0) / fwidth(uv);
            return max(0.5 * dot(unitRange, screenTexSize), 1.0);
        }

        float sampleCoverage(vec2 uv) {
            float d = texture(fontTexture, uv).r;
            float sd = (d - 0.5) * sdfScreenPxRange(uv);
            return clamp(sd + 0.5, 0.0, 1.0);
        }

        void main()
        {
            float mask = scissorMask(fragPos);
            vec4 color = fragColor;

            if (brushType > 0) {
                float factor = calculateBrushFactor();
                color = mix(brushColor1, brushColor2, factor);
            }

            // SDF text mode: UV.x >= 2.0. The atlas holds a single-channel signed distance field
            // (replicated across RGB); reconstruct sharp coverage from it.
            if (fragTexCoord.x >= 2.0) {
                vec2 uv = fragTexCoord - vec2(2.0);

                // Basic SDF Text
                finalColor = color * sampleCoverage(uv) * mask;
                return;

                // Sub-Pixel SDF Text - Needs a Dual Blend or something? It does sorta work but not well enough.
                //vec2 uvPerScreenX = dFdx(uv);
                //vec2 shift = uvPerScreenX / 3.0;
                //
                //vec3 coverage = vec3(sampleCoverage(uv - shift), sampleCoverage(uv), sampleCoverage(uv + shift));

                //float alpha = (coverage.x + coverage.y + coverage.z) / 3.0;
                //finalColor = vec4(color.rgb * coverage, alpha) * mask;
                //return;
            }

            // Edge anti-aliasing: coverage is baked into the geometry (fringe vertices) and carried
            // in fragTexCoord.x (1 = solid core, 0 = outer fringe edge).
            float edgeAlpha = clamp(fragTexCoord.x, 0.0, 1.0);

            float dpi = max(dpiScale, 0.001);
            vec2 logicalPos = fragPos / dpi;
            vec4 fill = color * texture(texture0, (brushTextureMat * vec4(logicalPos, 0.0, 1.0)).xy);

            // Backdrop blur: composite the fill over the blurred scene behind the shape.
            if (backdropBlurAmount > 0.0) {
                vec2 uv = fragPos / viewportSize;
                if (backdropFlipY == 1) uv.y = 1.0 - uv.y;
                vec3 blurred = texture(backdropTexture, uv).rgb;
                vec3 outRgb = blurred * (1.0 - fill.a) + fill.rgb;  // fill is premultiplied
                finalColor = vec4(outRgb, 1.0) * edgeAlpha * mask;
                return;
            }

            finalColor = fill * edgeAlpha * mask;
        }
    }

    ENDGLSL

    HLSLPROGRAM
    Vertex
    {
        cbuffer UIVS : register(b0)
        {
            float4x4 projection;
        };

        struct VSInput
        {
            float2 aPosition : POSITION;
            float2 aTexCoord : TEXCOORD0;
            float4 aColor : COLOR;
        };

        struct VSOutput
        {
            float4 position : SV_Position;
            float2 fragTexCoord : TEXCOORD0;
            float4 fragColor : COLOR0;
            float2 fragPos : TEXCOORD1;
        };

        VSOutput main(VSInput input)
        {
            VSOutput o;
            o.fragTexCoord = input.aTexCoord;
            o.fragColor = input.aColor;
            o.fragPos = input.aPosition;
            o.position = mul(projection, float4(input.aPosition, 0.0, 1.0));
            return o;
        }
    }

    Fragment
    {
        cbuffer UIPS : register(b1)
        {
            float4x4 scissorMat;
            float2 scissorExt;
            float4x4 brushMat;
            int brushType;
            float4 brushColor1;
            float4 brushColor2;
            float4 brushParams;
            float2 brushParams2;
            float4x4 brushTextureMat;
            float dpiScale;
            float2 viewportSize;
            float backdropBlurAmount;
            int backdropFlipY;
        };

        [[vk::binding(0)]] Texture2D texture0 : register(t0);
        [[vk::binding(0)]] SamplerState texture0Sampler : register(s0);
        [[vk::binding(1)]] Texture2D fontTexture : register(t1);
        [[vk::binding(1)]] SamplerState fontSampler : register(s1);
        [[vk::binding(2)]] Texture2D backdropTexture : register(t2);
        [[vk::binding(2)]] SamplerState backdropSampler : register(s2);

        struct PSInput
        {
            float4 position : SV_Position;
            float2 fragTexCoord : TEXCOORD0;
            float4 fragColor : COLOR0;
            float2 fragPos : TEXCOORD1;
        };

        float calculateBrushFactor(float2 fragPos)
        {
            float2 logicalPos = fragPos / max(dpiScale, 0.001);
            float2 transformedPoint = mul(brushMat, float4(logicalPos, 0.0, 1.0)).xy;
            if (brushType == 1)
            {
                float2 startPoint = brushParams.xy;
                float2 endPoint = brushParams.zw;
                float2 line = endPoint - startPoint;
                float lineLength = length(line);
                if (lineLength < 0.001) return 0.0;
                return saturate(dot(transformedPoint - startPoint, line) / (lineLength * lineLength));
            }
            if (brushType == 2)
            {
                float2 center = brushParams.xy;
                return saturate(smoothstep(brushParams.z, brushParams.w, length(transformedPoint - center)));
            }
            if (brushType == 3)
            {
                float2 center = brushParams.xy;
                float2 halfSize = brushParams.zw;
                float radius = brushParams2.x;
                float feather = brushParams2.y;
                if (halfSize.x < 0.001 || halfSize.y < 0.001) return 0.0;
                float2 q = abs(transformedPoint - center) - (halfSize - radius.xx);
                float dist = min(max(q.x, q.y), 0.0) + length(max(q, 0.0.xx)) - radius;
                return smoothstep(-feather * 0.5, feather * 0.5, dist);
            }
            return 0.0;
        }

        float scissorMask(float2 p)
        {
            if (scissorExt.x < 0.0 || scissorExt.y < 0.0) return 1.0;
            float dpi = max(dpiScale, 0.001);
            float2 logicalP = p / dpi;
            float2 transformedPoint = mul(scissorMat, float4(logicalP, 0.0, 1.0)).xy;
            float2 logicalExt = scissorExt / dpi;
            float2 distanceFromEdges = abs(transformedPoint) - logicalExt;
            float halfPixelLogical = 0.5 / dpi;
            float2 smoothEdges = halfPixelLogical.xx - distanceFromEdges;
            return saturate(smoothEdges.x) * saturate(smoothEdges.y);
        }

        float4 main(PSInput input) : SV_Target
        {
            float mask = scissorMask(input.fragPos);
            float4 color = input.fragColor;
            if (brushType > 0)
            {
                float factor = calculateBrushFactor(input.fragPos);
                color = lerp(brushColor1, brushColor2, factor);
            }

            if (input.fragTexCoord.x >= 2.0)
            {
                float2 uv = input.fragTexCoord - float2(2.0, 0.0);
                float d = fontTexture.Sample(fontSampler, uv).r;
                float coverage = saturate((d - 0.5) * 4.0 + 0.5);
                return color * coverage * mask;
            }

            float edgeAlpha = saturate(input.fragTexCoord.x);
            float dpi = max(dpiScale, 0.001);
            float2 logicalPos = input.fragPos / dpi;
            float4 fill = color * texture0.Sample(texture0Sampler, mul(brushTextureMat, float4(logicalPos, 0.0, 1.0)).xy);

            if (backdropBlurAmount > 0.0)
            {
                float2 uv = input.fragPos / viewportSize;
                if (backdropFlipY == 1) uv.y = 1.0 - uv.y;
                float3 blurred = backdropTexture.Sample(backdropSampler, uv).rgb;
                float3 outRgb = blurred * (1.0 - fill.a) + fill.rgb;
                return float4(outRgb, 1.0) * edgeAlpha * mask;
            }

            return fill * edgeAlpha * mask;
        }
    }
    ENDHLSL
}

Pass "BlurDown"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
    ZTest Off
    ZWrite Off
    Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;

        out vec2 TexCoords;

        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        uniform sampler2D _MainTex;
        uniform float _Offset;

        layout(location = 0) out vec4 FragColor;

        in vec2 TexCoords;

        void main()
        {
            vec2 halfpixel = (0.5 / vec2(textureSize(_MainTex, 0))) * _Offset;

            vec4 sum = texture(_MainTex, TexCoords) * 4.0;
            sum += texture(_MainTex, TexCoords - halfpixel);
            sum += texture(_MainTex, TexCoords + halfpixel);
            sum += texture(_MainTex, TexCoords + vec2(halfpixel.x, -halfpixel.y));
            sum += texture(_MainTex, TexCoords - vec2(halfpixel.x, -halfpixel.y));

            FragColor = sum / 8.0;
        }
    }

    ENDGLSL

    HLSLPROGRAM
    Vertex
    {
        struct VSInput
        {
            float3 vertexPosition : POSITION;
            float2 vertexTexCoord : TEXCOORD0;
        };

        struct VSOutput
        {
            float4 position : SV_Position;
            float2 TexCoords : TEXCOORD0;
        };

        VSOutput main(VSInput input)
        {
            VSOutput o;
            o.TexCoords = input.vertexTexCoord;
            o.position = float4(input.vertexPosition, 1.0);
            return o;
        }
    }

    Fragment
    {
        cbuffer BlurDownPS : register(b0)
        {
            float _Offset;
        };

        [[vk::binding(0)]] Texture2D _MainTex : register(t0);
        [[vk::binding(0)]] SamplerState _MainTexSampler : register(s0);

        struct PSInput
        {
            float4 position : SV_Position;
            float2 TexCoords : TEXCOORD0;
        };

        float4 main(PSInput input) : SV_Target
        {
            float2 texSize;
            _MainTex.GetDimensions(texSize.x, texSize.y);
            float2 halfpixel = (0.5 / texSize) * _Offset;
            float4 sum = _MainTex.Sample(_MainTexSampler, input.TexCoords) * 4.0;
            sum += _MainTex.Sample(_MainTexSampler, input.TexCoords - halfpixel);
            sum += _MainTex.Sample(_MainTexSampler, input.TexCoords + halfpixel);
            sum += _MainTex.Sample(_MainTexSampler, input.TexCoords + float2(halfpixel.x, -halfpixel.y));
            sum += _MainTex.Sample(_MainTexSampler, input.TexCoords - float2(halfpixel.x, -halfpixel.y));
            return sum / 8.0;
        }
    }
    ENDHLSL
}

Pass "BlurUp"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
    ZTest Off
    ZWrite Off
    Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;

        out vec2 TexCoords;

        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        uniform sampler2D _MainTex;
        uniform float _Offset;

        layout(location = 0) out vec4 FragColor;

        in vec2 TexCoords;

        void main()
        {
            vec2 halfpixel = (0.5 / vec2(textureSize(_MainTex, 0))) * _Offset;

            vec4 sum = texture(_MainTex, TexCoords + vec2(-halfpixel.x * 2.0, 0.0));
            sum += texture(_MainTex, TexCoords + vec2(-halfpixel.x, halfpixel.y)) * 2.0;
            sum += texture(_MainTex, TexCoords + vec2(0.0, halfpixel.y * 2.0));
            sum += texture(_MainTex, TexCoords + vec2(halfpixel.x, halfpixel.y)) * 2.0;
            sum += texture(_MainTex, TexCoords + vec2(halfpixel.x * 2.0, 0.0));
            sum += texture(_MainTex, TexCoords + vec2(halfpixel.x, -halfpixel.y)) * 2.0;
            sum += texture(_MainTex, TexCoords + vec2(0.0, -halfpixel.y * 2.0));
            sum += texture(_MainTex, TexCoords + vec2(-halfpixel.x, -halfpixel.y)) * 2.0;

            FragColor = sum / 12.0;
        }
    }

    ENDGLSL

    HLSLPROGRAM
    Vertex
    {
        struct VSInput
        {
            float3 vertexPosition : POSITION;
            float2 vertexTexCoord : TEXCOORD0;
        };

        struct VSOutput
        {
            float4 position : SV_Position;
            float2 TexCoords : TEXCOORD0;
        };

        VSOutput main(VSInput input)
        {
            VSOutput o;
            o.TexCoords = input.vertexTexCoord;
            o.position = float4(input.vertexPosition, 1.0);
            return o;
        }
    }

    Fragment
    {
        cbuffer BlurUpPS : register(b0)
        {
            float _Offset;
        };

        [[vk::binding(0)]] Texture2D _MainTex : register(t0);
        [[vk::binding(0)]] SamplerState _MainTexSampler : register(s0);

        struct PSInput
        {
            float4 position : SV_Position;
            float2 TexCoords : TEXCOORD0;
        };

        float4 main(PSInput input) : SV_Target
        {
            float2 texSize;
            _MainTex.GetDimensions(texSize.x, texSize.y);
            float2 halfpixel = (0.5 / texSize) * _Offset;
            float4 sum = _MainTex.Sample(_MainTexSampler, input.TexCoords + float2(-halfpixel.x * 2.0, 0.0));
            sum += _MainTex.Sample(_MainTexSampler, input.TexCoords + float2(-halfpixel.x, halfpixel.y)) * 2.0;
            sum += _MainTex.Sample(_MainTexSampler, input.TexCoords + float2(0.0, halfpixel.y * 2.0));
            sum += _MainTex.Sample(_MainTexSampler, input.TexCoords + float2(halfpixel.x, halfpixel.y)) * 2.0;
            sum += _MainTex.Sample(_MainTexSampler, input.TexCoords + float2(halfpixel.x * 2.0, 0.0));
            sum += _MainTex.Sample(_MainTexSampler, input.TexCoords + float2(halfpixel.x, -halfpixel.y)) * 2.0;
            sum += _MainTex.Sample(_MainTexSampler, input.TexCoords + float2(0.0, -halfpixel.y * 2.0));
            sum += _MainTex.Sample(_MainTexSampler, input.TexCoords + float2(-halfpixel.x, -halfpixel.y)) * 2.0;
            return sum / 12.0;
        }
    }
    ENDHLSL
}
