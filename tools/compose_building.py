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

Solid or hollow, and it is a gameplay decision rather than a modelling one
-------------------------------------------------------------------------
By default the shell gets **one box collider** covering the whole footprint: fifty little static
bodies would carve fifty little holes in the navmesh, and a background town house is one obstacle.

`--hollow` builds a house you can **walk into**: a collider per wall module, **none across the door
module**, and a floor. Use it only for a building the player actually enters.

⚠️ The navmesh holes are then the CORRECT result — the walls are supposed to obstruct, and the
interior is supposed to be walkable. Do not author an NPC routine through a hollow building.
⚠️ ⚠️ **`CharacterBody3D` has no step-up**, so the floor sits flush at y = 0 and nothing may lip
above it. A 30 cm threshold is an invisible wall in the player's own doorway.

Usage
-----
    python tools/compose_building.py <name> <wide> <deep> <storeys> [--hollow | --open]

    name      output goes to scenes/props/bld_<name>.tscn
    wide      modules across X   (2 -> a 4 m wall, 4.4 m with thickness)
    deep      modules along Z
    storeys   1 or 2; the upper storey is half-timbered
    --hollow  enterable: per-wall colliders, an open doorway, and a floor
    --open    an OPEN HALL: three walls, no front run at all, no door and no floor. A lodge, a
              market hall, a forge shelter - a roof held up over ground the player walks straight
              into. It is not `--hollow` with the door widened: there is no front wall to put a
              doorway in, so there is no doorway, and the terrain stays the floor (a laid floor
              would be a 20 cm lip across the open side, which is an invisible wall).
"""

import argparse
import os
import sys

YAW = {0: "1, 0, 0, 0, 1, 0, 0, 0, 1", 90: "0, 0, 1, 0, 1, 0, -1, 0, 0",
       180: "-1, 0, 0, 0, 1, 0, 0, 0, -1", 270: "0, 0, -1, 0, 1, 0, 1, 0, 0"}
STOREY = 3.12
MODULE = 2.0

# A wall module's depth, measured in-engine rather than read off the accessors (ASSET_POLICY §0.6:
# accessor bounds ignore node scale and will lie to you). Only --hollow uses it, for the per-wall
# colliders; the solid path boxes the whole shell and does not care.
THICKNESS = 0.41
GLAZING = {"wall_window": "window_wide", "wall_window_thin": "window_thin"}


def slots(count):
    """Module centre offsets along a run of `count` modules, centred on the origin."""
    return [(i - (count - 1) / 2.0) * MODULE for i in range(count)]


def compose(name, wide, deep, storeys, hollow=False, open_hall=False, *,
            wall_family="plaster", roof_axis="z", door_index=0,
            chimney="left", shutters=False, dormer=False, awning=False,
            balcony=False, stairs=False, weathering=False, ruined=False):
    # An open hall shares hollow's per-module colliders (a single shell box would wall off the very
    # side that is meant to be walked through) and differs in having no front run to collide with.
    per_module = hollow or open_hall or ruined
    roof_wide, roof_deep = (wide, deep) if roof_axis == "z" else (deep, wide)
    roof = f"roof_{roof_wide * 2}x{roof_deep * 2}"
    gable = f"gable_{roof_wide * 2}"
    modules = ["wall_plain", "wall_timber", "wall_door", "wall_window", "wall_window_thin",
               "window_wide", "window_thin", "corner", roof, gable, "door", "chimney", "wall_base"]
    if wall_family in ("stone-ground", "stone") or ruined:
        modules += ["wall_stone", "wall_stone_door", "wall_stone_window", "wall_stone_window_thin"]
    if shutters:
        modules += ["shutters_wide_open", "shutters_thin_closed"]
    if dormer:
        modules.append("roof_dormer")
    if awning:
        modules += ["roof_awning", "roof_supports"]
    if balcony:
        modules += ["balcony_straight", "balcony_corner"]
    if stairs:
        modules.append("stairs_exterior")
    if weathering:
        modules.append("vine")
    if chimney == "right":
        modules.append("chimney_alt")
    modules = list(dict.fromkeys(modules))
    if hollow:
        modules.append("floor_wood")
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

    def wall(node, module, x, y, z, yaw, timber=False, storey=0):
        use_stone = ruined or wall_family == "stone" or (wall_family == "stone-ground" and storey == 0)
        if use_stone:
            module = {
                "wall_plain": "wall_stone",
                "wall_door": "wall_stone_door",
                "wall_window": "wall_stone_window",
                "wall_window_thin": "wall_stone_window_thin",
            }.get(module, module)
        put(node, module, x, y, z, yaw)
        glazing = {
            "wall_window": "window_wide", "wall_window_thin": "window_thin",
            "wall_stone_window": "window_wide", "wall_stone_window_thin": "window_thin",
        }
        if module in glazing and not ruined:
            put(node + "Glass", glazing[module], x, y, z, yaw)
            if shutters:
                shutter = "shutters_wide_open" if "thin" not in module else "shutters_thin_closed"
                put(node + "Shutters", shutter, x, y, z, yaw)
        if timber:
            put(node + "Timber", "wall_timber", x, y, z, yaw)

    mid_x, mid_z = len(xs) // 2, len(zs) // 2
    for storey in range(storeys):
        y = storey * STOREY
        up = storey > 0
        tag = "U" if up else "G"
        for i, x in enumerate(xs):
            # ⚠️ A hollow house gets a window on its FRONT as well. A solid one does not, and that is
            # not laziness: a background town house is seen from the street and its frontage is one
            # of thirty, while a house you live in is looked AT — a blank front elevation with a
            # single door reads as a shed, which is exactly the complaint 37E was opened on.
            if not open_hall:
                front = "wall_window" if up else (
                    "wall_door" if i == door_index else
                    "wall_window" if hollow and i == len(xs) - 1 else "wall_plain")
                if not ruined or (storey == 0 and i != len(xs) - 1):
                    wall(f"{tag}Front{i}", front, x, y, half_z, 180, timber=up, storey=storey)
            back = "wall_window_thin" if (up and i == mid_x) else "wall_plain"
            if not ruined or i != 0:
                wall(f"{tag}Back{i}", back, x, y, -half_z, 0, timber=up, storey=storey)
        for i, z in enumerate(zs):
            side = "wall_window" if i == mid_z else "wall_plain"
            if not ruined or i < len(zs) - 1:
                wall(f"{tag}Left{i}", side, -half_x, y, z, 90, timber=up, storey=storey)
            if not ruined or i > 0:
                wall(f"{tag}Right{i}", side, half_x, y, z, 270, timber=up, storey=storey)
        # The open hall keeps ALL FOUR corner posts. They are what the roof reads as standing on,
        # and dropping the two on the open side leaves an eight-metre span floating in mid-air.
        for i, (cx, cz) in enumerate([(-half_x, -half_z), (half_x, -half_z),
                                      (-half_x, half_z), (half_x, half_z)]):
            put(f"{tag}Corner{i}", "corner", cx, y, cz)

    for i, x in enumerate(xs):
        if not open_hall:
            put(f"BaseFront{i}", "wall_base", x, 0.0, half_z, 180)
        put(f"BaseBack{i}", "wall_base", x, 0.0, -half_z, 0)
    for i, z in enumerate(zs):
        put(f"BaseLeft{i}", "wall_base", -half_x, 0.0, z, 90)
        put(f"BaseRight{i}", "wall_base", half_x, 0.0, z, 270)

    eaves = storeys * STOREY
    if not ruined:
        if roof_axis == "z":
            put("GableFront", gable, 0.0, eaves, half_z, 180)
            put("GableBack", gable, 0.0, eaves, -half_z, 0)
        else:
            put("GableLeft", gable, -half_x, eaves, 0.0, 90)
            put("GableRight", gable, half_x, eaves, 0.0, 270)
    # ⚠️ A HOLLOW HOUSE'S DOOR HANGS OPEN, and it has to. The leaf is a separate piece with no
    # collider of its own, and hollow mode deliberately leaves no collider across the door module —
    # so a shut leaf is a door the player walks straight THROUGH, which reads as clipping rather than
    # as an entrance. Swung back on its hinge it reads as a house someone lives in.
    # ⚠️ The hinge goes on the EDGE of the opening, not its centre. The leaf's origin IS its hinge
    # (local x -0.05..1.07), so an origin at the module centre swings the door to stand edge-on in
    # the middle of its own doorway — which renders as a post across the entrance.
    if open_hall:
        pass  # no front wall, so no doorway and nothing to hang a leaf on
    elif hollow:
        door_index = max(0, min(door_index, len(xs) - 1))
        put("Door", "door", xs[door_index] - 0.56, 0.0, half_z + 0.05, 270)
    else:
        door_index = max(0, min(door_index, len(xs) - 1))
        put("Door", "door", xs[door_index] + 0.51, 0.0, half_z + 0.20, 180)
    if not ruined:
        put("Roof", roof, 0.0, eaves, 0.0, 0 if roof_axis == "z" else 90)
        chimney_module = "chimney_alt" if chimney == "right" else "chimney"
        chimney_x = half_x - 1.0 if chimney == "right" else -half_x + 1.0
        put("Chimney", chimney_module, chimney_x, eaves + 1.06, -half_z + 1.0)
        if dormer:
            put("Dormer", "roof_dormer", 0.0, eaves + 0.15, half_z - 0.35, 180)
        if awning:
            put("Awning", "roof_awning", 0.0, 2.35, half_z + 0.75, 180)
            put("AwningSupports", "roof_supports", 0.0, 0.0, half_z + 0.35, 180)
        if balcony and storeys > 1:
            put("Balcony", "balcony_straight", 0.0, STOREY, half_z + 0.28, 180)
        if stairs:
            put("EntryStairs", "stairs_exterior", xs[door_index], 0.0, half_z + 1.1, 180)
        if weathering:
            put("Weathering", "vine", half_x - 0.25, 0.0, -half_z - 0.03, 0)

    # ⚠️ The floor is laid before the colliders below so a hollow house has something to stand on at
    # y = 0 exactly. One tile per module, because the kit's floor piece IS one module (2.00 x 2.00).
    if hollow and not open_hall:
        for i, x in enumerate(xs):
            for j, z in enumerate(zs):
                put(f"Floor{i}_{j}", "floor_wood", x, 0.0, z)

    # Colliders. Solid: one box on the whole shell. Hollow: one per wall module, and NONE across the
    # door module (front slot 0) — that absence is the doorway, and it is the whole of the feature.
    # Corners and the base trim get none either: they are thin decoration inside the wall planes, and
    # a collider on each would pinch the opening without appearing to.
    walls = []
    if per_module:
        for i, x in enumerate(xs):
            if (not open_hall and i != door_index and
                    (not ruined or i != len(xs) - 1)):
                walls.append((f"WallFront{i}", x, half_z, "x"))
            if not ruined or i != 0:
                walls.append((f"WallBack{i}", x, -half_z, "x"))
        for i, z in enumerate(zs):
            if not ruined or i < len(zs) - 1:
                walls.append((f"WallLeft{i}", -half_x, z, "z"))
            if not ruined or i > 0:
                walls.append((f"WallRight{i}", half_x, z, "z"))

    ext = "".join(
        '[ext_resource type="PackedScene" path="res://assets/models/architecture/mod_%s.gltf" id="%s"]\n'
        % (m, ids[m]) for m in modules)
    depth = deep * MODULE + 0.4
    width = wide * MODULE + 0.4

    if per_module:
        shapes = (f'[sub_resource type="BoxShape3D" id="Shape_wall_x"]\n'
                  f"size = Vector3({MODULE}, {eaves}, {THICKNESS})\n\n"
                  f'[sub_resource type="BoxShape3D" id="Shape_wall_z"]\n'
                  f"size = Vector3({THICKNESS}, {eaves}, {MODULE})\n\n")
        collider = '[node name="Colliders" type="StaticBody3D" parent="."]\n\n'
        if not open_hall and not ruined:
            shapes += (f'[sub_resource type="BoxShape3D" id="Shape_floor"]\n'
                       f"size = Vector3({wide * MODULE}, 0.2, {deep * MODULE})\n")
            collider += ('[node name="FloorShape" type="CollisionShape3D" parent="Colliders"]\n'
                         "transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, -0.1, 0)\n"
                         'shape = SubResource("Shape_floor")\n\n')
        for node, x, z, axis in walls:
            collider += (f'[node name="{node}" type="CollisionShape3D" parent="Colliders"]\n'
                         f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {x}, {eaves / 2.0}, {z})\n"
                         f'shape = SubResource("Shape_wall_{axis}")\n\n')
        if ruined:
            note = ("; RUINED: collision follows only the surviving wall modules. Missing walls and roof\n"
                    "; are genuinely absent -- there are no obsolete invisible colliders across the breaches.\n"
                    "; Terrain remains the floor so the ruin does not create a threshold or duplicate ground.")
        elif open_hall:
            note = ("; OPEN HALL (--open): three walls and no front run at all, so the whole south side is\n"
                    "; the way in. One collider per wall module and none where there is no wall. THE\n"
                    "; TERRAIN IS THE FLOOR - a laid floor would put a 20 cm lip across the open side, and\n"
                    "; CharacterBody3D has no step-up, so that lip is an invisible wall exactly where the\n"
                    "; building is supposed to be walked into. All four corner posts stay: the roof has to\n"
                    "; be seen to stand on something.")
        else:
            note = ("; ENTERABLE (--hollow): one collider per wall module and NONE across the door module,\n"
                    "; which is what makes the doorway an opening. The navmesh holes this carves are the\n"
                    "; intended result here — do not route an NPC through it. The floor is flush at y = 0\n"
                    "; because CharacterBody3D has no step-up and a lip would be an invisible wall.")
    else:
        shapes = (f'[sub_resource type="BoxShape3D" id="Shape_{name}"]\n'
                  f"size = Vector3({width}, {eaves}, {depth})\n")
        collider = ('[node name="Collider" type="StaticBody3D" parent="."]\n\n'
                    '[node name="Shape" type="CollisionShape3D" parent="Collider"]\n'
                    f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, {eaves / 2.0}, 0)\n"
                    f'shape = SubResource("Shape_{name}")\n')
        note = ("; The collider is ONE box on the whole shell, not one per module: fifty little static\n"
                "; bodies would carve fifty little holes in the navmesh, and the building is one obstacle.")

    command_flags = []
    if open_hall:
        command_flags.append("--open")
    elif hollow:
        command_flags.append("--hollow")
    elif ruined:
        command_flags.append("--ruined")
    if wall_family != "plaster":
        command_flags.append(f"--wall-family {wall_family}")
    if roof_axis != "z":
        command_flags.append(f"--roof-axis {roof_axis}")
    if door_index:
        command_flags.append(f"--door-index {door_index}")
    if chimney != "left":
        command_flags.append(f"--chimney {chimney}")
    for enabled, flag in ((shutters, "--shutters"), (dormer, "--dormer"),
                          (awning, "--awning"), (balcony, "--balcony"),
                          (stairs, "--stairs"), (weathering, "--weathering")):
        if enabled:
            command_flags.append(flag)
    regenerate = " ".join(["python tools/compose_building.py", name, str(wide), str(deep),
                           str(storeys), *command_flags])

    body = f'''[gd_scene load_steps={len(modules) + 4} format=3]

{ext}
{shapes}

; ====================================================================================================
; {name.upper()} ({wide}x{deep} modules, {storeys} storey{", open hall" if open_hall else ""}) - COMPOSED, not a monolithic mesh.
; Generated by tools/compose_building.py; regenerate with:
;     {regenerate}
; Read that file's header before editing this one - it carries the grid and the four traps this kit
; sets, every one of which is silent and visible only in a render.
;
; WARNING A WALL MODULE'S OUTER FACE IS ITS LOCAL -Z, so each face is yawed to point it outward
; (back 0, left 90, front 180, right 270). WARNING A WINDOW WALL IS AN OPENING and carries a separate
; insert; the gable ends close a roof that is otherwise open to the sky; the timber frame is an
; OVERLAY on a plain wall, not a wall. Judge this from BEHIND before believing it.
;
{note}
; ====================================================================================================

[node name="{name.title().replace("_", "")}" type="Node3D"]

[node name="Shell" type="Node3D" parent="."]

{"".join(nodes)}
{collider}'''
    out = f"scenes/props/bld_{name}.tscn"
    open(out, "w", encoding="utf-8", newline="\n").write(body)
    kind = "ruined" if ruined else "open hall" if open_hall else "hollow (enterable)" if hollow else "solid"
    print(f"{out}: {len(nodes)} module instances, {width} x {depth} m footprint, "
          f"eaves {eaves} m, {kind}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("name")
    parser.add_argument("wide", type=int)
    parser.add_argument("deep", type=int)
    parser.add_argument("storeys", type=int, choices=(1, 2))
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--hollow", action="store_true")
    mode.add_argument("--open", action="store_true", dest="open_hall")
    mode.add_argument("--ruined", action="store_true")
    parser.add_argument("--wall-family", choices=("plaster", "stone-ground", "stone"), default="plaster")
    parser.add_argument("--roof-axis", choices=("x", "z"), default="z")
    parser.add_argument("--door-index", type=int, default=0)
    parser.add_argument("--chimney", choices=("left", "right"), default="left")
    parser.add_argument("--shutters", action="store_true")
    parser.add_argument("--dormer", action="store_true")
    parser.add_argument("--awning", action="store_true")
    parser.add_argument("--balcony", action="store_true")
    parser.add_argument("--stairs", action="store_true")
    parser.add_argument("--weathering", action="store_true")
    opts = parser.parse_args()
    compose(opts.name, opts.wide, opts.deep, opts.storeys, hollow=opts.hollow,
            open_hall=opts.open_hall, wall_family=opts.wall_family, roof_axis=opts.roof_axis,
            door_index=opts.door_index, chimney=opts.chimney, shutters=opts.shutters,
            dormer=opts.dormer, awning=opts.awning, balcony=opts.balcony, stairs=opts.stairs,
            weathering=opts.weathering, ruined=opts.ruined)
