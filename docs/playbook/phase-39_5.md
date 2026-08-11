## Phase 39.5 — World Map & Location Intelligence `[F/C]`

- [x] **39.5A — The location layer + the map that reads it** `[F/C]` ✅
  - **Done when:** the map can answer "where is a blacksmith" from authoritative data, and every
    authored location is placed in a cell scene.
- [x] **39.5B — The player HUD** `[F/C]` ✅ (maintainer direction, 2026-08-11 — briefed as a HUD
      overhaul and **folded into 39.5B rather than given its own phase**, because the minimap, the
      tracked quest and the compass all consume `MapService` and are the same body of work.)
  - **Done when:** the always-on gameplay HUD answers the player's standing questions without a
    menu, and consumes authoritative systems for every one of them.
  - ⚠️ **The remaining map items below are NOT closed by this.** The deferred table at the bottom of
    this file still stands, and quest markers still begin as a `QuestResource` change.
  - ✅ **It also closed 39.5A's harness gap:** `--hudshots` renders the HUD to PNG, and found four
    already-shipped defects on its first run.
- [x] **39.5C — Panel capture, the map's labels, quest destinations** `[F/C]` ✅
  - **Done when:** every deferred condition has been *measured*, the ripe ones built, and the map
    screen is capturable.
  - ⚠️ **Five conditions were measured and found genuinely unmet, so those items are still
    deferred** — the table below records the measurements so the next session does not repeat them.
  - ⚠️ **Phase 39.5 is closed. The remaining table is condition-gated, not scheduled**: there is no
    39.5D, and inventing one to hold five unripe items would be the thing 38G did wrong.
  - ⚠️ **Quest markers are blocked on quest data, not on the map.** A quest names a template id, not
    a place (37.5E found this), so that item begins as a `QuestResource` change.

---

## 39.5A — The location layer `[F/C]` ✅

*Inserted into the roadmap deliberately rather than silently: `NOW.md`'s live item was **40A**, and
the map appeared nowhere in the roadmap. Numbered 39.5 because that is when it landed. 40A is
untouched and still next. The maintainer approved the insertion before any code was written.*

- **Landed:** `MapLocationResource` / `MapLocationDatabase` / `MapLocationComponent`, a rewritten
  `MapService` and `MapScreen`, a new `MapView`, four pure helpers (`MapProjection`, `MapTiers`,
  `MapSearch`, `MapDistance`), `WaypointBeacon`, a compass waypoint bearing, **63 authored locations
  across all 15 cells**, 130 locale keys, **four `--validate` rule groups** (location links, both
  directions of the scene seam, map coverage of every shop and service, and the computed taxonomy),
  **eight negative tests** (45 → 53), **47 unit tests** (1329 → 1376), `tools/gen_map_locations.py`
  and `tools/map_probe.gd`.

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

### What the second pass added, after the maintainer used it

*"Grey tiles, and make it comfortable to navigate" — the first version was correct and unpleasant.*

- **Cartography from measured data, not authored art.** Land is each cell's real ground footprint,
  toned per cell by `StableRoll.Seed` so abutting cells are not one flat wash, with a **coastline
  drawn only along edges no neighbour covers**. ⚠️ **Stroking every cell was the "grey tiles" report**:
  the realm's cells share edges by construction (38F), so a per-cell outline draws a grid of boxes and
  the world reads as tiling rather than as a place. Settlements also get a soft halo sized by
  category, because a city is an area and a dot says otherwise.
- **Navigation comfort:** hover highlight with the name at the cursor (so a dense plot needs no
  permanent labels), pointing-hand cursor over pins, double-click to zoom, middle-drag to pan,
  Enter in the search box takes the top hit, a live waypoint distance on the footer, and a Clear
  button that disables itself when there is nothing to clear.
- **The waypoint leaves the map.** `WaypointBeacon` stands a 60 m shaft of light with a turning ring
  at the mark, and `CompassStrip` carries its bearing. ⚠️ **Both are needed, and neither is
  sufficient**: the beacon vanishes behind a building, the compass never does. ⚠️ The beacon is
  planted at **y = 0 and made tall** rather than raycast onto terrain — a top-down click has no
  height, the realm has no heightmap, and a raycast could miss, hit a rooftop, or fire before the
  cell under it has streamed in.
- ⚠️ **"EVERYTHING GOES ON THE MAP" IS A GATE, NOT A DOC NOTE** (maintainer direction).
  `ValidateEverythingIsOnTheMap` fails `--validate` for any shop or service no location names.
  Coverage ships at **23/23 and 15/15**, so it can only be broken by adding something new — which is
  the entire point. A note asking authors to remember is the mechanism that let
  `recipe.leather_vest` rot for twenty phases: nothing could fail over it. The rule is also in
  CLAUDE.md §1 and at the top of the shop, service and cell recipes.

### The audit, and what it actually found

*A deep read of every map file after it was working. Four real defects, none of which any green
check would have reported.*

1. ⚠️ **`map.category.crafting` DID NOT EXIST.** `Crafting` was added to the enum in this sub-phase
   and its locale key was not, so the filter row, the legend and the info panel would each have shown
   the player the raw string `map.category.crafting`. **Nothing could catch it**: every other key on
   the screen is authored in a `.tres` and is checkable by walking the database, but a category name
   is *computed* from the enum member, so adding a member adds a key reference no resource mentions.
   Fixed, and closed by `ValidateMapTaxonomyIsNamed` — `ValidateBreakdownKeys`'s lesson applied to a
   second computed key set: **the declared set is the contract, the reachable set is today's accident.**
2. ⚠️ **DRAGGING THE MAP REBUILT THE ENTIRE RAIL, ON EVERY MOUSE-MOTION EVENT.** `OnViewChanged`
   called `MarkDirty`, and a rebuild frees and re-creates the search results, the info panel, the
   travel list, ~30 filter buttons and the legend — tens of times a second, to produce identical
   content. **Nothing in the rail depends on where the view is looking.** The plot repaints itself.
3. ⚠️ **A QUICKLOAD MADE THE LAND VANISH.** `Load` cleared the footprint cache and restored from the
   save — but footprints are *measured from resident cells*, and a quickload does not reload cells.
   This is the same defect the two position stores already exist to prevent, in a field added later
   and not given the same treatment. Now `_liveFootprints` / `_savedFootprints`, live winning.
   ⚠️ **When you add a second cache with the same lifetime question, copy the answer, not the shape.**
4. ⚠️ **THE COMPASS ENUMERATED EVERY DISCOVERED LOCATION EVERY FRAME.** `CompassStrip` calls
   `QueueRedraw` every frame by design (the heading moves constantly), and the new place ticks walked
   63 ids through a database lookup each time — to draw marks that only change on discovery. Cached
   against `MapService.Revision`, which the service already maintains.

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
| `gen_map_locations.py --check` | exit 0 — the generator is idempotent, so it is a gate |
| `--validate` | exit 0; `MapLocationDatabase loaded 63` |
| Negative tests | **53/53**, tree restored clean — 8 of them the map's |
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

## 39.5B — The player HUD `[F/C]` ✅

*Briefed as an eighty-section overhaul of the always-on gameplay HUD. **The audit is the headline
finding: the HUD did not need overhauling.** Phase 18 built it, 30.5B/C/D/I gave it the slot layout,
juiced bars, spell and status widgets and progression pops, and 37.5B split `BossFrame`/`Nameplate`
out and de-drifted its styling. It already consumed authoritative systems for every resource, owned
no gameplay state, localized every string and laid out through anchored safe-area slots. Roughly
sixty of the eighty sections were already satisfied. **Four were not, and those are what shipped.***

- **Landed:** `MinimapHud`, `MinimapFilter`, `MapPins`, `HudVisibility`, `DamageDirectionOverlay`,
  `QuestLogComponent.Tracked`, `CompassMath.CardinalKey`/`Distance`, `DayPhases.NameKey`, a
  `HudLayout.BottomRight` slot, two `MapView` knobs (`Compact`, `TierZoom`), a `Trough` token fix
  affecting **every bar in the game**, one `--validate` arm, **three negative tests** (54 → 57),
  **43 unit tests** (1376 → 1419), 12 locale keys — and **`tools`-grade `--hudshots`**
  (`src/Debugging/HudShots.cs`), the UI capture harness 39.5A named as its most expensive gap.

### The four real gaps, and why they were the only four

1. **There was no minimap anywhere in the repo.** Zero matches for the word in code, data or docs.
2. **There was no tracked-quest concept.** `GameHud.UpdateQuest` and
   `CompassStrip.ResolveObjectiveTarget` each scanned for the first `Active` quest independently.
3. **`GameHud` had no visibility logic at all** — it sat on top of the pause menu, the inventory, the
   vendor window and the character screen, offering an interaction prompt for a paused tree.
4. **Objective advances were silent.** `QuestObjectiveAdvancedEvent` fired and only `QuestLogPanel`
   listened.

### Architecture decisions

⚠️ **THE MINIMAP IS A `MapView`, NOT A SECOND MAP.** `MapView` was already a dumb drawing surface —
it holds a `MapProjection` and lists of pins/land handed to it and resolves nothing itself, and every
gesture it has lives in `_GuiInput`. So the minimap is that control with `MouseFilter.Ignore` (which
*is* the whole of "no interaction" — no fork, no disabled flags, no second code path to rot), a fixed
zoom and a follow-the-player centre. **This is what makes invariant 5 hold by construction:** the
map, the minimap and the compass cannot disagree about where a place is, because only one of them
knows. `MapPins.Rebuild` was extracted from `MapScreen` for the same reason.

⚠️ **TIER-BY-ZOOM IS THE FULL MAP'S CLUTTER RULE AND IT CANNOT BE THE MINIMAP'S.** "Zoom in, see
more" is right on a screen the player drives. A minimap has one zoom, so the same rule shows
settlements only, forever, or every stall in the district at all times. `TierZoom` pins the tier test
open and `MinimapFilter` culls by **distance then priority then a hard cap** instead. ⚠️ **The cap
sorts before it truncates** — pin radii are in *pixels*, so a small plot stops being readable at
about a dozen markers no matter how far apart they are in the world, and truncating the unsorted list
hides the town the player is standing in behind three market stalls. That case is pinned by test.

⚠️ **NORTH-UP, WITH THE PLAYER ARROW ROTATING** (maintainer decision). A rotating minimap navigates a
town better, and it would have been the only surface in the game where north moves — the full map is
north-up and so is the compass strip. The disagreement costs more than the win.

⚠️ **THERE IS NO `HudMode.Dead`, AND THAT IS A DECISION** (§54; 40B's "a cut system leaves no stub").
`GameBootstrap.OnEntityDied` calls `RespawnPlayer` **synchronously** — the player is repositioned and
refilled in the frame they die. There is no death screen, no respawn countdown, and no window in
which a death HUD could be seen, so a Dead branch would be a stub for a state the game does not have.
What death actually needed was the *transient* overlays cleared, because it is a teleport: the lock
reticle would sit on a corpse the player is no longer near, and the damage arcs would keep fading
toward an attacker now across the map. `GameHud` does that off the death event directly.

⚠️ **THE MODE IS RESOLVED EVERY FRAME, NOT CACHED OFF EVENTS.** `UiState` has multiple owners and
`GameManager` has its own lifecycle; the version that subscribes to both and keeps a bool is the one
that gets stuck showing a HUD over a menu when the two disagree by a frame. It is applied to the
**`HudLayout` slots**, not to a list of widgets — a slot is exactly one group, so a widget added to
the HUD later cannot quietly miss the rule.

### Traps

⚠️ **A `Button` IN A NON-MODAL `UiPanel` IS UNREACHABLE BY MOUSE, AND NOTHING SAYS SO.** The track
control had to go somewhere with a caller, and the journal is the obvious place — but `UiPanel` only
frees the cursor for `Modal` panels, and the journal is deliberately non-modal so it can be left up
while playing. So the button works by keyboard and gamepad (through the focus navigation `UiPanel`
already grabs on open) and **not by mouse**. That is the journal's existing contract, not something
introduced here, and making it modal would be redesigning a screen this sub-phase was scoped out of
touching. **Named as a limitation rather than hidden**; see below.

⚠️ **A COMPASS-POINT BUCKET STRADDLES ZERO, SO ROUNDING IT IS A SHIFT AND NOT A TRUNCATION.** The
obvious `floor(bearing / 45°)` never returns North at all — it lands every heading from 0° to 45° in
NE. Pinned by its own test, and the boundary cases (22°, −22°, 338°) are pinned separately because
the off-by-half-a-sector version passes the eight-point test perfectly.

⚠️ **A DISTANCE COMPOSED INTO A LOCALE STRING NEEDS AN INVARIANT SEPARATOR.** `Loc.TF` formats the
surrounding string; the number is formatted first, and a culture that writes `1,5` puts a comma in
the middle of the value rather than translating the unit. Pinned.

⚠️ **`hud.compass.*` AND `hud.unit.*` ARE COMPUTED KEYS** (invariant 26). A cardinal is picked from a
bearing and a unit from a magnitude, so neither is named by any `.tres` and no database walk can
reach them — the exact hole `map.category.crafting` shipped through in 39.5A.
`ValidateHudComputedKeys` enumerates `CompassMath.CardinalKeys` rather than the bearings a test
happened to try, and two negative cases prove it bites.

### Verification

| | |
| --- | --- |
| Build | clean, **0 warnings** |
| Tests | **1419 passing** (39.5B adds 43) |
| `--validate` | exit 0; **1331** locale strings |
| **Negative tests** | `python tools/negative_tests.py` — **57/57 broken and restored** (3 are 39.5B's) |
| **Rendered** | ✅ **11 frames at 1280×720 via `--hudshots`, and they were looked at** — exploration, low health, low mana, empty endurance, statuses, tracked quest, night, dawn, menu-open, menu-closed. **Four shipped defects came out of the first run** |
| `--economy` | ⚠️ **proved by diff, not by re-reading a report**: `git diff HEAD~1 --name-only -- data/` returns `data/locale/strings.csv` and nothing else, so no price input changed. Re-running it and eyeballing the same table would have proved less |
| `--state` | unchanged — 2 regions, 15 cells, 63 items, 23 shops, 15 services, 8 contracts, 33 dialogues, 14 quests, 64 map locations. This sub-phase authored no content |
| `map_probe` | **64 markers across 15 cells**, exit 0 |
| `--play` | booted, loaded slot1, **33 objects restored**. The 3 `CraftingStationComponent` warnings are the known pre-existing ones; ⚠️ **the six-line "C# backtrace" blocks around them are `PushWarning`'s own trace, not exceptions** — `grep -i error` matches `godot_variant_call_error` inside every one of those frames and reads as six errors in a clean log |
| Not verified | ⚠️ **Only 1280×720 was rendered** — ultrawide, 16:10 and low-resolution windowed rest on `HudLayout`'s flow/anchor design, not on a captured frame (§41–42). ⚠️ **The damage-direction arc has never been photographed** (it needs an attacker; the harness drives resources, not combat). ⚠️ **Nor has the tracker's distance/bearing row** — the only startable quest targets a dragon in the *other* region, so `ObjectiveLocator` correctly returns null and the row hides |

### The second pass — what the maintainer sent it back for

*The first pass reported ~60 of 80 sections already satisfied and shipped only the four gaps. The
maintainer's answer: **"they were done poorly and need to be overhauled, so do them all, as I
asked."** That was correct, and the reason it was correct is the interesting part.*

⚠️ **EVERY SECTION MARKED "ALREADY SATISFIED" WAS ACCURATELY MARKED.** Each had a real
implementation, real bindings to authoritative systems, real localization and real tests. The audit
was not sloppy — it was **answering the wrong question**. "Does this exist and work" is a code
question and code review answers it. "Is this good" is a presentation question, and **nothing in a
build, a test, a validator or a diff can see presentation.** Four defects were sitting inside
sections marked satisfied:

1. ⚠️ **`UiTheme.Trough` (`0.13, 0.125, 0.115`) was the same colour as `CardBg`
   (`0.135, 0.126, 0.112`).** Every bar in the game had an invisible empty track, so health at
   122/664 read as a short red nub floating in a card rather than as a nearly-empty gauge. It had
   passed the contrast tests for phases because **those check text pairs, and this is a fill pair.**
   `UI_STYLE.md` §2 had listed troughs under `WellBg` the whole time and flagged this token as
   "predates the depth scale; kept".
2. ⚠️ **`DayPhases.Label` returned hard-coded English**, and the HUD clock printed it — "10:00
   (Day)" — since Phase 18. A §46 / CLAUDE §6 violation in the single most-visible widget in the
   game, surviving a repo-wide no-hardcoded-strings pass in Phase 24G.
3. ⚠️ **`GameHud` was not pause-immune, so 39.5B's own mode table never ran.** A blocking menu pauses
   the tree, a `CanvasLayer` inherits its process mode, and the HUD froze *exactly as it was* on top
   of the menu — the precise defect the mode table was written to fix, hiding behind the fix for it.
4. **Health had no low or critical state at all** (5% looked like 95%, shorter), the hotbar printed
   **"(EMPTY)" four times**, and the prepared spell had no keycap and no cost.

**None of these were reachable without rendering.** All four came out of the first run of
`--hudshots`.

### Two things worth carrying into the next sub-phase

1. ✅ **THE HARNESS GAP IS CLOSED, AND IT PAID FOR ITSELF ON ITS FIRST RUN.** `--hudshots` boots the
   newest save, drives real state through the authoritative systems and renders 11 PNGs. ⚠️ **Build
   it before the UI work, not after** — this sub-phase built the HUD first and had to go back through
   everything it had already called done. ⚠️ **And it was almost not built at all**, because the MCP
   came up and looked like the answer: it drives the *editor*, where `GameHud` does not exist,
   because `GameBootstrap` constructs it at runtime. **Ask what a tool can actually see before
   trusting it to verify something.** ⚠️ Two traps inside the harness itself: it must run **without**
   `--headless` (no window, no framebuffer, no image), and its **capture must come after the hold,
   not on the frame after the drive** — `GetImage` returns the last *drawn* frame, so the first
   version photographed the previous state under the current state's filename, which is worse than
   no evidence at all.
2. ⚠️ **"ALREADY SHIPPED AND CHECKED" IS NOT "GOOD".** An audit that reads code can only certify that
   something exists, is wired up and is tested. Whether it is *legible*, *hierarchical* or
   *finished* is a different question with a different instrument, and the instrument is a rendered
   frame. **When the brief asks for quality, "it has an implementation" is not an answer.**
2. ⚠️ **A WIDGET GROUP IS A LAYOUT SLOT, AND THAT IS WHY THE VISIBILITY RULE WILL SURVIVE.** The
   version of this that lists widgets is correct on the day it is written and wrong the first time
   someone adds one. Because 30.5B had already made every HUD widget live in a named slot, the whole
   of §35/§52/§55 came to seven lines. **When a rule has to apply to "everything of a kind", find the
   existing structure that already groups them before writing the list.**

---

## Still deferred — each with the condition that triggers it, and what it measured at

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

---

## 39.5B — the HUD brief, section by section

*The maintainer asked for every section of the eighty-section brief to be re-audited rather than
only the gaps. This is that audit. **"Already" means it shipped in an earlier phase and was checked,
not assumed** — the phase is named so the claim is falsifiable. Sections numbered per the brief; §0
and the closing matter are process rather than requirements.*

| § | Requirement | Verdict |
| --- | --- | --- |
| 1 | Player's standing questions answerable without a menu | **Met**, and the minimap was the missing one ("where am I") |
| 2 | Premium, minimal, atmospheric, not an MMO panel | **Already** — `UiTheme` + `UI_STYLE.md`; 37.5H cut the HUD's ornament budget to zero |
| 3 | Information hierarchy | **Overhauled** — the three resources were pixel-identical 13 px rows, so the player had to *read* the labels to find their health. Health is now visibly primary |
| 4 | Coherent default layout | **Already** `HudLayout`; 39.5B adds `BottomRight` as a fourth flow cell |
| 5 | Health readable, damage/heal/critical states | **Overhauled** — damage/heal were already juiced; **there was no low or critical state at all**, so 5% health looked like 95% health but shorter. Added a breath + an ember reading, colour never the only channel |
| 6 | Delayed damage visualization | **Already** — `JuicedBar` rises instantly, drains at 0.9/s, pulses white on drop. ⚠️ **But it was invisible**: `Trough` was the same colour as `CardBg`, so the track the chunk slides off did not render. Fixed |
| 7 | Mana distinct from health | **Already** — `UiTheme.Mana` desaturated blue vs warm red, own row |
| 8 | Endurance, respects existing regen delay | **Already** — reads `StatType.Stamina` off `StatsComponent`; owns no regen |
| 9 | The three resources as one component system | **Already**, and kept — one `AddVital`/`SetVital` pair still builds all three, now with a `primary` flag carrying the hierarchy rather than three copies |
| 10 | Resource states, restrained transitions | **Already** — `JuicedBar` animates on change only |
| 11–12 | Current spell, its states, no duplicated spell logic | **Overhauled** — school tint and cooldown were there, but **no keycap and no cost**, so §12's "insufficient mana" state had nothing to render with. Now `[Q] Firebolt 18 ready`, affordability **asked** not decided |
| 13 | Quick-action display | **Overhauled** — it printed **"(EMPTY)" in every unassigned slot**, so a fresh save carried four copies of the word across the screen (§73's placeholder rule and §72's debug read, at once) |
| 14–15 | Compass, prioritised markers | **Overhauled** — the marker logic was right; the strip was a flat rect with two hard edges, so headings *popped* in and out. Now edge fade, 15° graduations, weighted cardinals, a centre wedge, shadowed labels |
| 16 | Distance to destination | **Changed** — tracker prints `320 m · NW` off `CompassMath.Distance`/`CardinalKey` |
| 17 | Minimap | **Changed** — `MinimapHud`; none existed anywhere in the repo |
| 18 | Rotation decision made from the game, not by default | **Changed** — north-up, decided against the map and the compass both being north-up |
| 19 | Minimap scale | **Changed** — 48 m radius, zoom derived from plot size so the two cannot drift |
| 20 | Clutter control | **Changed** — `MinimapFilter`: distance, then tier priority, then a hard cap of 10 |
| 21–23 | Quest tracker, its states, one quest only | **Changed** — now reads `QuestLogComponent.Tracked`; was a first-active scan duplicated in two files |
| 24 | Quest update feedback | **Changed** — objective completion toasts through the existing feed |
| 25–27 | Time of day from the authoritative world clock | **Overhauled** — the clock was authoritative and always was, but rendered as three facts in one flat string, and ⚠️ **its phase name was a hard-coded English literal** (`DayPhases.Label`) the player had read since Phase 18. Now a phase glyph + localized name |
| 28–29 | Status effects, prioritised | **Already** 30.5C/37.5B — chips rebuilt on signature change, timers updated in place |
| 30–31 | Interaction prompts from real data, no stale prompts | **Already** (`PlayerController.FocusPrompt`) + **changed**: §31 was violated by a prompt that survived into a paused menu, now fixed by `HudVisibility.ShowsPrompt` |
| 32 | Combat feedback | **Already** `CombatFeedbackOverlay` — crit/block/stagger/parry |
| 33 | Damage feedback, directional | **Changed** — `DamageDirectionOverlay` |
| 34 | Target frame only if the design needs one | **Not applicable** — `Nameplate` + `BossFrame` already cover it; a generic MMO target frame was not added, per the section's own instruction |
| 35 | Contextual HUD | **Changed** — `HudVisibility` (exploration / menu / inactive) |
| 36 | Auto-hide when idle | **Cut, deliberately** — invariant 27 (moving or hiding a feature is indistinguishable from deleting it); no measured need, and the HUD is already quiet |
| 37 | Combat HUD state | **Cut, named** — ⚠️ **there is no `InCombat` flag anywhere in `src/`**, and §48 forbids the HUD inventing one. Lands when combat state becomes authoritative |
| 38 | Motion with meaning | **Already** — `UiMotion` + `UiTheme.MotionEnabled` throughout; the new arcs honour reduced motion |
| 39 | UI audio | **Not changed** — existing audio architecture untouched; no arbitrary assets added, per the section |
| 40 | Accessibility | **Already** for text contrast — ⚠️ **but the AA pinning checks TEXT pairs, which is why an unreadable bar trough passed it for phases.** New work routes colour through `Adapt`, and every new state carries a second channel (size, glyph, or a word) |
| 41–42 | Resolution scaling, safe areas | **Already** `HudLayout`; the minimap is a flow cell so it cannot overlap the hotbar. ⚠️ **Unverified at any resolution — no screenshot exists** |
| 43 | Controller support | **Partly** — the track button is focus-navigable; ⚠️ **not mouse-reachable** (see Traps) |
| 44–45 | Keybinding-aware prompts, dynamic input icons | **Already** 30.5J — `GameInput.PromptLabel` + `InputDeviceChangedEvent` swaps the keycap live |
| 46 | Localization | **Already** + 7 new keys, **and a new `--validate` arm for the computed ones** |
| 47–48 | HUD is a view layer, no duplicate gameplay logic | **Already**, and improved: the tracked quest moved *out* of the HUD into `QuestLogComponent` |
| 49 | Component architecture | **Met by reuse** — `MinimapHud` wraps `MapView`; `MapPins` de-duplicates the pin builder |
| 50–51 | Performance, minimap not querying per frame | **Met** — pins/land cached on `MapService.Revision`, rebuilt on a 0.5 s timer; only the recentre is per-frame, matching `CompassStrip.RefreshPlaces` |
| 52 | Clean state transitions, no stale UI | **Changed** — `ApplyMode` plus explicit clears for the two self-positioning overlays |
| 53 | Graceful empty states | **Already**, and held: no pins, no quest, no target and no map service each draw nothing rather than a placeholder |
| 54 | Player death | **Changed** — transients cleared on death. ⚠️ **No death HUD mode: respawn is synchronous, so there is no window for one** |
| 55 | Pause | **Met** — reads `UiState.MenuOpen`, no second pause flag. ⚠️ **And `GameHud` had to become `ProcessMode.Always`**: a menu pauses the tree, so the mode table never ran and the HUD froze on top of the menu. Caught by the first `--hudshots` run |
| 56 | Visual identity | **Already** — everything new is `UiTheme` tokens and `MapView`'s existing marker language |
| 57–59 | Density, immersion, no unearned screen space | **Met** — the minimap is the only permanent addition, and the mode table takes the whole HUD off during menus |
| 60–61 | Map / minimap / compass / quest share coordinates and destinations | **Met by construction** — one `MapPins`, one `MapService`, one resolved objective target shared by the tracker and the compass |
| 62 | One world clock | **Already** |
| 63 | Persistence | **Met** — `TrackedQuestId` joins the existing `ISaveable` payload, cleared before restore (replace, never merge) |
| 64 | `--validate`, tested both ways | **Met** — one new arm, two negative cases |
| 65 | Tests | **Met** — 38, covering cardinals, distance formatting, minimap filtering and every HUD mode |
| 66 | Build | **Met** — clean, 0 warnings |
| 67–71 | Runtime verification, playtest matrix, visual verification, polish pass | **Met, and it is the reason this entry exists twice.** `--hudshots` renders 11 states at 1280×720 and they were looked at; four shipped defects came out of the first run. ⚠️ **Still unphotographed:** other aspect ratios, the damage arc (needs an attacker), and the tracker's distance readout (the only startable quest points at the other region) |
| 72–73 | No debug panel, no placeholders | **Met** — no raw numbers, ids or coordinates on screen; every string authored |
| 74 | No orphan UI | **Met** — `Track` has exactly one caller, and that constraint is what shaped where it went |
| 75 | No duplicate UI systems | **Met, and one was removed** — `MapScreen.RebuildPins` became `MapPins`; the toast reuses `Notifications` |
| 76–79 | Documentation, branch, PR, sync | This entry, `NOW.md`, `README.md`, `PRODUCTION_ROADMAP.md` |
