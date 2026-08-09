#!/usr/bin/env python3
"""Compose a building from the medieval megakit and write it as a reusable `.tscn`.

Why this exists
---------------
A monolithic `bld_*.glb` is one mesh: it cannot be re-roofed, it cannot lose a window, and every
instance of it is the same house. Composed on the kit's grid, a variant is a different module in
one slot. This script is that composition, written once, after the same loop was hand-written twice.

Unlike `gen_cell_props.py` and `dress_cell.py`, this one WRITES the file rather than printing it:
the output is a whole scene, not a fragment to paste into an authored cell.

The grid
--------
Walls are **2.00 m wide x 3.12 m tall**; a storey is one wall. A shell `wide x deep` modules has its
wall CENTRE planes at x = +/-wide, z = +/-deep (each module is 2 m, so a 2x3 shell is 4 x 6 m), and
the footprint is 4.4 x 6.4 m once thickness counts. Roofs and gables are cut for whole modules.

⚠️ A wall module's outer face is its local -Z
---------------------------------------------
Each face is yawed to turn that face outward: back 0, left 90, front 180, right 270. Get one wrong
and the building reads right from three sides and shows its plaster backing on the fourth.

⚠️ This kit separates a hole from the thing that fills it, and a missing filler is SILENT
------------------------------------------------------------------------------------------
Four bit while composing the first building, each looking finished from the angle it was built at:

* a pitched roof with **no gable end** is open to the sky at both ends;
* a window WALL is an **opening** -- without a window INSERT you see through the house and out the
  far side;
* the **door leaf** is a separate piece from the doorway, and it hangs on its hinge, so its origin
  is not its centre (local x -0.05..1.07; under yaw 180 the node sits 0.51 m off);
* `Wall_Plaster_WoodGrid` is an **overlay frame, not a wall** -- on its own the storey is a
  see-through lattice, and it layers ON a plain wall at the same transform.

Render front AND back before believing any of it.

Usage
-----
    python tools/compose_building.py <name> <wide> <deep> <storeys>

    name      output goes to scenes/props/bld_<name>.tscn
    wide      modules across X   (2 -> a 4 m wall, 4.4 m with thickness)
    deep      modules along Z
    storeys   1 or 2; the upper storey is half-timbered
"""

import os
import sys

YAW = {0: "1, 0, 0, 0, 1, 0, 0, 0, 1", 90: "0, 0, 1, 0, 1, 0, -1, 0, 0",
       180: "-1, 0, 0, 0, 1, 0, 0, 0, -1", 270: "0, 0, -1, 0, 1, 0, 1, 0, 0"}
STOREY = 3.12
MODULE = 2.0
GLAZING = {"wall_window": "window_wide", "wall_window_thin": "window_thin"}


def slots(count):
    """Module centre offsets along a run of `count` modules, centred on the origin."""
    return [(i - (count - 1) / 2.0) * MODULE for i in range(count)]


def compose(name, wide, deep, storeys):
    roof = f"roof_{wide * 2}x{deep * 2}"
    gable = f"gable_{wide * 2}"
    modules = ["wall_plain", "wall_timber", "wall_door", "wall_window", "wall_window_thin",
               "window_wide", "window_thin", "corner", roof, gable, "door", "chimney", "wall_base"]
    for m in (roof, gable):
        if not os.path.isfile(f"assets/models/architecture/mod_{m}.gltf"):
            raise SystemExit(f"no mod_{m}.gltf -- adopt it before composing a {wide}x{deep} shell")
    ids = {m: f"{i + 1}_{m}" for i, m in enumerate(modules)}

    half_x, half_z = wide * MODULE / 2.0, deep * MODULE / 2.0
    xs, zs = slots(wide), slots(deep)
    nodes = []

    def put(node, module, x, y, z, yaw=0):
        nodes.append(f'[node name="{node}" parent="Shell" instance=ExtResource("{ids[module]}")]\n'
                     f"transform = Transform3D({YAW[yaw]}, {x}, {y}, {z})\n")

    def wall(node, module, x, y, z, yaw, timber=False):
        put(node, module, x, y, z, yaw)
        if module in GLAZING:
            put(node + "Glass", GLAZING[module], x, y, z, yaw)
        if timber:
            put(node + "Timber", "wall_timber", x, y, z, yaw)

    mid_x, mid_z = len(xs) // 2, len(zs) // 2
    for storey in range(storeys):
        y = storey * STOREY
        up = storey > 0
        tag = "U" if up else "G"
        for i, x in enumerate(xs):
            front = "wall_window" if up else ("wall_door" if i == 0 else "wall_plain")
            wall(f"{tag}Front{i}", front, x, y, half_z, 180, timber=up)
            back = "wall_window_thin" if (up and i == mid_x) else "wall_plain"
            wall(f"{tag}Back{i}", back, x, y, -half_z, 0, timber=up)
        for i, z in enumerate(zs):
            side = "wall_window" if i == mid_z else "wall_plain"
            wall(f"{tag}Left{i}", side, -half_x, y, z, 90, timber=up)
            wall(f"{tag}Right{i}", side, half_x, y, z, 270, timber=up)
        for i, (cx, cz) in enumerate([(-half_x, -half_z), (half_x, -half_z),
                                      (-half_x, half_z), (half_x, half_z)]):
            put(f"{tag}Corner{i}", "corner", cx, y, cz)

    for i, x in enumerate(xs):
        put(f"BaseFront{i}", "wall_base", x, 0.0, half_z, 180)
        put(f"BaseBack{i}", "wall_base", x, 0.0, -half_z, 0)
    for i, z in enumerate(zs):
        put(f"BaseLeft{i}", "wall_base", -half_x, 0.0, z, 90)
        put(f"BaseRight{i}", "wall_base", half_x, 0.0, z, 270)

    eaves = storeys * STOREY
    put("GableFront", gable, 0.0, eaves, half_z, 180)
    put("GableBack", gable, 0.0, eaves, -half_z, 0)
    put("Door", "door", xs[0] + 0.51, 0.0, half_z + 0.20, 180)
    put("Roof", roof, 0.0, eaves, 0.0)
    put("Chimney", "chimney", -1.10, eaves + 1.06, -half_z + 1.0)

    ext = "".join(
        '[ext_resource type="PackedScene" path="res://assets/models/architecture/mod_%s.gltf" id="%s"]\n'
        % (m, ids[m]) for m in modules)
    depth = deep * MODULE + 0.4
    width = wide * MODULE + 0.4
    body = f'''[gd_scene load_steps={len(modules) + 2} format=3]

{ext}
[sub_resource type="BoxShape3D" id="Shape_{name}"]
size = Vector3({width}, {eaves}, {depth})

; ====================================================================================================
; {name.upper()} ({wide}x{deep} modules, {storeys} storey) - COMPOSED, not a monolithic mesh.
; Generated by tools/compose_building.py; regenerate with:
;     python tools/compose_building.py {name} {wide} {deep} {storeys}
; Read that file's header before editing this one - it carries the grid and the four traps this kit
; sets, every one of which is silent and visible only in a render.
;
; WARNING A WALL MODULE'S OUTER FACE IS ITS LOCAL -Z, so each face is yawed to point it outward
; (back 0, left 90, front 180, right 270). WARNING A WINDOW WALL IS AN OPENING and carries a separate
; insert; the gable ends close a roof that is otherwise open to the sky; the timber frame is an
; OVERLAY on a plain wall, not a wall. Judge this from BEHIND before believing it.
;
; The collider is ONE box on the whole shell, not one per module: fifty little static bodies would
; carve fifty little holes in the navmesh, and the building is one obstacle.
; ====================================================================================================

[node name="{name.title().replace("_", "")}" type="Node3D"]

[node name="Shell" type="Node3D" parent="."]

{"".join(nodes)}
[node name="Collider" type="StaticBody3D" parent="."]

[node name="Shape" type="CollisionShape3D" parent="Collider"]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, {eaves / 2.0}, 0)
shape = SubResource("Shape_{name}")
'''
    out = f"scenes/props/bld_{name}.tscn"
    open(out, "w", encoding="utf-8", newline="\n").write(body)
    print(f"{out}: {len(nodes)} module instances, {width} x {depth} m footprint, eaves {eaves} m")


if __name__ == "__main__":
    if len(sys.argv) != 5:
        raise SystemExit(__doc__)
    compose(sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]))
