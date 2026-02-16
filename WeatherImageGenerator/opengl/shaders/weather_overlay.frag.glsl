#version 330 core
in vec2 vTex;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;

// Clean pass-through shader for weather overlays (radar, temperature, etc.)
// Unlike the tile shader, this does NOT apply saturation boost, contrast curves,
// vignette, or edge fading — those effects corrupt weather data colors.
void main() {
    // Bitmap data is top-left origin; flip Y for correct orientation
    vec4 c = texture(uTexture, vec2(vTex.x, 1.0 - vTex.y));
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;

    // Preserve original radar/weather colors exactly as delivered by WMS
    FragColor = vec4(c.rgb, c.a * opacity);
}
