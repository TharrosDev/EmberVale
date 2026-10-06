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

Performance/Low/Medium/High/Ultra are native resources (`data/rendering/<Tier>.tres`) and
selectable in the persisted graphics settings. Palette, day/night, region atmosphere and weather
response remain common to every tier.

⚠️ **The saved int is append-only.** `Settings.RenderQuality` stores 0 Low, 1 Medium, 2 High,
3 Ultra, 4 Performance. Performance is the lowest tier and was added last, so it is 4; the options
menu lists the tiers cheapest first through `GraphicsMath.UiOrder`. Never renumber.

| Tier | Render scale | Sun shadow | Cascades | Filter | Sun atlas | SSAO | SSIL | Vol. fog | SSR | Glow | AA | Particles | Mesh LOD |
| --- | ---: | ---: | --- | --- | ---: | --- | --- | --- | --- | --- | --- | ---: | ---: |
| Performance | 0.60 | 30 m | 2 | hard | 1024 | off | off | off | off | off | off | 0.15 | 4 px |
| Low | 0.75 | 60 m | 2 blended | very low | 2048 | off | off | off | off | on, bilinear | off | 0.35 | 3 px |
| Medium | 1.00 | 90 m | 4 blended | project | 2048 | on | off | off | off | on | off | 0.50 | 2 px |
| High | 1.00 | 130 m | 4 blended | project | 4096 | on | on | on | off | on | off | 0.75 | 1 px |
| Ultra | 1.00 | 180 m | 4 blended | project | 4096 | on | on | on | on | on | off | 1.00 | 1 px |

Medium, High and Ultra are unchanged: every field added to `RenderQualityResource` defaults to what
the game did before the field existed, and those three files do not author the new fields.

The two low tiers also carry the world-scale inputs the streamer consumes, and the smaller buffers
that matter on a GPU whose video memory is system memory:

| Tier | Draw distance | Scatter density | Enemy shadows to | Omni/spot atlas | Sky radiance | Local lights to |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Performance | 0.60 | 0.40 | 12 m | 1024 | 64 | 22 m |
| Low | 0.80 | 0.70 | 25 m | 2048 | 128 | 32 m |
| Medium and up | 1.00 | 1.00 | unlimited | project (4096) | 256 | 45/60/75 m |

- **Cascades.** Each directional cascade is another pass over every shadow caster. Two cascades
  halve that against four; the near one covers the first quarter of the range.
- **Sky radiance.** The sun moves and the sky colours are rewritten every frame, so the radiance
  cubemap is re-rendered and re-filtered continuously. Its cost follows the square of its edge.
- **Omni/spot atlas.** Below 4096 it is split into fewer, larger slots (1/1/4/4 at 1024, 1/4/4/16
  at 2048) so the few lights that still cast keep their resolution.
- **Already half resolution.** SSAO and SSIL use the engine's default half-size buffers; there is
  no project override to add.

### What scales with quality

Everything in the two tables, and nothing else. In the world it reaches four consumers through
`WorldQualityScale` (a static holding the three world dials, with a `Changed` event), and none of
them reads or writes anything while the scale is 1 / 1 / uncut, which is Medium and up:

- **Draw distance** multiplies the streamer's **Far** radius only
  (`WorldStreamingPolicy.ScaleForQuality`), every scatter tile's visibility ranges (detail end,
  HLOD begin and end) and the biome cull distance. Near and Mid are where collision, navigation and
  gameplay exist and Backdrop decides which cells are resident, so those three never move: a lower
  tier draws props and buildings less far and plays on the same world.
- **Scatter density** thins ground cover only (grass, flowers, pebbles, ferns: the layers with a
  short detail range) through `MultiMesh.VisibleInstanceCount`. Trees, rocks and scrub are the
  silhouette and keep every instance. No buffer is rewritten.
- **Enemy shadows** stop past `ActorShadowDistance` (`EnemyAIComponent`, checked four times a
  second, restoring each mesh to what it cast before).
- **Particles and local lights** take `ParticleScale` and `LocalLightDistance` in
  `EnvironmentEmitters`, as before.

Three costs were cut on every tier and are not dials. Ground cover casts no shadow (set in the
region specs). The distant scatter tier takes a mesh LOD bias of 0.5. Both are built into the
prepared cells, so they reach the game only through the master bake. A body further than 40 m
from the player, outside an action clip, has its animation stepped every third frame by the
skipped time (`CharacterAnimationComponent`). All three change what Medium, High and Ultra show,
none has been looked at in a render, and the reference captures predate them.

### Adjusting a preset

The options menu has an Advanced Graphics section: render scale, upscaling (bilinear, FSR 1.0,
FSR 2.2), anti-aliasing (off, FXAA, MSAA 2x/4x, TAA), shadows (off, or any preset's shadow bundle),
ambient occlusion, volumetric fog and glow. Each control shows the preset's value until the player
moves it. A moved control is saved as an override and the preset then reads **Custom**; setting it
back to the preset's value, or choosing a preset, clears it. Overrides are stored in `Settings`
with a "follow the preset" default (`-1`, or `0` for render scale), so a settings file written
before they existed still resolves to exactly the preset it named. `GraphicsMath` holds the rules.

FSR 2.2 is offered because Forward+ supports it. No preset selects it: it is the most expensive of
the three, and it replaces TAA rather than stacking on it.

### First run

On a fresh install (no settings file and no save slots), `SettingsService` picks a preset from
the adapter name, vendor and device type plus installed memory and thread count
(`GraphicsAutoDetect`, pure and unit-tested), then saves it:

| Adapter | Preset | Frame cap |
| --- | --- | ---: |
| Software renderer or CPU device | Performance | 30 |
| Steam Deck APU | Low | 60 |
| Integrated, at least 15 GB installed and 8 threads | Low | 60 |
| Integrated, anything less | Performance | 60 |
| Discrete, at least 15 GB and 8 threads, not a known modest part | High | none |
| Discrete, otherwise | Medium | none |
| Unidentified | Medium | none |

A saved file is never re-detected over, and an install that has saves but no settings file keeps
Medium uncapped (what it was already running) and writes that down. Headless runs and automation
(`EMBERVALE_USER_DIR` set) skip detection and keep Medium, so captures and probes do not move with
the machine that runs them. By the table, a fresh install on the development laptop (Iris Xe,
14 GB) starts on Performance at 60.

### Frame pacing

V-Sync is on by default. The cap offers uncapped, 30, 40, 60, 120 and 144, and a saved cap is
always honoured. Only with V-Sync off and no cap do the title and pause screens fall back to 60, so
an unpaced menu does not run the GPU flat out. `SettingsService.ApplyFrameCap` follows
`GameStateChangedEvent`.

### Where quality reaches the renderer

`SkyController.ApplyQuality(tier)` is the only place. It resolves the preset and the overrides,
pushes viewport, shadow, environment and sky settings, and sets `DrawDistanceScale`,
`ScatterDensityScale` and `ActorShadowDistance` for the streamer at the marked seam. It returns
early when neither the tier nor the overrides changed, because every settings apply reaches it,
including each tick of a volume drag.

### Shader compilation

There is no hand-written warm-up pass. Forward+ compiles a mesh's specialised pipelines in the
background when its surface is loaded and draws with the ubershader until they are ready, and
every route into the world holds the loading screen until the landing cells are resident
(`LoadingCoordinator`). `ApplyQuality` runs when the sky is built, before any cell streams, so
MSAA, TAA and the scaling mode are fixed before those pipelines are requested. What this does not
cover is a material first created mid-play, such as a combat effect: on a machine's first ever
session its shader is compiled once, then served from the on-disk shader cache.

Directional shadows use up to four blended cascades. Outdoor GI uses sky radiance; no streaming cell
rebuilds voxel GI. SSIL is restrained and optional. Static authored interiors may use lightmaps,
but no new lightmap bake is claimed. Volumetric fog is capped and supplements distance atmosphere.
ACES remains the tonemapper, with authored exposure limits and no automatic exposure pumping.
Bloom has a bright-source threshold and zero blanket bloom.

The 60 FPS frame budget is **16.67 ms for the whole frame**. The contract's 12 ms GPU target and
component allocations are targets, not certification. CPU/wall-clock and GPU timings must be
reported separately. This machine has Intel Iris Xe integrated graphics; higher tiers require
measurement on their intended hardware and are not assumed to achieve 60 FPS here.

## Validation and debugging

F4 includes the active tier, fog density, wetness, snow and shelter alongside the frame,
streaming, draw-call and resource diagnostics, and now primitives, video memory split into
textures and buffers, the managed heap, allocation rate and garbage collections per generation.
It refreshes four times a second and does not process while hidden. `EnvironmentVisualStateEvent` exposes resolved time,
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
