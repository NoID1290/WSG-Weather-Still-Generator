// GRIB2 Atmosphere — Fragment Shader — DirectX 11 HLSL
// Day/night terminator and CAPE instability highlighting.

Texture2D    uCapeData : register(t0);
SamplerState uSampler  : register(s0);

cbuffer AtmosphereParams : register(b0)
{
    float  uTime;
    float  uOpacity;
    float  uSolarDeclination;
    float  uSubsolarLon;
    uint   uEnableTerminator;
    uint   uEnableCape;
    float  uCapeDataMin;
    float  uCapeDataMax;
    float4 uViewBounds; // minLat, minLon, maxLat, maxLon
};

struct PSInput
{
    float4 Position  : SV_POSITION;
    float2 vTex      : TEXCOORD0;
    float2 vScreenPos: TEXCOORD1;
};

static const float PI = 3.14159265359;
static const float DEG2RAD = PI / 180.0;

float4 main(PSInput input) : SV_TARGET
{
    float2 uv = float2(input.vTex.x, 1.0 - input.vTex.y);

    float lat = lerp(uViewBounds.x, uViewBounds.z, uv.y);
    float lon = lerp(uViewBounds.y, uViewBounds.w, uv.x);

    float3 color = float3(0.0, 0.0, 0.0);
    float alpha = 0.0;

    // Day/Night Terminator
    if (uEnableTerminator)
    {
        float latRad = lat * DEG2RAD;
        float decRad = uSolarDeclination * DEG2RAD;
        float hourAngleRad = (lon - uSubsolarLon) * DEG2RAD;

        float sinElev = sin(latRad) * sin(decRad)
                      + cos(latRad) * cos(decRad) * cos(hourAngleRad);
        float elevDeg = asin(clamp(sinElev, -1.0, 1.0)) / DEG2RAD;

        float nightAmount = smoothstep(1.0, -6.0, elevDeg);
        color = float3(0.02, 0.03, 0.08);
        alpha = nightAmount * 0.55;
    }

    // CAPE Instability
    if (uEnableCape)
    {
        float cape = uCapeData.Sample(uSampler, uv).r;
        if (cape > uCapeDataMin + 50.0)
        {
            float capeNorm = saturate((cape - uCapeDataMin) / max(uCapeDataMax - uCapeDataMin, 1.0));

            float3 capeColor = lerp(
                float3(1.0, 0.9, 0.3),
                float3(1.0, 0.2, 0.1),
                smoothstep(0.2, 0.8, capeNorm)
            );

            float pulse = 1.0;
            if (capeNorm > 0.4)
            {
                pulse = 0.85 + 0.15 * sin(uTime * 3.0 + uv.x * 10.0 + uv.y * 8.0);
            }

            float ringAlpha = 0.0;
            if (capeNorm > 0.6)
            {
                float ringDist = length((uv - 0.5) * 2.0);
                float ringPhase = frac(ringDist * 3.0 - uTime * 0.5);
                ringAlpha = smoothstep(0.0, 0.1, ringPhase) * (1.0 - smoothstep(0.1, 0.2, ringPhase));
                ringAlpha *= (capeNorm - 0.6) * 2.0;
            }

            float capeAlpha = smoothstep(0.05, 0.25, capeNorm) * 0.35 * pulse + ringAlpha * 0.2;
            color = lerp(color, capeColor, capeAlpha);
            alpha = max(alpha, capeAlpha);
        }
    }

    float border = 0.015;
    float edgeFade = smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x)
                   * smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;
    return float4(color, alpha * edgeFade * opacity);
}
