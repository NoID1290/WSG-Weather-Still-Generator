#version 450

layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;

layout(set=0, binding=0) uniform ProjectionUBO {
    mat4 uProjection;
};

layout(location=0) out vec2 vTex;

void main() {
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
    vTex = aTex;
}
