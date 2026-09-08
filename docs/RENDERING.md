# Rendering and environment

`SkyController` is the world-lifetime visual authority. `WorldEnvironmentBuilder` allocates its
sun and Environment once; the director owns all subsequent lighting, fog, exposure, weather
presentation and quality decisions. `WorldClock` and `WeatherDirector` remain the saved gameplay
authorities. Rendering never changes collision, movement, encounters, navigation or player saves.

## Authored inputs

- `data/rendering/DayCycle.tres`: eight cyclic keys, with separate sunlight, moonlight, sky,
  ambient, fog energy and exposure. Midnight interpolation wraps correctly. Transitions use an
  exponential weight independent of frame rate. Dawn and dusk retain detail; night has bounded
  ambient fill and practical lights, rather than a blue sun.
- Existing `WorldEnvironmentProfileResource`: region sunlight tint/intensity and haze. The
  director blends these values across region changes. Existing terrain biome layers, macro noise,
  slope/altitude masks, shoreline data, authored paths and LOD silhouettes are retained.
- `WorldPreparedRegionResource`: optional temperature/moisture grids from the same procedural
  climate used to bake terrain. Legacy schema-1 packages fall back to region climate settings.
  Height, water, generation identity and save schema do not change. Rebuild with
  `python tools/world_bake.py --bake`, then `--check`; never edit prepared binaries.
- `WeatherResource`: existing light, sky, fog and precipitation fields, plus wind strength.
- `Interior.tres`, `Dungeon.tres`, `Underwater.tres`: bounded presentation contributions.
  A roof ray selects the interior fallback. Water uses the existing declared body bounds.
  `EnvironmentVolume` selects an explicit profile inside an oriented box, with a blend distance
  and priority. It has no collision, no gameplay state and no separate Environment.

The machine-readable authoring specification is `data/rendering/VisualContract.json`. Existing
`docs/3D_ASSETS.md` continues to own source asset adoption and rigs. The contract records physical
material families, scale, normal/texture guidance, restrained wear, VFX rules and measurable budgets.

## Weather and materials

The world owns five project shader globals: `world_wetness`, `world_snow`, `world_rain`,
`world_wind` and `world_visual_time`. Wetness rises and dries gradually; snowfall depends on baked
climate and accumulation melts gradually. Surface response combines upward orientation and a
patch mask. This is visual accumulation, not a survival or movement mechanic.

Terrain retains its established six procedural layers. Rain darkens it by at most 22 percent and
reduces roughness with a 0.24 floor; snow is patchy rather than a uniform white overlay. Water
retains terrain-derived depth/shoreline, nonmetallic reflections, modest normals and alpha.
Wind and rain affect water agitation. Underwater tint/exposure consumes the existing water
contract; swimming remains outside this work.

Static scatter duplicates source mesh resources in memory and preserves each material surface.
Eligible StandardMaterial3D surfaces use the shared scatter shader for weather and restrained
root-pinned, independently phased vegetation wind. Normal-mapped, emissive, texture-packed
roughness/metallic and unsupported transparency materials retain their source materials.
No model, rig, animation, Meshy or Blender source is rewritten. This is deliberately not a
blanket replacement shader on characters or imported assets with advanced material features.

Rain and snow use camera-local particle boxes with bounded amounts. One quantized precipitation
collision heightfield is shared and only active during precipitation. Roof checks and explicit
shelter volumes suppress local precipitation. Surface wetness on arbitrary sheltered imported
props is not claimed; those materials keep their existing behavior unless adapted explicitly.

`EnvironmentEmitters` gives authored world lights a common distance fade and nearest-light shadow
budget. Practical lighting scales down in daytime; magical landmarks retain authored intensity.
Authored environmental particles use shared wind, quality scaling, distance limits and no shadows.
Combat-generated effects retain their own lifetime and `SpellSchools.Color` remains the magic hue
authority. Existing fire/ember assets are reused. Decals are reserved for locally authored physical
causes, with the limits in the visual contract; there is no indiscriminate decal scattering.

## Quality and performance

Low/Medium/High/Ultra are native resources and selectable in the persisted graphics settings.
Palette, day/night, region atmosphere and weather response remain common to every tier.

| Tier | Directional shadow distance | SSAO | SSIL | Volumetric fog | SSR | Particle scale |
| --- | ---: | --- | --- | --- | --- | ---: |
| Low | 55 m | off | off | off | off | 0.25 |
| Medium | 90 m | on | off | off | off | 0.50 |
| High | 130 m | on | on | on | off | 0.75 |
| Ultra | 180 m | on | on | on | on | 1.00 |

Low renders 3D at 75 percent of the viewport width and height while keeping UI at native
resolution. Medium and above render at native resolution. Mesh LOD thresholds are 3 pixels on
Low, 2 on Medium and 1 on High/Ultra; directional shadow atlases are 2048 on Low/Medium and
4096 on High/Ultra. These choices reduce the integrated-GPU cost without changing the palette.

Directional shadows use four blended cascades. Outdoor GI uses sky radiance; no streaming cell
rebuilds voxel GI. SSIL is restrained and optional. Static authored interiors may use lightmaps,
but no new lightmap bake is claimed. Volumetric fog is capped and supplements distance atmosphere.
ACES remains the tonemapper, with authored exposure limits and no automatic exposure pumping.
Bloom has a bright-source threshold and zero blanket bloom.

The 60 FPS frame budget is **16.67 ms for the whole frame**. The contract's 12 ms GPU target and
component allocations are targets, not certification. CPU/wall-clock and GPU timings must be
reported separately. This machine has Intel Iris Xe integrated graphics; higher tiers require
measurement on their intended hardware and are not assumed to achieve 60 FPS here.

## Validation and debugging

F4 includes the active tier, fog density, wetness, snow and shelter alongside the existing frame,
streaming, draw-call and resource diagnostics. `EnvironmentVisualStateEvent` exposes resolved time,
rain, snow, wind, wetness and shelter for environmental audio/effect consumers.

`python tools/embervale.py tool environment_route --render --timeout 300` boots the actual game
with an isolated automation save, dismisses the intro, retains the gameplay camera and freezes the
world clock. It captures opposing town views, all day/weather states, wilderness, waterfront,
alpine snow, the actual Ashfall house by day/night and an explicit dungeon-profile test. The latter tests composition outdoors and is
not evidence of an authored dungeon interior. The route also captures each quality tier and
records viewport GPU timings separately from wall-clock percentiles. PNG writes occur after
performance sampling. UI and live actor movement mean screenshots remain advisory, not a flaky
pixel-perfect blocking gate.

Ashfall's existing enterable house carries an authored interior volume because its visual roof has
no physics collider. The volume suppresses precipitation and blends interior fog/exposure without
adding a collision wall. The route checks shelter inside the real furnished room. Climate validation
is included in the SDK engine mode; the rendered route is included in visual/performance/full modes.

The editor/MCP entry point is `res://tools/environment_route.tscn`; it shares
`environment_route_runner.gd` with the SDK entry point. Start it with the local
`editor-application-set-state` tool and `scene` set to that path. Editor-launched captures go to
`artifacts/environment-route/<timestamp>/` and isolate their saves there. An open editor adds GPU
load, so use the standalone SDK route for performance comparisons. MCP's `runtime-errors-get`
currently reports `available: false`; its empty error list is not a runtime health assertion.
Use the route assertions, captured frames and SDK error-scanned logs together.

`EnvironmentValidation` is part of `--validate`. Pure transition tests run in the normal xUnit
suite. `python tools/embervale.py tool environment_climate_probe` checks prepared climate identity,
grid bounds and legacy package compatibility inside Godot. Use the full engine gates for
gameplay/streaming and separate rendered captures for visuals.
Prepared ground uses a one-metre grid: the former three-metre spacing lost the grading of narrow
authored trails and broke capsule/navigation traversal in three locations. Terrain, collision and
navigation still consume one shared baked field. This increases offline region data; it does not
add runtime terrain generation or change the authored route layout.
A native dummy-renderer error is a failed gate even if a probe printed PASS. Do not approve visual
baselines wholesale or describe incomplete MCP/runtime coverage as production sign-off.

Headless probes load one complete prepared scene per frame on the main thread. This prevents
dummy-renderer RID allocations during loading from racing scene instantiation; rendered gameplay
retains asynchronous requests and the existing streaming budgets. Placement checks wait for a
navigation map's first iteration before querying it; optional navigation still uses real physics
when no synchronized map is available. Detached pooled arrows release their render/physics
resources on actor teardown, including shots that resolve after the owner leaves the world.

The missing-cell regression fixture targets a missing prepared package identity, because production
does not load the authored source scene. Only that fixture's exact expected error messages are
allowlisted, and only if all assertions pass. The action-clip gate fixes the engine frame clock at
60 Hz while retaining the real rig, hit-window checks and duration tolerance.
