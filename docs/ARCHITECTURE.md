# Embervale — Architecture & Systems Reference

> **Read one section, not the file.** `grep -n "^##" docs/ARCHITECTURE.md` is the map.
> [`RECIPES.md`](RECIPES.md) is how to *add* content; this is how the machinery *works*. Working
> rules and gotchas are [`../CLAUDE.md`](../CLAUDE.md); persistence is [`SAVE_FORMAT.md`](SAVE_FORMAT.md).

Godot 4.7, C# (.NET 8), component-based, event-driven, resource-driven; buildable and playable at
every commit.

---

## 1. Architecture overview

### 1.1 Four lifetimes, and who owns each

**The load-bearing idea in the codebase.**

| Lifetime | Owner node | Created | Destroyed | Holds |
| --- | --- | --- | --- | --- |
| **Application** | `ApplicationRoot` (`Main.tscn` root) | process start | process exit | settings, input actions, localization, content databases, save-file IO |
| **Session** | `GameSession` | New Game / Load | quit to title, failed load | clock, autosave, economy ledgers, map discovery, companions, housing, bestiary, persistence directors, audio, UI, the player, `HiddenRealmReveal`, `EndingSequence` |
| **World** | `WorldHost` (child of the session) | with the session's world | before the session | region streamer, weather, sky, encounter and world-event directors, portals, safe zones, the Weave |
| **Entity** | the entity node | spawn | `QueueFree` | components (`OnInitialize`/`OnTeardown`) |

⚠️ **A service's lifetime is decided by WHERE IT IS PARENTED, and nothing else.**
`ServiceScope.For(node)` walks ancestors to the nearest `IServiceScopeHost`.

The lifecycle repeats without a scene reload: application → session → world → gameplay → world
destroyed → session destroyed → title. `SessionLifecycleCoordinator.DestroySession()` removes the
session **synchronously** (every `_ExitTree` has run when it returns), then resets the
process-lifetime statics (`SafeZones`, `Weave`, `PersistentActorRegistry`, `UiState`, `Invariant`);
`SessionResetTests` finds them by reflection. **Gate: `--lifecycle`** — three New Game → Playing →
save → destroy → Load → Playing → destroy round trips, failing on any surviving session, registration,
subscription, `ISaveable` or unfreed node.

### 1.2 Services: scoped, not global

`ServiceScope` holds one lifetime's services; `ServiceLocator` is the read side — `TryGet<T>` walks
World → Session → Application, inner shadowing outer. A node service registers with
`ServiceScope.RegisterOwned(this, this)` in `_EnterTree` and is unregistered by its own
`TreeExiting`. Disposing a scope removes exactly its entries; **a freed service in a live scope is an
`Invariant` violation**. Prefer explicit references (composition roots hand services to what they
build; components use `Entity.GetComponent`); the locator is for genuinely late-bound lookups. It holds
one instance per type: the player as `PlayerCharacter`; enemies are not registered.

### 1.3 Autoloads

`project.godot` `[autoload]`, in order: `EventBus` (typed pub/sub), `ServiceLocator` (scope reads),
`GameManager` (`GameState` machine), `SaveManager` (`ISaveable`s to `user://`). Each has a static
`Instance` set in `_EnterTree` and references no gameplay types. `Log`, `Invariant` and `GameInput`
are static classes; use `Log.Info/Warn/Error`, never `GD.Print`.

### 1.4 EventBus

```csharp
EventBus.Instance.Subscribe<EntityDiedEvent>(OnEntityDied);
EventBus.Instance.Publish(new EntityDiedEvent(entity));
EventBus.Instance.Unsubscribe<EntityDiedEvent>(OnEntityDied); // always pair
```

Events are immutable, past-tense `readonly record struct`s implementing `IGameEvent`. Always
unsubscribe in `OnTeardown`/`_ExitTree`. `Publish` snapshots handlers (safe to (un)subscribe during
dispatch) and catches/logs handler exceptions. Handlers run synchronously, even while paused.

### 1.5 Entities are compositions of components

`IEntity`: `DisplayName`, `RuntimeId`, `PersistentId`, `Node3D Body`, `GetComponent<T>()` /
`TryGetComponent` / `GetComponents` / `HasComponent`. Hosts: **`Entity : Node3D`** (static actors) and
**`CharacterEntity : CharacterBody3D`** (physics actors; `PlayerCharacter`, `EnemyEntity` are type
markers). Shared host logic is `EntityNode` (id allocation, child lookup, `FindOwner`).

**`EntityComponent : Node`** resolves its owner in `_Ready`; override **`OnInitialize`/`OnTeardown`,
never `_Ready`/`_ExitTree`**. Identity is assigned in `_EnterTree` (top-down); components initialize
from `_Ready` (bottom-up), so the host exists but a sibling may not have initialized.
`GetComponent<T>` searches **direct children only** and returns the **first** match. `Hitbox`/
`Hurtbox` are `Area3D`s that use `FindOwner` directly.

---

## 2. Systems

### 2.1 Stats (`src/Stats`)

- `StatType`: resources (`Health`, `Stamina`, `Mana`), primaries (`Strength`, `Dexterity`,
  `Intelligence`, `Vitality`, `Endurance`), derived (`Armor`, `PhysicalPower`, `SpellPower`,
  `MoveSpeed`, `AttackSpeed`, `CritChance`, `CritDamage`) and the six school resistances.
  `StatTypes.IsResource` classifies depleting ones. Append-only.
- `Stat`: `final = (base + Σflat) × (1 + ΣpercentAdd) × Π(1 + percentMult)`, lazily cached, fires
  `Changed`. `StatModifier(value, Flat|PercentAdd|PercentMult, Source)`; `RemoveModifiersFromSource`.
- `AttributeSet` (`.tres` presets, `ToBaseValues`, `CreateDefault`). A missing stat reads 0.
- `StatDerivation` (pure): the per-point table for the five primaries, `Bonuses`, `GrowthTotals`, and
  `DodgeStaminaFactor` / `ManaCostFactor`. `StatDerivationComponent` (player only, after Progression/Perks,
  before `RaceComponent`) listens to each primary's `Changed` and keeps one Flat modifier per derived stat,
  sourced to itself, sized `(Value - BaseValue) * coefficient`: an actor at its base primaries is untouched,
  re-applying is remove-then-add, and it never writes a primary so it cannot loop. Nothing is saved; it
  re-derives from the primaries, which re-derive from level, race, gear and perks.
- `StatsComponent`: one `Stat` per type, current values for resources, `ApplyDamage(amount, source)` /
  `Heal`, passive regen (never on a corpse), `ISaveable` (current resources), raises
  `ResourceChanged`/`EntityDamaged`/`EntityDied`/`EntityHealed` events.

### 2.2 Combat (`src/Combat`)

- `DamageType`: `Physical` (Armor), `Fire/Frost/Lightning/Arcane/Nature/Necrotic` (own resistance),
  `True` (unmitigated). `DamagePacket` (attacker-built, carries `Source`, and a `HitKind` + `Charge`
  the attacker stamps) and `DamageResult` (also `Parry`, `GuardBroken`, `Opening`).
- `CombatMath`: `RollAttack` (adds `PhysicalPower × 0.5`, crit chance capped 0.75, multiplier
  clamped 1.25–4), `RollSpell` (SpellPower + Intelligence), `Mitigate` → **one curve for every
  school**, `100/(100+x)` via `ResistanceStat` + `ArmorMultiplier`. **Resistance, never immunity**:
  the multiplier stays in `(0, 1]` for resistance; a negative value is a vulnerability that amplifies,
  bounded below ×2. An unblocked hit does at least 1 damage. `PoiseDamage` applies the wind-up
  multiplier.
- `Hurtbox` (layer Hurtbox, mask 0; needs a shape). An actor may carry several **hit zones**, each
  with a `ZoneId`, a `DamageMultiplier` and a `PoiseMultiplier` (`HitZoneResource`); `IsWeakPoint` is
  a damage multiplier of 1.5+. `HitDedupe` makes hits once per **owning entity** per swing or blast
  (shared by `Hitbox` and `SpellResolver`). When a swing overlaps several zones of one body, `Hitbox`
  delivers to the highest-multiplier zone, not whichever physics returned first.
- `Hitbox` (layer Hitbox, mask Hurtbox): `Activate(packet)` opens the window, `_PhysicsProcess`
  **polls overlaps** (never trust `area_entered` timing), skips its owner and same-`Team` hurtboxes.
- `CombatComponent`: `Team` (0 player, 1 hostile, 2 neutral), poise/stagger, `IsBlocking` (raise) and
  `GuardUp` (raised **and** not staggered — presentation reads this), `InWindup` +
  `WindupPoiseMultiplier`, `ReceiveDamage` (see the pipeline below). A flinch has its own timer
  (`IsFlinching`); `IsStaggered` means an interrupting reaction only.
- `PoiseReaction` resolves flinch/stagger/heavy/knockdown by `ReactionClass`; a flinch does not
  interrupt; a boss is never knocked down or pushed.
- `WeaponResource`: damage type, base/poise damage, stamina cost, an `Attacks` chain, or legacy
  wind-up/active/recovery floats synthesised into a chain (light links, a heavy, a roll-cut).
  `RecoveryCommit`/`FinisherRecoveryCommit` keep the start of a swing's recovery committed.
  `IsRanged` + projectile fields make a bow (see *Ranged* below).

**The defence pipeline (`ReceiveDamage`, rules in `DefenceRules` and `CombatMath`).**
Invulnerable (dodge i-frames) → whiff. Otherwise: is the attacker in the guard arc (front zone within
70% of `GuardArcDegrees`, flank the rest; the rear is uncovered)? A front guard may **parry** on
timing (`ParryGrade`: Perfect is free and staggers longest, Good, Late is a 90% deflect with no
riposte); arrows and spells can be blocked but never parried. A block costs stamina scaled by the
blow's guard pressure and weight; a guard that cannot pay, or that a `Charged` (≥0.9) or `Plunge`
blow crushes, **breaks** (full hit, long class-scaled stagger, `GuardBrokenEvent`). Then mitigation,
then poise (no poise chip while already staggered, so a stagger cannot be chain-locked). A body
becomes **open** (`OpenCause`: PoiseBreak, Parry, PerfectParry, GuardBreak; the stagger plus 0.25 s)
and the next melee blow on it is a `Riposte` critical (bonus by cause, once); a blow from ≥120° round
the back is a `Backstab`. Both publish `CriticalHitEvent`; bosses take 60% of the bonus; the player's
side (team 0) is never opened, so poise stays symmetric without a hidden multiplier on the player.
How each `HitKind` scales damage/poise/guard pressure is one table in `DefenceRules`; attackers stamp
`Kind`/`Charge` and never pre-scale by them.

**The action timeline — one clock.** `ActionDefinitionResource` authors an animation slot, a
duration, gameplay windows **as fractions of that duration**, commitment, cost, damage/poise scale,
hit volume and AI metadata. `ActionTimeline` is the pure arithmetic (phase, hit/cancel/combo windows,
stagger rules, clip speed). **`CharacterActionComponent`** is the one executor for every actor and reads
progress off the AnimationTree's playback position (`CharacterAnimationComponent.ActionProgress`);
`Duration = 0` lets the clip decide, a positive one warps the clip to fit; a body with no clip runs the
same fractions on a fallback timer. `ActionReleasedEvent` fires at the start of the active window
(melee opens its volume, a cast delivers, a bow looses). A stagger during wind-up cancels the action
unless `Interruptible = false` (hyperarmor); once the hit window opens the blow is committed — for
every actor, the player included. `ActionSelection` is what AI may know ("hit what is this far away").
`MotionWarp` closes the last of a committed attack's gap, bounded and **swept** so it never passes a
wall; it is not root motion (the Meshy clips carry none). Telegraphs run off
`AttackPerformedEvent.WindupSeconds` (the *effective* wind-up), never a constant.

**Offence on the executor.** Every swing carries a per-swing context (kind, charge, damage/poise
multipliers, hyperarmor, step, speed). Holding attack ≥ 0.22 s (`ChargeRules`) charges a heavy or
charged blow (`HitKind.Heavy` below charge 0.3, `Charged` from it, hyperarmour from 0.8); an attack
pressed in the air dives and lands as `Plunge` (`PlungeRules`, sphere `PlungeHitbox`); an attack out of
a roll is the weapon's roll-cut. `AttackInput` turns raw presses into intents, `AttackBuffer` only
accepts a press within 0.28 s of the cancel point, and `AttackDirections` picks a humanoid's
forward/back/side step once at the commit. A committed action's facing turns no faster than its
`TurnDegreesPerSecond` (always for AI; for the player only while locked on). `RecoveryVulnerable`
actions mark their committed tail through `CombatComponent.InWindup` (extra poise damage) and
`CombatComponent.RecoveryOpen` (a riposte opening, `OpenCause.Recovery`, closed by the first riposte);
`CharacterActionComponent.InCommittedRecovery` is public.

**Ranged (`RangedAttack`, `BowDrawComponent`, `Arrow`).** `Shoot` delegates to `RangedAttack.Fire`.
The player's draw is how long attack stayed held through the bow's startup (`RangedMath`); it scales the
*rolled* damage, speed and poise, and stamps `Charge`. Anyone without a `BowDrawComponent` looses a full
draw with `Charge = 0`. The launch direction is solved under gravity through
`AimController.Focus` (the crosshair convergence, **not** `AimPoint`, which the router sets to the
eye), bent by `AimAssistMath` (scaled by `CombatComfort.AimAssist`). `Arrow` queries the physics space
directly each sub-step (`HitZoneRouting`: nearest body, highest-multiplier zone; `WorldRay` for solid
world geometry, ignoring actor bodies that share layer 1).

**Presentation is one-way (`CombatFeedbackDirector`).** The director gathers `DamageDealtEvent`,
`EntityParriedEvent`, `GuardBrokenEvent`, `CriticalHitEvent` and `EntityStaggeredEvent` per target and
publishes one `HitConfirmedEvent` (a `HitOutcome`) at the end of the frame. Hit-stop, the mesh lurch,
`CombatFeedbackOverlay`, `DamageNumberLayer` and `DamageDirectionOverlay` consume it, so all need the
director in the session. Everything that punctuates a blow for feel reads `CombatComfort` (Hit Stop,
Screen Flash, Damage Numbers, Lock-On Assist, Aim Assist; Reduced Motion caps the first two at 25%).
`TelegraphComponent` classes (`TelegraphClass`: standard, parryable, unblockable, sweep) come from
`ActionDefinitionResource.Telegraph` / `SweepDegrees`; left on `Auto` they are inferred from the action
id, hitbox and commitment. `LockOnComponent` publishes `LockChangedEvent` and `LockBrokenEvent`. ⚠️ Its
line-of-sight ray masks `CameraBlocker`, not `World`: actors share the World layer and the ray ends
inside the target's own capsule, so a World mask made every actor unlockable. Look at combat with
`godot --path . --fixed-fps 60 -- --combat-shots` (`CombatShots`, PNGs under the user data
`combat_shots/`); it needs a real window and a save, and a live event banner or boss bar hides the
nameplate by design (`EMBERVALE_SLOT=<slot>` picks a save without one). A new number on a target starts
above the older ones' current height (`DamageNumberMath.LiftAbove`).

### 2.3 Movement and animation (`src/Movement`, `src/Animation`)

- `LocomotionComponent`: input-agnostic kinematic motor (`Move(delta, wishDir, sprint, jump)`; speed
  from `MoveSpeed`). `Flying` swaps gravity for a vertical servo toward `TargetAltitude` — vertical
  only, so horizontal steering is unchanged; landing is just descending until `IsGrounded`.
- `FlightComponent` (`src/Enemies`): take-off/land cycle from the AI profile (`TakeoffRange = 0` ⇒
  never flies); pure `FlightDecision`. Airborne, the AI holds its swing, skips the navmesh, and grounds
  on leaving combat.
- `CharacterAnimationComponent` + `LocomotionTree`: blend space over signed speed, state machine, a
  bone-masked upper-body layer. The shared full-body library is `anim_meshy.res`; the older
  `anim_library.res` still owns `ride`, `sitting`, `interact`, `swim`. A rigged body with no clips gets a
  created `AnimationPlayer`.
- `FootIkComponent` plants feet on terrain. `EquipmentSockets` + `EquipmentPresentationComponent` are
  the one socket contract (weapon, shield on the forearm, ammo).

### 2.4 Player (`src/Player`)

Six components, added last and in order by `PlayerFactory`:

| Component | Owns |
| --- | --- |
| `PlayerPhysicsQueries` | the pooled ray/sweep/overlap queries and the exclusion list (shared by three readers) |
| `PlayerCameraRig` | camera, view modes, blend, wall spring, FOV, layer summing |
| `PlayerLookInput` | mouse/stick turning, mouse capture |
| `InteractionSensor` | what `E` acts on, the HUD prompt, the hold-`E` pickup sweep |
| `AimController` | the `AimPoint` |
| `PlayerInputRouter` | input → sibling calls in one documented order |

- ⚠️ **The router keeps one `_PhysicsProcess`**; order is load-bearing: the camera rig runs inside
  the not-playing guard (it touches nodes a teardown frees), focus resolves before lock-on, the mount
  answers sprint before locomotion, dodge is refused mid-swing.
- **True first person**: the camera rides the head bone (position only) and the body stays visible.
  Third person is over-the-shoulder; body yaw equals camera yaw in both. The mode is
  `Settings.ThirdPersonCamera` (settings toggle and `V` flip the same value). Distance (2–6 m),
  shoulder and FOV are live settings applied by the rig.
- `CameraProfile` (exploration, sprint, combat, target-lock, aim, mounted) multiplies the player's
  settings, never replaces them. The spring sweeps `CameraBlocker` only, so actors never yank the
  camera. `CameraRigMath` is pure.
- ⚠️ **The rig is the only writer of the camera transform.** Shake, motion, lock-on/aim framing and the
  special views are `ICameraLayer` components on the player body that return a `CameraNudge`; the rig
  finds them by walking the body's children (`IEntity.GetComponents` is constrained to
  `EntityComponent`, so an interface cannot go through it) and sums them in `CombineLayers`. A layer
  that writes `Camera.Position` or `Rotation` itself puts two writers on one transform. Feel layers
  scale by `CameraComfort`. The rig ticks from the router, which does not run while the tree is paused; a dialogue pauses it, so the rig also ticks itself in `_Process` (always-processing) for the length of a dialogue only.
- Aim comes from the camera; interact reach is measured from the character. Lock-on facing is
  `LockOnComponent.FaceTarget()`.

### 2.5 Pause and blocking menus

`GameManager.RefreshPause()` is the **single writer** of `GetTree().Paused`: paused if the state is
`Paused` or a world-pausing menu is open. `UiState` counts `MenuOpen` (controls suspended) and
`WorldPaused` (subset that stops simulation). Modal `UiPanel`s pause by default and run
`ProcessMode.Always`; the boss-intro lock, narration and the dev console pass `pausesWorld: false`.
Never scatter `MenuOpen` checks through gameplay systems.

### 2.6 Enemies (`src/Enemies`)

| Piece | Owns |
| --- | --- |
| `EnemyAIComponent` | profile resolution, LOD clock, the state tick (Idle, Patrol, Investigate, Combat, Retreat, Returning, Dead), despawn |
| `EnemySenses` | sight (cached per profile interval, one reused ray), faction standing, provocation memory |
| `EnemyCasterTactics` | standoff banding, kiting, heal → attack → ward priority |
| `AiNavigator` | navmesh steering, arrival, facing — ⚠️ **shared with `CompanionAIComponent`** (a drifted copy once queried the nav server every frame); `debug_pass_regressions.gd` asserts no second copy returns |

Pure cores: `AiSenseRules`, `CombatTransition` (the five combat guards in order — leash before
health), `AiLodClock`, `GuardCycle`, `PackFlank`, `CasterDecision`, `TerritoryLeash`.

- **The roster is data.** `AIProfileResource` (`ai.*`) holds every knob; behaviours are branches
  gated on profile numbers, so they compose. `EnemyArchetypeResource` (`enemy.*`) is a creature:
  name, build paths, `ModelPath`, **`ModelScale`** (uniform scale so a boss can reuse a body at its own
  size; match `CapsuleHeight`), tint, profile, faction, spells, capsule, poise, regen, XP, `IsBoss`,
  `BossId`, `HitZones`, `DialogueId`. `EnemyArchetypeDatabase` registers a builder per archetype with
  `EnemyTemplateRegistry`, so a new `.tres` is spawnable; `EnemyArchetypeFactory` builds it (melee
  reach scales with height against 1.8 m). Remaining bespoke factories — `EnemyFactory` (goblin),
  `AshenAcolyteFactory` — are structurally different.
- Unknown template ids fall back to the goblin and the validator flags them.
- **Perception and hostility:** vision + FOV + line of sight + proximity; spotting broadcasts
  `EnemyAlertedEvent` (only if `AlertRadius > 0`); engagement requires
  `ReputationComponent.IsHostile(faction)`; an unfactioned actor defaults hostile; a direct hit sets
  `_provoked`.
- **Leash:** past `TerritoryRadius` the AI enters `Returning` and ignores the player until back
  inside `ReturnFraction` (0 = no leash).
- **Casters:** a `SpellcastingComponent` routes Combat to the caster branch; authored `KnownSpellIds`
  bypass `Learn`'s corruption gate.
- `AshenAffliction` (corruption variants): applied after `AddChild` from `EncounterDirector` off
  `CorruptionChance`; never changes `TemplateId`; always `Duplicate()`s materials.
- `LairSpawnComponent`: places a world boss; **the spawner persists** (one bool, `ISaveable`), not the
  boss, because a boss spawned on `RegionCellLoadedEvent` races `CellPersistenceDirector`.
- `BreathComponent`: holds a channel via `SpellcastingComponent.BeginCastById`, aims by pointing
  `CastOrigin` (so a hovering dragon breathes down); pure `BreathWindow`.
- `BestiaryEntryResource`/`BestiaryService` (`ISaveable`, `bestiary`): kills and Ashen kills per
  template id; validated in both directions.

### 2.7 Bosses and the Flamebearers (`src/Enemies`)

A boss fight is data: `BossResource` (`data/bosses`) holds ordered `BossPhaseResource`s (HP
threshold, stat escalation, `GrantSpellIds`, AI-profile swap, telegraph colour/energy,
`WindupPoiseMultiplier`, `Attacks`, `AddWaves`) plus enrage, encounter timing and reward. An archetype
names one via `BossId`; `EnemyArchetypeFactory` attaches `BossController` to any `IsBoss` archetype
(no `BossId` → the default three-stage table).

- Phases are entered at or below a threshold and never left; **a big hit enters every phase it
  crosses**. Granted spells go through `SpellcastingComponent.Learn` (ignores `PlayerLearnable`).
  Pure cores: `BossPhases`, `BossAdds`, `BossDefeat`.
- **The enrage clock starts on the first damage traded.** `BeginEncounter()` is idempotent and
  publishes `BossEncounterStartedEvent` once (the brazier calls it; otherwise first damage does).
- `TelegraphComponent` + `TelegraphRing` draw a model-independent ground ring for the reported
  wind-up, tinted by phase; both cues end early on `AttackInterruptedEvent`.
- Add waves spawn on phase entry, repeat on their interval under `MaxAlive`, and die with the boss
  through the damage path (loot and XP land). Spawn points are `Marker3D`s in group `boss_add_spawn`
  under the boss's parent; none → a ring. `ArenaHookComponent` reveals arena nodes by phase, scoped to
  bosses under its own arena, and resets on death.
- `BossEncounterDirector` resolves the **dead boss's** resource for intro/slow-mo, `RewardItemId`,
  `DefeatFlagId` and `DefeatDialogueId`; `BossDefeat.Resolve` makes reward, flag and dialogue one
  decision (a reward without a flag is rejected — it once re-offered the Iron King's ember on every
  dragon kill).
- **`BossSummonComponent`** (the brazier, an interactable) summons once and re-arms until the defeat
  flag is set. Exports: `BossTemplateId` (must build a `BossEntity`), `DefeatedFlagId`,
  `RequiredQuestId` and `RequiredFlagId` (both checked; empty = ungated; a missing quest log fails
  closed), `PromptKey` / `LockedPromptKey` (the locked prompt says why), `SpawnOffset`, **`FightId`**
  (a `boss.*` fight to run instead of the archetype's own `BossId`, set on the controller before
  `AddChild`) and **`ClosedFlagId`** (closes the challenge for good once set). The boss HUD banner
  names the boss actually summoned.
- **Yielding fights (Phase 47.5).** `BossResource.WithdrawHealthFraction` (valid `[0, 1)`, 0 = to the
  death; `BossPhases.ShouldWithdraw`/`WithdrawFractionValid`): at or below it the controller clears
  the adds, publishes **`BossWithdrewEvent`** and frees the body. The encounter director, boss frame,
  music and arena hooks end the fight on it exactly as on a death (defeat flag, reward, conversation),
  while kill credit, loot and the bestiary never see it. `--validate` fails a withdrawing boss with no
  `DefeatFlagId`. Phase one's speed bonuses apply at `BeginEncounter` (a duel opens slowed). The
  Ashen Knight's duels are `boss.ashen_knight_duel1`/`2` on his own archetype via `FightId`.
- **The seven Flamebearers** (`enemy.*`/`boss.*` ids in `playbook/finish.md`): the Iron King (Ember
  Crown arena), Storm Tyrant (Frostfang, Stormcrown), Beast Lord (Ashen Wilds plateau), Crimson Prophet
  (Sunspire, Crimson Mission), Hollow Queen (Pale Concord, Hollow Court), Ashen Knight (Celestial,
  Knight's Gate) and Morthul (Ash Throne, brazier gated on `flag.ashen_knight_defeated` via
  `RequiredFlagId`). Each defeat but Morthul's runs a `dialogue.<boss>_absorb` offer (absorb:
  `AddCorruption 25` + `flag.<boss>_absorbed`) and drops `item.relic.<x>_heart`. Closing that
  conversation plays the boss's vision (§2.11).

### 2.8 Items, loot and progression (`src/Items`, `src/Loot`, `src/Progression`)

**Templates and instances.**

- `ItemResource` (id, name, type, rarity, `MaxStack`, weight, value, icon, trade tags,
  **`WorldModelPath`**, one model for hand, ground and plinth) plus the catalogue fields `ItemLevel`,
  `Tier` (0 untiered, 1 to 6 by realm), `RequiredLevel`, `SetId` and `UniqueEffectId`, all
  absent-default. `ItemDatabase` maps ids.
- `EquippableItemResource` adds slot, flat `Bonus*` fields (armour, powers, health, stamina, mana,
  crit, move speed, all six school resistances) yielded by `StatBonuses()`, the two regeneration
  bonuses, `TwoHanded`, `WeaponClass`, `ArmorWeight` and an optional `Weapon`.
  `ConsumableItemResource` adds `Effect` (`ConsumableEffectKind`), `Magnitude`, `DurationSeconds`,
  the buff stat and kind, `CureStatusIds`, `CooldownSeconds` and `CooldownGroup`; the legacy
  `HealAmount` is still read when a Heal has no `Magnitude`.
- `ItemInstance`: rolled rarity, generated name, frozen affixes, and five additive fields saved only
  off their default: `Quality` (`CraftQuality`), `UpgradeLevel` (0 to `ItemUpgrades.MaxLevel`),
  `ItemLevel` (0 means the template's), `Locked`, `Junk`. `Value` and the template's flat bonuses are
  scaled by workmanship (`CraftQualities`, `ItemUpgrades`); rolled affixes never are. Only affix-less
  copies of equal workmanship and level stack (`CanStackWith`); marks are not compared. `ItemStack`
  holds an instance; inventory, equipment, pickups, UI and saves all flow instances.
- The numbers of generated gear are one budget: `ItemBudget` in `ItemValidator.Content.cs` mirrors
  `budget_points`, `weapon_damage`, `item_value` and `STAT_WEIGHT` in `tools/gen_items.py`, and
  `--validate` and `ItemCatalogueTests` recompute every written item against it.

**Inventory and equipment.**

- `InventoryComponent` (`ISaveable`): the pack (`Stacks`, limited by `Capacity`) and, when
  `UseMaterialBag` is set (the player only, in `PlayerFactory`), the uncapped `Materials` bag that
  takes every affix-less `ItemType.Material` (`IsBagItem`). `CountOf` / `Contains` / `RemoveItem`
  span both; `AllStacks` is for readers that must see everything. `AddInstance` clamps to capacity
  and returns what it stored; `AddOrOverflow` never loses an item; `CanAccept` / `SlotsNeededFor`
  answer room before a move. `SetLocked` / `SetJunk` are the only writers of the marks, and the
  lock is enforced at each action that would lose the item. `Load` clears both stores, ignores
  capacity and re-sorts every entry, which is also the bag migration in both directions.
- `Consume` asks `ConsumableEffectsComponent.Check` first (`ConsumeRefusal`: cooldown, already
  full, nothing to cure, level too low), removes the exact held unit, then `Apply`s: an instant or
  over-time restore, a buff as a runtime `StatusEffectResource` (`status.consumable.<leaf>`), or a
  cleanse. Cooldowns are keyed by `CooldownGroup` (else the item id), tick with the tree and are
  cleared on `GameLoadingEvent`.
- `EquipmentComponent` (`ISaveable`): `CanEquip` returns an `EquipRefusal` from `InventoryRules`
  (level, off hand against a two-handed main hand, no room for what comes off) and `Equip` applies
  only what it allowed. Bonuses are modifiers sourced to the instance. The Ammo slot holds a whole
  stack (`AmmoCount`, saved as `ammo_qty`); `ConsumeAmmo` refills from the pack and vacates the slot
  on the last arrow. `TakeEquipped` removes a worn piece without needing a pack slot (salvage and
  reforge use it).
- Derived state (`EquipmentComponent.Derived.cs`) is rebuilt by `RefreshDerived` after every
  change and at the end of `Load`, and is never saved: gear regeneration, `SetPieces`
  (`SetRules.CountPieces` over distinct worn pieces), set stat bonuses under one private source,
  and `ActiveUniqueEffectIds` (worn items' own plus active set thresholds').
- `UniqueEffectsComponent` rebuilds its active list on `EquipmentChangedEvent` and runs the ten
  `UniqueEffectKind`s from existing events (`DamageDealtEvent`, `EntityDamagedEvent`,
  `EntityDiedEvent`, `SpellHitEvent`, `ActionReleasedEvent`, `ItemPickedUpEvent`); the arithmetic is
  pure in `UniqueEffectRules`. Follow-up damage is guarded against re-entry. Nothing is saved.
- `HotbarComponent` (`ISaveable`, five slots) and `HotbarPanel` (cooldown sweep, the pad chord).

**Loot.**

- `LootTable` (`Entries`, gold, `QualityBonus`, `Tier`, `MinItemLevel` / `MaxItemLevel`,
  `DropsAsChest`) and `LootEntry` (item or nested `TablePath`, `Group` + `Weight`, `MinLevel` /
  `MaxLevel`, `MinRarity`, `OncePerSave`). `LootGenerator.Generate(table, LootContext)` walks the
  table: a table's own `Tier` overrides the context's, `{tier}` in a nested path resolves through
  `LootTiers.ResolvePath`, one row of a group is picked by weight, and nesting stops at depth 4.
- An equippable row with `RollAffixes` rolls rarity (`LootRarity.Roll` with table quality, perk
  luck and `PityRules.BonusQuality`), applies the pity guarantee and the row and template floors,
  picks an item level (`LootTiers.RollItemLevel`, clamped to the table's and the item tier's
  band), then affixes: `AffixDatabase.ApplicableTo(template, rarity, itemLevel)`, a signature
  affix first at Epic and above, no repeated stat or `Group`, values blended by quality and scaled
  by `AffixDefinition.ScaleForLevel`. `RollAffixed` is the same roll for crafting and shops.
- `LootComponent` rolls on death with `ContextFor(killer)`: the active region's realm tier, the
  player's level, the `LootQuality` perk and the `LootLedger`. A `DropsAsChest` table spawns a
  persistent `prop.cache` through `PersistentSpawnDirector` and arms its `ContainerLootComponent`
  with the table path; a withdrawing duel leaves none.
- `ContainerLootComponent` (`ISaveable`) rolls its table on first open with an RNG seeded from
  `LootSeeds.For(PersistentId, ledger.Salt)`, so the result does not change across reloads. It
  tracks the pickups it spilled and saves the uncollected ones (`spilled`), handing them back on
  the next press after a load.
- `LootLedger` (`ISaveable`, `loot_ledger`, owned by the player's `InteractionSensor`): the dry
  streak, once-per-save claims, the per-save salt (kept under 2^53 so it survives JSON) and the
  reward chest ordinal.
- `ItemPickupFactory` builds the pickup with a rarity beam; `ItemPickupComponent.IsAutoLoot` marks
  coin and plain materials for walk-over collection, except what the player dropped
  (`PlayerDropped`) and contraband. `AffixRegenBinding` turns regeneration affixes into regen
  rates on `EquipmentChangedEvent`.
- Kill attribution: `DamagePacket.Source` → `EntityDiedEvent.Killer`.

**Validation.** `ItemValidator` (`src/Debugging/ItemValidator*.cs`, called once from
`ContentValidator`) owns the item, set, unique-effect, affix, loot-table, recipe and reforge arms,
including obtainability (every generated equippable has a source) and stat budgets.

**Progression.** `ProgressionResource` (XP curve, max level, skill points, per-level gains),
`ExperienceComponent` (bounty), `ProgressionComponent` (`ISaveable`; growth recomputed from level,
never stored), `PerksComponent` (`ISaveable`, modifiers re-applied on load).

### 2.9 Quests (`src/Quests`)

- `QuestResource`: objectives, rewards (XP, gold, items, faction), `PrerequisiteQuestId`,
  `CompletionFlagId` (the world bridge — portals, map locations and actors re-derive from flags, so a
  completion never grows a second save store), `TimeLimitSeconds`, `SequentialObjectives`,
  `IsMainQuest`, `AllowsOneShotTarget`, and **`AutoStartFlagId`**.
- **`AutoStartFlagId`** starts (and tracks) the quest the moment that flag is set, and on
  `GameLoadedEvent` for any held flag whose quest is not yet in the log. This is how the main story
  chains act to act with no quest giver; `--validate` fails an auto-start flag nothing sets.
- `ObjectiveResource`: `Type` (Kill, Collect, Reach, Talk, Escort, Defend, Interact, Stealth),
  `TargetId`, `RequiredCount`, `LocationId`, branch gates `RequiredFlagId`/`ForbiddenFlagId` (inert
  when shut). `QuestProgress` stores one int per objective and refuses to complete on zero live ones.
- `QuestLogComponent` (`ISaveable`, on the player) is the one `Advance(type, targetId)` choke point.
  Kill credit accepts the player or a companion as killer. Stealth rides `EnemyStateChangedEvent`.
  `QuestStatus.Failed` is retakeable. Journal (`J`) splits Main/Side; the HUD tracks one quest.

### 2.10 Dialogue and story flags (`src/Dialogue`)

- `DialogueResource`: node graph (`DialogueNode` → `DialogueChoice` with `Goto`, one
  `DialogueCondition` + arg, one `DialogueEffect` + arg). Both enums are declarative and append-only.
- `DialogueSession` (plain object) filters `VisibleChoices()` and `Choose` applies the effect
  **before** resolving `Goto` — so `OpenShop`/`OpenService` hand over to their panel with no pause or
  mouse flicker, provided `Goto` is empty (validator-enforced).
- `StoryFlagsComponent` (`ISaveable`, on the player): named booleans, `Set`/`Clear`/`Has`, raises
  `StoryFlagChangedEvent`. Flags have no database: the validator checks readers against writers.
- `DialogueComponent` (interactable) opens `DialoguePanel` (modal).

### 2.11 Story, corruption and the ending (`src/Narrative`, `src/Corruption`, `src/UI`)

- `CorruptionComponent`: 0–100 meter, tiers, `EndingEligibility` via `CorruptionTiers.EligibilityOf`.
- **The main story** is four acts of `quest.main.*` quests chained by `AutoStartFlagId`, generated
  with the Archivist's reading and the throne choice by `tools/gen_main_story.py`: Act I (the slice,
  ending `flag.iron_king_defeated`) → `quest.main.gathering` → `quest.main.hidden` (on
  `flag.pale_concord_revealed`) → `quest.main.truth` (on `flag.hollow_queen_defeated`; the Archivist's
  conversation sets `flag.celestial_gate_open`) → `quest.main.celestial`.
- **`HiddenRealmReveal`** (session node): once `flag.storm_tyrant_defeated`,
  `flag.beast_lord_defeated` and `flag.crimson_prophet_defeated` are all held, sets
  `flag.pale_concord_revealed`; re-evaluated on every flag change, load and region change so an old
  save catches up. That flag is the Pale Concord's `UnlockFlagId`, so its door and map records appear
  with no further code. Pure rule: `ShouldReveal`.
- **`dialogue.ash_throne`** is a placed talkable (a player who walks away can return): Dawnfire needs
  `CorruptionBelow 60`, Lord of Embers `CorruptionAtLeast 40`, so below 40 only Dawnfire, 60+ only
  Embers, between either. Choices set `flag.ending_dawnfire` / `flag.ending_embers`.
- **`EndingSequence`** (a `NarrationSequence`, session node) reacts to an ending flag: the ending's
  cards, an epilogue card keyed to how many of `AbsorbFlags` are held, then credits; sets
  `flag.game_complete` (which also stops a reload replaying it). The player is then standing in the
  world; free roam continues. `OpeningSequence` and `ClosingSequence` are the other narration cards.
- **`VisionSequence`** (a `NarrationSequence`, session node): on `DialogueEndedEvent` for one of six
  absorb dialogues (`VisionSequence.Visions`) it sets `flag.vision.<name>` and plays
  `vision.<name>.1..3` once per save. Keyed on the conversation ending, never on the kill.
- **After the ending** the world answers through data: story-gated weather (§2.14) and NPC dialogue
  branches on `HasFlag flag.ending_*` (the Elder, Last Hearth's headwoman, the Saffra Wells wellkeeper).
- **`--story`** (`HeadlessStory`) raises each act's trigger flag in a real session and asserts the
  next quest started, the reveal happened only after Act II, the chain survives save/load, every
  Flamebearer template builds a boss, both duels withdraw and record their flag and conversation (and
  stay unset without a duel), every vision and ending card has locale text, each ending brings its sky
  (re-derived on load even from a save holding forbidden weather), and an ending flag plays the
  ending. It passes inside the exported build too.

### 2.12 World clock and schedules (`src/World`, `src/Npc`)

- `WorldClock` (`ISaveable` `worldclock`): 24 h day at `DayLengthSeconds` (180 s); publishes
  `TimeOfDayChangedEvent(Hour, DayPhase)` hourly; persists time and **`Day`**. `SetTimeOfDay` advances
  `Day` only for an hour ≥ 24 (so a rest from 20:00 to 08:00 asks for 32).
- `ScheduleResource` entries (`StartHour`, `Activity`, `Destination`); destinations are **cell-local**
  (`DestinationOf(entry, cellOrigin)`). `ScheduleComponent` walks the routine, flees nearby alerts and
  faces the player in dialogue. `--validate` fails a destination outside its cell.

### 2.13 Magic (`src/Magic`)

- `SpellResource`: school, Projectile/Area/Self/Cone/Ground/Barrier/Dash delivery and
  Instant/Charged/Channeled input mode. Data owns wind-up/recovery, interruption, guard/poise impact,
  piercing, charge-dependent status duration, placed telegraphs, barriers, health costs and status
  consumption. `SpellRouteValidator` rejects invalid numbers and unreachable learning declarations.
- `SpellcastingComponent` (`ISaveable`; known spells, ranks and prepared index): all releases use
  `CharacterActionComponent`'s clock. Channels hold that action and its animation at release, tick
  only after wind-up, and resume authored recovery on release. Interrupted wind-ups refund half the
  mana; silence, stagger and death cancel appropriately. Blink commits direction/distance and refunds
  a newly obstructed portion. Load replaces the spell list and cancels transient casts/cooldowns.
- `SpellProjectile` (pooled), `SpellGround`, `SpellBarrier` and `SpellResolver` share targeting,
  damage outcomes and deduplication. Projectile sweeps intercept a barrier's physical volume before
  ordinary geometry; its health and school interaction determine whether it ends. Sunfall breaks guard
  only on the direct central hit. School lifesteal uses actual health damage; Soul Tithe refunds only
  its own killing hit. Self support consumes named stacks before healing (Knit Bone).
- `StatusEffectsComponent` owns transient stacks, DoTs, wards, controls and immunity. Nested ticks,
  ward breaks and death spread each own an iteration snapshot. Refresh preserves tick cadence and
  stronger remaining duration. Death and `GameLoadingEvent` clear effects before saveables restore;
  status stun is separate from combat stagger, and Root/Stun cancel dodge movement and invulnerability.
- Every caster also cancels at death/pre-load events and rechecks controls at release/channel ticks.
  `DamageResult` captures actual health loss, lethal outcome and post-hit health fraction before
  synchronous respawn listeners run, so killing hits cannot re-afflict the revived player.
  `SpellLifetime` parents deliveries to the session/world scope and cancels them on pre-load or
  caster removal; active and pooled projectiles cannot return through a torn-down caster.
- `SchoolIdentity` and `SpellCombo` read pre-hit afflictions: ignite/detonation, chill/freeze/immunity,
  chain, actual-damage lifesteal, regrowth, ward/dispel and six combos. `SchoolMasteryComponent`
  saves points for six schools: five ranks grant power, cooldown trims at ranks 2/4 and resistance at
  rank 3; held channels bank at most one point per spell per second.
- **The fading Weave:** `RegionResource.WeavePotency` feeds the `Weave` static on world build and
  transition; `WeaveMath` weakens ordinary magic and strengthens corrupted magic as potency falls. No
  extra save state.
- `SpellLearning.TryLearn` is the shared tome/dialogue/trainer route (one learned/refused event).
  Spellbook purchase spends progression points and publishes once from the component; corrupt
  learning requires its tier and explicit embrace. `SpellAliases` migrates retired ids on restore.
  The magic probes cover core, status, learning/HUD, delivery lifetime and content, including real prepared tomes;
  probe drivers are excluded from shipping assemblies.

### 2.14 World systems: sky, weather, encounters, events (`src/World`)

- `SkyController` animates the sun and `Environment` from the clock, blends weather, applies the region
  atmosphere (`WorldEnvironmentProfileResource`: `SunTint`, `SunEnergyScale`, `HazeColor`, `HazeScale`,
  **`FixedSkyHour`** — ≥ 0 draws sky and sun at that hour while the clock, schedules and weather keep
  running; the Pale Concord's dusk). The dying-world base is set in
  `WorldSessionDirector.BuildEnvironment` plus `SkyController`'s palette constants (a haze floor).
  `RENDERING.md` covers quality tiers and effects. ⚠️ Palette alone cannot make a region a different
  place.
- `WeatherDirector` (`ISaveable` `weather`): weighted rolls, never the same twice, persisted id and
  remaining time. **Story-gated pool (Phase 44.5):** `WeatherResource.RequiredFlagId` (rollable only
  while set) and `ExcludedByFlagIds` (gone once any is set), pure rule `WeatherEligibility`. Derived,
  never saved: a newly set required flag forces its state at once; an ineligible current state is
  rolled away on a flag change, after a load and on a 2 s poll. `WeatherResource.SkyTint` multiplies
  sky, horizon, ambient and fog colour in `SkyController`. Dawnfire adds `weather.dawnfire` and
  excludes rain/storm/fog; Lord of Embers leaves only `weather.embers`.
- `EncounterDirector`: spawns groups around the player filtered by day phase, weather cadence, the
  active region and `RegionIds` (empty = anywhere), capped, cell-owned, not persisted, never inside a
  safe zone. **`RegionResource.ScopedContentOnly`** admits only encounters (and world events) naming
  that region.
- `WorldEventDirector`: one named event at a time (Raid/Cache/Hunt), time limit, rewards; cooldowns
  and compact active state persist under `world_events`; materialised actors are Near-cell owned.

### 2.15 Regions, streaming and travel (`src/World`, `src/Bootstrap`)

**Offline bake.** `tools/world_bake.py --bake` evaluates each region's spec and resources, conforms
authored nodes, builds terrain, collision, scatter/HLOD, navigation and traversal links, and writes one
packed scene per cell plus a `WorldPreparedRegionResource` and backdrop. `data/world_bake/manifest.json`
fingerprints inputs and hashes outputs; `--check` names any drift and is a required gate. Gameplay has
no live-generation fallback: missing data fails the region and the loading gate refuses activation.

**Streaming.** `RegionStreamer` picks a predictive tier per cell from position and velocity: **Near**
(gameplay, collision, navigation), **Mid** (visuals and terrain collision, simulation asleep), **Far**
(static visuals/HLOD), **Backdrop** (terrain/landmark continuity); beyond, unload. A two-second
projection preloads ahead, hysteresis prevents thrash, activation is staged under
`ActivationBudgetMilliseconds`, loads go queued → `LoadThreadedRequest` → resident → tier activation
with `MaxConcurrentLoadRequests` (drops to one under memory pressure). Only one region is active; a
realm transition is a session-world boundary. `RequirePosition` pins a landing Near; `IsPositionReady`
waits for real collision. `LoadingCoordinator` + `SafePlacementService` (ground, slope, capsule
clearance, optional nav) gate player loading and actor materialization. Exhausted player placement
aborts to title. Hazard recovery retains a last-resort analytic landing; its pending-placement
boundary is recorded in `RUNTIME_AUDIT.md`. Cell loaded/unloaded events describe
gameplay ownership.

- `RegionResource`: `Id`, `DisplayName`, `Realm`, `SpawnPoint` (Y is clearance), `Cells`, `Bounds`,
  weather bias, environment/performance/generation profiles, `WeavePotency`, `Neighbours`,
  `UnlockFlagId` (the region's door is hidden until set; declared on the destination), `SafeZone*`,
  `ScopedContentOnly`, and the toll fields.
- **Doors.** `RegionSetup` spawns one `RegionTransitionComponent` per neighbour. **`PortalPointFor`**
  reads `NeighbourPortalPoints[i]` (parallel to `Neighbours`) and falls back to `PortalPoint`, then to
  a point in front of `SpawnPoint`; **`ArrivalPointFrom`** reads `NeighbourArrivalPoints[i]` and falls
  back to `SpawnPoint`, so walking back through a crossing lands at that crossing. The validator
  rejects more door points than neighbours. The Ember Crown has a door per neighbour (Crown Pass,
  Ashen Breach, Southmarch Gate); the Sunspire library forecourt holds the story doors to the Pale
  Concord and the Celestial Realm.
- **Transitions.** `WorldSessionDirector.OnRegionTransitionRequested` (portal and `region goto`
  converge here) pays the toll, enters `GameState.Loading`, re-targets the streamer (`UnloadAll` +
  `Configure`, which records `ActiveRegionId`), lands the player, rebuilds portals and safe zones,
  requests a boundary autosave, and holds Loading until safe placement succeeds.
- **Tolls.** `TollGold`, `TollPermitFlagId`, `TollPassFlagId` (consumed), and **`TollFromRegionIds`**
  (empty = charge arrivals from every neighbour) on the destination; `TollFrom(fromRegionId)` is the
  owed amount; `RegionSetup.PayToll` charges, `TollFee.Resolve` decides, and the portal prompt quotes
  the same function. Fast travel is never tolled.
- **Safe zones** are a list: the region bubble plus each cell's `SafeRadius`; `SafeZones.Set`
  replaces, then per-cell adds.
- `CellPersistenceDirector` (`ISaveable` `cell_persistence`): culls removed `PersistentId`s and
  re-applies snapshots when a cell loads; removal detected by `TreeExiting` (suppressed during unload).
- `MapService` (`ISaveable` `map`): region and place discovery; `MapLocationResource` +
  `MapLocationComponent` (position = the component's node); `MapDiscoveryRules` (walk-up, sight, quest,
  flag); baked relief map; minimap; compass (`CompassMath`, `ObjectiveLocator`).
- `FastTravelService` (`ISaveable`): attuned `TravelNodeComponent`s; landings from the bake; a jump
  costs `TravelCosts.FeeFor` (free to an owned holding), charged in
  `WorldSessionDirector.OnFastTravelRequested`.
- Cells live at `scenes/regions/<region>/<cell>.tscn`, built at local origin. Navmesh bakes from
  **static colliders** only (mesh parsing forces a GPU readback); agents straight-line until a bake lands.

### 2.16 Terrain, water and world QA (`src/World`)

One `WorldHeightfield` per region, from one staged generator (continentalness, mountains, erosion
and valleys, relief, detail, a cached D8 drainage solve) and one `WorldGenerationProfileResource` per
realm, with authored landforms, roads and yards stamped over it. Authored circulation calms the
generator around itself; the continental tilt is not calmed. Macro fields are cached; cell vertices,
normals and collision build on worker threads under an epoch stamp.

- **Terrain material:** `WorldTerrainLayerResource` (a substance) and `WorldBiomeProfileResource` (six
  slots: Ground, Sparse, Rock, Cap, Road, Shore); `world_surface.gdshader` blends them; ecotones from
  generated moisture/wetness/alpine weight. ⚠️ **No ground textures** (`ART_STYLE.md`); every shader
  frequency needs a distance fade.
- `WorldLandformResource.Irregularity` warps natural boundaries only (early-out outside the band).
  `WorldRegionBackdrop` continues the field to the horizon.
- **Water:** `WorldWaterResource` declares a body; `WorldWater` owns the non-swimming contract (wade
  under 1.1 m, refuse above, `WorldRecovery` retrieves above 1.9 m); `WorldCellWater` draws the
  surface from the heightfield. Physics layer `Water` never blocks. A mesh in a `.tscn` is invisible to
  all of it.
- `WorldRecovery` returns the player from deep water or an exitless pit. `WorldTraversalAnalysis`
  sweeps a 3 m directed graph; `--validate` fails shallow traps.
- Scatter layers declare `MaxSlope`, `HeightRange`, `Clumping`, `Saturation`, climate bands; the HLOD
  proxy is the source mesh at reduced density.
- `docs/WORLD_AUTHORING.md` is the authority; `tools/world_quality_check.py` orchestrates the gates.

### 2.17 Crafting (`src/Crafting`)

- `CraftingRecipeResource`: `Station` (`CraftingStationType`: Hand, Forge, Workbench, Alchemy),
  `Ingredients`, output id and quantity, `OutputRarity`, `Tier` (1 to 6) and `ScrollItemId`. Its
  player-facing name is the locale row `<id>.name`. 67 of the 82 are generated from
  `catalogue.PLANNED_RECIPES` by `tools/gen_recipes.py`, which also writes the trainer's
  `TaughtRecipeIds` and checks `GameIds.Recipes.Starting`.
- `CraftingComponent` (`ISaveable`): known recipes (seeded from `GameIds.Recipes.Starting` on init
  and again on every `Load`; `Learn` from a trainer, `StudyScroll` from a recipe scroll). `CanMake`
  is knowledge, station (`StationAccepts`: hand recipes craft anywhere) and a real output;
  `CanCraft` adds the skill rank (`CraftingSkill.RequiredRank(Tier)`) and the ingredients, counted
  across pack and material bag. `Craft(recipe, station, count)` loops single crafts up to
  `MaxBulk`. A craft removes the inputs, adds the output, and rolls everything back when the
  output does not fit.
- **Skill and workmanship.** `CraftingSkill` is pure: XP to rank (0 to 10), XP per craft, the
  workmanship odds from mastery (rank above the recipe's requirement) and `AffixLuck`. Unstackable
  gear gets a `CraftQuality` from `CraftingSkill.Roll(rank, tier, crafts, recipeId)`, a
  `StableRoll` over the saved craft serial, so a quickload replays the outcome. Output with a
  rarity above Common goes through `LootGenerator.RollAffixed` at the template's item level.
- **Salvage.** `PlanSalvage` is the one computation of a salvage's yield (the window previews it,
  `Deconstruct` pays it): the recipe's ingredients at `Deconstruction.RecoveredQuantity` when the
  open station is the recipe's own, generic scrap otherwise, plus XP. A recipe that makes several
  at once is never reversed. Worn gear comes off through `EquipmentComponent.TakeEquipped`; a
  salvage whose materials do not fit is refused and the item put back. `SalvageAllJunk` runs each
  junk-marked piece through the same path.
- **Reforging** (Forge only). `RerollQuote` / `UpgradeQuote` / `PromoteQuote` return a
  `ReforgeQuote` (gold, the tier's ingot from `ReforgeRules.MaterialFor`, a block reason key, bill
  lines); `RerollAffix`, `Upgrade` and `Promote` do nothing a quote did not allow. Prices are pure
  in `ReforgeRules`: a reroll leaves the value unchanged and climbs with the item's prior rerolls
  (a ledger of `{fp, n}` rows keyed by `Fingerprint`, newest 128); an upgrade or promotion costs a
  base fee plus `GainMultiple` times the value it adds, and `ItemValidator.Crafting` fails
  `--validate` if any row is `Exploitable`. The replacement affix comes from the loot generator's
  pool at the item's level, less the stats and groups that stay, and the RNG is seeded from the
  fingerprint and the saved reforge serial.
- **Commission.** `Commission` supplies the missing ingredients, then crafts with
  `commissioned: true`: no skill gate, Common rarity, Standard workmanship, no skill XP. The price
  rules are `CommissionRules` (`src/Economy`).
- `MaterialSaving` hands one unit of the largest ingredient back on a derived roll.
- `CraftingStationComponent` opens the modal `CraftingPanel` (craft, salvage and reforge views,
  search, filters, quantity picker, one pinned recipe saved as `pinned`).
- Saved keys: `known`, `crafts`, and the additive `skill_xp`, `reforges`, `rerolls`, `pinned`.

### 2.18 Factions and guilds (`src/Factions`)

`FactionResource` (default reputation, `HostileThreshold` tier, kill penalty, enemies/allies, and for
guilds ranks plus `HubLocationId` and a four-role roster). `ReputationComponent` (`ISaveable`) shifts
standing on kills and propagates through the web; `ReputationTiers.Of`. `FactionComponent` tags actors
(not persisted). **A guild is a faction with ranks:** `GuildRules` derives every `guild.<slug>.*` flag
and resolves state in one ordered function; `StoryFlagsComponent` is the only writer; dialogue uses
`GuildRankAtLeast` / `GuildNotMember` / `GuildCanJoin` and effects `JoinGuild` / `GuildRank`.

### 2.19 Companions (`src/Companions`)

`CompanionResource` → `CompanionFactory` (team 0, `PersistentId` = companion id).
`CompanionAIComponent` (`ISaveable`): anchor/leash loop via pure `CompanionDecision`
(Hold/Regroup/Chase/Attack); **the leash beats the fight**; the player's lock-on target wins; the
proximity scan is gated by the player's standing; downed, never lost. Orders (`CompanionStance`
Follow/Hold/Engage). `CompanionRoster` (`ISaveable` `companions`): recruit/dismiss/stance, loyalty for
every companion ever recorded, load is a **reconcile** (`CompanionPartyReconcile`), `RegroupNow` after
hard loads. `CompanionLoyaltyComponent` is a projection. `CompanionRecruiterComponent` hides the world
NPC (visibility and collision) while recruited. A load suppresses announcement events.

### 2.20 Housing (`src/Housing`)

`PropertyResource` + `HousingService` (`ISaveable`). `PropertyDeedComponent` and pure
`PropertyClaim.Resolve` (owned → quest-locked → too expensive), read by prompt and interaction.
Claiming registers a fast-travel node at the player's position. Stashes and trophy stands are
inventories keyed by `PersistentId`; placed props ride `PersistentSpawnDirector` with
`place.<propertyId>#<n>` ids from `PlacementIds.Next`.

### 2.21 Economy (`src/Economy`)

`DESIGN.md` §6 owns intent; this is mechanism.

**One price authority.** `ShopPricing` is pure (plain values; the test project cannot construct Godot
objects) and spreads over `ItemInstance.Value`. `BuyPrice` rounds up, floors at 1, markup clamped
`>= 1`; `SellPrice` rounds down, floors at 0, fraction clamped `0..1` — so `sell <= value <= buy` for
any authored spread. ⚠️ The clamps do not stop a free round trip; `ValidateShopTrade`'s margin rule does.

⚠️ **The multiplication order is load-bearing:**

```
MarkupFor(markup, tier, specialty, haggled)   = markup   × PriceMultiplierFor(tier)
                                                         × SpecialtyBuyDiscount(0.95)
                                                         × HaggleRules.BuyFactor(0.90)
SellFractionFor(fraction, specialty, haggled) = fraction × SpecialtySellBonus(1.25)
                                                         × HaggleRules.SellFactor(1.10)
```

`PriceBreakdown` re-runs the price after each factor in exactly this order so its last line equals the
charge (`BuyLastLineIsTheTotal`). Standing is absent from the sell side on purpose; a haggle may move
it (bounded to one merchant per day). `PriceBreakdown` **is** the charge path: `VendorPanel`,
`CraftingPanel` and `MapScreen` display and charge its `Total`; `PriceBreakdown.AllKeys` is the
declared locale contract.

- **Local value** (`RegionDemand.ValueAt`): a cell's `Surplus` tags price at 0.62, `Demand` at 1.50.
  It moves the value, not the spread, so both sides of a counter stay symmetric; only this lets a
  carry between settlements pay. `ShopResource.LocalValue` is the call every price goes through;
  `LocalQuote` explains it; `PriceView` (Today/Peak/Trough) lets the validator test other days.
  ⚠️ Empty `ShopResource.CellId` prices at par.
- **Supply shocks** (`SupplyShockRules.Apply`) move a tag between a cell's lists for a bounded time —
  a list edit, not a multiplier, so every existing clamp and sweep covers it.

| Node (`ISaveable`, built in `GameSession.Build`) | Holds | Derives |
| --- | --- | --- |
| `ShopStockService` | stock, purses, absorption, investment rungs | restock due-ness from the day |
| `ContrabandImpound` | what the wardens took | the fine (`ContrabandLaw`) |
| `ConsignmentLedger` | listings and their day | whether one sold |
| `ContractLedger` | only what the player filled | the board rotation |
| `WagerLedger` | only throws spent today | win/loss from (day, throw) |
| `HaggleLedger` | only that the player asked | the answer from (day, shop) |
| `SupplyShockService` | the active window and what the player hauled in | the roll from (day, cell) |

⚠️ **Derive, then bound.** Anything a quickload could reroll is a pure function of the day; a ledger
stores only what the player *did*, never the offer or the answer. ⚠️ `string.GetHashCode()` is
randomised per process — `StableRoll` is a hand-written FNV-1a, pinned by cross-process tests.

**Services:** `ServiceResource` + `ServiceKind` + one `ServiceComponent`; `ServiceRules` pure,
`ShopPricing.ServicePrice` the price. Commission and hire are charged after their verb (only commission
rolls back). Any kind but Bank can be opened from dialogue (`OpenService`). A world prompt is not a
`Control`, so a standing-moved price is stated inline.

**Validation:** ~110 economy refusals in `ContentValidator`; per-rule negative tests in
`tools/negative_tests.py` (42 cases). `--economy` prints the arbitrage table (an observation).

### 2.22 Save (`src/Save`)

[`SAVE_FORMAT.md`](SAVE_FORMAT.md) is the contract (layout, `SaveId` rules, what is not saved,
failure policy, migrations).

- `ISaveable` (`SaveId`, `Save()` / `Load(dict)` with a Godot `Dictionary`). `SaveManager` writes
  `user://saves/<slot>/save.json` in a versioned envelope (format 4; legacy flat files remain
  readable) with a `sha256:` checksum over `objects`. Failed captures refuse the save, and failed
  restores refuse the load; load warns about orphaned entries and unclaimed saveables.
- **The decisions are pure and Godot-free**, so xUnit runs them: `SaveEnvelope.Read` validates a
  document's text without applying it (parse, shape, version, checksum) and is what both `LoadGame`
  and `InspectSlot` ask; `SaveChecksum` is the canonical hash; `SaveMigrations` is the v1 to v4
  chain with world data injected through `SaveMigrationLookups`; `SaveBackup` decides when the
  outgoing file is kept as `save.json.bak` and when a load reads it; `SaveSlots` / `SaveKind` /
  `SaveHealth` name the rosters; `SaveSlotPolicy` decides where a player save goes and what F9
  loads; `SetPieceSaveIds` builds the stable set-piece key; `SaveRead` is the tolerant reader every
  `Load` should use.
- **Writing.** `SaveGameCore` captures every saveable, serializes once, computes the checksum
  from that text and commits. In gates and tooling (`SaveWriteQueue.RunsInline`: headless, a
  redirected user dir, any user argument) `AtomicWrite` stages to `.tmp`, rotates a sound previous
  file to `.bak` and renames. In windowed play the same commit (`SaveFiles.Commit`) runs on
  `SaveWriteQueue`, one job at a time on a `SaveWriteWorker`; `SaveGame` returns once the snapshot
  is taken and `FinishSave` publishes `GameSavedEvent` or `SaveFailedEvent` when the disk answers.
  Every read of the slots, a session teardown and a window close `Flush` the queue first.
- **Blocks and events.** `PushSaveBlock(reasonKey)` returns a token; while any is live every save
  is refused (`BossController` holds one from the first blow, `DialoguePanel` for a conversation).
  `CanSaveNow` is what a menu asks. `SaveStartedEvent`, `GameSavedEvent` and `SaveFailedEvent`
  drive `SaveIndicator` and the toasts.
- **Slots and the player.** `SessionLifecycleCoordinator.TrySave` is the player's save (never into
  the autosave ring; a manual save makes its slot the session's `ActiveSlot`);
  `AutosaveBeforeQuit` writes the ring and flushes before the session is left. `InspectSlot`
  validates a slot the way a load would and reports `SaveHealth`, whether the backup will be read,
  and the header a load would use; `SaveSlotPanel` shows it.
- **Autosave.** `AutosaveCadence` is the pure clock (interval, debounce, event delay, deferral
  with a soft cap, ring index); `AutosaveService` feeds it play time and events, asks `CanSaveNow`
  and whether the player is busy (fought in the last few seconds, enemies engaged nearby, off the
  floor), and writes `auto1..auto3`. `SaveThumbnailService` caches the last frame of play when the
  state leaves `Playing` and encodes the 320x180 PNG on the write queue.
- **Identity:** `EntityComponent.SaveKey(prefix)` prefers `PersistentId` (the player is `player`).
  Components call **`RegisterSaveable()`** in `OnInitialize`, which registers only when the owner has a
  stable id (`SaveKeyPolicy`); world services register in `_EnterTree` with fixed keys. `savecheck` (F1)
  should report 0 volatile ids. Empty or duplicate `SaveId`s refuse the save and preserve progress.
- Benign warnings: "no usable entry for `<id>`" from a save older than the saveable.
- `PersistentSpawnDirector` (`spawns`) recreates tracked spawned actors from a manifest via
  `PersistentActorRegistry`; recreated components restore through the in-flight-load hook. A
  template the build no longer registers is skipped with a warning. Ambient mobs and loot stay
  transient, except what a chest spilled (§2.8). `CellPersistenceDirector` (`cell_persistence`)
  keeps a per-cell actor list and holds the state of streamed-out cells.

### 2.23 Audio (`src/Audio`)

`AudioBusLayout.Ensure()` creates Master/Music/SFX/Ambience/UI/Voice before settings apply; volumes
belong to `SettingsService`. `AudioCueRouting` maps a cue id to bus and positional flag by prefix.
`AudioLibrary` registers CC0 assets or `ProceduralAudio` placeholders (unknown id = silence, warn once).
`AudioDirector` plays pooled players off `SoundCueRequestedEvent`/`MusicCueRequestedEvent` or
`PlayCue`. `MusicDirector` + `MusicStateMachine` (Boss > Combat > Safe > Explore, crossfaded);
`AmbienceDirector` (weather > town > day/night); `FootstepComponent` reads a floor's `surface` metadata.

### 2.24 Composition roots and UI (`src/Bootstrap`, `src/UI`)

`scenes/Main.tscn` is one node with `ApplicationRoot`; everything else is built in C#.

| Type | Owns |
| --- | --- |
| `ApplicationRoot` | the process: CLI report modes (`--validate`, `--lifecycle`, `--story`, `--state`, `--economy`, `--worldgen`, `--world-bake`, `--worldmap`), input, localization, databases, audio buses, settings |
| `GameShellController` | the title screen and the flags that drive a session for a tool (`--play`, the shot harnesses) |
| `SessionLifecycleCoordinator` | New Game, Load, `DestroySession`, abort-to-title, the static reset list |
| `GameSession` | one playthrough: its scope and the ordered build of everything in it |
| `WorldHost` / `WorldSessionDirector` / `RegionSetup` | environment, weather, sky, streamer, encounters, portals, transitions, tolls, safe zones, fast travel |
| `UICompositionRoot` | HUD and panels, bound to the player's components |
| `PlayerHost` | spawn, respawn, persistent world actors |
| `LoadingCoordinator` | the gate every route into the world passes |
| `DeveloperToolsHost` | console, debug HUD, profiler, integrity checker, dummy, cheats |
| `SaveHeaderComposer` | the seams between `SaveManager` and gameplay |

⚠️ **`GameSession.Build()` is one ordered list on purpose**: clock before NPCs, weather before sky,
audio director before music director, cell persistence before the streamer, player before the
tutorial. ⚠️ **Quitting to the title does not reload the scene**; a second New Game runs in the same
process.

```text
ApplicationRoot                  (Application scope)
├── SessionHost                  SessionLifecycleCoordinator
│   └── GameSession              [Session scope]
│       ├── WorldHost            [World scope] — WorldDirector and world services
│       ├── UIRoot               GameHud + panels
│       ├── PlayerHost
│       ├── Loading
│       ├── DeveloperTools       [dev builds only]
│       └── session services     clock, autosave, ledgers, map, companions, reveal, ending
└── Shell                        GameShellController → MainMenu
```

**UI:** `GameHud` (vitals, spell, quest tracker, time/weather, event banner, nameplate, prompt,
crosshair, minimap, compass, party, boss bar), `PauseMenu`, `Notifications`/`Toast`, the panels, and
`UiTheme` + `UiPanel` for everything. The UI observes and sends intents; it is never the authority and
never reaches into the registry for gameplay. `UI_STYLE.md` is the visual language.

### 2.25 Debugging tools and the tooling boundary (`src/Debugging`)

- `DevConsole` (`F1`) with `DevCommands.RegisterAll` (commands go through the real choke points);
  `Invariant` + `WorldIntegrityChecker` (timer; subtracts pooled nodes from the orphan count);
  `ContentValidator` (`validate`, `validate-all`, `--validate`); `ProfilerOverlay` (`F4`);
  `ReproHarness` (seeded command replays); the analytics sink (dev builds only, a log, not state).
- `DeveloperToolsHost` is constructed only in dev builds (`BuildProfile`; `--capture` or an export
  hides dev tools and sandbox props). Quick save/load stay in every build.
- ⚠️ **Godot compiles every `.cs` into ONE assembly**, so the separation is per configuration:
  `EmbervaleTooling` (false under `ExportRelease`) excludes `addons/godot_mcp/**` and its NuGet
  packages, `src/Debugging/*Shots.cs`, `ShotHarness`, `ReproHarness`, and the `CS0618` suppression.
  `TreatWarningsAsErrors` is always on. `ContentValidator`, `Invariant`, the console and overlays stay
  (runtime-gated). `tools/check_shipping_assembly.py` scans the `ExportRelease` assembly.
- **Windows export** (2026-09-28): `export_presets.cfg` ("Windows Desktop", tracked, no credentials)
  excludes `tools/`, `tests/`, `docs/`, `reports/`, `artifacts/` and `include_filter`s
  `assets/models/manifest.json` and `data/world_bake/manifest.json` (non-resource files the game
  reads). `data/locale/strings.csv.import` uses `importer="keep"` so the raw CSV ships and `Loc` reads
  it directly (a translation import silently dropped it: raw keys on screen). `ApplicationRoot` runs
  the boot-time `ContentValidator` only when `OS.IsDebugBuild()` — several arms read `.tscn` text,
  which an export ships binary. `--story` runs inside the export as its smoke test.

**Layer rules** (dependencies point down, never in a cycle): Application (no session/world/player
knowledge) → Session (never outlives quit-to-title) → World (never outlives its session) →
Entity/component (reaches past its entity only through a scope) → Presentation (never decides
gameplay) → UI (never the authority) → Developer tooling (unreachable from shipping gameplay). `Core`
may not depend on `Debugging`; that is why `Invariant` lives in `Core.Diagnostics`.

---

## 3. Collision layers and teams

`CombatLayers` is the matrix: `WorldStatic`, `WorldDynamic`, `Player`, `Enemy`, `NPC`, `Projectile`,
`Hitbox`, `Hurtbox`, `Interaction`, `CameraBlocker`, `NavigationObstacle`, `Water`, `Trigger`,
`Ragdoll` (`World`/`Body` are legacy aliases). The camera reads only `WorldStatic | CameraBlocker`;
sensor layers never take part in motion. `WorldPhysicsContract` audits prepared cells (terrain on
`WorldStatic`, non-physical hit/hurt volumes, well-formed triggers, camera blockers, hitboxes querying
hurtboxes) in validation and at residency. **Teams:** 0 player (and companions), 1 hostile, 2 neutral
target; a hitbox never hits its owner or its own team.

## 4. Content and data pipeline

Content is `.tres` data for `[GlobalClass]` resources:

```
[gd_resource type="Resource" script_class="AttributeSet" load_steps=2 format=3 uid="uid://..."]
[ext_resource type="Script" path="res://src/Stats/AttributeSet.cs" id="1_attrset"]
[resource]
script = ExtResource("1_attrset")
Health = 100.0
```

- Load a project C# resource with **`ResidentResources.Load<T>`** (held for the process; a bare
  `GD.Load` risks the finalizer FATAL, and `ResidentResourceTests` fails it) and always provide a
  fallback — a missing resource must never crash boot. Engine types are exempt.
- **Enums export as ints and are append-only**; each guarded enum carries `// APPEND ONLY` and
  `EnumStabilityTests` pins ordinals. Reordering silently re-maps data (a Rare item becomes Epic).
- Ids referenced from code live in `GameIds` (`src/Core/GameIds.cs`); the validator flags drift.
- Some content is generated: regions (`gen_regions.py`), map locations (`gen_map_locations.py`), the
  main story (`gen_main_story.py`), prepared cells (`world_bake.py`), and the item catalogue: items,
  sets, unique effects and shop gear (`gen_items.py`), recipes (`gen_recipes.py`), affixes and loot
  tables (`items/gen_loot.py`), all from `tools/items/catalogue.py`, each with a `--check` gate.

### 4.1 Cross-reference validation

`ContentValidator` resolves every cross-reference at boot (debug builds only), via `validate`, and headless with
`--validate` (exit 0/1), feeding the `Invariant` counter. It covers item, quest, enemy template,
faction, region, status-effect, shop, service, recipe, property, dialogue-node and locale references
across loot tables, recipes, quests, dialogue, spells, factions, encounters, world events, shops,
services and properties; map-location and schedule seams; region geometry, routes, traps, pads and
doors; boss, bestiary and economy rules. **Story flags have no database**: a flag read (condition,
`UnlockFlagId`, `AutoStartFlagId`, gate) that nothing writes is an error; a flag written and never read
is legal; a typo in `SetFlag` itself still slips through. Code-set flags (the reveal, the ending) are
registered as writers in the validator.

## 5. Data flow at a glance

```
Input ─▶ PlayerInputRouter ─▶ CharacterActionComponent ─▶ Hitbox (ActionReleasedEvent opens it)
                                                           │ physics overlap
                                               CombatComponent.ReceiveDamage
                                                           │ block → mitigation
                                               StatsComponent.ApplyDamage
                                                           ├─▶ EntityDamagedEvent
                                                           └─▶ EntityDiedEvent
                                                                    │
        HUD · quests · loot · progression · factions · bestiary ◀───┘  (independent subscribers)
```

Publishers never know who listens; a new reaction is a new subscriber, not an edit to combat code.
