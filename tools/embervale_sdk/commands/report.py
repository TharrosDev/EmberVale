"""Existing state/economy/worldgen/lifecycle reports.

The game prints one compact `EMBERVALE_RESULT {json}` line by default; its facts become a `RESULT`
line here and `metrics.<target>` in summary.json. Pass the game's own options after `--`
(`report state -- --verbose` restores the prose, which lands in the step's stdout log).
"""
from ..contract import parse_result, result_brief

PASSTHROUGH = True
REPORTS = {"state", "economy", "worldgen", "lifecycle"}


def run(run, args, passthrough):
    if args.target not in REPORTS:
        raise ValueError("report requires state, economy, worldgen or lifecycle")
    result = run.godot(args.target, [], ["--" + args.target, *passthrough])
    report = parse_result(result.stdout)
    if report:
        run.result["metrics"][args.target] = report.get("facts", {})
        run.brief(result_brief(report))
    elif result.returncode == 0:
        run.brief(f"no {args.target} result line (a binary older than the quiet gates?): read "
                  f"{run.result['steps'][-1]['stdout']} in the run directory")
