// GRIB2 Rain/Snow Particles — Vertex Shader — DirectX 11 HLSL

cbuffer ParticleParams : register(b0)
{
    float uTime;
    float uViewportHeight;
    float uOpacity;
    float _pad0;
    float3x3 uTransform;
};

struct VSInput
{
    float4 aPosition : POSITION;   // x, y, z, life
    float4 aVelocity : TEXCOORD0;  // vx, vy, size, type
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float  vLife    : TEXCOORD0;
    float  vSize    : TEXCOORD1;
    float  vType    : TEXCOORD2;
    float  PointSize: PSIZE;
};

VSOutput main(VSInput input)
{
    VSOutput output;
    float3 p = mul(float3(input.aPosition.xy, 1.0), uTransform);
    output.Position = float4(p.xy, 0.0, 1.0);
    output.vLife = input.aPosition.w;
    output.vSize = input.aVelocity.z;
    output.vType = input.aVelocity.w;
    output.PointSize = input.aVelocity.z * (uViewportHeight / 800.0);
    return output;
}
