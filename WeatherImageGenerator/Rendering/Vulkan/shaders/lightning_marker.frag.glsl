#version 450

// Lightning strike marker fragment shader — Vulkan GLSL (SPIR-V)
// Identical visual logic to the OpenGL lightning_marker.frag.glsl variant.

layout(location = 0) in  vec2  vUv;
layout(location = 1) in  float vAge;
layout(location = 2) in  float vIsCG;
layout(location = 0) out vec4  FragColor;

void main() {
    float r = length(vUv);

    // ── Base colour by type ──
    vec3 cgColor  = vec3(1.00, 0.843, 0.251);   // #FFD740 warm yellow
    vec3 icColor  = vec3(0.251, 0.784, 1.00);   // #40C8FF electric blue
    vec3 baseColor= mix(icColor, cgColor, vIsCG);

    // ── Age fade: recent = full brightness, old = 10 % ──
    float ageFactor = mix(1.0, 0.10, vAge);

    // ── Core disc ──
    float coreR = 0.22;
    float coreA = smoothstep(coreR + 0.06, coreR - 0.06, r);

    // ── Specular highlight ──
    float spec  = smoothstep(0.14, 0.0, length(vUv - vec2(-0.07, 0.08))) * 0.45;

    // ── Soft glow halo ──
    float glowA = exp(-max(r - coreR, 0.0) * 6.5) * 0.70;

    // ── Sparkle rays (4-fold) ──
    float rayMask = 0.0;
    if (r > 0.01 && r < 0.85) {
        vec2  u   = vUv / r;
        float h   = abs(u.x);
        float v2  = abs(u.y);
        float ray = max(pow(h, 18.0), pow(v2, 18.0));
        float fd  = 1.0 - smoothstep(0.25, 0.80, r);
        rayMask   = ray * fd * 0.55;
    }

    // ── Combine ──
    float alpha = clamp(max(coreA, glowA * 0.45) + rayMask, 0.0, 1.0);
    vec3  color = min(vec3(1.0), baseColor + spec * 0.35);

    FragColor = vec4(color * ageFactor, alpha * ageFactor);
}
