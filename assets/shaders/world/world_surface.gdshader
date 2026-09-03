shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

// ==================================================================================================
// THE REALM'S GROUND MATERIAL.
//
// ⚠️ SIX SEMANTIC LAYERS, NO TEXTURES, ONE DRAW CALL PER CELL. Until this pass the whole surface of
// Embervale was three colours lerped by one octave of noise, which is why every screenshot of open
// country was a single flat brown or a single flat white with props standing on it. It is now a
// layered material: ground, the sparse second surface broken into it, rock exposed by SLOPE, a cap
// laid on by HEIGHT and shed off steep faces, the compacted road from the path mask, and the wet
// shore below a waterline. Each layer is a two-tone painted surface with its own grain, contrast,
// micro-relief and roughness, authored in data/terrain_layers/ and grouped by data/biomes/.
//
// ⚠️ AND THERE ARE DELIBERATELY NO TEXTURE FILES. docs/ART_STYLE.md §4/§6.3 forbid photo texturing
// ("a photo-realistic texture would still be wrong here"), and the model set it has to sit under
// ships with zero texture images. A CC0 PBR ground pack would have been the reflex and would have
// made the terrain the only photographed thing in a hand-painted world. Detail comes from the noise
// field instead — which also means there is no tile, so there is no tiling to hide, no texture array
// to stream and no VRAM cost at all.
//
// ANTI-REPETITION, in the three places it actually shows:
//   * a MACRO field at 60-140 m decides ground-vs-sparse and pushes the whole surface's tone, so a
//     hundred metres of country never matches the next hundred;
//   * every layer mixes TWO octaves at incommensurate scales, so nothing has one visible period;
//   * steep ground is sampled on a SIDE projection (x+z, y) instead of the top-down one, so a cliff
//     is not a stretched smear of the field above it and its detail runs vertically like rock does.
// ==================================================================================================

// --- layer slots: 0 ground · 1 sparse · 2 rock · 3 cap · 4 road · 5 shore -------------------------
// ⚠️ NOT hinted source_color: a packed array set from C# gets no sRGB conversion from the engine, so
// the conversion is done explicitly in WorldCellPresentation and these arrive linear.
uniform vec3 layer_low[6];
uniform vec3 layer_high[6];
// x grain (metres per cycle) · y breakup · z relief · w roughness
uniform vec4 layer_params[6];
uniform float layer_specular[6];

// --- placement -----------------------------------------------------------------------------------
uniform vec2 slope_band = vec2(0.35, 0.85);
uniform vec2 height_band = vec2(18.0, 34.0);
uniform float cap_slope_shed : hint_range(0.0, 1.0) = 0.8;
uniform float sparse_coverage : hint_range(0.0, 1.0) = 0.45;
uniform float macro_scale = 90.0;
uniform float macro_strength : hint_range(0.0, 0.6) = 0.22;
uniform float shore_level = -999.0;
uniform float shore_band = 0.0;
uniform float strata_scale = 3.2;
uniform float strata_strength : hint_range(0.0, 1.0) = 0.35;
uniform float terrain_seed = 3800.0;

// --- per-cell -------------------------------------------------------------------------------------
uniform vec4 tint : source_color = vec4(1.0);
uniform float tint_strength : hint_range(0.0, 1.0) = 0.0;

// --- the generated environment, straight off the mesh ---------------------------------------------
// COLOR.r road mask - COLOR.g yard mask - COLOR.b wetness - COLOR.a alpine weight
// UV2.x moisture - UV2.y mountain
//
// THIS IS WHAT MAKES A BIOME BORDER AN ECOTONE. Until this pass every placement decision in this
// shader came from world Y and the surface gradient alone, so "where does snow start" was a contour
// line at a constant height and "where is the ground wet" was a single per-cell waterline constant.
// Both are per-cell scalars pretending to be geography, and the second one could not describe a
// river at all. They are continuous fields now, sampled per vertex out of the same generator the
// ground itself came from, so a snowline drifts with exposure and moisture, a wet margin follows a
// channel across a cell boundary, and the patchy second surface thins out where the country is dry.
//
// The cell's authored biome still chooses WHICH six layers are in play. These choose how much of
// each is showing. That split is deliberate: art direction stays authored, distribution is generated.
uniform float environment_strength : hint_range(0.0, 1.0) = 1.0;

varying vec3 world_pos;
varying vec3 world_normal;
varying vec4 environment;
varying vec2 environment_uv;

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 345.45) + terrain_seed * 0.0013);
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

// Two octaves at an irrational-ish ratio, the second ROTATED.
//
// ⚠️ THE ROTATION IS NOT DECORATION. Value noise is built on an axis-aligned integer lattice, so
// every octave of it has horizontal and vertical structure in it — stack two unrotated octaves over
// a road at a grazing angle and the ground grows visible rectangles, which is what the first pass of
// this shader put down the Kingsway. A 31-degree turn between octaves has no common axis with the
// first, and the blockiness goes.
const mat2 OCTAVE_TURN = mat2(vec2(0.857, 0.515), vec2(-0.515, 0.857));

float grain2(vec2 p) {
	return vnoise(p) * 0.66 + vnoise(OCTAVE_TURN * p * 2.37 + 11.7) * 0.34;
}

// A layer's painted albedo plus the scalar micro-height its normal is derived from.
// `flat_uv` is the top-down projection, `side_uv` the one steep ground uses instead.
void eval_layer(vec3 low, vec3 high, vec4 params, vec2 flat_uv, vec2 side_uv, float sideness,
		float detail_fade, float near_fade, float view_distance,
		out vec3 albedo, out float rough, out float height) {
	// ⚠️ THE GRAIN COARSENS WITH DISTANCE, AND WITHOUT THIS THE FINE LAYERS MOIRÉ.
	// A layer authored at a 1.2 m cycle — gravel, mine spoil, scree, all the ones that SHOULD be
	// fine — is under a pixel wide by thirty metres out on ground seen at a grazing angle, which is
	// the textbook recipe for aliasing. The Emberdeep's spoil banks came out looking like polished
	// wood grain. Holding the cycle to roughly a pixel and a half is a mip bias by another name: the
	// pattern is no longer resolvable at that range, so the only thing lost is the aliasing.
	float scale = 1.0 / max(0.4, max(params.x, view_distance * 0.022));

	// ⚠️ BLEND THE RESULTS, NEVER THE COORDINATES. THIS ONE LINE DREW A CONTOUR MAP ON THE REALM.
	//
	// mix(flat_uv, side_uv, sideness) interpolates between two DIFFERENT coordinate systems, and a
	// noise field read at an interpolated coordinate is not an interpolation of that field — it is
	// the field evaluated along a path that sweeps sideways through it as the blend factor changes.
	// Across the slope band that factor varies smoothly over the whole of a hillside, so the pattern
	// smears into closed loops that follow the surface: the West Downs rendered as a topographic map
	// and the cliffs above the Emberdeep as fingerprints. It was invisible before real geography
	// arrived only because the terrain had almost no slope in it to blend across.
	//
	// Sampling both projections and mixing what comes back is the correct operation and the only one
	// that has no artefact of its own. The two early-outs matter: nearly all of the realm is walkable
	// ground that never reaches the side projection at all, and paying for two noise evaluations
	// everywhere to serve the cliffs would be most of a millisecond for nothing.
	float n;
	float fine;
	if (sideness <= 0.01) {
		vec2 uv = flat_uv * scale;
		n = grain2(uv);
		fine = vnoise(uv * 4.1 + 5.3);
	} else if (sideness >= 0.99) {
		vec2 uv = side_uv * scale;
		n = grain2(uv);
		fine = vnoise(uv * 4.1 + 5.3);
	} else {
		vec2 uv_flat = flat_uv * scale;
		vec2 uv_side = side_uv * scale;
		n = mix(grain2(uv_flat), grain2(uv_side), sideness);
		fine = mix(vnoise(uv_flat * 4.1 + 5.3), vnoise(uv_side * 4.1 + 5.3), sideness);
	}
	// ⚠️ THE 2.6 IS DOING REAL WORK. At 1.0 the mix only ever reaches Low and High at the noise
	// field's own extremes, which it hits on perhaps a percent of a hillside — so a two-tone layer
	// rendered as one tone and the first pass of this shader still produced flat brown countryside.
	// Over-driving the mix and clamping makes both tones actually appear, over area, in patches.
	albedo = mix(low, high, clamp((n - 0.5) * params.y * 2.6 + 0.5, 0.0, 1.0));
	// A little of the fine octave in the albedo too: it is what stops a hillside reading as a
	// gradient once the normal detail has faded out with distance.
	albedo *= 1.0 + (fine - 0.5) * 0.22 * params.y * near_fade;
	// ⚠️ Faded with the same near curve as the albedo term it comes from: a roughness that varies
	// at half a metre with no distance damping shimmers on any lit slope.
	rough = params.w * (1.0 - 0.12 * near_fade + 0.24 * fine * near_fade);
	height = params.z;
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	environment = COLOR;
	environment_uv = UV2;
}

void fragment() {
	vec3 wn = normalize(world_normal);
	// Rise over run — the same grade the C# validators and the traversal probe measure, so "rock
	// starts at 0.35" in a biome profile means the same number an author reads in a route report.
	float slope = length(wn.xz) / max(0.08, abs(wn.y));
	float slope_mask = smoothstep(slope_band.x, slope_band.y, slope);

	vec2 flat_uv = world_pos.xz;
	// ⚠️ THE SIDE PROJECTION USES FIXED WORLD PLANES. IT MUST NOT BE DERIVED FROM THE NORMAL.
	//
	// This has now been wrong twice, in opposite directions, and both versions drew a pattern on
	// every hillside in the realm. (x + z, y) was the first cut: two families of bands crossing into
	// herringbone. Projecting onto the face's own horizontal TANGENT was the second, and it is worse
	// in a way that only showed up once the terrain had real curvature in it — the tangent rotates
	// as the surface turns, so walking a coordinate around a dome sweeps it through 360 degrees and
	// the noise closes into concentric rings. The West Downs came out looking like a contour map,
	// and the Ember Crown's cliffs like fingerprints. A projection that depends on the surface is a
	// projection that spirals on a curved surface; there is no amount of warping that fixes it.
	//
	// So: the two vertical world planes, XY and ZY, chosen by which one the face is more nearly
	// parallel to. Both are FIXED — they cannot rotate, so nothing can wind around anything — and
	// the blend between them only happens on ground facing within a few degrees of the diagonal,
	// where the two coordinates are nearly interchangeable anyway. This is the standard triplanar
	// answer with the top plane already handled by flat_uv.
	float axis_x = abs(wn.x);
	float axis_z = abs(wn.z);
	// ⚠️ A NARROW BAND, BECAUSE THIS ONE IS STILL A COORDINATE MIX. Blending the two vertical
	// planes properly would mean a third noise evaluation for the sake of ground facing within a few
	// degrees of the diagonal. Keeping the cross-fade tight shrinks the residual swirl to a band a
	// metre or two wide on a curved face, and both planes carry the same field at the same scale, so
	// there is nothing on either side of it for the eye to catch on.
	float axis_blend = smoothstep(0.47, 0.53, axis_x / max(1e-4, axis_x + axis_z));
	vec2 side_uv = mix(
		vec2(world_pos.z, world_pos.y * 1.35),
		vec2(world_pos.x, world_pos.y * 1.35),
		axis_blend);
	// ⚠️ And it only engages on ground that is genuinely a face. Any projection change is visible
	// while it is happening, so doing it across the walkable band puts an artefact on exactly the
	// ground the player spends their time on.
	// ⚠️ ONLY THE ROCK LAYER TAKES THE SIDE PROJECTION, AND THAT IS A PERFORMANCE FACT AS WELL AS AN
	// ART ONE. Evaluating a layer in two projections and mixing the results is the only way to blend
	// them without smearing the field (see eval_layer), but it doubles the noise cost of every layer
	// that does it — and handing `sideness` to all six put the Ember Crown's mean frame at 20.6 ms
	// against a 15.5 ms baseline and its worst at 28.6, over the authored budget. Rock is the only
	// layer that has anything to say on a face steep enough to reach the side projection: the cap is
	// shed by slope, roads and yards do not exist on cliffs, and the ground and sparse layers are
	// already being replaced by rock there. So the other five stay top-down, which costs nothing and
	// looks identical.
	float sideness = smoothstep(0.8, 1.5, slope);
	float flat_sideness = 0.0;

	// Distance damping. High-frequency relief on a 300 m vista is aliasing, not detail.
	float view_distance = length(VERTEX);
	float detail_fade = 1.0 - smoothstep(45.0, 170.0, view_distance);
	// ⚠️ A SECOND, MUCH TIGHTER FADE FOR THE SUB-METRE DETAIL. Terrain is nearly always seen at a
	// grazing angle, and a 35 cm noise field on a hillside forty metres away is not detail — it is
	// one sample per several pixels, which aliases into moving herringbone. Fading the fine octaves
	// out by thirty metres costs nothing anyone can see and removes the chevrons entirely.
	float near_fade = 1.0 - smoothstep(9.0, 32.0, view_distance);

	float macro = grain2(flat_uv / max(20.0, macro_scale));
	float road = clamp(environment.r, 0.0, 1.0);
	float activity = clamp(environment.g, 0.0, 1.0);
	float wetness = clamp(environment.b, 0.0, 1.0) * environment_strength;
	float alpine = clamp(environment.a, 0.0, 1.0) * environment_strength;
	float moisture = mix(0.5, clamp(environment_uv.x, 0.0, 1.0), environment_strength);

	vec3 albedo;
	float rough;
	float height;   // the blended layers' micro-relief AMPLITUDE, not a noise sample
	eval_layer(layer_low[0], layer_high[0], layer_params[0], flat_uv, side_uv, flat_sideness, detail_fade,
		near_fade, view_distance, albedo, rough, height);
	float spec = layer_specular[0];

	// --- the patchy second surface, placed by the macro field so it is never uniform -------------
	{
		vec3 a; float r; float h;
		eval_layer(layer_low[1], layer_high[1], layer_params[1], flat_uv, side_uv, flat_sideness,
			detail_fade, near_fade, view_distance, a, r, h);
		// Dry country shows more of its second surface and wet country less: the sparse layer is
		// bare soil, dead grass, scree or ash in every biome that authors one, and all four are what
		// is left where the green did not hold. Moisture shifts the macro field's threshold rather
		// than scaling the result, so the PATCHES move and change shape instead of fading uniformly -
		// a fade would read as a haze over the ground rather than as different ground.
		float dryness = 1.0 - moisture;
		float coverage = clamp(sparse_coverage * (0.55 + dryness * 0.9), 0.0, 1.0);
		float m = smoothstep(0.5 - coverage * 0.5, 0.95 - coverage * 0.5, macro);
		m *= 1.0 - slope_mask * 0.7;
		albedo = mix(albedo, a, m);
		rough = mix(rough, r, m);
		height = mix(height, h, m);
		spec = mix(spec, layer_specular[1], m);
	}

	// --- rock, driven by the gradient. This is the line that makes a cliff read as a cliff. ------
	{
		vec3 a; float r; float h;
		eval_layer(layer_low[2], layer_high[2], layer_params[2], flat_uv, side_uv, sideness,
			detail_fade, near_fade, view_distance, a, r, h);
		// Bedding planes: a horizontal band field in WORLD Y, so exposed faces across the whole
		// region agree about where the beds are and a cut looks quarried rather than tinted.
		// ⚠️ THE BEDS ARE WARPED OR A DOME BECOMES A FINGERPRINT. Banding on raw world Y means the
		// beds are perfect horizontal planes, and a horizontal plane cutting a hill draws a contour
		// ring — so every knoll in the realm came out ringed like a tree stump. Nudging the Y the
		// bands are read at by a low-frequency lateral field tilts and buckles them, which is what
		// real bedding does anyway.
		// ⚠️ AND THE SECOND COORDINATE IS A NOISE FIELD, NOT (x + z). Using the diagonal sum meant
		// that on near-level ground — where world Y barely varies — the whole strata term reduced to
		// a function of x + z, which draws parallel 45-degree streaks. On the Ancient Aerie's
		// snowfields those were the most visible thing in the frame.
		// ⚠️ THE BEDS WIDEN WITH DISTANCE FOR THE SAME REASON THE GRAIN COARSENS. Banding on world Y
		// projects to closely spaced lines on a steep face, so a 2.2 m bed on the Emberdeep's spoil
		// banks resolved to well under a pixel at twenty metres and the whole excavation came out
		// looking like polished wood grain. This is the one term in the shader whose apparent
		// frequency depends on the SLOPE as well as the distance, which is why it aliased first.
		// ⚠️ TWO THINGS KEEP BEDDING FROM DRAWING CONTOUR RINGS, AND BOTH OF THEM ARE LOAD-BEARING.
		//
		// A band field read off world Y is, by construction, a set of horizontal planes. Where those
		// planes cut a curved hill they trace its contours, so a dome comes out ringed like a tree
		// stump and a spur comes out as nested chevrons. The existing warp - about a metre and a
		// third against a three-metre bed - was sized for terrain that barely curved, and the moment
		// real geography arrived the whole of the West Downs was drawing a maze pattern on itself.
		// The warp now runs to several times a bed thickness and carries a second, shorter octave,
		// so consecutive beds are displaced by different amounts and the rings cannot close.
		//
		// ⚠️ AND BEDDING ONLY SHOWS WHERE ROCK IS ACTUALLY EXPOSED. The rock layer starts blending in
		// at the biome's slope band - around 0.34, which is grassy downland - and strata rode in with
		// it at full strength. Real bedding is a feature of a cut face, not of a hillside with soil
		// on it, so it now fades in over a much steeper band of its own. Between them the pattern
		// went from the most visible thing in a wide shot to something you have to walk up to a
		// cliff to see, which is where it was always meant to be.
		float bed_scale = max(0.5, max(strata_scale, view_distance * 0.022));
		// One octave, scaled to the bed thickness. A second short octave was tried and reverted: it
		// bought nothing the slope gate below had not already fixed, and it cost two more noise
		// evaluations on every pixel with any rock in it.
		float bed_warp = (grain2(flat_uv / 26.0) - 0.5) * bed_scale * 2.6;
		float strata = vnoise(vec2((world_pos.y + bed_warp) / bed_scale,
			grain2(flat_uv / 30.0) * 5.0));
		float bedding = strata_strength * smoothstep(0.75, 1.45, slope);
		a *= 1.0 - bedding * (0.5 - strata) * 0.9;
		h *= 1.0 + bedding * 0.6;
		albedo = mix(albedo, a, slope_mask);
		rough = mix(rough, r, slope_mask);
		height = mix(height, h, slope_mask);
		spec = mix(spec, layer_specular[2], slope_mask);
	}

	// --- the altitude cap, shed off anything steep -----------------------------------------------
	{
		vec3 a; float r; float h;
		eval_layer(layer_low[3], layer_high[3], layer_params[3], flat_uv, side_uv, flat_sideness,
			detail_fade, near_fade, view_distance, a, r, h);
		// A SNOWLINE IS NOT A CONTOUR LINE, AND THE HEIGHT BAND ALONE COULD ONLY DRAW ONE.
		// The authored band is still the floor of the effect - a biome that says "snow above 30 m"
		// gets snow above 30 m - but the generator's alpine weight already knows about exposure,
		// mountain strength and the realm's own lapse rate, so a cold north face carries the cap
		// lower than a sheltered shoulder at the same altitude. The macro field keeps its few metres
		// of wobble on top, which is what stops the transition reading as a line at all.
		float m = smoothstep(height_band.x, height_band.y, world_pos.y);
		m = max(m, alpine);
		m = clamp(m + (macro - 0.5) * 0.45, 0.0, 1.0);
		m *= 1.0 - cap_slope_shed * slope_mask;
		albedo = mix(albedo, a, m);
		rough = mix(rough, r, m);
		height = mix(height, h, m);
		spec = mix(spec, layer_specular[3], m);
	}

	// --- the wet margin ---------------------------------------------------------------------------
	// TWO SOURCES, AND THE SECOND ONE IS WHY A RIVER HAS BANKS. shore_level/shore_band describe one
	// authored waterline per cell, which is all a lake needs and all a lake ever was. A generated
	// river runs downhill across four cells and has no single Y, so a height-banded shore cannot see
	// it: it would draw a horizontal stripe across the valley and nothing along the channel. The
	// generator's wetness field is the channel, so the two are taken together and the riparian
	// margin appears wherever the water actually is.
	// ⚠️ THE THRESHOLD IS 0.35 BECAUSE THAT IS WHERE THE LAYER STARTS CONTRIBUTING, AND A `> 0.0`
	// HERE COSTS A MILLISECOND. The mask below is smoothstep(0.35, 0.85, wetness), so anything drier
	// than 0.35 mixes in exactly nothing — but testing `wetness > 0.0` still ran a full extra layer
	// evaluation over every acre of the realm with any trace of moisture in it, which is most of it.
	// The original `shore_band > 0.0` comment called this "one compare" for a reason.
	if (shore_band > 0.0 || wetness > 0.35) {
		vec3 a; float r; float h;
		eval_layer(layer_low[5], layer_high[5], layer_params[5], flat_uv, side_uv, flat_sideness,
			detail_fade, near_fade, view_distance, a, r, h);
		float authored_shore = shore_band > 0.0
			? 1.0 - smoothstep(shore_level, shore_level + shore_band, world_pos.y)
			: 0.0;
		float m = max(authored_shore, smoothstep(0.35, 0.85, wetness));
		m *= 1.0 - slope_mask * 0.5;
		albedo = mix(albedo, a, m);
		rough = mix(rough, r, m);
		height = mix(height, h, m);
		spec = mix(spec, layer_specular[5], m);
	}

	// --- the road. It is last because a road is cut THROUGH whatever is there. --------------------
	{
		vec3 a; float r; float h;
		eval_layer(layer_low[4], layer_high[4], layer_params[4], flat_uv, side_uv, flat_sideness,
			detail_fade, near_fade, view_distance, a, r, h);
		// Wheel ruts and a worn crown: a strongly anisotropic field so the wear reads as travel
		// down a line rather than as a differently-coloured stripe.
		float ruts = grain2(flat_uv * vec2(0.55, 0.075));
		float wear = 0.80 + 0.30 * ruts;
		// Loose metalling: sparse bright chips through the compacted surface. Kept faint and taken
		// from the rotated two-octave field — a single unrotated octave threshholded high draws
		// literal white rectangles down the carriageway.
		float chips = smoothstep(0.78, 0.97, grain2(flat_uv * 3.1 + 17.0));
		vec3 road_albedo = a * wear + chips * 0.05 * near_fade;
		float road_mask = road;
		albedo = mix(albedo, road_albedo, road_mask);
		rough = mix(rough, r, road_mask);
		// A road is the smoothest ground in the realm; that is what "compacted" means.
		height = mix(height, h * 0.55, road_mask);
		spec = mix(spec, layer_specular[4], road_mask);
		// A yard is trodden, not metalled: the road surface at half strength, no chips.
		float yard = activity * (1.0 - road_mask);
		albedo = mix(albedo, mix(albedo, a * 0.95, 0.75), yard);
		rough = mix(rough, r, yard * 0.8);
		height = mix(height, h * 0.3, yard);
	}

	// --- macro tone. The one term applied to everything, and the reason a wide shot has depth. ----
	albedo *= 1.0 + (macro - 0.5) * macro_strength * 2.0;

	// THE CELL TINT IS GONE AND IT IS NOT COMING BACK. It could only apply a flat wash over a
	// rectangle, so it had to be faded out at the cell edge to stop it drawing the lattice back onto
	// the ground - a per-cell constant working against the whole point of a world-space field. Every
	// job it was doing is now done by the generated environment above, which varies continuously and
	// does not know where a cell ends. tint/tint_strength are retained as uniforms so the authored
	// resources that still carry them load without complaint; nothing reads them.
	albedo *= 1.0 + (moisture - 0.5) * 0.10;

	// --- micro-relief into the normal -------------------------------------------------------------
	// Derived from the same scalar field the albedo used, sampled twice more at a 12 cm offset. This
	// is what makes flat ground catch a low sun at all; without it the countryside is a painted
	// plane and every screenshot proves it.
	// Two scales, because one does not read: a ~2 m field gives the ground its lumps and a ~0.35 m
	// field gives it its tooth. Sampled by finite difference off the same noise the albedo used, so
	// the shading and the paint agree about where a stone is.
	if (detail_fade > 0.01) {
		// The bump gradient reads the same field the albedo did, so it takes the same projection
		// rather than an interpolated one. Picking the dominant projection instead of mixing keeps
		// this to one evaluation: a normal perturbation of a couple of centimetres does not need a
		// cross-fade, and a hard switch inside the slope band is invisible where the albedo is
		// already blending across it.
		vec2 duv = sideness > 0.5 ? side_uv : flat_uv;
		float amp = clamp(height, 0.0, 1.5);
		vec2 g = vec2(0.0);
		{
			// ⚠️ THE GRADIENT IS SCALED TO A REAL BUMP HEIGHT, NOT USED RAW.
			// (h0 - hx) / e is a dimensionless slope of the NOISE, which runs 0..1 over half a
			// metre — a slope of nearly one. Feeding that into the normal directly (which the first
			// cut did, multiplied by six) swings the normal through tens of degrees per pixel, and
			// a per-pixel normal that violent does not read as texture: it aliases into contour
			// moiré that follows the surface, which is what turned the Emberdeep's spoil banks into
			// polished wood grain. A 2 cm bump over a 2.2 m wave is a slope of about 0.06, and that
			// is the number this has to produce.
			float e = 0.55;
			float s0 = 1.0 / 2.2;
			float h0 = grain2(duv * s0);
			g += vec2(h0 - grain2((duv + vec2(e, 0.0)) * s0),
					  h0 - grain2((duv + vec2(0.0, e)) * s0)) / e * (0.26 * amp);
		}
		if (near_fade > 0.01) {
			float e = 0.11;
			float s1 = 1.0 / 0.35;
			float h0 = grain2(duv * s1 + 3.7);
			g += vec2(h0 - grain2((duv + vec2(e, 0.0)) * s1 + 3.7),
					  h0 - grain2((duv + vec2(0.0, e)) * s1 + 3.7)) / e * (0.05 * amp * near_fade);
		}
		vec3 bumped = normalize(wn + vec3(g.x, 0.0, g.y) * detail_fade);
		NORMAL = normalize((VIEW_MATRIX * vec4(bumped, 0.0)).xyz);
	}

	ALBEDO = clamp(albedo, vec3(0.0), vec3(1.0));
	ROUGHNESS = clamp(rough, 0.0, 1.0);
	SPECULAR = clamp(spec, 0.0, 1.0);
}
