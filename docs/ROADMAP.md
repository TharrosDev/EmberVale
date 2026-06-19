# Development Roadmap

Phases are tackled in order. Each must leave the repository buildable and
playable. A phase is "done" when its systems function in-game **and** round-trip
through save/load.

| #  | Phase                | Status      | Notes                                                        |
| -- | -------------------- | ----------- | ------------------------------------------------------------ |
| 1  | Core Architecture    | ✅ Done      | EventBus, ServiceLocator, GameManager, Entity/Component, Stats, Save, sandbox |
| 2  | Player Controller    | ⏳ Next      | First-person CharacterBody3D, camera, input map, movement    |
| 3  | Combat Framework     | ⬜ Planned   | Melee/ranged hitboxes, damage pipeline, stagger, crits       |
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

## Phase 2 — next steps (Player Controller)

1. `PlayerController` as an `Entity` with a `CharacterBody3D` root and a
   first-person `Camera3D`.
2. Input map (`move_*`, `jump`, `sprint`, `interact`, `attack`, `cast`).
3. `MovementComponent` driven by `MoveSpeed`/`Stamina` stats.
4. Mouse-look with sensitivity + Steam Deck gamepad support.
5. Replace the bootstrap camera with the player; keep the dummy as a target.
