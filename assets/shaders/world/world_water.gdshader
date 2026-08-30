shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_burley, specular_schlick_ggx;

// ==================================================================================================
// STANDING WATER, WITH A SHORELINE TAKEN FROM THE TERRAIN.
//
// ⚠️ THE DEPTH IS BAKED INTO VERTEX COLOUR, NOT SAMPLED FROM A DEPTH BUFFER. WorldCellWater samples
// the region heightfield when it builds the surface grid and writes (surface - ground) into COLOR.r.
// That is why the water can be drawn as a generous rectangle and still have a real coastline: it
// simply fades out wherever the terrain rises through it. The alternative — a screen-space depth
// fade — needs a depth texture read per pixel and still cannot tell the shader where the bank is on
// the frame the cell streams in.
//
// Before this, every water surface in the realm was a translucent BoxMesh whose straight edges had
// to be tucked inside the basin by hand, and whose corners lay on dry ground when they were not.
// ==================================================================================================

uniform vec3 shallow_color : source_color = vec3(0.22, 0.34, 0.33);
uniform vec3 deep_color : source_color = vec3(0.05, 0.13, 0.17);
uniform float opaque_depth = 2.2;
uniform float shore_fade = 0.55;   // metres of depth over which the edge comes in
uniform float depth_range = 12.0;  // must match WorldCellWater.DepthRange
uniform float wave_scale = 7.0;
uniform float wave_speed = 0.13;

varying float depth;
varying vec3 world_pos;

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 345.45));
	p += dot(p, p + 34.345);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x),
		mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0)), f.x), f.y);
}

void vertex() {
	// Vertex colour is 8-bit unorm, so the builder encodes the SIGNED depth about 0.5. Decode it.
	depth = (COLOR.r - 0.5) * 2.0 * depth_range;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// The shoreline. `depth` may be negative on dry ground, and that is what puts the alpha's zero
	// crossing on the terrain's own contour rather than on the mesh's boundary.
	float coverage = smoothstep(0.0, shore_fade, depth);
	float d = max(depth, 0.0);
	float tone = smoothstep(0.0, max(0.2, opaque_depth), d);

	vec2 w = world_pos.xz / max(0.5, wave_scale);
	float t = TIME * wave_speed;
	float ripple = vnoise(w + vec2(t, t * 0.63)) * 0.6 + vnoise(w * 2.3 - vec2(t * 0.8, t)) * 0.4;
	float rx = vnoise(w + vec2(0.08 + t, t * 0.63)) * 0.6 + vnoise((w + vec2(0.08, 0.0)) * 2.3 - vec2(t * 0.8, t)) * 0.4;
	float rz = vnoise(w + vec2(t, 0.08 + t * 0.63)) * 0.6 + vnoise((w + vec2(0.0, 0.08)) * 2.3 - vec2(t * 0.8, t)) * 0.4;

	// Ripples flatten out in the shallows, the way real water does as it loses depth to work with.
	// ⚠️ KEPT LOW ON PURPOSE. At 0.55 the ripple normal turned a lake into leopard spots from any
	// height: the specular highlight is far more sensitive to a normal perturbation than the ground
	// is, so the amplitude that reads as "texture" on soil reads as "boiling" on water.
	float agitation = 0.22 * smoothstep(0.1, 1.2, d);
	vec3 bumped = normalize(vec3((ripple - rx) * agitation, 0.06, (ripple - rz) * agitation));
	NORMAL = normalize((VIEW_MATRIX * vec4(bumped, 0.0)).xyz);

	ALBEDO = mix(shallow_color, deep_color, tone);
	// Foam and wet glitter along the margin: one term, and it is what stops the fade reading as
	// a fog rather than as an edge.
	float margin = (1.0 - smoothstep(0.0, 0.7, d)) * smoothstep(0.02, 0.18, d);
	ALBEDO += margin * 0.16 * (0.6 + 0.4 * ripple);
	// Opaque quickly. The lakebed under a metre of water is a shape the player can never reach and
	// never needed to see, and letting the terrain's own relief through made the surface mottle.
	ALPHA = coverage * mix(0.74, 0.96, tone);
	// ⚠️ Not smoother than 0.14. At 0.06 the sun's reflection is a mirror the size of a pixel, and
	// on a shallow rippled surface that is a field of blown-out white flecks rather than a highlight.
	ROUGHNESS = mix(0.30, 0.14, tone);
	METALLIC = 0.0;
	SPECULAR = 0.5;
}
