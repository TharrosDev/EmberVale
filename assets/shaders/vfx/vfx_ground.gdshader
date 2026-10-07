shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// ==================================================================================================
// SPELL EFFECT GROUND DISC: a flat mesh just above the floor for telegraphs, zones and sigils.
// A thin rim, wisps of noise drawn along the radius, an optional pattern texture (a rune ring,
// frost, glowing cracks, roots) turned by `spin`, and a bright front that sweeps out to `fill`
// (0..1) as a telegraph runs down.
//
// It is deliberately mostly empty: the body is wisps, not a filled disc, because a disc of light
// metres across under the camera is the whole lower half of the frame in one flat colour.
// `body_opacity` is the coverage governor's cut on that soft body alone (the rim, the front and a
// rune's lines are thin and stay). `pattern_floor` lifts the pattern's black point, so a texture
// with a soft base (frost, cracks) shows only its spokes and veins; `pattern_soft` lets the governor
// thin such a pattern too; `reveal` 1 hides the pattern outside `fill`, so frost spreads outward.
//
// `spin` is an angle set from code, not a speed read off TIME, so Reduced Motion can hold it still.
// A negative `flow` draws the noise inward (a gravity well). The lasting marks a spell leaves
// (scorch, frost) are Decals (VfxGroundMark), not this.
// ==================================================================================================

uniform sampler2D pattern : hint_default_black, repeat_disable, filter_linear_mipmap;
uniform sampler2D noise : hint_default_white, repeat_enable, filter_linear;
uniform vec4 tint : source_color = vec4(1.0);
uniform float energy = 2.5;
uniform float opacity = 1.0;
uniform float spin = 0.0;
uniform float fill = 1.0;
uniform float flow = 0.3;
uniform float pattern_mix = 1.0;
uniform float disc = 0.5;
uniform float rim = 1.0;
uniform float body_opacity = 1.0;
uniform float pattern_floor = 0.0;
uniform float pattern_soft = 0.0;
uniform float reveal = 0.0;

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	float c = cos(spin);
	float s = sin(spin);
	vec2 turned = vec2(p.x * c - p.y * s, p.x * s + p.y * c) * 0.5 + 0.5;
	float glyph = smoothstep(pattern_floor, 1.0, texture(pattern, turned).r) * pattern_mix;

	float angle = atan(p.y, p.x) / 6.2831853;
	float n = textureLod(noise, vec2(angle * 4.0, r * 1.2 - TIME * flow), 0.0).r;
	float n2 = textureLod(noise, vec2(angle * 9.0 + 0.31, r * 2.6 - TIME * flow * 1.7), 0.0).r;

	float wisps = smoothstep(0.42, 0.78, n * 0.6 + n2 * 0.5);
	float edge = smoothstep(0.89, 0.93, r) * (1.0 - smoothstep(0.94, 0.975, r)) * rim * (0.6 + 0.6 * n2);
	float inside = 1.0 - smoothstep(0.75, 0.96, r);
	float filled = 1.0 - smoothstep(fill - 0.08, fill, r);
	float front = smoothstep(fill - 0.05, fill, r) * (1.0 - smoothstep(fill, fill + 0.015, r)) * (1.0 - step(0.995, fill));
	float body = disc * inside * wisps * (0.3 + 0.7 * filled) * body_opacity * 0.55;
	glyph *= (0.4 + 0.6 * filled) * mix(1.0, filled, reveal) * mix(1.0, body_opacity, pattern_soft);
	float fade = smoothstep(0.3, 1.2, length(VERTEX));

	ALBEDO = tint.rgb * energy;
	ALPHA = clamp((edge + glyph + body + front * 0.9) * tint.a * opacity, 0.0, 1.0) * fade;
}
