shader_type spatial;
render_mode blend_mix, unshaded, cull_back, depth_draw_never, shadows_disabled, fog_disabled;

// ==================================================================================================
// SPELL EFFECT DISTORTION: a sphere that bends what is behind it, strongest toward its silhouette
// and nothing at its centre, so an expanding one reads as a pressure wave. Drawn only on the High
// and Ultra tiers and never under Reduced Motion (VfxRecipeRules decides; this shader is not even
// instanced otherwise, because reading the screen texture costs a copy of the frame).
//
// The render priority of the material is below every other effect, so the flares and particles of
// the same blast are drawn over the bent image rather than being erased by it.
// ==================================================================================================

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;
uniform float strength = 0.03;
uniform float opacity = 1.0;
uniform float rim_power = 2.5;

// HEAT HAZE. `shimmer` 0 leaves a pressure wave exactly as it was. At 1 the push is not the rim's
// but two taps of noise climbing the surface, strongest across the middle of the shape and nothing
// at its silhouette: the air over a wall of fire wavering, with no outline to see.
uniform sampler2D noise : hint_default_white, repeat_enable, filter_linear;
uniform float shimmer = 0.0;

void fragment() {
	vec3 normal = normalize(NORMAL);
	float facing = clamp(dot(normal, normalize(VIEW)), 0.0, 1.0);
	float rim = pow(1.0 - facing, rim_power);
	// Die away at the very silhouette, or the outline of the sphere shows as a hard circle.
	float band = rim * smoothstep(0.0, 0.12, facing);
	float fade = smoothstep(0.3, 1.2, length(VERTEX));
	vec2 push = normal.xy * band * strength * fade;
	float alpha = clamp(band * 4.0 * opacity, 0.0, 1.0) * fade;
	if (shimmer > 0.0) {
		vec2 flow = vec2(UV.x * 6.0, UV.y * 3.0 + TIME * 0.8);
		vec2 wobble = vec2(texture(noise, flow).r, texture(noise, flow + vec2(0.37, 0.61)).r) - vec2(0.5);
		float body = smoothstep(0.0, 0.45, facing);
		push = mix(push, wobble * strength * body * fade, shimmer);
		alpha = mix(alpha, clamp(body * opacity, 0.0, 1.0) * fade, shimmer);
	}

	ALBEDO = texture(screen_texture, SCREEN_UV + push).rgb;
	ALPHA = alpha;
}
