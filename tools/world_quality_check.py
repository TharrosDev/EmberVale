#!/usr/bin/env python3
"""One command that says whether a region is structurally healthy.

    python tools/world_quality_check.py                 # every region, every gate
    python tools/world_quality_check.py ember_crown     # one region
    python tools/world_quality_check.py --fast          # skip the in-engine probes (~15 s vs ~4 min)
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

EXIT CODES: 0 all gates passed · 1 at least one gate failed · 2 the harness could not run a gate
(a missing Godot binary, usually) — which is deliberately NOT the same as a failure, because
"could not check" and "checked and it is broken" must never look alike in CI.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

REGIONS = {
    "ember_crown": "data/regions/EmberCrown.tres",
    "frostfang_reach": "data/regions/FrostfangReach.tres",
}

# The console build prints to stdout; the plain .exe detaches and you lose the log you ran it for.
GODOT_ENV = "EMBERVALE_GODOT"
GODOT_FALLBACK = (
    r"C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64"
    r"\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
)


@dataclass
class Gate:
    name: str
    what: str
    command: list[str]
    slow: bool = False          # needs the engine and takes minutes
    per_region: bool = False    # @REGION@ in the command is replaced with the region's .tres


def godot() -> str | None:
    for candidate in (os.environ.get(GODOT_ENV), GODOT_FALLBACK, shutil.which("godot")):
        if candidate and Path(candidate).exists():
            return candidate
    return None


def gates(engine: str | None) -> list[Gate]:
    engine = engine or "godot"
    return [
        Gate("generation", "the committed .tres match their region specs",
             [sys.executable, "tools/gen_regions.py", "--check"]),
        Gate("build", "the C# compiles",
             ["dotnet", "build", "Embervale.sln", "-v", "q", "--nologo"]),
        Gate("tests", "the pure-logic suite",
             ["dotnet", "test", "tests/Embervale.Tests", "-v", "q", "--nologo"]),
        Gate("content", "references, well-formedness, reachability, route grades, off-route traps",
             [engine, "--headless", "--path", ".", "--", "--validate"]),
        Gate("negative", "the content rules still FAIL when deliberately broken",
             [sys.executable, "tools/negative_tests.py"]),
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
        Gate("traversal", "a real capsule walks every authored route in the real collision world",
             [engine, "--headless", "--path", ".", "--script",
              "res://tools/world_traversal_probe.gd"], slow=True),
        Gate("visuals", "the approach shots render and match the approved baseline",
             [engine, "--path", ".", "--script", "res://tools/world_shots.gd"], slow=True),
        # ⚠️ A REPORT, NOT A GATE — it exits 0 whatever it measures, and warns in its own output.
        # A frame-time threshold that fails a build is a threshold that fails on whichever machine
        # is busiest, and the first thing anyone does with a flaky gate is stop reading it.
        Gate("performance", "draw calls, primitives, frame time and video memory, per cell",
             [engine, "--path", ".", "--script", "res://tools/world_perf_probe.gd"], slow=True),
    ]


def run(gate: Gate, region_path: str | None, verbose: bool) -> tuple[bool, str, float]:
    command = [region_path if part == "@REGION@" else part for part in gate.command]
    started = time.monotonic()
    try:
        result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True,
                                encoding="utf-8", errors="replace")
    except FileNotFoundError as missing:
        return False, f"could not run: {missing}", time.monotonic() - started
    elapsed = time.monotonic() - started

    if result.returncode == 0:
        return True, "", elapsed

    # ⚠️ The tail, not the head. Every one of these tools prints its loading chatter first and its
    # verdict last, so a head-truncated failure report is a list of databases that loaded fine.
    output = (result.stdout or "") + (result.stderr or "")
    lines = [line for line in output.splitlines() if line.strip()]
    tail = lines if verbose else lines[-12:]
    return False, "\n".join("      " + line for line in tail), elapsed


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("region", nargs="?", choices=sorted(REGIONS), default=None,
                        help="limit the per-region gates; the global ones always run")
    parser.add_argument("--fast", action="store_true", help="skip the in-engine probes")
    parser.add_argument("--list", action="store_true", help="print the gates and exit")
    parser.add_argument("--verbose", action="store_true", help="print a failing gate's whole output")
    args = parser.parse_args()

    engine = godot()
    plan = gates(engine)

    if args.list:
        for gate in plan:
            mark = "slow" if gate.slow else "fast"
            print(f"  {gate.name:<10} [{mark}] {gate.what}")
        return 0

    if engine is None and not args.fast:
        print(f"world_quality_check: no Godot binary. Set {GODOT_ENV} or pass --fast.",
              file=sys.stderr)
        return 2

    targets = [args.region] if args.region else sorted(REGIONS)
    failures: list[str] = []
    skipped = 0

    print(f"world quality check - {', '.join(targets)}"
          f"{'  (fast: engine gates skipped)' if args.fast else ''}")
    print("-" * 78)

    for gate in plan:
        if gate.slow and args.fast:
            print(f"  {gate.name:<10} SKIP   {gate.what}")
            skipped += 1
            continue

        runs = [(region, str(ROOT / REGIONS[region])) for region in targets] if gate.per_region \
            else [(None, None)]
        for region, path in runs:
            label = gate.name if region is None else f"{gate.name}:{region.split('_')[0]}"
            ok, detail, elapsed = run(gate, path, args.verbose)
            print(f"  {label:<22} {'PASS' if ok else 'FAIL'}  {elapsed:5.1f}s  {gate.what}")
            if not ok:
                failures.append(label)
                if detail:
                    print(detail)

    print("-" * 78)
    if failures:
        print(f"FAILED: {', '.join(failures)}")
        return 1
    print(f"all gates passed{f' ({skipped} skipped)' if skipped else ''}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
