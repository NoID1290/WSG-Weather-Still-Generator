#version 450

layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;

layout(push_constant) uniform PC {
    mat4 uProjection;
    vec4 uColor;
    int uMode;
} pc;

layout(location=0) out vec2 vTex;

void main() {
    gl_Position = pc.uProjection * vec4(aPos, 0.0, 1.0);
    vTex = aTex;
}
