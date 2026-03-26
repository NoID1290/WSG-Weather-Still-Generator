#version 330 core

// Lightning strike marker fragment shader - OpenGL 3.3
//
// Renders a glowing dot for each lightning flash.
//   vIsCG == 1.0  ?  cloud-to-ground  (vivid yellow: #FFD740)
//   vIsCG == 0.0  ?  in-cloud         (electric blue: #40C8FF)
//
// Dot fades from full brightness (vAge=0) to dim (vAge=1).
// A soft halo expands outward to suggest an electric discharge.

in  vec2  vUv;
in  float vAge;
in  float vIsCG;
out vec4  FragColor;

uniform float uFlashBoost;  // 0.0 = no boost, 1.0 = peak flash
uniform float uIsNew;       // 1.0 = this strike arrived after the last fetch; 0.0 = pre-existing

void main() {
    float r = length(vUv);

    // -- Base colour by type --
    vec3 cgColor = vec3(1.00, 0.843, 0.251);  // #FFD740 warm yellow
    vec3 icColor = vec3(0.251, 0.784, 1.00);  // #40C8FF electric blue
    vec3 baseColor = mix(icColor, cgColor, vIsCG);

    // -- Color gradient: newest strikes flash white, fading to type color over first 25 % of life --
    vec3 ageColor = mix(vec3(1.0), baseColor, smoothstep(0.0, 0.25, vAge));

    // -- Age fade: recent = full brightness, old = 10 % --
    // Flash boost is gated by uIsNew so only brand-new strikes glow bright.
    float flashAmt = uIsNew * uFlashBoost;
    float ageFactor = mix(1.0, 0.10, vAge) * (1.0 + flashAmt * (1.0 - vAge) * 3.0);

    // -- Core disc (hard bright centre) --
    float coreR = 0.22;
    float coreA = smoothstep(coreR + 0.06, coreR - 0.06, r);

    // -- Specular highlight on upper-left quadrant --
    float spec  = smoothstep(0.14, 0.0, length(vUv - vec2(-0.07, 0.08))) * 0.45;

    // -- Soft glow halo --
    float glowA = exp(-max(r - coreR, 0.0) * 6.5) * 0.70;

    // -- Sparkle rays - 4-fold radial bright streaks --
    // dot(unit_uv, axis)^high_power picks out pixels near the axes
    float rayMask = 0.0;
    if (r > 0.01 && r < 0.85) {
        vec2  u    = vUv / r;
        float h    = abs(u.x);   // horizontal ray
        float v2   = abs(u.y);   // vertical ray
        float ray  = max(pow(h, 18.0), pow(v2, 18.0));
        float fade = (1.0 - smoothstep(0.25, 0.80, r));
        rayMask = ray * fade * 0.55;
    }

    // -- Combine --
    float alpha = clamp(max(coreA, glowA * 0.45) + rayMask, 0.0, 1.0);
    vec3  color = min(vec3(1.0), ageColor + spec * 0.35);

    // Apply age fade to both colour and alpha
    FragColor = vec4(color * ageFactor, alpha * ageFactor);
}
