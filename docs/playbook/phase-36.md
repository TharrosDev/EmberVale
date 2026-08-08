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
- [x] **36D — Adds/summon-wave + arena hooks** `[F]` ✅
  - **Done when:** bosses can summon add waves and bind arena hooks declaratively.
  - **Done:** `BossAddWaveResource` (sub-resource inside `BossPhaseResource.AddWaves`) names any
    registered enemy id, a count, an optional `RepeatSeconds` and a `MaxAlive` cap, plus the
    `HealthMultiplier` `WorldEventDirector` already uses for hunt champions — so any creature in the
    game can be somebody's adds with no new factory. `BossController` summons on phase entry, ticks
    repeats, and on the boss's death kills every add through the **ordinary damage path**, so their
    loot and XP still land; despawning them silently would quietly take back value the player had
    already earned. `BossAdds` is the pure core (`SpawnSlot` ring placement, `SummonCount` cap
    arithmetic, 10 tests).
  - **Arena hooks are declared in the arena's own `.tscn`.** Spawn points are `Marker3D`s tagged
    `groups=["boss_add_spawn"]`, resolved **by group** (renaming or re-parenting one cannot silently
    unbind it, which a node path would) and **scoped by ancestry** to markers under the boss's own
    parent (`Node.IsAncestorOf`) — so two loaded arenas can never lend each other spawns, with no
    distance heuristic and no `owned`-flag trap. No markers falls back to a computed ring, which is
    what a lair with no authored arena gets. `ArenaHookComponent` is a plain `Node` (it belongs to
    the arena, not an actor) that reveals authored-hidden nodes at a phase and **resets on the boss's
    death** — that reset is load-bearing, since `BossSummonComponent` deliberately re-arms until 28D
    persists the defeat, and an arena left lit would show the next challenger the last fight's final
    phase from the doorway.
  - **Authored:** the Iron King calls two `enemy.cinder_thrall` at 66%, then `enemy.cultist` on a
    22 s repeat capped at three alive from 33% — both existing `faction.fallen` archetypes, so
    authored rather than invented content. `arena.tscn` gained four spawn markers and four
    ember-vent lights revealed at phases 2 and 3 by two hooks. The dragons get no waves: a roost has
    no markers, and a dragon that summons is a design decision rather than a framework one — the
    ring fallback is covered by tests instead.
  - Build clean + 782 tests + `--validate` exit 0, with all three new rules negative-tested
    (unregistered template, zero count, uncapped repeat) rather than only shown not to false-positive
    + the edited arena headless-instantiated to confirm the bindings parse (4 markers in group,
    4 hidden vents, 2 hooks with the right phases) + 3 clean `--play` runs. **`--play` cannot spawn a
    boss** (the `F1` console needs keyboard input), so it covers boot, validation and scene loading;
    adds arriving on the markers, the vents lighting and the arena clearing on the kill are the
    maintainer's at-keyboard pass.
- [x] **36E — Boss intro/defeat sequencing + guaranteed relic reward** `[F]` ✅
  - **Done when:** intro/defeat/reward (relic + corruption gain) are standardized
    in the framework.
  - ⚠️ **This one fixed a live bug, not just a gap.** `BossEncounterDirector` held every value as a
    constant naming the Iron King — `flag.iron_king_defeated`, `item.relic.iron_heart`,
    `dialogue.iron_king_absorb`, the timings — while `OnDied` fired for **any** `BossEntity`, and
    since 36A the dragons are among them. The reward was correctly guarded by an already-defeated
    check, but `_absorbPending = true` sat *outside* it, and `IronKingAbsorb.tres`'s absorb choice
    carried `Effect 4 / "25"` with no condition of its own. `FrostfangReach.tres` gates the region
    behind `flag.iron_king_defeated`, so the player reaches the dragons only *after* the flag is
    legitimately set — and from then on **every dragon kill re-opened his "absorb the flame?" choice
    for another +25 corruption**, repeatable, into the meter that decides the endings.
  - **Done:** `BossResource` gained `Encounter` and `Reward` groups (intro lock, defeat slow-mo +
    time scale, music cue, `RewardItemId`/`RewardQuantity`, `DefeatFlagId`, `DefeatDialogueId`), and
    the director now resolves the **dead boss's own** resource through a new `BossController.Fight`
    accessor. The reward decision moved into the pure `BossDefeat.Resolve` — reward, flag and
    dialogue as one first-time-only outcome, 8 tests, because splitting them is exactly how this
    broke. Belt and braces on the content side: the absorb choice gained a `MissingFlag` condition,
    so a second caller could not resurrect it.
  - **Intro for every boss:** `BossController.BeginEncounter()` is idempotent. The brazier calls it
    right after summoning (the Iron King keeps his entrance beat) and the controller self-calls on
    the first damage traded, reusing 36A's `_engaged` moment — so a lair boss nobody summons finally
    gets an intro lock and a healthbar. `BossEncounterStartedEvent` became
    `(Boss, DisplayName, TotalPhases)`: it used to carry the literal Loc key `"boss.name"`, so every
    boss bar read generically, and `GameHud` hard-coded the phase readout as `1/3`.
  - **Authored:** the Iron King's values are the director's old constants, diffed against them —
    2.5 s / 1.0 s / 0.35, same relic, flag, dialogue and cue. The dragons author beats only, and
    deliberately **no** `DefeatFlagId`, leaving `LairSpawnComponent` the single writer of their
    defeat rather than a second system that could drift.
  - Build clean + 790 tests + `--validate` exit 0, with all three new rules negative-tested —
    unknown item, unknown dialogue, and a reward authored with no flag (the bug's exact shape) —
    + 3 clean `--play` runs. **`--play` cannot spawn a boss**, so that covers boot and validation.
    The at-keyboard gate: the Iron King's bar should now read his name and his phase count; kill him
    for relic + choice; **then kill a dragon and confirm the choice does not re-open**; and a dragon
    should now get an intro lock and a bar where it previously got neither.
  - **Phase 36 complete (36A–36E).**

---
