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
verdict (no invariant violation, no logged error, integrity clean) and "no perf regression". With
no baseline for this machine nothing is compared: the output says NO BASELINE and the run carries
a warning, so it does not read as a comparison that passed.
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


def baseline_key(facts, args):
    """One baseline per mode, region, scenario and display: a headless run is not comparable to a rendered one."""
    parts = [facts.get("mode", "session"), facts.get("region", ""), Path(args.repro).stem if args.repro else "",
             "headless" if facts.get("headless") else "render"]
    return "-".join(part for part in parts if part)


def file_verdict(run, verdict, code):
    """Files one perf_compare verdict in the run: its lines in the default output, then either the
    "no perf regression" assertion or, when nothing was compared, a warning that says so."""
    for line in perf_compare.render(verdict):
        run.brief(line)
    status = verdict["status"]
    if status == "refused":
        run.issue(f"{code}.baseline", f"baseline not updated: {verdict['reason']}")
    elif status in ("no-baseline", "incomparable"):
        why = "no baseline recorded for this machine" if status == "no-baseline" else verdict.get("reason", status)
        run.issue(f"{code}.not_compared", f"{verdict['key']}: nothing was compared ({why}); "
                  "this is not a regression verdict. Record a baseline with --update-baseline", "warning")
    elif status in ("regress", "ok"):
        run.result["assertions"].append(dict(name=f"no perf regression ({verdict['key']})",
                                             success=status == "ok", expected=[],
                                             actual=[e["key"] for e in verdict["regress"]]))


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
        run.result["assertions"].append(dict(name=f"session verdict {index}", success=bool(document.get("ok")),
                                             expected=[], actual=document.get("failures", [])))
        run.result["metrics"].setdefault("perf_report", {})[f"run_{index}"] = document.get("facts", {})
    if not reports:
        return
    facts = json.loads(reports[0].read_text(encoding="utf-8")).get("facts", {})
    verdict = perf_compare.evaluate(reports, baseline=args.baseline, update=args.update_baseline,
                                    tolerance=args.tolerance, key=baseline_key(facts, args), median=True)[0]
    run.result["metrics"]["perf_report"]["compare"] = verdict
    file_verdict(run, verdict, "perf_report")
