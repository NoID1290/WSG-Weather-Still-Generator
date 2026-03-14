#version 330 core

in  vec2  vUv;
in  float vType;
out vec4  FragColor;

uniform float uColorR;
uniform float uColorG;
uniform float uColorB;
uniform float uColorA;
uniform float uRingPhase;
uniform float uSelected;
uniform float uGlowStrength;

// ── IQ equilateral-triangle SDF, apex pointing up ────────────────────────
float sdEquilateralTriangle(vec2 p, float r) {
    const float k = 1.7320508; // sqrt(3)
    p.x = abs(p.x) - r;
    p.y = p.y + r / k;
    if (p.x + k * p.y > 0.0)
        p = vec2(p.x - k * p.y, -k * p.x - p.y) * 0.5;
    p.x -= clamp(p.x, -2.0 * r, 0.0);
    return -length(p) * sign(p.y);
}

void main() {
    vec3  baseColor = vec3(uColorR, uColorG, uColorB);
    float alpha     = 0.0;
    vec3  outColor  = baseColor;

    if (vType < 0.5) {
        // ── Station triangle ──────────────────────────────────────────────
        vec2  p   = vec2(vUv.x, -vUv.y + 0.12);
        float sdf = sdEquilateralTriangle(p, 0.78);

        float gs    = max(0.5, uGlowStrength);
        float glowA = exp(-max(sdf, 0.0) * 4.0 / gs) * 0.65;
        float coreA = smoothstep(0.06, -0.04, sdf);

        float spec     = smoothstep(0.38, 0.0, length(p - vec2(0.0, -0.62))) * 0.35;
        vec3  specColor = min(vec3(1.0), baseColor + spec);

        alpha    = max(glowA * 0.55, coreA);
        outColor = mix(baseColor * 0.85, specColor, coreA);

        if (uSelected > 0.5) {
            float ring = smoothstep(0.24, 0.15, abs(sdf + 0.20));
            outColor   = mix(outColor, vec3(1.0), ring * 0.85);
            alpha      = max(alpha, ring * 0.88);
        }

    } else {
        // ── Epicenter dot + animated rings ────────────────────────────────
        float r = length(vUv);

        float coreA = smoothstep(0.28, 0.18, r);
        float glowA = exp(-max(r - 0.22, 0.0) * 7.0) * 0.65;
        alpha    = max(coreA, glowA * 0.35);
        outColor = baseColor;

        float spec = smoothstep(0.18, 0.0, length(vUv - vec2(-0.08, 0.08)));
        outColor   = min(vec3(1.0), outColor + spec * 0.4);

        float ringAlpha = 0.0;
        for (int i = 0; i < 3; i++) {
            float phase  = fract(uRingPhase + float(i) * 0.3333);
            float ringR  = 0.08 + phase * 0.92;
            float fade   = 1.0 - phase;
            float width  = exp(-abs(r - ringR) * 22.0) * fade * 1.8;
            ringAlpha    = max(ringAlpha, width);
        }
        alpha += ringAlpha;
        alpha  = clamp(alpha, 0.0, 1.0);
    }

    FragColor = vec4(outColor, alpha * uColorA);
}
