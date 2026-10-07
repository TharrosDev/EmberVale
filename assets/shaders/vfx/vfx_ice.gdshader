shader_type spatial;
render_mode blend_premul_alpha, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// ==================================================================================================
// SPELL EFFECT ICE: a wall of ice, or a shell of it closed over a body. Not vfx_flow with the fire
// turned down (that was a flat white rectangle): ice is translucent blue plates, each catching the
// light differently, with bright seams between them, hairline cracks inside, a lit rim and a few
// glints that crawl. The body covers what is behind it by `occlude`; the rim, seams, cracks and
// glints are light added on top (premultiplied alpha, like the other effect shaders).
//
// `sheet` 1 is a wall: the rim follows the edges of the UVs and the top is cut into the points of
// crystal columns (`crest` is how deep they bite). `sheet` 0 is a shell: the rim is a fresnel off
// the normal. `erode` shatters it away plate by plate as it rises to 1.
// UV.y runs 0 at the top of a sheet to 1 at its base.
// ==================================================================================================

uniform sampler2D noise : hint_default_white, repeat_enable, filter_linear;
uniform vec4 color_core : source_color = vec4(0.9, 0.97, 1.0, 1.0);
uniform vec4 color_body : source_color = vec4(0.25, 0.55, 0.85, 1.0);
uniform vec4 color_deep : source_color = vec4(0.05, 0.16, 0.34, 1.0);
uniform float energy = 1.5;
uniform float opacity = 1.0;
uniform float occlude = 0.55;
uniform vec2 tiling = vec2(1.0, 1.0);
uniform float sheet = 1.0;
uniform float crest = 0.25;
uniform float erode = 0.0;
uniform float time_offset = 0.0;

void fragment() {
	vec2 uv = UV * tiling + vec2(time_offset * 0.37, time_offset * 0.11);

	// Plates: a coarse noise cut into five levels. A level is a plate; where two meet is a seam.
	float plate = textureLod(noise, uv * 0.9 + vec2(0.13, 0.57), 0.0).r;
	float facet = floor(plate * 5.0) / 5.0;
	float seam = smoothstep(0.84, 0.98, abs(fract(plate * 5.0) - 0.5) * 2.0);
	float fine = textureLod(noise, uv * 3.1 + vec2(0.4, 0.2), 0.0).r;
	float vein = 1.0 - smoothstep(0.0, 0.035, abs(fine - 0.5));
	float shimmer = textureLod(noise, uv * 1.7 + vec2(TIME * 0.02, 0.0), 0.0).r;
	float glint = shimmer * shimmer * shimmer * shimmer * shimmer * shimmer * (0.4 + facet) * 2.0;

	float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
	float rim = pow(1.0 - facing, 2.5);
	float cut = 1.0;
	if (sheet > 0.5) {
		float height = 1.0 - UV.y;
		float edge_x = min(UV.x, 1.0 - UV.x);
		float col = UV.x * tiling.x * 3.0;
		float column = textureLod(noise, vec2(floor(col) * 0.173 + 0.05, 0.31), 0.0).r;
		float tooth = abs(fract(col) - 0.5) * 2.0;
		float crest_line = 1.0 - crest * (0.25 + 0.75 * column) * (0.35 + 0.65 * tooth);
		cut = (1.0 - smoothstep(crest_line - 0.012, crest_line, height)) * smoothstep(0.0, 0.015, edge_x);
		float near_top = smoothstep(crest_line - 0.07, crest_line, height);
		float near_side = 1.0 - smoothstep(0.0, 0.05, edge_x);
		float column_edge = smoothstep(0.86, 1.0, tooth) * 0.45;
		rim = max(max(near_top, near_side), column_edge);
	}

	float shatter = smoothstep(erode, erode + 0.15, plate + 0.25);
	float lit = 0.25 + 0.75 * facet;
	float bright = clamp(rim * 0.9 + seam * 0.6 + vein * 0.3 + glint, 0.0, 1.0);
	vec3 body = mix(color_deep.rgb, color_body.rgb, lit);
	vec3 rgb = mix(body, color_core.rgb * energy, bright);
	float fade = smoothstep(0.3, 1.2, length(VERTEX));
	// A shell closed over a body (not a wall) is thick at its rim and seams and thin across its
	// middle, so whoever is frozen inside it can still be read.
	float thickness = mix(clamp(0.16 + rim * 0.9 + seam * 0.35 + glint * 0.5, 0.0, 1.0), 1.0, step(0.5, sheet));
	float alpha = clamp(cut * shatter * opacity * thickness, 0.0, 1.0) * fade;

	ALBEDO = rgb * alpha;
	ALPHA = alpha * occlude;
}
