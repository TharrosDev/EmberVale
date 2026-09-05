# Embervale — Architecture & Systems Reference

> **~23k tokens — read one section, not the file.** `grep -n "^## " docs/ARCHITECTURE.md`
> gives you the map; open only the § for the system you are changing.
> [`RECIPES.md`](RECIPES.md) is the actionable companion — that one is how to *add* content,
> this one is how the machinery *works*.

The deep reference for how Embervale is built: the core architecture, every
gameplay system that exists today, the collision/team model, and the
content/data pipeline. For the working agreement, coding conventions, gotchas,
step-by-step recipes and the development workflow, see
[`../CLAUDE.md`](../CLAUDE.md); for the phase plan see
[`PRODUCTION_ROADMAP.md`](PRODUCTION_ROADMAP.md).

> **One-line summary:** an original hybrid first/third-person, open-world fantasy action RPG (the player swaps views at any time) in
> **Godot 4.7** with **C# (.NET 8)**, built on a component-based, event-driven,
> resource-driven architecture, and kept buildable and playable at every commit.

---

## 1. Architecture overview

Three small ideas carry everything:

### 1.1 Four lifetimes, and who owns each

**This is the load-bearing idea in the codebase.** Everything else — the composition
roots, the service registry, the scene tree — is a consequence of it.

| Lifetime | Owner node | Created | Destroyed | Holds |
| --- | --- | --- | --- | --- |
| **Application** | `ApplicationRoot` (`Main.tscn` root) | process start | process exit | settings, input actions, localization, the content databases, save-file IO |
| **Session** | `GameSession` | New Game / Load | quit to title, failed load | clock, autosave, the six economy ledgers, map discovery, companions, housing, bestiary, persistence directors, audio, the UI, the player |
| **World** | `WorldHost` (child of the session) | with the session's world | before the session | region streamer, weather, sky, encounter and world-event directors, portals, safe zones, the Weave |
| **Entity** | the entity node itself | spawn | `QueueFree` | components, via `EntityComponent.OnInitialize`/`OnTeardown` |

⚠️ **Which lifetime a service gets is decided by WHERE IT IS PARENTED, and by nothing
else.** `ServiceScope.For(node)` walks a node's ancestors to the nearest
`IServiceScopeHost`, so moving a service to a different host in the tree is the whole
of changing its lifetime. There is no second declaration to keep in sync.

The lifecycle runs, and repeats, without a scene reload:

```text
application start → application services → GameSession created → session services
  → world loaded → world services → gameplay
  → world destroyed → session destroyed → back to the title, ready for another
```

`SessionLifecycleCoordinator.DestroySession()` removes the session node
**synchronously**, so every `_ExitTree` beneath it has run before it returns, then
resets the five process-lifetime statics (`SafeZones`, `Weave`,
`PersistentActorRegistry`, `UiState`, `Invariant`). `SessionResetTests` finds those
five by reflection rather than trusting the list, so a sixth one fails a test rather
than leaking into the next playthrough.

**The gate on all of it is `godot --headless -- --lifecycle`**: three New Game →
Playing → save → destroy → Load → Playing → destroy round trips, asserting after
every teardown that no session, service registration, event subscription, `ISaveable`
or unfreed node survives. Exit 1 on any failure.

### 1.1b Services: scoped, not global

`ServiceScope` (`src/Core/Services/ServiceScope.cs`) holds one lifetime's services.
`ServiceLocator` is the **read** side only — `TryGet<T>` walks World → Session →
Application, so an inner scope shadows an outer one.

```csharp
// A node service registers itself into whatever scope owns it, and the registration
// goes when the node does — no _ExitTree line to forget.
public override void _EnterTree() => ServiceScope.RegisterOwned(this, this);
```

Before the 2026-09-03 overhaul this was one process-wide dictionary that outlived
everything in it. Services had to remember to unregister, eleven call sites forgot
`IsInstanceValid`, and the locator ended up silently dropping freed registrants to
stay upright. **A stale registration now has nowhere to survive**: disposing a scope
removes exactly its own entries, and a freed service found in a live scope is an
`Invariant` violation rather than something absorbed quietly.

**Prefer an explicit reference.** A composition root that builds a service hands it
to what it builds; a component reaches its siblings through `Entity.GetComponent`.
The locator is for the genuinely late-bound case — an actor the world spawned asking
the session a question — and its call count is a health metric, not a budget.

### 1.1c Autoloads

Four, in `project.godot` `[autoload]`, in this order (later ones may use earlier):

| Autoload         | File                               | Responsibility                          |
| ---------------- | ---------------------------------- | --------------------------------------- |
| `EventBus`       | `src/Core/Events/EventBus.cs`      | Typed publish/subscribe message hub.    |
| `ServiceLocator` | `src/Core/Services/ServiceLocator.cs` | Resolves across the open scopes.     |
| `GameManager`    | `src/Core/GameManager.cs`          | Owns the `GameState` machine.           |
| `SaveManager`    | `src/Save/SaveManager.cs`          | Serializes `ISaveable`s to `user://`.   |

They outlive every scope, which is why they are autoloads and the scopes are not.
Each exposes a static `Instance` (set in `_EnterTree`). They never reference
gameplay-specific types, so they stay stable as content grows.

`Log` (`src/Core/Diagnostics/Log.cs`), `Invariant`
(`src/Core/Diagnostics/Invariant.cs`) and `GameInput` (`src/Core/GameInput.cs`) are
**static classes, not autoloads**. Use `Log.Info/Warn/Error`, never raw `GD.Print`.

### 1.2 EventBus — typed pub/sub (prefer over Godot signals)

`EventBus` dispatches arbitrary `IGameEvent` payloads, so new event types appear
anywhere without editing a central file. Publishers never know who listens.

```csharp
EventBus.Instance.Subscribe<EntityDiedEvent>(OnEntityDied);
EventBus.Instance.Publish(new EntityDiedEvent(entity));
// always pair:
EventBus.Instance.Unsubscribe<EntityDiedEvent>(OnEntityDied);
```

- Events are **immutable, past-tense `readonly record struct`s** implementing
  `IGameEvent`. They describe something that already happened, never a command.
- **Always unsubscribe** in `OnTeardown`/`_ExitTree` — handlers hold references
  and keep freed objects alive otherwise.
- `Publish` snapshots the handler list, so subscribing/unsubscribing during
  dispatch is safe; handler exceptions are caught and logged.

Event catalogue: `Core/Events/CoreEvents.cs`, `Combat/CombatEvents.cs`,
`Enemies/EnemyEvents.cs`.

### 1.3 Entities are compositions of components

`IEntity` (`src/Entities/IEntity.cs`) is the actor contract: `DisplayName`,
`RuntimeId`, `Node3D Body`, and `GetComponent<T>()`/`TryGetComponent`/
`GetComponents`/`HasComponent`. Two concrete hosts implement it:

- **`Entity : Node3D`** — static/non-physics actors (props, the training dummy).
- **`CharacterEntity : CharacterBody3D`** — kinematic actors that move under
  physics (player, enemies). Subclassed by `PlayerCharacter` and `EnemyEntity`
  as **type markers** (so the player is resolvable distinctly via ServiceLocator).

Because C# is single-inheritance, the shared host logic lives in
**`EntityNode`** (`src/Entities/EntityNode.cs`, `internal static`): runtime-id
allocation, child component lookup, and `FindOwner(Node)` (walk up to the first
`IEntity`). Both hosts delegate to it.

**`EntityComponent : Node`** (`src/Entities/EntityComponent.cs`) is the base for
all behaviour/data slices. Capabilities = the components a host carries. Key
facts:

- It resolves its owner in `_Ready` via `EntityNode.FindOwner(GetParent())` and
  exposes it as `IEntity? Entity`.
- **Override `OnInitialize()` / `OnTeardown()`, NOT `_Ready`/`_ExitTree`.**
  Overriding `_Ready` breaks owner resolution.
- You may override `_Process` / `_PhysicsProcess` freely (the base doesn't).

**Lifecycle ordering (critical):**
- `Entity`/`CharacterEntity` assign `RuntimeId` in `_EnterTree` — runs
  **top-down (parent first)**.
- `EntityComponent.OnInitialize` runs from `_Ready` — **bottom-up (children
  first)** — so the owner's identity already exists when a component initializes.

`GetComponent<T>` searches **direct children only**. Components are added as
direct children of the host. `Hitbox`/`Hurtbox` are `Area3D` (not
`EntityComponent`) and resolve their owner via `EntityNode.FindOwner` directly.

---

## 2. Systems reference (what exists today)

### 2.1 Stats (`src/Stats`)

- **`StatType`** enum — resources (`Health`, `Stamina`, `Mana`), primary
  attributes (`Strength`, `Dexterity`, `Intelligence`, `Vitality`, `Endurance`),
  derived/combat (`Armor`, `PhysicalPower`, `SpellPower`, `MoveSpeed`,
  `AttackSpeed`, `CritChance`, `CritDamage`). `StatTypes.IsResource(type)`
  classifies the depleting ones.
- **`Stat`** — base value + list of `StatModifier`; final value is lazily cached.
  Formula: `final = (base + Σflat) × (1 + ΣpercentAdd) × Π(1 + percentMult)`.
  Fires `Changed`.
- **`StatModifier`** — `(value, ModifierType {Flat, PercentAdd, PercentMult},
  object? Source)`. `Source` lets you bulk-remove (e.g. unequip):
  `stat.RemoveModifiersFromSource(item)`.
- **`AttributeSet`** — `[GlobalClass] Resource` of base values; designers author
  `.tres` presets. `ToBaseValues()` → dict; `CreateDefault()` fallback.
- **`StatsComponent`** — the universal gameplay currency. Builds one `Stat` per
  `StatType` from an `AttributeSet`; tracks **current** values for resources;
  exposes `GetValue/GetCurrent/GetMax/GetNormalized/SetCurrent/ModifyCurrent/
  RefillResources`, and combat helpers `ApplyDamage(amount)` / `Heal(amount)`.
  Has **passive regen** (`HealthRegen`/`StaminaRegen`/`ManaRegen`, per second,
  in `_Process`; health never regenerates a corpse). Implements **`ISaveable`**
  (persists current resource values; `SaveId = "stats:{RuntimeId}"`). Raises
  `ResourceChangedEvent`/`EntityDamagedEvent`/`EntityDiedEvent`/`EntityHealedEvent`.

### 2.2 Combat (`src/Combat`)

- **`DamageType`** — `Physical` (mitigated by `Armor`), `Fire/Frost/Lightning/Arcane/
  Nature/Necrotic` (each mitigated by its own resistance stat, Phase 34E), `True`
  (bypasses mitigation entirely).
- **`DamagePacket`** (attacker-built) and **`DamageResult`** (resolved) —
  `readonly record struct`s.
- **`CombatMath`** — `RollAttack(baseDamage, attackerStats)` → `(amount, isCrit)`
  (adds `PhysicalPower × 0.5`, rolls crit from `CritChance`/`CritDamage`);
  `Mitigate(amount, type, defenderStats)` maps the school to a stat via
  `ResistanceStat` and runs **one curve for all of them** — `100/(100+x)`, the same
  `ArmorMultiplier` physical uses.
- **Mitigation is resistance, never immunity** (Phase 34E). `ArmorMultiplier` stays in
  `(0, 1]`, so an enemy can be resistant enough to make a magic school the wrong first
  choice but never immune enough to make it a dead one — DESIGN's "no school a trap"
  rule. There is deliberately **no vulnerability side**: a negative resist clamps to ×1
  rather than amplifying. Every resist defaults to `0`, so an `AttributeSet` that
  doesn't mention them behaves exactly as it did before the system existed.
- **`CombatLayers`** — collision-layer masks: `World=1`, `Body=2`, `Hurtbox=4`,
  `Hitbox=8`.
- **`Hurtbox : Area3D`** — passive damageable region; layer `Hurtbox`, mask 0;
  points at the owner's `CombatComponent`. Needs a `CollisionShape3D` child.
  An actor may carry **several**: Phase 35A's hit zones, each with a `ZoneId` and
  a `DamageMultiplier` scaling damage *and* poise damage on the way through, so a
  dragon's head is a weak point and its tail is not. Authored as
  `EnemyArchetypeResource.HitZones`; one zone-less `Hurtbox` (multiplier `1`)
  remains the norm for everything humanoid-sized.
- **`HitDedupe`** — the "once per target" rule for a swing or a blast, keyed on the
  **owning entity**, not the hurtbox. Shared by `Hitbox` and
  `SpellResolver.Detonate`; without it a multi-zone body takes one full packet per
  zone the volume happens to clip.
- **`Hitbox : Area3D`** — damage-dealing region; layer `Hitbox`, mask `Hurtbox`.
  `Activate(packet)` opens the window; `_PhysicsProcess` **polls overlaps** and
  hits each *actor* once (via `HitDedupe`), skipping its own owner and
  **same-`Team`** hurtboxes (friendly fire off). `Deactivate()` closes. Needs a
  `CollisionShape3D` child.
- **`CombatComponent`** — defender brain: `Team` (0 player, 1 hostile, 2 neutral
  target), poise/stagger, `IsBlocking`, and `ReceiveDamage(packet)` which applies
  block → armor → `StatsComponent.ApplyDamage`, manages poise (raises
  `EntityStaggeredEvent`), and raises `DamageDealtEvent`.
- **`WeaponResource`** — `[GlobalClass] Resource`: damage type, base/poise damage,
  stamina cost, an authored `Attacks` chain, and the legacy wind-up/active/recovery
  floats that are **synthesised into a chain** when `Attacks` is empty (which is most
  weapons). `IsRanged` plus the projectile fields make it a bow; nothing else differs.

### The action timeline (the 2026-09-04 combat/animation overhaul)

⚠️ **`MeleeWeaponComponent` is gone, and with it the second clock.** It ran a `double`
stopwatch through Windup→Active→Recovery while `CharacterAnimationComponent` separately
fired a clip at whatever speed it was exported at, so the hitbox opened on a clock the
visible swing had never heard of — the Iron King's 0.55 s heave and a dagger's 0.15 s
flick played the same `Sword_Slash` identically.

- **`ActionDefinitionResource`** is the authored unit: animation slot, duration, the
  gameplay windows **as fractions of that duration**, commitment, cost, damage and poise
  scale, named hit volume, root motion, presentation and AI metadata.
- **`ActionTimeline`** is the pure arithmetic — phase, hit window, cancel window, combo
  window, stagger rules, and the clip speed that makes an animation span exactly the
  action.
- **`CharacterActionComponent`** is the one executor for every actor. It reads its
  progress off the animation (`CharacterAnimationComponent.ActionProgress`, which is the
  AnimationTree's own playback position); a `Duration` of 0 lets the clip decide outright,
  a positive one warps the clip to fit. A body with no clip runs the identical fractions
  on a fallback timer, so an unanimated actor fights correctly rather than not at all.
- **`ActionReleasedEvent`** fires on the rising edge of the active window. A melee action
  opens its volume there; a cast delivers its spell there; a bow looses its arrow there.
  One instant, one event.
- **`ActionSelection`** is what an AI is allowed to know: "hit the thing that is this far
  away". Which blow that is belongs to the weapon's authored actions; when it lands
  belongs to the animation.
- **`MotionWarp`** closes the last of the gap during a committed attack's wind-up,
  bounded in distance and angle and **swept** through the physics world, so an attack can
  never warp through a wall. It is not root motion: the Meshy clips carry no displacement
  at all.
- **`PoiseReaction`** decides what a broken guard actually does to a body —
  flinch/stagger/heavy/knockdown by `ReactionClass`. A boss is never knocked down and
  never pushed.

### 2.3 Movement (`src/Movement`)

- **`LocomotionComponent`** — reusable kinematic motor for a `CharacterEntity`.
  `Move(delta, wishDir /*world-space horizontal*/, sprint, jump)` applies gravity,
  acceleration, jump and `MoveAndSlide`. Speed comes from the `MoveSpeed` stat
  (falls back to `BaseSpeed`). **Input-agnostic** — the player controller and the
  enemy AI both feed it.
  `Flying` (Phase 35B) swaps gravity for a vertical servo toward `TargetAltitude`
  at `ClimbSpeed`. It is the **vertical axis only**: horizontal steering is
  untouched, so whatever drives a walker drives a flier unchanged. Landing needs no
  ground probe — descend with `Flying` still on, `MoveAndSlide` stops the body at
  the floor, and `IsGrounded` reports the touchdown.
- **`FlightComponent`** (`src/Enemies`) — the take-off/land cycle that sets the
  above. Tuning is on the `AIProfileResource` (`TakeoffRange = 0` ⇒ never flies,
  which is every profile but `ai.dragon`); the transitions are the pure
  `FlightDecision`. `EnemyAIComponent` keeps its FSM: it only holds its swing while
  airborne, skips the navmesh (ground corners are the wrong route when flying over
  obstacles), and grounds the flier on leaving combat so a corpse falls.

### 2.4 Player (`src/Player`)

The player is **six components**, not one controller. Each owns one job; the router
owns the order they run in. `PlayerController` (729 lines, ten jobs) was split on
2026-09-03 and deleted.

| Component | Owns |
| --- | --- |
| `PlayerPhysicsQueries` | the reused ray / sweep / overlap queries and the one exclusion list |
| `PlayerCameraRig` | the camera, the two view modes, the blend, the wall spring, FOV |
| `PlayerLookInput` | mouse and stick turning, and mouse capture |
| `InteractionSensor` | what `E` acts on, the prompt the HUD shows, the hold-`E` pickup sweep |
| `AimController` | where a bolt goes — the `AimPoint` node |
| `PlayerInputRouter` | input → sibling calls, in one documented order |

- **`PlayerCharacter : CharacterEntity`** — marker type registered in the session
  scope, so anything can resolve the player by a distinct type.
- ⚠️ **The router keeps a single `_PhysicsProcess` rather than each component ticking
  itself.** The order is load-bearing: the camera rig runs inside the not-playing
  guard because it dereferences nodes a world teardown is freeing; focus resolves
  before lock-on can be toggled onto it; the mount answers the sprint request before
  locomotion consumes it; dodge is refused during a committed swing. Godot orders
  sibling ticks by child order, which would make all of that an invisible consequence
  of the order `PlayerFactory` happens to add nodes in.
- ⚠️ **The queries are pooled once, for all three readers.** The sensor and the aim
  controller each fire a ray every physics frame and the rig sweeps a sphere;
  building them per call cost five native `RefCounted` objects a frame. Pooling them
  per component would undo the optimisation the pooling exists for.
- **First person is TRUE first person** (2026-09-05). The camera rides the body's own
  head bone — position only; taking the head's rotation would hand the player every head
  turn in every clip — and the body stays visible. `FirstPersonArmsComponent` and both
  `fp_arm_*.glb` are deleted: one skeleton, one action state, one set of equipment.
- **The camera has profiles** (`CameraProfile`): exploration, sprint, combat, target-lock
  and aim, resolved from gameplay in that priority order and eased between. Every field is
  a multiplier on the player's own distance/FOV settings, never a replacement — those are
  accessibility choices.
- **`CombatLayers.CameraBlocker`** is what the spring sweeps. Actors share the World layer,
  so sweeping World meant a companion walking behind the player yanked the camera in.
- **The camera is hybrid, and the mode is a setting.** `PlayerCameraRig.SetFirstPerson`
  is driven by `SettingsAppliedEvent` off `Settings.ThirdPersonCamera` — the settings
  panel's toggle and the `toggle_camera` key (`V`) both flip *that*, so there is one
  path into the mode and it persists. First person seats the camera on the eye pivot
  with the body shadows-only and the `FirstPersonArmsComponent` viewmodel visible;
  third person is over-the-shoulder, body shown, arms hidden. Body yaw equals camera
  yaw in **both**, so combat, lock-on, dodge and melee reach are mode-agnostic.
  **Camera distance (2–6 m) and shoulder side are player settings**, read live each
  frame so the sliders move the camera while they are being dragged.
- **FOV is a setting too, applied by the rig rather than by `SettingsService`** — it is
  a property of the player's `Camera3D`, not of the engine.
  `FirstPersonArmsComponent` re-derives its viewmodel scale whenever the FOV changes.
- **`CameraRigMath`** (pure, unit-tested) owns the eased mode blend, the wall spring
  and the aim direction. `PlayerCameraRig.CameraRestPosition` returns the *live*
  blended-and-sprung offset and is what `CameraShake` offsets around.
- **Aim comes from the camera, not the head.** The interact raycast starts at the
  camera and its reach is measured from the *character*, so third person can never
  reach further than first. In first person the camera sits on the pivot, so both are
  exact no-ops.
- **Lock-on facing lives on `LockOnComponent.FaceTarget()`**, not on the player: the
  rule is about the lock, so whoever holds a target faces it.
- **`PlayerFactory.Create(...)`** assembles the actor. The six components above are
  added last and in order — queries first (three siblings resolve them), router last
  (it resolves all the others).

### 2.4c Bosses (`src/Enemies`, Phase 36A)

A boss fight is authored data. `BossResource` (`data/bosses/*.tres`) holds an ordered
`BossPhaseResource` array — HP threshold, stat escalation, `GrantSpellIds`, an optional AI-profile
swap, telegraph colour/energy — plus the enrage fuse. An `EnemyArchetypeResource` names one through
`BossId`, the way it names an AI profile through `AiProfileId`; the archetype stays "what this
creature is made of", the boss resource is "how its fight is structured", and two bosses can share a
shape.

`BossController` runs it: phases entered at or below a threshold and never left, abilities granted
through `SpellcastingComponent.Learn` (the dialogue-reward path, which ignores `PlayerLearnable` —
what a monster spell needs), and a wind-up flare on the body's claimed emissive material.
`BossPhases` is the pure, tested core (`SelectPhase`, `ShouldEnrage`).

- **The enrage clock starts on the first damage traded with the boss**, not on
  `BossEncounterStartedEvent` — only `BossSummonComponent` publishes that, so a lair boss's fuse
  would never light.
- **A big hit lands in the deepest phase crossed**, entering every stage on the way, so escalation
  and ability grants are never skipped by a single large blow.
- **An unknown or absent `BossId` falls back to the Phase 28B three-stage table**, so a content typo
  costs the authored numbers, not the fight's structure.
- `EnemyArchetypeFactory` attaches the controller for any `IsBoss` archetype. Before 36A only the
  Iron King's bespoke factory did, so the three dragons were `BossEntity` healthbars with no phases
  and no escalation behind them.
- **Every boss goes through that one factory as of 36B** — the Iron King is
  `data/enemies/IronKing.tres` like anything else, and `BossFactory` is gone.
- **Wind-ups are telegraphed and interruptible (36C).** `TelegraphComponent` + `TelegraphRing` draw
  a ground ring under any actor for exactly the wind-up `AttackPerformedEvent.WindupSeconds`
  reports, tinted by the current boss phase. It is model-independent by construction, which is the
  point: `BossController`'s emissive flare needs a material only an authored model supplies, so the
  three greyboxed dragons used to warn of nothing at all. Both cues now end together, and both end
  early on `AttackInterruptedEvent`.
- **Add waves and arena hooks (36D).** `BossPhaseResource.AddWaves` holds `BossAddWaveResource`
  sub-resources: any registered enemy id, a count, an optional repeat interval and a live cap.
  `BossController` summons on phase entry, ticks repeats, and kills every add through the ordinary
  damage path when the boss falls — so their loot and XP still land, rather than the player losing
  value they had already earned. `BossAdds` is the pure core (`SpawnSlot`, `SummonCount`).
- **Intro, defeat and reward are the boss's own data (36E).** `BossController.BeginEncounter()` is
  idempotent and publishes `BossEncounterStartedEvent` once — the summoning brazier calls it on the
  entrance beat, and the controller self-calls on the first damage traded, so a lair boss nobody
  summons still gets an intro lock and a healthbar. `BossEncounterDirector` then resolves the *dead
  boss's* `BossResource` through its controller for the intro/slow-mo timings, the guaranteed
  `RewardItemId`, the `DefeatFlagId` and the `DefeatDialogueId`.
  > ⚠️ Every one of those was a constant naming the Iron King while the handler fired for **any**
  > `BossEntity`, and since 36A the dragons are among them. The reward was guarded by an
  > already-defeated check; the dialogue was queued outside it. So killing any dragon re-opened his
  > "absorb the flame?" choice and its +25 corruption — once per boss kill, into the meter that
  > decides the endings. `BossDefeat.Resolve` now makes reward, flag and dialogue one decision, and
  > the validator rejects a reward authored without a flag to record it.
- **An arena binds itself to the fight in its own scene, not in code.** `Marker3D`s tagged
  `groups=["boss_add_spawn"]` are where waves arrive — resolved by group (a rename cannot silently
  unbind one) and scoped to markers under the boss's own parent (two loaded arenas cannot borrow each
  other's). No markers means a computed ring, which is what a lair gets. `ArenaHookComponent` is a
  plain `Node` authored in the scene that reveals nodes at a given phase and resets on the boss's
  death — the reset matters, because `BossSummonComponent` re-arms until the defeat is persisted.
- **The interrupt is general, the tuning is boss data.** A stagger during `Phase.Windup` cancels a
  melee swing outright (`MeleeWeaponComponent`) and drops an active charge/channel
  (`SpellcastingComponent`) — for every actor, the player included. Once the hitbox opens the blow is
  committed. `BossPhaseResource.WindupPoiseMultiplier` scales incoming poise while its owner is
  winding up, applied through the pure `CombatMath.PoiseDamage` and carried on `CombatComponent` as
  `InWindup` + `WindupPoiseMultiplier`.

---

### 2.5 Enemies (`src/Enemies`)

**The brain is four pieces, not one class.** `EnemyAIComponent` was 1229 lines; it is
712 and coordinates rather than implements. Every archetype is still pure data — 16
`AIProfileResource` `.tres` files, unchanged by the 2026-09-03 split.

| Piece | Owns |
| --- | --- |
| `EnemyAIComponent` | profile resolution, the LOD clock, the seven-state tick, patrol/retreat/return/despawn |
| `EnemySenses` | sight (cached at the profile's interval, one reused ray per actor), faction standing, provocation memory |
| `EnemyCasterTactics` | standoff banding, kiting, the heal → attack → ward cast priority, the throttled group heal scan |
| `AiNavigator` | navmesh steering, arrival, facing, patrol snapping — ⚠️ **shared with `CompanionAIComponent`** |

Three engine-free rule cores carry what used to be untestable branches, and are:
`AiSenseRules` (the vertical vision gate and its flier exemption, the melee reach
gates, the four alert filters, the 3D shout radius, the ambush hold),
`CombatTransition` (the five combat guards **in order** — the territory leash before
the health check — plus where a coward goes and where an ambusher rests) and
`AiLodClock` (banked wall time for memory and cooldowns, raw frame delta for
movement). `AiSenseRulesTests`, `AiStateRulesTests` and `AiLodClockTests` pin them.

⚠️ **`AiNavigator` is shared for a reason, not for tidiness.** The companion brain
carried its own copy of the same three-answer navigation rule and the copies had
drifted: the companion's ran `MapGetClosestPoint` — a navigation-server query — every
frame where the enemy's paced it to 4 Hz, and it had no turn-rate slew at all.
`debug_pass_regressions.gd` now asserts a second copy has not come back.


- **`EnemyEntity : CharacterEntity`** — marker type for hostiles.
- **`EnemyState`** — `Idle, Patrol, Investigate, Combat, Retreat, Dead`.
- **`EnemyAIComponent`** — perception-driven FSM. Reuses `LocomotionComponent`
  (move) and `MeleeWeaponComponent` (attack). Perception = vision range + FOV
  cone + line-of-sight raycast + short-range proximity sense; tracks a
  last-known position. Spotting the player broadcasts `EnemyAlertedEvent` →
  nearby idle/patrolling allies `Investigate` (group coordination). Wounded
  (< `RetreatHealthFraction`) → `Retreat`. On death → `Dead` → despawn after a
  delay.
- **Caster branch (Phase 29.5F)** — when an actor carries a `SpellcastingComponent`,
  `EnemyAIComponent`'s Combat state routes to a caster behaviour instead of melee: it holds
  a cast band via pure `CasterDecision` (approach when far, **kite** when crowded, hold
  otherwise), faces the target so the cast aims true, and casts one spell per tick by
  priority — heal a wounded ally (`FindWoundedAlly`, same team, incl. itself), else the
  hardest-hitting ready offensive, else ward itself. It reuses the *player's*
  `SpellcastingComponent` (`TryCastById`, `TryCastSupportOn`) — no parallel casting system.
- **`EnemySpawnDirector : Node3D`** — keeps a population alive within a radius;
  seeds the camp on ready, respawns the dead (tracks via `TreeExited`).

#### The roster is data (Phase 34)

Phase 34 turned the enemy roster from code into content. **26 creatures are spawnable by id and
only three of them have a factory**; the rest are `.tres` files.

- **`AIProfileResource` / `AIProfileDatabase`** (`data/ai_profiles`, ids `ai.*`, Phase 34A) — every
  knob `EnemyAIComponent` used to export lives on a profile: perception (`VisionRange`,
  `FovDegrees`, `AlertRadius`), melee (`AttackRange`, `FlankSpreadDegrees`), standoff
  (`StandoffRange`, `KiteDistance`, `AllySupportRange`), guard (`BlockDuration`/`BlockRecovery`),
  ambush (`AmbushRange`), nerve (`RetreatHealthFraction`, `FleeOnSight`, `ProvokeMemory`) and LOD.
  **The component stayed one class** — each behaviour is a branch gated on a profile number, so they
  compose (a shielded flanking ambusher is authorable) instead of forking the brain. Pure helpers
  `GuardCycle`, `PackFlank` and `CasterDecision` hold the testable arithmetic.
- **`EnemyArchetypeResource` / `EnemyArchetypeDatabase`** (`data/enemies`, ids `enemy.*`, 34B–34F) —
  a creature as data: name key, build paths (attributes/weapon/loot/model), tint, AI profile,
  faction, `KnownSpellIds`, capsule dims, poise, regen, XP. The database registers a builder per
  archetype with `EnemyTemplateRegistry`, so **a new `.tres` is spawnable with no code change**.
- **`EnemyArchetypeFactory`** — the single shared builder for all of them (named
  `HumanoidEnemyFactory` until 34C generalized it). Melee reach scales with body height against a
  1.8 m humanoid reference, so a short quadruped bites at its own scale. The three bespoke factories
  that remain — `EnemyFactory` (goblin), `AshenAcolyteFactory` (a pure caster with no melee hitbox),
  `BossFactory` (Iron King, phase controller) — earn it by being *structurally* different, not by
  having different numbers.
- **A caster archetype** needs three things aligned and fails **silently** if any is missing: a
  non-empty `KnownSpellIds`, a standoff profile, and a real `Mana` pool in its `AttributeSet`.
  Authored `KnownSpellIds` bypass the `MinCorruptionTier` gate that `Learn` enforces, which is how
  `enemy.cinder_thrall` wields the player's corruption-gated lifesteal.
- **`AshenAffliction`** (Phase 34F) — the corruption *variant* layer: the same archetype, taken by
  Morthul. Applied **after** `AddChild` (stat modifiers need `StatsComponent` initialized) from
  `EncounterDirector`, rolled per enemy off `EncounterResource.CorruptionChance`. It never changes
  `TemplateId` (quest kill objectives match on it) and always `Duplicate()`s a material before
  tinting (otherwise the tint writes through to every other instance sharing that imported mesh).
- **`BestiaryEntryResource` / `BestiaryDatabase` / `BestiaryService`** (`data/bestiary`, 34G) — the
  Ash Hunters' journal: kills and Ashen kills per template id, `ISaveable` (`SaveId = "bestiary"`),
  fed by `EntityDiedEvent` and read by `BestiaryPanel` (`B`). Entries key off the **template id**,
  not the archetype, so the three bespoke creatures are catalogued too. Reveal staging is the pure
  `BestiaryStages.Of`. The validator checks this domain **in both directions** — every entry names a
  real creature *and* every registered creature has an entry.

### 2.6 Items & inventory (`src/Items`, `src/Interaction`)

- **`ItemType`** / **`ItemRarity`** enums (+ `ItemRarities.Color`). **`ItemResource`**
  — `[GlobalClass] Resource` template (`Id`, name, type, rarity, `MaxStack`,
  weight, value, icon). Author `.tres` under `data/items/`.
- **`ItemDatabase`** (static) — scans `data/items/` once at startup
  (`Initialize()` from the bootstrap) and maps `Id → ItemResource`, so save/loot
  resolve items by stable string id. New item = new `.tres`, no code.
- **`ItemStack`** — a template + mutable quantity (one slot).
- **`InventoryComponent`** — slot-based stacking container (`AddItem`/`RemoveItem`/
  `CountOf`/`Contains`, weight tracking). Implements **`ISaveable`**
  (`inventory:{RuntimeId}`; saves ids+quantities, resolves via `ItemDatabase`).
  Raises `InventoryChangedEvent`/`ItemPickedUpEvent`.
- **`InteractableComponent`** (`src/Interaction`, abstract) — base for things the
  player can interact with. `InteractionSensor` raycasts from the camera on the
  `interact` action and calls `Interact(player)`.
- **`ItemPickupComponent`** (an interactable) + **`ItemPickupFactory`** — world
  pickups (rarity-tinted cube + collider). Goblins drop hide/gold on death.
- **`InventoryPanel`** (`src/UI`) — the character screen (toggle `I`): equipment
  slots + backpack with Equip/Unequip buttons. Opening it frees the mouse and sets
  `Core.UiState.MenuOpen` (which suspends player look/move/attack). Rebuilt from a
  dirty flag in `_Process`, never during a button signal.
- **Equipment:** `EquipmentSlot` enum + `EquippableItemResource : ItemResource`
  (slot, flat stat bonuses, optional `WeaponResource`). **`EquipmentComponent`**
  (`ISaveable`, `equipment:{RuntimeId}`) equips/unequips between the inventory and
  slots, applies bonuses as `StatModifier`s sourced to the item (removed cleanly on
  unequip), and swaps the `MeleeWeaponComponent.Weapon` (restoring the factory
  baseline). Raises `EquipmentChangedEvent`.

### 2.6b Loot generation (`src/Loot`, `src/Items`)

- **`ItemInstance`** (`src/Items`) — the per-item runtime layer over an
  `ItemResource` template: a rolled `Rarity`, a generated `DisplayName`
  (prefix + base + suffix), and a frozen `ItemAffix` list. Mundane items are plain
  instances (`ItemInstance.Plain`); only affix-less instances stack (`CanStackWith`),
  so rolled gear is unique. `StatBonuses()` merges the equippable template's flats
  with affix bonuses. Serializes to/from a dict (`Save`/`FromSave`). **`ItemStack`
  now holds an `ItemInstance`**, and inventory/equipment/pickups/UI/save all flow
  instances (the `InventoryComponent.AddInstance`/`RemoveOneInstance`,
  `EquipmentComponent` keyed by instance).
- **`ItemAffix`** + **`AffixDefinition`** (`[GlobalClass]`, `data/affixes/*.tres`) +
  **`AffixDatabase`** — a definition declares a `StatType`, value range,
  `ModifierType`, `MinRarity`, gear-family flags (`ForWeapons/Armor/Accessories`)
  and a selection `Weight`; `AffixDatabase.ApplicableTo(item, rarity)` returns the
  eligible pool. A rolled `ItemAffix` becomes a `StatModifier` sourced to its
  instance when equipped.
- **`LootTable`** + **`LootEntry`** (`[GlobalClass]`, `data/loot/*.tres`) — a table
  is independent per-entry rolls (`DropChance`, `Min/MaxQuantity`, `RollAffixes`)
  plus an optional gold roll and a `QualityBonus`. **`LootGenerator`** rolls it into
  `LootDrop`s: `LootRarity.Roll` (quality-weighted tiers) → distinct weighted affixes
  → values scaled by rarity/quality. `RollAffixed(...)` forces a rarity for demos.
- **`LootComponent`** (`EntityComponent`) — on its owner's `EntityDiedEvent`, rolls
  its `LootTable` and spawns a pickup per drop (deferred add, scattered around the
  corpse). `EnemyFactory` attaches it (`data/loot/GoblinLoot.tres`), replacing the
  bootstrap's hard-coded goblin-hide drop.
- **`StatLabels`** (`src/Stats`) — short display names for `StatType`, used by affix
  tooltips.

### 2.6c Progression (`src/Progression`)

- **Kill attribution:** `EntityDiedEvent` carries an optional `Killer`;
  `StatsComponent.ApplyDamage(amount, source)` threads `DamagePacket.Source` into it
  so kills can be credited. (Old single-arg `new EntityDiedEvent(entity)` still
  compiles — the killer defaults to null.)
- **`ProgressionResource`** (`[GlobalClass]`, `data/progression/*.tres`) — XP curve
  (`BaseXpToLevel × level^XpCurveExponent`), `MaxLevel`, `SkillPointsPerLevel`, and
  per-level flat stat gains. **`ExperienceComponent`** — passive XP bounty granted to
  the killer (enemies carry it).
- **`ProgressionComponent`** (`EntityComponent`, `ISaveable`) — subscribes to
  `EntityDiedEvent`, awards the dead entity's `ExperienceComponent.XpValue` when it
  was the killer, resolves multi-level-ups, re-derives cumulative per-level stat
  growth as `StatModifier`s sourced to itself (`ApplyGrowth`), refills resources and
  banks skill points on level-up. `AddXp` / `SpendSkillPoints`; raises
  `XpGainedEvent` / `LeveledUpEvent`. Persists level / XP / unspent points (growth
  recomputed from level, never stored).
- **Perks:** `PerkResource` (`[GlobalClass]`, `data/perks/*.tres`, a rankable
  single-stat passive) + `PerkDatabase` + **`PerksComponent`** (`ISaveable`):
  `Learn` spends `ProgressionComponent` skill points and applies the perk bonus as a
  `StatModifier` sourced to the perk (recomputed per rank, re-applied on load).
  Raises `PerkChangedEvent`.
- **UI:** `DebugHud` shows `Level / XP / SP`; `InventoryPanel` (the character
  screen) shows progression + a PERKS section with Learn buttons. Debug key `X`
  grants 50 XP. Events live in `src/Progression/ProgressionEvents.cs`.

### 2.6d Quests (`src/Quests`)

- **Content:** `QuestResource` (`[GlobalClass]`, `data/quests/*.tres`) holds
  `ObjectiveResource` sub-resources (`ObjectiveType` Kill/Collect, `TargetId`,
  `RequiredCount`), `QuestItemReward`s, XP/gold rewards, an optional
  `FactionRewardId`/`FactionRewardAmount` pair (Phase 34.5C — the same shape
  `WorldEventResource` carries, applied through the player's `ReputationComponent`, and the
  only way authored content moves standing *upward*), and an optional
  `PrerequisiteQuestId`. `QuestDatabase` indexes them. Objective/reward arrays are
  authored untyped and read via `ObjectiveList()` / element cast (same as
  `LootTable.Entries`). `QuestProgress` is the runtime per-quest tracker (counts +
  `QuestStatus`).
- **`QuestLogComponent`** (`EntityComponent`, `ISaveable`, on the player) — subscribes
  to `EntityDiedEvent` (Kill objectives, credited via `e.Killer` ↔ `e.Entity.TemplateId`)
  and `ItemPickedUpEvent` (Collect, by `Item.Id`); on completion grants rewards through
  the sibling `ProgressionComponent.AddXp` and `InventoryComponent.AddItem`. Raises
  `QuestStarted`/`QuestObjectiveAdvanced`/`QuestCompleted` events; persists the log.
  `StartQuest`/`CanStart`/`IsActive`/`IsCompleted`.
- **`QuestGiverComponent`** (`InteractableComponent`) — an NPC that offers a quest on
  the player's `E` interact (honours prerequisites + already-active/completed).
- **UI:** `QuestLogPanel` is a non-modal read-only overlay toggled with `J`
  (it does **not** set `UiState.MenuOpen`); the HUD shows a compact active-quest
  tracker. Sandbox auto-starts "Cull the Goblins" and a Village Elder offers
  "Gather Iron".
- **Note:** kills are credited because melee sets `DamagePacket.Source = attacker`,
  which `StatsComponent.ApplyDamage` threads into `EntityDiedEvent.Killer`.

### 2.6e Dialogue (`src/Dialogue`)

- **Content:** `DialogueResource` (`[GlobalClass]`, `data/dialogue/*.tres`) is a node
  graph — `Id`, `SpeakerName`, `StartNodeId`, and `Nodes` (untyped array of
  `DialogueNode`: `Id`, optional `Speaker`, multiline `Text`, `Choices`). Each
  `DialogueChoice` has reply `Text`, a `Goto` node id (empty = end), a gating
  `DialogueCondition` + `ConditionArg`, and a fired `DialogueEffect` + `EffectArg`.
  Arrays are authored untyped and read via `NodeList()`/`ChoiceList()` (same pattern as
  `LootTable.Entries`/`QuestResource.Objectives`). `DialogueDatabase` indexes by id.
- **Conditions/effects are declarative — no scripting in `.tres`.** `DialogueCondition`:
  `Always`/`QuestAvailable`/`QuestActive`/`QuestCompleted`/`QuestNotStarted`/`HasFlag`/
  `MissingFlag`/`CorruptionAtLeast`/`CorruptionBelow`/`CompanionRecruited`/
  `CompanionNotRecruited`/`CompanionLoyaltyAtLeast`. `DialogueEffect`: `None`/`StartQuest`/
  `SetFlag`/`ClearFlag`/`AddCorruption`/`RecruitCompanion`/`DismissCompanion`/
  `AddCompanionLoyalty`/`LearnSpell`/`OpenShop`. Both are **append-only** (ordinals are authored
  into every `.tres`; `EnumStabilityTests` pins them).
- **`OpenShop` (38E) is how a merchant NPC trades.** An entity gets one interactable, so a
  `VendorComponent` behind a `DialogueComponent` never fires; a trade *choice* sidesteps that without
  displacing the conversation, and puts the shop id somewhere `ContentValidator` can read — a
  `VendorComponent.ShopId` in a `.tscn` is unvalidated and a typo there is silent. ⚠️ The choice must
  leave `Goto` empty (the validator enforces it): the effect is applied *before* `Goto` resolves, so
  `VendorPanel` registers with `UiState` before `DialoguePanel` deregisters and the owner count never
  reaches zero — which is what makes the handover free of pause and mouse-mode flicker.
- **`DialogueSession`** (plain runtime object, not a node) — walks one conversation:
  tracks the current node, `VisibleChoices()` filters by condition against the player's
  `QuestLogComponent` + `StoryFlagsComponent`, and `Choose(choice)` applies the effect
  then advances to `Goto` (or ends). Keeps the UI a thin view.
- **`StoryFlagsComponent`** (`EntityComponent`, `ISaveable`, `flags:{RuntimeId}`, on the
  player) — persistent named boolean flags giving conversations memory; `Set`/`Clear`/
  `Has`, raises `StoryFlagChangedEvent`. Deliberately general for later systems.
- **`DialogueComponent`** (`InteractableComponent`) — an NPC that, on `E` interact,
  resolves its `DialogueResource` and publishes `DialogueStartedEvent`. Replaces bare
  quest-givers: offering a quest is a choice effect.
- **UI:** `DialoguePanel` is a **modal** window driven by `DialogueStartedEvent` (builds
  the session, renders the line + choice buttons, sets `UiState.MenuOpen` + frees the
  mouse, rebuilds from a dirty flag). Raises `DialogueEndedEvent`. Sandbox: the Village
  Elder talks — offers "Gather Iron", branches on quest state, sets `flag.elder_thanked`.

### 2.6f World clock & NPC schedules (`src/World`, `src/Npc`)

- **`WorldClock`** (`src/World`, `Node`, `ISaveable` `worldclock`, `ServiceLocator`-
  registered, `ProcessMode.Pausable`) — advances a 24h day at `DayLengthSeconds` real
  seconds/day and publishes `TimeOfDayChangedEvent(Hour, DayPhase)` on each new hour (and
  on start/load). Exposes `TimeOfDay`/`Hour`/`Phase`/`Clock()`. The minimal time source
  for schedules; **Phase 13** builds the full day/night + weather model on top. Persists
  the time of day and, since **Phase 38B**, `Day` — a plain count of elapsed in-game days.
  Until then the game had no notion of a date at all: `TimeOfDay` wraps through `PosMod` and
  nothing counted the wraps, so "three days later" was inexpressible. Shop restock is the
  first consumer; `SetTimeOfDay(26)` rolls the date forward too, which is the only way to
  advance a day without waiting `DayLengthSeconds` for each one.
  `DayPhase` (Night/Dawn/Day/Dusk) is derived via `DayPhases.Of(hour)`.
  Created by the bootstrap; `DebugHud` shows the clock.
- **Schedule content** — `ScheduleResource` (`[GlobalClass]`, `data/schedules/*.tres`) holds
  `ScheduleEntry` sub-resources (`StartHour`, `Activity`, `Destination`), authored untyped
  and read via `EntryList()`. `EntryForHour(hour)` picks the active block (pre-dawn hours
  wrap to the last block). `ScheduleDatabase` indexes by id.
- **`ScheduleComponent`** (`src/Npc`, `EntityComponent`, on a static NPC `Entity`) — reads
  the clock (`ServiceLocator` → `WorldClock`), walks the host toward the current block's
  `Destination` with a simple kinematic step + `LookAt` (villagers need no physics), and
  raises `NpcActivityChangedEvent`. **Reactions:** a nearby `EnemyAlertedEvent` starts a
  timed flee away from the threat (overrides the schedule); a `DialogueStartedEvent` where
  it is the speaker freezes it to face the player until `DialogueEndedEvent`. Sandbox: the
  Elder walks well→forge→square→home→sleep as the clock turns, flees goblins, stops to talk.

### 2.6g Magic (`src/Magic`)

- **Spell content** — `SpellResource` (`[GlobalClass]`, `data/spells/*.tres`): `Id`,
  `DisplayName`, a `School` (a `DamageType`, so spells reuse the combat mitigation pipeline
  and tint via `SpellSchools.Color`), a `Delivery` (`SpellDelivery` Projectile/Area/Self/**Cone**),
  `ManaCost`, `Cooldown`, `BaseDamage`, `Healing`, an optional applied `StatusEffectId`, and
  delivery knobs (`Range`, `ProjectileSpeed`, `ImpactRadius`, `ConeAngleDegrees`).
  `SpellDatabase` indexes them. A **Cone** (Phase 35C, dragon breath) is a wedge along the
  caster's aim: `ConeAngleDegrees` is its *full* opening angle and `ImpactRadius` its length.
- **Status effects** — `StatusEffectResource` (`[GlobalClass]`, `data/status_effects/*.tres`):
  a timed condition with optional DoT (`DamagePerTick`/`TickInterval`) and one stat modifier
  (`ModStat`/`ModType`/`ModValue`) — burns, chills/slows, buffs. `StatusEffectDatabase` indexes
  them. **`StatusEffectsComponent`** (on every combatant — player, enemies, dummy) ticks active
  effects: DoT goes through `StatsComponent.ApplyDamage(.., source)` (DoT kills attribute to the
  caster); modifiers are `StatModifier`s sourced to the runtime `StatusEffect` instance (cleanly
  removed on expiry). Re-applying refreshes duration. **Transient — not saved** (like stagger).
- **`SpellcastingComponent`** (`EntityComponent`, `ISaveable`, the magic analogue of
  `MeleeWeaponComponent`) — `KnownSpellIds`, a prepared index, per-spell cooldowns, mana spend
  and `TryCast()`/`Cycle()`. Delivery is resource-driven: `CastProjectile` (spawns a
  `SpellProjectile` along `AimNode`, the player's camera pivot), `CastArea`
  (`SpellResolver.Detonate` burst centred on the caster), `CastSelf` (heal and/or self-buff).
  Input-agnostic and reusable by any actor. Persists known spells + prepared index (cooldowns
  transient). Damage rolls via **`CombatMath.RollSpell`** (SpellPower scaling, the mirror of
  `RollAttack`).
- **`SpellProjectile`** (`Area3D`, Hitbox layer / mask Hurtbox|World) — the moving analogue of a
  `Hitbox`: flies forward each physics frame, resolves on the first enemy hurtbox, world contact
  or end of range. **`SpellResolver`** does the impact: `HitOne` (single target), `Detonate`
  (a Hurtbox-layer sphere query for AoE), or `Sweep` (that same query narrowed by `SpellCone`
  to a wedge — the two share one private `Resolve`, so they cannot drift apart). All honour the
  same friendly-fire rules as hitboxes (never the caster, never same-team) and the same
  per-actor `HitDedupe`, then apply the spell's status. `SpellFlash` is a short-lived cosmetic
  burst sphere; a cone greyboxes as a line of widening flashes along its axis.
- **`TerritoryLeash`** + `AIProfileResource.TerritoryRadius` (Phase 35D) — the leash the AI never
  had. `_home` was read only by patrol/retreat and combat chased until line of sight broke, so a
  world boss could be walked out of its valley. Past the radius `TickCombat` drops to
  `EnemyState.Returning`, which heads home and **ignores the player the whole way** (an "unless it
  sees you" clause would let the player defeat the leash by standing in the doorway). Re-engaging
  needs it back inside `ReturnFraction` of the radius, so a boundary hover cannot flicker it.
  `0` = no leash = every profile but `ai.dragon`.
- **`LairSpawnComponent`** (`src/Enemies`, Phase 35D) — places a world boss in a region cell and
  remembers you killed it. **The spawner persists, not the boss:** `CellPersistenceDirector`
  reconciles on `RegionCellLoadedEvent`, which `RegionStreamer` publishes *after* `AddChild(root)`,
  so a boss spawned that frame races the walk and one spawned deferred loses it — either way a dead
  boss returns. The component is authored in the `.tscn` (so it is always found), is `ISaveable`,
  and stores one bool. The boss itself stays a plain transient actor.
- **`BreathComponent`** (`src/Enemies`, Phase 35C) — the only thing an enemy could not previously
  do: hold a *channel* open. It selects the breath via `SpellcastingComponent.BeginCastById`,
  drives `UpdateCast`, and ends it; the damage is entirely the ordinary spell path. It aims by
  pointing the actor's `CastOrigin` at the target, which is how a hovering dragon breathes
  **down** — the AI keeps the body itself level. Trigger rule is the pure `BreathWindow`.
- **UI & input** — `Q` casts the prepared spell, `F` cycles it; `DebugHud` shows mana, the
  prepared spell + cooldown, and active status effects on player/target. Events:
  `SpellCastEvent`/`SpellSelectedEvent`/`SpellsChangedEvent`/`StatusEffectAppliedEvent`/
  `StatusEffectRemovedEvent` (`src/Magic/MagicEvents.cs`). Sandbox spells (8): Firebolt,
  Fireball (AoE), Flame Lance (charged), Frost Nova (Area), Storm Conduit (channeled), Lesser
  Heal (Self HoT), Arcane Shield (Self ward), Ember Siphon (corrupted, Necrotic).

#### Spellcraft depth (Phase 29.5 — "Spellcraft & the Fading Weave")

The baseline above is Phase 12. Phase 29.5 turns magic into a build spine without a parallel
system — the same `SpellcastingComponent` still drives everything:

- **Cast archetypes** — `CastMode` (Instant / **Charged** / **Channeled**) on `SpellResource`,
  layered on the Projectile/Area/Self *shape*. `SpellcastingComponent` grows a cast state
  machine (`BeginCast`/`UpdateCast`/`EndCast`/`CancelCast`): charged scales power by hold time
  (pure `SpellCharge.PowerMultiplier`), channeled ticks at `ChannelTickInterval` for
  `ChannelManaPerSecond`. `PlayerInputRouter` drives press/hold/release.
- **School identities (29.5B)** — a shared on-hit seam, `SchoolIdentity.OnSpellHit`, invoked by
  `SpellResolver` after damage and *before* the spell's own status: **Fire** stacking ignite
  (`StatusEffectResource.MaxStacks`, DoT × stacks), **Frost** chill→freeze (`Frozen.tres` when
  the target is already chilled), **Lightning** one chain to the nearest other hostile,
  **Necrotic** caster lifesteal, **Nature** heal-over-time (`HealPerTick` + `Regrowth.tres`),
  **Arcane** the self-ward plus — since Phase 34E.5 — an **on-hit dispel**: the hit strips the
  target's longest-lasting beneficial status, never a harmful one, one per hit
  (`StatusMath.PickDispel`). That closed the table: **every school now has an on-hit identity.**
  A Self cast can never trigger it, since `OnSpellHit` is only reached from `SpellResolver`'s
  projectile/area paths while Self casts run through `SpellcastingComponent.CastSelf`. Statuses
  now number 6 (Burning, Chill, Frozen, Regrowth, Arcane Ward, Decay).
- **School mastery (29.5C)** — `SchoolMasteryComponent` (`ISaveable`) banks a point per cast of a
  school and converts points→rank via pure `SchoolMasteryMath`; `SpellcastingComponent` folds the
  rank's multiplier into damage and heals. `CombatMath.RollSpell` now also scales off Intelligence
  (alongside SpellPower).
- **Reactive combos (29.5D)** — `SpellCombo` reads the target's pre-hit afflictions on the same
  seam and resolves a `ComboRule` (Shatter = Lightning into Chill; Thermal Shock = Fire into
  Chill), bursting and consuming the status.
- **The fading Weave (29.5E)** — see §2.6h.
- **Enemy casters (29.5F)** — see §2.5.
- **Recovery, not vendoring (29.5E)** — `SpellTomeComponent` (an `InteractableComponent`) teaches a
  spell through the corruption-gated `SpellcastingComponent.Learn`; the sandbox seeds an Ashen
  Tome holding the corrupted Ember Siphon.

### 2.6h World systems (`src/World`)

Layered on the Phase 11 `WorldClock` (which already supplies time-of-day + `DayPhase` and
persists). Three pieces:

- **Day/night atmosphere** — **`SkyController`** (`Node3D`, `Pausable`) animates a
  `DirectionalLight3D` "sun" and the scene `Godot.Environment` from the clock's *continuous*
  `TimeOfDay` each frame: sun sweep/pitch, warm→white colour, energy by day factor, and sky/
  ambient darkening at night via `Environment.BackgroundEnergyMultiplier`. The sun + env are
  built by the bootstrap and **injected** (`Sun`/`Environment` properties). It also blends in
  the active weather and drives a rain `GpuParticles3D` that follows the player.
  - **Dying-world palette (Phase 27F) — the reference bar for every region.** The base mood is a
    shared, ashen, ever-hazy "dying world": `WorldSessionDirector.BuildEnvironment` sets the Environment
    base (ACES tonemap + muted exposure, an overcast-leaning desaturated `ProceduralSkyMaterial`,
    warm-grey ambient fill, soft glow), and `SkyController`'s labelled *Dying-world palette*
    constants set the day/night tuning (ashier sun tints, a dimmer noon ceiling, and a **haze
    floor** so the air is never perfectly clear even in clear weather). Weather `FogColor`s are
    ashen warm-grey, and the heartland defaults to `weather.cloudy`. The whole game is the dying
    world, so this is one shared palette; **per-realm variation is lifted into data at the 2026-08-28 layout rebuild**,
    and final art is Phase 53. Tune the look via those two clearly-marked spots — no per-region
    palette fields exist yet. (Ambient *audio* is deferred to **Phase 31**.)
- **Weather** — `WeatherResource` (`[GlobalClass]`, `data/weather/*.tres`): duration range,
  `SelectionWeight`, `LightEnergyScale`/`SkyEnergyScale`, `FogDensity`/`FogColor`,
  `Precipitation`. `WeatherDatabase` indexes them. **`WeatherDirector`** (`Node`, `ISaveable`
  `weather`, `ServiceLocator`-registered, `Pausable`) holds the active state + a countdown in
  in-game hours (off the clock), rolls a new weighted state on expiry (never the same twice),
  publishes `WeatherChangedEvent`, and persists current id + remaining time. `SkyController`
  reads `WeatherDirector.Current` each frame and `MoveToward`-blends the atmosphere.
- **Encounters** — `EncounterResource` (`[GlobalClass]`, `data/encounters/*.tres`): enemy
  template, count range, weight, per-`DayPhase` allow flags, and `RegionIds` (Phase 34.5B —
  **empty means anywhere**, which is why the pre-34.5B table needed no edits; authored when a
  creature belongs to one realm). `EncounterDatabase` indexes them.
  **`EncounterDirector`** (`Node3D`, `Pausable`) spawns groups around the player on a
  cadence scaled by phase (night) and weather (storm), filtered by day phase *and* the
  `RegionStreamer`'s `ActiveRegionId`, capped by `MaxConcurrent` and tracked
  via `TreeExited`, reusing `EnemyFactory`. Publishes `EncounterTriggeredEvent`. **Not
  persisted** (emergent/transient, like `EnemySpawnDirector`). The richer *named world-event*
  framework is Phase 17 — keep these lightweight.
- Events live in `src/World/WorldEvents.cs` (`TimeOfDayChangedEvent` + the two above). The HUD
  shows the current weather beside the clock.

### 2.6h-2 Regions & streaming (`src/World`, Phase 25)

The world is divided into authored **regions** (one per area; many per `Realm`). Phase 25A
established the data + convention; 25B adds the streamer; 25C adds hard transitions; the map and
fast-travel land in 25E–25G.

- **`RegionResource`** (`[GlobalClass]`, `data/regions/*.tres`): `Id` (`region.*`),
  `DisplayName`, `Realm` (the fixed `Realm` enum — the four LORE realms + the Celestial),
  `SpawnPoint` (where the player appears on entry, 25C), `Cells` (an array of `RegionCellResource`
  — the streamable sub-cells), `Bounds` (`Aabb`), an atmosphere bias (`DefaultWeatherId` +
  `DayPhaseBias`), a shared `WorldEnvironmentProfileResource`, a
  `WorldPerformanceBudgetResource`, a `WeavePotency` (the fading-Weave dial, below), and
  `Neighbours` (region ids —
  the map/fast-travel adjacency). `RegionDatabase` indexes them (mirrors `WeatherDatabase`); the
  save header reads the active region's `DisplayName` by id. New region = a `.tres`, no code.
- **The fading Weave (Phase 29.5E)** — `RegionResource.WeavePotency` (0..1, dev-tunable) feeds a
  global `Weave` static (mirrors `SafeZones`), set on world build and on every region transition
  alongside the safe-zone. Pure `WeaveMath` bends a cast by potency: as the Weave fails, **ordinary**
  magic weakens and costs more while **corrupted** magic (gated above `Untainted`) strengthens and
  cheapens — read by `SpellcastingComponent` into both damage and mana cost. Potency is region data,
  so it restores with the region on load (no extra save state). The `weave` console command
  inspects/tunes it; the two sandbox regions contrast (Ember Crown 1.0, Frostfang Reach 0.5).
- **`RegionCellResource`** (`[GlobalClass]`, a sub-resource of the region): `Id` (`<region>.<cell>`),
  `ScenePath`, `Center` (world position), `SafeRadius`, required `WorldCellPresentationResource`,
  and optional `WorldBiomeScatterResource`. The lightweight metadata the streamer reads without
  instancing the scene. Scatter layers are deterministic cosmetic MultiMeshes; exclusion circles
  and the presentation road mask keep gameplay space clear. Each layer can pair its detailed mesh
  with a reduced cone/box HLOD proxy and overlapping visibility ranges.
- **`RegionStreamer`** (`Node3D`, `Pausable`, built by the bootstrap): **every cell of the active
  region is resident** (38M2, maintainer direction). Each frame it enqueues any cell not yet in the
  tree; there is no distance test and no unload path during play. Until 38M2 a cell loaded inside its
  `LoadRadius` and was freed past `LoadRadius + UnloadMargin` through a pure `StreamDecision`;
  the current regions remain within explicit authored/runtime node and scatter budgets, so residency
  costs less than the seams distance-streaming bought — a routine walking an unloaded cell, a district popping in as the
  player crests a road, and a class of bug that only reproduces from one approach direction. The
  radius, the margin, `StreamDecision` and its tests were all deleted with the rule.
  ⚠️ **Both regions cannot be resident together** — Frostfang's `dragon_roost` (25, 0, -20) and
  `ancient_aerie` (25, 0, -110) share coordinate space with the Ember Crown's `arena` (55, 0, -10)
  and `wilds_north` (0, 0, -65), so the two would load inside each other. Whole-realm residency is a
  world-layout decision (the 2026-08-28 layout rebuild), not a streaming one.
  Loads now pass through explicit **queued → threaded request → ready → instanced** stages. Scene
  I/O and dependency loading happen through `ResourceLoader.LoadThreadedRequest`; the main thread
  polls completion and instances only the region budget's `MaxCellInstantiationsPerFrame`.
  `MaxConcurrentLoadRequests` controls parallel I/O and drops to one when global static memory is
  above the region limit, preserving forward progress instead of deadlocking the loading screen.
  It publishes `RegionCellLoadedEvent`/
  `RegionCellUnloadedEvent` — the seam Phase 25D's persistence hooks. The procedural sandbox is the
  always-loaded base; the streamer manages only the region's authored `Cells`. For a hard transition
  (25C) the bootstrap calls `UnloadAll()` (free every loaded cell + clear the queue) then
  `Configure(destination)` to re-target it without orphaning the old region's cells. Because
  `Configure` is called at **both** places the active region changes, it also records
  `ActiveRegionId` — the cheapest honest answer to "where is the player standing" for systems
  that need it, and what the encounter region gate reads (Phase 34.5B).
  Each loaded root also receives its seam-neutral presentation skin and optional biome scatter.
  The presentation is an indexed CPU-built heightfield with edge-flat topology and shader blending
  driven by world-space noise, height, slope, road, and roughness. `WorldVisibilityManager` leaves
  gameplay resident but culls cosmetic scatter cells beyond the authored distance; GeometryInstance
  ranges cross-fade detailed and HLOD batches. `WorldPerformanceMonitor` samples expanded runtime nodes, scatter instances, draw calls, static
  memory and frame time against the region budget; the validator separately gates authored `.tscn`
  node counts and requested scatter counts before runtime.
- **Hard transitions (25C)** — a `RegionTransitionComponent` (an `InteractableComponent` carrying a
  `TargetRegionId`) publishes a `RegionTransitionRequestedEvent`; `WorldSessionDirector` handles it: enter
  `GameState.Loading` (the `LoadingScreen` overlay shows on that state), re-target the streamer,
  teleport the player to the destination `SpawnPoint`, rebuild the neighbour portals, request a
  region-boundary autosave (`AutosaveService.RequestRegionChangeAutosave`), then hold Loading for a
  short settle (so the destination cells stream in) before returning to `Playing`. Portals are spawned
  by the bootstrap per `RegionResource.Neighbours`, so a reciprocal link is a two-way door with no
  per-scene authoring — at `RegionResource.PortalPoint` when the region authors one (38M2, which is
  how the Ember Crown's door stands at the Crossway gate rather than beside the player's spawn),
  otherwise a few metres in front of `SpawnPoint`. **The Crossway toll (38M) is charged here**, in
  `RegionSetup.PayToll`, because this handler is where the portal and `region goto` converge.
  Drive it from F1 with `region goto <id>`.
- **Cell persistence (25D)** — `CellPersistenceDirector` (`Node`, `ISaveable`, built before the
  streamer) keeps streamed-in actors that carry a `PersistentId` remembering themselves across cell
  unload/reload. On `RegionCellLoadedEvent` it walks the cell for persistent `IEntity` actors and
  reconciles them against a session ledger: ids in its `_removed` set are culled (dead enemies stay
  dead, looted pickups stay gone), survivors get snapshotted `ISaveable`-component state re-applied.
  Removal is detected uniformly via the body's `TreeExiting` (suppressed while the cell is
  unloading, so the streamer's own frees don't count). It snapshots survivors on
  `RegionCellUnloadedEvent` and is itself `ISaveable` (`cell_persistence`: a removed-id list + a
  component-state map), so the ledger round-trips through a full save/load. Authored actors stay in
  the cell `.tscn`; nothing about the authoring model changes.
- **World map (25E)** — `MapService` (`Node`, `ISaveable`, `map`) tracks discovery as two id sets:
  regions (revealed on entry — the bootstrap calls `DiscoverRegion` for the start region and each
  transition destination) and POIs (revealed when a cell first streams in, via
  `RegionCellLoadedEvent`). Marker positions are re-resolved from `RegionDatabase` at read time
  (region = `SpawnPoint`, POI = cell `Center`), so only the id sets persist; a `Revision` counter
  tells the UI when to rebuild. `MapScreen` (a non-modal `UiTheme` overlay toggled with `M`) plots
  discovered regions/POIs/player on a top-down `MapView` (pure-shape `_Draw`, north = −Z up) with a
  name legend; undiscovered regions are not drawn (fog).
- **HUD compass (25F)** — `CompassStrip` (a self-drawn `Control` owned by `GameHud`) is the live
  wayfinding strip: it reads the player's facing and `_Draw`s cardinal headings, dim ticks for the
  `MapService` POIs, and a bright marker for the active quest objective within a ±90° window. The
  pure angle maths is `CompassMath` (north = −Z, clockwise to +X; unit-tested); the objective is
  resolved to a live world target by `ObjectiveLocator` per objective type — Kill → nearest enemy in
  the `objective.enemy` group, Collect → nearest pickup in `objective.pickup` (the seam extends to
  Talk/Reach). Cardinal letters go through the `Loc` layer.
- **Fast travel (25G)** — `FastTravelService` (`Node`, `ISaveable`, `fasttravel`) is the network of
  attuned travel nodes; a `TravelNodeComponent` (a placed `InteractableComponent`) records itself
  (id + label + region + landing position) on interact. The (now modal) `MapScreen` lists a button per
  node; selecting one publishes a `FastTravelRequestedEvent`. The bootstrap reuses the 25C hard-load
  via a shared `PerformRegionLoad(destination, landing, message)` — the neighbour portals land at the
  region `SpawnPoint`, fast travel lands at the node position (same-region jumps allowed); the streamer
  only swaps regions when the destination differs, and clock/weather are untouched so arrival respects
  the current time. The `travel` dev command drives jumps headlessly.
  **A jump costs gold since 38C** (`Economy.TravelFee` / `Economy.TravelCosts`): free to a holding the
  player owns — matched through `PropertyResource.TravelNodeId` — a small fee within a region and a
  larger one across realms. ⚠️ It is charged in `WorldSessionDirector.OnFastTravelRequested`, **not** at the
  map screen, because that handler is where the map button and the `travel goto` command converge;
  gating only the UI would leave the console a free ride. `MapScreen` labels each button with the same
  `TravelCosts.FeeFor` call and greys an unaffordable one, so the price shown is the price taken.
- **Scene/world-partition convention** (for Phases 27/44 authoring): a region's sub-cell scenes
  live under `scenes/regions/<region>/<cell>.tscn`, where `<region>` is the id minus its
  `region.` prefix (e.g. `scenes/regions/ember_crown/waystone.tscn` for cell `ember_crown.waystone`).
  Keep each cell self-contained (its own static geometry, navmesh, props, spawn markers) at local
  origin (the streamer places the instance at the cell `Center`), positioned within the region's
  `Bounds`. Persistent actors in a cell carry a `PersistentId` so they restore via the
  `PersistentSpawnDirector` when the cell reloads (25D). `region.ember_crown` is **sixteen cells** and
  `region.frostfang_reach` **ten** (they were ten and five before the 2026-08-29 geography overhaul;
  eleven of the twenty-six are transitional country with no gameplay beat in them at all). Every one
  of the original fifteen had its physical layout rebuilt in the 2026-08-28 layout rebuild
  and each now has a distinct spatial identity rather than a shared road-and-plaza formula:
  `town_hub` (the Kingsway, an S-curve the square hangs off rather than a road through it),
  `embermarket` (the Coilyard — a compressed gate, a bent lane, a reveal into an off-centre yard, a
  cart-scaled east gate and a service alley, carrying **twelve merchants**: ten residents reached by
  conversation and two travellers on `VendorComponent`s, since only that route hides an away
  merchant), `crossway_post` (a dog-leg crossing that cannot be run), `emberdeep_mine` (a loop —
  defile, weighing yard, cut, pit head, spoil track), `tarn_landing` (a spit inside an L-shaped bay),
  `hollowreach` (a street between two flooded channels with a walled Hollow off it),
  `ashfall_homestead` (a hexagonal plot entered at a clipped corner), `wilds_north` (a forked road
  with the ruin between the branches), `wilds_west` (a rock corrie with one throat) and `arena` (a
  south gate where the road actually arrives, plus a collapsed north breach). Cells abut exactly so
  navmesh patches edge-connect; ⚠️ **the lattice is generated and checked by `tools/gen_regions.py`
  from `tools/region_spec_<region>.py` since the 2026-08-29 geography overhaul, and the ground is one
  region-wide `WorldHeightfield` rather than a slab per cell — two abutting cells sample the same
  continuous function, so seams match by construction instead of by flattening. The old arithmetic is
  arithmetic, not taste** — The 2026-08-28 layout rebuild found three seams a road pointed at and no cell opened onto.
- **Safe areas are a list** (38K). `SafeZones` holds the region's own `SafeZoneCenter`/`SafeZoneRadius`
  bubble plus one per cell that authors a `RegionCellResource.SafeRadius` (`0` = not a safe area),
  rebuilt by `WorldSessionDirector` on world build and on every region transition. It exists
  because a settlement can be more than one cell: widening the single bubble to cover a district a
  street away also smothers the encounters around the wilds cells. ⚠️ `SafeZones.Set` **replaces** and
  runs before the per-cell `Add`s, so a transition cannot leave the previous realm's districts
  protecting ground here.
- **Navmesh & enemy pathing** (Phase 27A): each cell wraps its walkable geometry in a
  `NavigationRegion3D` with a `CellNavBaker` (`src/World`) child that **bakes at stream-in** from the
  cell's **static colliders** (`NavigationMesh.geometry_parsed_geometry_type = StaticColliders` —
  runtime *mesh* parsing is avoided; it forces a GPU→CPU readback hitch, the 25.5B anti-hitch
  concern), so every cell needs a floor collider plus a collider on each obstacle. Enemies carry a
  `NavigationAgent3D` (`EnemyFactory`); `EnemyAIComponent.MoveTowards` steers toward the agent's next
  path corner (arrival judged against the final target via the pure `Movement.PathSteering`), and
  **falls back to straight-line steering** when no navmesh is reachable, so the navmesh-less
  procedural sandbox and any cell without a Nav region still work. Bake is async (`bake_finished`);
  until it lands, agents simply straight-line, so there is no hard ordering dependency.

### 2.6h-3 Terrain surface, water and world QA (`src/World`, the 2026-08-30 quality pass)

Everything here reads the one region `WorldHeightfield` described above; none of it is a second
source of truth about the ground.

- **Terrain material** — `WorldTerrainLayerResource` (`data/terrain_layers/`, 20 of them) is a
  *substance*: two palette tones, the grain they vary at, contrast, micro-relief and roughness.
  `WorldBiomeProfileResource` (`data/biomes/`, 10) is a *place*: six semantic slots — `Ground`,
  `Sparse`, `Rock` (by slope), `Cap` (by height, shed off steep faces), `Road` (from the path mask in
  vertex red) and `Shore` (below a waterline) — plus the bands that place them. A region names a
  default; a cell overrides it. `assets/shaders/world/world_surface.gdshader` blends the six.
  ⚠️ **There are no ground textures and that is `docs/ART_STYLE.md` §4/§6.3, not a shortcut** — the
  model set this sits under ships with zero texture images, so a PBR ground pack would make the
  terrain the only photographed thing in a hand-painted world. Consequences worth knowing: there is
  no tile, so no anti-tiling work and no VRAM; and every frequency in the shader needs a distance
  fade, because a sub-metre field at a grazing angle aliases into moiré rather than reading as detail.
- **Landform naturalisation** — `WorldLandformResource.Irregularity` warps a form's *boundary* by a
  noise field scaled to its own size, keeping its authored place, height and grade. The generator
  applies it to natural geography and never to anything levelling. ⚠️ It early-outs outside the
  transition band; without that it is three seconds of region load, because `Height()` runs a hundred
  thousand times per cell.
- **Distant landscape** — `WorldRegionBackdrop` is one non-colliding mesh that continues the region
  field outward into ridged mountains. It replaced 26 cylinder-cones on a circle, which were visible
  in every wide shot and did not solve the problem they existed for: the terrain still *ended* at the
  lattice edge.
- **Water** — `WorldWaterResource` on a cell declares a body; `WorldWater` pools them in world space
  and owns **Embervale's non-swimming safety contract** (wade under 1.1 m, the land refuses above it,
  `WorldRecovery` retrieves above 1.9 m); `WorldCellWater` draws the surface as a grid whose
  per-vertex depth comes from the heightfield, so the shoreline is the terrain's own contour.
  ⚠️ **A water surface authored as a mesh in a `.tscn` is invisible to the safety system** and is
  forbidden.
- **`WorldRecovery`** (one node on the streamer, all regions) — the standing promise that no ground
  is a dead end: deep water, or a pit whose local flood fill finds no walkable exit, puts the player
  back on the last dry ground they stood on. It recovers; it does not kill.
- **`WorldTraversalAnalysis`** — engine-free. Sweeps a region's lattice on a 3 m grid as a
  **directed** graph (descending within a survivable fall is a different edge from climbing within
  the 0.7 grade) and reports ground the player can reach and cannot leave. `--validate` fails only on
  *shallow* traps; a deep one is authored drama that `WorldRecovery` owns.
- **Scatter placement** — `BiomeScatterLayerResource` gained `MaxSlope`, `HeightRange`, `Clumping`
  and `Saturation`, and the HLOD proxy tier is now the source mesh at a fraction of the density
  rather than a cone or a box. `WorldScatterPlanner`'s spacing test is bucketed, and its test order
  is a performance decision: the O(accepted) one goes last.
- **Region atmosphere** — `WorldEnvironmentProfileResource.SunTint`/`SunEnergyScale`/`HazeColor`/
  `HazeScale`, applied by `SkyController` on top of the day/night and weather curves.
  ⚠️ **Palette alone cannot make a region look like a different place**: neutral-grey bedrock under a
  golden-hour sun *is* warm tan sand, which is what the Clan Hold rendered as while every colour in
  its spec was cold.
- **Tools** — `tools/world_quality_check.py` orchestrates all sixteen gates;
  `tools/world_perf_probe.gd` measures draw calls, primitives, median frame time and video memory per
  cell; `tools/region_spec_template.py` is the self-checking starter for a new region.
  `docs/WORLD_AUTHORING.md` is the authority.

### 2.6i Crafting (`src/Crafting`)

- **Recipe content** — `CraftingRecipeResource` (`[GlobalClass]`, `data/recipes/*.tres`): a
  `Station` (`CraftingStationType` Hand/Forge/Workbench/Alchemy/Cooking — Hand = anywhere), an
  untyped `Ingredients` array of `RecipeIngredient` sub-resources (item id + qty, read via
  `IngredientList()` by element cast — same pattern as `LootTable.Entries`), an `OutputItemId`/
  `OutputQuantity`, and an `OutputRarity`. `RecipeDatabase` indexes them.
- **`CraftingComponent`** (`EntityComponent`, `ISaveable`, on the player) — the known-recipe set
  (seeded from `StartingRecipeIds`, learnable via `Learn`), plus `CanCraft`/`Craft`: validates
  station + ingredients, consumes inputs from the sibling `InventoryComponent`, adds the output.
  Equippable output with `OutputRarity` > Common rolls affixes via `LootGenerator.RollAffixed`
  (crafting feeds the same gear pipeline as loot). Known recipes persist (`crafting:{RuntimeId}`).
  **Deconstruction** is the inverse: `CanDeconstruct`/`Deconstruct` reverse the station's recipe for
  an item (`DeconstructionRecipe`), consuming it for a floored fraction of its materials
  (`Deconstruction.RecoveredQuantity`, < craft cost so it can't duplicate) plus XP
  (`Deconstruction.Xp`, by item value + rarity) via the player's `ProgressionComponent`. No new
  content — it reuses the recipe graph; fires `ItemDeconstructedEvent`.
- **Stations & UI** — `CraftingStationComponent` (`InteractableComponent`) publishes
  `CraftingStationOpenedEvent` on `E`; `CraftingStationFactory` builds the world block.
  `CraftingPanel` (`src/UI`, modal, built through `UiTheme`) lists known recipes matching the
  station (+ `Hand`), with live have/need ingredient lines and a Craft button; `E` closes it
  (a `_justOpened` guard stops the opening press from also closing it). A **Craft / Salvage** tab
  toggle switches to the deconstruction list (inventory items with a station recipe, each showing
  its material + XP yield and a Deconstruct button). Events:
  `src/Crafting/CraftingEvents.cs`. Sandbox: a forge/workbench/alchemy yard west of spawn; the
  player knows six recipes forming an ore→ingot→sword chain.

### 2.6j Factions (`src/Factions`)

- **Faction content** — `FactionResource` (`[GlobalClass]`, `data/factions/*.tres`): `Id`,
  `DisplayName`, the player's `DefaultReputation` (≈ -100..100), a `HostileThreshold`
  (`ReputationTier`), a `KillReputationPenalty`, and `Enemies`/`Allies` faction-id lists.
  `FactionDatabase` indexes them. `ReputationTier` (Hated→Allied, low→high so comparisons
  work) is derived from a numeric value by `ReputationTiers.Of` (also `Label`/`Color`).
- **`FactionComponent`** (`EntityComponent`) tags an actor with a `FactionId` (goblins, the
  elder). Static archetype tag — read by the AI + reputation, **not persisted**.
- **`ReputationComponent`** (`EntityComponent`, `ISaveable`, `reputation:{RuntimeId}`, on the
  player) — seeds standings from faction defaults; on an `EntityDiedEvent` the player caused,
  shifts standing with the slain faction (down) and propagates through its web (enemies up,
  allies down). `Get`/`TierOf`/`IsHostile`/`Add`; raises `ReputationChangedEvent`; persists
  per-faction values.
- **Consequence** — `EnemyAIComponent` engages the player only while
  `ReputationComponent.IsHostile(factionId)` (standing at/below the faction's hostile tier);
  an unfactioned actor defaults hostile, and a direct hit sets a transient `_provoked` flag
  for self-defence regardless of standing. So reputation actually changes who fights you.
- **UI/debug** — the character screen has a **REPUTATION** section; debug key `K` raises goblin
  standing. Sandbox factions: `faction.goblins` (hostile, enemy of villagers) and
  `faction.villagers` (the elder). Dialogue/quest hooks keyed to standing are a future add-on
  over `ReputationComponent`.

### 2.6k World events (`src/World`)

The richer *named-event* layer over the ambient `EncounterDirector` (§2.6h): discrete,
announced events with an objective, time limit and rewards.

- **Event content** — `WorldEventResource` (`[GlobalClass]`, `data/world_events/*.tres`): a
  locale `NameKey`, `WorldEventKind` (`0` Raid / `1` Cache / `2` Hunt), `SelectionWeight`,
  `CooldownSeconds`,
  `TimeLimitSeconds`, per-`DayPhase` allow flags, spawn knobs (`EnemyTemplateId`, `MinCount`/
  `MaxCount`, `HealthMultiplier` for a champion, or `CacheItemId`/`CacheQuantity`), and rewards
  (`XpReward`, `GoldReward`, `RewardItemId`/`Quantity`, `FactionRewardId`/`Amount`).
  `WorldEventDatabase` indexes them.
- **`WorldEventDirector`** (`Node3D`, `Pausable`) — rolls one eligible event on a cadence
  (phase + weight + per-event cooldown), runs **one at a time**, spawns via `EnemyFactory` /
  `ItemPickupFactory` near the player, tracks the objective off `EntityDiedEvent` (by tracked
  runtime id) / `ItemPickedUpEvent`, enforces the time limit (fail + despawn raiders on
  expiry), and on success grants rewards through the player's `ProgressionComponent` /
  `InventoryComponent` / `ReputationComponent`. **Not persisted** (emergent, like encounters);
  the rewards persist via the saved components. `WorldEvent` is the runtime tracker; `Active`
  feeds the HUD. Events: `WorldEventStartedEvent`/`WorldEventProgressEvent`/`WorldEventEndedEvent`.

### 2.6l Companions (`src/Companions`, Phase 32)

The party: recruitable allies that fight on the player's team, take orders, hold a loyalty
standing, and persist. Built entirely on the existing character stack — a companion is the
*same* kind of actor as the player and the enemies, with a different brain.

- **Companion content** — `CompanionResource` (`[GlobalClass]`, `data/companions/*.tres`):
  `Id` (`companion.*`), `NameKey`/`TitleKey` (`Loc` keys), the build paths
  (`AttributesPath`/`WeaponPath`/`ModelPath`), `FactionId`, `KnownSpellIds` (non-empty ⇒ the
  actor gets a `SpellcastingComponent`, i.e. a caster companion), the follower envelope
  (`FollowDistance`/`EngageRadius`/`AttackRange`/`LeashRadius`), and the loyalty/content ids
  (`StartingLoyalty`, `LoyaltyQuestReward`, `LoyaltyQuestId`, `DialogueId`).
  `CompanionDatabase` indexes them; `CompanionRegistry` seeds its id→builder archetypes straight
  from the database (mirroring `EnemyTemplateRegistry`), so **a new companion is a `.tres`**.
- **`CompanionFactory`** builds the actor from the resource: collision, model, `NavigationAgent3D`,
  `StatsComponent`, `CombatComponent` (**team 0** — the player's team, which is what makes the
  shared `Hitbox` friendly-fire rule protect it), `LocomotionComponent`, hurt/hitbox +
  `MeleeWeaponComponent`, status effects, animation, faction, its own `DialogueComponent`, and
  the two companion components below. `PersistentId` = the companion id, so every component's
  state persists under a stable key.
- **`CompanionAIComponent`** (`EntityComponent`, `ISaveable`) — the follower brain, an
  *anchor/leash* loop rather than a patrol FSM. Each tick it resolves its anchor (its
  `CompanionFormation` slot behind the player, or the spot it was told to hold), picks a hostile,
  and hands both distances to the pure `CompanionDecision` → `Hold`/`Regroup`/`Chase`/`Attack`.
  The **leash beats the fight**: dragged past it, the companion breaks off and regroups, so a
  running enemy can never kite the party away from the player. Movement reuses the same
  `PathSteering` navmesh rule as `EnemyAIComponent`. **Assist focus:** whatever the player is
  locked onto (`LockOnComponent`) wins target selection outright. **Standing gates the
  proximity scan** (Phase 34.5B): every archetype is built on the hostile team, so team alone
  would have a companion open fire on a faction the player is at peace with. A candidate with a
  `FactionComponent` is only picked up when the player's `ReputationComponent.IsHostile` agrees —
  the same rule `EnemyAIComponent.PlayerIsTarget` uses. The lock-on focus and the
  damage-reaction path are deliberately **not** gated, so assisting a fight the player starts
  and defending one they didn't both still work. Out of health it goes
  **Downed, never lost** — it drops out and stands back up on a timer at a fraction of max HP,
  because companions carry quests. It persists its hold anchor and downed/recovery countdown
  (the roster can't see either).
- **Orders** — `CompanionStance` (Follow/Hold/Engage) with the pure `CompanionOrders` supplying
  the cycle and each order's leash/scan envelope (engage stretches both; hold tightens the scan).
  One key (`C` / D-pad right) cycles the whole band; an engage order **stands itself down** once
  the fighting stops.
- **`CompanionRoster`** (`Node`, `ISaveable` `"companions"`, `ServiceLocator`-registered) — the
  single entry point: `Recruit`/`RecruitAt`/`Dismiss`/`SetStance`/`CycleOrder`, plus the loyalty
  ledger (`LoyaltyOf`/`TierOf`/`AddLoyalty`/`SetLoyalty`). It persists the party (ids, stances,
  **transforms**) and loyalty **for every companion it has recorded, recruited or not** —
  dismissing someone must not wipe the history between you. Loading is a **reconcile**, not a
  rebuild (pure `CompanionPartyReconcile`): survivors keep their actor and are moved; only
  genuine newcomers are built. `RegroupNow()` cuts the following band to formation after a region
  hard-load, and a periodic catch-up teleport covers anything else that moves the world.
- **Loyalty** — pure `CompanionLoyalty`: 0–100 clamped, `LoyaltyTier` Wary→Sworn (append-only),
  a `Loc` name key per tier, and a `CombatBonus` per tier. `CompanionLoyaltyComponent` is a
  *projection*: it applies the current tier's bonus to the companion's power/health as stat
  modifiers and re-applies on `CompanionLoyaltyTierChangedEvent`. It stores nothing — the roster
  stays the source of truth.
- **Dialogue integration** — effects `RecruitCompanion` / `DismissCompanion` /
  `AddCompanionLoyalty` and conditions `CompanionRecruited` / `CompanionNotRecruited` /
  `CompanionLoyaltyAtLeast`, with `CompanionArg` parsing the `<companionId>:<amount>` form. So a
  companion's whole arc is authored content. `CompanionRecruiterComponent` sits on the world NPC
  and hides it (visibility **and** collision layer) while its companion travels with the player,
  restoring it on dismissal so the same conversation can recruit them again.
- **Events** — `CompanionRecruited`/`Dismissed`/`StanceChanged`/`OrderIssued`/`StateChanged`/
  `Downed`/`LoyaltyChanged`/`LoyaltyTierChanged`. The HUD's `PartyWidget` (self-hiding while the
  party is empty) and the toast feed react to these; nothing polls the roster.
- **Content today** — Kael (`companion.kael`): recruit quest `quest.kael.oath`, loyalty quest
  `quest.kael.brother`, conversation `dialogue.kael`, and an NPC in the Ember Crown hub. The
  other four LORE companions are Beta content. *No romance* — friendship/brotherhood (LORE).
- **Dev console** — `companion <list|recruit|dismiss|stance|order|loyalty>`; the `party` repro
  scenario runs a deterministic party-in-the-field.

### 2.6m Economy (`src/Economy`, Phase 38)

Shops, services, prices and the things that move them. `docs/DESIGN.md` §6 owns the *intent* (what
gold is for, where the sinks are); this section owns the mechanism, and the two deliberately do not
restate each other.

**One price authority, and everything hangs off it.** `ShopPricing` is pure and Godot-free — the
test project throws constructing any Godot object, which is why every parameter is a plain value.
It spreads over `ItemInstance.Value`, which already folds in rarity and affix count, so rolled loot
is priced for free and no second table can drift.

- `BuyPrice` rounds **up** and floors at **1**; its markup is clamped to **`>= 1`**.
- `SellPrice` rounds **down** and floors at **0**; its fraction is clamped to **`0..1`**.
- Those two clamps are why **`sell <= value <= buy` holds for any authored spread** — a money
  printer is unauthorable rather than merely un-authored. ⚠️ What they do **not** stop is a *free
  round trip*: a spread narrowed to nothing is frictionless churn, and only `ContentValidator`'s
  margin rule (`ValidateShopTrade`) keeps buying-and-selling-back costing something.

⚠️ **THE MULTIPLICATION ORDER IS LOAD-BEARING AND IT IS WRITTEN DOWN HERE BECAUSE NOTHING ELSE
STATES IT.**

```
MarkupFor(markup, tier, specialty, haggled)   = markup   × PriceMultiplierFor(tier)
                                                         × SpecialtyBuyDiscount(0.95)
                                                         × HaggleRules.BuyFactor(0.90)
SellFractionFor(fraction, specialty, haggled) = fraction × SpecialtySellBonus(1.25)
                                                         × HaggleRules.SellFactor(1.10)
```

Float multiplication is not associative, and `PriceBreakdown` re-runs `BuyPrice`/`SellPrice` after
each factor to print a running total. It accumulates in **exactly this sequence**, which is the only
reason its last line equals the charged total rather than landing a gold away from it. Reordering
either line is a silent off-by-one that one test (`BuyLastLineIsTheTotal`) catches and no reading
would. ⚠️ **Standing is absent from the sell side on purpose** (`MarkupFor`'s comment says why: with
both clamps in play a generous fraction converges on `sell == buy`). A **haggle** is the one thing
allowed to move it, because the ledger bounds it to one merchant for one day.

**What a good is worth *here* — the local-value layer** (38G). `RegionDemand.ValueAt` moves the
*value*, not the spread: a cell's authored `Surplus` tags price at `0.62` and its `Demand` tags at
`1.50`, and a tag in neither prices at the realm reference. ⚠️ **Symmetry is structural rather than a
rule to remember — a value has no sides**, so both halves of one counter spread over the same local
number and `sell <= LOCAL value <= buy` survives untouched *at a shop*, while two shops in different
places can finally disagree about a sack of grain. This is the only thing in the game that can make
a carry between settlements pay. ⚠️ `ShopResource.CellId` is **empty by default and empty means
par**, so a shop that forgets it prices as though it stood in town and only `--economy` shows it.

- `ShopResource.LocalValue(value, tags, view)` is the call **every** price must go through.
- `ShopResource.LocalQuote(value, tags)` answers the same question *with its reason* — the value,
  the tag the cell had an opinion about, and whether that opinion is a shock. It resolves the live
  tags **once**, so a caller cannot run the match twice against two different days.
- `PriceView` (`Today` / `Peak` / `Trough`) exists for `ContentValidator`: a rule proved only
  against today's prices is a rule that breaks on a day nobody was playing on.

**A supply shock is a temporary list edit, not a multiplier** (38T). `SupplyShockRules.Apply` moves
a tag from one of the cell's lists to the other for a bounded number of days and hands the result to
`RegionDemand.ValueAt` unchanged. Because it adds no factor, the clamps, the specialty premium, the
standing ramp, the haggle and 38F's `NoCombinationOfMultipliersLetsSellingBeatBuying` sweep all
cover it with no new argument — which is the answer to "what is this a spread over?" being *nothing;
it is not a spread*.

**`PriceBreakdown` is the charge path, not a commentary on it** (38U). It returns ordered
`PriceLine`s plus a `Total`, and `VendorPanel`, `CraftingPanel` and `MapScreen` all display *and
charge* that `Total`. A display-only breakdown beside the shipped expression would be two
expressions of one number; `PriceTooltip` renders it, and lives in `src/UI` because
`PriceBreakdown` may not touch Godot. `PriceBreakdown.AllKeys` is the declared locale contract that
`--validate` walks — deliberately the declared set rather than the reachable one, because a shock
line and a glutted stack are unreachable at the town square.

**The seven economy nodes, all `ISaveable`, built in `GameSession.Build`:**

| Node | Holds | Derives |
| --- | --- | --- |
| `ShopStockService` | stock remaining, purses, absorption, investment rungs | restock due-ness from the day |
| `ContrabandImpound` | what the wardens took | the fine, from `ContrabandLaw` |
| `ConsignmentLedger` | listings and their stamped day | whether one has sold |
| `ContractLedger` | **only what the player filled** | the whole board rotation, from the day |
| `WagerLedger` | **only throws spent today** | win/loss, from (day, throw) |
| `HaggleLedger` | **only that the player asked** | the answer, from (day, shop) |
| `SupplyShockService` | the active window **and** what the player hauled in | the roll, from (day, cell) |

⚠️ **DERIVE, THEN BOUND — TWO MECHANISMS, NEITHER SUBSTITUTING FOR THE OTHER.** Everything a
quickload could otherwise reroll is a pure function of the day, so a reload **replays** it; what
stops it being farmed is a ledger storing what the player *did*, never what was offered or what the
answer was. **Storing the outcome is the obvious shape and the one that rots.**
⚠️ `string.GetHashCode()` is randomised per process — `StableRoll` is a hand-written FNV-1a, and the
derived rolls are pinned by hard-coded across-process strings for exactly that reason.
⚠️ `SupplyShockService` is the first that needed **both** halves: the roll is derived, but a player
can end a shortage early by hauling goods in, and no clock can derive that.

**Services** (`ServiceResource` + `ServiceKind` + one `ServiceComponent`, 38D). Thirteen kinds branch
in one component rather than thirteen classes; `ServiceRules` is the pure half and
`ShopPricing.ServicePrice` the price, so a merchant and an innkeeper of the same faction move on one
discount ramp. ⚠️ **Two kinds are charged *after* their verb and every other one before**: a
commission fails on a full pack and a hire fails on a full party, and only the commission needs a
rollback. ⚠️ **A service can be fired from a conversation, except a `Bank`** — `DialogueEffect.
OpenService` runs the whole battery through `TryUse`, but a bank opens the *host entity's* inventory
and a conversation has no host entity. ⚠️ **A world interaction prompt is not a `Control`**, so a
service price that standing moved says so inline rather than on hover.

**Validation.** The economy battery is roughly 110 refusals across ~24 functions in
`ContentValidator` — see §4.1. ⚠️ **Its per-rule negative tests live in `tools/negative_tests.py`**
(38V): 42 cases that each break authored data, assert the *expected* refusal fired, and restore.
Run it after touching any economy rule or any authored price — a rule proven once decays as the data
moves under it, which is how `ValidateShopTrade`'s band tightened twice after its original proof.
`--economy` prints the realm's buy-low/sell-high table (the same `EconomyReport.Arbitrage` the
`economy` dev command prints) and is an observation rather than a gate.

### 2.7 Save (`src/Save`)

> 📖 **[`SAVE_FORMAT.md`](SAVE_FORMAT.md) is the contract** — the on-disk layout, the `SaveId` rules,
> **what is deliberately not saved**, the failure policy, and what to check before changing any of
> it. This section is the shape of the code; that one is what the bytes mean and what you can break.

- **`ISaveable`** — `SaveId`, `Godot.Collections.Dictionary Save()`,
  `void Load(dict)`. State exchanged as a Godot `Dictionary` → serializes to JSON
  with no reflection.
- **`SaveManager`** (autoload) — `Register/Unregister`, `SaveGame(slot)` /
  `LoadGame(slot)` to `user://saves/<slot>.json` in a versioned envelope
  (`{version, timestamp, objects: {SaveId: state}}`). On load, each live
  saveable pulls its own entry by `SaveId`. **Robustness guarantees:** writes are
  **atomic** (staged to `<slot>.json.tmp` then renamed, so a crash mid-write never
  truncates a good save); each `Save()`/`Load()` is wrapped so one throwing component
  is logged and skipped rather than corrupting/aborting the whole file; the envelope
  `version` is checked through a `TryMigrate` seam (a *newer*-than-known file is
  refused, an older one is upgraded step-by-step — no steps exist yet at v1); and load
  warns about both entries with **no live claimant** (orphaned state) and live
  saveables with **no saved entry**.

- **Stable identity (`PersistentId`):** an `ISaveable` component's `SaveId` comes from
  `EntityComponent.SaveKey(prefix)`, which prefers the owner's stable
  `IEntity.PersistentId` (e.g. the player is `PersistentId = "player"`, so its
  components save as `stats:player`, `inventory:player`, …) and only falls back to the
  volatile `RuntimeId` for transient actors. World singletons keep fixed, colon-free
  keys (`worldclock`, `weather`, `map`, `fasttravel`, `spawns`, `companions`, `tutorial`,
  `cell_persistence`, `bestiary`, `housing`, `shopstock`). Container inventories key off their own
  entity instead (`inventory:ember_crown.guild_vault` for 38D's bank vault, `inventory:ember_crown.cottage_chest`
  for 37B's stash) — the entity's authored `PersistentId` is the whole of that contract.

- **`SaveKeyPolicy` — why transient actors persist nothing** (Phase 25.5A). Components used to
  register with the `SaveManager` *unconditionally*, so transient actors (the training dummy,
  spawned goblins — no `PersistentId`) wrote volatile `stats:<runtimeId>` keys that could never be
  reclaimed after a world rebuild, producing three warning classes at once: *no PersistentId*,
  *no usable entry*, and *orphaned state*. The fix is a pure, Godot-free
  `SaveKeyPolicy` (`ShouldPersist` / `Key` / `IsVolatile`) plus
  `EntityComponent.RegisterSaveable()`, which registers a component **only when its owner has a
  stable `PersistentId`**. So: **components call `RegisterSaveable()` from `OnInitialize`, never
  `SaveManager.Register` directly**; a component-less world service registers itself in
  `_EnterTree` instead. The `savecheck` dev command (`F1`) audits every registered id through
  `IsVolatile` and should always report **0**.

- **Two benign warnings you will still see.** Loading a save made *before* a saveable existed logs
  *"no usable entry for `<id>`; it keeps its current state"* — that is forward-compat, not a bug,
  and it self-heals on the next save. Loading a save made *before* the 25.5A fix warns about its
  legacy `stats:*` keys — stale data, also not a bug. `SaveManager` also warns at write time if two
  saveables share a `SaveId`, which is the one to actually chase.

- **Spawned-actor persistence** — `SaveManager` only restores components of actors **already
  alive**; it can't recreate one missing from a freshly-loaded scene. `PersistentSpawnDirector`
  (`src/Save/`, a `Node` + `ISaveable` `"spawns"`, `ServiceLocator`-registered) closes that gap:
  `Spawn(templateId, persistentId, pos, yaw)` assigns identity and tracks the actor; `Save()`
  writes a manifest (template + id + transform) of the live tracked actors; `Load()` reconciles —
  despawning tracked actors absent from the save and recreating missing ones via
  `PersistentActorRegistry` (template id → builder, mirroring `EnemyTemplateRegistry`). Each
  recreated actor's components restore themselves through `SaveManager`'s **in-flight-load hook**
  (`Register` checks the active snapshot, so an actor that comes online mid-load restores at once).
  Sandbox: a persistent "Supply Cache" (`prop.cache`) east of spawn; dev console `pspawn`/`pdespawn`/
  `plist` exercise it. Ambient mobs/loot stay deliberately transient.

> Caveat: this is the foundation slice — only actors routed through the director persist. Converting
> ambient enemies/loot (with kill/pickup despawn tracking) is intentionally out of scope.

### 2.8 Flow & input

- **`GameState`** + **`GameManager`** — `ChangeState(next)` sets
  `GetTree().Paused = (next == Paused)`, runs with `ProcessMode.Always`, and
  raises `GameStateChangedEvent`. `TogglePause()`, `IsPlaying`.
- **`GameInput`** — action name constants + `EnsureActions()` binding them in
  code (idempotent). Actions: `move_forward/back/left/right`, `jump` (Space),
  `sprint` (Shift), `interact` (E), `attack` (LMB), `block` (RMB), `cast` (Q),
  `cycle_spell` (F), `inventory` (I), `journal` (J), `pause` (Esc).

### 2.8 Audio (`src/Audio`, Phase 31)

Event-driven, bus-based audio. The mixer graph is created in code so it never drifts from the
settings that drive it:

- **`AudioBuses`** (`src/Settings`) — the canonical bus names (Master/Music/SFX/Ambience/UI/
  Voice), shared by the `Settings` volume fields and the mixer. **`AudioBusLayout.Ensure()`**
  creates those buses at boot (routing each to Master) *before* the first `SettingsService.Apply()`,
  so every volume slider takes effect immediately. Bus volumes stay owned by
  `SettingsService.ApplyAudio()` (straight to `AudioServer`) — the director never touches volume.
- **`AudioCueRouting`** — pure (Godot-free, unit-tested) mapping of a cue id to its bus and
  positional flag by prefix (`sfx.`/`step.` → SFX, positional; `music.`/`amb.`/`ui.`/`voice.` → 2D).
  The one naming convention the whole game answers to when requesting a sound.
- **`ProceduralAudio`** + **`AudioLibrary`** — the cue id → `AudioStream` registry. Real CC0/open
  assets (`assets/audio/*.ogg`) are preferred per cue; `ProceduralAudio` synthesizes a placeholder
  fallback so no cue is ever silent. An unknown id resolves to silence + a one-time warning.
- **`AudioDirector : Node`** (ServiceLocator-registered, `ProcessMode.Always` so pause/UI cues
  sound) — consumes `SoundCueRequestedEvent` (combat swings/impacts) and `MusicCueRequestedEvent`
  (narrative beats) and exposes `PlayCue(id[, pos])` for direct callers (UI, footsteps). Plays
  through pooled `PositionalSfxPlayer` (3D) / `OneShotAudioPlayer` (2D) via `NodePool<T>`. Registers
  the shared `AudioLibrary` in the ServiceLocator so the `MusicDirector` reuses the built streams.
- **`MusicDirector : Node` + `MusicStateMachine`** (Phase 31B) — adaptive music. The pure machine
  resolves `Boss > Combat > Safe > Explore`; the director feeds it from EventBus (enemies in
  Combat/Retreat via `EnemyStateChangedEvent`, cleared on `EntityDiedEvent`/freed-body prune; boss
  from `BossEncounterStartedEvent`; safe polled from `SafeZones`) and crossfades two looping players
  (~1.5 s) on the Music bus. Beds are `music.{explore,safe,combat,boss}` cues (real CC0 track or
  procedural pad).
- **`AmbienceDirector` + `AmbienceSelection`** (Phase 31D) — a looping environmental bed on the
  Ambience bus. The pure selector resolves `weather > town > day/night`; the director feeds it from
  `WeatherChangedEvent`/`TimeOfDayChangedEvent` and a polled `SafeZones` "in town" signal, crossfading
  (~2 s). Beds are `amb.{day,night,rain,town}` (real CC0 field recording or procedural noise wash).
- **`FootstepComponent` + `FootstepGait` + `Surfaces`** (Phase 31E) — footstep SFX on the player. The
  pure `FootstepGait` fires a footfall every stride (cadence tracks speed); a downward ray reads the
  floor collider's `surface` node-metadata, mapped by `Surfaces.CueFromTag` to `step.{grass,wood,stone,
  snow}` (stone default). Emitted as a positional `SoundCueRequestedEvent`. Tag a floor `StaticBody3D`
  with a `surface` string to vary it; untagged floors play stone.

### 2.9 Composition roots (`src/Bootstrap`) & UI

`scenes/Main.tscn` is one node with `ApplicationRoot` on it. Everything else is built
in C# under the three roots below. `GameBootstrap` — 1503 lines and twenty-three
responsibilities — was dismantled on 2026-09-03 and deleted.

| Type | Owns |
| --- | --- |
| `ApplicationRoot : Node3D` | the process: the CLI report modes, input actions, localization, the content databases, the audio bus layout, settings, the content validator |
| `GameShellController` | the title screen, and the command-line flags that drive a session for a tool (`--play` and the five capture flags) |
| `SessionLifecycleCoordinator` | New Game, Load, **`DestroySession`**, abort-to-title; the static reset list |
| `GameSession : Node3D` | one playthrough: its scope, and the ordered build of everything in it |
| `WorldHost` / `WorldSessionDirector` | the loaded world: environment, weather, sky, streamer, encounters, portals, region transitions, fast travel |
| `UICompositionRoot` | the HUD and the panels, and binding the player's components into them |
| `PlayerHost` | player spawn, respawn, the persistent world actors |
| `LoadingCoordinator` | the gate every route into the world goes through |
| `DeveloperToolsHost` | console, debug HUD, profiler, integrity checker, the training dummy, the cheats — one gate, one place |
| `SaveHeaderComposer` | the two seams between `SaveManager` and live gameplay |

⚠️ **`GameSession.Build()` is deliberately one ordered list**, not several installers
called in an arbitrary order. The order is load-bearing and always was: the clock
before the NPCs that read it, weather before the sky that reads weather, the audio
director before the music director that reuses its library, cell persistence before
the streamer whose events it wants, the player before the tutorial that watches them.
Splitting it into per-layer installers would hide exactly the constraint that matters,
so each step delegates to the host that owns the thing being built and the sequence
stays readable in one place.

⚠️ **Quitting to the title no longer reloads the scene.** It used to, and `PauseMenu`'s
comment explained why: there was no way to dismantle a world in place. `DestroySession`
is that way now, so a second New Game can start in the same process.

**Scene tree at runtime:**

```text
ApplicationRoot                  (Main.tscn root, Application scope)
├── SessionHost                  SessionLifecycleCoordinator
│   └── GameSession              [per New Game / Load, Session scope]
│       ├── WorldHost            [World scope]
│       │   ├── WorldDirector    streamer, weather, sky, encounters, portals
│       │   └── …world services
│       ├── UIRoot               GameHud + panels
│       ├── PlayerHost           the player actor
│       ├── Loading              the gate
│       ├── DeveloperTools       [dev builds only]
│       └── …session services    clock, autosave, ledgers, map, companions
└── Shell                        GameShellController → MainMenu
```

**UI**

- **`GameHud : CanvasLayer`** — the default in-game HUD: vitals bars, prepared spell +
  cooldown + status line, quest tracker, time/weather, a world-event banner + aimed-target
  nameplate, an interaction prompt, and the `Crosshair`. It reads `InteractionSensor`
  for the nameplate and prompt and `LockOnComponent` for the reticle.
- **`PauseMenu : CanvasLayer`** — `Esc`. Resume / Quick Save / Quick Load / Quit to
  title / Quit. `ProcessMode.Always`; drives `GameManager` pause. Quit-to-title walks
  up to its `SessionLifecycleCoordinator` and asks it to destroy the session.
- **`Notifications` + `Toast`** — top-centre toast feed off discrete events.
- **The panels** — `InventoryPanel`, `SpellbookPanel`, `HotbarPanel`, `QuestLogPanel`,
  `DialoguePanel`, `VendorPanel`, `MapScreen` are held in fields by
  `UICompositionRoot` because something reads them. `CraftingPanel`, `StoragePanel`,
  `AppraisalPanel` and `ContractBoardPanel` are not: each is one instance answering an
  event from anywhere in the world, and a field that is assigned and never read is
  state describing nothing.
- **`UiTheme`** — the shared look. Build new UI through it.

> **UI altitude:** the UI observes gameplay and sends intents; it is never the
> authority. Everything in `UICompositionRoot` is handed its references by the
> session's build order — none of it reaches into the service registry to find
> gameplay for itself.

### 2.10 Debugging tools (`src/Debugging`)

Developer tooling behind function keys (Phase 20); all run `ProcessMode.Always`.

- **`DevConsole`** (`F1`) — an in-game command line: scrollback (`RichTextLabel`) + `LineEdit`
  that dispatches to a `Dictionary<string, ConsoleCommand>`. `DevCommands.RegisterAll` ships the
  built-ins (`spawn`/`give`/`xp`/`heal`/`rep`/`quest`/`time`/`weather`/`event`/`seed`/`repro`/
  `invariants`/`stats`/`help`/`clear`); they reach systems via the `ServiceLocator` (player +
  the registered world directors). Opening it frees the mouse + sets `UiState.MenuOpen`.
  `Execute(line)` runs a command and returns its output (reused by the repro harness).
- **`Invariant`** — `Check(cond, msg)` logs + counts violations (never throws).
  **`WorldIntegrityChecker`** (a `Node`) runs a sanity pass on a timer and on demand
  (`WorldIntegrityChecker.Run()` — the `invariants` command): player registered + core
  components, finite position, resources in range, no leaked orphan nodes (it subtracts the
  `NodePoolCensus.Parked` count so a pool's intentionally-detached working set is not flagged).
- **`ContentValidator`** — boot + on-demand content checks: `validate` (cross-references +
  well-formedness — no dangling ids, unique ids per domain, non-empty loot) and `validate-all`
  (adds graph reachability — dialogue orphans/dead-ends, quest completability, completion-flag
  self-gates, prerequisite cycles). Also runnable headless: `godot --headless --path . -- --validate`
  (exit 0/1).
- **`ProfilerOverlay`** (`F4`) — reads Godot `Performance` monitors (FPS, frame/physics ms, draw
  calls, node/orphan counts, static memory). Idle when hidden.
- **`ReproHarness`** — named scenarios that `GD.Seed` the global RNG then replay a fixed command
  list (`repro <name>`) for deterministic bug repro. New scenario = a one-line entry.
- **Analytics spine** (`src/Analytics`, dev-only) — `AnalyticsSink` (a `Node` wired in the
  bootstrap) subscribes to the EventBus and appends a JSON-lines log to `user://analytics/`
  (deaths by location, quest start/complete, level-ups), plus any `AnalyticsEvent` a system
  publishes for ad-hoc instrumentation. One file per session. Gated on `OS.IsDebugBuild()` — a
  complete no-op in retail builds — and deliberately not `ISaveable` (it is a log, not state).
- All of it is built by **`DeveloperToolsHost`**, and the gate is one `if` in
  `GameSession.Build()`: a capture or exported build never constructs that node at
  all, so there is nothing to accidentally respond to a stray keypress. Quick save and
  quick load are the exception and stay in every build — they are player conveniences,
  so the host processes keys unconditionally and filters the cheats itself.

#### The production / tooling boundary

⚠️ **Godot compiles every `.cs` under the project into ONE assembly.** Any script a
`.tscn` or `.tres` attaches must live there, so a separate `Embervale.EditorTools`
project is not reachable for gameplay code. The separation that *is* reachable is per
build configuration, and `Embervale.csproj` does it with an `EmbervaleTooling`
property — true by default, **false under `ExportRelease`**:

| Excluded when tooling is off | Why |
| --- | --- |
| `addons/godot_mcp/**` and its two NuGet packages | third-party, editor-bound |
| `src/Debugging/*Shots.cs`, `ShotHarness`, `ReproHarness` | screenshot/repro harnesses with no gameplay caller |
| the assembly-wide `NoWarn CS0618` | it only ever existed for three deprecated `EditorPlugin` calls in that addon |

`TreatWarningsAsErrors` is **true unconditionally**, so an obsolete-API call in our own
code is a build error in the shipping configuration rather than being invisible
everywhere. `ContentValidator`, `Invariant`, `DevConsole`, `DevCommands`,
`WorldIntegrityChecker` and `ProfilerOverlay` deliberately **stay**: `--validate` runs
from the game executable and is a required CI gate, and the overlays are already gated
at runtime by `BuildProfile`.

`python tools/check_shipping_assembly.py` proves the gate held rather than assuming
it, by scanning the `ExportRelease` assembly for the excluded type names.

⚠️ **There is no `export_presets.cfg` and this project has never been exported**, so
"the shipping build" is currently proved by `dotnet build -c ExportRelease` compiling
clean plus that scan — not by a real export artifact.

#### Layer ownership rules

Dependencies point **down** this list, never up, and never in a cycle:

| Layer | Owns | Must not |
| --- | --- | --- |
| Application | process services, startup/shutdown | know about a session, a world or a player |
| Session | the current save's runtime | outlive a quit to title |
| World | the loaded world and its regions | outlive the session that loaded it |
| Entity/component | actor behaviour | reach past its own entity except through a scope |
| Presentation | visual and audio representation | decide gameplay state |
| UI | observing gameplay, sending intents | become gameplay authority |
| Developer/tooling | dev surfaces and harnesses | be reachable from shipping gameplay |

`Core` may not depend on `Debugging` — which is why `Invariant` lives in
`Core.Diagnostics`: the service scopes assert with it.

---

### 2.4b Pause & blocking menus

`GameManager.RefreshPause()` is the **single writer** of `GetTree().Paused`, and it answers one
question: is the game state `Paused`, **or** is a world-pausing menu open? It runs on `ChangeState`
and on `UiState.Changed`.

`UiState` counts two sets. `MenuOpen` is "the player's controls are suspended" — `PlayerInputRouter`
holds position, drops the guard and cancels casts. `WorldPaused` is the subset that should also stop
the simulation, and it is what the pause flag reads. Every modal `UiPanel` pauses (the default);
the boss-intro lock, the opening narration and the dev console pass `pausesWorld: false` because the
world has to keep playing under them.

Because a modal panel pauses the tree, `UiPanel` sets `ProcessMode.Always` — otherwise a panel would
freeze itself the instant it opened.

> This replaced a per-system `UiState.MenuOpen` check that only `HitStopDirector` and
> `CompanionRoster` ever applied. Enemy AI, status effects and in-flight projectiles did not, so
> opening the inventory mid-fight left a frozen, un-blocking, un-dodging player taking free hits.

---

### 2.4c Housing (`src/Housing`, Phase 37)

`PropertyResource` (`data/properties/*.tres`) describes a claimable holding — name, region, a
`PriceGold` and/or a `RequiredQuestId`, and the `TravelNodeId` it registers on a claim.
`PropertyDatabase` indexes them (the `BossDatabase` mirror). `HousingService` is the owned set:
`[GlobalClass] : Node, ISaveable`, shaped on `FastTravelService` — registered with **both** the
`ServiceLocator` and the `SaveManager`, and unregistered from both.

`PropertyDeedComponent : InteractableComponent` is the post in the world. `PropertyClaim.Resolve` is
the pure decision behind it, and **both the prompt and the interaction read it**, so what the player
is told and what happens cannot drift apart.

- **The check order is deliberate**: owned → quest-locked → too-expensive. Reporting the price first
  would send a player to earn gold for something a quest is holding shut anyway.
- **Claiming registers a fast-travel node at the *player's* position, not the post's** — the roadmap's
  housing↔Phase 25 tie, and landing fast travel on a collider is a trap `TravelNodeComponent` already
  paid for once.
- **A property must be sold or earned**, and must name a travel node; `--validate` rejects a holding
  that is free-on-touch or that the player cannot return to.

---

## 3. Collision layers & teams

| Layer (bit)  | Value | Used by                                   |
| ------------ | ----- | ----------------------------------------- |
| World (1)    | 1     | Floor, props; default body layer/mask     |
| Body (2)     | 2     | Reserved for solid actor bodies           |
| Hurtbox (3)  | 4     | `Hurtbox` areas (monitorable)             |
| Hitbox (4)   | 8     | `Hitbox` areas (monitor hurtboxes)        |

`CharacterBody3D` actors and the floor use the default layer/mask (1), so they
collide physically. Hit/hurtboxes are `Area3D`s on their own layers and don't
affect body movement.

**Teams** (`CombatComponent.Team`, honored by `Hitbox`): `0` = player, `1` =
hostile, `2` = neutral target (dummy). A hitbox never hits its own owner or a
hurtbox sharing its team.

---

## 4. Content / data pipeline

Balance and content live in `.tres` resources, not code. A `.tres` for a C#
`[GlobalClass]` resource references the script and sets exported properties:

```
[gd_resource type="Resource" script_class="AttributeSet" load_steps=2 format=3 uid="uid://..."]
[ext_resource type="Script" path="res://src/Stats/AttributeSet.cs" id="1_attrset"]
[resource]
script = ExtResource("1_attrset")
Health = 100.0
...
```

Load at runtime with `GD.Load<T>("res://...")` and **always provide a fallback**
(`?? SomeType.CreateDefault()` or null-guard) so a missing/broken resource never
crashes the boot.

**Enums export as ints** (`DamageType = 0` == `Physical`) in both `.tres` and save
files. Treat every persisted/authored enum as **append-only** — reordering or
inserting a member silently re-maps existing data. `EnumStabilityTests` (in
`tests/Embervale.Tests`) pins their ordinals and fails the build if they change;
each guarded enum carries an `// APPEND ONLY` marker.

**Content ids referenced from code** (currency, seeded items, factions,
enemy/actor templates, the player's starting spells/recipes, sandbox quest/NPC
ids) are centralized in `GameIds` (`src/Core/GameIds.cs`) so a rename happens in
one place. Their values must match the ids authored in `.tres`; `ContentValidator`
flags any drift at boot. (Authored `.tres` ids and placeholder defaults like
`"item.unknown"` stay as literals — they are data, not code references.)

Existing presets: `data/attributes/{Player,Dummy,Goblin,Companion}Attributes.tres`,
`data/weapons/{IronSword,GoblinClaw}.tres`, `data/companions/Kael.tres`.

### 4.1 Content cross-reference validation

Many `.tres` fields are **cross-references** — a string id that must resolve in another
database. Historically a typo (`item.iron_ot`) failed *silently* (no drop, no reward, a
dead quest). `ContentValidator` (`src/Debugging/ContentValidator.cs`) now resolves them
all at boot (logged after the databases load) and on demand via the `validate` dev-console
command (`F1`). It feeds the shared `Invariant` counter, so the `invariants` check sees
content breakage too. Enforced references:

| Authored in | Field(s) | Must resolve in |
| ----------- | -------- | --------------- |
| `LootTable` / `LootEntry` | `ItemId`, `GoldItemId` (when gold rolls) | `ItemDatabase` |
| `CraftingRecipeResource` | ingredient `ItemId`s, `OutputItemId` | `ItemDatabase` |
| `QuestResource` | reward `ItemId`s, `GoldItemId`, Collect `TargetId` | `ItemDatabase` |
| `QuestResource` | Kill `TargetId` | `EnemyTemplateRegistry` |
| `QuestResource` | `PrerequisiteQuestId` | `QuestDatabase` |
| `QuestResource` | `FactionRewardId` | `FactionDatabase` |
| `DialogueResource` | choice `Goto`, quest condition/`StartQuest` args | nodes / `QuestDatabase` |
| `DialogueResource` / `RegionResource` | `HasFlag`/`MissingFlag` args, `UnlockFlagId` | any `SetFlag`/`ClearFlag` effect or code constant |
| `SpellResource` | `StatusEffectId` | `StatusEffectDatabase` |
| `FactionResource` | `Enemies` / `Allies` | `FactionDatabase` |
| `EncounterResource` / `WorldEventResource` | `EnemyTemplateId` | `EnemyTemplateRegistry` |
| `EncounterResource` | `RegionIds` | `RegionDatabase` |
| `WorldEventResource` | `CacheItemId`, `RewardItemId`, `FactionRewardId` | `ItemDatabase` / `FactionDatabase` |
| `DialogueResource` | `OpenShop` effect arg (38E) | `ShopDatabase` |
| `ShopResource` | stock `ItemId`s, `LeveledTable` entries, `FactionId`, `NameKey` | `ItemDatabase` / `FactionDatabase` / the locale catalogue |
| `ServiceResource` | `TaughtRecipeIds`, `FactionId`, `NameKey` | `RecipeDatabase` / `FactionDatabase` / the locale catalogue |
| `PropertyResource` | `RegionId`, `RequiredQuestId`, `NameKey` | `RegionDatabase` / `QuestDatabase` / the locale catalogue |

> **Story flags have no database**, so they are checked the only way a registry-less id can be:
> readers against writers (Phase 34.5C). A flag nothing ever `SetFlag`s is an error — that is the
> gate that never opens. The reverse is legal: a flag set and never read is a record of what
> happened. A typo in the `SetFlag` itself therefore still slips through.

**Enemy archetypes are now data-resolved:** spawners (encounters, world events) build foes
through `EnemyTemplateRegistry.Create(templateId, pos)`, not a hard-coded factory. A new
enemy type is a new factory + one `EnemyTemplateRegistry.Register(...)` line in the
bootstrap; until then unknown ids fall back to the goblin (and the validator flags them).

> **Enum-as-int fragility:** enums serialize to `.tres`/saves as their ordinal
> (`DamageType = 0` == `Physical`). **Do not reorder or remove enum members** that are
> persisted or authored — append only. Reordering silently re-maps existing data
> (a `Rare` item becomes `Epic`). Pinning save-critical enums to string keys is a tracked
> follow-up.

---

## 5. Data flow at a glance

A melee hit is resolved entirely through the spine — no system references another
directly; they meet at the `EventBus`:

```
Input ─▶ PlayerInputRouter ─▶ MeleeWeaponComponent ─▶ Hitbox
                                                       │ (physics overlap)
                                            CombatComponent.ReceiveDamage
                                                       │  block → armor → ApplyDamage
                                            StatsComponent.ApplyDamage
                                                       ├─▶ EventBus.Publish(EntityDamagedEvent)
                                                       └─▶ EventBus.Publish(EntityDiedEvent)
                                                                │
        HUD · quests · loot · progression · factions ◀─────────┘  (independent subscribers)
```

Because publishers never know who listens, new reactions (a sound, an
achievement, a new HUD widget) are added by subscribing — not by editing the
combat code that raised the event.
