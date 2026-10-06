shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// ==================================================================================================
// SPELL EFFECT SHOCK RING: a ring drawn procedurally on a unit quad, so one mesh is every ring at
// every radius. `radius` (0..1 of the half size of the quad) is animated from code; the leading
// edge is hard and the trail behind it soft, which is what reads as a shock front. `breakup` tears
// the band with noise so it is not a perfect vector circle.
// ==================================================================================================

uniform sampler2D noise : hint_default_white, repeat_enable, filter_linear;
uniform vec4 tint : source_color = vec4(1.0);
uniform float energy = 3.0;
uniform float opacity = 1.0;
uniform float radius = 0.8;
uniform float thickness = 0.14;
uniform float breakup = 0.35;
uniform float inner_glow = 0.0;
uniform float spin = 0.0;

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	float angle = atan(p.y, p.x) / 6.2831853;
	// textureLod: the angle wraps, and a derivative taken across the wrap would draw a seam.
	float n = textureLod(noise, vec2(angle * 3.0 + spin, r * 0.6 - TIME * 0.15), 0.0).r;

	float edge = r - radius;
	float band = smoothstep(-thickness, 0.0, edge) * (1.0 - smoothstep(0.0, thickness * 0.3, edge));
	band *= mix(1.0, 0.35 + 1.3 * n, breakup);
	float glow = inner_glow * (1.0 - smoothstep(0.0, max(radius, 0.001), r)) * (0.5 + 0.5 * n);
	float border = 1.0 - smoothstep(0.92, 1.0, r);
	float fade = smoothstep(0.3, 1.2, length(VERTEX));

	ALBEDO = tint.rgb * energy;
	ALPHA = clamp((band + glow) * tint.a * opacity * border, 0.0, 1.0) * fade;
}
