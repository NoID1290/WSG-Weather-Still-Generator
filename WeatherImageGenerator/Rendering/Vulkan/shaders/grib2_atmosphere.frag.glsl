#version 450
// GRIB2 Atmosphere — Fragment Shader (Vulkan)
// Day/night terminator and CAPE instability highlighting.

layout(location=0) in vec2 vTex;
layout(location=1) in vec2 vScreenPos;
layout(location=0) out vec4 FragColor;

layout(set=0, binding=0) uniform sampler2D uCapeData;

layout(push_constant) uniform PC {
    vec4 row0;
    vec4 row1;
    vec4 row2;
    float uTime;
    float uOpacity;
    float uSolarDeclination;
    float uSubsolarLon;
    float uEnableTerminator;
    float uEnableCape;
    float uCapeDataMin;
    float uCapeDataMax;
    vec4  uViewBounds;
} pc;

const float PI = 3.14159265359;
const float DEG2RAD = PI / 180.0;

void main() {
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);

    float lat = mix(pc.uViewBounds.x, pc.uViewBounds.z, uv.y);
    float lon = mix(pc.uViewBounds.y, pc.uViewBounds.w, uv.x);

    vec3 color = vec3(0.0);
    float alpha = 0.0;

    // Day/Night Terminator
    if (pc.uEnableTerminator > 0.5) {
        float latRad = lat * DEG2RAD;
        float decRad = pc.uSolarDeclination * DEG2RAD;
        float hourAngleRad = (lon - pc.uSubsolarLon) * DEG2RAD;

        float sinElev = sin(latRad) * sin(decRad)
                      + cos(latRad) * cos(decRad) * cos(hourAngleRad);
        float elevDeg = asin(clamp(sinElev, -1.0, 1.0)) / DEG2RAD;

        float nightAmount = smoothstep(1.0, -6.0, elevDeg);
        color = vec3(0.02, 0.03, 0.08);
        alpha = nightAmount * 0.55;
    }

    // CAPE Instability
    if (pc.uEnableCape > 0.5) {
        float cape = texture(uCapeData, uv).r;
        if (cape > pc.uCapeDataMin + 50.0) {
            float capeNorm = clamp((cape - pc.uCapeDataMin) / max(pc.uCapeDataMax - pc.uCapeDataMin, 1.0), 0.0, 1.0);

            vec3 capeColor = mix(
                vec3(1.0, 0.9, 0.3),
                vec3(1.0, 0.2, 0.1),
                smoothstep(0.2, 0.8, capeNorm)
            );

            float pulse = 1.0;
            if (capeNorm > 0.4) {
                pulse = 0.85 + 0.15 * sin(pc.uTime * 3.0 + uv.x * 10.0 + uv.y * 8.0);
            }

            float ringAlpha = 0.0;
            if (capeNorm > 0.6) {
                float ringDist = length((uv - 0.5) * 2.0);
                float ringPhase = fract(ringDist * 3.0 - pc.uTime * 0.5);
                ringAlpha = smoothstep(0.0, 0.1, ringPhase) * (1.0 - smoothstep(0.1, 0.2, ringPhase));
                ringAlpha *= (capeNorm - 0.6) * 2.0;
            }

            float capeAlpha = smoothstep(0.05, 0.25, capeNorm) * 0.35 * pulse + ringAlpha * 0.2;
            color = mix(color, capeColor, capeAlpha);
            alpha = max(alpha, capeAlpha);
        }
    }

    float border = 0.015;
    float edgeFade = smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x)
                   * smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    float opacity = pc.uOpacity > 0.0 ? pc.uOpacity : 1.0;
    FragColor = vec4(color, alpha * edgeFade * opacity);
}
