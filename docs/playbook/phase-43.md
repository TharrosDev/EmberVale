## Phase 43 — Cinematics & Scripted Sequences `[F]`

> `GameManager.RefreshPause`/`UiState` remain the gameplay-lock authority. Dialogue,
> animation, EventBus, `AudioDirector`, camera settings and story flags retain their facts.
> Sequences orchestrate; they do not duplicate those systems.

- [ ] **43A — Sequence schema, database, validator and recipe** `[F]`
  - **Goal:** statically check a data-driven timeline before camera production.
  - **Build / Author:** `CutsceneResource`, typed tracks/cues, cast/location ids, time/duration,
    required/optional actors, completion flag and restart policy; template/recipe; validation for
    ids, monotonic time, exclusive overlaps, missing endpoints and referenced loc/audio/animation.
  - **Do not:** embed arbitrary scripts or scene paths in cue payloads.
  - **Verify:** ordering tests and negative cases at both ends of ranges; template validates.
  - **Done when:** bad cues fail by name and valid scenes need no source edit.

- [ ] **43B — Playback lifecycle, input lock and recovery** `[F]`
  - **Goal:** never strand input, pause, HUD or camera state.
  - **Build / Author:** director start/end/abort, one non-pausing `UiState` lock, HUD policy,
    re-entrancy refusal, owner cleanup and camera restoration to the live first/third setting.
  - **Verify:** end/skip/load/tree removal/actor free, menu collision and two simultaneous starts.
  - **Done when:** every exit restores exact prior state and publishes one result.

- [ ] **43C — Camera tracks, blocking and animation** `[F/P]`
  - **Goal:** author shots and cast movement without bespoke nodes.
  - **Build / Author:** fixed/look-at/follow/keyed or spline camera minimum, blend/cut/fade/FOV;
    stable actor/marker binding, move/warp/face/wait/animation, AI/schedule snapshot/restore,
    absent-cast fallback and blocking timeout.
  - **Verify:** interpolation boundaries, target disappearance, nav/animation failure, skip mid-move,
    camera modes, ultrawide safe frame and actor schedule resume.
  - **Done when:** a three-actor multi-shot block plays, scrubs and recovers predictably.

- [ ] **43D — Dialogue staging and interactive-choice handoff** `[F/C]`
  - **Goal:** stage localized dialogue without cinematic-only prose/effects.
  - **Build / Author:** line/speaker/advance cues through dialogue/Loc authority; subtitle hooks; clean
    handoff to `DialoguePanel` for real choices and explicit time behavior during the choice.
  - **Verify:** subtitles, missing speaker, locale expansion, skip during line, controller choice,
    effect fires once.
  - **Done when:** staged lines and interactive branches coexist with `DialogueSession` owning effects.

- [ ] **43E — VFX, SFX, music and world cues** `[F/P]`
  - **Goal:** synchronize presentation through existing services.
  - **Build / Author:** typed VFX, positional/non-positional audio, music, screen treatment and safe
    EventBus cues; cleanup on skip/abort and reduced-motion alternatives.
  - **Verify:** buses, loop cleanup, music restoration, missing assets and freed VFX owner.
  - **Done when:** no player/effect leaks and Settings remain honored.

- [ ] **43F — Skip, checkpoint, death and load semantics** `[F]`
  - **Goal:** make skip/recovery story-safe.
  - **Build / Author:** hold-to-skip UI, authored skippable policy, atomic finalization shared by natural
    end/skip, already-seen/restart rule and pre-sequence autosave request at dangerous transitions.
    Never serialize playhead; reload restarts or resolves from an authored checkpoint.
  - **Verify:** skip at every cue class, death/load, quit between flag and transition, repeat trigger.
  - **Done when:** natural/skip/recovery converge without duplicated flags/rewards.

- [ ] **43G — Authoring/debug harness and timeline trace** `[F/P]`
  - **Goal:** make failures diagnosable in one session.
  - **Build / Author:** list/play/stop/seek/actor-report controls, cue/result trace, boundary-frame capture
    and validation preview; document cast/asset prerequisites.
  - **Verify:** bad cast/cue report, seek across every cue, aborted run and headless validation.
  - **Done when:** a future author identifies the first failed cue without source inspection.

- [ ] **43H — Representative boss intro and story set-piece** `[C]`
  - **Goal:** prove the whole pipeline on production scenes.
  - **Build / Author:** Iron King intro plus one dialogue-heavy transition with cameras, blocking,
    animation, VFX, audio, choice/flag and scene transition—no bespoke sequence code.
  - **Verify:** natural/skip/reload, optional companion absent/present, camera modes, reduced motion,
    subtitles and captured shot boundaries.
  - **Done when:** both scenes pass the same trace/validator and recover on every interruption.

---
