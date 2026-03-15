#version 330 core

// Station/epicenter marker vertex shader - OpenGL 3.3
// Reuses bound quad VAO (pos+tex) and positions sprite from uniforms.

layout(location = 0) in vec2 aPos;  // not used - kept for VAO compat
layout(location = 1) in vec2 aTex;  // quad UV [0,1], remapped to [-1,+1]

uniform float uNdcX;
uniform float uNdcY;
uniform float uHalfSizeX;
uniform float uHalfSizeY;
uniform float uMarkerType;

out vec2 vUv;
out float vType;

void main() {
    vec2 uv = aTex * 2.0 - 1.0;
    vUv     = uv;
    vType   = uMarkerType;
    gl_Position = vec4(uNdcX + uv.x * uHalfSizeX,
                       uNdcY + uv.y * uHalfSizeY,
                       0.0, 1.0);
}
