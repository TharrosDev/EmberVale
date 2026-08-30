# World authoring

This is the canonical exterior-region workflow. Read `CLAUDE.md`, `NOW.md`, `ARCHITECTURE.md`,
`ASSET_POLICY.md`, `SAVE_FORMAT.md`, and the new-region recipe in `RECIPES.md` before using it.

## World pillars

- Preserve the `RegionResource -> RegionCellResource[] -> authored .tscn -> RegionStreamer` contract.
- Design macro silhouette, meso routes/landmarks, and micro evidence of habitation or ecology.
- Treat cell coordinates, map anchors, persistent ids, shops, services, quest targets, schedules,
  safe zones, portals, encounter spaces, and property anchors as protected gameplay data.
- Keep hero composition authored. Automation may dress repetition, never decide why a place exists.
- Use the four Quaternius MegaKits first and adapt through `assets/models/`; do not mix styles casually.

## Location authority: one place, every surface

The placed `MapLocationComponent` is the world-position authority for a named place. Its
`MapLocationResource` supplies identity, category, semantic tier, discovery rules, and links to
existing shop/service/dialogue/property/travel records; it never supplies another coordinate.
`MapService` derives discovery, search, full-map, minimap, compass fallback, and saved cross-region
positions from that pair. Keep this chain intact:

`cell scene anchor -> MapLocationComponent -> MapLocationResource id -> MapService -> UI/quest/travel`

For a new dungeon entrance or other reachable POI:

1. Build and frame the physical entrance in the cell scene. Keep its interaction front and approach
   clear with an authored scatter exclusion; do not rely on deleting generated instances.
2. Add one row to `tools/gen_map_locations.py`, anchored to the entrance/building node itself. Choose
   category for what it is and tier for how far out it matters. Hidden exploration POIs leave
   `RevealWithCell` false; prominent skyline landmarks may reveal with the cell.
3. Run the generator, then `--check`. Do not add X/Z to a resource, quest, map widget, or travel list.
4. A Reach/Defend objective uses the canonical `location.*` id as `TargetId`. A live Kill/Collect/
   Talk/Interact objective may set `LocationId` as the geographic fallback; the live target wins while
   loaded. Completed or inactive branch objectives are never navigation targets.
5. If the POI owns fast travel, put `TravelNodeComponent` at the real landing point and link its
   `travel.*` id from the map location. Discovery and attunement remain separate: discovering the
   place may reveal the unavailable waystone, while interacting with it enables travel.
6. Run `--validate` and `map_probe.gd`. Validate the unknown state, discovered state, search result,
   selected/tracked marker, minimap, compass, unattuned waystone, and attuned travel action in the
   renderer. A static location must remain resolvable when its region is not resident through the
   saved `MapService` position; never keep the scene loaded just for navigation.

## The ground is one surface per region (the 2026-08-29 geography overhaul)

⚠️ **READ THIS BEFORE ANYTHING ELSE ON THIS PAGE.** Until 2026-08-29 every cell owned a flat
0.5 m `BoxMesh` floor, a matching `BoxShape3D`, and a decorative 4 cm surface skin that faded to
exactly zero at the cell boundary. Roads and yards were 6 cm slabs laid on top. That contract was
seam-safe and it was also the reason the realm read as fifteen rectangles touching: the terrain drew
the lattice on the ground in relief, and there was no way to put a hill anywhere.

What replaced it:

- **`WorldHeightfield` is the region's one ground function**, pooled from every cell's authored
  landforms, routes and yards in WORLD space and built once by `RegionStreamer.Configure`. Two cells
  that abut sample the identical function at the shared edge, so **seams match by construction and
  there is no edge fade anywhere.** A ridge authored on one cell runs into its neighbour on purpose.
- **`WorldCellPresentation` builds the terrain mesh AND its collider**, and parents the collider
  into the cell's `Nav`, so the navmesh bakes off real elevation with no extra wiring.
- **`WorldTerrainConform` drops every authored node onto that ground at load**, so a node's authored
  Y is its clearance ABOVE the ground. Opt out with the `terrain_absolute` group (water surfaces).
- **`WorldLandformResource` is the shape.** Mounds and ridges, added or levelling-to, in cell-local
  metres, and deliberately allowed to overhang the cell envelope. ⚠️ **Terrain makes geography;
  props only detail it.** A corrie built from scaled rock clusters, a crater built from a ring of
  boulders and a glacier pass built from alternating ice props are the three defects this resource
  exists to retire.
- **Grade discipline.** `CharacterBody3D`'s floor limit is 45 degrees and `StepUp.MaxHeight` is
  0.5 m, so a slope under about 0.7 rise-over-run is walkable, one over about 1.0 is an honest
  collider-free wall, and any raised ground over half a metre needs a ramp or it is a wall by
  accident. `--validate` refuses an authored route over a 0.80 grade;
  `tools/world_traversal_probe.gd` walks a real capsule down all 142 of them.
- **Two winding traps, both of which shipped for an afternoon.** The collision soup is wound the
  OPPOSITE way to the render mesh — physics does not care, but the navmesh baker reads the wrong
  winding as a ceiling and bakes nothing but rooftops. And the render mesh was itself back-facing for
  as long as there was an opaque floor underneath to hide it. If a cell bakes zero navigation
  polygons, or the world renders as props over open sky, look at `WorldTerrainMeshBuilder`'s two
  index loops before anything else.

## Create a region

1. ⚠️ **Author it in `tools/region_spec_<region>.py` and run `python tools/gen_regions.py`; the
   `.tres` is generated.** The lattice is declared as row bands split into columns and the generator
   checks it tiles the region's extent exactly before it writes a byte, and every seam route is
   authored once as a world point both cells derive their endpoint from. `--check` fails when the
   committed `.tres` is stale. Never rename a shipped id without a save migration.
2. Add one `WorldEnvironmentProfileResource` sub-resource. It owns the surface palette, relief,
   road colour, and distant-landscape palette/scale. Ember Crown and Frostfang Reach are examples.
3. Add one `WorldPerformanceBudgetResource`. Set authored-node limits from the `.tscn` census and
   runtime-node/draw/memory/frame limits from a settled representative play capture. Never use the
   visual screenshot harness for frame timing: synchronous PNG writes intentionally block frames.
4. The lattice is row bands in the spec and the generator checks it; do not work it out on paper.
5. Add a `MapLocationResource` and placed `MapLocationComponent` for every reachable named place,
   shop, service, dungeon, landmark, and quest destination. Use `tools/gen_map_locations.py`.
6. Configure weather, encounters, world events, economy cell ids, and realm travel in existing data.

## Create a cell

1. Author `scenes/regions/<region>/<cell>.tscn` at local origin. The streamer applies `Center`.
2. ⚠️ **Author NO floor and NO ground skins.** The terrain is the ground and the shader paints
   routes and yards from the path/activity masks the mesh builder writes into vertex colour. Forty
   6 cm road/yard slabs were deleted on 2026-08-29 — they were the literal flat rectangles.
3. Add a `WorldCellPresentationResource` with exact `Width`/`Depth`, stable `Seed`,
   `TopologyResolution` and optional tint. Every cell is required to have one. Author the cell's
   **geography first** with `WorldLandformResource` (hills, ridges, cliffs, cuts, terraces, basins,
   passes — these may overhang the envelope and should, at a seam), then its circulation with
   `WorldPathSegmentResource`, then the pads its buildings stand on with `WorldGroundAreaResource`
   (`Elevation` is an absolute world Y). There is no `RoadAxis` any more.
4. Shared-edge terrain is seam-neutral **because both sides evaluate the same world-space function**,
   not because it is flattened. Author landforms freely across a boundary; that is what hides the
   lattice. A road is a cut: at full path mask the centreline is exactly the graded line between its
   own endpoints, so its gradient is arithmetic you can predict, and a route's influence tapers past
   its own ends so a continuing route takes the corner cleanly.
5. Wrap walkable collision under `NavigationRegion3D/Nav`, parse static colliders, and add
   `CellNavBaker`. Inherited cells are safe: the baker duplicates the nav resource before baking.
6. Put important paths down first, then landmarks, then architecture/ecology, then micro dressing.
   Maintain at least an 8 m continuous mount route on primary roads and clear interaction fronts.

## Surface, roads, and distant landscape

`WorldCellPresentation` builds the cell's terrain mesh and its `ConcavePolygonShape3D` collider from
the region heightfield. Topology is generated on the CPU at the cell's budgeted resolution (aim for a
vertex every 1.5–2 m where the player walks shaped ground and every 3–5 m across open transitional
country); collision is built at its own coarser 2.5 m grid, because a walking capsule cannot feel a
triangle the navmesh's 0.3–0.5 m voxel grid would not resolve either. The cardinal `RoadAxis` strip
is gone: routes are `WorldPathSegmentResource`s and nothing else. The shader blends
surface/secondary/detail/road response by world-space noise, real height, real slope, the path mask
in vertex colour R and the activity mask in G — so material, levelling and scatter clearance all read
the same authoring. ⚠️ The per-cell `Tint` is faded out at the cell edge by the shader; applied flat
it is a rectangle of differently-coloured ground with a hard border.

`WorldRegionBackdrop` builds one non-colliding `MultiMeshInstance3D` outside the entire playable
lattice. It provides macro mountains in one draw call and must stay beyond every playable edge.
Never recreate per-cell mountain fences; the visual-QA loop proved they occlude neighboring cells.

## Ecology and dressing

- Add a `WorldBiomeScatterResource` to **every** cell. ⚠️ `Count` is a DENSITY — instances per
  100 x 100 m, scaled by the cell's own footprint — because cells range from 50 x 90 to 200 x 110 and
  a flat per-cell count draws the lattice back onto the ground in vegetation after the terrain has
  stopped drawing it. Scatter is conformed to the terrain height. Each `BiomeScatterLayerResource`
  names one imported scene, density, scale/spacing range, tint, visibility distance, and shadow
  policy. Runtime output is one MultiMesh source per accepted layer.
- Scatter is deterministic and cell-local. The planner clears the authored cardinal road, every
  `WorldPathSegmentResource`, and every `WorldGroundAreaResource` automatically;
  add `BiomeScatterExclusionResource` circles around lairs, landmarks, doors, arenas, schedule paths,
  and deliberate clearings. `--validate` enforces source paths and per-cell/region instance budgets.
- Use `tools/dress_cell.py` for deterministic clustered ground cover and `gen_cell_props.py` for
  literal authored, colliding prop stanzas. Read generated output before pasting it into a scene.
- Roads, settlements, encounter centres, doors, stalls, NPC schedule routes, and hero landmarks are
  exclusion zones. Shade species cluster under canopy; shoreline and industrial styles use their
  own profiles. Do not scatter uniformly.
- Repeated non-interactive background forms should use MultiMesh. Interactive or colliding props
  stay authored nodes so nav and interaction remain inspectable.

## LOD, HLOD, and visibility

- Each scatter layer has a detailed visibility end and fade margin. Optional HLOD shape/reduction
  settings build a second, much smaller primitive MultiMesh from the same deterministic placement
  set; detailed and proxy ranges overlap and cross-fade.
- `WorldVisibilityManager` checks the active camera four times per second by default and hides only
  cosmetic biome batches beyond `BiomeCullDistance`. Gameplay roots, navigation, schedules,
  interactables, and persistence stay resident and active.
- HLOD is a silhouette contract, not a second composition. Pick cone/box scale and colour to preserve
  the distant mass of the detailed species; never use it for a hero landmark or readable building.

## Region identities in the current world

- Ember Crown: warm worked timber/stone and ember light against ash, exposed rock, broken ruins,
  frontier vegetation, and dangerous roads. **Sixteen cells across 330 x 440 m**, town-centred; six
  of them (west_downs, mine_road, north_moor, ashen_reach, frontier_waste, fen_edge) are
  transitional country carrying a road, weather, vegetation and landform and **no gameplay beat at
  all**. Two are dead ends. ⚠️ Do not fill them in: a realm where every thirty metres has a purpose
  advertises on every step that it was designed.
- Frostfang Reach: cold blue-grey surface variation, broad mountain silhouettes, dead pine and ice
  clusters. **Ten cells across 340 x 380 m in its own coordinate band (x 260..600)** — it used to
  overlap the Ember Crown's arena and northern wilds, which is why `RegionStreamer` carried "both
  regions cannot be resident at once" as a limitation. Clan Hold is the inhabited hearth in mountain
  shelter; the glacier is a real pass between two 26 m ice walls; the Ancient's aerie is a massif
  above the tree line. Five cells are marches, snowfield and high traverse with nothing in them.

## Gameplay integration checklist

- Do not move `SpawnPoint`, `SafeZoneCenter`, portals, travel nodes, map pins, quest targets, NPCs,
  schedules, shops, services, crafting stations, encounter markers, lairs, or property anchors
  without updating every dependent system and testing save compatibility.
- Stable `PersistentId`, region, cell, map, shop, service, quest, faction, and flag ids do not change.
- Test first-person eye height, third-person roof/canopy clearance, and an 8 m mount corridor.
- Navigation must never route over scenery the player cannot traverse. Every solid visual needs an
  honest collider; ground cover and distant backdrops intentionally do not.

## Visual QA

Run the actual renderer after every environment batch:

```text
Godot_..._console.exe --path . --script res://tools/world_shots.gd
```

The harness initializes the centralized content databases, streams the real region pipeline, and
captures, for every cell, entry, centre, landmark, exit, and overview views in day and dusk lighting
under `tools/shots/world/`. ⚠️ **The cell table is read out of the region resource and every camera
is raycast onto the real terrain** — it used to be a hand-written list of centres and envelopes and a
literal 1.75 m eye height, which after the overhaul would have framed every shot at the old lattice
from inside a hillside while the baseline "passed". Inspect for floating or
sunken props, scale/rotation errors, gaps, z-fighting, repetition, blocked doors/routes, cell borders,
lighting discontinuity, and first/third-person occlusion. It disables performance sampling because
PNG capture is frame-blocking; profile timing in ordinary gameplay instead.

Every capture also generates a 12×8 RGB perceptual signature and compares all 150 frames against
`tests/visual_baselines/world_signatures.json`. A missing frame, new frame, changed signature shape,
or mean channel delta above the approved threshold fails the process. To approve an intentional
visual change only after inspecting the PNGs:

```text
Godot_..._console.exe --path . --script res://tools/world_shots.gd -- --update-world-baseline
Godot_..._console.exe --path . --script res://tools/world_shots.gd
```

## Performance and shipping gates

- A region is resident as a whole. Count every cell node and material as simultaneously live.
- `WorldPerformanceBudgetResource` separates authored `.tscn` nodes from expanded runtime nodes.
  `--validate` gates authored nodes and requested scatter counts. `WorldPerformanceMonitor` samples
  runtime nodes, scatter instances, draw calls, static memory, and frame time once per second and
  warns only after a sustained violation. F4 shows the current region-budget status.
- Terrain vertices, detailed+HLOD scatter instances, concurrent threaded requests, cell
  instantiations per frame, visibility distance, and visibility-update cadence are authored limits.
- `RegionStreamer` requests PackedScenes on worker threads, polls without blocking, then instantiates
  only the authored number of ready cells per frame. Static-memory pressure reduces request
  concurrency to one; it does not deadlock the loading screen by refusing the final cell.
- Prefer one shared shader, one region backdrop MultiMesh, baked static nav, and authored colliders.
- Profile representative settlement, wilds, weather, day, dusk, combat, and mount routes on target
  hardware. Record FPS/frame time, draw calls, node count, and the most expensive cell.
- Before shipping:

```text
dotnet build --warnaserror
dotnet test
Godot_..._console.exe --headless --path . -- --validate
Godot_..._console.exe --headless --path . -- --play
Godot_..._console.exe --path . --script res://tools/world_shots.gd
```

The automated gates establish structural correctness. A human traversal still owns interaction,
camera, mount, quest, and subjective composition sign-off.
