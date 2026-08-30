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

Read [`docs/WORLD_AUTHORING.md`](WORLD_AUTHORING.md) before touching a cell. `data/regions/*.tres`
is **generated** — edit `tools/region_spec_<region>.py` and run `python tools/gen_regions.py`.

## Last verified (2026-08-29 — the world-geography overhaul)

| Check | Result |
| --- | --- |
| Build | `dotnet build Embervale.sln` — 0 warnings, 0 errors |
| Tests | `dotnet test tests/Embervale.Tests` — **1510 passing** |
| `--validate` | exit 0; 70 map locations, 26 cells, 1460 locale strings |
| `--state` | 2 regions, **26 cells**, 63 items, 23 shops, 15 services, 70 map locations; both portals OPEN |
| Region seams | `check_region_seams.py` — PASS on both regions, every crossing 0.00 m |
| Cell layout | `check_cell_layout.py` — 0 overlapping structures in all 26 |
| Route grades | new `--validate` arm walks all authored routes against the heightfield; **22 unwalkable routes found and fixed**, now 0 |
| Traversal | `world_traversal_probe.gd` — **PASS, 142 authored route segments**, real navmesh + real terrain collision, capsule steps and drops like the player |
| Step-up | `stepup_probe.gd` — PASS: climbs the 0.45 m Salt Steps terrace, does not climb the bell tower |
| Map placement | `map_probe.gd` — PASS, 70 markers across 26 cells |
| Map generator | `gen_map_locations.py --check` — 0 files out of date |
| Save migration | v1 → v2 exercised on a real `auto1`: migrated, 34 objects restored, player landed at the region spawn, 0 errors |
| `--play` | all 16 Ember Crown cells streamed; **75 s with zero warnings and zero errors** — no draw-call or frame-time budget breach |
| World render | `world_shots.gd` — 26 cells, **260 frames**, inspected at eye level before the baseline was regenerated |
| Mesh census | Ember Crown **2155 → 1292** rendered meshes (−40%) across 5.4× the area; realm 1580 |

⚠️ **The perf overrun NOW.md carried for two revisions is gone.** `--play` on the Ember Crown warned
`draw calls … > 1800` and `frame ms … > 25` before this work (peaking 3214 / 60 ms) and warns
nothing now, because the lever that entry named was finally pulled: 1,277 ground-cover `Node3D`s and
40 road slabs became MultiMesh layers and one terrain draw per cell.

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
```

`godot`/`python` are not on this shell PATH. The 4.7.1 console executable at
`C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe`
and Codex's bundled Python work when launched with the elevated project environment. The configured
Godot MCP relay (`localhost:23630`) is still down; the whole verification spine above runs without it.
