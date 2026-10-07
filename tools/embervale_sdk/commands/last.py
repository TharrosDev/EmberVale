"""Re-read the newest run's compact result without running anything: last [N] [--failed]."""
import json

from quality_common import ROOT
from .. import compact

HELP = "print the compact result of the newest run (last 3 = the newest three; --failed = the newest failure)"
LIGHT = True
VERSION = False


def arguments(parser):
    parser.add_argument("--failed", action="store_true", help="only runs that did not pass")


def run(run, args, passthrough):
    try:
        count = int(args.target) if args.target else 1
    except ValueError:
        raise ValueError("last takes a number of runs, e.g. `last 3`")
    parent = (args.artifacts or ROOT / "artifacts/headless").resolve()
    shown = 0
    for directory in compact.runs(parent):
        result = compact.load(directory)
        if result is None or (args.failed and result.get("success")):
            continue
        if args.json_full:
            print(json.dumps(result, ensure_ascii=False))
        elif args.json:
            print(json.dumps(compact.compact(result), ensure_ascii=False))
        else:
            print(compact.text(result))
        shown += 1
        if shown >= max(1, count):
            break
    if not shown:
        print(f"no {'failed ' if args.failed else ''}run under {compact.short_path(parent)}")
        return 2
    return 0
