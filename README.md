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
| Engine          | Godot 4.3 (.NET / Mono)                  |
| Language        | C# (`net8.0`)                            |
| Architecture    | Component-based entities, event-driven   |
| Data            | Resource-driven (`.tres` content)        |
| Target platforms| Windows, Linux, Steam Deck               |

## Getting started

1. Install the **.NET / Mono build** of Godot 4.3+ and the .NET 8 SDK.
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
| Left mouse   | Melee attack (damages the dummy)    |
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
    ├── Entities/            # Entity + EntityComponent framework
    ├── Stats/               # Stats, modifiers, attribute resources
    ├── Save/                # ISaveable + SaveManager
    ├── UI/                  # DebugHud (and future gameplay UI)
    └── Bootstrap/           # GameBootstrap entry point
```

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for how the pieces fit
together and [`docs/ROADMAP.md`](docs/ROADMAP.md) for the development plan.
