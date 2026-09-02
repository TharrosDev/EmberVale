#!/usr/bin/env python3
"""Retire `prp_glacier.glb` from the three Frostfang cells that instance it fifteen times.

⚠️ THE WHOLE OF FROSTFANG'S ICE IS ONE MESH. `prp_glacier.glb` is a 768-triangle block that
appears as `Ice`, `Ice2`, `IceWestN/S`, `IceEastN/S`, `GlacierNW/NE/E/W` and five more across
`glacier.tscn`, `ancient_aerie.tscn` and `dragon_roost.tscn` — fifteen nodes, one silhouette,
told apart only by yaw. From anywhere in those cells at least two of them are in frame.

⚠️ AND EVERY PLACEMENT IS ANISOTROPICALLY SCALED. `ancient_aerie`'s GlacierNW has basis column
lengths 1.220, 0.908 and 1.248 with the two horizontal columns 2.4 degrees off perpendicular, so
the transform carries a small shear as well as a squash. It is mild — this is not a visibly
skewed mesh — but it is a real one, it defeats the importer's normals, and it exists only because
one prop was being stretched to stand in for five different shapes.

⚠️ THE ROUTES THROUGH THESE CELLS ARE AUTHORED AGAINST THESE FOOTPRINTS AND MUST NOT MOVE. The
comment above the far shards in `glacier.tscn` is explicit: "the route is the gaps BETWEEN these,
not a corridor with these beside it." So this tool changes WHICH mesh stands at each spot and
nothing else about where it stands: each node keeps its position and its yaw, and gets a UNIFORM
scale chosen so the new asset's horizontal footprint matches the area the old one covered. The
navmesh, the traversal probes and the layout gates therefore see the same obstacles.

Each node also gets its own collider sized from its own asset and scale, replacing the single
shared `Shape_glacier` box. ⚠️ Reusing a predecessor's collider is the trap `ASSET_POLICY.md` §0.5
records twice (`Shape_station`, `Shape_boulder`) and §12 of `CLAUDE.md` states as a rule: a model
swap does not authorize reusing its collision.

    python tools/replace_glacier_props.py [--check]
"""

from __future__ import annotations

import argparse
import math
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CELLS = ROOT / "scenes" / "regions" / "frostfang_reach"

# The retiring prop's real measured extent (x, height, z), from the Godot-imported scene.
OLD = (8.43, 4.96, 6.02)

# The replacements, measured the same way. Chosen per node below by the role the node plays.
NEW = {
    "wall": ("prp_glacier_wall.glb", (8.00, 6.40, 3.20)),
    "face": ("prp_glacier_face.glb", (12.24, 9.49, 7.41)),
    "slab": ("prp_ice_slab.glb", (4.40, 0.55, 4.00)),
    "shard": ("prp_ice_shard.glb", (1.04, 2.50, 1.11)),
}

# ⚠️ THE ASSIGNMENT IS BY ROLE, NOT ROUND-ROBIN. A cell whose two big masses read as one glacier
# wants the wall and the buttress; the outer chicane shards want the wall too, because they are
# what the player walks between and a wall gives a flat face to walk along. Nothing gets the slab
# or the shard here — those are scatter-layer species (see region_spec_frostfang.py), and dropping
# a 2.5 m shard into a slot sized for a 4.5 m mass would open a route the layout gate closed.
ASSIGNMENT = {
    "glacier.tscn": {
        "Ice": "face", "Ice2": "wall",
        "IceWestN": "wall", "IceWestS": "face", "IceEastN": "face", "IceEastS": "wall",
    },
    "ancient_aerie.tscn": {
        "GlacierNW": "face", "GlacierNE": "wall", "GlacierE": "wall", "GlacierW": "face",
    },
    "dragon_roost.tscn": {},  # filled from the file: every remaining glacier node becomes a wall
}

NODE = re.compile(
    r'\[node name="(?P<name>[^"]+)" parent="(?P<parent>[^"]*)"(?P<extra>[^\]]*)'
    r'instance=ExtResource\("(?P<res>[0-9]+_glacier)"\)\]\n'
    r'transform = Transform3D\((?P<basis>[^)]+)\)\n')


def horizontal_scale(basis: list[float]) -> tuple[float, float]:
    """The transform's horizontal and vertical scale, from the basis column lengths.

    A Godot Transform3D literal is nine basis numbers in ROW-major order followed by the origin,
    and the scale lives in the COLUMN lengths. Reading rows instead gives the right answer only
    for an axis-aligned transform, which none of these are.
    """
    columns = [(basis[0], basis[3], basis[6]), (basis[1], basis[4], basis[7]), (basis[2], basis[5], basis[8])]
    lengths = [math.sqrt(sum(c * c for c in column)) for column in columns]
    return (lengths[0] + lengths[2]) / 2.0, lengths[1]


def yaw_of(basis: list[float]) -> float:
    """The transform's yaw, taken from the first basis column projected onto the XZ plane."""
    return math.atan2(-basis[6], basis[0])


def rewrite(path: Path, check: bool) -> list[str]:
    text = path.read_text(encoding="utf-8")
    assignment = dict(ASSIGNMENT.get(path.name, {}))
    # A cell with no explicit table takes the wall everywhere — but the names still have to be
    # filled in, because the collider swap below is driven by this dict and an empty one silently
    # leaves every node pointing at the shared `Shape_glacier` that is about to be the wrong size.
    for match in NODE.finditer(text):
        assignment.setdefault(match.group("name"), "wall")
    notes: list[str] = []
    shapes: list[str] = []

    def replace(match: re.Match) -> str:
        name = match.group("name")
        basis = [float(v) for v in match.group("basis").split(",")]
        origin = basis[9:12]
        role = assignment.get(name, "wall")
        filename, extent = NEW[role]

        wide, tall = horizontal_scale(basis)
        yaw = yaw_of(basis)
        # Match the horizontal footprint the old prop covered, then keep the scale UNIFORM: the
        # shear and the squash go away, and only the mesh choice makes one instance differ from
        # the next. Height follows from that rather than being dialled separately, which is what
        # anisotropic scaling was doing and is exactly what made every mass the same proportions.
        old_span = math.sqrt(OLD[0] * OLD[2]) * wide
        scale = old_span / math.sqrt(extent[0] * extent[2])
        cos, sin = math.cos(yaw) * scale, math.sin(yaw) * scale

        notes.append(
            f"{path.name}:{name} {role} old {wide:.3f}/{tall:.3f} (aniso) -> uniform {scale:.3f}, "
            f"footprint {old_span:.2f} m")

        shape_id = f"Shape_ice_{name}"
        shapes.append(
            f'[sub_resource type="BoxShape3D" id="{shape_id}"]\n'
            # The collider is a child of the scaled node, so it is authored in the model's LOCAL
            # units — the 38E trap, recorded in ASSET_POLICY.md §0.6: a collider child inherits
            # its node's scale, and a shape written in world metres comes out `scale` times too big.
            f"size = Vector3({extent[0]:.3f}, {extent[1]:.3f}, {extent[2]:.3f})\n")

        return (
            f'[node name="{name}" parent="{match.group("parent")}"{match.group("extra")}'
            f'instance=ExtResource("{match.group("res")}")]\n'
            f"transform = Transform3D({cos:.4f}, 0, {sin:.4f}, 0, {scale:.4f}, 0, "
            f"{-sin:.4f}, 0, {cos:.4f}, {origin[0]:g}, {origin[1]:g}, {origin[2]:g})\n")

    updated, count = NODE.subn(replace, text)
    if count == 0:
        return [f"{path.name}: no glacier instances found"]

    # Repoint the ext_resource at the new mesh. Every node in one cell takes the same asset per
    # role, so a cell that mixes wall and face needs a second ext_resource.
    roles = {assignment.get(m.group("name"), "wall") for m in NODE.finditer(text)}
    primary = "face" if "face" in roles else "wall"
    updated = updated.replace(
        'path="res://assets/models/props/prp_glacier.glb"',
        f'path="res://assets/models/props/{NEW[primary][0]}"')
    for role in roles - {primary}:
        # A second resource id for the other role, declared beside the first.
        marker = f'path="res://assets/models/props/{NEW[primary][0]}" id="'
        index = updated.index(marker)
        res_id = re.search(r'id="([0-9]+_glacier)"', updated[index:]).group(1)
        new_id = res_id.replace("_glacier", f"_ice_{role}")
        line_end = updated.index("\n", index) + 1
        updated = (updated[:line_end]
                   + f'[ext_resource type="PackedScene" '
                     f'path="res://assets/models/props/{NEW[role][0]}" id="{new_id}"]\n'
                   + updated[line_end:])
        for name, assigned in assignment.items():
            if assigned == role:
                updated = re.sub(
                    rf'(\[node name="{re.escape(name)}"[^\]]*instance=ExtResource\(")[0-9]+_glacier("\)\])',
                    rf"\g<1>{new_id}\g<2>", updated)

    # Swap every shared Shape_glacier reference for the node's own shape, and re-centre it.
    #
    # ⚠️ THE COLLIDER'S Y OFFSET BELONGS TO THE MODEL, NOT TO THE SLOT. Every one of these shapes
    # sat at local y = 2.2, which is half of `prp_glacier`'s 4.96 m height less its 0.28 m of
    # ground offset. Carried onto a 6.4 m wall it centres the box a metre low, so the collision
    # stands proud of the ice at the bottom and stops a metre short of it at the top — and the
    # player walks into nothing, or through something, depending which end they meet. Every new
    # asset sits with its base at exactly y = 0 (see build_environment_assets.sit_on_ground), so
    # the centre is simply half the height.
    for name, role in assignment.items():
        extent = NEW[role][1]
        updated = re.sub(
            rf'(\[node name="Shape" type="CollisionShape3D" parent="[^"]*/{re.escape(name)}/Col"\]\n)'
            rf'(?:transform = [^\n]*\n)?shape = SubResource\("Shape_glacier"\)',
            lambda m, e=extent, n=name: (
                f"{m.group(1)}transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, "
                f'{e[1] / 2.0:.3f}, 0)\nshape = SubResource("Shape_ice_{n}")'),
            updated)
    # Drop the shared shape FIRST, then put the per-node shapes where it was. Inserting before
    # removing means the second index() lands inside the text just inserted.
    anchor = updated.index('[sub_resource type="BoxShape3D" id="Shape_glacier"]')
    end = updated.index("\n\n", anchor) + 2
    if 'SubResource("Shape_glacier")' in updated[end:]:
        raise SystemExit(f"{path.name}: Shape_glacier still has a referent — refusing to remove it")
    updated = updated[:anchor] + "\n".join(shapes) + "\n" + updated[end:]

    if not check:
        path.write_text(updated, encoding="utf-8", newline="\n")
    return notes


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for filename in ("glacier.tscn", "ancient_aerie.tscn", "dragon_roost.tscn"):
        for note in rewrite(CELLS / filename, args.check):
            print(f"  {note}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
