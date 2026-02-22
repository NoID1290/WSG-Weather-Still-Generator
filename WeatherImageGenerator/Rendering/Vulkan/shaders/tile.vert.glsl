#version 450

layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;

layout(push_constant) uniform PC {
    vec4 row0;   // mat3 row 0 (xyz) + pad
    vec4 row1;   // mat3 row 1 (xyz) + pad
    vec4 row2;   // mat3 row 2 (xyz) + pad
    float uOpacity;
    float uZoomNorm;
    float uEnableSaturation;
    float uEnableContrast;
    float uEnableVignette;
    float uEnableAtmosphere;
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
