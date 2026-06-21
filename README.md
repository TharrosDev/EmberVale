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
| `Q`          | Cast the prepared spell             |
| `F`          | Cycle the prepared spell            |
| `E`          | Interact (pick up items, talk to NPCs) |
| `I`          | Toggle inventory                    |
| `J`          | Toggle quest journal                |
| `H`          | Heal the training dummy             |
| `R`          | Respawn the dummy immediately       |
| `F5` / `F9`  | Quick-save / quick-load             |
| `F1`         | Toggle the developer console        |
| `F3`         | Toggle the developer debug overlay  |
| `F4`         | Toggle the profiler overlay         |
| `Esc`        | Open the pause menu                 |

The game HUD shows vitals, the prepared spell, active effects, a quest tracker,
time/weather, world-event banners, an aimed-target nameplate, and interaction
prompts. `Esc` opens a pause menu (Resume / Save / Load / Quit); `F3` reveals the
developer debug overlay (FPS, raw stats, the active world event).

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
    │   ├── Pooling/         # Generic NodePool (object reuse)
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
    ├── World/               # Clock, day/night, weather, encounters, world events
    ├── Npc/                 # NPC daily schedules + reactions
    ├── Magic/               # Spells, projectiles, AoE, status effects
    ├── Crafting/            # Recipes, stations, the crafting component
    ├── Factions/            # Reputation, faction tags, standing-driven hostility
    ├── Player/              # First-person controller + factory
    ├── Enemies/             # Enemy AI state machine + spawner
    ├── Save/                # ISaveable + SaveManager
    ├── Debugging/           # Dev console, profiler, invariants, repro harness
    ├── UI/                  # Game HUD, pause menu, toasts, panels, shared theme
    └── Bootstrap/           # GameBootstrap entry point
```

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for how the pieces fit
together and [`docs/PRODUCTION_ROADMAP.md`](docs/PRODUCTION_ROADMAP.md) for the
full development plan.

## Status

The 21-phase **systems roadmap is complete** — Embervale is a powerful,
data-driven sandbox that *can express* the game. The
[**production roadmap**](docs/PRODUCTION_ROADMAP.md) (Phases 22+) now carries it
from that sandbox to launch, gated First Playable → Vertical Slice → Alpha →
Beta → Release Candidate → Launch.

|              | Phase                                      |
| ------------ | ------------------------------------------ |
| ▶ **Current** | 21 — Content Expansion *(systems complete; production begins)* |
| ⏭ **Next**    | 22 — Production Bible & Content Pipeline    |

> Updated as each phase lands. The repo stays buildable and playable at every
> step; a phase is "done" when it works in-game **and** round-trips through
> save/load. See [`docs/PRODUCTION_ROADMAP.md`](docs/PRODUCTION_ROADMAP.md) for
> the full phase list and gates.
