#version 330 core
// Procedural lightning flash shader - strike-positioned with cloud illumination
// Flashes at exact lightning strike NDC locations, illuminates nearby area.
// Radar-aware: flash glow is stronger where clouds/precip exist.

in vec2 vTex;
in vec2 vNdc;
out vec4 FragColor;

uniform float uTime;
uniform float uLightningSignal;     // global signal [0,1]
uniform float uConvective;          // storm severity [0,1]
uniform sampler2D uRadarTex;        // radar overlay texture
uniform mat3  uRadarTransform;      // screen NDC -> radar UV
uniform float uRadarPresent;        // 1.0 if radar valid

// Per-strike data
uniform vec2  uStrikeNdc[32];       // NDC positions of active strikes
uniform int   uStrikeCount;         // number of active strikes
uniform float uStrikeFlash[32];     // per-strike flash phase [0,1] (1=just occurred)
uniform float uStrikeIsCG[32];      // 1.0 = cloud-to-ground, 0.0 = in-cloud

// -- Hash --
float lh(float n) { return fract(sin(n) * 43758.5453); }

// -- Radar sampling --
float sampleRadarAlpha(vec2 ndcPos) {
    vec3 radarCoord = uRadarTransform * vec3(ndcPos, 1.0);
    vec2 radarUv = radarCoord.xy;
    radarUv.y = 1.0 - radarUv.y;
    if (radarUv.x < 0.0 || radarUv.x > 1.0 || radarUv.y < 0.0 || radarUv.y > 1.0)
        return 0.0;
    return texture(uRadarTex, radarUv).a;
}

void main() {
    float sig = uLightningSignal * uConvective;
    if (sig < 0.01 && uStrikeCount == 0) discard;

    float totalFlash = 0.0;
    vec3  flashColor = vec3(0.0);
    float brightCore = 0.0;

    // -- Per-strike positioned flashes --
    for (int i = 0; i < uStrikeCount && i < 32; i++) {
        float d = distance(vNdc, uStrikeNdc[i]);
        float flash = uStrikeFlash[i];

        // Wider radius for visible effect; CG tighter but still substantial
        float radius = mix(0.40, 0.30, uStrikeIsCG[i]);
        float strength = mix(0.6, 1.0, uStrikeIsCG[i]);

        // Outer glow: smooth quadratic falloff
        float falloff = 1.0 - smoothstep(0.0, radius, d);
        falloff *= falloff;

        float contribution = falloff * flash * strength;
        totalFlash += contribution;

        // Bright white core at strike center (sharp flash effect)
        float coreRadius = mix(0.06, 0.03, uStrikeIsCG[i]);
        float core = (1.0 - smoothstep(0.0, coreRadius, d)) * flash * strength;
        brightCore += core;

        // Color: CG -> warm white-yellow, IC -> blue-purple
        vec3 cgColor = vec3(0.95, 0.92, 0.85);
        vec3 icColor = vec3(0.65, 0.60, 0.95);
        flashColor += mix(icColor, cgColor, uStrikeIsCG[i]) * contribution;
    }

    totalFlash = clamp(totalFlash, 0.0, 1.0);
    brightCore = clamp(brightCore, 0.0, 1.0);

    // -- Radar-aware glow (flash is stronger where clouds exist) --
    float radarBoost = 1.0;
    if (uRadarPresent > 0.5) {
        float ra = sampleRadarAlpha(vNdc);
        radarBoost = mix(0.4, 1.0, smoothstep(0.0, 0.10, ra));
    }

    totalFlash *= radarBoost;

    // -- Ambient sky-glow (distant storm illumination) --
    float ambient = sig * 0.025 * (0.7 + 0.3 * sin(uTime * 1.4));

    float alpha = clamp(totalFlash * 0.80 + brightCore * 0.95 + ambient, 0.0, 0.90);
    if (alpha < 0.005) discard;

    // Final color: bright white core blended over positioned flash color
    vec3 ambientColor = vec3(0.35, 0.25, 0.65);
    vec3 baseColor = totalFlash > 0.01
        ? mix(ambientColor, flashColor / max(totalFlash, 0.01), clamp(totalFlash * 2.0, 0.0, 1.0))
        : ambientColor;
    // Blend toward pure white in the bright core
    vec3 finalColor = mix(baseColor, vec3(1.0, 0.98, 0.95), clamp(brightCore * 1.5, 0.0, 1.0));

    FragColor = vec4(finalColor, alpha);
}
