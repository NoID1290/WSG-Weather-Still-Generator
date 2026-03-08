#version 330 core
// GRIB2 Volumetric Clouds - Fragment Shader (OpenGL 3.3)
// Creates realistic cloud rendering from cloud cover percentage grid data.
// Uses FBM noise modulated by actual data for volumetric appearance,
// with time-animated drift and light scattering approximation.

in vec2 vTex;
in vec2 vScreenPos;
out vec4 FragColor;

uniform sampler2D uCloudData;   // R32F cloud cover (0-100%)
uniform float uTime;
uniform float uOpacity;
uniform vec2  uSunDirection;    // Normalized sun direction in screen space
uniform float uNoiseScale;      // Detail noise scale (default ~8.0)
uniform float uDataMin;         // 0
uniform float uDataMax;         // 100

// -- Hash-based noise (no texture dependency) --
float hash(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f); // smoothstep

    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));

    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// -- Fractal Brownian Motion --
float fbm(vec2 p, int octaves) {
    float total = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;

    for (int i = 0; i < octaves; i++) {
        total += noise(p * frequency) * amplitude;
        frequency *= 2.0;
        amplitude *= 0.5;
    }
    return total;
}

void main() {
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);

    // Sample cloud cover from data texture
    float cloudCover = texture(uCloudData, uv).r;
    float coverNorm = clamp((cloudCover - uDataMin) / max(uDataMax - uDataMin, 0.001), 0.0, 1.0);

    // Skip clear areas
    if (coverNorm < 0.05) discard;

    // Animated UV for cloud drift
    float scale = uNoiseScale > 0.0 ? uNoiseScale : 8.0;
    vec2 drift = vec2(uTime * 0.008, uTime * 0.003);
    vec2 noiseUV = uv * scale + drift;

    // FBM noise for cloud texture detail
    float cloudNoise = fbm(noiseUV, 5);

    // Modulate noise by actual cloud cover data
    float cloudDensity = coverNorm * smoothstep(0.2, 0.6, cloudNoise);

    // Add secondary detail layer
    float detail = fbm(noiseUV * 3.0 + vec2(uTime * 0.02, 0.0), 3);
    cloudDensity = mix(cloudDensity, cloudDensity * (0.7 + 0.3 * detail), 0.4);

    // -- Light scattering approximation --
    // Brighter edge toward sun, darker away
    vec2 sunDir = length(uSunDirection) > 0.01 ? normalize(uSunDirection) : vec2(0.3, 0.5);
    float sunDot = dot(normalize(vScreenPos), sunDir) * 0.5 + 0.5;

    // Cloud base color: bright white -> gray depending on density
    vec3 brightColor = vec3(0.95, 0.96, 0.98);   // sunlit cloud
    vec3 shadowColor = vec3(0.55, 0.58, 0.65);    // cloud shadow
    vec3 cloudColor = mix(brightColor, shadowColor, cloudDensity * 0.6);

    // Sun-facing highlight
    cloudColor = mix(cloudColor, brightColor, sunDot * 0.3 * (1.0 - cloudDensity * 0.5));

    // Subtle silver lining at cloud edges
    float edgeNoise = fbm(noiseUV * 6.0, 2);
    float edgeBright = smoothstep(0.3, 0.5, cloudDensity) * (1.0 - smoothstep(0.5, 0.8, cloudDensity));
    cloudColor += vec3(0.1) * edgeBright * edgeNoise;

    // -- Edge blending --
    float edgeFade = 1.0;
    float border = 0.015;
    edgeFade *= smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x);
    edgeFade *= smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    float opacity = uOpacity > 0.0 ? uOpacity : 0.75;
    float finalAlpha = cloudDensity * opacity * edgeFade;

    // Gamma correction
    cloudColor = pow(cloudColor, vec3(1.0 / 1.05));

    FragColor = vec4(cloudColor, finalAlpha);
}
