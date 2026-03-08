#version 330 core
// GRIB2 Atmosphere - Vertex Shader (OpenGL 3.3)
// Shared vertex shader for day/night terminator and CAPE instability effects.

layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;

uniform mat3 uTransform;

out vec2 vTex;
out vec2 vScreenPos;

void main() {
    vec3 p = uTransform * vec3(aPos, 1.0);
    gl_Position = vec4(p.xy, 0.0, 1.0);
    vTex = aTex;
    vScreenPos = p.xy;
}
