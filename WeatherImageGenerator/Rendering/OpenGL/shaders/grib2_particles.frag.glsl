#version 330 core
// GRIB2 Rain/Snow Particles - Fragment Shader (OpenGL 3.3)
// Renders point sprites as rain streaks or snowflakes depending on type.

in float vLife;
in float vSize;
in float vType;

out vec4 FragColor;

uniform float uTime;
uniform float uOpacity;

void main() {
    vec2 pc = gl_PointCoord * 2.0 - 1.0; // [-1, 1]
    float opacity = uOpacity > 0.0 ? uOpacity : 0.6;

    float alpha;
    vec3 color;

    if (vType < 0.5) {
        // -- Rain --
        // Elongated vertical streak
        float streak = 1.0 - abs(pc.x) * 3.0;
        streak *= 1.0 - abs(pc.y) * 0.7;
        streak = max(streak, 0.0);
        alpha = streak * 0.7;
        color = vec3(0.7, 0.8, 1.0); // pale blue
    } else if (vType < 1.5) {
        // -- Snow --
        // Soft circular flake with sparkle
        float dist = length(pc);
        alpha = smoothstep(1.0, 0.3, dist);

        // 6-fold star pattern
        float angle = atan(pc.y, pc.x);
        float star = 0.5 + 0.5 * cos(angle * 6.0);
        alpha *= mix(0.8, 1.0, star * smoothstep(0.6, 0.2, dist));

        // Sparkle
        float sparkle = sin(uTime * 4.0 + gl_PointCoord.x * 20.0) * 0.15 + 0.85;
        alpha *= sparkle;

        color = vec3(0.95, 0.97, 1.0); // white
    } else {
        // -- Mixed --
        float dist = length(pc);
        float streak = 1.0 - abs(pc.x) * 2.5;
        streak *= 1.0 - abs(pc.y) * 0.8;
        streak = max(streak, 0.0);
        float flake = smoothstep(1.0, 0.4, dist);
        alpha = mix(streak * 0.6, flake * 0.5, 0.5);
        color = vec3(0.8, 0.85, 0.95); // pale
    }

    // Fade with remaining life
    float lifeFade = smoothstep(0.0, 0.3, vLife);
    alpha *= lifeFade * opacity;

    if (alpha < 0.01) discard;
    FragColor = vec4(color, alpha);
}
