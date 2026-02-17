#version 330 core
in vec2 vTex;
in vec2 vScreenPos;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;
uniform float uTime;       // elapsed seconds for subtle animation

// Professional weather overlay shader with smooth edge blending,
// subtle glow for high-intensity precipitation, and gamma-correct output.
void main() {
    // Bitmap data is top-left origin; flip Y for correct orientation
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);
    vec4 c = texture(uTexture, uv);
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;

    // --- Smooth alpha edge blend ---
    // Soften harsh cutoff at overlay boundaries for professional look
    float edgeFade = 1.0;
    float border = 0.015; // UV border width
    edgeFade *= smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x);
    edgeFade *= smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    // --- Lightweight 4-tap glow for high-intensity areas ---
    vec2 texelSize = 1.0 / vec2(textureSize(uTexture, 0));
    float bloomScale = 2.5; // spread
    float selfIntensity = dot(c.rgb, vec3(0.299, 0.587, 0.114));
    float bloomSum = 0.0;
    bloomSum += dot(texture(uTexture, uv + vec2( texelSize.x * bloomScale,  0.0)).rgb, vec3(0.333));
    bloomSum += dot(texture(uTexture, uv + vec2(-texelSize.x * bloomScale,  0.0)).rgb, vec3(0.333));
    bloomSum += dot(texture(uTexture, uv + vec2(0.0,  texelSize.y * bloomScale)).rgb, vec3(0.333));
    bloomSum += dot(texture(uTexture, uv + vec2(0.0, -texelSize.y * bloomScale)).rgb, vec3(0.333));
    float bloomAvg = bloomSum / 4.0;

    // Only add glow for bright precipitation returns
    float glowStrength = smoothstep(0.3, 0.8, bloomAvg) * 0.18;
    vec3 glowTint = c.rgb * (1.0 + glowStrength);
    vec3 finalColor = mix(c.rgb, glowTint, step(0.05, selfIntensity));

    // --- sRGB gamma-correct output ---
    finalColor = pow(finalColor, vec3(1.0 / 1.05)); // very subtle gamma lift

    // Combine: preserve original alpha, apply opacity + edge fade
    float finalAlpha = c.a * opacity * edgeFade;

    FragColor = vec4(finalColor, finalAlpha);
}
