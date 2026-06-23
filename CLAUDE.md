# CLAUDE.md — Embervale

Authoritative guide for working in this repository. Read this first. It explains
what the project is, how it is built, the conventions, the gotchas that will bite
you, and step-by-step recipes for adding new content without breaking things. The
**architecture and the full systems reference live in
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)** (see §5) — read the relevant
section there before changing a system.

> **One-line summary:** Embervale is an original third-person, open-world fantasy
> action RPG built in **Godot 4.7** with **C# (.NET 8)**, using a component-based,
> event-driven, resource-driven architecture. The repo is kept **buildable and
> playable at every commit**.

---

## 1. Mission & working agreement

You are the lead engineer building this game incrementally. The non-negotiables:

- **Always keep the repo buildable and playable.** A working ugly prototype beats
  a beautiful broken feature.
- **Build real, functioning systems** — never theoretical scaffolding. Every
  feature must be usable in-game the moment it lands.
- **Persistence is not optional.** Any system that holds gameplay state must be
  able to save/load (implement `ISaveable`).
- **Prefer composition and data.** New actors = new components + new `.tres`
  resources, not new inheritance chains or hard-coded values.
- **Respect existing architecture.** Inspect before adding; don't duplicate
  systems; refactor when it lowers long-term cost.
- **Check the Godot Asset Library before building from scratch.** When adding a
  new feature/system (or art/shader/tool), first check whether the Godot Asset
  Library already has something that fits, and reuse it instead of reinventing —
  fetch it from the asset's linked GitHub repo (the connected Godot MCP has no
  one-click install) and adapt it to our architecture. **Only** reuse when it fits
  our needs *exactly* and its license is compatible (this build is **private/
  personal — never sold or published —** so prefer MIT/CC0/open; avoid paid or
  closed assets). A near-miss you have to fight is worse than building clean — in
  that case, **build from scratch** against our patterns. Note what you pulled and
  its license where it lands.
- **Work in phases** (see §9). Determine the next highest-priority task and do it.

---

## 2. Tech stack & environment

| Thing            | Value                                                           |
| ---------------- | --------------------------------------------------------------- |
| Engine           | Godot 4.7 (.NET / Mono build)                                   |
| Language         | C# targeting `net8.0`, `Nullable` enabled, `ImplicitUsings` off |
| SDK              | `Godot.NET.Sdk/4.7.0` (see `Embervale.csproj`)                  |
| Assembly / root ns | `Embervale`                                                   |
| Entry scene      | `scenes/Main.tscn` → `GameBootstrap` (`src/Bootstrap`)          |
| Target platforms | Windows, Linux, Steam Deck (Forward+ renderer)                  |

**A Godot MCP server (`mcp__godot__*`) is connected**, running
**Godot 4.7.stable.mono** — the same engine and version this project targets. Through
it you can actually build and run the game: `run_project` + `get_debug_output` to
launch and capture errors/logs, `stop_project` to stop, `launch_editor`,
`get_project_info`, and scene edits (`create_scene`/`add_node`/`load_sprite`/
`save_scene`). **Prefer running the project to verify non-trivial changes** rather than
only reasoning about them.

⚠️ **`run_project` does NOT recompile C#.** It launches whatever `Embervale.dll` was
last built, so after editing any `.cs` you MUST rebuild first or the MCP runs a **stale
binary** (a silent trap — a behaviour-preserving change looks "verified" while your edit
never ran). The shell here **has `dotnet` 8.0**: rebuild with
`dotnet build Embervale.sln` (output goes to `.godot/mono/temp/bin/Debug/Embervale.dll`,
where the game loads it), *then* `run_project`. Run the pure-logic unit suite with
`dotnet test tests/Embervale.Tests`.

Other caveats: `run_project` launches the **real game window** — use it deliberately and
`stop_project` when done; it is not a headless check. The `WorldIntegrityChecker` (5s)
stays silent unless an invariant breaks, so give a run several seconds before trusting a
clean log. When you have **not** built+run something, say it was *reviewed against the
Godot 4.7 C# API* — reserve "verified/tested running" for output you actually captured.

There is **no CI** (the maintainer declined to add GitHub Actions). The green
**Vercel** check that appears on every PR is a meaningless no-op — Vercel is
trying to deploy a Godot game as a web app. Ignore it; do not treat it as a
build signal.

---

## 3. Build & run

**For the human:**
1. Install Godot 4.7+ **.NET build** and the .NET 8 SDK.
2. Open `project.godot` in the editor (it builds C# automatically), or
   `dotnet build Embervale.sln`.
3. Press Play. `scenes/Main.tscn` boots the sandbox.

**For you (Claude), via the Godot MCP** (see §2): after any `.cs` change, first
`dotnet build Embervale.sln` (the shell has dotnet 8.0) — `run_project` does **not**
recompile and will otherwise launch a stale binary. Then `run_project` (projectPath
`C:\Users\magnu\Embervale`) launches the sandbox, `get_debug_output` captures the
log/errors, `stop_project` stops it. Verify pure logic with
`dotnet test tests/Embervale.Tests`. Close the game (`stop_project`) when finished.

**Headless content check (no gameplay):** run the full content validator and exit —

```
godot --headless --path . -- --validate
```

The `--` forwards `--validate` as a user argument; `GameBootstrap` detects it
(`HeadlessValidation`), loads every database, runs `ContentValidator.RunAll()` (cross-
references + well-formedness + graph reachability), prints the report, and exits **0** on
pass / **1** on any issue. This is the one-command content gate for the maintainer (and
later CI). The same battery is also reachable in-game via the `validate-all` dev console
command (`F1`).

**Sandbox controls:** `WASD` move · mouse look · `Shift` sprint · `Space` jump ·
`LMB` attack · `RMB` block · `E` interact · `I` inventory · `H` heal dummy ·
`R` respawn dummy · `F5`/`F9` quick save/load · `Esc` pause (frees the cursor).
Goblins roam to the north (−Z) and drop loot.

---

## 4. Repository layout

```
.
├── project.godot            # Engine config + autoload registration + window/render
├── Embervale.sln / .csproj  # C# solution (net8.0, Godot.NET.Sdk 4.7.0)
├── icon.svg
├── CLAUDE.md                # You are here
├── README.md                # Public overview + roadmap table
├── docs/
│   ├── ARCHITECTURE.md      # Full architecture + systems reference (see §5)
│   ├── LORE.md              # World/story bible (setting, factions, characters, plot)
│   ├── PRODUCTION_ROADMAP.md # Production plan (Alpha → Beta → Launch, Phases 22+)
│   └── SESSION_PLAYBOOK.md  # Per-session sub-phase breakdown of every roadmap phase
├── scenes/
│   └── Main.tscn            # Entry scene (root has GameBootstrap script)
├── data/                    # Resource-driven content (.tres)
│   ├── attributes/          # AttributeSet presets (player, dummy, goblin)
│   ├── weapons/             # WeaponResource presets (iron sword, goblin claw)
│   ├── items/               # ItemResource / EquippableItemResource templates
│   ├── affixes/             # AffixDefinition presets (loot prefixes/suffixes)
│   ├── loot/                # LootTable presets (e.g. GoblinLoot)
│   ├── progression/         # ProgressionResource presets (XP curve + per-level gains)
│   ├── perks/               # PerkResource presets (rankable passives)
│   ├── quests/              # QuestResource presets (objectives + rewards)
│   ├── dialogue/            # DialogueResource presets (node-graph conversations)
│   ├── schedules/           # ScheduleResource presets (NPC daily routines)
│   ├── spells/             # SpellResource presets (firebolt, fireball, …)
│   ├── status_effects/     # StatusEffectResource presets (burning, chill, ward)
│   ├── weather/            # WeatherResource presets (clear, rain, storm, fog, …)
│   ├── encounters/         # EncounterResource presets (patrols, warbands)
│   ├── recipes/            # CraftingRecipeResource presets (ingot, sword, potion, …)
│   ├── factions/           # FactionResource presets (goblins, villagers)
│   ├── world_events/       # WorldEventResource presets (raid, cache, champion hunt)
│   └── regions/            # RegionResource presets (the Ember Crown sandbox region)
└── src/
    ├── Core/
    │   ├── Events/          # IGameEvent, EventBus (autoload), CoreEvents
    │   ├── Services/        # ServiceLocator (autoload)
    │   ├── Pooling/         # NodePool<T> generic object-reuse pool
    │   ├── Diagnostics/     # Log (static facade over GD.Print)
    │   ├── GameManager.cs   # Top-level GameState machine (autoload)
    │   ├── GameState.cs     # enum Boot/MainMenu/Loading/Playing/Paused/GameOver
    │   └── GameInput.cs      # Input actions defined in code
    ├── Entities/            # IEntity, Entity, CharacterEntity, EntityComponent, EntityNode
    ├── Stats/               # StatType, Stat, StatModifier, AttributeSet, StatsComponent
    ├── Movement/            # LocomotionComponent (reusable kinematic motor)
    ├── Combat/              # Damage pipeline, hitbox/hurtbox, weapons, CombatComponent
    ├── Items/               # ItemResource, ItemInstance, affixes, inventory, equipment, pickups
    ├── Loot/                # LootTable/LootEntry, LootGenerator, LootRarity, LootComponent
    ├── Progression/         # XP/levels (ProgressionComponent), perks, ExperienceComponent
    ├── Quests/              # QuestResource/objectives, QuestLogComponent, quest givers
    ├── Dialogue/            # Dialogue graph resources, session runner, story flags
    ├── World/               # WorldClock, day/night sky, weather, encounters, world events
    ├── Npc/                 # NPC schedule resources, ScheduleComponent (routines)
    ├── Magic/               # Spells, projectiles, AoE bursts, status effects
    ├── Crafting/            # Recipes, stations, CraftingComponent
    ├── Factions/            # Faction resources, ReputationComponent, FactionComponent
    ├── Interaction/         # InteractableComponent (raycast interact)
    ├── Player/              # PlayerCharacter, PlayerController, PlayerFactory
    ├── Enemies/             # EnemyEntity, EnemyAIComponent, EnemyFactory, EnemySpawnDirector
    ├── Save/                # ISaveable, SaveManager (autoload)
    ├── Debugging/           # DevConsole, ProfilerOverlay, Invariant, WorldIntegrityChecker, ReproHarness
    ├── UI/                  # DebugHud
    └── Bootstrap/           # GameBootstrap (assembles the sandbox)
```

**Conventions for new files:** namespace mirrors folder
(`Embervale.<Folder>[.<Sub>]`); one primary type per file; file name == type name.

---

## 5. Architecture & systems

The architecture (autoload spine, EventBus, entity/component model, stats,
persistence) and the full **systems reference** — combat, AI, items/loot,
progression, quests, dialogue, magic, world, crafting, factions, events, save,
UI, debugging — together with the **collision layers & teams** and the
**content/data pipeline** now live in
**[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)**. Read the relevant section
there before touching a system; the recipes in §8 below are its actionable
companion (how to add content), and the gotchas in §7 are the traps to avoid.

Quick map (folder → what lives there; see `docs/ARCHITECTURE.md` for detail):

| Folder | System |
| ------ | ------ |
| `src/Core` | Autoloads (`EventBus`, `ServiceLocator`, `GameManager`, `SaveManager`), pooling, diagnostics, input |
| `src/Entities` | `IEntity` / `Entity` / `CharacterEntity` / `EntityComponent` composition model |
| `src/Stats` | `StatType` / `Stat` / `StatModifier` / `AttributeSet` / `StatsComponent` |
| `src/Combat` `src/Movement` | Damage pipeline, hit/hurtboxes, weapons, `CombatComponent`; reusable locomotion |
| `src/Player` `src/Enemies` | Third-person controller; perception-FSM AI + `EnemyTemplateRegistry` |
| `src/Items` `src/Loot` | Inventory, equipment, item instances, affixes, loot tables |
| `src/Progression` `src/Quests` `src/Dialogue` | XP/perks, quests, conversation graphs + story flags |
| `src/Magic` `src/World` `src/Npc` | Spells/status effects; clock/weather/encounters/events; schedules |
| `src/Crafting` `src/Factions` | Recipes/stations; reputation/faction tags |
| `src/Save` | `ISaveable`, `SaveManager`, `PersistentId`, `PersistentSpawnDirector` |
| `src/UI` `src/Debugging` | `GameHud`/panels/`UiTheme`; dev console, profiler, integrity + content validators |

---

## 6. Coding conventions

- **Namespaces mirror folders**; one primary type per file; file name == type.
- **Nullable reference types are ON.** After a guard, capture a local
  (`IEntity owner = Entity!;`) or use `!`. Autoload singletons use
  `public static T Instance { get; private set; } = null!;` and guard duplicates
  in `_EnterTree`.
- **Components** end in `Component`; **events** are past-tense and end in `Event`;
  **resources** end in `Resource`/`Set`.
- **Use `Log`** (not `GD.Print`) for diagnostics.
- **No hard-coded player-facing strings** (Phase 24G). Every UI/dialogue string the
  player can read goes through `Loc.T("key")` (`src/Localization/Loc.cs`) with a key
  authored in `data/locale/strings.csv` — never a string literal in a `Label`/`Button`/
  toast. Diagnostics via `Log` and dev-console/debug text are exempt.
- **React to events** rather than polling singletons where practical.
- **Factories build detached, then add to tree.** Set component properties
  before `AddChild` where they're needed in `OnInitialize`; properties only used
  later (e.g. camera refs) can be set before the *host* enters the main tree.
- **`[GlobalClass]`** on Godot types you want creatable in the editor / usable in
  `.tres` (`Entity`, `CharacterEntity`, components, resources).
- Editorconfig: 4-space indent, `csharp_new_line_before_open_brace = all`
  (Allman braces), `using`s system-first.

---

## 7. Gotchas (read before debugging)

- **Never override `_Ready` in an `EntityComponent`** — it resolves the owner.
  Use `OnInitialize`/`OnTeardown`.
- **Lifecycle order:** identity is set in `_EnterTree` (top-down); components
  initialize in `_Ready` (bottom-up). Don't rely on a sibling component's
  `OnInitialize` having run — only on the host existing.
- **Autoload order** is fixed in `project.godot`; `EventBus`/`ServiceLocator`
  come before `GameManager`/`SaveManager`.
- **Pause deadlock:** when `GameState.Paused`, the tree is paused and normal
  nodes stop processing/inputting. The bootstrap and `GameManager` use
  `ProcessMode.Always` so pause can be toggled back. EventBus handlers run
  synchronously regardless of pause (plain C# calls), which is how the player
  re-captures the mouse on resume.
- **`Area3D` overlap timing:** enabling `Monitoring` updates overlaps on the next
  physics step. `Hitbox` polls each physics frame across its active window
  instead of trusting `area_entered` timing.
- **Dummy vs player origin:** the dummy is spawned at its capsule centre
  (`y=1`, shapes centred at local origin); the player/enemy origins are at the
  feet (shapes offset to `y = height/2`). Match shapes to mesh accordingly.
- **`GD.Load<T>` can return null** — always fall back.
- **`ServiceLocator` holds one instance per type.** The player is registered as
  `PlayerCharacter`; the dummy as `Entity`; enemies are **not** registered.
- Prefer running via the Godot MCP (`run_project` + `get_debug_output`, §2) to verify;
  when you don't run it, there's no substitute for careful Godot 4.7 C# API use.

---

## 8. Recipes (how to add things)

**A new component**
1. Create `src/<Area>/XxxComponent.cs` extending `EntityComponent`
   (`[GlobalClass]` if editor-creatable).
2. Resolve siblings/stats in `OnInitialize` via `Entity!.GetComponent<T>()`.
   Subscribe to events here; unsubscribe in `OnTeardown`.
3. Add it as a child of the actor in the relevant factory (or scene).

**A new actor / enemy type**
1. (Optional) marker subclass of `CharacterEntity` for type-level identity.
2. Add an `AttributeSet` `.tres` for its stats and (if it fights) a
   `WeaponResource` `.tres`.
3. Write a factory (mirror `EnemyFactory`) wiring: collision, mesh, `StatsComponent`,
   `CombatComponent` (set `Team`), `LocomotionComponent`, `Hurtbox`,
   `Hitbox` + `MeleeWeaponComponent`, and a behaviour component.

**A new weapon**
1. Author `data/weapons/Xxx.tres` (`script_class="WeaponResource"`).
2. Point a `MeleeWeaponComponent.Weapon` at it (factory or future equipment).

**A new item**
1. Author `data/items/Xxx.tres` (`script_class="ItemResource"`) with a unique
   `Id` (e.g. `item.material.silver`).
2. It is auto-indexed by `ItemDatabase` on startup. Reference it anywhere via
   `ItemDatabase.Get("item....")` — pickups (`ItemPickupFactory.Create`), loot
   drops, shops, recipes.
3. New interactable kinds: subclass `InteractableComponent` (override `Prompt`
   and `Interact`) and add a collider so the player's raycast can hit it.

**A new piece of equipment**
1. Author `data/items/Xxx.tres` (`script_class="EquippableItemResource"`,
   `MaxStack = 1`): set `Slot`, the `Bonus*` fields, and (for weapons) a `Weapon`
   `ext_resource` pointing at a `WeaponResource`.
2. It's indexed by `ItemDatabase` like any item; equip it via the character screen.
   Bonuses apply automatically through `EquipmentComponent` → `StatsComponent`.

**A new loot affix**
1. Author `data/affixes/Xxx.tres` (`script_class="AffixDefinition"`): unique `Id`,
   a `Label` fragment, `Kind` (0 Prefix / 1 Suffix), target `Stat`, `MinValue`/
   `MaxValue`, `MinRarity`, `Weight`, and the `For{Weapons,Armor,Accessories}` flags.
2. Auto-indexed by `AffixDatabase`; it enters the eligible pool for any equippable
   whose gear family + rolled rarity match. No code change.

**A new loot table / dropper**
1. Author `data/loot/Xxx.tres` (`script_class="LootTable"`) with `LootEntry`
   sub-resources (item id, `DropChance`, `Min/MaxQuantity`, `RollAffixes`), plus
   optional gold (`GoldChance`/`GoldMin`/`GoldMax`) and `QualityBonus`.
2. Add a `LootComponent` to the actor (set `Table` or `TablePath`); it rolls and
   spawns pickups on death. See `EnemyFactory` for the wiring.

**A new perk**
1. Author `data/perks/Xxx.tres` (`script_class="PerkResource"`): unique `Id`,
   `DisplayName`, `Description`, `MaxRank`, `Cost`, target `Stat`, `ModifierType`
   and `ValuePerRank`.
2. Auto-indexed by `PerkDatabase`; it appears in the character screen's PERKS list
   and is learnable once the player has skill points. No code change.

**A new XP-bearing enemy (or tuning the curve)**
1. Add an `ExperienceComponent { XpValue = N }` to the actor's factory (see
   `EnemyFactory`) to grant XP on death.
2. Tune levelling by editing `data/progression/PlayerProgression.tres` (or author a
   new `ProgressionResource` and point a `ProgressionComponent.CurvePath`/`Curve` at
   it).

**A new quest**
1. Author `data/quests/Xxx.tres` (`script_class="QuestResource"`) with a unique `Id`,
   `Title`/`Summary`, `Objectives` (an array of `ObjectiveResource` sub-resources:
   `Type` 0=Kill / 1=Collect, `TargetId` = entity `TemplateId` or item id,
   `RequiredCount`), and rewards (`XpReward`, `GoldReward`, `RewardItems` of
   `QuestItemReward`). Optional `PrerequisiteQuestId` chains it after another.
2. Auto-indexed by `QuestDatabase`. Start it via a `QuestGiverComponent` (set its
   `QuestId`) on a world `Entity`, in a `DialogueChoice` (`Effect` StartQuest), or
   directly with `player.GetComponent<QuestLogComponent>().StartQuest(...)`. Objectives
   advance and rewards apply automatically. No code change for new Kill/Collect quests.

**A new conversation**
1. Author `data/dialogue/Xxx.tres` (`script_class="DialogueResource"`): unique `Id`,
   `SpeakerName`, `StartNodeId`, and `Nodes` — an array of `DialogueNode` sub-resources
   (`Id`, optional `Speaker`, `Text`, `Choices`). Each `DialogueChoice` sub-resource has
   `Text`, a `Goto` node id (empty = end), an optional `Condition`+`ConditionArg` (gates
   visibility) and an optional `Effect`+`EffectArg` (`1`=StartQuest, `2`=SetFlag,
   `3`=ClearFlag). Enums export as ints (see `DialogueEnums.cs`).
2. Auto-indexed by `DialogueDatabase`. Attach a `DialogueComponent` (set its
   `DialogueId`) to a world `Entity` with a collider; the player's `E` interact opens it
   in `DialoguePanel`. No code change for new conversations.

**A new NPC routine**
1. Author `data/schedules/Xxx.tres` (`script_class="ScheduleResource"`): unique `Id` and
   `Entries` — an array of `ScheduleEntry` sub-resources (`StartHour` 0–23, `Activity`
   label, `Destination` world `Vector3`). Hours before the first block wrap to the last.
2. Auto-indexed by `ScheduleDatabase`. Add a `ScheduleComponent` (set its `ScheduleId`) to
   a static NPC `Entity`; it walks the routine off the `WorldClock` and reacts to alerts /
   dialogue. No code change for new routines.

**A new weather state**
1. Author `data/weather/Xxx.tres` (`script_class="WeatherResource"`): unique `Id`, `Type`,
   `SelectionWeight`, `MinHours`/`MaxHours`, and the atmosphere fields (`LightEnergyScale`,
   `SkyEnergyScale`, `FogDensity`/`FogColor`, `Precipitation`).
2. Auto-indexed by `WeatherDatabase`; the `WeatherDirector` can roll it and the
   `SkyController` renders it (light/fog/rain). No code change.

**A new region** (Phase 25)
1. Author `data/regions/Xxx.tres` (`script_class="RegionResource"`): unique `Id` (`region.*`),
   `DisplayName`, `Realm` (the `Realm` enum int), `SubCells` (`Array[String]` of cell scene ids),
   `Bounds` (`AABB`), `DefaultWeatherId` + `DayPhaseBias`, and `Neighbours` (`Array[String]` of
   region ids). Place its sub-cell scenes under `scenes/regions/<region>/<cell>.tscn` (see
   `docs/ARCHITECTURE.md` §2.6h-2).
2. Auto-indexed by `RegionDatabase`; the save header resolves the active region's name, and the
   25B `RegionStreamer` will stream its `SubCells`. Cross-refs (neighbours, default weather) are
   checked by the `ContentValidator`. No code change for a new region.

**A new encounter**
1. Author `data/encounters/Xxx.tres` (`script_class="EncounterResource"`): unique `Id`,
   `EnemyTemplateId`, `MinCount`/`MaxCount`, `SelectionWeight`, and the `At{Dawn,Day,Dusk,
   Night}` allow flags.
2. Auto-indexed by `EncounterDatabase`; the `EncounterDirector` spawns it around the player
   when its day phase is active. (Spawning currently routes through `EnemyFactory`, i.e. the
   goblin archetype, until more enemy factories exist.) No code change.

**A new world event**
1. Author `data/world_events/Xxx.tres` (`script_class="WorldEventResource"`): unique `Id`,
   `Kind` (`0`=Raid / `1`=Cache / `2`=Hunt), `SelectionWeight`, `CooldownSeconds`,
   `TimeLimitSeconds`, the `At{Dawn,Day,Dusk,Night}` flags, spawn knobs (enemy `MinCount`/
   `MaxCount` + `HealthMultiplier`, or `CacheItemId`/`CacheQuantity`), and rewards
   (`XpReward`, `GoldReward`, `RewardItemId`/`RewardItemQuantity`, `FactionRewardId`/
   `FactionRewardAmount`).
2. Auto-indexed by `WorldEventDatabase`; the `WorldEventDirector` rolls and runs it (announce →
   track → reward). New Raid/Cache/Hunt events need no code; a genuinely new behaviour is a new
   `WorldEventKind` + a branch in the director's start/track switch.

**A new crafting recipe**
1. Author `data/recipes/Xxx.tres` (`script_class="CraftingRecipeResource"`): unique `Id`,
   `Station` (`0`=Hand / `1`=Forge / `2`=Workbench / `3`=Alchemy / `4`=Cooking), an
   `Ingredients` array of `RecipeIngredient` sub-resources (`ItemId` + `Quantity`, same
   sub-resource `.tres` pattern as `LootEntry`), `OutputItemId`/`OutputQuantity`, and
   `OutputRarity` (`0`=Common plain; higher rolls affixes for an equippable output).
2. Auto-indexed by `RecipeDatabase`. The player learns it by id (seed via
   `CraftingComponent.StartingRecipeIds` in `PlayerFactory`, or call `Learn`); it then appears
   at a matching `CraftingStationComponent`. New stations: `CraftingStationFactory.Create(...)`
   in the bootstrap. No code change for new recipes.

**A new spell**
1. Author `data/spells/Xxx.tres` (`script_class="SpellResource"`): unique `Id`, `School`
   (a `DamageType`), `Delivery` (`0`=Projectile / `1`=Area / `2`=Self), `ManaCost`,
   `Cooldown`, `BaseDamage`, `Healing` (Self), an optional `StatusEffectId`, and the
   delivery knobs (`Range`/`ProjectileSpeed` for projectiles, `ImpactRadius` for an AoE
   burst — a Projectile with `ImpactRadius > 0` detonates as an area on impact).
2. Auto-indexed by `SpellDatabase`. Add the id to a `SpellcastingComponent.KnownSpellIds`
   (the player's is set in `PlayerFactory`); cast with `Q`, cycle with `F`. No code change.

**A new status effect**
1. Author `data/status_effects/Xxx.tres` (`script_class="StatusEffectResource"`): unique
   `Id`, `School`, `Duration`, optional DoT (`DamagePerTick`/`TickInterval`) and one stat
   modifier (`ModStat`/`ModType`/`ModValue`, e.g. `MoveSpeed` PercentMult `-0.5` = a slow),
   and `IsBeneficial` for buffs.
2. Auto-indexed by `StatusEffectDatabase`. Reference it from a spell's `StatusEffectId`; it
   applies to whoever the spell hits (or the caster, for a Self cast) via the target's
   `StatusEffectsComponent`. No code change.

**A new faction**
1. Author `data/factions/Xxx.tres` (`script_class="FactionResource"`): unique `Id`,
   `DefaultReputation`, `HostileThreshold` (a `ReputationTier` int, `2`=Unfriendly),
   `KillReputationPenalty`, and `Enemies`/`Allies` (`Array[String]([...])` of faction ids).
2. Auto-indexed by `FactionDatabase`; the player's `ReputationComponent` seeds a standing for
   it automatically. Tag actors with a `FactionComponent { FactionId = "..." }` (see
   `EnemyFactory` / the elder in the bootstrap) — enemy AI then keys aggression off the
   player's standing with that faction. No code change.

**A new stat**
1. Add to the `StatType` enum; if it's a depleting resource, update
   `StatTypes.IsResource`.
2. Add an exported field + mapping in `AttributeSet` (`ToBaseValues`).
3. Use via `StatsComponent.GetValue(StatType.Xxx)`.

**A new event**
1. Add a `readonly record struct XxxEvent(...) : IGameEvent` in the relevant
   `*Events.cs`.
2. `Publish` it where it happens; `Subscribe`/`Unsubscribe` where reacted to.

**A new persistent system**
1. Implement `ISaveable` (stable `SaveId`, `Save`/`Load` with a Godot
   `Dictionary`).
2. `SaveManager.Instance.Register(this)` in `OnInitialize`, `Unregister` in
   `OnTeardown`.

**A new input action**
1. Add a constant + `Bind(...)` in `GameInput`.
2. Read it via `Godot.Input.IsActionPressed/JustPressed/GetVector`.

**A new dev-console command**
1. In `DevCommands.RegisterAll`, `console.Register(new ConsoleCommand(name, usage, summary,
   (console, args) => ...))`. Resolve the player / a world director via the `ServiceLocator`
   (register the director there if it isn't yet), parse `args`, and return a result line.
2. It appears in `help` automatically; reach it in-game with `F1`. For determinism, add a
   scenario to `ReproHarness` (seed + the command sequence) and run it with `repro <name>`.

**Pooling a high-churn node** (perf)
1. Hold a `NodePool<T>` (`src/Core/Pooling`) on the owner; build it in `OnInitialize`
   (`new NodePool<T>(factory, prewarm)`) and `Clear()` it in `OnTeardown`.
2. Make the node reusable: build its children once in `_Ready`, expose a `Launch/Configure`
   to re-arm per use, and on "death" invoke a release callback (the pool's `Return`) instead
   of `QueueFree`. To spawn: `pool.Get()` → `AddChild` → position → `Launch(...)`. See
   `SpellProjectile` + `SpellcastingComponent`. (Throttle/sleep expensive per-frame work by
   distance to the player the way `EnemyAIComponent` does — perception cache + far-sleep.)

**A new UI panel / HUD widget**
1. Build it through `UiTheme` (`src/UI/UiTheme.cs`): `UiTheme.Panel()` for the frame,
   `UiTheme.Padding()` inside it, then `UiTheme.Header`/`Body`/`Action`/`Bar` for content —
   don't hand-roll styleboxes/fonts. A modal panel sets `UiState.MenuOpen` + frees the mouse;
   a non-modal overlay (like the journal) does not.
2. Rebuild from a dirty flag in `_Process` (never during a button signal). Add new palette
   colours/builders to `UiTheme` rather than per-panel so the look stays consistent (and the
   Phase 18 overhaul stays a one-file change).

---

## 9. Development workflow

- **Branch:** develop on a per-phase branch (e.g. `claude/phase-23d-…`) off `main`.
  **`main` is the trunk.** Never push directly to `main`; always go through a PR.
- **Per phase:** implement → keep buildable/playable → update `README.md` +
  `docs/PRODUCTION_ROADMAP.md` (mark phase done, queue next) → commit → push →
  open a PR into `main` and **merge it immediately** (`gh pr merge --merge --admin`).
  The maintainer wants each push landed on `main`, **not** parked in a draft PR for
  review — do not leave PRs open as drafts. (The PR still exists for history; it's
  just merged right away.)
- **After a merge:** the head branch may be auto-deleted; locally
  `git fetch origin main && git reset --hard origin/main` to resync, then carry on.
- **Commits:** clear, descriptive messages. Co-author/session trailers are added
  per harness configuration. Do **not** put model identifiers in commits/PRs.
- **No CI to satisfy.** The Vercel check is a no-op (see §2).

---

## 10. Roadmap status

> **Scope:** Phases 1–21 built *systems/infrastructure*, not the game's content.
> They yielded a data-driven sandbox that can express the game, not a finished
> game — the world, narrative, art, audio, balance and ship polish are the
> **production roadmap** (Phases 22+) that carries Embervale from that sandbox
> through Alpha → Beta → Launch. See `docs/PRODUCTION_ROADMAP.md`.

Done: **1 Core Architecture · 2 Player Controller · 3 Combat Framework ·
4 Enemy AI · 5 Inventory System · 6 Equipment System · 7 Loot Generation ·
8 Progression · 9 Quests · 10 Dialogue · 11 NPC Schedules · 12 Magic ·
13 World Systems · 14 HUD & Panels Polish · 15 Crafting · 16 Factions ·
17 Procedural Events · 18 Game UI Overhaul · 19 Optimization ·
20 Deep Debugging**. Next (ongoing): **21 Content Expansion** — the seam where
the systems roadmap hands off to the separate content/production roadmap.

> **Two UI phases, both done:** Phase 14 *polished the debug-grade overlay* (shared
> `UiTheme`, vitals bars, crosshair, framed panels). Phase 18 built the *real game UI*
> on top of it — `GameHud` (anchored widgets, nameplate, interaction prompt), a
> `PauseMenu`, a `Notifications`/`Toast` feed, item tooltips — and demoted the old
> `DebugHud` to an F3 developer overlay. The *meta/shell* (title screen, settings,
> save-slot flow) remains the separate content/production roadmap.

See `docs/PRODUCTION_ROADMAP.md` for the production plan (Phases 22+) that takes
the finished systems sandbox to launch, gated First Playable → Vertical Slice →
Alpha → Beta → Release Candidate → Launch.

---

## 11. Glossary

- **Actor / entity** — any in-world object implementing `IEntity`.
- **Component** — an `EntityComponent` child providing one slice of behaviour/data.
- **Resource** — a Godot `Resource` (`.tres`) holding authored data/content.
- **Hurtbox / Hitbox** — `Area3D`s that receive / deal damage.
- **Packet** — a `DamagePacket`, a self-contained description of one hit.
- **Team** — faction id on `CombatComponent` controlling friendly fire.
- **Autoload** — a Godot global singleton node declared in `project.godot`.
