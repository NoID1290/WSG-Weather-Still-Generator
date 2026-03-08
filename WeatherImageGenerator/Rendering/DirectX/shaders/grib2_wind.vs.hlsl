// GRIB2 Wind Streamlines — Vertex Shader — DirectX 11 HLSL

cbuffer TransformCB : register(b0)
{
    float3x3 uTransform;
};

struct VSInput
{
    float2 aPos : POSITION;
    float2 aTex : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float2 vTex     : TEXCOORD0;
};

VSOutput main(VSInput input)
{
    VSOutput output;
    float3 p = mul(float3(input.aPos, 1.0), uTransform);
    output.Position = float4(p.xy, 0.0, 1.0);
    output.vTex = input.aTex;
    return output;
}
