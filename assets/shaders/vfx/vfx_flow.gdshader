shader_type spatial;
render_mode blend_premul_alpha, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// ==================================================================================================
// SPELL EFFECT FLOW: two octaves of scrolling noise over a mesh. Fire sheets and fireballs (noise
// eroded by `erode`, thinning toward the top of the UVs), ice walls (mostly `occlude`), and the
// fresnel shells of wards, frozen targets and projectile bodies (`fresnel_mix` 1).
//
// Premultiplied alpha like vfx_sprite: `occlude` 0 is pure additive, 1 covers what is behind.
// UV.y runs 0 at the top of a sheet or sphere to 1 at its base.
// ==================================================================================================

uniform sampler2D noise : hint_default_white, repeat_enable, filter_linear_mipmap;
uniform vec4 color_core : source_color = vec4(1.0);
uniform vec4 color_edge : source_color = vec4(1.0, 0.5, 0.2, 1.0);
uniform float energy = 3.0;
uniform float opacity = 1.0;
uniform float occlude = 0.0;
uniform vec2 scroll = vec2(0.0, 0.6);
uniform vec2 tiling = vec2(1.0, 1.0);
uniform float time_offset = 0.0;
uniform float erode = 0.0;
uniform float fresnel_power = 2.0;
uniform float fresnel_mix = 0.0;
uniform float fade_top = 0.0;
uniform float fade_sides = 0.0;
uniform float edge_soft = 0.0;
uniform float soft_depth = 0.0;
uniform sampler2D depth_texture : hint_depth_texture, repeat_disable, filter_nearest;

// THE BALL OF A BLAST. All four default to 0, which leaves a sheet of fire or a ward exactly as it
// was. `billow` pushes the surface in and out along its normal with the noise, so a ball has a
// lumpy outline instead of a circle. `front` lights a thin bright line where the noise is just
// above the erosion threshold: the burning edge of every hole. `thin` makes the cooler parts of the
// body more transparent, so it is a mass of flame and not a filled disc. `cool` (0..1, animated
// from code) turns what is left of it to `color_dark` and makes it cover what is behind: smoke.
uniform float billow = 0.0;
uniform float front = 0.0;
uniform float thin = 0.0;
uniform float cool = 0.0;
uniform vec4 color_dark : source_color = vec4(0.03, 0.028, 0.026, 1.0);

void vertex() {
	if (billow > 0.0) {
		float lump = textureLod(noise, UV * tiling * 0.7 + scroll * (TIME + time_offset) * 0.5, 0.0).r;
		VERTEX += NORMAL * (lump - 0.5) * billow;
	}
}

void fragment() {
	float t = TIME + time_offset;
	vec2 uv = UV * tiling;
	float broad = texture(noise, uv + scroll * t).r;
	float fine = texture(noise, uv * 2.3 + vec2(0.37, 0.11) + scroll * t * 1.7).r;
	float n = broad * 0.62 + fine * 0.38;

	// Height above the base of the sheet: flames thin out as they climb.
	float height = 1.0 - UV.y;
	float threshold = erode + height * fade_top * 0.75;
	float lifted = n + (1.0 - height) * fade_top * 0.35;
	float body = smoothstep(threshold, threshold + 0.22, lifted);
	float burning = body * (1.0 - smoothstep(threshold + 0.22, threshold + 0.42, lifted)) * front;
	float sides = mix(1.0, smoothstep(0.0, 0.18, UV.x) * smoothstep(0.0, 0.18, 1.0 - UV.x), fade_sides);

	// The shell: a bright rim with the noise crawling over it, eaten away as `erode` rises.
	float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
	float rim = pow(1.0 - facing, max(fresnel_power, 0.01));
	float eaten = smoothstep(erode, erode + 0.2, n + 0.2);
	float shell = clamp(rim * 1.15 + n * (0.18 + rim * 0.6), 0.0, 1.0) * eaten;

	// A ball of fire has no outline: it thins where the surface turns away from the eye.
	float turned = mix(1.0, smoothstep(0.0, 0.55, facing), edge_soft);
	float intensity = mix(body * sides * turned, shell, fresnel_mix);
	float fade = smoothstep(0.3, 1.2, length(VERTEX));
	if (soft_depth > 0.0) {
		float scene_depth = textureLod(depth_texture, SCREEN_UV, 0.0).r;
		vec4 scene_view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, scene_depth, 1.0);
		scene_view.xyz /= scene_view.w;
		fade *= clamp((VERTEX.z - scene_view.z) / soft_depth, 0.0, 1.0);
	}

	float heat = smoothstep(0.35, 0.95, intensity * (0.6 + 0.6 * n));
	heat = max(heat * (1.0 - cool), burning * (1.0 - cool * 0.7));
	intensity *= mix(1.0, 0.3 + 0.7 * max(heat, cool), thin);
	vec3 rgb = mix(color_edge.rgb, color_core.rgb, heat) * energy;
	rgb = mix(rgb, color_dark.rgb, cool * (1.0 - burning));
	float alpha = clamp(intensity * opacity, 0.0, 1.0) * fade;
	ALBEDO = rgb * alpha;
	ALPHA = alpha * mix(occlude, max(occlude, 0.85), cool);
}
