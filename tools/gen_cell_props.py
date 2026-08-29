#!/usr/bin/env python3
"""Expand a compact prop table into Godot `.tscn` node stanzas.

Why this exists
---------------
A dressed region cell is 850-900 lines, of which ~200 are near-identical four-node stanzas:

    [node name="X" type="Node3D" parent="Nav"]
    transform = Transform3D(...)
    [node name="Model" parent="Nav/X" instance=ExtResource("...")]
    [node name="Collider" type="StaticBody3D" parent="Nav/X"]
    [node name="Shape" type="CollisionShape3D" parent="Nav/X/Collider"]
    transform = ...
    shape = SubResource("...")

Writing those by hand cost roughly 8k output tokens per cell. This script was written ad hoc for
the Emberdeep Mine and again for Tarn's Landing and thrown away both times; it is committed now.

⚠️ It prints to stdout. Read the output, then paste it into the cell — it is deliberately NOT a
build step, because the `.tscn` stays the authored artefact and must remain greppable and literal.

Usage
-----
    python tools/gen_cell_props.py props.txt          # a table file
    python tools/gen_cell_props.py -                  # or on stdin

Each non-blank, non-`#` line is:

    name  ext_id  x  z  shape_id  y_centre  [yaw]  [y_offset]

    name      node name in the cell, e.g. BoulderA
    ext_id    the ExtResource id of the model, e.g. 11_boulder
    x, z      LOCAL position in the cell (the streamer places the cell at its Center)
    shape_id  the SubResource id of the collider shape, e.g. Shape_boulder
    y_centre  collider centre height — normally half the model's measured height
    yaw       rotation about Y in degrees, any value, default 0
    y_offset  vertical nudge on the node itself, default 0 (used to sink docks/jetties flush)

Pass `--no-collider` for scenery that must not carve the navmesh or block the player.

Conventions this encodes, so they stop being retyped per prop
-------------------------------------------------------------
* every static prop parents to `Nav` (geometry outside the NavigationRegion3D is not carved)
* the collider is a child `StaticBody3D` + `CollisionShape3D`, centred at `y_centre`
* collider sizes come from the model's MEASURED bounding box, never a guess (38K shipped a tent
  with 2.4 m of its depth uncollided by guessing)
"""

import math
import sys

# Godot basis rows, computed for any yaw in degrees. The 2026-08-28 layout rebuild needs the
# off-cardinal angles an
# organically-grown district is made of; the five-entry table this replaces refused anything else.
def _yaw_basis(yaw):
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    return ", ".join(
        (lambda v: str(int(v)) if v == int(v) else str(v))(round(x, 4) + 0.0)
        for x in (c, 0.0, s, 0.0, 1.0, 0.0, -s, 0.0, c))


def stanza(name, ext_id, x, z, shape_id, y_centre, yaw=0, y_offset=0.0, collider=True, parent="Nav"):
    # Godot writes a root-level node's children as parent="Name", not parent="./Name" — the "."
    # form is only ever the root itself. Emitting "./Name" produces a scene that loads with every
    # child silently missing.
    under = name if parent == "." else f"{parent}/{name}"

    out = [
        "",
        f'[node name="{name}" type="Node3D" parent="{parent}"]',
        f"transform = Transform3D({_yaw_basis(yaw)}, {x}, {y_offset}, {z})",
        "",
        f'[node name="Model" parent="{under}" instance=ExtResource("{ext_id}")]',
    ]
    if collider:
        out += [
            "",
            f'[node name="Collider" type="StaticBody3D" parent="{under}"]',
            "",
            f'[node name="Shape" type="CollisionShape3D" parent="{under}/Collider"]',
            f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, {y_centre}, 0)",
            f'shape = SubResource("{shape_id}")',
        ]
    return "\n".join(out)


def main(argv):
    collider = "--no-collider" not in argv
    # Ground cover parents OUTSIDE the NavigationRegion3D (38B). The docstring's "everything static
    # parents to Nav" is about geometry the player must not walk through; grass and flowers are the
    # opposite case, and the cell's navmesh parses static colliders only, so a collider-less tuft
    # could not carve it from either side. Parenting out says the intent in the .tscn itself.
    parent = next((a.split("=", 1)[1] for a in argv if a.startswith("--parent=")), "Nav")
    args = [a for a in argv if not a.startswith("--")]
    if not args:
        raise SystemExit(__doc__)

    text = sys.stdin.read() if args[0] == "-" else open(args[0], encoding="utf-8").read()

    count = 0
    for lineno, line in enumerate(text.splitlines(), 1):
        line = line.split("#", 1)[0].strip()
        if not line:
            continue
        f = line.split()
        if len(f) < 6:
            raise SystemExit(f"line {lineno}: need at least 6 fields, got {len(f)}: {line}")
        name, ext_id, x, z, shape_id, y_centre = f[:6]
        yaw = float(f[6]) if len(f) > 6 else 0.0
        y_offset = f[7] if len(f) > 7 else 0
        print(stanza(name, ext_id, x, z, shape_id, y_centre, yaw, y_offset, collider, parent))
        count += 1

    print(f"\n; {count} props generated by tools/gen_cell_props.py", file=sys.stderr)


if __name__ == "__main__":
    main(sys.argv[1:])
