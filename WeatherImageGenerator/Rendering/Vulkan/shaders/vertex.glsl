#version 450

layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;

layout(set=0, binding=0) uniform TransformUBO {
    mat3 uTransform;
};

layout(location=0) out vec2 vTex;
layout(location=1) out vec2 vScreenPos;

void main() {
    vec3 p = uTransform * vec3(aPos, 1.0);
    gl_Position = vec4(p.xy, 0.0, 1.0);
    vTex = aTex;
    vScreenPos = p.xy;
}
