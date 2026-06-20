# Embervale

> An original first-person, open-world fantasy action RPG built in **Godot 4**
> with **C#**. Exploration, visceral melee and magic combat, deep character
> progression, and Diablo-style loot in a hand-crafted world with its own
> identity.

This repository is developed incrementally and is kept **buildable and
playable at every commit**. Working systems are always preferred over
theoretical ones.

---

## Tech stack

| Area            | Choice                                   |
| --------------- | ---------------------------------------- |
| Engine          | Godot 4.7 (.NET / Mono)                  |
| Language        | C# (`net8.0`)                            |
| Architecture    | Component-based entities, event-driven   |
| Data            | Resource-driven (`.tres` content)        |
| Target platforms| Windows, Linux, Steam Deck               |

## Getting started

1. Install the **.NET / Mono build** of Godot 4.7+ and the .NET 8 SDK.
2. Open the project: `godot --path . --editor` (or open `project.godot` in the editor).
3. Build the C# solution (Godot does this automatically on first run, or
   `dotnet build Embervale.sln`).
4. Press **Play**. The bootstrap sandbox loads `scenes/Main.tscn`.

### Sandbox controls

| Input        | Action                              |
| ------------ | ----------------------------------- |
| `W/A/S/D`    | Move                                |
| Mouse        | Look                                |
| `Shift`      | Sprint                              |
| `Space`      | Jump                                |
| Left mouse   | Melee attack                        |
| Right mouse  | Block                               |
| `E`          | Interact (pick up items, talk to NPCs) |
| `I`          | Toggle inventory                    |
| `J`          | Toggle quest journal                |
| `H`          | Heal the training dummy             |
| `R`          | Respawn the dummy immediately       |
| `F5` / `F9`  | Quick-save / quick-load             |
| `Esc`        | Toggle pause (frees the mouse)      |

The on-screen overlay shows live game state, FPS, and the target's resources —
proof that the core systems are wired together.

## Project layout

```
.
├── project.godot            # Engine config + autoload registration
├── Embervale.sln/.csproj    # C# solution
├── scenes/                  # Godot scenes (Main.tscn is the entry point)
├── data/                    # Resource-driven content (.tres) — attributes, etc.
├── docs/                    # Architecture & roadmap
└── src/
    ├── Core/                # Engine-level services (autoloads)
    │   ├── Events/          # EventBus + core event types
    │   ├── Services/        # ServiceLocator
    │   ├── Diagnostics/     # Logging
    │   ├── GameManager.cs   # Top-level state machine
    │   └── GameState.cs
    ├── Entities/            # Entity / CharacterEntity / EntityComponent framework
    ├── Stats/               # Stats, modifiers, attribute resources
    ├── Movement/            # LocomotionComponent (reusable motor)
    ├── Combat/              # Damage pipeline, hitbox/hurtbox, weapons
    ├── Items/               # Item resources, inventory, pickups, database
    ├── Interaction/         # InteractableComponent (interact action)
    ├── Quests/              # Quest resources, objectives, log, givers
    ├── Dialogue/            # Node-graph conversations, choices, story flags
    ├── World/               # World clock / time-of-day
    ├── Npc/                 # NPC daily schedules + reactions
    ├── Player/              # First-person controller + factory
    ├── Enemies/             # Enemy AI state machine + spawner
    ├── Save/                # ISaveable + SaveManager
    ├── UI/                  # DebugHud (and future gameplay UI)
    └── Bootstrap/           # GameBootstrap entry point
```

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for how the pieces fit
together and [`docs/ROADMAP.md`](docs/ROADMAP.md) for the full development plan.

## Roadmap

These 18 phases build the game's **systems and infrastructure** — not the game's
content. Completing them yields a powerful, data-driven sandbox that *can express*
the game; the actual world, narrative, art, audio, balance and ship polish are a
**separate content/production roadmap** introduced once these systems are done.
See [`docs/ROADMAP.md`](docs/ROADMAP.md) for the full scope note.

Phases are built in order; the repo stays buildable and playable at every step.
A phase is "done" when its systems work in-game **and** round-trip through
save/load.

| #  | Phase                | Status     |
| -- | -------------------- | ---------- |
| 1  | Core Architecture    | ✅ Done     |
| 2  | Player Controller    | ✅ Done     |
| 3  | Combat Framework     | ✅ Done     |
| 4  | Enemy AI             | ✅ Done     |
| 5  | Inventory System     | ✅ Done     |
| 6  | Equipment System     | ✅ Done     |
| 7  | Loot Generation      | ✅ Done     |
| 8  | Progression System   | ✅ Done     |
| 9  | Quest Framework      | ✅ Done     |
| 10 | Dialogue System      | ✅ Done     |
| 11 | NPC Schedules        | ✅ Done     |
| 12 | Magic System         | ⏳ Next     |
| 13 | World Systems        | ⬜ Planned  |
| 14 | Crafting             | ⬜ Planned  |
| 15 | Faction Systems      | ⬜ Planned  |
| 16 | Procedural Events    | ⬜ Planned  |
| 17 | Optimization         | ⬜ Ongoing  |
| 18 | Content Expansion    | ⬜ Ongoing  |
