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
	MODELVIEW_MATRIX = VIEW_MATRIX * world;
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	float shape = texture(mask, UV).r;
	vec4 colour = tint * COLOR;
	float fade = smoothstep(0.3, 1.2, length(VERTEX));
	if (soft_depth > 0.0) {
		// Fade where the quad meets the ground or a wall, so a flare on the floor has no cut line.
		float scene_depth = textureLod(depth_texture, SCREEN_UV, 0.0).r;
		vec4 scene_view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, scene_depth, 1.0);
		scene_view.xyz /= scene_view.w;
		fade *= clamp(1.0 - smoothstep(scene_view.z + soft_depth, scene_view.z, VERTEX.z), 0.0, 1.0);
	}
	float alpha = clamp(shape * colour.a * opacity, 0.0, 1.0) * fade;
	vec3 rgb = mix(colour.rgb, vec3(1.0), clamp(hot_core * shape * shape, 0.0, 1.0)) * energy;
	ALBEDO = rgb * alpha;
	ALPHA = alpha * occlude;
}
