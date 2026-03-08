#version 450
// GRIB2 Volumetric Clouds — Fragment Shader (Vulkan)

layout(location=0) in vec2 vTex;
layout(location=1) in vec2 vScreenPos;
layout(location=0) out vec4 FragColor;

layout(set=0, binding=0) uniform sampler2D uCloudData;

layout(push_constant) uniform PC {
    vec4 row0;
    vec4 row1;
    vec4 row2;
    float uTime;
    float uOpacity;
    float uNoiseScale;
    float uDataMin;
    float uDataMax;
    vec2  uSunDirection;
} pc;

float hash(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p, int octaves) {
    float total = 0.0;
    float amp = 0.5;
    float freq = 1.0;
    for (int i = 0; i < octaves; i++) {
        total += noise(p * freq) * amp;
        freq *= 2.0;
        amp *= 0.5;
    }
    return total;
}

void main() {
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);
    float cloudCover = texture(uCloudData, uv).r;
    float coverNorm = clamp((cloudCover - pc.uDataMin) / max(pc.uDataMax - pc.uDataMin, 0.001), 0.0, 1.0);

    if (coverNorm < 0.05) discard;

    float scale = pc.uNoiseScale > 0.0 ? pc.uNoiseScale : 8.0;
    vec2 drift = vec2(pc.uTime * 0.008, pc.uTime * 0.003);
    vec2 noiseUV = uv * scale + drift;

    float cloudNoise = fbm(noiseUV, 5);
    float cloudDensity = coverNorm * smoothstep(0.2, 0.6, cloudNoise);

    float detail = fbm(noiseUV * 3.0 + vec2(pc.uTime * 0.02, 0.0), 3);
    cloudDensity = mix(cloudDensity, cloudDensity * (0.7 + 0.3 * detail), 0.4);

    vec2 sunDir = length(pc.uSunDirection) > 0.01 ? normalize(pc.uSunDirection) : vec2(0.3, 0.5);
    float sunDot = dot(normalize(vScreenPos), sunDir) * 0.5 + 0.5;

    vec3 brightColor = vec3(0.95, 0.96, 0.98);
    vec3 shadowColor = vec3(0.55, 0.58, 0.65);
    vec3 cloudColor = mix(brightColor, shadowColor, cloudDensity * 0.6);
    cloudColor = mix(cloudColor, brightColor, sunDot * 0.3 * (1.0 - cloudDensity * 0.5));

    float edgeNoise = fbm(noiseUV * 6.0, 2);
    float edgeBright = smoothstep(0.3, 0.5, cloudDensity) * (1.0 - smoothstep(0.5, 0.8, cloudDensity));
    cloudColor += vec3(0.1) * edgeBright * edgeNoise;

    float edgeFade = 1.0;
    float border = 0.015;
    edgeFade *= smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x);
    edgeFade *= smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    float opacity = pc.uOpacity > 0.0 ? pc.uOpacity : 0.75;
    float finalAlpha = cloudDensity * opacity * edgeFade;

    cloudColor = pow(cloudColor, vec3(1.0 / 1.05));
    FragColor = vec4(cloudColor, finalAlpha);
}
