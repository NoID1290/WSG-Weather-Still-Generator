// UI Vertex Shader - DirectX 11 HLSL
// Orthographic projection for HUD text and rectangles

cbuffer ProjectionCB : register(b0)
{
    float4x4 uProjection;
};

struct VSInput
{
    float2 aPos : POSITION;
    float2 aTex : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float2 vTex : TEXCOORD0;
};

VSOutput main(VSInput input)
{
    VSOutput output;
    output.Position = mul(float4(input.aPos, 0.0, 1.0), uProjection);
    output.vTex = input.aTex;
    return output;
}
