#version 330 core
in vec2 vTex;
out vec4 FragColor;

uniform sampler2D uFontAtlas;
uniform vec4 uColor;         // text or rect color
uniform int uMode;           // 0 = textured glyph, 1 = flat rect

void main() {
    if (uMode == 0) {
        // Text mode: sample font atlas alpha channel
        float a = texture(uFontAtlas, vTex).r;
        FragColor = vec4(uColor.rgb, uColor.a * a);
    } else {
        // Rect mode: flat color with optional alpha
        FragColor = uColor;
    }
}
