#version 450

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vUv;
layout(location = 2) in vec2 vWorldXZ;
layout(location = 3) in vec4 vWeatherVariation;
layout(location = 0) out vec4 outColor;
layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color; // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _MainTex_ST; // @default = vec4(0.0, 0.0, 1.0, 1.0)
    vec4 _WeatherTint; // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    float _WeatherContrast; // @default = 1.0
    float _WeatherOpacity; // @default = 1.0
    float _WeatherEdgePower; // @default = 1.0
    float _ClusterCutoff; // @default = 0.22
    float _ClusterFeather; // @default = 0.38
    vec4 _ClusterOffset; // @default = vec4(0.0, 0.0, 0.0, 0.0)
} uMaterial;
layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _ClusterTex;

void main()
{
    vec4 color = texture(_MainTex, vUv);
    color.a = pow(clamp(color.a, 0.0, 1.0), max(uMaterial._WeatherEdgePower, 0.01));
    color *= vColor;
    color.rgb = clamp((color.rgb - 0.5) * uMaterial._WeatherContrast + 0.5, 0.0, 1.0);
    float maxTint = max(uMaterial._WeatherTint.r, max(uMaterial._WeatherTint.g, uMaterial._WeatherTint.b));
    float variationAmount = clamp((1.0 - maxTint) * 8.0, 0.0, 1.0);
    float stormBoost = clamp((0.65 - maxTint) * 6.0, 0.0, 1.0);
    float brightnessRange = mix(0.45, 0.95, stormBoost);
    float brightness = 1.0 + (vWeatherVariation.x * 2.0 - 1.0) * brightnessRange * variationAmount;
    float temperature = (vWeatherVariation.y * 2.0 - 1.0) * 0.12 * variationAmount;
    float green = (vWeatherVariation.z * 2.0 - 1.0) * 0.04 * variationAmount;
    vec3 tint = clamp(uMaterial._WeatherTint.rgb + vec3(-temperature, green, temperature), 0.0, 1.0);
    color.rgb *= tint * brightness;
    color.a *= uMaterial._WeatherOpacity;
    float noise = texture(_ClusterTex, vWorldXZ / 64.0 + uMaterial._ClusterOffset.xy).r;
    float cluster = smoothstep(uMaterial._ClusterCutoff,
        uMaterial._ClusterCutoff + max(uMaterial._ClusterFeather, 0.0001), noise);
    color.a *= mix(0.72, 1.0, cluster);
    outColor = color;
}
