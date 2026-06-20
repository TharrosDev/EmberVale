# Development Roadmap

## Scope: this is the *systems* roadmap, not the game

These 18 phases build **base-game infrastructure** — the reusable systems and
engine-on-top-of-Godot that the game runs on (architecture, player, combat, AI,
items/loot, progression, quests, dialogue, magic, world systems, crafting,
factions, events, optimization). They are *capabilities*, not content.

**Completing all 18 phases does not mean the game is finished.** When this roadmap
is done we will have a powerful, well-factored, data-driven sandbox that *can
express* the game — but it will still be a near-empty playground. The actual game
is authored on top of these systems and is explicitly **out of scope here**:

- World: regions, level geometry, points of interest, the map.
- Narrative: story, characters, and the actual quest/dialogue *content*.
- Specific enemies, bosses, items, spells, recipes (beyond sandbox test data).
- Balance, economy, difficulty curves.
- Art, models, animation, audio, music, VFX, UI polish.
- Meta/shell: main menu, settings, new-game and save-slot flow, onboarding.

The architecture is deliberately resource-driven so most of that content lands as
authored `.tres` data against these systems, not new code (item/affix/loot/
attribute resources today; quest/dialogue graphs, spells, recipes later).

A **separate content/production roadmap** (world, story, art, audio, balance, ship
polish) will be introduced once this systems roadmap is complete — that is the
larger body of work that turns the toolkit into a shippable game.

> Caveat: a few phases blur the line — Magic (12) and Crafting (14) need some
> content to be meaningful, and Content Expansion (18) is the seam where systems
> and content begin to overlap (hence ⬜ Ongoing rather than a finite deliverable).

## Phases

Phases are tackled in order. Each must leave the repository buildable and
playable. A phase is "done" when its systems function in-game **and** round-trip
through save/load.

| #  | Phase                | Status      | Notes                                                        |
| -- | -------------------- | ----------- | ------------------------------------------------------------ |
| 1  | Core Architecture    | ✅ Done      | EventBus, ServiceLocator, GameManager, Entity/Component, Stats, Save, sandbox |
| 2  | Player Controller    | ✅ Done      | First-person CharacterEntity, camera look, code-defined input, locomotion, melee |
| 3  | Combat Framework     | ✅ Done      | Hitbox/hurtbox, damage pipeline (armor/crit), weapons, combos, stamina, stagger |
| 4  | Enemy AI             | ✅ Done      | Perception FSM (idle/patrol/investigate/combat/retreat), coordination, spawner |
| 5  | Inventory System     | ✅ Done      | Item resources + database, stacking inventory, pickups, UI, save |
| 6  | Equipment System     | ✅ Done      | Slots, equippable items, stat bonuses, weapon swap, character UI |
| 7  | Loot Generation      | ✅ Done      | Item instances, procedural affixes, drop tables, loot component |
| 8  | Progression System   | ✅ Done      | XP, levels, per-level stat growth, skill points, perks      |
| 9  | Quest Framework      | ✅ Done      | Event-driven objectives, quest log, rewards, givers, save   |
| 10 | Dialogue System      | ✅ Done      | Node-graph conversations, choices, conditions/effects, story flags |
| 11 | NPC Schedules        | ✅ Done      | World clock, data-driven daily routines, event-driven reactions |
| 12 | Magic System         | ⏳ Next      | Schools, projectiles, AoE, status effects                   |
| 13 | World Systems        | ⬜ Planned   | Day/night, weather, encounters                              |
| 14 | Crafting             | ⬜ Planned   | Recipes, stations, materials                                |
| 15 | Faction Systems      | ⬜ Planned   | Reputation, consequences                                    |
| 16 | Procedural Events    | ⬜ Planned   | World events, dynamic spawns                                |
| 17 | Optimization         | ⬜ Ongoing   | Pooling, LOD, streaming                                     |
| 18 | Content Expansion    | ⬜ Ongoing   | Regions, enemies, quests via data                           |

## Phase 1 — delivered

Core architecture foundation that everything else builds on:

- **EventBus** — typed pub/sub decoupling all systems.
- **ServiceLocator** — registry for world-scoped systems.
- **GameManager / GameState** — top-level flow machine with pause handling.
- **Entity + EntityComponent** — composition-based actor framework.
- **Stats** — `Stat`/`StatModifier`/`AttributeSet`/`StatsComponent` with the
  full modifier pipeline and resource (HP/STA/MP) handling.
- **Save** — `ISaveable` + `SaveManager` writing versioned JSON to `user://`.
- **Sandbox** — `GameBootstrap` + `DebugHud`: a runnable scene that damages,
  heals, kills, respawns, saves, and loads a component-based entity.

## Phase 2 — delivered (Player Controller)

- Generalized the actor framework: `IEntity` interface + shared `EntityNode`
  helpers, so kinematic `CharacterEntity` (CharacterBody3D) and static `Entity`
  (Node3D) are both first-class component hosts. Events now carry `IEntity`.
- `GameInput`: input actions defined in code (WASD, jump, sprint, interact,
  attack, cast, pause) — type-checked, no fragile `project.godot` input block.
- `LocomotionComponent`: reusable ground motor (gravity, accel, jump,
  `MoveAndSlide`) driven by the `MoveSpeed` stat; AI will reuse it.
- `PlayerController`: first-person mouse-look (body yaw + camera pitch), drives
  locomotion, and a melee raycast that feeds the Phase 1 damage pipeline.
- `PlayerFactory`: assembles the player (collision, stats, camera, components).
- Sandbox: player walks the world and can melee the dummy to death; floor and
  dummy now have physics colliders.

## Phase 3 — delivered (Combat Framework)

- `DamageType` + `DamagePacket`/`DamageResult` value types and `CombatMath`
  (attacker-side crit roll & power scaling; defender-side armor mitigation).
- `Hitbox`/`Hurtbox` `Area3D` components on dedicated collision layers
  (`CombatLayers`); hitboxes poll overlaps during their active window and hit
  each target once.
- `CombatComponent`: poise/stagger, blocking, and the defender damage pipeline
  feeding `StatsComponent`; raises `DamageDealtEvent`/`EntityStaggeredEvent`.
- `WeaponResource` (resource-driven) + `MeleeWeaponComponent`: wind-up/active/
  recovery state machine with combos, finishers, stamina cost and attack-speed
  scaling.
- Player wields an Iron Sword (LMB attack, RMB block); the dummy has a hurtbox
  and combat component. `StatsComponent` gained passive stamina/mana regen.

## Phase 4 — delivered (Enemy AI)

- `EnemyEntity` (CharacterEntity) + `PlayerCharacter` marker type so enemies can
  resolve the player distinctly via the `ServiceLocator`.
- `EnemyAIComponent`: an Idle → Patrol → Investigate → Combat → Retreat → Dead
  state machine that reuses `LocomotionComponent` to move and
  `MeleeWeaponComponent` to attack — the same systems the player uses.
- Perception: vision range + FOV cone gated by a line-of-sight raycast, plus a
  short-range proximity sense; tracks a last-known position for investigation.
- Group coordination: spotting the player broadcasts `EnemyAlertedEvent`, pulling
  nearby idle/patrolling allies to investigate.
- Friendly fire prevented via a `Team` on `CombatComponent` honored by hitboxes.
- `EnemyFactory` + `EnemySpawnDirector` maintain a goblin camp population; dead
  enemies despawn and are replaced.
- Player can now be killed by enemies and respawns at the start.

## Phase 5 — delivered (Inventory System)

- `ItemResource` (`.tres`-driven: id, name, type, rarity, stack size, weight,
  value) + `ItemDatabase` that indexes `data/items/` by id for save/loot lookup.
- `ItemStack` runtime quantities; `InventoryComponent` (slot-based, stacking,
  add/remove/count, weight tracking) implementing `ISaveable`.
- `InteractableComponent` interaction base + raycast `interact` in the player
  controller; `ItemPickupComponent`/`ItemPickupFactory` for world pickups.
- `InventoryPanel` UI (toggle with `I`); goblins drop hide/gold on death.

## Phase 6 — delivered (Equipment System)

- `EquipmentSlot` enum + `EquippableItemResource` (slot, flat stat bonuses, optional
  `WeaponResource`) layered over `ItemResource`.
- `EquipmentComponent`: equip/unequip moves items to/from the `InventoryComponent`,
  applies stat bonuses as `StatModifier`s sourced to the item, and swaps the active
  weapon on the `MeleeWeaponComponent` (restoring the baseline on unequip).
  Implements `ISaveable`.
- The character screen (`InventoryPanel`) now shows equipment slots and the backpack
  with Equip/Unequip buttons; opening it frees the mouse (`UiState.MenuOpen`).
- Gear pickups in the sandbox: steel sword, leather cap/vest, hunter's ring.

## Phase 7 — delivered (Loot Generation)

- **`ItemInstance`** — an item-instance layer over `ItemResource` carrying a rolled
  `ItemRarity`, a generated display name (prefix + base + suffix), and a frozen list
  of `ItemAffix`es. Mundane items are plain instances (`ItemInstance.Plain`); only
  affix-less instances stack, so rolled gear is unique. `ItemStack` now holds an
  instance; inventory, equipment, pickups, UI and save/load all flow instances.
- **Affixes** — `AffixDefinition` (`[GlobalClass]` `.tres` under `data/affixes/`)
  declares a stat, value range, minimum rarity, gear-family applicability and weight.
  `AffixDatabase` indexes them and queries the eligible pool per equippable+rarity;
  a rolled `ItemAffix` maps onto a `StatModifier` sourced to its instance.
- **Drop tables** — `LootTable` + `LootEntry` (`[GlobalClass]` `.tres` under
  `data/loot/`) describe independent per-entry drop chances, quantities, an
  optional gold roll and a quality bias. `LootGenerator` rolls a table into
  `LootDrop`s: it picks rarity (`LootRarity`, quality-weighted), draws distinct
  affixes by weight, and rolls each value scaled by rarity/quality.
- **`LootComponent`** — on death, an actor rolls its `LootTable` and spawns a world
  pickup per drop, scattered around the corpse. Goblins now loot from
  `data/loot/GoblinLoot.tres` (hide/ore/potion/affixed sword + gold), replacing the
  hard-coded goblin-hide drop.
- Equipped instances apply template flats **and** rolled affixes to stats; the
  inventory/equipment screens show rarity colours and per-affix bonus lines; all of
  it round-trips through save/load (instances persist id + rarity + name + affixes).
- The sandbox seeds one procedurally-rolled Rare blade so the pipeline is visible
  the moment you press Play.

## Phase 8 — delivered (Progression System)

- **Kill attribution** — `EntityDiedEvent` now carries the optional `Killer`;
  `StatsComponent.ApplyDamage(amount, source)` threads the attacker (from
  `DamagePacket.Source`) into the death event so kills can be credited.
- **`ProgressionResource`** (`[GlobalClass]`, `data/progression/*.tres`) — the XP
  curve (`BaseXpToLevel × level^exponent`), level cap, per-level flat stat gains and
  skill points per level. **`ExperienceComponent`** — a passive XP bounty on
  enemies (goblins grant 25).
- **`ProgressionComponent`** (`ISaveable`) — listens for `EntityDiedEvent`s it
  caused, awards the dead entity's XP, resolves multi-level-ups against the curve,
  re-derives cumulative per-level stat growth as `StatModifier`s sourced to itself,
  refills resources on level-up, and banks skill points. Persists level / XP /
  unspent points (growth is recomputed from level, never stored).
- **Perks** — `PerkResource` (`[GlobalClass]`, `data/perks/*.tres`) + `PerkDatabase`
  + **`PerksComponent`** (`ISaveable`): rankable passives that spend skill points and
  apply a stat bonus as a `StatModifier` sourced to the perk; ranks persist and
  re-apply on load. Five starter perks (Toughness, Might, Precision, Endurance
  Training, Warding).
- **UI** — the HUD shows `Level / XP / SP`; the character screen (`I`) shows
  progression and a PERKS section with Learn buttons. Raises `XpGainedEvent` /
  `LeveledUpEvent` / `PerkChangedEvent`. Debug key **`X`** grants 50 XP.

## Phase 9 — delivered (Quest Framework)

- **`IEntity.TemplateId`** — lifted onto the interface so quests can match a slain
  actor to its archetype id (`Entity`/`CharacterEntity` already exposed it).
- **Quest content** — `QuestResource` (`[GlobalClass]`, `data/quests/*.tres`) with
  `ObjectiveResource` (Kill / Collect, target id, required count) and
  `QuestItemReward` sub-resources, XP/gold rewards and an optional
  `PrerequisiteQuestId` for chaining. `QuestDatabase` indexes them (the standard
  static-database pattern). Objective/reward arrays are authored untyped and read back
  by element cast (as `LootTable.Entries`).
- **`QuestLogComponent`** (`ISaveable`, on the player) — holds `QuestProgress` per
  quest, advances objectives from `EntityDiedEvent` (kills it caused) and
  `ItemPickedUpEvent`, and on completion grants rewards through the sibling
  `ProgressionComponent` (XP) and `InventoryComponent` (gold + items). Raises
  `QuestStartedEvent` / `QuestObjectiveAdvancedEvent` / `QuestCompletedEvent`; persists
  the full log.
- **`QuestGiverComponent`** (`InteractableComponent`) — an NPC offers a quest via the
  existing `E` raycast interact, honouring prerequisites and in-progress/completed state.
- **UI** — `QuestLogPanel`, a non-modal read-only overlay toggled with `J`, lists
  active quests with per-objective progress and a completed section; the HUD shows a
  compact active-quest tracker.
- **Sandbox** — "Cull the Goblins" (kill 3 goblins) auto-starts on Play; a Village
  Elder near spawn offers "Gather Iron" (collect 3 iron ore). Both round-trip
  through save/load.

## Phase 10 — delivered (Dialogue System)

- **Dialogue content** — a node-graph conversation as `.tres` under `data/dialogue/`:
  `DialogueResource` (`[GlobalClass]`: id, speaker, start node, nodes) holds
  `DialogueNode` sub-resources (id, speaker override, line text, choices), each with
  `DialogueChoice` sub-resources (reply text, `Goto` target node, a gating
  `DialogueCondition` and a fired `DialogueEffect`). Node/choice arrays are authored
  untyped and read back by element cast (the same pattern as quests/loot).
  `DialogueDatabase` indexes them by id (the standard static-database pattern). New
  conversation = a `.tres`, no code change.
- **Conditions & effects (declarative, no scripting)** — a choice is hidden unless its
  `DialogueCondition` passes (`QuestAvailable` / `QuestActive` / `QuestCompleted` /
  `QuestNotStarted` / `HasFlag` / `MissingFlag`), and picking it fires a
  `DialogueEffect` (`StartQuest`, `SetFlag`, `ClearFlag`). `DialogueSession` — a plain
  runtime walk of a conversation — evaluates conditions and applies effects against the
  player's `QuestLogComponent` and `StoryFlagsComponent`, keeping the UI a thin view.
- **`StoryFlagsComponent`** (`ISaveable`, on the player) — a persistent set of named
  boolean flags giving conversations memory ("you've met the elder"). Deliberately
  general so later systems (NPC schedules, world events) can read/write the same flags;
  raises `StoryFlagChangedEvent` and round-trips through save/load.
- **`DialogueComponent`** (`InteractableComponent`) — an NPC that opens a conversation
  on the player's `E` interact by publishing a `DialogueStartedEvent`. This replaces the
  bare quest-giver: offering a quest is now just a choice effect inside a conversation.
- **UI** — `DialoguePanel`, a modal window driven entirely by `DialogueStartedEvent`:
  it builds a session, renders the speaker line and condition-filtered choice buttons,
  applies effects on click and advances, and (like the character screen) frees the mouse
  and sets `UiState.MenuOpen` while open. Rebuilds from a dirty flag so a choice never
  frees its own button mid-signal.
- **Sandbox** — the Village Elder near spawn now *talks*: his conversation offers
  "Gather Iron" (a `StartQuest` choice gated on `QuestAvailable`), branches on the
  quest's state while it's active/completed, and on a thank-you sets the
  `flag.elder_thanked` story flag — which unlocks a friendlier greeting on later visits.
  Flags persist across save/load.

## Phase 11 — delivered (NPC Schedules)

- **World clock** — `WorldClock` (`Node`, `ISaveable`, `ServiceLocator`-registered) advances
  a 24-hour day at a configurable real-time rate and announces each new hour via
  `TimeOfDayChangedEvent(Hour, DayPhase)`. It is the minimal time source schedules need;
  **Phase 13 (World Systems)** will build the fuller day/night + weather model on top. The
  time of day persists through save/load, so routines resume where they left off. A
  `DayPhase` (Night/Dawn/Day/Dusk) is derived from the hour; the HUD shows the clock.
- **Schedule content** — `ScheduleResource` (`[GlobalClass]`, `data/schedules/*.tres`) holds
  `ScheduleEntry` sub-resources (`StartHour`, `Activity` label, `Destination`). Entries are
  authored untyped and read by element cast (the established pattern); `EntryForHour(hour)`
  resolves the active block (wrapping pre-dawn hours to the last block). `ScheduleDatabase`
  indexes them. New routine = a `.tres`, no code change.
- **`ScheduleComponent`** (`EntityComponent`, on a static NPC `Entity`) — reads the clock,
  walks the host toward the current block's destination with a simple kinematic step
  (villagers need no physics), facing where it goes, and reports its `Activity` via
  `NpcActivityChangedEvent`. **Reactions** are event-driven: a nearby `EnemyAlertedEvent`
  triggers a timed flee away from the threat (overriding the routine), and a
  `DialogueStartedEvent` where it is the speaker freezes it to face the player until the
  `DialogueEndedEvent`.
- **Sandbox** — the Village Elder now keeps a daily routine: the well at dawn, the forge by
  day, the square at midday, home by dusk, asleep at night — visibly walking between them as
  the (fast) clock turns. Lure goblins near and he flees; talk to him and he stops to face
  you. The clock's time of day round-trips through save/load.
