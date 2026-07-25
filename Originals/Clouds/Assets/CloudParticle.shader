Shader "Clouds/CloudParticle"
{
	Properties
	{
		_MainTex ("Particle Texture", 2D) = "white" {}
		_ClusterTex ("World-space Cluster Mask", 2D) = "white" {}
		_Color ("Tint", Color) = (1, 1, 1, 1)
		_WeatherTint ("Weather Tint", Color) = (1, 1, 1, 1)
		_WeatherContrast ("Weather Contrast", Range(1, 1.25)) = 1
		_WeatherOpacity ("Weather Opacity", Range(0.6, 2)) = 1
		_WeatherEdgePower ("Weather Edge Power", Range(0.5, 1.8)) = 1
		_ClusterCutoff ("Cluster Cutoff", Range(0, 1)) = 0.22
		_ClusterFeather ("Cluster Feather", Range(0.01, 1)) = 0.38
		_ClusterOffset ("Cluster Offset", Vector) = (0, 0, 0, 0)
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
			#pragma target 3.0

			#include "UnityCG.cginc"

			struct VertexInput
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				fixed4 color : COLOR;
				float2 texCoord : TEXCOORD0;
				float stableRandom : TEXCOORD1;
			};

			struct VertexOutput
			{
				float4 vertex : SV_POSITION;
				fixed4 color : COLOR;
				float2 texCoord : TEXCOORD0;
				float2 worldXZ : TEXCOORD1;
				float4 weatherVariation : TEXCOORD2;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			sampler2D _ClusterTex;
			fixed4 _Color;
			fixed4 _WeatherTint;
			float _WeatherContrast;
			float _WeatherOpacity;
			float _WeatherEdgePower;
			float _ClusterCutoff;
			float _ClusterFeather;
			float4 _ClusterOffset;

			float ParticleHash(float seed)
			{
				return frac(sin(seed * 12.9898 + 78.233) * 43758.5453);
			}

			VertexOutput Vert(VertexInput input)
			{
				fixed4 particleColor = input.color;
				float2 particleUV = input.texCoord;
				float particleSeed = input.stableRandom * 65535.0 + 1.0;
				float4 worldPosition = mul(unity_ObjectToWorld, input.vertex);

				VertexOutput output;
				output.vertex = UnityObjectToClipPos(input.vertex);
				output.color = particleColor * _Color;
				output.texCoord = TRANSFORM_TEX(particleUV, _MainTex);
				output.worldXZ = worldPosition.xz;
				output.weatherVariation = float4(
					ParticleHash(particleSeed),
					ParticleHash(particleSeed + 17.0),
					ParticleHash(particleSeed + 43.0),
					ParticleHash(particleSeed + 101.0));
				return output;
			}

			fixed4 Frag(VertexOutput input) : SV_Target
			{
				fixed4 cloud = tex2D(_MainTex, input.texCoord);
				cloud.a = pow(saturate(cloud.a), max(_WeatherEdgePower, 0.01));
				fixed4 color = cloud * input.color;
				color.rgb = saturate((color.rgb - 0.5) * _WeatherContrast + 0.5);
				float maximumTintChannel =
					max(_WeatherTint.r, max(_WeatherTint.g, _WeatherTint.b));
				float variationAmount = saturate((1.0 - maximumTintChannel) * 8.0);
				float stormBrightnessBoost = saturate((0.65 - maximumTintChannel) * 6.0);
				// Keep alpha independent from per-cloud luminance. Storm clouds need
				// almost the full usable RGB range so the variation reads as actual
				// brightness after straight-alpha compositing over the map.
				float brightnessRange = lerp(0.45, 0.95, stormBrightnessBoost);
				float brightnessVariation = 1.0
					+ (input.weatherVariation.x * 2.0 - 1.0)
						* brightnessRange
						* variationAmount;
				float temperatureVariation =
					(input.weatherVariation.y * 2.0 - 1.0) * 0.12 * variationAmount;
				float greenVariation =
					(input.weatherVariation.z * 2.0 - 1.0) * 0.04 * variationAmount;
				float3 variedWeatherTint = saturate(
					_WeatherTint.rgb
					+ float3(-temperatureVariation, greenVariation, temperatureVariation));
				color.rgb *= variedWeatherTint * brightnessVariation;
				color.a *= _WeatherOpacity;

				float2 clusterUV = input.worldXZ / 64.0 + _ClusterOffset.xy;
				float noise = tex2D(_ClusterTex, clusterUV).r;
				float clusterShape = smoothstep(
					_ClusterCutoff,
					_ClusterCutoff + max(_ClusterFeather, 0.0001),
					noise);
				float clusterMask = lerp(0.72, 1.0, clusterShape);
				color.a *= clusterMask;
				return color;
			}
			ENDCG
		}
	}
}
