# Stage A — Status & Known-Issues Ledger

> **Scope:** the Stage-A (Phases 22–25) integration sign-off. It is a point-in-time
> snapshot, not a live status of the whole project — later phases (26+, 29.5, …) are
> tracked in [`SESSION_PLAYBOOK.md`](SESSION_PLAYBOOK.md) and
> [`PRODUCTION_ROADMAP.md`](PRODUCTION_ROADMAP.md).

Status of the **Stage-A foundation (production systems 22–25)** after the Phase 25.5
hardening pass. This is the integration sign-off for Phase **25.5G**: it records what
the automated battery verifies, what remains a maintainer at-keyboard check, the known
issues, and perf baselines.

**Last swept:** 2026-06-27 (Phase 25.5G).
**Verdict:** automated battery **green**; integrated loop pending the maintainer
play-through itemized below. No known regressions.

---

## 1. Automated battery (reproducible)

| Check | Command | Result |
| --- | --- | --- |
| Build | `dotnet build Embervale.sln` | clean — 0 warnings, 0 errors |
| Unit tests | `dotnet test tests/Embervale.Tests` | **126 passed**, 0 failed (~30 ms) |
| Content gate | `godot --headless --path . -- --validate` | `ContentValidator.RunAll` OK, exit 0 |
| Boot integration | `run_project` → `get_debug_output` → `stop_project` | clean boot, `errors: []` |

The headless boot loads all 14 content databases, seeds the `EnemyTemplateRegistry`,
passes `ContentValidator`, and reaches `Boot → MainMenu` with no errors or invariant
breaks. `--validate` exercises the heavier `RunAll` battery (cross-refs + well-formedness
+ graph reachability + the 25.5F region-geometry & locale gates).

Pure-logic test pins accumulated across Stage A: `SaveKeyPolicyTests`,
`StreamDecisionTests`, `CorruptionTierTests`, `SettingsMathTests`, `UiStateTests`,
`LocaleAuditTests`, plus the pre-Stage-A suite.

---

## 2. Stage-A integration loop (systems 22–25)

| Surface | System(s) | Automation | At-keyboard |
| --- | --- | --- | --- |
| Meta-shell / settings | title, slots, `SettingsService` | boot reaches MainMenu; `SettingsMath` pinned | settings *feel* (sensitivity, Invert-Y) |
| Save / load | `SaveManager`, slot lifecycle, autosave ring | `SaveKeyPolicy` pinned; `savecheck` dev cmd | F5/F9 round-trip, slot delete/continue |
| Region streaming | `RegionStreamer`, cell persistence | `StreamDecision` pinned (load/unload/settle) | no pop-in, no hitch on traversal |
| Region transitions | portals, hard-load + settle gate | streamer-idle gate logic pinned | transition both ways, loading screen |
| Fast travel | `FastTravelService`, `TravelNodeComponent` | — | attune + jump lands safely (see #3) |
| Corruption | `CorruptionComponent`, tiers, HUD vignette | `CorruptionTiers.Transition` pinned | vignette/appearance track + reset on load |
| Compass / map / HUD | `CompassStrip`, `MapScreen`, `GameHud` | `CompassMath` pinned | markers track; menu overlap mouse state |
| Analytics | `AnalyticsSink` (dev-only) | subscriptions wired (25.5F) | `.jsonl` lines appear in a real session |

---

## 3. Known-issues ledger

| # | Issue | Status | Ref |
| --- | --- | --- | --- |
| 1 | Fast travel dropped the player **inside** the travel post (recorded the post's own position as the landing point) and trapped them. | **Fixed** — now records the attuning player's walkable position. | PR #85 |
| 2 | Pre-25.5A saves still warn on legacy `stats:<runtimeId>` keys. | **Expected** — stale data, not a regression; a fresh save is clean. | 25.5A |
| 3 | Nodes attuned in a *pre-fix* save still carry the old post landing position. | **Expected** — re-attune (or fresh attune) records the corrected spot. | PR #85 |
| 4 | `Settings.ReducedMotion` is exposed but unused. | **Deferred** — documented Phase-54 placeholder, by design. | 25.5D/E |

No open defects. Items 2–4 are documented expected-behaviour, not regressions.

---

## 4. Maintainer at-keyboard checks (to confirm in the play-through)

These are *unverified by automation* — they need a live game (`GameState.Playing`),
which the headless/MCP path can't drive. Each is the visible confirmation behind a
Stage-A fix:

- **25.5A** — New Game → F5 → F9 produces a **warning-free** save/load log; `savecheck`
  in the F1 console reports `0 volatile keys`.
- **25.5B** — cross a region transition: **no cell pop-in** after the loading screen
  clears; rapid traversal shows no thrash or hitch.
- **25.5C** — raise corruption (F1 `corrupt`), then F9 a low-corruption save: the HUD
  vignette and ash-vein appearance **reset down** (no stuck tier).
- **25.5D** — change Mouse-Sensitivity and toggle Invert-Y in settings: the camera
  **actually responds**.
- **25.5E** — open the inventory, press **F1** over it, close the console: the mouse
  **stays free** until the inventory is also closed.
- **25.5F** — after a transition + fast travel + save, the session
  `user://analytics/session_*.jsonl` contains `region_transition`, `fast_travel`,
  `corruption_tier`, and `save` lines.
- **25.5G (this)** — fast travel lands the player **beside** the post, free to move.

---

## 5. Perf baselines

Observable without a profiled live session:

- **Region streaming:** 1 cell instanced/frame (`RegionStreamer.LoadsPerFrame`), 10 m
  unload hysteresis (`UnloadMargin`) — waves spread across frames, no single-frame spike.
- **Post-transition loading gate:** min show 0.15 s, safety cap 3.0 s
  (`GameBootstrap.LoadingMin/MaxSeconds`); the screen holds until the streamer reports the
  destination settled, so no pop-in and no needless wait.
- **Unit suite:** 126 tests in ~30 ms (pure logic, no engine).

**TODO (maintainer):** capture a live FPS/hitch profile during traversal + a busy
encounter via the F3 `ProfilerOverlay`, and a memory check across repeated region
unload/reload, to set true runtime baselines.

---

## 6. Play-through checklist (closes 25.5G)

Run once end-to-end; each line exercises the system in parentheses:

1. New Game → move/look/sprint/jump/attack (player controller, locomotion, combat).
2. F5 save → F9 load; open the slot panel, delete a slot, Continue (save/slot lifecycle).
3. Walk a region transition both directions (streaming + hard-load + settle gate).
4. Attune a waystone, fast travel to it (fast travel — lands beside the post).
5. Raise corruption to a new tier, then F9 a lower save (corruption + HUD sync).
6. Open inventory/map/dialogue/pause and the F1 console in combinations (UI mouse state).
7. Check `user://analytics/session_*.jsonl` for the Stage-A event lines (analytics).
8. Watch the F3 profiler for hitches during 1–7 (perf).

No regressions expected; log anything found back into §3.
