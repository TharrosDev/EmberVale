# Development Roadmap

Phases are tackled in order. Each must leave the repository buildable and
playable. A phase is "done" when its systems function in-game **and** round-trip
through save/load.

| #  | Phase                | Status      | Notes                                                        |
| -- | -------------------- | ----------- | ------------------------------------------------------------ |
| 1  | Core Architecture    | ✅ Done      | EventBus, ServiceLocator, GameManager, Entity/Component, Stats, Save, sandbox |
| 2  | Player Controller    | ✅ Done      | First-person CharacterEntity, camera look, code-defined input, locomotion, melee |
| 3  | Combat Framework     | ✅ Done      | Hitbox/hurtbox, damage pipeline (armor/crit), weapons, combos, stamina, stagger |
| 4  | Enemy AI             | ⏳ Next      | Patrol/investigate/combat/retreat state machine              |
| 5  | Inventory System     | ⬜ Planned   | Item resources, stacks, container component                  |
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

## Phase 4 — next steps (Enemy AI)

1. `EnemyEntity` (CharacterEntity) reusing `LocomotionComponent` + combat.
2. AI state machine component: Idle → Patrol → Investigate → Combat → Retreat.
3. Perception (vision cone + last-known-position) driving transitions.
4. Navigation via `NavigationAgent3D`; chase and strafe behaviours.
5. Enemies attack the player through the existing hitbox/hurtbox pipeline.
