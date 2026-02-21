// Tile Fragment Shader - DirectX 11 HLSL
// Post-processing: saturation, contrast, vignette, atmospheric tint

Texture2D uTexture : register(t0);
SamplerState uSampler : register(s0);

cbuffer TileParams : register(b0)
{
    float uOpacity;
    float uZoomNorm;
    uint uEnableSaturation;
    uint uEnableContrast;
    uint uEnableVignette;
    uint uEnableAtmosphere;
};

struct PSInput
{
    float4 Position : SV_POSITION;
    float2 vTex : TEXCOORD0;
    float2 vScreenPos : TEXCOORD1;
};

float4 main(PSInput input) : SV_TARGET
{
    // Flip Y for top-left origin bitmap data
    float2 uv = float2(input.vTex.x, 1.0 - input.vTex.y);
    float4 c = uTexture.Sample(uSampler, uv);
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;

    float3 result = c.rgb;

    // --- Saturation boost (12%) ---
    if (uEnableSaturation)
    {
        float luma = dot(result, float3(0.2126, 0.7152, 0.0722));
        result = lerp(float3(luma, luma, luma), result, 1.12);
    }

    // --- Mild contrast curve ---
    if (uEnableContrast)
    {
        result = smoothstep(float3(-0.01, -0.01, -0.01), float3(1.01, 1.01, 1.01), result);
    }

    // --- Screen-space vignette ---
    if (uEnableVignette)
    {
        float dist = length(input.vScreenPos);
        float vignette = smoothstep(1.6, 0.4, dist);
        result *= lerp(0.55, 1.0, vignette);
    }

    // --- Atmospheric tint at low zoom ---
    if (uEnableAtmosphere)
    {
        float atmoFactor = smoothstep(0.0, 1.0, 1.0 - saturate(uZoomNorm)) * 0.10;
        result = lerp(result, float3(0.30, 0.40, 0.55), atmoFactor);
    }

    return float4(result, c.a * opacity);
}
