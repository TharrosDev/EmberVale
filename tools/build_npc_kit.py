#!/usr/bin/env python3
"""Build the Embervale modular NPC kit and correct human GLB material response.

Run through Blender so one command owns both the authored kit and the byte-safe material patch:
  blender --background --python tools/build_npc_kit.py -- C:/path/to/Embervale

Existing humanoid GLBs are never imported or re-exported. Only their glTF JSON material factors are
changed; buffers holding geometry, weights, bones and animation remain byte-identical.
"""

from __future__ import annotations

import json
import math
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Vector


HUMANS = (
    "npc_adventurer_f.glb", "npc_guild_rep.glb", "npc_hooded.glb", "npc_innkeeper.glb",
    "npc_kael.glb", "npc_merchant_f.glb", "npc_merchant_m.glb", "npc_townsman.glb",
    "npc_townswoman.glb", "npc_vendor.glb", "npc_woman_dress.glb",
)


def root_path() -> Path:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return Path(args[0]).resolve() if args else Path.cwd().resolve()


def read_glb(path: Path) -> tuple[dict, bytes]:
    raw = path.read_bytes()
    if raw[:4] != b"glTF":
        raise ValueError(f"{path} is not GLB")
    _, version, length = struct.unpack_from("<III", raw, 0)
    if version != 2 or length != len(raw):
        raise ValueError(f"invalid GLB header in {path}")
    document = None
    binary = b""
    offset = 12
    while offset < length:
        size, kind = struct.unpack_from("<II", raw, offset)
        payload = raw[offset + 8:offset + 8 + size]
        if kind == 0x4E4F534A:
            document = json.loads(payload.rstrip(b" \0"))
        elif kind == 0x004E4942:
            binary = payload
        offset += 8 + size
    if document is None:
        raise ValueError(f"no JSON chunk in {path}")
    return document, binary


def write_glb(path: Path, document: dict, binary: bytes) -> None:
    encoded = json.dumps(document, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    encoded += b" " * ((4 - len(encoded) % 4) % 4)
    binary += b"\0" * ((4 - len(binary) % 4) % 4)
    total = 12 + 8 + len(encoded) + (8 + len(binary) if binary else 0)
    output = bytearray(struct.pack("<III", 0x46546C67, 2, total))
    output += struct.pack("<II", len(encoded), 0x4E4F534A) + encoded
    if binary:
        output += struct.pack("<II", len(binary), 0x004E4942) + binary
    path.write_bytes(output)


def material_response(name: str) -> tuple[float, float]:
    key = name.lower()
    if "gold" in key:
        return 0.9, 0.32
    if "metal" in key:
        return 0.86, 0.42
    if any(word in key for word in ("skin",)):
        return 0.0, 0.68
    if any(word in key for word in ("eye", "pupil")):
        return 0.0, 0.48
    if any(word in key for word in ("hair", "brow", "moustache")):
        return 0.0, 0.8
    if any(word in key for word in ("boot", "shoe", "brown", "leather")):
        return 0.0, 0.72
    return 0.0, 0.86


def patch_humans(root: Path) -> None:
    folder = root / "assets" / "models" / "characters"
    for filename in HUMANS:
        path = folder / filename
        document, binary = read_glb(path)
        structural = json.dumps({key: document.get(key) for key in
            ("accessors", "animations", "bufferViews", "meshes", "nodes", "skins")}, sort_keys=True)
        changed = 0
        for material in document.get("materials", []):
            pbr = material.setdefault("pbrMetallicRoughness", {})
            metallic, roughness = material_response(material.get("name", ""))
            if pbr.get("metallicFactor") != metallic or pbr.get("roughnessFactor") != roughness:
                pbr["metallicFactor"] = metallic
                pbr["roughnessFactor"] = roughness
                changed += 1
            if filename == "npc_townsman.glb":
                if material.get("name") == "Worker_Yellow":
                    pbr["baseColorFactor"] = [0.18, 0.12, 0.035, 1.0]
                elif material.get("name") == "Worker_Vest":
                    pbr["baseColorFactor"] = [0.20, 0.055, 0.025, 1.0]
        write_glb(path, document, binary)
        verify, verify_binary = read_glb(path)
        verified_structural = json.dumps({key: verify.get(key) for key in
            ("accessors", "animations", "bufferViews", "meshes", "nodes", "skins")}, sort_keys=True)
        if verify_binary != binary or verified_structural != structural:
            raise RuntimeError(f"material patch altered structural payload: {filename}")
        print(f"material patch: {filename}: {changed} material(s), rig payload preserved")


def reset_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras,
                       bpy.data.lights):
        for item in list(datablocks):
            datablocks.remove(item)


def material(name: str, color: tuple[float, float, float, float], metallic=0.0, roughness=0.82):
    result = bpy.data.materials.new(name)
    result.diffuse_color = color
    result.use_nodes = True
    shader = result.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = color
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    return result


def cube(label: str, location, dimensions, mat, bevel=0.015):
    bpy.ops.mesh.primitive_cube_add(location=location)
    obj = bpy.context.object
    obj.name = label
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new("SoftenedEdges", "BEVEL")
        mod.width = bevel
        mod.segments = 2
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.data.materials.append(mat)
    return obj


def cylinder(label: str, location, radius, depth, mat, vertices=12, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location,
                                       rotation=rotation)
    obj = bpy.context.object
    obj.name = label
    obj.data.materials.append(mat)
    return obj


def torus(label: str, location, major, minor, mat, rotation=(0, 0, 0), major_segments=16):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=major_segments,
                                    minor_segments=6, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = label
    obj.data.materials.append(mat)
    return obj


def sphere(label: str, location, scale, mat, segments=16, rings=8):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=location)
    obj = bpy.context.object
    obj.name = label
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    return obj


def curve_tube(label: str, points, radius, mat):
    data = bpy.data.curves.new(label, "CURVE")
    data.dimensions = "3D"
    data.resolution_u = 1
    data.bevel_depth = radius
    data.bevel_resolution = 1
    spline = data.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for target, point in zip(spline.points, points):
        target.co = (*point, 1.0)
    obj = bpy.data.objects.new(label, data)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def trapezoid_panel(label: str, y: float, z: float, top: float, bottom: float,
                    height: float, depth: float, mat):
    z0, z1 = z - height / 2, z + height / 2
    y0, y1 = y - depth / 2, y + depth / 2
    vertices = [
        (-bottom / 2, y0, z0), (bottom / 2, y0, z0), (top / 2, y0, z1), (-top / 2, y0, z1),
        (-bottom / 2, y1, z0), (bottom / 2, y1, z0), (top / 2, y1, z1), (-top / 2, y1, z1),
    ]
    faces = [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2),
             (2, 6, 7, 3), (4, 0, 3, 7)]
    mesh = bpy.data.meshes.new(label)
    mesh.from_pydata(vertices, [], faces)
    mesh.materials.append(mat)
    obj = bpy.data.objects.new(label, mesh)
    bpy.context.collection.objects.link(obj)
    bevel = obj.modifiers.new("SoftenedEdges", "BEVEL")
    bevel.width = 0.01
    bevel.segments = 2
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    return obj


def join(name: str, objects) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.convert(target="MESH")
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    bpy.context.scene.cursor.location = Vector((0, 0, 0))
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    result.location = Vector((0, 0, 0))


def tabard(name, cloth, iron):
    parts = [
        trapezoid_panel("Front", -0.165, -0.18, 0.30, 0.25, 0.66, 0.025, cloth),
        trapezoid_panel("Back", 0.135, -0.18, 0.30, 0.25, 0.66, 0.025, cloth),
        cube("ShoulderL", (-0.15, -0.02, 0.16), (0.10, 0.34, 0.08), cloth, 0.01),
        cube("ShoulderR", (0.15, -0.02, 0.16), (0.10, 0.34, 0.08), cloth, 0.01),
        cylinder("Seal", (0, -0.23, 0.02), 0.055, 0.018, iron, vertices=12,
                 rotation=(math.pi / 2, 0, 0)),
    ]
    join(name, parts)


def build_kit(root: Path) -> None:
    reset_scene()
    cloth_charcoal = material("Cloth_Charcoal", (0.07, 0.075, 0.08, 1), roughness=0.9)
    cloth_blue = material("Cloth_Dawnwarden", (0.055, 0.12, 0.19, 1), roughness=0.9)
    cloth_rust = material("Cloth_Syndicate", (0.25, 0.075, 0.035, 1), roughness=0.9)
    cloth_ash = material("Cloth_AshHunter", (0.12, 0.115, 0.10, 1), roughness=0.92)
    cloth_archive = material("Cloth_Archive", (0.075, 0.16, 0.15, 1), roughness=0.9)
    cloth_ember = material("Cloth_Emberbound", (0.22, 0.055, 0.035, 1), roughness=0.9)
    cloth_ochre = material("Cloth_Ochre", (0.24, 0.16, 0.055, 1), roughness=0.9)
    leather = material("WornLeather", (0.16, 0.075, 0.035, 1), roughness=0.72)
    # One shared leather keeps the kit under the audit's material ceiling. Shape and lighting,
    # rather than a near-black duplicate material, separate belts and book covers at game scale.
    dark_leather = leather
    iron = material("DarkIron", (0.12, 0.13, 0.14, 1), metallic=0.86, roughness=0.42)
    brass = material("WornBrass", (0.34, 0.19, 0.055, 1), metallic=0.78, roughness=0.4)
    parchment = material("Parchment", (0.55, 0.42, 0.22, 1), roughness=0.88)
    wood = material("ToolWood", (0.16, 0.075, 0.03, 1), roughness=0.74)

    join("OuterVest", [
        cube("VestFront", (0, -0.175, -0.04), (0.43, 0.055, 0.48), leather, 0.025),
        cube("VestBack", (0, 0.14, -0.04), (0.43, 0.05, 0.48), leather, 0.025),
        cube("VestSideL", (-0.225, -0.01, -0.04), (0.045, 0.27, 0.42), leather, 0.015),
        cube("VestSideR", (0.225, -0.01, -0.04), (0.045, 0.27, 0.42), leather, 0.015),
    ])
    join("WorkApron", [
        trapezoid_panel("ApronBib", -0.165, -0.05, 0.27, 0.31, 0.36, 0.025, cloth_charcoal),
        trapezoid_panel("ApronSkirt", -0.17, -0.43, 0.34, 0.40, 0.48, 0.028, cloth_charcoal),
        cube("ApronBand", (0, -0.15, -0.22), (0.44, 0.04, 0.055), leather, 0.01),
    ])
    tabard("GuildTabardBlue", cloth_blue, brass)
    tabard("GuildTabardRust", cloth_rust, brass)
    tabard("GuildTabardAsh", cloth_ash, iron)
    tabard("GuildTabardArchive", cloth_archive, brass)
    tabard("GuildTabardEmber", cloth_ember, iron)
    tabard("GuildTabardOchre", cloth_ochre, brass)

    join("MerchantMantle", [
        torus("Collar", (0, 0, 0.08), 0.22, 0.055, cloth_ochre, major_segments=18),
        cube("LapelL", (-0.12, -0.19, -0.13), (0.13, 0.04, 0.46), cloth_ochre, 0.02),
        cube("LapelR", (0.12, -0.19, -0.13), (0.13, 0.04, 0.46), cloth_ochre, 0.02),
        cylinder("Clasp", (0, -0.225, 0.04), 0.045, 0.018, brass, vertices=12,
                 rotation=(math.pi / 2, 0, 0)),
    ])
    join("ShoulderCape", [
        sphere("Drape", (-0.23, 0.02, 0.03), (0.28, 0.21, 0.17), cloth_rust),
        cube("CapeTail", (-0.23, 0.10, -0.25), (0.34, 0.055, 0.52), cloth_rust, 0.025),
        cylinder("CapeClasp", (-0.13, -0.19, 0.04), 0.045, 0.018, brass, vertices=12,
                 rotation=(math.pi / 2, 0, 0)),
    ])
    join("BeltPouches", [
        torus("Belt", (0, 0, 0), 0.245, 0.024, dark_leather, major_segments=20),
        cube("PouchL", (-0.23, -0.10, -0.10), (0.16, 0.10, 0.19), leather, 0.025),
        cube("PouchR", (0.23, -0.08, -0.08), (0.14, 0.09, 0.16), leather, 0.025),
        cube("Buckle", (0, -0.25, 0), (0.08, 0.025, 0.07), brass, 0.008),
    ])
    join("Satchel", [
        cube("Bag", (0.29, -0.16, -0.35), (0.30, 0.14, 0.34), leather, 0.04),
        curve_tube("Strap", [(-0.20, -0.18, 0.24), (0.0, -0.21, -0.02),
                             (0.29, -0.18, -0.31)], 0.018, dark_leather),
        cube("SatchelClasp", (0.29, -0.24, -0.31), (0.07, 0.025, 0.06), brass, 0.008),
    ])
    join("CoinPouch", [
        sphere("CoinBag", (0.22, -0.12, -0.12), (0.11, 0.08, 0.14), leather),
        torus("Tie", (0.22, -0.12, -0.02), 0.045, 0.012, dark_leather, major_segments=12),
    ])
    join("Keys", [
        torus("KeyRing", (0.20, -0.12, -0.06), 0.055, 0.012, iron, rotation=(math.pi / 2, 0, 0)),
        cube("KeyA", (0.18, -0.12, -0.17), (0.025, 0.018, 0.17), iron, 0.004),
        cube("KeyB", (0.24, -0.12, -0.15), (0.025, 0.018, 0.14), brass, 0.004),
    ])
    join("Ledger", [
        cube("Book", (0.22, -0.13, -0.12), (0.20, 0.08, 0.28), dark_leather, 0.018),
        cube("Pages", (0.22, -0.18, -0.12), (0.16, 0.025, 0.23), parchment, 0.008),
        curve_tube("LedgerStrap", [(0.13, -0.10, 0.04), (0.03, -0.07, 0.10)], 0.012, leather),
    ])
    join("Mug", [
        cylinder("Cup", (-0.22, -0.10, -0.14), 0.07, 0.15, wood, vertices=12),
        torus("Handle", (-0.30, -0.10, -0.14), 0.06, 0.014, wood,
              rotation=(math.pi / 2, 0, 0), major_segments=12),
    ])
    join("RopeCoil", [
        torus(f"Rope{i}", (-0.22, -0.10, -0.10 + i * 0.018), 0.10, 0.016, cloth_ochre,
              rotation=(math.pi / 2, 0, 0), major_segments=14) for i in range(4)
    ])
    join("ScrollCase", [
        cylinder("Case", (-0.22, 0.10, -0.11), 0.045, 0.36, leather, vertices=12,
                 rotation=(0.18, 0.10, 0.18)),
        torus("CaseBand", (-0.22, 0.10, 0.05), 0.047, 0.012, brass,
              rotation=(0.18, 0.10, 0.18), major_segments=12),
    ])
    join("Knife", [
        cube("KnifeBlade", (0.22, -0.08, -0.17), (0.045, 0.025, 0.28), iron, 0.008),
        cube("KnifeGrip", (0.22, -0.08, 0.02), (0.065, 0.04, 0.14), dark_leather, 0.01),
        cube("KnifeGuard", (0.22, -0.08, -0.04), (0.12, 0.035, 0.025), brass, 0.006),
    ])
    join("Hammer", [
        cylinder("HammerHandle", (0.22, -0.08, -0.12), 0.022, 0.40, wood, vertices=10),
        cube("HammerHead", (0.22, -0.08, 0.09), (0.22, 0.09, 0.09), iron, 0.012),
    ])
    join("Quiver", [
        cylinder("QuiverBody", (0.23, 0.18, -0.06), 0.075, 0.54, leather, vertices=12,
                 rotation=(0.18, 0.08, -0.10)),
        *[cylinder(f"Arrow{i}", (0.19 + i * 0.035, 0.18, 0.23 + (i % 2) * 0.04),
                   0.009, 0.45, wood, vertices=8, rotation=(0.18, 0.08, -0.10)) for i in range(4)],
    ])
    join("Pauldron", [
        sphere("ShoulderShell", (0.28, 0, 0.04), (0.18, 0.20, 0.13), iron),
        cube("ShoulderRim", (0.29, -0.01, -0.02), (0.28, 0.25, 0.045), brass, 0.015),
    ])

    for obj in bpy.context.scene.objects:
        obj.select_set(obj.type == "MESH")
    output = root / "assets" / "models" / "equipment" / "npc_kit_embervale.glb"
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=str(output), export_format="GLB", use_selection=True,
                              export_yup=True, export_materials="EXPORT", export_animations=False,
                              export_apply=True)
    print(f"exported {len([o for o in bpy.context.scene.objects if o.type == 'MESH'])} modular pieces -> {output}")


def main() -> None:
    root = root_path()
    patch_humans(root)
    build_kit(root)


if __name__ == "__main__":
    main()
