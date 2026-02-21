#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;
uniform mat3 uTransform;
out vec2 vTex;
out vec2 vScreenPos; // NDC position for vignette/effects
void main() {
    vec3 p = uTransform * vec3(aPos, 1.0);
    gl_Position = vec4(p.xy, 0.0, 1.0);
    vTex = aTex;
    vScreenPos = p.xy;
}