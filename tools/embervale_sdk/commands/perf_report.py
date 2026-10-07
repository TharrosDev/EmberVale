"""Session verdict: play a real session for N seconds, then report frame times, memory, integrity and log counts.

    python tools/embervale.py perf-report --render                    20 s of a fresh game, with a window
    python tools/embervale.py perf-report --render --repro swarm      ... with six enemies spawned first
    python tools/embervale.py perf-report --render --update-baseline  record this machine's baseline
    python tools/embervale.py perf-report --render --repeat 3         three launches, per-key median compared
    python tools/embervale.py perf-report -- --exec=FILE              extra game arguments after --

It launches `-- --new-game --perf-report=<seconds>` in the run's isolated user directory (so there is
always a session to measure), reads the EMBERVALE_RESULT JSON the game writes, and compares it with
this machine's baseline through tools/perf_compare.py. Without --render the engine is headless:
nothing is drawn, so frame time is script and physics cost only and is baselined separately.

The result's metrics.perf_report holds the facts and the comparison. Assertions: the game's own
verdict (no invariant violation, no logged error, integrity clean) and "no perf regression".
"""
from __future__ import annotations

import json
from pathlib import Path

import perf_compare

HELP = "Play a session for N seconds; one verdict: frame percentiles, memory, integrity, log counts, baseline."
PASSTHROUGH = True


def arguments(parser):
    parser.add_argument("--seconds", type=float, default=20.0, help="seconds sampled after the warm-up (default 20)")
    parser.add_argument("--warmup", type=float, default=3.0, help="seconds skipped first (default 3)")
    parser.add_argument("--repro", help="repro script (tools/repro/<name>.txt or a path) run before sampling")
    parser.add_argument("--repeat", type=int, default=1, help="launches; the per-key median is what gets compared")
    parser.add_argument("--tolerance", type=float, default=0.10, help="relative change that counts as a regression")
    parser.add_argument("--update-baseline", action="store_true", help="record this run as this machine's baseline")


def game_arguments(args, report, passthrough=()):
    """The user arguments for one launch."""
    user = ["--new-game", f"--perf-report={args.seconds:g}", f"--warmup={args.warmup:g}", f"--report={report}"]
    if args.repro:
        user.append(f"--repro={args.repro}")
    return user + list(passthrough)


def summary_line(index, document):
    """One line a reader can judge the run from; the full facts are in metrics.perf_report."""
    f = document.get("facts", {})
    display = "headless" if f.get("headless") else f.get("resolution", "")
    line = (f"run {index}: {f.get('frames', 0)} frames in {f.get('seconds', 0)}s {display}"
            f" | frame ms p50 {f.get('frame_ms_p50')} p95 {f.get('frame_ms_p95')} p99 {f.get('frame_ms_p99')}"
            f" max {f.get('frame_ms_max')} | hitches >33/50/100: {f.get('hitches_gt33')}/{f.get('hitches_gt50')}"
            f"/{f.get('hitches_gt100')} | draws {f.get('draw_calls')} | static {f.get('static_mb')} MB"
            f" | enemies {f.get('enemies')}{' safe-zone' if f.get('safe_zone') else ''}"
            f" | violations {f.get('violations')} errors {f.get('log_errors')} warnings {f.get('log_warnings')}")
    return line


def run(run, args, passthrough):
    if not 1 <= args.repeat <= 9 or args.seconds <= 0 or args.warmup < 0:
        raise ValueError("perf-report needs --repeat 1..9, --seconds > 0 and --warmup >= 0")
    reports = []
    for index in range(args.repeat):
        report = run.artifacts / f"perf-report-{index}.json"
        run.godot(f"perf-report-{index}", [], game_arguments(args, report, passthrough), render=args.render)
        if not report.is_file():
            run.issue("perf_report.missing", "the game wrote no report: the session did not start or the process "
                      "ended early; see the Godot log", path=str(report))
            continue
        document = json.loads(report.read_text(encoding="utf-8"))
        reports.append(report)
        run.brief(summary_line(index, document))
        for hitch in document.get("facts", {}).get("worst_hitches", []):
            run.brief(f"  hitch {hitch}")
        for warning in document.get("warnings", []):
            run.brief(f"  warning: {warning}")
        run.result["assertions"].append(dict(name=f"session verdict {index}", success=bool(document.get("ok")),
                                             expected=[], actual=document.get("failures", [])))
        run.result["metrics"].setdefault("perf_report", {})[f"run_{index}"] = document.get("facts", {})
    if not reports:
        return
    # No key: perf_compare names the baseline from the facts (mode, region, repro plan), so
    # `python tools/perf_compare.py <report>` finds the same file. Headless and rendered runs are
    # kept apart by the machine id, which includes the adapter and resolution.
    verdict = perf_compare.evaluate(reports, baseline=args.baseline, update=args.update_baseline,
                                    tolerance=args.tolerance, median=True)[0]
    run.result["metrics"]["perf_report"]["compare"] = verdict
    for line in perf_compare.render(verdict):
        run.brief(line.replace("--update", "--update-baseline"))  # this command's spelling of the flag
    if verdict["status"] == "refused":
        run.issue("perf_report.baseline", f"baseline not updated: {verdict['reason']}")
    elif verdict["status"] in ("regress", "ok"):
        run.result["assertions"].append(dict(name="no perf regression", success=verdict["status"] == "ok",
                                             expected=[], actual=[e["key"] for e in verdict["regress"]]))
