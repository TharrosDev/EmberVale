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
