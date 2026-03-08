#version 450
// GRIB2 Contour Lines — Fragment Shader (Vulkan)
// Antialiased GPU contour lines using screen-space derivatives.

layout(location=0) in vec2 vTex;
layout(location=0) out vec4 FragColor;

layout(set=0, binding=0) uniform sampler2D uDataTex;

layout(push_constant) uniform PC {
    vec4 row0;
    vec4 row1;
    vec4 row2;
    float uDataMin;
    float uDataMax;
    float uContourInterval;
    float uContourWidth;
    float uOpacity;
    vec4  uContourColor;
} pc;

void main() {
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);
    float value = texture(uDataTex, uv).r;

    if (value < pc.uDataMin - 500.0) discard;

    float interval = pc.uContourInterval > 0.0 ? pc.uContourInterval : 4.0;
    float phase = value / interval;
    float fractPhase = fract(phase);
    float dPhase = fwidth(phase);
    float lineWidth = (pc.uContourWidth > 0.0 ? pc.uContourWidth : 1.5) * 0.5;

    float contour = 1.0 - smoothstep(0.0, dPhase * lineWidth, min(fractPhase, 1.0 - fractPhase));
    if (contour < 0.01) discard;

    float majorPhase = value / (interval * 5.0);
    float majorFract = fract(majorPhase);
    float dMajor = fwidth(majorPhase);
    float majorContour = 1.0 - smoothstep(0.0, dMajor * lineWidth * 1.5, min(majorFract, 1.0 - majorFract));

    vec4 lineColor = pc.uContourColor.a > 0.0 ? pc.uContourColor : vec4(0.2, 0.2, 0.2, 0.85);
    float alpha = mix(contour * 0.6, max(contour, majorContour), majorContour) * lineColor.a;

    float border = 0.015;
    float edgeFade = smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x)
                   * smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    float opacity = pc.uOpacity > 0.0 ? pc.uOpacity : 1.0;
    FragColor = vec4(lineColor.rgb, alpha * edgeFade * opacity);
}
