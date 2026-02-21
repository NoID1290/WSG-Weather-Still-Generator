// Generic Fragment Shader (Radar Palette) - DirectX 11 HLSL
// 6-stop radar color palette with glow effect

Texture2D uTexture : register(t0);
SamplerState uSampler : register(s0);

cbuffer FragParams : register(b0)
{
    float uOpacity;
};

struct PSInput
{
    float4 Position : SV_POSITION;
    float2 vTex : TEXCOORD0;
    float2 vScreenPos : TEXCOORD1;
};

// Enhanced 6-stop radar palette
float3 palette(float t)
{
    if (t < 0.15)       return lerp(float3(0.02, 0.01, 0.15), float3(0.0, 0.25, 0.85), t / 0.15);
    else if (t < 0.30)  return lerp(float3(0.0, 0.25, 0.85), float3(0.0, 0.7, 0.9),  (t - 0.15) / 0.15);
    else if (t < 0.50)  return lerp(float3(0.0, 0.7, 0.9),  float3(0.1, 0.85, 0.2),  (t - 0.30) / 0.20);
    else if (t < 0.65)  return lerp(float3(0.1, 0.85, 0.2),  float3(1.0, 0.95, 0.1),  (t - 0.50) / 0.15);
    else if (t < 0.82)  return lerp(float3(1.0, 0.95, 0.1),  float3(1.0, 0.2, 0.05), (t - 0.65) / 0.17);
    else                return lerp(float3(1.0, 0.2, 0.05), float3(0.85, 0.1, 0.65), (t - 0.82) / 0.18);
}

float4 main(PSInput input) : SV_TARGET
{
    float2 uv = float2(input.vTex.x, 1.0 - input.vTex.y);
    float4 tex = uTexture.Sample(uSampler, uv);

    float intensity = dot(tex.rgb, float3(0.299, 0.587, 0.114));
    float alpha = tex.a * uOpacity * smoothstep(0.015, 0.06, intensity);

    float3 color = palette(intensity);

    // --- Subtle glow for high-intensity areas ---
    uint texW, texH;
    uTexture.GetDimensions(texW, texH);
    float2 texelSize = 1.0 / float2(texW, texH);
    float bloomSum = 0.0;
    bloomSum += dot(uTexture.Sample(uSampler, uv + float2( texelSize.x,  0.0)).rgb, float3(0.333, 0.333, 0.333));
    bloomSum += dot(uTexture.Sample(uSampler, uv + float2(-texelSize.x,  0.0)).rgb, float3(0.333, 0.333, 0.333));
    bloomSum += dot(uTexture.Sample(uSampler, uv + float2( 0.0,  texelSize.y)).rgb, float3(0.333, 0.333, 0.333));
    bloomSum += dot(uTexture.Sample(uSampler, uv + float2( 0.0, -texelSize.y)).rgb, float3(0.333, 0.333, 0.333));
    bloomSum += dot(uTexture.Sample(uSampler, uv + float2( texelSize.x,  texelSize.y)).rgb, float3(0.333, 0.333, 0.333));
    bloomSum += dot(uTexture.Sample(uSampler, uv + float2(-texelSize.x, -texelSize.y)).rgb, float3(0.333, 0.333, 0.333));
    bloomSum += dot(uTexture.Sample(uSampler, uv + float2( texelSize.x, -texelSize.y)).rgb, float3(0.333, 0.333, 0.333));
    bloomSum += dot(uTexture.Sample(uSampler, uv + float2(-texelSize.x,  texelSize.y)).rgb, float3(0.333, 0.333, 0.333));
    float bloomAvg = bloomSum / 8.0;

    float glowFactor = smoothstep(0.25, 0.7, bloomAvg) * 0.15;
    float3 glowColor = palette(bloomAvg);
    color = lerp(color, color + glowColor * glowFactor, step(0.04, intensity));

    color = pow(color, float3(0.95, 0.95, 0.95));

    return float4(color, alpha);
}
