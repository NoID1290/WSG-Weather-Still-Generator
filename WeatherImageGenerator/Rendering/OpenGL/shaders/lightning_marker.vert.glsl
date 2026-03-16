#version 330 core

// Lightning strike marker vertex shader - OpenGL 3.3
// Positions a sprite quad in NDC space from per-draw uniforms.
// Passes age (0=recent, 1=oldest) and CG flag to the fragment shader.

layout(location = 0) in vec2 aPos;  // not used – kept for VAO compat
layout(location = 1) in vec2 aTex;  // quad UV [0,1], remapped to [-1,+1]

uniform float uNdcX;
uniform float uNdcY;
uniform float uHalfSizeX;
uniform float uHalfSizeY;
uniform float uAge;    // 0.0 = just occurred, 1.0 = oldest in window
uniform float uIsCG;   // 1.0 = cloud-to-ground (yellow), 0.0 = in-cloud (blue)

out vec2  vUv;
out float vAge;
out float vIsCG;

void main() {
    vec2 uv = aTex * 2.0 - 1.0;
    vUv     = uv;
    vAge    = uAge;
    vIsCG   = uIsCG;
    gl_Position = vec4(uNdcX + uv.x * uHalfSizeX,
                       uNdcY + uv.y * uHalfSizeY,
                       0.0, 1.0);
}
