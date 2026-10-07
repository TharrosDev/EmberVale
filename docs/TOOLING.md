# Embervale developer SDK

The canonical interface is `python tools/embervale.py`. It runs Godot 4.7 .NET and the
existing validators without a Godot editor or MCP server. Python uses only its standard
library. Run from the repository root; an absolute path to the entry point also works.

```powershell
python tools/embervale.py doctor
python tools/embervale.py build
python tools/embervale.py import
python tools/embervale.py validate
python tools/embervale.py test
python tools/embervale.py scenario tools/headless/scenarios/new-game.json
python tools/embervale.py scenario tools/headless/scenarios/gameplay-capture.json
python tools/embervale.py all --render --timeout 1800
```

## Agent quick reference

One command per task. `G` is the console Godot executable (`CLAUDE.md` §2); everything else is run
from the repository root. Each SDK run prints its failures and one verdict line ending in
`evidence=<run directory>`; add `-v` for the per-step and per-statement lines.

| Task | Command |
| --- | --- |
| Check what I changed | `python tools/embervale.py verify` (`--plan` first: the gates it would run and their cost) |
| Run a console script in the game | `python tools/embervale.py console "tp out; spawn enemy.goblin 3; frames 30; assert enemies.count ge 3"` |
| Validate only items | `python tools/embervale.py gate validate -- --only=items` (groups and arms: `-- --list`) |
| Ask the content one question | `python tools/embervale.py gate state -- --ids=quests --match=main`, or `-- --get=<id>` |
| See one view | `python tools/embervale.py shots shot --cell <cellId> --yaw 90 --hour 19.5` |
| See one UI state | `python tools/embervale.py shots shot --ui panelshots/<shot>` (names: `shots panelshots --list`) |
| Judge motion | `python tools/embervale.py shots shot --spell <id> --phase impact --film 16x3`, then read `<shot>.film.png` |
| Measure a fight | `python tools/embervale.py gate arena -- --arena=enemy.goblin --trials=5 --level=5` |
| Measure a session | `python tools/embervale.py perf-report --render --seconds 20` |
| Triage a log | `python tools/embervale.py logs` (the newest run), or `logs <file or run directory>` |
| Find who uses an id | `python tools/content.py refs <id, res:// path or model stem>` |
| See what a content change did | `python tools/content.py diff <rev>` |
| Long job in the background | `python tools/embervale.py job start world --mode engine`, then `job status` and `job wait --max 540` |
| Read the last result again | `python tools/embervale.py last` (`--failed` for the newest failure) |
| Check the machine before a heavy run | `python tools/embervale.py doctor` |
| Check every generator for drift | `python tools/regen.py --check` |
| Plan or watch a world bake | `python tools/world_bake.py --plan`, `--status` |

`python` resolves on the maintainer machine's shell (3.14 at 2026-10-07). If it does not, Codex's
bundled executable is
`C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe`;
in PowerShell use `& '<absolute-python-path>' tools/embervale.py doctor`.
The SDK discovers the console Godot executable in PATH or the existing Downloads
installation. Explicit `--godot`, `EMBERVALE_GODOT`, then `GODOT` take precedence;
an invalid explicit path fails rather than silently running a different engine.
`EMBERVALE_BLENDER` overrides optional Blender discovery. `dotnet` and `git` must be on PATH.
Doctor validates the actual engine's 4.7 Mono version, .NET 8 SDK/runtime and project targets.
`tools/headless/config.json` holds the default per-process timeout (900 seconds). It is the default
for a step with no timeout of its own, not a ceiling: a gate's declared timeout (1200, 1800 or 3600
seconds) stands, and only an explicit `--timeout` caps it.

## Commands

A command's own flags go after the command name; the shared flags (`--json`, `--timeout`,
`--artifacts`, `-v`, `--no-build`, `--wait-lock`, ...) work everywhere. `list` prints the names.

| Command | Coverage |
| --- | --- |
| `verify` | The git diff mapped to the smallest gate set that covers it. See *Changed-only iteration*. |
| `gate MODE -- OPTIONS` | One headless game mode (`validate state economy worldgen lifecycle story arena gates`), its result line folded into the run. See *Headless modes*. |
| `run NAME -- ARGS` | A headless mode or any session harness flag (`run story`, `run hudshots`) with the SDK's guards. See *Running a game flag*. |
| `console "SCRIPT"` | A dev-console script in an isolated new game or a copied save slot. See *Console scripts*. |
| `shots SUITE` | A render suite or one `shot`, fixed step, manifest, contact sheets, comparison. See *Shots*. |
| `perf-report` | A session played for N seconds, one verdict and a baseline comparison. See *Performance*. |
| `vfxperf` | Spell-effect frame cost at several tiers in one launch, a baseline verdict per tier. |
| `job ACTION` | Detached background runs: `start`, `status`, `wait`, `tail`, `list`, `cancel`. See *Background jobs*. |
| `logs [FILE or RUN_DIR]` | A log grouped into unique errors and warnings with counts. See *Log triage*. |
| `last [N]` | Reprints the newest run's compact result without launching anything; `--failed`, `--json`. Exit 2 when there is none. |
| `clean` | Deletes old run directories; `--keep 20`, `--older-than DAYS`, `--dry-run`. Nothing is pruned automatically. |
| `doctor` | Versions and configuration, then the machine: stale binary, free memory, running Godot processes, build servers, the heavy lock, the import cache, an interrupted negative battery, git, run count, MCP (info only). Does not start servers. |
| `import` | Godot editor importer, error detection even when Godot exits zero. `--reimport` removes only the regenerable `.godot/imported` cache before importing. |
| `build` | Entire solution, warnings-as-errors as configured by the project. Run import separately on a fresh checkout. |
| `validate` | Native recursive resource loading, dependency checks and detached scene instantiation, followed by the existing global `ContentValidator`. |
| `inspect` | Boot `--scene` or Main; serialize scene tree, groups, stored properties, resource paths, autoload configuration and state. |
| `smoke` | Boot for `--frames`, collect state/logs/metrics and detect runtime errors or abnormal exit. |
| `scenario PLAN.json` | Run the finite declarative operations below against a real game process. |
| `author PLAN.json` | Create/customize native scenes and resources, round-trip validation, optional protected publication. |
| `report NAME` | Existing state/economy/worldgen/lifecycle reports. The modes are quiet now, so this captures only the result line; use `gate NAME -- --verbose` for the prose. |
| `test` | Tool unittest suite plus the existing C# xUnit suite; TRX in the run directory. `--only tool\|game`, `--filter <xUnit expression>`, `--py <unittest -k pattern>`; a filter for one suite runs only that suite. Failed tests become `test.failed` diagnostics. |
| `screenshot [PLAN.json]` | Set up a scene/scenario and capture with metadata; repeat `--resolution` for a capture set. |
| `perf [PLAN.json]` | Set up scenario, warm up 60 frames, sample `--frames` wall-clock frame costs and engine monitors. |
| `world --mode MODE` | Existing fast/engine/visual/performance/full gate registry, using shared SDK execution and results. |
| `assets SUBCOMMAND -- ARGS` | Existing asset pipeline and its required adoption/build ordering. |
| `tool NAME -- ARGS` | Any existing top-level specialist Python/GDScript tool; `list` inventories them. GDScript capture tools need `--render`. This executes trusted repository tools, not user-supplied code. |
| `audit` | Doctor, build/import, full native + semantic validation, tool/game tests, existing engine/world gates, asset validation and new-game scenario. |
| `all` | Audit plus world screenshots and performance when `--render` is selected. Without a display it explicitly reports rendering gates as not requested. |
| `list` | Command and tool names; `list --json` is the full inventory with gate descriptions and modes. |

`world --mode engine` includes real capsule collision/traversal, navigation/route grades,
map placement, cell layout, world transitions, lifecycle teardown, the `story` gate, melee,
socket/animation and startup regressions. The `generators` gate (`regen.py --check` over the
generators no other gate covers) runs in every default mode, `fast` included. Fifteen pure-Python
check gates run up to `--parallel 4` at once when adjacent (`--parallel 1` is serial).
Their rules still live in the specialist tools. `world --mode full`
also runs the destructive-fixture negative battery, which intentionally refuses dirty
`data/` or `scenes/`; it is not silently bypassed by `all`. Run that battery on a clean
checkout after committing implementation work. It restores its fixtures in its existing
cleanup path. No SDK command auto-commits, changes branches or approves visual baselines.

## Results and process ownership

Every run creates `artifacts/headless/<UTC-time>-<unique-id>/`. `--artifacts DIR` changes
the **parent**, never overwrites an earlier run. A `.gdignore` prevents evidence import.
`summary.json` is atomically replaced. It contains:

```json
{
  "schema": 1,
  "run_id": "20260905T012000-example",
  "command": "scenario",
  "success": false,
  "exit_code": 5,
  "duration": 1.25,
  "godot_version": "4.7.1.stable.mono.official...",
  "diagnostics": [{"severity": "error", "code": "scenario.assertion", "path": "res://scenes/Main.tscn", "node": "/root/Main", "message": "Expected state"}],
  "assertions": [{"name": "Expected state", "success": false, "expected": true, "actual": false}],
  "metrics": {},
  "artifacts": ["summary.json"],
  "steps": []
}
```

Real results also include machine information, configuration, exact subprocess argument arrays,
per-step durations, raw OS exit codes and log paths. Native Godot diagnostics additionally
include origin file, line and function. Console parsing is a fallback for legacy
tools and build/import errors; the SDK's runtime gets native diagnostics through `OS.add_logger`.

### Output

The default output is the command's own product lines (a gate's facts, an arena row), the failures,
and one verdict line. A passing run is usually one line:

```text
PASS world/fast 33 steps (9 cached) 53s evidence=artifacts/headless/<run id>
FAIL <step> exit=N <first error, at most 200 characters> log=<path>
ERROR <code>: <message>
ASSERT <name>: expected <x>, actual <y>
```

There is no separate `Evidence:` line; the verdict carries `evidence=<dir>`. `summary.txt` holds the
same text beside `summary.json`.

| Flag | Prints |
| --- | --- |
| (none) | product lines, failures, the verdict |
| `-v`, `--verbose` | every step as it runs, and the lines a command only notes (console statements, the `SHOTS` line, perf comparison rows) |
| `--json` | one bounded compact object: `schema, run_id, command, success, exit_code, duration, steps` (a count), `cached, warnings, artifact_directory`, the failed steps, loose errors, failed assertions and `brief`. No `metrics`, `artifacts` or step list |
| `--json-full` | the whole result, as `summary.json` holds it |
| `--ndjson` | completed step records (`event: step`), then the compact object |

Anything that needs `metrics` reads `summary.json` in the run directory or passes `--json-full`.
Bulky runtime metrics (`dependency_graph`, `selected_paths`, `samples_ms`) are `*_count` in the
summary; the full data stays in the step's own result file.

Every run also writes `<run>/progress.json` (the step in flight, steps done and total, failures,
ETA seconds) and rewrites a partial `summary.json` after each step, so a run killed at its cap
still shows how far it got. Step output streams to `NN-name.stdout.log` and `.stderr.log` while the
step runs. ETAs come from `artifacts/.durations.json`, seeded from `embervale_sdk/costs.py`.

| Exit | Meaning |
| --- | --- |
| 0 | Every requested gate passed. |
| 1 | Validation/process error or a warning rejected by `--strict`. |
| 2 | Invalid request/configuration or missing prerequisite; also another run holding the heavy lock, and a game mode that refused its arguments. |
| 3 | Hard process timeout (including a hung descendant). |
| 4 | Abnormal process termination/crash; `job wait` when the job's supervisor vanished. |
| 5 | Scenario assertion, failed console statement, or performance budget/regression. |
| 75 | `job wait`: the job is still running after `--max`. Run it again. |
| 130 | Interrupted by user. |

Timeout/configuration/crash take precedence over assertion failures. A failed assertion takes
precedence over ordinary validation errors. Raw specialist exit codes remain in `steps`.
`--strict` promotes warnings to failure; runtime errors always fail, even after exit zero.
Screenshots, state/tree dumps, metrics, assertion details and Godot logs sit alongside the summary.
Nested asset/Blender/generator processes preserve separate stdout/stderr and command metadata
under `processes/`. Windows children belong to a kill-on-close Job Object; POSIX children use
a dedicated process group. Deadlines propagate to nested tools and cancellation cleans children.
No shell interpolation is used to invoke tools. A timeout is the hung-process detector;
quiet computation is not guessed to be a hang from an idle stdout stream.

Automation uses a per-run `user/` directory for **game saves and settings**, through
`UserDataPaths`. Normal play retains `user://`. Engine-level user data (for example shader
caches and engine logs outside the explicit log path) still follows Godot's own configuration.
New-game scenarios create only the isolated `automation` slot. This is a local finite batch
protocol, not a remotely exposed debugger or a daemon attached to arbitrary running games.

### Stale-build guard

Nothing that launches the game recompiles C#, so the SDK checks first. `Embervale.dll` is stale
when it is missing, older than `Embervale.csproj` or any `src/**/*.cs` or `addons/**/*.cs`, or was
built without tooling (no `ShotHarness` type in the assembly, which is what a world bake leaves
behind, however new the file is) (`embervale_sdk/freshness.py`). Before an engine launch the SDK then runs
`dotnet build Embervale.sln --nologo` as a step named `auto-build` and records
`configuration.auto_build`. A failed build raises diagnostic `build.stale`, exits 1 and does not
launch the engine. `--no-build` opts out.

| Launch | Guarded |
| --- | --- |
| Any command through `Run.godot` (`validate`, `gate`, `run`, `console`, `shots`, `perf-report`, scenarios, ...) | yes |
| `world` gates that launch the engine | yes |
| `tool NAME` and `assets ...` whose script calls `discover_godot` or `require_godot` | yes, unless an argument is `--check`, `--list`, `--help`, `-h` or `status` |
| `test --engine-tests` | builds once up front |
| `python tools/negative_tests.py` | rebuilds itself; `--no-build` opts out |
| A tool that launches the engine without those two helpers | no |
| A raw `godot ...` command line | no rebuild. The game itself logs one `STALE_BINARY ...` warning; `--strict-build` turns that into exit 1 before any mode runs |

The in-game check (`BuildFreshness`) runs only on the engine binary in a tooling build, and not for
`--script` GDScript probes.

### One engine run at a time

`%TEMP%/embervale-heavy.lock` is shared by every checkout and worktree (`EMBERVALE_HEAVY_LOCK`
overrides the path). `Run.godot`, engine gates, the `negative` gate, engine-launching tools and
every job take it. A second foreground engine command exits 2 and names the holder;
`--wait-lock SEC` waits instead. Nested SDK calls inherit it, and a dead owner's lock is taken over.

### Gate cache

In `world` and `verify`, a gate that passed on exactly these inputs is recorded as `cached` and not
run. The store is `artifacts/.gate-cache.json`; the key is the git tree ids, the content hash of
every changed or untracked file, the command, the seed and the frames. `.github` and `.claude`
never count. Never cached: a failure, `visuals`, `performance`, `environment-route`, any
report-only gate, any `--no-build` run. `--no-cache` skips the lookup. The tree is read once per
run, so a file edited mid-run can be stored under the old key.

### Background jobs

For anything longer than a foreground shell allows (the engine suite, a bake).

```text
python tools/embervale.py job start world --mode engine     # prints the id and the estimate
python tools/embervale.py job start tool world_bake --timeout 1200 -- --bake
python tools/embervale.py job status [ID]
python tools/embervale.py job wait [ID] --max 540            # exit = the job's; 75 if still running
python tools/embervale.py job tail [ID] [--lines 20] [--step]
python tools/embervale.py job list
python tools/embervale.py job cancel [ID]
```

Everything after `start` belongs to the job, so flags for `job` itself go before `start`. A
non-SDK command line runs as given. `ID` is any unique start or end of a job id; without it the
newest job is meant. Files are under `artifacts/jobs/<id>/` (`job.json`, `state.json`,
`progress.json`, `output.log`, `supervisor.log`, `supervisor.pid`). Status is one line:

```text
QUEUED <id> 0m12s behind pid 1234 (<what>): world --mode engine
RUNNING <id> 1m00s step 31/58 traversal 20s fails=1 eta=3m10s: world --mode engine
DONE PASS <id> exit=0 6m02s evidence=artifacts/headless/<run id>: world --mode engine
CANCELLED <id> after 2m10s: ...
DEAD <id> supervisor gone, last step traversal; see output.log: ...
```

Jobs queue on the heavy lock (up to 6 hours) and are capped at 4 hours.

### Log triage

```text
python tools/embervale.py logs [FILE|RUN_DIR] [--top 30] [--errors-only] [--raw] [--from-line N] [--json]
```

The default source is the newest run. Lines that differ only in ids, numbers or vectors are one
group: `E x58 L1203 <first raw message>` (`W` for warnings), with marker rows for `EMBERVALE_RESULT`
and `STALE_BINARY` lines, then `LOGS errors=2(3) warnings=1(2) noise=2 lines=13 source=...`. Exit 1
when a non-noise error exists, 0 otherwise, 2 with no log. The noise patterns are
`tools/headless/known_noise.json`; `--raw` shows them.

## Changed-only iteration

### `verify`: the gates for what changed

```text
python tools/embervale.py verify --plan          # the selection and its estimated cost; runs nothing
python tools/embervale.py verify                 # run it
python tools/embervale.py verify --base origin/main
python tools/embervale.py verify --budget 60     # skip any single step estimated above 60 s (reported)
```

Changed, staged and untracked files are mapped to gates through `RULES` in
`embervale_sdk/commands/verify.py`, and run in registry order. A gate's own script selects that
gate without a rule, and per-region gates narrow to the regions touched. Extra steps: `docs-lines`
(the 2,000-character rule, for any `*.md`), `tool-tests`, `assets-validate`. It never selects
`visuals`, `performance`, `environment-route` or `negative`; an `ADVISE` line names them when a
change calls for one. A path no rule knows is `UNMAPPED`: a warning, so `--strict` fails on it, and
the cue to add a rule. `--plan` creates no run directory and prints:

```text
PLAN 8 steps ~1m47s for 2 changed file(s): build 20s, tests 19s, ...
SKIP over --budget 60s: <steps>
ADVISE <text>
UNMAPPED 1: <paths>
NOTE longer than a foreground window: run it as `job start verify`
```

### A fast content loop

`validate --changed-only` below narrows the native resource scan, but the semantic
`ContentValidator` still runs whole. To run part of the content gate, filter its arms:

```text
python tools/embervale.py gate validate -- --list
python tools/embervale.py gate validate -- --only=items,crafting
```

A filtered run is marked `partial` and is not the gate (*Headless modes* below).

### `validate --changed-only`

```powershell
python tools/embervale.py validate --changed-only --json
python tools/embervale.py validate --changed-only --base origin/main
```

Selection includes staged, unstaged, deleted and untracked files. `--base` adds changes since
the merge base. Godot builds the dependency graph and expands the reverse transitive closure,
so changing a nested resource also selects its referring scenes. `.import` sidecars select their
source. C#, project settings, addon or tooling changes force a full scan. Global semantic database
validation always runs: a local edit can invalidate IDs and cross-database contracts.
The selected paths, graph, counts and fallback decision are recorded; no changed files is an
explicit zero-resource native scan, not a false full-project claim. Omit the flag for full coverage.
The native scan excludes editor addons, ignored directories, reports, artifacts and test fixtures;
the build handles C# and the existing specialist tests handle addon integrations.

## The game's command line

`godot` below is the console executable. Engine options go before `--`, the game's own after it.

### Session entry flags

All of these are read from the arguments **after `--`** only.

| Flag | Does |
| --- | --- |
| `--play` | Continues the newest save past the menu. With no save a bare `--play` logs an Info line naming `--new-game` and stays on the title; with a harness flag the run ends at once instead (below). |
| `--slot=<name>` | The save to continue (takes precedence over `EMBERVALE_SLOT`). A name that matches no save loads nothing and fails the run: `--slot=nope names no save (saves: keep)`, exit 1 (engine-run 2026-10-07). Only the legacy `EMBERVALE_SLOT` variable still falls back to the newest save with a warning. With `--new-game`, the slot to create. |
| `--new-game` | Tooling builds. Starts a real New Game (the prologue plays) in `--slot` or `automation`, and attaches the requested harnesses to it. **Refused unless `EMBERVALE_USER_DIR` is an absolute path**: a `Log.Error`, and the game stays on the title. The slot is 1 to 64 characters of letters, digits, `_` and `-`. Every SDK run qualifies, because the SDK sets the variable to the run's own `user/` directory. |
| `--quit-after=<seconds>` | Tooling builds. A real-time limit from shell ready (it runs through pause and ignores time scale). Exits 0, or 1 when there was an invariant violation or the requested session never started; an unparseable value exits 1 at once. Not the engine's own `--quit-after <frames>`, which goes before `--`. |
| a harness flag | Every flag in `SessionHarnesses.All` (`--hudshots`, `--shot`, `--exec`, `--perf-report`, ...) implies a session, as `--play` does. Several attach in table order. In a shipping build the table is empty and they do nothing. When the session never starts (a missing slot, no save, a refused new game, a failed load) the process ends with an `EMBERVALE_RESULT` line, gate `exec` for a script run and `session` otherwise, fact `session_started:false`, exit 1, or 2 when refused. |
| `--exec-allow-real-save` | Tooling builds. `--exec`, `--exec-file` and `--repro` change the session they run in, so they are **refused (exit 2, a result line, nothing written) unless `EMBERVALE_USER_DIR` is an absolute path** (the refusal was engine-run 2026-10-07 for `--exec`: the real saves folder was unchanged afterwards). This flag runs them on the real folder instead, with autosaves off and `save` refused for any slot but `console` (not engine-run). |

By hand, a fresh session therefore needs an isolated user directory:

```powershell
$env:EMBERVALE_USER_DIR = "$PWD\artifacts\scratch-user"
godot --headless --fixed-fps 60 --path . -- --new-game --exec "pos; get state" --quit-after=120
```

Flags that work on either side of `--`: the headless mode flags, `--strict-build`, `--report`.

| Flag | Does |
| --- | --- |
| `--strict-build` | Tooling builds on the engine binary: a stale `Embervale.dll` is a refusal before any mode or the shell runs: exit 2 and a result line whose gate is the requested mode, else `build` (exit 1 before 2026-10-07; unit-tested, not engine-run). Without it the same condition is one `STALE_BINARY` warning. |
| `--report=<path>` | Also writes the run's `EMBERVALE_RESULT` JSON to that file (directories are created; a write failure is a failure). |

Adding a harness is one line in `SessionHarnesses.All` (`src/Bootstrap/SessionHarnesses.cs`), inside
its `#if EMBERVALE_TOOLING` block. A tooling-only class whose file does not match
`src/Debugging/*Shots.cs` also needs a `Compile Remove` line in `Embervale.csproj` and its name in
`FORBIDDEN` in `tools/check_shipping_assembly.py`. `Capture: false` is for a harness that measures.

### The `EMBERVALE_RESULT` contract

Every headless mode, the console runner, the shot harnesses, `--perf-report`, `--repro` and
`--vfxperf` end with exactly one stdout line (`HeadlessReport`):

```text
EMBERVALE_RESULT {"schema":1,"gate":"state","ok":true,"exit_code":0,"elapsed_ms":412,"facts":{...},"failures":[],"warnings":[]}
```

`facts` is the mode's product (numbers, strings, arrays, nested objects). `failures` and `warnings`
are deduplicated strings. `exit_code` is the process exit code: 0 pass, 1 failed, 2 refused (a bad
flag or filter, a missing prerequisite). In a tooling build a stale binary adds the fact
`stale_binary: true` and a warning. `--report=<path>` writes the same object to a file, which is how
the SDK reads it. `elapsed_ms` is the process uptime when the line is written, engine boot included
(about 6 s for `--validate --only=items,crafting`, 41 s for a session refused after boot), not the
time the mode itself took.

Around it, a headless mode prints each failure once on stderr as `[ERROR] <gate>: <message>` and the
older `<gate>: PASS` or `<gate>: FAIL (n failure(s))` line. A passing run whose facts hold
`partial:true` (`--validate --only/--skip`, `--lifecycle --cycles=1`, `--story-only`,
`--story-mission`, `--no-boot-validate`) prints `<gate>: PARTIAL PASS (not the gate)` instead, still
exit 0 (engine-run for `--validate --only=items,crafting` only). Everything else is silent unless
`--verbose` (the log level is raised to warnings for the run, and warnings are not mirrored to the
engine log). `--world-bake` and `--worldmap` are the exceptions: they parse their own arguments,
stay as loud as they were and print no result line.

The Python and GDScript tools follow the same shape with their own names, each a name and one JSON
object as the last line: `PROBE` (`tools/probe_base.gd`), `REGEN` (`regen.py`), `WORLD_BAKE`
(`world_bake.py`), `WORLD_STATIC` (`check_world_static.py`), `MESHY` (`meshy_batch.py`), and with
`-v` the SDK's `SHOTS` line.

### Headless modes

```text
godot --headless --path . -- --gates          # every mode and its options, from the binary
python tools/embervale.py gate <validate|state|economy|worldgen|lifecycle|story|arena|gates> [-- options]
```

`gate` adds the mode flag, `--report=<run>/<mode>.json` and, for the session modes, `--seed`; the
arena also gets `--fixed-fps 60`. The facts are printed as one `<mode>: {json}` line (cut at 1,500
characters; the arena prints one line per matchup instead), warnings as `warning:` lines, and the
facts land in `metrics.<mode>` of `summary.json`. Everything after `--` reaches the game unchanged.

A command line is checked before anything runs (`HeadlessFlags`). With one mode requested, every
`--flag` after `--` must be that mode's, a common one or a session flag; two modes together are
refused; with no mode, a flag within two edits of a mode flag is refused when nothing on the line
starts a session. Each refusal is exit 2 and names the flag it probably meant.

Common flags: `--report`, `--verbose`, `--strict-build`, `--seed=N` (seeds the engine RNG and is
echoed as a fact), `--no-boot-validate` (skips the boot content pass; the result carries
`boot_validate:false, partial:true`), `--capture`, `--max-seconds=N` (a real-time limit for the
session modes, failing with the facts gathered so far), `--json` (the mode's fuller facts).

| Mode | Options | Result |
| --- | --- | --- |
| `--validate` | `--list`, `--only=<group\|arm\|group/Arm>,...`, `--skip=...`, `--slowest=N` (5) | The content gate: 57 arms in `ContentValidator.Arms`. Facts `arms_run, arms_total, issues, failed_arms, slowest`; a filtered run adds `partial, only, skip` and a warning. Issue lines are `[ERROR] validate: [group/Arm] <text>`. An unknown term exits 2 with the group list. Exit 0 or 1 |
| `--state` | `--ids=<kind>` (`--ids=list` names the kinds), `--match=<substring>`, `--get=<id>`, `--count` | The census: `regions, cells, items, shops, services, contracts, dialogues, quests, map_locations, region_ids`; `--json` adds `region_detail`. `--ids` gives `kind, match, count, ids`; `--get` gives `kind, id, path, properties`. An unknown kind or id exits 2. Otherwise exit 0 |
| `--economy` | `--top=N` (5) | `routes, paying, shops, best_margin, top`; `--json` gives `rows` as objects. Exit 0 |
| `--worldgen` | `--region=<id>` | `summary`, one string per region; `--json` adds `detail`. An unknown region exits 2 |
| `--lifecycle` | `--cycles=N` (3) | New Game, save, destroy, load round trips on isolated saves. Facts `cycles, baseline_services, baseline_subscriptions, baseline_orphans, worst_teardown_delta, orphans, invariant_violations`. Fewer than 3 cycles warns that it is not the gate. Exit 0 or 1 |
| `--save-reload` | | The quick-load and pause-menu reload audit. Exits 2 without an absolute `EMBERVALE_USER_DIR` |
| `--story` | `--story-only=A\|B\|C`, `--story-list`, `--story-mission=<n\|questId\|a..b>` | Below |
| `--arena=<roster>` | Below | Below. Tooling builds |
| `--world-bake`, `--worldmap` | their own | Run through `tools/world_bake.py`; no result line |
| `--gates` | | Facts `modes` (flag, help, options) and `common` |

`--validate --only=locale` is not a full key audit: arms in other groups resolve display keys too.
A new validator check is one row in `ContentValidator.Arms` (name, group, graph flag, method).

**`--story`** plays all 30 missions from New Game to credits through the real systems: run A
(Dawnfire), run B (Lord of Embers) and run C (legacy-save fixtures), on saves kept off the player's
own (`%TEMP%/embervale-story/<pid>` when no user directory is given). Facts `seconds, quests_played, runs, slowest, notes`; `--json` adds
`timeline`. `--story-only` plays some runs and is marked as not the gate. `--story-list` prints
`missions` and `start_points`. `--story-mission` starts a New Game, loads the fixture with the
latest frontier before the first mission asked for, and plays to the end of the range; it enters
from a legacy-shaped save, is marked `partial`, and never replaces the full gate. It proves wiring,
not that a fight can be won. It is also the `story` world gate (modes `engine` and `full`).

### The arena

```text
python tools/embervale.py gate arena -- --arena=enemy.goblin --trials=5 --level=5
python tools/embervale.py gate arena -- --arena=enemy.goblin*3 --policy=guard
python tools/embervale.py gate arena --timeout 590 -- --arena=all
godot --headless --fixed-fps 60 --path . -- --arena --list
```

A bot plays the player against a roster (`HeadlessArena`, `ArenaRunner`, `ArenaMath`; tooling
builds). A New Game is taken through the prologue, enemies come from the template registry as the
console `spawn` does, and the bot presses the real `move_forward`, `attack` and `block` actions, so
stamina, combos, lock-on and the damage pipeline are the game's own. Launch it with `--fixed-fps 60`
before `--` (the SDK does).

| Option | Default | Meaning |
| --- | --- | --- |
| `--arena=<roster>` | | Comma list of template ids, each fought alone; `id*3` for three at once; an `encounter.*` id; `all` (every archetype but the bosses); `bosses` |
| `--trials=N` | 3; 1 for `all` and `bosses` | Fights per matchup |
| `--seed=N` | 1 | Trial k is seeded N+k |
| `--level=N`, `--weapon=<itemId>`, `--equip=id,id` | | The player's build |
| `--setup="console line; ..."` | | Console lines run before the fights |
| `--policy=` | `aggressive` | `aggressive`, `guard` or `passive` |
| `--count=N` | 1 | Enemies per fight (1 to 12) |
| `--distance=M` | 6 | Starting distance |
| `--max-fight-s=S` | 90 | Game seconds before a fight is a timeout |
| `--speed=K` | 4 | Time scale and physics ticks multiplied together (1 to 16) |
| `--budget-s=S` | 480 | Real seconds for a sweep; when it runs out the result is `partial` with `remaining` as a ready roster string |

The fact `matchups` has one row per enemy: `enemy, trials, wins, losses, timeouts, win_rate,
ttk_s{p50,min,max}, dealt, taken, dps, enemy_dps, hp_left_min, swings, hits, enemy_attacks,
enemy_hits, blocked, staggers, parries, flags`; `--json` adds each trial. `ttk_s` is null without a
win. The SDK prints one line per matchup. The run fails (exit 1) when an enemy neither dealt nor
took damage, a spawn produced nothing, or the player has no weapon. Balance numbers never fail it:
they compare builds with each other and are not a difficulty verdict. The bot only closes and
swings; there is no caster or ranged policy.

### Console scripts

```text
python tools/embervale.py console "tp out; pos; assert player.safe eq false"
python tools/embervale.py console --file script.txt --render
python tools/embervale.py console "pos" --fixture <slot directory holding save.json>
python tools/embervale.py console --reference text|md|json
godot --headless --fixed-fps 60 --path . -- --new-game --exec "seed 7; tp out; spawn enemy.goblin 3; wait 2; assert enemies.count ge 3"
godot --headless --path . -- --console-help[=md|json]
```

`--exec "a; b"` takes a script inline and `--exec-file=<path>` from a file (`ConsoleScript`, tooling
builds). Both need an absolute `EMBERVALE_USER_DIR` or `--exec-allow-real-save` (*Session entry
flags*); the SDK `console` command always isolates. Statements split on `;` and newlines, `#` starts a comment, double quotes group a token. A
statement is anything the `F1` console accepts, or a runner verb:

| Verb | Does |
| --- | --- |
| `wait <seconds>` | Lets game time pass (scaled by `timescale`) |
| `frames <n>` | Lets n process frames pass |
| `wait-until <key> <op> <value> [seconds=10]` | Polls a `get` key each frame; fails after that many real seconds |
| `assert <key> <op> <value>` | Fails unless a `get` key compares true. Ops: `eq ne gt ge lt le contains` |
| `expect <text>` | Fails unless the most recent reply (from a command, `assert`, `wait-until` or `shot`; `wait`, `frames` and `input` reply nothing) contains the text |
| `shot <name>` | Writes the viewport to `<output>/<name>.png`. Needs a window |
| `input <action> [frames=2]` | Presses an InputMap action, holds it, releases it |
| `quit` | Stops the script here |

The whole script is checked before the first statement runs. One statement runs per frame at most,
and before each the runner waits for a usable world (a session with a player, not loading, ground
streamed under the player), which is what lets `tp`, `region goto`, `travel goto` and `load` be
followed by another statement. Flags: `--exec-timeout=<real seconds>` (300), `--exec-stop-on-fail`,
`--exec-verbose` (keeps Info logs on and prints every statement).

Output goes to `$EMBERVALE_ARTIFACTS/console/`, else `user://console/`: `result.ndjson` with one
line per statement, `{"i","cmd","ok","frame","out","data"?}`, then `{"event":"result",...}`; dumps
too long to reply inline; `shot` PNGs. Stdout gets one `[CON] FAIL <i> <cmd> -> <first line>` per
failed statement and the `EMBERVALE_RESULT` line (gate `exec`; facts `source, steps, ran, failed,
first_failure, frames, invariants, orphans, timescale, output, results`, plus `stopped_early` and,
in a new game, `narration_skipped`). `ran` counts the statements that completed. Exit 0 when every
statement passed and no invariant was violated, 1 otherwise; `result.ndjson` is flushed per
statement.

The SDK `console` command writes the script to `console-script.txt`, starts an isolated
`--new-game` (or copies `--fixture` into the run and continues it), and passes `--exec-file`,
`--report` and a `--quit-after` backstop. Its own flags: `--file`, `--fixture`, `--stop-on-fail`,
`--exec-timeout` (240), `--quiet`, `--game-log`, `--reference`. Each failed statement is a failed
assertion in `summary.json` and prints as `FAIL 3 <cmd> -> <reply>` plus `ERROR process.console:
statement 3 ...`; the SDK run exits 5 (the game process itself exits 1);
`console.incomplete` means the runner never reached its result line. The per-statement lines
(`ok 3 pos -> <first line>`) print with `-v` and on a failed run; otherwise read `metrics.console.steps` in
`summary.json` or `<run>/console/result.ndjson`.

The reference is generated from the binary: `--console-help` prints the 71 commands, the runner
verbs and the `get` keys with no session. The command groups:

| Group | Commands |
| --- | --- |
| Movement and spawning | `pos`, `tp <x> <z> [clearance]\|cell <cellId>\|loc <locationId>\|region <regionId>\|out [metres]`, `face`, `spawn <templateId> [n] [ahead <m>\|at <dx> <dz>]`, `spawn list [prefix]`, `region`, `travel` |
| Combat | `god [on\|off]`, `hurt <amount> [type]`, `enemies [radius]`, `killall [radius]`, `heal`, `status` |
| World | `time [<hour>\|+<hours>\|day <n>]`, `timescale`, `weather [list\|<id>\|stop]`, `event [list\|<id>\|stop]`, `seed`, `shock`, `worldgen`, `worldcells` |
| Player state | `give`, `take`, `inv`, `xp`, `sp`, `perk`, `learn`, `unlearn`, `spells`, `mana`, `school`, `rep`, `corruption`, `race`, `look`, `background`, `mount` |
| Story and quests | `quest <list\|status\|start\|advance\|complete\|reset>`, `flag`, `story`, `guild`, `companion`, `tutorial`, `opening` |
| Save and settings | `save [slot]`, `load [slot]`, `savecheck`, `autosave`, `settings`, `locale`, `log` |
| Query | `get [<key>...]`, `dump <player\|enemies\|entity <runtimeId>\|world\|saveables> [fileName]`, `hud [on\|off]`, `stats`, `derived`, `economy`, `shop`, `service` |
| Checks | `invariants`, `invariant-test [message]` (raises one violation through the real `Invariant` API, so a script run exits 1 and the flight recorder dumps; not engine-run), `validate`, `validate-all`, `repro [name]` |

`get` keys: `state frame fps timescale invariants orphans nodes menu`, `region cells.active
cells.resident world.settled world.ready`, `time.hour time.day time.phase weather event`,
`enemies.count enemies.near`, `player.hp player.hp_max player.mana player.stamina player.alive
player.god player.level player.gold player.corruption player.mounted player.x player.y player.z
player.yaw player.cell player.safe`, and the families `flag.<id> quest.<id> item.<id> spell.<id>
rep.<id> stat.<StatType>`.

⚠️ Traps: `wait` is seconds and `frames` is frames. An item count's key is `item.<itemId>`, so gold
is `item.item.currency.gold`. `god` multiplies maximum health; hits still land. `timescale` changes
how many physics steps a frame takes. `tp` checks the cell, safe zones and water at the landing,
not slope or props. `seed` does not reach loot rolls.

A new command goes in a `DevCommands.<Group>.cs` partial; it fails with `console.Fail(message)` and
returns data with `console.Reply(text, data)`. A new `get` key goes in `QueryKeys` and `Query`
together.

### Running a game flag

```text
python tools/embervale.py run state
python tools/embervale.py run story
python tools/embervale.py run hudshots -- --quit-after=90
python tools/embervale.py run spellshots --env EMBERVALE_SPELLSHOTS_TIER=low --save real
```

`run NAME` launches `-- --NAME` with the stale-build rebuild, the heavy lock, an owned timeout, an
isolated save directory and a run directory. Headless names: `validate state economy worldgen
worldmap world-bake lifecycle save-reload story`. Harness names are read from
`SessionHarnesses.cs`, so a new table line works with no edit; `shellshots` is also known. A
session harness gets `--new-game` by default (`--save new`); `--save real` continues the real saves
(the harness can write to them); `--save DIR` copies a user directory (the folder holding `saves/`)
into the run. The mode's result becomes a `RESULT <gate> ok <facts>` line and `metrics.gate_result`;
a failure adds `GATE-FAIL` lines. For the render suites prefer `shots`, which adds the fixed step,
the manifest and the sheets.

## Scenario protocol

```json
{
  "scene": "res://scenes/Main.tscn",
  "steps": [
    {"op": "new_game"},
    {"op": "wait_until", "node": "/root/Main", "property": "AutomationPlaying", "comparison": "eq", "value": true, "frames": 1800},
    {"op": "send_action", "action": "interact", "frames": 2},
    {"op": "send_action", "action": "move_forward", "frames": 6},
    {"op": "dump_state"},
    {"op": "capture_screenshot", "name": "spawn"}
  ]
}
```

`scene` must be a project-local `.tscn`/`.scn`. Node addresses must be absolute `/root/...`
without traversal. Operations are validated before process launch, capped at 1,000 operations
and 100,000 frames per wait. Assertions are literal comparisons, never expressions.

| Operation | Fields / behavior |
| --- | --- |
| `load_scene` | `scene`; replace the loaded scene through Godot's `PackedScene` APIs. |
| `new_game` | Invoke the explicit ApplicationRoot adapter; real lifecycle and loading gate. |
| `scene_tree`, `dump_state` | Snapshot nodes, groups, properties, resources, autoloads and state. |
| `get_property` | `node`, `property`; read an existing property and write its JSON representation. |
| `set_property` | `node`, `property`, `value`; only position/global_position/rotation_degrees/velocity (three-number vectors), visible (bool), fov (1..179). |
| `call_method` | `node`, `method`; only zero-argument show/hide/make_current/reset_physics_interpolation, where the node supports it. |
| `send_action` | `action`, optional `strength` 0..1 and `frames`; press and release a registered InputMap action. |
| `console` | `line` (one line, 1 to 400 characters), optional `allow_failure`; one dev-console command through `DevConsole.ExecuteJson`. Writes `console-<frame>.json`; a reply that is not ok raises `scenario.console`. Needs a live session in a tooling build, not under `--capture`. |
| `wait_frames` | `frames`; advance the controlled 60 Hz simulation. |
| `assert`, `wait_until` | `node`, optional `name`, `property`, `comparison`, `value`; eq/ne/gt/ge/lt/le/exists; wait_until has a finite `frames` budget. |
| `capture_screenshot` | Safe filename `name`; capture after `frame_post_draw`, write metadata. |
| `collect_logs`, `collect_metrics` | Log index or native monitor/raw wall-clock frame sample evidence. |
| `stop` | Finish the scenario, collect evidence and terminate. |

Scripts, arbitrary methods, eval, file writes and OS commands are **not** available through the
runtime protocol. The `console` operation is the one wide door: any dev-console command is reachable
through it, with no allowlist, so gameplay state a plan needs (items, flags, time, a teleport) is a
console line rather than a new adapter. Do not expand the method allowlist to reflection or `call`. Mutations affect the live instance,
not authored scenes/resources. When an assertion or native runtime error fails a rendered run,
the driver automatically captures a failure screenshot plus state, assertions and logs.
A truly headless run cannot capture pixels; it still writes tree/state/log evidence. A crash or
hard hang may prevent final in-engine evidence; raw process logs and the parent summary survive.

## Screenshots and performance

```powershell
python tools/embervale.py screenshot --viewpoint title --resolution 960x540 --resolution 1280x720
python tools/embervale.py screenshot tools/headless/scenarios/new-game.json --camera /root/Main/SessionHost/Session/Player/Camera --position 0,2,3 --rotation 0,180,0
python tools/embervale.py screenshot --baseline path/to/approved.png --threshold 0.02
python tools/embervale.py perf tools/headless/scenarios/new-game.json --frames 300 --max-frame-ms 25
python tools/embervale.py perf tools/headless/scenarios/new-game.json --baseline path/to/previous.metrics.json --threshold 0.10
python tools/embervale.py world --mode visual
python tools/embervale.py world --mode engine --gate save-audit --gate world-audit --gate runtime-audit
python tools/embervale.py tool architecture_shots --render
```

Inspect the actual tree before supplying `--camera`; the example path is illustrative.
Named viewpoints live in `tools/headless/viewpoints.json`. A plan establishes repeatable scene
and gameplay state; `--seed`, `--frames`, `--resolution`, `--camera`, `--position` and `--rotation`
control its captures. Screenshot commands start a rendering game process, never a Godot editor.
Specialist capture harnesses retain their own default settling waits unless `--frames` is
explicitly provided; the generic runtime's 120-frame default does not replace those waits.
Linux CI needs a display such as xvfb and a working rendering driver. The dummy `--headless`
renderer cannot return pixels.

The existing world harness still streams real regions, derives ground-aware authored viewpoints,
sets day/dusk atmosphere, awaits the drawn-frame barrier and applies its existing structural
signature thresholds. Its new metadata and run output directory preserve this work. The
existing C# `ShotHarness` retains its settle/drive/hold/draw logic and uses the same run directory. It captures at 1280x720;
`EMBERVALE_RES=1920x1080` (older alias `EMBERVALE_SHOT_SIZE`) sets another window size and `EMBERVALE_SHOT_UISCALE=0.75` the content-scale factor (the
`UiScale` setting's own 0.75-1.5 range), which is what changes the logical layout size (unset, it is the saved `UiScale` of the machine, so pin it to compare runs): the project stretches `canvas_items`, so
1920x1080 at scale 1 lays out as 1280x720 and 1920x1080 at 0.75 as 1707x960.
Other asset capture harnesses retain their composition and honor the shared artifact destination.
The merged combat overhaul retired the separate arm assets and `player_asset_shots.gd`;
use `gameplay-capture.json` for the real camera modes. The canonical world gate registry
retains the merged action-clip, equipment socket, animation library/tree, grounding,
ranged, camera, view-switch, prepared-world bake and streaming stress checks, and since the
2026-10 camera pass a `facing` gate (`tools/facing_probe.gd`: the chest faces the way the body
is going and the hips stay inside the capsule at every gait).

### Shots

```text
python tools/embervale.py shots list
python tools/embervale.py shots <suite> [--list] [--only "a,b*"] [--film [FRAMESxSTRIDE]] [--save SLOT_DIR]
       [--resolution WxH] [--ui-scale F] [--diff-last | --baseline DIR] [--no-analyze] [--no-thumbs]
       [--input-route] [--movie] [--spell-filter ..] [--spell-view tp|fp|both] [--tier ..]
python tools/embervale.py shots shot [--spec FILE | --cell ID | --location ID | --at=x,z | --region ID]
       [--yaw --pitch --distance --height --fov --hour --weather --view --hud --show-player --settle --name]
python tools/embervale.py shots shot --ui <suite>/<shot>
python tools/embervale.py shots shot --spell <id> --phase windup|release|impact|linger|all
```

`shots` is the preferred way to run a C# render harness. Suites: `hudshots panelshots uishots
shrine-shots guild-shots enemy-shots combat-shots look-shots metashots tradeshots spellshots
camshots shellshots`, and `shot` for a one-off. It launches rendered with `--fixed-fps 60` and the
resolution through `Run.godot`, so it gets the stale-build guard, the isolated user directory and
the process-tree timeout (540 s unless `--timeout`). The session is a `--new-game`, or `--save`
copies a slot folder into the run and continues it. `GuildShots` and `ShrineShots` may not find the
surroundings they assume in a new game; use `--save` for those. `--vfxperf` is deliberately not a
suite here: a fixed step would falsify its frame times.

Every harness (the suites above and `--shellshots`) reads the same options after `--`:

| Option | Does |
| --- | --- |
| `--only=name,name` | `*` and `?` wildcards, case-insensitive, whole-name match; `EMBERVALE_SHOT_ONLY` is the fallback. Unselected shots are still driven in order but not captured. A pattern that matches nothing fails the run and names it. `zz-summary.png` is not written in a filtered run |
| `--list` | Prints the shot names as an `EMBERVALE_RESULT` line (gate `shots-list`) and exits without rendering; works with `--headless` |
| `--no-thumbs` | Skips the `<name>.thumb.jpg` otherwise written beside each PNG |
| `--shots-verbose` | Restores the per-shot log lines, which are off by default |

Each run writes into `$EMBERVALE_ARTIFACTS/<flag without dashes>/` (every SDK run), else the
harness's `user://` folder:

* `manifest.json`: `schema, suite, ok, seconds, resolution, ui_scale, only, registered, captured,
  failed[], flagged[], focus_lost[], failures[]`, and per shot `name, selected, ok, file, frame,
  thumb, film, problem, flags[], stats{mean,std,min,max,black_pct,magenta_pct,hash}`.
* one `EMBERVALE_RESULT` line, gate `shots`, facts `suite, registered, captured, failed, flagged,
  focus_lost, dir, manifest`.
* a sidecar `<name>.png.json` per shot with the camera, hour and weather.

Flags are notes, not failures: `flat`, `black`, `magenta` (missing-material pixels) and
`same_as:<previous shot>`. The SDK reads the manifest into `metrics.shots` of `summary.json`, writes
contact sheets to `<dir>/sheet.png` (or `sheet_NN.png`), and with `--diff-last` compares against the
previous run of the suite, found through `artifacts/headless/latest-shots-<suite>.json`
(`--baseline DIR` names a directory instead). With `-v` it prints the same object as one
`SHOTS {json}` line: `suite, ok, registered, captured, seconds, failed, flagged, focus_lost, films,
dir, manifest, sheets, baseline, changed, missing_baseline`. Diagnostics: `shots.failed`,
`shots.incomplete` (no manifest: no session, a crash or the timeout), `shots.analysis`.

**One-off `--shot`** (`OneShots`). By hand it is
`godot --path . --fixed-fps 60 --resolution 1280x720 -- --shot --new-game <flags>` with an isolated
user directory, or `--shot=<file.json or inline JSON>`.

| Kind | Flags |
| --- | --- |
| World view | Place: `--at=x,z` (or `x,y,z`), `--cell=<id>`, `--location=<map location id>`, `--region=<id>`; default the region spawn. Camera: `--yaw` (0 is north, 90 east), `--pitch` (negative looks down, default -10), `--distance` (12; 0 puts the eye on the place), `--height` (1.6), `--fov` (70), `--view=free\|fp\|tp`. Sky: `--hour`, `--weather=<id>`. Also `--hud`, `--show-player`, `--settle=<frames>` (45), `--name`. Output `one_shots/<name>.png` and its sidecar |
| UI state | `--ui=<suite>/<shot>[,shot]` runs that suite restricted to those shots; files land in the suite's folder |
| Spell phase | `--spell=<id> --phase=windup\|release\|impact\|linger\|all` (default `impact`), `--view=tp\|fp`; runs `SpellShots` on that one spell |

A JSON spec may hold many views (an object, an array or `{"shots":[...]}`), all taken in one
launch. Write `--at=-40,12` with `=`: argparse rejects a space before a negative pair. `--shot`
hides only the HUD layer, so tutorial, toast and subtitle layers can still appear.

**Filmstrip.** `--film[=FRAMESxSTRIDE]` (default 12x4; 2 to 48 frames, stride 1 to 120) applies to
`--spellshots`, `--camshots` and `--shot`. For every selected shot it keeps the last FRAMES drawn
frames, STRIDE apart, before the capture and writes them as one `<shot>.film.png` with a
`<shot>.film.json` beside it; the manifest entry gets a `film` field, and `film_short` when fewer
frames than asked were kept (only frames between the cast and the capture exist), which the SDK
`SHOTS` line shows in brackets after the film name (not engine-run). Spacing is exact only under
`--fixed-fps 60`. It is the way to read motion from stills; the UI harnesses have no film.
`shots <suite> --movie` also records the run with the engine's `--write-movie` (AVI).

**Image analysis.** `tools/shot_analyze.py` (Pillow) prints one JSON line per image and a summary
line; exit 0, 1 with `--strict` when something is flagged or changed, 2 on a usage error.

```text
python tools/shot_analyze.py stats DIR [--flagged-only] [--full] [--strict]
python tools/shot_analyze.py diff DIR --baseline DIR2 [--out DIR] [--row-normalize] [--threshold 12] [--strict]
python tools/shot_analyze.py sheet DIR OUT.png [--columns 4] [--thumb-width 320] [--per-sheet 16] [--only-flagged] [--glob "*.png"]
python tools/shot_analyze.py thumbs DIR [--width 480]
```

`stats` is compact: an unflagged image is one short line (`image`, `mean`) and a flagged one carries
every measurement; `--full` prints every measurement for every image. `sheet` outlines flagged
images in red with or without `--only-flagged`. `diff` counts a baseline image the run did not
produce as changed (`missing_current`). These three were not re-run after the fixes.

`diff` reports the mean error, the changed share and up to five boxes, and writes one before,
after, heatmap triptych per changed image to `DIR/diff/<name>.diff.png`. A baseline at another
resolution is resized, not refused. `tools/make_contact_sheet.py` keeps its command line and calls
the same code.

**World shots.** `tools/world_shots.gd` discovers the regions from `data/regions/*.tres` and takes
`--region=<id|all>[,..]`, `--cell=<id>[,..]`, `--pass=day|dusk`, `--view=<substring>[,..]`,
`--res=WxH`, `--list` and `--shots-verbose` after `--`. With no selection it runs only the regions
the baseline covers, as the `visuals` gate does. With a selection, frames with no baseline are
counted as `unbaselined` rather than failed, and `--update-world-baseline` merges. It ends with an
`EMBERVALE_RESULT` line (gate `world-shots`) and writes `manifest.json`. It has no real sky, clock
or weather; use `--shot` for those.

### Spell, camera and enemy harnesses

Three session harnesses came with the spell-effect work. By hand each continues the newest save
like `--play` (or takes `--new-game` with an isolated user directory); `shots spellshots` and
`shots camshots` start a new game for them. Each needs a real window (never `--headless`) and is
excluded from a shipping build.

| Flag | Class | Produces |
| --- | --- | --- |
| `--spellshots` | `SpellShots` | every spell cast through the real cast button at practice targets on a level strip found by raycast, at dusk: `<spell>_<tp\|fp\|tpday>_<windup\|release\|impact\|linger>.png`. Enemy-only spells are cast at the player by the creature that owns them. Three of the player's own spells (`flame_lance`, `blizzard`, `storm_conduit`: a charged bolt, a nova and a channel) are also cast AT the first-person player by a humanoid enemy, as `<spell>_efp_<windup\|release\|impact\|linger>.png` ("efp", enemy first person; part of the first-person set, so not in a `tp`-only run). Each shot logs the node, visible, emitter and light counts under `VfxRoot`, and the run fails when a spell with a recipe drew nothing |
| `--camshots` | `CamShots` | both views at idle, walk, jog, sprint (with the three frames before it), both strafes, backpedal, a charge, a channel, looking down and up, a diagonal run, looking down at FOV 110 and at a sprint, and two side views with the capsule drawn. Each shot logs camera, chest, hips and casting-hand measurements, and in third person `view_pitch` and `feet_down_frame` (0 top, 1 bottom; the hotbar starts at about 0.89), and the run fails on a body drawn back to front, an eye in the chest or a casting hand out of frame |
| `--vfxperf` | `VfxPerfScenario` | eight casters in a ring looping the eight heaviest spells: `vfxperf_<tier>.json` with baseline, whole-run, steady and first-pass frame times (p50, p95, worst), the first cast's frame, the worst frame per spell's first use, and peak effect nodes, emitters, particles, lights and draw calls. V-sync and the frame cap are off for the run. Exits 1 if no cast began or nothing drew. `--vfxperf=performance,medium,ultra` (or `EMBERVALE_VFXPERF_TIERS`) runs the tiers in one boot on the same staged ring and adds `vfxperf_summary.json` and an `EMBERVALE_RESULT` line (gate `vfxperf`); files land in `$EMBERVALE_ARTIFACTS/vfxperf/` or `user://vfx_perf` |

| Variable | Read by | Does |
| --- | --- | --- |
| `EMBERVALE_SPELLSHOTS_FILTER` | `--spellshots` | comma list of spell ids or school names; default all |
| `EMBERVALE_SPELLSHOTS_VIEW` | `--spellshots` | `tp`, `fp` or `both` |
| `EMBERVALE_SPELLSHOTS_TIER` | `--spellshots`, `--vfxperf` | `performance`, `low`, `medium`, `high` or `ultra`: the spell-effect tier for the run |
| `EMBERVALE_SPELLSHOTS_REDUCED` | `--spellshots`, `--vfxperf` | `1` turns Reduced Motion on |
| `EMBERVALE_SPELLSHOTS_HOUR` | `--spellshots` | the dusk hour, default 19.5 |
| `EMBERVALE_SPELLSHOTS_BACKDROP` | `--spellshots` | `0` removes the dark wall stood behind the targets |
| `EMBERVALE_VFXPERF_SECONDS` | `--vfxperf` | how long the casters loop, default 20 |
| `EMBERVALE_VFXPERF_VIEW` | `--vfxperf` | `wide` (default: a raised camera with the whole ring in view), `tp` or `fp` |

`--enemy-shots` (`EnemyShots`, output under `user://enemy_shots`) gained gait frames with the same
work. For each enemy in the run that is on the shared humanoid rig (soldier, bandit, syndicate enforcer, clan shaman,
barrow wight, hollow necromancer, iron king) it adds seven frames from the front three-quarter:
`<enemy>--gait-idle.png`, `<enemy>--gait-walk-1.png` to `-3` and `<enemy>--gait-run-1.png` to
`-3`, where `<enemy>` is the id without `enemy.` and with hyphens (`hollow-necromancer`) and the
three numbered frames are a third of a clip apart. A body with no walk clip is shot on its run, as
it walks in play. `EMBERVALE_ENEMY_SHOT_ID=enemy.<id>` still limits a run to one enemy.

The shared `EMBERVALE_RES`, `EMBERVALE_ARTIFACTS`, `EMBERVALE_SLOT`, `EMBERVALE_USER_DIR` and
`EMBERVALE_FRAMES` apply as for the UI harnesses (`UI_STYLE.md` §13.9). Settings are changed on the
live object and never saved, and autosaves are off for the run. Prove a harness on a small batch
before a full run, for example `EMBERVALE_SPELLSHOTS_FILTER=emberlash,sunfall,storm_conduit,wither`
with `EMBERVALE_SPELLSHOTS_VIEW=tp`.

⚠️ **Keep the window focused during `--spellshots` and `--camshots`.** They drive the real input
actions, and Godot releases every pressed action when the window loses focus: a held charge fires
early and a channel ends. A focus loss is now recorded per shot in the manifest's `focus_lost`, and
fails the run only when the shot depended on a held input. `--direct-input` makes `SpellShots` cast
on the component instead of pressing the button; the SDK `shots` command passes it by default for
`spellshots`, `camshots` and `shot` (`--input-route` restores the button). `CamShots`' charge and
channel poses still need the held button. A run's problems are repeated at its end under `zz-summary`, and the
exit code is 1 if there were any. Grep the log for `the cast button did not start` (the input
route failed and the direct fallback was used) and `ran without its condition being met` (a
staging wait timed out).

`tools/gen_spell_sfx.py` is plain Python (numpy, with ffmpeg on PATH), not a Godot tool: it writes
the 25 spell cues and `manifest.json` under `assets/audio/sfx/spell/`, `--check` compares PCM
hashes with the committed manifest, and `--keep-wav DIR` leaves the wavs for listening. Hashes are
reproducible on one numpy build and may differ in the last bit on another. After a rewrite, let
Godot import the `.ogg` files and commit the `.import` files with them.

Generic screenshot diffing compares equally-sized RGB images using normalized mean absolute
channel error, writes metadata and a highlighted diff on failure, and never updates the baseline.
World baseline updates retain the existing review guard. Fixed simulation steps/seeds improve
repeatability but do not promise pixel identity across GPUs, renderers, shader clocks, driver
versions or asynchronous world-streaming schedules. Compare captures on the same configuration.

### Performance

`perf` reports wall-clock frame wait samples, p50/p95/p99/max/mean, hitch counts, process/physics time
(medians over the window), node/orphan counts, static, video, texture and buffer memory, draw calls
and primitives, with `suite: "sdk-perf"`, `requested_frames` and `paced: false`. The raw samples are
in `<name>.samples.json`, no longer in the metrics. Render counters are not meaningful on the dummy
renderer. `--render` enables rendered sampling. Screenshot readback should not be included in
the measurement plan. Budgets are opt-in and should use comparable hardware/scene/renderer;
there is no invented universal frame-time baseline. The existing per-cell performance probe
remains available through `world --mode performance`.

**Session verdict.** `--perf-report` (`SessionPerfReport`, tooling builds) samples every frame of a
live session and ends it with one `EMBERVALE_RESULT` line, gate `perf-report`.

```text
python tools/embervale.py perf-report [--render] [--seconds 20] [--warmup 3] [--repro NAME] [--repeat N]
       [--tolerance 0.10] [--update-baseline] [-- extra game args]
godot --path . -- --new-game --perf-report=20 --report=<path>      # needs an absolute EMBERVALE_USER_DIR
godot --path . -- --play --perf-report --quit-after=20
```

`--perf-report=<s>` samples that long after `--warmup=<s>` (3). Exit 1 on an invariant violation, a
logged error or zero sampled frames. Facts:

| Group | Facts |
| --- | --- |
| Run | `suite, mode, args, headless, capture, adapter, resolution, seconds, warmup_seconds, frames` |
| Frame time | `frame_ms_p50/p95/p99/max/avg`, `hitches_gt33/gt50/gt100`, `worst_hitches` |
| Cost | `script_ms, physics_ms, draw_calls, primitives` (medians), `nodes, orphans, orphans_leaked` |
| Memory and GC | `static_mb, video_mb, texture_mb, buffer_mb, managed_mb, alloc_kb_per_s, gc_gen0/1/2` |
| World | `region, active_cells, resident_cells, world_seconds, over_budget_seconds, world_worst_second_ms, safe_zone, enemies` |
| Health | `integrity_ok, integrity_issues, violations, log_errors, log_warnings` |

The SDK command launches `--new-game --perf-report=<s>` in the run's isolated user directory, reads
the report and compares it with this machine's baseline, with a per-key median over `--repeat`.
Without `--render` the engine is headless: nothing is drawn, so frame time is script and physics
cost only, and it is baselined separately. The facts and the comparison are `metrics.perf_report`;
the comparison rows print with `-v`. `--repro NAME` runs `tools/repro/<name>.txt` before sampling.

**Baselines.** `tools/perf_compare.py` compares any perf JSON (an `EMBERVALE_RESULT` file, SDK
`metrics.json`, `vfxperf_<tier>.json`, the world perf report, the streaming stress report) with
`tests/performance_baselines/<suite>/<key>.<machine>.json`.

```text
python tools/perf_compare.py CUR.json [MORE.json] [--median]
python tools/perf_compare.py CUR.json --update
python tools/perf_compare.py NEW.json --baseline OLD.json [--tolerance 0.10] [--key K] [--require-baseline] [--force] [--json]
```

A value regresses when it is worse than the baseline by more than the tolerance and by more than an
absolute floor for its kind. Output is `REGRESS` and `IMPROVE` rows and one summary line. Exit 0
for no regression, no baseline or another machine's baseline; 5 on a regression; 2 on bad input, a
refused `--update` or a missing baseline under `--require-baseline`. No baselines are committed
yet. A run or a baseline with fewer than `--min-frames` sampled frames (default 60) is not judged:
the verdict reads `INCOMPARABLE: N frames`, and `--update` refuses to record it (unit-tested, not
engine-run). `tests/performance_baselines/world_performance.json` is the old history file and is no
longer read by any tool.

**Spell effects.** `python tools/embervale.py vfxperf [--tiers performance,medium,ultra]
[--cast-seconds 20] [--view wide|tp|fp] [--tolerance 0.10] [--update-baseline]` runs `--vfxperf` at
several tiers in one launch in a new game and compares each tier with its baseline.

**Probes.**

| Probe | Arguments after `--` |
| --- | --- |
| `tools/world_perf_probe.gd` | `--region <id>`, `--cell <id>`, `--top N`, `--json`, `--json-file PATH`. Frame time is a tick delta between frames, so numbers from before 2026-10-07 are not comparable |
| `tools/world_streaming_stress_probe.gd` | `--region`, `--cycles N`, `--max-activation-ms X`, `--max-orphan-growth N`, `--max-static-growth-mb X`, `--json-file PATH`. The growth limits are opt-in |
| `tools/cell_mesh_census.gd` | `--region a,b`, `--table`, `--json-file PATH`, `--baseline PATH`, `--tolerance 0.25`, `--update-baseline`, `--strict`. A report unless `--strict`; no baseline is committed |

**Analytics.** A debug build writes `session_<stamp>.jsonl` under
`<EMBERVALE_ARTIFACTS>/analytics/` when that variable is set, else under the user directory; on a
session's first invariant violation the flight recorder writes `flight_<stamp>.jsonl` beside it.
`python tools/analytics.py summary [PATHS | --dir D] [--latest | --last N] [--json]` reads them into
a few lines: sessions and play time, deaths, kills, damage, gold by source, quest times.

**The per-cell probe covers all six realms.** `tools/world_perf_probe.gd` parks a camera at eye
height in every cell of the Ember Crown, Frostfang Reach, the Ashen Wilds, Sunspire, the Pale
Concord and the Celestial Realm (145 cells) and reports draw calls, primitives and frame time per
cell, plus resident video memory per region split into `texture_memory_mb` and
`buffer_memory_mb` (the remainder is the renderer's own targets). It measures the static scene
only: no AI, combat or HUD load. It runs at the automation default tier (Medium), because
first-run detection is skipped under `EMBERVALE_USER_DIR`. The before-numbers of the 2026-10
performance pass are in `artifacts/perf-baseline/README.md` (local, not committed).

### World bake

```powershell
python tools/world_bake.py --bake                       # only the regions whose inputs changed
python tools/world_bake.py --bake --full                # every region
python tools/world_bake.py --bake --region ember_crown  # also force the named region; repeatable
python tools/world_bake.py --check                      # name every stale, missing or unexpected output
python tools/world_bake.py --plan [--json] [--resume]   # what --bake would do and why; no engine, no build
python tools/world_bake.py --status [--json]            # progress of the running or last bake
python tools/world_bake.py --bake --resume              # keep the regions an interrupted bake finished
python tools/world_bake.py --bake --max-minutes 90 --restore-tooling
```

`--plan` lists each stale region with its cell count, an estimate and the reason (own inputs
changed, shared inputs changed, outputs missing or not as manifested), and ends with
`WORLD_BAKE {"plan":true,"to_bake":[...],"artifacts":N,"eta_seconds":...}`. `--bake` prints only
the engine's error lines; the full log is `artifacts/world_bake/bake.log`, and progress is written
to `artifacts/world_bake/status.json` while the engine runs, read from the output files' mtimes.
Its last line is `WORLD_BAKE {state, exit_code, done, total, ...}`. `--status` prints one line such
as `bake running 87/151 region.ember_crown ember_crown_tarn_east elapsed 212s eta 156s` and exits 3
while running, 0 done, 1 failed, timed out or interrupted, 2 with nothing recorded.
`--max-minutes N` (default 90) stops the engine with exit 124 and state `timeout`; `--bake --resume`
then keeps each region the interrupted run finished, when its signature is unchanged and its
outputs still hash the same (`artifacts/world_bake/journal.json`). Resume is per region, not per
cell. ⚠️ A bake builds `Embervale.csproj` alone with `-p:EmbervaleTooling=false` (the test project cannot
compile against that assembly) and so leaves the Debug assembly without the capture harnesses; `--restore-tooling`
rebuilds the solution afterwards, otherwise run `dotnet build Embervale.sln`. For a long bake use
`python tools/embervale.py job start tool world_bake --timeout 1200 -- --bake`.

`--bake` is incremental. A region's signature covers the shared inputs (the world code in
`src/World/*.cs`, biomes, models, shaders) plus its own region resource and cell scenes, and a
region is rebaked when that signature changed or one of its outputs is missing or edited. Editing
one realm's spec rebakes one realm. ⚠️ A change to a shared input still rebakes all six, because
nothing can prove which cell uses which model; a pass that touches `src/World/` or a model
`.import` is a full bake whatever flag is passed. Never run two bakes at once on this machine.

## Architecture and maintenance

* `embervale.py` + `embervale_sdk/`: interface, plans, results, changed-file selection.
  `commands/` holds the subcommand registry; `compact.py` the output; `freshness.py`, `heavy.py`,
  `cache.py`, `costs.py`, `jobs.py`, `triage.py` and `testing.py` the guards and helpers above.
* `quality_common.py` + `process_tree.py`: one executable discovery/process/artifact layer.
* `headless/driver.gd`, `validate.gd`, `logger.gd`, `capture.gd`: native finite runtime protocol.
* `world_quality_check.py`: the existing gate registry and a compatibility CLI forwarding to
  the SDK. Its duplicate runner has been removed; old modes/region/list flags still work.
* `assets.py`: existing asset orchestration; retained adoption/build ordering, shared subprocess
  execution and artifact placement. Import failure now stops adoption before manifest advancement.
* Existing Blender, Meshy adoption, generators, probes and audit scripts remain specialist
  implementations. Their child process execution uses the shared layer. No remote Meshy or
  Blender MCP integration has been invented. The optional Godot MCP remains editor-bound;
  SDK headless imports skip booting its relay and avoid regenerating editor skills.
* CI calls these same commands and uploads `artifacts/`. The existing advisory policy for
  renderer-dependent validation is preserved. Shipping exclusion is still verified by the
  existing `check_shipping_assembly` gate; the export itself is outside the SDK (below).
* `embervale_sdk/contract.py` drops `[McpPlugin]` lines (the Godot-MCP addon logging its relay as
  down on every editor launch) from the diagnostics altogether; they are tooling state, not project
  errors or warnings.

### Adding a subcommand

A new subcommand is one module under `tools/embervale_sdk/commands/` and nothing else: it is found
by name, so there is no `COMMANDS` list or dispatch in `main()` to edit. (`cli.COMMANDS` is now
`tuple(REGISTRY)`; the original commands are still registered by `cli.py`.)

```python
# tools/embervale_sdk/commands/my_thing.py   ->   python tools/embervale.py my-thing --depth 2
HELP = "One line for `list` and --help."

def arguments(parser):            # optional: flags only this command accepts
    parser.add_argument("--depth", type=int, default=1)

def run(run, args, passthrough):  # run is the cli.Run for this invocation
    run.godot("my-thing", [], ["--state"])            # engine step: timeout, logs, stale build, heavy lock
    run.process("my-step", ["python", "tools/x.py"])  # any other process step
    run.result["metrics"]["my_thing"] = {"depth": args.depth}
    run.brief("my-thing: depth 2")                    # a product line, shown above the verdict
```

| Module attribute | Meaning |
| --- | --- |
| `run(run, args, passthrough)` | Required |
| `arguments(parser)` | The command's own flags. They go after the command name; one that collides with a shared flag fails at startup |
| `HELP` | One line (default: the first docstring line) |
| `NAME` | Default: the file name with `_` as `-` |
| `VERSION = False` | Skip the `godot --version` step |
| `PASSTHROUGH = True` | Accept arguments after `--` |
| `LIGHT = True` | Launches nothing and wants no run directory (`last`, `logs`, `job`, `clean`, `verify --plan`): `run` is called with `run=None`, prints its own output and returns its exit code. `cli.run_command(args, passthrough, entry, body)` gives such a command a real run when it needs one |

Inside `run`: `run.issue(code, message)` records a diagnostic, `run.brief(line)` adds a product line
to the compact output, `run.note(line)` prints only with `-v`, `run.guard_tool(script, args)`
applies the stale-build guard to a tool, and `raise ValueError` is a usage error (exit 2). A module
whose name starts with `_` is not a command. `commands/report.py` is the smallest worked example.

Run `python tools/embervale.py test` after changes to process/protocol code. The engine battery
and representative scenarios exercise the native half. Unit tests cover process cleanup, timeout
output, Unicode/path handling, fail-closed discovery, Git selection, result semantics and unsafe
operation rejection. No test duplicates game content rules in Python.


Native protocol tests: `python tools/embervale.py test --engine-tests --render` exercises
the real driver against an isolated fixture, including property/method round trips, scene reload,
missing scenes, assertion exit codes, screenshot baseline/diff and automatic failure captures.
`inspect --resource res://...` inspects a resource without starting the game; `inspect --node
/root/...` focuses a live tree dump. `--max-warnings N` fails on excessive warning occurrences.
Repeated diagnostics retain a `count` instead of duplicating hundreds of identical rows.
`report state|economy|worldgen|lifecycle` exposes the existing C# report/probe entry points; `gate`
and `run` are the fuller routes to the same modes.

`python tools/negative_tests.py [--only TEXT] [--list] [--restore] [--no-build]` journals the
original bytes of every file a case will mutate under `artifacts/negative-journal/` before mutating.
The next start restores them, `--restore` does only that, and a journal whose owner is still alive
is refused (exit 2). `python tools/godot_mcp_check.py [--probe] [--json]` is the MCP check `doctor`
reports as an info row.

## Content tools

No engine and no build; run them directly. Every `content.py` subcommand takes `--json`.

```text
python tools/content.py refs <id | res://path | model-stem> [--limit 25]
python tools/content.py refs --prefix item.material. | --unused [--kind items] | --dangling
python tools/content.py census
python tools/content.py diff <revA> [<revB>] [--kind K] [--fields] [--max-lines 60]
python tools/content.py balance enemies|loot|recipes|xp [--flagged] [--save a.json] [--baseline a.json --threshold 0.15]
python tools/check_world_static.py [--region R] [--only seams,layout] [--json] [-v]
```

| Command | Output |
| --- | --- |
| `refs` | `def` lines, then `use <kind> <path> <section.field>` lines, a `locale` line and a `summary` line, across every data folder, the scenes, `src/**/*.cs` and `tools/**/*.py`. Exit 1 when the id is neither defined nor used. The index is cached at `artifacts/content/index.json` |
| `census` | One line per data folder with file counts split into generated (the file carries a `Generated by tools/...` marker) and hand, plus cells per region, scenes and locale rows |
| `diff` | One line per fact: `+ id (kind)`, `- id`, `~ enemy.x (enemies) Health 80 -> 95; ...`, then a `summary` line and a JSON report under `artifacts/content/`. The default second revision is the worktree, untracked files included. `data/world_bake` is excluded |
| `balance` | Fixed columns and a `flags` column (`tanky`, `fragile`, `xp-outlier`, `drops-nothing`, `missing-item` and others). An estimate: no perks, hit zones, block, statuses or spells. Exits 2 when the lines it mirrors in `src/Combat/CombatMath.cs` change |
| `check_world_static.py` | Seams, layout and composition for all regions in one process; failures only, then `WORLD_STATIC {json}`; logs in `artifacts/world_static/` |

`tools/tres_reader.py` is the shared read-only `.tres`/`.tscn` reader behind `content.py`
(`tools/campaign/tres.py` keeps its own, because its patcher edits lines in place).

GDScript probes that extend `res://tools/probe_base.gd` share `--only`, `--region` and
`--result-file`, fail when no check ran, and end with `FAIL:` lines, a `PASS:` line and
`PROBE {"probe","ok","checks","failures","metrics","seconds"}`, also written to
`$EMBERVALE_ARTIFACTS/<probe>.result.json`. `magic_lifetime_probe.gd`, `region_transition_probe.gd`
and `cell_mesh_census.gd` use it; the other probes keep their own output.

## Content generators

Committed scripts that write authored content. They are pure Python (no Godot), idempotent, and
are run directly or through `python tools/embervale.py tool NAME`. Never hand-edit a file one of
them owns; edit its table and run it. `--check` writes nothing and exits 1 on drift.

| Generator | Source of truth | Writes | `--check` | World gate |
| --- | --- | --- | --- | --- |
| `gen_regions.py` | `region_spec_*.py` | `data/regions/*.tres` | yes | `generation` |
| `gen_map_locations.py` | its own table | map locations and their placements | yes | |
| `gen_campaign.py` | `tools/campaign/specs/` | the 30 main-story quests and their dialogue (`gen_main_story.py` only forwards to it) | yes | |
| `gen_perks.py` | its own table | `data/perks`, the `progression:perks` locale block | yes | |
| `gen_appearance.py` | its own table | `data/appearance` | no | |
| `gen_items.py` | `tools/items/catalogue.py` + its own number and text tables | generated items, player weapons, item sets, unique effects, shop gear pools and stock rows, the `ics:content` locale block, the id lists in `ItemValidator.Content.cs` | yes | `item-generation` |
| `gen_recipes.py` | `catalogue.PLANNED_RECIPES` | generated recipes, recipe names, the trainer's taught list; also checks `GameIds.Recipes.Starting` and the commission margin (`--report` prints it) | yes | `recipe-generation` |
| `items/gen_loot.py` | `catalogue.py` + its own tables | generated affixes, tier pools, boss chest tables, `ChestLoot.tres`, the tier rows of the family tables, the Flamebearers' `LootTablePath` | yes | `loot-generation` |

**One runner checks or regenerates all of them.** `tools/regen.py` holds the registry in dependency
order (`--list` prints it, so the order is not restated here) and calls each generator's own entry
point:

```text
python tools/regen.py --check | --fix | --stale | --list [--only NAME ...] [--no-cache] [--json] [-v]
```

It prints the failing lines, then `REGEN {"ok":...,"drift":[...],"failed":[...],"errors":[...],
"skipped":[...],"cached":N,...}`; each generator's full output is `artifacts/regen/<name>.log`.
Exit 0 clean, 1 drift or a failed check, 2 a generator crashed, 3 the lock
(`artifacts/regen/.lock`, stale after 15 minutes) is held. `--fix` writes only what drifted, then
checks again. An entry whose needs are missing is `skipped` (`gen_spell_sfx` without numpy and
ffmpeg). `--check` is read-only: a generator with no `--check` of its own (`gen_appearance`,
`gen_player_mask`) runs as a copy inside a temp folder and the result is compared with the tree. A
generator named with `--only` whose dependency is missing is an error (exit 2), not a skip. Results are
cached in `artifacts/regen/cache.json`. The `generators` world gate runs `regen.py --check` over
the generators no other gate covers.

`tools/meshy_batch.py PLAN OUT` gained `--status`, `--dry-run`, `--max-minutes 9` (exit 3; the same
command resumes), `--ledger`, `--no-ledger`, `--sheet` and `--json`, and ends with `MESHY {json}`.
A real run appends finished items to `reports/3d/archive/meshy-migration/manifest.csv` with status
`generated`; adoption still changes that row by hand. `python tools/assets.py status --json` is the
asset state as one line. Exit code 3 means "still running, pending or lock held" in
`world_bake.py --status`, `meshy_batch.py` and `regen.py` alike.

The three catalogue generators share `tools/items/catalogue.py`, which decides what exists and is
safe to import (it reads and writes nothing); `python tools/items/catalogue.py` prints its census
and self-check. After a catalogue edit run `gen_items.py`, `gen_recipes.py` and `items/gen_loot.py`,
then each with `--check`. Their three gates run in the `fast`, `engine` and `full` world modes,
before the build. (`world_bake.py` is not in this table: it needs the engine and has its own rules.)
[`tools/items/README.md`](../tools/items/README.md) is the authoring guide and
[`RECIPES.md`](RECIPES.md) has the per-kind recipes. Scaffolds that print rather than own a file:
`gen_guild_dialogue.py`, `gen_merchant_dialogue.py`, `gen_cell_props.py`, `compose_building.py`,
`compose_district.py`, `new_cell_scenes.py`.

## Story gate and Windows export

The story gate is `python tools/embervale.py run story` (or `gate story`, or the `story` world
gate); the raw form below is what runs inside an export. The export itself is not an SDK command.
Run these one at a time.

```text
godot --headless --path . -- --story      # exit 0/1/2; HeadlessStory
dotnet build Embervale.csproj -c ExportRelease && python tools/check_shipping_assembly.py
python -c "from pathlib import Path; Path('build/windows').mkdir(parents=True, exist_ok=True)"
godot --headless --recovery-mode --path . --export-release "Windows Desktop" build/windows/Embervale.exe
build/windows/Embervale.exe --headless -- --story
```

`--story` plays all 30 missions from New Game to credits through the real systems, in runs A, B
and C (*Headless modes* above has its options and facts). It proves wiring, not that a fight can be
won. With no absolute `EMBERVALE_USER_DIR` it points its saves at a temp directory of its own and
removes it afterwards; an export build ignores the variable and relies on autosaves being off and
inline save writes for the run. The export uses the tracked `export_presets.cfg` preset "Windows Desktop" and the
4.7.1 .NET templates in `%APPDATA%\Godot\export_templates\4.7.1.stable.mono`; output under `build/` is
gitignored. Create the output directory in a fresh checkout before exporting. Editor recovery mode
keeps development plugins from connecting to a local MCP server during packaging; it does not disable
gameplay scripts in the exported build. Running `--story` inside the export is the smoke test: it fails on missing locale text,
missing manifests or anything else the export dropped. An export has no `--new-game`, `--quit-after`,
`--strict-build`, stale check, `--arena`, `--save-reload`, `--exec` or session harnesses; `--play`,
`--slot` and the other headless modes work.

## Create, customize and build

`author` constructs scenes and resources with Godot APIs. It packs, saves, reloads and
instantiates scenes before reporting success. By default it only stages `authored.tscn`
or `authored.tres` in the run artifacts. Review the changes recorded in the summary first.

```text
python tools/embervale.py author tools/headless/examples/workshop.json
python tools/embervale.py author tools/headless/examples/weapon.json
python tools/embervale.py author tools/headless/examples/workshop.json --write res://scenes/authored/Workshop.tscn
python tools/embervale.py screenshot --scene res://scenes/authored/Workshop.tscn --frames 60
python tools/embervale.py validate --changed-only
```

The workshop example creates a platform, collision, materials, a prop, spawn marker, camera
and lighting. These are SDK construction fixtures, not approved production art. For real
content, follow the existing asset adoption and world-authoring contracts.
The weapon example duplicates the existing C# WeaponResource template and customizes its
exported name, damage and stamina cost, without changing the template itself.

Plans contain `name`, optional project-local `source`, and an `operations` array. A source
PackedScene is instantiated; a source Resource is deeply duplicated. Operations can
`create_node`, `create_resource`, `instantiate_scene`, `customize`, `add_group`, or
`remove_node`. Created IDs are unique; `root` identifies the construction root. Targets
and parents are IDs or relative paths within that construction. Property references use
`{"resource":"created_id"}` or `{"path":"res://path/to/resource.tres"}`. Vectors/colors
use numeric arrays. Unknown properties and incompatible types fail explicitly.

For example, a plan with `source` set to an existing material resource and an operation
`{"op":"customize","target":"root","properties":{"roughness":0.8}}` produces a
customized resource copy. A scene plan can use `instantiate_scene` with `id`, `scene`
and `parent`, then customize its relative child paths. Use `inspect --resource` and
scene-tree dumps to discover valid resource properties and node paths first.

The scenario `build` operation accepts the same plan inline and attaches the constructed
subtree to an explicit parent in the current game scene:

```json
{"scene":"res://tests/headless/ProtocolFixture.tscn","steps":[
  {"op":"build","parent":"/root/Fixture","plan":{"name":"Placement","operations":[
    {"op":"create_node","id":"Spawn","type":"Marker3D","properties":{"position":[1,2,3]}}
  ]}},
  {"op":"assert","node":"/root/Fixture/Placement/Spawn","property":"position","comparison":"eq","value":[1,2,3]},
  {"op":"dump_state"}
]}
```

Live construction lasts for that run. Persisted authoring requires `--write`, restricted to
`scenes/*.tscn` and `data/*.tres`; generated `data/regions` is protected. Existing destinations
require `--overwrite`; the SDK retains a backup and refuses publication if the destination
changed while Godot was constructing the artifact. Publication uses an atomic file replacement.
There is no arbitrary code, script attachment, unrestricted method dispatch, or direct `.tscn`
text rewriting. `list --json` publishes the finite node/resource/operation allowlists.
The engine regression battery covers native construction, save/reload, live placement, and
rejection of unknown properties, alongside the Python plan validation tests.
