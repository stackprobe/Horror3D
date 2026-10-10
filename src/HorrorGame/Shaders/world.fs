#version 330
in vec3 worldPosition;
in vec3 normal;
in vec4 color;
uniform vec3 eye;
uniform vec3 beam;
uniform float lamp;
uniform vec4 exitGlow;
uniform float curse;
out vec4 finalColor;
void main() {
    vec3 delta = worldPosition - eye;
    float d = length(delta);
    float cone = smoothstep(0.83, 0.975, dot(normalize(delta), beam));
    float light = cone * lamp * 2.3 / (1.0 + d*d*0.012);
    float moon = 0.28 + 0.22 * max(dot(normalize(normal), normalize(vec3(-0.3,1.0,0.2))),0.0);
    vec3 lit = color.rgb * (vec3(0.46,0.60,0.79)*moon + vec3(1.0,0.88,0.66)*light);
    float fog = 1.0-exp(-d*d*0.00085);
    // 脱出可能時は、祭壇・足元・木々より高い光の柱を強く明滅させる。
    vec3 toExit = worldPosition - exitGlow.xyz;
    float altar = 1.0 - smoothstep(1.3, 1.9, length(toExit.xz));
    altar *= 1.0 - smoothstep(3.6, 4.1, toExit.y);
    float ground = (1.0 - smoothstep(0.0, 7.0, length(toExit.xz)))
        * (1.0 - smoothstep(0.05, 0.5, abs(toExit.y)));
    float beacon = (1.0 - smoothstep(0.65, 0.8, length(toExit.xz)))
        * smoothstep(3.4, 3.6, toExit.y) * (1.0 - smoothstep(28.2, 28.4, toExit.y));
    float glow = exitGlow.w * max(max(altar, ground * 0.55), beacon);
    vec3 emission = vec3(0.35, 0.85, 0.72) * glow;
    // 霧越しにも目印が残るよう、発光の減衰を通常の照明より緩やかにする。
    vec3 scene = mix(lit, vec3(0.024,0.042,0.056), clamp(fog,0.0,0.98));
    vec3 result = scene + emission * exp(-d * 0.004);
    float grain = 0.85 + 0.15 * sin(worldPosition.x * 3.0 + sin(worldPosition.z * 2.0) + worldPosition.y * 4.0);
    float shade = dot(lit, vec3(0.3, 0.5, 0.2));
    vec3 cursed = mix(vec3(0.20 + shade * 1.2, 0.008, 0.018) * grain,
        vec3(0.10, 0.002, 0.006), clamp(fog, 0.0, 0.95));
    finalColor = vec4(mix(result, cursed, curse), color.a);
}
