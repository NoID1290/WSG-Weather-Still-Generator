// UI Fragment Shader - DirectX 11 HLSL
// Text rendering from font atlas or flat colored rectangles

Texture2D uFontAtlas : register(t0);
SamplerState uSampler : register(s0);

cbuffer UIParams : register(b0)
{
    float4 uColor;
    int uMode; // 0 = textured glyph, 1 = flat rect
};

struct PSInput
{
    float4 Position : SV_POSITION;
    float2 vTex : TEXCOORD0;
};

float4 main(PSInput input) : SV_TARGET
{
    if (uMode == 0)
    {
        // Text mode: sample font atlas red channel as alpha
        float a = uFontAtlas.Sample(uSampler, input.vTex).r;
        return float4(uColor.rgb, uColor.a * a);
    }
    else if (uMode == 1)
    {
        // Rect mode: flat color
        return uColor;
    }
    else
    {
        // Image texture mode: RGBA texture bound to the atlas slot
        float4 texColor = uFontAtlas.Sample(uSampler, input.vTex);
        return texColor * float4(1.0, 1.0, 1.0, uColor.a);
    }
}
