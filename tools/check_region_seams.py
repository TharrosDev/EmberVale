#!/usr/bin/env python3
"""Validate that authored world-cell roads have matching openings across real cell seams.

    python tools/check_region_seams.py data/regions/EmberCrown.tres

The validator reads cell centres, presentation sizes and authored path endpoints from the region
resource itself. A route that reaches an edge must meet an abutting cell and that neighbour must
author a corresponding endpoint at the same world-space opening. Interior route ends are ignored.
"""

from __future__ import annotations

from dataclasses import dataclass
import math
from pathlib import Path
import re
import sys

EDGE_TOLERANCE = 0.75
SEAM_TOLERANCE = 1.25


@dataclass(frozen=True)
class Route:
    name: str
    start: tuple[float, float]
    end: tuple[float, float]
    width: float


@dataclass(frozen=True)
class Cell:
    cell_id: str
    scene_path: str
    center: tuple[float, float]
    width: float
    depth: float
    routes: tuple[Route, ...]

    @property
    def left(self): return self.center[0] - self.width * 0.5
    @property
    def right(self): return self.center[0] + self.width * 0.5
    @property
    def top(self): return self.center[1] - self.depth * 0.5
    @property
    def bottom(self): return self.center[1] + self.depth * 0.5


def numbers(text: str) -> tuple[float, ...]:
    return tuple(float(value.strip()) for value in text.split(","))


def blocks(text: str) -> dict[str, str]:
    pattern = re.compile(r'^\[sub_resource[^\]]*id="([^"]+)"[^\]]*\]\n(.*?)(?=^\[|\Z)', re.M | re.S)
    return {match.group(1): match.group(2) for match in pattern.finditer(text)}


def parse_region(path: Path) -> list[Cell]:
    resources = blocks(path.read_text(encoding="utf-8-sig"))
    routes: dict[str, Route] = {}
    presentations: dict[str, tuple[float, float, tuple[str, ...]]] = {}

    for resource_id, body in resources.items():
        start = re.search(r"^Start = Vector2\(([^)]+)\)", body, re.M)
        end = re.search(r"^End = Vector2\(([^)]+)\)", body, re.M)
        if start and end:
            width = re.search(r"^Width = ([\d.\-]+)", body, re.M)
            routes[resource_id] = Route(
                resource_id, numbers(start.group(1))[:2], numbers(end.group(1))[:2],
                float(width.group(1)) if width else 4.0)

    for resource_id, body in resources.items():
        # A presentation is identified by its script, not by having roads: since the 2026-08-29
        # overhaul a cell may legitimately author none (ash_approach is deliberately unroaded), and
        # keying on `Paths =` made the parser KeyError on the first one that did.
        if not resource_id.startswith("Presentation_"):
            continue
        authored = re.search(r"^Paths = .*?\(\[(.*?)\]\)", body, re.M)
        width = re.search(r"^Width = ([\d.\-]+)", body, re.M)
        depth = re.search(r"^Depth = ([\d.\-]+)", body, re.M)
        route_ids = tuple(re.findall(r'SubResource\("([^"]+)"\)', authored.group(1))) if authored else ()
        presentations[resource_id] = (
            float(width.group(1)) if width else 52.0,
            float(depth.group(1)) if depth else 52.0,
            route_ids)

    cells = []
    for body in resources.values():
        cell_id = re.search(r'^Id = "((?!region\.)[a-z0-9_]+\.[a-z0-9_]+)"', body, re.M)
        scene = re.search(r'^ScenePath = "([^"]+)"', body, re.M)
        center = re.search(r"^Center = Vector3\(([^)]+)\)", body, re.M)
        presentation = re.search(r'^Presentation = SubResource\("([^"]+)"\)', body, re.M)
        if not (cell_id and scene and center and presentation):
            continue
        width, depth, route_ids = presentations[presentation.group(1)]
        xyz = numbers(center.group(1))
        cells.append(Cell(
            cell_id.group(1), scene.group(1), (xyz[0], xyz[2]), width, depth,
            tuple(routes[route_id] for route_id in route_ids)))
    return cells


def find_repo(path: Path) -> Path | None:
    for candidate in (path.resolve(), *path.resolve().parents):
        if (candidate / "project.godot").exists():
            return candidate
    return None


def cell_for_scene(scene: Path) -> Cell | None:
    repo = find_repo(scene)
    if repo is None:
        return None
    wanted = "res://" + scene.resolve().relative_to(repo).as_posix()
    for region in sorted((repo / "data" / "regions").glob("*.tres")):
        for cell in parse_region(region):
            if cell.scene_path == wanted:
                return cell
    return None


def edge_of(cell: Cell, local: tuple[float, float]) -> str | None:
    x, z = local
    distance, edge = min((
        (abs(x + cell.width * 0.5), "west"), (abs(x - cell.width * 0.5), "east"),
        (abs(z + cell.depth * 0.5), "north"), (abs(z - cell.depth * 0.5), "south")))
    return edge if distance <= EDGE_TOLERANCE else None


def world_point(cell: Cell, local: tuple[float, float]) -> tuple[float, float]:
    return cell.center[0] + local[0], cell.center[1] + local[1]


def touches_edge(cell: Cell, point: tuple[float, float], source_edge: str) -> bool:
    x, z = point
    if source_edge == "west":
        return abs(x - cell.right) <= EDGE_TOLERANCE and cell.top - EDGE_TOLERANCE <= z <= cell.bottom + EDGE_TOLERANCE
    if source_edge == "east":
        return abs(x - cell.left) <= EDGE_TOLERANCE and cell.top - EDGE_TOLERANCE <= z <= cell.bottom + EDGE_TOLERANCE
    if source_edge == "north":
        return abs(z - cell.bottom) <= EDGE_TOLERANCE and cell.left - EDGE_TOLERANCE <= x <= cell.right + EDGE_TOLERANCE
    return abs(z - cell.top) <= EDGE_TOLERANCE and cell.left - EDGE_TOLERANCE <= x <= cell.right + EDGE_TOLERANCE


def opposite(edge: str) -> str:
    return {"west": "east", "east": "west", "north": "south", "south": "north"}[edge]


def seam_endpoints(cell: Cell):
    for route in cell.routes:
        for label, local in (("start", route.start), ("end", route.end)):
            if (edge := edge_of(cell, local)) is not None:
                yield route, label, edge, world_point(cell, local)


def validate_region(path: Path) -> int:
    cells = parse_region(path)
    repo = find_repo(path)
    if repo is None:
        print(f"{path}: cannot locate project.godot", file=sys.stderr)
        return 1
    failures = 0
    for cell in cells:
        if not (repo / cell.scene_path.removeprefix("res://")).exists():
            print(f"  MISSING {cell.cell_id}: {cell.scene_path}")
            failures += 1

    checked: set[tuple[str, str, str]] = set()
    for cell in cells:
        for route, label, edge, world in seam_endpoints(cell):
            # Arena's named collapsed breach is readable scenery, not a route into another cell.
            # Keeping this exception semantic makes any newly added open edge fail by default.
            if "breach" in route.name.lower():
                print(f"  EDGE_TERMINUS_OK {cell.cell_id}:{route.name}.{label} is an authored collapsed breach")
                continue
            neighbours = [other for other in cells if other != cell and touches_edge(other, world, edge)]
            if not neighbours:
                print(f"  OPEN_EDGE {cell.cell_id}:{route.name}.{label} reaches {edge} at {world} with no abutting cell")
                failures += 1
                continue
            neighbour = min(neighbours, key=lambda other: math.dist(other.center, world))
            matches = []
            for other_route, other_label, other_edge, other_world in seam_endpoints(neighbour):
                distance = math.dist(world, other_world)
                if other_edge == opposite(edge) and distance <= SEAM_TOLERANCE:
                    matches.append((other_route, other_label, distance))
            key = tuple(sorted((cell.cell_id, neighbour.cell_id))) + (f"{world[0]:.2f},{world[1]:.2f}",)
            if matches:
                if key not in checked:
                    other_route, other_label, distance = min(matches, key=lambda item: item[2])
                    print(f"  SEAM_OK {cell.cell_id}:{route.name}.{label} <-> {neighbour.cell_id}:{other_route.name}.{other_label} ({distance:.2f} m)")
                    checked.add(key)
            else:
                print(f"  SEAM_MISMATCH {cell.cell_id}:{route.name}.{label} reaches {neighbour.cell_id} at {world}, but it has no matching {opposite(edge)} opening")
                failures += 1
    print(f"{path}: {'PASS' if failures == 0 else f'FAIL ({failures})'}")
    return failures


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    sys.exit(1 if validate_region(Path(sys.argv[1])) else 0)
