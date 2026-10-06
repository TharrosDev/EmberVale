shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// ==================================================================================================
// SPELL EFFECT SHOCK RING: a ring drawn procedurally on a flat mesh, so one mesh is every ring at
// every radius. It is a shock FRONT, not a band: a hair-thin bright leading edge, a torn wake
// trailing in behind it that fades toward the centre, and optional radial streaks in that wake.
// Nothing fills the disc inside it (`inner_glow` is a faint tint at most): a ring that is a thick
// flat donut with a lit middle is what the first renders showed, and it reads as a plastic disc.
//
// The ring sits at `radius` (0..1 of the half size of the mesh) and the NODE is scaled as the ring
// grows, so the mesh can be an annulus and the empty middle costs no fill (VfxMaterials.Annulus
// covers 0.42..1.0; the ring is held at 0.8). `thickness` is the depth of the wake, in the same
// units. `breakup` tears the wake and roughens the front with noise.
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
uniform float streaks = 0.0;

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	float angle = atan(p.y, p.x) / 6.2831853;
	// textureLod: the angle wraps, and a derivative taken across the wrap would draw a seam.
	float n = textureLod(noise, vec2(angle * 3.0 + spin, r * 0.6 - TIME * 0.15), 0.0).r;
	float fine = textureLod(noise, vec2(angle * 11.0 - spin, r * 2.0 + TIME * 0.4), 0.0).r;

	float edge = r - radius;
	float behind = 1.0 - step(0.0, edge);
	float lead = smoothstep(-thickness * 0.22, 0.0, edge) * (1.0 - smoothstep(0.0, thickness * 0.12, edge));
	lead *= mix(1.0, 0.55 + 0.9 * fine, breakup);
	float wake = smoothstep(-thickness, 0.0, edge) * behind;
	float torn = mix(1.0, smoothstep(0.38, 0.72, n * 0.55 + fine * 0.6), breakup);
	wake = wake * wake * torn;
	float ray = fine * fine * fine * fine * streaks * smoothstep(-thickness * 2.5, 0.0, edge) * behind;
	float glow = inner_glow * (1.0 - smoothstep(0.0, max(radius, 0.001), r)) * (0.5 + 0.5 * n);
	float border = 1.0 - smoothstep(0.92, 1.0, r);
	float fade = smoothstep(0.3, 1.2, length(VERTEX));

	ALBEDO = mix(tint.rgb, vec3(1.0), clamp(lead * 0.6, 0.0, 1.0)) * energy;
	ALPHA = clamp((lead + wake * 0.45 + ray * 0.6 + glow) * tint.a * opacity * border, 0.0, 1.0) * fade;
}
