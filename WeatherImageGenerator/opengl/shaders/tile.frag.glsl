#version 330 core
in vec2 vTex;
in vec2 vScreenPos;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;

void main() {
    // Bitmap data is top-left origin; flip Y when sampling for correct orientation
    vec4 c = texture(uTexture, vec2(vTex.x, 1.0 - vTex.y));
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;

    // --- Subtle saturation boost ---
    float luma = dot(c.rgb, vec3(0.2126, 0.7152, 0.0722));
    vec3 saturated = mix(vec3(luma), c.rgb, 1.12); // gentle 12% boost

    // --- Mild contrast ---
    vec3 contrasted = smoothstep(vec3(-0.01), vec3(1.01), saturated);

    // No edge fade - tiles must be seamless (edge blending causes visible seams)
    // No per-tile vignette - vignette must be applied screen-wide, not per-tile

    FragColor = vec4(contrasted, c.a * opacity);
}
