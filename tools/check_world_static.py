#!/usr/bin/env python3
"""The engine-free world checks for every region in one process: seams, layout, composition.

    python tools/check_world_static.py                      all three checks, all regions
    python tools/check_world_static.py --region ember_crown --only seams,layout
    python tools/check_world_static.py --json               one object, nothing else
    python tools/check_world_static.py -v                   also name every passing check

The world runner starts check_region_seams.py, check_cell_layout.py and check_world_composition.py
once per region: eighteen processes whose output is mostly SEAM_OK lines. This runs the same
functions in one process, prints only the lines that explain a failure, writes each check's full
output to artifacts/world_static/<check>-<region>.log and ends with `WORLD_STATIC {json}`.
Exit 0 when every check passes, 1 otherwise. It owns no rule: each one stays in its own tool.
"""

from __future__ import annotations

import argparse
import contextlib
import io
import json
import time
from pathlib import Path

from quality_common import ROOT

import check_cell_layout
import check_region_seams
import check_world_composition

LOGS = ROOT / "artifacts" / "world_static"
EXPLAINS = ("FAIL", "OPEN_EDGE", "MISMATCH", "OVERLAP", "OUTSIDE", "ERROR", "Traceback")
LINE_CAP = 12

CHECKS = {
    "seams": lambda path: 1 if check_region_seams.validate_region(path) else 0,
    "layout": lambda path: check_cell_layout.main([str(path)]),
    "composition": lambda path: check_world_composition.main([str(path)]),
}


def slug(name: str) -> str:
    return name.replace("_", "").replace(".tres", "").lower()


def regions(root: Path = ROOT, wanted: list[str] | None = None) -> list[Path]:
    paths = sorted((root / "data" / "regions").glob("*.tres"))
    if not wanted:
        return paths
    keys = {slug(name.removeprefix("region.")) for name in wanted}
    chosen = [path for path in paths if slug(path.name) in keys]
    missing = keys - {slug(path.name) for path in chosen}
    if missing:
        # Exit 2, not 1: 1 means a check failed.
        import sys
        print(f"unknown region(s): {', '.join(sorted(missing))}; "
              f"known: {', '.join(path.stem for path in paths)}", file=sys.stderr)
        raise SystemExit(2)
    return chosen


def explain(output: str) -> list[str]:
    """The lines of a failed check worth reading; its last lines when none is marked."""
    lines = [line.rstrip() for line in output.splitlines() if line.strip()]
    marked = [line for line in lines if any(word in line for word in EXPLAINS)]
    chosen = marked or lines[-5:]
    return chosen[:LINE_CAP] + ([f"  ... {len(chosen) - LINE_CAP} more"] if len(chosen) > LINE_CAP else [])


def run(paths: list[Path], names: list[str], logs: Path | None = LOGS, checks=None) -> dict:
    checks = checks or CHECKS
    started = time.monotonic()
    failures: list[dict] = []
    passed = 0
    for name in names:
        for path in paths:
            buffer = io.StringIO()
            try:
                with contextlib.redirect_stdout(buffer):
                    code = checks[name](path)
            except SystemExit as error:
                code = error.code if isinstance(error.code, int) else 1
                buffer.write(f"ERROR {error}\n")
            except Exception as error:  # a crashed check is a failed check, with its reason
                code = 1
                buffer.write(f"ERROR {type(error).__name__}: {error}\n")
            output = buffer.getvalue()
            if logs is not None:
                logs.mkdir(parents=True, exist_ok=True)
                (logs / f"{name}-{path.stem}.log").write_text(output, encoding="utf-8")
            if code:
                failures.append({"check": name, "region": path.stem, "lines": explain(output)})
            else:
                passed += 1
    return {"ok": not failures, "exit_code": 1 if failures else 0, "regions": [path.stem for path in paths],
            "checks": names, "passed": passed, "failed": len(failures), "failures": failures,
            "seconds": round(time.monotonic() - started, 1)}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--region", action="append", default=[], help="region slug or resource stem; repeatable")
    parser.add_argument("--only", default="", help="comma list of: " + ", ".join(CHECKS))
    parser.add_argument("--json", action="store_true")
    parser.add_argument("-v", "--verbose", action="store_true")
    args = parser.parse_args(argv)
    names = [name for name in args.only.split(",") if name] or list(CHECKS)
    unknown = [name for name in names if name not in CHECKS]
    if unknown:
        parser.error(f"unknown check(s): {', '.join(unknown)}")
    result = run(regions(wanted=args.region), names)
    if args.json:
        print(json.dumps(result))
        return result["exit_code"]
    if args.verbose:
        failed = {(f["check"], f["region"]) for f in result["failures"]}
        for name in names:
            for region in result["regions"]:
                if (name, region) not in failed:
                    print(f"ok    {name} {region}")
    for failure in result["failures"]:
        print(f"FAIL  {failure['check']} {failure['region']}")
        for line in failure["lines"]:
            print(f"  {line.strip()}")
    compact = {key: value for key, value in result.items() if key != "failures"}
    compact["failures"] = [f"{f['check']}:{f['region']}" for f in result["failures"]]
    print("WORLD_STATIC " + json.dumps(compact))
    return result["exit_code"]


if __name__ == "__main__":
    raise SystemExit(main())
