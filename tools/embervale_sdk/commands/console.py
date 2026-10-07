"""Run dev-console commands in an isolated game session and print one compact line per statement.

    python tools/embervale.py console "tp out; spawn enemy.goblin 3; wait 2; assert enemies.count ge 3"
    python tools/embervale.py console --file my_script.txt --render      # `shot` needs --render
    python tools/embervale.py console "pos; inv" --fixture path/to/saves/slot1
    python tools/embervale.py console --reference md                      # the command table, no session

The session is a fresh New Game unless --fixture names a save slot directory (one holding save.json),
which is copied into the run's own user directory and continued. Nothing touches the player's saves.
The script goes to the game's --exec-file runner (src/Debugging/ConsoleScript.cs); its statements are
the F1 console's commands plus wait / frames / wait-until / assert / expect / shot / input / quit.
Exit 0 when every statement passed, 5 when one failed, the usual SDK codes otherwise.
"""
from __future__ import annotations

import json
import re
import shutil
from pathlib import Path

HELP = "Run a dev-console script in an isolated new game (or a copied save slot); one line per statement."

VERSION = False  # one engine launch per run: the session itself
RESULT_PREFIX = "EMBERVALE_RESULT "
SLOT = re.compile(r"[A-Za-z0-9_-]{1,64}")
MAX_LINES = 38  # the compact output shows 40 brief lines


def arguments(parser):
    parser.add_argument("--file", type=Path, help="read the script from a file (statements split on ; and newlines, # comments)")
    parser.add_argument("--fixture", type=Path, help="a save slot directory (holding save.json) to continue instead of a new game")
    parser.add_argument("--stop-on-fail", action="store_true", help="end the script at the first failed statement")
    parser.add_argument("--exec-timeout", type=float, default=240.0, help="real seconds before the game ends a hung script (default 240)")
    parser.add_argument("--quiet", action="store_true", help="print failed statements only")
    parser.add_argument("--game-log", action="store_true", help="keep the game's Info log on (default: warnings and errors only)")
    parser.add_argument("--reference", choices=("text", "md", "json"), help="print the generated command reference and exit; starts no session")


def script_text(args):
    """The script, from the positional argument or --file (exactly one)."""
    if bool(args.target) == bool(args.file):
        raise ValueError("console needs a script: either \"cmd; cmd\" or --file PATH")
    text = args.file.read_text(encoding="utf-8") if args.file else args.target
    if not text.strip():
        raise ValueError("the console script is empty")
    return text


def session_arguments(args, user_dir, script_path, report_path):
    """The game's user arguments for this run. Copies the --fixture slot into user_dir first."""
    if not 1 <= args.exec_timeout <= 3600:
        raise ValueError("--exec-timeout must be 1..3600 seconds")
    user = []
    if args.fixture:
        source = args.fixture.resolve()
        if not (source / "save.json").is_file() or not SLOT.fullmatch(source.name):
            raise ValueError("--fixture must be a save slot directory holding save.json, named with letters, digits, _ or -")
        shutil.copytree(source, Path(user_dir) / "saves" / source.name)
        user += ["--play", f"--slot={source.name}"]
    else:
        user += ["--new-game"]
    user += [f"--exec-file={script_path}", f"--exec-timeout={args.exec_timeout:g}", f"--report={report_path}",
             # The backstop for a session that never starts: the script node is then never attached.
             f"--quit-after={args.exec_timeout + 30:g}"]
    if args.stop_on_fail:
        user.append("--exec-stop-on-fail")
    if args.game_log:
        user.append("--exec-verbose")
    return user


def read_results(path):
    """The runner's NDJSON as (statements, summary-or-None). Unparseable lines are skipped."""
    statements, summary = [], None
    if not Path(path).is_file():
        return statements, summary
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        try:
            row = json.loads(line)
        except ValueError:
            continue
        if row.get("event") == "result":
            summary = row
        elif "cmd" in row:
            statements.append(row)
    return statements, summary


def line_for(row, width=160):
    """One statement as one line: status, number, command, its reply with line breaks as ' | '."""
    out = " | ".join(part.strip() for part in str(row.get("out", "")).splitlines() if part.strip())
    text = f"{'ok  ' if row.get('ok') else 'FAIL'} {row.get('i')} {row.get('cmd')}" + (f" -> {out}" if out else "")
    return text if len(text) <= width else text[:width - 1] + "…"


def fold(run, statements, summary, quiet=False, reasons=()):
    """Folds the runner's results into the SDK result: metrics, one assertion per failed statement.
    `reasons` are the runner's own failure lines, shown when it never reached its result line."""
    failed = [row for row in statements if not row.get("ok")]
    metrics = run.result["metrics"]["console"] = dict(
        statements=len(statements), failed=len(failed), complete=summary is not None,
        results=str(Path(run.artifacts) / "console" / "result.ndjson"),
        steps=[dict(i=row.get("i"), cmd=row.get("cmd"), ok=bool(row.get("ok")), out=str(row.get("out", ""))[:300],
                    **({"data": row["data"]} if "data" in row else {})) for row in statements])
    for row in failed:
        # shown: the FAIL line above the verdict already says it, so the compact output adds no ASSERT line.
        run.result["assertions"].append(dict(name=f"console {row.get('i')}: {row.get('cmd')}", success=False,
                                             expected="ok", actual=str(row.get("out", ""))[:300], shown=True))
    if summary is None:
        run.issue("console.incomplete", "; ".join(reasons) or
                  "the script runner did not finish (no result line): the session did not "
                  "start, the game crashed, or the run was cut off; see the Godot log")
    # The statement lines are this command's product, so they go in the compact output. Past
    # MAX_LINES only the failures are listed; result.ndjson always has every statement.
    shown = [row for row in statements if not row.get("ok")] if quiet or len(statements) > MAX_LINES else statements
    for row in shown[:MAX_LINES]:
        run.brief(line_for(row))
    if len(shown) < len(statements) and not quiet:
        run.brief(f"{len(statements) - len(shown)} ok statements not listed: {metrics['results']}")


def reference(run, style):
    result = run.godot("console-help", [], [f"--console-help={style}"])
    table = "\n".join(line for line in result.stdout.splitlines()
                      if not line.startswith(RESULT_PREFIX) and not line.startswith("Godot Engine"))
    target = Path(run.artifacts) / f"console-reference.{'md' if style == 'md' else 'json' if style == 'json' else 'txt'}"
    target.write_text(table.strip() + "\n", encoding="utf-8")
    run.result["metrics"]["console_reference"] = str(target)
    # The table is what was asked for: print it whole (the compact output caps a run at 40 lines).
    if run.args.json or run.args.ndjson or run.args.json_full:
        run.brief(f"reference: {target}")
    else:
        print(table.strip(), flush=True)


def run(run, args, passthrough):
    if args.reference:
        reference(run, args.reference)
        return
    text = script_text(args)
    if not args.render and re.search(r"(?m)(^|;)[ 	]*shot[ 	]", text):
        raise ValueError("this script has a `shot` statement: add --render (a headless run draws nothing)")
    script_path = Path(run.artifacts) / "console-script.txt"
    script_path.write_text(text, encoding="utf-8")
    report_path = Path(run.artifacts) / "console-report.json"
    user = session_arguments(args, run.env["EMBERVALE_USER_DIR"], script_path, report_path)
    engine = ["--fixed-fps", "60"]
    if args.render and args.resolution:
        engine += ["--resolution", args.resolution[0]]
    run.godot("console", engine, user, render=args.render)
    statements, summary = read_results(Path(run.artifacts) / "console" / "result.ndjson")
    report = {}
    if report_path.is_file():
        try:
            report = json.loads(report_path.read_text(encoding="utf-8"))
        except ValueError:
            pass
    failures = report.get("failures", ())
    step = run.result["steps"][-1]
    if failures and step.get("process_exit_code") == 1:
        # The runner said why it exited 1 (failed statements, or a script it refused), and that is
        # reported below; "exit 1; see the log" would be a second line saying less.
        run.result["diagnostics"] = [d for d in run.result["diagnostics"] if d.get("code") != "process.failed"]
        if summary is not None and all(str(f).startswith("statement ") for f in failures):
            step.update(exit_code=0, success=True)   # the failed statements set the exit code (5)
    fold(run, statements, summary, quiet=args.quiet, reasons=failures)
    run.result["metrics"]["console"]["facts"] = report.get("facts", {})
