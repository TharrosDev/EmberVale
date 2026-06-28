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

- [x] **22F — Headless validation entry point** `[F]`
  - **Goal:** let the maintainer run validation without launching into gameplay.
  - **Tasks:** add a headless/`--validate` path (a Godot `--headless` script or a
    `GameState` boot branch) that loads the databases, runs `validate-all`, prints
    a report, and exits non-zero on failure. Document the invocation in CLAUDE.md
    §3 and the README.
  - **Done when:** a documented one-command content check exists (reviewed against
    the API; the human runs it).

- [x] **22G — `data/_templates/` canonical starting `.tres`** `[P]`
  - **Goal:** copy-paste starting points for every content type.
  - **Tasks:** create `data/_templates/` with one minimal, commented `.tres`
    exemplar per content domain already in CLAUDE.md §8 (item, equippable, affix,
    loot table, perk, quest, dialogue, schedule, weather, encounter, world event,
    recipe, spell, status effect, faction). Each is the recipe's "canonical
    starting point."
  - **Done when:** every §8 domain has a template; `validate` stays green
    (templates either valid or excluded by an `_` convention).

- [x] **22H — Telemetry/analytics spine (dev-only)** `[F]`
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

- [x] **23A — `CorruptionComponent` core + events + save** `[F]`
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

- [x] **23B — Corruption dev console + debug surface** `[F]`
  - **Goal:** make it testable before it has any visual.
  - **Tasks:** register a `corruption` console command (`get` / `set N` / `add N`
    / `tier`) per CLAUDE.md §8 "new dev-console command," resolving the player via
    `ServiceLocator`. Add a line to the F3 debug overlay showing value + tier.
  - **Done when:** the maintainer can drive corruption from `F1` and watch it on
    F3.

- [x] **23C — Dialogue conditions/effects for corruption** `[F]`
  - **Goal:** let conversations gate and modify corruption.
  - **Tasks:** extend `DialogueEnums.cs` with `Condition` `CorruptionAtLeast` /
    `CorruptionBelow` and `Effect` `AddCorruption`. Wire evaluation in the dialogue
    session runner against `CorruptionComponent`. Author one test dialogue using
    each. (Extends CLAUDE.md §8 "new conversation"; read `src/Dialogue/` first.)
  - **Done when:** a conversation visibly branches on corruption and a choice can
    raise it; `validate` understands the new enum values.

- [x] **23D — Corruption UI: character-screen gauge** `[F]`
  - **Goal:** the player can see their corruption.
  - **Tasks:** add a corruption gauge to the character screen via `UiTheme.Bar`
    (CLAUDE.md §8 "new UI panel"). Label the current tier. Rebuild from a dirty
    flag in `_Process`, never in a signal handler.
  - **Done when:** the gauge reflects live corruption + tier through `UiTheme`.

- [x] **23E — Corruption HUD vignette at high tiers** `[F/P]`
  - **Goal:** ambient dread at Ashbound/Embers.
  - **Tasks:** add a subtle screen vignette/desaturation overlay in `GameHud` that
    fades in by tier (subscribe to `CorruptionTierChangedEvent`). Keep it through
    `UiTheme` palette; intensity is data-light and tweakable.
  - **Done when:** crossing into high tiers visibly shifts the screen; reverting
    lowers it.

- [x] **23F — `CorruptionAppearanceController` (hook stub)** `[F]`
  - **Goal:** the seam the future model/VFX work plugs into.
  - **Tasks:** add a `CorruptionAppearanceController` on the player that, per tier,
    swaps a placeholder material/emissive (eye glow, ash-vein tint) on whatever
    player mesh exists now. Drive it off the tier event. Designed so Phase 30 can
    replace placeholders with real materials without changing the wiring.
  - **Done when:** each tier shows a *distinct* placeholder appearance change;
    documented as the hook for Phase 30.

- [x] **23G — NPC reaction / global "dread" standing** `[F]` ✅
  - **Goal:** the world fears a corrupted player.
  - **Tasks:** have `ReputationComponent`/faction AI read corruption as a global
    standing modifier ("dread") so high corruption nudges NPC hostility/dialogue.
    Reuse the existing reputation math; don't add a parallel system. (Read
    `src/Factions/`.)
  - **Done when:** raising corruption measurably shifts at least one faction's
    standing/AI reaction; round-trips through save.
  - **Done:** `ReputationComponent` now derives a global `Dread` penalty from the
    sibling `CorruptionComponent`'s tier (Touched 5 · Marked 15 · Ashbound 30 ·
    Embers 50) and exposes `Effective(faction)` = earned `Get` − `Dread`, clamped.
    `TierOf`/`IsHostile` route through `Effective`, so the existing enemy-AI
    `PlayerIsTarget` gate makes factions turn on a corrupted player **live** (and
    stand down as corruption falls) with no new system. Earned standing and its
    persistence are untouched (dread is derived from the already-saved corruption,
    so it round-trips for free). Surfaced in the character-screen reputation panel
    (a "Dread −N" line + effective tiers), the F3 debug HUD, and the `corruption`
    dev-console command.

- [x] **23H — Corrupted ability gating + both-endings eligibility hook** `[F/C]` ✅
  - **Goal:** corruption unlocks corrupted variants and feeds the endings dial.
  - **Tasks:** add a corruption-tier gate option to `SpellResource`/`PerkResource`
    consumption (author one corrupted spell + one corrupted perk `.tres` gated by
    tier — CLAUDE.md §8 recipes, no new system). Expose a
    `CorruptionComponent.EndingEligibility` read (Dawnfire vs Lord of Embers
    threshold) that Phase 49 will consume. Document the contract.
  - **Done when:** a tier-gated spell/perk is learnable only above its tier; an
    ending-eligibility value is queryable and saved.
  - **Done:** `SpellResource`/`PerkResource` gained a `MinCorruptionTier` export
    (default `Untainted`, so existing content is ungated). `SpellcastingComponent.Learn`
    and `PerksComponent.CanLearn`/`Learn` resolve the sibling `CorruptionComponent`
    (the 23G lazy pattern) and refuse content above the player's tier. Authored
    `data/spells/EmberSiphon.tres` + `data/perks/AshbornMight.tres`, both gated at
    Marked; the perk shows `[needs Marked]` in the character screen until then, and a
    `learn <id>` dev command verifies the spell gate. `CorruptionComponent.EndingEligibility`
    (`EndingPath` Undecided/Dawnfire/LordOfEmbers) is pure-derived from the saved meter
    via `CorruptionTiers.EligibilityOf` (Dawnfire <40, LordOfEmbers ≥60), unit-tested and
    surfaced in the `corruption` console output. Phase 23 (Corruption) is now complete.

---

## Phase 24 — Meta-Shell & Localization Spine `[F]`

> The title/menu/settings/save-slot shell the systems roadmap excluded, plus the
> i18n layer that must land *before* mass content authoring.

- [x] **24A — `MainMenu` scene + `GameState.MainMenu` boot** `[F]` ✅
  - **Goal:** the game boots to a menu, not straight into the sandbox.
  - **Tasks:** add a `MainMenu` scene (New Game / Continue / Load / Settings /
    Quit, built through `UiTheme`). Make `GameBootstrap`/`GameManager` boot into
    `GameState.MainMenu` and transition to `Playing` on New Game/Continue. Keep the
    sandbox reachable (New Game → existing bootstrap path).
  - **Done when:** launching shows the menu; New Game enters the world; Quit exits.
    No save logic yet (buttons can be stubbed/disabled).
  - **Done:** new `src/UI/MainMenu.cs` (a code-built `CanvasLayer` via `UiTheme`,
    mirroring `PauseMenu`) — New Game + Quit live, Continue/Load/Settings disabled
    stubs for 24B–24F. `GameBootstrap._Ready` now inits databases/validates then
    `ShowMainMenu()` + boots `GameState.MainMenu`; the sandbox build is extracted into
    `StartNewGame()` (the original path), invoked by New Game, guarded by `_sandboxBuilt`
    (which also gates the debug/save key shortcuts). Verified: boots to
    `Boot -> MainMenu` and stops before "Sandbox ready", no errors; build + 58 tests +
    `--validate` green.

- [x] **24B — `SaveManager`: single-file → slot directories** `[F]` ✅
  - **Goal:** multiple independent saves.
  - **Tasks:** refactor `SaveManager` from one file to `user://saves/<slot>/`.
    Add slot create/list/delete and a save *header* (region, level, playtime,
    corruption tier, timestamp). Keep `ISaveable` registration API unchanged.
    Read `src/Save/` first; preserve back-compat or write a one-time migration.
  - **Done when:** multiple slots coexist; F5/F9 still work against the active
    slot; headers populate.
  - **Done:** each slot is now a directory `user://saves/<slot>/` holding `save.json`
    (the unchanged versioned envelope + an embedded `header`) and `header.json` (a
    lightweight mirror the 24C browser reads without parsing the full save). New
    `SaveSlotInfo` (slot/timestamp/playtime/region/level/corruption tier) +
    `ListSlots`/`ReadHeader`/`DeleteSlot`. `SaveManager` accumulates playtime while
    Playing and restores it per-slot on load; header gameplay fields come from a
    `HeaderProvider` delegate the bootstrap sets (so `SaveManager` stays decoupled).
    Legacy `<slot>.json` is still read and migrated away on the next save. `ISaveable`
    API and `SaveGame`/`LoadGame` signatures unchanged; F5/F9 still target `quick`.
    Verified in-engine: New Game → quick-save wrote `saves/quick/{save,header}.json`
    (header populated: Ember Crown / level 1 / Untainted / playtime), legacy
    `quick.json` removed, and quick-load restored 19 objects. Build + 58 tests +
    `--validate` green.

- [x] **24C — Save-slot UI (New/Load/Continue + metadata)** `[F]` ✅
  - **Goal:** the player manages saves from the shell.
  - **Tasks:** build the slot-select panel (list slots with header metadata +
    screenshot thumbnail; New into empty slot; Load; Delete with confirm). Wire
    Continue = most-recent slot. Capture a screenshot on save for the thumbnail.
  - **Done when:** full new/continue/load/delete flow works from the menu through
    `UiTheme`, round-tripping real saves.
  - **Done:** new `src/UI/SaveSlotPanel.cs` — a `UiTheme` slot browser (roster
    slot1–3) showing each filled slot's thumbnail + metadata (region · level · tier ·
    playtime · date) with New/Overwrite, Load, and Delete (inline confirm), opened by
    the `MainMenu` in New or Load intent. `MainMenu` Continue/Load are now live
    (Continue = most-recent slot via `ListSlots`). `SaveManager` gained an `ActiveSlot`
    (F5/F9 + pause Save/Load target it) and best-effort `screenshot.png` capture on
    save; `GameBootstrap` split into `BuildWorld()` + slot-aware
    `StartNewGame(slot)`/`StartLoadedGame(slot)` (load = build world then `LoadGame`).
    Verified in-engine: Continue built a fresh world and restored the most-recent save
    (19 objects), pause Save/Load round-tripped, and `saves/quick/screenshot.png` was
    written. Build + 58 tests + `--validate` green.

- [x] **24D — Autosave + quicksave + manual cadence** `[F]` ✅
  - **Goal:** robust save cadence on top of slots.
  - **Tasks:** add autosave triggers (region change, major quest beat, time
    interval) writing to a rotating autosave slot; keep quicksave (F5/F9) and
    manual save-from-pause. Guard against saving mid-cutscene/load.
  - **Done when:** autosave/quicksave/manual all target the slot system safely; no
    double-save races.
  - **Done:** new `src/Save/AutosaveService.cs` — a bootstrap-created, `ServiceLocator`
    -registered node that owns the cadence while `SaveManager` stays the low-level writer
    (mirrors the Encounter/WorldEvent director pattern). Autosaves rotate through a 3-slot
    ring (`auto1..auto3`); the next slot is chosen empty-or-oldest from the on-disk headers
    (pure `NextAutosaveSlot`, unit-tested), so rotation survives restarts with no extra
    persistence, and they never touch `ActiveSlot` (F5/F9 + pause Save/Load still target the
    player's slot). Triggers: a 5-min active-play interval, `QuestCompletedEvent`,
    `LeveledUpEvent`, plus a documented `RequestRegionChangeAutosave()` seam (uncalled until
    Phase 25 streaming). Guards: fires only while `GameManager.IsPlaying` (covers
    loading/paused/menu; cutscene is the Phase 43 seam) and is debounced to ≥60s between any
    two autosaves, so two triggers in quick succession can't double-write. `SaveGame` gained a
    `(slot, isAutosave)` overload that flavours `GameSavedEvent` (now `IsAutosave`); the
    `Notifications` feed toasts "Autosaved" on autosave only (manual F5 stays quiet). The Load
    browser surfaces existing autosaves as read-only rows (Load + Delete, never overwritten by
    New); Continue already picks the most recent of all slots via `ListSlots`. An `autosave`
    dev command forces one / prints ring status. Verified: build + 62 tests (4 new
    `AutosaveServiceTests`) + `--validate` green; in-engine New Game builds the world with the
    service wired and no errors.

- [x] **24E — `Settings` resource + `SettingsService`** `[F]` ✅
  - **Goal:** persisted options applied at runtime.
  - **Tasks:** add a `Settings` resource (graphics, audio bus volumes, controls,
    gameplay, accessibility placeholders) persisted to `user://settings.tres` via
    a `SettingsService` (`ServiceLocator`-registered). Apply on boot.
  - **Done when:** settings persist across launches and apply on load; audio-bus
    fields are ready for Phase 31 to consume.
  - **Done:** new `src/Settings/Settings.cs` — a `[GlobalClass]` data `Resource` with graphics
    (`WindowMode`/`VSync`/`MaxFps`), six linear audio-bus volumes (Master/Music/SFX/Ambience/UI/
    Voice, paired to bus names via a shared `AudioBuses` constants class so the Phase 31 mixer and
    these fields can't drift), controls/gameplay (`MouseSensitivity`/`InvertY`/`Difficulty`), and
    accessibility placeholders (`ReducedMotion`/`SubtitlesEnabled`/`UiScale`). `SettingsService`
    (a plain class, `ServiceLocator`-registered) loads `user://settings.tres` via
    `ResourceLoader`/`ResourceSaver` (cache-ignored; missing/unreadable → defaults), `Apply()`s
    graphics to `DisplayServer`/`Engine.MaxFps` and each volume to whatever buses exist (Master now,
    the rest once Phase 31 creates them), and exposes `Save`/`ResetToDefaults`. The bootstrap creates
    it and calls `LoadAndApply()` in `_Ready` **before** the title menu so the first frame honours
    saved options; the menu's Settings button stays a stub until the 24F panel. Pure
    `SettingsMath.LinearToDb`/`ClampVolume` (fader→dB with a -80 dB silence floor) back the audio
    apply and are unit-tested. A `settings` dev command shows/sets/resets (persists + applies) for
    verification ahead of the 24F UI. Verified: build + 71 tests (9 new `SettingsMathTests`) +
    `--validate` green; in-engine boot runs `LoadAndApply` with no errors. (The `.tres` save uses the
    same `[GlobalClass]` resource mechanism as the whole content pipeline; the explicit save path is
    reachable via the `settings set` console command pending the 24F panel.)

- [x] **24F — Settings UI panel** `[F]` ✅
  - **Goal:** the options menu.
  - **Tasks:** build the Settings panel (tabs/sections for Graphics/Audio/Controls/
    Gameplay/Accessibility) through `UiTheme`, reading/writing `SettingsService`.
    Reachable from both MainMenu and PauseMenu.
  - **Done when:** changing a setting applies live and persists; reachable from
    both shells.
  - **Done:** new `src/UI/SettingsPanel.cs` — a modal `UiTheme` panel with scrollable
    Graphics / Audio / Controls / Gameplay / Accessibility sections, each control bound to the live
    `SettingsService.Current`: window-mode / max-FPS / difficulty dropdowns, V-Sync / invert-Y /
    reduced-motion / subtitles toggles, and master/music/effects/ambience/interface/voice volume
    sliders (+ mouse-sensitivity, UI-scale) with live % / value readouts. Changes **apply live**
    (`SettingsService.Apply` on every change, so volume drags and window mode update instantly) and
    **persist** on Back, on each discrete toggle/dropdown change, and on a slider's drag-end (so a
    drag doesn't thrash the file). Reachable from **both shells**: the title `MainMenu` Settings
    button (now live) and a new `PauseMenu` Settings button both hide themselves and call
    `SettingsPanel.Open(...)`, restoring on Back. The panel sets `UiState.MenuOpen` + frees the mouse
    and runs `ProcessMode.Always` (works while paused); `PauseMenu` now suppresses its Esc-resume
    while a modal is open so Esc backs out of settings instead of resuming. Three reusable builders
    (`UiTheme.Toggle`/`Slider`/`Dropdown`) were added so the look stays one-file. Verified: build +
    71 tests + `--validate` green; in-engine boot to the menu with the wired Settings button, no
    errors. (Interactive open/drag/persist round-trip wasn't driven — the Godot MCP can't inject
    clicks; the controls are stock Godot widgets on the proven `SettingsService`.)

- [x] **24G — Localization spine: `Loc` facade + translation pipeline** `[F]` ✅
  - **Goal:** every string goes through a key from here on.
  - **Tasks:** add a `Loc` static facade over Godot's `TranslationServer`; set up
    the `.po`/CSV pipeline and an `en` base catalogue. Document the rule in
    CLAUDE.md (§6 conventions) and PRODUCTION_ROADMAP DoD #6: **no hard-coded
    player-facing strings after this lands.**
  - **Done when:** `Loc.T("key")` resolves from the catalogue; the convention is
    documented.
  - **Done:** new `src/Localization/Loc.cs` — a static facade over `TranslationServer`:
    `Initialize()` (called from `GameBootstrap._Ready` before any UI) reads the
    `data/locale/strings.csv` catalogue, builds a Godot `Translation` per locale column, registers
    them, and selects `en`; `T("key")` resolves in the active locale (an unknown key returns the key
    itself — a visible fallback), `TF("key", args)` formats, and `SetLocale` switches (guarded to
    loaded locales). The catalogue (`data/locale/strings.csv`, `keys,en` + comment lines) is seeded
    with ~55 shell strings (menu/pause/settings/save-slot) ready for the 24H retrofit; new locales are
    just a new column. The CSV is loaded at **runtime** via `FileAccess` (not the editor's CSV import)
    so the repo stays buildable/playable without an editor round-trip and the catalogue lives as plain
    data alongside the rest of `data/`. The pure `LocCatalog.Parse` (RFC-4180 quoting, comment/blank
    skip, multi-locale, empty-cell fallback) is unit-tested. The **no-hard-coded-strings rule** is now
    documented in CLAUDE.md §6 and PRODUCTION_ROADMAP DoD #6. Verified: build + 79 tests (8 new
    `LocCatalogTests`) + `--validate` green; in-engine boot logs *"Localization: loaded 55 string(s)
    across 1 locale(s); locale 'en'"* with no errors — the catalogue parses and registers live.

- [x] **24H — Retrofit shell strings through `Loc`** `[F]` ✅
  - **Goal:** prove the layer end-to-end on real UI.
  - **Tasks:** route all MainMenu/Settings/PauseMenu/save-slot strings through
    `Loc` keys; add them to the `en` catalogue. This is the template every later
    UI follows.
  - **Done when:** the shell has zero hard-coded display strings; switching the
    catalogue language visibly changes them.
  - **Done:** every player-facing string in the four shell surfaces —
    `MainMenu`, `PauseMenu`, `SaveSlotPanel`, `SettingsPanel` — now resolves through
    `Loc.T`/`Loc.TF` against `data/locale/strings.csv` (title/subtitle, all menu + pause
    buttons, every settings section header / row label / dropdown option, the slot headers,
    `Slot {0}`/`Autosave {0}`, the `— Empty —` placeholder, the composed slot metadata line
    via `slots.entry`/`slots.playtime`, and all confirm/cancel/overwrite/load/delete actions).
    Only `Log`/dev-console diagnostics and bare numerics (FPS presets) remain literal. The
    catalogue grew the two format strings (`slots.entry`, `slots.playtime`) to 57 entries. A
    `locale [code]` dev command lists loaded locales and switches the active one (re-open a
    menu to see the change — strings resolve at build time via `Loc.T`); adding a second
    language is now a CSV column. Verified: build + 79 tests + `--validate` green; in-engine
    boot logs *"loaded 57 string(s)"*, the localized title menu builds, and Continue→load
    works — no errors from the retrofit (the save-system `PersistentId` warnings are
    pre-existing and unrelated).

> **Phase 24 (Meta-Shell & Localization Spine) is complete (24A–24H).** The game boots to a
> localized title shell with multi-slot saves, autosave, settings, and an i18n spine; from here
> no player-facing string is hard-coded. **Next: Phase 25 — Region Streaming & World Map.**

---

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
    `ARCHITECTURE.md` §2.6h-2 + a "A new region" recipe in CLAUDE.md §8. Verified: build + 79 tests
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

## Phase 25.5 — Stage A Hardening & Stabilization `[F/P]`

> A consolidation pass on **everything built so far** — **debug, optimize and harden,
> no new features** — before races/region/boss stack on top. Every sub-phase is a
> focused pass on *existing* code. Keep the repo buildable/playable at every commit;
> each sub-phase ends with the relevant subsystem re-verified (build + tests +
> `--validate` + an in-engine or harness run).
>
> Two bands, do them in any order (foundation-first is reasonable): **25.5A–G** harden
> the Stage A production work (Phases 22–25 — corruption, shell, streaming, map/compass,
> fast travel); **25.5H–P** are a fresh regression/hardening pass over the foundational
> **systems 1–21**, building on (not repeating) the earlier Phase 19 (optimization) and
> Phase 20 (deep debugging) passes — now that Stage A leans on them and the codebase has
> grown. (Phase 21 Content Expansion is the ongoing content seam, not hardened here; the
> 19/20 *re-runs* fold into 25.5B profiling + 25.5G's integration sweep.)

- [x] **25.5A — Save/load integrity sweep** `[F]` ✅
  - **Goal:** clean, warning-free persistence across every system built so far.
  - **Tasks:** root-cause the recurring boot/load warnings (transient actors logging
    *"no PersistentId"*; *"orphaned state on load"* for stale `stats:*` keys) — give
    intentionally-transient actors a clear policy (stable id, or suppress the warning)
    and prune orphaned entries on load. Confirm every new `ISaveable` (corruption,
    settings, map, fasttravel, cell-persistence, save slots) round-trips with zero
    spurious warnings. Read `src/Save/` first.
  - **Done when:** a New-Game → play → F5/F9 cycle and a Continue produce **no**
    spurious save warnings; a save/load self-check (dev command or `ReproHarness`
    scenario) passes.
  - **Done:** root cause — every `ISaveable` `EntityComponent` registered with the
    `SaveManager` *unconditionally*, so **transient actors** (the training dummy, spawned
    goblins — no `PersistentId`) wrote volatile `stats:<runtimeId>` keys. Those can never
    be reclaimed after a world rebuild (the reload spawns fresh actors with new runtime
    ids), producing all three warning classes at once: *"no PersistentId"* (key build),
    *"no usable entry"* (live key absent from the save) and *"orphaned state"* (saved key
    with no live claimant). Fix: a new pure `src/Save/SaveKeyPolicy.cs`
    (`ShouldPersist`/`Key`/`IsVolatile`, Godot-free) + a `EntityComponent.RegisterSaveable()`
    gate that registers a component **only when its owner has a stable `PersistentId`** —
    transient actors now persist *nothing*, so no volatile keys are ever written. Verified
    the ordering holds: `PlayerFactory` sets `PersistentId="player"` in the initializer and
    `PersistentSpawnDirector.AssignIdentity` sets it before the actor enters the tree, both
    *before* `OnInitialize`, so every would-be-persistent actor still registers. All 11
    `ISaveable` components updated; `OnTeardown` `Unregister` stays (safe no-op when skipped).
    A `savecheck` dev command (F1) audits `SaveManager.RegisteredSaveIds` and reports any
    volatile (would-orphan) key — the runnable check; after the fix it reports **0**. Build +
    **105 tests** (14 new `SaveKeyPolicyTests`) + `--validate` (exit 0) green. (A New Game →
    F5/F9 producing a fully warning-free log is the maintainer's at-keyboard confirmation;
    the policy is proven by unit tests and the `savecheck` audit. Note: loading a *pre-fix*
    save still warns on its legacy `stats:*` keys — that's stale data, not a code bug; a
    fresh save is clean. The *"no usable entry for map/fasttravel/cell_persistence"* lines on
    old saves are normal forward-compat — those services post-date the save.)

- [x] **25.5B — Region streaming stability & profiling** `[F/P]` ✅
  - **Goal:** streaming and cell persistence are hitch-free and correct under stress.
  - **Tasks:** stress the `RegionStreamer` (fast boundary crossing, hysteresis thrash,
    multi-cell load waves vs the 1-cell/frame budget); replace the fixed 0.4s
    transition settle (`_loadingCountdown`) with a streamer-idle gate to kill pop-in;
    verify `CellPersistenceDirector` reconciliation under repeated load/unload + a full
    save/load. Profile load hitches.
  - **Done when:** rapid traversal/transitions show no thrash or visible pop-in
    (reviewed in-engine); persistence survives repeated unload/reload and save/load.
  - **Done:** the headline fix — the post-transition loading screen no longer clears on a
    **fixed 0.4 s timer** (which pops in cells whenever a region needs more than ~24
    frames of the 1-cell/frame budget). It now holds until the streamer reports the
    destination **settled**: a new `RegionStreamer.IsSettledAround(origin)` returns true
    only when the pending-load queue is drained *and* every in-range cell is loaded. The
    bootstrap (`PerformRegionLoad` → `_loadingElapsed` gate in `_Process`) resumes
    `Playing` when settled, bounded by a `LoadingMinSeconds` (0.15 s, so the screen never
    flickers) and a `LoadingMaxSeconds` (3.0 s safety cap, so a cell that fails to load can
    never hang the screen). The teleport happens before the gate starts, so it polls the
    destination position. Pure predicate `StreamDecision.IsCellSettled(distance, radius,
    isLoaded)` (= out-of-range *or* loaded) is unit-tested (3 new). **Hysteresis thrash**
    is already covered by the existing `StreamDecision` 10 m unload margin + its tests (a
    player loitering on a boundary can't thrash a cell); the 1-cell/frame budget already
    spreads load waves. **`CellPersistenceDirector`** reconciliation was reviewed against
    `src/Save/` during 25.5A and is unchanged here; 25.5A's transient-actor fix means
    streamed-in mobs no longer write orphaning state. Build + **108 tests** (3 new) +
    `--validate` (exit 0) green. (The visible "no pop-in after a transition" + a
    repeated unload/reload + save/load persistence run is the maintainer's at-keyboard
    check; the gate logic is unit-tested and the field/ordering verified by review.)

- [x] **25.5C — Corruption system hardening** `[F]` ✅
  - **Goal:** the defining mechanic is robust at its edges.
  - **Tasks:** edge-case tier thresholds/transitions, appearance/dialogue/ability
    gating, the both-endings eligibility dial; confirm `CorruptionTierChangedEvent`
    fires correctly under rapid changes and the HUD gauge/vignette stay in sync; verify
    the save round-trip.
  - **Done when:** corruption drives its consequences with no missed/duplicated tier
    events and round-trips through save/load; covered by a unit or harness check.
  - **Done:** found and fixed a real **load-desync bug**. `CorruptionComponent.Load()` re-synced
    consequence systems *"from a clean Untainted baseline"* — it only fired a
    `CorruptionTierChangedEvent` when the loaded tier was non-Untainted, hardcoding the old tier as
    `Untainted`. Correct for a fresh Continue (the component starts at 0), but **wrong for an
    in-session quickload (F9)**: loading a low-corruption save while at a high tier (or shifting
    between two non-Untainted tiers) fired *no* tier event, so the HUD vignette, the
    `CorruptionAppearanceController` (ash veins/eye-glow), NPC dread and ability gates — all of which
    set absolute state off the tier event — stayed stuck at the old tier. Fix: a pure
    `CorruptionTiers.Transition(oldValue, newValue)` → `(old, new, changed)` now drives **both**
    `Apply()` (Add/Set) and `Load()`, so loading emits the same event the live path would, re-syncing
    every consumer in the correct direction (including *down*). Tier mapping, monotonicity and the
    endings-eligibility dial were already pinned; added 4 `Transition` tests covering the
    upward/downward/same-band/between-non-Untainted cases (the downward case is the bug). Rapid
    changes already fire exactly one tier event per band-crossing `Apply` (no-op when unchanged), and
    a multi-band jump emits one event carrying the true old+new — consumers set absolute state, so no
    miss/dup. Build + **112 tests** (4 new) + `--validate` (exit 0) green. (The visible vignette/
    appearance reset on an F9 that lowers corruption is the maintainer's at-keyboard check via the
    23B `corrupt` dev command; the transition logic is unit-tested and `Load`/`Apply` now share it.)

- [x] **25.5D — Meta-shell, settings & state-machine robustness** `[F]` ✅
  - **Goal:** the shell never wedges or corrupts a slot.
  - **Tasks:** exercise the save-slot lifecycle (new/continue/load/delete, autosave
    ring rotation, quick/manual save), slot-metadata integrity, settings apply-on-boot
    across every category, and the MainMenu↔Loading↔Playing↔Paused machine under odd
    sequences (load while paused, delete the active slot, etc.).
  - **Done when:** every shell path is exercised with no soft-locks, lost slots, or
    unapplied settings.
  - **Done:** audited the three shell surfaces and fixed the one real defect — **unapplied
    settings**. The 24F settings panel exposes a Mouse-Sensitivity slider (0.05–2.0) and an
    Invert-Y toggle, but `PlayerController` used a *hardcoded* `MouseSensitivity = 0.0028f`,
    never read `SettingsService`, and never honoured Invert-Y — so both controls did nothing.
    Fix: the controller now resolves `SettingsService` from the `ServiceLocator` in
    `OnInitialize` and scales its base sensitivity by the live multiplier each frame, with
    Invert-Y flipping the pitch axis; the maths is a pure `SettingsMath.LookStep` /
    `ApplyPitch` pair (5 new tests; the default 1.0 multiplier reproduces the old feel
    exactly, so no behaviour change at defaults). Reading live each frame means a mid-game
    change in the pause→settings panel applies immediately, no re-apply plumbing needed.
    **State machine** (audited, sound): `GameManager.TogglePause` only acts in Playing/Paused,
    so Esc during Loading/MainMenu can't wedge a transition; only `Paused` pauses the tree, so
    `Loading`/`Playing` always unpause (a load *while* paused correctly resumes). **Slot
    lifecycle** (audited, sound): `DeleteSlot` removes files+dir and leaves `ActiveSlot`
    dangling, but the next F5 just recreates that slot (no soft-lock), and Continue is disabled
    when no saves exist; autosave-ring rotation and slot headers are already unit-pinned. Build +
    **117 tests** (5 new) + `--validate` (exit 0) green. (Feeling the sensitivity slider/Invert-Y
    actually move the camera is the maintainer's at-keyboard check; the look maths is unit-tested
    and the `SettingsService` wiring verified against the boot order — registered at line 118
    before the player is built.)

- [x] **25.5E — UI/HUD interaction & input hardening** `[F/P]` ✅
  - **Goal:** the new UI surfaces compose cleanly.
  - **Tasks:** audit mouse-mode/`UiState.MenuOpen` correctness across overlapping
    menus (modal map vs pause vs inventory vs dialogue vs dev console), focus
    navigation, the compass/vignette/loading-screen overlays, reduced-motion guards,
    and a localization sweep (no hard-coded player strings slipped through since 24G).
  - **Done when:** opening/closing any combination of menus leaves the mouse + player
    control in the right state; no untranslated UI strings remain.
  - **Done:** fixed a real **overlapping-menu mouse bug**. `UiState.MenuOpen` was a single
    `bool` that six surfaces (Inventory/Crafting/Dialogue/Map panels, SettingsPanel, DevConsole)
    each set independently, *and* each drove the OS mouse mode off its own local `open` flag.
    Opening two at once — e.g. the **F1 dev console over an open inventory**, or settings over a
    modal — then closing the inner one flipped `MenuOpen` to false and **recaptured the mouse
    while the outer menu was still visible**, so the player looked around behind it. Root cause:
    a bool can't represent "two menus open", and each close read its *local* state, not the
    aggregate. Fix: `UiState` is now an **owner set** (`HashSet<object>`, `Open(this)`/
    `Close(this)`, `MenuOpen => count > 0`) and every mouse decision reads the aggregate
    `UiState.MenuOpen`, so the mouse only recaptures when the *last* menu closes. Pure and
    Godot-free → 4 new `UiStateTests` pinning the exact overlap case. No `PlayerController`/
    `PauseMenu` change — both already consume `UiState.MenuOpen`, which now means the right
    thing. **Audits (no change):** localization sweep of `src/UI` is clean — the only literal is
    the dev console's `PlaceholderText`, exempt per CLAUDE.md §6; `Settings.ReducedMotion` is a
    documented Phase-54 placeholder (nothing consumes it yet, by design, not a regression);
    overlay `Layer` ordering + `MouseFilter = Ignore` on non-interactive overlays read correct.
    Build + **121 tests** (4 new) + `--validate` (exit 0) green. (Opening the inventory, pressing
    F1 over it, and closing the console to confirm the mouse stays free until the inventory also
    closes is the maintainer's at-keyboard check; the count logic is unit-tested.)

- [x] **25.5F — Validator & analytics coverage** `[F]` ✅
  - **Goal:** `--validate` is a real gate for the new content/id domains.
  - **Tasks:** grow `ContentValidator` to cover the Stage A domains added since 22
    (regions, cells, travel nodes, corruption-gated content, locale-key presence) and
    confirm the analytics spine logs the new events; close validator gaps.
  - **Done when:** `--validate` catches a deliberately-broken region/cell/travel/locale
    reference; analytics records the Stage A events.
  - **Done:** closed two real validator gaps and the analytics gap. **Locale** had *no*
    validation: a new pure `LocaleAudit.Audit(csv, "en")` (Godot-free, unit-tested) flags
    duplicate keys (`LocCatalog.Parse` dedupes last-wins, silently dropping a string) and keys
    with no default-locale value (the UI falls back to the raw key, e.g. `menu.settings`); a new
    `ContentValidator.ValidateLocale` reads `data/locale/strings.csv` and runs it. **Region/cell
    geometry** was unchecked: `ValidateRegions` now also asserts the region `SpawnPoint` and each
    cell `Center` sit inside `Bounds` (`Aabb.HasPoint`) — `SpawnPoint` is where every portal *and*
    fast-travel node lands, so an out-of-bounds spawn (the scannable "travel" reference) drops the
    player in the void. **Analytics:** `AnalyticsSink` now subscribes to the four Stage-A events
    that already fire — `RegionTransitionRequested` → `region_transition`, `FastTravelRequested` →
    `fast_travel`, `CorruptionTierChanged` → `corruption_tier{from,to}`, `GameSaved` →
    `save{slot,autosave}` — each a one-line `Record` to the session `.jsonl`. **Proven**, not just
    reasoned: temporarily duplicating a `strings.csv` key *and* moving the EmberCrown `SpawnPoint`
    to `(9999,…)` each made `--validate` exit **1** naming the exact issue; both reverted, exit 0
    again. Build + **126 tests** (5 new `LocaleAuditTests`) + `--validate` (exit 0) green.
    (ponytail: travel-node *components* live in cell `.tscn`s, validated at runtime on discovery —
    the geometry gate covers the authored `SpawnPoint`, not every scene. The analytics lines firing
    in a real session log is the maintainer's at-keyboard check.)

- [x] **25.5G — Integration regression sweep & known-issues ledger** `[C/P]` ✅
  - **Goal:** declare the Stage-A-so-far foundation stable.
  - **Tasks:** a full play-through touching every system 22–25 together; fix
    interaction bugs found; record residual issues + perf baselines in a `docs/`
    ledger (e.g. `docs/STAGE_A_STATUS.md`).
  - **Done when:** the integrated Stage A loop runs end to end with no known
    regressions; the ledger captures what remains.
  - **Done:** ran the full automated regression battery — `dotnet build` clean,
    **126 tests** pass, `--validate` (`ContentValidator.RunAll`) exit 0, and a headless
    boot capture (`run_project`→`get_debug_output`→`stop_project`) that loads all 14
    databases, seeds the enemy registry, passes `ContentValidator`, and reaches
    `Boot → MainMenu` with **`errors: []`**. The sweep surfaced and fixed the
    fast-travel-traps-player bug the maintainer reported (recorded the post's own position
    as the landing point → now records the attuning player's walkable spot, PR #85).
    Authored **`docs/STAGE_A_STATUS.md`** — the Stage-A ledger: the green automated battery,
    the 22–25 integration loop (automated vs at-keyboard per surface), the known-issues table
    (1 fixed, 3 documented expected-behaviours, 0 open defects), perf baselines (streaming
    budget, loading-gate min/max, suite runtime) with a live-profile TODO, and the ordered
    play-through checklist. **Honest `[C/P]` boundary:** the automated loop is green and the
    ledger captures what remains; the full human play-through (every 22–25 system exercised
    *together* in a live game — undrivable via MCP) is the maintainer's remaining sign-off,
    itemized as the at-keyboard checklist in §4/§6 of the ledger.

> **Systems 1–21 hardening (25.5H–P).** A fresh pass over the foundational systems the
> whole game stands on. Each sub-phase clusters a few related systems; the bar is the
> same — root-cause bugs, tighten edge cases, profile hot paths, leave the subsystem
> re-verified (build + tests + `--validate` + an in-engine/harness run), **no new
> features.** Reference `docs/ARCHITECTURE.md` for each system before touching it.

- [x] **25.5H — Core, entity/component, events, stats & pooling** `[F]` (systems 1) ✅
  - **Goal:** the architectural spine is leak-free and correct.
  - **Tasks:** audit `EventBus` subscribe/unsubscribe symmetry (the bootstrap already
    warns on handlers that survive teardown — drive that to zero), `ServiceLocator`
    registration lifetimes, autoload order assumptions, the `EntityComponent`
    `OnInitialize`/`OnTeardown` lifecycle (never `_Ready`), `StatModifier` stack/unstack
    correctness, and `NodePool` reset-on-reuse. Read `src/Core`, `src/Entities`,
    `src/Stats` first.
  - **Done when:** no leaked handlers on scene teardown; stat modifiers apply/remove
    cleanly; pooled nodes re-arm with no stale state (covered by a unit/harness check).
  - **Done:** audited the spine — **largely clean**, with one latent fragility fixed.
    *Audit:* **EventBus** subscribe/unsubscribe is symmetric per-type across every subscriber
    (components unsubscribe in `OnTeardown`, UI/directors in `_ExitTree`; both fire on
    `QueueFree`). **NodePool** is sound — reset-on-reuse is the caller's job by design, and
    `SpellProjectile.Launch` re-arms every field per shot (`_resolved`/`_life`/direction/packet/
    material/`Monitoring`), no stale carry-over. **Stat modifiers** are correct and now fully
    pinned (`EquipmentComponent` sources each modifier to the item instance and removes by
    source). **ServiceLocator** stale refs (player/dummy/streamer never `Unregister`ed) are
    *quit-only* — `BeginSession` guards `_sandboxBuilt`, so the sandbox is never rebuilt
    in-session; a fix would be dead code, so left as-is. *Fix:* `EntityComponent` ran
    `OnTeardown` on **every** `_ExitTree` (Godot fires it on each tree removal) while
    `OnInitialize` runs once in `_Ready` — a detach/re-attach or double `_ExitTree` would
    double-unsubscribe/unregister and leave the component dead. Added an `_initialized` guard so
    init/teardown pair **exactly once**; identical on the normal path, safe against a double exit.
    *Tests:* completed the stat-unstack coverage `StatTests` lacked — `RemoveModifier`,
    `ClearModifiers`, `Changed`-on-remove, and `RemoveModifiersFromSource` stripping all of a
    source's stacked modifiers. Build + **130 tests** (4 new) + `--validate` (exit 0) + a clean
    headless boot capture (all databases → `MainMenu`, `errors: []`) green.

- [x] **25.5I — Player controller, locomotion & combat framework** `[F]` (systems 2, 3) ✅
  - **Goal:** movement and the damage pipeline are tight and predictable.
  - **Tasks:** input handling under pause/menu (mouse-mode + `UiState`), locomotion edge
    cases (slopes, ledges, sprint/jump), and the combat pipeline — `DamagePacket`,
    hit/hurtbox `Area3D` overlap timing (the known gotcha: `Hitbox` polls per physics
    frame), team/friendly-fire, poise/stagger/block. Read `src/Player`, `src/Movement`,
    `src/Combat`.
  - **Done when:** no missed/double hits across the i-frame and overlap-timing cases;
    movement has no stuck/clip states in a review pass.
  - **Done:** audited the pipeline — **solid**, with one input fix + the missing combat-formula
    coverage closed. *Audit:* the `Hitbox` polls overlaps each physics frame across the active
    window and dedupes per target via `_alreadyHit` (cleared per swing) — no missed/double hits
    from the Monitoring-next-frame gotcha; owner-skip and same-`Team` friendly-fire skip both hold;
    `MeleeWeaponComponent` gates re-attack on phase + stamina + stagger and rolls a fresh
    `DamagePacket` per swing; `CombatComponent` block→stamina, poise→stagger and block-prevents-poise
    are correct. Locomotion's grounded `velocity.Y` isn't zeroed, but `MoveAndSlide` clamps it and
    jump sets absolute velocity, so no stuck/clip state. *Fix:* the player set `_combat.IsBlocking`
    only on the live path, so raising the guard then opening a menu could **strand "blocking" true**;
    the not-playing and menu early-returns now call `DropHeldInput()` to release it (live input is
    re-read on the first frame back in control). *Coverage:* `CombatMath` had **no tests** despite
    running on every hit — extracted the pure `ArmorMultiplier(armor)` kernel (the `100/(100+armor)`
    curve) and pinned it (curve points, negative-armor clamp, monotonic-and-bounded). Build +
    **144 tests** (6 new) + `--validate` (exit 0) + clean headless boot (`errors: []`) green.

- [x] **25.5J — Enemy AI, perception & spawning** `[F/P]` (system 4) ✅
  - **Goal:** AI is correct and cheap, including across streaming.
  - **Tasks:** perception-FSM transitions (aggro/leash/search/return), the far-sleep /
    perception-cache throttle (`EnemyAIComponent`), `EnemySpawnDirector` density + clean
    despawn, and AI behaviour for enemies inside *streamed* cells (load/unload while
    engaged). Read `src/Enemies`.
  - **Done when:** AI transitions are correct with no thrash; far enemies sleep; no
    orphaned/duplicated enemies across cell load/unload; profiled cost is flat.
  - **Done:** audited the AI — **solid**, with one spawn fix + the perception kernel pinned.
    *Audit:* the perception FSM transitions cleanly (Idle/Patrol→Combat on sight or provoke,
    Combat→Investigate on lost LoS, Investigate→Patrol on timeout, Retreat at low health, stand-down
    when no longer hostile) with no thrash; the far-sleep LOD (`ActiveDistance` 45 m → tick every
    `SleepInterval`, shadow off) and the perception cache (`PerceptionInterval`-throttled LoS raycast)
    both work; `OnTeardown` unsubscribes (verified in 25.5H), so an enemy freed by a cell unload or
    death leaves no orphaned handler — and there's no in-session world rebuild to duplicate it. *Fix:*
    `EnemySpawnDirector._respawnTimer` started at 0 and was only reset after a spawn, so the **first
    replacement after a kill popped instantly**; `OnEnemyRemoved` now restarts the respawn clock, so
    every refill waits the full `RespawnInterval` (consistent density cadence). *Coverage:* extracted
    the pure `EnemyPerception.InViewCone(forward, toTarget, fov)` kernel (the FOV cone run on every
    sight tick) from `EnemyAIComponent` and pinned it (dead-ahead/behind/90°, the ±half-FOV boundary,
    degenerate-vector default-visible). Build + **150 tests** (6 new) + `--validate` (exit 0) + clean
    headless boot (`errors: []`) green. (A live profile of a crowd's flat cost is the maintainer's
    F4 check; the throttle/LOD logic is verified by review.)

- [x] **25.5K — Inventory, equipment & loot generation** `[F]` (systems 5, 6, 7) ✅
  - **Goal:** items move and roll correctly.
  - **Tasks:** stack merge/split, capacity/carry-weight, equip/unequip stat application
    through `EquipmentComponent` → `StatsComponent`, affix-roll distribution/rarity
    gating, loot-table edge cases (empty/zero-weight/over-quantity), pickup despawn +
    persistence. Read `src/Items`, `src/Loot`.
  - **Done when:** equip bonuses apply/remove exactly; loot rolls stay in-bounds; a
    fuzz/harness check over rolls passes.
  - **Done:** audited the three systems — **solid** — and closed the phase's named fuzz gap.
    *Audit:* `InventoryComponent.AddInstance` tops up compatible stacks then fills slots to `MaxStack`,
    respects `Capacity`, and returns the amount stored (merge/split correct); `EquipmentComponent`
    applies bonuses as `StatModifier`s **sourced to the instance** and removes them by source on
    unequip, so equip/unequip apply and remove **exactly** (weapon swap restores the default; `Load`
    clears before re-applying); `LootGenerator.Generate` guards null table, empty id, missed
    drop-chance, missing template, `min>=max`/`<=0` quantity, and an all-zero-weight pool. *Coverage:*
    the roll math was Godot-RNG-bound and untested — extracted the pure kernels `LootRarity.Select(
    quality, roll01)` and `AffixDefinition.BlendValue(min, max, quality, roll01)` (no behaviour change;
    `Roll` now just feeds `rng.Randf()`), then added `LootRollFuzzTests` sweeping the full `[0,1)`
    sample space: rarities always valid + monotonic in the sample + higher quality biases upward;
    affix values always in `[min,max]` (incl. equal/inverted/thin bounds), non-decreasing in sample
    and quality, endpoints reachable. Build + **166 tests** (16 new) + `--validate` (exit 0) + clean
    headless boot (`errors: []`) green. *Latent (not fixed, ponytail):* `AddInstance` shares one
    `ItemInstance` across the stacks it makes for a **unique** item added with `quantity>1` — not
    reachable today (loot emits one-per-unit; equippable recipes output qty 1).

- [x] **25.5L — Progression, quests & dialogue** `[F]` (systems 8, 9, 10) ✅
  - **Goal:** progression and narrative plumbing are robust.
  - **Tasks:** XP curve/level-up boundaries, perk apply/respec, quest objective
    advance/complete/prereq chains and reward grant, dialogue graph traversal +
    condition/effect resolution (extend the existing `DialogueGraphAnalysis`), and
    story-flag persistence. Read `src/Progression`, `src/Quests`, `src/Dialogue`.
  - **Done when:** quests advance/complete with no stuck objectives; dialogue branches
    resolve conditions/effects correctly; flags round-trip (harness/unit covered).
  - **Done:** audited the three systems — **solid** — and pinned the two boundary kernels.
    *Audit:* `QuestLogComponent.Advance` clamps each count to `RequiredCount` and skips complete
    objectives, `TryComplete` flips to `Completed` and grants rewards exactly once, `CanStart` gates on
    `PrerequisiteQuestId` via `IsCompleted`, and `StartQuest` runs `TryComplete` immediately so a
    0-objective / 0-required quest can't stick — no stuck path. `StoryFlagsComponent` is a guarded
    `HashSet` with a HashSet↔array `Save`/`Load` (round-trips by construction). Dialogue graph structure
    is already pinned by `DialogueGraphAnalysis` + tests; `DialogueSession.Evaluate`/`ApplyEffect` are a
    plain switch over quest-log/flag/corruption state. *Coverage:* extracted the pure
    `ProgressionMath.XpToReach(level, baseXp, exponent, maxLevel)` + `Resolve(level, xp, maxLevel,
    addedXp, xpToReach)` out of `ProgressionResource`/`ProgressionComponent.AddXp` (no behaviour change),
    and `ObjectiveProgress.IsComplete`/`AllMet` out of `QuestProgress`. New `ProgressionMathTests` pin
    the curve (positive, strictly increasing, 0 at/after cap) and the level-up boundaries (one-short =
    no level, **exact threshold = level with 0 remainder**, multi-level overflow, excess discarded at
    cap, `need<=0` guard against an infinite loop, non-positive grant no-op); `ObjectiveProgressTests`
    pin the completion boundary (0/negative requirement met immediately = no stuck, exact, over-count,
    mixed `AllMet`). Build + **186 tests** (20 new) + `--validate` (exit 0) + clean headless boot that
    auto-loaded into Playing and ran the quest/XP path (`errors: []`) green.

- [x] **25.5M — Magic, status effects & combat math** `[F]` (system 12) ✅
  - **Goal:** spellcasting and effects resolve consistently.
  - **Tasks:** cooldown/mana gating, projectile pooling reuse (`SpellProjectile`), AoE
    resolution, status-effect stacking/refresh/expiry and DoT tick cadence, and
    `CombatMath` roll/scaling correctness. Read `src/Magic`, `src/Combat/CombatMath`.
  - **Done when:** effects stack/expire correctly with no leaked pooled projectiles;
    damage/heal math is pinned by a unit check.
  - **Done:** audited magic/effects — **solid** — and pinned the damage + DoT-cadence kernels.
    *Audit:* `SpellcastingComponent.CanCast` requires cooldown ≤ 0 **and** mana ≥ `ManaCost`; `TryCast`
    deducts mana + stamps the cooldown. `SpellProjectile` is inert until `Launch` arms it and resolves on
    a hostile hit **or** `_life<=0` timeout → `_resolved=true` then a deferred `Release` → the pool
    reclaims it (a projectile that hits nothing still times out and returns — **no leak**); `_resolved`
    guards double-resolve. `StatusEffectsComponent` **refreshes** (never unbounded-stacks) on re-apply,
    sources stat modifiers to the effect and strips them on expiry/`ClearAll`/death, and its DoT loop is
    guarded by `HasDamageOverTime => DamagePerTick>0 && TickInterval>0`; heal is a flat `_stats.Heal`.
    *Coverage:* extracted the pure `CombatMath.ScaleDamage(base, power, scaling)` (the offensive base +
    power share behind every hit/cast; `RollAttack`/`RollSpell` route through it) and
    `StatusMath.AdvanceDot(tickTimer, delta, interval)` (the DoT catch-up loop, extracted from `Tick`) —
    both no behaviour change. Extended `CombatMathTests` (ScaleDamage for both 0.5/0.6 constants + a
    scale→`ArmorMultiplier` pipeline pin) and added `StatusMathTests` (no tick pre-interval, exact
    boundary, multi-tick catch-up across a large delta, carry-over remainder, `interval<=0` no-loop
    guard). Build + **197 tests** (11 new) + `--validate` (exit 0) + clean headless boot into Playing
    with combat damage flowing through the pipeline (`errors: []`) green.

- [x] **25.5N — World clock/weather/encounters, NPC schedules & procedural events** `[F]` (systems 11, 13, 17) ✅
  - **Goal:** the living-world directors are stable, including across streaming.
  - **Tasks:** `WorldClock` day/phase transitions, weather selection/transition, the
    encounter director's day-phase gating + spawn cleanup, NPC `ScheduleComponent`
    routines off the clock, and the world-event director lifecycle (announce → track →
    reward → cooldown). Verify all behave across region transitions. Read `src/World`,
    `src/Npc`.
  - **Done when:** clock/weather/encounter/event/schedule cycles run for a long session
    with no stuck states, leaked spawns, or double-fires across transitions.
  - **Done:** found and fixed a real **spawn leak across region transitions**, and pinned the two pure
    time kernels. *Fix:* `EncounterDirector` and `WorldEventDirector` spawn enemies as children of the
    **persistent** bootstrap root (`GetParent().AddChild`), but `PerformRegionLoad` only unloads the
    streamed cells — so on a transition their spawns orphaned into the new region and kept ticking. Both
    now subscribe to `RegionTransitionRequestedEvent`: the event director aborts an in-progress event
    through its existing `Fail` path (despawn raiders + stamp cooldown, no stuck `_active`), and the
    encounter director tracks its spawns and frees them (`_alive` self-heals via `TreeExited`).
    *Audit (no change):* `WorldClock` wraps via `Mathf.PosMod` and fires the hour/phase change once per
    hour; `WeatherDirector` rolls weighted selection on a timer; `WorldEventDirector` allows one
    `_active`, stamps the cooldown on `End`; `ScheduleComponent` yields to panic/dialogue and re-picks
    its block on resume. *Coverage:* pinned `DayPhases.Of` (phase boundaries + negative/>24 wrap) and
    extracted the schedule wrap-lookup into pure `ScheduleMath.ActiveEntryIndex(startHours, hour)` (from
    `ScheduleResource.EntryForHour`) with tests (exact start, mid-block, before-first wrap-to-last,
    single, empty, unordered). Build + **217 tests** (20 new) + `--validate` (exit 0) + clean headless
    boot into Playing with both directors online (`errors: []`) green. *Latent (not fixed, ponytail):*
    the legacy EmberCrown goblin camp (`EnemySpawnDirector`) is persistent and not region-scoped, so it
    keeps spawning at its fixed point after a transition — early-sandbox content, a separate concern.

- [x] **25.5O — Crafting & faction/reputation systems** `[F]` (systems 15, 16) ✅
  - **Goal:** crafting and standing behave at their edges.
  - **Tasks:** recipe learn/station-gating/ingredient consumption/output rolling, and
    faction reputation thresholds → hostility, `FactionComponent` tags driving enemy AI
    aggression, kill/standing penalties. Read `src/Crafting`, `src/Factions`.
  - **Done when:** crafting consumes/produces exactly; reputation tiers flip
    hostility correctly and persist.
  - **Done:** audited both systems — **solid** — and pinned the two load-bearing predicates.
    *Audit:* `CraftingComponent.Craft` is `CanCraft`-gated (knows recipe + `StationAccepts` + output
    exists + has ingredients) then removes each ingredient and adds the output — **consumes then produces
    exactly**; non-Common equippable output rolls a fresh affixed instance per unit (no shared-instance
    aliasing), plain output stacks, a deleted output fails cleanly; `Learn` de-dupes. `ReputationComponent.Add`
    clamps to `[Min,Max]` and only fires on an actual change; `Effective` subtracts the corruption `Dread`
    and re-clamps; `IsHostile` is `TierOf <= HostileThreshold`; a player kill applies `KillReputationPenalty`
    and echoes ± through the faction's enemy/ally web; `Save`/`Load` clamp + re-publish (round-trips by
    construction). *Coverage:* pinned `ReputationTiers.Of` (all seven band edges -100→Hated … 90→Allied +
    monotonic-non-decreasing across `[Min,Max]`) and exposed `CraftingComponent.StationAccepts`
    (private→public, pure predicate) with tests (Hand crafts anywhere; a station recipe only at its exact
    station). Build + **242 tests** (25 new) + `--validate` (exit 0) + clean headless boot into Playing
    (`errors: []`) green.

- [x] **25.5P — Legacy UI panels & HUD** `[P/F]` (systems 14, 18) ✅
  - **Goal:** the older UI surfaces are consistent and warning-free.
  - **Tasks:** the pre-25 panels (inventory, equipment, crafting, dialogue, quest log,
    pause) on `UiTheme` — dirty-flag rebuild correctness (never rebuild during a button
    signal), tooltip system, nameplate/interaction-prompt/toast feed, and fold these
    into the 25.5E mouse-mode/`UiState` audit so old + new menus compose. Confirm no
    hard-coded strings remain (route through `Loc`). Read `src/UI`.
  - **Done when:** every panel opens/closes/rebuilds cleanly with correct mouse-mode and
    no console errors; no untranslated legacy strings.
  - **Done:** the mechanics were already solid; the real gap was **localization**, now closed.
    *Audit:* the post-25 panels (Settings, SaveSlot, MainMenu, Pause, Crafting, Map) already route
    through `Loc`, build on `UiTheme`, and use `UiState.Open/Close` + dirty-flag rebuild (verified 25.5E);
    re-reading the four pre-25 surfaces confirmed each modal frees the mouse via `UiState` and rebuilds
    from a `_dirty` flag in `_Process`, never mutating the tree inside a button signal (`QuestLogPanel`/
    `GameHud` are intentionally non-modal HUD overlays). *Fix:* routed ~30 hard-coded player-facing
    strings through `Loc.T`/`Loc.TF` across `InventoryPanel` (the CHARACTER screen — `char.*`),
    `QuestLogPanel` (`questlog.*`), `DialoguePanel` (`dialogue.leave`), `GameHud` (`hud.*`) and two
    `CraftingPanel` stragglers (`craft.recipes_none`/`craft.craft`), with 33 new keys in
    `data/locale/strings.csv` (80 → **113** strings); interpolated lines use `Loc.TF` with `{0}`
    placeholders (sign/precision formats pre-formatted to args). `DebugHud` left exempt (F3 dev overlay,
    CLAUDE.md §6). Pure glyphs/number-unit fragments (`✓`/`•`/counters/separators) carry no language and
    stay. Build + **242 tests** (unchanged) + `--validate` exit 0 (locale audit green: every key resolves,
    no dupes/missing) + clean headless boot into Playing (`errors: []`); a residual-literal grep over the
    four panels is empty. On-screen rendering is the maintainer's at-keyboard check.

> **Stage A Hardening (Phase 25.5A–P) complete.** Every Stage-A subsystem audited; real bugs fixed
> (save-key collisions, corruption load desync, mouse recapture, fast-travel trap, lifecycle guard,
> respawn cadence, block-strand, cross-transition spawn leak) and the load-bearing pure kernels pinned
> by **242 unit tests**. The whole game stays buildable, `--validate`-clean, and boots `errors: []`.

---

## Phase 26 — Playable Races & Character Creation `[F]`

> Six LORE races as data-driven trait sets + a creator that writes them into the
> player at spawn.

- [x] **26A — `RaceResource` + `RaceDatabase`** `[F]` ✅
  - **Goal:** races are data.
  - **Tasks:** add `RaceResource` (`.tres`: id, name, `AttributeSet` deltas, innate
    perk/ability ids, starting reputation tweaks, appearance option ids) +
    auto-indexed `RaceDatabase` (mirror `ItemDatabase`). No new inheritance.
  - **Done when:** a `RaceResource` loads and indexes; the schema covers all six
    LORE races' needs.
  - **Done:** new `src/Races/` system — `RaceResource` (`[GlobalClass] : Resource`: `Id`, `DisplayName`,
    multiline `Description`, sparse `StatDeltas` [`RaceStatDelta` sub-resource = `StatType` + signed flat
    `Amount`], `InnatePerkIds`/`InnateSpellIds`/`AppearanceOptionIds` string arrays, `ReputationTweaks`
    [`RaceReputationTweak` = faction id + amount], with typed `StatDeltaList()`/`ReputationTweakList()`
    read-backs mirroring `ScheduleResource`). `RaceDatabase` copies `PerkDatabase` (auto-scans
    `res://data/races`, `Get`/`All`, dup-id warn) and registers in `ContentDatabases.InitializeAll`.
    `ContentValidator.ValidateRaces` gates innate perk→`PerkDatabase`, spell→`SpellDatabase`, and
    reputation faction→`FactionDatabase` refs (+ duplicate race ids). Schema covers all six LORE races'
    needs (Valari magic, Grondar strength, Sylthari survival, Draekyn dragon-ability seed, Umbral stealth
    + distrust, Human flexible). Proof `data/races/Human.tres` loads. Composition only — a new race is a
    `.tres`, no code. Build + **242 tests** + `--validate` exit 0 + boot logs `RaceDatabase loaded 1
    race(s)` (`errors: []`).

- [x] **26B — Author the six race `.tres`** `[C]` ✅
  - **Goal:** Human, Valari, Grondar, Sylthari, Draekyn, Umbral exist as data.
  - **Tasks:** author all six `data/races/*.tres` per LORE traits (Valari magic
    affinity, Grondar strength/endurance, Sylthari wildlife communion, Draekyn
    dragon ability seed, Umbral stealth, Human flexible). Reference existing
    perks/stats; create any small new perk `.tres` they need (CLAUDE.md §8 "new
    perk"). Pure content.
  - **Done when:** six valid race `.tres`; `validate` green; traits reference real
    ids.
  - **Done:** authored the five remaining races (Human shipped in 26A) — **Valari** (+3 Int/+4 SpellPower/
    +20 Mana, innate `spell.firebolt`), **Grondar** (+5 Str/+4 End/+3 Vit/+20 HP/−0.4 Move, innate
    `perk.toughness`), **Sylthari** (+3 Dex/+2 Vit/+0.4 Move, innate `perk.endurance_training`),
    **Draekyn** (+2 Str/+2 SpellPower/+0.2 CritDmg, innate `spell.fireball` dragon-breath seed,
    `faction.villagers −10` feared), **Umbral** (+4 Dex/+0.4 Move/+0.03 Crit, innate `perk.precision`,
    `faction.villagers −15` distrusted). **No new perks needed** — innate spells + stat deltas + the three
    ungated perks (toughness/endurance_training/precision) cover every trait, so this stayed pure content.
    `AppearanceOptionIds` left empty (the catalogue lands in 26D). All traits reference real ids;
    `--validate` exit 0 (`ValidateRaces` green) + boot logs `RaceDatabase loaded 6 race(s)` (`errors: []`).
    242 tests unaffected (content-only).

- [x] **26C — `PlayerFactory` consumes a creation profile** `[F]` ✅
  - **Goal:** the chosen race actually shapes the player.
  - **Tasks:** add a `CharacterProfile` (race id, name, appearance, background) and
    have `PlayerFactory` apply race deltas as `StatModifier`s, seed innate perks,
    and apply reputation tweaks at spawn (CLAUDE.md §6 factory rules — set props
    before `AddChild`). Persist the profile in the save header.
  - **Done when:** spawning with different races yields different starting stats/
    perks/standing; the profile saves/loads.
  - **Done:** `CharacterProfile` (pure C# — `RaceId`/`CharacterName`/`AppearanceOptionIds`/`Background`,
    `Human` default, `ToHeaderFields`/`FromHeaderFields` round-trip). New `RaceComponent` added **last**
    in `PlayerFactory` (so Stats/Perks/Spellcasting/Reputation are initialized) applies the race in
    `OnInitialize`: stat deltas → flat `StatModifier`s sourced to itself (remove-then-add → idempotent,
    `RefillResources`), and on New Game grants innate perks (new free `PerksComponent.GrantFree`), `Learn`s
    innate spells, and `Add`s reputation tweaks. `PlayerFactory.Create(pos, profile, applyStartingGrants)`
    (parameterless overload keeps Human default). Bootstrap holds `_activeProfile` — New Game uses Human
    (26D's creator wires the chosen one here), Load reads the slot header → rebuilds the profile and spawns
    with `applyStartingGrants:false` (the save overlay restores the granted perks/spells/rep). Profile
    persists via `BuildSaveHeader` + `SaveSlotInfo` (`race_id`/`char_name`). Dev `race [id]` command
    live-applies a race for at-keyboard verification (stat swap + idempotent perk/spell re-grant; skips
    reputation to avoid accumulation). Build clean + **246 tests** (+4 `CharacterProfileTests` round-trip)
    + `--validate` exit 0 + boot through the load path logs `Loaded game … as Wanderer (race.human)`
    (`errors: []`). `AppearanceOptionIds`/`Background` carried + persisted but not yet consumed (26D).

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

## Phase 29.5 — Spellcraft & the Fading Weave `[F]`

> Magic made deep + original. Phase 12 built the *system*; this gives it identity and
> depth so magic is a real build spine for the slice (DESIGN §1.5). All new *mechanics*
> land here, before the G2 freeze; breadth/content is woven through 26/34/35/42/47–48/51.
> Theme: magic is the fading **Weave** of a dying world — recover lost spellcraft, and
> corruption is the darker shortcut (extends 23H). Read `src/Magic/` first.

- [ ] **29.5A — Cast archetypes: Charged + Channeled** `[F]`
  - **Goal:** casts have feel beyond fire-and-forget.
  - **Tasks:** add a `CastMode` (Instant · Charged · Channeled) to `SpellResource`
    (append-only enum), layered on the existing Projectile/Area/Self *shape*; give
    `SpellcastingComponent` charge build-up (power/radius scale with hold) and channel
    (sustained tick at a mana-per-second cost, interruptible). Drive from the player
    controller. Persists nothing new (transient cast state).
  - **Done when:** one charged and one channeled spell cast and feel distinct from instant;
    mana drains correctly; round-trips (known spells already save).
- [ ] **29.5B — School identities + status effects** `[F/C]`
  - **Goal:** each `DamageType` school plays differently, not just tint+resist.
  - **Tasks:** author the signature mechanic + status effects per school — Fire ignite/DoT
    stacks, Frost chill→freeze, Lightning chain-to-nearby, Arcane ward/dispel, Nature
    heal-over-time/totem, Necrotic lifesteal/decay (corruption-gated per 23H). Mostly new
    `StatusEffectResource` `.tres` + small resolver hooks (CLAUDE.md §8).
  - **Done when:** every school has a distinct on-hit behavior provable in the sandbox.
- [ ] **29.5C — Spell scaling + school mastery track** `[F]`
  - **Goal:** "hard to master" magic ceiling that isn't just bigger numbers.
  - **Tasks:** extend `CombatMath.RollSpell` scaling off SpellPower/Intelligence; add a
    per-school **mastery** that ranks by casting that school and empowers/unlocks its
    spells (reuse perk/progression patterns; `ISaveable`). New `MasteryComponent` or fold
    into progression.
  - **Done when:** casting a school raises its mastery, which measurably empowers it;
    mastery round-trips through save/load.
- [ ] **29.5D — Reactive spell combos** `[F]`
  - **Goal:** cross-school reads, the magic analogue of the combat read.
  - **Tasks:** a small `SpellCombo` resolver that inspects the target's
    `StatusEffectsComponent` on hit and fires a bonus effect (Chill + Lightning = shatter,
    etc.), data-described where possible.
  - **Done when:** at least two combos trigger and are documented; no hard-coded one-offs.
- [ ] **29.5E — The fading Weave (region potency + spell recovery)** `[F]`
  - **Goal:** the dying-world magic identity, mechanical.
  - **Tasks:** a light, dev-tunable per-region **magic-potency** dial (ties to Phase 25
    streaming) feeding cast cost/power; spells are *recovered* (tome/teacher), not vendored
    — a `Learn`/recovery seam reusing the 23H learn path; corrupted casting eases as potency
    falls. A `weave` dev-console command to inspect/tune.
  - **Done when:** potency visibly shifts cast power in two regions; a recovered spell is
    learnable via the recovery path; saved.
- [ ] **29.5F — Enemy & NPC caster AI** `[F]`
  - **Goal:** the world casts back (the sandbox has zero enemy magic today).
  - **Tasks:** a casting behavior in `EnemyAIComponent` (cast at range, kite to keep
    distance, heal/buff allies) reusing `SpellcastingComponent` on enemies; one caster
    archetype factory (a Valari mage / cultist) with a `.tres` spell loadout.
  - **Done when:** an enemy caster engages with spells, kites, and is beatable; reuses the
    player casting path, no parallel system.
- [ ] **29.5G — Magic UI + one signature spell per school (slice content)** `[F/C]`
  - **Goal:** the slice shows magic as a real, legible spine.
  - **Tasks:** a spellbook/school view with charge/channel/mastery feedback through
    `UiTheme` (functional; beautified in 30.5); author one signature spell per school for
    the slice (full catalogue is Phase 51).
  - **Done when:** the player can browse schools, see mastery/charge, and cast a signature
    spell from each school; content validates.

---

## Phase 30 — Animation, Models & Visual Identity `[P]`

> Art-heavy. Model authoring (30B, 30D, 30H) is built in Blender via the Blender MCP
> (`mcp__blender__*`, CLAUDE.md §2) and exported to glTF under the Phase 19/57 import/LOD
> conventions; the human still supplies whatever the MCP doesn't cover (rig finishing,
> hand-painted texture passes, audio). Each sub-phase integrates one asset class against
> existing states.

- [ ] **30A — Art-direction style guide** `[P]`
  - **Done when:** `docs/ART_STYLE.md` pins the dying-world language (ash, faded
    colour, embers) + import/LOD conventions feeding Phase 19/57.
- [ ] **30B — Player character model** `[P]`
  - **Goal:** the player has a real mesh to rig, not a placeholder capsule.
  - **Tasks:** built in Blender via the Blender MCP — base mesh + texture set matched
    to `ART_STYLE.md` (30A); modular gear/weapon attach points for the equipment the
    player can visibly wear/wield; export to glTF.
  - **Done when:** a static, importable player mesh with equip sockets exists in-engine.
- [ ] **30C — Third-person character + weapon rig integration** `[P]`
  - **Done when:** the rigged player character (30B's mesh) + a weapon play
    attack/block/idle driven by combat states.
- [ ] **30D — Core enemy + key-NPC model set** `[P]`
  - **Goal:** the slice cast named in the Phase 30 header (core enemies, key NPCs,
    the boss) has real meshes, not the goblin-only placeholder.
  - **Tasks:** built in Blender via the Blender MCP — goblin model (+ one variant),
    the Iron King boss model, and the key Ember Crown NPCs from Phase 27 (vendor,
    innkeeper, guild rep) — each matched to `ART_STYLE.md` (30A); export to glTF.
  - **Done when:** each listed actor has a distinct, importable mesh/texture set.
- [ ] **30E — Spell-casting animations + cast VFX by school** `[P]`
  - **Done when:** casting plays animations and school-tinted VFX matched to
    `SpellSchools`.
- [ ] **30F — Core enemy animation set (goblin + Iron King)** `[P]`
  - **Done when:** locomotion/attack/hit/death sets (driving 30D's meshes) drive
    the existing AI/combat states for the slice cast.
- [ ] **30G — Third-person body for cutscenes/reflections** `[P]`
  - **Done when:** a TP body exists for the Phase 43 cutscenes and corruption
    appearance (23F) hangs off it.
- [ ] **30H — World/environment model set for the Ember Crown slice** `[P]`
  - **Goal:** the Phase 27 Ember Crown slice has real dressing, not greybox.
  - **Tasks:** built in Blender via the Blender MCP — town-hub building kit (inn,
    guild presence, vendor stalls, crafting stations, a housing-plot exterior) +
    wilds POI dressing (rocks, ruins, foliage), matched to `ART_STYLE.md` (30A);
    export to glTF.
  - **Done when:** the Ember Crown walkable slice can be dressed with real meshes
    instead of placeholder primitives.
- [ ] **30I — Status/impact VFX library + corruption materials** `[P]`
  - **Done when:** status effects + corruption tiers (replacing 23F placeholders)
    use real materials/VFX.

---

## Phase 30.5 — UI & HUD Overhaul `[P/F]`

> Take the functional UI (Phase 14/18) and the individual surfaces grown across 23–30 to
> **one cohesive, ship-quality** look. Build the design system first (30.5A), then HUD, then
> menus, then feel/input. All strings go through the Phase 24 `Loc` layer. Each sub-phase
> leaves the game buildable/playable; verify in-engine (build → `run_project`, CLAUDE.md §3).

- [ ] **30.5A — Design tokens + `docs/UI_STYLE.md`** `[P]`
  - **Goal:** the foundation every other surface answers to.
  - **Tasks:** grow `UiTheme` (`src/UI/UiTheme.cs`) from palette+builders into real **tokens**
    — palette, type scale, spacing, radius, elevation, motion (durations/easing). Write
    `docs/UI_STYLE.md` pinning the dying-world UI identity (ash/faded/ember), matched to
    `docs/ART_STYLE.md` (30A).
  - **Done when:** tokens exist and one widget is rebuilt on them as proof; the style guide is
    the documented source of truth.
- [ ] **30.5B — HUD architecture & layout system** `[F]`
  - **Done when:** a responsive, **UI-scalable**, safe-area-aware HUD container with anchored
    widget slots exists; `GameHud` is refactored onto it with no regressions.
- [ ] **30.5C — Core HUD widgets rebuilt** `[F/P]`
  - **Done when:** vitals (health/stamina/mana), prepared spell + cooldown, status effects,
    and the crosshair are rebuilt on the tokens with value-change juice.
- [ ] **30.5D — Wayfinding HUD** `[F/P]`
  - **Done when:** compass, quest tracker, interaction prompt, nameplate, world-event banners,
    and toasts/notifications are unified on the new system.
- [ ] **30.5E — Combat & boss HUD** `[F/P]`
  - **Done when:** boss healthbar, lock-on reticle, crit/stagger/block/parry screen feedback,
    and the corruption vignette hook (23E) are unified (ties Phase 28/29/23).
- [ ] **30.5F — Panel & screen framework** `[F]`
  - **Done when:** a screen/route manager + a reusable modal/non-modal panel shell, tab system,
    list/grid, and tooltip system exist; one panel is ported to prove the framework.
- [ ] **30.5G — Inventory / character / equipment / perks panels rebuilt** `[F/P]`
  - **Done when:** all four are rebuilt on the 30.5F framework + tokens, feature-parity, no
    regressions.
- [ ] **30.5H — Crafting / dialogue / journal / map panels rebuilt** `[F/P]`
  - **Done when:** the remaining panels are rebuilt on the framework; `DialoguePanel` keeps its
    modal behaviour, the journal/map stay non-modal.
- [ ] **30.5I — Motion & microinteractions** `[F/P]`
  - **Done when:** screen/panel transitions, hover/press feedback, and value-change animations
    (damage, XP, level-up) are in, behind a reduced-motion guard.
- [ ] **30.5J — Gamepad & focus navigation** `[F]`
  - **Done when:** a controller/keyboard **focus-navigation** system drives HUD-adjacent menus,
    with input-device-aware glyphs; no menu is mouse-only.
- [ ] **30.5K — UI scale & legibility pass** `[F/P]`
  - **Done when:** a global UI-scale option + font sizing + a contrast/legibility audit land,
    verified readable at min-spec and Steam Deck resolutions (a precursor to Phase 54).

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

## Phase 34.5 — Frostfang Clans & Beast-Race Factions `[F/C]`

> LORE names Frostfang's warrior clans/beast races as a culture, not generic
> wildlife. Give them a faction identity before they dissolve into the bestiary.

- [ ] **34.5A — Frostfang Clans `FactionResource` + hub presence** `[F/C]`
  - **Done when:** the clan faction exists with a hub/outpost; reputation/dread
    (23G) applies to it like any faction.
- [ ] **34.5B — Clan archetypes (raider, beast-tamer, shaman)** `[C]`
  - **Done when:** three clan archetypes exist on the Phase 34 matrix with
    distinct loot/AI profiles.
- [ ] **34.5C — Clan questline + rank chain** `[C]`
  - **Done when:** a short multi-quest arc with rank-up flags is completable;
    `validate-all` green.

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

## Phase 40.5 — Dungeon & Puzzle Framework `[F]`

> Ruins/temples/dragon-nests imply puzzles and traps; no phase before this builds the
> tooling. Lands before Phase 50 authors dungeons against it.

- [ ] **40.5A — `PuzzleComponent` + lever/pressure-plate primitive** `[F]`
  - **Done when:** a lever/plate puzzle gates a door/reward and is solvable + reset
    -safe.
- [ ] **40.5B — Sequence + light/shadow puzzle primitives** `[F]`
  - **Done when:** two more puzzle types exist on the same component family.
- [ ] **40.5C — Trap primitives (spikes/darts/collapsing floor)** `[F]`
  - **Done when:** trap hazards deal damage through the existing `DamagePacket`
    pipeline and are placeable as data.
- [ ] **40.5D — Relic-trial vault convention + one authored example** `[F/C]`
  - **Done when:** one vault (puzzle + guardian encounter) is authored end-to-end
    as the template Phase 51E's relics reuse.
- [ ] **40.5E — CLAUDE.md §8 recipe + `ContentValidator` checks** `[F/P]`
  - **Done when:** "a new puzzle/trap" is documented and content is checked for
    solvability/dangling triggers.

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

## Phase 41.5 — Divine Shrines & Blessings `[F/C]`

> The Seven Gods get a full LORE section and zero in-game presence beyond Morthul.
> This mechanizes the other six as shrine blessings.

- [ ] **41.5A — `ShrineResource` + `BlessingComponent` core** `[F]`
  - **Done when:** a shrine interactable grants a persistent passive bonus on
    first visit; `ISaveable`.
- [ ] **41.5B — Author the six gods' shrines (one per realm + placement)** `[C]`
  - **Done when:** six shrines exist, each with a distinct domain-flavored
    blessing; `validate` green.
- [ ] **41.5C — Corruption-gated blessing refusal/curse** `[F/C]`
  - **Done when:** a high-corruption visit to at least one shrine triggers a
    refusal/curse variant instead of the blessing.

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

## Phase 42.5 — The Crimson Cult `[F/C]`

> The Crimson Prophet "built an empire of worshippers" (LORE) — give it a real
> in-world faction, not just a boss fight at the end of Sunspire.

- [ ] **42.5A — Crimson Cult `FactionResource` + hub/outpost presence** `[F/C]`
  - **Done when:** the cult exists as a hostile faction with an outpost in
    Sunspire; reputation/dread applies.
- [ ] **42.5B — Cult zealot/inquisitor archetypes** `[C]`
  - **Done when:** two cult archetypes exist on the Phase 34 matrix.
- [ ] **42.5C — Infiltration questline (branching, feeds into 47E)** `[C]`
  - **Done when:** a branching infiltration arc is completable and feeds into
    the Crimson Prophet arc's flags.

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

## Phase 43.5 — Flamebearer Vision Sequences `[F/C]`

> DESIGN §5 demands the corruption theme be *felt*. A flashback per fallen
> Flamebearer (built on Phase 43's tooling) makes "becoming them" experiential.

- [ ] **43.5A — `VisionSequence` cutscene variant (desaturated/ash playback mode)** `[F]`
  - **Done when:** a vision plays through the Phase 43 timeline system with the
    distinct visual treatment, skippable.
- [ ] **43.5B — Wire vision trigger to the boss-defeat hook (28D/36E)** `[F]`
  - **Done when:** defeating a framework boss can trigger its vision
    automatically.
- [ ] **43.5C — Author the six Flamebearer visions** `[C]`
  - **Done when:** all six visions exist and play at the correct story beat;
    `validate-all` green.

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

## Phase 44.5 — World State: Realm Decay & Restoration `[F]`

> Dawnfire's "the lands heal" needs a world-scale state to pay off, mirroring
> `CorruptionTier`'s shape but realm-scoped (DESIGN §2.1's "return changed" arrow).

- [ ] **44.5A — `RealmStateComponent` + per-region decay tier** `[F]`
  - **Done when:** a region's tier can be set/read, persists, and is queryable
    by other systems.
- [ ] **44.5B — Story-flag-driven tier transitions (one realm wired as proof)** `[F/C]`
  - **Done when:** defeating that realm's Flamebearer measurably shifts its
    tier.
- [ ] **44.5C — Visual hooks (lighting/fog/weather-bias per tier)** `[F/P]`
  - **Done when:** the tier change is visible in the proof realm, ready for the
    Phase 53 art pass to build on.
- [ ] **44.5D — Ending-state write (Dawnfire heals / Lord of Embers ashen, all realms)** `[F]`
  - **Done when:** Phase 49's ending choice writes a final tier across all four
    realms.

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

## Phase 47.5 — The Ashen Knight: Rival Duels `[C]`

> "The player's greatest rival" (LORE) needs a rival *arc*. 47F seeded it; this
> phase pays it off with content.

- [ ] **47.5A — Mid-Act-II duel (escape-clause encounter)** `[C]`
  - **Done when:** the duel is fightable, ends in a scripted escape/draw, and
    sets a story flag.
- [ ] **47.5B — Act III duel (escalated, second encounter)** `[C]`
  - **Done when:** the second duel plays harder/different and feeds the Act IV
    flag set (49B).

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

## Phase 50.5 — Lore Codex & Compendium `[F/C]`

> Phase 50G authors lore-book collectibles; nothing reads them back. A compendium
> distinct from the combat bestiary (34G).

- [ ] **50.5A — `CodexEntryResource` + `CodexDatabase`** `[F]`
  - **Done when:** codex entries are data, unlock on a flag/collectible pickup,
    and persist.
- [ ] **50.5B — Codex UI panel (on the 30.5F framework)** `[F]`
  - **Done when:** unlocked entries are browsable in a panel; locked entries
    show as teasers.
- [ ] **50.5C — Seed entries for every god/Flamebearer/realm/guild** `[C]`
  - **Done when:** every named LORE entity has a codex entry, wired to its
    existing unlock trigger (a 50G book or a story flag).

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

## Phase 51.5 — Enchanting & Relic Socketing `[F/C]`

> Not LORE-mandated — an optional itemization deepener. Cut cleanly if it doesn't
> clear playtest.

- [ ] **51.5A — `SocketComponent` + socket count by rarity** `[F]`
  - **Done when:** rare+ gear can have empty sockets that round-trip through
    save/load.
- [ ] **51.5B — `EnchantResource` + socket/unsocket flow** `[F/C]`
  - **Done when:** an enchant item can be socketed/removed and visibly changes
    stats.

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

## Phase 53.5 — Photo Mode `[P]`

> Not LORE-mandated — a polish-tier nicety pairing with the Phase 53 art pass.

- [ ] **53.5A — Free camera + hide-HUD toggle in pause state** `[P]`
  - **Done when:** the player can detach the camera and hide UI for a
    screenshot, then resume cleanly.
- [ ] **53.5B — A few dying-world filters/vignettes** `[P]`
  - **Done when:** at least 2 filters are selectable and match the Phase 53 art
    direction.

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
