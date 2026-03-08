#version 330 core
// GRIB2 Wind Streamlines - Fragment Shader (OpenGL 3.3)
// Texture-advection wind visualization inspired by earth.nullschool.net.
// Reads previous frame's trail texture, advects particles by sampling U/V wind grids,
// fades trails, and mixes in new seed particles colored by wind speed.

in vec2 vTex;
out vec4 FragColor;

uniform sampler2D uPrevTrail;   // Previous frame trail (ping-pong read)
uniform sampler2D uWindU;       // U wind component (R32F, m/s)
uniform sampler2D uWindV;       // V wind component (R32F, m/s)
uniform sampler1D uPaletteTex;  // Color palette (speed -> color)
uniform sampler2D uSeedTex;     // Random seed particle positions

uniform float uTrailDecay;     // Decay factor (0.96 typical)
uniform float uSpeedScale;     // Advection speed scale
uniform float uTime;
uniform float uOpacity;
uniform float uDataMin;        // Wind speed min (0)
uniform float uDataMax;        // Wind speed max (160 km/h)

void main() {
    vec2 uv = vTex;

    // Read previous trail (faded)
    vec4 prevColor = texture(uPrevTrail, uv) * uTrailDecay;

    // Sample wind at this position
    float u = texture(uWindU, uv).r;
    float v = texture(uWindV, uv).r;

    // Wind speed for coloring
    float speed = sqrt(u * u + v * v) * 3.6; // m/s -> km/h
    float speedT = clamp((speed - uDataMin) / max(uDataMax - uDataMin, 0.001), 0.0, 1.0);

    // Advect: trace back to where this pixel's color came from
    vec2 windOffset = vec2(u, -v) * uSpeedScale; // flip v for screen coords
    vec2 srcUV = uv - windOffset;
    vec4 advectedColor = texture(uPrevTrail, srcUV) * uTrailDecay;

    // Seed new particles: sample the seed texture to check if a particle should appear here
    float seed = texture(uSeedTex, uv + vec2(sin(uTime * 1.3) * 0.01, cos(uTime * 0.9) * 0.01)).r;
    bool isSeedParticle = seed > 0.98; // sparse seeding

    vec3 windColor = texture(uPaletteTex, speedT).rgb;

    // Compose: advected trail + optional new seed particle
    vec3 result = advectedColor.rgb;
    float resultAlpha = advectedColor.a;

    if (isSeedParticle && speed > 1.0) {
        result = mix(result, windColor, 0.7);
        resultAlpha = max(resultAlpha, 0.6);
    }

    // Fade calm areas to transparent
    float windAlpha = smoothstep(0.0, 5.0, speed);
    resultAlpha *= windAlpha;

    float opacity = uOpacity > 0.0 ? uOpacity : 0.7;
    FragColor = vec4(result, resultAlpha * opacity);
}
