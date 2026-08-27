shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform vec4 surface_color : source_color;
uniform vec4 secondary_color : source_color;
uniform vec4 detail_color : source_color;
uniform vec4 road_color : source_color;
uniform vec2 cell_size = vec2(52.0);
uniform float relief : hint_range(0.0, 0.18) = 0.055;
uniform float detail_scale : hint_range(0.25, 8.0) = 2.5;
uniform int road_axis = 0;
uniform float road_width = 5.0;
uniform float road_offset = 0.0;
uniform float seed = 38.0;
uniform vec4 tint : source_color = vec4(1.0);
uniform float tint_strength : hint_range(0.0, 1.0) = 0.0;

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 345.45));
    p += dot(p, p + 34.345 + seed);
    return fract(p.x * p.y);
}

float value_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x),
        mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0)), f.x), f.y);
}

void vertex() {
    vec2 centred = abs(UV * 2.0 - 1.0);
    float seam_fade = smoothstep(1.0, 0.78, max(centred.x, centred.y));
    float macro = value_noise(UV * detail_scale * 3.0);
    float micro = value_noise(UV * detail_scale * 11.0 + 17.0);
    VERTEX.y += ((macro - 0.5) * 1.4 + (micro - 0.5) * 0.35) * relief * seam_fade;
}

void fragment() {
    vec2 metres = (UV - vec2(0.5)) * cell_size;
    float macro = value_noise(UV * detail_scale * 3.0);
    float micro = value_noise(UV * detail_scale * 15.0 + 31.0);
    vec3 ground = mix(surface_color.rgb, secondary_color.rgb, smoothstep(0.28, 0.78, macro));
    ground = mix(ground, detail_color.rgb, smoothstep(0.72, 0.95, micro) * 0.35);
    float cross_axis = road_axis == 1 ? metres.x : metres.y;
    float road = road_axis == 0 ? 0.0 : 1.0 - smoothstep(road_width * 0.42, road_width * 0.58, abs(cross_axis - road_offset));
    float ruts = 0.82 + 0.18 * value_noise(metres * vec2(0.7, 0.13));
    ground = mix(ground, road_color.rgb * ruts, road);
    ground = mix(ground, tint.rgb, tint_strength);
    ALBEDO = ground;
    ROUGHNESS = mix(0.98, 0.86, road);
}
