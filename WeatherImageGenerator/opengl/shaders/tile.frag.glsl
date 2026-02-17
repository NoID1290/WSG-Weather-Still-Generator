#version 330 core
in vec2 vTex;
in vec2 vScreenPos;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;
uniform float uZoomNorm; // 0.0 = zoomed out (world), 1.0 = zoomed in (street)

void main() {
    // Bitmap data is top-left origin; flip Y when sampling for correct orientation
    vec4 c = texture(uTexture, vec2(vTex.x, 1.0 - vTex.y));
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;

    // --- Saturation boost (12%) ---
    float luma = dot(c.rgb, vec3(0.2126, 0.7152, 0.0722));
    vec3 saturated = mix(vec3(luma), c.rgb, 1.12);

    // --- Mild contrast curve ---
    vec3 contrasted = smoothstep(vec3(-0.01), vec3(1.01), saturated);

    // --- Screen-space vignette (viewport-wide, not per-tile) ---
    // vScreenPos is in NDC (-1..1), so length at corners ≈ 1.41
    float dist = length(vScreenPos);
    float vignette = smoothstep(1.6, 0.4, dist);
    contrasted *= mix(0.55, 1.0, vignette);

    // --- Atmospheric tint at low zoom (subtle blue haze for distant views) ---
    float atmoFactor = smoothstep(0.0, 1.0, 1.0 - clamp(uZoomNorm, 0.0, 1.0)) * 0.10;
    contrasted = mix(contrasted, vec3(0.30, 0.40, 0.55), atmoFactor);

    FragColor = vec4(contrasted, c.a * opacity);
}
