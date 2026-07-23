Shader "Default/Blit"

Properties
{
}

Pass "Blit"
{
    Tags { "RenderOrder" = "Opaque" }

    // Rasterizer culling mode
    Blend Alpha
    Cull None
    ZTest Off
    ZWrite Off

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
		layout (location = 0) out vec4 finalColor;
		
		in vec2 TexCoords;

		uniform sampler2D _MainTex;

		void main()
		{
			finalColor = texture(_MainTex, TexCoords).rgba;
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
		struct PSInput
		{
			float4 position : SV_Position;
			float2 TexCoords : TEXCOORD0;
		};

		[[vk::binding(0)]] Texture2D _MainTex : register(t0);
		[[vk::binding(0)]] SamplerState _MainTexSampler : register(s0);

		float4 main(PSInput input) : SV_Target
		{
			return _MainTex.Sample(_MainTexSampler, input.TexCoords);
		}
	}

	ENDHLSL
}
