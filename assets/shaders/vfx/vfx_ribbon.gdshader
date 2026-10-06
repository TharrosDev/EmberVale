shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled, skip_vertex_transform;

// ==================================================================================================
// SPELL EFFECT RIBBON: lightning, beams, tethers and projectile trails.
//
// The mesh is a centre line. Every vertex sits ON the line; NORMAL carries the direction of the
// line there, UV.y says which side of the line the vertex belongs to (0 or 1) and UV2.x is the half
// width in metres. The ribbon is widened here, in view space, at right angles to the line and to
// the eye, so it faces the camera from anywhere without being rebuilt when the camera moves.
// (`skip_vertex_transform`: this shader writes VERTEX in view space itself.)
//
// UV.x runs along the line. COLOR is per-vertex brightness and alpha (a trail fading to its tail).
// ==================================================================================================

uniform sampler2D noise : hint_default_white, repeat_enable, filter_linear;
uniform vec4 color_core : source_color = vec4(1.0);
uniform vec4 color_edge : source_color = vec4(0.5, 0.7, 1.0, 1.0);
uniform float energy = 5.0;
uniform float opacity = 1.0;
uniform float width_scale = 1.0;
uniform float flow_speed = 0.0;
uniform float flow_amount = 0.0;
uniform float uv_scale = 1.0;

void vertex() {
	vec3 view_position = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 view_tangent = (MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
	vec3 side = cross(view_tangent, view_position);
	float side_length = length(side);
	if (side_length > 0.000001) {
		side = side / side_length;
	} else {
		side = vec3(1.0, 0.0, 0.0);
	}
	view_position += side * (UV.y * 2.0 - 1.0) * UV2.x * width_scale;
	VERTEX = view_position;
	NORMAL = vec3(0.0, 0.0, 1.0);
}

void fragment() {
	float across = clamp(abs(UV.y * 2.0 - 1.0), 0.0, 1.0);
	float core = 1.0 - smoothstep(0.0, 0.28, across);
	float glow = (1.0 - across) * (1.0 - across);
	float n = textureLod(noise, vec2(UV.x * uv_scale - TIME * flow_speed, UV.y * 0.5), 0.0).r;
	float flow = mix(1.0, 0.25 + 1.5 * n, flow_amount);
	float fade = smoothstep(0.3, 1.2, length(VERTEX));

	ALBEDO = mix(color_edge.rgb, color_core.rgb, core) * COLOR.rgb * energy;
	ALPHA = clamp((core + glow * 0.7 * flow) * COLOR.a * opacity, 0.0, 1.0) * fade;
}
