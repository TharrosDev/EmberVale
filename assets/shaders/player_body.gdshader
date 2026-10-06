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
// shadow. It is a per-instance uniform, so the corruption controller's material copies carry it and
// no other body sharing this mesh (the creator's preview) is touched. Radius 0, the default, is off.
// ==================================================================================================

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
// No mipmaps on the mask: a blurred mask smears a small hair or skin island over the clothing island beside it.
uniform sampler2D region_mask : hint_default_black, filter_linear, repeat_enable;

uniform float metallic_amount = 1.0;
uniform float roughness_amount = 1.0;
uniform float specular_amount = 0.5;

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

const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);

vec3 shift_region(float lum, vec3 tint, vec3 ref, float weight)
{
	float scale = clamp(lum / max(dot(ref, LUMA), 0.0001), 0.3, 1.6);
	return (tint - ref) * scale * weight;
}

void fragment()
{
	if (!IN_SHADOW_PASS && fp_head.w > 0.0)
	{
		// VERTEX is in view space here; the sphere is given in world space.
		vec3 world_pos = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
		if (distance(world_pos, fp_head.xyz) < fp_head.w)
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
	EMISSION = mix(glow_color, ember_tint, ember_mix) * glow_energy * (0.4 * m.r + 1.5 * m.b);
}
