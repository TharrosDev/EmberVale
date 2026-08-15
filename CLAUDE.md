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
  owns it** — `docs/playbook/` is the authority on that split, and honouring it is
  not scaffolding. What is forbidden is a system with **no caller at all when its phase
  closes**: `CraftingComponent.Learn` sat with zero callers from Phase 15 to Phase 35, and
  `recipe.leather_vest` rotted behind it the whole time.
- **Persistence is not optional.** Any system that holds gameplay state must be
  able to save/load (implement `ISaveable`).
- ⚠️ **IF THE PLAYER CAN GO THERE, IT GOES ON THE MAP — IN THE SAME SUB-PHASE THAT ADDS IT**
  (maintainer direction, 2026-08-10). Every **shop, service, settlement, dungeon, landmark, quest
  destination and point of interest** authored from now on gets a `MapLocationResource` and a placed
  `MapLocationComponent` *as part of the work that creates it*, never as a follow-up. Author it with
  `tools/gen_map_locations.py`; the recipe is `docs/RECIPES.md` → *a new map location*.
  **This is enforced, not requested:** `ContentValidator.ValidateEverythingIsOnTheMap` fails
  `--validate` for any shop or service no map location names, and coverage ships at 23/23 and 15/15
  so the rule can only be broken by adding something new. The map is a world-readability system only
  while it is complete — the first merchant with no pin is the one that teaches the player the map
  cannot be trusted, and after that they stop looking at it. ⚠️ **Quest destinations are not yet
  coverable and that is tracked, not forgotten:** a quest names a template id rather than a place
  (39.5B), so when quest-to-location linking lands, this rule extends to quests and gets its own
  validator arm.
- **Prefer composition and data.** New actors = new components + new `.tres`
  resources, not new inheritance chains or hard-coded values.
- **Respect existing architecture.** Inspect before adding; don't duplicate
  systems; refactor when it lowers long-term cost.
- **3D models: THE FOUR PACKS FIRST, ALWAYS** (maintainer direction, 2026-08-08 — this
  supersedes the older "search the web first" rule, which is now step 3). The art set is
  four Quaternius CC0 MegaKits vendored under `assets/library/`, and **the near-entirety of
  the game is to be built from them**:

  | Bundle | Covers | Models |
  | --- | --- | --- |
  | `medieval_megakit/` | modular architecture — walls, roofs, doors, windows, floors, stairs | 176 |
  | `medieval_interiors/` | interiors, furniture, containers, tools, market stalls | 94 |
  | `nature_megakit/` | trees, pines, bushes, grass, flowers, rocks, pebbles, rock paths | 68 |
  | `animations/` | 46-clip universal animation library (retargeted — `ASSET_POLICY.md` §0.2) | 1 |

  **The order is fixed. Stop at the first step that works:**
  1. **The four packs.** `ls assets/library/<pack>/` and read `manifest.json`. Do not skip this
     because a name did not come to mind — the library has been "searched" from memory twice and
     been wrong both times.
  2. **The other vendored bundles** (`men/`, `women/`, `monsters/`, `animals/`, `rpg_items/`,
     `dungeons/`, `survival/`, `nature/`, `rts/`, `medieval_village/`). Characters and creatures are
     **not** in the four packs, so this is where they come from.
  3. **The open web** (Poly Pizza, Kenney, Quaternius, OpenGameArt, Sketchfab) — only once 1 and 2
     genuinely do not have it, and CC0/MIT only.
  4. **Build it in Blender via the MCP.** The rare exception, and now genuinely rare: with 746
     vendored models the honest answer is almost always in step 1 or 2. ⚠️ **That MCP is not
     connected by default any more** (2026-08-10) — §2 says what re-adding it costs, and reaching
     this step is a conversation with the maintainer rather than a tool call.

  ⚠️ **Mixing kits is the thing to avoid.** Four kits by one author read as one world; a stray
  model from a fifth source reads as a mistake even when it is well made. If step 3 or 4 is reached,
  match the pack's flat-shaded, untextured-looking style or do not adopt it.

  **No crediting is required** (maintainer direction, 2026-08-08). This build is personal, never
  published and never sold, and every asset in it is CC0, so no attribution was ever legally owed.
  `assets/CREDITS.md` is **frozen as history** — do not add entries to it and do not treat a missing
  entry as unfinished work. `assets/library/manifest.json` stays, because it is an *index* rather
  than a credit: it is what makes step 1 above cheap, and searching it costs one `grep`.

  **Full policy: [`docs/ASSET_POLICY.md`](docs/ASSET_POLICY.md)** — mandatory, and it supersedes
  any older build-from-scratch or attribution guidance in this repo.
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
| Engine           | Godot **4.7.1** (.NET / Mono build) — 4.7.0 until 2026-08-09     |
| Language         | C# targeting `net8.0`, `Nullable` enabled, `ImplicitUsings` off |
| SDK              | `Godot.NET.Sdk/4.7.0` (see `Embervale.csproj`)                  |
| Assembly / root ns | `Embervale`                                                   |
| Entry scene      | `scenes/Main.tscn` → `GameBootstrap` (`src/Bootstrap`)          |
| Target platforms | Windows, Linux, Steam Deck (Forward+ renderer)                  |

**The Godot MCP is [IvanMurzak/Godot-MCP](https://github.com/IvanMurzak/Godot-MCP) v0.20.1**
(maintainer direction, 2026-08-09 — it replaced `@coding-solo/godot-mcp`, which was last published
in February and whose npm build is missing its own upstream RCE fix). It is a **C# editor addon
vendored into this repo** at `addons/godot_mcp/`, wired by `.mcp.json` (project-scoped) and declared
in `Embervale.csproj` — see the comments there before touching either.

⚠️ **IT IS EDITOR-BOUND, AND THE OLD ONE WAS NOT.** Its ~42 tools drive a **running Godot editor**;
they cannot launch a headless run. So the verification spine of this repo is unchanged and is still
the shell: `dotnet build`, `dotnet test`, and `--validate` / `--economy` / `--state` / `--play`
through the console exe below. What the MCP adds that the shell cannot is **viewport, camera and
isolated-node screenshots** — which is aimed squarely at §7's most expensive recurring defect, the
"RENDER IT" trap that has fired seven times and currently needs a hand-copied `tools/market_shots.gd`.

⚠️ **It is in Custom (local) mode on purpose.** The server is the `gamedev-mcp-server` binary running
on **this machine** at `localhost:23630`, under `.ai-game-dev/` (gitignored — a 40 MB binary and a
credential file do not belong in the history). The vendor default is a hosted cloud at `ai-game.dev`
that would route this project's scenes and scripts through a third party. **Do not switch modes
without asking.**

⚠️ **IT IS DOWN AT THE START OF EVERY SESSION AND NOTHING SAYS SO.** Nothing below survives a reboot,
a closed editor or a restarted Claude Code, and **every failure mode reads as a broken tool rather
than a stopped process.** Three pieces have to line up, and the middle one is usually the missing one:

| Half | What it is | Who starts it | Alive when |
| --- | --- | --- | --- |
| The server | `.ai-game-dev/server/gamedev-mcp-server.exe --port 23630` | **A human, once per boot** — nothing here spawns it | `gamedev-mcp-server.exe` is in the task list and 23630 is LISTENING |
| The editor | Godot open **on this project**, in Custom mode, pointed at that URL | **A human, every session** | a `Godot_v4.7.1…` process is running |
| The tools | `.mcp.json` → `http://localhost:23630/p/<pin>` | **Claude Code, at startup only** | `mcp__ai-game-developer__*` appear in the tool list |

**Bringing it up — three commands, in this order, all verified 2026-08-09:**

```
.ai-game-dev/server/gamedev-mcp-server.exe --port 23630        # leave running; loopback only
godot-cli open . --mode Custom --url http://localhost:23630 \
  --editor-path <the console-less .exe from §2's path>
godot-cli wait-for-ready .
```

**Check it with one command before planning any work that needs it** — `godot-cli status .` reports
both processes, and its "everything is off" answer looks like this (**captured 2026-08-10**):

```
Godot Editor Process
WARN: Godot is not running with this project
MCP Server
  URL: http://localhost:23630
Probing http://localhost:23630...
ERROR: Not available (timed out)
ERROR: Godot is not running and MCP server is not reachable
```

⚠️ **A LISTENING PORT IS NOT A WORKING MCP, AND THAT IS THE TRAP WORTH THE INK.** In that capture
`gamedev-mcp-server.exe` **was** running and 23630 **was** LISTENING in `netstat` — the probe still
timed out and every tool call failed, because the server only relays and there was no editor behind
it to answer. So `netstat` and the task list prove nothing here; **`godot-cli status .` is the probe**.
The failure a tool call gives instead is an HTTP 503 that names retries rather than a missing editor,
which is why it reads as a broken server:

```
ERROR: HTTP 503: Service Unavailable
  "error": "Invoke 'RunCallTool': Failed to invoke '…Model.RequestCallTool' after 10 retries."
```

⚠️ **Do not start either half yourself, and do not work around it.** Opening a Godot editor is a GUI
process on the maintainer's desktop; ask them to run the three commands above. And note what is
*not* blocked meanwhile: `dotnet build`, `dotnet test` and every `--validate` / `--economy` /
`--state` / `--play` run go through the console exe and **do not touch the MCP at all**, so a session
with the editor closed can still do the whole verification spine. The one thing genuinely lost is
screenshots — which is exactly the "RENDER IT" trap, so a sub-phase that places anything in the world
**needs the editor up before it starts**, not after the placement is done.

⚠️ **The port has to match in three places or nothing connects**, and the failure is a bare
"connection refused": the server's `--port`, the editor's `--url` (it reads `GODOT_MCP_HOST` **at
process start**, so an editor already running in the wrong mode must be closed and reopened —
`godot-cli close .`), and `.mcp.json`, which `godot-cli configure . --agent claude-code` writes as
`http://localhost:23630/p/<pin>`. The CLI derives 23630 from the project path; the binary defaults to
**8080**, which is the mismatch to expect.

⚠️ **`.mcp.json` is read when Claude Code starts**, and **the server has to be reachable at that
moment** — otherwise no `mcp__ai-game-developer__*` tools exist for the whole session no matter what
you fix afterwards (39.5A ran its entire session this way). Restarting is the only cure; until then
the same tools are reachable over HTTP: `godot-cli run-tool <name> . --url http://localhost:23630
--input '{...}'`, and `godot-cli status .` says whether the editor and server are both up. Tool names
are the folder names under `.claude/skills/`; each `SKILL.md` carries the argument schema (⚠️ they
are the *tool's* names, e.g. `scene-open` takes `resourcePath`, not `path`).

⚠️ **THERE ARE TWO TOOL ENDPOINTS AND `run-tool` ONLY REACHES ONE** (39.5A). `ping` lives at
`/api/system-tools/` and **404s** through `run-tool` with `Tool with Name 'ping' not found` — which
reads exactly like a broken connection on a connection that is working perfectly. Use
`godot-cli run-system-tool ping . --url ...` for those, and probe with a real editor tool such as
`scene-list-opened` instead. Both were captured working this way; `screenshot-viewport` returns a
genuine PNG once the editor is attached.

⚠️ **OPENING GODOT FROM THE PROJECT MANAGER PUTS IT IN CLOUD MODE, SILENTLY** (39.5A, and it is the
cause of the `SKILL.md` drift 39A reported). `GODOT_MCP_HOST` / `GODOT_MCP_CONNECTION_MODE` are read
at process start and are only set when the editor is launched **through `godot-cli open`**. Launch it
any other way and the running editor talks to `ai-game.dev` while the local server sits listening with
nothing behind it — which is precisely the "listening port, 503 anyway" state above, and it also
regenerates all 42 `.claude/skills/*/SKILL.md` pointing at the cloud. **If the skill docs show cloud
URLs, the editor is in the wrong mode: close it and reopen with the `godot-cli open` line above.**

⚠️ **THE BLENDER MCP NO LONGER STARTS WITH A SESSION** (maintainer direction, 2026-08-10). Its
`uvx blender-mcp` entry was **removed from the user-level `~/.claude.json`**, so no `blender-mcp.exe`
is spawned at startup and **no `mcp__blender__*` tools appear in the tool list at all.** That is the
intended state: it was launching a process every session for a step-4 tool that almost no session
reaches (746 models are vendored, and §1 stops at step 1 or 2 nearly every time).

**If a session genuinely needs it, the maintainer re-adds it — ask, do not do it yourself:**

```
claude mcp add blender -s user -- uvx blender-mcp     # then RESTART Claude Code; the tools
                                                      # are read at startup and not before
```

⚠️ **And re-adding it is only half.** The other half is a socket server **inside a running Blender**
(`BlenderMCP` add-on on `localhost:9876`, installed at
`%APPDATA%\Blender Foundation\Blender\5.1\scripts\addons\addon.py`), started by hand: launch
`C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`, press `N` in the viewport for the
sidebar, open the **`BlenderMCP`** tab, press **`Connect to Claude`**. With the server entry back but
Blender closed, every call returns this and nothing else (**captured 2026-08-10**, when it was still
registered):

```
Error getting scene info: Could not connect to Blender. Make sure the Blender addon is running.
```

**So there are two different "it is missing" states and they mean opposite things:** *no
`mcp__blender__*` tools at all* is the normal, intended state and needs no action — reach for the
vendored library instead; *tools present but calls returning the string above* means the entry is back
and Blender is closed, so ask the maintainer to connect the add-on.

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

⚠️ **NOTHING THAT RUNS THE GAME RECOMPILES C#.** Launching the engine — from a shell or from the
editor — runs whatever `Embervale.dll` was last built, so after editing any `.cs` you MUST rebuild
first or you are exercising a **stale binary** (a silent trap: a behaviour-preserving change looks
"verified" while your edit never ran). The shell here **has `dotnet` 8.0**: rebuild with
`dotnet build Embervale.sln` (output goes to `.godot/mono/temp/bin/Debug/Embervale.dll`, where the
game loads it), *then* run. Pure-logic unit suite: `dotnet test tests/Embervale.Tests`.

A plain launch lands on the **main menu**, not in the world (Phase 24's meta-shell), and the menu's
buttons need input no tool here can inject — so it verifies boot and database loading, nothing
in-world. Use `--play` (§3) when you need an actual session. The `WorldIntegrityChecker` (5s) stays
silent unless an invariant breaks, so give a run several seconds before trusting a clean log. When
you have **not** built+run something, say it was *reviewed against the Godot 4.7 C# API* — reserve
"verified/tested running" for output you actually captured.

⚠️ **`godot` is not on `PATH`.** The `godot …` invocations below are shorthand; the binary is

```
C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe
```

Use the `_console.exe` variant from a shell — the plain `.exe` detaches and prints nothing to
stdout, so you lose the log you ran it for.

**CI runs in GitHub Actions** (`.github/workflows/ci.yml`, added by the 2026-08-15 audit —
CI had been declined earlier, and the maintainer reversed that once the audit showed 1426
tests and the whole `ContentValidator` battery depended on someone remembering two commands).
Two jobs on every push:

- **Build & test** — `dotnet build --warnaserror` then `dotnet test`. This is the gate, and it
  has no external dependency beyond NuGet. Red here means the code.
- **Validate content** — downloads Godot and runs `-- --validate`, which exits non-zero on a
  content failure. Slower (it imports `assets/`, cached on the `.import` hashes) and has more
  moving parts, so it is deliberately a separate job.

⚠️ **`.github/workflows/` needs a token with `workflow` scope.** The session OAuth token does
not have it; that file was pushed through the GitHub API instead. If a workflow edit is ever
rejected on push, that is why — it is not a repo permission problem.

⚠️ The green **Vercel** check that also appears on every PR is still a meaningless no-op —
Vercel is trying to deploy a Godot game as a web app. Ignore that one; the two CI jobs above
are the build signal.

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

**Headless content census (no gameplay):** `godot --headless --path . -- --state` prints how many
regions, cells, items, shops, services, dialogues and quests exist, and every cell with its centre.
It reads the databases the game loads, so it cannot drift from a doc. Use it instead of grepping
`data/` at the start of a session. Exits **0** — a census, not a gate.

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
                  ASSET_POLICY · PRODUCTION_ROADMAP · NOW.md · playbook/  (§5 says which to read when)
scenes/           Main.tscn (entry, GameBootstrap) + regions/<region>/<cell>.tscn
assets/
  library/        Vendored Quaternius CC0 SOURCE art, .gdignore'd — Godot never imports or
                  exports it. A model enters the game only by being adapted into models/
  models/         The models the game actually loads
  CREDITS.md      Provenance + licence for every asset. Mandatory before commit
data/             Authored content, one folder per resource type
src/              One folder per system — §5 maps folder → system
tests/            Embervale.Tests (xUnit, pure logic only; a Godot Resource cannot be constructed)
tools/            Dev harnesses, not shipped content:
                    market_shots.gd          instantiates a cell and renders it (copy per use)
                    gen_cell_props.py        prop table -> .tscn node stanzas
                    gen_merchant_dialogue.py the resident-merchant graph scaffold
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
| **Touch anything that saves or loads** | **[`SAVE_FORMAT.md`](docs/SAVE_FORMAT.md)** — the `SaveId` contract, what is deliberately *not* saved, and the failure policy | ~3k |
| Continue the roadmap | **[`docs/NOW.md`](docs/NOW.md) first**, then your phase's file in [`docs/playbook/`](docs/playbook/README.md) | ~1k + ~8k |
| Check a phase's scope or gate | [`PRODUCTION_ROADMAP.md`](docs/PRODUCTION_ROADMAP.md) | ~22k |
| Make a design call (economy, difficulty, systems cut) | [`DESIGN.md`](docs/DESIGN.md) | ~9k |
| Write or place anything the player reads | [`LORE.md`](docs/LORE.md) | ~3k |
| Add or adapt a model | [`ASSET_POLICY.md`](docs/ASSET_POLICY.md) + `assets/CREDITS.md` | ~2k |
| Build or restyle a model / a screen | [`ART_STYLE.md`](docs/ART_STYLE.md) / [`UI_STYLE.md`](docs/UI_STYLE.md) | ~4k / ~7k |

**Start every session at [`docs/NOW.md`](docs/NOW.md)** — where the project is, the live invariants,
and the commands, in about a screen. It is the only place project state is maintained; everything
else links to it.

The playbook is **one file per phase** in [`docs/playbook/`](docs/playbook/README.md) — open only
yours, and read the two entries above it. Its most useful content is almost always the "two things
worth carrying into the next sub-phase" line on the entries just before yours.

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
- ⚠️ **No documentation line over ~2,000 characters** (agent-ergonomics pass). A table cell that
  wants a paragraph becomes a `###` section below the table with a link from the cell. This is not
  style: `PRODUCTION_ROADMAP.md` had a **15,421-character** phase cell on one line and `README.md`
  an 11,582-character one, so *any* `grep` matching a phase word dumped ~5k tokens of prose into
  context. Check with `awk 'length($0)>2000' docs/*.md README.md CLAUDE.md`.
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

Its table of contents lists all 40 by name — one `Read` of the ToC is cheaper than carrying the
list here, where it loaded every session whether or not any content was being authored.

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
- **CI must be green before a merge** — build + tests, and content validation (see §2). The
  Vercel check remains a no-op and is not a signal.

---

## 10. Roadmap status

**Where the project is lives in [`docs/NOW.md`](docs/NOW.md) and nowhere else.** It carries the
current sub-phase, the next one, the last verification numbers, and the live invariants — about a
screen. It used to be duplicated here, in `README.md`, in the roadmap and in the playbook, and all
four were rewritten every sub-phase.

- **Phases 1–21 built the systems**, not the game — a data-driven sandbox that *can* express
  Embervale. **Phases 22+ are the production roadmap** that carries it to launch.
- **Stage A ✅** (22–28 + 25.5, gate G0 reached). **Stage B ⏳** — 29–33 are built, and
  **gate G1 needs a maintainer play-through and one export**. That is the only thing between here
  and G1, and no amount of further building moves it.
- **Stage C ⏳ in progress**, and it is the arc you are almost certainly working in.

**Two docs carry the detail and this one deliberately does not:**
[`docs/PRODUCTION_ROADMAP.md`](docs/PRODUCTION_ROADMAP.md) §11 mirrors phase-level status;
[`docs/playbook/`](docs/playbook/README.md) is the per-sub-phase tracker and holds every
retrospective and trap. **Read the playbook entry for the sub-phase you are about to do** — the ones
immediately before it usually name the thing that will bite you.

### Standing constraints (these are rules, not history)

- **The art set is four Quaternius CC0 MegaKits** (maintainer direction, 2026-08-08 — the priority
  order is §1, the detail is [`docs/ASSET_POLICY.md`](docs/ASSET_POLICY.md) §0). **746** models are
  vendored at `assets/library/` behind a `.gdignore`; a model enters the game by being **adapted
  into `assets/models/`**, and that is now the only step — **crediting is not required and
  `assets/CREDITS.md` is frozen as history.** Everything is CC0 and the build is personal, never
  published and never sold.
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
- ⚠️ **THERE ARE NO SURVIVAL NEEDS IN THIS GAME, AND PHASE 40 IS STRUCK** (maintainer direction,
  2026-08-12). No durability, no hunger, no thirst, no temperature, and no repair service — all cut,
  not deferred and not condition-gated, so **do not propose any of them as a fix for anything.** Food
  items stay what they are: instant-heal consumables with a `food` trade tag. 40B's rule that a cut
  system leaves no stub is what the cut was executed under, and it survives the phase being struck —
  `docs/NOW.md` invariant 28 is its home. `docs/DESIGN.md` §6 carries the gold-sink table.
- ⚠️ **PHASE 40.5 IS STRUCK TOO** — no puzzle, trap or dungeon-framework tooling (same direction).
  Phase 50 authors dungeons as rooms with encounters and loot; Phase 51E's relic trials will need
  their own answer when they land. `docs/playbook/phase-40_5.md` records the consequence.

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

Terms are defined where they are used: `IEntity`/`EntityComponent` in `src/Entities`, `DamagePacket`
in `src/Combat`, hurt/hitboxes in `docs/ARCHITECTURE.md` §2. It lived here as a list and cost tokens
every session to answer questions nobody was asking.
