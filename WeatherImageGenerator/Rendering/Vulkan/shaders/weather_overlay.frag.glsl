#version 450

layout(location=0) in vec2 vTex;
layout(location=1) in vec2 vScreenPos;

layout(location=0) out vec4 FragColor;

layout(set=0, binding=0) uniform sampler2D uTexture;

layout(push_constant) uniform PC {
    vec4 row0;
    vec4 row1;
    vec4 row2;
    float uOpacity;
    float uTime;
    float uEnableGlow;
} pc;

void main() {
    vec4 c = texture(uTexture, vTex);
    float opacity = pc.uOpacity > 0.0 ? pc.uOpacity : 1.0;

    // --- Smooth alpha edge blend ---
    float edgeFade = 1.0;
    float border = 0.015;
    edgeFade *= smoothstep(0.0, border, vTex.x) * smoothstep(0.0, border, 1.0 - vTex.x);
    edgeFade *= smoothstep(0.0, border, vTex.y) * smoothstep(0.0, border, 1.0 - vTex.y);

    vec3 finalColor = c.rgb;

    if (pc.uEnableGlow > 0.5) {
        vec2 texelSize = 1.0 / vec2(textureSize(uTexture, 0));
        float bloomScale = 2.5;
        float selfIntensity = dot(c.rgb, vec3(0.299, 0.587, 0.114));
        float bloomSum = 0.0;
        bloomSum += dot(texture(uTexture, vTex + vec2( texelSize.x * bloomScale,  0.0)).rgb, vec3(0.333));
        bloomSum += dot(texture(uTexture, vTex + vec2(-texelSize.x * bloomScale,  0.0)).rgb, vec3(0.333));
        bloomSum += dot(texture(uTexture, vTex + vec2(0.0,  texelSize.y * bloomScale)).rgb, vec3(0.333));
        bloomSum += dot(texture(uTexture, vTex + vec2(0.0, -texelSize.y * bloomScale)).rgb, vec3(0.333));
        float bloomAvg = bloomSum / 4.0;

        float glowStrength = smoothstep(0.3, 0.8, bloomAvg) * 0.18;
        vec3 glowTint = c.rgb * (1.0 + glowStrength);
        finalColor = mix(c.rgb, glowTint, step(0.05, selfIntensity));
    }

    finalColor = pow(finalColor, vec3(1.0 / 1.05));
    float finalAlpha = c.a * opacity * edgeFade;
    FragColor = vec4(finalColor, finalAlpha);
}
