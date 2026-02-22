#version 450

layout(location=0) in vec2 aPos;
layout(location=1) in float aLineEdge;

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

layout(location=0) out vec2 vLineCoord;

void main() {
    gl_Position = vec4(aPos + vec2(pc.uOffsetX, pc.uOffsetY), 0.0, 1.0);
    vLineCoord = vec2(aLineEdge, 0.0);
}
