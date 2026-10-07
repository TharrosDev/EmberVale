shader_type spatial;
render_mode blend_premul_alpha, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// ==================================================================================================
// SPELL EFFECT SPRITE: one camera-facing quad. Every flare core, halo and streak, and every particle
// of every VfxBurst preset, is drawn with this shader.
//
// PREMULTIPLIED ALPHA, SO ONE SHADER IS BOTH ADDITIVE AND SMOKE. `occlude` 0 writes ALPHA 0 and the
// colour is simply added (fire, sparks, halos); `occlude` 1 writes the mask as ALPHA and the same
// quad covers what is behind it (dark smoke). Nothing here relies on glow: the colour is multiplied
// by `energy` (4 to 8 for a core), which blooms where glow is on and clips to a hot read where it
// is off.
//
// Works on a MeshInstance3D and as a GPUParticles3D draw pass: INSTANCE_CUSTOM.x is the particle
// angle (zero on a plain instance) and COLOR is the particle colour ramp (white on a plain one).
// With `align_velocity` the long axis of the quad follows the instance Y axis (ParticleFlagAlignY)
// and only rolls to face the camera, which is what makes a spark a streak.
//
// Every vfx shader fades in view space between 0.3 m and 1.2 m of the camera, so an effect in the
// first-person hand or a breath in the face never fills the screen.
// ==================================================================================================

uniform sampler2D mask : hint_default_white, repeat_disable, filter_linear_mipmap;
uniform vec4 tint : source_color = vec4(1.0);
uniform float energy = 1.0;
uniform float opacity = 1.0;
uniform float occlude = 0.0;
uniform float hot_core = 0.0;
uniform float spin = 0.0;
uniform float align_velocity = 0.0;
uniform float soft_depth = 0.0;
uniform sampler2D depth_texture : hint_depth_texture, repeat_disable, filter_nearest;

// HEAT (flame puffs that cool to smoke). With `use_heat` 1 the particle colour ramp is not a colour:
// its red is brightness and its green is heat, 1 at birth and 0 when cold. The sprite is then drawn
// in `tint` while hot and `tint_cool` when cold, and covers `occlude` while hot and `occlude_cool`
// when cold: one emitter is the fire and the smoke it turns into.
uniform float use_heat = 0.0;
uniform vec4 tint_cool : source_color = vec4(0.02, 0.02, 0.02, 1.0);
uniform float occlude_cool = 0.0;

// DISSOLVE: the mask is torn by noise (more as a heat particle cools), so a puff is ragged and
// breaks up instead of being a soft disc that fades.
uniform sampler2D noise : hint_default_white, repeat_enable, filter_linear;
uniform float dissolve = 0.0;

// COVERAGE (VfxCoverageRules.SpriteOpacity is this same rule, tested). A particle cannot be governed
// from code one by one, so a large soft one is governed here: with `span_limit` above 0, a sprite
// wider than that many metres per metre of distance from the eye is thinned in proportion, down to
// `span_floor`. A puff of flame or mist that drifts up to the camera is seen through, never a wall.
uniform float span_limit = 0.0;
uniform float span_floor = 0.12;
varying float governed;

void vertex() {
	float scale_x = length(MODEL_MATRIX[0].xyz);
	float scale_y = length(MODEL_MATRIX[1].xyz);
	mat4 world;
	if (align_velocity > 0.5) {
		vec3 y_axis = MODEL_MATRIX[1].xyz / max(scale_y, 0.000001);
		vec3 to_camera = normalize(INV_VIEW_MATRIX[3].xyz - MODEL_MATRIX[3].xyz);
		vec3 x_axis = normalize(cross(y_axis, to_camera) + vec3(0.00001, 0.0, 0.0));
		vec3 z_axis = cross(x_axis, y_axis);
		world = mat4(vec4(x_axis * scale_x, 0.0), vec4(y_axis * scale_y, 0.0), vec4(z_axis, 0.0), MODEL_MATRIX[3]);
	} else {
		float angle = INSTANCE_CUSTOM.x + spin;
		float c = cos(angle);
		float s = sin(angle);
		mat4 facing = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
		mat4 roll = mat4(vec4(c, -s, 0.0, 0.0), vec4(s, c, 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
		mat4 size = mat4(vec4(scale_x, 0.0, 0.0, 0.0), vec4(0.0, scale_y, 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
		world = facing * roll * size;
	}
	governed = 1.0;
	if (span_limit > 0.0) {
		float depth = max(0.5, -(VIEW_MATRIX * vec4(MODEL_MATRIX[3].xyz, 1.0)).z);
		governed = clamp(span_limit * depth / max(max(scale_x, scale_y), 0.0001), span_floor, 1.0);
	}
	MODELVIEW_MATRIX = VIEW_MATRIX * world;
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	float shape = texture(mask, UV).r;
	float heat = mix(1.0, COLOR.g, use_heat);
	float bright = mix(1.0, COLOR.r, use_heat);
	if (dissolve > 0.0) {
		float grain = textureLod(noise, UV * 1.3, 0.0).r;
		float cut = dissolve * (0.2 + 0.6 * (1.0 - heat));
		shape *= smoothstep(cut, cut + 0.3, shape * (0.55 + 0.9 * grain));
	}
	vec3 plain = tint.rgb * COLOR.rgb;
	vec3 heated = mix(tint_cool.rgb, tint.rgb, heat) * bright;
	vec3 base = mix(plain, heated, use_heat);
	float fade = smoothstep(0.3, 1.2, length(VERTEX));
	if (soft_depth > 0.0) {
		// Fade where the quad meets the ground or a wall, so a flare on the floor has no cut line.
		// View-space Z is negative ahead of the camera: the scene behind the quad is further below 0.
		float scene_depth = textureLod(depth_texture, SCREEN_UV, 0.0).r;
		vec4 scene_view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, scene_depth, 1.0);
		scene_view.xyz /= scene_view.w;
		fade *= clamp((VERTEX.z - scene_view.z) / soft_depth, 0.0, 1.0);
	}
	float alpha = clamp(shape * tint.a * COLOR.a * opacity, 0.0, 1.0) * fade * governed;
	vec3 rgb = mix(base, vec3(1.0), clamp(hot_core * shape * shape, 0.0, 1.0)) * energy;
	ALBEDO = rgb * alpha;
	ALPHA = alpha * mix(occlude, mix(occlude_cool, occlude, heat), use_heat);
}
