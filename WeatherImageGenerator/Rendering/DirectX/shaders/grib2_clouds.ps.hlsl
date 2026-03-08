// GRIB2 Volumetric Clouds — Fragment Shader — DirectX 11 HLSL

Texture2D    uCloudData : register(t0);
SamplerState uSampler   : register(s0);

cbuffer CloudParams : register(b0)
{
    float uTime;
    float uOpacity;
    float uNoiseScale;
    float uDataMin;
    float uDataMax;
    float2 uSunDirection;
};

struct PSInput
{
    float4 Position  : SV_POSITION;
    float2 vTex      : TEXCOORD0;
    float2 vScreenPos: TEXCOORD1;
};

float hash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + float2(1.0, 0.0));
    float c = hash(i + float2(0.0, 1.0));
    float d = hash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float fbm(float2 p, int octaves)
{
    float total = 0.0;
    float amp = 0.5;
    float freq = 1.0;
    for (int i = 0; i < octaves; i++)
    {
        total += noise(p * freq) * amp;
        freq *= 2.0;
        amp *= 0.5;
    }
    return total;
}

float4 main(PSInput input) : SV_TARGET
{
    float2 uv = float2(input.vTex.x, 1.0 - input.vTex.y);
    float cloudCover = uCloudData.Sample(uSampler, uv).r;
    float coverNorm = saturate((cloudCover - uDataMin) / max(uDataMax - uDataMin, 0.001));

    if (coverNorm < 0.05) discard;

    float scale = uNoiseScale > 0.0 ? uNoiseScale : 8.0;
    float2 drift = float2(uTime * 0.008, uTime * 0.003);
    float2 noiseUV = uv * scale + drift;

    float cloudNoise = fbm(noiseUV, 5);
    float cloudDensity = coverNorm * smoothstep(0.2, 0.6, cloudNoise);

    float detail = fbm(noiseUV * 3.0 + float2(uTime * 0.02, 0.0), 3);
    cloudDensity = lerp(cloudDensity, cloudDensity * (0.7 + 0.3 * detail), 0.4);

    float2 sunDir = length(uSunDirection) > 0.01 ? normalize(uSunDirection) : float2(0.3, 0.5);
    float sunDot = dot(normalize(input.vScreenPos), sunDir) * 0.5 + 0.5;

    float3 brightColor = float3(0.95, 0.96, 0.98);
    float3 shadowColor = float3(0.55, 0.58, 0.65);
    float3 cloudColor = lerp(brightColor, shadowColor, cloudDensity * 0.6);
    cloudColor = lerp(cloudColor, brightColor, sunDot * 0.3 * (1.0 - cloudDensity * 0.5));

    float edgeNoise = fbm(noiseUV * 6.0, 2);
    float edgeBright = smoothstep(0.3, 0.5, cloudDensity) * (1.0 - smoothstep(0.5, 0.8, cloudDensity));
    cloudColor += float3(0.1, 0.1, 0.1) * edgeBright * edgeNoise;

    float edgeFade = 1.0;
    float border = 0.015;
    edgeFade *= smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x);
    edgeFade *= smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    float opacity = uOpacity > 0.0 ? uOpacity : 0.75;
    float finalAlpha = cloudDensity * opacity * edgeFade;

    cloudColor = pow(cloudColor, float3(1.0 / 1.05, 1.0 / 1.05, 1.0 / 1.05));
    return float4(cloudColor, finalAlpha);
}
