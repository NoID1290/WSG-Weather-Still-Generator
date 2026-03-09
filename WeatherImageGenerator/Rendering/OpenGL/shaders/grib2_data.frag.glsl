#version 330 core
// GRIB2 Data Visualization - Fragment Shader (OpenGL 3.3)
// Samples a single-channel float (R32F) data texture containing weather grid values,
// normalizes to [0,1], and looks up color from a 1D RGBA palette texture.
// Uses Mercator-corrected UV mapping to align data sampling with map tiles.

in vec2 vTex;
in vec2 vScreenPos;
out vec4 FragColor;

uniform sampler2D uDataTex;     // R32F grid data (Ni x Nj)
uniform sampler1D uPaletteTex;  // RGBA8 color palette (256 texels)

uniform float uDataMin;         // Field minimum in display units
uniform float uDataMax;         // Field maximum in display units
uniform float uOpacity;         // Overall layer opacity (0-1)
uniform float uTime;            // Elapsed seconds for animation
uniform float uEnableGlow;      // 1.0 = glow on, 0.0 = off
uniform int   uFieldType;       // 0=Temp, 1=Wind, 2=Precip, 3=Cloud, 4=Pressure, 5=CAPE

// Viewport-to-grid mapping (Mercator projection)
uniform float uViewMercMin;     // Mercator Y at viewport bottom (MinLat)
uniform float uViewMercMax;     // Mercator Y at viewport top (MaxLat)
uniform float uViewMinLon;      // Viewport minimum longitude (degrees)
uniform float uViewLonRange;    // Viewport longitude range (degrees)
uniform float uGridFirstLat;    // Grid first latitude (row 0 of texture)
uniform float uGridLatExtent;   // Grid lat extent: LastLat - FirstLat (can be negative for N->S)
uniform float uGridMinLon;      // Grid minimum longitude (degrees, e.g. 0 for GDPS)
uniform float uGridLonRange;    // Grid longitude range (degrees, e.g. 359.75 for GDPS)

void main() {
    // -- Convert screen UV to geographic coordinates --
    // vTex.y: 0 = bottom (MinLat), 1 = top (MaxLat) in Mercator space
    float mercY = mix(uViewMercMin, uViewMercMax, vTex.y);
    float lat = atan(sinh(mercY)) * (180.0 / 3.14159265);

    // vTex.x: 0 = left (MinLon), 1 = right (MaxLon) - longitude is linear
    float lon = uViewMinLon + vTex.x * uViewLonRange;

    // -- Map geographic coords to data grid texture UV --
    // Grid V: texture V=0 is row 0 = FirstLat, V=1 is last row = LastLat
    // Works for both S->N (extent > 0) and N->S (extent < 0) scanning
    float gridV = (lat - uGridFirstLat) / uGridLatExtent;

    // Grid U: Normalize longitude to grid range (handles wrapping grids like FirstLon=180)
    float gridLon = lon;
    if (gridLon < uGridMinLon)
        gridLon += 360.0;
    float gridU = (gridLon - uGridMinLon) / max(uGridLonRange, 0.001);

    vec2 uv = clamp(vec2(gridU, gridV), vec2(0.001), vec2(0.999));

    // Sample raw data value from R32F texture (GPU bilinear interpolation)
    float rawValue = texture(uDataTex, uv).r;

    // Normalize to [0,1] for palette lookup
    float range = uDataMax - uDataMin;
    float t = clamp((rawValue - uDataMin) / max(range, 0.001), 0.0, 1.0);

    // Discard missing data (sentinel values far below min)
    if (rawValue < uDataMin - 500.0) discard;

    // Sample color palette
    vec4 paletteColor = texture(uPaletteTex, t);

    // --- Smooth edge blending (use screen-space vTex, not grid UV) ---
    float edgeFade = 1.0;
    float border = 0.012;
    edgeFade *= smoothstep(0.0, border, vTex.x) * smoothstep(0.0, border, 1.0 - vTex.x);
    edgeFade *= smoothstep(0.0, border, vTex.y) * smoothstep(0.0, border, 1.0 - vTex.y);

    vec3 color = paletteColor.rgb;

    // --- Glow for high-intensity areas ---
    if (uEnableGlow > 0.5) {
        vec2 texelSize = 1.0 / vec2(textureSize(uDataTex, 0));

        // Sample 4 neighbors to compute local intensity gradient
        float n0 = texture(uDataTex, uv + vec2( texelSize.x * 3.0,  0.0)).r;
        float n1 = texture(uDataTex, uv + vec2(-texelSize.x * 3.0,  0.0)).r;
        float n2 = texture(uDataTex, uv + vec2(0.0,  texelSize.y * 3.0)).r;
        float n3 = texture(uDataTex, uv + vec2(0.0, -texelSize.y * 3.0)).r;

        float avgNeighbor = (n0 + n1 + n2 + n3) / 4.0;
        float neighborT = clamp((avgNeighbor - uDataMin) / max(range, 0.001), 0.0, 1.0);

        float glowStrength = smoothstep(0.4, 0.85, neighborT) * 0.22;
        color = mix(color, color * (1.0 + glowStrength), step(0.1, t));

        // Subtle pulse for extreme values
        if (t > 0.85) {
            float pulse = sin(uTime * 2.5) * 0.04 + 0.04;
            color += color * pulse;
        }
    }

    // --- sRGB gamma correction ---
    color = pow(color, vec3(1.0 / 1.08));

    // --- Transparency logic ---
    // For precipitation and cloud cover, low values should be more transparent
    float alphaScale = 1.0;
    if (uFieldType == 2) { // Precipitation
        alphaScale = smoothstep(0.0, 0.02, t);
    } else if (uFieldType == 3) { // CloudCover
        alphaScale = smoothstep(0.0, 0.08, t) * 0.85;
    }

    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;
    float finalAlpha = paletteColor.a * opacity * edgeFade * alphaScale;

    FragColor = vec4(color, finalAlpha);
}
