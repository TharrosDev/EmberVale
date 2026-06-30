# Embervale

> An original third-person, open-world fantasy action RPG built in **Godot 4**
> with **C#**. Exploration, visceral melee and magic combat, deep character
> progression, Diablo-style loot, and a corruption system that reshapes how a
> dying world reacts to you — all in a hand-crafted world with its own identity.

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

### Validate content (headless)

Check that all authored content is well-formed without entering gameplay:

```
godot --headless --path . -- --validate
```

This runs the full content validator (cross-references, well-formedness, and dialogue/
quest graph reachability) and exits **0** on pass or **1** on any issue — a one-command
content gate for scripts and CI. The same battery is available in-game via the
`validate-all` developer-console command.

### Sandbox controls

| Input        | Action                              |
| ------------ | ----------------------------------- |
| `W/A/S/D`    | Move                                |
| Mouse        | Look / orbit the third-person camera |
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
| `X`          | Grant XP (debug)                    |
| `P`          | Add corruption (debug)              |
| `F5` / `F9`  | Quick-save / quick-load             |
| `F1`         | Toggle the developer console        |
| `F3`         | Toggle the developer debug overlay  |
| `F4`         | Toggle the profiler overlay         |
| `Esc`        | Open the pause menu                 |

The game HUD shows vitals, the prepared spell, active effects, a quest tracker,
time/weather, world-event banners, an aimed-target nameplate, and interaction
prompts; a dark blood-red vignette bleeds in at high corruption. The character
screen (`I`) carries a corruption gauge alongside progression, equipment, perks
and reputation. `Esc` opens a pause menu (Resume / Save / Load / Quit); `F3`
reveals the developer debug overlay (FPS, raw stats, corruption, the active
world event).

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
    ├── Corruption/          # Corruption meter, tiers, appearance + dialogue hooks
    ├── Player/              # Third-person controller + factory
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

**Phase 22** (Production Bible & Content Pipeline) and **Phase 23 — The Corruption
System** (the LORE's defining mechanic) are complete. Corruption ships the 0–100
meter with tier thresholds, save/load, a dev console + F3 readout, dialogue
conditions/effects, a character-screen gauge, the dread vignette, the per-tier
appearance hook, global NPC "dread" standing (the world turns hostile as you
corrupt), tier-gated corrupted abilities, and the `EndingEligibility` dial behind
the two endings (23A–23H).

**Phases 24–29 are complete**, carrying the sandbox to **Gate G0 — First Playable**:
the meta-shell & localization spine (24), region streaming + world map + fast travel (25),
playable races & character creation (26), the Ember Crown greybox + navmesh + the Warband
quest arc (27), the Iron King boss with the defeat→relic→absorb→corruption beat (28), and
the full **Combat Feel** pass (29A–29I: hit-stop, camera shake, weapon trails, crit/block/
stagger/parry screen feedback, dodge i-frames, parry/riposte, lock-on, input buffering, and
the anti-mash stamina pacing). **Phase 29.5 — Spellcraft & the Fading Weave** is in progress
(29.5A charged/channeled casts; 29.5B school identities; 29.5C school mastery; 29.5D spell combos).

|              | Phase                                          |
| ------------ | ---------------------------------------------- |
| ✅ **Done**    | 22–29 + G0 First Playable; 29.5A–29.5D          |
| ▶ **Current** | 29.5 — Spellcraft & the Fading Weave            |
| ⏭ **Next**    | 29.5E — The fading Weave (region potency)       |

> Updated as each phase lands. The repo stays buildable and playable at every
> step; a phase is "done" when it works in-game **and** round-trips through
> save/load. See [`docs/PRODUCTION_ROADMAP.md`](docs/PRODUCTION_ROADMAP.md) for
> the full phase list and gates.
