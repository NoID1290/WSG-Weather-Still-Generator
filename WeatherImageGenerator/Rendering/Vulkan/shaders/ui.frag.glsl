#version 450

layout(location=0) in vec2 vTex;

layout(location=0) out vec4 FragColor;

layout(set=0, binding=1) uniform sampler2D uFontAtlas;
layout(set=0, binding=2) uniform UIParams {
    vec4 uColor;
    int uMode;   // 0 = textured glyph, 1 = flat rect
};

void main() {
    if (uMode == 0) {
        float a = texture(uFontAtlas, vTex).r;
        FragColor = vec4(uColor.rgb, uColor.a * a);
    } else {
        FragColor = uColor;
    }
}
