// Procedural cloud pixel shader - Perlin-Worley + 2D ray-march lighting.

Texture2D    uRadarTex : register(t1);
SamplerState uSampler  : register(s0);

cbuffer ProcCloudsCB : register(b0)
{
    float  uTime;
    float  uCloudCoverage;
    float2 uSunDir;
    float  uCloudDensity;
    float  uCloudContrast;
    float  uCloudBrightness;
    float  uRaymarchSteps;
    float  uRadarPresent;
    int    uStrikeCount;
    float2 uPad0;
    float4 uRadarTransformR0;
    float4 uRadarTransformR1;
    float4 uRadarTransformR2;
    float  uOpacityMultiplier;
    float  uRadarThreshold;
    float  uRadarMaskUpper;
    float  uRadarSpreadStep;
    float  uRadarSpreadInfluence;
    float  uStormDarkening;
    float2 uPad1;
    float3 uDarkCloudColor;
    float  uPad2;
    float3 uBrightCloudColor;
    float  uPad3;
    float4 uStrikeNdcFlash[32]; // xy = NDC pos, z = flash intensity, w = unused
};

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 vTex     : TEXCOORD0;
    float2 vNdc     : TEXCOORD1;
};

float sat(float x) { return clamp(x, 0.0, 1.0); }

float2 ndcToRadarUv(float2 ndcPos)
{
    float3 rc3 = float3(ndcPos, 1.0);
    float2 ruv = float2(
        dot(rc3, float3(uRadarTransformR0.xyz)),
        dot(rc3, float3(uRadarTransformR1.xyz))
    );
    ruv.y = 1.0 - ruv.y;
    return ruv;
}

float3 sampleRadar(float2 ndcPos)
{
    float2 radarUv = ndcToRadarUv(ndcPos);

    if (radarUv.x < 0.0 || radarUv.x > 1.0 || radarUv.y < 0.0 || radarUv.y > 1.0)
        return float3(0.0, 0.0, 0.0);

    float4 rc = uRadarTex.Sample(uSampler, radarUv);

    // Map radar color to precipitation intensity (0=lightest, 1=heaviest)
    // Radar palette: blue/purple(light) -> green(moderate) -> yellow/orange(heavy) -> red(extreme)
    float precipIntensity = sat(
        rc.r * 0.7
        - rc.b * 0.5
        + sat(rc.r - rc.g) * 0.5
        + sat(rc.g - rc.b) * 0.15
    );

    return float3(rc.a, precipIntensity, dot(rc.rgb, float3(0.299, 0.587, 0.114)));
}

float hash12(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float2 hash22(float2 p)
{
    return float2(
        frac(sin(dot(p, float2(269.5, 183.3))) * 43758.5453),
        frac(sin(dot(p, float2(419.2, 371.9))) * 43758.5453)
    );
}

float remap(float x, float a, float b, float c, float d)
{
    float t = (x - a) / max(1e-4, (b - a));
    return lerp(c, d, t);
}

float perlinNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);

    float a = hash12(i);
    float b = hash12(i + float2(1.0, 0.0));
    float c = hash12(i + float2(0.0, 1.0));
    float d = hash12(i + float2(1.0, 1.0));

    f = f * f * (3.0 - 2.0 * f);
    return lerp(a, b, f.x) + (c - a) * f.y * (1.0 - f.x) + (d - b) * f.x * f.y;
}

float2 curlNoise(float2 uv)
{
    float2 eps = float2(0.0, 1.0);

    float n1 = perlinNoise(uv + eps);
    float n2 = perlinNoise(uv - eps);
    float a = (n1 - n2) / (2.0 * eps.y);

    n1 = perlinNoise(uv + eps.yx);
    n2 = perlinNoise(uv - eps.yx);
    float b = (n1 - n2) / (2.0 * eps.y);

    return float2(a, -b);
}

float worleyNoise(float2 uv, float freq, float t, bool useCurl)
{
    uv *= freq;
    uv += t + (useCurl ? curlNoise(uv * 2.0) : float2(0.0, 0.0));

    float2 id = floor(uv);
    float2 gv = frac(uv);

    float minDist = 100.0;
    [unroll]
    for (int y = -1; y <= 1; ++y)
    {
        [unroll]
        for (int x = -1; x <= 1; ++x)
        {
            float2 offset = float2((float)x, (float)y);
            float2 h = hash22(id + offset) * 0.8 + 0.1;
            h += offset;
            float2 d = gv - h;
            minDist = min(minDist, dot(d, d));
        }
    }
    return minDist;
}

float perlinFbm(float2 uv, float freq, float t)
{
    uv *= freq;
    uv += t;
    float amp = 0.5;
    float n = 0.0;
    [unroll]
    for (int i = 0; i < 8; ++i)
    {
        n += amp * perlinNoise(uv);
        uv *= 1.9;
        amp *= 0.55;
    }
    return n;
}

float4 worleyFbm(float2 uv, float freq, float t, bool useCurl)
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
    return float4(fbm0, fbm1, fbm2, fbm3);
}

float cloudShape(float2 uv, float t)
{
    float coverageBase = sat(uCloudCoverage) * 1.45;
    float coverage = hash12(float2(uv.x * 1.6, uv.y)) * 0.1 + (coverageBase * 0.5 + 0.5);
    coverage = sat(coverage * uCloudDensity);

    float pfbm = perlinFbm(uv, 2.0, t);
    float4 lowW = worleyFbm(uv, 1.6, t * 1.55, false);
    float4 highW = worleyFbm(uv, 8.0, t * 4.6, true);

    float perlinWorley = remap(abs(pfbm * 2.0 - 1.0), 1.0 - lowW.x, 1.0, 0.0, 1.0);
    perlinWorley = remap(perlinWorley, 1.0 - coverage, 1.0, 0.0, 1.0) * coverage;

    float worleyLow = lowW.y * 0.625 + lowW.z * 0.25 + lowW.w * 0.125;
    float worleyHigh = highW.y * 0.625 + highW.z * 0.25 + highW.w * 0.125;

    float c = remap(perlinWorley, (worleyLow - 1.0) * 0.64, 1.0, 0.0, 1.0);
    c = remap(c, worleyHigh * 0.18, 1.0, 0.0, 1.0);
    c = pow(sat(c), uCloudContrast);
    return sat(c);
}

float4 main(PS_INPUT input) : SV_TARGET
{
    if (uCloudCoverage < 0.01) discard;
    if (uRadarPresent < 0.5) discard;

    float3 rs = sampleRadar(input.vNdc);
    float radarAlpha = rs.x;
    float radarIntensity = rs.y;
    if (radarAlpha < uRadarThreshold) discard;

    float spread = 0.0;
    float radarNdcStep = max(0.001, uRadarSpreadStep);
    spread += sampleRadar(input.vNdc + float2(radarNdcStep, 0.0)).x;
    spread += sampleRadar(input.vNdc + float2(-radarNdcStep, 0.0)).x;
    spread += sampleRadar(input.vNdc + float2(0.0, radarNdcStep)).x;
    spread += sampleRadar(input.vNdc + float2(0.0, -radarNdcStep)).x;
    spread *= 0.25;

    float radarMask = smoothstep(uRadarThreshold * 0.75, uRadarMaskUpper, radarAlpha + spread * uRadarSpreadInfluence);
    // precipIntensity already maps 0-1 from radar color (blue=0, red=1)
    float stormFactor = sat(radarIntensity);

    // Use geo-anchored radar UV for noise so clouds move/zoom with the map
    float2 geoUv = ndcToRadarUv(input.vNdc) * 12.0;
    // Slow animated time for churning cloud detail (clouds stay in place but shift internally)
    float t = fmod(uTime, 7200.0) * 0.008;

    // Noise adds texture detail only - never creates holes in cloud coverage
    float noiseDetail = perlinFbm(geoUv, 2.0, t);
    float worleyDetail = 1.0 - worleyNoise(geoUv, 3.0, t * 0.5, true);
    float texDetail = sat(noiseDetail * 0.5 + worleyDetail * 0.5);
    // Shape is radar-driven: wherever radar exists, clouds exist
    // Noise only modulates between 0.65 and 1.0 for surface detail
    float shape = radarMask * lerp(0.65, 1.0, texDetail);

    // Smooth cloud edges: use noise to feather the boundary for organic look
    float edgeNoise = perlinFbm(geoUv * 0.8, 3.0, t * 0.3) * 0.5 + 0.5;
    float edgeMask = smoothstep(0.0, 0.55, radarMask + edgeNoise * 0.2 - 0.15);
    shape *= edgeMask;

    // Edge opacity: soft gradient from transparent at boundary to opaque in core
    // Use wider smoothstep + noise for natural wispy cloud edge falloff
    float edgeOpacity = smoothstep(0.0, 0.7, radarMask + edgeNoise * 0.25 - 0.12);
    edgeOpacity = pow(edgeOpacity, 0.6);

    // Simple raymarch for lighting using geo-anchored UVs
    float2 sun = normalize(max(length(uSunDir), 1e-4) * uSunDir + float2(1e-4, 0.0));
    float steps = clamp(uRaymarchSteps, 4.0, 16.0);
    float invSteps = 1.0 / steps;
    float2 sunStep = sun * float2(0.33, 0.33) * invSteps;
    float2 marchUv = geoUv;
    float extinction = 1.0;

    [loop]
    for (int i = 0; i < 16; ++i)
    {
        if ((float)i >= steps) break;
        marchUv += sunStep;
        float mNoise = perlinFbm(marchUv, 2.0, t) * 0.5 + 0.5;
        float c = radarMask * mNoise;
        extinction *= clamp(1.0 - c, 0.0, 1.0);
    }

    float cloudLight = exp(-(extinction + 0.03)) * (1.0 - exp(-(extinction + 0.03) * 2.2)) * 2.2;
    cloudLight *= shape;

    // Multi-stop cloud color gradient based on radar intensity
    // Light precip (blue) = bright silver-gray, moderate (green) = medium gray,
    // heavy (yellow/orange) = dark charcoal, extreme (red) = near-black
    float3 lightCol  = float3(0.82, 0.84, 0.88);
    float3 medCol    = float3(0.55, 0.56, 0.60);
    float3 heavyCol  = float3(0.28, 0.28, 0.32);
    float3 extremeCol = uDarkCloudColor;

    float3 cloudCol;
    if (stormFactor < 0.33)
        cloudCol = lerp(lightCol, medCol, stormFactor / 0.33);
    else if (stormFactor < 0.66)
        cloudCol = lerp(medCol, heavyCol, (stormFactor - 0.33) / 0.33);
    else
        cloudCol = lerp(heavyCol, extremeCol, (stormFactor - 0.66) / 0.34);

    // Add procedural lighting variation for 3D depth
    cloudCol = lerp(cloudCol, cloudCol * 1.25, sat(cloudLight * uCloudBrightness) * 0.35);

    // Subtle edge highlight for cloud rim lighting
    float rimLight = sat(1.0 - edgeMask * 1.5) * radarMask;
    cloudCol = lerp(cloudCol, lightCol, rimLight * 0.15);

    // Lightning - subtle under-cloud illumination glow
    float lightning = 0.0;
    [loop]
    for (int j = 0; j < uStrikeCount && j < 32; j++) {
        float d = distance(input.vNdc, uStrikeNdcFlash[j].xy);
        float falloff = 1.0 - smoothstep(0.0, 0.45, d);
        lightning += falloff * uStrikeNdcFlash[j].z;
    }
    lightning = sat(lightning * 0.35);

    // Warm under-glow instead of bright white wash
    float3 glowColor = float3(0.75, 0.70, 0.85);
    cloudCol = lerp(cloudCol, glowColor, lightning * shape);

    // Alpha with soft edge opacity falloff for natural cloud boundaries
    float alpha = sat(shape * 2.0) * edgeOpacity;
    alpha *= max(0.0, uOpacityMultiplier);
    if (alpha < 0.01) discard;

    return float4(cloudCol, alpha);
}
