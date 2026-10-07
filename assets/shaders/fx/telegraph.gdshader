shader_type spatial;
render_mode blend_mix, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// ==================================================================================================
// WIND-UP TELEGRAPH: the ground warning under an attacker (src/Combat/TelegraphRing.cs). The SHAPE is
// the mesh, never this shader: a flat ring, disc or fan whose footprint is the danger, with UV.x
// running 0 at the inner edge to 1 at the outer edge and UV.y running 0..1 along the arc. This only
// decides how that footprint is inked: a see-through body that is darker at the caster, a part
// already "swept" that is lit (it reaches the outer edge at the cast moment), a bright front on the
// sweep, a brighter rim that shimmers along the arc, and edges that fade out instead of ending on a
// hard aliased line.
//
// `plain` is the High Contrast look (flat body, hard solid rim) and `motion` is 0 under Reduced
// Motion (no shimmer). Mix blend, not additive: an additive warning vanishes on bright sand at noon.
// ==================================================================================================

uniform vec4 fill_color : source_color = vec4(1.0, 0.4, 0.1, 1.0);
uniform vec4 rim_color : source_color = vec4(1.0, 0.4, 0.1, 1.0);
uniform float fill_alpha = 0.35;
uniform float rim_alpha = 0.9;
uniform float rim_width = 0.14;
uniform float progress = 0.0;
uniform float inner_edge = 1.0;
uniform float side_edge = 0.0;
uniform float motion = 1.0;
uniform float plain = 0.0;

void fragment() {
	float r = UV.x;
	float a = UV.y;
	float side = min(a, 1.0 - a);

	// Edges: a soft fall-off wide enough to hide the polygon, or one screen pixel when plain.
	float edge_r = mix(0.07, 0.0, plain) + max(fwidth(r) * 1.5, 0.001);
	float edge_a = mix(0.03, 0.0, plain) + max(fwidth(a) * 1.5, 0.0005);
	float mask = 1.0 - smoothstep(1.0 - edge_r, 1.0, r);
	mask *= mix(1.0, smoothstep(0.0, edge_r, r), inner_edge);
	mask *= mix(1.0, smoothstep(0.0, edge_a, side), side_edge);

	// The rim: the outer band, and the two sides of a fan.
	float rim = smoothstep(1.0 - rim_width, 1.0 - (rim_width * 0.45), r);
	rim = max(rim, side_edge * (1.0 - smoothstep(edge_a, edge_a + 0.03, side)));
	float shimmer = 1.0 - (0.2 * motion * (0.5 + (0.5 * sin((a * 37.699) - (TIME * 5.0)))));

	// The body: what the sweep has passed is lit, what it has not is dim, and the front is a line.
	float swept = 1.0 - smoothstep(progress - 0.04, progress + 0.04, r);
	float front = (1.0 - smoothstep(0.0, 0.06, abs(r - progress))) * step(0.02, progress) * step(progress, 0.98);
	float gradient = mix(mix(0.6, 1.0, r), 1.0, plain);
	float body = fill_alpha * gradient * mix(0.7, 1.0, swept);
	body += front * fill_alpha * 0.8 * (1.0 - plain);

	float alpha = mix(body, rim_alpha * shimmer, rim);
	ALBEDO = mix(fill_color.rgb, rim_color.rgb, max(rim, front * 0.6 * (1.0 - plain)));
	ALPHA = clamp(alpha, 0.0, 1.0) * mask;
}
