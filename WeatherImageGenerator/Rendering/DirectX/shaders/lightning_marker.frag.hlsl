// Lightning strike marker pixel shader - DirectX 11 HLSL
//
// vIsCG == 1.0  ?  cloud-to-ground  (vivid yellow #FFD740)
// vIsCG == 0.0  ?  in-cloud         (electric blue #40C8FF)
//
// Dot fades from full brightness (vAge=0) to dim (vAge=1).

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 vUv      : TEXCOORD0;
    float  vAge     : TEXCOORD1;
    float  vIsCG    : TEXCOORD2;
};

float4 main(PS_INPUT input) : SV_TARGET
{
    float r = length(input.vUv);

    // -- Base colour by type --
    float3 cgColor = float3(1.00, 0.843, 0.251);   // #FFD740 warm yellow
    float3 icColor = float3(0.251, 0.784, 1.00);   // #40C8FF electric blue
    float3 baseColor = lerp(icColor, cgColor, input.vIsCG);

    // -- Age fade --
    float ageFactor = lerp(1.0, 0.10, input.vAge);

    // -- Core disc --
    float coreR = 0.22;
    float coreA = smoothstep(coreR + 0.06, coreR - 0.06, r);

    // -- Specular highlight --
    float spec = smoothstep(0.14, 0.0, length(input.vUv - float2(-0.07, 0.08))) * 0.45;

    // -- Soft glow halo --
    float glowA = exp(-max(r - coreR, 0.0) * 6.5) * 0.70;

    // -- Sparkle rays (4-fold) --
    float rayMask = 0.0;
    [branch]
    if (r > 0.01 && r < 0.85)
    {
        float2 u   = input.vUv / r;
        float  h   = abs(u.x);
        float  v2  = abs(u.y);
        float  ray = max(pow(h, 18.0), pow(v2, 18.0));
        float  fd  = 1.0 - smoothstep(0.25, 0.80, r);
        rayMask = ray * fd * 0.55;
    }

    // -- Combine --
    float alpha = clamp(max(coreA, glowA * 0.45) + rayMask, 0.0, 1.0);
    float3 color = min(float3(1.0, 1.0, 1.0), baseColor + spec * 0.35);

    return float4(color * ageFactor, alpha * ageFactor);
}
