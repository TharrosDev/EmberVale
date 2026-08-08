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
