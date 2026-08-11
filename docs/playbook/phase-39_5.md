## Phase 39.5 — World Map & Location Intelligence `[F/C]`

- [x] **39.5A — The location layer + the map that reads it** `[F/C]` ✅
  - **Done when:** the map can answer "where is a blacksmith" from authoritative data, and every
    authored location is placed in a cell scene.
- [ ] **39.5B — Cartography, quests on the map, districts** `[F/C]`
  - **Done when:** the deferred table at the bottom of this file has had its conditions met.

---

## 39.5A — The location layer `[F/C]` ✅

*Inserted into the roadmap deliberately rather than silently: `NOW.md`'s live item was **40A**, and
the map appeared nowhere in the roadmap. Numbered 39.5 because that is when it landed. 40A is
untouched and still next. The maintainer approved the insertion before any code was written.*

- **Landed:** `MapLocationResource` / `MapLocationDatabase` / `MapLocationComponent`, a rewritten
  `MapService` and `MapScreen`, a new `MapView`, four pure helpers (`MapProjection`, `MapTiers`,
  `MapSearch`, `MapDistance`), **63 authored locations across all 15 cells**, 129 locale keys, five
  `--validate` rules, five negative tests (45 → 50), **47 unit tests** (1329 → 1376),
  `tools/gen_map_locations.py` and `tools/map_probe.gd`.

### The one architectural decision

⚠️ **A LOCATION'S POSITION IS ITS NODE'S TRANSFORM IN THE CELL SCENE, AND IS NEVER AUTHORED TWICE.**
The `.tres` says what a place *is* and links to the authoritative shop/service/dialogue record **by
id**; a `MapLocationComponent` parented to the stall or keeper it names says *where*. Nudge a market
stall and its pin moves with it, because the pin is a child of the stall. This is
`TravelNodeComponent`'s existing "authored where it sits, not in a database" rule reused, and it is
the only reason the map cannot drift from the world.

⚠️ **The corollary is that the seam lives in `.tscn`, which nothing used to check.** `IDS.md` records
this as an open hole for `shop.*` and `service.*`: a typo in a `VendorComponent.ShopId` gives **no
prompt at all** rather than an error. A map location is referenced from a scene *by construction*, so
shipping with the same hole would mean a mistyped `LocationId` produced a marker that silently never
appears — indistinguishable from a location the player has not discovered yet. `--validate` now
regex-scans cell scenes **in both directions**: every id a scene names must exist, and every authored
location must be placed. The second direction is §54's no-orphan rule enforced by a machine.

### The three things that were only found by playing it

⚠️ **THE WHOLE GREEN BATTERY WALKED PAST ALL THREE.** Build, 1374 tests, `--validate`, 50 negative
tests, `--economy`, `--state`, `--play` and `tools/map_probe.gd` were green while the map was, in
order: showing nothing, black, and unable to fast travel. The maintainer found each within minutes of
opening it. **This is 39A's "render from the seat, not at the object" wearing UI clothes: a defect
that lives in screen space is only visible from screen space, and this repo currently has no way to
capture that** — `--play` cannot press `M`, and the Godot MCP drives the *editor*, not the game.

1. ⚠️ **A `MapProjection` IS BUILT BEFORE LAYOUT, SO ITS VIEWPORT IS `(1,1)`.** `WorldToScreen`
   centres on `Viewport * 0.5`, so the entire world projected about a **half-pixel origin at the
   top-left corner** and every marker was culled off-screen. `MapProjection.Resized` existed and was
   *never called* — the method was written in the same sitting and the call site was simply missed.
   ⚠️ **The symptom named the wrong system:** what stayed on screen was the region lettering, which
   is not a marker, so it read as "discovery is broken". `MapView.Fitted` now reconciles on every
   read and two tests pin both states. **A value type carrying layout state is a stale-cache bug
   with no cache in it** — invariant 7's family, one member further out.
2. ⚠️ **A CORRECT PLOT ON A DARK RECTANGLE READS AS A SCREEN THAT FAILED TO LOAD.** Fixed with real
   data rather than art: `MapService` measures each cell's ground footprint from the geometry
   actually in it as the cell streams in, persists it, and the view draws the known world as land.
   ⚠️ Ground is identified as **"big and flat"** (vertical extent < 2 m, area > 100 m²) because
   **a cell has no authored size** — `RegionCellResource` carries a centre and nothing else, and the
   floor dimensions exist only as prose in the region `.tres` header. Unioning every visual would let
   one tall tree stretch a cell across the realm.
3. ⚠️ **MOVING A FEATURE SOMEWHERE BETTER IS INDISTINGUISHABLE FROM DELETING IT.** Fast travel moved
   from a flat always-visible list onto the selected marker. But waystone pins are `Secondary` tier
   and discovered by **proximity**, so a player with five attunements could open the map and see no
   way to travel at all. The list is back *and* selection offers it: any location in a cell with an
   attuned waystone now offers the jump, because you select "The Embermarket", not the unremarkable
   stone at its north end. ⚠️ **Attunement is the gate, not marker discovery** — gating on both would
   refuse a jump the player has already paid for.

### Decisions worth carrying

- ⚠️ **TWO POSITION STORES, AND THE SPLIT IS LOAD-BEARING.** `_livePositions` is what components
  registered this run; `_savedPositions` is what a save remembered; reads prefer live. Invariant 1
  says a region loads whole and only one region is resident, so the other region's markers have no
  live position to offer — but a save must never overwrite a marker that is standing right there.
  **A load replaces the saved half and does not touch the live half.**
- **Discovery has two states, Unknown and Discovered.** Rumoured and Fully-Known were **cut, not
  stubbed** (40B's rule, 39C the worked example). ⚠️ The condition is a check, not a verdict:
  *when a dialogue graph sets a flag naming a place the player has not visited.*
- ⚠️ **`RevealWithCell` MEANS "KNOWN ON ENTERING THE REGION", NOT "ON ENTERING THE CELL"** — because a
  region loads whole. That is the intent for settlements (a local knows the towns of their homeland)
  and is why nothing a player must *find* uses it. **The three Frostfang roosts deliberately do not.**
- **There is no `DistrictKey`, and that is a decision.** What looks like a district convention —
  `shop.embermarket.*` rather than `shop.ember_crown.*` — is an id namespace whose segments are
  exactly the cell names, so every label would have read "The Embermarket › The Embermarket".
  A field every `.tres` sets to empty is the stub 40B forbids. Condition: *when a settlement's cell
  authors more than one named quarter.*
- **Group picks the shape, category picks the glyph.** Six silhouettes the eye can separate, 27
  categories under them. Twenty-seven subtly different icons is the "30 markers that look almost
  identical" failure; colour never works alone, so `ColorVision` costs nothing.
- **Tier culling is why clustering is not here.** Clustering solves "too many markers at the zoom
  where they are all visible"; culling means that state does not arise. ⚠️ Condition: *when two
  `Detail` markers overlap at `DetailZoom`.* The Embermarket's 17 pins sit 5–11 m apart.
- **Five settlement names are REUSED from their waystone's locale key** rather than authored again.
  One place, one name; renaming the waystone renames the pin.
- ⚠️ **`tools/gen_map_locations.py` generates the `.tres`, the locale block and the scene markers from
  ONE table**, and `--check` is a gate. Its first version was not idempotent — the locale block was
  spliced with `\n` into a CRLF file, so it rewrote itself every run and `--check` was permanently
  dirty and therefore useless.
- ⚠️ **`tools/map_probe.gd` reported 16 cells where the realm has 15**, because an unanchored
  `Center = Vector3(` matches inside a **region's `SafeZoneCenter`** and paired a bogus centre with
  the last cell id seen. Both regexes are anchored now. **A probe that is confidently wrong about a
  count it prints is worse than no probe.**

### Verified

| | |
| --- | --- |
| Build | clean, **0 warnings** |
| Tests | **1376 passing** (39.5A adds 47) |
| `--validate` | exit 0; `MapLocationDatabase loaded 63` |
| Negative tests | **50/50**, tree restored clean |
| `--economy` | **price landscape identical**, diffed against `HEAD~1` data rather than assumed. ⚠️ The first attempt was void: `git stash` had nothing to stash on a committed tree, so both runs used the same code |
| `--state` | 2 regions, 15 cells, 63 items, 23 shops, 15 services, **63 map locations** |
| `map_probe` | **63 markers across 15 cells**, each resolving to a distinct in-cell world position |
| `--play` | booted, restored 33 objects, no new warnings. ⚠️ The 3 `CraftingStationComponent has no owning Entity ancestor` warnings are **pre-existing** — confirmed by running the same boot against `HEAD~1` scenes and counting the same 6 lines |
| Not verified | ⚠️ **No screenshot of the map screen exists.** Everything visual here was verified by the maintainer opening it, which is how all three defects above were found |

### Two things worth carrying into the next sub-phase

1. ⚠️ **THIS REPO CANNOT SEE ITS OWN UI, AND THAT IS NOW THE MOST EXPENSIVE GAP IN THE HARNESS.**
   Seven "RENDER IT" firings were about 3D placement, and `tools/*_shots.gd` answers those. A UI
   screen has no equivalent: `--play` cannot press a key, and the Godot MCP drives the editor rather
   than the running game. **Three defects shipped through a fully green battery for exactly this
   reason.** A `--mapshots`-style bootstrap mode that builds a screen, drives real state into it and
   renders it to PNG is the missing tool, and it is worth a sub-phase of its own.
2. ⚠️ **WHEN A FEATURE MOVES, LEAVE THE OLD DOOR OPEN UNTIL THE NEW ONE IS PROVEN REACHABLE.** Fast
   travel was not broken, it was *relocated behind a discovery gate that had not been walked yet* —
   and to the player that is identical to a deletion. Ask of any moved affordance: **what state is
   the player in when the new path does not exist yet?**

---

## Deferred to 39.5B — each with the condition that triggers it

| Brief § | Deferred | Lands when |
| --- | --- | --- |
| §15 | Marker clustering | two `Detail` markers overlap at `DetailZoom`. Tier culling means they do not today. |
| §20, §21, §30 | Quest markers, search areas, journal ↔ map links | `QuestResource` gains a place to point at. 37.5E found the blocker: **a quest names a template id, not a location.** A quest-data change, not a map change. |
| §19 | Live NPC positions | `ScheduleComponent` exposes a current destination the map can read without polling every NPC. Static "kept by" via `DialogueId` ships now, and search already finds a keeper by name. |
| §19 | Quest-only NPCs as locations | they have no trade, so a category for them would be a lie. Lands with quest integration above. |
| §24 | Route drawing / reachability | measured need. Roads are not a graph; the navmesh is per-cell. |
| §11 | Districts | a settlement's cell authors more than one named quarter. |
| §33, §27 | Terrain cartography, fog rendering | after the information hierarchy is proven. The land layer is the floor, not the ceiling. |
| §26 | Rumoured / Fully-Known | a dialogue graph sets a flag naming a place the player has not visited. |
