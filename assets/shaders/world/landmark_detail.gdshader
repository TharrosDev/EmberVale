shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, cull_disabled;

// ==================================================================================================
// LANDMARK SURFACE: the model's own textures plus a stone grain that only exists up close.
//
// WHY. A generated landmark carries ONE atlas over a 12-36 m body, which is 20-40 texels per metre
// at its foot: sharp on the skyline, a smear when the player stands against it. This keeps the
// model's albedo, normal and ORM maps exactly as imported and lays a procedural grain over them,
// projected from WORLD space on three planes, that fades out with distance (gone by
// detail_fade_end, 25 m). No texture file is added: the grain is the same lattice value noise the
// ground uses (world_surface.gdshader), so a monolith and the turf under it share one hand.
//
// COST. Past the fade distance the fragment is three texture reads and the weather lines; the grain
// sits behind one branch. Inside it, each projection plane with a visible weight costs two noise
// taps (eight hashes) and returns its slope analytically, so there are no finite-difference
// re-samples. A flat face pays for one plane, a corner for two or three.
//
// APPLIED BY landmark_detail.gd on the root of a scenes/props/lm_*.tscn wrapper, which copies the
// imported StandardMaterial3D's textures into these uniforms at _ready. Weather (world_wetness,
// world_snow) follows world_scatter.gdshader line for line; fog is the engine's own.
//
// Back faces are DRAWN, as the imported material draws them: every generated .glb is exported
// doubleSided, and the smaller pieces (rubble, the ruined wall, the outcrop) have open edges that
// would show as holes the moment this material replaced the imported one. A back face is lit from
// its own side (FRONT_FACING below), which is what the engine does for a StandardMaterial3D.
// ==================================================================================================

global uniform float world_wetness;
global uniform float world_snow;

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform bool has_albedo_texture = false;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap, repeat_enable;
uniform bool has_normal_texture = false;
uniform float normal_scale = 1.0;
// glTF packing: R occlusion, G roughness, B metallic.
uniform sampler2D orm_texture : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool has_orm_texture = false;
uniform bool orm_has_occlusion = false;
uniform float roughness_value = 0.8;
uniform float metallic_value = 0.0;

// Metres across one coarse lump. The fine tooth is 4.7 times smaller.
uniform float detail_scale = 0.8;
// How far the grain pushes the albedo either way (0.3 = up to 30% lighter or darker).
uniform float detail_albedo : hint_range(0.0, 1.0) = 0.3;
// Multiplier on the grain's slope. 1.0 tilts the normal by about 5-10 degrees, 25 at the sharpest.
uniform float detail_bump : hint_range(0.0, 3.0) = 1.0;
uniform float detail_fade_end = 25.0;

varying vec3 world_pos;
varying vec3 world_normal;

// Integer lattice hash. The ground's fract-based hash loses its bits a few kilometres from the
// origin at this frequency; landmarks stand in every realm, so this one works on the cell index.
float hash21(vec2 cell) {
	uvec2 q = uvec2(ivec2(cell)) * uvec2(1597334673u, 1103515245u);
	uint n = (q.x ^ q.y) * 1597334673u;
	return float(n) * (1.0 / 4294967295.0);
}

// Value noise. x = value 0..1, yz = its gradient per unit of p.
vec3 vnoise_d(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	vec2 du = 6.0 * f * (1.0 - f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	float k1 = b - a;
	float k2 = c - a;
	float k4 = a - b - c + d;
	return vec3(a + k1 * u.x + k2 * u.y + k4 * u.x * u.y, du * vec2(k1 + k4 * u.y, k2 + k4 * u.x));
}

// The second octave is turned 31 degrees so the two lattices share no axis (world_surface.gdshader).
const mat2 OCTAVE_TURN = mat2(vec2(0.857, 0.515), vec2(-0.515, 0.857));

// One projection plane. x = tone 0..1, yz = dimensionless slope of the grain along the plane's axes.
vec3 grain(vec2 uv, float fine) {
	vec2 p = uv / max(detail_scale, 0.05);
	vec3 lump = vnoise_d(p);
	vec3 tooth = vnoise_d(OCTAVE_TURN * p * 4.7 + 11.7);
	float tone = lump.x * 0.62 + mix(0.5, tooth.x, fine) * 0.38;
	// d/dp of tooth(M * p * 4.7) is 4.7 * transpose(M) * gradient; `v * M` is that transpose product.
	// The lump's slope is left per cell, so a bigger detail_scale makes deeper lumps, not flatter ones.
	vec2 slope = lump.yz * 0.22 + (tooth.yz * OCTAVE_TURN) * (0.034 * 4.7 * fine);
	return vec3(tone, slope);
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
}

void fragment() {
	vec4 texel = albedo_color;
	if (has_albedo_texture) {
		texel *= texture(albedo_texture, UV);
	}
	vec3 albedo = texel.rgb;
	float rough = roughness_value;
	float metal = metallic_value;
	if (has_orm_texture) {
		vec3 orm = texture(orm_texture, UV).rgb;
		rough *= orm.g;
		metal *= orm.b;
		if (orm_has_occlusion) {
			AO = orm.r;
		}
	}
	if (has_normal_texture) {
		NORMAL_MAP = texture(normal_texture, UV).rgb;
		NORMAL_MAP_DEPTH = normal_scale;
	}

	vec3 wn = normalize(world_normal);
	if (!FRONT_FACING) {
		wn = -wn;
	}
	float view_distance = length(VERTEX);
	float fade = 1.0 - smoothstep(detail_fade_end * 0.4, detail_fade_end, view_distance);
	// A wrapper that only tints (detail_strength 0) pays for no grain at any distance.
	if (fade > 0.004 && detail_albedo + detail_bump > 0.0) {
		// The tooth is a sixth of a metre across; it leaves before it can shimmer.
		float fine = 1.0 - smoothstep(detail_fade_end * 0.2, detail_fade_end * 0.55, view_distance);
		vec3 weight = abs(wn);
		weight *= weight;
		weight *= weight;
		weight /= weight.x + weight.y + weight.z;
		float tone = 0.0;
		float used = 0.0;
		vec3 slope = vec3(0.0);
		// Offsets keep the three planes from showing the same pattern where they meet on a corner.
		if (weight.x > 0.03) {
			vec3 g = grain(world_pos.zy + vec2(37.1, 9.3), fine);
			tone += g.x * weight.x;
			used += weight.x;
			slope += vec3(0.0, g.z, g.y) * weight.x;
		}
		if (weight.y > 0.03) {
			vec3 g = grain(world_pos.xz, fine);
			tone += g.x * weight.y;
			used += weight.y;
			slope += vec3(g.y, 0.0, g.z) * weight.y;
		}
		if (weight.z > 0.03) {
			vec3 g = grain(world_pos.xy + vec2(71.9, 23.7), fine);
			tone += g.x * weight.z;
			used += weight.z;
			slope += vec3(g.y, g.z, 0.0) * weight.z;
		}
		tone /= max(used, 0.001);
		albedo *= 1.0 + (tone - 0.5) * 2.0 * detail_albedo * fade;
		rough = clamp(rough + (0.5 - tone) * 0.2 * fade, 0.0, 1.0);
		// Tilt against the grain's surface gradient: the part of the slope lying in the surface.
		slope *= detail_bump * fade;
		vec3 bumped = normalize(wn - (slope - wn * dot(slope, wn)));
		NORMAL = normalize((VIEW_MATRIX * vec4(bumped, 0.0)).xyz);
	}

	// Weather, as world_scatter.gdshader does it, so a landmark and the rocks at its foot agree.
	float wet = world_wetness * smoothstep(0.1, 0.8, wn.y);
	float patches = 0.5 + 0.5 * sin(world_pos.x * 1.8 + sin(world_pos.z * 2.1));
	float snow = world_snow * smoothstep(0.5, 0.95, wn.y) * smoothstep(0.15, 0.65, patches);
	ALBEDO = clamp(mix(albedo * (1.0 - wet * 0.18), vec3(0.70, 0.76, 0.80), snow), vec3(0.0), vec3(1.0));
	ROUGHNESS = mix(mix(rough, max(0.24, rough * 0.55), wet), 0.86, snow);
	METALLIC = metal * (1.0 - snow);
}
