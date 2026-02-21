#version 450

layout(location=0) in vec2 vTex;
layout(location=1) in vec2 vScreenPos;

layout(location=0) out vec4 FragColor;

layout(set=0, binding=1) uniform sampler2D uTexture;
layout(set=0, binding=2) uniform TileParams {
    float uOpacity;
    float uZoomNorm;
    bool uEnableSaturation;
    bool uEnableContrast;
    bool uEnableVignette;
    bool uEnableAtmosphere;
};

void main() {
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);
    vec4 c = texture(uTexture, uv);
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;

    vec3 result = c.rgb;

    // --- Saturation boost (12%) ---
    if (uEnableSaturation) {
        float luma = dot(result, vec3(0.2126, 0.7152, 0.0722));
        result = mix(vec3(luma), result, 1.12);
    }

    // --- Mild contrast curve ---
    if (uEnableContrast) {
        result = smoothstep(vec3(-0.01), vec3(1.01), result);
    }

    // --- Screen-space vignette ---
    if (uEnableVignette) {
        float dist = length(vScreenPos);
        float vignette = smoothstep(1.6, 0.4, dist);
        result *= mix(0.55, 1.0, vignette);
    }

    // --- Atmospheric tint at low zoom ---
    if (uEnableAtmosphere) {
        float atmoFactor = smoothstep(0.0, 1.0, 1.0 - clamp(uZoomNorm, 0.0, 1.0)) * 0.10;
        result = mix(result, vec3(0.30, 0.40, 0.55), atmoFactor);
    }

    FragColor = vec4(result, c.a * opacity);
}
