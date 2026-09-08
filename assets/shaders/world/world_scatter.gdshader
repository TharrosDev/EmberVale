shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, cull_disabled;

global uniform float world_wetness;
global uniform float world_snow;
global uniform vec3 world_wind;
global uniform float world_visual_time;
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform bool has_texture = true;
uniform vec4 albedo_color : source_color = vec4(1.0);
uniform float roughness_value = 0.8;
uniform float metallic_value = 0.0;
uniform float saturation = 1.0;
uniform float alpha_cut = 0.0;
uniform float wind_response = 0.0;
varying vec3 world_normal;
varying vec3 world_pos;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
    // Root stays planted; per-instance world position breaks synchronization. Cap tip travel.
    float tip = clamp(VERTEX.y * 0.12, 0.0, 1.0);
    float gust = sin(world_visual_time * 1.4 + world_pos.x * 0.31 + world_pos.z * 0.47);
    vec3 displacement = world_wind * wind_response * tip * tip * gust * 0.025;
    VERTEX += (inverse(MODEL_MATRIX) * vec4(displacement,0.0)).xyz;
}
void fragment() {
    vec4 texel = albedo_color;
    if (has_texture) texel *= texture(albedo_texture, UV);
    vec3 base = mix(vec3(dot(texel.rgb,vec3(.2126,.7152,.0722))),texel.rgb,saturation) * COLOR.rgb;
    float wet = world_wetness * smoothstep(.1,.8,world_normal.y);
    float patches = .5 + .5 * sin(world_pos.x * 1.8 + sin(world_pos.z * 2.1));
    float snow = world_snow * smoothstep(.5,.95,world_normal.y) * smoothstep(.15,.65,patches);
    ALBEDO = mix(base * (1.0 - wet * .18), vec3(.70,.76,.80),snow);
    ROUGHNESS = mix(mix(roughness_value,max(.24,roughness_value*.55),wet),.86,snow);
    METALLIC = metallic_value * (1.0 - snow);
    if (alpha_cut > 0.0) {
        ALPHA = texel.a * COLOR.a;
        ALPHA_SCISSOR_THRESHOLD = alpha_cut;
    }
}
