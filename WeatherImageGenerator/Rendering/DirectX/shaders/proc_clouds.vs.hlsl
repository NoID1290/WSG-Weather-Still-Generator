// Procedural FX vertex shader — DirectX 11 HLSL
// Fullscreen quad pass-through, outputs NDC position for radar transform.

struct VS_INPUT
{
    float2 aPos : POSITION;
    float2 aTex : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float2 vTex     : TEXCOORD0;
    float2 vNdc     : TEXCOORD1;
};

VS_OUTPUT main(VS_INPUT input)
{
    VS_OUTPUT o;
    o.Position = float4(input.aPos, 0.0, 1.0);
    o.vTex = input.aTex;
    o.vNdc = input.aPos;
    return o;
}
