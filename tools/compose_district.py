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
    # The generated world set (2026-10 overhaul): tools/make_landmark_wrapper.py wrapper scenes, each
    # a solid model with its own primitive colliders and the landmark detail material. The footprint
    # is the model's measured width and depth at scale 1 (make_landmark_wrapper.py --slices), so the
    # road check below keeps the whole piece off the carriageway and its shoulder.
    # bld_ruin_tower.tscn stays on disk (tools/check_architecture_kit.py lists it) and is no longer placed.
    "monolith": ("res://scenes/props/lm_monolith_a.tscn", (5.1, 5.4)),
    "column_broken": ("res://scenes/props/lm_column_broken.tscn", (8.0, 5.9)),
    "wall_ruin": ("res://scenes/props/lm_wall_ruin.tscn", (10.0, 8.3)),
    "wall_rampart": ("res://scenes/props/lm_wall_rampart.tscn", (14.7, 8.0)),
    # The tower above its stepped base course (which is 9.1 x 10.2 and 1.8 m high).
    "gate_tower": ("res://scenes/props/lm_gate_tower.tscn", (7.8, 8.4)),
    "ruin_tower": ("res://scenes/props/lm_ruin_tower.tscn", (15.5, 15.4)),
    "bell_tower": ("res://scenes/props/lm_bell_tower.tscn", (5.7, 6.8)),
    "arch": ("res://scenes/props/lm_arch.tscn", (14.1, 13.0)),
    "bone_arch": ("res://scenes/props/lm_bone_arch.tscn", (6.5, 1.7)),
    "iron_citadel": ("res://scenes/props/lm_iron_citadel.tscn", (56.5, 56.2)),
    "colossus": ("res://scenes/props/lm_colossus_a.tscn", (13.6, 10.3)),
    "colossus_head": ("res://scenes/props/lm_colossus_head.tscn", (19.0, 16.7)),
    "godhall_fragment": ("res://scenes/props/lm_godhall_fragment.tscn", (45.5, 25.1)),
    "dragon_skeleton": ("res://scenes/props/lm_dragon_skeleton.tscn", (12.7, 17.8)),
    "rock_outcrop": ("res://scenes/props/lm_rock_outcrop_a.tscn", (21.1, 10.6)),
    "rock_boulder": ("res://scenes/props/lm_rock_boulder_a.tscn", (6.2, 6.1)),
    "rock_rubble": ("res://scenes/props/lm_rock_rubble_a.tscn", (4.6, 4.7)),
}

# Models without their own collider: scene, collider box (w, h, d).
BOXED = {
    "house_a": ("res://assets/models/architecture/bld_house_a.glb", (4.8, 7.5, 5.9)),
    "house_b": ("res://assets/models/architecture/bld_house_b.glb", (4.3, 6.8, 5.1)),
    "cottage": ("res://assets/models/architecture/bld_cottage.glb", (4.9, 4.2, 4.8)),
    "timber_stack": ("res://assets/models/props/prp_timber_stack.glb", (3.2, 1.4, 1.6)),
    "market_stand": ("res://assets/models/props/prp_market_stand.glb", (3.0, 2.6, 2.0)),
    # Generated props. Boxes are the measured hull a capsule meets, not the full bounds: the well's
    # low step and the tent's pegged skirt are under the 0.5 m step-up and stay walkable.
    "well": ("res://assets/models/world/prp_well_a.glb", (2.6, 2.6, 2.8)),
    "cart": ("res://assets/models/world/prp_cart_a.glb", (3.4, 1.5, 1.9)),
    "tent": ("res://assets/models/world/prp_tent_a.glb", (3.6, 2.6, 3.6)),
    "clutter_pile": ("res://assets/models/world/prp_clutter_pile_a.glb", (2.3, 1.3, 1.5)),
}

# Small props with no collider (they would snag the capsule and matter to nobody).
LOOSE = {
    "lamp_post": "res://assets/models/world/prp_lamp_post_a.glb",
    "banner": "res://assets/models/world/prp_banner_a.glb",
    "campfire": "res://assets/models/world/prp_campfire_a.glb",
    "brazier": "res://assets/models/props/prp_brazier.glb",
    "fence": "res://assets/models/world/prp_fence_a.glb",
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


def basis(yaw_degrees: float, scale: float = 1.0, depth: float = 1.0) -> str:
    r = math.radians(yaw_degrees)
    c, s = math.cos(r) * scale, math.sin(r) * scale
    # A .tscn stores the basis by ROWS, so the three numbers of a local axis are read down a column:
    # local X = (c, 0, s), local Z = (-s, 0, c). That is a turn of MINUS `yaw` about +Y in Godot's
    # own sense (rotation_degrees.y = -yaw): a layout's yaw runs the other way from an editor's, and
    # a piece's +Z front points along (-sin yaw, cos yaw). Every row in district_layouts.py was
    # surveyed against this, so it is the convention, not a thing to "correct" here.
    # `depth` thins the local Z axis only (a rampart segment in a ring): the long faces keep their
    # proportions, and a box collider follows a non-uniform scale where a cylinder would not.
    return f"{fmt(c)}, 0, {fmt(-s * depth)}, 0, {fmt(scale)}, 0, {fmt(s)}, 0, {fmt(c * depth)}"


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
                # A `;` comment closing a generated block introduces the hand-authored node after
                # it. It is not ours: dropping it with the block lost a campaign note on every run.
                tail = []
                for line in reversed(block.rstrip("\n").split("\n")):
                    if not line.startswith(";"):
                        break
                    tail.append(line)
                if tail:
                    out.append("\n".join(reversed(tail)) + "\n")
                continue
        elif skip and not header.startswith("["):
            continue
        out.append(block)
    return "".join(out)


def wrapper_tint(scene: str) -> tuple[float, float, float]:
    """The albedo_tint a wrapper scene ships with (white when it sets none)."""
    text = (ROOT / scene.replace("res://", "")).read_text(encoding="utf-8")
    found = re.search(r"^albedo_tint = Color\(([^)]*)\)", text, re.M)
    if not found:
        return (1.0, 1.0, 1.0)
    r, g, b = [float(v) for v in found.group(1).split(",")[:3]]
    return (r, g, b)


def generated_pins(text: str) -> list[str]:
    """Map-pin blocks parented to generated nodes. They belong to tools/gen_map_locations.py, not to
    this tool, so a recomposition carries them over instead of deleting them."""
    # Only the pin's own lines: a `;` comment after it introduces the next hand-authored node and
    # stays where it is (strip_generated keeps it there).
    return ["\n".join(line for line in block.split("\n") if not line.startswith(";"))
            for block in re.split(r"(?m)^(?=\[)", text)
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
        transform = f"Transform3D({basis(item.yaw, item.scale, item.depth)}, {fmt(lx)}, {fmt(item.y)}, {fmt(lz)})"
        groups = ' groups=["world_landmark"]' if item.landmark else ""
        name = f"Dx_{item.name}"
        if item.kind in COMPOSED:
            tint = ""
            if item.tint is not None:
                # A realm's stone colour multiplies the wrapper's own tint (landmark_detail.gd).
                base = wrapper_tint(COMPOSED[item.kind][0])
                tint = "albedo_tint = Color(" + ", ".join(fmt(round(b * t, 3)) for b, t in zip(base, item.tint)) + ", 1)\n"
            nodes.append(f'[node name="{name}" parent="Nav" instance=ExtResource("dx_{item.kind}"){groups}]\n'
                         f"transform = {transform}\n{tint}")
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


# Kinds whose plan is a disc: a square hull would claim 41% more ground at its corners.
ROUND = {"ruin_tower", "rock_boulder", "rock_rubble"}


def generated(kind: str) -> bool:
    """A piece of the generated world set (a scenes/props/lm_*.tscn wrapper)."""
    return kind in COMPOSED and "/lm_" in COMPOSED[kind][0]


def hull(item) -> list[tuple[float, float]]:
    """World points on the item's plan, turned by its yaw and sized by its scale. A generated piece
    is checked as what it is: a 45 m hall fragment lying along a road is not a 12 m disc. The kit
    buildings keep the inscribed disc their frontages were surveyed with."""
    w, d = footprint(item.kind)
    if not generated(item.kind):
        radius = min(w, d) * 0.5
        return [(item.x, item.z)] + [(item.x + radius * math.cos(a), item.z + radius * math.sin(a))
                                     for a in (i * math.pi / 8 for i in range(16))]
    hw, hd = w * 0.5 * item.scale, d * 0.5 * item.scale * item.depth
    if item.kind in ROUND:
        radius = max(hw, hd)
        return [(item.x, item.z)] + [(item.x + radius * math.cos(a), item.z + radius * math.sin(a))
                                     for a in (i * math.pi / 8 for i in range(16))]
    r = math.radians(item.yaw)
    c, s = math.cos(r), math.sin(r)
    # Local X = (c, s) and local Z = (-s, c) on the ground: see basis().
    return [(item.x + ux * hw * c - uz * hd * s, item.z + ux * hw * s + uz * hd * c)
            for ux in (-1.0, -0.5, 0.0, 0.5, 1.0) for uz in (-1.0, -0.5, 0.0, 0.5, 1.0)]


def borrowed_resources(text: str) -> list[str]:
    """Hand-authored nodes that instance a generated `dx_*` resource. That id belongs to this tool:
    it is dropped when the last generated node of its kind goes and repointed when the kind's model
    changes, and either one silently changes a node nobody meant to touch (the tipped cart wreck in
    the capital took the new cart this way). Such a node declares its own ext_resource."""
    out = []
    for block in re.split(r"(?m)^(?=\[)", text):
        header = block.split("\n", 1)[0]
        if not header.startswith("[node ") or 'ExtResource("dx_' not in block:
            continue
        name = re.search(r'name="([^"]+)"', header).group(1)
        parent = re.search(r'parent="([^"]*)"', header)
        parent = parent.group(1) if parent else ""
        if not (name.startswith("Dx_") or parent.startswith("Nav/Dx_")):
            out.append(f"{parent}/{name}" if parent not in ("", ".") else name)
    return out


def road_conflicts(items, cells_by_key, roads) -> list[str]:
    """A structure whose footprint reaches into a road's carriageway or shoulder (invariant 21)."""
    issues = []
    for item in items:
        points = hull(item)
        worst = None
        for road in roads:
            for (ax, az), (bx, bz) in zip(road.points, road.points[1:]):
                vx, vz = bx - ax, bz - az
                for px, pz in points:
                    t = max(0.0, min(1.0, ((px - ax) * vx + (pz - az) * vz) / (vx * vx + vz * vz)))
                    over = road.width * 0.5 + road.shoulder - math.hypot(px - (ax + vx * t), pz - (az + vz * t))
                    if over > 0 and (worst is None or over > worst):
                        worst = over
        if worst is not None:
            issues.append(f"{item.name} ({item.kind}) reaches {worst:.1f} m into a road's carriageway or shoulder")
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
    import region_spec_ashen
    import region_spec_celestial
    import region_spec_ember
    import region_spec_frostfang
    import region_spec_pale_concord
    import region_spec_sunspire
    # All six realms (2026-10): the generated landmarks stand in every one of them. Only the Ember
    # Crown and Frostfang turn pads and terraces into ground; elsewhere a row places a node and
    # nothing else.
    specs = (region_spec_ember, region_spec_frostfang, region_spec_ashen, region_spec_sunspire,
             region_spec_pale_concord, region_spec_celestial)
    all_cells = [cell for spec in specs for cell in spec.cells()]
    roads = [road for spec in specs for road in spec.roads()]
    grouped: dict[str, list] = {c.cell_id: [] for c in all_cells}
    for item in ITEMS:
        cell = cell_containing(all_cells, item.x, item.z)
        if cell is None:
            raise SystemExit(f"{item.name} at ({item.x}, {item.z}) is outside every cell")
        grouped[cell.cell_id].append(item)
    issues = road_conflicts(ITEMS, None, roads)
    names = [item.name for item in ITEMS]
    issues += [f"{name} is placed twice" for name in sorted({n for n in names if names.count(n) > 1})]
    for issue in issues:
        print(f"CONFLICT: {issue}")
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
        for node in borrowed_resources(new):
            issues.append(f"{path.relative_to(ROOT)}: hand-authored node '{node}' instances a generated dx_ resource")
            print(f"CONFLICT: {issues[-1]}")
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
