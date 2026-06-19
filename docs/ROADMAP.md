# Development Roadmap

Phases are tackled in order. Each must leave the repository buildable and
playable. A phase is "done" when its systems function in-game **and** round-trip
through save/load.

| #  | Phase                | Status      | Notes                                                        |
| -- | -------------------- | ----------- | ------------------------------------------------------------ |
| 1  | Core Architecture    | ✅ Done      | EventBus, ServiceLocator, GameManager, Entity/Component, Stats, Save, sandbox |
| 2  | Player Controller    | ✅ Done      | First-person CharacterEntity, camera look, code-defined input, locomotion, melee |
| 3  | Combat Framework     | ⏳ Next      | Melee/ranged hitboxes, damage pipeline, stagger, crits       |
| 4  | Enemy AI             | ⬜ Planned   | Patrol/investigate/combat/retreat state machine              |
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

## Phase 3 — next steps (Combat Framework)

1. `WeaponResource` (damage, speed, range, type) — resource-driven.
2. Damage pipeline: armor mitigation, crit roll, damage types/resistances.
3. Attack states: wind-up, active hitbox, recovery; combos and heavy attacks.
4. Blocking, parrying, stagger/poise on `StatsComponent`.
5. Hitbox/hurtbox `Area3D` components replacing the demo melee raycast.
