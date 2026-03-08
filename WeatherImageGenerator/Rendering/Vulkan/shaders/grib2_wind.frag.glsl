#version 450
// GRIB2 Wind Streamlines — Fragment Shader (Vulkan)
// Texture-advection wind visualization.

layout(location=0) in vec2 vTex;
layout(location=0) out vec4 FragColor;

layout(set=0, binding=0) uniform sampler2D uPrevTrail;
layout(set=0, binding=1) uniform sampler2D uWindU;
layout(set=0, binding=2) uniform sampler2D uWindV;
layout(set=0, binding=3) uniform sampler1D uPaletteTex;
layout(set=0, binding=4) uniform sampler2D uSeedTex;

layout(push_constant) uniform PC {
    vec4 row0;
    vec4 row1;
    vec4 row2;
    float uTrailDecay;
    float uSpeedScale;
    float uTime;
    float uOpacity;
    float uDataMin;
    float uDataMax;
} pc;

void main() {
    vec2 uv = vTex;
    vec4 prevColor = texture(uPrevTrail, uv) * pc.uTrailDecay;

    float u = texture(uWindU, uv).r;
    float v = texture(uWindV, uv).r;
    float speed = sqrt(u * u + v * v) * 3.6;
    float speedT = clamp((speed - pc.uDataMin) / max(pc.uDataMax - pc.uDataMin, 0.001), 0.0, 1.0);

    vec2 windOffset = vec2(u, -v) * pc.uSpeedScale;
    vec2 srcUV = uv - windOffset;
    vec4 advectedColor = texture(uPrevTrail, srcUV) * pc.uTrailDecay;

    float seed = texture(uSeedTex, uv + vec2(sin(pc.uTime * 1.3) * 0.01, cos(pc.uTime * 0.9) * 0.01)).r;
    bool isSeedParticle = seed > 0.98;

    vec3 windColor = texture(uPaletteTex, speedT).rgb;

    vec3 result = advectedColor.rgb;
    float resultAlpha = advectedColor.a;

    if (isSeedParticle && speed > 1.0) {
        result = mix(result, windColor, 0.7);
        resultAlpha = max(resultAlpha, 0.6);
    }

    float windAlpha = smoothstep(0.0, 5.0, speed);
    resultAlpha *= windAlpha;

    float opacity = pc.uOpacity > 0.0 ? pc.uOpacity : 0.7;
    FragColor = vec4(result, resultAlpha * opacity);
}
