#version 330 core
layout(location=0) in vec2 aPos;       // NDC coordinates (-1..1)
layout(location=1) in float aLineEdge; // 0 = center, 1 = edge (for AA)
uniform vec2 uOffset;                  // NDC offset for crosshair-as-mouse
out vec2 vLineCoord;
void main() {
    gl_Position = vec4(aPos + uOffset, 0.0, 1.0);
    vLineCoord = vec2(aLineEdge, 0.0);
}