# Embervale verification matrix

`python tools/world_quality_check.py --mode <mode>` is the canonical entry point. It orchestrates
the existing specialist tools; validation rules remain in their owning C#, GDScript or Python tool.
Every run has hard process timeouts and writes `artifacts/quality/<UTC run>/summary.json`, per-gate
logs, and exact reproduction commands. Exit 0 means every requested gate actually ran and passed;
exit 1 means a check ran and failed; exit 2 means prerequisites prevented the requested work.

| Mode / tool | What it proves | Gate or report | Rendering | Artifacts |
| --- | --- | --- | --- | --- |
| `--mode fast` | generated source and prepared-world fingerprints, warning-free shipping build, pure xUnit logic, region template, seams and layout | gate, every change | no | quality summary/logs |
| `--mode engine` | fast gates plus `--validate`, live map/step/collision/scene/streaming/new-game/traversal/melee regressions and streaming stress | gate, every change | headless | quality summary/logs |
| `--mode visual` | deterministic 1280×720 world states and localized 32×18 visual comparison | gate, every change | yes | current PNGs, baseline JSON, failure heatmaps, logs |
| `--mode performance` | warmup + median per-cell draws, primitives, frame time, memory and historical reference | deterministic authored budgets gate in `--validate`; hardware timing is report-only | yes | `performance.json` and logs |
| `--mode full` | engine + visual + performance + exact negative mutation battery | gate except hardware timing; weekly/manual | yes | all of the above |
| `dotnet test tests/Embervale.Tests` | deterministic functions and rules that do not need a scene tree | gate | no | test runner output |
| `python tools/world_bake.py --check` | every prepared cell/region output exists, hashes exactly, and matches the complete source fingerprint | gate | no | console diagnostics |
| `debug_pass_regressions.gd` | real terrain below spawn/portals, streaming retry/failure, finite transforms, collision, runtime behaviour | gate | headless | runner log |
| `world_streaming_stress_probe.gd` | rapid traversal, distant cycling, boundary oscillation, collision/nav readiness and bounded residency | gate | headless | runner log |
| `tools/godot_mcp_check.py --probe` | local-only MCP config plus a real editor round trip before viewport/camera/isolated captures | prerequisite | editor | console diagnostics |
| HUD/panel/shrine/shell shot harnesses | named live gameplay/UI state exists and produces a nonblank 1280×720 PNG | capture fails on missing prerequisites; human visual review remains | yes | `user://...shots` |

## Classification

The xUnit project contains pure deterministic logic only. Anything that constructs a `Node`, reads
`.tres` through `GD.Load`, needs physics/navigation/rendering, or depends on a live scene belongs in
the existing in-engine regression/probe harness. Embervale does not use GUT: adopting a second test
framework would duplicate the already-established engine harness without improving what it proves.

Visual baselines may only be updated explicitly, after reviewing all current PNGs:

```text
godot --path . --resolution 1280x720 --script res://tools/world_shots.gd -- --update-world-baseline
```

Never use an update command as a way to make an unexplained diff green.
