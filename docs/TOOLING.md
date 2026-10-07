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

On the maintainer machine Python is not on PATH. Codex's bundled executable is
`C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe`.
In PowerShell use `& '<absolute-python-path>' tools/embervale.py doctor`.
The SDK discovers the console Godot executable in PATH or the existing Downloads
installation. Explicit `--godot`, `EMBERVALE_GODOT`, then `GODOT` take precedence;
an invalid explicit path fails rather than silently running a different engine.
`EMBERVALE_BLENDER` overrides optional Blender discovery. `dotnet` and `git` must be on PATH.
Doctor validates the actual engine's 4.7 Mono version, .NET 8 SDK/runtime and project targets.
`tools/headless/config.json` holds the default per-process timeout (900 seconds).

## Commands

| Command | Coverage |
| --- | --- |
| `doctor` | Required versions/configuration and optional tool availability; does not start servers. |
| `import` | Godot editor importer, error detection even when Godot exits zero. `--reimport` removes only the regenerable `.godot/imported` cache before importing. |
| `build` | Entire solution, warnings-as-errors as configured by the project. Run import separately on a fresh checkout. |
| `validate` | Native recursive resource loading, dependency checks and detached scene instantiation, followed by the existing global `ContentValidator`. |
| `inspect` | Boot `--scene` or Main; serialize scene tree, groups, stored properties, resource paths, autoload configuration and state. |
| `smoke` | Boot for `--frames`, collect state/logs/metrics and detect runtime errors or abnormal exit. |
| `scenario PLAN.json` | Run the finite declarative operations below against a real game process. |
| `author PLAN.json` | Create/customize native scenes and resources, round-trip validation, optional protected publication. |
| `report NAME` | Existing state/economy/worldgen/lifecycle reports. |
| `test` | Tool unittest suite plus the existing C# xUnit suite; TRX in the run directory. |
| `screenshot [PLAN.json]` | Set up a scene/scenario and capture with metadata; repeat `--resolution` for a capture set. |
| `perf [PLAN.json]` | Set up scenario, warm up 60 frames, sample `--frames` wall-clock frame costs and engine monitors. |
| `world --mode MODE` | Existing fast/engine/visual/performance/full gate registry, using shared SDK execution and results. |
| `assets SUBCOMMAND -- ARGS` | Existing asset pipeline and its required adoption/build ordering. |
| `tool NAME -- ARGS` | Any existing top-level specialist Python/GDScript tool; `list` inventories them. GDScript capture tools need `--render`. This executes trusted repository tools, not user-supplied code. |
| `audit` | Doctor, build/import, full native + semantic validation, tool/game tests, existing engine/world gates, asset validation and new-game scenario. |
| `all` | Audit plus world screenshots and performance when `--render` is selected. Without a display it explicitly reports rendering gates as not requested. |
| `list` | Inventory of tools and gate descriptions/modes, also available as JSON. |

`world --mode engine` includes real capsule collision/traversal, navigation/route grades,
map placement, cell layout, world transitions, lifecycle teardown, melee, socket/animation
and startup regressions. Their rules still live in the specialist tools. `world --mode full`
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
include origin file, line and function. `--json` prints only the final JSON on stdout.
`--ndjson` streams completed step records (`event: step`) and a final `event: result`.
Human mode prints progress and diagnostic summaries. Console parsing is a fallback for legacy
tools and build/import errors; the SDK's runtime gets native diagnostics through `OS.add_logger`.

| Exit | Meaning |
| --- | --- |
| 0 | Every requested gate passed. |
| 1 | Validation/process error or a warning rejected by `--strict`. |
| 2 | Invalid request/configuration or missing prerequisite. |
| 3 | Hard process timeout (including a hung descendant). |
| 4 | Abnormal process termination/crash. |
| 5 | Scenario assertion or performance budget failed. |
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

## Changed-only iteration

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
| `wait_frames` | `frames`; advance the controlled 60 Hz simulation. |
| `assert`, `wait_until` | `node`, optional `name`, `property`, `comparison`, `value`; eq/ne/gt/ge/lt/le/exists; wait_until has a finite `frames` budget. |
| `capture_screenshot` | Safe filename `name`; capture after `frame_post_draw`, write metadata. |
| `collect_logs`, `collect_metrics` | Log index or native monitor/raw wall-clock frame sample evidence. |
| `stop` | Finish the scenario, collect evidence and terminate. |

Scripts, arbitrary methods, eval, file writes and OS commands are **not** available through the
runtime protocol. Extending supported gameplay state means adding a narrow reviewed adapter;
do not expand the method allowlist to reflection or `call`. Mutations affect the live instance,
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
`EMBERVALE_SHOT_SIZE=1920x1080` sets another window size and `EMBERVALE_SHOT_UISCALE=0.75` the content-scale factor (the
`UiScale` setting's own 0.75-1.5 range), which is what changes the logical layout size (unset, it is the saved `UiScale` of the machine, so pin it to compare runs): the project stretches `canvas_items`, so
1920x1080 at scale 1 lays out as 1280x720 and 1920x1080 at 0.75 as 1707x960.
Other asset capture harnesses retain their composition and honor the shared artifact destination.
The merged combat overhaul retired the separate arm assets and `player_asset_shots.gd`;
use `gameplay-capture.json` for the real camera modes. The canonical world gate registry
retains the merged action-clip, equipment socket, animation library/tree, grounding,
ranged, camera, view-switch, prepared-world bake and streaming stress checks, and since the
2026-10 camera pass a `facing` gate (`tools/facing_probe.gd`: the chest faces the way the body
is going and the hips stay inside the capsule at every gait).

Three session harnesses came with the spell-effect work. Each continues the newest save like
`--play`, needs a real window (never `--headless`) and is excluded from a shipping build.

| Flag | Class | Produces |
| --- | --- | --- |
| `--spellshots` | `SpellShots` | every spell cast through the real cast button at practice targets on a level strip found by raycast, at dusk: `<spell>_<tp\|fp\|tpday>_<windup\|release\|impact\|linger>.png`. Enemy-only spells are cast at the player by the creature that owns them. Each shot logs the node, visible, emitter and light counts under `VfxRoot`, and the run fails when a spell with a recipe drew nothing |
| `--camshots` | `CamShots` | both views at idle, walk, jog, sprint (with the three frames before it), both strafes, backpedal, a charge, a channel, looking down and up, a diagonal run, looking down at FOV 110 and at a sprint, and two side views with the capsule drawn. Each shot logs camera, chest, hips and casting-hand measurements, and the run fails on a body drawn back to front, an eye in the chest or a casting hand out of frame |
| `--vfxperf` | `VfxPerfScenario` | eight casters in a ring looping the eight heaviest spells: `vfxperf_<tier>.json` with baseline, whole-run, steady and first-pass frame times (p50, p95, worst), the first cast's frame, the worst frame per spell's first use, and peak effect nodes, emitters, particles, lights and draw calls. V-sync and the frame cap are off for the run. Exits 1 if no cast began or nothing drew |

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

The shared `EMBERVALE_RES`, `EMBERVALE_ARTIFACTS`, `EMBERVALE_SLOT`, `EMBERVALE_USER_DIR` and
`EMBERVALE_FRAMES` apply as for the UI harnesses (`UI_STYLE.md` §13.9). Settings are changed on the
live object and never saved, and autosaves are off for the run. Prove a harness on a small batch
before a full run, for example `EMBERVALE_SPELLSHOTS_FILTER=emberlash,sunfall,storm_conduit,wither`
with `EMBERVALE_SPELLSHOTS_VIEW=tp`.

⚠️ **Keep the window focused during `--spellshots` and `--camshots`.** They drive the real input
actions, and Godot releases every pressed action when the window loses focus: a held charge fires
early and a channel ends. A run's problems are repeated at its end under `zz-summary`, and the
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

Perf reports wall-clock frame wait samples, p95/max, process/physics time, node/orphan counts,
static memory, draw calls and primitives. Render counters are not meaningful on the dummy
renderer. `--render` enables rendered sampling. Screenshot readback should not be included in
the measurement plan. Budgets are opt-in and should use comparable hardware/scene/renderer;
there is no invented universal frame-time baseline. The existing per-cell performance probe
remains available through `world --mode performance`.

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
```

`--bake` is incremental. A region's signature covers the shared inputs (the world code in
`src/World/*.cs`, biomes, models, shaders) plus its own region resource and cell scenes, and a
region is rebaked when that signature changed or one of its outputs is missing or edited. Editing
one realm's spec rebakes one realm. ⚠️ A change to a shared input still rebakes all six, because
nothing can prove which cell uses which model; a pass that touches `src/World/` or a model
`.import` is a full bake whatever flag is passed. Never run two bakes at once on this machine.

## Architecture and maintenance

* `embervale.py` + `embervale_sdk/`: interface, plans, results, changed-file selection.
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
* `embervale_sdk/contract.py` filters `[McpPlugin]` lines (the Godot-MCP addon logging its relay as
  down on every editor launch) out of error diagnostics; they are tooling state, not project errors.

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
`report state|economy|worldgen|lifecycle` exposes the existing C# report/probe entry points.

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

Neither is an SDK command; run them through the console Godot directly, one at a time.

```text
godot --headless --path . -- --story      # exit 0/1; HeadlessStory
dotnet build Embervale.csproj -c ExportRelease && python tools/check_shipping_assembly.py
python -c "from pathlib import Path; Path('build/windows').mkdir(parents=True, exist_ok=True)"
godot --headless --recovery-mode --path . --export-release "Windows Desktop" build/windows/Embervale.exe
build/windows/Embervale.exe --headless -- --story
```

`--story` raises each act's trigger flag in a real session and checks the act chain, the hidden-realm
reveal, save/load, every Flamebearer template, both rival duels, every vision and ending card's locale
text, both ending skies (also after a reload) and the ending sequence. It proves wiring, not that a
fight can be won. The export uses the tracked `export_presets.cfg` preset "Windows Desktop" and the
4.7.1 .NET templates in `%APPDATA%\Godot\export_templates\4.7.1.stable.mono`; output under `build/` is
gitignored. Create the output directory in a fresh checkout before exporting. Editor recovery mode
keeps development plugins from connecting to a local MCP server during packaging; it does not disable
gameplay scripts in the exported build. Set `EMBERVALE_USER_DIR` to a disposable directory under
`artifacts/` for the story probes. Running `--story` inside the export is the smoke test: it fails on missing locale text,
missing manifests or anything else the export dropped.

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
