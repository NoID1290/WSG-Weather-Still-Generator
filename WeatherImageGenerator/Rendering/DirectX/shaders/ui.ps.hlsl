// UI fragment shader - DirectX 11 HLSL

Texture2D uFontAtlas : register(t0);
SamplerState uSampler : register(s0);

cbuffer UIParams : register(b0)
{
    float4 uColor;
    int    uMode;   // 0 = textured glyph, 1 = flat rect
    float3 _pad;
};

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 vTex     : TEXCOORD0;
};

float4 main(PS_INPUT input) : SV_TARGET
{
    if (uMode == 0)
    {
        float a = uFontAtlas.Sample(uSampler, input.vTex).r;
        return float4(uColor.rgb, uColor.a * a);
    }
    else
    {
        return uColor;
    }
}
