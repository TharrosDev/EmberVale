#!/usr/bin/env python3
"""Rebuild Embervale's modular player-facing art assets with Blender.

Run with Blender, not CPython:
  blender --background --python tools/build_player_weapon_assets.py -- <repo-root>

The script deliberately does not round-trip the skinned player GLB.  The normalized
62-bone rig and its bone-parented children have been damaged by Blender round-trips in
the past.  Player material factors are patched in-place at the glTF JSON layer, while
new first-person arms and rigid equipment are exported as independent GLBs.
"""

from __future__ import annotations

import json
import math
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


ROOT = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
CHARACTERS = ROOT / "assets" / "models" / "characters"
WEAPONS = ROOT / "assets" / "models" / "weapons"
EQUIPMENT = ROOT / "assets" / "models" / "equipment"


def patch_glb_materials(path: Path, factors: dict[str, tuple[float, float]]) -> None:
    """Patch only glTF material factors; preserve geometry, skins and animation bytes."""
    raw = path.read_bytes()
    if raw[:4] != b"glTF":
        raise ValueError(f"Not a GLB: {path}")
    _, version, length = struct.unpack_from("<III", raw, 0)
    if version != 2 or length != len(raw):
        raise ValueError(f"Invalid GLB header: {path}")
    offset = 12
    chunks: list[tuple[int, bytes]] = []
    document = None
    while offset < length:
        size, kind = struct.unpack_from("<II", raw, offset)
        payload = raw[offset + 8: offset + 8 + size]
        if kind == 0x4E4F534A:
            document = json.loads(payload.rstrip(b" \x00").decode("utf-8"))
        else:
            chunks.append((kind, payload))
        offset += 8 + size
    if document is None:
        raise ValueError(f"No JSON chunk: {path}")
    found: set[str] = set()
    for material in document.get("materials", []):
        name = material.get("name", "")
        if name not in factors:
            continue
        metallic, roughness = factors[name]
        pbr = material.setdefault("pbrMetallicRoughness", {})
        pbr["metallicFactor"] = metallic
        pbr["roughnessFactor"] = roughness
        found.add(name)
    missing = set(factors) - found
    if missing:
        raise ValueError(f"Missing materials in {path.name}: {sorted(missing)}")
    encoded = json.dumps(document, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    encoded += b" " * ((4 - len(encoded) % 4) % 4)
    rebuilt = bytearray(struct.pack("<III", 0x46546C67, 2, 0))
    rebuilt += struct.pack("<II", len(encoded), 0x4E4F534A) + encoded
    for kind, payload in chunks:
        rebuilt += struct.pack("<II", len(payload), kind) + payload
    struct.pack_into("<I", rebuilt, 8, len(rebuilt))
    path.write_bytes(rebuilt)


def reset() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name: str, color: tuple[float, float, float, float], metallic: float, roughness: float):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = color
    mat.use_nodes = True
    node = mat.node_tree.nodes.get("Principled BSDF")
    node.inputs["Base Color"].default_value = color
    node.inputs["Metallic"].default_value = metallic
    node.inputs["Roughness"].default_value = roughness
    return mat


def shell(name: str, y0: float, y1: float, center: tuple[float, float],
          r0: tuple[float, float], r1: tuple[float, float], mat, sides: int = 10):
    """Create a capless elliptical tapered shell along local +Y."""
    cx, cz = center
    verts = []
    for y, radii in ((y0, r0), (y1, r1)):
        rx, rz = radii
        for i in range(sides):
            a = math.tau * i / sides
            verts.append((cx + math.cos(a) * rx, y, cz + math.sin(a) * rz))
    faces = []
    for i in range(sides):
        n = (i + 1) % sides
        faces.append((i, n, sides + n, sides + i))
    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.materials.append(mat)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def torus_band(name: str, y: float, center: tuple[float, float], radius: tuple[float, float],
               width: float, mat, sides: int = 10):
    cx, cz = center
    bpy.ops.mesh.primitive_torus_add(
        major_radius=1.0, minor_radius=width, major_segments=sides, minor_segments=4,
        location=(cx, y, cz), rotation=(math.pi / 2, 0, 0))
    obj = bpy.context.object
    obj.name = name
    obj.scale = (radius[0], radius[1], min(radius))
    obj.data.materials.append(mat)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return obj


def export_selected(path: Path, objects: list[bpy.types.Object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True,
        export_apply=True, export_yup=True, export_materials="EXPORT")


def equipment_to_godot_y(objects: list[bpy.types.Object]) -> None:
    """The exporter maps Blender (X,Y,Z) to Godot (X,Z,-Y); make authored +Y become Godot +Y."""
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
        obj.rotation_euler.x = math.pi / 2
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)


def add_arm_clothing(prefix: str) -> list[bpy.types.Object]:
    cloth = material(prefix + "_GambesonCloth", (0.11, 0.12, 0.075, 1), 0.0, 0.9)
    leather = material(prefix + "_WornLeather", (0.16, 0.095, 0.055, 1), 0.0, 0.66)
    iron = material(prefix + "_ColdIron", (0.23, 0.27, 0.31, 1), 0.82, 0.43)
    center = (0.022, 0.215)
    pieces = [
        shell(prefix + "_Sleeve", 0.295, 0.52, center, (0.098, 0.098), (0.12, 0.115), cloth),
        shell(prefix + "_Bracer", 0.18, 0.335, center, (0.091, 0.091), (0.104, 0.10), leather),
        torus_band(prefix + "_Cuff", 0.19, center, (0.096, 0.096), 0.012, iron),
        torus_band(prefix + "_BracerRim", 0.32, center, (0.108, 0.104), 0.010, iron),
    ]
    return pieces


def mirror_objects(objects: list[bpy.types.Object]) -> list[bpy.types.Object]:
    mirrored = []
    mirror = Matrix.Diagonal(Vector((-1.0, 1.0, 1.0, 1.0)))
    for source in objects:
        obj = source.copy()
        obj.data = source.data.copy()
        bpy.context.collection.objects.link(obj)
        obj.data.transform(mirror)
        obj.data.flip_normals()
        obj.name = source.name.replace("Right", "Left").replace("_R", "_L")
        mirrored.append(obj)
    return mirrored


def build_first_person_arms() -> None:
    reset()
    bpy.ops.import_scene.gltf(filepath=str(CHARACTERS / "fp_arm.glb"))
    source = next(obj for obj in bpy.context.scene.objects if obj.type == "MESH")
    source.name = "HeroArmRight"
    skin = material("Skin", (0.49, 0.335, 0.19, 1), 0.0, 0.72)
    source.data.materials.clear()
    source.data.materials.append(skin)
    right = [source] + add_arm_clothing("Right")
    export_selected(CHARACTERS / "fp_arm_right.glb", right)
    left = mirror_objects(right)
    export_selected(CHARACTERS / "fp_arm_left.glb", left)


def bevelled_cube(name: str, dimensions: tuple[float, float, float], location, mat, bevel=0.02):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    modifier = obj.modifiers.new("EdgeBevel", "BEVEL")
    modifier.width = bevel
    modifier.segments = 1
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.data.materials.append(mat)
    return obj


def build_equipment() -> None:
    reset()
    leather = material("WornLeather", (0.15, 0.085, 0.045, 1), 0.0, 0.68)
    iron = material("ColdIron", (0.24, 0.28, 0.32, 1), 0.84, 0.44)
    cloth = material("FadedGambeson", (0.10, 0.11, 0.065, 1), 0.0, 0.9)

    plate = bevelled_cube("PauldronPlate", (0.16, 0.15, 0.08), (0, 0.065, 0), leather, 0.025)
    ridge = bevelled_cube("PauldronRidge", (0.035, 0.17, 0.092), (0, 0.065, 0), iron, 0.01)
    plate.scale = ridge.scale = Vector((0.78, 0.78, 0.78))
    bpy.context.view_layer.objects.active = plate
    equipment_to_godot_y([plate, ridge])
    export_selected(EQUIPMENT / "eqp_pauldron_embervale.glb", [plate, ridge])

    reset()
    leather = material("WornLeather", (0.13, 0.065, 0.035, 1), 0.0, 0.7)
    iron = material("ColdIron", (0.22, 0.26, 0.30, 1), 0.86, 0.42)
    pouch = bevelled_cube("UtilityPouch", (0.20, 0.22, 0.09), (0, -0.11, 0), leather, 0.025)
    flap = bevelled_cube("PouchFlap", (0.205, 0.085, 0.102), (0, -0.07, -0.008), leather, 0.018)
    clasp = bevelled_cube("PouchClasp", (0.035, 0.055, 0.014), (0, -0.095, -0.062), iron, 0.005)
    equipment_to_godot_y([pouch, flap, clasp])
    export_selected(EQUIPMENT / "eqp_pouch_embervale.glb", [pouch, flap, clasp])

def main() -> None:
    patch_glb_materials(CHARACTERS / "chr_player_base.glb", {
        "Green": (0.0, 0.90), "LightGreen": (0.0, 0.88), "Skin": (0.0, 0.72),
        "Black": (0.0, 0.76), "Grey": (0.0, 0.70), "Eyebrows": (0.0, 0.78),
        "Eye": (0.0, 0.52), "Hair": (0.0, 0.78), "Brown2": (0.0, 0.68),
        "Brown": (0.0, 0.64), "Gold": (0.78, 0.46),
    })
    patch_glb_materials(WEAPONS / "wpn_sword_iron.glb", {
        "DarkSteel": (0.82, 0.46), "LightSteel": (0.90, 0.32), "Steel": (0.88, 0.38),
        "DarkWood": (0.0, 0.72), "LightWood": (0.0, 0.64),
    })
    build_first_person_arms()
    build_equipment()
    print("Built player viewmodel/equipment assets and patched player/sword material factors.")


if __name__ == "__main__":
    main()
