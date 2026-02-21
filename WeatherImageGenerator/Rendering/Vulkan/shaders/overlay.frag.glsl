#version 450

layout(location=0) in vec2 vLineCoord;

layout(location=0) out vec4 FragColor;

layout(set=0, binding=0) uniform OverlayParams {
    vec2 _pad_offset; // matches vert UBO layout
    vec3 uColor;
    float uAlpha;
    float uTime;
    bool uEnablePulse;
};

void main() {
    float pulse = uEnablePulse ? (0.85 + 0.15 * sin(uTime * 2.5)) : 1.0;
    float finalAlpha = uAlpha * pulse;

    float edge = abs(vLineCoord.x);

    float inner = 1.0 - smoothstep(0.28, 0.48, edge);
    float outer = 1.0 - smoothstep(0.60, 1.0, edge);

    vec3 col = mix(vec3(0.0), uColor, inner);
    finalAlpha *= outer;

    FragColor = vec4(col, finalAlpha);
}
