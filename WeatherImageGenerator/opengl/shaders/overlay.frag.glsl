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

    // Anti-aliased rendering: fade edges based on distance from line center
    float aa = 1.0 - smoothstep(0.4, 1.0, abs(vLineCoord.x));
    finalAlpha *= aa;

    FragColor = vec4(uColor, finalAlpha);
}