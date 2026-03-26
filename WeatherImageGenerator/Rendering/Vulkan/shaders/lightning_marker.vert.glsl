#version 450

// Lightning strike marker vertex shader — Vulkan GLSL (SPIR-V)
// Uses push constants for per-sprite positioning (same approach as station_marker.vert.glsl).

layout(location = 0) in vec2 aPos; // unused — kept for input-layout compat
layout(location = 1) in vec2 aTex; // quad UV (0..1), remapped to (-1..+1)

layout(push_constant) uniform PC {
    float ndcX;
    float ndcY;
    float halfSizeX;
    float halfSizeY;
    float age;        // 0.0 = just occurred, 1.0 = oldest in window
    float isCG;       // 1.0 = cloud-to-ground, 0.0 = in-cloud
    float flashBoost; // 0.0 = no boost, 1.0 = peak flash
    float isNew;      // 1.0 = arrived after last fetch (flash gated); 0.0 = pre-existing
} pc;

layout(location = 0) out vec2  vUv;
layout(location = 1) out float vAge;
layout(location = 2) out float vIsCG;
layout(location = 3) out float vFlashBoost;
layout(location = 4) out float vIsNew;

void main() {
    vec2 uv = aTex * 2.0 - 1.0;
    vUv  = uv;
    vAge = pc.age;
    vIsCG = pc.isCG;
    vFlashBoost = pc.flashBoost;
    vIsNew      = pc.isNew;
    gl_Position = vec4(pc.ndcX + uv.x * pc.halfSizeX,
                       pc.ndcY + uv.y * pc.halfSizeY,
                       0.0, 1.0);
}
