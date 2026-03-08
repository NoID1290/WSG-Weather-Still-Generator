#version 330 core
// GRIB2 Contour Lines - Fragment Shader (OpenGL 3.3)
// GPU-rendered isobar and isotherm contour lines using screen-space derivatives.
// Antialiased contour lines with optional value labels.

in vec2 vTex;
out vec4 FragColor;

uniform sampler2D uDataTex;       // R32F field data
uniform float uDataMin;
uniform float uDataMax;
uniform float uContourInterval;   // e.g., 4.0 for isobars every 4 hPa
uniform vec4  uContourColor;      // RGBA line color
uniform float uContourWidth;      // Line width in texels (1.0-3.0)
uniform float uOpacity;
uniform int   uFieldType;         // 0=Temp, 4=Pressure

void main() {
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);

    // Sample raw data value
    float value = texture(uDataTex, uv).r;

    // Discard missing data
    if (value < uDataMin - 500.0) discard;

    float interval = uContourInterval > 0.0 ? uContourInterval : 4.0;

    // Phase within contour interval: 0 at each contour line
    float phase = value / interval;
    float fractPhase = fract(phase);

    // Screen-space derivative for antialiasing
    float dPhase = fwidth(phase);
    float lineWidth = (uContourWidth > 0.0 ? uContourWidth : 1.5) * 0.5;

    // Antialiased contour: thin line where phase crosses integer
    float contour = 1.0 - smoothstep(0.0, dPhase * lineWidth, min(fractPhase, 1.0 - fractPhase));

    if (contour < 0.01) discard;

    // Major contours (every 5 intervals) get thicker/brighter
    float majorPhase = value / (interval * 5.0);
    float majorFract = fract(majorPhase);
    float dMajor = fwidth(majorPhase);
    float majorContour = 1.0 - smoothstep(0.0, dMajor * lineWidth * 1.5, min(majorFract, 1.0 - majorFract));

    // Blend: major contours are darker/thicker
    vec4 lineColor = uContourColor.a > 0.0 ? uContourColor : vec4(0.2, 0.2, 0.2, 0.85);
    float alpha = mix(contour * 0.6, max(contour, majorContour), majorContour) * lineColor.a;

    // Edge blending
    float border = 0.015;
    float edgeFade = smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x)
                   * smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;
    FragColor = vec4(lineColor.rgb, alpha * edgeFade * opacity);
}
