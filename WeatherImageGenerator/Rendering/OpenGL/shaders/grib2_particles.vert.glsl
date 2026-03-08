#version 330 core
// GRIB2 Rain/Snow Particles - Vertex Shader (OpenGL 3.3)
// Transforms particle point sprites. Each particle has position and velocity attributes.

layout(location=0) in vec4 aPosition;  // x, y, z, life
layout(location=1) in vec4 aVelocity;  // vx, vy, size, type (0=rain, 1=snow, 2=mix)

uniform mat3 uTransform;
uniform float uTime;
uniform float uViewportHeight;

out float vLife;
out float vSize;
out float vType;

void main() {
    vec3 p = uTransform * vec3(aPosition.xy, 1.0);
    gl_Position = vec4(p.xy, 0.0, 1.0);

    vLife = aPosition.w;
    vSize = aVelocity.z;
    vType = aVelocity.w;

    // Point size scaled by viewport and particle size attribute
    gl_PointSize = vSize * (uViewportHeight / 800.0);
}
