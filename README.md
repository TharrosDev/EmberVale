# Embervale

> An original third-person, open-world fantasy action-RPG built in **Godot 4.7**
> with **C# (.NET 8)**. A dying world whose magic is failing — explore it, fight
> with weight, master a deep spell system, and let a **corruption** system reshape
> how that world reacts to you, all the way to one of two endings.

Embervale is a solo-built, component-driven game developed **incrementally** and
kept **buildable and playable at every commit**. A working ugly prototype always
beats a beautiful broken feature — every system here is real and usable in-game the
moment it lands, never theoretical scaffolding.

> **Private / personal project** — not for sale or public release.

---

## What is Embervale?

Nyth, the goddess of magic, is dead, and the **Weave** that carries all magic is
fading. Into that failing world comes corruption — an easier, darker path to power
that the world can feel. Embervale is the game built on that premise: a hand-crafted
realm with its own lore, factions and history, where your choices (and how corrupt
you let yourself become) bend the world's reactions, the spells you can claim, and
how your story ends.

Under the hood it is a **data-driven sandbox**: actors are composed from components,
systems talk over an event bus, and nearly all content (spells, enemies, quests,
loot, regions, dialogue…) is authored as Godot `.tres` resources — so new content is
new data, not new code.

## Design pillars

Drawn from [`docs/DESIGN.md`](docs/DESIGN.md):

- **Combat with weight.** Poise & stagger are real and per-actor; hits land with
  freeze-frames, shake and feedback; commitment and timing matter. *No button
  mashing* — stamina gates offense and recovery punishes over-commitment.
- **Magic as the fading Weave.** Every school is meant to be a viable build spine,
  none a trap. Magic isn't bought — lost spellcraft is *recovered*, and the dying
  Weave makes ordinary magic weaker while the corrupted path grows stronger.
- **Breadth without a class lock.** Melee, ranged and magic share one stat spine;
  the build is authored by the player over time, not chosen at creation.
- **A corruption spine.** A single 0–100 meter threads combat, dialogue, the world's
  hostility toward you, gated abilities, and the two endings.
- **A hand-crafted, reactive world.** Streamed regions, a day/night clock, weather,
  NPC routines, factions and emergent events — not a procedural wallpaper.

## Feature tour

Everything below is implemented and playable today.

### Combat feel
Hit-stop / freeze-frames, camera shake, weapon trails, directional hit reactions,
crit / block / stagger / parry screen feedback, **dodge i-frames**, **parry & riposte**
windows, **lock-on** with soft targeting and target cycling, input buffering, and an
anti-mash **stamina & poise economy**. Damage flows through one pipeline
(`CombatMath`) with armor mitigation, crits and poise damage.

### Magic — Spellcraft & the fading Weave *(the marquee system)*
- **Cast archetypes** — every spell is **Instant**, **Charged** (hold to empower) or
  **Channeled** (a sustained beam at a mana-per-second cost).
- **Six school identities**, each playing differently rather than just re-tinting:
  **Fire** stacking ignite · **Frost** chill→freeze · **Lightning** chain-to-nearby ·
  **Nature** regrowth heal-over-time · **Necrotic** lifesteal (corrupted) · **Arcane**
  wards.
- **School mastery** — casting a school ranks a persistent mastery track that
  empowers it; a "hard to master" ceiling, not just bigger numbers.
- **Reactive combos** — cross-school reads: *Shatter* (Lightning into a Chilled foe),
  *Thermal Shock* (Fire into Chilled), each consuming the status it triggers on.
- **The fading Weave** — a per-region magic-potency dial: as the Weave fails,
  ordinary casts weaken and cost more while **corrupted** casts strengthen and
  cheapen. Lost spells are **recovered** from tomes/teachers, not vendored.
- **Enemy casters** — foes cast back: they hold range, **kite** when crowded, lob
  spells, ward themselves and heal wounded allies — reusing the *same* casting system
  the player does.

### The world
Distance-based **region streaming** (with a settle-gated loading screen), hard region
transitions + **fast travel**, a **world map** and HUD **compass**, a day/night
**world clock**, **weather** (clear/rain/storm/fog…), ambient **encounters** by time
of day, and announced **world events** (raids, supply caches, champion hunts).

### Character & loot
Six playable **races** + character creation, XP / levels with a **perk** tree,
**Diablo-style loot** (rarities + prefix/suffix **affixes** rolled on drop),
**equipment** with stat bonuses, and **crafting** at typed stations (forge, workbench,
alchemy, cooking).

### A living world
NPCs walk daily **schedules** and react to threats and conversation; **factions** with
reputation drive who's hostile; **quests** with kill/collect objectives and
prerequisite chains; node-graph **dialogue** with conditional choices and side
effects (start quest, set flag, add corruption); and the **corruption** meter with its
tier-gated abilities, the dread vignette, a per-tier appearance hook, and **two
endings**.

### Meta-shell & save
Main menu (New Game / Continue / Load / Settings), **multi-slot saves** with rich
headers, a full UI suite (HUD, inventory, character screen, crafting, dialogue, map,
quest log, settings), and a **localization** layer — every player-facing string goes
through `Loc.T(...)`.

## Content at a glance

| Content | Count | Examples |
| ------- | ----: | -------- |
| Spells | 8 | Firebolt, Fireball, Flame Lance (charged), Frost Nova, Storm Conduit (channeled), Lesser Heal, Arcane Shield, Ember Siphon (corrupted) |
| Status effects | 5 | Burning (stacking), Chill, Frozen, Regrowth (HoT), Arcane Ward |
| Enemy archetypes | 3 | Goblin, Iron King (boss), Ashen Acolyte (caster) |
| Regions | 2 | The Ember Crown, Frostfang Reach |
| Factions | 3 | Goblin Marauders, Villagers, The Fallen |
| Races | 6 | Human, Draekyn, Grondar, Sylthari, Umbral, Valari |
| Items / weapons | 14 / 4 | potions, materials, leather/steel gear, relics |
| Quests / dialogues | 6 / 6 | the Warband arc, the Elder, vendors |
| Recipes / perks | 7 / 6 | iron ingot, steel sword, health potion · Might, Warding |
| Weather / encounters / events | 5 / 5 / 3 | storm, fog · goblin patrols · raid, cache, hunt |
| Affixes | 11 | Keen, Sturdy, Of the Tiger, Of Warding |

## Build & run

**Prerequisites:** the **.NET / Mono build** of Godot 4.7+ and the .NET 8 SDK.

```bash
# Build the C# solution (Godot also builds it on first run)
dotnet build Embervale.sln

# Open in the editor and press Play (boots scenes/Main.tscn → the sandbox)
godot --path . --editor

# Headless content gate — validates all authored content, exits 0/1, enters no gameplay
godot --headless --path . -- --validate

# Pure-logic unit tests (~300 across 43 files)
dotnet test tests/Embervale.Tests
```

> Editing C#? Run `dotnet build` **before** launching — running the project does not
> recompile and will otherwise load a stale binary.

### Sandbox controls

| Input | Action | | Input | Action |
| ----- | ------ |-| ----- | ------ |
| `W/A/S/D` | Move | | `Q` | Cast prepared spell |
| Mouse | Look / orbit camera | | `F` | Cycle prepared spell |
| `Shift` | Sprint | | Middle mouse | Lock-on (wheel cycles target) |
| `Space` | Jump | | `E` | Interact (pick up, talk) |
| `Ctrl` | Dodge roll (i-frames) | | `I` / `J` / `M` | Inventory / Journal / Map |
| Left mouse | Melee attack | | `1`–`9` | Hotbar |
| Right mouse | Block | | `Esc` | Pause menu |

**Debug shortcuts** (sandbox only): `H` heal dummy · `R` respawn dummy · `X` +50 XP ·
`P` +10 corruption · `K` shift goblin reputation · `F5`/`F9` quick save/load ·
`F1` dev console · `F3` debug overlay · `F4` profiler.

## Developer tooling

- **Dev console (`F1`)** — 40+ commands: `corruption`, `learn`, `mastery`, `weave`,
  `spawn <n> <templateId>`, `region`, `travel`, `quest`, `validate-all`, `repro`, …
- **Content validator** — cross-references, well-formedness, and dialogue/quest graph
  reachability; runs on boot, via `validate-all`, or headless with `--validate`.
- **Overlays** — `F3` debug HUD (FPS, raw stats, corruption, active event), `F4`
  profiler.
- **World integrity checker** — silently watches runtime invariants (every 5s).
- **Repro harness** — record a seed + command sequence, replay deterministically.
- **Unit suite** — pure-logic xUnit tests for the load-bearing math (combat, mastery,
  the Weave, status cadence, save-key policy, dialogue-graph analysis, …).

## Architecture in brief

Component-based entities (`IEntity` / `Entity` / `CharacterEntity` + `EntityComponent`),
an **event-driven** core (a synchronous `EventBus`), and **resource-driven** content
(`.tres` indexed by auto-loading databases). Four autoloads form the spine: `EventBus`,
`ServiceLocator`, `GameManager`, `SaveManager`. Any system that holds gameplay state
implements `ISaveable`. See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full
systems reference.

```
.
├── project.godot            # Engine config + autoload registration
├── Embervale.sln/.csproj    # C# solution (net8.0, Godot.NET.Sdk 4.7.0)
├── scenes/                  # Godot scenes (Main.tscn is the entry point)
├── data/                    # Resource-driven content (.tres), one folder per domain
├── docs/                    # Architecture, design, lore, roadmap
└── src/
    ├── Core/                # Autoloads (EventBus, ServiceLocator, GameManager, SaveManager), pooling, input
    ├── Entities/            # Entity / CharacterEntity / EntityComponent framework
    ├── Stats/               # Stats, modifiers, attribute resources
    ├── Movement/  Combat/    # Locomotion motor; damage pipeline, hit/hurtbox, combat feel
    ├── Magic/               # Spells, cast archetypes, schools, mastery, combos, the Weave, status effects
    ├── Items/  Loot/         # Inventory, equipment, pickups; loot tables + affixes
    ├── Progression/         # XP, levels, perks
    ├── Quests/  Dialogue/    # Objectives + log; node-graph conversations + story flags
    ├── World/  Npc/          # Clock, weather, encounters, events, regions/streaming, fast travel; schedules
    ├── Crafting/  Factions/  # Recipes + stations; reputation + standing-driven hostility
    ├── Corruption/          # The corruption meter, tiers, appearance + dialogue hooks, endings
    ├── Races/               # Playable races + character creation
    ├── Player/  Enemies/     # Third-person controller; perception-FSM AI (+ caster branch), spawning
    ├── Interaction/         # InteractableComponent (raycast interact)
    ├── Save/                # ISaveable, SaveManager, persistence directors
    ├── Localization/        # Loc string layer
    ├── UI/  Debugging/  Analytics/   # HUD/menus/theme; console, validators, overlays; event logging
    └── Bootstrap/           # GameBootstrap (assembles the sandbox)
```

## Status & roadmap

The 21-phase **systems roadmap is complete** — Embervale is a data-driven sandbox
that *can express* the game. The [**production roadmap**](docs/PRODUCTION_ROADMAP.md)
(Phases 22+) now carries it to launch through six hard gates:

| Gate | Bar | Status |
| ---- | --- | ------ |
| **G0 — First Playable** | one region, a boss, the corruption hook | ✅ Done (Phases 22–29) |
| **G1 — Vertical Slice** | a 30–60 min slice that looks & plays shipped | 🟢 In progress |
| **G2 — Alpha** | every system/mechanic exists | ⏭ Queued |
| **G3 — Beta** | all content in | ⏭ |
| **G4 — Release Candidate** | zero blocker bugs | ⏭ |
| **G5/G6 — Launch / Live** | shipped; then patches & content | ⏭ |

**Now:** closing **G0 → G1**. Phases 22–29 are done (corruption, meta-shell &
localization, region streaming + map + fast travel, races & creation, the Ember Crown
greybox, the Iron King boss, and the full combat-feel pass), and **Phase 29.5 —
Spellcraft & the Fading Weave — is complete**: cast archetypes, school identities,
mastery, combos, the Weave, enemy casters, and a school-grouped spellbook with one
signature spell per school.

|              | Phase                                          |
| ------------ | ---------------------------------------------- |
| ✅ **Done**    | 22–29 + G0 First Playable; 29.5 Spellcraft      |
| ▶ **Current** | 30 — Animation, Models & Visual Identity        |
| ⏭ **Next**    | 30.5 — UI & HUD Overhaul                        |

> A phase is "done" when it works in-game **and** round-trips through save/load.

## Documentation

| Doc | What it covers |
| --- | -------------- |
| [`CLAUDE.md`](CLAUDE.md) | Working agreement, conventions, gotchas, and content recipes |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Full systems reference |
| [`docs/DESIGN.md`](docs/DESIGN.md) | Design bible — pillars and intent |
| [`docs/LORE.md`](docs/LORE.md) | World/story bible |
| [`docs/PRODUCTION_ROADMAP.md`](docs/PRODUCTION_ROADMAP.md) | The Phase 22+ plan and gates |
| [`docs/SESSION_PLAYBOOK.md`](docs/SESSION_PLAYBOOK.md) | Per-phase sub-task breakdown |
| [`docs/IDS.md`](docs/IDS.md) | Content id naming scheme + audit |
| [`docs/STAGE_A_STATUS.md`](docs/STAGE_A_STATUS.md) | Stage-A (Phases 22–25) integration sign-off |
