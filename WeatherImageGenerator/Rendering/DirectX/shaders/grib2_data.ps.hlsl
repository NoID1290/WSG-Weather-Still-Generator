// GRIB2 Data Visualization — Fragment Shader — DirectX 11 HLSL
// GPU-side color mapping: R32F data texture -> 1D palette lookup.

Texture2D   uDataTex    : register(t0);
Texture1D   uPaletteTex : register(t1);
SamplerState uSampler   : register(s0);
SamplerState uPalSampler: register(s1);

cbuffer Grib2DataParams : register(b0)
{
    float uOpacity;
    float uTime;
    uint  uEnableGlow;
    float uFieldType;
    float uDataMin;
    float uDataMax;
};

struct PSInput
{
    float4 Position  : SV_POSITION;
    float2 vTex      : TEXCOORD0;
    float2 vScreenPos: TEXCOORD1;
};

float4 main(PSInput input) : SV_TARGET
{
    float2 uv = float2(input.vTex.x, 1.0 - input.vTex.y);

    float rawValue = uDataTex.Sample(uSampler, uv).r;
    float range = uDataMax - uDataMin;
    float t = saturate((rawValue - uDataMin) / max(range, 0.001));

    if (rawValue < uDataMin - 500.0) discard;

    float4 paletteColor = uPaletteTex.Sample(uPalSampler, t);

    // Edge blending
    float edgeFade = 1.0;
    float border = 0.012;
    edgeFade *= smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x);
    edgeFade *= smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    float3 color = paletteColor.rgb;

    // Glow
    if (uEnableGlow)
    {
        uint texW, texH;
        uDataTex.GetDimensions(texW, texH);
        float2 texelSize = 1.0 / float2((float)texW, (float)texH);

        float n0 = uDataTex.Sample(uSampler, uv + float2( texelSize.x * 3.0,  0.0)).r;
        float n1 = uDataTex.Sample(uSampler, uv + float2(-texelSize.x * 3.0,  0.0)).r;
        float n2 = uDataTex.Sample(uSampler, uv + float2(0.0,  texelSize.y * 3.0)).r;
        float n3 = uDataTex.Sample(uSampler, uv + float2(0.0, -texelSize.y * 3.0)).r;
        float avgN = (n0 + n1 + n2 + n3) / 4.0;
        float nT = saturate((avgN - uDataMin) / max(range, 0.001));
        float glow = smoothstep(0.4, 0.85, nT) * 0.22;
        color = lerp(color, color * (1.0 + glow), step(0.1, t));

        if (t > 0.85)
        {
            float pulse = sin(uTime * 2.5) * 0.04 + 0.04;
            color += color * pulse;
        }
    }

    color = pow(color, float3(1.0 / 1.08, 1.0 / 1.08, 1.0 / 1.08));

    float alphaScale = 1.0;
    int fieldType = (int)uFieldType;
    if (fieldType == 2) alphaScale = smoothstep(0.0, 0.02, t);
    else if (fieldType == 3) alphaScale = smoothstep(0.0, 0.08, t) * 0.85;

    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;
    float finalAlpha = paletteColor.a * opacity * edgeFade * alphaScale;

    return float4(color, finalAlpha);
}
