#version 450

layout(location = 0) in vec3 inPosition; // @vertex Position required
layout(location = 1) in vec3 inNormal; // @vertex Normal optional @default = vec3(0.0, 1.0, 0.0)
layout(location = 2) in vec4 inColor; // @vertex Color optional @default = vec4(1.0, 1.0, 1.0, 1.0)
layout(location = 3) in vec4 inUv0; // @vertex TexCoord0 optional @default = vec4(0.0, 0.0, 0.0, 0.0)
layout(location = 4) in float inStableRandom; // @vertex TexCoord1 optional @default = 0.0
layout(push_constant) uniform Push { mat4 modelMatrix; } push;
layout(set = 0, binding = 0) uniform VertexUniforms { mat4 ViewProj; } uVertex;

layout(location = 0) out vec4 vColor;
layout(location = 1) out vec2 vUv;
layout(location = 2) out vec2 vWorldXZ;
layout(location = 3) out vec4 vWeatherVariation;
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

float particleHash(float seed)
{
    return fract(sin(seed * 12.9898 + 78.233) * 43758.5453);
}

void main()
{
    vec4 world = push.modelMatrix * vec4(inPosition, 1.0);
    float seed = inStableRandom * 65535.0 + 1.0;
    gl_Position = uVertex.ViewProj * world;
    vColor = inColor * uMaterial._Color;
    vUv = inUv0.xy * uMaterial._MainTex_ST.zw + uMaterial._MainTex_ST.xy;
    vWorldXZ = world.xz;
    vWeatherVariation = vec4(particleHash(seed), particleHash(seed + 17.0),
        particleHash(seed + 43.0), particleHash(seed + 101.0));
}
