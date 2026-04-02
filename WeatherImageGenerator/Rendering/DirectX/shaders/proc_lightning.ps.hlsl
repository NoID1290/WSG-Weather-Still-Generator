// Procedural lightning flash pixel shader — DirectX 11 HLSL
// Strike-positioned flashes with radar-aware cloud illumination.

Texture2D    uRadarTex : register(t1);
SamplerState uSampler  : register(s0);

cbuffer ProcLightningCB : register(b0)
{
    float uTime;
    float uLightningSignal;
    float uConvective;
    float uRadarPresent;
    int   uStrikeCount;
    float3 _pad0;
    // mat3 as 3 float4 rows
    float4 uRadarTransformR0;
    float4 uRadarTransformR1;
    float4 uRadarTransformR2;
    // Strike data: xy = NDC, z = flash, w = isCG
    float4 uStrikeData[32];
};

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 vTex     : TEXCOORD0;
    float2 vNdc     : TEXCOORD1;
};

float sampleRadarAlpha(float2 ndcPos) {
    float3 rc3 = float3(ndcPos, 1.0);
    float2 radarUv = float2(
        dot(rc3, float3(uRadarTransformR0.xyz)),
        dot(rc3, float3(uRadarTransformR1.xyz))
    );
    radarUv.y = 1.0 - radarUv.y;
    if (radarUv.x < 0.0 || radarUv.x > 1.0 || radarUv.y < 0.0 || radarUv.y > 1.0)
        return 0.0;
    return uRadarTex.Sample(uSampler, radarUv).a;
}

float4 main(PS_INPUT input) : SV_TARGET
{
    float sig = uLightningSignal * uConvective;
    if (sig < 0.01 && uStrikeCount == 0) discard;

    float totalFlash = 0.0;
    float3 flashColor = float3(0.0, 0.0, 0.0);
    float brightCore = 0.0;

    for (int i = 0; i < uStrikeCount && i < 32; i++) {
        float2 strikePos = uStrikeData[i].xy;
        float flash = uStrikeData[i].z;
        float isCG = uStrikeData[i].w;

        float d = distance(input.vNdc, strikePos);

        // Wider radius for visible effect; CG tighter but still substantial
        float radius = lerp(0.40, 0.30, isCG);
        float strength = lerp(0.6, 1.0, isCG);

        // Outer glow: smooth quadratic falloff
        float falloff = 1.0 - smoothstep(0.0, radius, d);
        falloff *= falloff;

        float contribution = falloff * flash * strength;
        totalFlash += contribution;

        // Bright white core at strike center (sharp flash effect)
        float coreRadius = lerp(0.06, 0.03, isCG);
        float core = (1.0 - smoothstep(0.0, coreRadius, d)) * flash * strength;
        brightCore += core;

        float3 cgColor = float3(0.95, 0.92, 0.85);
        float3 icColor = float3(0.65, 0.60, 0.95);
        flashColor += lerp(icColor, cgColor, isCG) * contribution;
    }

    totalFlash = clamp(totalFlash, 0.0, 1.0);
    brightCore = clamp(brightCore, 0.0, 1.0);

    // Radar-aware glow
    float radarBoost = 1.0;
    if (uRadarPresent > 0.5) {
        float ra = sampleRadarAlpha(input.vNdc);
        radarBoost = lerp(0.4, 1.0, smoothstep(0.0, 0.10, ra));
    }
    totalFlash *= radarBoost;

    // Ambient distant storm glow
    float ambient = sig * 0.025 * (0.7 + 0.3 * sin(uTime * 1.4));

    float alpha = clamp(totalFlash * 0.80 + brightCore * 0.95 + ambient, 0.0, 0.90);
    if (alpha < 0.005) discard;

    // Final color: bright white core blended over positioned flash color
    float3 ambientColor = float3(0.35, 0.25, 0.65);
    float3 baseColor = totalFlash > 0.01
        ? lerp(ambientColor, flashColor / max(totalFlash, 0.01), clamp(totalFlash * 2.0, 0.0, 1.0))
        : ambientColor;
    // Blend toward pure white in the bright core
    float3 finalColor = lerp(baseColor, float3(1.0, 0.98, 0.95), clamp(brightCore * 1.5, 0.0, 1.0));

    return float4(finalColor, alpha);
}
