#version 330 core
in vec2 vTex;
in vec2 vScreenPos;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;
uniform float uTime;       // elapsed seconds for subtle animation
uniform bool uEnableGlow;  // toggle bloom/glow effect

// Professional weather overlay shader with smooth edge blending,
// subtle glow for high-intensity precipitation, and gamma-correct output.
void main() {
    // Bitmap data is top-left origin; flip Y for correct orientation
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);
    vec4 c = texture(uTexture, uv);
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;

    // --- Smooth alpha edge blend ---
    float edgeFade = 1.0;
    float border = 0.015;
    edgeFade *= smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x);
    edgeFade *= smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    vec3 finalColor = c.rgb;

    if (uEnableGlow) {
        // --- Lightweight 4-tap glow for high-intensity areas ---
        vec2 texelSize = 1.0 / vec2(textureSize(uTexture, 0));
        float bloomScale = 2.5;
        float selfIntensity = dot(c.rgb, vec3(0.299, 0.587, 0.114));
        float bloomSum = 0.0;
        bloomSum += dot(texture(uTexture, uv + vec2( texelSize.x * bloomScale,  0.0)).rgb, vec3(0.333));
        bloomSum += dot(texture(uTexture, uv + vec2(-texelSize.x * bloomScale,  0.0)).rgb, vec3(0.333));
        bloomSum += dot(texture(uTexture, uv + vec2(0.0,  texelSize.y * bloomScale)).rgb, vec3(0.333));
        bloomSum += dot(texture(uTexture, uv + vec2(0.0, -texelSize.y * bloomScale)).rgb, vec3(0.333));
        float bloomAvg = bloomSum / 4.0;

        float glowStrength = smoothstep(0.3, 0.8, bloomAvg) * 0.18;
        vec3 glowTint = c.rgb * (1.0 + glowStrength);
        finalColor = mix(c.rgb, glowTint, step(0.05, selfIntensity));
    }

    // --- sRGB gamma-correct output ---
    finalColor = pow(finalColor, vec3(1.0 / 1.05));

    float finalAlpha = c.a * opacity * edgeFade;
    FragColor = vec4(finalColor, finalAlpha);
}
