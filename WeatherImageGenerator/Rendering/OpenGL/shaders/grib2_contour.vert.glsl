#version 330 core
// GRIB2 Contour Lines - Vertex Shader (OpenGL 3.3)

layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;

uniform mat3 uTransform;

out vec2 vTex;

void main() {
    vec3 p = uTransform * vec3(aPos, 1.0);
    gl_Position = vec4(p.xy, 0.0, 1.0);
    vTex = aTex;
}
