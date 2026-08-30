#!/usr/bin/env python3
"""Emit data/regions/*.tres from the macro world layout (the 2026-08-29 geography overhaul).

    python tools/gen_regions.py            # write the region resources
    python tools/gen_regions.py --check    # exit 1 if either file would change

WHY THIS IS GENERATED AND THE OTHER .tres ARE NOT. A region resource is the one place in the repo
where the numbers are ARITHMETIC rather than taste: a cell's envelope, its centre, and its
neighbours' envelopes and centres have to tile a rectangle exactly, and every road that reaches a
seam has to meet a matching endpoint on the far side of it at the same world point. Three shipped
seam defects (NOW.md invariant 11) were all somebody doing that arithmetic in their head. Here the
lattice is declared as row bands and column splits, the tiling is CHECKED before anything is
written, and every seam route is authored ONCE as a world point that both cells derive their local
endpoint from. It is impossible to author half of a seam in this file.

The prose that used to live in the .tres headers lives in NOTES below and is emitted with the file,
because the reason a cell is where it is has to survive the next person who wants to move it.

⚠️ THE INTERIOR CIRCULATION OF EVERY EXISTING CELL IS NOT AUTHORED HERE. Paths and ground areas that
predate this overhaul are lifted verbatim out of the previous revision's .tres (see LEGACY) so the
2026-08-28 layout rebuild's work — the Coilyard, the Crookway, the Kingsway's S, Emberdeep's working
loop, the Wilds North fork, Tarn's spit, Hollowreach's channels, the corrie throat, the arena's gate
and breach — is preserved to the metre. What this file adds around them is geography and approach.
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REGIONS = ROOT / "data" / "regions"

# The revision the untouched interior circulation is lifted from. Bumping this is a deliberate act:
# it re-imports whatever that commit says the Coilyard and the Crookway are.
LEGACY_REV = "f5bde08"


# --------------------------------------------------------------------------------------------------
# Spec types
# --------------------------------------------------------------------------------------------------

@dataclass(frozen=True)
class Mound:
    """Radial landform. `flat` 0 adds `h`; 1 levels the ground to it."""
    at: tuple[float, float]
    ext: tuple[float, float]
    h: float
    fall: float = 0.7
    flat: float = 0.0
    rot: float = 0.0


@dataclass(frozen=True)
class Ridge:
    """Swept landform: a ridgeline, scarp, embankment, gully or channel."""
    a: tuple[float, float]
    b: tuple[float, float]
    half: float
    h: float
    fall: float = 0.6
    flat: float = 0.0


@dataclass(frozen=True)
class Route:
    """A cell-local road segment authored by this file (approaches and outskirts)."""
    a: tuple[float, float]
    b: tuple[float, float]
    width: float = 5.0
    shoulder: float = 2.0


@dataclass(frozen=True)
class Yard:
    """A cell-local levelled working surface authored by this file."""
    at: tuple[float, float]
    ext: tuple[float, float]
    feather: float = 2.5
    blend: float = 0.8
    elevation: float = 0.0


@dataclass
class Cell:
    key: str                      # short id used for sub-resource names
    cell_id: str                  # the stable gameplay id — NEVER changes
    scene: str
    center: tuple[float, float]   # world x, z
    size: tuple[float, float]     # width, depth
    resolution: int
    seed: int
    note: str = ""
    tint: tuple[float, float, float] | None = None
    tint_strength: float = 0.0
    safe_radius: float = 0.0
    surplus: tuple[str, ...] = ()
    demand: tuple[str, ...] = ()
    shocks: tuple[str, ...] = ()
    landforms: tuple = ()
    routes: tuple[Route, ...] = ()
    yards: tuple[Yard, ...] = ()
    legacy_paths: tuple[str, ...] = ()     # sub-resource ids lifted from LEGACY_REV
    legacy_areas: tuple[str, ...] = ()
    area_elevation: dict[str, float] = field(default_factory=dict)
    scatter: str | None = None             # id of a shared scatter profile
    new_scene: str | None = None           # body of a transitional cell scene to create

    @property
    def left(self) -> float: return self.center[0] - self.size[0] / 2
    @property
    def right(self) -> float: return self.center[0] + self.size[0] / 2
    @property
    def top(self) -> float: return self.center[1] - self.size[1] / 2
    @property
    def bottom(self) -> float: return self.center[1] + self.size[1] / 2


@dataclass(frozen=True)
class Seam:
    """One road crossing, authored once as a WORLD point both cells derive their endpoint from."""
    a: str                       # cell key
    b: str                       # cell key
    at: tuple[float, float]      # world x, z on the shared edge
    reach_a: tuple[float, float] # the interior point in cell A the crossing is joined to (local)
    reach_b: tuple[float, float]
    width: float = 5.0
    shoulder: float = 2.0


def local(cell: Cell, world: tuple[float, float]) -> tuple[float, float]:
    return (round(world[0] - cell.center[0], 3), round(world[1] - cell.center[1], 3))


# --------------------------------------------------------------------------------------------------
# Lattice validation
# --------------------------------------------------------------------------------------------------

def check_tiling(name: str, cells: list[Cell], rows: list[tuple[float, float]],
                 extent_x: tuple[float, float]) -> list[str]:
    """Every cell sits in exactly one row band and the bands tile the extent with no gap."""
    issues: list[str] = []
    for lo, hi in rows:
        band = sorted((c for c in cells if abs(c.top - lo) < 0.001 and abs(c.bottom - hi) < 0.001),
                      key=lambda c: c.left)
        if not band:
            issues.append(f"{name}: row band z {lo}..{hi} has no cells")
            continue
        cursor = extent_x[0]
        for cell in band:
            if abs(cell.left - cursor) > 0.001:
                issues.append(
                    f"{name}: {cell.cell_id} starts at x {cell.left} but the row reached {cursor}")
            cursor = cell.right
        if abs(cursor - extent_x[1]) > 0.001:
            issues.append(f"{name}: row band z {lo}..{hi} ends at x {cursor}, not {extent_x[1]}")
    banded = sum(len([c for c in cells if abs(c.top - lo) < 0.001 and abs(c.bottom - hi) < 0.001])
                 for lo, hi in rows)
    if banded != len(cells):
        issues.append(f"{name}: {len(cells) - banded} cell(s) are not in any row band")
    return issues


def check_seams(name: str, cells: dict[str, Cell], seams: list[Seam]) -> list[str]:
    issues: list[str] = []
    for seam in seams:
        for key in (seam.a, seam.b):
            cell = cells[key]
            x, z = seam.at
            on_x = abs(x - cell.left) < 0.001 or abs(x - cell.right) < 0.001
            on_z = abs(z - cell.top) < 0.001 or abs(z - cell.bottom) < 0.001
            inside = (cell.left - 0.001 <= x <= cell.right + 0.001 and
                      cell.top - 0.001 <= z <= cell.bottom + 0.001)
            if not (inside and (on_x or on_z)):
                issues.append(f"{name}: seam {seam.a}<->{seam.b} at {seam.at} is not on {key}'s edge")
    return issues


def check_envelopes(name: str, cells: list[Cell], routed: dict[str, list[Route]]) -> list[str]:
    issues: list[str] = []
    for cell in cells:
        half_w, half_d = cell.size[0] / 2, cell.size[1] / 2
        for route in routed.get(cell.key, []):
            for point in (route.a, route.b):
                if abs(point[0]) > half_w + 0.01 or abs(point[1]) > half_d + 0.01:
                    issues.append(f"{name}: {cell.cell_id} route point {point} is outside its envelope")
        for yard in cell.yards:
            if abs(yard.at[0]) > half_w + 0.01 or abs(yard.at[1]) > half_d + 0.01:
                issues.append(f"{name}: {cell.cell_id} yard {yard.at} is outside its envelope")
    return issues


# --------------------------------------------------------------------------------------------------
# Legacy import
# --------------------------------------------------------------------------------------------------

_BLOCK = re.compile(r'^\[sub_resource[^\]]*id="([^"]+)"\]\n(.*?)(?=^\[|\Z)', re.M | re.S)


def legacy_blocks(filename: str) -> dict[str, str]:
    text = subprocess.run(
        ["git", "show", f"{LEGACY_REV}:data/regions/{filename}"],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", check=True).stdout
    return {m.group(1): m.group(2).rstrip() + "\n" for m in _BLOCK.finditer(text)}


def retype(block: str, script_id: str) -> str:
    """Legacy blocks name their script by ext_resource id; the new files renumber them."""
    return re.sub(r'^script = ExtResource\("[^"]+"\)$', f'script = ExtResource("{script_id}")',
                  block, flags=re.M)


# --------------------------------------------------------------------------------------------------
# Emission
# --------------------------------------------------------------------------------------------------

def color(rgb: tuple[float, float, float]) -> str:
    return f"Color({rgb[0]}, {rgb[1]}, {rgb[2]}, 1)"


def emit(region_key: str, header: str, cells: list[Cell], seams: list[Seam],
         legacy: dict[str, str], environment: str, budget: str, resource: str,
         scatter_blocks: str) -> str:
    routed: dict[str, list[Route]] = {c.key: list(c.routes) for c in cells}
    by_key = {c.key: c for c in cells}
    for seam in seams:
        a, b = by_key[seam.a], by_key[seam.b]
        routed[seam.a].append(Route(seam.reach_a, local(a, seam.at), seam.width, seam.shoulder))
        routed[seam.b].append(Route(local(b, seam.at), seam.reach_b, seam.width, seam.shoulder))

    out: list[str] = [header, ""]
    out.append(environment)
    out.append(budget)
    if scatter_blocks:
        out.append(scatter_blocks)

    for cell in cells:
        out.append(f"; ---------------------------------------------------------------------------------")
        out.append(f"; {cell.cell_id}   centre ({cell.center[0]}, {cell.center[1]})   "
                   f"{cell.size[0]:g} x {cell.size[1]:g}   "
                   f"x {cell.left:g}..{cell.right:g}  z {cell.top:g}..{cell.bottom:g}")
        if cell.note:
            for line in cell.note.strip().splitlines():
                out.append(f"; {line.strip()}")
        out.append("")

        landform_ids: list[str] = []
        for i, form in enumerate(cell.landforms):
            fid = f"Land_{cell.key}_{i}"
            landform_ids.append(fid)
            out.append(f'[sub_resource type="Resource" id="{fid}"]')
            out.append('script = ExtResource("8_landform")')
            if isinstance(form, Mound):
                out.append("Shape = 0")
                out.append(f"Center = Vector2({form.at[0]}, {form.at[1]})")
                out.append(f"Extent = Vector2({form.ext[0]}, {form.ext[1]})")
                if form.rot:
                    out.append(f"Rotation = {form.rot}")
            else:
                out.append("Shape = 1")
                out.append(f"Center = Vector2({form.a[0]}, {form.a[1]})")
                out.append(f"End = Vector2({form.b[0]}, {form.b[1]})")
                out.append(f"Extent = Vector2({form.half}, {form.half})")
            out.append(f"Height = {form.h}")
            out.append(f"Falloff = {form.fall}")
            if form.flat:
                out.append(f"Flatten = {form.flat}")
            out.append("")

        path_ids: list[str] = []
        for pid in cell.legacy_paths:
            path_ids.append(pid)
            out.append(f'[sub_resource type="Resource" id="{pid}"]')
            out.append(retype(legacy[pid], "6_path").rstrip())
            out.append("")
        for i, route in enumerate(routed[cell.key]):
            rid = f"Path_{cell.key}_ap{i}"
            path_ids.append(rid)
            out.append(f'[sub_resource type="Resource" id="{rid}"]')
            out.append('script = ExtResource("6_path")')
            out.append(f"Start = Vector2({route.a[0]}, {route.a[1]})")
            out.append(f"End = Vector2({route.b[0]}, {route.b[1]})")
            out.append(f"Width = {route.width}")
            out.append(f"Shoulder = {route.shoulder}")
            out.append("")

        area_ids: list[str] = []
        for aid in cell.legacy_areas:
            area_ids.append(aid)
            body = retype(legacy[aid], "7_area").rstrip()
            if aid in cell.area_elevation:
                body += f"\nElevation = {cell.area_elevation[aid]}"
            out.append(f'[sub_resource type="Resource" id="{aid}"]')
            out.append(body)
            out.append("")
        for i, yard in enumerate(cell.yards):
            yid = f"Area_{cell.key}_y{i}"
            area_ids.append(yid)
            out.append(f'[sub_resource type="Resource" id="{yid}"]')
            out.append('script = ExtResource("7_area")')
            out.append(f"Center = Vector2({yard.at[0]}, {yard.at[1]})")
            out.append(f"Radius = Vector2({yard.ext[0]}, {yard.ext[1]})")
            out.append(f"Feather = {yard.feather}")
            out.append(f"SurfaceBlend = {yard.blend}")
            out.append(f"Elevation = {yard.elevation}")
            out.append("")

        out.append(f'[sub_resource type="Resource" id="Presentation_{cell.key}"]')
        out.append('script = ExtResource("4_presentation")')
        out.append(f"Width = {cell.size[0]}")
        out.append(f"Depth = {cell.size[1]}")
        out.append(f"Seed = {cell.seed}")
        if cell.tint:
            out.append(f"Tint = {color(cell.tint)}")
            out.append(f"TintStrength = {cell.tint_strength}")
        out.append(f"TopologyResolution = {cell.resolution}")
        if landform_ids:
            out.append('Landforms = Array[ExtResource("8_landform")]([' +
                       ", ".join(f'SubResource("{i}")' for i in landform_ids) + "])")
        if path_ids:
            out.append('Paths = Array[ExtResource("6_path")]([' +
                       ", ".join(f'SubResource("{i}")' for i in path_ids) + "])")
        if area_ids:
            out.append('GroundAreas = Array[ExtResource("7_area")]([' +
                       ", ".join(f'SubResource("{i}")' for i in area_ids) + "])")
        out.append("")

        out.append(f'[sub_resource type="Resource" id="Cell_{cell.key}"]')
        out.append('script = ExtResource("2_cell")')
        out.append(f'Id = "{cell.cell_id}"')
        out.append(f'ScenePath = "{cell.scene}"')
        out.append(f"Center = Vector3({cell.center[0]}, 0, {cell.center[1]})")
        out.append(f'Presentation = SubResource("Presentation_{cell.key}")')
        if cell.scatter:
            out.append(f'BiomeScatter = SubResource("{cell.scatter}")')
        if cell.safe_radius:
            out.append(f"SafeRadius = {cell.safe_radius}")
        for name, tags in (("Surplus", cell.surplus), ("Demand", cell.demand),
                           ("ShockTags", cell.shocks)):
            if tags:
                out.append(f'{name} = Array[String]([' + ", ".join(f'"{t}"' for t in tags) + "])")
        out.append("")

    cell_list = ", ".join(f'SubResource("Cell_{c.key}")' for c in cells)
    out.append(resource.replace("@CELLS@", cell_list))
    return "\n".join(out).rstrip() + "\n"


def write(path: Path, text: str, check: bool) -> bool:
    current = path.read_text(encoding="utf-8") if path.exists() else ""
    if current == text:
        return False
    if check:
        print(f"would change: {path.relative_to(ROOT)}")
        return True
    path.write_text(text, encoding="utf-8")
    print(f"wrote {path.relative_to(ROOT)}")
    return True


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    from region_spec_ember import build_ember          # noqa: E402
    from region_spec_frostfang import build_frostfang  # noqa: E402

    changed = False
    issues: list[str] = []
    for builder, filename in ((build_ember, "EmberCrown.tres"),
                              (build_frostfang, "FrostfangReach.tres")):
        text, problems = builder(legacy_blocks(filename))
        issues += problems
        changed |= write(REGIONS / filename, text, args.check)

    if issues:
        for issue in issues:
            print(f"LATTICE ERROR: {issue}", file=sys.stderr)
        return 2
    return 1 if (args.check and changed) else 0


if __name__ == "__main__":
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    raise SystemExit(main())
