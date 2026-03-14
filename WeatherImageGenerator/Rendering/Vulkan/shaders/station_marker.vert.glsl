#version 450

// Vertex shader for GPU vector station/epicenter markers.
// No vertex buffer: we use the bound quad VB (pos+tex), but map from
// the texcoord (0..1) to UV (-1..+1) and position the quad via push constants.

layout(location = 0) in vec2 aPos; // unused — kept for input-layout compat
layout(location = 1) in vec2 aTex; // quad UV (0..1), remapped to (-1..+1)

layout(push_constant) uniform PC {
    float ndcX;        // marker centre NDC X
    float ndcY;        // marker centre NDC Y
    float halfSizeX;   // quad half-width  in NDC
    float halfSizeY;   // quad half-height in NDC
    float markerType;  // 0 = triangle station, 1 = epicenter dot+rings
    float colorR;
    float colorG;
    float colorB;
    float colorA;
    float ringPhase;   // 0..1 animated ring phase
    float selected;    // 0 or 1
    float glowStrength;
} pc;

layout(location = 0) out vec2 vUv;

void main() {
    // Convert texcoord [0,1] to UV [-1,+1]
    vec2 uv = aTex * 2.0 - 1.0;
    vUv = uv;
    gl_Position = vec4(pc.ndcX + uv.x * pc.halfSizeX,
                       pc.ndcY + uv.y * pc.halfSizeY,
                       0.0, 1.0);
}
