#!/usr/bin/env python3
"""Write the placed structures of a district into its cell scenes (2026-09 world rebuild).

    python tools/compose_district.py            # (re)write every district in tools/district_layouts.py
    python tools/compose_district.py --check    # exit 1 if a scene is out of date with its layout

A settlement is designed in WORLD coordinates as a street plan (tools/district_layouts.py): which
building stands where, which way its door faces, and the levelled terraces it stands on. This tool
turns that plan into scene nodes under each cell's `Nav`, in the cell's own frame, and the region
spec turns the same plan's terraces into ground areas — one source for both, so a moved house can
never leave its pad behind.

Generated nodes are named `Dx_*` and their resources `dx_*`/`DxShape_*`; every run removes the previous
generation first, so the tool is idempotent and hand-authored nodes are never touched.
"""

from __future__ import annotations

import io
import math
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LF, CRLF = chr(10), chr(13) + chr(10)
sys.path.insert(0, str(ROOT / "tools"))

# Prefabs that carry their own collider (tools/compose_building.py output): scene, footprint (w, d).
COMPOSED = {
    "townhouse": ("res://scenes/props/bld_townhouse.tscn", (4.4, 6.4)),
    "townhouse_wide": ("res://scenes/props/bld_townhouse_wide.tscn", (8.4, 4.4)),
    "townhouse_balcony": ("res://scenes/props/bld_townhouse_balcony.tscn", (4.4, 6.4)),
    "shop_awning": ("res://scenes/props/bld_shop_awning.tscn", (4.4, 6.4)),
    "cottage_modular": ("res://scenes/props/bld_cottage_modular.tscn", (4.4, 4.4)),
    "cottage_shuttered": ("res://scenes/props/bld_cottage_shuttered.tscn", (4.4, 4.4)),
    "farmhouse_long": ("res://scenes/props/bld_farmhouse_long.tscn", (6.4, 4.4)),
    "workshop_open": ("res://scenes/props/bld_workshop_open.tscn", (6.4, 6.4)),
    "longhouse_stone": ("res://scenes/props/bld_longhouse_stone.tscn", (6.4, 8.4)),
    "inn_courtyard": ("res://scenes/props/bld_inn_courtyard.tscn", (8.4, 8.4)),
    "ruin_house": ("res://scenes/props/bld_ruin_house.tscn", (4.4, 4.4)),
    "ruin_tower": ("res://scenes/props/bld_ruin_tower.tscn", (4.4, 4.4)),
}

# Models without their own collider: scene, collider box (w, h, d).
BOXED = {
    "house_a": ("res://assets/models/architecture/bld_house_a.glb", (4.8, 7.5, 5.9)),
    "house_b": ("res://assets/models/architecture/bld_house_b.glb", (4.3, 6.8, 5.1)),
    "cottage": ("res://assets/models/architecture/bld_cottage.glb", (4.9, 4.2, 4.8)),
    "watch_tower": ("res://assets/models/props/prp_watch_tower.glb", (3.0, 9.0, 3.0)),
    "well": ("res://assets/models/props/prp_well.glb", (2.2, 2.5, 2.2)),
    "cart": ("res://assets/models/props/prp_cart.glb", (1.8, 1.6, 3.2)),
    "timber_stack": ("res://assets/models/props/prp_timber_stack.glb", (3.2, 1.4, 1.6)),
    "ruin_wall": ("res://assets/models/props/prp_ruin_wall.glb", (4.0, 3.0, 1.0)),
    "ruin_pillar": ("res://assets/models/props/prp_ruin_pillar.glb", (1.2, 4.0, 1.2)),
    "boulder": ("res://assets/models/props/prp_boulder_large.glb", (4.0, 3.0, 4.0)),
    "tent": ("res://assets/models/props/prp_tent.glb", (3.0, 2.4, 3.0)),
    "market_stand": ("res://assets/models/props/prp_market_stand.glb", (3.0, 2.6, 2.0)),
    "hay": ("res://assets/models/props/prp_hay.glb", (1.6, 1.4, 1.6)),
    "gate_palisade": ("res://assets/models/props/prp_gate_palisade.glb", (6.0, 4.0, 1.0)),
    # The Iron Citadel (rts castle, adopted at real size): a landmark hull, not an interior.
    "castle_fortress": ("res://assets/models/architecture/bld_castle_fortress.glb", (55.3, 35.2, 50.9)),
    # A cairn is waist-high stone the player walks into, so it stops them.
    "rock_cluster": ("res://assets/models/props/prp_rock_cluster.glb", (2.2, 1.4, 1.8)),
}

# Small props with no collider (they would snag the capsule and matter to nobody).
LOOSE = {
    "lamp_post": "res://assets/models/props/prp_lamp_post.glb",
    "barrel": "res://assets/models/props/prp_barrel.glb",
    "crate": "res://assets/models/props/prp_crate.glb",
    "sacks": "res://assets/models/props/prp_sacks.glb",
    "banner": "res://assets/models/props/prp_banner_guild.glb",
    "campfire": "res://assets/models/props/prp_campfire.glb",
    "brazier": "res://assets/models/props/prp_brazier.glb",
    "fence": "res://assets/models/props/prp_fence.glb",
    "bench": "res://assets/models/props/prp_bench.glb",
}


def footprint(kind: str) -> tuple[float, float]:
    if kind in COMPOSED:
        return COMPOSED[kind][1]
    if kind in BOXED:
        w, _, d = BOXED[kind][1]
        return (w, d)
    return (1.0, 1.0)


def fmt(v: float) -> str:
    v = round(v, 4)
    return str(int(v)) if float(v).is_integer() else str(v)


def basis(yaw_degrees: float, scale: float = 1.0) -> str:
    r = math.radians(yaw_degrees)
    c, s = math.cos(r) * scale, math.sin(r) * scale
    # Godot Transform3D basis columns: x=(c,0,-s), y=(0,1,0), z=(s,0,c) for a rotation about +Y.
    return f"{fmt(c)}, 0, {fmt(-s)}, 0, {fmt(scale)}, 0, {fmt(s)}, 0, {fmt(c)}"


def strip_generated(text: str) -> str:
    text = re.sub(r'^\[ext_resource [^\n]*id="dx_[^"]+"\]\n', "", text, flags=re.M)
    text = re.sub(r'^\[sub_resource type="BoxShape3D" id="DxShape_[^"]+"\]\nsize = [^\n]*\n\n?', "", text, flags=re.M)
    out, skip = [], False
    for block in re.split(r"(?m)^(?=\[)", text):
        header = block.split("\n", 1)[0]
        if header.startswith("[node "):
            name = re.search(r'name="([^"]+)"', header).group(1)
            parent = re.search(r'parent="([^"]*)"', header)
            parent = parent.group(1) if parent else ""
            skip = name.startswith("Dx_") or "/Dx_" in parent or parent.startswith("Dx_") \
                or parent.startswith("Nav/Dx_")
            if skip:
                continue
        elif skip and not header.startswith("["):
            continue
        out.append(block)
    return "".join(out)


def generated_pins(text: str) -> list[str]:
    """Map-pin blocks parented to generated nodes. They belong to tools/gen_map_locations.py, not to
    this tool, so a recomposition carries them over instead of deleting them."""
    return [block for block in re.split(r"(?m)^(?=\[)", text)
            if re.match(r'\[node name="MapPin" type="Node3D" parent="Nav/Dx_[^"]+"\]', block)]


def compose(scene_text: str, cell_center: tuple[float, float], items: list) -> str:
    pins = generated_pins(scene_text)
    text = strip_generated(scene_text).rstrip("\n") + "\n"
    kinds = sorted({i.kind for i in items})
    ext_lines, sub_lines = [], []
    for kind in kinds:
        if kind in COMPOSED:
            ext_lines.append(f'[ext_resource type="PackedScene" path="{COMPOSED[kind][0]}" id="dx_{kind}"]')
        elif kind in BOXED:
            ext_lines.append(f'[ext_resource type="PackedScene" path="{BOXED[kind][0]}" id="dx_{kind}"]')
            w, h, d = BOXED[kind][1]
            sub_lines.append(f'[sub_resource type="BoxShape3D" id="DxShape_{kind}"]\nsize = Vector3({fmt(w)}, {fmt(h)}, {fmt(d)})\n')
        elif kind in LOOSE:
            ext_lines.append(f'[ext_resource type="PackedScene" path="{LOOSE[kind]}" id="dx_{kind}"]')
        else:
            raise SystemExit(f"unknown kind '{kind}'")

    last_ext = list(re.finditer(r"^\[ext_resource [^\n]*\]$", text, re.M))
    at = last_ext[-1].end() if last_ext else text.index("\n") + 1
    text = text[:at] + ("\n" if last_ext else "") + "\n".join(ext_lines) + text[at:]
    first_sub_or_node = re.search(r"^\[(sub_resource|node) ", text, re.M)
    text = text[:first_sub_or_node.start()] + "\n".join(sub_lines) + ("\n" if sub_lines else "") + text[first_sub_or_node.start():]

    nodes = []
    for item in items:
        lx, lz = item.x - cell_center[0], item.z - cell_center[1]
        transform = f"Transform3D({basis(item.yaw, item.scale)}, {fmt(lx)}, 0, {fmt(lz)})"
        groups = ' groups=["world_landmark"]' if item.landmark else ""
        name = f"Dx_{item.name}"
        if item.kind in COMPOSED:
            nodes.append(f'[node name="{name}" parent="Nav" instance=ExtResource("dx_{item.kind}"){groups}]\n'
                         f"transform = {transform}\n")
        elif item.kind in BOXED:
            w, h, d = BOXED[item.kind][1]
            nodes.append(f'[node name="{name}" type="Node3D" parent="Nav"{groups}]\ntransform = {transform}\n\n'
                         f'[node name="Model" parent="Nav/{name}" instance=ExtResource("dx_{item.kind}")]\n\n'
                         f'[node name="Collider" type="StaticBody3D" parent="Nav/{name}"]\n\n'
                         f'[node name="Shape" type="CollisionShape3D" parent="Nav/{name}/Collider"]\n'
                         f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, {fmt(h * 0.5)}, 0)\n"
                         f'shape = SubResource("DxShape_{item.kind}")\n')
        else:
            nodes.append(f'[node name="{name}" parent="Nav" instance=ExtResource("dx_{item.kind}")]\n'
                         f"transform = {transform}\n")
    names = {f"Dx_{i.name}" for i in items}
    kept = [pin.rstrip("\n") + "\n" for pin in pins
            if re.search(r'parent="Nav/(Dx_[^"]+)"', pin).group(1) in names]
    return text + "\n" + "\n".join(nodes) + ("\n" + "\n".join(kept) if kept else "")


def road_conflicts(items, cells_by_key, roads) -> list[str]:
    """A structure whose footprint reaches into a road's carriageway or shoulder (invariant 21)."""
    issues = []
    for item in items:
        w, d = footprint(item.kind)
        radius = min(w, d) * 0.5
        for road in roads:
            for (ax, az), (bx, bz) in zip(road.points, road.points[1:]):
                vx, vz = bx - ax, bz - az
                t = max(0.0, min(1.0, ((item.x - ax) * vx + (item.z - az) * vz) / (vx * vx + vz * vz)))
                dist = math.hypot(item.x - (ax + vx * t), item.z - (az + vz * t))
                if dist < road.width * 0.5 + road.shoulder + radius:
                    issues.append(f"{item.name} ({item.kind}) stands {dist:.1f} m from a road centreline "
                                  f"(needs {road.width * 0.5 + road.shoulder + radius:.1f})")
    return issues


def overlaps(items, all_cells) -> list[str]:
    """A planned structure standing inside another planned or hand-authored one."""
    from check_cell_layout import structure
    solids = [(i.name, i.x, i.z, max(footprint(i.kind)) * 0.5) for i in items]
    for cell in all_cells:
        path = ROOT / cell.scene.replace("res://", "")
        for name, x, z, r in structure(str(path)):
            if not name.startswith("Dx_"):
                solids.append((f"{cell.key}/{name}", cell.center[0] + x, cell.center[1] + z, r))
    issues = []
    for i, a in enumerate(solids):
        for b in solids[i + 1:]:
            if not (a[0].startswith(("Bw", "Ln", "Wr", "Eg")) or b[0].startswith(("Bw", "Ln", "Wr", "Eg")))                     and "/" in a[0] and "/" in b[0]:
                continue
            if math.hypot(a[1] - b[1], a[2] - b[2]) < (a[3] + b[3]) * 0.8:
                issues.append(f"{a[0]} overlaps {b[0]}")
    return issues


def main(argv: list[str]) -> int:
    from district_layouts import ITEMS
    from gen_regions import cell_containing
    import region_spec_ember
    import region_spec_frostfang
    all_cells = list(region_spec_ember.cells()) + list(region_spec_frostfang.cells())
    grouped: dict[str, list] = {c.cell_id: [] for c in all_cells}
    for item in ITEMS:
        cell = cell_containing(all_cells, item.x, item.z)
        if cell is None:
            raise SystemExit(f"{item.name} at ({item.x}, {item.z}) is outside every cell")
        grouped[cell.cell_id].append(item)
    issues = road_conflicts(ITEMS, None, region_spec_ember.roads() + region_spec_frostfang.roads())
    for issue in issues:
        print(f"ROAD CONFLICT: {issue}")
    stale = 0
    for cell in all_cells:
        path = ROOT / cell.scene.replace("res://", "")
        # Scenes are always LF on disk (.gitattributes: *.tscn text eol=lf) - a stray CRLF that
        # sneaks in (a checkout quirk, an editor save) must never be "preserved" here, or this
        # tool rewrites the *whole* file to CRLF on its next run and world_bake.py's source
        # fingerprint goes stale on a clean checkout for no content reason.
        raw = io.open(path, encoding="utf-8", newline="").read()
        current = raw.replace(CRLF, LF)
        items = grouped[cell.cell_id]
        if not items and "Dx_" not in current:
            continue
        new = compose(current, cell.center, items) if items else strip_generated(current)
        if new != current:
            stale += 1
            if "--check" in argv:
                print(f"out of date: {path.relative_to(ROOT)}")
            else:
                io.open(path, "w", encoding="utf-8", newline="\n").write(new)
                print(f"composed {len(items)} structure(s) into {path.relative_to(ROOT)}")
    return 1 if (issues or ("--check" in argv and stale)) else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
