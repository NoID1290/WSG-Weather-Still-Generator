// Station/epicenter marker pixel shader — DirectX 11 HLSL

cbuffer MarkerPSCB : register(b0)
{
    float colorR;
    float colorG;
    float colorB;
    float colorA;
    float ringPhase;
    float selected;
    float glowStrength;
    float _padPS;
};

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 vUv      : TEXCOORD0;
    float  vType    : TEXCOORD1;
};

// ── IQ equilateral-triangle SDF, apex pointing up ─────────────────────────
float sdEquilateralTriangle(float2 p, float r)
{
    static const float k = 1.7320508; // sqrt(3)
    p.x = abs(p.x) - r;
    p.y = p.y + r / k;
    if (p.x + k * p.y > 0.0)
        p = float2(p.x - k * p.y, -k * p.x - p.y) * 0.5;
    p.x -= clamp(p.x, -2.0 * r, 0.0);
    return -length(p) * sign(p.y);
}

float4 main(PS_INPUT input) : SV_TARGET
{
    float3 baseColor = float3(colorR, colorG, colorB);
    float  alpha     = 0.0;
    float3 outColor  = baseColor;

    if (input.vType < 0.5)
    {
        // ── Station triangle ─────────────────────────────────────────────
        float2 p   = float2(input.vUv.x, -input.vUv.y + 0.12);
        float  sdf = sdEquilateralTriangle(p, 0.78);

        float gs    = max(0.5, glowStrength);
        float glowA = exp(-max(sdf, 0.0) * 4.0 / gs) * 0.65;
        float coreA = smoothstep(0.06, -0.04, sdf);

        float  spec     = smoothstep(0.38, 0.0, length(p - float2(0.0, -0.62))) * 0.35;
        float3 specColor = min(float3(1.0, 1.0, 1.0), baseColor + spec);

        alpha    = max(glowA * 0.55, coreA);
        outColor = lerp(baseColor * 0.85, specColor, coreA);

        if (selected > 0.5)
        {
            float ring = smoothstep(0.24, 0.15, abs(sdf + 0.20));
            outColor   = lerp(outColor, float3(1.0, 1.0, 1.0), ring * 0.85);
            alpha      = max(alpha, ring * 0.88);
        }
    }
    else
    {
        // ── Epicenter dot + animated rings ───────────────────────────────
        float r = length(input.vUv);

        float coreA = smoothstep(0.28, 0.18, r);
        float glowA = exp(-max(r - 0.22, 0.0) * 7.0) * 0.65;
        alpha    = max(coreA, glowA * 0.35);
        outColor = baseColor;

        float  spec = smoothstep(0.18, 0.0, length(input.vUv - float2(-0.08, 0.08)));
        outColor    = min(float3(1.0, 1.0, 1.0), outColor + spec * 0.4);

        float ringAlpha = 0.0;
        [unroll]
        for (int i = 0; i < 3; i++)
        {
            float phase = frac(ringPhase + (float)i * 0.3333);
            float ringR = 0.08 + phase * 0.92;
            float fade  = 1.0 - phase;
            float width = exp(-abs(r - ringR) * 22.0) * fade * 1.8;
            ringAlpha   = max(ringAlpha, width);
        }
        alpha += ringAlpha;
        alpha  = saturate(alpha);
    }

    return float4(outColor, alpha * colorA);
}
