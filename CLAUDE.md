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
- **Build real, functioning systems** — never theoretical scaffolding. A feature lands
  complete and *exercisable*: authored data, `--validate` coverage, tests for any pure logic,
  and at least one way to drive it (an interactable, a dialogue effect, a dev-console command).
  **A sub-phase may land the mechanism and leave its world placement to the sub-phase that
  owns it** — `docs/SESSION_PLAYBOOK.md` is the authority on that split, and honouring it is
  not scaffolding. What is forbidden is a system with **no caller at all when its phase
  closes**: `CraftingComponent.Learn` sat with zero callers from Phase 15 to Phase 35, and
  `recipe.leather_vest` rotted behind it the whole time.
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

**Headless economy report (no gameplay):** `godot --headless --path . -- --economy` loads every
database, prints the realm's buy-low/sell-high table and exits **0** (an observation, not a gate). It
is the same `EconomyReport.Arbitrage` the `economy` dev command prints — and it exists because the
`F1` console cannot be driven from a remote session, so a console-only report would ship unexercised.

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
`I` inventory · `T` spellbook · `B` bestiary · `C` party order ·
`H` heal dummy · `R` respawn dummy · `F5`/`F9` quick save/load · `Esc` pause (frees the cursor).
Hotbar is `1`–`5`. Gamepad plays the whole game (sticks move/look, RT/LT attack/guard, A/B jump/dodge).
**Any blocking menu pauses the scene tree**; a cinematic lock (boss intro, prologue) does not —
see `UiState.Open(owner, pausesWorld:)`.
Goblins roam to the north (−Z) and drop loot.

---

## 4. Repository layout

```
project.godot     Engine config + autoload registration (order matters — see §7)
Embervale.sln     C# solution (net8.0, Godot.NET.Sdk 4.7.0)
CLAUDE.md         You are here
README.md         Public overview + the player-facing phase table
docs/             ARCHITECTURE · RECIPES · IDS · DESIGN · LORE · ART_STYLE · UI_STYLE
                  ASSET_POLICY · PRODUCTION_ROADMAP · SESSION_PLAYBOOK  (§5 says which to read when)
scenes/           Main.tscn (entry, GameBootstrap) + regions/<region>/<cell>.tscn
assets/
  library/        Vendored Quaternius CC0 SOURCE art, .gdignore'd — Godot never imports or
                  exports it. A model enters the game only by being adapted into models/
  models/         The models the game actually loads
  CREDITS.md      Provenance + licence for every asset. Mandatory before commit
data/             Authored content, one folder per resource type
src/              One folder per system — §5 maps folder → system
tests/            Embervale.Tests (xUnit, pure logic only; a Godot Resource cannot be constructed)
tools/            Dev harnesses, not shipped content (market_shots.gd renders a cell)
```

**`data/` is uniform, so it does not need listing:** the folder name *is* the resource type
(`data/shops/` holds `ShopResource` `.tres`), every folder is **auto-indexed by a matching
`XxxDatabase` at boot**, and adding a file to one is all it takes to register new content — which
is why almost every recipe in [`docs/RECIPES.md`](docs/RECIPES.md) is "author a `.tres`, no code
change". `data/_templates/` holds blanks to copy; `data/locale/strings.csv` is the `Loc` catalogue
every player-facing string goes through (§6). `ls data/` is cheaper than a list here that drifts.

**Conventions for new files:** namespace mirrors folder (`Embervale.<Folder>[.<Sub>]`); one primary
type per file; file name == type name.

## 5. Architecture & systems

The architecture (autoload spine, EventBus, entity/component model, stats,
persistence) and the full **systems reference** — combat, AI, items/loot,
progression, quests, dialogue, magic, world, crafting, factions, events, save,
UI, debugging — together with the **collision layers & teams** and the
**content/data pipeline** now live in
**[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)**. Read the relevant section
there before touching a system; [`docs/RECIPES.md`](docs/RECIPES.md) is its actionable companion
(how to add content), and the gotchas in §7 are the traps to avoid.

### Which doc to open, and when

**Open the one you need, not all of them** — three of these are large and only one of them (this
file) is free.

| You are about to… | Read | Size |
| --- | --- | --- |
| Author content of any kind | [`RECIPES.md`](docs/RECIPES.md) — **the one recipe only** | ~17k tok total |
| Change how a system works | [`ARCHITECTURE.md`](docs/ARCHITECTURE.md) — the relevant § only | ~23k |
| Pick an id for anything new | [`IDS.md`](docs/IDS.md) | ~3k |
| Continue the roadmap | [`SESSION_PLAYBOOK.md`](docs/SESSION_PLAYBOOK.md) — **your sub-phase's entry and the two before it** | ~89k total |
| Check a phase's scope or gate | [`PRODUCTION_ROADMAP.md`](docs/PRODUCTION_ROADMAP.md) | ~22k |
| Make a design call (economy, difficulty, systems cut) | [`DESIGN.md`](docs/DESIGN.md) | ~9k |
| Write or place anything the player reads | [`LORE.md`](docs/LORE.md) | ~3k |
| Add or adapt a model | [`ASSET_POLICY.md`](docs/ASSET_POLICY.md) + `assets/CREDITS.md` | ~2k |
| Build or restyle a model / a screen | [`ART_STYLE.md`](docs/ART_STYLE.md) / [`UI_STYLE.md`](docs/UI_STYLE.md) | ~4k / ~7k |

⚠️ **`SESSION_PLAYBOOK.md` is ~89k tokens and is a chronological log — never read it whole.** It is
sectioned by phase; `grep -n` for your sub-phase id and read outwards. Its most useful content is
almost always the "two things worth carrying into the next sub-phase" line on the entries just
before yours.

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
| `src/Housing` `src/Economy` | Claimable holdings + placement; shops, vendors and the buy/sell spread |
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
- ⚠️ **A component may never `AddChild` to `Entity.Body` directly in `OnInitialize`** — always
  `Entity!.Body.CallDeferred(Node.MethodName.AddChild, node)`. The body is still setting up its own
  children during a component's `_Ready`, so Godot **refuses** the add ("parent node is busy setting up
  children"), *logs it, and carries on* — it does not throw. The node you just built is then a live C#
  object that is not in the tree, which fails in three places at once and none of them look related:
  its `_Ready` never runs (so fields assigned there stay null and every later call throws an NRE
  through a `?.` that passes), it renders nothing, and it leaks as an orphan node for the run — which
  is what the `WorldIntegrityChecker` orphan invariant is actually catching when it fires.
  `TelegraphComponent` shipped in 36C without the defer and produced 58 NREs and ~50 orphan leaks in
  one playthrough; `WeaponTrailComponent`, `LairSpawnComponent` and `TrophyStandComponent` all defer
  and always did. **A node built for the tree should also build its own resources in its constructor,
  not in `_Ready`** — the deferred add leaves a one-frame window where it is alive but not ready, and
  a caller landing in that window should draw nothing rather than crash.
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
- **`ISaveable.Load` must *replace* live state, never merge over it.** A load is not always applied
  to a fresh world — a quickload keeps every live actor and component, so anything `Load` does not
  explicitly overwrite survives from the timeline being abandoned. The rule: for every fact you
  restore, ask what happens when the saved value is **absent, `false`, or `0`** while the live value
  is not. `Clear()` the collection before repopulating; write the `else` branch for the boolean.
  A repo-wide audit (2026-08-05) found this in 6 of 27 implementations, and the symptoms were never
  obviously save-related — a downed companion re-wounded on load, spells still on cooldown from a
  future that never happened, a chest that looked plundered but was full, a faction hostile in a
  save that predates it. `EquipmentComponent.Load` and `PerksComponent.Load` are the models to copy:
  both strip what they applied *before* rebuilding from the save.
- **A load restores state; it does not narrate one.** Suppress the announcement events on the restore
  path — a reconcile that re-publishes them toasts "Kael joins you" on every reload. UI that must
  survive a load should re-derive from `GameLoadedEvent` instead, which is what `PartyWidget` and
  `CompanionRecruiterComponent` already do.
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

## 8. Recipes → [`docs/RECIPES.md`](docs/RECIPES.md)

**Adding content — a shop, a quest, a boss, a region, an item — has a recipe, and it lives in
[`docs/RECIPES.md`](docs/RECIPES.md).** Read the one you need before you author anything; each is
the fields to set, the order to set them in, and the trap it has already sprung on somebody. **Every
⚠️ in that file is a defect that shipped.**

It is a separate file for one measured reason: it was **66% of this one**, and this one loads into
every session while no session needs more than one recipe. Splitting it cut the standing cost of
opening this repo by roughly two thirds and lost nothing — the recipes are one `Read` away.

The 40 recipes, so you know what exists without opening it:

- **Code** — A new component · A new status effect · A new stat · A new event · A new persistent system · A new input action · A new dev-console command · A new UI panel / HUD widget
- **Actors & combat** — A new actor / enemy type · A new boss fight (Phases 36A–36D) · A big/boss creature with body zones (Phase 35A) · Making a creature fly (Phase 35B) · A breath weapon (Phase 35C) · Placing a world boss in a lair (Phase 35D) · A creature that talks (Phase 35F) · A new weapon
- **Items & progression** — A new item · A new piece of equipment · A new loot affix · A new loot table / dropper · A new perk · A new XP-bearing enemy (or tuning the curve) · A new crafting recipe · A new spell
- **World & content** — A new quest · A new conversation · A new NPC routine · A new weather state · A new encounter · A new world event · A new faction
- **Economy & housing** — A new claimable property (Phase 37A) · Giving a property a stash (Phase 37B) · A new placeable prop or a buildable yard (Phase 37C) · Giving a property a trophy stand (Phase 37D) · A new shop / merchant (Phase 38A–38J) · A new service — trainer / bank / inn / stable (Phase 38D) · A production settlement (Phase 38N1) · A tolled crossing — toll, permit, bribe (Phase 38M) · A new gold sink (Phase 38C)

⚠️ **If you are about to author content and cannot find a recipe for it, that is a finding.** Write
one when you are done, in the same shape: what to author, in what order, and what bit you personally.

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

**Where the project is: Stage C, Phase 38 (economy), sub-phases 38A–38N2 done, 38O next.**

- **Phases 1–21 built the systems**, not the game — a data-driven sandbox that *can* express
  Embervale. **Phases 22+ are the production roadmap** that carries it to launch.
- **Stage A ✅** (22–28 + 25.5, gate G0 reached). **Stage B ⏳** — 29–33 are built, and
  **gate G1 needs a maintainer play-through and one export**. That is the only thing between here
  and G1, and no amount of further building moves it.
- **Stage C ⏳ in progress.** Complete: **34** (AI profiles + the enemy roster + per-school
  resistances + the bestiary), **34.5** (Frostfang Clans and their hold), **35** (dragons: hit
  zones, flight, breath, lairs), **36** (bosses as authored data, telegraphed and interruptible),
  **37** (housing: deeds, stashes, placeable props, trophy stands), **37.5** (the UI overhaul).
  In progress: **38** (economy) — shops, stock depth, standing-priced trade, paid services, trade
  tags and specialties, saturation, gated shelves and merchant investment, trading hours and
  travelling merchants, the Embermarket district and its twelve merchants, and the Crossway toll
  with the permit and the bribe that get past it.

**Two docs carry the detail and this one deliberately does not:**
[`docs/PRODUCTION_ROADMAP.md`](docs/PRODUCTION_ROADMAP.md) §11 mirrors phase-level status;
[`docs/SESSION_PLAYBOOK.md`](docs/SESSION_PLAYBOOK.md) is the live per-sub-phase tracker and holds
every retrospective and trap. **Read the playbook entry for the sub-phase you are about to do** —
the ones immediately before it usually name the thing that will bite you.

### Standing constraints (these are rules, not history)

- **The art set standardises on Quaternius CC0 packs** (maintainer direction, 2026-08-05 — policy
  in [`docs/ASSET_POLICY.md`](docs/ASSET_POLICY.md) §0, provenance in `assets/CREDITS.md`). 401
  models are vendored at `assets/library/` behind a `.gdignore`; a model enters the game only by
  being **adapted into `assets/models/`** and credited. **Every model is CC0 and the project owes
  no attribution — keep it that way.**
- **Four asset traps, each of which shipped a defect before it was written down:** judge a
  candidate **from behind and at eye level** (an open-backed cottage nearly shipped twice; a
  **hi-vis vest and hard hat** stood in a medieval market until someone rendered it close up);
  exclude the glTF importer's `glTF_not_exported` **`Icosphere`** when measuring a rig, or every
  scale comes out 1 m too tall; **verify a written asset by parsing the file**, not the Blender
  viewport; and **do not round-trip a rigged model** — it destroys bone-parented children, so when
  a rig already fits, the correct adaptation is a **file copy**.
- ⚠️ **Check what is already vendored before pulling anything** (38N2). The library was declared
  "out of medieval bodies" in 38L; the open-web pull that followed returned a file **byte-identical**
  to `assets/library/women/adventurer.glb`, which had been sitting unadapted since the migration —
  38L's claim that the unused women were all CC-BY 3.0 was wrong about that one. `ls` the library and
  read `manifest.json`'s licence field first.
- ⚠️ **Render every candidate body at eye level, front and back, before adopting it.** Four of six
  candidates in 38N2 were unusable (modern dress, a punk with a chainsaw, an ornament that is not a
  person, a four-bone rig), and none of it was visible from a filename. This trap has now fired three
  times: `npc_townsman` (hi-vis, 38K→38L), `npc_merchant_f` (t-shirt and trainers, 38L→38N1).
- **A region loads whole** (maintainer direction, 38M2). Every cell of the active region is resident
  from the moment it is entered; `RegionStreamer` has no distance test and no unload path during
  play, and `RegionCellResource.LoadRadius`, `StreamDecision` and its tests were deleted with the
  rule. ⚠️ **Both regions cannot be resident at once** — Frostfang's roosts share coordinate space
  with the Ember Crown's arena and northern wilds, so that is a Phase 44 world-layout question and
  not a streaming one. A new cell is therefore permanently in the tree: author accordingly.
- ⚠️ **The `rts` library pack is roughly 1/6 scale** and nothing in the files says so (38M2). Measure
  any candidate against a 1.8 m reference before authoring around it, and adapt through
  `nodes/root_scale` in the `.import` rather than a Blender round-trip.
- **Repair does not exist and that is a decision** — no durability concept exists anywhere, Phase
  40A decides whether to adopt or explicitly cut it, and 40B's rule is that a cut system leaves no
  stub. `docs/DESIGN.md` §6 carries the authoritative gold-sink table.

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
