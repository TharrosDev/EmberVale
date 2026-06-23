# Embervale — Architecture & Systems Reference

The deep reference for how Embervale is built: the core architecture, every
gameplay system that exists today, the collision/team model, and the
content/data pipeline. For the working agreement, coding conventions, gotchas,
step-by-step recipes and the development workflow, see
[`../CLAUDE.md`](../CLAUDE.md); for the phase plan see
[`PRODUCTION_ROADMAP.md`](PRODUCTION_ROADMAP.md).

> **One-line summary:** an original third-person, open-world fantasy action RPG in
> **Godot 4.7** with **C# (.NET 8)**, built on a component-based, event-driven,
> resource-driven architecture, and kept buildable and playable at every commit.

---

## 1. Architecture overview

Three small ideas carry everything:

### 1.1 Autoload services (the spine)

Registered in `project.godot` `[autoload]`, in this order (order matters — later
ones may use earlier ones):

| Autoload         | File                               | Responsibility                          |
| ---------------- | ---------------------------------- | --------------------------------------- |
| `EventBus`       | `src/Core/Events/EventBus.cs`      | Typed publish/subscribe message hub.    |
| `ServiceLocator` | `src/Core/Services/ServiceLocator.cs` | Registry for world-scoped systems.   |
| `GameManager`    | `src/Core/GameManager.cs`          | Owns the `GameState` machine.           |
| `SaveManager`    | `src/Save/SaveManager.cs`          | Serializes `ISaveable`s to `user://`.   |

Each exposes a static `Instance` (set in `_EnterTree`, pattern below). They never
reference gameplay-specific types, so they stay stable as content grows.

`Log` (`src/Core/Diagnostics/Log.cs`) and `GameInput` (`src/Core/GameInput.cs`)
are **static classes, not autoloads**. Use `Log.Info/Warn/Error`, never raw
`GD.Print`. `GameInput.EnsureActions()` is called once by the bootstrap.

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

- **`DamageType`** — `Physical` (armor-mitigated), `Fire/Frost/Lightning/Arcane/
  Nature/Necrotic` (resist later), `True` (bypasses mitigation).
- **`DamagePacket`** (attacker-built) and **`DamageResult`** (resolved) —
  `readonly record struct`s.
- **`CombatMath`** — `RollAttack(baseDamage, attackerStats)` → `(amount, isCrit)`
  (adds `PhysicalPower × 0.5`, rolls crit from `CritChance`/`CritDamage`);
  `Mitigate(amount, type, defenderStats)` → armor curve `100/(100+armor)` for
  physical.
- **`CombatLayers`** — collision-layer masks: `World=1`, `Body=2`, `Hurtbox=4`,
  `Hitbox=8`.
- **`Hurtbox : Area3D`** — passive damageable region; layer `Hurtbox`, mask 0;
  points at the owner's `CombatComponent`. Needs a `CollisionShape3D` child.
- **`Hitbox : Area3D`** — damage-dealing region; layer `Hitbox`, mask `Hurtbox`.
  `Activate(packet)` opens the window; `_PhysicsProcess` **polls overlaps** and
  hits each hurtbox once, skipping its own owner and **same-`Team`** hurtboxes
  (friendly fire off). `Deactivate()` closes. Needs a `CollisionShape3D` child.
- **`CombatComponent`** — defender brain: `Team` (0 player, 1 hostile, 2 neutral
  target), poise/stagger, `IsBlocking`, and `ReceiveDamage(packet)` which applies
  block → armor → `StatsComponent.ApplyDamage`, manages poise (raises
  `EntityStaggeredEvent`), and raises `DamageDealtEvent`.
- **`WeaponResource`** — `[GlobalClass] Resource`: damage type, base/poise damage,
  stamina cost, wind-up/active/recovery times, attack-speed multiplier, combo
  length, finisher multiplier.
- **`MeleeWeaponComponent`** — attacker FSM Idle→Windup→Active→Recovery. `TryAttack()`
  starts a swing (costs stamina, blocked while staggered); during Active it opens
  the assigned `Hitbox` with a freshly rolled packet; chaining during Recovery
  advances the combo (final hit = finisher). Scaled by weapon × `AttackSpeed` stat.

### 2.3 Movement (`src/Movement`)

- **`LocomotionComponent`** — reusable kinematic motor for a `CharacterEntity`.
  `Move(delta, wishDir /*world-space horizontal*/, sprint, jump)` applies gravity,
  acceleration, jump and `MoveAndSlide`. Speed comes from the `MoveSpeed` stat
  (falls back to `BaseSpeed`). **Input-agnostic** — the player controller and the
  enemy AI both feed it.

### 2.4 Player (`src/Player`)

- **`PlayerCharacter : CharacterEntity`** — marker type registered in the
  `ServiceLocator`, so enemies resolve the player by a distinct type.
- **`PlayerController : EntityComponent`** — third-person input + camera. Reads
  `GameInput` actions, drives `LocomotionComponent`, mouse-look (body yaw +
  camera-pivot pitch), routes `attack`→`MeleeWeaponComponent.TryAttack()` and
  `block`→`CombatComponent.IsBlocking`. Subscribes to `GameStateChangedEvent` to
  capture/release the mouse. Camera pivot is **injected** by the factory.
- **`PlayerFactory.Create(pos)`** — assembles the player (collision capsule,
  stats from `PlayerAttributes.tres`, locomotion, combat `Team 0`, hurtbox,
  camera pivot + camera, melee hitbox, weapon from `IronSword.tres`, controller).

### 2.5 Enemies (`src/Enemies`)

- **`EnemyEntity : CharacterEntity`** — marker type for hostiles.
- **`EnemyState`** — `Idle, Patrol, Investigate, Combat, Retreat, Dead`.
- **`EnemyAIComponent`** — perception-driven FSM. Reuses `LocomotionComponent`
  (move) and `MeleeWeaponComponent` (attack). Perception = vision range + FOV
  cone + line-of-sight raycast + short-range proximity sense; tracks a
  last-known position. Spotting the player broadcasts `EnemyAlertedEvent` →
  nearby idle/patrolling allies `Investigate` (group coordination). Wounded
  (< `RetreatHealthFraction`) → `Retreat`. On death → `Dead` → despawn after a
  delay.
- **`EnemyFactory.Create(pos)`** — builds a visible goblin (mesh, collision,
  stats from `GoblinAttributes.tres`, combat `Team 1`, hurtbox, weapon from
  `GoblinClaw.tres` + hitbox, AI).
- **`EnemySpawnDirector : Node3D`** — keeps a population alive within a radius;
  seeds the camp on ready, respawns the dead (tracks via `TreeExited`).

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
  player can interact with. `PlayerController` raycasts from the camera on the
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
  `RequiredCount`), `QuestItemReward`s, XP/gold rewards and an optional
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
  `MissingFlag`. `DialogueEffect`: `None`/`StartQuest`/`SetFlag`/`ClearFlag`.
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
  the time of day. `DayPhase` (Night/Dawn/Day/Dusk) is derived via `DayPhases.Of(hour)`.
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
  and tint via `SpellSchools.Color`), a `Delivery` (`SpellDelivery` Projectile/Area/Self),
  `ManaCost`, `Cooldown`, `BaseDamage`, `Healing`, an optional applied `StatusEffectId`, and
  delivery knobs (`Range`, `ProjectileSpeed`, `ImpactRadius`). `SpellDatabase` indexes them.
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
  or end of range. **`SpellResolver`** does the impact: `HitOne` (single target) or `Detonate`
  (a Hurtbox-layer sphere query for AoE), honouring the same friendly-fire rules as hitboxes
  (never the caster, never same-team), then applies the spell's status. `SpellFlash` is a
  short-lived cosmetic burst sphere.
- **UI & input** — `Q` casts the prepared spell, `F` cycles it; `DebugHud` shows mana, the
  prepared spell + cooldown, and active status effects on player/target. Events:
  `SpellCastEvent`/`SpellSelectedEvent`/`StatusEffectAppliedEvent`/`StatusEffectRemovedEvent`
  (`src/Magic/MagicEvents.cs`). Sandbox spells: Firebolt, Fireball (AoE), Frost Nova (Area
  slow), Lesser Heal (Self), Arcane Shield (Self buff).

### 2.6h World systems (`src/World`)

Layered on the Phase 11 `WorldClock` (which already supplies time-of-day + `DayPhase` and
persists). Three pieces:

- **Day/night atmosphere** — **`SkyController`** (`Node3D`, `Pausable`) animates a
  `DirectionalLight3D` "sun" and the scene `Godot.Environment` from the clock's *continuous*
  `TimeOfDay` each frame: sun sweep/pitch, warm→white colour, energy by day factor, and sky/
  ambient darkening at night via `Environment.BackgroundEnergyMultiplier`. The sun + env are
  built by the bootstrap and **injected** (`Sun`/`Environment` properties). It also blends in
  the active weather and drives a rain `GpuParticles3D` that follows the player.
- **Weather** — `WeatherResource` (`[GlobalClass]`, `data/weather/*.tres`): duration range,
  `SelectionWeight`, `LightEnergyScale`/`SkyEnergyScale`, `FogDensity`/`FogColor`,
  `Precipitation`. `WeatherDatabase` indexes them. **`WeatherDirector`** (`Node`, `ISaveable`
  `weather`, `ServiceLocator`-registered, `Pausable`) holds the active state + a countdown in
  in-game hours (off the clock), rolls a new weighted state on expiry (never the same twice),
  publishes `WeatherChangedEvent`, and persists current id + remaining time. `SkyController`
  reads `WeatherDirector.Current` each frame and `MoveToward`-blends the atmosphere.
- **Encounters** — `EncounterResource` (`[GlobalClass]`, `data/encounters/*.tres`): enemy
  template, count range, weight, and per-`DayPhase` allow flags. `EncounterDatabase` indexes
  them. **`EncounterDirector`** (`Node3D`, `Pausable`) spawns groups around the player on a
  cadence scaled by phase (night) and weather (storm), capped by `MaxConcurrent` and tracked
  via `TreeExited`, reusing `EnemyFactory`. Publishes `EncounterTriggeredEvent`. **Not
  persisted** (emergent/transient, like `EnemySpawnDirector`). The richer *named world-event*
  framework is Phase 17 — keep these lightweight.
- Events live in `src/World/WorldEvents.cs` (`TimeOfDayChangedEvent` + the two above). The HUD
  shows the current weather beside the clock.

### 2.6h-2 Regions & streaming (`src/World`, Phase 25)

The world is divided into authored **regions** (one per area; many per `Realm`). Phase 25A
establishes the data + convention; the streamer, map, and fast-travel land in 25B–25G.

- **`RegionResource`** (`[GlobalClass]`, `data/regions/*.tres`): `Id` (`region.*`),
  `DisplayName`, `Realm` (the fixed `Realm` enum — the four LORE realms + the Celestial),
  `SubCells` (the streamable cell scene ids), `Bounds` (`Aabb`), an atmosphere bias
  (`DefaultWeatherId` + `DayPhaseBias`), and `Neighbours` (region ids — the map/fast-travel
  adjacency). `RegionDatabase` indexes them (mirrors `WeatherDatabase`); the save header reads
  the active region's `DisplayName` by id. New region = a `.tres`, no code.
- **Scene/world-partition convention** (for Phases 27/44 authoring): a region's sub-cell scenes
  live under `scenes/regions/<region>/<cell>.tscn`, where `<region>` is the id minus its
  `region.` prefix (e.g. `scenes/regions/ember_crown/hub.tscn` for sub-cell `ember_crown.hub`).
  Keep each cell self-contained (its own static geometry, navmesh, props, spawn markers) and
  positioned in world space within the region's `Bounds`, so the 25B **`RegionStreamer`** can
  load/unload it by distance with no bespoke wiring. Persistent actors in a cell carry a
  `PersistentId` so they restore via the `PersistentSpawnDirector` when the cell reloads (25D).
  The current flat sandbox is the single region `region.ember_crown` (one always-loaded cell).

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
- **Stations & UI** — `CraftingStationComponent` (`InteractableComponent`) publishes
  `CraftingStationOpenedEvent` on `E`; `CraftingStationFactory` builds the world block.
  `CraftingPanel` (`src/UI`, modal, built through `UiTheme`) lists known recipes matching the
  station (+ `Hand`), with live have/need ingredient lines and a Craft button; `E` closes it
  (a `_justOpened` guard stops the opening press from also closing it). Events:
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
  `WorldEventKind` (`0` Raid / `1` Cache / `2` Hunt), `SelectionWeight`, `CooldownSeconds`,
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

### 2.7 Save (`src/Save`)

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
  volatile `RuntimeId` for transient actors — logging a warning when it does, because
  that state will not survive a reload. World singletons keep fixed keys (`worldclock`,
  `weather`).

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

### 2.9 Bootstrap & UI

- **`GameBootstrap : Node3D`** (`scenes/Main.tscn` root) — assembles the sandbox:
  registers input, initializes the databases, builds the world (sun, sky, floor), the
  UI (`GameHud`, `DebugHud`, `Notifications`, `PauseMenu`, the modal panels), the
  directors (clock, weather, sky, encounters, world events), the training dummy, the
  player, the goblin camp, the crafting yard and scattered pickups. Runs with
  `ProcessMode.Always` so debug keys work while paused (`F3` toggles `DebugHud`; `Esc` is
  the `PauseMenu`). Routes player/dummy/enemy deaths.
- **`GameHud : CanvasLayer`** — the **default in-game HUD** (Phase 18): anchored widgets —
  vitals bars (bottom-left), prepared spell + cooldown + status line, quest tracker
  (top-right), time/weather (top-left), a world-event banner + aimed-target nameplate
  (top-centre), an interaction prompt (bottom-centre), and the `Crosshair`. Persistent nodes
  updated each frame from the player (`SetPlayer`) + the world directors (`Set*`). The
  nameplate/prompt read `PlayerController.FocusedEntity`/`FocusPrompt` (a per-frame raycast).
- **`PauseMenu : CanvasLayer`** — modal pause menu on `Esc` (Resume / Quick Save / Quick Load /
  Quit), `ProcessMode.Always` so it works while paused; drives `GameManager` pause + dims the
  scene. Owns `Esc` (the bootstrap no longer toggles pause directly).
- **`Notifications` + `Toast`** — top-centre toast feed: subscribes to discrete events
  (level-up, quest start/complete, world-event start/end) and spawns self-fading `Toast`s.
- **`InventoryPanel`/`QuestLogPanel`/`DialoguePanel`/`CraftingPanel`** — the modal/overlay
  panels (character screen `I`, journal `J`, dialogue, crafting). Item rows carry hover
  tooltips (`TooltipText`).
- **`DebugHud : CanvasLayer`** — now a **developer overlay hidden by default, toggled with
  `F3`** (FPS, raw stats, target internals, active world event). No longer the primary HUD.
- **`UiTheme`** (`src/UI/UiTheme.cs`) — the shared look for all UI: palette + builders for
  framed panels (`PanelStyle` is public for transient widgets like toasts), padded containers,
  accent headers, body lines, styled buttons and coloured resource bars. **Build new UI through
  it.** `Crosshair` (`src/UI/Crosshair.cs`) is the code-drawn aim marker (owned by `GameHud`).

> **UI altitude:** the game HUD (`GameHud`), pause menu and toasts are the real in-game UI;
> `DebugHud` is the F3 dev overlay. Build new gameplay UI through `UiTheme`, not hand-styled
> controls. The meta/shell (title screen, settings, save-slot flow) is the separate
> content/production roadmap.

### 2.10 Debugging tools (`src/Debugging`)

Developer tooling behind function keys (Phase 20); all run `ProcessMode.Always`.

- **`DevConsole`** (`F1`) — an in-game command line: scrollback (`RichTextLabel`) + `LineEdit`
  that dispatches to a `Dictionary<string, ConsoleCommand>`. `DevCommands.RegisterAll` ships the
  built-ins (`spawn`/`give`/`xp`/`heal`/`rep`/`time`/`weather`/`event`/`seed`/`repro`/
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
  (adds graph reachability — dialogue orphans/dead-ends, quest completability, prerequisite
  cycles). Also runnable headless: `godot --headless --path . -- --validate` (exit 0/1).
- **`ProfilerOverlay`** (`F4`) — reads Godot `Performance` monitors (FPS, frame/physics ms, draw
  calls, node/orphan counts, static memory). Idle when hidden.
- **`ReproHarness`** — named scenarios that `GD.Seed` the global RNG then replay a fixed command
  list (`repro <name>`) for deterministic bug repro. New scenario = a one-line entry.
- **Analytics spine** (`src/Analytics`, dev-only) — `AnalyticsSink` (a `Node` wired in the
  bootstrap) subscribes to the EventBus and appends a JSON-lines log to `user://analytics/`
  (deaths by location, quest start/complete, level-ups), plus any `AnalyticsEvent` a system
  publishes for ad-hoc instrumentation. One file per session. Gated on `OS.IsDebugBuild()` — a
  complete no-op in retail builds — and deliberately not `ISaveable` (it is a log, not state).
- Wired in the bootstrap; `F1`/`F4` toggles sit alongside the `F3` `DebugHud`. Console commands
  rely on `WorldClock.SetTimeOfDay`, `WeatherDirector.Force`, `WorldEventDirector.ForceStart`
  (registered in the `ServiceLocator`).

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

Existing presets: `data/attributes/{Player,Dummy,Goblin}Attributes.tres`,
`data/weapons/{IronSword,GoblinClaw}.tres`.

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
| `DialogueResource` | choice `Goto`, quest condition/`StartQuest` args | nodes / `QuestDatabase` |
| `SpellResource` | `StatusEffectId` | `StatusEffectDatabase` |
| `FactionResource` | `Enemies` / `Allies` | `FactionDatabase` |
| `EncounterResource` / `WorldEventResource` | `EnemyTemplateId` | `EnemyTemplateRegistry` |
| `WorldEventResource` | `CacheItemId`, `RewardItemId`, `FactionRewardId` | `ItemDatabase` / `FactionDatabase` |

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
Input ─▶ PlayerController ─▶ MeleeWeaponComponent ─▶ Hitbox
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
