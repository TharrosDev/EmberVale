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
    a poolable effect (docs/RECIPES.md pooling).
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
