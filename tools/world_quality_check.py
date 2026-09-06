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
    expected_errors: tuple[str, ...] = ()  # exact negative-fixture messages; only on a passing gate


def gates(engine: str | None) -> list[Gate]:
    engine = engine or "godot"
    return [
        Gate("generation", "the committed .tres match their region specs",
             [sys.executable, "tools/gen_regions.py", "--check"]),
        Gate("world-bake", "prepared cells match every generation input and output hash",
             [sys.executable, "tools/world_bake.py", "--check"]),
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
              "res://tools/debug_pass_regressions.gd"], slow=True, timeout=1200,
             expected_errors=(r"Cannot open file 'res://scenes/regions/ember_crown/__does_not_exist.tscn'",
                              r"Failed loading resource: res://scenes/regions/ember_crown/__does_not_exist.tscn",
                              r"RegionStreamer: cell 'ember_crown.fen_edge' threaded load failed \(Failed\); giving up after 3 attempts")),
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
        Gate("ranged", "arrows leave on the release frame, hit once, and do not tunnel",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/ranged_probe.gd"], slow=True),
        Gate("camera", "walls retract the camera, people do not, and it restores",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/camera_probe.gd"], slow=True),
        Gate("traversal", "a real capsule walks every authored route in the real collision world",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/world_traversal_probe.gd"], slow=True, timeout=1800),
        Gate("streaming-stress", "rapid travel, boundary oscillation and unload soak keep collision/nav healthy",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/world_streaming_stress_probe.gd"], slow=True,
             modes=("engine", "full"), timeout=1800),
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


def main() -> int:
    """Compatibility entry point; all execution and results belong to the SDK."""
    from embervale_sdk.cli import main as sdk_main
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("region", nargs="?", choices=sorted(REGIONS))
    parser.add_argument("--mode", choices=("fast", "engine", "visual", "performance", "full"), default="full")
    parser.add_argument("--fast", action="store_true")
    parser.add_argument("--list", action="store_true")
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument("--artifacts")
    args = parser.parse_args()
    command = ["list"] if args.list else ["world", "--mode", "fast" if args.fast else args.mode]
    if args.region: command += ["--region", args.region]
    if args.artifacts: command += ["--artifacts", args.artifacts]
    return sdk_main(command)


if __name__ == "__main__":
    raise SystemExit(main())
