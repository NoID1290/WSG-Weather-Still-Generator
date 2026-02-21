// Overlay (Crosshair) Fragment Shader - DirectX 11 HLSL
// Renders crosshair with anti-aliased edges and optional pulse animation

cbuffer OverlayParams : register(b0)
{
    float3 uColor;
    float uAlpha;
    float uTime;
    uint uEnablePulse;
};

struct PSInput
{
    float4 Position : SV_POSITION;
    float2 vLineCoord : TEXCOORD0;
};

float4 main(PSInput input) : SV_TARGET
{
    float pulse = uEnablePulse ? (0.85 + 0.15 * sin(uTime * 2.5)) : 1.0;
    float finalAlpha = uAlpha * pulse;

    float edge = abs(input.vLineCoord.x);

    // Inner fill (colored line)
    float inner = 1.0 - smoothstep(0.28, 0.48, edge);
    // Outer border (black outline)
    float outer = 1.0 - smoothstep(0.60, 1.0, edge);

    float3 col = lerp(float3(0.0, 0.0, 0.0), uColor, inner);
    finalAlpha *= outer;

    return float4(col, finalAlpha);
}
