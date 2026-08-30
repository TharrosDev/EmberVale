shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform vec4 surface_color : source_color;
uniform vec4 secondary_color : source_color;
uniform vec4 detail_color : source_color;
uniform vec4 road_color : source_color;
uniform vec2 cell_size = vec2(52.0);
uniform float detail_scale : hint_range(0.25, 8.0) = 2.5;
uniform vec4 tint : source_color = vec4(1.0);
uniform float tint_strength : hint_range(0.0, 1.0) = 0.0;
uniform vec2 world_origin = vec2(0.0);
uniform float terrain_seed = 3800.0;
uniform float surface_roughness : hint_range(0.0, 1.0) = 0.96;
uniform float detail_roughness : hint_range(0.0, 1.0) = 0.88;
uniform float road_roughness : hint_range(0.0, 1.0) = 0.82;
uniform vec2 slope_blend = vec2(0.08, 0.32);
uniform vec2 height_blend = vec2(0.04, 0.15);

varying float terrain_height;

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 345.45));
    p += dot(p, p + 34.345 + terrain_seed);
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
    terrain_height = VERTEX.y;
}

void fragment() {
    vec2 metres = (UV - vec2(0.5)) * cell_size;
    vec2 world_metres = world_origin + metres;
    float macro = value_noise(world_metres * detail_scale * 0.055);
    float micro = value_noise(world_metres * detail_scale * 0.21 + 31.0);
    vec3 ground = mix(surface_color.rgb, secondary_color.rgb, smoothstep(0.28, 0.78, macro));
    float slope = 1.0 - clamp(dot(normalize(NORMAL), vec3(0.0, 1.0, 0.0)), 0.0, 1.0);
    float slope_mask = smoothstep(slope_blend.x, slope_blend.y, slope);
    float height_mask = smoothstep(height_blend.x, height_blend.y, terrain_height);
    float detail_mask = max(slope_mask, height_mask * smoothstep(0.42, 0.78, micro));
    ground = mix(ground, detail_color.rgb, detail_mask);
    float road = COLOR.r;
    float activity = COLOR.g;
    float ruts = 0.82 + 0.18 * value_noise(metres * vec2(0.7, 0.13));
    ground = mix(ground, road_color.rgb * ruts, road);
    ground = mix(ground, mix(surface_color.rgb, road_color.rgb, 0.58), activity);
    // ⚠️ THE CELL TINT FADES OUT AT THE CELL EDGE. Applied flat it is a rectangle of different-
    // coloured ground with a hard border — the exact artefact the 2026-08-29 overhaul exists to
    // remove, drawn by the one part of the pipeline that still knew where a cell ended. The
    // Emberdeep's ochre and the Ash Roost's soot now bleed out into their neighbours instead.
    float tint_edge = smoothstep(0.0, 0.22, UV.x) * smoothstep(0.0, 0.22, 1.0 - UV.x)
        * smoothstep(0.0, 0.22, UV.y) * smoothstep(0.0, 0.22, 1.0 - UV.y);
    ground = mix(ground, tint.rgb, tint_strength * tint_edge);
    ALBEDO = ground;
    ROUGHNESS = mix(mix(surface_roughness, detail_roughness, detail_mask), road_roughness, max(road, activity));
}
