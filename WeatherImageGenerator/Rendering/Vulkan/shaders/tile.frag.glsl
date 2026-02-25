#version 450

layout(location=0) in vec2 vTex;
layout(location=1) in vec2 vScreenPos;

layout(location=0) out vec4 FragColor;

layout(set=0, binding=0) uniform sampler2D uTexture;

layout(push_constant) uniform PC {
    vec4 row0;
    vec4 row1;
    vec4 row2;
    float uOpacity;
    float uZoomNorm;
    float uEnableSaturation;
    float uEnableContrast;
    float uEnableVignette;
    float uEnableAtmosphere;
} pc;

void main() {
    vec4 c = texture(uTexture, vTex);
    float opacity = pc.uOpacity > 0.0 ? pc.uOpacity : 1.0;
    vec3 result = c.rgb;

    // --- Saturation boost (12%) ---
    if (pc.uEnableSaturation > 0.5) {
        float luma = dot(result, vec3(0.2126, 0.7152, 0.0722));
        result = mix(vec3(luma), result, 1.12);
    }

    // --- Mild contrast curve ---
    if (pc.uEnableContrast > 0.5) {
        result = smoothstep(vec3(-0.01), vec3(1.01), result);
    }

    // --- Screen-space vignette ---
    if (pc.uEnableVignette > 0.5) {
        float dist = length(vScreenPos);
        float vignette = smoothstep(1.6, 0.4, dist);
        result *= mix(0.55, 1.0, vignette);
    }

    // --- Atmospheric tint at low zoom ---
    if (pc.uEnableAtmosphere > 0.5) {
        float atmoFactor = smoothstep(0.0, 1.0, 1.0 - clamp(pc.uZoomNorm, 0.0, 1.0)) * 0.10;
        result = mix(result, vec3(0.30, 0.40, 0.55), atmoFactor);
    }

    FragColor = vec4(result, c.a * opacity);
}
