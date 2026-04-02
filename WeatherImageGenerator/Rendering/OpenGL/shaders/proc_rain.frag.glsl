#version 330 core
// Procedural rain/snow shader - radar-driven spatial positioning
// Rain streaks and snow particles appear only where radar shows precipitation.
// Intensity and density driven by actual radar alpha at each pixel.

in vec2 vTex;
in vec2 vNdc;
out vec4 FragColor;

uniform float uTime;
uniform float uRainIntensity;       // global scalar [0,1]
uniform float uRainCoverage;        // global scalar [0,1]
uniform float uSnowMix;             // snow ratio [0,1]
uniform sampler2D uRadarTex;        // radar overlay texture
uniform mat3  uRadarTransform;      // screen NDC -> radar UV
uniform float uRadarPresent;        // 1.0 if radar valid

// -- Hash functions --
float rh(float n) { return fract(sin(n) * 43758.5453); }
float rh2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

// -- Radar sampling --
vec2 sampleRadar(vec2 ndcPos) {
    vec3 radarCoord = uRadarTransform * vec3(ndcPos, 1.0);
    vec2 radarUv = radarCoord.xy;
    radarUv.y = 1.0 - radarUv.y;

    if (radarUv.x < 0.0 || radarUv.x > 1.0 || radarUv.y < 0.0 || radarUv.y > 1.0)
        return vec2(0.0);

    vec4 rc = texture(uRadarTex, radarUv);
    float luma = dot(rc.rgb, vec3(0.299, 0.587, 0.114));
    return vec2(rc.a, rc.a * luma); // (alpha, intensity)
}

// -- Rain layer --
float rainLayer(vec2 uv, float dens, float spd, float seed, float intens) {
    float cw  = 1.0 / dens;
    float col = floor(uv.x / cw);
    float off = rh(col * 137.1 + seed);
    float y   = fract(uv.y + uTime * spd * (0.7 + off * 0.6) + off);
    float x   = fract(uv.x / cw);
    float str = smoothstep(0.48, 0.50, 1.0 - abs(x - 0.5));
    return smoothstep(0.06 + intens * 0.04, 0.0, y) * str;
}

void main() {
    float intens = uRainIntensity;
    float cov = uRainCoverage;
    float snow = clamp(uSnowMix, 0.0, 1.0);
    if (intens < 0.02 && cov < 0.02) discard;

    // Require radar to spatially gate rain - no fullscreen fallback
    if (uRadarPresent < 0.5) discard;

    vec2 rs = sampleRadar(vNdc);
    float radarAlpha = rs.x;
    float radarIntensity = rs.y;

    // No precipitation at this pixel - no rain/snow here
    if (radarAlpha < 0.03) discard;

    // Modulate intensity by local radar strength
    float localIntens = intens * clamp(radarAlpha * 2.0, 0.3, 1.0);
    float localMask = smoothstep(0.02, 0.10, radarAlpha);

    // Heavier radar returns -> denser rain
    localIntens = max(localIntens, radarIntensity * 1.2);

    float spd = 0.8 + localIntens * 0.8;

    // -- Rain streaks --
    float rain = 0.0;
    if (snow < 0.85) {
        rain  = rainLayer(vTex, 38.0, spd,        0.0,   localIntens);
        rain += rainLayer(vTex, 55.0, spd * 0.85, 137.1, localIntens);
        rain += rainLayer(vTex, 72.0, spd * 1.15, 274.3, localIntens);
        rain  = clamp(rain, 0.0, 1.0) * (1.0 - snow * 0.8);
    }

    // -- Snow particles --
    float flake = 0.0;
    if (snow > 0.1) {
        vec2 cell = floor(vTex * 60.0);
        vec2 loc  = fract(vTex * 60.0) - 0.5;
        if (rh2(cell) > 0.6) {
            vec2 drift = vec2(
                sin(uTime * 0.4 + rh2(cell + vec2(7.3, 2.1)) * 6.28) * 0.15,
                0.0
            );
            flake = smoothstep(0.35, 0.1, length(loc - drift)) * 0.7;
        }
    }

    float alpha = (rain * 0.30 + flake * snow * 0.40) *
                  clamp(localIntens * 1.2 + cov * 0.5, 0.0, 1.0) *
                  localMask;
    if (alpha < 0.01) discard;

    FragColor = vec4(mix(vec3(0.72, 0.82, 0.96), vec3(0.94, 0.96, 1.0), snow), alpha);
}
