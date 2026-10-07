#!/usr/bin/env python3
"""Build one labelled PNG contact sheet from a directory of QA screenshots.

Kept for its old command line; the work is `tools/shot_analyze.py sheet`, which also pages,
outlines flagged frames and reads thumbnails."""
from __future__ import annotations

import argparse
from pathlib import Path

from shot_analyze import make_sheets


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--columns", type=int, default=4)
    parser.add_argument("--thumb-width", type=int, default=480)
    args = parser.parse_args()

    paths = sorted(path for path in args.input.glob("*.png") if path.resolve() != args.output.resolve())
    if not paths:
        raise SystemExit(f"No PNGs found in {args.input}")
    make_sheets(paths, args.output, args.columns, args.thumb_width, per_sheet=0)
    print(f"Contact sheet: {args.output} ({len(paths)} images)")


if __name__ == "__main__":
    main()
