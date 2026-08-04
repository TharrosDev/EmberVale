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
4. **Verify** the *Done when* bar for real. This environment **can** build and run
   (CLAUDE.md §2): `dotnet build Embervale.sln`, `dotnet test tests/Embervale.Tests`,
   and `godot --headless --path . -- --validate`. Run all three; a phase that
   changes content is not done until `--validate` exits 0. Two things still need a
   human at the keyboard — the `F1` dev console (no CLI equivalent) and anything
   behind `F5`/`F9` — so say plainly which checks you *ran* and which you are
   handing over. Reserve "verified" for output you actually captured.
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

## Phase 25.5 — Stage A Hardening & Stabilization `[F/P]` ✅ **complete (A–P)**

> A consolidation pass over **everything built to that point** — debug, optimize, harden, no new
> features — before races/boss/slice stacked on top. **25.5A–G** hardened the Stage A production
> work (22–25); **25.5H–P** were a fresh regression pass over the foundational systems 1–21.
> The integration sign-off, perf baselines and known-issues ledger live in
> [`STAGE_A_STATUS.md`](STAGE_A_STATUS.md); the durable engineering rules that came out of it are
> in `ARCHITECTURE.md` (§2.7 Save especially) and CLAUDE.md §7. This block is the log.

**Stage A band (22–25 hardening)**

- [x] **25.5A — Save/load integrity sweep** `[F]` — root-caused the recurring save warnings:
  components registered with the `SaveManager` unconditionally, so transient actors wrote volatile
  `stats:<runtimeId>` keys that could never be reclaimed. Fixed with the pure `SaveKeyPolicy` +
  `EntityComponent.RegisterSaveable()`, so **transient actors persist nothing**. Added the
  `savecheck` dev command. → `ARCHITECTURE.md` §2.7.
- [x] **25.5B — Region streaming stability & profiling** `[F/P]` — the post-transition loading
  screen no longer clears on a fixed 0.4 s timer (which popped cells in whenever a region needed
  more than the 1-cell/frame budget); it holds until the streamer reports idle.
- [x] **25.5C — Corruption system hardening** `[F]` — fixed a load desync where the tier event
  didn't re-fire after `Load`, leaving appearance/UI on the pre-load tier.
- [x] **25.5D — Meta-shell, settings & state-machine robustness** `[F]` — state-machine edges,
  settings round-trip, mouse recapture on resume.
- [x] **25.5E — UI/HUD interaction & input hardening** `[F/P]` — input/focus edges; the fast-travel
  trap and block-strand bugs.
- [x] **25.5F — Validator & analytics coverage** `[F]` — widened `ContentValidator` and the
  analytics sink over the Stage A systems.
- [x] **25.5G — Integration regression sweep & known-issues ledger** `[C/P]` — the sign-off pass;
  its output is `STAGE_A_STATUS.md`.

**Systems band (1–21 regression pass)**

- [x] **25.5H — Core, entity/component, events, stats & pooling** `[F]`
- [x] **25.5I — Player controller, locomotion & combat framework** `[F]`
- [x] **25.5J — Enemy AI, perception & spawning** `[F/P]`
- [x] **25.5K — Inventory, equipment & loot generation** `[F]`
- [x] **25.5L — Progression, quests & dialogue** `[F]`
- [x] **25.5M — Magic, status effects & combat math** `[F]`
- [x] **25.5N — World clock/weather/encounters, NPC schedules & procedural events** `[F]`
- [x] **25.5O — Crafting & faction/reputation systems** `[F]`
- [x] **25.5P — Legacy UI panels & HUD** `[P/F]` — also completed the `Loc` sweep over the four
  legacy panels (80 → 113 strings), leaving `DebugHud` exempt per CLAUDE.md §6.

> **Outcome.** Real bugs fixed — save-key collisions, corruption load desync, mouse recapture,
> a fast-travel trap, a lifecycle guard, respawn cadence, block-strand, a cross-transition spawn
> leak — and the load-bearing pure kernels pinned by **242 unit tests** (the suite that has since
> grown to 579). The repo stayed buildable, `--validate`-clean and booting `errors: []` throughout.

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

- [x] **26D — `CharacterCreator` screen** `[F]` ✅
  - **Goal:** the new-game creation flow.
  - **Tasks:** build the creator (race pick with trait summary, appearance options,
    name, optional background) through `UiTheme`, fed by `RaceDatabase`, writing a
    `CharacterProfile`. Hook it into MainMenu → New Game → world spawn. All strings
    via `Loc`.
  - **Done when:** New Game → create a character → spawn into the world with the
    chosen race applied; flow round-trips through the save header.
  - **Done:** `CharacterCreator` (`CanvasLayer`, mirrors `SaveSlotPanel`, built via `UiTheme`): a
    `UiTheme.Dropdown` race picker over `RaceDatabase.All` with a live **trait summary** (the race's
    `Description`, each stat delta as signed amount + localized stat name, innate perk/spell `DisplayName`s,
    reputation tweaks by faction `DisplayName`), a name `LineEdit`, and an optional background `LineEdit`;
    Begin builds a `CharacterProfile` and Back returns to the title. `MainMenu` New Game → slot pick →
    creator → `NewCharacterRequested(slot, profile)`; `GameBootstrap.StartNewGame(slot, profile)` spawns
    from it (the 26C plumbing applies the race). New `StatNames` helper (localized `StatType` names) +
    15 `stat.*` and ~11 `create.*` keys (`strings.csv` 113→139). All strings via `Loc` (no literals).
    Build clean + **247 tests** (+1 `StatNamesTests`: every `StatType` → distinct non-fallback key) +
    `--validate` exit 0 + boot logs `loaded 139 string(s)`, `errors: []`. UI reviewed against the Godot 4.7
    C# API (the New Game → creator → spawn click-path is a windowed interaction, not headless-drivable).
    Appearance deferred (no catalogue/renderer until Phase 30 models). **Phase 26 complete.**

---

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
    NPCs (home → work → tavern → sleep routines) per CLAUDE.md §8 "new NPC
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

## Phase 28 — First Boss: a Fallen Flamebearer (Iron King slice) `[F/C]`

> One full multi-phase boss to build and prove boss tooling ahead of Phase 36, and
> to wire the defeat → reward → corruption-gain loop.

- [x] **28A — Iron King actor + arena** `[F/C]` ✅
  - **Goal:** the boss exists in a space.
  - **Tasks:** build the Iron King as a `CharacterEntity` via a boss factory
    (mirror `EnemyFactory`): stats `AttributeSet`, `CombatComponent` (Team), a
    weapon, hurt/hitboxes, AI behaviour. Build an arena sub-cell with an entry
    trigger. Register in `ServiceLocator` if the boss bar needs it.
  - **Done when:** you can enter the arena and fight a functional (single-phase)
    Iron King.
  - **Done:** `BossFactory` (`src/Enemies/BossFactory.cs`) mirrors `EnemyFactory` to build a
    `BossEntity` "Iron King" — bigger capsule + dark-iron/ember material, `IronKingAttributes.tres`
    (650 HP, Armor 15, slow heavy hits), `CombatComponent` Team 1 with `MaxPoise 150` (shrugs off chip
    stagger), `IronKingMaul.tres` weapon, and the **reused** `EnemyAIComponent` tuned for a boss
    (`RetreatHealthFraction=0`, `VisionRange 40`, `AttackRange 3.5`). Hostile via a new
    `faction.fallen` (`data/factions/Fallen.tres`, default-hostile). `BossEntity : EnemyEntity`
    marker (`src/Enemies/BossEntity.cs`) is its own `ServiceLocator` type (the 28C bar / 28D corruption
    hook). Registered as `enemy.iron_king` in `EnemyTemplateRegistry` (seeded 1→2). The **arena**
    (`scenes/regions/ember_crown/arena.tscn`) is a streamed sub-cell (nav + floor + a U of walls open
    toward town) added to `EmberCrown.tres` `Cells` at `(55,0,-10)`; its **entry trigger** is an
    E-interact **challenge brazier** (`BossSummonComponent` — mirrors `RegionTransitionComponent`) that
    spawns the Iron King once, registers him, and re-arms on his death (the seed for the Phase 36
    `BossController` — intro lock/phases graft here). Build clean + 251 tests + `--validate` 0
    (`faction.fallen`/`IronKingAttributes`/`IronKingMaul`/arena cell resolve, registry reports
    `enemy.iron_king`); arena instances + bakes navmesh clean; boots clean (`errors: []`).
    **Deferred to 28B–D:** multi-phase + telegraphs, healthbar + intro/defeat, loot + corruption-gain +
    defeat persistence (so he re-summons on cell reload and drops nothing yet). Walking east to the
    arena and fighting him is the maintainer's at-keyboard check (MCP can't drive movement/`E`/combat).

- [x] **28B — Multi-phase behaviour + telegraphed attacks** `[F]` ✅
  - **Goal:** phases and readable wind-ups.
  - **Tasks:** add HP-threshold phase transitions (e.g. 66%/33%) that change the
    ability set, and telegraphed wind-up timing on heavy attacks (the "no
    button-mashing" feel). Keep it data-light but real; this becomes the seed for
    `BossController` in Phase 36 — note the generalizable bits.
  - **Done when:** the fight has ≥2 distinct phases with telegraphed attacks.
  - **Done:** a new `BossController` (`src/Enemies/BossController.cs`, added in `BossFactory`) rides on
    top of the shared `EnemyAIComponent`/`MeleeWeaponComponent` — no AI rewrite. **3 phases:** it
    watches `DamageDealtEvent` for hits on the boss and, crossing 66% / 33% HP, stacks attack-speed +
    move-speed `StatModifier`s (`boss.phase2/3`) so the later thirds are visibly more relentless;
    publishes `BossPhaseChangedEvent(boss, phase, total)` (the 28C bar / Phase-36 generalisation hook).
    **Telegraphs:** every swing (`AttackPerformedEvent` from the boss) flares the body's emissive glow
    during the maul's 0.55 s wind-up and fades it over the swing — readable heavy hits, brighter/redder
    each phase. **Generalizable bits for Phase 36** (noted in the class doc): the HP-threshold→profile
    table + publish-on-transition event, and the telegraph as a presentation hook any wind-up can drive.
    Build clean + 251 tests + `--validate` 0; boots clean (`errors: []`). Seeing the phase flares +
    speed-up mid-fight is the maintainer's at-keyboard check.

- [x] **28C — Boss healthbar + intro/defeat sequencing** `[F]` ✅
  - **Goal:** the boss UI/flow beats.
  - **Tasks:** add a boss healthbar to `GameHud` (through `UiTheme`), a short intro
    lock and a defeat sequence (slow-mo/fade hook for Phase 43 cinematics later).
    All strings via `Loc`.
  - **Done when:** the bar tracks the boss; intro and defeat beats play cleanly.
  - **Done:** `BossSummonComponent` now publishes a new `BossEncounterStartedEvent(boss, "boss.name")`.
    Two consumers, cleanly split: **(UI)** `GameHud.BuildBossBar()` — a top-centre panel (name + wide
    `UiTheme.Bar` + "Phase n/3" + a transient message line) that shows on start, polls
    `stats.GetNormalized(Health)` each frame, updates the phase label off `BossPhaseChangedEvent` (28B),
    and on the boss's `EntityDiedEvent` hides the bar + plays a defeat message + a manual `ColorRect`
    fade pulse (wall-clock timed, so `TimeScale` can't slow it). **(Flow)** `BossEncounterDirector`
    (`ProcessMode.Always`, created in `GameBootstrap`) — intro lock via `UiState.Open/Close` (~2.5 s)
    and a slow-mo defeat (`Engine.TimeScale = 0.35` for ~1 s), both timed off `Time.GetTicksMsec` and
    safety-restored on teardown. 4 `Loc` strings added (catalogue → 213). Build clean + 251 tests +
    `--validate` 0; full play boot clean (`errors: []`, arena streamed). The bar/intro/defeat *feel* is
    the maintainer's at-keyboard check (MCP can't drive `E`/combat).

- [x] **28D — Defeat → reward → corruption-gain loop** `[F/C]` ✅
  - **Goal:** wire the boss to corruption + loot.
  - **Tasks:** on defeat, grant a guaranteed reward (a placeholder divine-relic
    item `.tres`) and raise corruption via `CorruptionComponent` (absorbing his
    fragment). Author the reward + the "absorb the flame?" dialogue/choice beat.
    Add a placeholder music cue hook for Phase 31.
  - **Done when:** defeating the Iron King grants the relic and visibly raises
    corruption; the whole beat round-trips through save/load.
  - **Done:** `BossEncounterDirector` (the persistent boss coordinator) now, on the boss's death,
    grants `item.relic.iron_heart` ("Heart of the Iron King", new `data/items/IronHeartRelic.tres`,
    Legendary) to the player's inventory, sets the persisted story flag `flag.iron_king_defeated`,
    publishes a placeholder `MusicCueRequestedEvent("music.boss_defeat")` (the Phase 31 audio hook), and
    — after the slow-mo beat settles — opens the **"absorb the flame?"** dialogue
    (`data/dialogue/IronKingAbsorb.tres`) via `DialogueStartedEvent`. The *Absorb* choice uses the
    existing `DialogueEffect.AddCorruption` (+25 → crosses Untainted→Touched, firing
    `CorruptionTierChangedEvent` that the vignette/appearance react to); *Leave it* declines. The brazier
    (`BossSummonComponent`) now reads the flag and goes cold (empty prompt, `Interact` no-ops) so his
    defeat persists — no re-fight, no re-grant. **No save code added** — corruption, inventory and story
    flags are all existing `ISaveable`s, so the beat round-trips for free. 8 `Loc` strings (→221), 13
    items / 6 dialogues. Build clean + 251 tests + `--validate` 0 (dialogue graph reachable); boot clean.
    The defeat→relic→absorb→corruption→save/load chain is the maintainer's at-keyboard **Gate G0** pass.

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

- [x] **29A — Hit-stop / freeze frames + hit-pause tuning** `[F/P]` ✅
  - **Done when:** landing/taking a heavy hit briefly freezes for weight; tunable;
    off during pause/cutscene.
  - **Done:** new `HitStopDirector` (`src/Combat/`, `ProcessMode.Always`, bootstrap-created) dips
    `Engine.TimeScale` to a freeze on `DamageDealtEvent`/`EntityStaggeredEvent`, restored off wall-clock
    (`Time.GetTicksMsec`) — the 28C slow-mo pattern, scoped to brief per-hit freezes. The window comes
    from a pure, unit-tested `HitStop.DurationMs(amount, isCrit, isBlocked, staggered)` (light→heavy by
    damage, +crit, stagger longest, blocked a tick, sub-`MinDamage` = no freeze; a stronger/later hit
    extends). Guards satisfy "off during pause/cutscene": ignores triggers unless `IsPlaying &&
    !UiState.MenuOpen` (the boss intro lock raises `UiState`), bails the freeze if it leaves Playing, and
    won't engage while another time effect owns `TimeScale` (the boss defeat slow-mo) — they never
    overlap live combat. All knobs are `HitStop` consts. Also centered the inventory panel on-screen.
    Build clean + 259 tests (+5 HitStop) + `--validate` 0; boot clean. Feel/tuning is the maintainer's
    at-keyboard pass.
- [x] **29B — Camera shake + directional hit reactions** `[F/P]` ✅
  - **Done when:** crits/blocks/stagger shake the camera; hits push reactions in
    the hit direction.
  - **Done:** `CameraShake` (a `Node` under the player's `Camera3D`) runs a trauma model —
    `DamageDealtEvent` adds crit/block trauma, `EntityStaggeredEvent` adds stagger trauma — and offsets
    the camera around its rest pose by `ShakeMath.Amplitude(trauma)` (quadratic) × noise each frame,
    decaying to rest. The camera leaf is otherwise untouched by mouse-look, so the shake doesn't fight
    the controls. `HitReactionComponent` (on player + goblin) lurches the actor's mesh in the hit
    direction (`Source`→`Target`, works for melee and arrows) and eases it back — visual-only, never the
    `CharacterBody3D`. Pure `ShakeMath` knobs are unit-tested. Build clean + 263 tests (+4) +
    `--validate` 0; boot clean. Feel/tuning is the maintainer's at-keyboard pass.
- [x] **29C — Weapon trails, impact VFX/SFX hooks** `[F/P]` ✅
  - **Done when:** swings show trails and impacts spawn placeholder VFX/SFX through
    a poolable effect (CLAUDE.md §8 pooling).
  - **Done:** `CombatFeedbackDirector` (bootstrap `Node`) owns a `NodePool<ImpactEffect>` and on every
    `DamageDealtEvent` spawns a pooled expand-and-fade spark at the target (tinted gold/grey/white by
    crit/block/hit) + publishes a positional `SoundCueRequestedEvent` (the Phase 31 audio hook). The
    cue id + tint come from a pure, unit-tested `CombatFx`. `WeaponTrailComponent` (player + goblin)
    flashes a translucent slash quad in front of the body on `AttackPerformedEvent` and fades it out —
    skipped for a ranged swing (bow fires an "sfx.combat.bow" cue instead). Gotcha fixed: a component
    can't `AddChild` to its own entity body during `_Ready` ("parent busy setting up children") — it
    orphans the node; deferred via `CallDeferred(Node.MethodName.AddChild, …)`. Build clean + 266 tests
    (+3 CombatFx) + `--validate` 0; **combat-tested run, no orphan leak**. VFX polish is the maintainer's
    eye; real audio is Phase 31.
- [x] **29D — Screen feedback on crit/stagger/block/parry** `[F/P]` ✅
  - **Done when:** each combat state has a distinct screen/HUD feedback through
    `UiTheme`.
  - **Done:** `CombatFeedbackOverlay` (CanvasLayer) flashes a full-screen colour tint + a short word per
    player combat state — crit (gold), block (steel), stagger (red), parry (bright) — keyed off the combat
    events from the player's perspective (`ServiceLocator`). Pure `CombatFeedbackFx` (tint/alpha). New
    `EntityParriedEvent`.
- [x] **29E — Dodge i-frames + roll** `[F]` ✅
  - **Done when:** a dodge with invulnerability frames exists and is tunable;
    integrates with stamina.
  - **Done:** `Dodge` input (Ctrl) → `DodgeComponent.TryDodge` gates on grounded + stamina + not
    rolling/staggered, spends stamina, drives a burst roll via `LocomotionComponent.StartDash`, and opens an
    i-frame window (`CombatComponent.IsInvulnerable`, which whiffs the hit in `ReceiveDamage`). All
    timings/cost are export knobs; pure `Dodge` helper unit-tested.
- [x] **29F — Parry / riposte windows** `[F]` ✅
  - **Done when:** a timed block parries and opens a riposte; mistimed block takes
    chip/stagger.
  - **Done:** `CombatComponent` measures time since the guard rose; a hit within `ParryWindow` parries
    (full negate, attacker staggered = the riposte opening, `EntityParriedEvent` → 29D flash). A
    mistimed/held block chips damage **and** chip poise (`BlockPoiseFactor`) so a held guard can break into a
    stagger. New `Stagger()` helper; pure `Parry` helper unit-tested.
- [x] **29G — Animation-cancel windows + input buffering** `[F]` ✅
  - **Done when:** attacks have commit + cancel windows and buffered inputs feel
    responsive, not mashy.
  - **Done:** `MeleeWeaponComponent` buffers an attack pressed mid-commit (Windup/Active) and
    auto-releases it at the cancel window (Recovery/Idle) — cancelling recovery into the next combo hit, so
    an early press lands. Exposes `IsCommitted`; dodge is gated on it. Pure `AttackBuffer.ShouldRelease`.
- [x] **29H — Lock-on / soft target from `FocusedEntity`** `[F]` ✅
  - **Done when:** a real target-lock with switching, built out from the Phase 18
    `FocusedEntity`.
  - **Done:** `LockOnComponent` locks the aimed-at/nearest hostile on middle-mouse, cycles nearby hostiles
    on the wheel, drops dead/out-of-range targets (sphere sweep, input-only). `PlayerController` auto-yaws
    the body to the target (mouse pitches only) → strafe; `GameHud` reticles it + nameplate priority. Pure
    `LockOn` cycle/range maths.
- [x] **29I — Stamina/poise pacing tune (anti-mash)** `[F/P]` ✅
  - **Done when:** stamina/poise costs discourage mashing per the `docs/DESIGN.md`
    combat pillar; documented values.
  - **Done:** `StatsComponent.StaminaRegenDelay` (0.9s) pauses stamina regen after every spend, so a mash
    starves the bar (empties in ~10 swings, locks out attack/dodge/block) while spaced reads sustain. Pure
    `StaminaPacing.CanRegen`; tuned shape documented in DESIGN §1.6.

---

## Phase 29.5 — Spellcraft & the Fading Weave `[F]`

> Magic made deep + original. Phase 12 built the *system*; this gives it identity and
> depth so magic is a real build spine for the slice (DESIGN §1.5). All new *mechanics*
> land here, before the G2 freeze; breadth/content is woven through 26/34/35/42/47–48/51.
> Theme: magic is the fading **Weave** of a dying world — recover lost spellcraft, and
> corruption is the darker shortcut (extends 23H). Read `src/Magic/` first.

- [x] **29.5A — Cast archetypes: Charged + Channeled** `[F]` ✅
  - **Goal:** casts have feel beyond fire-and-forget.
  - **Tasks:** add a `CastMode` (Instant · Charged · Channeled) to `SpellResource`
    (append-only enum), layered on the existing Projectile/Area/Self *shape*; give
    `SpellcastingComponent` charge build-up (power/radius scale with hold) and channel
    (sustained tick at a mana-per-second cost, interruptible). Drive from the player
    controller. Persists nothing new (transient cast state).
  - **Done when:** one charged and one channeled spell cast and feel distinct from instant;
    mana drains correctly; round-trips (known spells already save).
  - **Done:** `CastMode` enum on `SpellResource` + `SpellcastingComponent` cast state machine
    (`BeginCast`/`UpdateCast`/`EndCast`/`CancelCast`): charged scales damage by hold time via pure
    `SpellCharge.PowerMultiplier`; channeled ticks every `ChannelTickInterval` at `ChannelManaPerSecond`,
    interrupted on key-up/out-of-mana/death. `PlayerController` drives press/hold/release off the Cast key.
    Slice spells **Flame Lance** (charged Fire) + **Storm Conduit** (channeled Lightning). Damage-only power
    scaling for now (projectile impact-radius scaling deferred).
- [x] **29.5B — School identities + status effects** `[F/C]` ✅
  - **Goal:** each `DamageType` school plays differently, not just tint+resist.
  - **Tasks:** author the signature mechanic + status effects per school — Fire ignite/DoT
    stacks, Frost chill→freeze, Lightning chain-to-nearby, Arcane ward/dispel, Nature
    heal-over-time/totem, Necrotic lifesteal/decay (corruption-gated per 23H). Mostly new
    `StatusEffectResource` `.tres` + small resolver hooks (CLAUDE.md §8).
  - **Done when:** every school has a distinct on-hit behavior provable in the sandbox.
  - **Done:** one shared on-hit seam (`SchoolIdentity.OnSpellHit`, invoked by `SpellResolver`
    after damage, before the spell's own status). **Fire** = stacking ignite (`StatusEffectResource.MaxStacks`,
    DoT × stacks; Burning stacks to 5). **Frost** = chill→freeze (`Frozen.tres` hard-root, applied when
    hitting an already-chilled target). **Lightning** = single chain to the nearest other hostile for ½
    damage (`StormConduit`). **Nature** = heal-over-time (`HealPerTick` on the status resource +
    `Regrowth.tres`; `LesserHeal` now leaves a regrowth). **Necrotic** = caster lifesteals 35% of the hit
    (corruption-gated by the spell, e.g. `EmberSiphon`). **Arcane** = the ward (`ArcaneShield`) stays its
    identity; on-hit dispel deferred until an offensive Arcane spell exists (29.5G). Pure bits unit-tested
    (`SchoolIdentityTests`).
- [x] **29.5C — Spell scaling + school mastery track** `[F]` ✅
  - **Goal:** "hard to master" magic ceiling that isn't just bigger numbers.
  - **Tasks:** extend `CombatMath.RollSpell` scaling off SpellPower/Intelligence; add a
    per-school **mastery** that ranks by casting that school and empowers/unlocks its
    spells (reuse perk/progression patterns; `ISaveable`). New `MasteryComponent` or fold
    into progression.
  - **Done when:** casting a school raises its mastery, which measurably empowers it;
    mastery round-trips through save/load.
  - **Done:** `RollSpell` now also scales off Intelligence (alongside gear's SpellPower). New
    `SchoolMasteryComponent` (`ISaveable`) banks a point per cast of a school (off `SpellCastEvent`),
    converts points→rank via pure `SchoolMasteryMath` (10 casts/rank, cap 5, +8%/rank), and
    `SpellcastingComponent` folds the school's mastery multiplier into every cast's damage **and** heal.
    Points persist; rank is derived. `mastery` dev command inspects it. Curve unit-tested
    (`SchoolMasteryMathTests`). Mastery-gated *unlocks* deferred (no spell needs one yet).
- [x] **29.5D — Reactive spell combos** `[F]` ✅
  - **Goal:** cross-school reads, the magic analogue of the combat read.
  - **Tasks:** a small `SpellCombo` resolver that inspects the target's
    `StatusEffectsComponent` on hit and fires a bonus effect (Chill + Lightning = shatter,
    etc.), data-described where possible.
  - **Done when:** at least two combos trigger and are documented; no hard-coded one-offs.
  - **Done:** `SpellCombo` reads the target's pre-hit afflictions on the same on-hit seam (before the
    spell's own status applies) and resolves the first matching rule from a declarative `ComboRule[]` table:
    **Shatter** (Lightning into Chill) and **Thermal Shock** (Fire into Chill) — each a burst plus consuming
    the chill. `StatusEffectsComponent.Consume` strips the spent status. Pure matcher unit-tested
    (`SpellComboTests`); table promotes to `.tres` only if the catalogue grows (Phase 51).
- [x] **29.5E — The fading Weave (region potency + spell recovery)** `[F]` ✅
  - **Goal:** the dying-world magic identity, mechanical.
  - **Tasks:** a light, dev-tunable per-region **magic-potency** dial (ties to Phase 25
    streaming) feeding cast cost/power; spells are *recovered* (tome/teacher), not vendored
    — a `Learn`/recovery seam reusing the 23H learn path; corrupted casting eases as potency
    falls. A `weave` dev-console command to inspect/tune.
  - **Done when:** potency visibly shifts cast power in two regions; a recovered spell is
    learnable via the recovery path; saved.
  - **Done:** `RegionResource.WeavePotency` (0..1, dev-tunable) feeds a global `Weave` static (mirrors
    `SafeZones`), set on world build + every region transition. Pure `WeaveMath` bends a cast by potency:
    as the Weave fades, **ordinary** magic weakens + costs more (×0.5 pow / ×1.5 cost at zero), **corrupted**
    magic (gated above Untainted) strengthens + cheapens (×1.4 / ×0.6) — the 23H temptation made mechanical.
    `SpellcastingComponent` folds it into both damage and mana cost. Two regions contrast (Ember Crown 1.0,
    Frostfang Reach 0.5). Recovery seam: `SpellTomeComponent` (an interactable) teaches a spell through the
    same corruption-gated `Learn` — an Ashen Tome near spawn holds the corrupted Ember Siphon. `weave`
    dev command inspects/tunes. Potency is region data, so it restores on load with the region (no new save
    state); learned spells already persist. Math unit-tested (`WeaveMathTests`).
- [x] **29.5F — Enemy & NPC caster AI** `[F]` ✅
  - **Goal:** the world casts back (the sandbox has zero enemy magic today).
  - **Tasks:** a casting behavior in `EnemyAIComponent` (cast at range, kite to keep
    distance, heal/buff allies) reusing `SpellcastingComponent` on enemies; one caster
    archetype factory (a Valari mage / cultist) with a `.tres` spell loadout.
  - **Done when:** an enemy caster engages with spells, kites, and is beatable; reuses the
    player casting path, no parallel system.
  - **Done:** the `EnemyAIComponent` Combat state gains a **caster branch** (taken when the actor has a
    `SpellcastingComponent` — the very component the player uses, no parallel system): hold the cast band
    via pure `CasterDecision` (approach when far, **kite** when crowded, hold otherwise), face the target so
    the cast aims true, and pick one cast per tick by priority — **heal a wounded ally** (`FindWoundedAlly`
    over the enemy group, on the caster's team, incl. itself), else the hardest-hitting ready **offensive**
    spell, else **ward itself**. New `SpellcastingComponent` levers reused by the AI: `TryCastById`,
    `TryCastSupportOn(ally)`. First archetype: the **Ashen Acolyte** (`AshenAcolyteFactory` +
    `CultistAttributes.tres`), a squishy Fallen fire-caster (Firebolt/Fireball/ArcaneShield/LesserHeal) that
    aims from a chest `CastOrigin` marker; registered in `EnemyTemplateRegistry`, spawnable via
    `spawn <n> enemy.ashen_acolyte`. Wounded casters also cast while retreating. Positioning unit-tested
    (`CasterDecisionTests`). The school-themed caster *roster* is Phase 34 (data, no new code).
- [x] **29.5G — Magic UI + one signature spell per school (slice content)** `[F/C]` ✅
  - **Goal:** the slice shows magic as a real, legible spine.
  - **Tasks:** a spellbook/school view with charge/channel/mastery feedback through
    `UiTheme` (functional; beautified in 30.5); author one signature spell per school for
    the slice (full catalogue is Phase 51).
  - **Done when:** the player can browse schools, see mastery/charge, and cast a signature
    spell from each school; content validates.
  - **Done:** every school now has a **signature spell with its own delivery mechanic**: Fire =
    **Flame Lance** (29.5A charged) · Lightning = **Ball Lightning** (a slow orb that *homes* on the
    nearest hostile — new `SpellResource.HomingRange` + pure `SpellHoming.Steer`, applied per-frame in
    `SpellProjectile`) · Frost = **Blizzard** (a lingering *zone* — `ZoneDuration`/`ZoneTickInterval`
    spawn a `SpellZone` that re-`Detonate`s the spell on a cadence, chilling everything inside) ·
    Arcane = **Blink** (`BlinkDistance` teleports the caster along their aim, ray-stopped by world
    geometry) · Nature = **Lifebloom Totem** (`SummonDuration` spawns a `SpellTotem` that heals its
    owner per tick) · Necrotic = **Ember Siphon** (23H corrupted lifesteal). The **spellbook** (the
    character screen's Spells tab) is now a school view: spells grouped under a per-school header
    showing **mastery rank/cap + power bonus** with a progress bar toward the next rank (29.5C data),
    cast-mode tags (`[charged]`/`[channeled]`), and the existing Buy/Upgrade rows — funded by a new
    **SpellPoints** pool on `ProgressionComponent` (1/level, saved as `spell_sp`) so spells no longer
    compete with perks for skill points. **Charge/channel feedback**: `GameHud` gained a school-tinted
    cast meter under the vitals (fills with `SpellcastingComponent.ChargeProgress`, pinned full while
    channeling) and the prepared-spell footer states `charging…`/`channeling`. All strings through
    `Loc` (catalogue 260). Build + **313 tests** (5 new `SpellHomingTests`) + `--validate` (12 spells,
    exit 0) green; in-engine boot clean. **Phase 29.5 (Spellcraft & the Fading Weave) is complete.**

---

## Phase 30 — Animation, Models & Visual Identity `[P]`

> Art-heavy. Model authoring (30B, 30D, 30H) is built in Blender via the Blender MCP
> (`mcp__blender__*`, CLAUDE.md §2) and exported to glTF under the Phase 19/57 import/LOD
> conventions; the human still supplies whatever the MCP doesn't cover (rig finishing,
> hand-painted texture passes, audio). Each sub-phase integrates one asset class against
> existing states.

- [x] **30A — Art-direction style guide** `[P]` ✅
  - **Done when:** `docs/ART_STYLE.md` pins the dying-world language (ash, faded
    colour, embers) + import/LOD conventions feeding Phase 19/57.
  - **Done:** `docs/ART_STYLE.md` written. Direction (maintainer-pinned): **low-poly but
    detailed — Skyrim's grounded, weathered fantasy realism in clean faceted geometry**
    ("carved, not sculpted"): silhouette-first, facets-as-feature, detail via material
    layering/painted wear (no photo textures — sourced PBR must be repainted/posterized).
    Pins the three-layer dying-world language (faded base / ash / ember accents) with a
    hex master palette + saturation discipline, per-realm grading for all five realms,
    the corruption-tier body arc (23F/30I hook), and the school VFX tint law off
    `SpellSchools.Color`. Conventions: per-class triangle + texel budgets and LOD
    thresholds (Steam-Deck-aware, the Phase 19/57 seam), material/vertex-color rules,
    1u=1m + 0.5 m modular-kit grid, fog-as-material atmosphere, and the Blender-MCP →
    `.glb` → `assets/models/<class>/` pipeline incl. collision suffixes, rig limits, and
    an `assets/CREDITS.md` licensing rule for sourced assets. Ends with a 7-point
    per-asset review checklist. Doc-only; no code touched.
- [x] **30B — Player character model** `[P]` ✅
  - **Goal:** the player has a real mesh to rig, not a placeholder capsule.
  - **Tasks:** built in Blender via the Blender MCP — base mesh + texture set matched
    to `ART_STYLE.md` (30A); modular gear/weapon attach points for the equipment the
    player can visibly wear/wield; export to glTF.
  - **Done when:** a static, importable player mesh with equip sockets exists in-engine.
  - **Done:** `assets/models/characters/chr_player_base.glb` (~1.6k tris, 1.8 m, origin at feet),
    built in Blender via the MCP as an **organic connected low-poly body** (skin-modifier over a
    stick skeleton → subsurf → decimate, smooth-shaded with ~60° sharp edges) after the first
    boxy draft was rejected as too Roblox — the maintainer's "low-poly but realistic-ish, never
    blocky" call is now pinned in `ART_STYLE.md` §1.1. Palette materials per region (skin, tunic,
    trousers, leather boots/belt/pads, steel bracers, gold buckle — flat colours, no textures) with
    slim faceted hard-surface gear; **five equip sockets** (`socket_hand_r/l`, `socket_back`,
    `socket_hip_l`, `socket_head`) as empties inside the glb. `PlayerFactory` now instantiates the
    model as `BodyMesh` (turned 180° for glTF→Godot forward, capsule fallback kept if the asset is
    missing), and `CorruptionAppearanceController` was generalized from one-material tinting to
    claiming **every surface material** under the body root (uniquely duplicated, per-material base
    albedo remembered) so each palette colour ashes toward the same dark tone per tier — the 23F
    contract unchanged. Two live-playtest fixes from the maintainer: the corruption emissive made
    the whole model glow red on a corrupted save → the ember-vein emissive now applies to **skin
    materials only** (names survive glTF import), dimmer and ember-toned per ART_STYLE §2.2, with
    clothes only ashing; and the belt buckle floated off the body → embedded into the waist mesh.
    Build + 313 tests green; headless `--import` + in-engine runs clean (the maintainer drove the
    world live during verification). Rigging/animation is 30C.
- [x] **30C — Third-person character + weapon rig integration** `[P]` ✅
  - **Done when:** the rigged player character (30B's mesh) + a weapon play
    attack/block/idle driven by combat states.
  - **Done:** the 30B body is now **rigged and animated**: a 17-bone humanoid armature
    (pelvis/spine/chest/neck/head + arm/leg chains) skinned with automatic weights in Blender
    (via the MCP) and six authored clips baked into the same glb through NLA tracks —
    `idle-loop`, `run-loop`, `block-loop`, `attack` (overhead right swing), `hit` (flinch),
    `death` (crumple). New `assets/models/weapons/wpn_sword_iron.glb` (~176 tris) hangs off the
    right-hand bone via a `BoneAttachment3D` (`PlayerFactory.AttachWeaponVisual`, bone found by
    name scan) so it follows every clip — purely cosmetic, hit timing stays with
    `MeleeWeaponComponent`. New `src/Animation/CharacterAnimationComponent.cs` drives the
    `AnimationPlayer` from existing state with **no new gameplay wiring**: `AttackPerformedEvent`
    → attack one-shot, `EntityDamagedEvent` → hit flinch (suppressed while blocking),
    `StatsComponent.IsAlive` false → latched death (unlatches on respawn),
    `CombatComponent.IsBlocking` → block loop, else horizontal velocity picks run/idle with a
    0.15 s crossblend. Clip names resolve by prefix so the importer's `-loop` handling can't
    break it, and any humanoid shipping those clip names (the 30F enemy sets) reuses the
    component unchanged. Build + 313 tests + `--import` green; verified in-engine live — the
    maintainer fought goblins with the rigged character during the run, no errors.
- [x] **30D — Core enemy + key-NPC model set** `[P]` ✅
  - **Goal:** the slice cast named in the Phase 30 header (core enemies, key NPCs,
    the boss) has real meshes, not the goblin-only placeholder.
  - **Tasks:** built in Blender via the Blender MCP — goblin model (+ one variant),
    the Iron King boss model, and the key Ember Crown NPCs from Phase 27 (vendor,
    innkeeper, guild rep) — each matched to `ART_STYLE.md` (30A); export to glTF.
  - **Done when:** each listed actor has a distinct, importable mesh/texture set.
  - **Done:** six organic low-poly models (the 30B skin-modifier technique, parameterized by
    height/bulk/head-scale/hunch) authored via the Blender MCP and exported: **goblin** (1.1 m,
    hunched, big-headed, mossy skin + loincloth, ~1.3k tris) and **goblin brute** variant (1.45 m,
    bulky) under `assets/models/creatures/`, the **Iron King** (2.4 m armored colossus — painted
    sabatons/faulds/gauntlets plus fitted pauldrons-with-spikes, a crown band sunk onto the skull,
    inset ember eyes, an embedded chest ember-core and girdle gem, ~1.5k tris; rebuilt once after
    the maintainer flagged hovering pieces — armor is now placed against measured mesh landmarks),
    and **vendor / innkeeper / guild rep** NPCs (distinct tunic/apron/robe palettes + hair) under
    `assets/models/characters/`. In-engine wiring where placeholders lived: `EnemyFactory` and
    `BossFactory` instantiate their models as "Mesh" (capsule fallbacks kept); `BossController`'s
    phase/telegraph glow generalized to claim the model's first **emissive surface** (the ember
    core) instead of requiring a capsule MaterialOverride; and `HitReactionComponent.FindMesh` now
    prefers the conventional `BodyMesh`/`Mesh` root — fixing the player hit-recoil that silently
    broke when 30B nested meshes under a skeleton. NPC meshes get placed when the town actors
    exist (30H dressing); goblin/boss animation sets are 30F. Build + 313 tests + `--import` +
    boot-to-menu run green.
- [x] **30E — Spell-casting animations + cast VFX by school** `[P]` ✅
  - **Done when:** casting plays animations and school-tinted VFX matched to
    `SpellSchools`.
  - **Done:** the player rig gained two clips (re-imported into Blender via the MCP, authored,
    re-exported — the glb now ships 8): **`cast`** (a left-palm thrust with chest turn, 12f
    one-shot) and **`channel-loop`** (a sustained two-hand reach with tremble). A stray
    `Icosphere` that had slipped into the 30C export was removed. `CharacterAnimationComponent`
    now subscribes to `SpellCastEvent`: an instant/charged release plays the cast one-shot **and
    pops a `SpellFlash` at the casting hand** (left-hand bone world pose via the skeleton, chest
    fallback) tinted `SpellSchools.Color(spell.School)` — the existing detonate flash reused as
    cast VFX, so every school's cast reads in its colour with no new VFX system. While
    `IsCharging`/`IsChanneling` the channel-loop pose wins the state pick (above block), and a
    channel's per-tick cast events skip the one-shot/flash spam. Build + 313 tests + `--import`
    green; in-engine run clean with the maintainer playing live. Real per-school particle VFX
    remain 30I's status/impact library pass.
- [x] **30F — Core enemy animation set (goblin + Iron King)** `[P]` ✅
  - **Done when:** locomotion/attack/hit/death sets (driving 30D's meshes) drive
    the existing AI/combat states for the slice cast.
  - **Done:** the goblin and Iron King glbs are now **rigged and animated** via a parameterized
    Blender-MCP pass (the 30C 17-bone humanoid armature scaled to each body's height/bulk/hunch,
    automatic weights) with five clips each baked through NLA tracks: `idle-loop`, `run-loop`,
    `attack` (goblin = wild right swipe; Iron King = two-hand overhead slam), `hit`, `death`.
    Bone-heat weighting failed wholesale on the Iron King's disjoint armor islands (all 1980
    verts unweighted) — fixed with **rigid nearest-bone binding** (point-to-bone-segment scan),
    which suits plate armor anyway. Both factories add the 30C `CharacterAnimationComponent`
    (`BodyMeshPath = "Mesh"`) — zero new gameplay wiring, since enemies already publish
    `AttackPerformedEvent` through the same `MeleeWeaponComponent`, take `EntityDamagedEvent`,
    and keep a death timer before `QueueFree` that gives the death clip room to play. Build +
    313 tests + `--import` green; verified in-engine live — the maintainer fought animated
    goblins and quick-saved repeatedly, no new errors (WASAPI audio-device noise pre-exists).
- [x] **30G — Third-person body for cutscenes/reflections** `[P]` ✅
  - **Done when:** a TP body exists for the Phase 43 cutscenes and corruption
    appearance (23F) hangs off it.
  - **Done:** satisfied by 30B/30C with no additional work — the rigged
    `chr_player_base.glb` **is** the third-person body (always visible under the TP camera,
    full clip set for Phase 43 to sequence), and `CorruptionAppearanceController` already
    hangs off it per-surface (30B): skin-only ember veins + whole-body ashing per tier.
    Recorded here so the checkbox reflects reality rather than re-doing the work.
- [x] **30H — World/environment model set for the Ember Crown slice** `[P]` ✅
  - **Goal:** the Phase 27 Ember Crown slice has real dressing, not greybox.
  - **Tasks:** built in Blender via the Blender MCP — town-hub building kit (inn,
    guild presence, vendor stalls, crafting stations, a housing-plot exterior) +
    wilds POI dressing (rocks, ruins, foliage), matched to `ART_STYLE.md` (30A);
    export to glTF.
  - **Done when:** the Ember Crown walkable slice can be dressed with real meshes
    instead of placeholder primitives.
  - **Done:** a 10-piece environment kit built via the Blender MCP (60–360 tris each, palette
    materials): `assets/models/architecture/bld_house_a.glb` (timber-framed gabled house fitted
    to the 6×5×8 greybox footprint — door, windows, beams) and `bld_house_b.glb` (the inn:
    stone base, chimney, ember sign, fitted to 8×6×6), plus `props/` — waystone monolith with an
    ember rune band, forge (stone hearth + ember bed + anvil), workbench, alchemy table
    (glowing retort), guild banner, rock cluster, ruin pillar, dead pine. **The town hub is
    dressed**: `town_hub.tscn`'s greybox `BoxMesh` visuals were stripped (all `StaticBody3D`
    colliders, entities, dialogue/schedule/station components untouched) and the kit instanced
    in their places — and the five capsule NPCs (elder, three vendors, innkeeper) now wear the
    **30D character models** (guild-rep robe for the elder, vendor/innkeeper bodies). The wilds
    get an augmentation pass: `wilds_north.tscn` gains scattered pines, rock clusters, and two
    ruin pillars (one tilted) over its existing greybox. `--import` + `--validate` (cell scene
    paths re-resolve) + in-engine streaming run green — the maintainer walked the dressed town
    and wilds live, both cells stream in/out with no errors.
- [x] **30I — Status/impact VFX library + corruption materials** `[P]` ✅
  - **Done when:** status effects + corruption tiers (replacing 23F placeholders)
    use real materials/VFX.
  - **Done:** **status VFX** — new `src/Magic/StatusEffectVfxComponent.cs`, a purely cosmetic
    component driven by the existing `StatusEffectAppliedEvent`/`RemovedEvent`: while a status
    afflicts an actor, a small looping school-tinted `GpuParticles3D` swirl (ring-emitted
    billboarded emissive quads, built in code — no scene asset) hangs on the body, so burning
    reads orange, chill ice-blue, regrowth green at a glance; on removal it stops emitting and
    fades out. Wired onto the player, goblin, Iron King, Ashen Acolyte and the training dummy.
    Impact/melee VFX already existed (`ImpactEffect`, Phase 29). **Corruption materials** — the
    23F linear-lerp placeholder is replaced with the ART_STYLE §2.2 **per-tier arc** in
    `CorruptionAppearanceController`: Touched = faint violet skin veining + violet emissive ·
    Marked = ash-grey skin patches + dim ember · Ashbound = charred skin + rising ember glow ·
    Embers = banked-coal skin + bright ember; clothing/gear only gathers ash, skin carries the
    tint + emissive. Also this sub-phase: CLAUDE.md §2 gains the maintainer's **Blender scene
    hygiene rule** (never leave models stacked at origin — lay assets out side by side so
    they're reviewable in the viewport). Build + 313 tests + `--validate` (exit 0) green;
    in-engine run clean with the maintainer playing. Final particle/material art is Phase 53.
    **Phase 30 (Animation, Models & Visual Identity) is complete (30A–30I).**
- [x] **30J — Maintainer art revision: poly bands, creature redesign, full greybox sweep** `[P]` ✅
  - **Goal (maintainer-directed, 2026-07-02):** (1) new triangle bands — player/key bosses
    ~1500, enemies/NPCs ~800, buildings/hero props 550–800, heavily instanced world props
    exempt ("looks good" is the bar); (2) enemies must read as **fantasy creatures, not
    humanoids**, goblins at ~2/3 player height; (3) **zero greyboxes** left anywhere in the
    world; (4) all existing models deep-upgraded to the bands.
  - **Done:** `ART_STYLE.md` §3 rewritten to the new bands. **Creatures** — goblin (1.12 m)
    and brute (1.4 m, horned) rebuilt as hunched, long-armed, knuckle-dragging creatures with
    snouts, ears/horns, tails, bone-pale back spikes, claws and digitigrade legs (~730–750
    tris), rigged (18 bones incl. tail) with creature clips (tail-wag idle, scamper, lunging
    double-claw attack, flinch, sideways-crumple death). **Player** rebuilt at exactly 1500
    tris and re-rigged with all 8 clips; **Iron King** rebuilt at 1500 with extra armor
    (knee plates, chimney-capped crown) and re-rigged. A mid-session player-deform bug the
    maintainer caught ("glitched and stretched") was root-caused to Blender's bone-heat
    solve silently producing garbage weights — all rigs now use **deterministic
    nearest-two-bone inverse-distance binding** (rigid snap when one bone clearly owns a
    vert), pose-verified in the viewport before export. **NPCs** rebuilt at 800 tris each.
    **New: Ashen Acolyte model** (752 tris, hooded robe, ember face-void/belt, rigged with
    cast clip) wired into its factory + `CharacterAnimationComponent` — no capsule enemies
    remain. **Env kit upgraded** into band (houses gain porches/ridge beams/side windows/
    chimney caps, waystone 12-facet + base stones + second rune ring, stations gain bellows/
    tools/stools/vials/shelves, banner gains finial/fray). **Greybox sweep** — new models
    (`prp_ruin_wall`, `prp_arena_wall` crenellated, `prp_brazier`, `prp_glacier`,
    `prp_training_dummy`, `prp_cache_chest`, `prp_tome_stand`, `prp_crate`, `prp_tent`,
    `prp_campfire`, `prp_relic`) replace every remaining primitive visual: wilds_north
    (ruin wall, rocks, fallen pillar, crate), wilds_west (rocks, tents, campfire + pines),
    arena (3 crenellated walls + challenge brazier), frostfang glacier cell (two faceted ice
    masses), town relic, and the bootstrap's training dummy/supply cache/Ashen Tome (all
    with fallbacks; colliders and gameplay untouched; ground planes remain terrain, item
    pickups remain glowing markers). Build + 313 tests + `--import` + `--validate` (exit 0)
    green; in-engine live run clean — the maintainer fought the new creature goblins and
    walked the dressed camps during verification.
- [x] **30K — Maintainer design pivot: first-person gameplay** `[P/F]` ✅
  - **Goal (maintainer-directed, 2026-07-02):** main gameplay is **first-person**; the
    third-person body/rig/clips are retained for cutscenes.
  - **Done:** the camera pivot moved to eye height (1.62 m) with the camera riding it
    directly (near plane 0.08); mouse-look mechanics unchanged (yaw on body, pitch on
    pivot), so aim/interact/lock-on all work as before. The player's own body renders
    **shadows-only** in first person — except the held-weapon subtree, so sword swings
    still sweep through view — and the full third-person rig + 8 clips stay intact.
    The seam is `PlayerController.SetFirstPerson(bool)`: `false` swings the camera back
    to the old orbit (offsets kept as `PlayerFactory.ThirdPerson*`) and re-shows the
    body — the Phase 43 cutscene director's hook. Identity docs updated (CLAUDE.md,
    README, ARCHITECTURE, LORE, ROADMAP: "first-person … third-person body retained for
    cutscenes"). Build + 313 tests green; verified live in-engine — the maintainer
    played first-person combat during the run, no errors.
- [x] **30L — Maintainer playability pass: lamps, FP arms, loot chests, visibility** `[P/F]` ✅
  - **Goal (maintainer-directed, 2026-07-02):** finish the playability batch cut short by a
    session limit — village/map lighting, first-person hands, a roomier inventory, and
    enemies readable through the fog (without losing the fog's mood).
  - **Done:** six `prp_lamp_post` instances placed around the town hub square (navmesh-carving
    colliders + warm `OmniLight3D` at the lantern, shadows off), plus firelight on the
    wilds-west campfire and the arena challenge brazier — the world's first point lights.
    First-person viewmodel (`FirstPersonArmsComponent`): `fp_arm.glb` pair riding the camera,
    right hand carrying the sword model, all-procedural motion (speed-driven walk bob,
    `AttackPerformedEvent` slash arc alternating with combo index, raised guard while
    blocking); the skeleton-held sword is now shadows-only in FP so it isn't doubled.
    Inventory/character screen fills the view with a 70 px gutter (full-rect anchors, scroll
    expands) instead of the old 440 px column. Fog weather density 0.04 → 0.025 so enemies
    resolve at combat range while the ashen veil (haze floor untouched) keeps the mood —
    pairs with 30J's emissive goblin eyes. Also landed from the cut-short session:
    `ContainerLootComponent` (E loots the supply cache's inventory into the player's, seeded
    potions + gold on fresh spawns). Build + 313 tests green; boot + `ContentValidator` OK
    in-engine; all three edited cells headless-instantiated clean (6+1+1 lights verified).
    The FP-arm motion itself is reviewed against the Godot 4.7 C# API — maintainer playtest
    pending.
- [x] **30M — Maintainer design pivot: hybrid first/third person** `[P/F]` ✅
  - **Goal (maintainer-directed, 2026-08-03):** the game is **both**. 30K's retained
    third-person rig becomes a real playable mode the player swaps to at any time from the
    settings menu, not just a cutscene pose.
  - **Done:** the settings toggle, the live `SettingsAppliedEvent` path and the body
    shadows-only swap all already existed from 30K — what was missing was everything that
    made third person *playable*. New `CameraRigMath` (pure, 14 tests) owns the three:
    an eased mode blend (0.18 s, snapped on initialize so a save resumed in TP opens there),
    the wall spring (sphere `CastMotion` pivot→camera, **instant pull-in / eased push-out**
    at 6 m/s, so the camera is never inside geometry — the gap 30K's own doc comment flagged),
    and the aim direction. `CameraRestPosition` now returns the *live* blended+sprung offset,
    so `CameraShake` follows it for free. Third person is over-the-shoulder: a 0.6 m shoulder
    offset joins the existing back/rise, body yaw still equals camera yaw in both modes, so
    combat, lock-on, dodge and melee reach needed no changes at all. **Aim parity:** the
    interact raycast now starts at the camera (not the head) with its reach measured from the
    *character*, so TP can never reach further than FP; spells aim along a new `AimPoint` node
    re-aimed each frame at the crosshair's convergence point. Both are exact no-ops in first
    person — the invariant the tests pin. `V` (`GameInput.ToggleCamera`) flips the persisted
    setting rather than a local flag, so the key and the panel can never disagree. Build +
    720 tests + `--validate` (exit 0) green; verified live in-engine — the maintainer fought
    goblins and looted during the `--play` runs, no errors.
  - **Follow-up (maintainer-requested, same session):** camera **distance** (2–6 m slider) and
    **shoulder side** (right / left / centred dropdown) exposed in the settings panel's Gameplay
    section, both applying live so they can be judged while being dragged. `ThirdPersonRest` reads
    them off `_settings.Current` per frame; `Settings.ShoulderOffset()` delegates to
    `CameraRigMath.ShoulderOffset` so the mapping is pinned by tests (including a nonsense side id
    falling back to the right shoulder rather than throwing). `SetFirstPerson` gained a
    no-change early-out, because the panel re-applies on every drag frame and the body-mesh
    shadow walk is not free. 724 tests, `--validate` exit 0, 4 clean `--play` runs. **FOV** (60–110°)
    followed in the Graphics section — applied by `PlayerController`, not `SettingsService`, since
    it belongs to the player's camera rather than the engine. The catch worth knowing:
    `FirstPersonArmsComponent` scales the arms by the ratio of the world and viewmodel FOV
    half-angle tangents, so it now re-derives that scale whenever the camera's FOV changes —
    otherwise the slider silently undoes the whole trick and the hands read undersized.
  - **Code-only deep debug pass (maintainer-requested, 2026-08-04):** a static audit of all 345
    source files. Six issues found and fixed:
    1. **Blocking menus suspended the player but not the world.** `GetTree().Paused` was set only
       for `GameState.Paused`; every modal `UiPanel` merely set `UiState.MenuOpen`, whose only
       gameplay consumer froze the *player* — and `DropHeldInput` drops the guard and cancels casts.
       So reading the inventory or talking to an NPC mid-fight left a frozen, un-blocking,
       un-dodging player taking free hits with DoTs still ticking. Fixed at the root:
       `GameManager.RefreshPause()` is now the single writer of the paused flag, answering
       `State == Paused || UiState.WorldPaused`, driven by a new `UiState.Changed` event.
       `UiState.Open(owner, pausesWorld: true)` is the default; the boss intro, the opening
       narration and the dev console pass `false` because the world must keep playing under them.
       `UiPanel` gained `ProcessMode.Always` or a modal panel would freeze itself on open.
       *(The per-system `MenuOpen` check was the tempting fix and is the one that had already
       failed — only `HitStopDirector` and `CompanionRoster` ever remembered it.)*
    2. **Far-LOD enemies barely advanced their wall-clock timers.** The sleep early-out returned
       before `_stateTimer += delta`, so those timers advanced one *frame* per 0.5 s sleep interval —
       a 12 s provoke memory ran for ~6 real minutes and the enemy never stood down. Slept time is
       now banked and applied as `wall`; movement and turn slew still use `delta`, since stepping a
       sleeping actor by half a second of motion would teleport it.
    3. **Wounded enemies ping-ponged Combat↔Retreat forever.** Nothing heals them, so the re-engage
       ending a retreat tripped the same `RetreatHealthFraction` check that started it. New
       `AIProfileResource.RetreatCooldown` (default 10 s, `0` restores the old behaviour) gates it.
    4. **`ServiceLocator` could hand out a freed node.** Six services register without ever
       unregistering and 11 of 25 read sites never checked `IsInstanceValid` — latent today only
       because `_sandboxBuilt` allows one world build per process. Fixed in the one read path
       (`Resolve`) rather than at 11 call sites: a freed registrant is dropped and reported.
    5. **Gamepad had no gameplay bindings at all** — it could open every menu and not walk out of
       the first room. Added sticks for move/look, RT/LT attack/guard, A/B jump/dodge, L3/R3
       sprint/lock-on, RB/LB cast/cycle, D-Left camera swap. Right-stick look is polled per frame
       (a stick is a held deflection, not a delta) through the new pure `SettingsMath.StickLookStep`
       — squared response past a 0.15 deadzone, framerate-independent, 8 tests.
    6. **README documented a 9-slot hotbar**; there are 5. Docs corrected (maintainer kept 5).
    740 tests, `--validate` exit 0, 6 clean `--play` runs.
  - **Trap paid on the way (now a CLAUDE.md §7 gotcha):** the rig was first hoisted *above*
    `PlayerController`'s not-playing guard so the camera would keep settling during a load. That
    dereferences the injected camera/pivot/aim nodes while a teardown is freeing them, and it
    produced an intermittent `gchandle.is_released` fatal on exit — **2 runs in 10** against
    **0 in 9** on the unmodified tree, with nothing in the gameplay log to point at it. Moving it
    back inside the guard (still above the menu-open return, so it settles with the inventory up)
    took it to 0 in 8.

---

## Phase 30.5 — UI & HUD Overhaul `[P/F]`

> Take the functional UI (Phase 14/18) and the individual surfaces grown across 23–30 to
> **one cohesive, ship-quality** look. Build the design system first (30.5A), then HUD, then
> menus, then feel/input. All strings go through the Phase 24 `Loc` layer. Each sub-phase
> leaves the game buildable/playable; verify in-engine (build → `run_project`, CLAUDE.md §3).

- [x] **30.5A — Design tokens + `docs/UI_STYLE.md`** `[P]` ✅
  - **Goal:** the foundation every other surface answers to.
  - **Done:** `UiTheme` regrown into token groups with the public API preserved (all ~36
    consuming files untouched): palette retuned from generic blue-grey/gold to the art
    bible's identity (warm-charcoal `PanelBg`/`Trough`, bone-pale `Text`, ash `Dim`,
    ember-gold `Accent` + rationed ember-orange `AccentHot`, corruption **violet** per
    ART_STYLE §2); a five-step type scale (`Caption`→`Display`) with a new `Caption`
    builder; spacing (`SpaceXs..Xl`) and radius (`RadiusSm/Md/Lg`) scales consumed by the
    styleboxes; motion tokens (`DurationFast/Base/Slow`) behind `UiTheme.Duration()`, which
    collapses to 0 under the reduced-motion setting (the 30.5I guard, landed early); and a
    shared interactive style with an ember **focus** stylebox on `Action`/`Dropdown` — the
    visibility seam 30.5J's gamepad navigation rides. `docs/UI_STYLE.md` written as the
    source of truth (identity, tokens, type/spacing/motion rules, widget + text rules,
    roadmap seams). Build + 313 tests green; booted in-engine to the token-rendered main
    menu, no errors.
- [x] **30.5B — HUD architecture & layout system** `[F]` ✅
  - **Done:** `HudLayout` (`src/UI/HudLayout.cs`) — a full-screen, mouse-transparent root with
    anchored **slots** (`TopLeft`/`TopCenter`/`TopRight`/`BottomLeft`/`BottomCenter` stacks +
    a free `Overlay` layer) inside a single safe-area margin (`SafeMargin`, token `SpaceLg`),
    replacing every per-widget anchor/offset in `GameHud`. The top-centre widgets (compass,
    boss bar, event banner, nameplate) now stack with automatic spacing — hidden widgets
    collapse, fixing the hand-tuned y-offsets (6/14/40/66) that visually collided when
    several showed at once. **UI scale made real:** the previously-dead `Settings.UiScale`
    (0.75–1.5 slider, Phase 24F) now drives `Window.ContentScaleFactor` in
    `SettingsService.ApplyGraphics` — with the project's `canvas_items` stretch this scales
    every 2D surface live, no per-widget math. **Bring-up findings (live maintainer
    playtest):** anchors applied *after* tree entry resolved against a stale zero rect and
    dumped every widget at the origin — `HudLayout` now builds fully in its constructor
    ("build detached, then add", §6). The bottom edge became a full-width **flow bar**
    (vitals left, twin spacers centring a `BottomDock` the quick-use hotbar parents into via
    `HotbarPanel.Dock`) after the maintainer hit the hotbar/vitals overlap at 1.5× UI scale —
    flow siblings can't overlap at any scale/resolution (hotbar cells 112→88 px, and its
    visibility toggle moved from the layer to the docked panel). Placement sweep across all
    other surfaces: toasts moved top-centre→top-right (the centre column belongs to the
    boss/banner/nameplate stack), the F3 DebugHud readout dropped below the clock widget and
    its controls hint moved to bottom-right, the journal overlay dropped below the clock.
    Build + 313 tests green; verified across three live maintainer play sessions.
- [x] **30.5C — Core HUD widgets rebuilt** `[F/P]` ✅
  - **Done:** `JuicedBar` (`src/UI/JuicedBar.cs`) — a themed resource bar with value-change
    juice: rises instantly (heals feel responsive), drains with a ~0.9/s lag so hits read as
    a chunk sliding off, and pulses the fill white-hot for 0.25 s on a drop; honours reduced
    motion (snaps, no pulse) and exposes `Snap()` for subject changes. Drives the three
    vitals, the nameplate (snaps when the aimed-at target changes so lag never animates
    across subjects) and the boss bar (snaps full on encounter start). **Prepared-spell
    widget:** school-tinted spell name + READY(accent)/charging/channeling/`N.Ns` state
    caption + a thin school-tinted recovery bar that fills while the spell cools down —
    replacing the old footer text blob (footer now shows level only). **Status-effect
    chips:** the text line became per-effect chips (buff = dead-green, affliction = school
    colour, live countdown); the row rebuilds only on set-signature change, timers update
    in place. Crosshair joined the palette (bone-pale token). Build + 313 tests green;
    verified during a live maintainer combat session (goblin fight: damage drain/pulse and
    nameplate exercised, no errors).
- [x] **30.5D — Wayfinding HUD** `[F/P]` ✅
  - **Done:** the wayfinding surfaces unified on the 30.5A/B system. **Interaction prompt:**
    a real keycap chip (`UiTheme.KeyCap`) + prompt text, the cap's glyph resolved live from
    the InputMap via `GameInput.KeyLabel` (remap-proof — the Phase 54 seam). **Quest
    tracker:** the per-frame StringBuilder blob became structured rows (accent title, one
    caption per objective, complete objectives tick to dead-green with ✓) rebuilt only when
    the quest id/progress signature changes. **World-event banner:** the countdown split
    into its own readout that heats to ember orange (`AccentHot`) in the final 10 s.
    Compass and nameplate were already on-system (30.5B/C); toasts were restyled/moved in
    30.5B and keep their fade (slide motion is 30.5I). Build + 313 tests green; verified in
    a live maintainer session (goblin kills advanced the tracker rows; loot prompts used
    the keycap chip; no errors).
- [x] **30.5E — Combat & boss HUD** `[F/P]` ✅
  - **Done:** combat feedback unified on the identity. **Crosshair hit-marker:** landing
    damage kicks the reticle arms outward for 0.18 s — ember orange (`AccentHot`) on crits,
    bone pale otherwise — so hits confirm at the point of aim; suppressed under reduced
    motion, and the reticle only redraws while a pop decays. **Screen-flash tints**
    (`CombatFeedbackFx`, still pure/unit-tested) retuned from generic gold/steel-blue to the
    dying-world identity: crit = ember orange, block = cold steel, stagger = ashen red,
    parry = bright ember-gold pop; under reduced motion the full-screen flash is suppressed
    (photosensitivity) and the state word alone shows. **Lock-on reticle** breathes
    (0.8–1.0 alpha sine) so the lock reads live, motion-gated. Boss bar was juiced in 30.5C
    (drain lag + snap-full on encounter start); the corruption vignette moved to the
    bible's violet in 30.5A. Build + 313 tests green; verified in a live maintainer combat
    session (damage dealt and taken, no errors).
- [x] **30.5F — Panel & screen framework** `[F]` ✅
  - **Done:** `UiPanel` (`src/UI/UiPanel.cs`) — the reusable panel shell every screen builds
    on: themed frame, optional toggle input action, the **modal contract** (register with
    `UiState` + free/recapture the mouse; non-modal overlays skip it via `Modal => false`),
    and the rebuild-from-a-dirty-flag loop (`MarkDirty` → at most one `Rebuild` per frame,
    never inside a button signal). Subclasses implement `BuildShell` (static layout, once)
    and `Rebuild` (dynamic rows). **`UiTabs`** — the shared tab strip (accent-highlighted
    active tab, `TabChanged` event). **`UiTheme.ScrollList`/`ClearChildren`** — the list
    body + rebuild-clear helpers every panel repeats. **Proving port:** `InventoryPanel`
    (the richest panel: modal + tabs + scroll list) rebuilt on the framework with ~80 lines
    of hand-rolled plumbing deleted, feature-parity. *Scope note:* item tooltips stay on
    Godot's native `TooltipText` (already themed-adjacent, zero code); a screen/route
    manager was deliberately skipped — the four meta screens (menu/creator/slots/loading)
    are wired directly by the bootstrap and a router would be speculative structure until
    30.5J's gamepad focus needs a screen stack. Build + 313 tests green; verified in a long
    live maintainer session (combat, region travel, saves; no errors).
- [x] **30.5G — Inventory / character / equipment / perks panels rebuilt** `[F/P]` ✅
  - **Done:** the sub-phase was authored before these surfaces merged into the tabbed
    character screen (maintainer note), so 30.5G = the four **tab pages** (Gear incl.
    equipment+backpack, Spells, Progression, Perks) fully on the 30.5F framework + tokens.
    The panel shell/tabs/scroll ported in 30.5F's proving port; this pass finished the
    content: the last colour literals tokenized (dread → `Corruption`, affix lines →
    `Caption` in `Good`), row spacing on the spacing scale, and a **level-XP progress bar**
    added to the Progression tab (accent fill, the same glanceable shape as the corruption
    gauge below it — previously XP was numbers-only while corruption had a bar).
    Feature-parity everywhere else. Build + 313 tests green; verified in a live maintainer
    session (post-load full-vitals confirmed at legendary-boosted max HP; no errors).
- [x] **30.5H — Crafting / dialogue / journal / map panels rebuilt** `[F/P]` ✅
  - **Done:** all four remaining panels ported onto the 30.5F `UiPanel` framework, deleting
    their duplicated modal/mouse/dirty-flag plumbing. **CraftingPanel** — modal; the
    craft/salvage switch moved from rebuilt-per-frame disabled-button rows into a static
    `UiTabs` strip in the shell (resets to Craft on open), title now a static header updated
    per station; keeps its E-to-close with the just-opened swallow via a `_Process` override
    around the base. **DialoguePanel** — keeps its modal behaviour (per spec); event-driven
    open/close and the `DialogueEndedEvent` publish unchanged. **QuestLogPanel (journal)** —
    stays **non-modal** (`Modal => false`, per spec), J-toggle via the base's `ToggleAction`;
    its literal colours tokenized (title `Accent`, done rows `Good`, pending `Text`).
    **MapScreen** — modal (Phase 25G fast-travel buttons need the mouse; the spec's
    "non-modal" predates 25G), M-toggle via the base; revision-diff refresh kept as a
    `_Process` override that `MarkDirty()`s. Build + 313 tests green; booted in-engine to
    the world with all panels constructed, no errors.
- [x] **30.5I — Motion & microinteractions** `[F/P]` ✅
  - **Done:** the motion pass on the 30.5A tokens, all of it behind the reduced-motion guard
    (`UiTheme.Duration`/`MotionEnabled` collapse everything to instant/static). **`UiMotion`**
    (`src/UI/UiMotion.cs`) — the pure ease-out/ease-in/progress curves, unit-tested like
    `CompassMath`. **Panel transitions:** `UiPanel.SetOpen` fades the shell in (ease-out,
    `DurationBase`) for every panel on the framework — closing stays instant so dismissal never
    lags input; the pause menu fades in the same way, and the loading screen now fades *out*
    (`DurationSlow` ease-in exit) to reveal the arrival while still covering instantly on entry.
    **Toasts:** the slide-in deferred from 30.5B — `Toast` became a margin wrapper around the
    chip (+s/−s margins shift it without fighting the stack container) sliding 24 px in from
    the right while fading up. **Hover/press:** `UiTheme.ApplyInteractiveStyle` layers a
    modulate ease (`AnimateModulate`, kill-previous, pause-proof) over the stylebox swap —
    brighten on hover/focus (the 30.5J seam), sink on press, on every `Action`/`Dropdown`.
    **Value changes:** damage juice already landed in 30.5C/E (drain lag, pulse, hit-marker);
    this pass added the XP/level beats — a "+N XP" pop under the level footer that accumulates
    rapid gains then fades, and a centred Display-size "Level N" flourish (fade in → hold →
    fade out with a slight rise; ember gold). Build + 317 tests green; verified in a live
    maintainer session (pause/settings cycles, two region transitions with the loading fade,
    pickups; no errors).
- [x] **30.5J — Gamepad & focus navigation** `[F]` ✅
  - **Done:** controller/keyboard drive every menu; no menu is mouse-only. **Input layer:**
    `GameInput.BindGamepad` — Start=pause, Y=inventory, Select=journal, D-Up=map, X=interact,
    and the left stick mapped onto the built-in `ui_*` focus actions (D-pad/A/B are engine
    defaults); full gameplay-on-controller stays Phase 54. **Focus system:** `UiFocus`
    (`src/UI/UiFocus.cs`) — grab-first-focusable on open, and a child-index **path
    record/restore across the dirty-flag rebuild** so a rebuild never strands focus; wired
    into `UiPanel` (grab on open, restore on every rebuild), the pause menu, settings, main
    menu (re-grabs whenever a sub-screen restores it), save slots (deferred re-grab after
    refresh) and the character creator. `ScrollList`/settings scrolls set `FollowFocus`.
    **Cancel routing:** ui_cancel (Esc/B) closes modal `UiPanel`s (dialogue opts out — it ends
    via choices), backs out of settings/slots/creator (creator ignores it while a text field
    has focus), resumes from the pause menu; a `LastCancelCloseFrame` stamp keeps one Esc
    press from both closing a panel and opening the pause menu. **Device-aware glyphs:**
    `InputDevice` (fed by `GameManager._Input`) tracks the active device and publishes
    `InputDeviceChangedEvent` (mouse *motion* deliberately ignored); `GameInput.PromptLabel`
    resolves key vs pad glyphs live from the InputMap and the HUD keycap swaps "E" ↔ "X" on
    device flip. The pad-button label map is pure and pinned by tests (317 → 327). Build +
    tests green; booted in-engine to the token-rendered main menu with the focus grab live,
    no errors. Full-loop gamepad play verified next maintainer session.
- [x] **30.5K — UI scale & legibility pass** `[F/P]` ✅
  - **Done:** the global UI-scale option already landed real in 30.5B (`Settings.UiScale`
    0.75–1.5 → `ContentScaleFactor`), and the 1280×720 `canvas_items`/expand base means Steam
    Deck (1280×800) renders at reference scale — so this pass was the **audit**, made
    executable. **`UiContrast`** (`src/UI/UiContrast.cs`) — pure WCAG luminance/ratio math;
    **`UiContrastTests`** pins every text-bearing token pair ≥4.5:1 (AA) and bar fills ≥3:1,
    so a palette retune can never silently regress readability. The audit caught and fixed:
    `Dim` below AA on button faces (retuned 0.55/0.53/0.47 → 0.58/0.56/0.50); the deep
    corruption violet used as *text* at 2.8:1 (new `CorruptionText` token for text, the
    bible's fill violet untouched for gauge/vignette); and two **double-dim modulate bugs**
    (`UiTabs` inactive tabs and the HUD spell-state caption multiplied `Dim` onto already-dim
    fonts, ~2.9:1) — both now swap font colour, and the style guide bans whole-control
    modulate for text. Font sizing: `CaptionFontSize` 11 → 12, the pinned legibility floor.
    Build + 344 tests green (17 new); booted in-engine clean. **Phase 30.5 complete.**

---

## Phase 31 — Audio Foundations `[F/P]`

- [x] **31A — `AudioDirector` + Godot audio buses** `[F]`
  - **Done when:** master/music/SFX/ambience/UI/voice buses exist, registered in
    `ServiceLocator`, volumes wired to `SettingsService` (24E).
  - **Done:** `src/Audio/` — `AudioBusLayout.Ensure()` creates the six buses at boot (before the
    first settings apply, so every volume slider takes effect); `AudioDirector` (ServiceLocator-
    registered, `ProcessMode.Always`) consumes the already-published `SoundCueRequestedEvent` /
    `MusicCueRequestedEvent`, playing pooled 3D/2D one-shots. `ProceduralAudio` synthesizes
    placeholder PCM streams (no binary assets; swap for recordings at Phase 52); `AudioLibrary`
    is the cue-id→stream registry (unknown id → silent + warn-once); routing (bus + positional by
    id prefix) is the pure, unit-tested `AudioCueRouting`. Verified in-engine: `buses=6`, combat
    SFX live through a goblin fight, zero errors.
- [x] **31B — Adaptive music state machine** `[F]`
  - **Done when:** exploration/combat/boss/safe states crossfade, driven by
    EventBus (combat start/end, boss start, region/day-phase change).
  - **Done:** `MusicDirector` + pure `MusicStateMachine` (boss > combat > safe > explore, unit-tested).
    Combat tracks enemies in Combat/Retreat via `EnemyStateChangedEvent` (cleared on state change,
    `EntityDiedEvent`, or a freed-body prune); boss from `BossEncounterStartedEvent` until the boss
    dies; safe polls `SafeZones`. Two looping players crossfade 1.5s on the Music bus; beds come from
    the shared `AudioLibrary` (real CC0 track per state when present, else a distinct procedural pad).
    Verified in-engine: `MusicDirector ready`, combat state entered/left through a goblin fight, zero
    errors. *(Real CC0 music tracks per state are a follow-up; procedural pads hold until then.)*
  - Also in this checkpoint: **fixed the world map rendering off-screen** (top-right corner) — `MapScreen`
    used `SetAnchorsPreset(Center)`, which reseated its offsets against the shell's zero build-time size;
    now uses the explicit centre-anchor + offset pattern the other panels use.
- [x] **31C — Combat & interaction SFX hooks** `[F/P]`
  - **Done when:** hit/cast/pickup/level-up/UI events fire SFX through the director.
  - **Done:** `AudioDirector` now also consumes `ItemPickedUpEvent` (positional `sfx.pickup`),
    `SpellCastEvent` (positional `sfx.cast`), and `LeveledUpEvent` (2D `sfx.levelup`); combat hit
    SFX already landed in 31A. UI clicks route through one seam — `UiTheme.Action` plays `ui.click`
    on every menu button's press. Real CC0 for cast (`spell_01`, rubberduck, OpenGameArt) + the
    Kenney pickup/UI files; level-up stays procedural until sourced. The `AudioLibrary` load helper
    was unified so procedural-until-sourced cues log at info (not warning) — the error channel stays
    clean. Verified in-engine: `19 cues, 13 real`, combat/pickups through a fight, zero errors.
- [x] **31D — 3D ambience per region/weather/time** `[F/P]`
  - **Done when:** regions/weather/day-phase drive looping 3D ambience beds.
  - **Done:** `AmbienceDirector` + pure `AmbienceSelection` (weather > town > day/night, unit-tested)
    crossfade a looping bed on the Ambience bus, driven by `WeatherChangedEvent` /
    `TimeOfDayChangedEvent` and a polled `SafeZones` "in town" signal. Beds `amb.{day,night,rain,town}`
    come from the shared `AudioLibrary` (real CC0 field recording per bed when present, else a procedural
    filtered-noise wash). Also added a **`--play` dev arg** (parallels `--validate`) that boots straight
    into the most recent save so gameplay/directors launch deterministically for verification. Verified
    in-engine via `godot --path . -- --play`: `AmbienceDirector ready`, world built, zero errors.
- [x] **31E — Footsteps by surface** `[F/P]`
  - **Done when:** footstep SFX vary by surface material under the player.
  - **Done:** `FootstepComponent` on the player emits a positional step cue every stride while grounded
    and moving; the pure `FootstepGait` paces footfalls (cadence tracks speed) and a short downward ray
    reads the floor collider's `surface` node-metadata, mapped by `Surfaces.CueFromTag` to
    `step.{grass,wood,stone,snow}` (real Kenney footstep files, CP1.5) with a stone default when untagged.
    Both pure helpers unit-tested (12 cases). Verified in-engine via `--play`: component live, no inert
    warning, zero errors. *(Calibration knob: region floor colliders aren't tagged yet, so footsteps
    default to stone until a floor sets `surface` metadata — a content pass.)*

> **Phase 31 — Audio Foundations complete.** Mixer + `AudioDirector`, real CC0 SFX, adaptive music,
> interaction/UI SFX, environmental ambience, and surface footsteps all landed. Remaining audio polish
> (real CC0 music tracks + ambience field recordings, surface tagging) is production work toward Phase 52.

---

## Phase 32 — Companion System `[F]`

- [x] **32A — `CompanionComponent` + follower AI core** `[F]` ✅
  - **Done when:** a companion follows/holds on the player's team, reusing
    `EnemyAIComponent`/`Locomotion`/`Combat`; recruit/dismiss API; `ISaveable`
    roster.
  - **Landed:** `CompanionAIComponent` (anchor/leash follower FSM on the shared
    `LocomotionComponent` + `PathSteering` + `MeleeWeaponComponent`), the pure
    `CompanionDecision`/`CompanionFormation` cores (15 unit tests), `CompanionFactory`
    (team 0) + `CompanionRegistry`, and an `ISaveable` `CompanionRoster` with
    recruit/dismiss/stance + a save-reconciling party. Kael is recruitable via the
    `companion` dev command; toasts on join/leave/down.
- [x] **32B — Command states (follow / hold / engage)** `[F]` ✅
  - **Done when:** the player can command stance via a quick command; combat assist
    works.
  - **Landed:** an `Engage` order alongside follow/hold; `C` (D-pad right) cycles the
    party's standing order with a toast; the pure `CompanionOrders` sets each order's
    leash/scan envelope (6 tests); assist focus makes companions prioritise the
    player's lock-on target; an engage order stands itself down once the fighting
    stops; a self-hiding `PartyWidget` shows each companion's health + current order.
- [x] **32C — `CompanionResource` + loyalty standing** `[F]` ✅
  - **Done when:** companions are data (`CompanionResource`) with a per-companion
    loyalty standing (reuse `ReputationComponent` patterns), persistent.
  - **Landed:** `CompanionResource` + `CompanionDatabase` (`data/companions/Kael.tres`);
    the registry and factory now build entirely from the resource (stats, weapon,
    model, faction, spells, follower envelope). Loyalty is a 0–100 standing with
    Wary/Steady/Trusted/Sworn tiers held and persisted by the roster (kept even for
    dismissed companions), projected onto stats by `CompanionLoyaltyComponent`.
    Dialogue gained `RecruitCompanion`/`DismissCompanion`/`AddCompanionLoyalty`
    effects and `CompanionRecruited`/`CompanionNotRecruited`/`CompanionLoyaltyAtLeast`
    conditions, so 32E is authorable content. Validator + 24 new tests.
- [x] **32D — Party persistence + save round-trip** `[F]` ✅
  - **Done when:** roster, positions, and loyalty survive save/load and region
    streaming.
  - **Landed:** the party save now carries each companion's transform, and loading
    is a *reconcile* (pure `CompanionPartyReconcile` + 7 tests) — survivors keep
    their actor and move, only genuine newcomers are built. `CompanionAIComponent`
    became `ISaveable` for the state the roster can't see (hold anchor, downed +
    recovery countdown). Region hard-loads call `RegroupNow()` so the band cuts to
    formation the moment the player lands, while held companions stay put. A
    `party` repro scenario pins a deterministic party-in-the-field run.
- [x] **32E — Kael authored fully (recruit + loyalty quest + dialogue)** `[C]` ✅
  - **Done when:** one complete companion (Kael) is recruitable with a dialogue
    graph + recruit quest + loyalty quest; the rest deferred to Beta.
  - **Landed:** Kael Aldemar, last shield of the Emberguard, stands in the Ember
    Crown hub. A 14-node conversation carries the whole arc: the recruit quest
    *The Oathkeeper's Debt*, the recruit itself, the loyalty quest *What the Ash
    Took* (his sword-brother Toren's plunder), a one-time loyalty payoff sealed
    behind a story flag, a trust-gated personal line, and an amicable parting.
    `CompanionRecruiterComponent` swaps the town NPC out while he travels with you
    and back when dismissed; the recruited actor carries his own dialogue so
    personal content is reachable from the party member. `KaelContentTests` checks
    the authored graph (reachability, string keys, ordinals, prerequisites) without
    needing Godot.

**Phase 32 complete** — 32A–32E all landed.

---

## Phase 33 — Vertical Slice Assembly & Onboarding `[C/P]`

- [x] **33A — Opening sequence + new-game → creation → world flow** `[C/P]` ✅
  - **Done when:** new game runs creation → opening → Ember Crown as one seamless
    flow.
  - **Landed:** `OpeningSequence` — five narration cards over black carrying the
    LORE premise and closing on the player's own name, played *over the
    already-built world* so the last card lifts on the Ember Crown with nothing
    left to load. Input is held through `UiState` with the mouse still captured;
    interact/attack skip it (Esc deliberately doesn't, so one press can't also
    open the pause menu); it never plays on a load. Pacing lives in the pure
    `OpeningTimeline` (9 tests). `opening` dev command replays it.
- [x] **33B — Diegetic tutorial: movement/look/combat** `[C/P]` ✅
  - **Done when:** move/look/attack/block/dodge are taught via prompts/toasts,
    skippable.
  - **Landed:** `TutorialDirector` teaches look → move → sprint → attack → block →
    dodge by *watching the player play them* — nothing blocks input, gates a door,
    or waits on a modal. Completion reads real game state (a swing is
    `MeleeWeaponComponent.IsCommitted`, a dodge is `Locomotion.IsDashing`), so a
    keypress that did nothing teaches nothing. One self-hiding `TutorialHint` line
    above the hotbar is the whole visible footprint, with live key/gamepad glyphs.
    A **Show Tutorial Hints** setting switches it off live; progress persists so a
    reload never re-teaches. Pure `TutorialScript` (9 tests + ordinal pin);
    `tutorial <status|skip|restart>` dev command.
- [x] **33C — Diegetic tutorial: magic/interact/inventory/quests** `[C/P]` ✅
  - **Done when:** the remaining verbs are taught the same way; nothing blocks a
    veteran from skipping.
  - **Landed:** interact → inventory → journal → cast appended to the same script
    (interact first because it is the verb that makes anything happen; magic last
    because it is the only optional one). These are discrete moments rather than
    held inputs, so they arrive as events — two new ones, `InteractionPerformedEvent`
    (published where the interaction actually fires, not on the keypress) and
    `UiPanelToggledEvent` (any `UiPanel` open/close, reusable beyond onboarding) —
    plus the existing `SpellCastEvent`. Still nothing blocks input, and the Settings
    toggle / `tutorial skip` end the whole thing at any point.
- [~] **33D — Slice stitch: quest chain → guild taste → Iron King → corruption beat → cliffhanger** `[C/P]`
  - **Done when:** 30–60 min plays as one continuous, polished arc.
  - **Built:** the brazier is quest-gated with a prompt that says why; the elder names Kael once the
    bounty is done and carries a corruption warning before the arena; regions declare an
    `UnlockFlagId` so the Frostfang door stays out of the starting square until the Iron King falls;
    `SliceDirector` + `ClosingSequence` end the arc on a card that branches on whether the ember was
    taken; the auto-seeded sandbox quest is gone so the journal starts empty; the elder's
    conversation moved off literal strings. `DialogueContentTests` now validates **every** graph.
  - **Outstanding:** the arc has never been played — see `VERTICAL_SLICE_PLAN.md` §5.2.
  - **Plan:** see [`VERTICAL_SLICE_PLAN.md`](VERTICAL_SLICE_PLAN.md) — the locked design decisions
    (warband chain as spine with Kael woven through, hard-gated boss, closing card + Frostfang
    portal), the eight beats, the seven gaps to close, and a task-by-task build order with
    acceptance criteria.
- [~] **33E — Slice polish + external-build capture pass** `[P]`
  - **Done when:** a capture-ready external build candidate exists; rough edges in
    the slice path are gone.
  - **Built:** `BuildProfile` gates every piece of sandbox scaffolding — the training
    dummy, debug camp, loose loot, spell tome, F1/F3/F4 overlays and the single-key
    cheats — so an **exported build is the slice automatically**, with `--capture`
    giving the same experience from the editor. `export_presets.cfg` adds Windows and
    Linux presets. Capture checklist and the known cosmetic gaps are documented in
    `VERTICAL_SLICE_PLAN.md` §8.
  - **Built (local session, Blender MCP):** Kael has his own model —
    `assets/models/characters/npc_kael.glb`, 785 tris, built on the player's rig so he
    inherits the whole clip set and actually animates in combat. `Kael.tres` and the
    `town_hub.tscn` `Model` instance both point at it, closing §6.6 / §8.4 / §8.5.
    Also fixed: `--validate` never called `Loc.Initialize()`, so headless runs reported
    Kael's authored display keys as missing and the gate was red on `main`.
  - **Play-through (maintainer, 2026-07-30):** the §5.2 full arc was played locally and
    came back clean — no blocking findings. That closes `VERTICAL_SLICE_PLAN.md` §4.8
    Task 8 and the polish half of 33E.
  - **Outstanding:** the export presets have never been opened in Godot's export dialog
    (§8.2) — the last Gate G1 item, and one that needs a human in the editor.

> **🚩 Gate G1 — Vertical Slice.** A stranger plays 30–60 min that looks and feels
> shipped: real art/audio, weighty combat, a companion, a boss, the corruption
> payoff. (Roadmap §3.)

---

# Stage C — Alpha / Feature Complete (→ G2)

> After G2 we never invent a mechanic again. Front-load **all** remaining systems.

---

## Phase 34 — Enemy & Creature Roster `[F/C]` ✅ **complete**

> Turned the enemy roster from code into content. **26 creatures are spawnable by id and only
> three have a factory** — the rest are `.tres`. The systems reference is
> `ARCHITECTURE.md` §2.5; the authoring recipes are CLAUDE.md §8. This block is the log.

- [x] **34A — AI behaviour profiles: data-fy `EnemyAIComponent`** `[F]`
  - `AIProfileResource` + `AIProfileDatabase` (`data/ai_profiles/`, ids `ai.*`). Every knob the
    component exported moved onto the resource, and **the component stayed one class** — each
    behaviour is a branch gated on a profile number, so they compose (a shielded flanking ambusher
    is authorable) instead of forking the brain. Pure `GuardCycle` + `PackFlank` hold the testable
    arithmetic. The goblin kept `ai.brute` at the old defaults, so slice feel was untouched.
- [x] **34B — Humanoid archetypes (bandit, cultist, soldier, Iron Syndicate)** `[F/C]`
  - `EnemyArchetypeResource` + `EnemyArchetypeDatabase` (`data/enemies/`) driven by one shared
    factory, which registers a builder per archetype with `EnemyTemplateRegistry` — a new `.tres`
    is spawnable with no code change. Four archetypes, four encounters, two new factions
    (`faction.outlaws`, `faction.iron_syndicate`).
- [x] **34C — Beast archetypes** `[F/C]`
  - Grey wolf, dire wolf, frost stalker, thornback boar, ashfall elk on two new profiles
    (`ai.territorial`, `ai.prey`), plus `faction.beasts` — hostile by default with **zero kill
    penalty**, the standing a Sylthari communion perk can later flip. Beasts carry no coin.
  - `HumanoidEnemyFactory` → **`EnemyArchetypeFactory`**: it builds quadrupeds now, so the name had
    to stop lying. Melee reach became body-relative (`height / 1.8`), since a 0.9 m wolf was
    otherwise biting a metre past its own nose. Only behaviour delta: the 1.85 m soldier's box grew
    2.8%.
- [x] **34D — Undead archetypes (the Hollow Queen's legions)** `[F/C]`
  - Hollow husk, bone knight, barrow wight, grave shade and a necromancer on two fearless profiles
    (`ai.mindless`, `ai.deathless_guard` — every prior melee profile retreats on wounds, and the
    dead shouldn't), plus `faction.hollow` and the Necrotic school's first enemy content
    (`spell.wither` + `status.decay`).
  - **The first caster archetype authored as data** — all nine prior archetypes had empty
    `KnownSpellIds`, so the path 34B built had never run. `spell.knit_bone` bought ally-mending for
    free from 34A's caster-support branch: the necromancer repairs its own husks with no new code.
  - Bug fixed at the root: the spellbook renders *every* spell in `SpellDatabase`, so monster
    loadouts leaked in as purchasable. `SpellResource.PlayerLearnable` + one filter at the single
    seam every future faction caster routes through.
- [x] **34E — Construct + elemental archetypes** `[F/C]`
  - Three constructs (new `ai.sentry` — holds its post, never patrols, never calls for help) and one
    elemental per offensive school, each resistant to its own.
  - **Had to land a mechanic first:** `CombatMath.Mitigate` mitigated only Physical, so nothing could
    resist a magic school and an elemental had no way to be elemental. Six resistance stats through
    the *same* `ArmorMultiplier` curve — resistance, never immunity. It also closed a live bug where
    a school-typed melee weapon bypassed armour entirely.
  - `spell.arcane_lance` — Arcane's first offensive spell; it had only Self casts.
- [x] **34E.5 — Arcane on-hit dispel** `[F]`
  - An Arcane hit strips the target's longest-lasting buff, never a harmful one, one per hit.
    **Every magic school now has an on-hit identity** — the table 29.5B opened is closed. A Self cast
    can't trigger it (`OnSpellHit` is only reached from the projectile/area paths).
- [x] **34F — Corrupted / Ashen creatures** `[F/C]`
  - Built as a **variant layer**, not another roster row: `AshenAffliction` takes any spawned enemy
    and makes it Morthul's — tougher, charred, ember-lit, worth more — rolled per enemy off
    `EncounterResource.CorruptionChance`. Corruption is authored per *place*, since LORE puts it on
    the realm and never on the player. 35E's "Ash dragon (corrupted elite)" inherits this.
  - Two flagships a modifier can't produce: `enemy.ash_maw` and `enemy.cinder_thrall`, which wields
    the player's own corruption-gated lifesteal.
  - One line of LORE added under Morthul — the sentence the mechanic rests on.
- [x] **34F.5 — Encounter table balance pass** `[C]`
  - A playthrough reported seeing far fewer new enemies than the roster held. The roster was fine;
    the table wasn't. Two archetypes had **no encounter at all** (34E shipped them `spawn`-only);
    the goblin still carried its Phase-4 weights and took **44% of every daylight roll**; and dawn
    was a duplicate of day. After: dawn 10→14 types, day 10→12, goblin share 44%→~20%.
- [x] **34G — `BestiaryDatabase` + bestiary UI** `[F/C]`
  - `B` opens the Ash Hunters' field journal: 26 creatures, seven tabs, kill counts, Ashen counts,
    and lore staged Unseen → Sighted → Known. Built on `UiPanel` + the `MapService` persistence
    shape; `EntityDiedEvent` already carried `TemplateId`, so no new event was needed.
  - Entries key off the **template id**, not the archetype — the goblin, Iron King and Ashen Acolyte
    have no archetype at all. Counts party kills, not just the player's (a quest is a contract; a
    journal is a record). Also fixed three hard-coded English `DisplayName`s.

### What outlived the session

- **Durable rules moved into the permanent docs**, which are the ones to trust: CLAUDE.md §8 has
  the recipes (new archetype, AI profile, bestiary entry, corrupted variant, new stat) and the
  traps — a caster needs spells *and* a standoff profile *and* a Mana pool or it silently never
  casts; never change `TemplateId`; always `Duplicate()` a material before tinting.
  `ARCHITECTURE.md` §2.5 and §2.2 describe the systems.
- **The validator got stricter twice, both times from a real bug.** `CorruptionChance` is range-
  checked (34F), and the bestiary is checked **in both directions** — every registered creature must
  have an entry (34G). That second one is the guard against the exact failure 34F.5 had to fix by
  hand: content that exists but nothing can reach.
- **Both guards were proven by making them fail**, not by trusting them.

### Still owed to Phase 34 (maintainer, at the keyboard)

Everything below needs the `F1` console or `F5`/`F9`, which no remote session can drive:

- **The bestiary's `ISaveable` round trip** — kill a few creatures, quick-save, quick-load, confirm
  the counts survive. This is a Done-when clause that has only been read, not run.
- **An Ashen spawn, seen.** `time 22`, wait out a wolf pack, confirm the nameplate reads *Ashen
  Wolf* — then kill a plain wolf and confirm it is **not** tinted (that is where a material-sharing
  bug would surface).
- **Resistance visibly landing:** `spawn 1 enemy.cinder_wisp`, hit it with `firebolt`, expect about
  half damage.
- **The necromancer mending its husks** — `spawn 2 enemy.hollow_husk`, then
  `spawn 1 enemy.hollow_necromancer`, hurt a husk. No automated coverage at all.
- Spot-check the read of `spawn 1 enemy.stone_sentinel` (150 poise: a flurry can't stagger it, one
  committed heavy hit can) and `spawn 3 enemy.wolf` (the pack fans out rather than queueing).

### Known limits, deliberately not fixed

- `EncounterResource` has **no region filter**, so a Frostfang creature can roll in the Ember Crown.
- **One encounter = one template id**, so mixed warbands (an alpha with its pack, a necromancer with
  husks) aren't authorable. The necromancer's mending is only observable when groups overlap.
- **Art:** every 34-series creature is a tinted capsule. A 2.4 m stone golem reads worst. Phase 53.

---

## Phase 34.5 — Frostfang Clans & Beast-Race Factions `[F/C]` ✅ **complete**

> LORE names Frostfang's warrior clans/beast races as a culture, not generic
> wildlife. Give them a faction identity before they dissolve into the bestiary.
>
> It landed as one: a hold you can walk into, warriors who ignore you until you
> give them a reason, and a rank chain that moves your standing both ways. The
> authoring recipes are CLAUDE.md §8; this block is the log.

- [x] **34.5A — Frostfang Clans `FactionResource` + hub presence** `[F/C]` ✅
  - **Done when:** the clan faction exists with a hub/outpost; reputation/dread
    (23G) applies to it like any faction.
  - **Landed:** `faction.frostfang_clans` (`data/factions/FrostfangClans.tres`) —
    `DefaultReputation 10`, `Enemies` the Hollow, `Allies` the beasts. Its
    `HostileThreshold` is **`1` (Hostile), not the usual `2`** — at `2` a merely
    *Touched* player (dread −5) would arrive at a hostile hub, which makes the
    whole hold unenterable for a corruption the game treats as minor. Reputation
    and dread need no wiring: `ReputationComponent` seeds every faction in the
    database, so the clans appear in the character screen the moment the `.tres`
    lands.
  - **Landed:** the clan hold —
    `scenes/regions/frostfang_reach/clan_hold.tscn`, a new `frostfang_reach.clan_hold`
    cell at `(100, 0, −20)`. Town-hub parity: navmesh + baker, three longhouses,
    three tents, four braziers (white-blue, per `ART_STYLE.md`'s Frostfang light),
    dead pines/rocks/glaciers, all three crafting stations, a waystone, and four
    NPCs tagged `faction.frostfang_clans` — Hjalvar Stormbound (chief), Sigrun
    Ironhand (quartermaster), Yrsa Houndmother (beast-tamer, seeding 34.5B) and
    Old Vetle (hearthkeeper). Each has a `Loc`-keyed conversation and a schedule.
  - **The cell carries its own floor, and has to.** `GameBootstrap.BuildEnvironment`
    builds one 80 × 80 ground plane at the world origin; Frostfang sits at x ≈ 100,
    so outside a cell's own greybox there is nothing under you but the infinite
    `WorldBoundaryShape3D`. The hold's 60 m floor is sized to cover the region
    `SpawnPoint (100, 1.2, 0)` as well, so you arrive standing on it.
  - **Two region edits with teeth:** the glacier cell moved from `(100, 0, −14)` to
    `(100, 0, −60)` (its ice props sat inside the new floor), and Frostfang finally
    has a safe zone — `SafeZoneCenter (100, 0, −20)`, radius 30 — without which the
    `EncounterDirector` spawns wolves in the middle of the hold.
  - **Schedule destinations are absolute world space**, not cell-local, so every
    entry in the four `data/schedules/Clan*.tres` is authored around x ≈ 100. This
    is the trap to remember when 34.5B/C add more clan NPCs.
  - **Verified:** `dotnet build` clean, `dotnet test` 611/611, and
    `--validate` exits 0. The cell scene was additionally load-checked headless
    (`load(…).instantiate()` → 9 children) because `ContentValidator` only proves a
    cell's `ScenePath` *exists*, never that it parses.
  - **Still owed (maintainer, at the keyboard)** — the `F1` console and `M`/`I`
    screens no remote session can drive:
    - Walk the hold: ground renders, the four NPCs stand on it, `E` opens each
      conversation, the waystone registers a fast-travel node, `M` shows a
      **Clan Hold** POI.
    - The Done-when itself: character screen reads *Frostfang Clans — Neutral*;
      `rep faction.frostfang_clans -80` turns the hold hostile; raising corruption
      subtracts the Dread line from it like any other faction.
    - Stand in the hold at night for a minute — the safe zone should keep the
      ambient spawner out.
  - **Known limits:** no clan combatants exist yet (34.5B), so killing a clansman
    means killing a peaceful NPC; the hold is still reachable only behind
    `flag.iron_king_defeated`; and `TravelNodeComponent.RegionId` is not validated
    (`ContentValidator.cs:757`), so that one field fails silently if it ever drifts.
- [x] **34.5B — Clan archetypes (raider, beast-tamer, shaman)** `[C]` ✅
  - **Done when:** three clan archetypes exist on the Phase 34 matrix with
    distinct loot/AI profiles.
  - **Landed:** `enemy.clan_raider` (`ai.shielded`, poise 75, steel sword),
    `enemy.clan_beast_tamer` (`ai.pack_flanker`, fast and thin), and
    `enemy.clan_shaman` (`ai.caster`, frost nova + lesser heal) — three distinct
    existing AI profiles, three distinct loot tables, no new profile file and no
    new weapon. All three carry `FrostResist` 35–60, which is what the Reach's
    creatures should cost a fire build, free off 34E.
  - **They are neutral, and that is the feature.** The clans sit at Neutral
    standing after 34.5A, so `EnemyAIComponent.PlayerIsTarget` returns false and a
    clan patrol *ignores you*. Hit one and it fights back; drop your standing and
    the whole faction turns. The archetypes are the first actors in the game that
    are hostile-team but not hostile.
  - **Two bugs that had to be fixed for that to be true**, both root-caused rather
    than worked around:
    - **Companions attacked neutrals.** `CompanionAIComponent` targeted on team
      alone, and `EnemyArchetypeFactory` builds *every* archetype on the hostile
      team — so Kael would have opened fire on a clansman on sight and started a
      war the player never chose. `PlayerWouldFight` now gates the proximity scan
      on the player's standing, mirroring `EnemyAIComponent.PlayerIsTarget`. It
      deliberately does **not** gate the lock-on focus or the
      `OnDamageDealt` reaction, so assisting a fight the player starts and
      defending one they didn't both still work, and an unfactioned actor is
      hostile exactly as before.
    - **Encounters had no region filter** — the known limit logged under Phase 34.
      `EncounterResource.RegionIds` (**empty = anywhere**, so all 28 existing files
      were untouched) plus one predicate in `EncounterDirector.PickEligible`, fed
      by a new `RegionStreamer.ActiveRegionId`. The streamer is re-`Configure`d at
      both places the region changes, so it needed no `GameBootstrap` edit and no
      new file. `encounter.frost_stalker` and `encounter.rime_drift` are now gated
      to Frostfang, which takes 0.75 of weight out of the Ember Crown pool 34F.5
      tuned — the valley loses two creatures that never belonged there.
  - **The validator got stricter again**, same habit: an encounter naming an
    unknown region now fails `--validate`. A typo there would otherwise narrow the
    encounter to nowhere, and the only symptom is a creature that quietly stops
    appearing. **Proven by making it fail** before it was trusted.
  - **Verified:** `dotnet build` clean, `dotnet test` 611/611, `--validate` exits 0
    (26 archetypes, 29 templates, 29 bestiary entries, 31 encounters, 488 strings).
  - **Still owed (maintainer, at the keyboard)** — all of it needs `F1`:
    - `region goto region.frostfang_reach`, `spawn 1 enemy.clan_raider` — it should
      ignore you until you swing, then fight.
    - **With Kael recruited, spawn a clansman beside him — he must not open fire.**
      This is the fix most likely to be wrong.
    - `spawn 1 enemy.clan_shaman` — casting proves the mana pool landed (a caster
      with no mana just stands there, silently); mending a hurt clansman proves the
      ally-heal path.
    - `rep faction.frostfang_clans -80` → all three turn hostile on sight.
    - Stand in the Ember Crown a few minutes: no clan patrol, no frost stalker, no
      rime shard. Then the same in Frostfang outside the hold's 30 m safe zone.
  - **Known limits:** **one encounter = one template id** still holds, so a
    beast-tamer cannot spawn *with* her stalkers — `encounter.clan_hunt` and the
    now-Frostfang-only `encounter.frost_stalker` overlap by chance instead, the
    same compromise 34G recorded for the necromancer and its husks. And the region
    gate is a whitelist on encounters only; world events are still global.
- [x] **34.5C — Clan questline + rank chain** `[C]` ✅
  - **Done when:** a short multi-quest arc with rank-up flags is completable;
    `validate-all` green.
  - **Landed — the rank chain:** three links on `PrerequisiteQuestId`, one rank
    each. `quest.clan.proving` (Hjalvar; break 4 `enemy.rime_shard`, the one
    creature that is both Frostfang-only and hostile) → **`flag.clan.named`** ·
    `quest.clan.stores` (Sigrun; 5 beast pelts) → **`flag.clan.sworn`** ·
    `quest.clan.hollow` (Hjalvar; 5 `enemy.hollow_husk`, the faction's declared
    `Enemies`) → **`flag.clan.hearth_kin`**. Nothing in the arc asks you to kill a
    frost stalker or a clansman: the tamer's own 34.5A line makes stalkers
    clan-raised, and `faction.beasts` is a clan ally.
  - **The fiction was already written and unfired.** Hjalvar: *"a name is what you
    carry, not what you are given."* Sigrun: *"come back when the hold knows your
    name."* Her line is now a literal `HasFlag flag.clan.named` gate — she refuses
    to trade until the hold has named you, which is what she always said.
  - **Landed — the betrayal branch:** `quest.clan.exile.proof` (kill 3
    `enemy.clan_raider`) → `flag.clan.oathbreaker`, then `quest.clan.exile.rite`
    (kill 2 `enemy.clan_shaman`) → `flag.clan.bloodfeud`. Given by a new NPC,
    **Halvar One-Hand**, an exile camped at his own fire in the hold's far corner —
    a rival faction would have needed an NPC from nowhere; an exile explains
    himself. He has no `FactionComponent`: he is nobody's.
  - **The branch pays in Syndicate standing, not negative clan standing.** Killing
    clansmen already costs 12 a head automatically, so the two contracts cost ~36
    and ~24 clan reputation on their own; adding a negative quest reward would have
    been charging twice for one act. It also closes the branch behind you — enough
    kills and the hold turns hostile and stops talking, exactly as 34.5B designed.
  - **Mutual exclusivity is two `MissingFlag` gates**, no new machinery: the chief's
    work hub needs `MissingFlag flag.clan.oathbreaker`, the exile's needs
    `MissingFlag flag.clan.hearth_kin`. A `DialogueChoice` has **one** `Effect`, so
    it cannot both start a quest and set a flag — the flag rides on the following
    node's farewell choice, the `Elder.tres` shape.
  - **Quests can now move reputation.** `QuestResource` gained
    `FactionRewardId`/`FactionRewardAmount`, mirroring `WorldEventResource` field
    for field, applied in `GrantRewards` **before** the no-inventory bail (standing
    is owed whether or not you can carry anything). That is what makes rank visible
    with **no UI work at all** — the character screen already lists the clans, so
    the arc walks the tier Neutral → Friendly → Honored. Phase 42A still owns the
    real rank framework and display; this is the field it will build on.
  - **The validator got stricter again, and this one was overdue.** Story flags are
    the only id family with no database behind them, so nothing had ever checked
    them: a mistyped `HasFlag` is a gate that never opens, silently and for good.
    `ValidateStoryFlags` now cross-references readers against writers — dialogue
    `HasFlag`/`MissingFlag` args and `RegionResource.UnlockFlagId` against every
    `SetFlag`/`ClearFlag` effect plus the three code constants. The reverse is
    *not* an error: a flag set and never read is a legitimate record of what
    happened. **Proven by making it fail** on a doctored `flag.clan.namd`.
  - **Verified:** `dotnet build` clean, `dotnet test` **619/619** (the per-file
    dialogue suite picked up the new conversation), `--validate` exits 0 with the
    full graph battery — 13 quests, 12 conversations, 585 strings. The edited cell
    scene was load-checked headless again (11 children, exile and his fire present).
  - **Still owed (maintainer, at the keyboard)** — needs `F1` and the `I`/`J` screens:
    - Walk the loyal arc: accept the proving from Hjalvar, `spawn 4 enemy.rime_shard`,
      kill them, turn in. The journal tracks it and the character screen's
      **Frostfang Clans** line climbs.
    - Sigrun must **refuse** before `flag.clan.named` and offer the stores after it.
      That gate is the whole point of the rank chain.
    - Finish link 3: the chief greets you as hearth-kin, Yrsa and Old Vetle have new
      lines, and **the exile's offer is gone**.
    - On a separate save, take Halvar's contract instead: Syndicate standing rises,
      clan standing falls ~36, and the chief's work hub disappears.
    - `F5`/`F9` across a rank-up — flags and quest progress are separate `ISaveable`s
      and both must survive.
  - **Known limits:** objectives are still only Kill/Collect, so "go and speak to
    someone" cannot be an objective — every turn-in is a conversation the player has
    to remember to have. Rank is invisible outside dialogue and the reputation tier
    it grants (42A owns a real rank display). And a quest completed once can never
    be re-taken, so the arc is one-way per save.

### Phase 34.5 — what outlived the session

- **The clans are the first faction the game treats as a people rather than a spawn
  table**: a hold you can walk into, warriors who ignore you until you give them a
  reason, and an arc that moves your standing in both directions.
- **Three durable rules moved into the permanent docs**, which are the ones to trust:
  CLAUDE.md §8 now records that an encounter without `RegionIds` rolls in every
  region, and that a quest can pay in faction standing.
- **The validator gained three checks in three sub-phases**, each closing a failure
  mode with no symptom: an encounter narrowed to a region that does not exist, a
  quest paying an unknown faction, and a flag nothing ever sets. All three were
  proven by making them fail.
- **Two neutral-actor bugs were fixed at the root**, not at the call site: companions
  no longer open fire on factions the player is at peace with, and ambient encounters
  no longer leak across realms.

---

## Phase 35 — Dragons `[F/C]`

- [x] **35A — Dragon body: multi-hit-zone scalable boss actor** `[F]` ✅
  - **Done when:** a large multi-hurtbox dragon actor exists with tail/wing melee.
  - **Landed as data, not a dragon factory.** `HitZoneResource` + `HitZones`/`IsBoss`/
    `DirectionalMelee` on `EnemyArchetypeResource`, built by the one
    `EnemyArchetypeFactory`. 35D/35E/35F are now `.tres` and nothing else, and Phase 36
    inherits zones for free. `enemy.wild_dragon` is the first body that is not one volume:
    head ×2.0, wings ×1.4, body ×1.0, tail ×0.6.
  - **The bug this phase existed to fix.** `Hitbox._alreadyHit` and
    `SpellResolver.Detonate`'s `struck` both deduped **per hurtbox**. That was invisible
    while every actor had exactly one — with four, a single sword arc or fireball clipping
    three zones billed three full `DamagePacket`s. Both now route through
    `Combat/HitDedupe.cs`, keyed on the **owning entity** (the hurtbox itself is the
    fallback key, so `GameBootstrap`'s owner-less training dummy is unchanged).
  - **Zones replace the capsule, they don't overlap it.** Two hurtboxes over the same
    flesh would not double-damage any more, but whichever the physics query returned first
    would silently decide the multiplier.
  - **`AIProfileResource.TurnSpeedDegrees` is what makes the arcs real.** `FaceTowards`
    used `LookAt` — an instant snap — so a dragon would always be looking at you and only
    ever bite. The profile now slews at a turn rate (`0` = snap = every pre-35A archetype,
    byte-identical). `ai.dragon` turns at 55°/s, which is the dial to tune if flanking
    feels too easy or impossible.
  - **The greybox is generated from the zones**, one blob per hurtbox, weak points
    lightened. It cannot drift out of alignment with what is damageable — the trap a
    hand-placed placeholder sets. The `.glb` is a later art pass, as the Iron King got in
    30D.
  - **Verified:** `dotnet build` clean, `dotnet test` **628/628** (17 new across
    `HitDedupeTests` + `DragonMeleeTests`), `--validate` exits 0 with 30 templates / 30
    bestiary entries. The archetype was additionally load-checked headless (all four zones
    parse with their authored multipliers — a typed `Array[Resource]` is the kind of thing
    that fails to empty in silence), and `--play` boots into a live world with combat
    resolving and no errors.
  - **Still owed (maintainer, at the keyboard)** — the `F1` console:
    - `spawn 1 enemy.wild_dragon`, then hit the head and the tail: the numbers should
      differ by the authored multipliers, and **one swing must produce one number, not
      four**. Cast an AoE into it — same check, one tick.
    - Walk round it: the tail should answer from behind, the wing from a flank, and 55°/s
      should feel like a real turn rate rather than a stuck dragon.
    - The boss healthbar should appear (the `IsBoss` → `BossEntity` path).
  - **Known limits:** the dragon is spawn-only — nothing places it in the world until 35G,
    and it has no encounter entry. It walks; flight is 35B. It reuses `BeastLoot` and has
    no bespoke drops. Its greybox is a `Node3D`, not a `MeshInstance3D`, so
    `EnemyAIComponent.SetShadow`'s distance LOD silently no-ops on it (`_mesh` is null) —
    it costs a shadow at range until the model lands.
- [x] **35B — Aerial AI: flight pathing, takeoff/landing** `[F]` ✅
  - **Done when:** the dragon flies, lands, and takes off under AI control.
  - **Flight is the vertical axis and nothing else.** `LocomotionComponent.Flying`
    swaps gravity for a servo toward `TargetAltitude` at `ClimbSpeed`; horizontal
    movement is untouched, so `EnemyAIComponent` steers a flier with exactly the code
    that steers a walker. There is no second pathing system and no aerial branch in the
    FSM — that split is why the whole sub-phase is four narrow guards and one component.
  - **Tuning lives on the AI profile**, as `TurnSpeedDegrees` did in 35A:
    `TakeoffRange`/`HoverAltitude`/`ClimbSpeed`/`AirborneDuration`/`GroundedDuration`.
    `TakeoffRange = 0` is every other profile in the game and costs them one comparison.
    `ai.dragon` is 16 m / 12 m / 6 m·s⁻¹ / 4.5 s / 8 s. **No new archetype and no new
    enemy** — `enemy.wild_dragon` simply gained flight.
  - **The cycle is time-boxed on purpose.** `FlightDecision` (pure, unit-tested) runs
    `Grounded → TakingOff → Airborne → Landing → Grounded`; it takes off when the target
    is past `TakeoffRange` *or* after `GroundedDuration` of melee, and always lands. A
    dragon allowed to choose would stay up, and with no breath until 35C that is a fight
    where neither side can act. **That hover window is where 35C's breath goes.**
  - **Landing needs no raycast.** Descend with `Flying` still on, target an altitude below
    the floor, and `MoveAndSlide` stops the body — `IsGrounded` ends the phase. Uneven
    ground and landing higher than you took off are free.
  - **Four guards in the AI, all narrow.** Range is measured horizontally, so a dragon
    hovering overhead read as "in reach" and would swing at empty air — the swing is now
    gated on not being airborne. The navmesh is bypassed while flying (its corners route
    around obstacles it is flying over). Leaving combat, including dying, grounds it, so a
    corpse falls instead of hanging in the sky. Melee resumes during `Landing` — the
    descent is the swoop's payoff, not a helpless phase.
  - **Verified:** `dotnet build` clean, `dotnet test` **640/640** (12 new in
    `FlightDecisionTests`, including a full-cycle walk proving no phase is a dead end),
    `--validate` exits 0. `ai.dragon`'s five flight fields were load-checked headless
    (and `ai.boss` confirmed still at `TakeoffRange 0`), and `--play` boots into a live
    world with the walking roster fighting normally — the `Flying == false` path is every
    other enemy in the game.
  - **Still owed (maintainer, at the keyboard)** — the `F1` console:
    - `spawn 1 enemy.wild_dragon` and back away past 16 m: it should climb, close on you
      from the air, and land — not hover indefinitely, not fall out of the sky.
    - It must not swing while airborne, and must resume melee the moment it is down.
    - Kill it mid-flight: the corpse should fall.
    - Watch a full cycle for pacing. `AirborneDuration` and `GroundedDuration` are the
      dials, and 12 m may read as too high once there is a model to see.
  - **Known limits:** no flight animation — the greybox has no `AnimationPlayer`, so the
    clips land with the `.glb`. Nothing persists: a spawned dragon is transient and
    nothing on the enemy AI path is `ISaveable`, so a save mid-flight is not a case that
    exists yet. The climb is a constant-velocity servo, not accelerated — fine for a
    greybox, worth easing when the wings are real.
- [x] **35C — Breath attacks (cones/AoE) via SpellResolver** `[F]` ✅
  - **Done when:** breath attacks reuse `SpellResolver`/status for cone/AoE damage.
  - **Breath is a spell, not an attack.** `spell.dragon_breath` is `Delivery = Cone`,
    `CastMode = Channeled`, Fire school, 55° × 14 m, applying `status.burning` — so it goes
    through `SpellResolver`, school resistances, `SchoolIdentity` and the status pipeline
    exactly as any player spell does. The roadmap asked for this specifically, and it is why
    35E/35F's Ash and Ancient breaths cost a `.tres` each.
  - **`SpellDelivery.Cone` + `SpellResolver.Sweep`.** A cone is `Detonate`'s sphere query
    narrowed by one predicate, so both shapes share a single private `Resolve` rather than
    becoming two resolvers that must be kept in step. The geometry is the pure `SpellCone`;
    `ConeAngleDegrees` is the **full** width, which the tests pin — reading it as a half-angle
    would silently double every cone ever authored.
  - **Hurtbox position is the shape child's, not the Area's.** An `Area3D`'s origin is the
    actor's origin, so testing it would place a 35A dragon's head, wings and tail at the same
    point and let a cone take all four or none. `VolumeCentre` reads the `CollisionShape3D`,
    falling back to the Area for the ordinary single-shape hurtbox.
  - **The blocker this had to clear.** `TickCombat` branched to standoff/kiting on
    `_casting != null || _profile.IsStandoff` — so giving the dragon spells would have stopped
    it biting and turned 35A's melee arcs into dead code. The first half was **already
    redundant**: every spell-carrying actor with an `EnemyAIComponent` (the seven 34D/34E
    archetypes *and* the bespoke Ashen Acolyte) uses `ai.caster`, whose standoff range already
    sets `IsStandoff`; companions use a different AI entirely. Dropping it states the real rule
    — **a caster is a profile that stands off, not an actor that holds spells.**
  - **Aiming from 12 m up.** `Aim()` reads the `CastOrigin` node's forward and the AI keeps the
    body level, so a hovering dragon would have breathed straight over your head.
    `BreathComponent` points that node at the target before casting; every delivery shape
    inherits the pitch without knowing why.
  - **`BeginCastById` is the one thing enemies lacked** — `TryCastById` is instant-only, and a
    channel needs `BeginCast` → `UpdateCast` → `EndCast`. It mirrors the existing method rather
    than adding a parallel casting path.
  - **Grounded it must turn to breathe; airborne it need not.** On the ground the breath is gated
    on facing, so 35A's flanking denies it and the 55°/s turn rate is a real beat. In the air the
    dragon is overhead with its aim pitched down, where a facing gate on a level body would only
    make the hover window fire at random. `BreathWindow` is pure and pins the asymmetry.
  - **Verified:** `dotnet build` clean, `dotnet test` **655/655** (15 new across `SpellConeTests`
    and `BreathWindowTests`, plus the updated `SpellDelivery_Ordinals` — the test that exists to
    catch exactly this kind of enum edit), `--validate` exits 0 with the new cone and breath
    rules. `DragonBreath.tres` and `WildDragon.tres` were load-checked headless (delivery
    ordinal 3, cast mode 2, the loadout carrying the breath id), and `--play` boots into a live
    world with combat resolving and no script errors.
  - **Still owed (maintainer, at the keyboard)** — the `F1` console:
    - `spawn 1 enemy.wild_dragon`: stand in front and burn, stand behind and don't. Confirm the
      burning status applies and that resistances read as Fire.
    - Let it take off — it should breathe **down** at you from the hover, not overhead.
    - Confirm it still bites, wing-sweeps and tail-swipes between breaths. That is what the
      standoff-clause fix buys.
    - `spawn 1 enemy.hollow_necromancer` and confirm it still kites and casts exactly as before —
      that clause is the one edit here touching shipped behaviour.
  - **Known limits:** the cone greyboxes as four widening `SpellFlash` spheres along its axis —
    legible, but a real particle cone is an art pass. There is no wind-up telegraph: the breath
    starts the frame it is decided, which is a Phase 36 concern (`BossController` owns
    telegraphs) and will matter more once there is an animation to read. Mana is the only limiter
    besides the 6 s cooldown, and the dragon's 120 mana at 18/s means it cannot chain breaths
    indefinitely — worth re-checking once 35D/35E tune the variants.
- [x] **35D — Wild dragon variant (territorial world boss)** `[F/C]` ✅
  - **Done when:** a Wild dragon spawns as a territorial world boss.
  - **It has somewhere to be.** `scenes/regions/frostfang_reach/dragon_roost.tscn` — a third
    Frostfang cell at `(25, 0, −20)`, 90 m of open ground ringed with crags to break the breath
    cone against, glaciers and dead pines. Before this the dragon existed only as a dev-console
    `spawn`.
  - **"Territorial" was the missing mechanic, not a tuning value.** The AI had **no leash**:
    `_home` was read only by patrol and retreat, and `TickCombat` chased until line of sight
    broke — a flying dragon would have followed the player out of Frostfang entirely.
    `AIProfileResource.TerritoryRadius` (`0` = every other profile, unchanged) plus the pure
    `TerritoryLeash` and a new `EnemyState.Returning`. `ai.dragon` owns 45 m.
  - **Returning ignores the player the whole way home**, deliberately. An "unless it can see you"
    clause — which is what `Investigate` does, and why that state could not be reused — would let
    the player defeat the leash by standing in the doorway. Coming home clears `_provoked` and
    resets `_lastKnownPos`, or it would re-engage the instant it arrived.
  - **The hysteresis matters.** Re-engaging needs it back within `ReturnFraction` (0.75) of the
    radius. A single threshold makes a creature sitting on the boundary flicker between chasing
    and leaving every frame.
  - **The state came free from 35B/35C.** `EnterState` already grounds a flier and stops a breath
    on any non-Combat state, so a dragon that disengages mid-air lands and stops breathing with no
    new code. `EnemyState` is documented as not persisted and deliberately unpinned, so appending
    to it is safe.
  - **Persist the spawner, never the boss.** `CellPersistenceDirector` reconciles on
    `RegionCellLoadedEvent`, which `RegionStreamer` publishes *after* `AddChild(root)`
    (`RegionStreamer.cs:174,178`) — a dragon spawned that frame races the walk and a deferred one
    misses it outright, so a killed boss would return every time the valley reloaded.
    `LairSpawnComponent` is authored in the `.tscn` with a stable `PersistentId`, is `ISaveable`,
    and holds one bool. Both restore paths were traced: `SaveManager.Register` restores
    synchronously from an in-flight load *before* the deferred spawn, and
    `CellPersistenceDirector.Save` snapshots live cells so a save taken standing in the roost is
    complete.
  - **Placed west, not north.** North was the obvious spot and the wrong one — the glacier cell
    sits at `z = −60` and its props would have ended up inside the roost's floor, the same mistake
    34.5A had to undo. West butts the roost's floor against the hold's at `x = 70`: walkable the
    whole way, no overlap, no co-planar z-fighting, and the 45 m territory ends right at the hold's
    edge so the dragon will not follow you into it.
  - **It drops like a boss now** — `DragonLoot` replaces the `BeastLoot` placeholder 35A flagged as
    owed: 3–6 `item.material.dragon_scale` (a new Rare material), rubies, an affixed ring, and
    150–320 gold.
  - **Verified:** `dotnet build` clean, `dotnet test` **662/662** (7 new in `TerritoryLeashTests`,
    including that radius 0 never leashes — the property keeping every existing archetype
    unchanged), `--validate` exits 0. The cell scene was load-checked headless (it parses, the nest
    carries its `PersistentId`, the region reports three cells).
    **And the whole thing was seen working in `--play`:** the roost streamed in, the dragon spawned,
    fought, took damage at **8 / 16 / 27** per hit — the 35A zone multipliers live in a real fight —
    died, and dropped 3 items.
  - **Still owed (maintainer, at the keyboard):**
    - **Run away.** Past 45 m it must break off, walk home and drop aggro rather than following you
      to the clan hold. This is the phase's headline and the one thing no remote session can drive.
    - **Kill it, leave, come back**, then `F5`/`F9` a save round-trip: it must stay dead both times.
      The code paths are traced above but the round-trip itself is unrun.
    - Walk the roost for ground/props, and confirm the drops read as scales and gold rather than
      beast pelts.
  - **Known limits:** no map POI for the roost — you find it by walking west from the hold, with
    nothing on `M` to suggest it. No respawn, by choice: a world boss that returns is a balance
    call (Phase 56), not a 35D one. Frostfang is still gated behind `flag.iron_king_defeated`, so
    the roost is unreachable until the Iron King falls.
- [x] **35E — Ash dragon variant (corrupted elite)** `[F/C]` ✅
  - **Done when:** an Ash dragon exists as a corrupted elite enemy.
  - **The payoff phase.** `enemy.ash_dragon` is a second dragon built entirely from 35A–35D's
    pipeline: attributes, a breath spell, an AI profile, an archetype, loot, a bestiary entry, a
    lair scene and a region cell. **No new systems** — every field it uses already existed.
  - **Its own creature, not a corrupted Wild one.** 34F's rule is that a corrupted creature is the
    base archetype plus `AshenAffliction`, and that rule is right — for *the same creature*
    corrupted. `Afflict` deliberately never changes `TemplateId`, so an afflicted Wild dragon could
    never have its own bestiary page or lore. `LORE.md` gives Ash Dragons their own section
    alongside Wild and Ancient: they are a kind of dragon, not a tinted one.
  - **LORE says "among the most dangerous enemies in the game", so the numbers say it too** — 1900 HP
    to the Wild dragon's 1400, more power, 50 m of territory, and the **zone multipliers are
    deliberately flatter** (head ×1.6 not ×2.0, tail ×0.85 not ×0.6). A corrupted thing has no good
    side to be on, which makes it the harder fight before any stat is compared.
  - **Necrotic breath, not Fire.** `spell.ash_breath` is a wider (80°), shorter (11 m) cone applying
    `status.decay`. Fire resistance buys the player nothing, so the second dragon has to be prepared
    for differently rather than fought the same way.
  - **Placed east, mirroring the Wild roost west.** The hold sits between them: wild roost floor
    `x ∈ [−20, 70]`, hold `[70, 130]`, ash roost `[130, 230]` — three floors butted edge to edge,
    walkable across, none overlapping. Its territory is sized to its own floor exactly so a chase
    can never spill into the hold's safe zone.

  - **🐛 A 35D bug this phase exposed and fixed.** The maintainer saw the dragon spawn "way off its
    den and well into the void". `LairSpawnComponent` passed a **world** position to
    `EnemyTemplateRegistry.Create`, which sets a **local** one, and then parented the actor under the
    cell root — which the streamer had already moved to the cell centre. The offset applied twice:
    the wild roost's dragon landed at `(50, −40)` instead of `(25, −20)`, which its 90 m floor
    happened to cover, so **the bug shipped in 35D looking fine**. The ash roost at `x = 180` threw
    its dragon to `x = 360`, past the region bounds. Fixed by create-at-zero → add → set
    `GlobalPosition`, which is the order `BossSummonComponent` already used — the lair spawner was
    the deviation. `EnemySpawnDirector` had the same latent defect (harmless only because it sits at
    the world origin) and was aligned to the same order.
  - **Verified:** `dotnet build` clean, `dotnet test` **662/662** (no new tests — this phase adds no
    logic, and YAGNI applies to tests too), `--validate` exits 0 with 31 bestiary entries. The ash
    roost was load-checked headless — it parses, its `PersistentId` is distinct from the wild
    roost's (they share a `SaveId` prefix, so a collision would make killing one mark both), the
    breath reads school 6 / delivery 3 / mode 2, and the region reports four cells. **And it was seen
    fighting in `--play`:** the roost streamed in, the dragon spawned *in its den* after the fix, and
    took 24 / 28 / 53 / 96 per hit off 1900 HP — the flatter zone spread, live.
  - **Still owed (maintainer, at the keyboard):**
    - Fight it: the breath must apply **decay**, and fire resistance must not help.
    - Kill it, then confirm the **Wild** dragon in the west roost is still alive — the two lairs must
      persist independently.
    - Stay-dead across a cell reload and an `F5`/`F9` round-trip, for both dragons.
  - **Known limits:** a save taken while a roost is loaded logs
    `entry 'lair:…' had no live claimant on load (orphaned state)` if the cell is not streamed in at
    load time. Harmless — `CellPersistenceDirector` does the real restore when the cell arrives — and
    it is inherent to every cell-authored `ISaveable` (`ContainerLootComponent` registers the same
    way); worth a look if the orphan diagnostic is ever tightened. **Two hand-authored roost cells is
    fine; a third should promote the roost into a reusable scene rather than a third copy.** No map
    POI for either lair.
- [x] **35F — Ancient dragon: dialogue-capable quest/lore giver** `[F/C]` ✅
  - **Done when:** an Ancient dragon can hold a conversation (`DialogueComponent`)
    and give quests/lore.
  - **The first actor that is a boss and a conversation at once.** `enemy.ancient_dragon`
    (Vharyx the Unspoken) sits in a 90 m aerie north of the Wild dragon's roost, holds
    `dialogue.ancient_dragon`, gives `quest.ancient.kin`, and fights like the other two if you
    make it. Everything 35A–35E built is reused unchanged: hit zones, directional melee,
    flight, a cone breath, a territory leash, a persisted lair spawner.
  - **Four small code seams, all of them general:**
    - `EnemyArchetypeResource.DialogueId` → `EnemyArchetypeFactory` attaches a
      `DialogueComponent`. Nothing else was needed to make it reachable — the interact
      raycast is unmasked and resolves the owner from whatever collider it hits, so the body
      the creature already has is the target.
    - `DialogueEffect.LearnSpell` (**ordinal 8**) → `SpellcastingComponent.Learn`. This is the
      conversational half of 29.5E's recovery seam, where `SpellTomeComponent` was the
      found-object half, and it closes the roadmap's "earning one's favor teaches a recovered
      spell". `Learn` ignores `PlayerLearnable`, which is exactly why it works: the spell can
      be given but never bought.
    - `LairSpawnComponent.DefeatFlagId` → sets a story flag on the kill (and re-applies it on
      load). **Nothing in the game turned a kill into a flag before**, so "you have slain the
      boss" was not askable by a dialogue condition or a gated interactable. Every world boss
      gets it, not just this one.
    - `SpellTomeComponent.RequiredFlagId` → a tome that will not open until a flag is held.
      Together with the above, that is the defeat route: the hoard sits in the aerie from the
      start and yields the same word once its keeper is dead.
  - **🐛 A live UI defect this phase surfaced.** `InventoryPanel.BuildSpells` filtered the
    character screen on `PlayerLearnable`, so a spell the player had *actually learned* but
    could never buy rendered nowhere. The 35F reward would have been invisible in the one
    screen that lists your spells. Fixed at the filter (`|| _spellcasting.IsKnown(s)`); it was
    a latent bug for any recovered enemy-grade spell, not just this one.
  - **Neutral until provoked cost nothing.** `faction.dragons` — its own faction, deliberately
    not the Wild dragon's `faction.beasts` or the Ash dragon's `faction.fallen`, so clearing
    the wilds of wyrms does not make the one you can talk to draw breath on you. Default
    standing Neutral, `HostileThreshold` at Unfriendly: `EnemyAIComponent.PlayerIsTarget`
    already returns false above the threshold and `OnDamaged` already sets `_provoked`. **No
    AI code was written for this phase.**
  - **The roost debt is paid.** 35D and 35E both ended with "a third roost should promote the
    roost into a reusable scene rather than become a third copy", so it was promoted *before*
    the third one landed. `scenes/regions/roost.tscn` + `RoostCell.cs` own the nav region,
    baker, floor mesh/collider and the `Nest`/`Lair` markers; all three roosts are inherited
    scenes carrying only their floor knobs, their identity, their occupant and their props.
    The floor's mesh, shape and material are base-scene sub-resources shared by every roost,
    so `RoostCell` `Duplicate()`s each before touching it — otherwise sizing the third would
    have resized the other two. The Wild roost's floor roughness moved 0.9 → 0.95 in the
    merge; nothing else about either existing roost changed.
  - **One spell, not two.** `spell.elder_word` is the Ancient's breath *and* the thing it
    teaches — Arcane, so neither the Fire resistance the Wild dragon teaches you to carry nor
    the Necrotic one the Ash dragon does buys anything. Making the reward literally the weapon
    that was used on you is one `.tres` instead of two, and it reads better than either.
  - **The quest ties the three dragons together.** `quest.ancient.kin` is a Kill objective on
    `enemy.ash_dragon` — 35E's boss — so the favour route is earned by real work in the same
    region rather than by exhausting a dialogue tree, and Frostfang's three lairs are one story
    instead of three fights. There is no "return and tell it" objective (the quest system is
    Kill/Collect only), so the turn-in is a conversation the player has to remember to have.
  - **Verified:** `dotnet build` clean, `dotnet test` **670/670**, `--validate` exits 0 (29
    archetypes, 32 bestiary entries, 9 factions, 14 quests, 13 conversations, 18 spells, 620
    strings). All four roost scenes were **instantiated headless** — the base plus all three
    derived cells build with their own floor size (90 / 90 / 100 / 90), their own prop counts,
    distinct `PersistentId`s, the right occupant each, and the hoard + defeat flag only on the
    aerie. A headless `--play` into the Frostfang save booted clean and **streamed the
    re-expressed wild roost cell** (`RegionStreamer: loaded cell 'frostfang_reach.dragon_roost'`)
    with zero errors and no nav-bake warning — that is the 35F regression proof for Part 2.
  - **⚠️ Rebuild before you believe a scene check.** The first headless run reported "Cannot
    instantiate C# script … RoostCell.cs" on all four scenes. Not a scene bug — `Embervale.dll`
    predated the new file. This is the §2 stale-binary trap wearing a different costume: it
    looked exactly like a broken inherited scene.
  - **Still owed (maintainer, at the keyboard):** the aerie is ~117 m from where the save sits,
    so `--play` proved boot and save restore, **not** that the Ancient spawns or speaks.
    - Walk to the aerie: the cell streams, the dragon is *in* it, and `E` reads as talking, not
      fighting.
    - Take the quest, kill the Ash dragon, return: the favour branch appears, the Elder Word is
      taught, and it **shows and casts** from the character screen (the fix above).
    - Hit it: it turns hostile, and breaks off at its territory edge rather than following you.
    - Kill it instead: the hoard's tome opens (it must refuse before the kill), the other two
      dragons are unaffected, and all three lairs stay dead across a cell reload and `F5`/`F9`.
  - **Known limits:** the 35E orphaned-`ISaveable` warning on load is unchanged and still
    harmless. No map POI for any of the three lairs. No new unit tests — everything added is
    Godot-node-bound (a session, two components, a factory branch) and the pure-logic suite
    takes no nodes; the one thing that could silently corrupt saves, the new enum ordinal, is
    pinned in `EnumStabilityTests` (which now also pins the three companion effects it had
    been missing since 32C).
- [x] **35G — Dragon encounters in Frostfang + high-end world events** `[C]` ✅
  - **Done when:** dragon encounters seed Frostfang Reach and the world-event
    tables.
  - **The Reach became dragon country.** Every dragon before this was a fixed lair boss you
    travelled to; nothing dragon-shaped happened on its own. `enemy.frost_drake` now wanders
    Frostfang as an ambient encounter, an **Elder Drake** Hunt and a **Spilled Hoard** Cache give
    the event table its first late-game tier, and the scales all four dragonkin drop forge into
    **drakescale mail**.
  - **A lesser dragon, not the named three.** Pointing an encounter at `enemy.ash_dragon` would
    have put two of a one-of-a-kind creature in the world and made `quest.ancient.kin` farmable
    from a random roll. The drake is deliberately **not boss furniture** — no `IsBoss`, no hit
    zones, no directional melee. Those exist so a fight has geography, and geography is for a
    creature you travel to. Declining the zones also declined a whole new AI profile: zones
    without a turn rate leave 35A's flank arcs dead, so it reuses `ai.brute` unchanged.
  - **The champion tier is a multiplier, not a second archetype** — `MinCount = 1` +
    `HealthMultiplier`, the trick `event.goblin_champion` has used since Phase 17.
  - **🐛 A live 34.5B gap this phase closed.** `WorldEventResource` had **no `RegionIds`**. 34.5B
    gave encounters a region gate after frost stalkers prowled the Ember Crown for two phases, and
    the *other* director never got one — so goblin raids had been rolling in Frostfang Reach ever
    since the region existed, and a drake hunt would have rolled in the starting valley. Added the
    field, the `AllowedIn(regionId)` gate, the `RegionStreamer.ActiveRegionId` lookup
    `EncounterDirector` already did, and the validator's unknown-region check. **The three Phase 17
    goblin events were gated to `region.ember_crown` in the same pass** — fixing only the new
    entries would have left the live bug in place.
  - **Gear can carry a resistance now.** `EquippableItemResource` exposed seven `Bonus*` fields and
    not one of 34E's `*Resist` stats, so resistance was authorable on an `AttributeSet` only:
    enemies could shrug off a school and the player could not. `BonusFrostResist` is one export and
    one `yield return` — it is a `StatType`, so equipment, tooltips and the character screen pick
    it up untouched. Only Frost, because only this item needs it; the other five are two lines each.
  - **⚠️ Nothing in the game teaches a recipe.** `CraftingComponent.Learn` exists and **has no
    caller** — no tome, no dialogue effect, no quest reward — and unlike the bestiary, `--validate`
    has no reachability check for recipes. `recipe.leather_vest` has therefore been unreachable
    since Phase 15. `recipe.drakescale_mail` is seeded in `PlayerFactory` like the other six and
    gated on its **ingredient** instead: eight dragon scales, which only Frostfang's dragonkin drop.
    A `LearnRecipe` dialogue effect would be cheap now that 35F put `LearnSpell` next door.
  - **Verified:** `dotnet build` clean, `dotnet test` **670/670**, `--validate` exits 0 (30
    archetypes, 33 bestiary entries, 19 spells, 32 encounters, **5** world events, 8 recipes, 622
    strings). Checked by hand that no encounter or world event references `enemy.wild_dragon`,
    `enemy.ash_dragon` or `enemy.ancient_dragon` — those three are lair-only by design and nothing
    enforces it. **And the whole loop ran in a headless `--play`:** the Elder Drake hunt started in
    Frostfang, the drake built and fought and died, the event completed and paid out, and a Spilled
    Hoard followed — with no goblin event firing in the region, which is the gate doing its job.
  - **Tuned off that run.** The champion went in at `HealthMultiplier = 3.0` (the goblin's) and the
    log showed a **1260 HP** drake — within sight of the Wild dragon's 1400, on an event with a
    180 s hard timer. Dropped to 2.0. A boss you must beat on a stopwatch is a different thing from
    a hunt.
  - **Still owed (maintainer, at the keyboard):** the ambient `encounter.drake_flight` is the one
    piece the log did not catch — it is the lowest weight in the region's pool by design (0.25
    against clan patrols, rites, hunts, stalkers and rime drifts), so it needs a play session rather
    than an idle boot. Fight a drake and confirm the **breath actually fires** (34D's silent failure
    is a caster with no mana just standing there), then confirm the mail drops from the hunt, equips,
    and shows its frost resistance on the character screen.
  - **Known limits:** the 35E orphaned-`ISaveable` warning on load is unchanged. The drake has no
    model (capsule + `PlaceholderTint`) like most of the roster. No new unit tests — this phase is
    content plus two field-and-a-line seams, and the pure-logic suite takes no Godot nodes.

**Phase 35 (Dragons) is complete — 35A–35G.**

---

## Phase 36 — Boss Framework & Encounter Design `[F]`

- [x] **36A — `BossResource` schema (phases, abilities, enrage)** `[F]` ✅
  - **Done when:** a boss is describable as data (HP-threshold phases, per-phase
    ability sets, enrage timer).
  - **Scope call (maintainer, 2026-08-04):** the schema *runs* in this pass rather than landing as
    an unread definition — a resource with no consumer is exactly the theoretical scaffolding §1
    forbids, and wiring it is what turns the dragons into actual boss fights. 36B therefore shrinks
    to moving the Iron King off `BossFactory`.
  - **Done:** `BossResource` + `BossPhaseResource` (sub-resource array, the `HitZoneResource`
    pattern) + `BossDatabase` (mirrors `EnemyArchetypeDatabase`, initialized *before* it so the
    validator can cross-check) + the pure `BossPhases` (`SelectPhase`/`ShouldEnrage`, 13 tests).
    `EnemyArchetypeResource.BossId` names one; `EnemyArchetypeFactory` attaches a `BossController`
    to any `IsBoss` archetype — **which is the line that gave the three dragons a fight at all**:
    they were `BossEntity` healthbars with no phases and no escalation, because only the Iron King's
    bespoke factory ever attached a controller. (Correction made while doing 36B: they still have no
    telegraph *flare*. `ClaimEmissiveSurface` needs an emission-enabled material and only an authored
    model supplies one — the hit-zone greybox is albedo-only. Phases, escalation and enrage do run.
    A model-independent wind-up presentation is 36C's.)
    `BossController` is now data-driven end to end: phases (entered at or below a threshold, never
    left, deepest-crossed on a big hit), per-phase stat escalation under a `boss.phase{n}` source
    (remove-then-add, so a reload cannot stack it), ability grants via `SpellcastingComponent.Learn`,
    optional AI-profile swap, per-phase telegraph colour/energy, and an enrage fuse. Phase 28B's
    table survives as `FallbackBoss`, so a missing or misspelled id costs the authored numbers rather
    than the structure.
    **Enrage keys off the first damage traded, not `BossEncounterStartedEvent`** — only
    `BossSummonComponent` publishes that (the Iron King's path), so every lair boss would have had a
    fuse that never lit. That gap is 36E's.
    Authored: `IronKing.tres` reproducing his Phase 28B numbers *exactly* (1.0/0.66/0.33,
    +25%/+15% then +30%/+20%, peaks 2.5/3.5/5.5, same `WarnColor`, no enrage — the equivalence is
    what makes "no behaviour regression" checkable, and it was diffed against the old constants), and
    one per dragon: the wild dragon escalates only, the Ash dragon harder and enrages sooner, the
    Ancient dragon escalates least but grants `spell.dragon_breath` at a third health — the ability
    set demonstrated with existing monster-only spells rather than invented content.
    Validator covers the domain **in both directions** (descending phases from 1.0, resolvable grant
    spells and profile ids, an archetype's `BossId` resolves, and a `BossId` only on an `IsBoss`
    archetype); the three new rules were confirmed to fire by breaking each and seeing exit 1.
    Build clean + 759 tests + `--validate` exit 0 + 3 clean `--play` runs. **`--play` cannot spawn a
    boss** (the `F1` console needs keyboard input), so the in-engine result proves boot, database
    loading and validation — the phase flares, grants and fuse are reviewed against the Godot 4.7 C#
    API and pinned by unit tests; seeing them fire is the maintainer's at-keyboard check.
- [x] **36B — `BossController` generalized from the Iron King** `[F]` ✅
  - **Done when:** the Iron King (Phase 28) is re-expressed through
    `BossController`/`BossResource` with no behaviour regression.
  - **Done:** 36A had already moved his fight into `data/bosses/IronKing.tres`; this is the other
    half — he is now `data/enemies/IronKing.tres` built by `EnemyArchetypeFactory`, and the 133-line
    `BossFactory` is **deleted**. `EnemyTemplateRegistry` drops its explicit registration (the
    archetype loop covers him, and keeping both logged "template is being replaced" with the winner
    decided by ordering), and `BossSummonComponent` — the one caller that bypassed the registry —
    goes through `EnemyTemplateRegistry.Create` with a `is not BossEntity` guard, so an archetype
    that ever loses `IsBoss` fails loudly instead of registering a plain `EnemyEntity` as the
    `ServiceLocator`'s `BossEntity`.
  - **Not a pure no-op, and the maintainer chose each difference.** Reach is now derived from his
    height like every creature since 34C (front reach 2.30 m → 2.46 m, ~+7% on a slow telegraphed
    maul) rather than adding hitbox-override exports used by one actor. He also *gains* four things
    his factory silently skipped: `HitReactionComponent` (his 30F rig already ships the clips),
    `WeaponTrailComponent`, membership of `ObjectiveLocator.EnemyGroup` (the HUD compass could not
    point at the game's first boss), and the shared 0.6 m nav stop distance.
  - **Two validator bugs surfaced by being the first archetype to hit them:**
    `RequirePath` treats empty as missing, so requiring a model would have failed the 20-odd
    archetypes that deliberately greybox — narrowed to "an *authored* path must resolve". And
    `LootTablePath` was required outright while `EnemyArchetypeFactory` has always treated it as
    optional; the contradiction went unnoticed only because every archetype happened to have a table
    until the Iron King, who drops nothing (28D's reward loop grants his relic). Both narrowed rules
    were negative-tested by pointing them at bad paths and watching them fail.
  - Build clean + 759 tests + `--validate` exit 0 (**31 archetypes, +1; registry still 33; no
    "being replaced" warning** — the three numbers that prove the swap) + 3 clean `--play` runs.
    **`--play` cannot spawn him** (the `F1` console needs keyboard input), so that covers boot,
    database loading and registration, not the fight — the at-keyboard check is
    `spawn 1 enemy.iron_king` plus lighting the brazier for the summon path.
- [x] **36C — Telegraph/wind-up + interrupt/stagger tooling** `[F]` ✅
  - **Done when:** reusable telegraph + interrupt/stagger windows drive off boss
    data.
  - **Three gaps it closed, all found by reading:**
    1. **A stagger interrupted nothing.** `MeleeWeaponComponent.StartSwing` refused to *begin* a
       swing while staggered, but `_PhysicsProcess` advanced `Windup → Active` regardless — so
       staggering a boss mid-wind-up did not stop the blow. `CancelCast` had existed since 29.5A
       with exactly one caller (the player's menu/pause handler), so a staggered caster finished its
       spell and a staggered dragon finished its breath.
    2. **A greyboxed boss telegraphed nothing** — the emissive flare needs a material only an
       authored model supplies (the 36B correction).
    3. **The flare was out of step with the danger**, fading over a fixed 0.5 s while the maul wound
       up for `WindupTime / AttackSpeed()` — and *further* out of step in a speed-buffed phase.
  - **Done:** `AttackPerformedEvent` carries the **effective** `WindupSeconds`, and a new
    `AttackInterruptedEvent` marks a cancelled action. A stagger during `Phase.Windup` cancels the
    swing (no hitbox, combo reset, buffer cleared so it cannot fire the instant the stagger lifts);
    once the hitbox is open the blow is committed, which keeps the punish window a thing to aim for
    rather than a race. `SpellcastingComponent` drops an active charge/channel on the same check,
    placed *before* its cooldown early-out — a cast with nothing on cooldown is still a cast — and
    `BreathComponent` needed no change, since it already stops when `IsChanneling` goes false.
    New `TelegraphRing` + `TelegraphComponent` draw a ground ring for exactly the reported wind-up,
    sized to the creature's actual reach and tinted by its current phase; both cues die early on an
    interrupt, and so do the player's viewmodel arms. `TelegraphComponent` knows nothing about
    bosses — `EnemyArchetypeFactory` happens to attach it to boss archetypes, which is what makes it
    the reusable half rather than another boss-shaped special case.
    Tuning is boss data: `BossPhaseResource.WindupPoiseMultiplier` scales incoming poise while its
    owner winds up, through the new pure `CombatMath.PoiseDamage` and two plain properties on
    `CombatComponent` (`InWindup`, written by the component that owns the window;
    `WindupPoiseMultiplier`, written by `BossController` on phase entry). The Iron King stays at
    `1.0` in all three phases — no regression; the dragons run 1.2–1.6, so their big swings are worth
    attacking into.
  - ⚠️ **Player-facing difficulty change, chosen deliberately:** "general tooling" includes the
    player, so being staggered mid-swing now cancels the player's attack too. Poise is symmetric;
    this was called out before implementation rather than discovered in play.
  - **Also:** `ApplyPhasePresentation` runs at initialize because phase one is never *entered*
    (`AdvanceTo` only steps up from it), so its colour and vulnerability would otherwise sit on
    defaults for the whole opening stage.
    Build clean + 772 tests (13 new: ring curve + poise arithmetic) + `--validate` exit 0, with the
    new "multiplier must be positive" rule negative-tested by setting one to `0` and watching it
    fail + 3 clean `--play` runs. **`--play` cannot spawn a boss** (the `F1` console needs keyboard
    input), so that covers boot and registration; a telegraph is a presentation feature and the real
    gate is the maintainer's at-keyboard pass — ring on a dragon, flare timing on the Iron King,
    a broken wind-up, a cancelled cast, and taking a stagger mid-swing as the player.
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
- [ ] **42.5C — Infiltration questline (branching, feeds into 47D)** `[C]`
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

## Phase 44 — Alpha Content Pass: all five realms blocked out `[C]`

> One sub-phase per realm = a big-but-bounded content session each; the spine ties
> them together. **The realm↔Flamebearer pairing below is LORE's** (LORE.md §The Four
> Realms + §The Fifth Realm) — it was off by one slot here until the Pale Concord gave
> the Hollow Queen a home.

- [ ] **44A — Ember Crown: extend to full first-pass extent** `[C]`
  - **Done when:** the realm beyond the slice region is greyboxed with hubs/POIs/
    encounters + the Iron King lair finalized as a framework boss.
- [ ] **44B — Frostfang Reach: hub, POIs, encounters, Storm Tyrant lair stub** `[C]`
- [ ] **44C — Ashen Wilds: hub, POIs, encounters, Beast Lord lair stub** `[C]`
- [ ] **44D — Sunspire Dominion: hub, POIs, encounters, Crimson Prophet lair stub** `[C]`
  - **Done when (each A–D):** the realm is reachable via streaming/fast-travel with
    a hub, key POIs, encounter sets, and the resident fallen-Flamebearer boss stub;
    `validate-all` green.
- [ ] **44E — The Pale Concord: the lost realm + Hollow Queen lair stub** `[C]`
  - **Done when:** the fifth realm exists as a region with a hub, POIs, encounters
    (the 34D undead are its natives) and the Hollow Queen's lair stub — but is
    **not** reachable by fast travel or a neighbour link, since LORE keeps it off
    every map until 47E's discovery beat opens the way. `Realm.PaleConcord` already
    exists for its `RegionResource`; nothing else is authored yet.
  - **The fiction wants rules, and this is where they get decided** — a sky stalled
    at dusk, nothing ripening, nothing rotting, nobody able to die. Candidates: no
    natural health regen inside its bounds, a `WorldClock` that doesn't advance,
    corpses that never despawn, NPCs who ask only to be released. Each is a real
    system change (`StatsComponent`, `WorldClock`, the despawn path), so pick
    deliberately or explicitly cut — don't let the realm ship as a reskinned field.
- [ ] **44F — Main-quest spine connecting all realms** `[C]`
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
  - **Done when:** Phase 49's ending choice writes a final tier across all five
    realms — including the Pale Concord, where "the lands heal" has to mean something
    different, since its problem was never decay.

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
- [ ] **47B — Storm Tyrant arc (Frostfang Reach)** `[C]`
- [ ] **47C — Beast Lord arc (The Ashen Wilds)** `[C]`
- [ ] **47D — Crimson Prophet arc (Sunspire Dominion)** `[C]`
- [ ] **47E — Hollow Queen arc (The Pale Concord)** `[C]`
  - **Extra bar:** this arc opens with a **discovery beat** — the realm is on no map
    (LORE §The Fifth Realm), so the questline has to establish that it exists before
    it can be entered. The other four arcs travel to a known place; this one finds a
    hidden one, and the 44E region stays unreachable until this beat fires.
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

> **Continuity note (player-requested inventory pass, 2026-06-29):** the 9 `EquipmentSlot`s are all
> real/functional but only MainHand (sword + **new bow → OffHand**), Head, Chest, Ring have authored
> items. **Hands / Legs / Feet / Amulet + the full weapon/armor/accessory variety are this phase (51A–C)**
> — intentionally empty until then. (Panel *polish* is Phase 30.5G; real ranged combat / firing the bow
> has no phase yet.) The bow is a placeholder OffHand equippable that swaps the active weapon.

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
  gate's criteria are verified in a build. The automated battery (build, tests,
  `--validate`, a live `--play`) is runnable here; the gate's *play-it-through*
  criteria are the maintainer's, and only they can close a gate.
- **Every sub-phase still owes the full DoD** (`PRODUCTION_ROADMAP.md` §0.3):
  builds, playable, `ISaveable` round-trips, `validate-all` green, docs updated,
  draft PR. The **Done when** line is *extra*, not instead.
</content>
</invoke>

---

## Appendix — deliberate-shortcut ledger (`ponytail:` markers)

Harvested by the Phase 35 audit. These are **known ceilings deliberately accepted**, not
bugs and not oversights — each one names the cheap thing that was built and the upgrade
path if it ever stops being enough. They are recorded here because a marker buried in a
source file is invisible to planning, which is how "later" quietly becomes "never".

Nothing here is scheduled. Revisit an entry only when its stated trigger actually fires.

| Where | Ceiling accepted | Upgrade trigger |
| ----- | ---------------- | --------------- |
| `Combat/CombatMath.cs` | No vulnerability side — a negative resist clamps to ×1 | An encounter needs damage *amplified*, not just resisted (DESIGN §1.5 permits it, alongside a resisted-school answer) |
| `Magic/SpellCombo.cs` | Combo table lives in code, not a `.tres` | A content author (not an engineer) needs to add combos |
| `Magic/SpellZone.cs` | Zones spawn at the caster with a fixed radius | Aim-placed or growing zones are wanted |
| `Magic/SpellTotem.cs` | Heals its owner only — no AI, collision or nav | A real summon system is needed (not before Phase 36's boss adds) |
| `Magic/SpellTomeComponent.cs` | One tome teaches one spell | A multi-spell archive — but that is just several tomes |
| `Magic/SchoolIdentity.cs` | Lightning single-jump; Arcane one buff per hit | A school needs more reach than one hop |
| `Magic/SchoolMasteryComponent.cs` | 1 mastery point per cast *event* — a channel ranks per tick | Channelled spells out-rank instants in practice |
| `World/SafeZones.cs` | One safe zone per region | A region needs a second safe area |
| `World/Weave.cs` | One ambient potency value per region | Ley-site restoration lands as content |
| `World/CellNavBaker.cs` | On-thread navmesh bake at cell load | A cell's geometry grows enough to stall a worker visibly |
| `Quests/ObjectiveLocator.cs` | Linear scan of the enemy group per call | Group size grows past what the caller's throttle hides |
| `UI/CompassStrip.cs` | Objective target re-resolved on a timer, cached | Targets move fast enough that the cache reads stale |
| `Player/FirstPersonArmsComponent.cs` | Same unmirrored mesh on both hands | Real first-person arm assets replace the greybox (Phase 53) |
| `Player/PlayerController.cs` | Camera spring masks `World`, which actor bodies share — a companion stepping behind the player pulls the camera in | It reads as twitchy in play; the fix is a dedicated camera-blocker layer |
| `Races/RaceComponent.cs` | Dev-tool race swap skips reputation | A player-facing respec/race-change is ever offered |
| `Enemies/AshenAcolyteFactory.cs` | Reuses the goblin loot table | A Fallen/cultist table is authored |
| `Localization/LocaleAudit.cs` | Hand-walks CSV lines (no quoted-comma support) | A string legitimately needs a comma inside quotes |
| `Magic/SpellcastingComponent.cs` | Blink is a straight horizontal ray | Vertical or curved blink is wanted |
| `Debugging/ContentValidator.cs` (×2) | Travel nodes validated at runtime, not authored; one regex for scene-authored flags | A second scene-authored writer of either kind appears |

**House rule going forward:** when you write a `ponytail:` marker, name the ceiling *and*
the trigger — a shortcut with no stated upgrade condition is indistinguishable from a bug
six months later.
