# Embervale — Session Playbook (the day-by-day breakdown)

> **What this is.** [`PRODUCTION_ROADMAP.md`](PRODUCTION_ROADMAP.md) lays out the
> *phases* (22–66) and the five gates. Each of those phases is far too large to
> finish in a single Claude Code session — they were written as milestones, not
> work units. **This document breaks every phase into lettered sub-phases
> (22A, 22B, 22C …)**, each one sized to fit comfortably inside a *single
> session/context window* and to leave the repo **buildable and playable at the
> end** (CLAUDE.md §1).
>
> Work it **top to bottom**. Open a session, pick the next unchecked sub-phase,
> do *only* that sub-phase, satisfy its **Done when** bar, commit, and stop.
> One sub-phase ≈ one session ≈ one small PR (or one commit on the phase's PR).

---

## 0. How to use this playbook

### 0.1 The session loop (do this every time)

1. **Pick** the next unchecked `[ ]` sub-phase in order. Do not skip ahead — the
   ordering encodes dependencies.
2. **Read** the sub-phase's *Goal*, *Tasks*, and *Done when*. Read the linked
   CLAUDE.md §8 recipe and the relevant `docs/ARCHITECTURE.md` section **before**
   touching code.
3. **Do** only that sub-phase. If you discover it's two sessions of work, split
   it: do the first half, append a new lettered sub-phase for the remainder, and
   stop.
4. **Verify** the *Done when* bar. Run `validate` (and any new validators) in the
   dev console mentally/against the API — this container can't build (CLAUDE.md
   §2), so "verified" means *reviewed against the Godot 4.7 C# API*, never "ran
   it."
5. **Persist** — if the sub-phase added stateful data, it implements `ISaveable`
   and round-trips *before* you call it done (CLAUDE.md §1).
6. **Commit** with a clear message; tick the box here and update the phase's row
   in `PRODUCTION_ROADMAP.md` §11 if the whole phase closed. Push; open/append the
   draft PR (CLAUDE.md §9).
7. **Stop.** Don't roll two sub-phases into one session unless the second is
   trivially small (a doc tweak, a `.tres` you already have all the data for).

### 0.2 Sub-phase sizing rules (what fits in one session)

A sub-phase is correctly sized when it is **one** of:

- **One new component/service** + its events + its save hook + wiring into *one*
  factory/scene. (Not three components.)
- **One new resource type** (`XxxResource` + its `XxxDatabase` + auto-index) with
  *one* authored example `.tres` and the recipe doc entry.
- **A batch of pure content** (`.tres` only, no code) — e.g. "author 6 enemy
  `.tres` against the existing factory" — capped so the batch is reviewable.
- **One UI panel/widget** built through `UiTheme`.
- **One integration/QA pass** over a bounded slice (one region, one quest line).

If a task needs *new code in three+ systems at once*, it is a phase, not a
sub-phase — split it.

### 0.3 Tags (carried from the roadmap)

**[F]** new engine/feature code · **[C]** content authoring (mostly `.tres`) ·
**[P]** production craft (art/audio/UX/perf/ship). Most sub-phases blend; the tag
marks the centre of gravity. **[C]** sub-phases are the cheapest sessions (data,
no code) — batch them when momentum is good.

### 0.4 Legend

- `[ ]` not started · `[~]` in progress (split mid-session) · `[x]` done.
- **DoD** = the phase-level Definition of Done in `PRODUCTION_ROADMAP.md` §0.3.
  Every sub-phase inherits it; the **Done when** line is the sub-phase's *extra*
  bar on top of "it builds, it's playable, it saves, `validate` is green."

---

# Stage A — Pre-production & First Playable (→ G0)

---

## Phase 22 — Production Bible & Content Pipeline `[F/P]`

> Make authoring fast, safe, and consistent *before* there's a lot of content.
> Mostly tooling and docs — low engine risk, high leverage.

- [x] **22A — `docs/DESIGN.md`: combat & moment-to-moment pillars** `[P]`
  - **Goal:** pin the design the LORE leaves open, starting with combat.
  - **Tasks:** create `docs/DESIGN.md`; write the *combat pillars* (Skyrim breadth
    × Elden Ring weight, "no button mashing"), the core moment-to-moment loop
    (explore → fight → loot → grow), and the input/feel intent that Phase 29 will
    answer to. Cross-link CLAUDE.md combat sections.
  - **Done when:** `docs/DESIGN.md` exists with the Combat + Core Loop sections
    filled; no code touched.

- [x] **22B — `docs/DESIGN.md`: progression, difficulty & economy intent** `[P]`
  - **Goal:** finish the design bible's remaining pillars.
  - **Tasks:** add *Progression* (no class lock, player-authored builds, perk
    intent), *Difficulty philosophy* (easy to learn / hard to master, options not
    class locks), *Corruption fantasy* (sets up Phase 23), and *Economy intent*
    (gold sinks, scarcity in a dying world).
  - **Done when:** all five pillar sections complete; this is the document
    balancers/authors answer to.

- [x] **22C — ID & naming registry doc + audit** `[F/P]`
  - **Goal:** one documented namespace scheme for every content domain.
  - **Tasks:** locate the existing central id constants (PR #31). Write
    `docs/IDS.md` (or a section in DESIGN) documenting the scheme for `item.*`,
    `quest.*`, `npc.*`, `region.*`, `boss.*`, `faction.*`, `relic.*`, `dialogue.*`,
    `flag.*`, `spell.*`, `recipe.*`, etc. Audit current `data/**.tres` ids for
    conformance; list violators.
  - **Done when:** the scheme is documented and current ids are audited against it
    (a short conformance list in the doc).

- [x] **22D — `ContentValidator`: structural rules (no dead refs → well-formed)** `[F]`
  - **Goal:** grow validation from "references resolve" to "content is well-formed."
  - **Tasks:** in the `ContentValidator`, add checks: no duplicate ids per domain;
    loot tables non-empty; every quest objective `TargetId` resolves; every
    dialogue `Goto`/`StartNodeId` resolves. Read `src/Debugging/` for the existing
    validator shape first.
  - **Done when:** new rules implemented and surfaced; running `validate` reports
    the new classes of error.

- [x] **22E — `ContentValidator`: graph reachability (quests + dialogue)** `[F]`
  - **Goal:** catch unreachable content, the subtle content-scale bug.
  - **Tasks:** add reachability analysis — dialogue graphs have no orphan nodes
    and no dead ends that aren't intentional terminals; quest objective chains are
    completable; prerequisite quest chains don't cycle. Add a `validate-all`
    console command that runs the full battery.
  - **Done when:** `validate-all` exists and flags an intentionally-broken test
    graph; no false positives on current content.

- [ ] **22F — Headless validation entry point** `[F]`
  - **Goal:** let the maintainer run validation without launching into gameplay.
  - **Tasks:** add a headless/`--validate` path (a Godot `--headless` script or a
    `GameState` boot branch) that loads the databases, runs `validate-all`, prints
    a report, and exits non-zero on failure. Document the invocation in CLAUDE.md
    §3 and the README.
  - **Done when:** a documented one-command content check exists (reviewed against
    the API; the human runs it).

- [ ] **22G — `data/_templates/` canonical starting `.tres`** `[P]`
  - **Goal:** copy-paste starting points for every content type.
  - **Tasks:** create `data/_templates/` with one minimal, commented `.tres`
    exemplar per content domain already in CLAUDE.md §8 (item, equippable, affix,
    loot table, perk, quest, dialogue, schedule, weather, encounter, world event,
    recipe, spell, status effect, faction). Each is the recipe's "canonical
    starting point."
  - **Done when:** every §8 domain has a template; `validate` stays green
    (templates either valid or excluded by an `_` convention).

- [ ] **22H — Telemetry/analytics spine (dev-only)** `[F]`
  - **Goal:** lightweight event logging so balance/QA later have data.
  - **Tasks:** add `AnalyticsEvent : IGameEvent` and a dev-only `AnalyticsSink`
    that subscribes to the EventBus and logs to `user://analytics/` (deaths by
    location, quest start/complete, level-ups). Gated off by default in retail via
    a build/Settings flag. Implement `ISaveable` only if it must persist across
    sessions (it shouldn't — it's a log).
  - **Done when:** dev builds emit a structured analytics log; retail path is a
    no-op; documented in ARCHITECTURE.

---

## Phase 23 — The Corruption System `[F]`

> The LORE's **defining mechanic**. The single most important new system in the
> whole production roadmap; the slice and all narrative gate on it. Build the core
> first, then wire one consequence per session.

- [ ] **23A — `CorruptionComponent` core + events + save** `[F]`
  - **Goal:** the 0–100 meter and tier state, persistent.
  - **Tasks:** add `src/Corruption/CorruptionComponent.cs` (`EntityComponent`,
    `[GlobalClass]`, on the player). 0–100 value; `Add/Set` API; a `CorruptionTier`
    enum (Untainted → Touched → Marked → Ashbound → Embers) with thresholds.
    Fire `CorruptionChangedEvent` and `CorruptionTierChangedEvent` in a new
    `CorruptionEvents.cs`. Implement `ISaveable` (stable `SaveId`), register in
    `OnInitialize`, unregister in `OnTeardown`. Add to `PlayerFactory`.
  - **Done when:** corruption can be raised/queried in code, fires tier events at
    thresholds, and round-trips save/load. (CLAUDE.md §8 "new component" + "new
    persistent system" + "new event".)

- [ ] **23B — Corruption dev console + debug surface** `[F]`
  - **Goal:** make it testable before it has any visual.
  - **Tasks:** register a `corruption` console command (`get` / `set N` / `add N`
    / `tier`) per CLAUDE.md §8 "new dev-console command," resolving the player via
    `ServiceLocator`. Add a line to the F3 debug overlay showing value + tier.
  - **Done when:** the maintainer can drive corruption from `F1` and watch it on
    F3.

- [ ] **23C — Dialogue conditions/effects for corruption** `[F]`
  - **Goal:** let conversations gate and modify corruption.
  - **Tasks:** extend `DialogueEnums.cs` with `Condition` `CorruptionAtLeast` /
    `CorruptionBelow` and `Effect` `AddCorruption`. Wire evaluation in the dialogue
    session runner against `CorruptionComponent`. Author one test dialogue using
    each. (Extends CLAUDE.md §8 "new conversation"; read `src/Dialogue/` first.)
  - **Done when:** a conversation visibly branches on corruption and a choice can
    raise it; `validate` understands the new enum values.

- [ ] **23D — Corruption UI: character-screen gauge** `[F]`
  - **Goal:** the player can see their corruption.
  - **Tasks:** add a corruption gauge to the character screen via `UiTheme.Bar`
    (CLAUDE.md §8 "new UI panel"). Label the current tier. Rebuild from a dirty
    flag in `_Process`, never in a signal handler.
  - **Done when:** the gauge reflects live corruption + tier through `UiTheme`.

- [ ] **23E — Corruption HUD vignette at high tiers** `[F/P]`
  - **Goal:** ambient dread at Ashbound/Embers.
  - **Tasks:** add a subtle screen vignette/desaturation overlay in `GameHud` that
    fades in by tier (subscribe to `CorruptionTierChangedEvent`). Keep it through
    `UiTheme` palette; intensity is data-light and tweakable.
  - **Done when:** crossing into high tiers visibly shifts the screen; reverting
    lowers it.

- [ ] **23F — `CorruptionAppearanceController` (hook stub)** `[F]`
  - **Goal:** the seam the future model/VFX work plugs into.
  - **Tasks:** add a `CorruptionAppearanceController` on the player that, per tier,
    swaps a placeholder material/emissive (eye glow, ash-vein tint) on whatever
    player mesh exists now. Drive it off the tier event. Designed so Phase 30 can
    replace placeholders with real materials without changing the wiring.
  - **Done when:** each tier shows a *distinct* placeholder appearance change;
    documented as the hook for Phase 30.

- [ ] **23G — NPC reaction / global "dread" standing** `[F]`
  - **Goal:** the world fears a corrupted player.
  - **Tasks:** have `ReputationComponent`/faction AI read corruption as a global
    standing modifier ("dread") so high corruption nudges NPC hostility/dialogue.
    Reuse the existing reputation math; don't add a parallel system. (Read
    `src/Factions/`.)
  - **Done when:** raising corruption measurably shifts at least one faction's
    standing/AI reaction; round-trips through save.

- [ ] **23H — Corrupted ability gating + both-endings eligibility hook** `[F/C]`
  - **Goal:** corruption unlocks corrupted variants and feeds the endings dial.
  - **Tasks:** add a corruption-tier gate option to `SpellResource`/`PerkResource`
    consumption (author one corrupted spell + one corrupted perk `.tres` gated by
    tier — CLAUDE.md §8 recipes, no new system). Expose a
    `CorruptionComponent.EndingEligibility` read (Dawnfire vs Lord of Embers
    threshold) that Phase 49 will consume. Document the contract.
  - **Done when:** a tier-gated spell/perk is learnable only above its tier; an
    ending-eligibility value is queryable and saved.

---

## Phase 24 — Meta-Shell & Localization Spine `[F]`

> The title/menu/settings/save-slot shell the systems roadmap excluded, plus the
> i18n layer that must land *before* mass content authoring.

- [ ] **24A — `MainMenu` scene + `GameState.MainMenu` boot** `[F]`
  - **Goal:** the game boots to a menu, not straight into the sandbox.
  - **Tasks:** add a `MainMenu` scene (New Game / Continue / Load / Settings /
    Quit, built through `UiTheme`). Make `GameBootstrap`/`GameManager` boot into
    `GameState.MainMenu` and transition to `Playing` on New Game/Continue. Keep the
    sandbox reachable (New Game → existing bootstrap path).
  - **Done when:** launching shows the menu; New Game enters the world; Quit exits.
    No save logic yet (buttons can be stubbed/disabled).

- [ ] **24B — `SaveManager`: single-file → slot directories** `[F]`
  - **Goal:** multiple independent saves.
  - **Tasks:** refactor `SaveManager` from one file to `user://saves/<slot>/`.
    Add slot create/list/delete and a save *header* (region, level, playtime,
    corruption tier, timestamp). Keep `ISaveable` registration API unchanged.
    Read `src/Save/` first; preserve back-compat or write a one-time migration.
  - **Done when:** multiple slots coexist; F5/F9 still work against the active
    slot; headers populate.

- [ ] **24C — Save-slot UI (New/Load/Continue + metadata)** `[F]`
  - **Goal:** the player manages saves from the shell.
  - **Tasks:** build the slot-select panel (list slots with header metadata +
    screenshot thumbnail; New into empty slot; Load; Delete with confirm). Wire
    Continue = most-recent slot. Capture a screenshot on save for the thumbnail.
  - **Done when:** full new/continue/load/delete flow works from the menu through
    `UiTheme`, round-tripping real saves.

- [ ] **24D — Autosave + quicksave + manual cadence** `[F]`
  - **Goal:** robust save cadence on top of slots.
  - **Tasks:** add autosave triggers (region change, major quest beat, time
    interval) writing to a rotating autosave slot; keep quicksave (F5/F9) and
    manual save-from-pause. Guard against saving mid-cutscene/load.
  - **Done when:** autosave/quicksave/manual all target the slot system safely; no
    double-save races.

- [ ] **24E — `Settings` resource + `SettingsService`** `[F]`
  - **Goal:** persisted options applied at runtime.
  - **Tasks:** add a `Settings` resource (graphics, audio bus volumes, controls,
    gameplay, accessibility placeholders) persisted to `user://settings.tres` via
    a `SettingsService` (`ServiceLocator`-registered). Apply on boot.
  - **Done when:** settings persist across launches and apply on load; audio-bus
    fields are ready for Phase 31 to consume.

- [ ] **24F — Settings UI panel** `[F]`
  - **Goal:** the options menu.
  - **Tasks:** build the Settings panel (tabs/sections for Graphics/Audio/Controls/
    Gameplay/Accessibility) through `UiTheme`, reading/writing `SettingsService`.
    Reachable from both MainMenu and PauseMenu.
  - **Done when:** changing a setting applies live and persists; reachable from
    both shells.

- [ ] **24G — Localization spine: `Loc` facade + translation pipeline** `[F]`
  - **Goal:** every string goes through a key from here on.
  - **Tasks:** add a `Loc` static facade over Godot's `TranslationServer`; set up
    the `.po`/CSV pipeline and an `en` base catalogue. Document the rule in
    CLAUDE.md (§6 conventions) and PRODUCTION_ROADMAP DoD #6: **no hard-coded
    player-facing strings after this lands.**
  - **Done when:** `Loc.T("key")` resolves from the catalogue; the convention is
    documented.

- [ ] **24H — Retrofit shell strings through `Loc`** `[F]`
  - **Goal:** prove the layer end-to-end on real UI.
  - **Tasks:** route all MainMenu/Settings/PauseMenu/save-slot strings through
    `Loc` keys; add them to the `en` catalogue. This is the template every later
    UI follows.
  - **Done when:** the shell has zero hard-coded display strings; switching the
    catalogue language visibly changes them.

---

## Phase 25 — Region Streaming & World Map `[F]`

> Replace the single flat sandbox with streamed authored regions, a map, and
> fast travel — before authoring four realms.

- [ ] **25A — `RegionResource` + region scene convention** `[F]`
  - **Goal:** regions are authorable data + scenes.
  - **Tasks:** add `RegionResource` (`.tres`: id, display name, realm, sub-cell
    list, bounds, default weather/day-phase bias, neighbour links) + a
    `RegionDatabase` auto-index. Define the region/sub-cell scene naming + placement
    convention (world-partition discipline) in a doc. Author one `RegionResource`
    for the current sandbox.
  - **Done when:** the sandbox is described by a `RegionResource`; the convention
    is documented for Phases 27/44.

- [ ] **25B — `RegionStreamer`: load/unload by distance** `[F]`
  - **Goal:** stream sub-cells around the player with a budget.
  - **Tasks:** add `RegionStreamer` that loads/unloads sub-cell scenes by distance
    with hysteresis and a per-frame instancing budget (don't hitch). Reuse the
    Phase 19 pooling/throttle discipline. Keep the current sandbox working as a
    single always-loaded cell.
  - **Done when:** moving across cell boundaries loads/unloads without a visible
    hitch (reviewed against the API); the sandbox still boots.

- [ ] **25C — Hard transitions + loading screen (realm-to-realm)** `[F]`
  - **Goal:** discrete loads between realms.
  - **Tasks:** add a loading-screen state (`GameState.Loading` already exists) for
    hard transitions; tear down the old region, load the new, restore the player.
    Trigger via a transition volume/door interactable.
  - **Done when:** stepping through a transition runs a clean load and spawns the
    player correctly in the new region.

- [ ] **25D — Persistent actors across streaming (PersistentSpawnDirector)** `[F]`
  - **Goal:** the world remembers itself across load/unload.
  - **Tasks:** ensure streamed-in actors with `PersistentId` restore their state
    via the existing `PersistentSpawnDirector` (PR #29) when their cell reloads
    (dead enemies stay dead, looted chests stay looted). Read `src/Save/` first.
  - **Done when:** kill/loot an actor, leave the cell, return — state persists;
    round-trips through a full save/load too.

- [ ] **25E — World map data + screen** `[F]`
  - **Goal:** a data-driven map.
  - **Tasks:** build a map screen from region metadata + discovered POIs (a
    `MapMarker` data list), rendered through `UiTheme`. Fog/undiscovered regions
    hidden until visited. `ISaveable` discovery state.
  - **Done when:** the map shows visited regions/POIs and persists discovery.

- [ ] **25F — HUD compass + quest markers** `[F]`
  - **Goal:** on-screen wayfinding.
  - **Tasks:** add a compass strip to `GameHud` showing cardinal headings, nearby
    discovered POIs, and the active quest objective marker (read the quest log).
    Through `UiTheme`/`GameHud`.
  - **Done when:** the compass tracks heading and points at the active objective.

- [ ] **25G — Fast-travel graph** `[F]`
  - **Goal:** travel between discovered nodes.
  - **Tasks:** add discoverable travel nodes (interactables that register on the
    map), a fast-travel action from the map screen (gated by discovery), and
    arrival that respects clock/weather. Reuse the hard-transition load path (25C).
  - **Done when:** discovering and selecting a travel node moves the player there
    via a clean load; discovery + node list persist.

---

## Phase 26 — Playable Races & Character Creation `[F]`

> Six LORE races as data-driven trait sets + a creator that writes them into the
> player at spawn.

- [ ] **26A — `RaceResource` + `RaceDatabase`** `[F]`
  - **Goal:** races are data.
  - **Tasks:** add `RaceResource` (`.tres`: id, name, `AttributeSet` deltas, innate
    perk/ability ids, starting reputation tweaks, appearance option ids) +
    auto-indexed `RaceDatabase` (mirror `ItemDatabase`). No new inheritance.
  - **Done when:** a `RaceResource` loads and indexes; the schema covers all six
    LORE races' needs.

- [ ] **26B — Author the six race `.tres`** `[C]`
  - **Goal:** Human, Valari, Grondar, Sylthari, Draekyn, Umbral exist as data.
  - **Tasks:** author all six `data/races/*.tres` per LORE traits (Valari magic
    affinity, Grondar strength/endurance, Sylthari wildlife communion, Draekyn
    dragon ability seed, Umbral stealth, Human flexible). Reference existing
    perks/stats; create any small new perk `.tres` they need (CLAUDE.md §8 "new
    perk"). Pure content.
  - **Done when:** six valid race `.tres`; `validate` green; traits reference real
    ids.

- [ ] **26C — `PlayerFactory` consumes a creation profile** `[F]`
  - **Goal:** the chosen race actually shapes the player.
  - **Tasks:** add a `CharacterProfile` (race id, name, appearance, background) and
    have `PlayerFactory` apply race deltas as `StatModifier`s, seed innate perks,
    and apply reputation tweaks at spawn (CLAUDE.md §6 factory rules — set props
    before `AddChild`). Persist the profile in the save header.
  - **Done when:** spawning with different races yields different starting stats/
    perks/standing; the profile saves/loads.

- [ ] **26D — `CharacterCreator` screen** `[F]`
  - **Goal:** the new-game creation flow.
  - **Tasks:** build the creator (race pick with trait summary, appearance options,
    name, optional background) through `UiTheme`, fed by `RaceDatabase`, writing a
    `CharacterProfile`. Hook it into MainMenu → New Game → world spawn. All strings
    via `Loc`.
  - **Done when:** New Game → create a character → spawn into the world with the
    chosen race applied; flow round-trips through the save header.

---

## Phase 27 — First Playable Region: Ember Crown `[C/P]`

> Author **one real region** end-to-end to prove the pipeline produces
> ship-quality space. Mostly content + first-pass art, on top of streaming (25).

- [ ] **27A — Ember Crown layout greybox + region/cell setup** `[C/P]`
  - **Goal:** the spatial shell, streamed.
  - **Tasks:** lay out a walkable Ember Crown slice as `RegionResource` + sub-cell
    scenes (town hub footprint, surrounding wilds), navmesh baked, transitions to
    neighbours stubbed. Greybox geometry only.
  - **Done when:** you can walk the whole region with streaming + navmesh working.

- [ ] **27B — Town hub: vendors, inn, guild presence, crafting stations** `[C]`
  - **Goal:** a living hub.
  - **Tasks:** place vendor NPCs (stub shops until Phase 38), an inn, a guild
    presence marker, and `CraftingStationFactory` stations (forge/workbench/
    alchemy). Use existing factories/components; author the NPC `Entity`s with
    colliders + interactables.
  - **Done when:** the hub has functioning crafting stations and interactable NPCs;
    `validate` green.

- [ ] **27C — Scheduled NPC population** `[C]`
  - **Goal:** the hub feels inhabited.
  - **Tasks:** author `ScheduleResource`s and attach `ScheduleComponent`s to hub
    NPCs (home → work → tavern → sleep routines) per CLAUDE.md §8 "new NPC
    routine." Give 3–5 named NPCs full day routines.
  - **Done when:** NPCs walk believable daily routines off the `WorldClock`.

- [ ] **27D — Wilds: encounters, POIs, loot** `[C]`
  - **Goal:** the explorable surround.
  - **Tasks:** author `EncounterResource`s for the wilds (goblins/wildlife), place
    POIs (a ruin, a cache, a mini-camp) with `LootComponent` droppers and
    interactables. Day-phase-appropriate encounter flags. Pure content.
  - **Done when:** the wilds spawn encounters and reward exploration; loot drops
    and persists.

- [ ] **27E — Starter quest chain in the Ember Crown** `[C]`
  - **Goal:** a real questline to play.
  - **Tasks:** author a 3–4 quest chain (Kill/Collect for now; richer types come in
    Phase 41) with `QuestGiverComponent`/`DialogueComponent` givers, prerequisite
    chaining, and rewards. All dialogue/quest strings via `Loc`.
  - **Done when:** the chain is startable, advanceable, and completable end-to-end;
    `validate-all` green.

- [ ] **27F — First-pass ambience, lighting & audio bed** `[P]`
  - **Goal:** the quality bar, first pass.
  - **Tasks:** set day/night lighting mood, weather bias, and a first-pass ambience
    bed (placeholder audio is fine pre-Phase 31). Establish the dying-world palette
    in this region as the reference for all later regions.
  - **Done when:** the region reads as a *place* with mood, not greybox; documented
    as the bar.

---

## Phase 28 — First Boss: a Fallen Flamebearer (Iron King slice) `[F/C]`

> One full multi-phase boss to build and prove boss tooling ahead of Phase 36, and
> to wire the defeat → reward → corruption-gain loop.

- [ ] **28A — Iron King actor + arena** `[F/C]`
  - **Goal:** the boss exists in a space.
  - **Tasks:** build the Iron King as a `CharacterEntity` via a boss factory
    (mirror `EnemyFactory`): stats `AttributeSet`, `CombatComponent` (Team), a
    weapon, hurt/hitboxes, AI behaviour. Build an arena sub-cell with an entry
    trigger. Register in `ServiceLocator` if the boss bar needs it.
  - **Done when:** you can enter the arena and fight a functional (single-phase)
    Iron King.

- [ ] **28B — Multi-phase behaviour + telegraphed attacks** `[F]`
  - **Goal:** phases and readable wind-ups.
  - **Tasks:** add HP-threshold phase transitions (e.g. 66%/33%) that change the
    ability set, and telegraphed wind-up timing on heavy attacks (the "no
    button-mashing" feel). Keep it data-light but real; this becomes the seed for
    `BossController` in Phase 36 — note the generalizable bits.
  - **Done when:** the fight has ≥2 distinct phases with telegraphed attacks.

- [ ] **28C — Boss healthbar + intro/defeat sequencing** `[F]`
  - **Goal:** the boss UI/flow beats.
  - **Tasks:** add a boss healthbar to `GameHud` (through `UiTheme`), a short intro
    lock and a defeat sequence (slow-mo/fade hook for Phase 43 cinematics later).
    All strings via `Loc`.
  - **Done when:** the bar tracks the boss; intro and defeat beats play cleanly.

- [ ] **28D — Defeat → reward → corruption-gain loop** `[F/C]`
  - **Goal:** wire the boss to corruption + loot.
  - **Tasks:** on defeat, grant a guaranteed reward (a placeholder divine-relic
    item `.tres`) and raise corruption via `CorruptionComponent` (absorbing his
    fragment). Author the reward + the "absorb the flame?" dialogue/choice beat.
    Add a placeholder music cue hook for Phase 31.
  - **Done when:** defeating the Iron King grants the relic and visibly raises
    corruption; the whole beat round-trips through save/load.

> **🚩 Gate G0 — First Playable.** New game → creation → Ember Crown → core loop →
> defeat the Iron King slice → gain corruption → save/load intact, with corruption
> visibly changing something. (Roadmap §2.) Verify the full chain before opening
> Stage B.

---

# Stage B — Vertical Slice (→ G1)

> Everything in the slice is **ship-quality**. These sub-phases polish, not
> prototype.

---

## Phase 29 — Combat Feel & Game Juice `[F/P]`

- [ ] **29A — Hit-stop / freeze frames + hit-pause tuning** `[F/P]`
  - **Done when:** landing/taking a heavy hit briefly freezes for weight; tunable;
    off during pause/cutscene.
- [ ] **29B — Camera shake + directional hit reactions** `[F/P]`
  - **Done when:** crits/blocks/stagger shake the camera; hits push reactions in
    the hit direction.
- [ ] **29C — Weapon trails, impact VFX/SFX hooks** `[F/P]`
  - **Done when:** swings show trails and impacts spawn placeholder VFX/SFX through
    a poolable effect (CLAUDE.md §8 pooling).
- [ ] **29D — Screen feedback on crit/stagger/block/parry** `[F/P]`
  - **Done when:** each combat state has a distinct screen/HUD feedback through
    `UiTheme`.
- [ ] **29E — Dodge i-frames + roll** `[F]`
  - **Done when:** a dodge with invulnerability frames exists and is tunable;
    integrates with stamina.
- [ ] **29F — Parry / riposte windows** `[F]`
  - **Done when:** a timed block parries and opens a riposte; mistimed block takes
    chip/stagger.
- [ ] **29G — Animation-cancel windows + input buffering** `[F]`
  - **Done when:** attacks have commit + cancel windows and buffered inputs feel
    responsive, not mashy.
- [ ] **29H — Lock-on / soft target from `FocusedEntity`** `[F]`
  - **Done when:** a real target-lock with switching, built out from the Phase 18
    `FocusedEntity`.
- [ ] **29I — Stamina/poise pacing tune (anti-mash)** `[F/P]`
  - **Done when:** stamina/poise costs discourage mashing per the `docs/DESIGN.md`
    combat pillar; documented values.

---

## Phase 30 — Animation, Models & Visual Identity `[P]`

> Art-heavy; the human supplies assets. Each sub-phase integrates one asset class
> against existing states.

- [ ] **30A — Art-direction style guide** `[P]`
  - **Done when:** `docs/ART_STYLE.md` pins the dying-world language (ash, faded
    colour, embers) + import/LOD conventions feeding Phase 19/57.
- [ ] **30B — First-person arms + weapon rig integration** `[P]`
  - **Done when:** rigged FP arms + a weapon play attack/block/idle driven by
    combat states.
- [ ] **30C — Spell-casting animations + cast VFX by school** `[P]`
  - **Done when:** casting plays animations and school-tinted VFX matched to
    `SpellSchools`.
- [ ] **30D — Core enemy animation set (goblin + Iron King)** `[P]`
  - **Done when:** locomotion/attack/hit/death sets drive the existing AI/combat
    states for the slice cast.
- [ ] **30E — Third-person body for cutscenes/reflections** `[P]`
  - **Done when:** a TP body exists for the Phase 43 cutscenes and corruption
    appearance (23F) hangs off it.
- [ ] **30F — Status/impact VFX library + corruption materials** `[P]`
  - **Done when:** status effects + corruption tiers (replacing 23F placeholders)
    use real materials/VFX.

---

## Phase 31 — Audio Foundations `[F/P]`

- [ ] **31A — `AudioDirector` + Godot audio buses** `[F]`
  - **Done when:** master/music/SFX/ambience/UI/voice buses exist, registered in
    `ServiceLocator`, volumes wired to `SettingsService` (24E).
- [ ] **31B — Adaptive music state machine** `[F]`
  - **Done when:** exploration/combat/boss/safe states crossfade, driven by
    EventBus (combat start/end, boss start, region/day-phase change).
- [ ] **31C — Combat & interaction SFX hooks** `[F/P]`
  - **Done when:** hit/cast/pickup/level-up/UI events fire SFX through the director.
- [ ] **31D — 3D ambience per region/weather/time** `[F/P]`
  - **Done when:** regions/weather/day-phase drive looping 3D ambience beds.
- [ ] **31E — Footsteps by surface** `[F/P]`
  - **Done when:** footstep SFX vary by surface material under the player.

---

## Phase 32 — Companion System `[F]`

- [ ] **32A — `CompanionComponent` + follower AI core** `[F]`
  - **Done when:** a companion follows/holds on the player's team, reusing
    `EnemyAIComponent`/`Locomotion`/`Combat`; recruit/dismiss API; `ISaveable`
    roster.
- [ ] **32B — Command states (follow / hold / engage)** `[F]`
  - **Done when:** the player can command stance via a quick command; combat assist
    works.
- [ ] **32C — `CompanionResource` + loyalty standing** `[F]`
  - **Done when:** companions are data (`CompanionResource`) with a per-companion
    loyalty standing (reuse `ReputationComponent` patterns), persistent.
- [ ] **32D — Party persistence + save round-trip** `[F]`
  - **Done when:** roster, positions, and loyalty survive save/load and region
    streaming.
- [ ] **32E — Kael authored fully (recruit + loyalty quest + dialogue)** `[C]`
  - **Done when:** one complete companion (Kael) is recruitable with a dialogue
    graph + recruit quest + loyalty quest; the rest deferred to Beta.

---

## Phase 33 — Vertical Slice Assembly & Onboarding `[C/P]`

- [ ] **33A — Opening sequence + new-game → creation → world flow** `[C/P]`
  - **Done when:** new game runs creation → opening → Ember Crown as one seamless
    flow.
- [ ] **33B — Diegetic tutorial: movement/look/combat** `[C/P]`
  - **Done when:** move/look/attack/block/dodge are taught via prompts/toasts,
    skippable.
- [ ] **33C — Diegetic tutorial: magic/interact/inventory/quests** `[C/P]`
  - **Done when:** the remaining verbs are taught the same way; nothing blocks a
    veteran from skipping.
- [ ] **33D — Slice stitch: quest chain → guild taste → Iron King → corruption beat → cliffhanger** `[C/P]`
  - **Done when:** 30–60 min plays as one continuous, polished arc.
- [ ] **33E — Slice polish + external-build capture pass** `[P]`
  - **Done when:** a capture-ready external build candidate exists; rough edges in
    the slice path are gone.

> **🚩 Gate G1 — Vertical Slice.** A stranger plays 30–60 min that looks and feels
> shipped: real art/audio, weighty combat, a companion, a boss, the corruption
> payoff. (Roadmap §3.)

---

# Stage C — Alpha / Feature Complete (→ G2)

> After G2 we never invent a mechanic again. Front-load **all** remaining systems.

---

## Phase 34 — Enemy & Creature Roster `[F/C]`

- [ ] **34A — AI behaviour profiles: data-fy `EnemyAIComponent`** `[F]`
  - **Done when:** ranged/caster/shielded/pack-flank/fleeing/ambush are *tunable
    profiles/data*, not one-off subclasses.
- [ ] **34B — Humanoid archetypes (bandit, cultist, soldier, Iron Syndicate)** `[F/C]`
  - **Done when:** each is a factory archetype + `.tres` (attributes/loot/XP/
    profile); all four playable.
- [ ] **34C — Beast archetypes (wolves, Sylthari wildlife)** `[F/C]`
  - **Done when:** beast archetypes exist with appropriate AI profiles.
- [ ] **34D — Undead archetypes (Hollow Queen's legions)** `[F/C]`
  - **Done when:** undead archetypes exist and fight.
- [ ] **34E — Construct + elemental archetypes** `[F/C]`
  - **Done when:** constructs and elementals exist with distinct profiles.
- [ ] **34F — Corrupted/Ashen creature archetypes** `[F/C]`
  - **Done when:** corrupted variants exist (tie to the corruption fiction).
- [ ] **34G — `BestiaryDatabase` + in-game bestiary UI** `[F/C]`
  - **Done when:** kills/lore track in a bestiary screen (Ash Hunters fantasy)
    through existing UI patterns; `ISaveable`.

---

## Phase 35 — Dragons `[F/C]`

- [ ] **35A — Dragon body: multi-hit-zone scalable boss actor** `[F]`
  - **Done when:** a large multi-hurtbox dragon actor exists with tail/wing melee.
- [ ] **35B — Aerial AI: flight pathing, takeoff/landing** `[F]`
  - **Done when:** the dragon flies, lands, and takes off under AI control.
- [ ] **35C — Breath attacks (cones/AoE) via SpellResolver** `[F]`
  - **Done when:** breath attacks reuse `SpellResolver`/status for cone/AoE damage.
- [ ] **35D — Wild dragon variant (territorial world boss)** `[F/C]`
  - **Done when:** a Wild dragon spawns as a territorial world boss.
- [ ] **35E — Ash dragon variant (corrupted elite)** `[F/C]`
  - **Done when:** an Ash dragon exists as a corrupted elite enemy.
- [ ] **35F — Ancient dragon: dialogue-capable quest/lore giver** `[F/C]`
  - **Done when:** an Ancient dragon can hold a conversation (`DialogueComponent`)
    and give quests/lore.
- [ ] **35G — Dragon encounters in Frostfang + high-end world events** `[C]`
  - **Done when:** dragon encounters seed Frostfang Reach and the world-event
    tables.

---

## Phase 36 — Boss Framework & Encounter Design `[F]`

- [ ] **36A — `BossResource` schema (phases, abilities, enrage)** `[F]`
  - **Done when:** a boss is describable as data (HP-threshold phases, per-phase
    ability sets, enrage timer).
- [ ] **36B — `BossController` generalized from the Iron King** `[F]`
  - **Done when:** the Iron King (Phase 28) is re-expressed through
    `BossController`/`BossResource` with no behaviour regression.
- [ ] **36C — Telegraph/wind-up + interrupt/stagger tooling** `[F]`
  - **Done when:** reusable telegraph + interrupt/stagger windows drive off boss
    data.
- [ ] **36D — Adds/summon-wave + arena hooks** `[F]`
  - **Done when:** bosses can summon add waves and bind arena hooks declaratively.
- [ ] **36E — Boss intro/defeat sequencing + guaranteed relic reward** `[F]`
  - **Done when:** intro/defeat/reward (relic + corruption gain) are standardized
    in the framework.

---

## Phase 37 — Housing & Player Property `[F]`

- [ ] **37A — `PropertyComponent` + `HousingService` (claim/own)** `[F]`
  - **Done when:** a property can be purchased/claimed; ownership is `ISaveable`.
- [ ] **37B — Per-property persistent storage** `[F]`
  - **Done when:** property storage extends inventory persistence and round-trips.
- [ ] **37C — Placeable crafting stations + decoration** `[F]`
  - **Done when:** the player can place stations (`CraftingStationFactory`) and
    decorations in an owned property; placement persists.
- [ ] **37D — Trophy/display slots + one playable property authored** `[F/C]`
  - **Done when:** trophy slots work and one property type is fully playable; the
    rest are content.

---

## Phase 38 — Economy, Vendors & Services `[F/C]`

- [ ] **38A — `VendorComponent` + `ShopResource` (buy/sell)** `[F]`
  - **Done when:** buy/sell works against the item system with buy/sell spreads.
- [ ] **38B — Stock: static + restock + leveled** `[F]`
  - **Done when:** vendor stock supports static lists, restock timers, and leveled
    pools.
- [ ] **38C — Reputation discounts + gold sinks** `[F/C]`
  - **Done when:** faction standing modifies prices; defined gold sinks exist.
- [ ] **38D — Services: repair / trainer / bank / inn / stable** `[F/C]`
  - **Done when:** trainer (buy perks/points), bank (storage), innkeeper (rest/
    time-skip), stablemaster (mounts stub), and repair (if durability adopted in 40)
    are interactable services.
- [ ] **38E — Wire real shops into Ember Crown vendors** `[C]`
  - **Done when:** the Phase 27 stub vendors become real shops; `validate` green.

---

## Phase 39 — Mounts & Traversal `[F]`

- [ ] **39A — `MountComponent`: summon/dismount + mounted locomotion** `[F]`
  - **Done when:** summon/mount/dismount works with mounted move/sprint/stamina.
- [ ] **39B — Mounted-combat rules + fast-travel integration** `[F]`
  - **Done when:** combat-while-mounted rules are defined and mounts integrate with
    fast travel.
- [ ] **39C — Traversal verbs the world needs (climb/swim/ledge)** `[F]`
  - **Done when:** only the verbs region design (44) requires are added and tuned.

---

## Phase 40 — Survival & Needs (scoped decision) `[F]`

- [ ] **40A — Design decision recorded in `docs/DESIGN.md`** `[P]`
  - **Done when:** durability/food/rest/temperature are each explicitly **adopted
    or cut** with rationale. An empty build is a valid outcome.
- [ ] **40B — Implement the adopted need(s) only** `[F]`
  - **Done when:** whatever survived 40A is built `ISaveable` and integrated (e.g.
    durability → repair service in 38D); cut systems leave no stub.

---

## Phase 41 — Quest Authoring at Scale & Branching `[F/C]`

- [ ] **41A — Reach/Explore + Talk objective types** `[F]`
  - **Done when:** both new `ObjectiveResource` types are event-driven like the
    existing two and authorable.
- [ ] **41B — Escort + Defend/Survive objective types** `[F]`
  - **Done when:** escort and defend/survive objectives work with fail states.
- [ ] **41C — Interact/Use + Timed + Stealth objective types** `[F]`
  - **Done when:** the remaining objective types are authorable and validated.
- [ ] **41D — Choice/Branch objectives + quest state graphs** `[F]`
  - **Done when:** quests can branch on story flags/dialogue effects into multiple
    paths/endings with failure states.
- [ ] **41E — Quest-driven world changes** `[F]`
  - **Done when:** a quest can change the world (an NPC dies, a region opens),
    persistently.
- [ ] **41F — Quest-debug console + `ContentValidator` extension** `[F]`
  - **Done when:** `quest start/advance/complete/reset` exist and `validate-all`
    covers the new objective/branch types.

---

## Phase 42 — Guild & Faction Questlines `[C]`

- [ ] **42A — Membership/rank flag framework + small rank UI** `[F]`
  - **Done when:** join/rank-up flag chains + a minimal rank display exist (reuse
    flags + factions).
- [ ] **42B — Dawnwardens questline + hub presence** `[C]`
- [ ] **42C — Ash Hunters questline + hub presence** `[C]`
- [ ] **42D — Veiled Archive questline + hub presence** `[C]`
- [ ] **42E — Iron Syndicate questline + hub presence** `[C]`
- [ ] **42F — Emberbound questline + hub presence** `[C]`
  - **Done when (each B–F):** the guild is a joinable `FactionResource` with a
    multi-quest arc, ranks, hub presence, and rewards; `validate-all` green.

---

## Phase 43 — Cinematics & Scripted Sequences `[F]`

- [ ] **43A — `CutsceneResource` + `SequenceDirector` timeline core** `[F]`
  - **Done when:** a timeline of camera moves + fades plays, pausing gameplay
    cleanly via `GameState`, skippable.
- [ ] **43B — Actor blocking + dialogue staging on the timeline** `[F]`
  - **Done when:** cutscenes can move actors and stage dialogue (reuse the dialogue
    system).
- [ ] **43C — VFX/SFX/music cues on the timeline** `[F]`
  - **Done when:** cutscenes trigger VFX/SFX/music through the `AudioDirector`.
- [ ] **43D — Author 2 set-pieces (boss intro + a story beat)** `[C]`
  - **Done when:** two real cutscenes prove the tooling end-to-end.

---

## Phase 44 — Alpha Content Pass: all four realms blocked out `[C]`

> One sub-phase per realm = a big-but-bounded content session each; the spine ties
> them together.

- [ ] **44A — Ember Crown: extend to full first-pass extent** `[C]`
  - **Done when:** the realm beyond the slice region is greyboxed with hubs/POIs/
    encounters + the Iron King lair finalized as a framework boss.
- [ ] **44B — Frostfang Reach: hub, POIs, encounters, Hollow Queen lair stub** `[C]`
- [ ] **44C — Ashen Wilds: hub, POIs, encounters, Storm Tyrant lair stub** `[C]`
- [ ] **44D — Sunspire Dominion: hub, POIs, encounters, Beast Lord lair stub** `[C]`
  - **Done when (each A–D):** the realm is reachable via streaming/fast-travel with
    a hub, key POIs, encounter sets, and the resident fallen-Flamebearer boss stub;
    `validate-all` green.
- [ ] **44E — Crimson Prophet lair stub + main-quest spine connecting all realms** `[C]`
  - **Done when:** every realm + boss + guild is reachable and the main-quest spine
    threads them (rough but complete in extent).

---

## Phase 45 — Alpha Hardening & Feature Freeze `[F/P]`

- [ ] **45A — Full-feature integration test pass** `[F/P]`
  - **Done when:** a documented pass exercises every system together; interaction
    bugs are logged.
- [ ] **45B — Fix system-interaction bugs (burn-down)** `[F]`
  - **Done when:** the 45A bug list is burned to zero blockers.
- [ ] **45C — Streaming-world load profiling** `[P]`
  - **Done when:** the streamed world is profiled under load; hitches/regressions
    are logged for Phase 57.
- [ ] **45D — Declare feature freeze + record the exception process** `[P]`
  - **Done when:** the feature list is locked in `docs/PRODUCTION_ROADMAP.md`; the
    "new-mechanic exception" rule is written.

> **🚩 Gate G2 — Alpha / Feature Complete.** Every mechanic exists and works
> together; the whole game's *shape* is traversable. The schedule is de-risked.
> (Roadmap §4.)

---

# Stage D — Beta / Content Complete (→ G3)

> Pure authoring against frozen systems — the most parallelizable, most
> session-friendly work. Story acts split by act-beat; one beat ≈ one session.

---

## Phase 46 — Main Story, Act I: Awakening `[C]`

- [ ] **46A — Opening + inciting incident (Seventh Flamebearer reveal)** `[C]`
- [ ] **46B — First hunt: ancient forces begin hunting the player** `[C]`
- [ ] **46C — First companion recruitment beat (Kael, story-integrated)** `[C]`
- [ ] **46D — The corruption seed beat** `[C]`
- [ ] **46E — Act I → Act II hook + flag handoff** `[C]`
  - **Done when (each):** the beat's quests/dialogue/cutscenes/flags are authored
    and play in sequence; `validate-all` green; all strings via `Loc`.

---

## Phase 47 — Main Story, Act II: Gathering the Flame `[C]`

> The bulk of the game — one realm arc per sub-phase.

- [ ] **47A — Iron King arc (Ember Crown): questline + boss + relic + corruption beat + guild ties** `[C]`
- [ ] **47B — Hollow Queen arc (Frostfang Reach)** `[C]`
- [ ] **47C — Storm Tyrant arc (Ashen Wilds)** `[C]`
- [ ] **47D — Beast Lord arc (Sunspire Dominion)** `[C]`
- [ ] **47E — Crimson Prophet arc** `[C]`
- [ ] **47F — Ashen Knight rivalry seeds across the arcs** `[C]`
  - **Done when (each):** the arc's questline + boss (framework) + relic reward +
    corruption beat + guild hooks are authored and completable; `validate-all`
    green.

---

## Phase 48 — Main Story, Act III: Truth of the Gods `[C]`

- [ ] **48A — Divine Cataclysm history reveal (Veiled Archive beats)** `[C]`
- [ ] **48B — Morthul / Ash King true-nature reveal** `[C]`
- [ ] **48C — "Someone must sit upon the Ash Throne" thematic pivot** `[C]`
- [ ] **48D — Act III → Act IV setup + ending-eligibility checkpoint** `[C]`
  - **Done when (each):** authored and playable; corruption ending-eligibility
    (23H) is referenced correctly.

---

## Phase 49 — Main Story, Act IV: The Celestial War + Endings `[C]`

- [ ] **49A — Assault on the ruined Celestial Realm** `[C]`
- [ ] **49B — Ashen Knight final confrontation** `[C]`
- [ ] **49C — Morthul confrontation** `[C]`
- [ ] **49D — The final choice + branch gating (corruption + loyalty)** `[C]`
- [ ] **49E — Dawnfire ending + epilogues** `[C]`
- [ ] **49F — Lord of Embers ending + epilogues** `[C]`
  - **Done when (each):** the beat is authored and reachable; both endings gate
    correctly on corruption (23H) and companion loyalty (32C); per-choice epilogues
    play.

---

## Phase 50 — Side Content, Activities & World Density `[C]`

- [ ] **50A — Ember Crown side quests + POIs + ambient life** `[C]`
- [ ] **50B — Frostfang Reach side quests + POIs + ambient life** `[C]`
- [ ] **50C — Ashen Wilds side quests + POIs + ambient life** `[C]`
- [ ] **50D — Sunspire Dominion side quests + POIs + ambient life** `[C]`
- [ ] **50E — Dungeons/lairs pass (all realms)** `[C]`
- [ ] **50F — World-event + encounter tables filled out** `[C]`
- [ ] **50G — Collectibles (Veiled Archive lore books) + bounties (Syndicate/Hunters)** `[C]`
- [ ] **50H — Companion loyalty quests (Nyra, Orik, Seraphine, Vex)** `[C]`
  - **Done when (each):** the content is authored, reachable, and `validate-all`
    green; density goals for the slice met.

---

## Phase 51 — Itemization, Loot & Reward Economy Pass `[C]`

- [ ] **51A — Weapon catalogue per tier/realm** `[C]`
- [ ] **51B — Armor catalogue per tier/realm** `[C]`
- [ ] **51C — Accessory catalogue + affix/set families** `[C]`
- [ ] **51D — Consumables/materials/recipes catalogue** `[C]`
- [ ] **51E — Divine relics (unique flamebearer-power items, corruption-tied)** `[C]`
- [ ] **51F — Reward placement + loot-table curation across the game** `[C]`
  - **Done when (each):** the catalogue slice is authored, balanced for *placement*
    (numeric balance is Phase 56), and `validate-all` green.

---

## Phase 52 — Full Audio & Music Production `[P]`

- [ ] **52A — Adaptive score per realm** `[P]`
- [ ] **52B — Boss/theme music cues** `[P]`
- [ ] **52C — Full SFX coverage pass** `[P]`
- [ ] **52D — Ambience per region/weather/time (final)** `[P]`
- [ ] **52E — VO integration for key story/companion beats** `[P]`
  - **Done when (each):** assets are integrated through the `AudioDirector` and bus
    mix; no placeholder audio remains in that slice.

---

## Phase 53 — Art Complete & World Beautification `[P]`

- [ ] **53A — Ember Crown final art + lighting + set dressing** `[P]`
- [ ] **53B — Frostfang Reach final art pass** `[P]`
- [ ] **53C — Ashen Wilds final art pass** `[P]`
- [ ] **53D — Sunspire Dominion final art pass** `[P]`
- [ ] **53E — Character/creature/boss final models** `[P]`
- [ ] **53F — Dying-world VFX polish + visual cohesion pass** `[P]`
  - **Done when (each):** no greybox remains in that slice; the dying-world art
    direction is fully realized; LOD discipline (Phase 19) maintained.

---

## Phase 54 — Accessibility & Input `[F/P]`

- [ ] **54A — Full input remapping (KB/M + controller)** `[F]`
- [ ] **54B — Subtitles + speaker names + sizing** `[F/P]`
- [ ] **54C — Colorblind options + UI scaling** `[F/P]`
- [ ] **54D — Scalable difficulty options** `[F]`
- [ ] **54E — Aim/lock-on assists** `[F]`
- [ ] **54F — Steam Deck input/UI verification** `[P]`
  - **Done when (each):** the option works, persists through `SettingsService`, and
    is exposed in the Settings UI.

---

## Phase 55 — Content-Complete Integration & First Full Playthrough `[C/P]`

- [ ] **55A — Full playthrough: Act I → Act II (both realms paths)** `[C/P]`
- [ ] **55B — Full playthrough: Act III → Act IV → Dawnfire ending** `[C/P]`
- [ ] **55C — Full playthrough: Lord of Embers ending path** `[C/P]`
- [ ] **55D — Narrative/flag/sequence-break fix burn-down** `[C]`
- [ ] **55E — Reachability audit: every quest/region/boss/companion/guild** `[C]`
  - **Done when (each):** the path completes with no placeholders/sequence breaks;
    bugs logged and burned down.

> **🚩 Gate G3 — Beta / Content Complete.** Whole game playable end to end, both
> endings reachable, all art/audio in, no placeholders. (Roadmap §5.)

---

# Stage E — Release Candidate (→ G4)

> No new content — stabilize, balance, certify.

---

## Phase 56 — Balance & Difficulty Tuning `[C/P]`

- [ ] **56A — Combat math pass (damage/armor/crit/weapon classes/schools)** `[C/P]`
- [ ] **56B — XP curve + level cap tuning** `[C/P]`
- [ ] **56C — Economy tuning (prices/gold flow/sinks)** `[C/P]`
- [ ] **56D — Encounter pacing + boss difficulty pass** `[C/P]`
- [ ] **56E — Corruption pacing (both endings earnable, temptation reads)** `[C/P]`
- [ ] **56F — Difficulty-option tuning + telemetry review** `[C/P]`
  - **Done when (each):** values tuned via existing resources, informed by
    playtest/telemetry (Phase 22H); changes documented.

---

## Phase 57 — Performance & Memory Cert `[P]`

- [ ] **57A — Frame-budget profiling on min-spec PC** `[P]`
- [ ] **57B — Steam Deck frame-budget profiling** `[P]`
- [ ] **57C — Streaming hitch elimination** `[P]`
- [ ] **57D — Draw-call / LOD / shadow budget pass** `[P]`
- [ ] **57E — Memory ceiling + load-time targets** `[P]`
- [ ] **57F — Shader pre-compilation** `[P]`
  - **Done when (each):** the target metric is met and measured (profile-guided,
    not guessed); maintainer-verified on hardware.

---

## Phase 58 — Save/Load Hardening & Migration `[F]`

- [ ] **58A — 100+ hour / large-save stress** `[F]`
- [ ] **58B — Schema migration across patches (`TryMigrate`)** `[F]`
- [ ] **58C — Corruption recovery + slot integrity** `[F]`
- [ ] **58D — Autosave cadence + cloud-save compatibility** `[F]`
  - **Done when (each):** the failure mode is exercised and handled; no data-loss
    path remains.

---

## Phase 59 — Bug Triage, QA & Soak `[P]`

- [ ] **59A — Functional QA pass: per region** `[P]`
- [ ] **59B — Functional QA pass: per quest/system** `[P]`
- [ ] **59C — Soak/longevity tests** `[P]`
- [ ] **59D — Grow `Embervale.Tests` + in-engine GUT regression suite** `[F]`
- [ ] **59E — Crash-free-session target + blocker burn-down** `[P]`
  - **Done when (each):** the pass is complete, bugs are triaged into the database,
    and blockers trend to zero.

---

## Phase 60 — Localization Completion & Culturalization `[C/P]`

- [ ] **60A — Full string extraction audit (no hard-coded strings)** `[C]`
- [ ] **60B — Translation integration (shipped languages)** `[C]`
- [ ] **60C — Font/glyph coverage (CJK as scoped)** `[P]`
- [ ] **60D — Text-fit/overflow LQA + culturalization review** `[C/P]`
  - **Done when (each):** coverage is complete for that slice; made cheap by the
    Phase 24G `Loc` discipline.

---

## Phase 61 — Platform Compliance & Storefront `[P]`

- [ ] **61A — Steam cert: TRC/cloud/controller-glyph requirements** `[P]`
- [ ] **61B — Achievements/trophies** `[P]`
- [ ] **61C — Store page (capsule, screenshots, trailer cut from the slice)** `[P]`
- [ ] **61D — Age ratings + EULA + credits** `[P]`
- [ ] **61E — Reproducible release-build pipeline** `[P]`
  - **Done when (each):** the requirement is satisfied and verifiable against
    platform docs.

---

## Phase 62 — Release Candidate & Gold Master `[P]`

- [ ] **62A — Code/content lock** `[P]`
- [ ] **62B — RC build series + final cert pass** `[P]`
- [ ] **62C — Day-one patch plan** `[P]`
- [ ] **62D — Gold-master sign-off (zero known crash/blocker bugs)** `[P]`
  - **Done when (each):** the RC milestone step is met against the G4 bar.

> **🚩 Gate G4 — Release Candidate.** Gold-master-quality, certified, zero
> blockers, day-one patch staged. (Roadmap §6.)

---

# Stage F — Launch (→ G5)

## Phase 63 — Launch `[P]`

- [ ] **63A — Final pre-launch checklist + build submission** `[P]`
- [ ] **63B — Store page live + monitoring/telemetry on** `[P]`
- [ ] **63C — Ship + day-one patch live + support channels staffed** `[P]`
  - **Done when:** Embervale is live on Windows/Linux/Steam Deck.

> **🚩 Gate G5 — Launch.** Embervale is live. (Roadmap §7.)

---

# Stage G — Live / Post-launch (→ G6)

## Phase 64 — Launch Response & Stabilization `[P]`

- [ ] **64A — Real-player crash/telemetry triage** `[P]`
- [ ] **64B — Hotfix wave** `[P]`
- [ ] **64C — First balance patch + community response** `[P]`

## Phase 65 — Post-Launch Content (the long tail) `[C/F]`

- [ ] **65A — New Game+ (carry-over + escalation, corruption/relics)** `[F]`
- [ ] **65B — Higher difficulty tiers** `[F/C]`
- [ ] **65C — Additional regions/dungeons/bosses** `[C]`
- [ ] **65D — More companions + loyalty content** `[C]`
- [ ] **65E — Seasonal world events** `[C]`

## Phase 66 — Expansion / DLC Framework `[F/C]`

- [ ] **66A — Entitlement / DLC content loading** `[F]`
- [ ] **66B — New-realm-sized expansion seam** `[F/C]`
- [ ] **66C — Expansion shipping tooling (no base-game fork)** `[F]`

> **🚩 Gate G6 — Live.** A shipped game with a sustainable content cadence.
> (Roadmap §8.)

---

## Appendix — keeping this playbook honest

- **Re-derive sizing as you go.** If a sub-phase repeatedly overflows a session,
  the *next* time you hit its sibling, split it pre-emptively and update this file.
- **This file is the live tracker.** Tick boxes here per session; mirror only the
  *phase-level* status into `PRODUCTION_ROADMAP.md` §11 so the two don't drift.
- **The gates are real.** Don't open a stage's first sub-phase until the prior
  gate's criteria are maintainer-verified in a build (CLAUDE.md §2 — this
  container can't build; "verified" = the human confirmed it).
- **Every sub-phase still owes the full DoD** (`PRODUCTION_ROADMAP.md` §0.3):
  builds, playable, `ISaveable` round-trips, `validate-all` green, docs updated,
  draft PR. The **Done when** line is *extra*, not instead.
</content>
</invoke>
