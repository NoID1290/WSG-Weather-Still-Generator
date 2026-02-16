#version 330 core
in vec2 vTex;
in vec2 vScreenPos;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;

void main() {
    // Bitmap data is top-left origin; flip Y when sampling for correct orientation
    vec4 c = texture(uTexture, vec2(vTex.x, 1.0 - vTex.y));
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;

    // --- Saturation boost ---
    float luma = dot(c.rgb, vec3(0.2126, 0.7152, 0.0722));
    vec3 saturated = mix(vec3(luma), c.rgb, 1.18); // subtle 18% boost

    // --- Contrast S-curve ---
    vec3 contrasted = smoothstep(vec3(-0.02), vec3(1.02), saturated);

    // --- Vignette ---
    float dist = length(vScreenPos); // distance from center in NDC (0..~1.4)
    float vignette = 1.0 - smoothstep(0.8, 1.8, dist) * 0.22; // subtle darkening at edges

    // --- Anti-aliased tile edge blending ---
    float edgeFade = 1.0;
    float edgeWidth = 0.005;
    edgeFade *= smoothstep(0.0, edgeWidth, vTex.x);
    edgeFade *= smoothstep(0.0, edgeWidth, 1.0 - vTex.x);
    edgeFade *= smoothstep(0.0, edgeWidth, vTex.y);
    edgeFade *= smoothstep(0.0, edgeWidth, 1.0 - vTex.y);

    vec3 finalColor = contrasted * vignette;
    FragColor = vec4(finalColor, c.a * opacity * edgeFade);
} 