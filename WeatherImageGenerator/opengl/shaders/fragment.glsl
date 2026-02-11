#version 330 core
in vec2 vTex;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;

vec3 palette(float t) {
    // smooth blue -> cyan -> green -> yellow -> red
    if (t < 0.25) return mix(vec3(0.0,0.0,0.2), vec3(0.0,0.4,1.0), t/0.25);
    else if (t < 0.5) return mix(vec3(0.0,0.4,1.0), vec3(0.0,0.8,0.0),(t-0.25)/0.25);
    else if (t < 0.75) return mix(vec3(0.0,0.8,0.0), vec3(1.0,1.0,0.0),(t-0.5)/0.25);
    else return mix(vec3(1.0,1.0,0.0), vec3(1.0,0.0,0.0),(t-0.75)/0.25);
}

void main() {
    // Invert Y to correct texture orientation
    vec4 tex = texture(uTexture, vec2(vTex.x, 1.0 - vTex.y));

    // Derive intensity from color (simple average)
    float intensity = dot(tex.rgb, vec3(0.3333));

    // Threshold small values to transparent
    float alpha = tex.a * uOpacity * smoothstep(0.02, 0.05, intensity);

    vec3 color = palette(intensity);
    FragColor = vec4(color, alpha);
}