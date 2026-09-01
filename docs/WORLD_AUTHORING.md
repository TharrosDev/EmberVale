# World authoring

The canonical exterior-region workflow. Read `CLAUDE.md`, `NOW.md`, `ARCHITECTURE.md`,
`ART_STYLE.md`, `ASSET_POLICY.md` and `SAVE_FORMAT.md` before using it.

> **If you are here to build a new region, the shortest path is:**
> `cp tools/region_spec_template.py tools/region_spec_<yours>.py`, follow its numbered comments,
> then `python tools/world_quality_check.py <yours>`. The template is a working spec — running it
> directly builds a four-cell region in memory and checks its lattice — and every rule on this page
> is either enforced by a validator or written into its comments. This page is the long version.

---

## 1. The one-paragraph model

**Terrain creates geography. Props reinforce geography.** A region is one continuous ground function
(`WorldHeightfield`) pooled from every cell's authored landforms, roads and yards in world space.
That function is the rendered mesh, the collider, the navigation source, the surface the props are
conformed onto, and the surface the scatter is planted on. Nothing else in the world is allowed to
*be* the shape of the land: a corrie built from scaled rock clusters, a crater built from a ring of
boulders and a glacier pass built from alternating ice props are the three defects the landform
resource exists to retire, and all three shipped.

---

## 2. Region planning, in the order that works

Every step constrains the next. Doing them out of order means doing some of them twice.

| # | Step | The question it answers |
| - | ---- | ----------------------- |
| 1 | **Macro geography** | Where are the mountains, the valleys, the water and the barriers? What makes two places feel far apart? |
| 2 | **POI placement** | *Why* is the settlement / dungeon / landmark here? "At the crossing", "in the lee of the ridge", "on the seam" are reasons. "It is the starting area" is a role, not a reason. |
| 3 | **Route network** | How does a road get from one to the next *following* the ground rather than cutting through it? |
| 4 | **Transitional space** | Which cells exist to contain nothing? |
| 5 | **Detailed landforms** | The gameplay geography inside each cell: the cover, the high ground, the throat, the drop. |
| 6 | **Biome and materials** | Which of the ten shipped biomes is this ground made of? |
| 7 | **POI scenes** | The buildings, the interactables, the NPCs. |
| 8 | **Scatter and dressing** | The ecology profile first, authored props as *detail* on top of it. |
| 9 | **Map integration** | ⚠️ In the same change that adds the place. Never as a follow-up. |
| 10 | **The QA suite** | `python tools/world_quality_check.py <region>` |

### Scale

Aim for **90–120 m centre to centre** between neighbouring locations and **150–300 m** to anything
that should feel remote. Under about 60 m two locations share a property line and the realm reads as
a corridor of rooms — that was the Ember Crown before the overhaul, at 52 m. Over about 400 m with
nothing in between, the player is walking to fill a progress bar.

### Empty space, and why it is the hardest thing to keep

Six of the Ember Crown's sixteen cells and five of Frostfang's ten carry a road, weather, vegetation
and landform and **no gameplay beat at all**. That is the feature. A realm where every thirty metres
has a purpose advertises on every step that it was designed, and the locations that do matter stop
reading as locations. ⚠️ **Do not fill them in.** If a beat wants to happen in a transitional cell,
the honest question is whether the realm needs another beat.

### Path semantics — the one exception to the above

**A strong visible path creates an expectation, and a path that pays out with nothing at all teaches
the player that following a road is not worth the walk.** After that every road in the realm is worth
less, including the ones that go somewhere. So a dead end stays mechanically empty and still
*acknowledges* the walk: a vista, an abandoned structure, an unusual natural feature, a distant
landmark view, a piece of environmental storytelling. The Fen Edge causeway ends at two drowned
pillars looking back at Hollowreach's wharf; the West Downs drovers' track ends at a collapsed
sheepfold on the highest ground in the cell. Neither has loot, an interactable, an enemy, a map pin
or a line of dialogue.

⚠️ **The moment one dead end pays out in items, every dead end has to.** Curiosity is answered with a
view and a story. That is the whole budget.

### Settlements

Outskirts blend into landscape. A settlement's edge is a thinning of density, not a line — set the
scatter profile's `EdgePadding` to about 1 so the verges run into the next cell's country instead of
stopping three metres short of the seam.

---

## 3. The ground is one surface per region

⚠️ **READ THIS BEFORE ANYTHING ELSE STRUCTURAL.** Until 2026-08-29 every cell owned a flat 0.5 m
`BoxMesh` floor, a matching `BoxShape3D`, and a decorative 4 cm surface skin that faded to exactly
zero at the cell boundary. Roads and yards were 6 cm slabs laid on top. That contract was seam-safe
and it was also the reason the realm read as fifteen rectangles touching.

- **`WorldHeightfield` is the region's one ground function**, built once by
  `RegionStreamer.Configure`. Two cells that abut sample the identical function at the shared edge,
  so **seams match by construction and there is no edge fade anywhere.** A ridge authored on one cell
  runs into its neighbour on purpose.
- **`WorldCellPresentation` builds the terrain mesh AND its collider**, and parents the collider into
  the cell's `Nav`, so the navmesh bakes off real elevation with no extra wiring.
- **`WorldTerrainConform` drops every authored node onto that ground at load**, so a node's authored
  Y is its clearance *above* the ground. Opt out with the `terrain_absolute` group.
- **`WorldRegionBackdrop` continues the same function outward** past the lattice into ridged
  mountains — one draw call, no collision. It samples the real heightfield, so the join is not a
  join. (It was twenty-six grey cones on a circle; every wide shot in the repository showed it.)
- **Grade discipline.** `CharacterBody3D`'s floor limit is 45° and `StepUp.MaxHeight` is 0.5 m, so a
  slope under about **0.7** rise-over-run is walkable, one over about **1.0** is an honest
  collider-free wall, and any raised ground over half a metre needs a ramp or it is a wall by
  accident. `--validate` refuses an authored route over a 0.80 grade.
- **Two winding traps, both of which shipped for an afternoon.** The collision soup is wound the
  *opposite* way to the render mesh — physics does not care, but the navmesh baker reads the wrong
  winding as a ceiling and bakes nothing but rooftops. And the render mesh was itself back-facing for
  as long as there was an opaque floor underneath to hide it. If a cell bakes zero navigation
  polygons, or the world renders as props over open sky, look at `WorldTerrainMeshBuilder`'s two
  index loops before anything else.

### Landform authoring primitives

There is one resource — `WorldLandformResource` — and two shapes. Everything the world needs is a
combination of them, and that is deliberate: fifteen classes for fifteen nouns would each need their
own seam behaviour, their own culling and their own validator.

| To author | Shape | Recipe |
| --------- | ----- | ------ |
| hill, knoll | Mound | `h` positive, `fall` 0.85–0.95, `flat` 0 |
| basin, hollow | Mound | `h` negative, `fall` 0.85–0.95 |
| crater | Mound (rim) + Mound (floor) | a wide positive rim, then a narrower negative floor inside it |
| plateau, terrace, shelf | Mound | `flat` 0.85–1.0, `fall` 0.3–0.5 — a level surface at `h` |
| building pad, plaza, pit floor | GroundArea | `Elevation` is an **absolute world Y** |
| cliff, scarp | Mound or Ridge | `fall` 0.10–0.30 — grade past 1.0 is a wall with no collider |
| ridgeline, embankment | Ridge | `h` positive, `half` 8–25 |
| ravine, gully, crevasse, channel | Ridge | `h` negative, `fall` 0.2–0.35, `flat` 1 |
| mine cut | Ridge (negative) + Mound (spoil bank beside it) | |
| valley | two Ridges facing each other, or one wide negative Mound |
| pass | two Ridges with a gap, plus a Mound for the saddle floor | Glacier Pass is exactly this |
| summit | Mound (`fall` 0.6) + Mound (`flat` 1) for the court on top | the Ancient Aerie |
| shoreline | Mound (gentle wadeable shelf, `flat` 0.85) then Mound (`fall` 0.2, deep) | the shelf is what makes it a shore rather than a step |
| marsh | wide negative Mound, `fall` 0.9, plus a Wetland biome | |
| road | PathSegment, not a landform | a road is a *cut*: at full mask its centreline is the graded line between its own endpoints |
| river corridor | Ridge (negative, `flat` 1) plus a Water body over it | |

**Naturalisation.** `Irregularity` warps a landform's boundary by a noise field scaled to its own
size, so it keeps its authored place, height and grade while its *edge* stops being drawable with a
compass. The generator applies **0.26 to every natural landform and 0 to anything levelling**
(`flat` > 0.5) — a pit floor, a terrace and a market pad are made things and should look made.
Override per landform with `irr=`.

---

## 4. Terrain materials and biomes

⚠️ **There are no ground textures in this repository and that is the contract, not a shortcut.**
`ART_STYLE.md` §4/§6.3 forbid photo texturing outright and the model set the terrain sits under ships
with zero texture images. A CC0 PBR ground pack would make the terrain the only photographed thing in
a hand-painted world. Detail comes from the noise field instead — which also means there is no tile,
so there is no tiling to hide, no texture array to stream and no VRAM cost.

Three layers of data, each reusable by everything above it:

```
data/terrain_layers/*.tres    a SUBSTANCE   soil, mud, dead grass, cliff rock, scree, ash, snow, ice…
        ↓  (six semantic slots)
data/biomes/*.tres            a PLACE       temperate lowland, wetland, excavated, alpine, glacier…
        ↓  (region default + per-cell override)
tools/region_spec_<region>.py a REGION
```

**The six slots are semantic, not a paint order:**

| Slot | What it is | Driven by |
| ---- | ---------- | --------- |
| `Ground` | what this country is made of, flat and dry | — |
| `Sparse` | the patchy second surface broken into it | macro noise + `SparseCoverage` |
| `Rock` | what a slope exposes | gradient, `SlopeBand` |
| `Cap` | what altitude adds — scree, snow, bare summit | height, `HeightBand`, shed by `CapSlopeShed` |
| `Road` | the compacted travelled surface | the path mask in vertex red |
| `Shore` | the wet margin under and above the waterline | `ShoreLevel` / `ShoreBand` |

Ten profiles ship: `TemperateLowland` `Pasture` `Woodland` `Wetland` `BurnedHeath` `Excavated`
`AshWaste` `Alpine` `Snowfield` `Glacier`. **Reach for one first and override it on the cell rather
than forking it.** A new profile is warranted when a region needs a ground identity none of the ten
has; build it out of the existing layers so the *next* region can use it too.

The per-cell `Tint` is legacy. It can only apply a flat wash and has to be faded at the cell edge to
stop it drawing the lattice back onto the ground; a per-cell **biome override** is the replacement.

### Atmosphere is part of the material

⚠️ **Palette alone cannot make a region look like a different place: neutral-grey bedrock under a
golden-hour sun IS warm tan sand.** That is not a metaphor — it is what the Clan Hold rendered as
while every colour in its spec was cold. `WorldEnvironmentProfileResource` carries four fields that
fix it, all multipliers on top of the day/night and weather curves rather than replacements for them:

- `SunTint` — the region's key light colour. Frostfang: `(0.74, 0.82, 0.96)`.
- `SunEnergyScale` — under 1 for a realm under permanent overcast.
- `HazeColor` — what the air is made of. Ash in the Ember Crown, blown snow in Frostfang. The single
  strongest cue for distance and biome in a wide shot.
- `HazeScale` — over about 2.5 the far cells stop being readable.

---

## 5. Water: the non-swimming safety contract

There is no swimming in Embervale, and the world has real basins with steep banks in it. Every water
body inherits three rules by being **declared as data** (`WorldWaterResource` on the cell) rather
than drawn as a mesh in a scene:

1. **Under `WadeDepth` (1.1 m) is ordinary ground.** The player walks it and fights in it. Shallow
   margins are a feature — they are what makes a shore read as a shore rather than as a wall.
2. **Over it is out of bounds, and the LAND says so.** Author a bank steeper than the 45° floor
   limit, as Hollowreach's drop-off and the Tarn's shelf do. ⚠️ **An invisible wall is forbidden**
   where terrain can communicate the boundary, which is everywhere.
3. **Over `DrownDepth` (1.9 m), `WorldRecovery` puts the player back** on the last dry, walkable
   ground they stood on. It recovers; it does not kill. A player who can neither escape nor survive
   loses progress to a mistake the world never warned them about.

Rule 3 is what makes rules 1 and 2 safe to author against: no arrangement of terrain, knockback,
dismount or dragon breath can leave a player stuck, so an author may dig a real basin without proving
every metre of its rim is climbable.

⚠️ **Draw a water body LARGER than its basin.** `WorldCellWater` bakes per-vertex depth from the
heightfield and fades the surface out wherever the ground rises through the waterline, so the
coastline is the terrain's own contour. A rectangle sized to fit *inside* the basin leaves a straight
edge of open water short of its own shore — which is what two rounds of hand-sizing produced before.

⚠️ **Never author a water surface as a mesh in a `.tscn`.** It is invisible to the system whose job is
keeping the player out of it.

`WorldRecovery` also covers **pits with no walkable exit**, by the same mechanism: well below the
surrounding ground, no progress, and a local flood fill finds no way out.

---

## 6. Ecology and dressing

- Add a `WorldBiomeScatterResource` to **every** cell. ⚠️ `Count` is a **density** — instances per
  100 × 100 m, scaled by the cell's own footprint — because cells range from 50 × 90 to 200 × 110 and
  a flat per-cell count draws the lattice back onto the ground in vegetation after the terrain has
  stopped drawing it.
- **Four fields separate a scattered layer from a generated one:**
  - `MaxSlope` — the steepest ground the species stands on. Trees ~0.4, scrub ~0.6, loose stone ~1.0.
    ⚠️ Without it, vegetation grows sideways out of every cliff in the region, which is exactly what
    happened to the corrie walls and the glacier's buttresses.
  - `HeightRange` — the altitude band it survives in. This is how a tree line happens.
  - `Clumping` / `ClumpScale` — how hard it gathers into stands. ⚠️ **Even spacing is the most
    recognisable pattern there is**; the eye finds the regularity long before it finds a repeated
    model.
  - `Saturation` — under 1 drains the source model's colour. The only way to make one shared asset
    read as two regions' worth of material: a tint *multiplies*, so it can darken a hue but never
    drain it.
- Instances lean partway toward the ground normal, so a hillside is not a pin cushion.
- Scatter is deterministic and cell-local. The planner clears every `WorldPathSegmentResource` and
  `WorldGroundAreaResource` automatically; add `BiomeScatterExclusionResource` circles around lairs,
  landmarks, doors, arenas, schedule paths and deliberate clearings.
- Repeated non-interactive background forms use MultiMesh. Interactive or colliding props stay
  authored nodes so nav and interaction remain inspectable.
- **Props are detail, not structure.** Rocks, cliffs, glacier assets and vegetation provide
  silhouette breakup, believable geological exposure, cover and landmarks. They do not make
  mountains, craters, corries or passes. Avoid rings of rocks, evenly spaced cliff props, repeated
  identical chunks of one model and rows of trees.

---

## 7. LOD, HLOD and visibility

- Each scatter layer has a detailed visibility end and fade margin. `HlodShape` non-zero builds a
  second, sparser MultiMesh from the same deterministic placement set; the ranges overlap and
  cross-fade.
- ⚠️ **The proxy tier uses the SAME mesh at a fraction of the density.** It used to be a five-sided
  cone or a unit cube in a flat colour, and at the ranges that actually engage — 92 m for scrub —
  a hillside of them read as a scattering of black crates. An HLOD tier is a *silhouette* contract; a
  primitive keeps the mass and throws away the silhouette, which is the half that matters at distance.
- `WorldVisibilityManager` checks the camera four times a second and hides only cosmetic biome
  batches beyond `BiomeCullDistance`. Gameplay roots, navigation, schedules, interactables and
  persistence stay resident.

---

## 8. Location authority: one place, every surface

The placed `MapLocationComponent` is the world-position authority for a named place. Its
`MapLocationResource` supplies identity, category, tier, discovery rules and links to existing
shop/service/dialogue/property/travel records; it never supplies another coordinate.

`cell scene anchor → MapLocationComponent → MapLocationResource id → MapService → UI/quest/travel`

For a new reachable POI:

1. Build and frame the physical entrance in the cell scene. Keep its interaction front and approach
   clear with an authored scatter exclusion.
2. Add one row to `tools/gen_map_locations.py`, anchored to the entrance node itself.
3. Run the generator, then `--check`. Do not add X/Z to a resource, quest, map widget or travel list.
4. A Reach/Defend objective uses the canonical `location.*` id as `TargetId`.
5. If the POI owns fast travel, put `TravelNodeComponent` at the real landing point.
6. `--validate` and `map_probe.gd`.

⚠️ **A LEVELLED PAD IS USUALLY A ROAD, SO DO NOT BUILD ON ONE (42B).** A `GroundArea` exists where a
settlement needed flat ground, which is where its road already runs: `Area_crossway_compound` is 12 m
deep and `Path_crossway_compound` plus its shoulder is 7 m of that, and `Area_hollowreach_street` is
8 m of road and 2 m of shoulder in an 8 m pad. Two guild hubs were placed on their pads for the good
reason that the ground was flat and reached, and both blocked their cells' routes. Read the cell's
paths out of the generated `.tres` (`Width`, `Shoulder`), clear `Width/2 + Shoulder` from every
centreline, and author a new `Yard` beside the road with the abutting pad's own `Elevation`.
⚠️ `--validate`, `check_cell_layout.py` and `cell_scene_audit.gd` all pass on a building sitting
across a road. **`world_traversal_probe.gd` is the gate that finds it.**

⚠️ **Scenery is not a place.** The drowned pillars at the Fen Edge and the sheepfold on the West Downs
have no map pin *on purpose*: a pin would advertise them, and the point of a dead-end feature is that
the player found it. A pin is for somewhere the player can go *back* to and needs to.

---

## 9. Automated quality gates

```text
python tools/world_quality_check.py --mode full        # every gate + reports
python tools/world_quality_check.py --mode fast        # engine/rendering-free deterministic gates
python tools/world_quality_check.py --mode engine      # content + Godot runtime regressions
python tools/world_quality_check.py --mode visual      # deterministic captures + localized diffs
python tools/world_quality_check.py --mode performance # structured machine-sensitive report
python tools/world_quality_check.py --list     # what it runs
```

One runner, with per-process timeouts and structured artifacts. It **orchestrates**; every rule lives
in the specialist tool that owns it. See `tests/README.md` for the authoritative matrix.

| Gate | Proves |
| ---- | ------ |
| `generation` | the committed `.tres` match their region specs |
| `build` / `tests` | the C# compiles; the pure-logic suite passes |
| `content` | references, well-formedness, reachability, **route grades**, **off-route traps** |
| `negative` | the content rules still *fail* when deliberately broken |
| `template` | the new-region starter still builds and its lattice is sound |
| `seams` | every road reaching a cell edge meets its opposite number |
| `layout` | no structure overlaps or leaves its cell envelope |
| `map` | every marker sits on the thing it names, in the right region |
| `stepup` | the player can still climb the realm's raised ground |
| `meshes` | the rendered mesh census against the per-cell budgets |
| `traversal` | a real capsule walks every authored route in the real collision world |
| `visuals` | the approach shots render and match the approved baseline |

### Off-route traversal

`WorldTraversalAnalysis` sweeps the whole lattice on a 3 m grid as a **directed** graph — walking
down and walking up are different edges — and finds ground the player can reach and cannot leave.

- A **trap** is reachable but cannot get home. A **pocket** is unreachable, which is usually
  deliberate (a cliff top, the far side of a corrie wall) and is not reported.
- ⚠️ **Only *shallow* traps fail.** A deep one is a hazard the author meant, the player *fell* into
  it, and `WorldRecovery` gets them out. A shallow one — a dish under 3 m deep whose rim cannot be
  re-climbed — tells the player nothing and is always an accident of two landforms overlapping.
- Water-covered basins are never traps: the recovery contract already owns them, and a rule that
  failed here would forbid lakes.

---

## 10. Visual QA

```text
Godot_..._console.exe --path . --script res://tools/world_shots.gd
```

Entry, centre, landmark, exit and overview views of every cell, in day and dusk, **lit by the
region's own atmosphere profile**. ⚠️ It used to light every region identically, which meant
Frostfang Reach was reviewed and signed off under the Ember Crown's golden hour — a screenshot gate
blind to a region's own air approves the wrong world.

Every capture generates a localized 32×18 perceptual grid compared against
`tests/visual_baselines/world_signatures.json`. The gate limits both whole-frame drift and the
number of locally changed blocks, so a missing prop cannot be averaged away. Failures keep current
PNGs and red heatmaps under `tools/shots/world_diffs/`. Inspect for floating or sunken props, scale and
rotation errors, gaps, z-fighting, repetition, blocked doors, cell borders, lighting discontinuity
and first/third-person occlusion.

⚠️ **Require player-height approach shots, not only overhead captures**, and

⚠️ **NEVER regenerate the baseline before looking at the PNGs.** A baseline regenerated without
inspection is a gate that has been switched off.

```text
Godot_..._console.exe --path . --script res://tools/world_shots.gd -- --update-world-baseline
```

---

## 11. Performance

- A region is resident as a whole. Count every cell node and material as simultaneously live.
- `WorldPerformanceBudgetResource` separates authored `.tscn` nodes from expanded runtime nodes.
  ⚠️ Read `MaxResidentScatterInstances` as a **memory** limit and `MaxDrawCalls` as a **GPU** limit:
  raising the first is nearly free because scatter is MultiMesh, and raising the second usually means
  authored `Node3D` props are doing a job scatter should be doing.
- A terrain cell is **one draw call whatever its resolution**. Aim for a vertex every 1.5–2 m where
  the player walks shaped ground and every 3–5 m across open transitional country.
- The terrain material is six painted layers and no texture samples at all, so it costs ALU rather
  than bandwidth, and it has two distance fades built in: sub-metre detail dies by 32 m and the
  coarse field by 170 m. Removing them is how a shader that looks fine on a hillside starts aliasing
  into herringbone on every long view.
- Never take frame time from the screenshot harness; its PNG writes deliberately block frames.

---

## 12. The region quality contract

Development invariants. Breaking one of these is how the world regressed before, and each is either
enforced by the gate named or is a review rule.

| Invariant | Enforced by |
| --------- | ----------- |
| Never use a flat `BoxMesh` as the primary terrain of an exterior region. | `layout`, review |
| Never use repeated rock props as the primary shape of a mountain, crater, corrie or pass. | review |
| Never manually author opposite sides of the same region seam. | `generation` + `seams` — the spec makes it impossible |
| Never let every POI touch the next POI. | review (§2 scale) |
| Never let every 30 metres contain a gameplay beat. | review (§2 empty space) |
| Never create an obvious trail without deciding what expectation it creates. | review (§2 path semantics) |
| Never add deep water without satisfying the recovery contract. | declaring it as data *is* the contract |
| Never author a water surface as a scene mesh. | review — it is invisible to the safety system |
| Never leave ground the player can walk into and cannot walk out of. | `content` → off-route traps |
| Never commit a region whose map does not match the playable world. | `map` |
| Never regenerate visual baselines before reviewing the screenshots. | review — the one gate a human owns |
| Never solve performance by silently reducing readability or deleting landmarks. | review |
| Never scatter a species without a `MaxSlope`. | the default is 0.7; setting 0 is a deliberate act |
| Never move `SpawnPoint`, `SafeZoneCenter`, portals, travel nodes, map pins, quest targets, schedules, shops, services, stations, encounter markers, lairs or property anchors without updating every dependent system and testing save compatibility. | `content`, `map`, save migration |
| Stable `PersistentId`, region, cell, map, shop, service, quest, faction and flag ids do not change. | `content` |

---

## 13. Region identities in the current world

- **Ember Crown** — temperate dying countryside. Worked timber and ember light against ash, old
  roads, subdued vegetation, brown-green earth, weathered stone; disturbed industrial ground around
  the Emberdeep, wetland at the Tarn and Hollowreach, and a progressively harsher burnt frontier
  north of the Crossway. **Sixteen cells across 330 × 440 m**, town-centred; six are transitional and
  two of those are dead ends.
- **Frostfang Reach** — alpine, and unmistakably a different country. Bedrock as the *ground* rather
  than soil over it, snow that lies on shelves and slides off faces, wind-beaten surfaces, sparse
  vegetation under a tree line, dramatic vertical silhouettes, cold light and thick air. **Ten cells
  across 340 × 380 m in its own coordinate band (x 260..600).** Five cells are marches, snowfield and
  high traverse with nothing in them.

---

## 14. Before shipping

```text
python tools/world_quality_check.py
Godot_..._console.exe --headless --path . -- --play
```

The automated gates establish structural correctness. A human traversal still owns interaction,
camera, mount, quest and subjective composition sign-off.

---

## 15. The measured baseline (2026-08-30 world-quality pass)

Kept here rather than in `docs/NOW.md`, which stays one screen and carries only the CURRENT
sub-phase's numbers. Re-measure with `godot --path . --script res://tools/world_perf_probe.gd`
before claiming a region got cheaper or dearer than this.

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
