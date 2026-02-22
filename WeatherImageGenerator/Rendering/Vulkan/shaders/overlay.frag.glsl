#version 450

layout(location=0) in vec2 vLineCoord;

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PC {
    float uOffsetX;
    float uOffsetY;
    float uColorR;
    float uColorG;
    float uColorB;
    float uAlpha;
    float uTime;
    float uEnablePulse;
} pc;

void main() {
    float pulse = pc.uEnablePulse > 0.5 ? (0.85 + 0.15 * sin(pc.uTime * 2.5)) : 1.0;
    float finalAlpha = pc.uAlpha * pulse;

    float edge = abs(vLineCoord.x);
    float inner = 1.0 - smoothstep(0.28, 0.48, edge);
    float outer = 1.0 - smoothstep(0.60, 1.0, edge);

    vec3 col = mix(vec3(0.0), vec3(pc.uColorR, pc.uColorG, pc.uColorB), inner);
    finalAlpha *= outer;

    FragColor = vec4(col, finalAlpha);
}
