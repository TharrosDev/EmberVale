shader_type spatial;
render_mode cull_disabled;

// ==================================================================================================
// THE PLAYER BODY: ONE MESH, ONE BAKED ATLAS, RECOLOURED BY REGION.
//
// chr_player_base.glb has a single surface and a single baked Meshy atlas, so appearance cannot swap
// materials per part. tools/gen_player_mask.py writes chr_player_base_mask.png beside the atlas:
//   R = skin, G = hair, B = eyes. Clothing and gear are zero and never change colour.
//
// A region keeps the atlas detail and is moved by (chosen tint - reference average): the reference is the
// average colour of that region in the unmodified atlas, so a tint equal to its reference is a no-op and
// the default look is bit-for-bit the model. The shift is scaled by the pixel's brightness against the
// reference, so shading and fine texture survive a blonde or a deep-brown change alike.
//
// Corruption (CorruptionAppearanceController) drives ash_*, skin_wash_*, glow_* and ember_mix (how far the
// tier's glow colour gives way to the character's chosen ember_tint): the whole body gathers
// ash, the skin takes the tier's wash and an ember glow that also lights the eyes.
//
// Metallic/roughness/specular come from the imported glTF material (PlayerAppearance copies them) so the
// lit look does not change when this replaces it.
//
// First person (PlayerCameraRig) sets fp_head on the player's own mesh instance: a world-space sphere
// round the animated head (xyz centre, w radius). Everything inside it is discarded in every pass but
// the shadow pass, so the camera can sit in the head without seeing it and the head still casts its
// shadow. fp_eye is a second sphere round the camera itself, as wide as its near plane's corners:
// whatever of the body a clip swings that close is opened cleanly rather than sliced by the near
// plane. Both are per-instance uniforms, so the corruption controller's material copies carry them
// and no other body sharing this mesh (the creator's preview) is touched. Radius 0, the default, is
// off.
//
// view_fade thins the body out near the camera with a screen-door dither (no blending, so it sorts
// and shadows like the solid mesh it is). x, y are first person: body further than y from the
// vertical line through the head sphere (an arm, not the chest or legs) fades as it comes inside x of
// the eye and is gone at about two thirds of x, so a sprint's arm pump is not a sleeve across the
// screen. z is third person: the whole body fades as it comes inside z of the camera, for a camera a
// wall or the ground has squeezed onto the player's back. All zero, the default, is off.
//
// EMISSION CARRIES THE COLOUR. Every Meshy body ships one material: the atlas as base colour AND as
// the emissive texture, with no metallic factor, which glTF reads as metallic 1. The stock material
// is therefore drawn by its emission; its albedo, on a full metal, takes no diffuse light at all.
// emission_amount is that emission (PlayerAppearance copies the source material's energy), applied
// to the recoloured atlas so tints, ash and the skin wash all show. Without it the player was the
// one body in the game drawn as bare metal: a silhouette at midday.
// ==================================================================================================

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
// No mipmaps on the mask: a blurred mask smears a small hair or skin island over the clothing island beside it.
uniform sampler2D region_mask : hint_default_black, filter_linear, repeat_enable;

uniform float metallic_amount = 1.0;
uniform float roughness_amount = 1.0;
uniform float specular_amount = 0.5;
uniform float emission_amount = 0.0;

uniform vec3 skin_ref : source_color = vec3(0.676, 0.506, 0.439);
uniform vec3 hair_ref : source_color = vec3(0.191, 0.153, 0.125);
uniform vec3 eye_ref : source_color = vec3(0.287, 0.213, 0.179);
uniform vec3 skin_tint : source_color = vec3(0.676, 0.506, 0.439);
uniform vec3 hair_tint : source_color = vec3(0.191, 0.153, 0.125);
uniform vec3 eye_tint : source_color = vec3(0.287, 0.213, 0.179);

uniform float ash_amount = 0.0;
uniform vec3 ash_color : source_color = vec3(0.2, 0.17, 0.17);
uniform float skin_wash_amount = 0.0;
uniform vec3 skin_wash_color : source_color = vec3(0.4, 0.4, 0.4);
uniform vec3 glow_color : source_color = vec3(0.8, 0.3, 0.1);
uniform float glow_energy = 0.0;
uniform vec3 ember_tint : source_color = vec3(0.82, 0.34, 0.1);
uniform float ember_mix = 0.0;

instance uniform vec4 fp_head = vec4(0.0, 0.0, 0.0, 0.0);
instance uniform vec4 fp_eye = vec4(0.0, 0.0, 0.0, 0.0);
instance uniform vec4 view_fade = vec4(0.0, 0.0, 0.0, 0.0);

const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);

vec3 shift_region(float lum, vec3 tint, vec3 ref, float weight)
{
	float scale = clamp(lum / max(dot(ref, LUMA), 0.0001), 0.3, 1.6);
	return (tint - ref) * scale * weight;
}

void fragment()
{
	if (!IN_SHADOW_PASS && (fp_head.w > 0.0 || fp_eye.w > 0.0))
	{
		// VERTEX is in view space here; the spheres are given in world space.
		vec3 world_pos = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
		if (distance(world_pos, fp_head.xyz) < fp_head.w || distance(world_pos, fp_eye.xyz) < fp_eye.w)
		{
			discard;
		}
	}

	if (!IN_SHADOW_PASS && (view_fade.x > 0.0 || view_fade.z > 0.0))
	{
		// How much of the surface is kept here, 0..1; view space puts the camera at the origin.
		float from_eye = length(VERTEX);
		float keep = 1.0;
		if (view_fade.z > 0.0)
		{
			keep = smoothstep(view_fade.z * 0.45, view_fade.z, from_eye);
		}
		if (view_fade.x > 0.0 && from_eye < view_fade.x)
		{
			vec3 world_pos = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
			if (distance(world_pos.xz, fp_head.xz) > view_fade.y)
			{
				keep = min(keep, smoothstep(view_fade.x * 0.68, view_fade.x, from_eye));
			}
		}
		// Interleaved gradient noise: a stable per-pixel threshold, finer than a 4x4 Bayer tile.
		float threshold = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
		if (keep < 1.0 && keep <= threshold)
		{
			discard;
		}
	}

	vec3 base = texture(albedo_tex, UV).rgb;
	// A texel half in a gutter beside another island reads 0.5, so the ramp starts above it.
	vec3 m = smoothstep(vec3(0.55), vec3(0.9), texture(region_mask, UV).rgb);
	float lum = dot(base, LUMA);

	vec3 col = base;
	col += shift_region(lum, skin_tint, skin_ref, m.r);
	col += shift_region(lum, hair_tint, hair_ref, m.g);
	col += shift_region(lum, eye_tint, eye_ref, m.b);
	col = max(col, vec3(0.0));

	col = mix(col, ash_color, ash_amount);
	col = mix(col, skin_wash_color, skin_wash_amount * m.r);

	ALBEDO = col;
	METALLIC = metallic_amount;
	ROUGHNESS = roughness_amount;
	SPECULAR = specular_amount;
	EMISSION = col * emission_amount
		+ mix(glow_color, ember_tint, ember_mix) * glow_energy * (0.4 * m.r + 1.5 * m.b);
}
