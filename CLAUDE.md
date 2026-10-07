# CLAUDE.md — Embervale

> **Developer SDK:** [Tooling reference](docs/TOOLING.md). Use `python tools/embervale.py` for local/CI automation,
> structured results, safe scenarios, screenshots, and shared process/artifact handling.
> `world_quality_check.py` is now a compatibility entry point to this SDK.


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
  owns it**, and honouring that split is not scaffolding. What is forbidden is a system with **no caller at all when its phase
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
  , so when quest-to-location linking lands, this rule extends to quests and gets its own
  validator arm. (An objective's optional `LocationId` is already validated against the map.)
- **Prefer composition and data.** New actors = new components + new `.tres`
  resources, not new inheritance chains or hard-coded values.
- **Respect existing architecture.** Inspect before adding; don't duplicate
  systems; refactor when it lowers long-term cost.
- **3D models: THREE LANES** (maintainer direction, 2026-10-06; it supersedes "the four packs first, do
  not mix kits" for the outdoor world). **Characters and creatures are generated with Meshy** — semi-realistic,
  per the prompt stem in `docs/3D_ASSETS.md`. **The outdoor world — trees, rocks, ice, landmarks, ruins,
  outdoor props — is generated too**: Meshy text-to-3D (`meshy-5`, preview 5 + texture 10 credits, short
  prompts, `target_polycount` set at preview, **never the image route**) into `assets/models/world/`, plus two
  in-house procedural ground-cover meshes (`tools/gen_ground_cover.py`). Owner rule: **2-3 models per
  type**, variety from scatter transform and tint. Landmarks are solid walk-around set pieces, nothing
  roofed or enterable. **The modular housing and architecture kit, enterable buildings, interiors and a listed
  set of small interactive props stay Quaternius on purpose** (Meshy cannot make hollow, enterable
  buildings), from the vendored bundles under `assets/library/`:

  | Bundle | Covers | Models |
  | --- | --- | --- |
  | `medieval_megakit/` | modular architecture — walls, roofs, doors, windows, floors, stairs | 176 |
  | `medieval_interiors/` | interiors, furniture, containers, tools, market stalls | 94 |
  | `nature_megakit/` | the retired outdoor nature set (replaced by the generated world; source only) | 68 |
  | `animations/` | 46-clip universal animation library (retargeted — `docs/3D_ASSETS.md`) | 1 |

  **The order is fixed. Stop at the first step that works:**
  1. **Inventory what exists.** `python tools/assets.py status`, `ls assets/models/<folder>/`, `ls
     assets/library/<pack>/` and `manifest.json`, and the ledger `reports/3d/archive/meshy-migration/
     manifest.csv`. The library has been "searched" from memory twice and been wrong both times.
  2. **The lane.** Cast and outdoor world: generate (`docs/RECIPES.md` → *a new generated world model*,
     *a new landmark*). Housing kit and interiors: the vendored packs.
  3. **The other vendored bundles** (`men/`, `women/`, `monsters/`, `animals/`, `rpg_items/`,
     `dungeons/`, `survival/`, `nature/`, `rts/`, `medieval_village/`).
  4. **The open web** (Poly Pizza, Kenney, Quaternius, OpenGameArt, Sketchfab) — only once the above
     genuinely do not have it, and CC0/MIT only.
  5. **Build it in Blender via the MCP.** The rare exception: ⚠️ **that MCP is not connected by default**
     (2026-08-10) — §2 says what re-adding it costs, and reaching this step is a conversation with the
     maintainer rather than a tool call.

  ⚠️ **TASKS EXPIRE, THE LEDGER IS THE LIBRARY.** A Meshy model that is not in `assets/models/` and not
  in `reports/3d/archive/meshy-migration/manifest.csv` costs credits again. Every new generation is
  appended there in the commit that adopts it (id, prompt, preview/refine/rig task ids, date, credits);
  a rejected attempt is a short note, not a row. ⚠️ **Mixing kits is still the thing to avoid inside the
  kept housing kit**: a model from a fifth source next to it reads as a mistake. The generated outdoor
  world is the deliberate exception, held together by the variety rule and `docs/ART_STYLE.md` §1.

  **No crediting is required** (maintainer direction, 2026-08-08). This build is personal, never
  published and never sold, and every asset in it is CC0, so no attribution was ever legally owed.
  `assets/CREDITS.md` is **frozen as history** — do not add entries to it and do not treat a missing
  entry as unfinished work. `assets/library/manifest.json` stays, because it is an *index* rather
  than a credit: it is what makes step 1 above cheap, and searching it costs one `grep`.

  **The contract is [`docs/3D_ASSETS.md`](docs/3D_ASSETS.md)** — rig families, adoption, the traps,
  validation. `docs/ASSET_POLICY.md` covers sourcing and licence only. Both supersede any older
  build-from-scratch or attribution guidance in this repo.
- **Code, plugins and tools: check the Godot Asset Library before reinventing.**
  Distinct from the art rule above. Fetch from the asset's linked GitHub repo (the
  connected Godot MCP has no one-click install) and adapt it to our architecture.
  Reuse only when it fits our needs *exactly* and its licence is compatible (this
  build is **private/personal — never sold or published —** so prefer MIT/CC0/open;
  avoid paid or closed). For *code*, a near-miss you have to fight is still worse
  than building clean. Note what you pulled and its licence where it lands.
- **Work in small, complete steps** (see §9). Determine the next highest-priority task — `docs/NOW.md`
  and `docs/PRODUCTION_ROADMAP.md` §4 say what is open — and do it.

---

## 2. Tech stack & environment

| Thing            | Value                                                           |
| ---------------- | --------------------------------------------------------------- |
| Engine           | Godot **4.7.1** (.NET / Mono build) — 4.7.0 until 2026-08-09     |
| Language         | C# targeting `net8.0`, `Nullable` enabled, `ImplicitUsings` off |
| SDK              | `Godot.NET.Sdk/4.7.0` (see `Embervale.csproj`)                  |
| Assembly / root ns | `Embervale`                                                   |
| Entry scene      | `scenes/Main.tscn` → `ApplicationRoot` (`src/Bootstrap`)        |
| Target platforms | Windows, Linux — PC and laptop only (Forward+ renderer)         |

**The Godot MCP is [IvanMurzak/Godot-MCP](https://github.com/IvanMurzak/Godot-MCP) v0.20.1**
(maintainer direction, 2026-08-09 — it replaced `@coding-solo/godot-mcp`, which was last published
in February and whose npm build is missing its own upstream RCE fix). It is a **C# editor addon
vendored into this repo** at `addons/godot_mcp/`, wired by `.mcp.json` (project-scoped) and declared
in `Embervale.csproj` — see the comments there before touching either.

⚠️ **IT IS EXCLUDED FROM A SHIPPING BUILD (2026-09-03).** `Embervale.csproj` has an
`EmbervaleTooling` property — true by default, false under `ExportRelease` — and when it is false
the addon, its two NuGet packages, the `src/Debugging/*Shots.cs` harnesses and `VfxPerfScenario.cs`
are not compiled, and
neither is the assembly-wide `CS0618` suppression the addon required. `TreatWarningsAsErrors` is on
unconditionally. Check it with `dotnet build Embervale.csproj -c ExportRelease && python
tools/check_shipping_assembly.py`. Godot compiles every `.cs` under the project into ONE assembly,
so this per-configuration exclusion is the only separation available — a separate tooling *project*
is not reachable for anything a scene attaches a script to.

⚠️ **IT IS EDITOR-BOUND, AND THE OLD ONE WAS NOT.** Its ~42 tools drive a **running Godot editor**;
they cannot launch a headless run. So the verification spine of this repo is unchanged and is still
the shell: `dotnet build`, `dotnet test`, and `--validate` / `--economy` / `--state` / `--play`
through the console exe below. What the MCP adds that the shell cannot is **viewport, camera and
isolated-node screenshots** — aimed at the most expensive recurring defect here, the "RENDER IT"
trap (placement that reads fine in a `.tscn` and looks wrong on screen).

⚠️ **It is in Custom (local) mode on purpose.** The server is the `gamedev-mcp-server` binary running
on **this machine** at `localhost:23630`. The current installation is under
`.godot/mcp-server/win-x64/`; older installations used `.ai-game-dev/` (both gitignored — binaries
and credentials do not belong in history). The vendor default is a hosted cloud at `ai-game.dev`
that would route this project's scenes and scripts through a third party. **Do not switch modes
without asking.**

Check current processes at the start of a session: a relay may already be running even when the
editor is closed. Reuse it when compatible. Three pieces have to line up:

| Half | What it is | Who starts it | Alive when |
| --- | --- | --- | --- |
| The server | local `gamedev-mcp-server.exe port=23630` | **Agent or human**, when needed for the task | `gamedev-mcp-server.exe` is in the task list and 23630 is LISTENING |
| The editor | Godot open **on the task's checkout**, in Custom mode, pointed at that URL | **Agent or human**, when needed for validation | a `Godot_v4.7.1…` process is running |
| The tools | `.mcp.json` → `http://localhost:23630/p/<pin>` | **Claude Code, at startup only** | `mcp__ai-game-developer__*` appear in the tool list |

**Bringing it up:** start the installed local relay only if it is absent, using a hidden background
process. The current server's arguments are `port=23630 plugin-timeout=10000
client-transport=streamableHttp auth=none`. Then run on the task's checkout:

```
godot-cli open . --mode Custom --url http://localhost:23630 \
  --editor-path <the console-less .exe from §2's path>
godot-cli wait-for-ready . --url http://localhost:23630
```

**Check it with one repo command before planning screenshot work** —
`python tools/godot_mcp_check.py --probe` proves `.mcp.json` is loopback-only, then runs
`godot-cli status .` and a real editor tool with timeouts. A relay without an editor, a cloud URL,
a missing CLI, or a failed editor round trip is an explicit non-zero failure. It reports
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

**Agents may start the local server and editor when the task needs them** (maintainer direction,
2026-09-07). No separate human-start permission is required. Check the exact worktree and existing
processes first; reuse a compatible local relay, and launch the editor on the task's isolated
checkout. Do not close or redirect another checkout's editor or server. Keep Custom mode and
loopback URLs, and prove readiness with an actual editor tool. Start background helpers hidden;
show the editor when interactive review needs it. Shell builds, tests and rendered automation
remain available independently; a listening server alone never counts as MCP validation.

⚠️ **The port has to match in three places or nothing connects**, and the failure is a bare
"connection refused": the server's `--port`, the editor's `--url` (it reads `GODOT_MCP_HOST` **at
process start**, so an editor already running in the wrong mode must be closed and reopened —
`godot-cli close .`), and `.mcp.json`, which `godot-cli configure . --agent claude-code` writes as
`http://localhost:23630/p/<pin>`. The CLI derives 23630 from the project path; the binary defaults to
**8080**, which is the mismatch to expect.

⚠️ **`.mcp.json` is read when Claude Code starts**, and **the server has to be reachable at that
moment** — otherwise no `mcp__ai-game-developer__*` tools exist for the whole session no matter what
you fix afterwards. Restarting is the only cure; until then
the same tools are reachable over HTTP: `godot-cli run-tool <name> . --url http://localhost:23630
--input '{...}'`, and `godot-cli status .` says whether the editor and server are both up. Tool names
are the folder names under `.claude/skills/`; each `SKILL.md` carries the argument schema (⚠️ they
are the *tool's* names, e.g. `scene-open` takes `resourcePath`, not `path`).

⚠️ **THERE ARE TWO TOOL ENDPOINTS AND `run-tool` ONLY REACHES ONE.** `ping` lives at
`/api/system-tools/` and **404s** through `run-tool` with `Tool with Name 'ping' not found` — which
reads exactly like a broken connection on a connection that is working perfectly. Use
`godot-cli run-system-tool ping . --url ...` for those, and probe with a real editor tool such as
`scene-list-opened` instead; `screenshot-viewport` returns a genuine PNG once the editor is attached.

⚠️ **OPENING GODOT FROM THE PROJECT MANAGER PUTS IT IN CLOUD MODE, SILENTLY.** `GODOT_MCP_HOST` / `GODOT_MCP_CONNECTION_MODE` are read
at process start and are only set when the editor is launched **through `godot-cli open`**. Launch it
any other way and the running editor talks to `ai-game.dev` while the local server sits listening with
nothing behind it — which is precisely the "listening port, 503 anyway" state above, and it also
regenerates all 42 `.claude/skills/*/SKILL.md` pointing at the cloud. **If the skill docs show cloud
URLs, the editor is in the wrong mode: close it and reopen with the `godot-cli open` line above.**

⚠️ **THE BLENDER MCP NO LONGER STARTS WITH A SESSION** (maintainer direction, 2026-08-10). Its
`uvx blender-mcp` entry was **removed from the user-level `~/.claude.json`**, so no `blender-mcp.exe`
is spawned at startup and **no `mcp__blender__*` tools appear in the tool list at all.** That is the
intended state: it was launching a process every session for a step-5 tool that almost no session
reaches (1,136 models are vendored, the cast and the outdoor world are generated, and §1 stops at step 1 to 3 nearly every time).

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
([`docs/ASSET_POLICY.md`](docs/ASSET_POLICY.md)).

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
**This is now guarded, but only on some routes.** `python tools/embervale.py` rebuilds a stale DLL
before it launches the engine (step `auto-build`; `--no-build` opts out), and a tooling build run
on the engine binary logs one `STALE_BINARY` warning, or exits 1 with `-- --strict-build`. A **raw
`godot ...` run still executes the stale binary** after that warning, the editor's `run_project`
does too, and `--script` GDScript probes are not checked at all. The table of what is guarded is in
[`docs/TOOLING.md`](docs/TOOLING.md) (*Stale-build guard*).

A plain launch lands on the **main menu**, not in the world, and the menu's
buttons need input no tool here can inject — so it verifies boot and database loading, nothing
in-world. Use `--play`, or `--new-game` through the SDK (§3), when you need an actual session. The `WorldIntegrityChecker` (5s) stays
silent unless an invariant breaks, so give a run several seconds before trusting a clean log. When
you have **not** built+run something, say it was *reviewed against the Godot 4.7 C# API* — reserve
"verified/tested running" for output you actually captured.

⚠️ **`godot` is not on `PATH`.** The `godot …` invocations below are shorthand; the binary is

```
C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe
```

Use the `_console.exe` variant from a shell — the plain `.exe` detaches and prints nothing to
stdout, so you lose the log you ran it for.

**CI runs in GitHub Actions** (`.github/workflows/ci.yml`). Two jobs on every push/PR, plus a weekly/manual full battery:

- **Fast deterministic (`Build & test`) — REQUIRED.** Generation, warning-free build, xUnit,
  template, seams and layout. About a minute. This is the one that earns its keep: it proves the
  repo builds, tests and validates **from a clean checkout by someone who is not the maintainer** —
  an unstaged `.tres`, an asset that only imports because the local `.godot/` cache already has it,
  a test that passes on local state. A local run structurally cannot catch that class of bug.
- **Engine + visual (`Validate content`) — ADVISORY since 2026-08-31** (maintainer direction). Strict
  asset import, runtime regressions, deterministic visual comparison, uploaded failure evidence. It
  still runs and still uploads; it no longer blocks a merge (`continue-on-error: true`).
  ⚠️ **Why:** it renders the game on a GPU-less runner under xvfb, which is not the renderer its
  baselines were captured on. It failed thirteen Frostfang frames on ground shading alone, and the
  step-up probe aborted with SIGABRT *after* printing PASS — neither about the repository. **Run
  `python tools/world_quality_check.py` locally instead**, where a frame can be looked at.
- **Weekly/manual full** — exact negative mutations and structured performance history.

**`.github/workflows/` edits push normally**; the session token carries `workflow` scope. ⚠️ If a
workflow push is ever rejected, a missing `workflow` scope is the reason, not a repo permission.

⚠️ The green **Vercel** check that also appears on every PR is still a meaningless no-op —
Vercel is trying to deploy a Godot game as a web app. Ignore that one; the Codex quality jobs above
are the build signal.

---

## 3. Build & run

**For the human:**
1. Install Godot 4.7+ **.NET build** and the .NET 8 SDK.
2. Open `project.godot` in the editor (it builds C# automatically), or
   `dotnet build Embervale.sln`.
3. Press Play. `scenes/Main.tscn` boots to the main menu; *New Game* / *Continue* enters the world.

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

The `--` forwards `--validate` as a user argument; `ApplicationRoot` detects it
(`HeadlessValidation`), loads every database, runs `ContentValidator.RunAll()` (cross-
references + well-formedness + graph reachability), prints the report, and exits **0** on
pass / **1** on any issue. ⚠️ **It also walks every region lattice for off-route terrain traps** —
ground the player can walk into and cannot climb out of — so it is slow, and catches a class of defect
no file could show.

**Headless gates that run a real session:** `--lifecycle` (three New Game → save → destroy → Load
round trips, failing on any leaked session, service, subscription, saveable or node) and `--story`
(`HeadlessStory`: plays the whole campaign, New Game to credits, through the real systems three
ways in one process: run A the clean road to the Dawnfire ending, run B every ember to the Lord of
Embers, run C legacy-save fixtures; `--story-mission=<n>` plays one stretch from a fixture and is
partial). Both exit 0/1, or 2 on a bad option. `--story` proves wiring, not that a
fight can be won. It also runs inside the Windows export as its smoke test (`docs/NOW.md` has the
export command).

**The canonical quality runner:** `python tools/world_quality_check.py --mode full` orchestrates the
specialist gates in dependency order. `fast` is engine/rendering-free; `engine` adds `--validate`
and live Godot regressions; `visual` runs deterministic captures and localized comparisons;
`performance` emits a structured machine-sensitive report; `full` adds the exact negative mutation
battery. Every subprocess has a timeout and each run writes summary, logs and reproduction commands
under `artifacts/quality/`. `--fast` remains an alias for `--mode fast`; `--list` prints the matrix.

**Headless content census (no gameplay):** `godot --headless --path . -- --state` prints how many
regions, cells, items, shops, services, dialogues and quests exist, and every cell with its centre.
It reads the databases the game loads, so it cannot drift from a doc. Use it instead of grepping
`data/` at the start of a session. Exits **0** — a census, not a gate.

**Headless economy report (no gameplay):** `godot --headless --path . -- --economy` loads every
database, prints the realm's buy-low/sell-high table and exits **0** (an observation, not a gate). It
is the same `EconomyReport.Arbitrage` the `economy` dev command prints. Every headless mode now ends
with one `EMBERVALE_RESULT {json}` line and is quiet unless `--verbose`; `-- --gates` lists the modes
and their options, and a misspelt mode flag exits 2 instead of idling on the title.

**Launch straight into gameplay (dev):** `godot --path . -- --play` boots past the menu into
the most recent save, so systems that only init on world build (the audio directors, spawners)
can be launched deterministically — useful for capturing runtime logs without driving the menu
(the menu's *Continue* needs input the MCP can't inject). It continues the newest save slot
(`--slot=<name>` picks one); with no saves it stays on the menu, and `--new-game` (tooling builds,
isolated `EMBERVALE_USER_DIR` only) is the alternative. This is the one-command content gate for the maintainer (and
later CI). The same battery is also reachable in-game via the `validate-all` dev console
command (`F1`).

**The dev console runs from a shell.** `python tools/embervale.py console "tp out; spawn
enemy.goblin 3; frames 30; assert enemies.count ge 3"` runs any `F1` command, plus `wait`, `frames`,
`assert`, `wait-until`, `expect`, `shot` and `input`, in an isolated new game and exits non-zero on
a failed statement (raw form: `-- --new-game --exec "..."` with an absolute `EMBERVALE_USER_DIR`;
all session flags go after `--`). `--new-game` is refused without that isolated directory, so it
cannot touch real saves. [`docs/TOOLING.md`](docs/TOOLING.md) has the verbs, the `get` keys and the
other one-command routes (`gate`, `shots`, `perf-report`, `verify`).

**What a bare `--play` still doesn't prove:** it resumes where the save left off, which for the
Ember Crown is usually the town hub *inside* the region's 34 m `SafeZoneRadius`, where the
`EncounterDirector` deliberately won't spawn. A quiet log after a `--play` run therefore proves
boot, database loading and save restore — **not** that new enemies spawn or fight. To exercise a
fight, leave the safe zone with `tp out` in a console script, or measure one with `gate arena`. Say
which of the two you got; don't let one stand in for the other.

**Sandbox controls:** `WASD` move · mouse look · `Shift` sprint · `Caps Lock` walk · `Space` jump ·
`LMB` attack · `RMB` block · `Q` cast · hold `F` spell wheel, tap `F` previous spell ·
`E` interact · `V` swap first/third person ·
`I` inventory · `T` spellbook · `B` bestiary · `C` party order ·
`H` heal dummy · `R` respawn dummy · `F5`/`F9` quick save/load · `Esc` pause (frees the cursor).
Hotbar is `1`–`5`. Gamepad plays the whole game (sticks move/look, RT/LT attack/guard, A/B jump/dodge,
RB cast, hold LB for the spell wheel on the right stick, tap LB for the previous spell).
**Any blocking menu pauses the scene tree**; a cinematic lock (boss intro, prologue) does not —
see `UiState.Open(owner, pausesWorld:)`.

---

## 4. Repository layout

```
project.godot     Engine config + autoload registration (order matters — see §7)
Embervale.sln     C# solution (net8.0, Godot.NET.Sdk 4.7.0)
CLAUDE.md         You are here
README.md         Public overview of the game
docs/             NOW · MECHANICS · PRODUCTION_ROADMAP · HISTORY · ARCHITECTURE · RECIPES · IDS · SAVE_FORMAT
                  DESIGN · LORE · ART_STYLE · UI_STYLE · RENDERING · ASSET_POLICY · 3D_ASSETS
                  WORLD_AUTHORING · WORLD_ATLAS · TOOLING · playbook/finish.md  (§5 says which when)
scenes/           Main.tscn (entry, ApplicationRoot) + regions/<region>/<cell>.tscn
assets/
  library/        Vendored Quaternius CC0 SOURCE art, .gdignore'd — Godot never imports or
                  exports it. A model enters the game only by being adapted into models/
  models/         The models the game actually loads
  CREDITS.md      Frozen history — do not add entries (§1)
data/             Authored content, one folder per resource type
src/              One folder per system — §5 maps folder → system
tests/            Embervale.Tests (xUnit, pure logic only; a Godot Resource cannot be constructed)
tools/            Generators, gates and harnesses (not shipped): region_spec_*.py + gen_regions.py,
                  world_bake.py, world_atlas.py, gen_map_locations.py, gen_main_story.py,
                  assets.py, embervale.py (the SDK; subcommands in embervale_sdk/commands/),
                  regen.py (every generator), content.py (refs, census, diff, balance),
                  perf_compare.py, shot_analyze.py, analytics.py, repro/*.txt console scripts,
                  *_probe.gd probes (probe_base.gd), *_shots.gd render harnesses
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
| Find out whether a mechanic exists, and where | [`MECHANICS.md`](docs/MECHANICS.md) — one line per mechanic | ~6k |
| Author content of any kind | [`RECIPES.md`](docs/RECIPES.md) — **the one recipe only** | ~10k tok total |
| Change how a system works | [`ARCHITECTURE.md`](docs/ARCHITECTURE.md) — the relevant § only | ~13k |
| Pick an id for anything new | [`IDS.md`](docs/IDS.md) | ~3k |
| **Touch anything that saves or loads** | **[`SAVE_FORMAT.md`](docs/SAVE_FORMAT.md)** — the `SaveId` contract, what is deliberately *not* saved, and the failure policy | ~3k |
| Pick up work | **[`docs/NOW.md`](docs/NOW.md) first**, then [`playbook/finish.md`](docs/playbook/finish.md) for the finish run's ids | ~4k + ~2k |
| Check a phase's status or what is left | [`PRODUCTION_ROADMAP.md`](docs/PRODUCTION_ROADMAP.md) | ~2k |
| Learn what a past phase did, or its traps | [`HISTORY.md`](docs/HISTORY.md), then the repository log for the old playbook | ~4k |
| Build or change a region | [`WORLD_AUTHORING.md`](docs/WORLD_AUTHORING.md) and [`WORLD_ATLAS.md`](docs/WORLD_ATLAS.md) | ~8k |
| Make a design call (economy, difficulty, systems cut) | [`DESIGN.md`](docs/DESIGN.md) | ~9k |
| Write or place anything the player reads | [`LORE.md`](docs/LORE.md) | ~3k |
| **Add, replace or adapt a model** | **[`3D_ASSETS.md`](docs/3D_ASSETS.md)** — the whole contract, then `python tools/assets.py status` | ~6k |
| Decide where a model should come from | [`ASSET_POLICY.md`](docs/ASSET_POLICY.md) — sourcing and licence only | ~2k |
| Restyle a model / a screen | [`ART_STYLE.md`](docs/ART_STYLE.md) / [`UI_STYLE.md`](docs/UI_STYLE.md) | ~4k / ~7k |

**Start every session at [`docs/NOW.md`](docs/NOW.md)** — where the project is, the live invariants,
and the commands. It is the only place project state is maintained; everything else links to it.

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

Magic regression gates: `python tools/embervale.py tool magic_core_probe`, `magic_status_probe`,
`magic_learning_probe`, `magic_content_probe` and `magic_lifetime_probe`. Build and import first;
all five also belong to the engine/full world suite. The content probe checks prepared world tome
interactions; the lifetime probe checks cancellation before live load and session/caster ownership.
The integrated contract and probe coverage are in [`docs/playbook/magic.md`](docs/playbook/magic.md);
system ownership is in [`ARCHITECTURE.md`](docs/ARCHITECTURE.md#213-magic-srcmagic).

Spell effects are one layer behind one door: gameplay calls the `SpellVfx` facade
(`src/Magic/Vfx`) and never builds an effect node itself. Before adding or changing a spell's look
read the header of `src/Magic/Vfx/SpellVfx.cs` and of `SpellVfx.Kit.cs` (the kit's API: shaped
particles, motifs, the helpers a special calls), then the *Spell effects* part of
[`ARCHITECTURE.md`](docs/ARCHITECTURE.md#213-magic-srcmagic) and
[`RENDERING.md`](docs/RENDERING.md#spell-effects). Their rules are `docs/NOW.md` invariants 53 to 56,
and the harnesses that render them (`--spellshots`, `--camshots`, `--vfxperf`) are in NOW's command
list. ⚠️ No agent has ever seen these effects move: they were judged from still frames.

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
- **No hard-coded player-facing strings.** Every UI/dialogue string the
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
  `TelegraphComponent` once shipped without the defer: 58 NREs and ~50 orphan leaks in one
  playthrough. **A node built for the tree should also build its own resources in its constructor,
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
- ⚠️ **Load a project C# `Resource` by path with `ResidentResources.Load<T>`, never bare `GD.Load`.**
  A wrapper nobody holds can be collected while the native object is still cached, and the next
  load of that path is a `gchandle.is_released()` FATAL on the finalizer thread (the `--lifecycle`
  intermittent, closed 2026-09-25). `ResidentResourceTests` enforces it; engine types (scenes,
  textures, meshes) are exempt.
- **A stagger cancels a wind-up, not a live blow.** `CharacterActionComponent` drops an action (unless it is `Interruptible = false`) only
  during its wind-up; once the hit window opens the attack is committed. `SpellcastingComponent`
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
  An audit once found this in 6 of 27 implementations, and the symptoms were never
  obviously save-related — a downed companion re-wounded on load, spells still on cooldown from a
  future that never happened, a chest that looked plundered but was full, a faction hostile in a
  save that predates it. `EquipmentComponent.Load` and `PerksComponent.Load` are the models to copy:
  both strip what they applied *before* rebuilding from the save.
- **A load restores state; it does not narrate one.** Suppress the announcement events on the restore
  path — a reconcile that re-publishes them toasts "Kael joins you" on every reload. UI that must
  survive a load should re-derive from `GameLoadedEvent` instead, which is what `PartyWidget` and
  `CompanionRecruiterComponent` already do.
- **A service's lifetime is where it is parented.** Register with
  `ServiceScope.RegisterOwned(this, this)`; the node's own `TreeExiting` unregisters it. A freed
  registrant found in a live scope is an `Invariant` violation — a dereferenced freed node is a hard
  `gchandle.is_released` crash, not a null check away.
- **Don't dereference injected nodes outside `PlayerInputRouter`'s not-playing guard.** The
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

Its table of contents lists every recipe by name — one `Read` of the ToC is cheaper than carrying the
list here, where it loaded every session whether or not any content was being authored.

⚠️ **If you are about to author content and cannot find a recipe for it, that is a finding.** Write
one when you are done, in the same shape: what to author, in what order, and what bit you personally.

## 9. Development workflow

- **Branch:** develop on a per-phase branch (e.g. `claude/phase-23d-…`) off `main`.
  **`main` is the trunk.** Never push directly to `main`; always go through a PR.
- **Per change:** implement → keep buildable/playable → rewrite `docs/NOW.md` (and
  `docs/PRODUCTION_ROADMAP.md` if a phase's status changed) → commit → push →
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

## 10. Project status

**Where the project is lives in [`docs/NOW.md`](docs/NOW.md) and nowhere else** — current state,
open work, the last verification numbers and the live invariants. The game is complete from New Game
to credits; [`docs/PRODUCTION_ROADMAP.md`](docs/PRODUCTION_ROADMAP.md) has phase status and what is
left, [`docs/HISTORY.md`](docs/HISTORY.md) what each phase produced and the traps worth knowing, and
[`docs/playbook/finish.md`](docs/playbook/finish.md) the finish run's id registry. Do not duplicate
status here or in `README.md`.

### Standing constraints (these are rules, not history)

- **Three art lanes: the cast and the outdoor world are generated, the housing and architecture kit
  is Quaternius** (§1, and [`docs/3D_ASSETS.md`](docs/3D_ASSETS.md)). 1,136 models are vendored at
  `assets/library/` behind a `.gdignore`; a kept-kit model enters the game by being **adopted into
  `assets/models/`** via `python tools/assets.py adopt`, a generated one through `adopt-batch`, and that
  is the only step — **crediting is not required and `assets/CREDITS.md` is frozen as history.** The
  outdoor world is stylised-realistic, not faceted low-poly. There are no cosmetic bolt-ons (the enemy
  identity kit, the NPC outfit kit, the player's pauldrons and pouch are deleted): a body's look is its mesh.
- **Four asset traps, each of which shipped a defect before it was written down:** judge a
  candidate **from behind and at eye level** (an open-backed cottage nearly shipped twice; a
  **hi-vis vest and hard hat** stood in a medieval market until someone rendered it close up);
  exclude the glTF importer's `glTF_not_exported` **`Icosphere`** when measuring a rig, or every
  scale comes out 1 m too tall; **verify a written asset by parsing the file**, not the Blender
  viewport; and **do not round-trip a rigged model** — it destroys bone-parented children, so when
  a rig already fits, the correct adaptation is a **file copy**.
- ⚠️ **Check what is already vendored before pulling anything.** An open-web pull once returned a
  file **byte-identical** to `assets/library/women/adventurer.glb`, unadapted since the migration.
  `ls` the library and read `manifest.json`'s licence field first.
- ⚠️ **Render every candidate body at eye level, front and back, before adopting it.** Four of six
  candidates in one batch were unusable (modern dress, a punk with a chainsaw, an ornament that is
  not a person, a four-bone rig), and none of it was visible from a filename.
- **A region streams prepared cells by distance** (Near/Mid/Far/Backdrop radii in the spec's
  `BUDGET`); collision exists only at Near and Mid, so a probe must focus the streamer on a point
  before asking about the ground there. ⚠️ **The six realms sit in disjoint atlas bands**
  (`docs/WORLD_ATLAS.md`, checked by `tools/world_atlas.py --check`): the Ember Crown about
  x −520..520, z −720..440, Frostfang Reach north of it, the Ashen Wilds east, the Sunspire Dominion
  south, the Pale Concord far west and the Celestial Realm far north-east. ⚠️ **The Pale Concord's
  name must not appear in player-visible text before its reveal** (`docs/NOW.md` invariant 34). ⚠️ **Places live in world space,
  cells are partitions** (the 2026-09 world rebuild): author geography and roads in the spec's world
  coordinates and settlements at a content origin; `tools/check_world_composition.py` fails a region
  whose places line up with its lattice. Schedules are cell-local; a property names its cell.
- ⚠️ **THE GROUND IS ONE GENERATED SURFACE PER REGION AND CELL SCENES CARRY NO FLOOR** (the
  2026-08-29 geography overhaul). `data/regions/*.tres` is **generated** from
  `tools/region_spec_<region>.py` by `tools/gen_regions.py`, which checks the cell lattice tiles
  exactly and that every seam route is authored from both sides of one world point. Terrain, its
  collider and the navmesh source all come from `WorldHeightfield`; an authored node's Y is a
  clearance above the ground, not a world height. Read `docs/WORLD_AUTHORING.md` before touching a
  cell — the traps there are the ones that cost this overhaul its afternoons.
- ⚠️ **ONE RUNNER SAYS WHETHER A REGION IS HEALTHY: `python tools/world_quality_check.py --mode full`**.
  It orchestrates the specialist gates in the order they have to run —
  generation, build, tests, `--validate`, the negative battery, the starter template, seams, layout,
  map markers, step-up, the mesh census, a region swap, a melee swing, a real capsule on every route,
  the screenshot regression and a per-cell performance sample. Use the explicit modes in §3. It
  ORCHESTRATES; every rule lives in the tool that owns it, and adding a check there that is not
  implemented elsewhere is how two validators start disagreeing.
- ⚠️ **A NEW REGION STARTS FROM `tools/region_spec_template.py`, NOT FROM A COPY OF THE EMBER
  CROWN.** Copying the shipped spec drags eight hundred lines of one realm's history along and
  inherits its cell sizes, noise seed and road widths as though they were physics. The template is a
  real spec — running it directly builds a four-cell region and checks its lattice — and its numbered
  comments are the workflow.
- ⚠️ **THERE ARE NO GROUND TEXTURES AND THERE MUST NOT BE.** The terrain material is six painted
  layers of noise from `data/terrain_layers/` grouped by `data/biomes/`; `docs/ART_STYLE.md` §4/§6.3
  forbid photo texturing, and the model set the terrain sits under ships with zero texture images. A
  CC0 PBR ground pack is the reflex here and it would make the terrain the only photographed thing in
  a hand-painted world.
- ⚠️ **DEEP WATER IS DECLARED DATA, NOT A MESH.** `WorldWaterResource` on a cell puts a body under
  `WorldWater`'s non-swimming recovery contract and lets `WorldCellWater` take its shoreline from the
  terrain. A translucent plane authored in a `.tscn` is invisible to the system whose whole job is
  keeping the player out of it — six of them shipped that way, over basins 4.5 m deep.
- ⚠️ **The `rts` library pack is roughly 1/6 scale** and nothing in the files says so. Measure
  any candidate against a 1.8 m reference before authoring around it, and adapt through
  `nodes/root_scale` in the `.import` rather than a Blender round-trip.
- ⚠️ **THERE ARE NO SURVIVAL NEEDS IN THIS GAME, AND PHASE 40 IS STRUCK** (maintainer direction,
  2026-08-12). No durability, no hunger, no thirst, no temperature, and no repair service — all cut,
  not deferred and not condition-gated, so **do not propose any of them as a fix for anything.** Food
  items stay what they are: instant-heal consumables with a `food` trade tag. 40B's rule that a cut
  system leaves no stub is what the cut was executed under, and it survives the phase being struck —
  `docs/NOW.md` invariant 35 is its home. `docs/DESIGN.md` §6 carries the gold-sink table.
- ⚠️ **PHASE 40.5 IS STRUCK TOO** — no puzzle, trap or dungeon-framework tooling (same direction).
  Dungeons are rooms with encounters and loot on existing tooling; any future relic trial needs its
  own answer that is not a puzzle system.

---

## 11. Glossary

Terms are defined where they are used: `IEntity`/`EntityComponent` in `src/Entities`, `DamagePacket`
in `src/Combat`, hurt/hitboxes in `docs/ARCHITECTURE.md` §2. It lived here as a list and cost tokens
every session to answer questions nobody was asking.

---

## 12. 3D assets

**[`docs/3D_ASSETS.md`](docs/3D_ASSETS.md) is the contract. `python tools/assets.py status` is the
state.** Those two answer everything: the five rig families, where models come from, how to adopt
one, and every trap that has shipped a defect. Nothing under `reports/3d/archive/` is required
reading, and this section deliberately does not restate the rules — it used to, and the copy drifted.

Replacing a model is `python tools/assets.py adopt <src> <dest>` then `python tools/assets.py
validate`. Rig mapping, textures, retargeting, `.import` configuration and the ordering are handled;
re-fitting the collision capsule is the one part that still needs judgement.

A generated outdoor-world model goes `tools/meshy_batch.py` → `python tools/assets.py adopt-batch` →
`tools/make_landmark_wrapper.py` (landmarks, trees, rocks) → `tools/repoint_models.py`, and its prompt and
task ids are appended to `reports/3d/archive/meshy-migration/manifest.csv`; the steps and the traps they
hit are in `docs/RECIPES.md` (*Assets*) and `docs/3D_ASSETS.md` (OUTDOOR WORLD). The cast and the world are
generated, the housing kit is not: sourcing is `docs/ASSET_POLICY.md` §0.1.

⚠️ **Compilation, import, tests and `--validate` are not visual validation.** Every 3D trap this
repo has recorded was invisible in a log and visible only in a render.
