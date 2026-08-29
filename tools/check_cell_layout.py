#!/usr/bin/env python3
"""Report major overlaps and out-of-bounds structure in cells or an entire region.

Why this exists
---------------
The 2026-08-28 layout rebuild re-laid every cell by moving transforms. A stall pushed into a
building, or two houses on
the same ground, is invisible in the `.tscn`, invisible to `--validate` (which checks references,
not geometry) and only shows up by walking there.

    python tools/check_cell_layout.py scenes/regions/ember_crown/embermarket.tscn [--slack=0.0]
    python tools/check_cell_layout.py data/regions/EmberCrown.tres

⚠️ IT ONLY LOOKS AT BIG THINGS, AND THAT IS THE WHOLE DESIGN. It compares top-level nodes whose
`dress_cell.py` footprint is >= 3 m — buildings, stalls, towers, carts, trees, walls — and ignores
clutter, ground cover, lights, actors and every child node. The first version compared everything
and reported 330 pairs, of which about six were real: barrels are SUPPOSED to touch, a merchant is
SUPPOSED to stand at her stall, and a child's transform is local to its parent so comparing it with
a world position is meaningless. A checker nobody reads is worse than no checker.
"""

import math
import os
from pathlib import Path
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dress_cell import footprint  # noqa: E402
from check_region_seams import cell_for_scene, find_repo, parse_region, validate_region  # noqa: E402

BIG = 3.0

# Ground skins are flat slabs the props are MEANT to stand on. Their names collide with the prop
# vocabulary ("GateLane" contains "gate", "TimberYard" contains "timber"), so they are named out
# rather than inferred.
# Things that are SUPPOSED to touch. Fence panels abut at corners by design; a chest, a stand or a
# bed is furniture standing inside the building whose footprint the checker would otherwise flag.
TOUCHING = ("fence", "chest", "stand", "bed", "deed", "pin")

SKIN = ("lane", "yard", "road", "walk", "court", "steps", "stair", "spine", "turn", "crookway",
        "forecourt", "plaza", "aisle", "floor", "apron", "ring", "path")


def structure(path):
    """Top-level (name, x, z, radius) for every node parented to the cell root or its Nav region."""
    out, pending = [], None
    for line in open(path, encoding="utf-8"):
        line = line.strip()
        if line.startswith("[node name="):
            name = line.split('"', 2)[1]
            parent = line.split('parent="', 1)[1].split('"', 1)[0] if 'parent="' in line else None
            pending = name if parent in ("Nav", ".") else None
        elif pending and line.startswith("transform = Transform3D("):
            v = [float(x) for x in line[len("transform = Transform3D("):-1].split(",")]
            r = footprint(pending)
            low = pending.lower()
            if r >= BIG and not any(w in low for w in SKIN + TOUCHING):
                out.append((pending, v[9], v[11], r))
            pending = None
        elif pending and line.startswith("position = Vector3("):
            v = [float(x) for x in line[len("position = Vector3("):-1].split(",")]
            r = footprint(pending)
            low = pending.lower()
            if r >= BIG and not any(w in low for w in SKIN + TOUCHING):
                out.append((pending, v[0], v[2], r))
            pending = None
    return out


def validate_scene(path: Path, slack: float = 0.0) -> int:
    props = structure(path)
    hits = 0
    for i, (n1, x1, z1, r1) in enumerate(props):
        for n2, x2, z2, r2 in props[i + 1:]:
            d = math.hypot(x1 - x2, z1 - z2)
            # 0.7 of the mean keep-out radius. dress_cell's radii are scatter keep-outs and run
            # ~40% wider than the model's real hull; used raw they call a normal terraced row an
            # overlap, and a checker that cries wolf is one nobody reads.
            want = (r1 + r2) / 2.0 * 0.7 - slack
            if d < want:
                print(f"  OVERLAP {n1} ({x1}, {z1}) x {n2} ({x2}, {z2}): {d:.2f} m, want {want:.2f}")
                hits += 1

    # The presentation dimensions are the cell's actual authored ground. A major prop may extend
    # over an edge for silhouette, but its centre outside the ground means the placement drifted.
    if (cell := cell_for_scene(Path(path))) is not None:
        half_width, half_depth = cell.width * 0.5, cell.depth * 0.5
        for name, x, z, _radius in props:
            if abs(x) > half_width + 0.25 or abs(z) > half_depth + 0.25:
                print(f"  OUTSIDE {name} centre ({x}, {z}) outside {cell.width} x {cell.depth} m bounds")
                hits += 1

    print(f"{path}: {len(props)} structures, {hits} issue(s)")
    return hits


def main(argv):
    if not argv:
        raise SystemExit(__doc__)
    path = Path(argv[0])
    slack = next((float(a.split("=")[1]) for a in argv if a.startswith("--slack=")), 0.0)
    if path.suffix == ".tres":
        failures = validate_region(path)
        repo = find_repo(path)
        if repo is None:
            return 1
        for cell in parse_region(path):
            failures += validate_scene(repo / cell.scene_path.removeprefix("res://"), slack)
        return 1 if failures else 0
    return 1 if validate_scene(path, slack) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
