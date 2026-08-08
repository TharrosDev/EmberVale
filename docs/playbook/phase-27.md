## Phase 27 — First Playable Region: Ember Crown `[C/P]`

> Author **one real region** end-to-end to prove the pipeline produces
> ship-quality space. Mostly content + first-pass art, on top of streaming (25).

- [x] **27A — Ember Crown layout greybox + region/cell setup** `[C/P]` ✅
  - **Goal:** the spatial shell, streamed.
  - **Tasks:** lay out a walkable Ember Crown slice as `RegionResource` + sub-cell
    scenes (town hub footprint, surrounding wilds), navmesh baked, transitions to
    neighbours stubbed. Greybox geometry only.
  - **Done when:** you can walk the whole region with streaming + navmesh working.
  - **Done:** Ember Crown is now three streamed greybox cells — `town_hub.tscn` (a 60×60
    plaza floor + four greybox buildings, with the 25G waystone/travel node + the 25D
    persistent relic folded in), `wilds_north.tscn` (the old ruin wall + rocks) and
    `wilds_west.tscn` (rocks) — replacing the two old stub cells (`waystone.tscn`/
    `north_ruin.tscn`, deleted). `EmberCrown.tres` indexes all three with overlapping
    `LoadRadius`/`Center`s so the floors adjoin and their navmesh patches edge-connect;
    the Frostfang neighbour link (25C) gives the stubbed realm-to-realm transition. **The
    navmesh is real and the AI paths on it:** a new full nav layer — every cell wraps its
    geometry in a `NavigationRegion3D` baked at stream-in by a new `CellNavBaker`
    (`src/World/`), sourcing **static colliders** (not visual meshes — the engine flags a
    GPU→CPU readback stall for runtime mesh parsing, the 25.5B anti-hitch concern), so each
    cell gets a floor collider feeding the bake. Enemies gained a `NavigationAgent3D`
    (`EnemyFactory`, radius/height matched to the capsule) and `EnemyAIComponent.MoveTowards`
    now steers toward the agent's next path corner around obstacles, judging arrival by the
    **final** target via the pure `PathSteering.ShouldSteer` (4 new tests), and **falls back
    to straight-line** steering when no navmesh is under the actor (`IsTargetReachable()`
    false) — so the navmesh-less procedural sandbox is unchanged. Region/navmesh convention
    documented in ARCHITECTURE §2.6h-2 + CLAUDE §8 "A new region". Build clean + **251 tests**
    (+4 `PathSteeringTests`) + `--validate` exit 0; **ran each cell scene in-engine** — the
    runtime bake completes with **zero warnings** (collider-sourced, agent dims aligned to the
    0.25 voxel grid) and the boot path is clean (`errors: []`). Walking the whole region and
    watching goblins path around the greybox buildings is the maintainer's at-keyboard check
    (the Godot MCP can't inject New Game / movement); the bake + agent wiring ran live without
    errors and the steering rule is unit-pinned.

- [x] **27B — Town hub: vendors, inn, guild presence, crafting stations** `[C]` ✅
  - **Goal:** a living hub.
  - **Tasks:** place vendor NPCs (stub shops until Phase 38), an inn, a guild
    presence marker, and `CraftingStationFactory` stations (forge/workbench/
    alchemy). Use existing factories/components; author the NPC `Entity`s with
    colliders + interactables.
  - **Done when:** the hub has functioning crafting stations and interactable NPCs;
    `validate` green.
  - **Done:** the hub population is now **authored as nodes inside `town_hub.tscn`**
    (the 27A self-contained-cell convention — no bootstrap code), all children of the
    cell root (not the `NavigationRegion3D`, so their colliders don't carve the navmesh,
    matching the waystone/relic). Added: the **Village Elder** (migrated out of the
    bootstrap's old `SpawnQuestGiver` — same `Entity` + capsule + collider +
    `FactionComponent`/`DialogueComponent`/`ScheduleComponent` shape, `dialogue.elder` +
    `schedule.elder`); **three vendor NPCs** (general goods / smith / apothecary) + an
    **innkeeper** (inn = the NE building) + an **Adventurers' Guild notice** banner, each
    an interactable `DialogueComponent` pointed at a new shared stub `data/dialogue/
    VendorStub.tres` (`dialogue.vendor_stub`, + `GameIds.Dialogues.VendorStub`) with a
    per-NPC `SpeakerName` override (one dialogue resource, distinct names/tints); and the
    **three crafting stations** (Forge/Workbench/Alchemy as `CraftingStationComponent`
    nodes — the factory's component, authored in-scene). The now-duplicate code-built
    crafting yard and quest-giver were removed from `GameBootstrap` (`SpawnCraftingStations`
    + `SpawnQuestGiver` and their `BuildWorld` calls; the `SpawnRegionPortals` cref updated).
    Build clean + **251 tests** + `--validate` exit 0 (DialogueDatabase now 2 conversations,
    all `DialogueComponent` refs resolve). **Ran in-engine** — the cell bakes its navmesh with
    zero warnings, and on New Game the hub streamed into the live world (`loaded cell
    'ember_crown.town_hub'`) with the NPCs + stations and **no errors**. Vendor shops are
    dialogue stubs until Phase 38; NPC schedules/pathing and routine alignment to the new
    buildings are 27C. (Talking to an NPC and pressing `E` at a station is the maintainer's
    at-keyboard check — the MCP can't inject `E`; the dialogue/station systems are unchanged
    and proven, and the wiring loaded live without errors.)

- [x] **27C — Scheduled NPC population** `[C]` ✅
  - **Goal:** the hub feels inhabited.
  - **Tasks:** author `ScheduleResource`s and attach `ScheduleComponent`s to hub
    NPCs (home → work → tavern → sleep routines) per docs/RECIPES.md "new NPC
    routine." Give 3–5 named NPCs full day routines.
  - **Done when:** NPCs walk believable daily routines off the `WorldClock`.
  - **Done:** all **five** hub NPCs now run full day routines off the `WorldClock` (work by day
    → tavern at 18h → home to sleep at night; pre-dawn wraps to the night block). Authored four new
    `ScheduleResource`s — `data/schedules/{VendorGoods,VendorSmith,VendorAlch,Innkeeper}.tres`
    (`schedule.vendor_goods` / `_smith` / `_alch` / `innkeeper`, + `GameIds.Schedules` constants) —
    and re-aimed the Elder's existing `schedule.elder` blocks (old-sandbox coords) at the new hub
    features (plaza/forge/square/tavern/SW-house, in world coords = cell `Center (0,0,-10)` + local).
    Each routine is attached by adding a `ScheduleComponent {ScheduleId}` node to the NPC in
    `town_hub.tscn` (the Elder already had one). **No code** — `ScheduleComponent` already drives a
    plain `Entity` kinematically off the clock (the 27B-proven path); this is pure data + component
    wiring. Build clean + **251 tests** + `--validate` exit 0 (`ScheduleDatabase` 1→**5**, all
    `ScheduleComponent` refs resolve); in-engine the cell bakes navmesh with zero warnings and the
    hub boots clean (`errors: []`). NPCs move kinematically (straight-line, clipping greybox walls —
    navmesh-pathed NPC movement is a later refinement, not 27C). Watching the routines over a day via
    F1 `time <hour>` (8 = stalls, 18 = tavern, 22 = houses) is the maintainer's at-keyboard check —
    the MCP can't drive time/movement; the schedule data + wiring loaded live without errors.

- [x] **27D — Wilds: encounters, POIs, loot** `[C]` ✅
  - **Goal:** the explorable surround.
  - **Tasks:** author `EncounterResource`s for the wilds (goblins/wildlife), place
    POIs (a ruin, a cache, a mini-camp) with `LootComponent` droppers and
    interactables. Day-phase-appropriate encounter flags. Pure content.
  - **Done when:** the wilds spawn encounters and reward exploration; loot drops
    and persists.
  - **Done:** pure content, no code. **Encounters:** two new `EncounterResource`s —
    `data/encounters/GoblinAmbush.tres` (dusk/night, 2–3) and `GoblinForagers.tres` (dawn/day, 1) —
    join the existing three (`EncounterDatabase` 3→5); the `EncounterDirector` spawns them around the
    player by day-phase + weight, and the goblins drop loot via their existing `LootComponent` +
    `GoblinLoot`. **POIs** authored into the two wilds cells as greybox props (children of the cell
    root unless they're obstacles, then under `Nav` to carve the navmesh like the rocks) + persistent
    `ItemPickupComponent` pickups (the proven town_hub-relic pattern: `Entity` + unique `PersistentId`
    + mesh + collider + pickup, reusing existing items): `wilds_north` got an *old-watchtower ruin*
    (fallen-pillar prop + IronIngot/RubyGem) and an *abandoned cache* (crate + HealthPotion×2/IronOre×3);
    `wilds_west` got a *goblin mini-camp* (campfire + two tents + GoblinHide×2/GoldCoin×15/HealingHerb×2).
    Each pickup carries a unique `PersistentId` so the 25D `CellPersistenceDirector` keeps it looted
    across unload/reload + save/load. Build clean + **251 tests** + `--validate` exit 0 (encounters'
    `EnemyTemplateId` + every pickup `Item` resolve); in-engine both wilds cells instance with the POIs,
    bake navmesh with zero warnings, and the game boots clean (`errors: []`; only a pre-existing WASAPI
    audio warning). **Known limitations (flagged, deferred):** encounters aren't spatially gated, so
    they can also spawn near the hub (a region/safe-zone gate is a future [F]); "wildlife" needs a new
    enemy archetype (code) so 27D uses goblins; POI loot is pickups (no openable-container UI yet).
    Walking the wilds to fight encounters + collect/persist POI loot is the maintainer's at-keyboard
    check (the MCP can't drive movement/`E`/combat); the data + wiring loaded live without errors.
  - **27D follow-up (player request, code):** two of the deferred items above are now closed.
    (1) **Hub safe zone** — `RegionResource` gained `SafeZoneCenter`/`SafeZoneRadius` (EmberCrown:
    centre `(0,0,-10)`, r 34, covering the town); a static `SafeZones` (`src/World/SafeZones.cs`) holds
    the active region's bubble, populated by `GameBootstrap` at world build + each region transition.
    The `EncounterDirector` and hostile (non-cache) `WorldEventDirector` events now reject ring points
    inside it (`SafeZones.TryRingPointOutside`), so ambient enemies/raids spawn only in the wilds; the
    static goblin camp moved from the town edge `(0,0,-8)` out to `(0,0,-58)` (wilds_north). Loot caches
    and **scripted** spawns (quest/event enemies via `EnemyFactory`) bypass the gate, so a mission
    thief/assassin in town still works. (2) **Hold-`E` auto-pickup** — `PlayerController` sweeps a 3.5 m
    sphere for `ItemPickupComponent`s while `E` is held (every 0.12 s, non-just-pressed frames), so a
    pile of drops is vacuumed instead of tapped one-by-one (player-confirmed working). Build clean + 251
    tests + `--validate` 0; the maintainer's automated continue scenario ran clean (goblin fought in
    wilds_north, loot collected, saved, `errors: []`).

- [x] **27E — Starter quest chain in the Ember Crown** `[C]` ✅
  - **Goal:** a real questline to play.
  - **Tasks:** author a 3–4 quest chain (Kill/Collect for now; richer types come in
    Phase 41) with `QuestGiverComponent`/`DialogueComponent` givers, prerequisite
    chaining, and rewards. All dialogue/quest strings via `Loc`.
  - **Done when:** the chain is startable, advanceable, and completable end-to-end;
    `validate-all` green.
  - **Done:** a 4-quest prereq-chained arc — **The Warband** — each given by a town NPC through a full
    branching dialogue graph (the user chose full-dialogue-per-NPC + Loc-now). (1) Guild Notice board
    `quest.warband.bounty` (kill 3 goblins) → (2) Bryn the Smith `quest.warband.forge` (collect 3 iron
    ore) → (3) Mirela the Apothecary `quest.warband.remedies` (collect 4 healing herb) → (4) Village
    Elder `quest.warband.heart` (kill 5 goblins → Steel Sword). Chaining is automatic: each giver's
    "offer" choice uses `DialogueCondition.QuestAvailable` → `QuestLogComponent.CanStart`, which gates on
    the prior quest's completion; `Effect=StartQuest` on accept; objectives advance + rewards apply
    automatically (no turn-in). New dialogues `data/dialogue/{GuildBoard,Smith,Apothecary}.tres` +
    finale branch grafted into `Elder.tres`; the Smith/Apothecary/Guild NPCs in `town_hub.tscn`
    repointed off the vendor stub. **Loc routing** added: the quest/dialogue *content* render sites
    (GameHud tracker, QuestLogPanel, DialoguePanel speaker/text/choice, QuestGiver prompt) now wrap in
    `Loc.T` — `Loc.T` passes unknown keys through, so existing literal content is unaffected while the
    ~69 new keys in `data/locale/strings.csv` resolve (catalogue 139→208). Build clean + 251 tests +
    `--validate` 0 (`QuestDatabase` 2→6, `DialogueDatabase` 2→5, every `StartQuest`/`Condition`/prereq
    arg resolves); boots clean (`errors: []`). Walking the chain (accept → see the next giver's offer
    stay hidden until done → complete to the Elder's finale, text shown localized) is the maintainer's
    at-keyboard check (MCP can't drive `E`/movement).

- [x] **27F — First-pass ambience, lighting & audio bed** `[P]` ✅
  - **Goal:** the quality bar, first pass.
  - **Tasks:** set day/night lighting mood, weather bias, and a first-pass ambience
    bed (placeholder audio is fine pre-Phase 31). Establish the dying-world palette
    in this region as the reference for all later regions.
  - **Done when:** the region reads as a *place* with mood, not greybox; documented
    as the bar.
  - **Done:** established the **dying-world palette** (user chose Moderate intensity) as the shared
    reference bar — the whole game is the dying world, so one base palette, not per-region data
    (per-realm variation is Phase 44). `GameBootstrap.BuildEnvironment` now sets an ACES tonemap +
    muted exposure, an overcast-leaning desaturated `ProceduralSkyMaterial` (no more bright blue),
    warm-grey ambient fill, soft glow, and softer sun shadows. `SkyController` gained a labelled
    *Dying-world palette* constants block: ashier dawn/dusk + muted warm-grey noon sun, a dimmer noon
    energy ceiling (1.15→0.9), and a **haze floor** (`max(weatherFog, 0.006)` + ash-tinted fog) so the
    air is never perfectly clear even in clear weather. Weather `FogColor`s re-tinted cool-blue→ashen
    warm-grey across all 5 states; the heartland default biased `weather.clear`→`weather.cloudy` (and
    Clear's weight dropped) so it leans overcast. Documented as the bar in `ARCHITECTURE.md` §2.6h.
    **Audio bed deferred to Phase 31** (no audio system/asset exists; not in 27F's done-when — no
    half-built scaffolding). Build clean + 251 tests + `--validate` 0 (`weather.cloudy` resolves);
    boots clean (`errors: []`). **The visual judgement is the maintainer's at-keyboard check** — the
    MCP can't screenshot the running game; the palette constants are left as labelled knobs to nudge
    if Moderate lands too strong/weak (scrub with F1 `time 6/12/18/22` + `weather …`).
  - **→ Phase 27 (First Real Region — Ember Crown) complete.** Next: Phase 28 (first boss).

---
