#version 330 core
in vec2 vTex;
out vec4 FragColor;
uniform sampler2D uTexture;
void main() {
    // Bitmap data is top-left origin; flip Y when sampling for correct orientation
    vec4 c = texture(uTexture, vec2(vTex.x, 1.0 - vTex.y));
    FragColor = c;
} 