#version 450

layout(location=0) in vec2 vTex;

layout(location=0) out vec4 FragColor;

layout(set=0, binding=0) uniform sampler2D uFontAtlas;

layout(push_constant) uniform PC {
    mat4 uProjection;
    vec4 uColor;
    int uMode;   // 0 = textured glyph, 1 = flat rect
} pc;

void main() {
    if (pc.uMode == 0) {
        float a = texture(uFontAtlas, vTex).r;
        FragColor = vec4(pc.uColor.rgb, pc.uColor.a * a);
    } else {
        FragColor = pc.uColor;
    }
}
