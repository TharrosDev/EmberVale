shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// ==================================================================================================
// SPELL EFFECT GROUND DISC: a flat quad just above the floor for telegraphs, zones and sigils.
// A rim, a soft disc of radially flowing noise, an optional pattern texture (the rune ring) turned
// by `spin`, and a bright front that sweeps out to `fill` (0..1) as a telegraph runs down.
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

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	float c = cos(spin);
	float s = sin(spin);
	vec2 turned = vec2(p.x * c - p.y * s, p.x * s + p.y * c) * 0.5 + 0.5;
	float glyph = texture(pattern, turned).r * pattern_mix;

	float angle = atan(p.y, p.x) / 6.2831853;
	float n = textureLod(noise, vec2(angle * 4.0, r * 1.2 - TIME * flow), 0.0).r;

	float edge = smoothstep(0.86, 0.93, r) * (1.0 - smoothstep(0.95, 1.0, r)) * rim;
	float inside = 1.0 - smoothstep(0.75, 0.98, r);
	float filled = 1.0 - smoothstep(fill - 0.08, fill, r);
	float front = smoothstep(fill - 0.12, fill, r) * (1.0 - smoothstep(fill, fill + 0.02, r)) * (1.0 - step(0.995, fill));
	float body = disc * inside * (0.25 + 0.75 * n) * (0.35 + 0.65 * filled);
	float fade = smoothstep(0.3, 1.2, length(VERTEX));

	ALBEDO = tint.rgb * energy;
	ALPHA = clamp((edge + glyph * (0.4 + 0.6 * filled) + body + front * 0.9) * tint.a * opacity, 0.0, 1.0) * fade;
}
