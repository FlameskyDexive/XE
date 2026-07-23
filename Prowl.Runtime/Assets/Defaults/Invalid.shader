Shader "Default/Invalid"

Properties
{
}

Pass "Invalid"
{
	Tags { "RenderType" = "Opaque" }
	Cull None

	GLSLPROGRAM
	Vertex
	{
		#include "ProwlCG"
		#include "VertexAttributes"

		void main()
		{
			gl_Position = PROWL_MATRIX_VP * vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		layout (location = 0) out vec4 fragColor;

		void main()
		{
			fragColor = vec4(1.0, 0.0, 1.0, 1.0);
		}
	}
	ENDGLSL

	HLSLPROGRAM
	Vertex
	{
		cbuffer ProwlPerFrame : register(b0)
		{
			float4x4 PROWL_MATRIX_VP;
		};

		struct VSInput
		{
			float3 vertexPosition : POSITION;
		};

		float4 main(VSInput input) : SV_Position
		{
			return mul(PROWL_MATRIX_VP, float4(input.vertexPosition, 1.0));
		}
	}

	Fragment
	{
		float4 main() : SV_Target
		{
			return float4(1.0, 0.0, 1.0, 1.0);
		}
	}
	ENDHLSL
}
