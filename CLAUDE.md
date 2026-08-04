# CLAUDE.md — Embervale

Authoritative guide for working in this repository. Read this first. It explains
what the project is, how it is built, the conventions, the gotchas that will bite
you, and step-by-step recipes for adding new content without breaking things. The
**architecture and the full systems reference live in
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)** (see §5) — read the relevant
section there before changing a system.

> **One-line summary:** Embervale is an original hybrid first/third-person
> (swappable at any time), open-world fantasy
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
- **3D models: search the web first — always.** Every model request starts with a
  thorough search of reputable open-source repositories (Poly Pizza, Kenney,
  Quaternius, OpenGameArt, Sketchfab, Khronos glTF samples, …), then a licence and
  fit evaluation, then **adaptation via the Blender MCP** if the asset is close but
  not perfect. Building from scratch is the **rare exception** and requires that a
  real search found nothing, adapting is impractical, *and* combining assets cannot
  solve it. **Never reverse that order**, and never assume a model does not exist —
  "I couldn't think of one" is not a search. Every asset needs a verified licence
  and a `assets/CREDITS.md` entry before it is done. **Full policy:
  [`docs/ASSET_POLICY.md`](docs/ASSET_POLICY.md)** — it is mandatory and it
  supersedes any older build-from-scratch guidance in this repo.
- **Code, plugins and tools: check the Godot Asset Library before reinventing.**
  Distinct from the art rule above. Fetch from the asset's linked GitHub repo (the
  connected Godot MCP has no one-click install) and adapt it to our architecture.
  Reuse only when it fits our needs *exactly* and its licence is compatible (this
  build is **private/personal — never sold or published —** so prefer MIT/CC0/open;
  avoid paid or closed). For *code*, a near-miss you have to fight is still worse
  than building clean. Note what you pulled and its licence where it lands.
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

**The Blender MCP is an adaptation tool, not an asset source.** Its job is adapting downloads,
changing proportions, simplifying meshes, combining assets, repairing geometry, improving UVs,
adjusting materials, building LODs and optimizing for gameplay. Reach for it to *modify* what a
web search found; authoring an original model is the exception the §1 rule gates
([`docs/ASSET_POLICY.md`](docs/ASSET_POLICY.md)). Through Phase 30 every model here was built
from scratch — that is history, not the current default.

**Blender MCP scene hygiene (maintainer rule, 2026-07-02):** when authoring models via the
Blender MCP, **never leave multiple models stacked at the world origin** (each "centered
within itself"). Lay assets out side by side with clear spacing (e.g. 2–3 m apart along +X)
so the maintainer can see at a glance what is being made in the Blender viewport; only zero
an object's location transiently at export time (glTF export needs origin-relative
placement), and move it back or lay out the next asset offset afterwards.

⚠️ **`run_project` does NOT recompile C#.** It launches whatever `Embervale.dll` was
last built, so after editing any `.cs` you MUST rebuild first or the MCP runs a **stale
binary** (a silent trap — a behaviour-preserving change looks "verified" while your edit
never ran). The shell here **has `dotnet` 8.0**: rebuild with
`dotnet build Embervale.sln` (output goes to `.godot/mono/temp/bin/Debug/Embervale.dll`,
where the game loads it), *then* `run_project`. Run the pure-logic unit suite with
`dotnet test tests/Embervale.Tests`.

Other caveats: `run_project` launches the **real game window** — use it deliberately and
`stop_project` when done; it is not a headless check. It also lands on the **main menu**, not
in the world (Phase 24's meta-shell), and the menu's buttons need input the MCP can't inject —
so a bare `run_project` verifies boot and database loading, nothing in-world. Use `--play`
(§3) when you need an actual session. The `WorldIntegrityChecker` (5s) stays silent unless an
invariant breaks, so give a run several seconds before trusting a clean log. When you have
**not** built+run something, say it was *reviewed against the Godot 4.7 C# API* — reserve
"verified/tested running" for output you actually captured.

⚠️ **`godot` is not on `PATH`.** The `godot …` invocations below are shorthand; the binary is

```
C:\Users\magnu\Downloads\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe
```

Use the `_console.exe` variant from a shell — the plain `.exe` detaches and prints nothing to
stdout, so you lose the log you ran it for.

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
3. Press Play. `scenes/Main.tscn` boots to the main menu; *New Game* / *Continue* enters the
   sandbox world.

**For you (Claude), via the Godot MCP** (see §2): after any `.cs` change, first
`dotnet build Embervale.sln` (the shell has dotnet 8.0) — `run_project` does **not**
recompile and will otherwise launch a stale binary. Then `run_project` (projectPath
`C:\Users\magnu\Embervale`) launches the game **on the main menu**, `get_debug_output` captures
the log/errors, `stop_project` stops it. To reach the world instead, launch with `--play` (below)
from a shell. Verify pure logic with `dotnet test tests/Embervale.Tests`. Close the game
(`stop_project`) when finished.

**Headless content check (no gameplay):** run the full content validator and exit —

```
godot --headless --path . -- --validate
```

The `--` forwards `--validate` as a user argument; `GameBootstrap` detects it
(`HeadlessValidation`), loads every database, runs `ContentValidator.RunAll()` (cross-
references + well-formedness + graph reachability), prints the report, and exits **0** on
pass / **1** on any issue.

**Launch straight into gameplay (dev):** `godot --path . -- --play` boots past the menu into
the most recent save, so systems that only init on world build (the audio directors, spawners)
can be launched deterministically — useful for capturing runtime logs without driving the menu
(the menu's *Continue* needs input the MCP can't inject). It continues the newest save slot; with
no saves it stays on the menu. This is the one-command content gate for the maintainer (and
later CI). The same battery is also reachable in-game via the `validate-all` dev console
command (`F1`).

**What `--play` still can't verify:** the **`F1` dev console needs keyboard input**, and there is
no CLI equivalent — so no `spawn`/`time`/`rep` from a remote session. `--play` also resumes where
the save left off, which for the Ember Crown is usually the town hub *inside* the region's 34 m
`SafeZoneRadius`, where the `EncounterDirector` deliberately won't spawn. A quiet log after a
`--play` run therefore proves boot, database loading and save restore — **not** that new enemies
spawn or fight. Say which of the two you got; don't let one stand in for the other.

**Sandbox controls:** `WASD` move · mouse look · `Shift` sprint · `Space` jump ·
`LMB` attack · `RMB` block · `E` interact · `V` swap first/third person ·
`I` inventory · `B` bestiary · `C` party order ·
`H` heal dummy · `R` respawn dummy · `F5`/`F9` quick save/load · `Esc` pause (frees the cursor).
Hotbar is `1`–`5`. Gamepad plays the whole game (sticks move/look, RT/LT attack/guard, A/B jump/dodge).
**Any blocking menu pauses the scene tree**; a cinematic lock (boss intro, prologue) does not —
see `UiState.Open(owner, pausesWorld:)`.
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
│   ├── SESSION_PLAYBOOK.md  # Per-session sub-phase breakdown of every roadmap phase
│   └── VERTICAL_SLICE_PLAN.md # Phase 33D/E build plan (slice arc, capture pass, gaps)
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
│   ├── regions/            # RegionResource presets (Ember Crown, Frostfang Reach)
│   ├── races/              # RaceResource presets (Human, Draekyn, Grondar, Sylthari, Umbral, Valari)
│   ├── companions/         # CompanionResource presets (Kael) — Phase 32
│   ├── bosses/            # BossResource presets (boss.*) — phases/abilities/enrage, Phase 36A
│   ├── properties/        # PropertyResource presets (property.*) — claimable holdings, Phase 37A
│   ├── ai_profiles/        # AIProfileResource presets (ai.*) — enemy personalities, Phase 34A
│   ├── enemies/            # EnemyArchetypeResource presets (enemy.*) — the roster, Phase 34B–34F
│   ├── bestiary/           # BestiaryEntryResource presets — creature lore/reveal, Phase 34G
│   ├── _templates/         # blank authoring templates to copy from
│   └── locale/             # strings.csv — the Loc localization catalogue
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
    ├── World/               # WorldClock, sky/weather, encounters, world events, regions/streaming, fast travel, the Weave
    ├── Npc/                 # NPC schedule resources, ScheduleComponent (routines)
    ├── Magic/               # Spells, cast archetypes, school identities/mastery/combos, the Weave, status effects
    ├── Crafting/            # Recipes, stations, CraftingComponent
    ├── Factions/            # Faction resources, ReputationComponent, FactionComponent
    ├── Corruption/          # CorruptionComponent, tiers, appearance + dialogue hooks, endings
    ├── Races/               # RaceResource, RaceComponent, character creation (Phase 26)
    ├── Companions/          # Party roster, follower AI, formation/leash cores (Phase 32)
    ├── Onboarding/          # TutorialDirector + script (diegetic hints, Phase 33)
    ├── Housing/             # PropertyResource/Database, HousingService, deed component (Phase 37)
    ├── Interaction/         # InteractableComponent (raycast interact)
    ├── Player/              # PlayerCharacter, PlayerController, PlayerFactory
    ├── Enemies/             # EnemyEntity, EnemyAIComponent, AIProfile/EnemyArchetype/Bestiary resources+databases, EnemyArchetypeFactory (+2 bespoke), AshenAffliction, EnemyTemplateRegistry
    ├── Save/                # ISaveable, SaveManager (autoload), persistence directors
    ├── Localization/        # Loc string layer (Loc.T)
    ├── Analytics/           # AnalyticsSink (EventBus → user://analytics, dev-gated)
    ├── Debugging/           # DevConsole, ProfilerOverlay, Invariant, WorldIntegrityChecker, ReproHarness
    ├── UI/                  # GameHud, PauseMenu, panels/screens, UiTheme; DebugHud (F3 dev overlay)
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
| `src/Combat` `src/Movement` | Damage pipeline (armour **and** per-school resistances on one curve), hit/hurtboxes, weapons, `CombatComponent`; reusable locomotion |
| `src/Player` `src/Enemies` | Hybrid FP/TP controller + camera rig (`CameraRigMath`); one profile-driven AI brain, the data roster (`ai.*`/`enemy.*`/bestiary) behind `EnemyTemplateRegistry`, and the Ashen variant layer |
| `src/Items` `src/Loot` | Inventory, equipment, item instances, affixes, loot tables |
| `src/Progression` `src/Quests` `src/Dialogue` | XP/perks, quests, conversation graphs + story flags |
| `src/Magic` `src/World` `src/Npc` | Spells/status effects; clock/weather/encounters/events; schedules |
| `src/Crafting` `src/Factions` | Recipes/stations; reputation/faction tags |
| `src/Companions` | `CompanionRoster` (party, loyalty + persistence), `CompanionAIComponent`, `CompanionResource`, formation/leash/order cores |
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
- **A stagger cancels a wind-up, not a live blow (36C).** `MeleeWeaponComponent` drops the swing
  only while `Phase.Windup`; once the hitbox opens the attack is committed. `SpellcastingComponent`
  drops an active charge/channel the same way (which is also how a breath ends, since
  `BreathComponent` stops when `IsChanneling` goes false). This applies to **every actor including
  the player** — poise is symmetric.
- **A telegraph must run off `AttackPerformedEvent.WindupSeconds`, never a constant.** That value is
  the *effective* wind-up (weapon time ÷ attack speed), so a boss phase that buffs attack speed
  shortens the cue and the danger together. `BossController` used a fixed 0.5 s and drifted.
- **A blocking menu pauses the tree; a cinematic lock does not.** `UiState.Open` defaults to
  `pausesWorld: true` and `GameManager.RefreshPause` is the only writer of `GetTree().Paused`. Don't
  scatter `UiState.MenuOpen` checks through gameplay systems to "stop things during menus" — that
  approach is exactly what failed (only 2 of ~50 ticking systems ever remembered it, so the
  inventory froze the player and nothing else). Do pass `pausesWorld: false` for anything the player
  is being held still to *watch*.
- **`ServiceLocator` drops a freed registrant on read** rather than handing it out. Several services
  register without ever unregistering; a dereferenced freed node is a hard `gchandle.is_released`
  crash, not a null check away.
- **Don't dereference injected nodes outside `PlayerController`'s not-playing guard.** The
  camera/pivot/aim nodes are being freed during a world teardown or a save/load rebuild, so
  per-frame work that touches them (the camera rig) must stay *inside* the `IsPlaying` early-out.
  Hoisting it above the guard produced an intermittent `gchandle.is_released` fatal on exit —
  2 runs in 10, and nothing in the gameplay log to point at it.
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

   **Usually you should not write a factory at all** — author a
   `data/enemies/Xxx.tres` (`script_class="EnemyArchetypeResource"`) instead and
   `EnemyArchetypeFactory` builds it, `EnemyArchetypeDatabase` registers it, and
   `spawn <id>` works with no code. A bespoke factory earns its place only by doing
   something structurally different (goblin, Ashen Acolyte). The Iron King had one until Phase
   36B and lost it: once his phases moved into `data/bosses/`, his factory was a worse copy of the
   shared one — it silently skipped the hit reaction, the weapon trail and the quest enemy group.

**A new boss fight (Phases 36A–36D)**
1. Author `data/bosses/Xxx.tres` (`script_class="BossResource"`): unique `Id` (`boss.*`) and
   `Phases` — an array of `BossPhaseResource` sub-resources, **ordered high health to low**, the
   first at `HealthFraction = 1.0`. Each phase carries its `AttackSpeedBonus`/`MoveSpeedBonus`
   (fractions, applied as `PercentMult` under a `boss.phase{n}` source), optional `GrantSpellIds`,
   an optional `AiProfileId` swap, and its `TelegraphColor`/`TelegraphEnergy` wind-up flare.
   Optionally add the enrage fuse: `EnrageSeconds` (`0` = none), `EnrageSpellIds`, the two enrage
   bonuses, and `EnrageForcesFinalPhase`. `WindupPoiseMultiplier` (36C) decides how punishable the
   phase's wind-ups are — above `1` makes the telegraph a window worth attacking into, below `1`
   hardens it. It must stay positive; `0` is a phase that can never be interrupted, which in play
   looks exactly like the interrupt being broken, so `--validate` rejects it.
   `AddWaves` (36D) is an array of `BossAddWaveResource` sub-resources — `TemplateId` (any registered
   enemy), `Count`, `RepeatSeconds` (`0` = once on entering the phase), `MaxAlive` (`0` = uncapped)
   and `HealthMultiplier`. ⚠️ **A repeating wave must set `MaxAlive`**; the validator rejects one
   without it, because an uncapped repeat ends the fight by burying the player rather than beating
   them. Adds die with the boss through the ordinary damage path, so their loot and XP still land.
   The `Encounter`/`Reward` groups (36E) carry the intro lock, the defeat slow-mo, the guaranteed
   `RewardItemId`, the `DefeatFlagId` and the `DefeatDialogueId` that offers the corruption choice.
   ⚠️ **A reward or a defeat conversation requires a `DefeatFlagId`** — without one nothing records
   that it already happened, so it pays out on every death. That is not hypothetical: it is the
   shape of the bug 36E fixed, and `--validate` now rejects it. Leave `DefeatFlagId` empty on a lair
   boss; `LairSpawnComponent` already records those, and a second writer of the same fact drifts.
2. Point an archetype at it: set `IsBoss = true` **and** `BossId = "boss.xxx"` on
   `data/enemies/Xxx.tres`. `EnemyArchetypeFactory` attaches the `BossController`; there is no code
   to write. An `IsBoss` archetype with no `BossId` still gets a controller and falls back to the
   default three-stage escalation, so a boss is never left with no structure at all.
3. **An arena binds itself to the fight in its own `.tscn`, not in code** (36D). Tag `Marker3D`s
   `groups=["boss_add_spawn"]` and waves arrive there — found by group, so renaming or re-parenting a
   marker cannot silently unbind it, and scoped to markers under the boss's own parent, so two loaded
   arenas cannot lend each other spawns. With no markers the adds fall back to a ring around the
   boss, which is what a lair gets. Add an `ArenaHookComponent` (`ActivateAtPhase` + `Reveals`
   node paths) to have the arena itself reveal things as the fight escalates; it resets on the boss's
   death, because `BossSummonComponent` deliberately re-arms until the defeat is persisted.
   See `scenes/regions/ember_crown/arena.tscn` for both.
4. ⚠️ **The enrage clock starts on the first damage traded with the boss**, not on
   `BossEncounterStartedEvent` — only `BossSummonComponent` publishes that (the Iron King's path),
   so keying off it would leave every lair boss with a fuse that never lit.
5. ⚠️ **Mark any spell a phase grants `PlayerLearnable = false`.** The grant goes through the same
   path a dialogue reward uses, which ignores that flag — but the player's spellbook lists every
   spell in the database, so a monster ability would otherwise show up as purchasable.
6. `--validate` checks the domain **in both directions**: phases must descend from `1.0`, granted
   spells and profile ids must resolve, an archetype's `BossId` must exist, and a `BossId` may only
   sit on an `IsBoss` archetype (otherwise it is a silent no-op).

**A new claimable property (Phase 37A)**
1. Author `data/properties/Xxx.tres` (`script_class="PropertyResource"`): unique `Id`
   (`property.*`), a `NameKey` in `strings.csv`, its `RegionId`, and a `TravelNodeId` — claiming
   registers the holding as a fast-travel destination, which is what makes owning it worth anything.
2. Give it a way to be had: a `PriceGold`, a `RequiredQuestId`, or both. ⚠️ **Neither is rejected by
   `--validate`** — a property that is neither sold nor earned is claimed by the first player who
   walks into its post. A missing `TravelNodeId` is rejected too: gold spent on somewhere you cannot
   return to.
3. Place the deed: an `Entity` in a region cell with a collider (the interact raycast needs one) and
   a `PropertyDeedComponent { PropertyId = "property.xxx" }`. See `CottageDeed` in
   `scenes/regions/ember_crown/town_hub.tscn`.
4. ⚠️ **Every refusal must say which refusal it is.** The prompt reports owned / quest-locked /
   too-expensive separately, in that order — the quest gate before the price, so a player is never
   sent to earn gold for something a quest is holding shut. `PropertyClaim.Resolve` owns that order
   and both the prompt and the interaction read it, so they cannot drift apart.

**A big/boss creature with body zones (Phase 35A)**
1. Author the archetype `.tres` as above, plus:
   - `HitZones` — an array of `HitZoneResource` sub-resources (`Id`,
     `DamageMultiplier`, `Offset`, `Radius`, `Height`; height ≤ 2×radius makes it a
     sphere). Non-empty **replaces** the whole-body capsule hurtbox, and doubles as
     the greybox silhouette, so the visual can never drift from what is damageable.
     The multiplier scales poise damage too — a headshot staggers harder.
   - `IsBoss = true` → the actor is a `BossEntity`, which is what the Phase 28C
     healthbar and the 28D corruption-on-kill loop resolve by type.
   - `DirectionalMelee = true` → a `DragonMeleeComponent` swaps the one
     `MeleeWeaponComponent`'s hitbox between jaws/wing/tail by the target's bearing.
2. **Give its AI profile a `TurnSpeedDegrees`.** The AI faces its target before every
   swing, and the default (`0`) snaps instantly — a body that always looks at you can
   only ever use its frontal attack, so the flank and rear arcs are dead code without
   a turn rate. It is also the knob that makes a heavy creature *feel* heavy.
3. `ContentValidator` checks the zones (ids unique and non-empty, radius and
   multiplier positive, directional melee backed by zones).

**Making a creature fly (Phase 35B)**
1. Set `TakeoffRange > 0` on its **AI profile** (`data/ai_profiles/Xxx.tres`) plus
   `HoverAltitude`, `ClimbSpeed`, `AirborneDuration` and `GroundedDuration`. Flight
   is a property of the profile, not the archetype — `EnemyArchetypeFactory` attaches
   a `FlightComponent` when the profile can fly, and `0` (the default on all four)
   means no flight and no cost.
2. **Keep the airborne window short.** A flier with no ranged attack that hovers
   indefinitely is a fight where neither side can act. The cycle is deliberately
   `Grounded → TakingOff → Airborne → Landing → Grounded`, never open-ended.
3. Nothing else needs changing: the AI steers a flier horizontally exactly as it
   steers a walker, and `LocomotionComponent.Flying` owns the vertical axis alone.
   `ContentValidator` rejects half-authored flight tuning either way round.

**A breath weapon (Phase 35C)**
1. Author `data/spells/Xxx.tres` with `Delivery = 3` (Cone) and `CastMode = 2`
   (Channeled): `ConeAngleDegrees` is the **full** opening angle, `ImpactRadius` is
   the cone's *length*, and `PlayerLearnable = false` for a monster's breath. It is
   an ordinary spell — school resistances, `SchoolIdentity`, status effects and
   `SpellResolver` all apply with no special-casing.
2. On the archetype set `BreathSpellId` **and** add the same id to `KnownSpellIds`
   (the breath is cast through the normal spellcasting path, not around it — the
   validator rejects one without the other). `BreathDuration` is how long the
   channel is held.
3. **A caster is a profile that stands off, not an actor that holds spells.** Giving
   a melee creature spells does not turn it into a kiter — `EnemyAIComponent` branches
   on `AIProfileResource.IsStandoff` (`StandoffRange > AttackRange`) alone. Set a
   standoff range only if you actually want it to back away.

**Placing a world boss in a lair (Phase 35D)**
1. Set `TerritoryRadius` on its AI profile. Without it the AI **chases forever** —
   `_home` is otherwise read only by patrol and retreat — and a flying boss will
   follow the player into the next realm. `0` is no leash, which is every other
   profile.
2. Add a marker `Entity` to the region cell's `.tscn` with a **stable
   `PersistentId`** and a `LairSpawnComponent` (`TemplateId`, `SpawnOffset`). It
   builds the creature through `EnemyTemplateRegistry` — no new factory.
3. **Persist the spawner, never the boss.** `CellPersistenceDirector` reconciles on
   `RegionCellLoadedEvent`, published *after* the streamer adds the cell root, so a
   boss spawned in that frame races the walk and a deferred one misses it entirely —
   either way the boss resurrects every time the cell reloads. The authored spawner is
   always found, so it holds the "defeated" bit instead.
4. **The cell carries its own floor** (see 34.5A). Size it for the fight: the roost's
   floor is 90 m because the territory radius is 45. Butt it against a neighbouring
   cell's floor rather than overlapping — co-planar floors z-fight — and keep it clear
   of other cells' props and the region's safe zone.
5. **Give each lair its own `PersistentId`.** `LairSpawnComponent.SaveId` derives from
   it, so two lairs sharing one means killing either marks both defeated.
6. **Inherit `scenes/regions/roost.tscn`** (Phase 35F paid the debt the two hand-authored
   roosts flagged). The base owns the nav region + baker, the floor mesh/collider and the
   `Nest`/`Lair` markers; a roost overrides the `RoostCell` script's `FloorSize`/
   `FloorColor`/`EmberColor`/`EmberEnergy`, the `Nest`'s `PersistentId`, the `Lair`'s
   `TemplateId`, and adds its props **as children of `Nav`** (geometry outside the
   navigation region is not carved into the bake). Floor mesh, shape and material are
   base-scene sub-resources and therefore shared by every roost — `RoostCell` `Duplicate()`s
   each before touching it, and anything else you vary must do the same.
7. **Set `DefeatFlagId` if anything needs to know the boss is dead** (35F). It is the only
   thing in the game that turns a kill into a story flag, so it is what a dialogue
   condition or a gated interactable (e.g. `SpellTomeComponent.RequiredFlagId`) can ask.

**A creature that talks (Phase 35F)**
1. Set `DialogueId` on the archetype. `EnemyArchetypeFactory` attaches a
   `DialogueComponent`, and the player's interact raycast is unmasked — it resolves the
   owner from whatever collider it hits, so the body the creature already has is the
   target. No extra collision, no bespoke factory.
2. **Put it in a faction the player is not hostile to**, or it attacks before the prompt is
   ever readable. `faction.dragons` is the pattern: `DefaultReputation` in the Neutral band,
   `HostileThreshold` at Unfriendly. `EnemyAIComponent.PlayerIsTarget` does the rest, and
   the first player hit sets `_provoked` regardless — neutral-until-provoked is pure data.
3. To have it **teach a recovered spell**, use the `LearnSpell` dialogue effect (`8`) with a
   `spell.*` id. It goes through the same corruption-gated `SpellcastingComponent.Learn` a
   tome does and **ignores `PlayerLearnable`**, which is how a spell that can never be
   bought can still be given. Mark such a spell `PlayerLearnable = false`; the character
   screen lists it anyway once it is known.

⚠️ **Spawning an actor into a region cell: create at zero, add, *then* set
`GlobalPosition`.** The factories and `EnemyTemplateRegistry.Create` take a **local**
position, and a cell's root has already been moved to the cell's centre by the
streamer — so handing `Create` a world position applies the cell offset twice.
`BossSummonComponent` has always done it in the right order; the 35D lair spawner did
not, and its dragon landed on the wrong part of the map (visibly in the void once a
cell sat far from the origin). `EnemySpawnDirector` had the same latent bug.

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
   `ext_resource` pointing at a `WeaponResource`. `BonusFrostResist` (Phase 35G) is the
   only one of the 34E resistances gear can carry so far — the other five are one
   `[Export]` and one line in `StatBonuses()` each, added when an item wants them.
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
   `QuestItemReward`, and `FactionRewardId`/`FactionRewardAmount` — Phase 34.5C, the same
   pair `WorldEventResource` has; the amount may be negative). Optional
   `PrerequisiteQuestId` chains it after another. **Objectives are Kill/Collect only** —
   "go and talk to X" is not expressible, so a turn-in is a conversation the player has to
   remember to have.
   ⚠️ **A Kill objective must name something that respawns.** `--validate` requires the target to be
   spawnable by an encounter or world event, because a lair boss is killed once and stays dead — a
   quest taken afterwards can never complete and never leaves the journal (Phase 35F shipped exactly
   that). Targeting a one-shot boss needs `AllowsOneShotTarget = true` **and** an offering dialogue
   that gates on the target still being alive; see `quest.ancient.kin` + `dialogue.ancient_dragon`,
   which pair it with `LairSpawnComponent.DefeatFlagId`.
   **Story flags** (`Effect` SetFlag / `Condition` HasFlag) are the only way to mark
   *state* a quest can't: membership, a rank, a favour owed. They have no database, so
   `--validate` can only catch a flag that **nothing ever sets** — a `SetFlag` typo still
   fails silently. A choice carries **one** `Effect`, so a choice that starts a quest cannot
   also set a flag: hang the flag on the next node's farewell choice (see `Elder.tres`).
2. Auto-indexed by `QuestDatabase`. Start it via a `QuestGiverComponent` (set its
   `QuestId`) on a world `Entity`, in a `DialogueChoice` (`Effect` StartQuest), or
   directly with `player.GetComponent<QuestLogComponent>().StartQuest(...)`. Objectives
   advance and rewards apply automatically. No code change for new Kill/Collect quests.

**A new conversation**
1. Author `data/dialogue/Xxx.tres` (`script_class="DialogueResource"`): unique `Id`,
   `SpeakerName`, `StartNodeId`, and `Nodes` — an array of `DialogueNode` sub-resources
   (`Id`, optional `Speaker`, `Text`, `Choices`). Each `DialogueChoice` sub-resource has
   `Text`, a `Goto` node id (empty = end), an optional `Condition`+`ConditionArg` (gates
   visibility — incl. `QuestAvailable`, `HasFlag`, and `CorruptionAtLeast`/`CorruptionBelow`)
   and an optional `Effect`+`EffectArg` (`1`=StartQuest, `2`=SetFlag, `3`=ClearFlag,
   `4`=AddCorruption, `5`=RecruitCompanion, `6`=DismissCompanion, `7`=AddCompanionLoyalty
   (`<companionId>:<delta>`)). Companion gates come as conditions too (`CompanionRecruited`,
   `CompanionNotRecruited`, `CompanionLoyaltyAtLeast` = `<companionId>:<value>`).
   Enums export as ints (see `DialogueEnums.cs`).
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
   `DisplayName`, `Realm` (the `Realm` enum int), `SpawnPoint` (`Vector3` — where the player
   appears on entry, Phase 25C), `Bounds` (`AABB`), `DefaultWeatherId` + `DayPhaseBias`,
   `Neighbours` (`Array[String]` of region ids), and `Cells` — an array of `RegionCellResource`
   sub-resources (each: `Id` `<region>.<cell>`, `ScenePath`, `Center` `Vector3`, `LoadRadius`).
   Place each cell scene at `scenes/regions/<region>/<cell>.tscn`, built at local origin (the
   streamer positions the instance at `Center`); see `docs/ARCHITECTURE.md` §2.6h-2.
   **Navmesh (Phase 27A):** wrap the cell's walkable geometry in a `NavigationRegion3D` "Nav" with a
   `NavigationMesh` whose `geometry_parsed_geometry_type = 1` (**static colliders** — never visual
   meshes; runtime mesh parsing forces a GPU→CPU readback hitch), and add a `CellNavBaker`
   (`src/World/CellNavBaker.cs`) as its child so the navmesh **bakes at stream-in**. Give the cell a
   floor `StaticBody3D`+`CollisionShape3D` (the bake's walkable surface) and a collider on every
   obstacle (they carve the mesh). Keep `agent_*` dims on the 0.25 voxel grid (`agent_height = 1.75`,
   `agent_max_climb = 0.5`) to avoid precision warnings. Enemy `NavigationAgent3D`s path on it
   automatically; with no Nav region they fall back to straight-line steering, so a navmesh is
   optional per cell but expected for any space enemies fight in.
2. Auto-indexed by `RegionDatabase`; the save header resolves the active region's name, and the
   `RegionStreamer` loads/unloads the `Cells` by distance (hysteresis + a per-frame budget). The
   `ContentValidator` checks neighbours, default weather, and that each cell `ScenePath` resolves.
   No code change for a new region.
3. **Hard transitions (Phase 25C):** declaring a region in another's `Neighbours` makes the
   bootstrap spawn a travel portal between them automatically (a `RegionTransitionComponent` at the
   region's `SpawnPoint`). Stepping through (or `region goto <id>` in F1) publishes a
   `RegionTransitionRequestedEvent`; the bootstrap shows the `LoadingScreen`, re-targets the
   streamer (`UnloadAll` + `Configure`), teleports the player to the destination's `SpawnPoint`, and
   autosaves the boundary. Reciprocal links give a two-way door. No code change for a new transition.

**A new encounter**
1. Author `data/encounters/Xxx.tres` (`script_class="EncounterResource"`): unique `Id`,
   `EnemyTemplateId`, `MinCount`/`MaxCount`, `SelectionWeight`, the `At{Dawn,Day,Dusk,
   Night}` allow flags, `CorruptionChance` (0..1, Phase 34F — see below), and `RegionIds`
   (Phase 34.5B — `Array[String]` of `region.*` ids; **empty means anywhere**). Author
   `RegionIds` whenever the creature belongs to one realm, or it rolls in every region: that
   is how frost stalkers ended up prowling the Ember Crown for two phases. A misspelled id
   narrows the encounter to *nowhere* and `--validate` is the only thing that catches it.
2. Auto-indexed by `EncounterDatabase`; the `EncounterDirector` spawns it around the player
   when its day phase is active, resolving `EnemyTemplateId` through `EnemyTemplateRegistry`
   — so any registered archetype works, not just the goblin (Phase 34B). No code change.

**A corrupted (Ashen) variant of an existing creature** (Phase 34F)
1. **Don't author a new archetype for it.** Set `CorruptionChance` on an encounter and each enemy
   it spawns rolls to rise Ashen: `AshenAffliction.Afflict` adds named `"ashen"` stat modifiers,
   scales XP, prefixes the nameplate via `enemy.ashen_prefix`, and chars the body with the same
   ash/ember colours `CorruptionAppearanceController` uses on the player. An "Ashen Wolf" authored
   as its own `.tres` is a copy of `Wolf.tres` that drifts the moment either is tuned.
2. Corruption is a property of the **place**, not the player — LORE attributes it to Morthul and
   the realm. Author the chance per encounter; Phase 44.5's realm decay tier can drive it later.
3. Reach for a real archetype only when the creature is more than a tinted, tougher base — a
   different AI profile, spell loadout or faction (see `enemy.ash_maw`, `enemy.cinder_thrall`).
4. If you extend the affliction: never change `TemplateId` (quest kill objectives match on it), and
   always `Duplicate()` a material before tinting it or the change writes through to every other
   instance sharing that imported resource.

**A new world event**
1. Author `data/world_events/Xxx.tres` (`script_class="WorldEventResource"`): unique `Id`,
   `Kind` (`0`=Raid / `1`=Cache / `2`=Hunt), `SelectionWeight`, `CooldownSeconds`,
   `TimeLimitSeconds`, `RegionIds` (Phase 35G — `Array[String]` of `region.*` ids;
   **empty means anywhere**, exactly as for encounters, so author it whenever the event
   belongs to one realm or it rolls in every region — that is how goblin raids reached
   Frostfang Reach), the `At{Dawn,Day,Dusk,Night}` flags, spawn knobs (enemy `MinCount`/
   `MaxCount` + `HealthMultiplier` — a Hunt champion is just a count of 1 and a multiplier,
   not a second archetype, or `CacheItemId`/`CacheQuantity`), and rewards
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
3. ⚠️ **Seed it in `GameIds.Recipes.Starting`, or it is dead content.** `CraftingComponent.Learn`
   exists and **nothing in the game calls it** — there is no recipe tome, dialogue effect or quest
   reward that teaches one (that seam is Phase 38's). That array is therefore the whole of
   reachability, it is what `PlayerFactory` seeds, and **`--validate` now checks it in both
   directions** like the bestiary — an unseeded recipe fails the build rather than rotting silently
   the way `recipe.leather_vest` did from Phase 15 to Phase 35. Gate a late-game recipe on a
   **scarce ingredient** instead, the way `recipe.drakescale_mail` gates on eight dragon scales that
   only Frostfang's dragonkin drop.

**A new spell**
1. Author `data/spells/Xxx.tres` (`script_class="SpellResource"`): unique `Id`, `School`
   (a `DamageType`), `Delivery` (`0`=Projectile / `1`=Area / `2`=Self), `ManaCost`,
   `Cooldown`, `BaseDamage`, `Healing` (Self), an optional `StatusEffectId`, and the
   delivery knobs (`Range`/`ProjectileSpeed` for projectiles, `ImpactRadius` for an AoE
   burst — a Projectile with `ImpactRadius > 0` detonates as an area on impact).
2. Auto-indexed by `SpellDatabase`. Add the id to a `SpellcastingComponent.KnownSpellIds`
   (the player's is set in `PlayerFactory`); cast with `Q`, cycle with `F`. No code change.
3. ⚠️ **The spellbook lists every spell in the database**, so an enemy's spell appears in the
   player's character screen as purchasable unless you set `PlayerLearnable = false`
   (Phase 34D). Set it on any spell authored for a monster loadout.

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
1. Add to the `StatType` enum (**append only** — ordinals persist in `.tres`/saves); if it's a
   depleting resource, update `StatTypes.IsResource`.
2. Add an exported field + mapping in `AttributeSet` (`ToBaseValues`).
3. Add a `Loc` key in `StatNames.Key` + `strings.csv`. **Not optional** —
   `StatNamesTests.EveryStatType_MapsToADistinctNonFallbackKey` fails on any stat without one.
4. Extend `EnumStabilityTests.StatType_Ordinals` to pin the new ordinal.
5. Use via `StatsComponent.GetValue(StatType.Xxx)`. A stat missing from an `AttributeSet` reads
   `0`, so a new stat is inert for existing content until something authors it.

> Worked example — the Phase 34E resistance family (`FireResist` … `NecroticResist`).
> `CombatMath.Mitigate` routes each `DamageType` through `CombatMath.ResistanceStat` and reuses
> `ArmorMultiplier`, so there is **one** defence curve, and resistance never becomes immunity
> (DESIGN's "no school a trap" rule). Authoring an enemy that shrugs off a school is now pure
> data: set the matching `*Resist` on its `AttributeSet`.

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

**A new sound cue / audio asset** (Phase 31)
1. Pick a cue id by convention: `sfx.*` / `step.*` (positional, SFX bus), `music.*`,
   `amb.*`, `ui.*`, `voice.*` (2D). The prefix alone determines the bus + positional flag via
   the pure `AudioCueRouting` — no per-cue wiring.
2. Register its sound in `AudioLibrary.Build()`: a real asset (`GD.Load<AudioStream>(...)` of a
   CC0/open `.ogg`/`.wav` under `assets/audio/`) if one exists, else a `ProceduralAudio`
   placeholder. An unregistered id plays silence and warns once — never throws.
3. Request it: publish `SoundCueRequestedEvent(id, pos)` / `MusicCueRequestedEvent(id)`, or call
   `ServiceLocator.Get<AudioDirector>().PlayCue(id[, pos])`. No code change to add a cue whose
   prefix already routes.

**A new companion** (Phase 32)
1. Author `data/companions/Xxx.tres` (`script_class="CompanionResource"`): unique `Id`
   (`companion.*`), `NameKey`/`TitleKey` (`Loc` keys — add them to `data/locale/strings.csv`, the
   validator fails without them), the build paths (`AttributesPath`/`WeaponPath`/`ModelPath`),
   `FactionId`, optional `KnownSpellIds` (non-empty ⇒ it gets a `SpellcastingComponent`, i.e. a
   caster companion), the follower envelope (`FollowDistance`/`EngageRadius`/`AttackRange`/
   `LeashRadius`), and the loyalty knobs (`StartingLoyalty`, `LoyaltyQuestReward`,
   `RecruitQuestId`/`LoyaltyQuestId`/`DialogueId`).
2. It is auto-indexed by `CompanionDatabase` and auto-registered in `CompanionRegistry` — **no code
   change**. Recruit it *by id*: a `DialogueChoice` (`Effect` `5`=RecruitCompanion), a quest hook,
   `ServiceLocator.Get<CompanionRoster>().Recruit("companion.x")`, or `companion recruit <id>` in the
   F1 console. The roster spawns the actor into a formation slot, tracks loyalty, persists the party,
   and reconciles it back on load.

**A new enemy archetype — humanoid, beast or undead** (Phase 34B/34C/34D)
1. Author `data/enemies/Xxx.tres` (`script_class="EnemyArchetypeResource"`): unique `Id`
   (`enemy.*`), a `NameKey` authored in `strings.csv`, the build paths (`AttributesPath`,
   `WeaponPath`, `LootTablePath`, optional `ModelPath` — empty falls back to a capsule in
   `PlaceholderTint`), an `AiProfileId` (see above), `FactionId`, and `XpValue`.
   `CapsuleRadius`/`CapsuleHeight` size the body *and* the melee reach — the hitbox scales off
   height against a 1.8 m humanoid reference, so a short quadruped bites at its own scale
   (Phase 34C) with no extra knob to set.
   **To make it a caster** (Phase 34D) three things must line up, and the failure is silent:
   a non-empty `KnownSpellIds` (adds the `SpellcastingComponent`), a standoff `AiProfileId`
   like `ai.caster` (so it kites instead of closing), **and a real `Mana` pool in its
   `AttributeSet`** — spells with no mana means it just stands there, with no warning. Tune
   `ManaRegen` on the archetype for cast pacing. Mark enemy-only spells
   `PlayerLearnable = false` or they show up in the player's spellbook.
2. Auto-indexed by `EnemyArchetypeDatabase`, which registers a builder with
   `EnemyTemplateRegistry`, so `EnemyArchetypeFactory` builds it and encounters/world events/quest
   kill-targets can reference the id immediately. Add a `data/encounters/*.tres` pointing at it to
   make it actually appear in the wilds. No code change — reach for a bespoke factory only when the
   actor is *structurally* different (the boss's phase controller, the acolyte's cast origin), not
   when it just has different numbers.

**A new bestiary entry** (Phase 34G)
1. Author `data/bestiary/Xxx.tres` (`script_class="BestiaryEntryResource"`): `Id` = the **enemy
   template id** it documents, `LoreKey` (authored in `strings.csv` as `enemy.<name>.lore`),
   `Category` (`0` Humanoid / `1` Beast / `2` Undead / `3` Construct / `4` Elemental / `5` Ashen /
   `6` Boss), and `KillsToKnow` — kills before the full page opens (`1` for a boss you fight once,
   so it skips the Sighted stage). Leave `NameKey` empty unless the creature has no
   `EnemyArchetypeResource` to take one from.
2. Auto-indexed by `BestiaryDatabase`; the `B` screen picks it up with no code change.
3. ⚠️ **`--validate` checks this domain in both directions.** An entry must name a registered
   template, *and* every registered template must have an entry — so adding an enemy without a
   bestiary page fails the build. That is intentional: it is the guard against content that exists
   but nothing can reach.

**A new enemy AI personality** (Phase 34A)
1. Author `data/ai_profiles/Xxx.tres` (`script_class="AIProfileResource"`): unique `Id`
   (`ai.*`) plus the knobs you want off the defaults — perception (`VisionRange`,
   `FovDegrees`, `AlertRadius`), melee (`AttackRange`, `FlankSpreadDegrees`), standoff
   (`StandoffRange`, `KiteDistance`), guard (`BlockDuration`, `BlockRecovery`), nerve
   (`RetreatHealthFraction`, `FleeOnSight`), and `AmbushRange`.
2. Auto-indexed by `AIProfileDatabase`. Point a factory's
   `EnemyAIComponent { ProfileId = "ai.xxx" }` at it. No code change — the behaviours are
   branches in the one brain, gated on these numbers, so they combine freely (a shielded
   flanking ambusher is just three knobs). A zeroed knob turns its behaviour off; an
   unknown id warns and falls back to `ai.brute`.

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
20 Deep Debugging · 21 Content Expansion** — the seam where the systems roadmap
handed off to the production roadmap, which is now the live one.

**Production roadmap, where it actually stands:** Stage A ✅ (22–28 + 25.5, Gate G0
reached) · Stage B ⏳ (29–33 built; **Gate G1 needs a maintainer play-through and one
export** — that is the only thing between here and G1) · Stage C ⏳ **in progress**:
**Phase 34 is complete (34A–34G)** — AI profiles, humanoid/beast/undead/construct/
elemental archetypes, per-school damage resistances, every magic school's on-hit
identity, Ashen corruption variants, and the bestiary. **Phase 34.5 is complete
(34.5A–34.5C)** — the Frostfang Clans faction, their clan hold (Frostfang Reach's
first settlement), three clan archetypes that stay neutral until provoked, and a
rank chain with a betrayal branch. **Phase 35 is complete (35A–35G)** — dragons: bodies with hit
zones, flight, breath weapons, lairs, and dragon country. **Phase 36 is in progress: 36A is done** —
boss fights are authored data (`data/bosses/*.tres`), **36B and 36C are done** — the Iron King is an
ordinary archetype now, so there is one path through the boss pipeline, and wind-ups are both
telegraphed (a model-independent ground ring) and interruptible (a stagger cancels a wind-up or a
cast, for every actor). **36D and 36E are done too** — phases summon add waves, an arena binds its
spawn points and phase reactions declaratively in its own scene, and every boss's intro, defeat beat
and guaranteed reward come from its own resource. **Phase 36 is complete (36A–36E).**
**Phase 37 is in progress: 37A is done** — a property can be bought and/or earned, ownership
persists, and claiming it registers a fast-travel node. Next: 37B, per-property storage. `docs/SESSION_PLAYBOOK.md` is the live per-sub-phase tracker;
`docs/PRODUCTION_ROADMAP.md` §11 mirrors phase-level status only.

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
