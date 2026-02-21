#version 330 core
in vec2 vLineCoord;  // passed from vertex shader for AA
out vec4 FragColor;
uniform vec3 uColor;
uniform float uAlpha;
uniform float uTime;  // elapsed seconds for pulse animation
uniform bool uEnablePulse; // toggle crosshair pulse animation

void main() {
    // Soft pulse animation for center crosshair (gentle breathing effect)
    float pulse = uEnablePulse ? (0.85 + 0.15 * sin(uTime * 2.5)) : 1.0;
    float finalAlpha = uAlpha * pulse;

    float edge = abs(vLineCoord.x);

    // Inner fill (the colored line): smooth transition for green core
    float inner = 1.0 - smoothstep(0.28, 0.48, edge);
    // Outer border (black outline): wider, smoother falloff at the edge
    float outer = 1.0 - smoothstep(0.60, 1.0, edge);

    // Mix: black outline where outer is visible but inner is not
    vec3 col = mix(vec3(0.0), uColor, inner);
    finalAlpha *= outer;

    FragColor = vec4(col, finalAlpha);
}