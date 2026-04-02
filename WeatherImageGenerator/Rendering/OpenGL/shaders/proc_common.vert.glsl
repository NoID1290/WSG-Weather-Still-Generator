#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;
out vec2 vTex;
out vec2 vNdc;
void main() {
    gl_Position = vec4(aPos, 0.0, 1.0);
    vTex = aTex;
    vNdc = aPos;  // pass NDC position for radar transform
}
