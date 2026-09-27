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

⚠️ EVERY ROAD AND PAD IS SPEC DATA (2026-09 world rebuild). Interior streets and pads used to be
lifted byte for byte out of git revision f5bde08, which pinned every settlement to the metre and made
moving one a git archaeology exercise (plus a shallow-clone fallback that broke CI for three days).
They are now named `Route`/`Yard` entries in the region specs, so a place moves with its numbers.
"""

from __future__ import annotations

import argparse
import difflib
import re
import subprocess
import sys
import math
from dataclasses import dataclass, field, replace
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REGIONS = ROOT / "data" / "regions"

# --------------------------------------------------------------------------------------------------
# Spec types
# --------------------------------------------------------------------------------------------------

@dataclass(frozen=True)
class Mound:
    """Radial landform. `flat` 0 adds `h`; 1 levels the ground to it.

    `irr` bends the boundary out of its ellipse. Leave it None and the generator applies the house
    rule: NATURAL geography (flat == 0) gets DEFAULT_IRREGULARITY, MADE surfaces (flat > 0.5 - pit
    floors, terraces, pads) get none, because a made thing should look made.
    """
    at: tuple[float, float]
    ext: tuple[float, float]
    h: float
    fall: float = 0.7
    flat: float = 0.0
    rot: float = 0.0
    irr: float | None = None


@dataclass(frozen=True)
class Ridge:
    """Swept landform: a ridgeline, scarp, embankment, gully or channel. See Mound for `irr`."""
    a: tuple[float, float]
    b: tuple[float, float]
    half: float
    h: float
    fall: float = 0.6
    flat: float = 0.0
    irr: float | None = None


@dataclass(frozen=True)
class Route:
    """A cell-local road segment authored by this file (approaches and outskirts)."""
    a: tuple[float, float]
    b: tuple[float, float]
    width: float = 5.0
    shoulder: float = 2.0
    # The sub-resource id. None takes the positional Path_<cell>_ap<n> id; a name is how an interior
    # street keeps a stable, greppable identity (and how check_region_seams exempts a "breach").
    name: str | None = None


@dataclass(frozen=True)
class Yard:
    """A cell-local levelled working surface authored by this file."""
    at: tuple[float, float]
    ext: tuple[float, float]
    feather: float = 2.5
    blend: float = 0.8
    # WARNING: METRES ABOVE THE GENERATED GROUND UNDER THIS YARD'S OWN CENTRE, never an
    # absolute world Y. 0 means "level with the country here", which is what almost every
    # yard wants and what makes a settlement follow its hillside instead of stepping off it.
    elevation: float | None = 0.0
    # The sub-resource id. None takes the positional Area_<cell>_y<n> id.
    name: str | None = None


@dataclass(frozen=True)
class Water:
    """A declared body of standing water. See WorldWaterResource: the extent may be drawn
    generously larger than the basin, because the shoreline comes from the terrain."""
    at: tuple[float, float]
    ext: tuple[float, float]
    y: float = 0.05
    ident: str = "Water"
    shallow: tuple[float, float, float] = (0.22, 0.34, 0.33)
    deep: tuple[float, float, float] = (0.05, 0.13, 0.17)
    opaque: float = 2.2


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
    waters: tuple[Water, ...] = ()
    scatter: str | None = None             # id of a shared scatter profile
    biome: str | None = None               # data/biomes/<name>.tres, overriding the region default
    new_scene: str | None = None           # body of a transitional cell scene to create
    # ⚠️ WHERE THIS CELL'S AUTHORED CONTENT SITS IN THE WORLD (2026-09 world rebuild). Settlements were
    # authored around their cell's own origin, so every place stood on a cell centre and the realm
    # read as a lattice. `origin` is the world point the content frame is placed at; every local
    # landform, route, yard, water body and seam reach point in this cell is shifted by
    # origin - center, and tools/shift_cell_content.py moves the scene's nodes by the same amount.
    # None keeps content centred (the right thing for an empty cell).
    origin: tuple[float, float] | None = None

    @property
    def offset(self) -> tuple[float, float]:
        if self.origin is None:
            return (0.0, 0.0)
        return (round(self.origin[0] - self.center[0], 3), round(self.origin[1] - self.center[1], 3))

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
# World-space authoring (2026-09 world rebuild)
# --------------------------------------------------------------------------------------------------
#
# ⚠️ A REALM IS DESIGNED IN WORLD SPACE AND STORED IN CELLS. A mountain range, a river valley and a
# road between two towns do not know where the streaming lattice is, and authoring them cell by cell
# is exactly how every road came to cross every seam at its midpoint. Author geography and roads as
# world coordinates here; the generator assigns each landform to the cell that contains it and splits
# each road at every edge it crosses, generating the seam points both cells derive from.

def _shift(item, dx: float, dz: float):
    if not (dx or dz):
        return item
    move = lambda p: (round(p[0] + dx, 3), round(p[1] + dz, 3))
    if isinstance(item, Mound):
        return replace(item, at=move(item.at))
    if isinstance(item, Ridge):
        return replace(item, a=move(item.a), b=move(item.b))
    if isinstance(item, Route):
        return replace(item, a=move(item.a), b=move(item.b))
    if isinstance(item, (Yard, Water)):
        return replace(item, at=move(item.at))
    raise TypeError(item)


def apply_origins(cells: list[Cell], seams: list[Seam]) -> tuple[list[Cell], list[Seam]]:
    """Moves every cell's content frame to its `origin` (see Cell.origin)."""
    by_key = {c.key: c for c in cells}
    moved = []
    for cell in cells:
        dx, dz = cell.offset
        moved.append(replace(
            cell,
            landforms=tuple(_shift(f, dx, dz) for f in cell.landforms),
            routes=tuple(_shift(r, dx, dz) for r in cell.routes),
            yards=tuple(_shift(y, dx, dz) for y in cell.yards),
            waters=tuple(_shift(w, dx, dz) for w in cell.waters)))
    shifted_seams = []
    for seam in seams:
        ax, az = by_key[seam.a].offset
        bx, bz = by_key[seam.b].offset
        shifted_seams.append(replace(
            seam, reach_a=(seam.reach_a[0] + ax, seam.reach_a[1] + az),
            reach_b=(seam.reach_b[0] + bx, seam.reach_b[1] + bz)))
    return moved, shifted_seams


def cell_containing(cells: list[Cell], x: float, z: float) -> Cell | None:
    for cell in cells:
        if cell.left - 1e-6 <= x <= cell.right + 1e-6 and cell.top - 1e-6 <= z <= cell.bottom + 1e-6:
            return cell
    return None


def place_world(cells: list[Cell], items) -> list[Cell]:
    """Adds world-space landforms, yards and waters to the cells that contain their centres."""
    extra: dict[str, list] = {c.key: [] for c in cells}
    issues = []
    for item in items:
        if isinstance(item, Ridge):
            cx, cz = (item.a[0] + item.b[0]) / 2, (item.a[1] + item.b[1]) / 2
        else:
            cx, cz = item.at
        cell = cell_containing(cells, cx, cz)
        if cell is None:
            raise ValueError(f"world item at ({cx}, {cz}) is outside the lattice: {item}")
        extra[cell.key].append(_shift(item, -cell.center[0], -cell.center[1]))
    out = []
    for cell in cells:
        forms = [i for i in extra[cell.key] if isinstance(i, (Mound, Ridge))]
        yards = [i for i in extra[cell.key] if isinstance(i, Yard)]
        waters = [i for i in extra[cell.key] if isinstance(i, Water)]
        out.append(replace(cell, landforms=tuple(cell.landforms) + tuple(forms),
                           yards=tuple(cell.yards) + tuple(yards),
                           waters=tuple(cell.waters) + tuple(waters)))
    return out


@dataclass(frozen=True)
class Road:
    """A world-space road: a polyline the generator splits at every cell edge it crosses and into
    pieces no longer than `step`, so each piece grades between its own ground rather than cutting a
    straight trench across a hill."""
    points: tuple[tuple[float, float], ...]
    width: float = 5.0
    shoulder: float = 2.0
    step: float = 40.0


def realize_roads(cells: list[Cell], roads: list[Road]) -> tuple[list[Cell], list[str]]:
    """Appends each road's per-cell pieces to its cells. Returns the cells and any issues."""
    xs = sorted({round(v, 3) for c in cells for v in (c.left, c.right)})
    zs = sorted({round(v, 3) for c in cells for v in (c.top, c.bottom)})
    pieces: dict[str, list[Route]] = {c.key: [] for c in cells}
    issues: list[str] = []
    for road in roads:
        for (ax, az), (bx, bz) in zip(road.points, road.points[1:]):
            ts = {0.0, 1.0}
            if bx != ax:
                ts |= {(x - ax) / (bx - ax) for x in xs if 0 < (x - ax) / (bx - ax) < 1}
            if bz != az:
                ts |= {(z - az) / (bz - az) for z in zs if 0 < (z - az) / (bz - az) < 1}
            ordered = sorted(ts)
            points = [(ax + (bx - ax) * t, az + (bz - az) * t) for t in ordered]
            # Keep only the points where the road actually changes cell: the candidate breaks include
            # every column edge of EVERY row, and a split at another row's edge would leave a piece
            # end a hair from this cell's edge that the seam checker rightly calls an open road.
            owners = [cell_containing(cells, (p[0] + q[0]) / 2, (p[1] + q[1]) / 2)
                      for p, q in zip(points, points[1:])]
            kept = [points[0]]
            for i in range(1, len(points) - 1):
                if owners[i - 1] is not owners[i]:
                    kept.append(points[i])
            kept.append(points[-1])
            points = kept
            for (px, pz), (qx, qz) in zip(points, points[1:]):
                length = math.hypot(qx - px, qz - pz)
                if length < 0.5:
                    continue
                mx, mz = (px + qx) / 2, (pz + qz) / 2
                cell = cell_containing(cells, mx, mz)
                if cell is None:
                    issues.append(f"road piece ({px:.1f},{pz:.1f})->({qx:.1f},{qz:.1f}) leaves the lattice")
                    continue
                for corner_x in (cell.left, cell.right):
                    for corner_z in (cell.top, cell.bottom):
                        for ex, ez in ((px, pz), (qx, qz)):
                            if math.hypot(ex - corner_x, ez - corner_z) < 3.0:
                                issues.append(f"road crosses the lattice within 3 m of a cell corner at "
                                              f"({corner_x}, {corner_z}); move a vertex")
                parts = max(1, math.ceil(length / road.step))
                cuts = [0.0]
                for i in range(1, parts):
                    cx, cz = px + (qx - px) * i / parts, pz + (qz - pz) * i / parts
                    near_edge = min(abs(cx - cell.left), abs(cx - cell.right),
                                    abs(cz - cell.top), abs(cz - cell.bottom)) < 1.5
                    if not near_edge:
                        cuts.append(i / parts)
                cuts.append(1.0)
                for s, e in zip(cuts, cuts[1:]):
                    sx, sz = px + (qx - px) * s, pz + (qz - pz) * s
                    ex, ez = px + (qx - px) * e, pz + (qz - pz) * e
                    pieces[cell.key].append(Route(
                        (round(sx - cell.center[0], 2), round(sz - cell.center[1], 2)),
                        (round(ex - cell.center[0], 2), round(ez - cell.center[1], 2)),
                        road.width, road.shoulder))
    out = [replace(c, routes=tuple(c.routes) + tuple(pieces[c.key])) for c in cells]
    return out, issues


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
# Emission
# --------------------------------------------------------------------------------------------------

def color(rgb: tuple[float, float, float]) -> str:
    return f"Color({rgb[0]}, {rgb[1]}, {rgb[2]}, 1)"


BIOME_DIR = "res://data/biomes"

# How far a NATURAL landform's boundary is bent out of its ellipse by default. See
# WorldLandformResource.Irregularity: 0 is a compass-drawn shape, 0.45 is a broken ridgeline. A
# MADE surface (flat > 0.5) gets none, and any landform may override with irr=.
DEFAULT_IRREGULARITY = 0.26
NEWLINE = chr(10)


def wire_biomes(header: str, environment: str, cells: list[Cell], default: str) -> tuple[str, str]:
    """Splice a [ext_resource] line per referenced biome into the header and reference them.

    ⚠️ THE HEADER'S load_steps IS REWRITTEN, NOT HAND-COUNTED. Godot treats load_steps as a hint and
    survives a wrong one, which is exactly why a hand-maintained count rots silently — the file loads,
    the editor renumbers it on the next save, and the generated output stops matching --check.
    """
    used: dict[str, str] = {}
    for name in [default] + [c.biome for c in cells if c.biome]:
        if name and name not in used:
            used[name] = f"{20 + len(used)}_biome_{name.lower()}"

    lines = header.splitlines()
    last = max(i for i, line in enumerate(lines) if line.startswith("[ext_resource"))
    injected = [f'[ext_resource type="Resource" path="{BIOME_DIR}/{n}.tres" id="{i}"]'
                for n, i in used.items()]
    lines[last + 1:last + 1] = injected
    steps = sum(1 for line in lines if line.startswith("[ext_resource")) + 1
    lines = [re.sub(r"load_steps=\d+", f"load_steps={steps}", line, count=1)
             if line.startswith("[gd_resource") else line for line in lines]

    marker = 'script = ExtResource("3_environment")'
    environment = environment.replace(
        marker,
        marker + NEWLINE + 'Biome = ExtResource("%s")' % used[default])
    for cell in cells:
        cell.biome_ref = used[cell.biome] if cell.biome else None
    return NEWLINE.join(lines), environment


def emit(region_key: str, header: str, cells: list[Cell], seams: list[Seam],
         environment: str, budget: str, resource: str,
         scatter_blocks: str, default_biome: str = "TemperateLowland") -> str:
    routed: dict[str, list[Route]] = {c.key: list(c.routes) for c in cells}
    by_key = {c.key: c for c in cells}
    for seam in seams:
        a, b = by_key[seam.a], by_key[seam.b]
        routed[seam.a].append(Route(seam.reach_a, local(a, seam.at), seam.width, seam.shoulder))
        routed[seam.b].append(Route(local(b, seam.at), seam.reach_b, seam.width, seam.shoulder))

    header, environment = wire_biomes(header, environment, cells, default_biome)
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
                # A landform that LEVELS ground states a target height, and a target authored as an
                # absolute world Y stops meaning what it said the moment real geography appears under
                # it. `h` on a levelling form is therefore metres above the generated country at its
                # own centre - "a twelve-metre shelf", not "a shelf whose top is at y=12". Additive
                # forms (flat 0) already follow their ground and are untouched.
                #
                # The same test that decides irregularity below decides this, and for the same
                # reason: over 0.5 is a made thing, under it is a piece of landscape.
                if form.flat > 0.5:
                    out.append("ElevationMode = 1")
            irregularity = form.irr if form.irr is not None else (
                0.0 if form.flat > 0.5 else DEFAULT_IRREGULARITY)
            if irregularity:
                out.append(f"Irregularity = {irregularity}")
            out.append("")

        path_ids: list[str] = []
        unnamed = 0
        for route in routed[cell.key]:
            if route.name:
                rid = route.name
            else:
                rid = f"Path_{cell.key}_ap{unnamed}"
                unnamed += 1
            path_ids.append(rid)
            out.append(f'[sub_resource type="Resource" id="{rid}"]')
            out.append('script = ExtResource("6_path")')
            out.append(f"Start = Vector2({route.a[0]}, {route.a[1]})")
            out.append(f"End = Vector2({route.b[0]}, {route.b[1]})")
            out.append(f"Width = {route.width}")
            out.append(f"Shoulder = {route.shoulder}")
            out.append("")

        area_ids: list[str] = []
        unnamed = 0
        for yard in cell.yards:
            # ⚠️ AN ELEVATION IS METRES ABOVE THE GENERATED GROUND UNDER THE PAD'S OWN CENTRE, never a
            # world Y (NOW.md invariant 23). Every pad is RelativeToBase; `elevation=None` omits the
            # field, which the resource reads as 0.
            if yard.name:
                yid = yard.name
            else:
                yid = f"Area_{cell.key}_y{unnamed}"
                unnamed += 1
            area_ids.append(yid)
            out.append(f'[sub_resource type="Resource" id="{yid}"]')
            out.append('script = ExtResource("7_area")')
            out.append(f"Center = Vector2({yard.at[0]}, {yard.at[1]})")
            out.append(f"Radius = Vector2({yard.ext[0]}, {yard.ext[1]})")
            out.append(f"Feather = {yard.feather}")
            out.append(f"SurfaceBlend = {yard.blend}")
            if yard.name:
                out.append("ElevationMode = 1")
                if yard.elevation is not None:
                    out.append(f"Elevation = {yard.elevation}")
            else:
                out.append(f"Elevation = {yard.elevation}")
                out.append("ElevationMode = 1")
            out.append("")

        water_ids: list[str] = []
        for i, body in enumerate(cell.waters):
            wid = f"Water_{cell.key}_{i}"
            water_ids.append(wid)
            out.append(f'[sub_resource type="Resource" id="{wid}"]')
            out.append('script = ExtResource("12_water")')
            out.append(f'Id = "{body.ident}"')
            out.append(f"Center = Vector2({body.at[0]}, {body.at[1]})")
            out.append(f"Extent = Vector2({body.ext[0]}, {body.ext[1]})")
            out.append(f"SurfaceY = {body.y}")
            # A waterline is a target height, so it is metres above the generated country under the
            # body rather than an absolute world Y - see WorldWaterResource.ElevationMode. Authored
            # as an absolute it drowned Hollowreach's own shoreline the moment the realm got real
            # elevation, because the ground under the fen is eight metres lower than the number was
            # written against.
            out.append("ElevationMode = 1")
            out.append(f"ShallowColor = {color(body.shallow)}")
            out.append(f"DeepColor = {color(body.deep)}")
            out.append(f"OpaqueDepth = {body.opaque}")
            out.append("")

        out.append(f'[sub_resource type="Resource" id="Presentation_{cell.key}"]')
        out.append('script = ExtResource("4_presentation")')
        out.append(f"Width = {cell.size[0]}")
        out.append(f"Depth = {cell.size[1]}")
        out.append(f"Seed = {cell.seed}")
        if getattr(cell, "biome_ref", None):
            out.append(f'Biome = ExtResource("{cell.biome_ref}")')
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
        if water_ids:
            out.append('Water = Array[ExtResource("12_water")]([' +
                       ", ".join(f'SubResource("{i}")' for i in water_ids) + "])")
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
    out.append(anchor_points(resource.replace("@CELLS@", cell_list), cells))
    return "\n".join(out).rstrip() + "\n"


def _num(value: float) -> str:
    value = round(value, 3)
    return str(int(value)) if float(value).is_integer() else str(value)


def anchor_points(text: str, cells: list[Cell]) -> str:
    """Resolve region-level world points from the cells they belong to.

    ⚠️ A REGION POINT IS AN OFFSET FROM A CELL, NEVER A WORLD LITERAL (2026-09 world rebuild). The
    spawn, portal and safe-zone centre used to be typed as world Vector3s beside the cells, so moving
    the town or the Crossway silently left the player spawning in the old place. Write them as
      @CELL(key, dx, y, dz)@            -> Vector3(center.x + dx, y, center.z + dz)
      @ORIGIN(key, dx, y, dz)@          -> the same, from the cell's content origin (Cell.origin)
      @WORLD(x, y, z)@                  -> a world point the spec authors directly (world geometry)
      @BOUNDS(margin, y, height)@       -> the lattice's AABB grown by `margin` on x/z
    """
    by_key = {c.key: c for c in cells}

    def cell_point(match: re.Match) -> str:
        key, dx, y, dz = [part.strip() for part in match.group(1).split(",")]
        cell = by_key[key]
        return (f"Vector3({_num(cell.center[0] + float(dx))}, {_num(float(y))}, "
                f"{_num(cell.center[1] + float(dz))})")

    def bounds(match: re.Match) -> str:
        margin, y, height = (float(part) for part in match.group(1).split(","))
        left = min(c.left for c in cells) - margin
        top = min(c.top for c in cells) - margin
        right = max(c.right for c in cells) + margin
        bottom = max(c.bottom for c in cells) + margin
        return (f"AABB({_num(left)}, {_num(y)}, {_num(top)}, {_num(right - left)}, "
                f"{_num(height)}, {_num(bottom - top)})")

    def origin_point(match: re.Match) -> str:
        key, dx, y, dz = [part.strip() for part in match.group(1).split(",")]
        cell = by_key[key]
        ox, oz = cell.origin if cell.origin is not None else cell.center
        return f"Vector3({_num(ox + float(dx))}, {_num(float(y))}, {_num(oz + float(dz))})"

    def world_point(match: re.Match) -> str:
        x, y, z = (float(part) for part in match.group(1).split(","))
        return f"Vector3({_num(x)}, {_num(y)}, {_num(z)})"

    text = re.sub(r"@ORIGIN\(([^)]*)\)@", origin_point, text)
    text = re.sub(r"@WORLD\(([^)]*)\)@", world_point, text)
    text = re.sub(r"@CELL\(([^)]*)\)@", cell_point, text)
    return re.sub(r"@BOUNDS\(([^)]*)\)@", bounds, text)


def write(path: Path, text: str, check: bool) -> bool:
    current = path.read_text(encoding="utf-8") if path.exists() else ""
    if current == text:
        return False
    if check:
        # ⚠️ "would change" ON ITS OWN IS NOT AN ACTIONABLE FAILURE, and it cost this repo three
        # days of a red required CI job that nobody could act on: the gate said a generated file
        # was stale, every local run said it was current, and the message carried nothing to tell
        # the two apart. A --check that fails on a machine you cannot reach has to say WHAT differs.
        print(f"would change: {path.relative_to(ROOT)}")
        for line in difflib.unified_diff(
                current.splitlines(), text.splitlines(),
                fromfile=f"{path.name} (on disk)", tofile=f"{path.name} (generated)",
                lineterm="", n=1):
            print(f"  {line}")
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
    from region_spec_ashen import build_ashen          # noqa: E402
    from region_spec_sunspire import build_sunspire    # noqa: E402
    from region_spec_pale_concord import build_pale_concord  # noqa: E402
    from region_spec_celestial import build_celestial  # noqa: E402

    changed = False
    issues: list[str] = []
    for builder, filename in ((build_ember, "EmberCrown.tres"),
                              (build_frostfang, "FrostfangReach.tres"),
                              (build_ashen, "AshenWilds.tres"),
                              (build_sunspire, "Sunspire.tres"),
                              (build_pale_concord, "PaleConcord.tres"),
                              (build_celestial, "CelestialRealm.tres")):
        text, problems = builder()
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
