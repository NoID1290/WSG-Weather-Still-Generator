#version 330 core
in vec2 vTex;
in vec2 vScreenPos;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;
uniform float uZoomNorm; // 0.0 = zoomed out (world), 1.0 = zoomed in (street)
uniform float uZoomBlur; // 0.0 = no blur, >0 = radial zoom motion blur strength

// Shader toggle uniforms
uniform bool uEnableSaturation;
uniform bool uEnableContrast;
uniform bool uEnableVignette;
uniform bool uEnableAtmosphere;

void main() {
    // Bitmap data is top-left origin; flip Y when sampling for correct orientation
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);
    vec4 c = texture(uTexture, uv);
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;

    vec3 result = c.rgb;

    // --- Radial zoom motion blur (screen-space, from center) ---
    if (uZoomBlur > 0.001) {
        vec2 dir = vScreenPos; // direction from screen center (NDC)
        float strength = uZoomBlur * 0.02; // scale to subtle offset
        vec3 blurAccum = result;
        blurAccum += texture(uTexture, uv + dir * strength * 0.25).rgb;
        blurAccum += texture(uTexture, uv + dir * strength * 0.50).rgb;
        blurAccum += texture(uTexture, uv + dir * strength * 0.75).rgb;
        blurAccum += texture(uTexture, uv - dir * strength * 0.25).rgb;
        result = blurAccum / 5.0;
    }

    // --- Saturation boost (12%) ---
    if (uEnableSaturation) {
        float luma = dot(result, vec3(0.2126, 0.7152, 0.0722));
        result = mix(vec3(luma), result, 1.12);
    }

    // --- Mild contrast curve ---
    if (uEnableContrast) {
        result = smoothstep(vec3(-0.01), vec3(1.01), result);
    }

    // --- Screen-space vignette (viewport-wide, not per-tile) ---
    if (uEnableVignette) {
        float dist = length(vScreenPos);
        float vignette = smoothstep(1.6, 0.4, dist);
        result *= mix(0.55, 1.0, vignette);
    }

    // --- Atmospheric tint at low zoom (subtle blue haze for distant views) ---
    if (uEnableAtmosphere) {
        float atmoFactor = smoothstep(0.0, 1.0, 1.0 - clamp(uZoomNorm, 0.0, 1.0)) * 0.10;
        result = mix(result, vec3(0.30, 0.40, 0.55), atmoFactor);
    }

    FragColor = vec4(result, c.a * opacity);
}
