#!/usr/bin/env python3
"""One command that says whether a region is structurally healthy.

    python tools/world_quality_check.py --mode full     # every region, every gate/report
    python tools/world_quality_check.py ember_crown     # one region
    python tools/world_quality_check.py --mode fast     # engine/rendering-free deterministic gates
    python tools/world_quality_check.py --list          # what it runs, without running anything

WHY THIS EXISTS
---------------
The gates were all here already and there were eleven of them, spread across two languages, three
invocation styles and a documentation page. A future session adding a region had to know that
`gen_regions.py --check` comes before `dotnet build`, that `--validate` needs the `--` separator and
the console exe, that `check_region_seams.py` takes the generated `.tres` rather than the spec, and
that `world_traversal_probe.gd` is the one that takes four minutes. The predictable outcome of an
eleven-step manual checklist is that people run the first four.

⚠️ IT ORCHESTRATES; IT DOES NOT VALIDATE. Every rule below lives in the tool that owns it. Adding a
check here that is not implemented somewhere else is how two validators start disagreeing, and the
one nobody runs is always the correct one.

MODES: fast · engine · visual · performance · full. EXIT CODES: 0 all requested gates passed ·
1 at least one gate failed · 2 the harness could not run a requested gate
(a missing Godot binary, usually) — which is deliberately NOT the same as a failure, because
"could not check" and "checked and it is broken" must never look alike in CI.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import sys
from dataclasses import dataclass
from pathlib import Path

from quality_common import command_text, discover_godot, machine_fingerprint, run_process, write_json

ROOT = Path(__file__).resolve().parent.parent

REGIONS = {
    "ember_crown": "data/regions/EmberCrown.tres",
    "frostfang_reach": "data/regions/FrostfangReach.tres",
}

@dataclass
class Gate:
    name: str
    what: str
    command: list[str]
    slow: bool = False          # needs the engine and takes minutes
    per_region: bool = False    # @REGION@ in the command is replaced with the region's .tres
    modes: tuple[str, ...] = ("fast", "engine", "full")
    timeout: int = 600
    report_only: bool = False


def gates(engine: str | None) -> list[Gate]:
    engine = engine or "godot"
    return [
        Gate("generation", "the committed .tres match their region specs",
             [sys.executable, "tools/gen_regions.py", "--check"]),
        Gate("build", "the C# compiles",
             ["dotnet", "build", "Embervale.sln", "-v", "q", "--nologo"]),
        Gate("tests", "the pure-logic suite",
             ["dotnet", "test", "tests/Embervale.Tests", "-v", "q", "--nologo"]),
        Gate("architecture", "building prefabs, collision modes, materials and live callers agree",
             [sys.executable, "tools/check_architecture_kit.py"]),
        Gate("content", "references, well-formedness, reachability, route grades, off-route traps",
             [engine, "--headless", "--path", ".", "--", "--validate"],
             modes=("engine", "full"), timeout=900),
        Gate("lifecycle", "sessions and worlds are created and destroyed repeatedly without leaking",
             [engine, "--headless", "--path", ".", "--", "--lifecycle"],
             modes=("engine", "full"), timeout=1200),
        Gate("shipping-assembly", "the ExportRelease build carries no MCP addon or capture harness",
             [sys.executable, "tools/check_shipping_assembly.py"]),
        Gate("building-collision", "a real player capsule enters doors/breaches but not walls",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/building_collision_probe.gd"], modes=("engine", "full")),
        Gate("negative", "the content rules still FAIL when deliberately broken",
             [sys.executable, "tools/negative_tests.py"], modes=("full",), timeout=3600),
        # ⚠️ The starter is a real spec and this is what keeps it one. An example nobody runs is an
        # example that stopped working three months ago and nobody found out.
        Gate("template", "the new-region starter still builds and its lattice is sound",
             [sys.executable, "tools/region_spec_template.py"]),
        Gate("seams", "every road reaching a cell edge meets its opposite number",
             [sys.executable, "tools/check_region_seams.py", "@REGION@"], per_region=True),
        Gate("layout", "no structure overlaps or leaves its cell envelope",
             [sys.executable, "tools/check_cell_layout.py", "@REGION@"], per_region=True),
        Gate("map", "every marker sits on the thing it names, in the right region",
             [engine, "--headless", "--path", ".", "--script", "res://tools/map_probe.gd"], slow=True),
        Gate("stepup", "the player can still climb the realm's raised ground",
             [engine, "--headless", "--path", ".", "--script", "res://tools/stepup_probe.gd"], slow=True),
        Gate("meshes", "the rendered mesh census against the per-cell budgets",
             [engine, "--headless", "--path", ".", "--script", "res://tools/cell_mesh_census.gd"],
             slow=True),
        Gate("scenes", "every cell's authored nodes are visible, solid and correctly placed",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/cell_scene_audit.gd"], slow=True),
        Gate("regressions", "the 2026-08-30 debugging pass's defects stay fixed",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/debug_pass_regressions.gd"], slow=True, timeout=1200),
        Gate("transition", "a region swap leaves no orphaned, duplicated or missing cell",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/region_transition_probe.gd"], slow=True),
        Gate("melee", "a swing opens its hitbox inside its own active window, and hits once",
             [engine, "--headless", "--path", ".", "--script", "res://tools/melee_probe.gd"],
             slow=True),
        Gate("action-clip", "on a rigged body the clip IS the clock, warped or natural",
             [engine, "--headless", "--path", ".", "--script", "res://tools/action_clip_probe.gd"],
             slow=True),
        Gate("sockets", "every humanoid rig carries the equipment socket contract",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/equipment_socket_probe.gd"], slow=True),
        Gate("anim-library", "the shared library is whole, keeps its legs, and moves a real body",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/anim_library_probe.gd"], slow=True),
        Gate("anim-tree", "locomotion blends, the upper-body mask holds, the action clock is honest",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/locomotion_tree_probe.gd"], slow=True),
        Gate("view-switch", "first/third person swap keeps the action, combo and equipment",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/view_switch_probe.gd"], slow=True),
        Gate("grounding", "feet meet sloped ground and a warping attack cannot pass a wall",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/grounding_probe.gd"], slow=True),
        Gate("traversal", "a real capsule walks every authored route in the real collision world",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/world_traversal_probe.gd"], slow=True, timeout=1800),
        Gate("visuals", "the approach shots render and match the approved baseline",
             [engine, "--path", ".", "--resolution", "1280x720", "--script",
              "res://tools/world_shots.gd"], slow=True, modes=("visual", "full"), timeout=1800),
        # ⚠️ A REPORT, NOT A GATE — it exits 0 whatever it measures, and warns in its own output.
        # A frame-time threshold that fails a build is a threshold that fails on whichever machine
        # is busiest, and the first thing anyone does with a flaky gate is stop reading it.
        Gate("performance", "draw calls, primitives, frame time and video memory, per cell",
             [engine, "--path", ".", "--resolution", "1280x720", "--script",
              "res://tools/world_perf_probe.gd", "--", "--json-file", "@ARTIFACT@/performance.json"],
             slow=True, modes=("performance", "full"), timeout=1800, report_only=True),
    ]


def run(gate: Gate, region_path: str | None, artifacts: Path, label: str,
        verbose: bool) -> tuple[str, str, float, int, str]:
    command = [region_path if part == "@REGION@" else
               part.replace("@ARTIFACT@", str(artifacts)) for part in gate.command]
    result = run_process(command, timeout=gate.timeout, cwd=ROOT)
    log_path = artifacts / f"{label}.log"
    log_path.write_text(result.output, encoding="utf-8")
    if result.launch_error:
        status, detail = "BLOCKED", f"could not start: {result.launch_error}"
    elif result.timed_out:
        status, detail = "TIMEOUT", f"exceeded {gate.timeout}s; process tree terminated"
    elif result.returncode != 0:
        status, detail = "FAIL", f"exit code {result.returncode}"
    else:
        status, detail = ("REPORT" if gate.report_only else "PASS"), ""
    lines = [line for line in result.output.splitlines() if line.strip()]
    tail = lines if verbose else lines[-15:]
    output = "\n".join("      " + line for line in tail) if detail else ""
    return status, detail + ("\n" + output if output else ""), result.elapsed_seconds, \
        result.returncode, command_text(command)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("region", nargs="?", choices=sorted(REGIONS), default=None,
                        help="limit the per-region gates; the global ones always run")
    parser.add_argument("--mode", choices=("fast", "engine", "visual", "performance", "full"),
                        default="full", help="quality battery to run")
    parser.add_argument("--fast", action="store_true", help="compatibility alias for --mode fast")
    parser.add_argument("--list", action="store_true", help="print the gates and exit")
    parser.add_argument("--verbose", action="store_true", help="print a failing gate's whole output")
    parser.add_argument("--artifacts", type=Path,
                        help="artifact directory (default artifacts/quality/<UTC run id>)")
    args = parser.parse_args()

    mode = "fast" if args.fast else args.mode
    engine_path = discover_godot()
    engine = str(engine_path) if engine_path else None
    plan = gates(engine)

    if args.list:
        for gate in plan:
            listed_modes = tuple(mode for mode in gate.modes if not (mode == "fast" and gate.slow))
            print(f"  {gate.name:<12} [{','.join(listed_modes):<24}] "
                  f"{'report' if gate.report_only else 'gate'}  {gate.what}")
        return 0

    selected = [gate for gate in plan if mode in gate.modes and
                not (mode == "fast" and gate.slow)]
    if engine is None and any("godot" in gate.command[0].lower() for gate in selected):
        print("world_quality_check: requested mode needs Godot. Set EMBERVALE_GODOT or GODOT "
              "to the .NET console executable.",
              file=sys.stderr)
        return 2

    targets = [args.region] if args.region else sorted(REGIONS)
    run_id = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    artifacts = (args.artifacts or ROOT / "artifacts" / "quality" / run_id).resolve()
    artifacts.mkdir(parents=True, exist_ok=True)
    results: list[dict] = []
    failures: list[str] = []
    blocked: list[str] = []
    commands: list[str] = []

    print(f"Embervale quality - {mode} - {', '.join(targets)} -> {artifacts}")
    print("-" * 78)

    for gate in selected:
        runs = [(region, str(ROOT / REGIONS[region])) for region in targets] if gate.per_region \
            else [(None, None)]
        for region, path in runs:
            label = gate.name if region is None else f"{gate.name}:{region.split('_')[0]}"
            status, detail, elapsed, exit_code, reproduction = run(
                gate, path, artifacts, label.replace(":", "-"), args.verbose)
            commands.append(reproduction)
            print(f"  {label:<22} {status:<7} {elapsed:5.1f}s  {gate.what}")
            results.append({"name": label, "status": status, "what": gate.what,
                            "expected": "exit code 0", "actual": detail.splitlines()[0] if detail else "exit code 0",
                            "exit_code": exit_code, "elapsed_seconds": elapsed,
                            "reproduction": reproduction, "log": f"{label.replace(':', '-')}.log"})
            if status in ("FAIL", "TIMEOUT"):
                failures.append(label)
            elif status == "BLOCKED":
                blocked.append(label)
            if detail:
                print(f"      expected exit code 0; {detail}")
                print(f"      reproduce: {reproduction}")

    print("-" * 78)
    (artifacts / "commands.log").write_text("\n".join(commands) + "\n", encoding="utf-8")
    write_json(artifacts / "summary.json", {
        "schema": 1, "run_id": run_id, "mode": mode, "regions": targets,
        "status": "blocked" if blocked else "failed" if failures else "passed",
        "machine": machine_fingerprint(), "godot": engine,
        "evidence": {"logs": str(artifacts),
                     "world_current": str(ROOT / "tools/shots/world"),
                     "world_diffs": str(ROOT / "tools/shots/world_diffs"),
                     "visual_baseline": str(ROOT / "tests/visual_baselines/world_signatures.json"),
                     "performance": str(artifacts / "performance.json")},
        "results": results})
    print(f"summary: {artifacts / 'summary.json'}")
    if blocked:
        print(f"BLOCKED: {', '.join(blocked)}")
        return 2
    if failures:
        print(f"FAILED: {', '.join(failures)}")
        return 1
    print("all requested gates passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
