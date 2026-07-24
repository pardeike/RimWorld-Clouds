Shader "Clouds/CloudParticle"
{
	Properties
	{
		_MainTex ("Particle Texture", 2D) = "white" {}
		_Color ("Tint", Color) = (1, 1, 1, 1)
	}

	SubShader
	{
		Tags
		{
			"Queue" = "Transparent+201"
			"RenderType" = "Transparent"
			"IgnoreProjector" = "True"
		}

		Blend SrcAlpha OneMinusSrcAlpha
		Cull Back
		Lighting Off
		ZWrite Off

		Stencil
		{
			Ref 0
			ReadMask 17
			Comp Equal
			Pass Keep
		}

		Pass
		{
			CGPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma target 2.0

			#include "UnityCG.cginc"

			struct VertexInput
			{
				float4 vertex : POSITION;
				fixed4 color : COLOR;
				float2 texCoord : TEXCOORD0;
			};

			struct VertexOutput
			{
				float4 vertex : SV_POSITION;
				fixed4 color : COLOR;
				float2 texCoord : TEXCOORD0;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _Color;

			VertexOutput Vert(VertexInput input)
			{
				VertexOutput output;
				output.vertex = UnityObjectToClipPos(input.vertex);
				output.color = input.color * _Color;
				output.texCoord = TRANSFORM_TEX(input.texCoord, _MainTex);
				return output;
			}

			fixed4 Frag(VertexOutput input) : SV_Target
			{
				return tex2D(_MainTex, input.texCoord) * input.color;
			}
			ENDCG
		}
	}
}
