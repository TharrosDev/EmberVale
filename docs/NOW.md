# NOW — where the project is

**This is the single source of project state. Rewrite it; do not append to it.**

## Where we are

- **Stage C.** Economy (38), mounts/traversal (39), map intelligence (39.5), quest authoring (41)
  and divine shrines (41.5A–C) are closed. **Phases 40 and 40.5 are struck, not deferred:** this game
  has no survival needs, durability, hunger, encumbrance, puzzle, trap, or vault system. A cut system
  leaves no stub.
- **The world-geography overhaul (2026-08-29) ✅ CLOSED — out of band, maintainer-directed, and not a
  roadmap phase.** The 2026-08-28 layout rebuild fixed every POI's internal design and left the realm
  still reading as *cell → POI → cell → POI*: fifteen locations packed edge-to-edge inside 210 × 250
  metres, on flat 0.5 m slabs, under a 4 cm decorative wobble that faded to exactly zero at every
  boundary. **The ground is now one continuous heightfield per region with real elevation, real
  collision and real navigation, and there is no seam fade anywhere.** Every POI's interior
  circulation is the layout rebuild's, lifted verbatim. ⚠️ **This is still NOT Phase 44** — that phase
  blocks out all five realms and is ahead of us.
- **NEXT: 42A — membership/rank flag framework + a small rank UI**, reusing the existing story flags
  and `FactionResource`. It is the gate the five guild questlines (42B–F) all sit behind.

### What the overhaul actually changed

| | Was | Is |
| --- | --- | --- |
| Ember Crown | 10 cells, 210 × 250 m | **16 cells, 330 × 440 m** (6 of them empty transitional country) |
| Frostfang Reach | 5 cells, 250 × 200 m, **overlapping the Ember Crown** | **10 cells, 340 × 380 m, x 260..600** — disjoint |
| Ground | one flat `BoxMesh` + `BoxShape3D` per cell | one region `WorldHeightfield`; terrain mesh **is** the collider and the navmesh source |
| Relief | 0.055 m (five centimetres) | 1.4 m of countryside noise + authored landforms to ±30 m |
| Seams | height forced to 0 in the outer 24% of every cell | both cells evaluate the same world function — matched by construction |
| Roads/yards | 40 flat 6 cm slabs | painted by the terrain shader from the path/activity masks |
| Ground cover | 1,277 individual `Node3D`s | MultiMesh scatter, `Count` as a density per 100 × 100 m |
| Town → market | 56 m | 95 m | 
| Town square → arena | 150 m | **285 m**, through the gate, the wilds and an empty frontier |

- **The world-quality pass (2026-08-30) CLOSED - out of band, maintainer-directed, and not a
  roadmap phase.** The geography overhaul gave the realm real shape; this one made it look, feel and
  validate like it. **Terrain materials, region atmosphere, water, off-route safety and the region
  authoring framework are the five things it added**, and the last of those is the point: a future
  region now starts from `tools/region_spec_template.py`, and one command,
  `python tools/world_quality_check.py <region>`, says whether it is healthy.

### What the quality pass actually changed

| | Was | Is |
| --- | --- | --- |
| Terrain surface | 3 flat colours lerped by one octave of noise | **six semantic layers** - ground, sparse, rock by slope, cap by height, road, shore - from `data/terrain_layers/` grouped by `data/biomes/`: **20 substances, 10 biomes, still zero texture files** |
| Landform shape | exact ellipses and swept capsules | boundaries warped by `Irregularity` (0.26 on natural forms, 0 on anything levelling) |
| Distant landscape | 26 grey cylinder-cones on a circle | a **picture frame of real terrain** that samples the region field, so the horizon is the same surface continued |
| HLOD proxies | five-sided cones and unit cubes, visibly black crates at 92 m | the **same mesh** at 1/N density |
| Scatter | uniform over anything, including 60-degree faces | `MaxSlope`, `HeightRange`, `Clumping`, `Saturation`; instances lean with the ground |
| Water | 6 translucent `BoxMesh` planes, invisible to every system | **declared `WorldWaterResource` data**: shoreline taken from the terrain, and covered by `WorldWater`'s non-swimming recovery contract |
| Region light | one warm sun and one ash haze for every realm | `SunTint`/`SunEnergyScale`/`HazeColor`/`HazeScale` per region. **Frostfang reads alpine because its LIGHT is cold**, which no palette change could do |
| Off-route QA | nothing looked anywhere but the roads | `WorldTraversalAnalysis` sweeps the lattice as a **directed** graph and fails a build on ground you can walk into and not out of |
| The gates | eleven commands in two languages | **one**: `python tools/world_quality_check.py` |
| A new region | copy Ember Crown's spec and edit coordinates | `tools/region_spec_template.py`, which self-checks and is gated |

Read [`docs/WORLD_AUTHORING.md`](WORLD_AUTHORING.md) before touching a cell. `data/regions/*.tres`
is **generated** — edit `tools/region_spec_<region>.py` and run `python tools/gen_regions.py`.

## Last verified (2026-08-30 - the world-quality pass)

`python tools/world_quality_check.py` - **all 16 gates PASS**. Individually:

| Check | Result |
| --- | --- |
| Build | `dotnet build Embervale.sln` - 0 warnings, 0 errors |
| Tests | `dotnet test tests/Embervale.Tests` - **1525 passing** (+15: traversal analysis, water, landform irregularity, the scatter terrain gate) |
| `--validate` | exit 0 - now also walks the whole lattice for off-route traps |
| `--state` | 2 regions, 26 cells, 70 map locations; both portals OPEN |
| `--play` | boots, restores `auto1`, streams all 16 Ember Crown cells, 0 errors |
| Generation | `gen_regions.py --check` - clean |
| Negative battery | `negative_tests.py` - every rule still fails when broken |
| Starter template | `region_spec_template.py` - builds, lattice sound |
| Seams / layout | PASS on both regions |
| Traversal | `world_traversal_probe.gd` - PASS, 142 route segments. ⚠️ it caught the two dead-end prop clusters this pass added sitting **in** the road corridor; they were moved |
| Off-route traps | 2 real traps found in Frostfang (the Aerie's north precipice floor, a Glacier Pass crevasse). Both are deep, authored hazards, so `WorldRecovery` owns them and only *shallow* traps fail the build |
| Step-up / map / mesh census | PASS |
| World render | `world_shots.gd` - 260 frames, **lit by each region's own atmosphere for the first time**, inspected at eye level before the baseline was regenerated |

**Measured cost, same machine (Intel Iris Xe), `world_perf_probe.gd`, median frame time:**

| | Ember Crown before -> after | Frostfang before -> after |
| --- | --- | --- |
| Draw calls (mean/cell) | 622 -> **634** | 161 -> **158** |
| Primitives (mean/cell) | 1.17 M -> **1.05 M** | 214 k -> **219 k** |
| Frame time (mean/cell) | 17.6 ms -> **14.0 ms** | 13.4 ms -> **11.9 ms** |
| Frame time (worst cell) | 28.6 ms -> **20.0 ms** | 37.0 ms -> **16.7 ms** |
| Video memory | 483 MB -> **379 MB** | 413 MB -> **305 MB** |
| Region build (streamed+settled) | 2.2 s -> **3.4 s** | 0.9 s -> **1.9 s** |

⚠️ **The one regression is region BUILD time, and it is on a loading screen.** It was 5.2 s
before three fixes: the irregularity warp now early-outs outside a landform's transition band, the
backdrop samples the real field only within 45 m of the lattice, and the scatter spacing test is
bucketed rather than O(n-squared). Everything the player sees per frame got cheaper.

## Live invariants

1. **Gameplay state persists, and Load REPLACES live collections.** Save ids are stable primary
   keys; clear before restore, including every false/empty branch. A partial restore is a failed load.
2. **One surface owns each fact.** A shrine body is a caller; the player's claimed-id set is the only
   blessing authority. Never add per-shrine flags or a second blessing ledger.
3. **A gate belongs at the choke point, not in the caller.** Put the next condition where the
   mutation already funnels — `SafeLanding` is the newest example: every teleport in the game goes
   through two functions, so the "never put the player under the ground" rule lives in those two.
4. **All player-facing text uses `Loc.T()` and a `strings.csv` key.**
5. **If the player can go there, map it in the same sub-phase.** A map location's position is the
   transform of its `MapLocationComponent` parent in a cell scene, never a resource coordinate.
6. **Render world changes at eye level, front and back, with people and furniture around them.**
   Reading a transform is not a placement review. ⚠️ **And the render harness must stand on the real
   ground** — `world_shots.gd` raycasts every camera onto terrain now, because a literal 1.75 m eye
   height photographs the inside of a hill and the baseline passes.
7. **Before authoring content of an existing kind, read an existing `.tres` header.**
8. **An authored numeric range fails silently at both ends.** Every new range needs a validator arm
   and a negative case in *each* direction.
9. **An event that fires conditionally is not automatically the event that means the thing.**
10. **A cache key is a subscription.** Any newly drawn fact must be part of every cache/signature
    that renders it, not merely an event listener.
11. ⚠️ **A SEAM IS NO LONGER ARITHMETIC — IT IS GENERATED, AND THAT REPLACED THIS RULE.** Cells used
    to meet at coordinates two files had to agree about by hand, and three defects shipped that way.
    `tools/gen_regions.py` now refuses to write a region whose row bands do not tile its extent
    exactly, and every seam route is authored ONCE as a world point both cells derive their local
    endpoint from — half a seam cannot be authored. The ground matches because both cells evaluate
    the same `WorldHeightfield`, not because anything is flattened.
12. **A layout constraint written as a coordinate outlives its reason.** Author dependencies as
    offsets and ids, never as absolute points. ⚠️ The overhaul moved thirteen cells and every single
    thing that broke was an absolute world number someone had written down: nine schedule
    destinations, a property's placement centre, a portal point, a step-up probe, a screenshot
    harness's cell table and a probe's eye height. The town hub's centre was deliberately left at
    (0, 0, -10) purely to avoid a fourteenth.
13. ⚠️ **TERRAIN MAKES GEOGRAPHY; PROPS ONLY DETAIL IT.** A corrie built from scaled rock clusters, a
    crater from a ring of boulders, a glacier pass from six alternating ice models — all three
    shipped, all three are landforms now. If a shape needs to exist, it goes in
    `WorldLandformResource` and the props dress what the terrain already says.
14. ⚠️ **SOME OF THE WORLD MUST CONTAIN NOTHING.** Eleven of the realm's twenty-six cells carry a
    road, weather, vegetation and landform and no gameplay beat at all; three are dead ends. That is
    the feature. A world where every thirty metres has a purpose advertises on every step that it was
    designed. Do not fill them in.

15. ⚠️ **DEEP WATER IS NOT A TRAP BECAUSE IT IS DECLARED, NOT BECAUSE IT IS SHALLOW.** There is
    no swimming. Under 1.1 m the player walks; above it the LAND refuses them with a bank past the
    45-degree floor limit, never an invisible wall; above 1.9 m `WorldRecovery` puts them back on the
    last dry ground. A water surface authored as a mesh in a `.tscn` is invisible to all three and is
    forbidden - declare a `WorldWaterResource` on the cell.
16. ⚠️ **A SHALLOW HOLE IS A BUG; A DEEP ONE IS A HAZARD.** `--validate` sweeps the whole
    lattice as a directed graph - walking down and walking up are different edges - and fails on
    ground the player can WALK into and not climb out of. It deliberately does not fail on the ones
    they FELL into: those are authored drama and the recovery service owns them.
17. ⚠️ **A SPECIES DECLARES THE GROUND IT STANDS ON.** `MaxSlope` defaults to 0.7 for a reason:
    once the world had 60-degree faces in it, a uniform scatter grew trees and boulders sideways out
    of every cliff in two regions. `Clumping` is the companion rule - even spacing is the most
    recognisable pattern there is, and the eye finds it long before it finds a repeated model.
18. ⚠️ **AN HLOD TIER IS A SILHOUETTE CONTRACT.** The proxy is the same mesh at a fraction of
    the density. Cones and boxes keep the mass and throw away the outline, which is the half that
    matters at the range they engage - from the town square they read as black crates on a hillside.

## Commands worth knowing

```text
dotnet build Embervale.sln
dotnet test tests/Embervale.Tests
python tools/gen_regions.py            # data/regions/*.tres is GENERATED; --check gates it
godot --headless --path . -- --validate
godot --headless --path . -- --state
godot --path . -- --play
python tools/negative_tests.py          # refuses to run while data/ or scenes/ is dirty — commit first
python tools/check_region_seams.py data/regions/EmberCrown.tres
python tools/check_cell_layout.py data/regions/EmberCrown.tres
godot --headless --path . --script res://tools/world_traversal_probe.gd
godot --headless --path . --script res://tools/map_probe.gd
godot --headless --path . --script res://tools/stepup_probe.gd
godot --headless --path . --script res://tools/cell_mesh_census.gd
godot --path . --script res://tools/world_shots.gd      # add -- --update-world-baseline AFTER inspecting
godot --path . --script res://tools/world_perf_probe.gd # draws, primitives, frame time, video memory
python tools/world_quality_check.py                     # ALL of the above, in order, one verdict
python tools/region_spec_template.py                    # the new-region starter, self-checking
```

`godot`/`python` are not on this shell PATH. The 4.7.1 console executable at
`C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe`
and Codex's bundled Python work when launched with the elevated project environment. The configured
Godot MCP relay (`localhost:23630`) is still down; the whole verification spine above runs without it.
