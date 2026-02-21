// Overlay/crosshair fragment shader - DirectX 11 HLSL

cbuffer OverlayParams : register(b0)
{
    float3 uColor;
    float  uAlpha;
    float  uTime;
    uint   uEnablePulse;
};

struct PS_INPUT
{
    float4 Position   : SV_POSITION;
    float2 vLineCoord : TEXCOORD0;
};

float4 main(PS_INPUT input) : SV_TARGET
{
    float pulse = uEnablePulse ? (0.85 + 0.15 * sin(uTime * 2.5)) : 1.0;
    float finalAlpha = uAlpha * pulse;

    float edge = abs(input.vLineCoord.x);

    float inner = 1.0 - smoothstep(0.28, 0.48, edge);
    float outer = 1.0 - smoothstep(0.60, 1.0, edge);

    float3 col = lerp(float3(0.0, 0.0, 0.0), uColor, inner);
    finalAlpha *= outer;

    return float4(col, finalAlpha);
}
