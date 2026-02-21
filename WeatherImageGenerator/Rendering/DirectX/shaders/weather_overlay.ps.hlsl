// Weather overlay fragment shader - DirectX 11 HLSL
// Smooth edge blending, glow for high-intensity precipitation, gamma-correct output

Texture2D uTexture : register(t0);
SamplerState uSampler : register(s0);

cbuffer WeatherOverlayParams : register(b0)
{
    float uOpacity;
    float uTime;
    uint  uEnableGlow;
    float _pad0;
};

struct PS_INPUT
{
    float4 Position  : SV_POSITION;
    float2 vTex      : TEXCOORD0;
    float2 vScreenPos: TEXCOORD1;
};

float4 main(PS_INPUT input) : SV_TARGET
{
    float2 uv = float2(input.vTex.x, 1.0 - input.vTex.y);
    float4 c = uTexture.Sample(uSampler, uv);
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;

    // --- Smooth alpha edge blend ---
    float edgeFade = 1.0;
    float border = 0.015;
    edgeFade *= smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x);
    edgeFade *= smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    float3 finalColor = c.rgb;

    if (uEnableGlow)
    {
        uint texW, texH;
        uTexture.GetDimensions(texW, texH);
        float2 texelSize = 1.0 / float2((float)texW, (float)texH);
        float bloomScale = 2.5;
        float selfIntensity = dot(c.rgb, float3(0.299, 0.587, 0.114));
        float bloomSum = 0.0;
        bloomSum += dot(uTexture.Sample(uSampler, uv + float2( texelSize.x * bloomScale,  0.0)).rgb, float3(0.333, 0.333, 0.333));
        bloomSum += dot(uTexture.Sample(uSampler, uv + float2(-texelSize.x * bloomScale,  0.0)).rgb, float3(0.333, 0.333, 0.333));
        bloomSum += dot(uTexture.Sample(uSampler, uv + float2(0.0,  texelSize.y * bloomScale)).rgb, float3(0.333, 0.333, 0.333));
        bloomSum += dot(uTexture.Sample(uSampler, uv + float2(0.0, -texelSize.y * bloomScale)).rgb, float3(0.333, 0.333, 0.333));
        float bloomAvg = bloomSum / 4.0;

        float glowStrength = smoothstep(0.3, 0.8, bloomAvg) * 0.18;
        float3 glowTint = c.rgb * (1.0 + glowStrength);
        finalColor = lerp(c.rgb, glowTint, step(0.05, selfIntensity));
    }

    // --- sRGB gamma-correct output ---
    finalColor = pow(finalColor, float3(1.0 / 1.05, 1.0 / 1.05, 1.0 / 1.05));

    float finalAlpha = c.a * opacity * edgeFade;
    return float4(finalColor, finalAlpha);
}
