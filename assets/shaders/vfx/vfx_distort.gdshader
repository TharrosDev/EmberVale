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

void fragment() {
	vec3 normal = normalize(NORMAL);
	float facing = clamp(dot(normal, normalize(VIEW)), 0.0, 1.0);
	float rim = pow(1.0 - facing, rim_power);
	// Die away at the very silhouette, or the outline of the sphere shows as a hard circle.
	float band = rim * smoothstep(0.0, 0.12, facing);
	float fade = smoothstep(0.3, 1.2, length(VERTEX));
	vec2 push = normal.xy * band * strength * fade;

	ALBEDO = texture(screen_texture, SCREEN_UV + push).rgb;
	ALPHA = clamp(band * 4.0 * opacity, 0.0, 1.0) * fade;
}
