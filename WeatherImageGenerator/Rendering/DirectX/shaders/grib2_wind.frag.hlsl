// GRIB2 Wind Streamlines — Fragment Shader — DirectX 11 HLSL
// Texture-advection wind visualization.

Texture2D   uPrevTrail  : register(t0);
Texture2D   uWindU      : register(t1);
Texture2D   uWindV      : register(t2);
Texture1D   uPaletteTex : register(t3);
Texture2D   uSeedTex    : register(t4);
SamplerState uSampler   : register(s0);
SamplerState uPalSampler: register(s1);

cbuffer WindParams : register(b0)
{
    float uTrailDecay;
    float uSpeedScale;
    float uTime;
    float uOpacity;
    float uDataMin;
    float uDataMax;
};

struct PSInput
{
    float4 Position : SV_POSITION;
    float2 vTex     : TEXCOORD0;
};

float4 main(PSInput input) : SV_TARGET
{
    float2 uv = input.vTex;
    float4 prevColor = uPrevTrail.Sample(uSampler, uv) * uTrailDecay;

    float u = uWindU.Sample(uSampler, uv).r;
    float v = uWindV.Sample(uSampler, uv).r;
    float speed = sqrt(u * u + v * v) * 3.6;
    float speedT = saturate((speed - uDataMin) / max(uDataMax - uDataMin, 0.001));

    float2 windOffset = float2(u, -v) * uSpeedScale;
    float2 srcUV = uv - windOffset;
    float4 advectedColor = uPrevTrail.Sample(uSampler, srcUV) * uTrailDecay;

    float seed = uSeedTex.Sample(uSampler, uv + float2(sin(uTime * 1.3) * 0.01, cos(uTime * 0.9) * 0.01)).r;
    bool isSeedParticle = seed > 0.98;

    float3 windColor = uPaletteTex.Sample(uPalSampler, speedT).rgb;

    float3 result = advectedColor.rgb;
    float resultAlpha = advectedColor.a;

    if (isSeedParticle && speed > 1.0)
    {
        result = lerp(result, windColor, 0.7);
        resultAlpha = max(resultAlpha, 0.6);
    }

    float windAlpha = smoothstep(0.0, 5.0, speed);
    resultAlpha *= windAlpha;

    float opacity = uOpacity > 0.0 ? uOpacity : 0.7;
    return float4(result, resultAlpha * opacity);
}
