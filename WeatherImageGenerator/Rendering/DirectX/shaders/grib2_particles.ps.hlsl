// GRIB2 Rain/Snow Particles — Fragment Shader — DirectX 11 HLSL

cbuffer ParticleParams : register(b0)
{
    float uTime;
    float uViewportHeight;
    float uOpacity;
};

struct PSInput
{
    float4 Position : SV_POSITION;
    float  vLife    : TEXCOORD0;
    float  vSize    : TEXCOORD1;
    float  vType    : TEXCOORD2;
};

float4 main(PSInput input) : SV_TARGET
{
    // DX11 does not have gl_PointCoord; particles rendered as screen-aligned quads.
    // Use vTex from geometry shader or screen-space calculation.
    // For point sprite emulation, compute ptc from SV_POSITION vs center.
    float2 ptc = frac(input.Position.xy) * 2.0 - 1.0;
    float opacity = uOpacity > 0.0 ? uOpacity : 0.6;

    float alpha;
    float3 color;

    if (input.vType < 0.5)
    {
        // Rain streak
        float streak = 1.0 - abs(ptc.x) * 3.0;
        streak *= 1.0 - abs(ptc.y) * 0.7;
        streak = max(streak, 0.0);
        alpha = streak * 0.7;
        color = float3(0.7, 0.8, 1.0);
    }
    else if (input.vType < 1.5)
    {
        // Snow flake
        float dist = length(ptc);
        alpha = smoothstep(1.0, 0.3, dist);
        float angle = atan2(ptc.y, ptc.x);
        float star = 0.5 + 0.5 * cos(angle * 6.0);
        alpha *= lerp(0.8, 1.0, star * smoothstep(0.6, 0.2, dist));
        float sparkle = sin(uTime * 4.0 + ptc.x * 20.0) * 0.15 + 0.85;
        alpha *= sparkle;
        color = float3(0.95, 0.97, 1.0);
    }
    else
    {
        // Mixed
        float dist = length(ptc);
        float streak = 1.0 - abs(ptc.x) * 2.5;
        streak *= 1.0 - abs(ptc.y) * 0.8;
        streak = max(streak, 0.0);
        float flake = smoothstep(1.0, 0.4, dist);
        alpha = lerp(streak * 0.6, flake * 0.5, 0.5);
        color = float3(0.8, 0.85, 0.95);
    }

    float lifeFade = smoothstep(0.0, 0.3, input.vLife);
    alpha *= lifeFade * opacity;

    if (alpha < 0.01) discard;
    return float4(color, alpha);
}
