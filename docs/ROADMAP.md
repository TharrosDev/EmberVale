# Development Roadmap

Phases are tackled in order. Each must leave the repository buildable and
playable. A phase is "done" when its systems function in-game **and** round-trip
through save/load.

| #  | Phase                | Status      | Notes                                                        |
| -- | -------------------- | ----------- | ------------------------------------------------------------ |
| 1  | Core Architecture    | ✅ Done      | EventBus, ServiceLocator, GameManager, Entity/Component, Stats, Save, sandbox |
| 2  | Player Controller    | ✅ Done      | First-person CharacterEntity, camera look, code-defined input, locomotion, melee |
| 3  | Combat Framework     | ✅ Done      | Hitbox/hurtbox, damage pipeline (armor/crit), weapons, combos, stamina, stagger |
| 4  | Enemy AI             | ✅ Done      | Perception FSM (idle/patrol/investigate/combat/retreat), coordination, spawner |
| 5  | Inventory System     | ⏳ Next      | Item resources, stacks, container component                  |
| 6  | Equipment System     | ⬜ Planned   | Slots, stat modifiers from gear                              |
| 7  | Loot Generation      | ⬜ Planned   | Rarity tiers, procedural affixes, drop tables                |
| 8  | Progression System   | ⬜ Planned   | XP, levels, skills, perks                                    |
| 9  | Quest Framework      | ⬜ Planned   | Objectives, branching, consequences                         |
| 10 | Dialogue System      | ⬜ Planned   | Node-graph conversations, choices                           |
| 11 | NPC Schedules        | ⬜ Planned   | Daily routines, reactions                                   |
| 12 | Magic System         | ⬜ Planned   | Schools, projectiles, AoE, status effects                   |
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

## Phase 5 — next steps (Inventory System)

1. `ItemResource` (id, name, icon, stack size, type) — resource-driven.
2. `InventoryComponent`: slots, stacking, add/remove/query, weight/capacity.
3. Item pickups in the world (interactable entities) using the interact action.
4. Inventory UI panel; save/load of inventory contents.
5. Foundation for equipment (Phase 6) and loot (Phase 7).
