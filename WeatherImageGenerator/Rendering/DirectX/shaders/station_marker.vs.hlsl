// Station/epicenter marker vertex shader — DirectX 11 HLSL
// Reuses bound quad VB (POSITION + TEXCOORD0) but positions the sprite
// entirely from cbuffer uniforms; the texcoord drives the UV [-1,+1] space.

cbuffer MarkerVSCB : register(b0)
{
    float ndcX;
    float ndcY;
    float halfSizeX;
    float halfSizeY;
    float markerType;  // passed through to PS via TEXCOORD1
    float3 _padVS;
};

struct VS_INPUT
{
    float2 aPos : POSITION;   // not used — kept for input-layout compat
    float2 aTex : TEXCOORD0;  // quad UV [0,1], remapped to [-1,+1]
};

struct VS_OUTPUT
{
    float4 Position  : SV_POSITION;
    float2 vUv       : TEXCOORD0;  // [-1,+1]
    float  vType     : TEXCOORD1;  // markerType forwarded
};

VS_OUTPUT main(VS_INPUT input)
{
    VS_OUTPUT o;
    float2 uv   = input.aTex * 2.0 - 1.0;   // [0,1] → [-1,+1]
    o.vUv       = uv;
    o.vType     = markerType;
    o.Position  = float4(ndcX + uv.x * halfSizeX,
                         ndcY + uv.y * halfSizeY,
                         0.0, 1.0);
    return o;
}
