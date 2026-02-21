// UI vertex shader - DirectX 11 HLSL

cbuffer ProjectionCB : register(b0)
{
    float4x4 uProjection;
};

struct VS_INPUT
{
    float2 aPos : POSITION;
    float2 aTex : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float2 vTex     : TEXCOORD0;
};

VS_OUTPUT main(VS_INPUT input)
{
    VS_OUTPUT output;
    output.Position = mul(float4(input.aPos, 0.0, 1.0), uProjection);
    output.vTex = input.aTex;
    return output;
}
