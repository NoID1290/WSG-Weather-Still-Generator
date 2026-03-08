#version 450
// GRIB2 Atmosphere — Vertex Shader (Vulkan)
// Day/night terminator + CAPE instability.

layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;

layout(push_constant) uniform PC {
    vec4 row0;
    vec4 row1;
    vec4 row2;
    float uTime;
    float uOpacity;
    float uSolarDeclination;
    float uSubsolarLon;
    float uEnableTerminator;
    float uEnableCape;
    float uCapeDataMin;
    float uCapeDataMax;
    vec4  uViewBounds;  // minLat, minLon, maxLat, maxLon
} pc;

layout(location=0) out vec2 vTex;
layout(location=1) out vec2 vScreenPos;

void main() {
    mat3 xform = mat3(pc.row0.xyz, pc.row1.xyz, pc.row2.xyz);
    vec3 p = xform * vec3(aPos, 1.0);
    gl_Position = vec4(p.xy, 0.0, 1.0);
    vTex = aTex;
    vScreenPos = p.xy;
}
