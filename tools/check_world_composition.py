#!/usr/bin/env python3
"""Fail a region whose places sit on its streaming lattice instead of on its geography.

    python tools/check_world_composition.py data/regions/EmberCrown.tres [--report]

Streaming cells are implementation partitions. A player who can see the lattice in where the places
are — every settlement parked on a cell centre, three towns sharing one x, every neighbour 90 m
from the next, every road crossing a seam at its exact midpoint — reads the realm as rooms. None of
that is visible in a file, every other gate passes it, and it is exactly the layout this repository
shipped until the 2026-09 world rebuild. So it is measured here, from committed data only:

  centre-parked   a MAJOR place (settlement, dungeon, mine, arena, camp) within 12% of its cell's
                  half-size of the cell centre
  aligned         pairs of majors more than 60 m apart that share an x or a z within 2.5 m
  even spacing    nearest-neighbour distances between majors whose spread (coefficient of
                  variation) is under 0.2 — a lattice, not a country
  seam midpoints  roads that cross a shared cell edge within 4 m of that edge's midpoint

Positions are the real world transforms of each MapPin: the cell centre from the region `.tres`
plus the pin's parent chain composed out of the cell `.tscn`, which is what the map probe measures
in-engine. ⚠️ It is a FAST gate on purpose: it needs no Godot, so it runs in the required CI job.
"""

from __future__ import annotations

import math
from pathlib import Path
import re
import statistics
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from check_region_seams import find_repo, parse_region  # noqa: E402

# MapCategory order (src/World/MapCategory.cs) — the same contract gen_map_locations.py keeps.
CATEGORY = [
    "Capital", "Town", "Village", "Outpost", "Camp", "Wilds",
    "Smith", "Merchant", "Alchemist", "Provisioner", "Outfitter", "Jeweller", "Scriptorium",
    "Inn", "Bank", "Stable", "Trainer", "Contracts", "Crafting", "Arena",
    "Mine", "Dungeon", "Landmark",
    "Gate", "Waystone",
    "Home", "Waypoint",
]
# A major place is somewhere a realm is organised around. Wilds are regions of country, not points;
# a Home is the player's own plot inside a settlement; waystones and shops belong to their place.
MAJOR = {"Capital", "Town", "Village", "Outpost", "Camp", "Mine", "Dungeon", "Arena"}

CENTRE_FRACTION = 0.12
CENTRE_MIN = 6.0
ALIGN_TOLERANCE = 2.5
ALIGN_MIN_DISTANCE = 60.0
MIN_SPACING_CV = 0.2
MIDPOINT_TOLERANCE = 4.0
EDGE_TOLERANCE = 0.75


def identity():
    return [1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0]


def compose(parent, child):
    """Godot Transform3D(basis columns x,y,z; origin) as 12 floats, parent * child."""
    pb, po = parent[:9], parent[9:]
    cb, co = child[:9], child[9:]

    def rot(b, v):  # basis columns are b[0:3], b[3:6], b[6:9]
        return [b[0] * v[0] + b[3] * v[1] + b[6] * v[2],
                b[1] * v[0] + b[4] * v[1] + b[7] * v[2],
                b[2] * v[0] + b[5] * v[1] + b[8] * v[2]]

    basis = rot(pb, cb[0:3]) + rot(pb, cb[3:6]) + rot(pb, cb[6:9])
    origin = [a + b for a, b in zip(rot(pb, co), po)]
    return basis + origin


def scene_nodes(path: Path) -> dict[str, tuple[list[float], str]]:
    """{node path relative to root: (local transform, LocationId or '')} for one .tscn."""
    nodes: dict[str, tuple[list[float], str]] = {}
    current = None
    for raw in path.read_text(encoding="utf-8-sig").splitlines():
        line = raw.strip()
        if line.startswith("[node "):
            name = re.search(r'name="([^"]+)"', line).group(1)
            parent = re.search(r'parent="([^"]*)"', line)
            if parent is None:
                current = "."
            else:
                p = parent.group(1)
                current = name if p == "." else f"{p}/{name}"
            nodes[current] = (identity(), "")
        elif current is None:
            continue
        elif line.startswith("transform = Transform3D("):
            values = [float(v) for v in line[len("transform = Transform3D("):-1].split(",")]
            nodes[current] = (values, nodes[current][1])
        elif line.startswith("position = Vector3("):
            v = [float(x) for x in line[len("position = Vector3("):-1].split(",")]
            nodes[current] = (identity()[:9] + v, nodes[current][1])
        elif line.startswith("LocationId = "):
            nodes[current] = (nodes[current][0], line.split('"')[1])
        elif line.startswith("["):
            current = None
    return nodes


def world_of(nodes, node_path):
    chain = []
    path = node_path
    while path != ".":
        chain.append(nodes.get(path, (identity(), ""))[0])
        path = path.rsplit("/", 1)[0] if "/" in path else "."
    transform = identity()
    for local in reversed(chain):
        transform = compose(transform, local)
    return transform[9], transform[11]


def locations(repo: Path) -> dict[str, tuple[str, str]]:
    out = {}
    for tres in (repo / "data" / "map_locations").glob("*.tres"):
        text = tres.read_text(encoding="utf-8-sig")
        lid = re.search(r'^Id = "([^"]+)"', text, re.M)
        cat = re.search(r"^Category = (\d+)", text, re.M)
        cell = re.search(r'^CellId = "([^"]+)"', text, re.M)
        if lid and cat and cell:
            out[lid.group(1)] = (CATEGORY[int(cat.group(1))], cell.group(1))
    return out


def measure(region_path: Path):
    repo = find_repo(region_path)
    cells = parse_region(region_path)
    catalogue = locations(repo)
    majors = []  # (id, category, x, z, cell)
    for cell in cells:
        scene = repo / cell.scene_path.replace("res://", "")
        if not scene.exists():
            continue
        nodes = scene_nodes(scene)
        for node_path, (_, lid) in nodes.items():
            if not lid or lid not in catalogue:
                continue
            category, _ = catalogue[lid]
            if category not in MAJOR:
                continue
            lx, lz = world_of(nodes, node_path)
            majors.append((lid, category, cell.center[0] + lx, cell.center[1] + lz, cell))

    failures, notes = [], []

    for lid, _, x, z, cell in majors:
        limit = max(CENTRE_MIN, CENTRE_FRACTION * min(cell.width, cell.depth) * 0.5)
        offset = math.hypot(x - cell.center[0], z - cell.center[1])
        if offset < limit:
            failures.append(f"centre-parked: {lid} is {offset:.1f} m from {cell.cell_id}'s centre "
                            f"(limit {limit:.1f} m)")

    aligned = []
    for i, a in enumerate(majors):
        for b in majors[i + 1:]:
            distance = math.hypot(a[2] - b[2], a[3] - b[3])
            if distance < ALIGN_MIN_DISTANCE:
                continue
            if abs(a[2] - b[2]) < ALIGN_TOLERANCE or abs(a[3] - b[3]) < ALIGN_TOLERANCE:
                aligned.append(f"{a[0]} / {b[0]}")
    allowed = max(1, len(majors) // 6)
    if len(aligned) > allowed:
        failures.append(f"aligned: {len(aligned)} major pairs share an axis (allowed {allowed}): "
                        + "; ".join(aligned[:8]))

    if len(majors) >= 4:
        nearest = []
        for a in majors:
            nearest.append(min(math.hypot(a[2] - b[2], a[3] - b[3]) for b in majors if b is not a))
        mean = statistics.mean(nearest)
        cv = statistics.pstdev(nearest) / mean if mean else 0.0
        notes.append(f"majors {len(majors)}, nearest-neighbour mean {mean:.0f} m, "
                     f"min {min(nearest):.0f} m, max {max(nearest):.0f} m, cv {cv:.2f}")
        if cv < MIN_SPACING_CV:
            failures.append(f"even spacing: nearest-neighbour cv {cv:.2f} < {MIN_SPACING_CV} "
                            "(majors sit on a lattice)")

    crossings = midpoints = 0
    for cell in cells:
        for route in cell.routes:
            for lx, lz in (route.start, route.end):
                on_x = abs(abs(lx) - cell.width * 0.5) <= EDGE_TOLERANCE
                on_z = abs(abs(lz) - cell.depth * 0.5) <= EDGE_TOLERANCE
                if not (on_x or on_z):
                    continue
                crossings += 1
                along = lz if on_x else lx
                if abs(along) <= MIDPOINT_TOLERANCE:
                    midpoints += 1
    if crossings:
        notes.append(f"seam crossings {crossings // 2}, at an edge midpoint {midpoints // 2}")
        if midpoints > crossings * 0.4:
            failures.append(f"seam midpoints: {midpoints // 2} of {crossings // 2} road crossings "
                            "are within 4 m of their edge's midpoint")
    return majors, failures, notes


def main(argv: list[str]) -> int:
    paths = [Path(a) for a in argv if not a.startswith("--")]
    if not paths:
        print(__doc__)
        return 2
    status = 0
    for path in paths:
        majors, failures, notes = measure(path)
        print(f"{path.name}:")
        for note in notes:
            print(f"  {note}")
        if "--report" in argv:
            for lid, category, x, z, cell in majors:
                print(f"  {category:8s} {lid:40s} ({x:7.1f}, {z:7.1f})  in {cell.cell_id}")
        for failure in failures:
            print(f"  FAIL {failure}")
        if failures:
            status = 1
        else:
            print("  PASS composition")
    return status


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
