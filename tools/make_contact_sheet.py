#!/usr/bin/env python3
"""Build a labeled PNG contact sheet from a directory of QA screenshots."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


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
    font = ImageFont.load_default(size=18)
    label_height = 34
    with Image.open(paths[0]) as sample:
        thumb_height = round(sample.height * args.thumb_width / sample.width)
    rows = (len(paths) + args.columns - 1) // args.columns
    sheet = Image.new("RGB", (args.columns * args.thumb_width, rows * (thumb_height + label_height)), "#171a1f")
    draw = ImageDraw.Draw(sheet)
    for index, path in enumerate(paths):
        with Image.open(path) as image:
            thumb = image.convert("RGB")
            thumb.thumbnail((args.thumb_width, thumb_height), Image.Resampling.LANCZOS)
        x = (index % args.columns) * args.thumb_width
        y = (index // args.columns) * (thumb_height + label_height)
        sheet.paste(thumb, (x, y))
        draw.text((x + 10, y + thumb_height + 7), path.stem.replace("_", " "), fill="#f1e4cb", font=font)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(args.output, optimize=True)
    print(f"Contact sheet: {args.output} ({len(paths)} images)")


if __name__ == "__main__":
    main()
