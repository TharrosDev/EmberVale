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

## Create a region

1. Author `data/regions/Xxx.tres` with a stable `region.*` id, bounds, spawn/safe-zone data,
   atmosphere, neighbours, and cells. Never rename a shipped id without a save migration.
2. Add one `WorldEnvironmentProfileResource` sub-resource. It owns the surface palette, relief,
   road colour, and distant-landscape palette/scale. Ember Crown and Frostfang Reach are examples.
3. Add one `WorldPerformanceBudgetResource`. Set authored-node limits from the `.tscn` census and
   runtime-node/draw/memory/frame limits from a settled representative play capture. Never use the
   visual screenshot harness for frame timing: synchronous PNG writes intentionally block frames.
4. Work out the complete cell lattice on paper. Record every exact shared edge beside the data.
5. Add a `MapLocationResource` and placed `MapLocationComponent` for every reachable named place,
   shop, service, dungeon, landmark, and quest destination. Use `tools/gen_map_locations.py`.
6. Configure weather, encounters, world events, economy cell ids, and realm travel in existing data.

## Create a cell

1. Author `scenes/regions/<region>/<cell>.tscn` at local origin. The streamer applies `Center`.
2. Keep the proven gameplay slab and collider exact. Visible primitive ground may remain only when
   covered by the presentation surface; invisible primitives remain valid collision/nav support.
3. Add a `WorldCellPresentationResource` to the cell resource with exact `Width`/`Depth`, stable
   `Seed`, road axis/width/offset, and optional tint. Every cell is required to have one.
4. Shared-edge terrain is seam-neutral: `WorldTerrainMeshBuilder` builds indexed vertices and
   normals from continuous `WorldTerrainMath`, flattening topology exactly at every boundary and
   across the authored road. Never raise a boundary independently. More than cosmetic relief requires a jointly
   authored collision/nav stitch shared by both cells.
5. Wrap walkable collision under `NavigationRegion3D/Nav`, parse static colliders, and add
   `CellNavBaker`. Inherited cells are safe: the baker duplicates the nav resource before baking.
6. Put important paths down first, then landmarks, then architecture/ecology, then micro dressing.
   Maintain at least an 8 m continuous mount route on primary roads and clear interaction fronts.

## Surface, roads, and distant landscape

`WorldCellPresentation` adds a centimetre-high indexed terrain mesh over the authored collider.
Topology is generated on the CPU at the cell's budgeted resolution, with deterministic world-space
height sampling, road flattening, explicit normals, and exact flat boundary vertices. The shader no
longer deforms vertices: it blends surface/secondary/detail/road material response by world-space
noise, height, slope, road mask, tint, and authored roughness. It has no save state or navigation
authority. The underlying scene remains the gameplay truth until a future region deliberately opts
into jointly authored terrain collision/nav stitches.

`WorldRegionBackdrop` builds one non-colliding `MultiMeshInstance3D` outside the entire playable
lattice. It provides macro mountains in one draw call and must stay beyond every playable edge.
Never recreate per-cell mountain fences; the visual-QA loop proved they occlude neighboring cells.

## Ecology and dressing

- Add an optional `WorldBiomeScatterResource` to a cell for repeated, non-interactive ecology.
  Each `BiomeScatterLayerResource` names one imported scene, count, scale/spacing range, tint,
  visibility distance, and shadow policy. Runtime output is one MultiMesh source per accepted layer.
- Scatter is deterministic and cell-local. The planner clears the authored road automatically;
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
  frontier vegetation, and dangerous roads. Its ten cells preserve the town-centred lattice.
- Frostfang Reach: cold blue-grey surface variation, broad mountain silhouettes, dead pine and ice
  clusters. Clan Hold is the inhabited hearth; the glacier is a compressed travel corridor; the
  Wild, Ash, and Ancient roosts use distinct ecology, ember, and ruin languages.

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
captures, for every cell, entry, centre, landmark,
exit, and overview views in day and dusk lighting under `tools/shots/world/`. Inspect for floating or
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
