## Phase 25 — Region Streaming & World Map `[F]`

> Replace the single flat sandbox with streamed authored regions, a map, and
> fast travel — before authoring four realms.

- [x] **25A — `RegionResource` + region scene convention** `[F]` ✅
  - **Goal:** regions are authorable data + scenes.
  - **Tasks:** add `RegionResource` (`.tres`: id, display name, realm, sub-cell
    list, bounds, default weather/day-phase bias, neighbour links) + a
    `RegionDatabase` auto-index. Define the region/sub-cell scene naming + placement
    convention (world-partition discipline) in a doc. Author one `RegionResource`
    for the current sandbox.
  - **Done when:** the sandbox is described by a `RegionResource`; the convention
    is documented for Phases 27/44.
  - **Done:** new `src/World/RegionResource.cs` (`[GlobalClass]`, mirrors `WeatherResource`):
    `Id`/`DisplayName`/`Realm` (a new fixed `Realm` enum — the four realms + Celestial)/`SubCells`/
    `Bounds` (`Aabb`)/`DefaultWeatherId`+`DayPhaseBias`/`Neighbours`, indexed by a
    `RegionDatabase` (copy of `WeatherDatabase`, registered in `ContentDatabases.InitializeAll`).
    The sandbox is authored as `data/regions/EmberCrown.tres` (`region.ember_crown`, realm
    EmberCrown, one `ember_crown.hub` sub-cell, clear/Day bias); `GameBootstrap.BuildSaveHeader`
    now reads the region name from `RegionDatabase` (via a `_currentRegionId`) instead of the old
    hard-coded literal, and `GameIds.Regions.EmberCrown` registers the id. `ContentValidator` gains
    region dup-id + neighbour/default-weather cross-ref checks (in `CollectCoreIssues`, so the boot
    and `--validate` gates both run them). The region/sub-cell scene convention
    (`scenes/regions/<region>/<cell>.tscn`, world-partition discipline) is documented in
    `ARCHITECTURE.md` §2.6h-2 + a "A new region" recipe in docs/RECIPES.md. Verified: build + 79 tests
    + `--validate` green (region checks pass); in-engine boot logs *"RegionDatabase loaded 1
    region(s)"* and the save header now reports "The Ember Crown" from the resource. No streaming
    yet — that is 25B.

- [x] **25B — `RegionStreamer`: load/unload by distance** `[F]` ✅
  - **Goal:** stream sub-cells around the player with a budget.
  - **Tasks:** add `RegionStreamer` that loads/unloads sub-cell scenes by distance
    with hysteresis and a per-frame instancing budget (don't hitch). Reuse the
    Phase 19 pooling/throttle discipline. Keep the current sandbox working as a
    single always-loaded cell.
  - **Done when:** moving across cell boundaries loads/unloads without a visible
    hitch (reviewed against the API); the sandbox still boots.
  - **Done:** new `RegionCellResource` (`[GlobalClass]`: `Id`/`ScenePath`/`Center`/`LoadRadius`);
    `RegionResource.SubCells` evolved into `Cells: Array[RegionCellResource]`. New `RegionStreamer`
    (`Node3D`, pausable, built in `BuildWorld` + `ServiceLocator`-registered) resolves the player
    each frame, computes planar distance to each cell, and applies the pure
    `StreamDecision.Decide(distance, loadRadius, unloadMargin, isLoaded)` — load inside `LoadRadius`,
    keep out to `+UnloadMargin` (~10 m hysteresis), then unload; loads are budgeted to **one instance
    per frame** (a drain queue, the `PackedScene` `ResourceLoader`-cached) so a wave never hitches,
    and `RegionCellLoaded`/`UnloadedEvent` publish for the 25D persistence seam. The sandbox is
    authored with two demo cells (`data/regions/EmberCrown.tres` + `scenes/regions/ember_crown/
    {waystone,north_ruin}.tscn`): a spawn-adjacent waystone and a far north ruin. `ContentValidator`
    now checks each cell `ScenePath` resolves. The procedural sandbox stays the always-loaded base.
    Verified: build + 85 tests (6 new `StreamDecisionTests`) + `--validate` green; **in-engine the
    waystone streamed in near spawn and streamed out (with hysteresis) as the player walked away**,
    while the out-of-range north_ruin never loaded — both load + unload paths confirmed live, no
    errors. Convention updated in ARCHITECTURE §2.6h-2 + CLAUDE §8.

- [x] **25C — Hard transitions + loading screen (realm-to-realm)** `[F]` ✅
  - **Goal:** discrete loads between realms.
  - **Tasks:** add a loading-screen state (`GameState.Loading` already exists) for
    hard transitions; tear down the old region, load the new, restore the player.
    Trigger via a transition volume/door interactable.
  - **Done when:** stepping through a transition runs a clean load and spawns the
    player correctly in the new region.
  - **Done:** a `RegionTransitionComponent` (an `InteractableComponent`) publishes a new
    `RegionTransitionRequestedEvent`; `GameBootstrap` performs the swap on the event (same
    shape as `DialogueComponent`): `ChangeState(Loading)` → `RegionStreamer.UnloadAll()` (new)
    + `Configure(destination)` re-targets the streamer → teleport the player to the
    destination's new `RegionResource.SpawnPoint` (new export) → rebuild neighbour portals →
    `RequestRegionChangeAutosave()` (the pre-built 24D seam) → a short `_loadingCountdown`
    settle (reusing the `_respawnCountdown` idiom) lets the new cells stream in behind a new
    `LoadingScreen` overlay before `ChangeState(Playing)`. Portals are spawned per
    `RegionResource.Neighbours` (a glowing torus + collider, in front of each region's spawn)
    and swapped on transition. A second region — `data/regions/FrostfangReach.tres` (Realm 1) +
    `scenes/regions/frostfang_reach/glacier.tscn` — gives EmberCrown a neighbour to travel to;
    `EmberCrown.tres` gained `SpawnPoint` + the neighbour link. A `region <list|goto <id>>` dev
    command drives transitions from F1. Verified in-engine: the maintainer walked the portals
    EmberCrown ⇄ Frostfang repeatedly — log shows `Playing -> Loading`, old cell unloads,
    `Entering <region>`, the destination cell streams in, `Loading -> Playing`, both ways with
    no new errors (the `PersistentId`/orphan warnings are pre-existing). Build + 85 tests +
    `--validate` (2 regions, neighbour + cell-path checks) green.

- [x] **25D — Persistent actors across streaming (PersistentSpawnDirector)** `[F]` ✅
  - **Goal:** the world remembers itself across load/unload.
  - **Tasks:** ensure streamed-in actors with `PersistentId` restore their state
    via the existing `PersistentSpawnDirector` (PR #29) when their cell reloads
    (dead enemies stay dead, looted chests stay looted). Read `src/Save/` first.
  - **Done when:** kill/loot an actor, leave the cell, return — state persists;
    round-trips through a full save/load too.
  - **Done:** new `src/Save/CellPersistenceDirector.cs` — a `Node`/`ISaveable` (ServiceLocator +
    SaveManager registered, built in `BuildWorld` before the streamer) bridges streamed cells to
    per-actor persistence without changing the authoring model (actors stay in the cell `.tscn`).
    On `RegionCellLoadedEvent` it walks the cell subtree for `IEntity` actors with a `PersistentId`
    and reconciles: an id in its `_removed` ledger is culled (`QueueFree`), survivors get any
    snapshotted `ISaveable`-component state re-applied (health/inventory). Removal is detected
    uniformly via the actor body's `TreeExiting` (enemy death *and* pickup despawn both count),
    suppressed while the cell is unloading (an `_unloading` cell-id guard, since the streamer's own
    frees fire the same signal). On `RegionCellUnloadedEvent` it snapshots survivors. It is itself
    `ISaveable` (`SaveId "cell_persistence"`: a `removed` id list + a `state` map keyed by component
    `SaveId`), snapshotting live cells in `Save()` and re-reconciling them in `Load()`, so the
    ledger round-trips through a full save/load. Demo: a persistent "Waystone Relic" pickup
    (`HealthPotion`) authored into `scenes/regions/ember_crown/waystone.tscn`
    (`PersistentId = "ember_crown.waystone.relic"`, mirrors `ItemPickupFactory`'s node shape) — take
    it, leave the cell, return → it stays gone, and `_removed` survives save/load. Build + 85 tests
    + `--validate` + clean boot green. (The interactive pick-up→leave→return and save/load
    round-trip is the maintainer's at-keyboard check — the Godot MCP can't inject New Game / movement
    / `E`; logic reviewed against the Godot 4.7 C# API.)

- [x] **25E — World map data + screen** `[F]` ✅
  - **Goal:** a data-driven map.
  - **Tasks:** build a map screen from region metadata + discovered POIs (a
    `MapMarker` data list), rendered through `UiTheme`. Fog/undiscovered regions
    hidden until visited. `ISaveable` discovery state.
  - **Done when:** the map shows visited regions/POIs and persists discovery.
  - **Done:** new `src/World/MapService.cs` — a `Node`/`ISaveable` (ServiceLocator + SaveManager
    registered, `SaveId "map"`) that tracks discovery as two id sets: regions (revealed on entry —
    the bootstrap calls `DiscoverRegion` for the starting region in `BuildWorld` and for the
    destination on each 25C transition) and POIs (revealed when a cell first streams in — it
    subscribes to `RegionCellLoadedEvent`, which also reveals the owning region). Marker geometry is
    re-resolved from `RegionDatabase` at read time (region pos = `SpawnPoint`, POI pos = cell
    `Center`), so only the id sets persist; a `Revision` counter signals the UI to rebuild. New
    `MapMarker` record `(Id, Label, X, Z)` is the plot datum. New `src/UI/MapScreen.cs` — a non-modal
    overlay toggled with a new `M` input (`GameInput.Map`), like the journal: a `UiTheme` panel with
    a `MapView : Control` that `_Draw`s discovered regions (gold discs), POIs (dim dots) and the
    player (blue marker) fitted to the rect (north = −Z up; pure shapes, no font dep), plus a name
    legend; undiscovered regions are simply not drawn (fog). Strings (`map.title`, `map.empty`) go
    through `Loc` (catalogue now 61). Build + 85 tests + `--validate` + clean boot (61 strings) green.
    (Opening the map with `M` and watching discovery fill in / persist across save-load is the
    maintainer's at-keyboard check — the MCP can't inject New Game / `M`; logic reviewed against the
    Godot 4.7 C# API.)

- [x] **25F — HUD compass + quest markers** `[F]` ✅
  - **Goal:** on-screen wayfinding.
  - **Tasks:** add a compass strip to `GameHud` showing cardinal headings, nearby
    discovered POIs, and the active quest objective marker (read the quest log).
    Through `UiTheme`/`GameHud`.
  - **Done when:** the compass tracks heading and points at the active objective.
  - **Done:** new `src/UI/CompassStrip.cs` — a self-drawn `Control` owned by `GameHud`
    (built center-top in `_Ready`, fed the player via `SetPlayer`). Each frame it reads the
    player's facing (`Body` forward = `-GlobalBasis.Z`), then `_Draw`s a ±90°-FOV strip:
    cardinal letters (N highlighted), dim ticks for every discovered POI from 25E's
    `MapService.PoiMarkers()` (reached via `ServiceLocator`), and a bright marker for the active
    quest objective. The pure heading/strip arithmetic is `src/UI/CompassMath.cs` (wrap, heading,
    bearing, relative-angle, strip-offset, FOV cull), pinned by 6 new `CompassMathTests`
    (convention: North = `-Z`, angle clockwise to `+X`). The objective is resolved by a new
    `src/Quests/ObjectiveLocator.cs` *per type* — Kill → nearest live enemy whose `TemplateId`
    matches (enemies join an `objective.enemy` group in `EnemyFactory`), Collect → nearest world
    pickup whose item id matches (pickups join `objective.pickup` in `ItemPickupFactory`; a new
    `ItemPickupComponent.ItemId` exposes it); the `switch` is the seam for future Talk/Reach types.
    Resolution is throttled (~0.4 s, cached) — a `ponytail:` note marks the linear group scan as the
    ceiling. Cardinal letters go through the `Loc` layer (`hud.compass.*` keys in `strings.csv`, +8).
    Build + **91 tests** (was 85) + `--validate` (exit 0) green; **ran the game in-engine** — entered
    Playing with the HUD/compass live, the goblin Kill quest active and the waystone POI streaming
    in/out as the player moved, with **no compass errors** (only pre-existing save-`PersistentId`
    warnings + an unrelated WASAPI audio device error). The visual heading/marker confirmation — N
    where expected (flip the `-Z` knob if reversed), the POI tick and goblin marker tracking — is the
    maintainer's at-keyboard check; the draw + resolve paths ran live without throwing.

- [x] **25G — Fast-travel graph** `[F]` ✅
  - **Goal:** travel between discovered nodes.
  - **Tasks:** add discoverable travel nodes (interactables that register on the
    map), a fast-travel action from the map screen (gated by discovery), and
    arrival that respects clock/weather. Reuse the hard-transition load path (25C).
  - **Done when:** discovering and selecting a travel node moves the player there
    via a clean load; discovery + node list persist.
  - **Done:** new `src/World/FastTravelService.cs` — a `Node`/`ISaveable` (`SaveId
    "fasttravel"`, ServiceLocator + SaveManager registered, built next to `MapService`) tracking the
    set of attuned travel nodes (id + label + region + landing position), with a `Revision` counter
    for the UI; the full node is persisted (it carries its own position, not a database lookup), so the
    network round-trips save/load. A `TravelNodeComponent` (`src/World/TravelNodeComponent.cs`, an
    `InteractableComponent`, mirrors `RegionTransitionComponent`) is the world interactable: on `E` it
    `Discover`s itself (records its world position) and is revealed on the map. The map screen
    (`src/UI/MapScreen.cs`) gained a **FAST TRAVEL** section listing a button per attuned node, and is
    now **modal** (frees the mouse + suspends player control via `UiState.MenuOpen`, mirroring the
    inventory) so the buttons are clickable; a button publishes a new `FastTravelRequestedEvent` and
    closes the map. The bootstrap's 25C handler was refactored into a shared
    `PerformRegionLoad(destination, landing, message)` — the neighbour-portal path passes the region
    `SpawnPoint`, the new `OnFastTravelRequested` passes the node's position and allows same-region
    jumps; the streamer only swaps when the region actually changes, and the world clock/weather are
    left untouched so arrival respects current time/weather. A `travel <list|goto <id>>` dev command
    (mirrors `region`) drives jumps from F1 — the runnable check. A demo waystone (the
    `travel.ember_crown.waystone` node + a cylinder collider) is authored into
    `scenes/regions/ember_crown/waystone.tscn`. Build + **91 tests** + `--validate` (exit 0) green;
    **ran the game** — the waystone cell streamed in with the new node + collider, and the refactored
    portal path still travelled EmberCrown ⇄ Frostfang both ways, all with no new errors (the
    `fasttravel` save key is recognized; only the pre-existing `PersistentId`/orphan save warnings
    remain). The interactive attune → open map → click → warp + save/load-persistence run is the
    maintainer's at-keyboard check (the Godot MCP can't inject `E`/`M`/a mouse click).

---
