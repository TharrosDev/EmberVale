# CLAUDE.md — Embervale

Authoritative guide for working in this repository. Read this first. It explains
what the project is, how it is built, the architecture and conventions, every
system that exists today, the gotchas that will bite you, and step-by-step
recipes for adding new content without breaking things.

> **One-line summary:** Embervale is an original first-person, open-world fantasy
> action RPG built in **Godot 4.7** with **C# (.NET 8)**, using a component-based,
> event-driven, resource-driven architecture. The repo is kept **buildable and
> playable at every commit**.

---

## 1. Mission & working agreement

You are the lead engineer building this game incrementally. The non-negotiables:

- **Always keep the repo buildable and playable.** A working ugly prototype beats
  a beautiful broken feature.
- **Build real, functioning systems** — never theoretical scaffolding. Every
  feature must be usable in-game the moment it lands.
- **Persistence is not optional.** Any system that holds gameplay state must be
  able to save/load (implement `ISaveable`).
- **Prefer composition and data.** New actors = new components + new `.tres`
  resources, not new inheritance chains or hard-coded values.
- **Respect existing architecture.** Inspect before adding; don't duplicate
  systems; refactor when it lowers long-term cost.
- **Work in phases** (see §12). Determine the next highest-priority task and do it.

---

## 2. Tech stack & environment

| Thing            | Value                                                           |
| ---------------- | --------------------------------------------------------------- |
| Engine           | Godot 4.7 (.NET / Mono build)                                   |
| Language         | C# targeting `net8.0`, `Nullable` enabled, `ImplicitUsings` off |
| SDK              | `Godot.NET.Sdk/4.7.0` (see `Embervale.csproj`)                  |
| Assembly / root ns | `Embervale`                                                   |
| Entry scene      | `scenes/Main.tscn` → `GameBootstrap` (`src/Bootstrap`)          |
| Target platforms | Windows, Linux, Steam Deck (Forward+ renderer)                  |

**This container cannot build or run the game** — there is no Godot/.NET
toolchain installed here. Write code carefully against the Godot 4.7 C# API; the
human builds/runs in the Godot editor. Do **not** claim something was
"tested/verified running" — say it was reviewed against the API.

There is **no CI** (the maintainer declined to add GitHub Actions). The green
**Vercel** check that appears on every PR is a meaningless no-op — Vercel is
trying to deploy a Godot game as a web app. Ignore it; do not treat it as a
build signal.

---

## 3. Build & run (for the human)

1. Install Godot 4.7+ **.NET build** and the .NET 8 SDK.
2. Open `project.godot` in the editor (it builds C# automatically), or
   `dotnet build Embervale.sln`.
3. Press Play. `scenes/Main.tscn` boots the sandbox.

**Sandbox controls:** `WASD` move · mouse look · `Shift` sprint · `Space` jump ·
`LMB` attack · `RMB` block · `E` interact · `I` inventory · `H` heal dummy ·
`R` respawn dummy · `F5`/`F9` quick save/load · `Esc` pause (frees the cursor).
Goblins roam to the north (−Z) and drop loot.

---

## 4. Repository layout

```
.
├── project.godot            # Engine config + autoload registration + window/render
├── Embervale.sln / .csproj  # C# solution (net8.0, Godot.NET.Sdk 4.7.0)
├── icon.svg
├── CLAUDE.md                # You are here
├── README.md                # Public overview + roadmap table
├── docs/
│   ├── ARCHITECTURE.md      # Narrative architecture overview
│   └── ROADMAP.md           # Phase plan + per-phase delivery notes
├── scenes/
│   └── Main.tscn            # Entry scene (root has GameBootstrap script)
├── data/                    # Resource-driven content (.tres)
│   ├── attributes/          # AttributeSet presets (player, dummy, goblin)
│   ├── weapons/             # WeaponResource presets (iron sword, goblin claw)
│   ├── items/               # ItemResource / EquippableItemResource templates
│   ├── affixes/             # AffixDefinition presets (loot prefixes/suffixes)
│   ├── loot/                # LootTable presets (e.g. GoblinLoot)
│   ├── progression/         # ProgressionResource presets (XP curve + per-level gains)
│   ├── perks/               # PerkResource presets (rankable passives)
│   ├── quests/              # QuestResource presets (objectives + rewards)
│   ├── dialogue/            # DialogueResource presets (node-graph conversations)
│   └── schedules/           # ScheduleResource presets (NPC daily routines)
└── src/
    ├── Core/
    │   ├── Events/          # IGameEvent, EventBus (autoload), CoreEvents
    │   ├── Services/        # ServiceLocator (autoload)
    │   ├── Diagnostics/     # Log (static facade over GD.Print)
    │   ├── GameManager.cs   # Top-level GameState machine (autoload)
    │   ├── GameState.cs     # enum Boot/MainMenu/Loading/Playing/Paused/GameOver
    │   └── GameInput.cs      # Input actions defined in code
    ├── Entities/            # IEntity, Entity, CharacterEntity, EntityComponent, EntityNode
    ├── Stats/               # StatType, Stat, StatModifier, AttributeSet, StatsComponent
    ├── Movement/            # LocomotionComponent (reusable kinematic motor)
    ├── Combat/              # Damage pipeline, hitbox/hurtbox, weapons, CombatComponent
    ├── Items/               # ItemResource, ItemInstance, affixes, inventory, equipment, pickups
    ├── Loot/                # LootTable/LootEntry, LootGenerator, LootRarity, LootComponent
    ├── Progression/         # XP/levels (ProgressionComponent), perks, ExperienceComponent
    ├── Quests/              # QuestResource/objectives, QuestLogComponent, quest givers
    ├── Dialogue/            # Dialogue graph resources, session runner, story flags
    ├── World/               # WorldClock (time-of-day) + world events
    ├── Npc/                 # NPC schedule resources, ScheduleComponent (routines)
    ├── Interaction/         # InteractableComponent (raycast interact)
    ├── Player/              # PlayerCharacter, PlayerController, PlayerFactory
    ├── Enemies/             # EnemyEntity, EnemyAIComponent, EnemyFactory, EnemySpawnDirector
    ├── Save/                # ISaveable, SaveManager (autoload)
    ├── UI/                  # DebugHud
    └── Bootstrap/           # GameBootstrap (assembles the sandbox)
```

**Conventions for new files:** namespace mirrors folder
(`Embervale.<Folder>[.<Sub>]`); one primary type per file; file name == type name.

---

## 5. Architecture overview

Three small ideas carry everything:

### 5.1 Autoload services (the spine)

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

### 5.2 EventBus — typed pub/sub (prefer over Godot signals)

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

### 5.3 Entities are compositions of components

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

## 6. Systems reference (what exists today)

### 6.1 Stats (`src/Stats`)

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

### 6.2 Combat (`src/Combat`)

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

### 6.3 Movement (`src/Movement`)

- **`LocomotionComponent`** — reusable kinematic motor for a `CharacterEntity`.
  `Move(delta, wishDir /*world-space horizontal*/, sprint, jump)` applies gravity,
  acceleration, jump and `MoveAndSlide`. Speed comes from the `MoveSpeed` stat
  (falls back to `BaseSpeed`). **Input-agnostic** — the player controller and the
  enemy AI both feed it.

### 6.4 Player (`src/Player`)

- **`PlayerCharacter : CharacterEntity`** — marker type registered in the
  `ServiceLocator`, so enemies resolve the player by a distinct type.
- **`PlayerController : EntityComponent`** — first-person input + camera. Reads
  `GameInput` actions, drives `LocomotionComponent`, mouse-look (body yaw +
  camera-pivot pitch), routes `attack`→`MeleeWeaponComponent.TryAttack()` and
  `block`→`CombatComponent.IsBlocking`. Subscribes to `GameStateChangedEvent` to
  capture/release the mouse. Camera pivot is **injected** by the factory.
- **`PlayerFactory.Create(pos)`** — assembles the player (collision capsule,
  stats from `PlayerAttributes.tres`, locomotion, combat `Team 0`, hurtbox,
  camera pivot + camera, melee hitbox, weapon from `IronSword.tres`, controller).

### 6.5 Enemies (`src/Enemies`)

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

### 6.6 Items & inventory (`src/Items`, `src/Interaction`)

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

### 6.6b Loot generation (`src/Loot`, `src/Items`)

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

### 6.6c Progression (`src/Progression`)

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

### 6.6d Quests (`src/Quests`)

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

### 6.6e Dialogue (`src/Dialogue`)

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

### 6.6f World clock & NPC schedules (`src/World`, `src/Npc`)

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

### 6.7 Save (`src/Save`)

- **`ISaveable`** — `SaveId`, `Godot.Collections.Dictionary Save()`,
  `void Load(dict)`. State exchanged as a Godot `Dictionary` → serializes to JSON
  with no reflection.
- **`SaveManager`** (autoload) — `Register/Unregister`, `SaveGame(slot)` /
  `LoadGame(slot)` to `user://saves/<slot>.json` in a versioned envelope
  (`{version, timestamp, objects: {SaveId: state}}`). On load, each live
  saveable pulls its own entry by `SaveId`.

> Caveat: `SaveId` currently uses runtime id, so save/load round-trips within a
> session. Cross-session entity identity (stable ids for spawned actors) is a
> later concern — design new persistence with that in mind.

### 6.8 Flow & input

- **`GameState`** + **`GameManager`** — `ChangeState(next)` sets
  `GetTree().Paused = (next == Paused)`, runs with `ProcessMode.Always`, and
  raises `GameStateChangedEvent`. `TogglePause()`, `IsPlaying`.
- **`GameInput`** — action name constants + `EnsureActions()` binding them in
  code (idempotent). Actions: `move_forward/back/left/right`, `jump` (Space),
  `sprint` (Shift), `interact` (E), `attack` (LMB), `block` (RMB), `cast` (Q),
  `inventory` (I), `pause` (Esc).

### 6.9 Bootstrap & UI

- **`GameBootstrap : Node3D`** (`scenes/Main.tscn` root) — assembles the sandbox:
  registers input, initializes the `ItemDatabase`, builds the world (directional
  light, sky, floor mesh + `WorldBoundaryShape3D` collider), the `DebugHud` and
  `InventoryPanel`, the training dummy (team 2), the player, the goblin camp, and
  scattered item pickups. Runs with `ProcessMode.Always` so it can unpause and
  read debug keys while paused. Routes player/dummy/enemy deaths (enemies drop
  loot).
- **`InventoryPanel : CanvasLayer`** — toggleable inventory read-out (key `I`).
- **`DebugHud : CanvasLayer`** — on-screen overlay (FPS, game state, player
  HP/stamina, target, last hit with CRIT/blocked). Built in code.

---

## 7. Collision layers & teams

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

## 8. Content / data pipeline

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
crashes the boot. Enums export as ints (`DamageType = 0` == `Physical`).

Existing presets: `data/attributes/{Player,Dummy,Goblin}Attributes.tres`,
`data/weapons/{IronSword,GoblinClaw}.tres`.

---

## 9. Coding conventions

- **Namespaces mirror folders**; one primary type per file; file name == type.
- **Nullable reference types are ON.** After a guard, capture a local
  (`IEntity owner = Entity!;`) or use `!`. Autoload singletons use
  `public static T Instance { get; private set; } = null!;` and guard duplicates
  in `_EnterTree`.
- **Components** end in `Component`; **events** are past-tense and end in `Event`;
  **resources** end in `Resource`/`Set`.
- **Use `Log`** (not `GD.Print`) for diagnostics.
- **React to events** rather than polling singletons where practical.
- **Factories build detached, then add to tree.** Set component properties
  before `AddChild` where they're needed in `OnInitialize`; properties only used
  later (e.g. camera refs) can be set before the *host* enters the main tree.
- **`[GlobalClass]`** on Godot types you want creatable in the editor / usable in
  `.tres` (`Entity`, `CharacterEntity`, components, resources).
- Editorconfig: 4-space indent, `csharp_new_line_before_open_brace = all`
  (Allman braces), `using`s system-first.

---

## 10. Gotchas (read before debugging)

- **Never override `_Ready` in an `EntityComponent`** — it resolves the owner.
  Use `OnInitialize`/`OnTeardown`.
- **Lifecycle order:** identity is set in `_EnterTree` (top-down); components
  initialize in `_Ready` (bottom-up). Don't rely on a sibling component's
  `OnInitialize` having run — only on the host existing.
- **Autoload order** is fixed in `project.godot`; `EventBus`/`ServiceLocator`
  come before `GameManager`/`SaveManager`.
- **Pause deadlock:** when `GameState.Paused`, the tree is paused and normal
  nodes stop processing/inputting. The bootstrap and `GameManager` use
  `ProcessMode.Always` so pause can be toggled back. EventBus handlers run
  synchronously regardless of pause (plain C# calls), which is how the player
  re-captures the mouse on resume.
- **`Area3D` overlap timing:** enabling `Monitoring` updates overlaps on the next
  physics step. `Hitbox` polls each physics frame across its active window
  instead of trusting `area_entered` timing.
- **Dummy vs player origin:** the dummy is spawned at its capsule centre
  (`y=1`, shapes centred at local origin); the player/enemy origins are at the
  feet (shapes offset to `y = height/2`). Match shapes to mesh accordingly.
- **`GD.Load<T>` can return null** — always fall back.
- **`ServiceLocator` holds one instance per type.** The player is registered as
  `PlayerCharacter`; the dummy as `Entity`; enemies are **not** registered.
- This container can't compile — there is no substitute for careful API use.

---

## 11. Recipes (how to add things)

**A new component**
1. Create `src/<Area>/XxxComponent.cs` extending `EntityComponent`
   (`[GlobalClass]` if editor-creatable).
2. Resolve siblings/stats in `OnInitialize` via `Entity!.GetComponent<T>()`.
   Subscribe to events here; unsubscribe in `OnTeardown`.
3. Add it as a child of the actor in the relevant factory (or scene).

**A new actor / enemy type**
1. (Optional) marker subclass of `CharacterEntity` for type-level identity.
2. Add an `AttributeSet` `.tres` for its stats and (if it fights) a
   `WeaponResource` `.tres`.
3. Write a factory (mirror `EnemyFactory`) wiring: collision, mesh, `StatsComponent`,
   `CombatComponent` (set `Team`), `LocomotionComponent`, `Hurtbox`,
   `Hitbox` + `MeleeWeaponComponent`, and a behaviour component.

**A new weapon**
1. Author `data/weapons/Xxx.tres` (`script_class="WeaponResource"`).
2. Point a `MeleeWeaponComponent.Weapon` at it (factory or future equipment).

**A new item**
1. Author `data/items/Xxx.tres` (`script_class="ItemResource"`) with a unique
   `Id` (e.g. `item.material.silver`).
2. It is auto-indexed by `ItemDatabase` on startup. Reference it anywhere via
   `ItemDatabase.Get("item....")` — pickups (`ItemPickupFactory.Create`), loot
   drops, shops, recipes.
3. New interactable kinds: subclass `InteractableComponent` (override `Prompt`
   and `Interact`) and add a collider so the player's raycast can hit it.

**A new piece of equipment**
1. Author `data/items/Xxx.tres` (`script_class="EquippableItemResource"`,
   `MaxStack = 1`): set `Slot`, the `Bonus*` fields, and (for weapons) a `Weapon`
   `ext_resource` pointing at a `WeaponResource`.
2. It's indexed by `ItemDatabase` like any item; equip it via the character screen.
   Bonuses apply automatically through `EquipmentComponent` → `StatsComponent`.

**A new loot affix**
1. Author `data/affixes/Xxx.tres` (`script_class="AffixDefinition"`): unique `Id`,
   a `Label` fragment, `Kind` (0 Prefix / 1 Suffix), target `Stat`, `MinValue`/
   `MaxValue`, `MinRarity`, `Weight`, and the `For{Weapons,Armor,Accessories}` flags.
2. Auto-indexed by `AffixDatabase`; it enters the eligible pool for any equippable
   whose gear family + rolled rarity match. No code change.

**A new loot table / dropper**
1. Author `data/loot/Xxx.tres` (`script_class="LootTable"`) with `LootEntry`
   sub-resources (item id, `DropChance`, `Min/MaxQuantity`, `RollAffixes`), plus
   optional gold (`GoldChance`/`GoldMin`/`GoldMax`) and `QualityBonus`.
2. Add a `LootComponent` to the actor (set `Table` or `TablePath`); it rolls and
   spawns pickups on death. See `EnemyFactory` for the wiring.

**A new perk**
1. Author `data/perks/Xxx.tres` (`script_class="PerkResource"`): unique `Id`,
   `DisplayName`, `Description`, `MaxRank`, `Cost`, target `Stat`, `ModifierType`
   and `ValuePerRank`.
2. Auto-indexed by `PerkDatabase`; it appears in the character screen's PERKS list
   and is learnable once the player has skill points. No code change.

**A new XP-bearing enemy (or tuning the curve)**
1. Add an `ExperienceComponent { XpValue = N }` to the actor's factory (see
   `EnemyFactory`) to grant XP on death.
2. Tune levelling by editing `data/progression/PlayerProgression.tres` (or author a
   new `ProgressionResource` and point a `ProgressionComponent.CurvePath`/`Curve` at
   it).

**A new quest**
1. Author `data/quests/Xxx.tres` (`script_class="QuestResource"`) with a unique `Id`,
   `Title`/`Summary`, `Objectives` (an array of `ObjectiveResource` sub-resources:
   `Type` 0=Kill / 1=Collect, `TargetId` = entity `TemplateId` or item id,
   `RequiredCount`), and rewards (`XpReward`, `GoldReward`, `RewardItems` of
   `QuestItemReward`). Optional `PrerequisiteQuestId` chains it after another.
2. Auto-indexed by `QuestDatabase`. Start it via a `QuestGiverComponent` (set its
   `QuestId`) on a world `Entity`, in a `DialogueChoice` (`Effect` StartQuest), or
   directly with `player.GetComponent<QuestLogComponent>().StartQuest(...)`. Objectives
   advance and rewards apply automatically. No code change for new Kill/Collect quests.

**A new conversation**
1. Author `data/dialogue/Xxx.tres` (`script_class="DialogueResource"`): unique `Id`,
   `SpeakerName`, `StartNodeId`, and `Nodes` — an array of `DialogueNode` sub-resources
   (`Id`, optional `Speaker`, `Text`, `Choices`). Each `DialogueChoice` sub-resource has
   `Text`, a `Goto` node id (empty = end), an optional `Condition`+`ConditionArg` (gates
   visibility) and an optional `Effect`+`EffectArg` (`1`=StartQuest, `2`=SetFlag,
   `3`=ClearFlag). Enums export as ints (see `DialogueEnums.cs`).
2. Auto-indexed by `DialogueDatabase`. Attach a `DialogueComponent` (set its
   `DialogueId`) to a world `Entity` with a collider; the player's `E` interact opens it
   in `DialoguePanel`. No code change for new conversations.

**A new NPC routine**
1. Author `data/schedules/Xxx.tres` (`script_class="ScheduleResource"`): unique `Id` and
   `Entries` — an array of `ScheduleEntry` sub-resources (`StartHour` 0–23, `Activity`
   label, `Destination` world `Vector3`). Hours before the first block wrap to the last.
2. Auto-indexed by `ScheduleDatabase`. Add a `ScheduleComponent` (set its `ScheduleId`) to
   a static NPC `Entity`; it walks the routine off the `WorldClock` and reacts to alerts /
   dialogue. No code change for new routines.

**A new stat**
1. Add to the `StatType` enum; if it's a depleting resource, update
   `StatTypes.IsResource`.
2. Add an exported field + mapping in `AttributeSet` (`ToBaseValues`).
3. Use via `StatsComponent.GetValue(StatType.Xxx)`.

**A new event**
1. Add a `readonly record struct XxxEvent(...) : IGameEvent` in the relevant
   `*Events.cs`.
2. `Publish` it where it happens; `Subscribe`/`Unsubscribe` where reacted to.

**A new persistent system**
1. Implement `ISaveable` (stable `SaveId`, `Save`/`Load` with a Godot
   `Dictionary`).
2. `SaveManager.Instance.Register(this)` in `OnInitialize`, `Unregister` in
   `OnTeardown`.

**A new input action**
1. Add a constant + `Bind(...)` in `GameInput`.
2. Read it via `Godot.Input.IsActionPressed/JustPressed/GetVector`.

---

## 12. Development workflow

- **Branch:** develop on `claude/happy-maxwell-19hx51`. **`main` is the trunk.**
  Never push directly to `main`; open PRs into it.
- **Per phase:** implement → keep buildable/playable → update `README.md` +
  `docs/ROADMAP.md` (mark phase done, queue next) → commit → push →
  open a **draft PR** into `main`. The maintainer reviews/merges.
- **After a merge:** the head branch may be auto-deleted; locally
  `git fetch origin main && git reset --hard origin/main` to resync, then carry on.
- **Commits:** clear, descriptive messages. Co-author/session trailers are added
  per harness configuration. Do **not** put model identifiers in commits/PRs.
- **No CI to satisfy.** The Vercel check is a no-op (see §2).

---

## 13. Roadmap status

> **Scope:** these 18 phases build *systems/infrastructure*, not the game's
> content. Finishing them yields a data-driven sandbox that can express the game,
> not a finished game — the world, narrative, art, audio, balance and ship polish
> are a **separate content/production roadmap** introduced once the systems are
> done. See the scope note at the top of `docs/ROADMAP.md`.

Done: **1 Core Architecture · 2 Player Controller · 3 Combat Framework ·
4 Enemy AI · 5 Inventory System · 6 Equipment System · 7 Loot Generation ·
8 Progression · 9 Quests · 10 Dialogue · 11 NPC Schedules**. Next: **12 Magic**.

Then (in order): 13 World Systems · 14 Crafting · 15 Factions ·
16 Procedural Events · 17 Optimization · 18 Content Expansion.

See `docs/ROADMAP.md` for per-phase delivery notes and the next-steps checklist.

---

## 14. Glossary

- **Actor / entity** — any in-world object implementing `IEntity`.
- **Component** — an `EntityComponent` child providing one slice of behaviour/data.
- **Resource** — a Godot `Resource` (`.tres`) holding authored data/content.
- **Hurtbox / Hitbox** — `Area3D`s that receive / deal damage.
- **Packet** — a `DamagePacket`, a self-contained description of one hit.
- **Team** — faction id on `CombatComponent` controlling friendly fire.
- **Autoload** — a Godot global singleton node declared in `project.godot`.
