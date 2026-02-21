// Overlay/crosshair vertex shader - DirectX 11 HLSL

cbuffer OverlayCB : register(b0)
{
    float2 uOffset;
};

struct VS_INPUT
{
    float2 aPos      : POSITION;
    float  aLineEdge : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position   : SV_POSITION;
    float2 vLineCoord : TEXCOORD0;
};

VS_OUTPUT main(VS_INPUT input)
{
    VS_OUTPUT output;
    output.Position = float4(input.aPos + uOffset, 0.0, 1.0);
    output.vLineCoord = float2(input.aLineEdge, 0.0);
    return output;
}
