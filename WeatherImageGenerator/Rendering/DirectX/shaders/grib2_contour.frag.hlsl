// GRIB2 Contour Lines — Fragment Shader — DirectX 11 HLSL
// Antialiased GPU contour lines using screen-space derivatives.

Texture2D    uDataTex : register(t0);
SamplerState uSampler : register(s0);

cbuffer ContourParams : register(b0)
{
    float uDataMin;
    float uDataMax;
    float uContourInterval;
    float uContourWidth;
    float uOpacity;
    float3 _pad0;
    float4 uContourColor;
};

struct PSInput
{
    float4 Position : SV_POSITION;
    float2 vTex     : TEXCOORD0;
};

float4 main(PSInput input) : SV_TARGET
{
    float2 uv = float2(input.vTex.x, 1.0 - input.vTex.y);
    float value = uDataTex.Sample(uSampler, uv).r;

    if (value < uDataMin - 500.0) discard;

    float interval = uContourInterval > 0.0 ? uContourInterval : 4.0;
    float phase = value / interval;
    float fractPhase = frac(phase);
    float dPhase = fwidth(phase);
    float lineWidth = (uContourWidth > 0.0 ? uContourWidth : 1.5) * 0.5;

    float contour = 1.0 - smoothstep(0.0, dPhase * lineWidth, min(fractPhase, 1.0 - fractPhase));
    if (contour < 0.01) discard;

    float majorPhase = value / (interval * 5.0);
    float majorFract = frac(majorPhase);
    float dMajor = fwidth(majorPhase);
    float majorContour = 1.0 - smoothstep(0.0, dMajor * lineWidth * 1.5, min(majorFract, 1.0 - majorFract));

    float4 lineColor = uContourColor.a > 0.0 ? uContourColor : float4(0.2, 0.2, 0.2, 0.85);
    float alpha = lerp(contour * 0.6, max(contour, majorContour), majorContour) * lineColor.a;

    float border = 0.015;
    float edgeFade = smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x)
                   * smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;
    return float4(lineColor.rgb, alpha * edgeFade * opacity);
}
