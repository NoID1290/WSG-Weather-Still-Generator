#version 450

layout(location=0) in vec2 vTex;

layout(location=0) out vec4 FragColor;

layout(set=0, binding=0) uniform sampler2D uTexture;

layout(push_constant) uniform PC {
    vec4 row0;
    vec4 row1;
    vec4 row2;
    float uOpacity;
} pc;

// Enhanced 6-stop radar palette
vec3 palette(float t) {
    if (t < 0.15)       return mix(vec3(0.02, 0.01, 0.15), vec3(0.0, 0.25, 0.85), t / 0.15);
    else if (t < 0.30)  return mix(vec3(0.0, 0.25, 0.85), vec3(0.0, 0.7, 0.9),  (t - 0.15) / 0.15);
    else if (t < 0.50)  return mix(vec3(0.0, 0.7, 0.9),  vec3(0.1, 0.85, 0.2),  (t - 0.30) / 0.20);
    else if (t < 0.65)  return mix(vec3(0.1, 0.85, 0.2),  vec3(1.0, 0.95, 0.1),  (t - 0.50) / 0.15);
    else if (t < 0.82)  return mix(vec3(1.0, 0.95, 0.1),  vec3(1.0, 0.2, 0.05), (t - 0.65) / 0.17);
    else                return mix(vec3(1.0, 0.2, 0.05), vec3(0.85, 0.1, 0.65), (t - 0.82) / 0.18);
}

void main() {
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);
    vec4 tex = texture(uTexture, uv);

    float intensity = dot(tex.rgb, vec3(0.299, 0.587, 0.114));
    float alpha = tex.a * pc.uOpacity * smoothstep(0.015, 0.06, intensity);

    vec3 color = palette(intensity);

    // --- Subtle glow for high-intensity areas ---
    vec2 texelSize = 1.0 / vec2(textureSize(uTexture, 0));
    float bloomSum = 0.0;
    bloomSum += dot(texture(uTexture, uv + vec2( texelSize.x,  0.0)).rgb, vec3(0.333));
    bloomSum += dot(texture(uTexture, uv + vec2(-texelSize.x,  0.0)).rgb, vec3(0.333));
    bloomSum += dot(texture(uTexture, uv + vec2( 0.0,  texelSize.y)).rgb, vec3(0.333));
    bloomSum += dot(texture(uTexture, uv + vec2( 0.0, -texelSize.y)).rgb, vec3(0.333));
    bloomSum += dot(texture(uTexture, uv + vec2( texelSize.x,  texelSize.y)).rgb, vec3(0.333));
    bloomSum += dot(texture(uTexture, uv + vec2(-texelSize.x, -texelSize.y)).rgb, vec3(0.333));
    bloomSum += dot(texture(uTexture, uv + vec2( texelSize.x, -texelSize.y)).rgb, vec3(0.333));
    bloomSum += dot(texture(uTexture, uv + vec2(-texelSize.x,  texelSize.y)).rgb, vec3(0.333));
    float bloomAvg = bloomSum / 8.0;

    float glowFactor = smoothstep(0.25, 0.7, bloomAvg) * 0.15;
    vec3 glowColor = palette(bloomAvg);
    color = mix(color, color + glowColor * glowFactor, step(0.04, intensity));

    color = pow(color, vec3(0.95));
    FragColor = vec4(color, alpha);
}
