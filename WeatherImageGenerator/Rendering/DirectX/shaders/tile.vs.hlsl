// Tile vertex shader - DirectX 11 HLSL
// Translates map tile vertices using a 3x3 affine transform

cbuffer TransformCB : register(b0)
{
    row_major float3x3 uTransform;
};

struct VS_INPUT
{
    float2 aPos : POSITION;
    float2 aTex : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position  : SV_POSITION;
    float2 vTex      : TEXCOORD0;
    float2 vScreenPos: TEXCOORD1;
};

VS_OUTPUT main(VS_INPUT input)
{
    VS_OUTPUT output;
    float3 p = mul(float3(input.aPos, 1.0), uTransform);
    output.Position = float4(p.xy, 0.0, 1.0);
    output.vTex = input.aTex;
    output.vScreenPos = p.xy;
    return output;
}
