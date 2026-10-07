"""Run one headless game mode and merge its EMBERVALE_RESULT into the SDK result.

    python tools/embervale.py gate validate -- --only=items,crafting
    python tools/embervale.py gate state -- --ids=quests --match=main
    python tools/embervale.py gate story -- --story-mission=27
    python tools/embervale.py gate arena -- --arena=enemy.goblin --trials=5 --level=5
    python tools/embervale.py gate arena -- --arena=all
    python tools/embervale.py gate gates            # every mode and its options

The target is the mode's name without dashes. Everything after `--` goes to the game unchanged, so a
mode's own options (and `--verbose`, `--json`) are written exactly as on the engine command line. The
mode's facts land in `metrics.<target>` of summary.json; its report is `<target>.json` in the run
directory. A mode that refuses its arguments (exit 2) is a configuration error here too.
"""
from __future__ import annotations

import json

HELP = "Run one headless game mode (validate, state, economy, worldgen, lifecycle, story, arena, gates)."
PASSTHROUGH = True

MODES = ("validate", "state", "economy", "worldgen", "lifecycle", "story", "arena", "gates")
# Modes that build a session: they take the SDK seed, so two runs of one command are comparable.
SESSION = ("lifecycle", "story", "arena")
# The arena measures game time, so its frames must not follow the wall clock.
ENGINE_ARGUMENTS = {"arena": ["--fixed-fps", "60"]}


def has_flag(arguments, flag):
    return any(a == flag or a.startswith(flag + "=") for a in arguments)


def user_arguments(target, passthrough, report_path, seed):
    """The game's user arguments for `target`: the mode flag (unless the caller already gave it, as
    `--arena=<roster>` must be), the caller's own arguments, the report path and the seed."""
    mode = "--" + target
    arguments = list(passthrough)
    if not has_flag(arguments, mode):
        arguments.insert(0, mode)
    if not has_flag(arguments, "--report"):
        arguments.append(f"--report={report_path}")
    if target in SESSION and not has_flag(arguments, "--seed"):
        arguments.append(f"--seed={seed}")
    return arguments


def merge_report(result, target, data):
    """Folds a mode's report into an SDK result dict. Failures are not copied: the game prints each
    one as an [ERROR] line, which the step scan already turned into a diagnostic. Returns the exit
    code the step should carry (2 for a refused run), or None to leave it alone."""
    result["metrics"][target] = data.get("facts", {})
    for warning in data.get("warnings", []):
        result["diagnostics"].append(dict(severity="warning", code=f"gate.{target}", path=None, node=None,
                                          message=warning))
    return 2 if data.get("exit_code") == 2 else None


BRIEF_LIMIT = 1500


def matchup_line(row):
    """One arena matchup as a line an agent can compare by eye."""
    ttk = row.get("ttk_s") or {}
    flags = ",".join(row.get("flags") or []) or "-"
    return (f"  {row.get('enemy')}: win {row.get('wins')}/{row.get('trials')} loss {row.get('losses')} "
            f"timeout {row.get('timeouts')} | ttk p50 {ttk.get('p50')}s | dealt {row.get('dealt')} taken {row.get('taken')} "
            f"| dps {row.get('dps')} vs {row.get('enemy_dps')} | hp_left_min {row.get('hp_left_min')} "
            f"| swings {row.get('swings')} hits {row.get('hits')} | enemy attacks {row.get('enemy_attacks')} "
            f"hits {row.get('enemy_hits')} blocked {row.get('blocked')} | flags {flags}")


def brief_lines(target, facts, report_name):
    """The mode's facts as the lines the compact output shows: the verdict alone would send every
    caller to the report file for the one thing they ran the mode for."""
    rows = facts.get("matchups")
    if target == "arena" and isinstance(rows, list):
        head = {k: v for k, v in facts.items() if k != "matchups"}
        return [f"{target}: " + json.dumps(head, ensure_ascii=False)] + [matchup_line(row) for row in rows]
    text = json.dumps(facts, ensure_ascii=False)
    if len(text) > BRIEF_LIMIT:
        text = text[:BRIEF_LIMIT] + f"... (cut; whole facts in {report_name})"
    return [f"{target}: {text}"]


def run(run, args, passthrough):
    target = args.target
    if target not in MODES:
        raise ValueError(f"gate requires one of: {', '.join(MODES)}")
    report = run.artifacts / f"{target}.json"
    run.godot(target, ENGINE_ARGUMENTS.get(target, []), user_arguments(target, passthrough, report, args.seed))
    if not report.is_file():
        if run.result["steps"][-1]["exit_code"] == 0:
            run.issue("gate.no_report", f"{target} exited 0 but wrote no report; is the binary older than the gate?")
        return
    code = merge_report(run.result, target, json.loads(report.read_text(encoding="utf-8")))
    for line in brief_lines(target, run.result["metrics"][target], report.name):
        run.brief(line)
    if code is not None:
        run.result["steps"][-1]["exit_code"] = code
