// Lightning strike marker vertex shader — DirectX 11 HLSL
// Positions a sprite quad in NDC space from cbuffer uniforms.
// Passes age and CG/IC flag to pixel shader.

cbuffer LightningVSCB : register(b0)
{
    float ndcX;
    float ndcY;
    float halfSizeX;
    float halfSizeY;
    float age;        // 0.0 = just occurred, 1.0 = oldest in window
    float isCG;       // 1.0 = cloud-to-ground (yellow), 0.0 = in-cloud (blue)
    float flashBoost; // 0.0 = no boost, 1.0 = peak flash
    float _padVS;
};

struct VS_INPUT
{
    float2 aPos : POSITION;   // not used — kept for input-layout compat
    float2 aTex : TEXCOORD0;  // quad UV [0,1], remapped to [-1,+1]
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float2 vUv      : TEXCOORD0;  // [-1,+1]
    float  vAge     : TEXCOORD1;
    float  vIsCG    : TEXCOORD2;
    float  vFlashBoost : TEXCOORD3;
};

VS_OUTPUT main(VS_INPUT input)
{
    VS_OUTPUT o;
    float2 uv = input.aTex * 2.0 - 1.0;
    o.vUv     = uv;
    o.vAge    = age;
    o.vIsCG   = isCG;
    o.vFlashBoost = flashBoost;
    o.Position = float4(ndcX + uv.x * halfSizeX,
                        ndcY + uv.y * halfSizeY,
                        0.0, 1.0);
    return o;
}
