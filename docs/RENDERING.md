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

The world has no hand-written warm-up pass. Forward+ compiles a mesh's specialised pipelines in the
background when its surface is loaded and draws with the ubershader until they are ready, and
every route into the world holds the loading screen until the landing cells are resident
(`LoadingCoordinator`). `ApplyQuality` runs when the sky is built, before any cell streams, so
MSAA, TAA and the scaling mode are fixed before those pipelines are requested. What this does not
cover is a material first created mid-play: on a machine's first ever session its shader is
compiled once, then served from the on-disk shader cache. Spell effects are the one such family
with a warm-up of their own: on the first frames with a camera `SpellVfxDirector` draws one of
every block in front of it at scale 0.001 for three frames (`WarmupFrames`, `WarmupScale`), and
builds the ground pattern textures then too. Whether that removes the first-cast hitch on a cold
shader cache has not been separated out from the `--vfxperf` numbers.

## Spell effects

Everything a spell draws goes through the `SpellVfx` facade and is owned by the session's
`SpellVfxDirector` (`src/Magic/Vfx`; the code map is
[`ARCHITECTURE.md`](ARCHITECTURE.md#213-magic-srcmagic)). It is the stated exception to "no
ordinary-world magical glow" in `data/rendering/VisualContract.json` (`vfx.magic`,
`vfx.spell_effects`): a cast, its travel, its impact and what it leaves behind may glow, with hue
from `SpellSchools.Color` through `VfxPalette`. Nothing else in the world gains glow by it.

**Shaders.** Seven, in `assets/shaders/vfx`, each one shared `Shader` with a `ShaderMaterial` per
pooled node (every node animates its own colour and fade). There are no texture imports: every
image is built in code by `VfxTextures`.

| Shader | Draws |
| --- | --- |
| `vfx_sprite` | billboards: flares, halos, rays, every particle and the pieces of a `VfxMotif`. Premultiplied alpha, so one shader is additive light and covering smoke. It also thins a puff by its width on screen |
| `vfx_flow` | scrolling noise bodies: fire balls, wall sheets, shells, breath tongues. `tongues` above 0 turns a sheet into licks of flame standing on a hot ground line with faded ends (the Pyre Wall); at 0 a sheet is as it was |
| `vfx_ring` | shock rings: a thin torn front with a wake, on an annulus mesh |
| `vfx_ribbon` | lightning, beams, tethers and trails; width is clamped to an angle, so a ribbon beside the first-person camera is a line and not a wedge |
| `vfx_distort` | screen-texture refraction shells. `shimmer` at 1 is heat haze: the image wavers across the whole shape, climbing, with no outline; at 0 it is the pressure wave it was |
| `vfx_ground` | ground discs: a rim, a school pattern and wisps |
| `vfx_ice` | ice walls and frozen shells: plates, seams, a lit rim, a jagged crest |

All seven fade in view space between 0.3 m and 1.2 m from the camera. If any fails to load or to
compile, `VfxMaterials.Load` logs `Spell effects: shader X did not compile; spell effects are off`,
`SpellVfx.Active` is false for the session and every spell falls back to its plain shape. `--validate`
checks `vfx_sprite`, `vfx_flow`, `vfx_ring`, `vfx_ribbon`, `vfx_distort`, `vfx_ground`,
`player_body.gdshader` and `fx/telegraph.gdshader` for a parse failure; ⚠️ `vfx_ice` is not in
that list, so only the runtime check covers it.

**Shaped sprites.** A particle is no longer always a soft dot. `VfxTextures` builds a mask per
`VfxSprite` from the pure `VfxTextureRules`: `Flame` (a tongue standing tip up), `Snow` (a streak),
`Flake` (a snowflake), `Spark` (jagged), `Wisp` (a tendril), `Ash` (a tumbling flake), `Mote`
(small and four-pointed), and the smoke `Puff` now has a lobed, ragged outline. The school picks
the shape under every recipe with no recipe edit (`VfxSpawner.School`, set by `VfxCast`): motes
thrown by a Frost cast are snowflakes, sparks thrown by a Lightning cast are jagged and by a Frost
cast streaks of snow, wisps are tendrils, and `VfxEmitter.Flame` is tongues that still cool to
smoke. `VfxBurstSpec.Sprite` forces a shape where it suits the preset (`VfxBurstPresets.Fits`); an
unsuitable one is ignored. Four emitter presets came with them: `VfxEmitter.Snow`, `AshFlake`,
`FlameLick` and `Flurry`.

**Motifs.** `VfxMotif` is a handful of shaped sprites moving in a pattern (`VfxMotion`: `Lick`,
`Orbit`, `Crackle`, `Swirl`, `Inward`, `Lance`): the instances of one `MultiMesh` drawn with
`vfx_sprite`, so one draw call and no particle simulation. Where each piece stands is the pure
`VfxMotifRules.Pose`, and a motif never has more than `VfxMotifRules.MaxCount` pieces. Every
wind-up wears its school's motif at the hand and every generic bolt its school's shaped head
(`VfxMotifRules.Windup` / `Head`). On Performance and Low the wind-up motif takes the place of the
particle stream, one draw for one, and a bolt's head takes the place of the white core of its
glow.

**Generic looks that changed.** The Pyre Wall is a `tongues` sheet; Medium and up add a second,
lower pair of layers and `FlameLick` particles along its length, and Ultra adds a slab of heat
haze over it (`VfxDistortionSpec.Shimmer`; not under Reduced Motion, and no other tier has it). A
ward shell is fitted to the body, bright at first, then fades to a faint shimmer and stays
(`VfxShellSpec.SettleHold` / `SettleSeconds` / `SettleOpacity`). Frozen shells (the ice shell aura,
the Freeze proc, the frost impact shell) are fitted to the collision shape with `BodyFit`, a
capsule lying on its side included. A lightning mark wears arcs for as long as the status lasts,
above Performance. The numbers are in the header of `src/Magic/Vfx/SpellVfx.Kit.cs`.

**Render layer 12 is reserved for spell effects.** Every effect mesh is drawn on it
(`VfxMaterials.RenderLayer`) and every ground-mark decal's cull mask leaves it out
(`VfxMaterials.DecalMask`). A decal tints every surface in its box, unshaded additive quads
included, and a pale frost or rune mark drew square patches over the flares and shards above it
until the two were separated. Put nothing else on layer 12, and give any new decal that can sit
under an effect the same mask. No camera in the game sets a cull mask, and one that did would have
to include the layer.

**Tiers.** `Settings.SpellEffects` is -1 (follow the graphics preset) or 0 to 4 in visual order:
Performance, Low, Medium, High, Ultra. ⚠️ That is not `Settings.RenderQuality`'s saved order, so
the preset is mapped by `VfxBudgetRules.FromRenderQuality`, never cast. `SpellVfxDirector` writes
the result into `VfxQuality` at session start and on every `SettingsAppliedEvent`; this is beside
`SkyController.ApplyQuality`, not through it. The numbers are `VfxBudgetRules` and the contract
JSON repeats the first table.

| | Performance | Low | Medium | High | Ultra |
| --- | ---: | ---: | ---: | ---: | ---: |
| Particle multiplier | 0.25 | 0.45 | 0.7 | 1.0 | 1.5 |
| Spell lights at once | 2 | 3 | 5 | 8 | 12 (1 shadowed) |
| Longest light range | 5 m | 7 m | 10 m | 14 m | 18 m |
| Distortion | off | off | off | on | on |
| Ground marks at once | 0 | 0 | 6 | 12 | 24 |
| Secondary debris and smoke | off | off | on | on | on, doubled |
| Bolt segments / branches | 6 / 0 | 8 / 0 | 12 / 1 | 16 / 2 | 24 / 3 |
| Bolt strands | 1 | 1 | 2 | 2 | 3 |
| Projectile trail | core and halo | short | normal | long | long with sparks |
| Live effect budget | 12 | 20 | 32 | 48 | 64 |
| Full-detail distance | 25 m | 35 m | 50 m | 70 m | 90 m |
| Rays on a blast | no | yes | yes | yes | yes |
| Billow, mist, crystals, glints | no | no | yes | yes | yes |
| Smoke column | no | no | no | yes | yes |
| Debris layers | 0 | 0 | 1 | 1 | 2 |
| Soft depth fade on particles | no | no | yes | yes | yes |
| Standing zone floor | rim only | whole | whole | whole | whole |
| Largest soft glow quad (frame heights) | 0.45 | 0.55 | 0.7 | 0.85 | 1.0 |

Past the full-detail distance only the flare draws; past 1.5 times it nothing spawns
(`VfxBudgetRules.DetailAt`). The budget counts effect groups, not nodes. Over budget the oldest
effect that is not the player's is recycled first. Wind-up auras, telegraphs, zones, walls, totems,
bolts in flight and status auras are essential: they are never recycled and take no place in the
budget (`VfxLedger`). Glow is off on the Performance preset, so no effect may rely on bloom to
read: the layered cores and the halo have to look hot without it.

**The coverage governor** (`VfxCoverageRules`). The first renders were white-outs: a soft additive
quad costs its pixels however faint it is, and several stacked ones clip to white. Three rules
came out of that, and every large element obeys them.

- A soft layer's opacity falls with the share of the frame it covers. It is full up to 4.5% of
  the frame and down to 0.12 at 35% (20% on Performance), with a brief brighter pop in its first
  0.08 s. Its quad is shrunk to the tier's span in the last table row.
- A large white core is capped in size and gone in 0.15 s.
- Each particle puff is thinned by its own width on screen (`SpriteOpacity`, mirrored in
  `vfx_sprite` as `span_limit` and `span_floor`): thinning starts at 0.42 m of width per metre of
  distance and stops at 0.12.

The estimate assumes a 70 degree vertical field of view at 16:9. A blast is built from structure
(rays, a thin ring, an eroding body, particles, debris, smoke), never from a bigger disc.

**Comfort.** The spell layer's own screen flash is one `CanvasLayer` (`VfxScreen`) under
`VfxScreenRules`: at most 0.14 alpha times the player's screen-flash setting, at most 0.1 for an
enemy's spell and only when it struck the player, 0.05 under Reduced Motion, never two within
0.3 s, pulled nearly to warm white, and only for a blast centred on the player. A body-sized
sphere shell is not drawn while the camera is inside it (`VfxScreenRules.Engulfs`), because from
inside it is an uncapped full-screen flash. Reduced Motion also removes distortion and holds bolts
still. A landed spell hit no longer raises `CombatFeedbackOverlay`'s full-screen hit tint; melee
hits keep it. The same layer draws the **edge shimmer** (`VfxSpawner.ScreenEdge`): school colour at
the edges of the screen with the middle left clear, capped by `VfxScreenRules.EdgePeak` (0.3 times
the flash setting, 0.12 under Reduced Motion).

**First person.** The player's own wind-up aura, release flash and the start of a bolt or beam are
anchored to a point fixed in the view at the lower left, where the casting (left) hand is
(`VfxViewRules.HandOffset`), past the near fade and clear of the crosshair, because the hand bone
itself sits inside the fade band. The projectile's picture starts there and settles onto the true
path over 0.25 s; the collision area, the aim and the muzzle are untouched. A held glow there is
hand-sized and school-coloured (its energy and halo are cut near the eye), and the player's own
bolt head is drawn at half size (`ProjectileViewScale`).

A flat ring or disc on the floor about the camera, which is what the player's own self-cast is in
first person, was a bright band across the bottom of the view and through the hotbar. It is cut
to 16% while its radius is under about 3 m and drawn in full by 4.5 m
(`VfxScreenRules.SelfRing`); the generic release puts an edge shimmer in its place for every Self
delivery the player casts in first person (`SelfCastInView`). ⚠️ The cut applies to the player's
own effects and to anybody's one-shot rings. A telegraph is never cut, and neither is an enemy's
standing zone: that is the thing to get out of. A ward hit on the local player in first person is
an edge shimmer too.

**Telegraphs are not part of this layer.** An attacker's wind-up warning is `TelegraphRing`
(`src/Combat`) drawn by `assets/shaders/fx/telegraph.gdshader`: mix blend, not additive (an
additive warning vanishes on bright sand at noon), a see-through body at about a third opacity, a
brighter rim a fixed width on the ground that is a little over white so it blooms where glow is
on, a lit part that sweeps to the outer edge as the blow arrives, and edges that fade out. The
shape is the mesh, never the shader, so the footprint is unchanged. High Contrast sets `plain` (a
flat solid body, a hard rim) and Reduced Motion sets `motion` to 0 (no shimmer, no unblockable
pulse). If the shader does not load the ring falls back to a plain hard-edged material and logs
`Telegraphs: telegraph.gdshader did not compile; warnings are drawn plain.`

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
