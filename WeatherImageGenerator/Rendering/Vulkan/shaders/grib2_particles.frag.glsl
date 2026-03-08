#version 450
// GRIB2 Rain/Snow Particles — Fragment Shader (Vulkan)

layout(location=0) in float vLife;
layout(location=1) in float vSize;
layout(location=2) in float vType;

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PC {
    vec4 row0;
    vec4 row1;
    vec4 row2;
    float uTime;
    float uViewportHeight;
    float uOpacity;
} pc;

void main() {
    vec2 ptc = gl_PointCoord * 2.0 - 1.0;
    float opacity = pc.uOpacity > 0.0 ? pc.uOpacity : 0.6;

    float alpha;
    vec3 color;

    if (vType < 0.5) {
        // Rain streak
        float streak = 1.0 - abs(ptc.x) * 3.0;
        streak *= 1.0 - abs(ptc.y) * 0.7;
        streak = max(streak, 0.0);
        alpha = streak * 0.7;
        color = vec3(0.7, 0.8, 1.0);
    } else if (vType < 1.5) {
        // Snow flake
        float dist = length(ptc);
        alpha = smoothstep(1.0, 0.3, dist);
        float angle = atan(ptc.y, ptc.x);
        float star = 0.5 + 0.5 * cos(angle * 6.0);
        alpha *= mix(0.8, 1.0, star * smoothstep(0.6, 0.2, dist));
        float sparkle = sin(pc.uTime * 4.0 + gl_PointCoord.x * 20.0) * 0.15 + 0.85;
        alpha *= sparkle;
        color = vec3(0.95, 0.97, 1.0);
    } else {
        // Mixed
        float dist = length(ptc);
        float streak = 1.0 - abs(ptc.x) * 2.5;
        streak *= 1.0 - abs(ptc.y) * 0.8;
        streak = max(streak, 0.0);
        float flake = smoothstep(1.0, 0.4, dist);
        alpha = mix(streak * 0.6, flake * 0.5, 0.5);
        color = vec3(0.8, 0.85, 0.95);
    }

    float lifeFade = smoothstep(0.0, 0.3, vLife);
    alpha *= lifeFade * opacity;

    if (alpha < 0.01) discard;
    FragColor = vec4(color, alpha);
}
