"""Spell-effect cost at several effect tiers in ONE launch, each compared with this machine's baseline.

    python tools/embervale.py vfxperf                                  performance, medium and ultra, 20 s each
    python tools/embervale.py vfxperf --tiers ultra --cast-seconds 10
    python tools/embervale.py vfxperf --update-baseline

It launches `-- --new-game --vfxperf=<tiers>` with a window (the scenario refuses a headless display),
then runs tools/perf_compare.py on each `vfxperf_<tier>.json` the game wrote under the run's
artifacts. One assertion per tier: "no perf regression"; a tier with no baseline is a warning
and a NO BASELINE line instead, never a pass.
"""
from __future__ import annotations

import perf_compare

from .perf_report import file_verdict

HELP = "Spell-effect frame cost at several tiers in one launch, with a baseline verdict per tier."
TIERS = ("performance", "low", "medium", "high", "ultra")


def arguments(parser):
    parser.add_argument("--tiers", default="performance,medium,ultra", help="comma-separated effect tiers, run in this order")
    parser.add_argument("--cast-seconds", type=float, default=20.0, help="seconds of casting per tier (default 20)")
    parser.add_argument("--view", choices=("wide", "tp", "fp"), default="wide")
    parser.add_argument("--tolerance", type=float, default=0.10, help="relative change that counts as a regression")
    parser.add_argument("--update-baseline", action="store_true", help="record each tier as this machine's baseline")


def parse_tiers(text):
    tiers = [tier.strip().lower() for tier in text.split(",") if tier.strip()]
    unknown = [tier for tier in tiers if tier not in TIERS]
    if not tiers or unknown or len(set(tiers)) != len(tiers):
        raise ValueError(f"--tiers takes distinct tiers from {'|'.join(TIERS)}; got '{text}'")
    return tiers


def safety_seconds(tiers, cast_seconds):
    """--quit-after for the launch: far past a healthy run, so a session that never starts or a ring
    that never casts ends with exit 1 instead of sitting at the deadline of the whole command."""
    return int(len(tiers) * (cast_seconds + 15) + 120)


def run(run, args, passthrough):
    tiers = parse_tiers(args.tiers)
    if not 2 <= args.cast_seconds <= 600:
        raise ValueError("--cast-seconds must be 2..600")
    run.env.update(EMBERVALE_VFXPERF_SECONDS=f"{args.cast_seconds:g}", EMBERVALE_VFXPERF_VIEW=args.view)
    run.godot("vfxperf", [], ["--new-game", f"--vfxperf={','.join(tiers)}",
                              f"--report={run.artifacts / 'vfxperf.json'}",
                              f"--quit-after={safety_seconds(tiers, args.cast_seconds)}"], render=True)
    files = [run.artifacts / "vfxperf" / f"vfxperf_{tier}.json" for tier in tiers]
    missing = [path.name for path in files if not path.is_file()]
    if missing:
        run.issue("vfxperf.missing", f"the game did not write {', '.join(missing)}; see the Godot log")
    present = [path for path in files if path.is_file()]
    if not present:
        return
    verdicts = perf_compare.evaluate(present, update=args.update_baseline, tolerance=args.tolerance)
    run.result["metrics"]["vfxperf"] = verdicts
    for verdict in verdicts:
        file_verdict(run, verdict, "vfxperf")
