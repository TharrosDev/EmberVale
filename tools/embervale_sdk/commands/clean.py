"""Prune old run directories: clean [--keep 20] [--older-than DAYS] [--dry-run].

Only directories named like a run id under artifacts/headless (and its native-tests folder) and
artifacts/jobs are touched; baselines and anything else under artifacts/ are never candidates.
The newest --keep runs stay, and a failed run stays for seven days so its evidence can be read.
"""
import json
import shutil
import time

from quality_common import ROOT
from .. import compact

HELP = "delete old run directories under artifacts/headless and artifacts/jobs, keeping the newest and recent failures"
LIGHT = True
VERSION = False
KEEP_FAILED_DAYS = 7


def arguments(parser):
    parser.add_argument("--keep", type=int, default=20, help="newest runs to keep per folder (default 20)")
    parser.add_argument("--older-than", type=float, metavar="DAYS", help="only delete runs older than this many days")
    parser.add_argument("--dry-run", action="store_true", help="report what would be deleted, delete nothing")


def passed(directory):
    """True for a run, or a job, that finished and passed. Anything else (failed, killed, still
    running) is evidence and is kept for KEEP_FAILED_DAYS."""
    try:
        if (directory / "job.json").is_file():
            state = json.loads((directory / "state.json").read_text(encoding="utf-8"))
            return state.get("state") == "done" and state.get("exit_code") == 0
        return bool(json.loads((directory / "summary.json").read_text(encoding="utf-8")).get("success"))
    except (OSError, ValueError):
        return False


def candidates(parent, keep, older_than_days, now):
    """Run directories under one parent that may go, oldest first."""
    out = []
    for directory in compact.runs(parent)[max(0, keep):]:
        age_days = (now - directory.stat().st_mtime) / 86400
        if older_than_days is not None and age_days < older_than_days:
            continue
        if not passed(directory) and age_days < KEEP_FAILED_DAYS:
            continue
        out.append(directory)
    return out


def size(directory):
    return sum(p.stat().st_size for p in directory.rglob("*") if p.is_file())


def run(run, args, passthrough):
    if args.keep < 0:
        raise ValueError("--keep must not be negative")
    now, removed, freed, kept = time.time(), 0, 0, 0
    for parent in (ROOT / "artifacts/headless", ROOT / "artifacts/headless/native-tests", ROOT / "artifacts/jobs"):
        doomed = candidates(parent, args.keep, args.older_than, now)
        kept += len(compact.runs(parent)) - len(doomed)
        for directory in doomed:
            freed += size(directory)
            if not args.dry_run:
                shutil.rmtree(directory, ignore_errors=True)
            removed += 1
    line = dict(removed=removed, kept=kept, freed_mb=round(freed / 2**20, 1), dry_run=args.dry_run)
    print(json.dumps(line) if args.json or args.json_full else
          f"CLEAN {'would remove' if args.dry_run else 'removed'}={removed} kept={kept} freed={line['freed_mb']}MB")
    return 0
