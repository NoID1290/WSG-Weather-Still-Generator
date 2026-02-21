#version 450

layout(location=0) in vec2 aPos;
layout(location=1) in float aLineEdge;

layout(set=0, binding=0) uniform OverlayVert {
    vec2 uOffset;
};

layout(location=0) out vec2 vLineCoord;

void main() {
    gl_Position = vec4(aPos + uOffset, 0.0, 1.0);
    vLineCoord = vec2(aLineEdge, 0.0);
}
