#version 450
// GRIB2 Data Visualization — Fragment Shader (Vulkan)
// GPU-side color mapping: R32F data texture → 1D palette lookup.

layout(location=0) in vec2 vTex;
layout(location=1) in vec2 vScreenPos;
layout(location=0) out vec4 FragColor;

layout(set=0, binding=0) uniform sampler2D uDataTex;
layout(set=0, binding=1) uniform sampler1D uPaletteTex;

layout(push_constant) uniform PC {
    vec4 row0;
    vec4 row1;
    vec4 row2;
    float uOpacity;
    float uTime;
    float uEnableGlow;
    float uFieldType;
    float uDataMin;
    float uDataMax;
} pc;

void main() {
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);

    float rawValue = texture(uDataTex, uv).r;
    float range = pc.uDataMax - pc.uDataMin;
    float t = clamp((rawValue - pc.uDataMin) / max(range, 0.001), 0.0, 1.0);

    if (rawValue < pc.uDataMin - 500.0) discard;

    vec4 paletteColor = texture(uPaletteTex, t);

    // Edge blending
    float edgeFade = 1.0;
    float border = 0.012;
    edgeFade *= smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x);
    edgeFade *= smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    vec3 color = paletteColor.rgb;

    // Glow
    if (pc.uEnableGlow > 0.5) {
        vec2 texelSize = 1.0 / vec2(textureSize(uDataTex, 0));
        float n0 = texture(uDataTex, uv + vec2( texelSize.x * 3.0,  0.0)).r;
        float n1 = texture(uDataTex, uv + vec2(-texelSize.x * 3.0,  0.0)).r;
        float n2 = texture(uDataTex, uv + vec2(0.0,  texelSize.y * 3.0)).r;
        float n3 = texture(uDataTex, uv + vec2(0.0, -texelSize.y * 3.0)).r;
        float avgN = (n0 + n1 + n2 + n3) / 4.0;
        float nT = clamp((avgN - pc.uDataMin) / max(range, 0.001), 0.0, 1.0);
        float glow = smoothstep(0.4, 0.85, nT) * 0.22;
        color = mix(color, color * (1.0 + glow), step(0.1, t));

        if (t > 0.85) {
            float pulse = sin(pc.uTime * 2.5) * 0.04 + 0.04;
            color += color * pulse;
        }
    }

    color = pow(color, vec3(1.0 / 1.08));

    float alphaScale = 1.0;
    int fieldType = int(pc.uFieldType);
    if (fieldType == 2) alphaScale = smoothstep(0.0, 0.02, t);
    else if (fieldType == 3) alphaScale = smoothstep(0.0, 0.08, t) * 0.85;

    float opacity = pc.uOpacity > 0.0 ? pc.uOpacity : 1.0;
    float finalAlpha = paletteColor.a / 255.0 * opacity * edgeFade * alphaScale;

    FragColor = vec4(color, finalAlpha);
}
