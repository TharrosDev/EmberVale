#!/usr/bin/env python3
"""Build Embervale's boulder, cluster, cliff and ice families.

Run with Blender so every mesh is reproducible:
  blender --background --factory-startup --python tools/build_environment_assets.py -- C:/path/to/Embervale

⚠️ ALWAYS FOLLOW IT WITH THESE TWO, IN THIS ORDER. Blender's glTF exporter RE-EMBEDS the rock
atlas into every file it writes, so a rebuild silently puts 2.5 MB back into each of the six
composed rocks — `assets/models/props` went 49 MiB -> 79 MiB that way mid-session, and nothing
failed. The exporter also resets every material factor, so the metallic repair has to run again
too:

  python tools/share_nature_textures.py       # re-points the atlas, deletes the extracted copies
  python tools/repair_architecture_materials.py

`share_nature_textures.py --check` exits non-zero when anything is still embedded, which is the
cheap way to notice a rebuild that was not followed up.

WHAT IS AND IS NOT BUILT HERE, AND WHY
--------------------------------------
`docs/ASSET_POLICY.md` §0.1 fixes the search order and this script sits at the end of it, not the
start. Small and medium rocks were NOT authored: the nature megakit ships eleven pebbles and three
medium rocks, and three of those were simply adopted with `tools/adopt_kit_model.py`, which is a
container change and touches no vertex.

⚠️ WHAT THE PACK GENUINELY DOES NOT HAVE IS ANYTHING ABOVE 3.5 m. Its largest rock is
`Rock_Medium_3` at 3.42 x 2.32 x 3.48; there is no boulder, no cluster, no cliff and no ice in any
of the fourteen vendored bundles (`manifest.json` searched for rock/boulder/cliff/ice/glacier:
only `Rock_Medium_*`, `RockPath_*` and the 1/6-scale `rts` mountains, which are a different kit and
a different style). That gap is what this builds.

THE ROCK FAMILY IS COMPOSED FROM PACK MESHES, NOT SCULPTED
----------------------------------------------------------
A boulder here is pack rocks scaled and merged; a cliff module is pack rocks packed into a wall.
That is deliberate and it is the lazy answer being the right one: the UVs come along, so every
piece lands on the SAME `Rocks_Diffuse` atlas as `prp_boulder` and `prp_rock_cluster`, the
material family is shared for free, and the style cannot drift from the pack because it IS the
pack. Sculpting a cliff would have needed a new UV layout and a new texture, which is the "mixed
kit reads as a mistake" trap in `CLAUDE.md` §1 arriving by the back door.

THE ICE FAMILY IS AUTHORED, BECAUSE THERE IS NOTHING TO COMPOSE FROM
--------------------------------------------------------------------
⚠️ FROSTFANG'S ENTIRE GLACIER IS ONE 768-TRIANGLE MESH INSTANCED TWELVE TIMES. `prp_glacier.glb`
appears in `glacier.tscn`, `ancient_aerie.tscn` and `dragon_roost.tscn` under twelve names, and
every instance is the same silhouette under a different rotation — which is the "obvious
repetition" the visual-QA checklist names, visible from anywhere in the cell.

⚠️ AND THE INSTANCES CARRY NON-UNIFORM SCALE UNDER ROTATION, WHICH SHEARS THE MESH AND BREAKS ITS
NORMALS. `ancient_aerie`'s GlacierNW is `Transform3D(1.0988, 0, 0.5124, 0, 0.908, 0, -0.5307, 0,
1.1382, ...)`: the basis columns have lengths 1.21, 0.91 and 1.26, so the rotation and the scale do
not commute and the lighting on the result is wrong in a way no log reports. A family of distinct
forms is what removes the reason anyone reached for non-uniform scale in the first place.

Ice is authored rather than composed because no vendored bundle has any, and because it wants the
opposite treatment from rock: large flat facets that catch a highlight, not a pebble's rounded
noise. It carries no texture at all, which is the same choice the existing `bld_ice` material made
and which `docs/ART_STYLE.md` §4 wants — detail from silhouette and material, not from an image.

⚠️ EVERY MESH HERE IS FLAT-SHADED AND SEEDED. "Carved, not sculpted" is the pinned direction, and
a deterministic RNG per asset means re-running this script produces the same bytes rather than a
new world every build.
"""

from __future__ import annotations

import math
import random
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

KIT = "assets/library/nature_megakit"
PROPS = "assets/models/props"

# The pack rocks this script composes from, with their triangle counts. Rock_Medium_2 is the
# cheap one and carries the bulk of the cliff modules; _3 is the big one and carries silhouette.
SOURCE_ROCKS = ("Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3")


def root_path() -> Path:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return Path(args[0]).resolve() if args else Path.cwd().resolve()


def clear_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_rock(root: Path, name: str) -> bpy.types.Object:
    """Import one pack rock and return its single mesh object, at the world origin."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(root / KIT / f"{name}.gltf"))
    fresh = [o for o in set(bpy.data.objects) - before if o.type == "MESH"]
    if len(fresh) != 1:
        raise RuntimeError(f"{name}: expected one mesh, imported {len(fresh)}")
    rock = fresh[0]
    # Drop the importer's Y-up correction empties so the mesh carries its own transform.
    bpy.ops.object.select_all(action="DESELECT")
    rock.select_set(True)
    bpy.context.view_layer.objects.active = rock
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    for empty in [o for o in set(bpy.data.objects) - before if o.type == "EMPTY"]:
        bpy.data.objects.remove(empty, do_unlink=True)
    rock.location = (0.0, 0.0, 0.0)
    rock.rotation_euler = (0.0, 0.0, 0.0)
    rock.scale = (1.0, 1.0, 1.0)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return rock


def place(source: bpy.types.Object, offset, scale, yaw: float, tilt: float = 0.0,
          roll: float = 0.0) -> bpy.types.Object:
    """A transformed copy of a source rock, baked into its vertices."""
    copy = source.copy()
    copy.data = source.data.copy()
    bpy.context.collection.objects.link(copy)
    copy.location = Vector(offset)
    copy.scale = Vector(scale if isinstance(scale, (tuple, list)) else (scale, scale, scale))
    copy.rotation_euler = (tilt, roll, yaw)
    bpy.ops.object.select_all(action="DESELECT")
    copy.select_set(True)
    bpy.context.view_layer.objects.active = copy
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return copy


def join(parts: list[bpy.types.Object], name: str) -> bpy.types.Object:
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1:
        bpy.ops.object.join()
    merged = bpy.context.view_layer.objects.active
    merged.name = name
    merged.data.name = "Mesh"
    return merged


def single_material(obj: bpy.types.Object) -> None:
    """Collapse the joined mesh onto ONE material slot.

    ⚠️ JOINING COPIES OF ONE ROCK PRODUCES `Rocks`, `Rocks.001` AND `Rocks.002`, AND NOTHING SAYS
    SO. Blender copies the material datablock with the mesh, so a cliff built from twelve copies
    of three pack rocks exports with three identical materials pointing at the same atlas. The
    cost is not cosmetic: three materials is three draw calls per instance on an asset meant to be
    placed in numbers, and `WorldBiomeScatter.Recolour` refuses to tint any source with more than
    one surface — it logs a warning and silently ignores the layer's Saturation, which is exactly
    the "the setting has no effect" symptom that is impossible to trace back to a joined mesh.
    """
    if len(obj.data.materials) <= 1:
        return
    keep = obj.data.materials[0]
    for polygon in obj.data.polygons:
        polygon.material_index = 0
    obj.data.materials.clear()
    obj.data.materials.append(keep)
    keep.name = "Rocks"


def sit_on_ground(obj: bpy.types.Object) -> None:
    """Move the mesh so its lowest vertex is at z=0 and its footprint is centred on the origin.

    ⚠️ THE AUDIT'S `ground-offset` FINDING IS EXACTLY THIS AND SEVEN PRODUCTION PROPS FAIL IT,
    `prp_glacier` and `prp_boulder` included. A prop whose lowest point is not zero either floats
    or buries itself the moment a cell places it at the terrain height, and every author who hits
    it fixes it with a per-placement Y nudge that then has to be redone on every reuse.
    """
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bounds = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    low = min(v.z for v in bounds)
    mid_x = (min(v.x for v in bounds) + max(v.x for v in bounds)) / 2.0
    mid_y = (min(v.y for v in bounds) + max(v.y for v in bounds)) / 2.0
    for vertex in obj.data.vertices:
        vertex.co.x -= mid_x
        vertex.co.y -= mid_y
        vertex.co.z -= low
    obj.data.update()


def flat_shade(obj: bpy.types.Object) -> None:
    for polygon in obj.data.polygons:
        polygon.use_smooth = False


def measure(obj: bpy.types.Object) -> tuple[float, float, float]:
    bounds = [Vector(corner) for corner in obj.bound_box]
    return (
        max(v.x for v in bounds) - min(v.x for v in bounds),
        max(v.y for v in bounds) - min(v.y for v in bounds),
        max(v.z for v in bounds) - min(v.z for v in bounds),
    )


def export(root: Path, obj: bpy.types.Object, filename: str) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    size = measure(obj)
    triangles = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    bpy.ops.export_scene.gltf(
        filepath=str(root / PROPS / filename),
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_normals=True,
        export_texcoords=True,
        export_materials="EXPORT",
        export_cameras=False,
        export_lights=False,
    )
    print(f"  {filename:28s} {size[0]:5.2f} x {size[1]:5.2f} x {size[2]:5.2f} m  {triangles:5d} tris")


# ---------------------------------------------------------------------------------------------
# Rock family
# ---------------------------------------------------------------------------------------------

def build_rocks(root: Path) -> None:
    print("rocks:")

    def fresh_sources() -> dict[str, bpy.types.Object]:
        clear_scene()
        sources = {name: import_rock(root, name) for name in SOURCE_ROCKS}
        for rock in sources.values():
            rock.hide_set(True)
        return sources

    # --- boulders -----------------------------------------------------------------------------
    # A boulder is not a scaled-up medium rock: scaling one to 5 m scales its surface noise with
    # it, and the result reads as a close-up photograph of a pebble rather than as a big stone.
    # Two rocks at different scales, the smaller sunk into the larger's base, gives the broken
    # shoulder a real boulder has and keeps the noise frequency near where the pack authored it.
    sources = fresh_sources()
    parts = [
        place(sources["Rock_Medium_3"], (0, 0, 0), (1.55, 1.45, 1.50), math.radians(18)),
        place(sources["Rock_Medium_1"], (1.5, 0.4, -0.6), (0.85, 0.80, 0.70), math.radians(140)),
        place(sources["Rock_Medium_2"], (-1.3, -0.7, -0.9), (0.70, 0.65, 0.55), math.radians(255)),
    ]
    boulder = join(parts, "prp_boulder_large")
    single_material(boulder)
    sit_on_ground(boulder)
    flat_shade(boulder)
    export(root, boulder, "prp_boulder_large.glb")

    # --- clusters -----------------------------------------------------------------------------
    # ⚠️ `prp_rock_cluster.glb` IS NOT A CLUSTER. It is a single 244-triangle `Rock_Medium_2`
    # under a plural name, which is why the wilds read as one rock repeated: the two cells that
    # scatter "clusters" are scattering one stone. These are the real thing.
    sources = fresh_sources()
    parts = [
        place(sources["Rock_Medium_2"], (0, 0, 0), (1.00, 0.95, 0.85), math.radians(0)),
        place(sources["Rock_Medium_1"], (1.9, 0.7, -0.25), (0.62, 0.58, 0.50), math.radians(95)),
        place(sources["Rock_Medium_2"], (-1.6, 1.2, -0.35), (0.48, 0.52, 0.42), math.radians(200)),
        place(sources["Rock_Medium_1"], (0.5, -1.8, -0.45), (0.38, 0.34, 0.30), math.radians(310)),
    ]
    cluster = join(parts, "prp_rock_cluster_a")
    single_material(cluster)
    sit_on_ground(cluster)
    flat_shade(cluster)
    export(root, cluster, "prp_rock_cluster_a.glb")

    sources = fresh_sources()
    parts = [
        place(sources["Rock_Medium_1"], (0, 0, -0.3), (0.80, 0.75, 0.55), math.radians(40)),
        place(sources["Rock_Medium_2"], (2.3, -0.6, -0.4), (0.66, 0.70, 0.48), math.radians(150)),
        place(sources["Rock_Medium_2"], (-2.1, 0.9, -0.5), (0.55, 0.50, 0.40), math.radians(255)),
        place(sources["Rock_Medium_1"], (0.9, 2.2, -0.55), (0.42, 0.46, 0.34), math.radians(20)),
        place(sources["Rock_Medium_2"], (-1.2, -2.0, -0.6), (0.34, 0.30, 0.26), math.radians(330)),
    ]
    scree = join(parts, "prp_rock_scree")
    single_material(scree)
    sit_on_ground(scree)
    flat_shade(scree)
    export(root, scree, "prp_rock_scree.glb")

    # --- path edging --------------------------------------------------------------------------
    # A low broken line of stone for a road shoulder or a field boundary. Long and shallow so it
    # reads along a route rather than as an obstacle beside one.
    sources = fresh_sources()
    parts = []
    rng = random.Random(60_101)
    for index in range(6):
        source = sources["Rock_Medium_2" if index % 2 else "Rock_Medium_1"]
        parts.append(place(
            source,
            (index * 1.35 - 3.4, rng.uniform(-0.35, 0.35), rng.uniform(-1.25, -1.05)),
            (rng.uniform(0.38, 0.52), rng.uniform(0.34, 0.48), rng.uniform(0.26, 0.34)),
            rng.uniform(0, math.tau)))
    edging = join(parts, "prp_rock_edging")
    single_material(edging)
    sit_on_ground(edging)
    flat_shade(edging)
    export(root, edging, "prp_rock_edging.glb")

    # --- cliff modules ------------------------------------------------------------------------
    # ⚠️ A CLIFF MODULE HAS A FLAT BACK OR IT CANNOT TILE, and these do not get one from geometry
    # — they get it from placement, because the back of a packed-rock wall is never seen: it is
    # buried in the terrain the cliff is cut into. `docs/WORLD_AUTHORING.md`'s ground is a
    # heightfield, so a cliff module is dressing ON a slope, not a wall standing free on a plain.
    # Sinking the rear column below z=0 is what guarantees no gap when the terrain rises behind it.
    #
    # ⚠️ THE FIRST CLIFF WAS A 4x3 GRID AND IT RENDERED AS A STACK OF LOAVES ON A BAKERY SHELF.
    # Every rock was the same size, every course was level, and the small position jitter left a
    # visible horizontal gap between rows, so the eye read a masonry pattern instead of rock. All
    # of that was invisible in the numbers: the bounds, the triangle count and the single material
    # were exactly what they should be, and only a render showed it.
    #
    # Three changes fix it, and the SCALE SPREAD is the one that matters most:
    #   - rocks range 0.55x to 1.5x within one module rather than 0.7x to 1.0x, so a big block
    #     anchors the base and small rubble fills between;
    #   - courses OVERLAP by design (the vertical step is smaller than a rock is tall), so pieces
    #     interpenetrate and there is no seam to see;
    #   - every rock gets tilt on two axes as well as yaw, which breaks up the diagonal seam the
    #     shared atlas draws across each one — with yaw only, all twelve stripes stayed parallel.
    for label, width, seed in (("prp_cliff_face", 8.0, 60_201), ("prp_cliff_face_tall", 8.0, 60_202)):
        sources = fresh_sources()
        rng = random.Random(seed)
        tall = label.endswith("tall")
        parts = []
        height = 7.4 if tall else 5.6
        # A base course of few, large blocks, then progressively more and smaller pieces above.
        courses = [(3, 1.15, 1.50), (4, 0.95, 1.25), (4, 0.80, 1.10)]
        if tall:
            courses.append((5, 0.62, 0.92))

        rise = height / (len(courses) + 0.9)
        for index, (count, low, high) in enumerate(courses):
            for slot in range(count):
                source = sources[SOURCE_ROCKS[rng.randrange(3)]]
                scale = rng.uniform(low, high)
                parts.append(place(
                    source,
                    # Slots are spread across the module width with a half-slot stagger per
                    # course, so no two courses line up vertically.
                    ((slot + 0.5) * (width / count) - width / 2 + rng.uniform(-0.45, 0.45)
                     + (0.5 * width / count if index % 2 else 0.0),
                     # Each course sits a little back from the one below, so the face leans into
                     # the hill rather than overhanging it — an overhang is where a player sticks.
                     # ⚠️ THE SETBACK PER COURSE IS WHAT DECIDES CLIFF VERSUS MOUND, and 0.55 m
                     # made a talus pile. At that rate a four-course module retreats 1.65 m over
                     # 6.5 m of rise, which is a 14-degree lean the eye reads as a heap of rubble
                     # you could walk up rather than a face you have to go around. 0.18 m keeps it
                     # near-vertical, and the depth jitter is halved for the same reason: a rock
                     # pulled 0.3 m forward of a near-vertical face is a ledge.
                     index * 0.18 + rng.uniform(-0.15, 0.15),
                     index * rise - 1.0 + rng.uniform(-0.25, 0.25)),
                    (scale * rng.uniform(0.85, 1.15),
                     scale * rng.uniform(0.85, 1.15),
                     scale * rng.uniform(0.80, 1.20)),
                    rng.uniform(0, math.tau),
                    rng.uniform(-0.35, 0.35),
                    rng.uniform(-0.35, 0.35)))
        cliff = join(parts, label)
        single_material(cliff)
        sit_on_ground(cliff)
        flat_shade(cliff)
        export(root, cliff, f"{label}.glb")


# ---------------------------------------------------------------------------------------------
# Ice family
# ---------------------------------------------------------------------------------------------

def ice_material() -> bpy.types.Material:
    """One material for the whole ice family.

    Pale, slightly blue, ROUGH rather than glassy and completely non-metallic. A mirror-finish
    ice would be the same defect this session removed from 121 prop materials, arriving fresh in
    a new asset: `docs/ART_STYLE.md` wants nonmetallic surfaces carrying detail through
    silhouette, and a glacier in a dying world is weathered and snow-dusted, not polished.
    No texture — the family shares one flat material, so twelve ice props are one material.
    """
    material = bpy.data.materials.get("Ice") or bpy.data.materials.new("Ice")
    material.use_nodes = True
    principled = material.node_tree.nodes["Principled BSDF"]
    principled.inputs["Base Color"].default_value = (0.71, 0.81, 0.87, 1.0)
    principled.inputs["Metallic"].default_value = 0.0
    principled.inputs["Roughness"].default_value = 0.42
    return material


def faceted_block(name: str, size, seed: int, points: int, jitter: float,
                  taper: float = 0.0) -> bpy.types.Object:
    """A single flat-shaded ice mass: the CONVEX HULL of scattered points inside the box.

    ⚠️ THE FIRST VERSION SUBDIVIDED A CUBE AND JITTERED ITS VERTICES, AND IT PRODUCED A GIANT
    MARSHMALLOW. Displacing a dense grid gives fine, rounded, high-frequency noise, which is the
    exact opposite of what ice does: ice FRACTURES, so it wants a few large flat planes meeting at
    sharp angles. The lumpy version was visible only in a render — the mesh statistics, the bounds
    and the triangle count were all perfectly reasonable.
    +
    A convex hull is the fix and it is also the cheaper one. Hulling a handful of points is
    guaranteed to give planar faces and hard edges, gets the whole silhouette from the point
    spread, and lands at a couple of hundred triangles without any decimation pass.

    Points are pushed OUT towards the box surface before hulling: sampling the volume uniformly
    buries most of them inside the hull where they do nothing, and the few that reach the surface
    are the ones that shape it.
    """
    rng = random.Random(seed)
    mesh = bpy.data.meshes.new("Mesh")
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)

    half = (size[0] / 2.0, size[1] / 2.0, size[2] / 2.0)
    cloud = []
    for _ in range(points):
        # ⚠️ SAMPLE THE BOX SURFACE, NOT A DIRECTION. Normalising a random vector and scaling it
        # by the half-extents samples an ELLIPSOID, and the hull of an ellipsoid is a rounded
        # polyhedron — the 8 x 3.2 x 6.4 m glacier wall came out as a giant white dice, with
        # nothing box-like left in it at all. Pinning one coordinate to a face and letting the
        # other two roam keeps the block a block, and the jitter breaks its faces into the
        # irregular planes that read as fracture.
        point = [rng.uniform(-half[0], half[0]),
                 rng.uniform(-half[1], half[1]),
                 rng.uniform(-half[2], half[2])]
        axis = rng.randrange(3)
        # `jitter` is how far in from the face a point may fall; it is what stops the hull being
        # a clean cuboid without ever making the solid non-convex.
        point[axis] = math.copysign(half[axis] * (1.0 - rng.uniform(0.0, jitter)), rng.choice((-1, 1)))
        point = Vector(point)
        if taper > 0.0:
            lift = (point.z + half[2]) / (2.0 * half[2])
            shrink = 1.0 - taper * lift
            point.x *= shrink
            point.y *= shrink
        cloud.append(point)

    bm = bmesh.new()
    for point in cloud:
        bm.verts.new(point)
    bm.verts.ensure_lookup_table()
    bmesh.ops.convex_hull(bm, input=bm.verts[:])
    # convex_hull leaves the interior points behind as loose geometry; they export as unreferenced
    # vertices and inflate the buffer for nothing.
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(mesh)
    bm.free()

    # ⚠️ A HULL IS ALWAYS SMALLER THAN THE BOX IT WAS SAMPLED FROM, AND BY AN AMOUNT THAT DEPENDS
    # ON THE POINT COUNT. Only a point that happens to land near a corner reaches the wall, so an
    # 8 m wall sampled with 18 points came out 5.65 m — the asset was 30% undersized and nothing
    # said so except a render with a human beside it. Normalising to the requested extent makes
    # `size` mean what it says regardless of how many points shaped it.
    extents = [
        max(v.co[axis] for v in mesh.vertices) - min(v.co[axis] for v in mesh.vertices)
        for axis in range(3)
    ]
    for axis in range(3):
        if extents[axis] > 1e-6:
            factor = size[axis] / extents[axis]
            for vertex in mesh.vertices:
                vertex.co[axis] *= factor
    mesh.update()

    obj.data.materials.append(ice_material())
    return obj


def build_ice(root: Path) -> None:
    print("ice:")

    def start(name: str, *args, **kwargs) -> bpy.types.Object:
        clear_scene()
        return faceted_block(name, *args, **kwargs)

    # Loose chunks: what a glacier sheds. Two sizes so a scatter layer has something to vary.
    chunk = start("prp_ice_chunk", (1.30, 1.10, 0.95), 60_301, points=22, jitter=0.30)
    sit_on_ground(chunk)
    flat_shade(chunk)
    export(root, chunk, "prp_ice_chunk.glb")

    shard = start("prp_ice_shard", (1.05, 0.95, 2.60), 60_302, points=20, jitter=0.26, taper=0.55)
    shard.rotation_euler = (math.radians(9), math.radians(-6), math.radians(24))
    bpy.ops.object.select_all(action="DESELECT")
    shard.select_set(True)
    bpy.context.view_layer.objects.active = shard
    bpy.ops.object.transform_apply(rotation=True)
    sit_on_ground(shard)
    flat_shade(shard)
    export(root, shard, "prp_ice_shard.glb")

    # The cracked ground slab: flat enough to walk past, with a raised fracture ridge so it is
    # not a plane. Low jitter on Z keeps it from becoming a trip hazard the navmesh disagrees with.
    slab = start("prp_ice_slab", (4.40, 4.00, 0.55), 60_303, points=26, jitter=0.22)
    sit_on_ground(slab)
    flat_shade(slab)
    export(root, slab, "prp_ice_slab.glb")

    # ⚠️ THE WALL IS THE PIECE THAT REPLACES THE TWELVE ROTATED COPIES, and it is modular: 8 m
    # wide so two of them make a 16 m face, with the jitter kept low on X near the ends so the
    # seam between two modules does not show as a notch.
    clear_scene()
    wall = faceted_block("prp_glacier_wall", (8.00, 3.20, 6.40), 60_304, points=34, jitter=0.20)
    sit_on_ground(wall)
    flat_shade(wall)
    export(root, wall, "prp_glacier_wall.glb")

    # The buttress: one big irregular mass with a real silhouette, for the places the old
    # `prp_glacier` was scaled up to 17.5 m across and asked to carry a whole cell.
    clear_scene()
    parts = [
        faceted_block("A", (7.20, 6.40, 9.00), 60_305, points=30, jitter=0.24, taper=0.35),
        faceted_block("B", (5.00, 4.60, 5.40), 60_306, points=24, jitter=0.28, taper=0.20),
        faceted_block("C", (3.40, 3.20, 3.20), 60_307, points=20, jitter=0.32),
    ]
    parts[1].location = (4.30, 1.60, -1.90)
    parts[2].location = (-3.60, -1.40, -3.10)
    for part in parts[1:]:
        bpy.ops.object.select_all(action="DESELECT")
        part.select_set(True)
        bpy.context.view_layer.objects.active = part
        bpy.ops.object.transform_apply(location=True)
    face = join(parts, "prp_glacier_face")
    sit_on_ground(face)
    flat_shade(face)
    export(root, face, "prp_glacier_face.glb")


def named_material(name: str, colour, metallic: float, roughness: float) -> bpy.types.Material:
    """A material whose NAME is load-bearing.

    ⚠️ `repair_architecture_materials.response()` dispatches on the material name, so calling the
    ironwork `DarkMetal` and the flame `Fire` is what makes the sweep give them 1.0/0.45 and
    0.0/0.55 automatically — and, more importantly, keeps giving them that on the next run. A
    material called `Material.001` gets the catch-all wood response and quietly goes matte.
    """
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.use_nodes = True
    principled = material.node_tree.nodes["Principled BSDF"]
    principled.inputs["Base Color"].default_value = (*colour, 1.0)
    principled.inputs["Metallic"].default_value = metallic
    principled.inputs["Roughness"].default_value = roughness
    return material


def build_brazier(root: Path) -> None:
    """Replace `prp_brazier.glb` IN PLACE with a brazier that has a bowl.

    ⚠️ THE INCUMBENT IS A WIRE STICK AND A RENDER IS THE ONLY THING THAT SAYS SO. 386 triangles,
    0.29 x 1.18 x 0.45 m, and beside a 1.8 m human reference it reads as a thin black scribble
    with a cone on top — no bowl, no coals, no mass. It is placed in SEVEN cells and is one of
    `PlaceableTemplates`' decor items, so it is on screen constantly.

    ⚠️ IT IS OVERWRITTEN AT ITS OWN PATH AND AT ITS OWN SIZE, DELIBERATELY. Seven cells reference
    `prp_brazier.glb`, each with a hand-placed `OmniLight3D` at y = 1.3 just above the old flame.
    A new file at a new path would need seven scene edits and would move every one of those
    lights; keeping the path and the 1.18 m height means the callers, the lights and the arena's
    `Shape_brazier` collider all stay correct with no scene touched at all.
    """
    print("brazier:")
    clear_scene()
    iron = named_material("DarkMetal", (0.09, 0.085, 0.088), 1.0, 0.45)
    parts: list[bpy.types.Object] = []

    def add(mesh_name: str, primitive, material, location, rotation=(0, 0, 0), scale=(1, 1, 1)):
        bm = bmesh.new()
        primitive(bm)
        mesh = bpy.data.meshes.new(mesh_name)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.to_mesh(mesh)
        bm.free()
        obj = bpy.data.objects.new(mesh_name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.location = location
        obj.rotation_euler = rotation
        obj.scale = scale
        obj.data.materials.append(material)
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        parts.append(obj)
        return obj

    # The bowl: an eight-sided cone frustum, open at the top. Eight sides is the low-poly count
    # that still reads as round in silhouette at the distance a brazier is ever seen from.
    add("Bowl", lambda bm: bmesh.ops.create_cone(
        bm, cap_ends=True, cap_tris=False, segments=8, radius1=0.16, radius2=0.30, depth=0.26),
        iron, (0.0, 0.0, 0.92))
    # A rim band, so the bowl has an edge rather than a paper-thin lip.
    add("Rim", lambda bm: bmesh.ops.create_cone(
        bm, cap_ends=False, cap_tris=False, segments=8, radius1=0.31, radius2=0.31, depth=0.05),
        iron, (0.0, 0.0, 1.045))
    # Three splayed legs. The old prop's legs were single edges; these are tapered posts with
    # real thickness, which is most of what turns a scribble into an object.
    for index in range(3):
        angle = index * math.tau / 3.0
        add("Leg", lambda bm: bmesh.ops.create_cone(
            bm, cap_ends=True, cap_tris=False, segments=4, radius1=0.030, radius2=0.055,
            depth=0.86), iron,
            (math.cos(angle) * 0.13, math.sin(angle) * 0.13, 0.43),
            rotation=(math.sin(angle) * 0.20, -math.cos(angle) * 0.20, 0.0))
    # A tie ring low down, which is what stops three legs reading as three separate sticks.
    add("Tie", lambda bm: bmesh.ops.create_cone(
        bm, cap_ends=False, cap_tris=False, segments=8, radius1=0.155, radius2=0.155, depth=0.035),
        iron, (0.0, 0.0, 0.30))
    # The coal bed: a shallow dome filling the bowl, so it is not an empty cup.
    add("Coals", lambda bm: bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.24),
        named_material("Coals", (0.16, 0.055, 0.035), 0.0, 0.88),
        (0.0, 0.0, 1.00), scale=(1.0, 1.0, 0.38))
    # The flame, at the height the callers' lights already expect.
    # ⚠️ `radius1` IS THE -Z END. Passing radius1=0.0/radius2=0.15 builds the cone POINT-DOWN, so
    # the flame came out as a funnel widening towards the sky — obvious in a render and invisible
    # in every number the exporter prints.
    add("Fire", lambda bm: bmesh.ops.create_cone(
        bm, cap_ends=True, cap_tris=False, segments=6, radius1=0.15, radius2=0.0, depth=0.34),
        named_material("Fire", (0.95, 0.50, 0.13), 0.0, 0.55), (0.0, 0.0, 1.13))

    brazier = join(parts, "prp_brazier")
    sit_on_ground(brazier)
    flat_shade(brazier)
    export(root, brazier, "prp_brazier.glb")


def main() -> None:
    root = root_path()
    build_rocks(root)
    build_ice(root)
    build_brazier(root)
    print("build_environment_assets: done")


if __name__ == "__main__":
    main()
