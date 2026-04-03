#version 330 core

in vec2 vTex;
in vec2 vNdc;
out vec4 FragColor;

uniform float uTime;
uniform float uCloudCoverage;
uniform vec2  uSunDir;
uniform float uCloudDensity;
uniform float uCloudContrast;
uniform float uCloudBrightness;
uniform float uRaymarchSteps;
uniform sampler2D uRadarTex;
uniform mat3  uRadarTransform;
uniform float uRadarPresent;
uniform float uOpacityMultiplier;
uniform vec3  uDarkCloudColor;
uniform vec3  uBrightCloudColor;
uniform float uRadarThreshold;
uniform float uRadarMaskUpper;
uniform float uRadarSpreadStep;
uniform float uRadarSpreadInfluence;
uniform float uStormDarkening;

uniform vec2  uStrikeNdc[32];
uniform int   uStrikeCount;
uniform float uStrikeFlash[32];

float sat(float x) { return clamp(x, 0.0, 1.0); }

vec2 ndcToRadarUv(vec2 ndcPos)
{
    vec3 radarCoord = uRadarTransform * vec3(ndcPos, 1.0);
    vec2 ruv = radarCoord.xy;
    ruv.y = 1.0 - ruv.y;
    return ruv;
}

vec3 sampleRadar(vec2 ndcPos)
{
    vec2 radarUv = ndcToRadarUv(ndcPos);

    if (radarUv.x < 0.0 || radarUv.x > 1.0 || radarUv.y < 0.0 || radarUv.y > 1.0)
    {
        return vec3(0.0);
    }

    vec4 rc = texture(uRadarTex, radarUv);

    // Map radar color to precipitation intensity (0=lightest, 1=heaviest)
    // Radar palette: blue/purple(light) -> green(moderate) -> yellow/orange(heavy) -> red(extreme)
    float precipIntensity = sat(
        rc.r * 0.7
        - rc.b * 0.5
        + sat(rc.r - rc.g) * 0.5
        + sat(rc.g - rc.b) * 0.15
    );

    return vec3(rc.a, precipIntensity, dot(rc.rgb, vec3(0.299, 0.587, 0.114)));
}

float hash12(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

vec2 hash22(vec2 p)
{
    return vec2(
        fract(sin(dot(p, vec2(269.5, 183.3))) * 43758.5453),
        fract(sin(dot(p, vec2(419.2, 371.9))) * 43758.5453)
    );
}

float remap(float x, float a, float b, float c, float d)
{
    float t = (x - a) / max(1e-4, (b - a));
    return mix(c, d, t);
}

float perlinNoise(vec2 x)
{
    vec2 i = floor(x);
    vec2 f = fract(x);

    float a = hash12(i);
    float b = hash12(i + vec2(1.0, 0.0));
    float c = hash12(i + vec2(0.0, 1.0));
    float d = hash12(i + vec2(1.0, 1.0));

    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

vec2 curlNoise(vec2 uv)
{
    vec2 eps = vec2(0.0, 1.0);

    float n1 = perlinNoise(uv + eps);
    float n2 = perlinNoise(uv - eps);
    float a = (n1 - n2) / (2.0 * eps.y);

    n1 = perlinNoise(uv + eps.yx);
    n2 = perlinNoise(uv - eps.yx);
    float b = (n1 - n2) / (2.0 * eps.y);

    return vec2(a, -b);
}

float worleyNoise(vec2 uv, float freq, float t, bool useCurl)
{
    uv *= freq;
    uv += t + (useCurl ? curlNoise(uv * 2.0) : vec2(0.0));

    vec2 id = floor(uv);
    vec2 gv = fract(uv);

    float minDist = 100.0;
    for (float y = -1.0; y <= 1.0; y += 1.0)
    {
        for (float x = -1.0; x <= 1.0; x += 1.0)
        {
            vec2 offset = vec2(x, y);
            vec2 h = hash22(id + offset) * 0.8 + 0.1;
            h += offset;
            vec2 d = gv - h;
            minDist = min(minDist, dot(d, d));
        }
    }

    return minDist;
}

float perlinFbm(vec2 uv, float freq, float t)
{
    uv *= freq;
    uv += t;
    float amp = 0.5;
    float n = 0.0;
    for (int i = 0; i < 8; ++i)
    {
        n += amp * perlinNoise(uv);
        uv *= 1.9;
        amp *= 0.55;
    }
    return n;
}

vec4 worleyFbm(vec2 uv, float freq, float t, bool useCurl)
{
    float w0 = (freq < 4.0) ? (1.0 - worleyNoise(uv, freq * 1.0, t * 1.0, false)) : 0.0;
    float w1 = 1.0 - worleyNoise(uv, freq * 2.0, t * 2.0, useCurl);
    float w2 = 1.0 - worleyNoise(uv, freq * 4.0, t * 4.0, useCurl);
    float w3 = 1.0 - worleyNoise(uv, freq * 8.0, t * 8.0, useCurl);
    float w4 = 1.0 - worleyNoise(uv, freq * 16.0, t * 16.0, useCurl);

    float fbm0 = (freq > 4.0) ? 0.0 : (w0 * 0.625 + w1 * 0.25 + w2 * 0.125);
    float fbm1 = w1 * 0.625 + w2 * 0.25 + w3 * 0.125;
    float fbm2 = w2 * 0.625 + w3 * 0.25 + w4 * 0.125;
    float fbm3 = w3 * 0.75 + w4 * 0.25;
    return vec4(fbm0, fbm1, fbm2, fbm3);
}

float cloudShape(vec2 uv, float t)
{
    float coverageBase = sat(uCloudCoverage) * 1.45;
    float coverage = hash12(vec2(uv.x * 1.6, uv.y)) * 0.1 + (coverageBase * 0.5 + 0.5);
    coverage = sat(coverage * uCloudDensity);

    float pfbm = perlinFbm(uv, 2.0, t);
    vec4 lowW = worleyFbm(uv, 1.6, t * 1.55, false);
    vec4 highW = worleyFbm(uv, 8.0, t * 4.6, true);

    float perlinWorley = remap(abs(pfbm * 2.0 - 1.0), 1.0 - lowW.r, 1.0, 0.0, 1.0);
    perlinWorley = remap(perlinWorley, 1.0 - coverage, 1.0, 0.0, 1.0) * coverage;

    float worleyLow = lowW.g * 0.625 + lowW.b * 0.25 + lowW.a * 0.125;
    float worleyHigh = highW.g * 0.625 + highW.b * 0.25 + highW.a * 0.125;

    float c = remap(perlinWorley, (worleyLow - 1.0) * 0.64, 1.0, 0.0, 1.0);
    c = remap(c, worleyHigh * 0.18, 1.0, 0.0, 1.0);
    c = pow(sat(c), uCloudContrast);
    return sat(c);
}

void main()
{
    if (uCloudCoverage < 0.01)
    {
        discard;
    }
    if (uRadarPresent < 0.5)
    {
        discard;
    }

    vec3 rs = sampleRadar(vNdc);
    float radarAlpha = rs.x;
    float radarIntensity = rs.y;

    if (radarAlpha < uRadarThreshold)
    {
        discard;
    }

    float spread = 0.0;
    float radarNdcStep = max(0.001, uRadarSpreadStep);
    spread += sampleRadar(vNdc + vec2(radarNdcStep, 0.0)).x;
    spread += sampleRadar(vNdc + vec2(-radarNdcStep, 0.0)).x;
    spread += sampleRadar(vNdc + vec2(0.0, radarNdcStep)).x;
    spread += sampleRadar(vNdc + vec2(0.0, -radarNdcStep)).x;
    spread *= 0.25;

    float radarMask = smoothstep(uRadarThreshold * 0.75, uRadarMaskUpper, radarAlpha + spread * uRadarSpreadInfluence);
    // precipIntensity already maps 0-1 from radar color (blue=0, red=1)
    float stormFactor = sat(radarIntensity);

    // Use geo-anchored radar UV for noise so clouds move/zoom with the map
    vec2 geoUv = ndcToRadarUv(vNdc) * 12.0;

    // Noise adds texture detail only - never creates holes in cloud coverage
    float noiseDetail = perlinFbm(geoUv, 2.0, 0.0);
    float worleyDetail = 1.0 - worleyNoise(geoUv, 3.0, 0.0, true);
    float texture = sat(noiseDetail * 0.5 + worleyDetail * 0.5);
    // Shape is radar-driven: wherever radar exists, clouds exist
    // Noise only modulates between 0.65 and 1.0 for surface detail
    float shape = radarMask * mix(0.65, 1.0, texture);

    // Simple raymarch for lighting using geo-anchored UVs
    vec2 sun = normalize(max(length(uSunDir), 1e-4) * uSunDir + vec2(1e-4, 0.0));
    float steps = clamp(uRaymarchSteps, 4.0, 16.0);
    float invSteps = 1.0 / steps;
    vec2 sunStep = sun * vec2(0.33) * invSteps;
    vec2 marchUv = geoUv;
    float extinction = 1.0;

    for (int i = 0; i < 16; ++i)
    {
        if (float(i) >= steps) break;
        marchUv += sunStep;
        float mNoise = perlinFbm(marchUv, 2.0, 0.0) * 0.5 + 0.5;
        float c = radarMask * mNoise;
        extinction *= clamp(1.0 - c, 0.0, 1.0);
    }

    float cloudLight = exp(-(extinction + 0.03)) * (1.0 - exp(-(extinction + 0.03) * 2.2)) * 2.2;
    cloudLight *= shape;

    // Cloud color driven by radar intensity:
    // High intensity (red pixels) = dark clouds, low intensity (green) = lighter gray
    vec3 darkCol = uDarkCloudColor;
    vec3 brightCol = uBrightCloudColor;
    float intensityDarken = sat(stormFactor);
    vec3 cloudCol = mix(brightCol, darkCol, intensityDarken);
    // Add subtle procedural lighting variation
    cloudCol = mix(cloudCol, brightCol, sat(cloudLight * uCloudBrightness) * 0.3);

    // Lightning - subtle under-cloud illumination glow
    float lightning = 0.0;
    for (int i = 0; i < uStrikeCount && i < 32; ++i)
    {
        float d = distance(vNdc, uStrikeNdc[i]);
        float falloff = 1.0 - smoothstep(0.0, 0.45, d);
        lightning += falloff * uStrikeFlash[i];
    }
    lightning = sat(lightning * 0.35);

    // Warm under-glow instead of bright white wash
    vec3 glowColor = vec3(0.75, 0.70, 0.85);
    cloudCol = mix(cloudCol, glowColor, lightning * shape);

    // Fully opaque where cloud shape exists
    float alpha = sat(shape * 2.5) * radarMask;
    alpha *= max(0.0, uOpacityMultiplier);

    if (alpha < 0.01)
    {
        discard;
    }

    FragColor = vec4(cloudCol, alpha);
}
