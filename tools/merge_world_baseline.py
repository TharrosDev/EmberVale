#!/usr/bin/env python3
"""Merge reviewed cell signatures into the committed world visual baseline.

This is deliberately narrower than the all-or-nothing Godot update switch.  It restores
HEAD's baseline and copies only explicitly named cell prefixes from a completed current
capture, preventing an unrelated region from being approved by accident.
"""

from __future__ import annotations

import argparse
import json
import subprocess
from pathlib import Path


BASELINE = Path("tests/visual_baselines/world_signatures.json")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "cells",
        nargs="+",
        help="Exact signature cell prefixes, for example ember_crown.town_hub",
    )
    args = parser.parse_args()

    current = json.loads(BASELINE.read_text(encoding="utf-8"))
    committed = json.loads(
        subprocess.check_output(
            ["git", "show", f"HEAD:{BASELINE.as_posix()}"], text=True
        )
    )
    if set(current["signatures"]) != set(committed["signatures"]):
        raise SystemExit("refusing merge: current and committed frame sets differ")

    copied: list[str] = []
    for cell in args.cells:
        prefix = f"{cell}/"
        matches = [key for key in current["signatures"] if key.startswith(prefix)]
        if len(matches) != 10:
            raise SystemExit(f"refusing merge: {cell!r} matched {len(matches)} frames, expected 10")
        for key in matches:
            committed["signatures"][key] = current["signatures"][key]
        copied.extend(matches)

    BASELINE.write_text(
        json.dumps(committed, separators=(",", ":"), sort_keys=True), encoding="utf-8"
    )
    print(f"merged {len(copied)} reviewed frames from {len(args.cells)} cells")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
