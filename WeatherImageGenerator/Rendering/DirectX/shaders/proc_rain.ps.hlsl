// Procedural rain/snow pixel shader — DirectX 11 HLSL
// Radar-driven: rain streaks/snow only where radar shows precipitation.

Texture2D    uRadarTex : register(t1);
SamplerState uSampler  : register(s0);

cbuffer ProcRainCB : register(b0)
{
    float uTime;
    float uRainIntensity;
    float uRainCoverage;
    float uSnowMix;
    float uRadarPresent;
    float3 _pad0;
    // mat3 as 3 float4 rows
    float4 uRadarTransformR0;
    float4 uRadarTransformR1;
    float4 uRadarTransformR2;
};

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 vTex     : TEXCOORD0;
    float2 vNdc     : TEXCOORD1;
};

float rh(float n) { return frac(sin(n) * 43758.5453); }
float rh2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

float2 sampleRadar(float2 ndcPos) {
    float3 rc3 = float3(ndcPos, 1.0);
    float2 radarUv = float2(
        dot(rc3, float3(uRadarTransformR0.xyz)),
        dot(rc3, float3(uRadarTransformR1.xyz))
    );
    radarUv.y = 1.0 - radarUv.y;
    if (radarUv.x < 0.0 || radarUv.x > 1.0 || radarUv.y < 0.0 || radarUv.y > 1.0)
        return float2(0.0, 0.0);
    float4 rc = uRadarTex.Sample(uSampler, radarUv);
    float luma = dot(rc.rgb, float3(0.299, 0.587, 0.114));
    return float2(rc.a, rc.a * luma);
}

float rainLayer(float2 uv, float dens, float spd, float seed, float intensity) {
    float cw  = 1.0 / dens;
    float col = floor(uv.x / cw);
    float off = rh(col * 137.1 + seed);
    float y   = frac(uv.y + uTime * spd * (0.7 + off * 0.6) + off);
    float x   = frac(uv.x / cw);
    float str = smoothstep(0.48, 0.50, 1.0 - abs(x - 0.5));
    return smoothstep(0.06 + intensity * 0.04, 0.0, y) * str;
}

float4 main(PS_INPUT input) : SV_TARGET
{
    float intens = uRainIntensity;
    float cov = uRainCoverage;
    float snow = clamp(uSnowMix, 0.0, 1.0);
    if (intens < 0.02 && cov < 0.02) discard;

    float localIntens = intens;
    float localMask = 1.0;

    if (uRadarPresent > 0.5) {
        float2 rs = sampleRadar(input.vNdc);
        float radarAlpha = rs.x;
        float radarIntensity = rs.y;
        if (radarAlpha < 0.03) discard;
        localIntens = intens * clamp(radarAlpha * 2.0, 0.3, 1.0);
        localMask = smoothstep(0.02, 0.10, radarAlpha);
        localIntens = max(localIntens, radarIntensity * 1.2);
    }

    float spd = 0.8 + localIntens * 0.8;

    float rain = 0.0;
    if (snow < 0.85) {
        rain  = rainLayer(input.vTex, 38.0, spd,        0.0, localIntens);
        rain += rainLayer(input.vTex, 55.0, spd * 0.85, 137.1, localIntens);
        rain += rainLayer(input.vTex, 72.0, spd * 1.15, 274.3, localIntens);
        rain  = clamp(rain, 0.0, 1.0) * (1.0 - snow * 0.8);
    }

    float flake = 0.0;
    if (snow > 0.1) {
        float2 cell = floor(input.vTex * 60.0);
        float2 loc  = frac(input.vTex * 60.0) - 0.5;
        if (rh2(cell) > 0.6) {
            float2 drift = float2(
                sin(uTime * 0.4 + rh2(cell + float2(7.3, 2.1)) * 6.28) * 0.15,
                0.0
            );
            flake = smoothstep(0.35, 0.1, length(loc - drift)) * 0.7;
        }
    }

    float alpha = (rain * 0.30 + flake * snow * 0.40) *
                  clamp(localIntens * 1.2 + cov * 0.5, 0.0, 1.0) *
                  localMask;
    if (alpha < 0.01) discard;

    return float4(lerp(float3(0.72, 0.82, 0.96), float3(0.94, 0.96, 1.0), snow), alpha);
}
