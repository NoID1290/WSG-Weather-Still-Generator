#version 330 core
// GRIB2 Atmosphere - Fragment Shader (OpenGL 3.3)
// Combines day/night terminator overlay with CAPE instability highlighting.
// Day/night: darkens night hemisphere with smooth twilight gradient.
// CAPE: pulsing warm tint for high convective potential, expanding rings for extreme values.

in vec2 vTex;
in vec2 vScreenPos;
out vec4 FragColor;

uniform sampler2D uCapeData;      // R32F CAPE data (J/kg), or empty
uniform float uTime;
uniform float uOpacity;

// Day/night terminator parameters
uniform float uSolarDeclination;  // Degrees
uniform float uSubsolarLon;      // Degrees
uniform float uEnableTerminator;  // 1.0 = on, 0.0 = off

// CAPE parameters
uniform float uEnableCape;        // 1.0 = on
uniform float uCapeDataMin;       // 0
uniform float uCapeDataMax;       // 5000

// Viewport geographic bounds (passed as uniforms for lat/lon mapping)
uniform vec4 uViewBounds;         // (minLat, minLon, maxLat, maxLon)

const float PI = 3.14159265359;
const float DEG2RAD = PI / 180.0;

void main() {
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);

    // Map UV to geographic coordinates
    float lat = mix(uViewBounds.x, uViewBounds.z, uv.y); // minLat -> maxLat
    float lon = mix(uViewBounds.y, uViewBounds.w, uv.x); // minLon -> maxLon

    vec3 color = vec3(0.0);
    float alpha = 0.0;

    // ===========================================
    // Day/Night Terminator
    // ===========================================
    if (uEnableTerminator > 0.5) {
        float latRad = lat * DEG2RAD;
        float decRad = uSolarDeclination * DEG2RAD;
        float hourAngleRad = (lon - uSubsolarLon) * DEG2RAD;

        // Solar elevation angle at this point
        float sinElev = sin(latRad) * sin(decRad)
                      + cos(latRad) * cos(decRad) * cos(hourAngleRad);
        float elevation = asin(clamp(sinElev, -1.0, 1.0));
        float elevDeg = elevation / DEG2RAD;

        // Twilight gradient: 6 deg civil twilight band
        // elevDeg > 0 = full day, < -6 = full night
        float nightAmount = smoothstep(1.0, -6.0, elevDeg);

        // Night tint: dark blue-black
        vec3 nightColor = vec3(0.02, 0.03, 0.08);
        color = nightColor;
        alpha = nightAmount * 0.55;
    }

    // ===========================================
    // CAPE Instability Highlighting
    // ===========================================
    if (uEnableCape > 0.5) {
        float cape = texture(uCapeData, uv).r;

        if (cape > uCapeDataMin + 50.0) {
            float capeNorm = clamp((cape - uCapeDataMin) / max(uCapeDataMax - uCapeDataMin, 1.0), 0.0, 1.0);

            // Warm tint proportional to CAPE
            vec3 capeColor = mix(
                vec3(1.0, 0.9, 0.3),    // moderate: warm yellow
                vec3(1.0, 0.2, 0.1),    // extreme: hot red
                smoothstep(0.2, 0.8, capeNorm)
            );

            // Pulsing intensity for high CAPE
            float pulse = 1.0;
            if (capeNorm > 0.4) {
                pulse = 0.85 + 0.15 * sin(uTime * 3.0 + uv.x * 10.0 + uv.y * 8.0);
            }

            // Expanding concentric rings for extreme CAPE (>3000 J/kg)
            float ringAlpha = 0.0;
            if (capeNorm > 0.6) {
                float ringDist = length((uv - 0.5) * 2.0); // could use per-cell center
                float ringPhase = fract(ringDist * 3.0 - uTime * 0.5);
                ringAlpha = smoothstep(0.0, 0.1, ringPhase) * (1.0 - smoothstep(0.1, 0.2, ringPhase));
                ringAlpha *= (capeNorm - 0.6) * 2.0;
            }

            // Heat shimmer UV distortion
            float shimmer = 0.0;
            if (capeNorm > 0.5) {
                shimmer = sin(uv.y * 50.0 + uTime * 5.0) * 0.003 * capeNorm;
            }

            vec2 distortedUV = uv + vec2(shimmer, shimmer * 0.5);
            float distortedCape = texture(uCapeData, distortedUV).r;

            float capeAlpha = smoothstep(0.05, 0.25, capeNorm) * 0.35 * pulse + ringAlpha * 0.2;

            // Composite CAPE over terminator
            color = mix(color, capeColor, capeAlpha);
            alpha = max(alpha, capeAlpha);
        }
    }

    // Edge blending
    float border = 0.015;
    float edgeFade = smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x)
                   * smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);

    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;
    FragColor = vec4(color, alpha * edgeFade * opacity);
}
