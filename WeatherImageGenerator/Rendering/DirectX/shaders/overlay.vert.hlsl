// Overlay (Crosshair) Vertex Shader - DirectX 11 HLSL

cbuffer OverlayCB : register(b0)
{
    float2 uOffset;
};

struct VSInput
{
    float2 aPos : POSITION;
    float aLineEdge : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float2 vLineCoord : TEXCOORD0;
};

VSOutput main(VSInput input)
{
    VSOutput output;
    output.Position = float4(input.aPos + uOffset, 0.0, 1.0);
    output.vLineCoord = float2(input.aLineEdge, 0.0);
    return output;
}
