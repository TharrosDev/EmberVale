#!/usr/bin/env python3
"""Read screenshots so an agent does not have to open every one.

    python tools/shot_analyze.py stats  DIR [--flagged-only] [--strict]
    python tools/shot_analyze.py diff   DIR --baseline DIR2 [--out DIR3] [--row-normalize] [--strict]
    python tools/shot_analyze.py sheet  DIR OUT.png [--columns 4] [--thumb-width 320] [--per-sheet 16] [--only-flagged]
    python tools/shot_analyze.py thumbs DIR [--width 480]

Every command prints one JSON object per line: one per image (stats, diff), then one summary line.

stats   brightness mean/std, percent black, percent missing-material magenta, and flags:
        flat (one colour), black (>= 97 % black), magenta (>= 0.5 %), same_as:<image> (pixel-identical
        to an earlier image of the run: a drive that changed nothing, or a stale capture).
diff    each image against the same file name in the baseline directory: mean error, percent of an
        80x45 block grid that changed, up to five bounding boxes [x, y, w, h] in image pixels, and for
        a changed image one triptych PNG (before | after | heatmap with the boxes) at 480 px a panel.
        A different resolution is resized, not refused. --row-normalize removes each block row's median
        difference first, so a tone shift between two renderers does not count (the rule
        tools/world_shots.gd uses).
sheet   labelled contact sheets, 16 images a page, flagged images outlined in red.
thumbs  a 480 px JPEG beside each PNG.

Exit 0, or 1 with --strict when anything was flagged or changed, or 2 for a usage error.
Needs Pillow. Film strips (*.film.png), diff triptychs (*.diff.png) and sheets are not inputs.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont, ImageStat

BLACK_FLAG_PCT = 97.0
MAGENTA_FLAG_PCT = 0.5
FLAT_RANGE = 3
ANALYSIS_WIDTH = 480
GRID = (80, 45)
BLOCK_THRESHOLD = 12.0
MAX_BOXES = 5
NOT_INPUT = (".film.png", ".diff.png")


def images_in(directory, pattern="*.png"):
    """The PNG screenshots of a run, sorted by name; generated strips, diffs and sheets are left out."""
    directory = Path(directory)
    return sorted(p for p in directory.glob(pattern)
                  if p.is_file() and not p.name.endswith(NOT_INPUT) and not p.stem.startswith("sheet"))


def _small(image, width=ANALYSIS_WIDTH):
    image = image.convert("RGB")
    if image.width > width:
        image = image.resize((width, max(1, round(image.height * width / image.width))), Image.Resampling.BILINEAR)
    return image


def image_stats(path):
    """Measurements of one image, on a 480 px copy. Channel values are 0..255."""
    with Image.open(path) as source:
        size = [source.width, source.height]
        small = _small(source)
    grey = small.convert("L")
    stat = ImageStat.Stat(grey)
    low, high = stat.extrema[0]
    red, green, blue = small.split()
    pixels = small.width * small.height
    brightest = ImageChops.lighter(ImageChops.lighter(red, green), blue)
    black = sum(brightest.histogram()[:8])
    magenta_mask = ImageChops.multiply(
        ImageChops.multiply(red.point(lambda v: 255 if v > 230 else 0), green.point(lambda v: 255 if v < 60 else 0)),
        blue.point(lambda v: 255 if v > 230 else 0))
    magenta = magenta_mask.histogram()[255]
    return dict(image=Path(path).name, resolution=size, mean=round(stat.mean[0], 2), std=round(stat.stddev[0], 2),
                min=low, max=high, black_pct=round(black * 100.0 / pixels, 2),
                magenta_pct=round(magenta * 100.0 / pixels, 3),
                hash=hashlib.md5(small.tobytes()).hexdigest()[:16])


def flags_for(stats, earlier=None):
    """The short words that say an image deserves a look. `earlier` maps a hash to the first image with it."""
    flags = []
    if stats["max"] - stats["min"] < FLAT_RANGE:
        flags.append("flat")
    if stats["black_pct"] >= BLACK_FLAG_PCT:
        flags.append("black")
    if stats["magenta_pct"] >= MAGENTA_FLAG_PCT:
        flags.append("magenta")
    if earlier and stats["hash"] in earlier:
        flags.append("same_as:" + earlier[stats["hash"]])
    return flags


def analyze(paths):
    """Stats and flags for a run's images, in order. Returns a list of dicts, each with `flags`."""
    seen = {}
    results = []
    for path in paths:
        stats = image_stats(path)
        stats["flags"] = flags_for(stats, seen)
        seen.setdefault(stats["hash"], stats["image"])
        results.append(stats)
    return results


def block_deltas(actual, expected, row_normalize=False, grid=GRID):
    """Mean absolute channel difference of each block of a grid laid over two images, row by row.
    With row_normalize, each row's median signed difference is removed per channel first."""
    a = actual.convert("RGB").resize(grid, Image.Resampling.BOX).tobytes()
    b = expected.convert("RGB").resize(grid, Image.Resampling.BOX).tobytes()
    width, height = grid
    deltas = []
    for row in range(height):
        start = row * width * 3
        signed = [[a[start + x * 3 + c] - b[start + x * 3 + c] for x in range(width)] for c in range(3)]
        medians = [sorted(channel)[width // 2] if row_normalize else 0 for channel in signed]
        deltas.append([sum(abs(signed[c][x] - medians[c]) for c in range(3)) / 3.0 for x in range(width)])
    return deltas


def changed_boxes(deltas, threshold=BLOCK_THRESHOLD, limit=MAX_BOXES):
    """Connected groups of changed blocks as [x, y, w, h] in block units, largest first."""
    height, width = len(deltas), len(deltas[0])
    seen = [[False] * width for _ in range(height)]
    boxes = []
    for y in range(height):
        for x in range(width):
            if seen[y][x] or deltas[y][x] <= threshold:
                continue
            stack, x0, y0, x1, y1, count = [(x, y)], x, y, x, y, 0
            seen[y][x] = True
            while stack:
                cx, cy = stack.pop()
                count += 1
                x0, y0, x1, y1 = min(x0, cx), min(y0, cy), max(x1, cx), max(y1, cy)
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < width and 0 <= ny < height and not seen[ny][nx] and deltas[ny][nx] > threshold:
                        seen[ny][nx] = True
                        stack.append((nx, ny))
            boxes.append((count, [x0, y0, x1 - x0 + 1, y1 - y0 + 1]))
    boxes.sort(key=lambda item: -item[0])
    return [box for _, box in boxes[:limit]]


def diff_images(actual_path, expected_path, row_normalize=False, threshold=BLOCK_THRESHOLD, triptych=None):
    """Compares one image with its baseline. Writes a triptych to `triptych` when it changed."""
    with Image.open(actual_path) as source:
        actual = source.convert("RGB")
    with Image.open(expected_path) as source:
        expected = source.convert("RGB")
    resized = expected.size != actual.size
    if resized:
        expected = expected.resize(actual.size, Image.Resampling.BILINEAR)
    mean_error = ImageStat.Stat(ImageChops.difference(actual, expected).convert("L")).mean[0] / 255.0
    deltas = block_deltas(actual, expected, row_normalize)
    blocks = GRID[0] * GRID[1]
    changed = sum(1 for row in deltas for value in row if value > threshold)
    sx, sy = actual.width / GRID[0], actual.height / GRID[1]
    boxes = [[round(x * sx), round(y * sy), round(w * sx), round(h * sy)] for x, y, w, h in changed_boxes(deltas, threshold)]
    result = dict(image=Path(actual_path).name, changed=changed > 0, mean_error=round(mean_error, 5),
                  changed_pct=round(changed * 100.0 / blocks, 2), boxes=boxes,
                  peak=round(max(max(row) for row in deltas), 1))
    if resized:
        result["resized_baseline"] = True
    if changed and triptych:
        result["triptych"] = str(_write_triptych(actual, expected, deltas, boxes, threshold, Path(triptych)))
    return result


def _write_triptych(actual, expected, deltas, boxes, threshold, path, panel=ANALYSIS_WIDTH):
    height = max(1, round(actual.height * panel / actual.width))
    before = expected.resize((panel, height), Image.Resampling.BILINEAR)
    after = actual.resize((panel, height), Image.Resampling.BILINEAR)
    heat = Image.frombytes("L", GRID, bytes(
        min(255, int(value / max(1.0, threshold * 2.0) * 255)) if value > threshold else 0
        for row in deltas for value in row))
    heat = heat.resize((panel, height), Image.Resampling.NEAREST)
    overlay = Image.composite(Image.new("RGB", (panel, height), "#ff2a2a"), after.point(lambda v: v // 3), heat)
    draw = ImageDraw.Draw(overlay)
    scale = panel / actual.width
    for x, y, w, h in boxes:
        draw.rectangle([x * scale, y * scale, (x + w) * scale - 1, (y + h) * scale - 1], outline="#ffe14a", width=2)
    sheet = Image.new("RGB", (panel * 3 + 8, height), "#171a1f")
    for index, image in enumerate((before, after, overlay)):
        sheet.paste(image, (index * (panel + 4), 0))
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path, optimize=True)
    return path


def diff_dirs(directory, baseline, out=None, row_normalize=False, threshold=BLOCK_THRESHOLD):
    """Every image of `directory` against the same name in `baseline`."""
    out = Path(out) if out else Path(directory) / "diff"
    results = []
    for path in images_in(directory):
        other = Path(baseline) / path.name
        if not other.is_file():
            results.append(dict(image=path.name, missing_baseline=True, changed=False))
            continue
        results.append(diff_images(path, other, row_normalize, threshold, out / (path.stem + ".diff.png")))
    return results


def make_sheets(paths, output, columns=4, thumb_width=320, per_sheet=16, flagged=(), labels=None):
    """Labelled contact sheets of `paths`. One file at `output`, or output_01, _02, ... when the
    images need more than `per_sheet` cells. Returns the files written."""
    paths = [Path(p) for p in paths]
    if not paths:
        return []
    output = Path(output)
    flagged = set(flagged)
    font = ImageFont.load_default(size=max(11, thumb_width // 24))
    label_height = max(18, thumb_width // 14)
    with Image.open(paths[0]) as sample:
        thumb_height = max(1, round(sample.height * thumb_width / sample.width))
    pages = [paths] if not per_sheet else [paths[i:i + per_sheet] for i in range(0, len(paths), per_sheet)]
    written = []
    for number, page in enumerate(pages, 1):
        rows = (len(page) + columns - 1) // columns
        sheet = Image.new("RGB", (columns * thumb_width, rows * (thumb_height + label_height)), "#171a1f")
        draw = ImageDraw.Draw(sheet)
        for index, path in enumerate(page):
            thumb_file = path.with_name(path.stem + ".thumb.jpg")
            with Image.open(thumb_file if thumb_file.is_file() else path) as image:
                thumb = image.convert("RGB")
                thumb.thumbnail((thumb_width, thumb_height), Image.Resampling.BILINEAR)
            x = (index % columns) * thumb_width
            y = (index // columns) * (thumb_height + label_height)
            sheet.paste(thumb, (x, y))
            marked = path.name in flagged
            if marked:
                draw.rectangle([x, y, x + thumb_width - 1, y + thumb_height - 1], outline="#ff3b3b", width=3)
            label = (labels or {}).get(path.name, path.stem)
            draw.text((x + 6, y + thumb_height + 2), label, fill="#ff8a8a" if marked else "#f1e4cb", font=font)
        target = output if len(pages) == 1 else output.with_name(f"{output.stem}_{number:02d}{output.suffix}")
        target.parent.mkdir(parents=True, exist_ok=True)
        sheet.save(target, optimize=True)
        written.append(target)
    return written


def make_thumbs(paths, width=ANALYSIS_WIDTH):
    written = []
    for path in paths:
        target = Path(path).with_name(Path(path).stem + ".thumb.jpg")
        with Image.open(path) as image:
            _small(image, width).save(target, quality=80)
        written.append(target)
    return written


def _emit(item):
    print(json.dumps(item, ensure_ascii=False), flush=True)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    stats = sub.add_parser("stats")
    stats.add_argument("directory", type=Path)
    stats.add_argument("--flagged-only", action="store_true", help="print only the flagged images and the summary")
    stats.add_argument("--strict", action="store_true")
    diff = sub.add_parser("diff")
    diff.add_argument("directory", type=Path)
    diff.add_argument("--baseline", type=Path, required=True)
    diff.add_argument("--out", type=Path, help="where the triptychs go (default DIR/diff)")
    diff.add_argument("--row-normalize", action="store_true")
    diff.add_argument("--threshold", type=float, default=BLOCK_THRESHOLD, help="block difference (0..255) that counts as changed")
    diff.add_argument("--strict", action="store_true")
    sheet = sub.add_parser("sheet")
    sheet.add_argument("directory", type=Path)
    sheet.add_argument("output", type=Path)
    sheet.add_argument("--columns", type=int, default=4)
    sheet.add_argument("--thumb-width", type=int, default=320)
    sheet.add_argument("--per-sheet", type=int, default=16, help="0 puts everything on one sheet")
    sheet.add_argument("--only-flagged", action="store_true")
    sheet.add_argument("--glob", default="*.png")
    thumbs = sub.add_parser("thumbs")
    thumbs.add_argument("directory", type=Path)
    thumbs.add_argument("--width", type=int, default=ANALYSIS_WIDTH)
    args = parser.parse_args(argv)

    if not args.directory.is_dir():
        _emit(dict(error=f"not a directory: {args.directory}"))
        return 2
    if args.command == "stats":
        results = analyze(images_in(args.directory))
        flagged = [r["image"] for r in results if r["flags"]]
        for result in results:
            if result["flags"] or not args.flagged_only:
                _emit(result)
        _emit(dict(summary=True, directory=str(args.directory), images=len(results), flagged=flagged))
        return 1 if args.strict and flagged else 0
    if args.command == "diff":
        if not args.baseline.is_dir():
            _emit(dict(error=f"not a directory: {args.baseline}"))
            return 2
        results = diff_dirs(args.directory, args.baseline, args.out, args.row_normalize, args.threshold)
        changed = [r["image"] for r in results if r.get("changed")]
        for result in results:
            if result.get("changed") or result.get("missing_baseline"):
                _emit(result)
        _emit(dict(summary=True, directory=str(args.directory), baseline=str(args.baseline), images=len(results),
                   changed=changed, missing_baseline=[r["image"] for r in results if r.get("missing_baseline")]))
        return 1 if args.strict and changed else 0
    if args.command == "sheet":
        paths = [p for p in images_in(args.directory, args.glob) if p.resolve() != args.output.resolve()]
        results = analyze(paths) if args.only_flagged else []
        flagged = [r["image"] for r in results if r["flags"]]
        if args.only_flagged:
            paths = [p for p in paths if p.name in flagged]
        written = make_sheets(paths, args.output, args.columns, args.thumb_width, args.per_sheet, flagged)
        _emit(dict(summary=True, images=len(paths), sheets=[str(p) for p in written]))
        return 0
    written = make_thumbs(images_in(args.directory), args.width)
    _emit(dict(summary=True, thumbs=len(written), directory=str(args.directory)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
