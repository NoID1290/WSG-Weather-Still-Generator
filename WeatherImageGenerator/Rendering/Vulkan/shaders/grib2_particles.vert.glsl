#version 450
// GRIB2 Rain/Snow Particles — Vertex Shader (Vulkan)

layout(location=0) in vec4 aPosition;  // x, y, z, life
layout(location=1) in vec4 aVelocity;  // vx, vy, size, type

layout(push_constant) uniform PC {
    vec4 row0;
    vec4 row1;
    vec4 row2;
    float uTime;
    float uViewportHeight;
    float uOpacity;
} pc;

layout(location=0) out float vLife;
layout(location=1) out float vSize;
layout(location=2) out float vType;

void main() {
    mat3 xform = mat3(pc.row0.xyz, pc.row1.xyz, pc.row2.xyz);
    vec3 p = xform * vec3(aPosition.xy, 1.0);
    gl_Position = vec4(p.xy, 0.0, 1.0);

    vLife = aPosition.w;
    vSize = aVelocity.z;
    vType = aVelocity.w;

    gl_PointSize = vSize * (pc.uViewportHeight / 800.0);
}
