"""Triage a Godot or SDK log into unique errors and warnings with counts: logs [PATH].

PATH is a log file or a run directory. Without it the newest run under artifacts/headless is
read (its *.godot.log files, else its step logs). Works on a log the SDK did not produce, such
as a raw `--play` or `--spellshots` run saved with `--log-file` or shell redirection.
Exit 0 when no error remains after known noise, 1 when one does, 2 when there is no log.
"""
import json
from pathlib import Path

from quality_common import ROOT
from .. import compact, triage

HELP = "triage a log file or run directory into unique errors/warnings with counts and first location"
LIGHT = True
VERSION = False


def arguments(parser):
    parser.add_argument("--top", type=int, default=30, help="how many groups to print (default 30)")
    parser.add_argument("--errors-only", action="store_true", help="leave the warnings out")
    parser.add_argument("--raw", action="store_true", help="do not suppress the known-noise patterns")
    parser.add_argument("--from-line", type=int, default=1, help="ignore the log before this line")


def sources(target, parent):
    """The files to read for a target (a file, a run directory, or None for the newest run)."""
    if target:
        path = Path(target)
        if path.is_file():
            return [path]
        if not path.is_dir():
            raise ValueError(f"no such log file or run directory: {target}")
    else:
        path = next(iter(compact.runs(parent)), None)
        if path is None:
            raise ValueError(f"no run under {compact.short_path(parent)}; pass a log file")
    engine_logs = sorted(path.glob("*.godot.log"))
    return engine_logs or sorted(path.glob("*.stdout.log")) + sorted(path.glob("*.stderr.log"))


def run(run, args, passthrough):
    files = sources(args.target, (args.artifacts or ROOT / "artifacts/headless").resolve())
    if not files:
        raise ValueError("that run directory holds no logs")
    text = "\n".join(f.read_text(encoding="utf-8", errors="replace") for f in files)
    if args.from_line > 1:
        text = "\n" * (args.from_line - 1) + "\n".join(text.splitlines()[args.from_line - 1:])
    report = triage.triage(text, () if args.raw else triage.known_noise())
    source = compact.short_path(files[0] if len(files) == 1 else files[0].parent)
    if args.json or args.json_full:
        if not args.json_full:
            report["groups"] = [g for g in report["groups"] if not args.errors_only or g["severity"] == "error"][:args.top]
        print(json.dumps(dict(report, source=source), ensure_ascii=False))
    else:
        print("\n".join(triage.render(report, args.top, args.errors_only, source)))
    return 1 if any(g["severity"] == "error" for g in report["groups"]) else 0
