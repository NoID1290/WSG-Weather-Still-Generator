#version 330 core
in vec2 vTex;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;
void main() {
    // Bitmap data is top-left origin; flip Y when sampling for correct orientation
    vec4 c = texture(uTexture, vec2(vTex.x, 1.0 - vTex.y));
    // uOpacity defaults to 1.0 (set on init); overlays may use lower values
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;
    FragColor = vec4(c.rgb, c.a * opacity);
} 