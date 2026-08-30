shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, cull_disabled;

// ==================================================================================================
// SHARED SCATTER MATERIAL — the one thing a MultiMesh instance colour cannot do: DESATURATE.
//
// ⚠️ WHY THIS EXISTS. Scatter tints multiply the source model's albedo, and a multiply can darken a
// colour but never drain it. Frostfang Reach and the Ember Crown share one rock model, whose atlas
// is olive; on snow, tinted every shade of blue anyone tried, it still read as a mossy green boulder
// sitting on a glacier. Region identity was being fought colour by colour and losing.
//
// One extra term fixes it: pull the sampled albedo toward its own luminance before the instance tint
// multiplies it. A layer opts in with BiomeScatterLayerResource.Saturation; at 1.0 nothing here runs
// differently from a StandardMaterial3D and the layer keeps its own material instead.
// ==================================================================================================

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 albedo_color : source_color = vec4(1.0);
uniform float saturation : hint_range(0.0, 2.0) = 1.0;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.92;
uniform float alpha_cut : hint_range(0.0, 1.0) = 0.0;

// Rec. 709, the same weights the rest of the engine uses for luminance.
const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);

void fragment() {
	vec4 texel = texture(albedo_texture, UV) * albedo_color;
	vec3 drained = mix(vec3(dot(texel.rgb, LUMA)), texel.rgb, saturation);
	ALBEDO = drained * COLOR.rgb;
	ROUGHNESS = roughness_value;
	if (alpha_cut > 0.0) {
		ALPHA = texel.a * COLOR.a;
		ALPHA_SCISSOR_THRESHOLD = alpha_cut;
	}
}
