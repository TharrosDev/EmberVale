#!/usr/bin/env python3
"""Build Embervale enemy identity meshes and repair creature material response.

Run with Blender so all geometry is reproducible:
  blender --background --factory-startup --python tools/build_enemy_identity_assets.py -- C:/path/to/Embervale

The source rigs that remain useful are never round-tripped. Their glTF JSON material factors are
patched byte-safely, while the modular kit and the seven inappropriate blob/construct replacements
are authored as new Blender geometry. The replacements ship idle, locomotion, attack, hit and death
clips so the shared gameplay animation resolver remains authoritative.
"""

from __future__ import annotations

import json
import math
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Vector


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
    document, binary = None, b""
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


def response(name: str) -> tuple[float, float]:
    key = name.lower()
    if any(word in key for word in ("steel", "iron", "metal", "blade", "armor", "armour", "chain", "gold")):
        return (0.82, 0.44)
    if any(word in key for word in ("stone", "rock", "ice", "crystal", "horn", "hoof", "bone", "tooth", "teeth")):
        return (0.0, 0.82)
    if any(word in key for word in ("skin", "fur", "hair", "cloth", "robe", "wood", "leather", "main", "muzzle")):
        return (0.0, 0.76)
    if any(word in key for word in ("eye", "pupil", "ember", "core", "magic")):
        return (0.0, 0.38)
    return (0.0, 0.78)


def patch_creature_materials(root: Path) -> None:
    folder = root / "assets" / "models" / "creatures"
    for path in sorted(folder.glob("*.glb")):
        if not (path.name.startswith("enm_") or path.name.startswith("boss_")):
            continue
        # These five are rebuilt below and need no source-payload preservation check.
        if path.name in {"enm_cinder_wisp.glb", "enm_storm_mote.glb", "enm_rime_shard.glb",
                          "enm_ruin_crawler.glb", "enm_ward_golem.glb", "enm_stone_sentinel.glb",
                          "enm_ash_maw.glb"}:
            continue
        document, binary = read_glb(path)
        structural = json.dumps({key: document.get(key) for key in
            ("accessors", "animations", "bufferViews", "meshes", "nodes", "skins")}, sort_keys=True)
        changed = 0
        for entry in document.get("materials", []):
            pbr = entry.setdefault("pbrMetallicRoughness", {})
            metallic, roughness = response(entry.get("name", ""))
            if pbr.get("metallicFactor") != metallic or pbr.get("roughnessFactor") != roughness:
                pbr["metallicFactor"], pbr["roughnessFactor"] = metallic, roughness
                changed += 1
        write_glb(path, document, binary)
        verify, verify_binary = read_glb(path)
        verified = json.dumps({key: verify.get(key) for key in
            ("accessors", "animations", "bufferViews", "meshes", "nodes", "skins")}, sort_keys=True)
        if verify_binary != binary or verified != structural:
            raise RuntimeError(f"material patch altered rig/geometry payload: {path.name}")
        print(f"material patch: {path.name}: {changed} material(s), rig payload preserved")


def reset_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for blocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.armatures,
                   bpy.data.actions, bpy.data.cameras, bpy.data.lights):
        for item in list(blocks):
            blocks.remove(item)


def material(name, color, metallic=0.0, roughness=0.82, emission=None, alpha=1.0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color[:3], alpha)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color[:3], alpha)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    if emission:
        if shader.inputs.get("Emission Color"):
            shader.inputs["Emission Color"].default_value = (*emission[:3], 1.0)
        if shader.inputs.get("Emission Strength"):
            shader.inputs["Emission Strength"].default_value = emission[3]
    if alpha < 1.0:
        shader.inputs["Alpha"].default_value = alpha
        mat.surface_render_method = "DITHERED"
    return mat


def finish(obj, mat, name=None, bevel=0.0):
    if name:
        obj.name = name
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel and obj.type == "MESH":
        mod = obj.modifiers.new("WornEdges", "BEVEL")
        mod.width, mod.segments = bevel, 2
        bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.data.materials.append(mat)
    return obj


def cube(name, location, scale, mat, rotation=(0, 0, 0), bevel=0.02):
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.object
    obj.scale = scale
    return finish(obj, mat, name, bevel)


def sphere(name, location, scale, mat, segments=12, rings=8):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=location)
    obj = bpy.context.object
    obj.scale = scale
    return finish(obj, mat, name)


def ico(name, location, scale, mat, subdivision=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivision, location=location)
    obj = bpy.context.object
    obj.scale = scale
    return finish(obj, mat, name)


def cylinder(name, location, radius, depth, mat, rotation=(0, 0, 0), vertices=10):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth,
                                       location=location, rotation=rotation)
    return finish(bpy.context.object, mat, name, 0.01)


def cone(name, location, radius1, radius2, depth, mat, rotation=(0, 0, 0), vertices=10):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius1, radius2=radius2, depth=depth,
                                   location=location, rotation=rotation)
    return finish(bpy.context.object, mat, name)


def torus(name, location, major, minor, mat, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=16,
                                    minor_segments=6, location=location, rotation=rotation)
    return finish(bpy.context.object, mat, name)


def tube(name, points, radius, mat):
    curve = bpy.data.curves.new(name, "CURVE")
    curve.dimensions, curve.resolution_u, curve.bevel_depth, curve.bevel_resolution = "3D", 1, radius, 1
    spline = curve.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for target, point in zip(spline.points, points):
        target.co = (*point, 1.0)
    obj = bpy.data.objects.new(name, curve)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.convert(target="MESH")
    return bpy.context.object


def join_piece(name, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    return result


def spike_fan(prefix, positions, mat, radius=0.06, depth=0.32):
    return [cone(f"{prefix}_{i}", pos, radius, 0.008, depth, mat) for i, pos in enumerate(positions)]


def build_kit(root: Path) -> None:
    reset_scene()
    iron = material("ColdIron", (0.10, 0.12, 0.14), 0.84, 0.46)
    rust = material("BurialIron", (0.20, 0.12, 0.07), 0.62, 0.68)
    leather = material("WornLeather", (0.12, 0.055, 0.025), 0.0, 0.82)
    cloth = material("FadedCloth", (0.10, 0.085, 0.07), 0.0, 0.94)
    clan = material("ClanHide", (0.22, 0.18, 0.12), 0.0, 0.95)
    bone = material("OldBone", (0.50, 0.44, 0.32), 0.0, 0.88)
    spectral = material("ShadeGlass", (0.15, 0.23, 0.31), 0.0, 0.36, (0.10, 0.28, 0.42, 0.7), 0.78)
    ember = material("EmberRune", (0.36, 0.08, 0.025), 0.0, 0.42, (0.95, 0.16, 0.025, 1.8))
    frost = material("RimeCrystal", (0.32, 0.55, 0.68), 0.0, 0.26, (0.10, 0.30, 0.48, 0.65))
    ash = material("AshStone", (0.095, 0.075, 0.065), 0.0, 0.92)
    dragon = material("DragonHorn", (0.20, 0.16, 0.11), 0.0, 0.82)
    moss = material("OldMoss", (0.15, 0.20, 0.095), 0.0, 0.98)

    # Thornback: the retained cattle rig gains a low snout, paired tusks and a thorned back ridge.
    join_piece("BoarHead", [sphere("snout", (0, -0.24, -0.03), (0.22, 0.30, 0.16), leather),
        cone("tusk_l", (-0.17, -0.38, -0.07), 0.045, 0.008, 0.30, bone, (math.pi/2, 0, -0.20)),
        cone("tusk_r", (0.17, -0.38, -0.07), 0.045, 0.008, 0.30, bone, (math.pi/2, 0, 0.20)),
        cube("brow", (0, -0.08, 0.12), (0.27, 0.09, 0.07), ash, bevel=0.035)])
    join_piece("BoarThornback", spike_fan("thorn", [(-0.14, y, 0.20 + 0.05*i) for i, y in enumerate((-0.42,-0.18,0.06,0.30))],
        dragon, 0.07, 0.38) + spike_fan("thorn_r", [(0.14, y, 0.20 + 0.05*i) for i, y in enumerate((-0.42,-0.18,0.06,0.30))], dragon, 0.07, 0.38))

    # Human factions and undead: model-specific silhouettes, not palette swaps.
    join_piece("WightBurialArmor", [cube("breast", (0,-0.03,-0.05), (0.30,0.10,0.34), rust, bevel=0.05),
        cube("shoulder_l", (-0.34,0,0.13), (0.16,0.16,0.10), rust, rotation=(0,0,0.22), bevel=0.04),
        cube("shoulder_r", (0.34,0,0.13), (0.16,0.16,0.10), rust, rotation=(0,0,-0.22), bevel=0.04),
        cube("gravecloth", (0,0.07,-0.46), (0.24,0.04,0.28), cloth, rotation=(0.08,0,0), bevel=0.01)])
    join_piece("WightCrown", spike_fan("crown", [(-0.18,0,0.13),(0,0,0.20),(0.18,0,0.13)], rust, 0.035, 0.26))
    join_piece("ShadeVeil", [torus("broken_halo", (0,0,0.08), 0.42, 0.025, spectral, (math.pi/2,0,0)),
        *[cone(f"veil_{i}", (x,0,-0.30), 0.10, 0.015, 0.62, spectral) for i,x in enumerate((-0.28,0,0.28))]])
    join_piece("ShadeHalo", [torus("halo", (0,0,0.04), 0.26, 0.022, spectral, (math.pi/2,0,0)),
        *spike_fan("shade", [(-0.16,0,0.18),(0.16,0,0.18)], spectral, 0.04, 0.30)])
    join_piece("ShamanFurs", [cube("hide", (0,0,-0.08), (0.34,0.12,0.38), clan, bevel=0.06),
        cylinder("collar", (0,0,0.22), 0.34, 0.12, clan, (math.pi/2,0,0), 12)])
    join_piece("ShamanMask", [cube("mask", (0,-0.12,0), (0.18,0.08,0.24), bone, bevel=0.04),
        *spike_fan("antler", [(-0.16,0,0.20),(0.16,0,0.20)], bone, 0.04, 0.36)])
    join_piece("ShamanTotem", [tube("staff", [(0,-0.55,-0.65),(0,-0.55,0.65)], 0.035, leather),
        torus("totem", (0,-0.55,0.48), 0.18, 0.035, bone), cone("fang", (0,-0.55,0.16),0.07,0.01,0.26,bone)])
    join_piece("NecroRibs", [*[tube(f"rib_{i}", [(-0.25,0,z),(0, -0.08,z-0.05),(0.25,0,z)], 0.025, bone) for i,z in enumerate((0.18,0.03,-0.12))],
        cube("sash", (0,0.08,-0.35), (0.20,0.04,0.32), cloth, rotation=(0,0,0.16), bevel=0.01)])
    join_piece("NecroCowl", [torus("cowl", (0,0,-0.02), 0.28, 0.07, cloth),
        cone("occult_horn", (0.20,0,0.18),0.045,0.01,0.30,bone,rotation=(0,0,-0.35))])
    join_piece("NecroFocus", [sphere("focus", (0,0,0), (0.15,0.15,0.15), spectral),
        torus("focus_ring", (0,0,0),0.22,0.025,bone,(math.pi/2,0,0))])

    join_piece("SoldierHarness", [cube("surcoat", (0,0,-0.08),(0.29,0.09,0.40),cloth,bevel=0.02),
        cube("brigandine", (0,-0.04,0.02),(0.31,0.08,0.29),iron,bevel=0.035),
        cylinder("belt", (0,0,-0.30),0.31,0.08,leather,(math.pi/2,0,0),12)])
    join_piece("SoldierKettle", [cylinder("helm", (0,0,0.04),0.25,0.24,iron,vertices=12),
        cylinder("brim", (0,0,-0.05),0.34,0.05,iron,vertices=14)])
    join_piece("BanditMantle", [cube("mantle", (0,0,-0.02),(0.33,0.12,0.32),leather,rotation=(0,0,0.06),bevel=0.05),
        tube("rope", [(-0.31,-0.12,0.18),(0,-0.18,-0.08),(0.31,-0.12,0.18)],0.025,clan)])
    join_piece("BanditMask", [cube("mask", (0,-0.16,-0.04),(0.22,0.05,0.11),cloth,bevel=0.025)])
    join_piece("EnforcerArmor", [cube("coat", (0,0,-0.05),(0.31,0.10,0.40),cloth,bevel=0.03),
        cube("pauldron_l", (-0.37,0,0.16),(0.18,0.18,0.11),iron,rotation=(0,0,0.30),bevel=0.05),
        cube("pauldron_r", (0.37,0,0.16),(0.18,0.18,0.11),iron,rotation=(0,0,-0.30),bevel=0.05),
        cylinder("seal", (0,-0.13,-0.18),0.08,0.035,rust,(math.pi/2,0,0),12)])
    join_piece("EnforcerMask", [cube("visor", (0,-0.18,0.02),(0.24,0.06,0.12),iron,bevel=0.025),
        cube("crest", (0,0,0.20),(0.04,0.13,0.20),rust,bevel=0.015)])

    # Animals.
    join_piece("DireWolfMane", [*[cone(f"mane_{i}", (x,0.0,z),0.08,0.015,0.34,ash,rotation=(0,0,ang))
        for i,(x,z,ang) in enumerate(((-0.28,0.12,-0.75),(-0.14,0.30,-0.35),(0,0.38,0),(0.14,0.30,0.35),(0.28,0.12,0.75)))],
        cube("shoulders", (0,0,-0.03),(0.38,0.22,0.28),ash,bevel=0.10)])
    join_piece("DireWolfFangs", [cone("fang_l",(-0.10,-0.19,-0.08),0.035,0.005,0.20,bone,(math.pi/2,0,0)),
        cone("fang_r",(0.10,-0.19,-0.08),0.035,0.005,0.20,bone,(math.pi/2,0,0)),cube("brow",(0,-0.05,0.10),(0.22,0.09,0.07),ash,bevel=0.04)])
    join_piece("FrostStalkerRidge", spike_fan("rime", [(0,y,0.12+0.06*i) for i,y in enumerate((-0.32,-0.10,0.12,0.34))],frost,0.055,0.34))
    join_piece("FrostStalkerMask", [cube("ice_brow",(0,-0.08,0.08),(0.20,0.08,0.06),frost,bevel=0.03),
        cone("chin",(0,-0.16,-0.12),0.06,0.01,0.22,frost,(math.pi/2,0,0))])
    join_piece("AshMawCarapace", [cube("basalt",(0,0,0),(0.42,0.30,0.30),ash,bevel=0.12),
        *spike_fan("maw", [(-0.24,-0.02,0.18),(0,0,0.28),(0.24,-0.02,0.18)],ember,0.06,0.34)])
    join_piece("AshMawJaws", [torus("jaw_ring",(0,-0.18,-0.03),0.22,0.045,rust,(math.pi/2,0,0)),
        *spike_fan("teeth", [(x,-0.24,-0.06) for x in (-0.15,-0.05,0.05,0.15)],bone,0.025,0.15)])

    # Dragons: one reliable foundation, four unmistakable age/element silhouettes.
    join_piece("WildDragonCrown", [*spike_fan("antler_l", [(-0.28,0,0.05),(-0.36,0,0.25)],dragon,0.07,0.50),
        *spike_fan("antler_r", [(0.28,0,0.05),(0.36,0,0.25)],dragon,0.07,0.50)])
    join_piece("WildDragonDorsal", spike_fan("wild_spine", [(0,y,0.26+0.05*i) for i,y in enumerate((-0.55,-0.25,0.05,0.35,0.60))],dragon,0.10,0.60))
    join_piece("AshDragonCrown", [*spike_fan("ash_horn", [(-0.34,0,0.10),(0,0,0.35),(0.34,0,0.10)],ember,0.10,0.62),
        torus("iron_halo",(0,0,0.08),0.45,0.05,iron,(math.pi/2,0,0))])
    join_piece("AshDragonChains", [tube("chain_l",[(-0.65,0,0.25),(-0.25,-0.10,-0.25),(0,-0.08,-0.42)],0.045,iron),
        tube("chain_r",[(0.65,0,0.25),(0.25,-0.10,-0.25),(0,-0.08,-0.42)],0.045,iron)])
    join_piece("FrostDragonCrest", [*spike_fan("frost_horn", [(-0.30,0,0.10),(0,0,0.34),(0.30,0,0.10)],frost,0.09,0.55),
        cube("ice_brow",(0,-0.10,0.02),(0.32,0.10,0.10),frost,bevel=0.04)])
    join_piece("FrostDragonDorsal", spike_fan("frost_spine", [(0,y,0.25+0.06*i) for i,y in enumerate((-0.5,-0.2,0.1,0.4))],frost,0.10,0.60))
    join_piece("AncientDragonCrown", [torus("elder_halo",(0,0,0.08),0.48,0.04,rust,(math.pi/2,0,0)),
        *spike_fan("elder_horn", [(-0.38,0,0.10),(0.38,0,0.10)],bone,0.11,0.72)])

    # Hero boss.
    join_piece("IronKingPlate", [cube("king_breast",(0,-0.02,-0.02),(0.37,0.12,0.43),iron,bevel=0.06),
        cube("king_should_l",(-0.46,0,0.20),(0.24,0.22,0.15),rust,rotation=(0,0,0.34),bevel=0.06),
        cube("king_should_r",(0.46,0,0.20),(0.24,0.22,0.15),rust,rotation=(0,0,-0.34),bevel=0.06),
        cylinder("heart",(0,-0.16,-0.02),0.11,0.04,ember,(math.pi/2,0,0),12)])
    join_piece("IronKingCrown", [cylinder("crown_band",(0,0,-0.02),0.27,0.10,rust,vertices=12),
        *spike_fan("crown_spike", [(-0.20,0,0.16),(0,0,0.25),(0.20,0,0.16)],iron,0.055,0.38)])
    join_piece("IronKingChains", [tube("king_chain_l",[(-0.42,0,0.15),(-0.22,0.12,-0.30),(0,-0.02,-0.50)],0.04,iron),
        tube("king_chain_r",[(0.42,0,0.15),(0.22,0.12,-0.30),(0,-0.02,-0.50)],0.04,iron),
        torus("broken_link",(0,0,-0.55),0.12,0.035,rust,(math.pi/2,0,0))])
    join_piece("IronKingBack", [cube("back_banner",(0,0.10,-0.08),(0.28,0.04,0.52),cloth,bevel=0.01),
        *spike_fan("back_blade", [(-0.24,0.08,0.28),(0.24,0.08,0.28)],rust,0.07,0.55)])
    join_piece("IronKingWeapon", [tube("king_haft",[(0,0,-0.62),(0,0,0.62)],0.055,leather),
        cube("king_axe",(0,0,0.58),(0.34,0.08,0.18),iron,rotation=(0,0,0.12),bevel=0.04),
        cone("king_axe_tip",(0.38,0,0.63),0.16,0.015,0.34,rust,rotation=(0,math.pi/2,0))])

    # Additional roster foundations that share the same grounded human family.
    join_piece("BoneKnightArmor", [cube("boneplate",(0,-0.04,-0.02),(0.32,0.10,0.40),rust,bevel=0.05),
        *[tube(f"bone_{i}",[(-0.26,0,z),(0.26,0,z)],0.025,bone) for i,z in enumerate((0.20,0.05,-0.10))]])
    join_piece("ClanRaiderArmor", [cube("raider_hide",(0,0,-0.04),(0.34,0.12,0.39),clan,bevel=0.06),
        cube("raider_iron",(0,-0.08,0.05),(0.28,0.06,0.22),iron,bevel=0.04)])
    join_piece("CultAshMark", [torus("ash_mark",(0,-0.15,0),0.18,0.025,ember,(math.pi/2,0,0)),
        cube("ash_sash",(0.05,0,-0.25),(0.18,0.04,0.34),cloth,rotation=(0,0,-0.18),bevel=0.01)])
    join_piece("ArcaneEchoRings", [torus("echo_a",(0,0,0),0.40,0.025,spectral,(math.pi/2,0,0)),
        torus("echo_b",(0,0,0),0.30,0.025,spectral,(0,math.pi/2,0))])

    output = root / "assets" / "models" / "equipment" / "enemy_identity_kit.glb"
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=str(output), export_format="GLB", use_selection=True,
        export_yup=True, export_materials="EXPORT", export_animations=False, export_apply=True)
    print(f"exported enemy identity kit: {output.name}")


def armature(name, bones):
    data = bpy.data.armatures.new(name + "Rig")
    arm = bpy.data.objects.new(name + "Rig", data)
    bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for bone_name, head, tail, parent in bones:
        bone = data.edit_bones.new(bone_name)
        bone.head, bone.tail = head, tail
        if parent:
            bone.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def weight(obj, arm, bone_name):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    group = obj.vertex_groups.new(name=bone_name)
    group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
    mod = obj.modifiers.new("EnemyRig", "ARMATURE")
    mod.object = arm
    obj.parent = arm


def key(action, arm, frame, values):
    arm.animation_data.action = action
    for bone_name, location, rotation in values:
        pose = arm.pose.bones[bone_name]
        pose.rotation_mode = "XYZ"
        pose.location = location
        pose.rotation_euler = rotation
        pose.keyframe_insert("location", frame=frame, group=bone_name)
        pose.keyframe_insert("rotation_euler", frame=frame, group=bone_name)


def animate(arm):
    arm.animation_data_create()
    clips = {
        "idle-loop": [(1, [("Torso",(0,0,0),(0,0,-0.03))]), (16,[("Torso",(0,0,0.035),(0,0,0.03))]), (31,[("Torso",(0,0,0),(0,0,-0.03))])],
        "run-loop": [(1,[("Torso",(0,0,0),(0.10,0,-0.08))]), (8,[("Torso",(0,0,0.05),(-0.10,0,0.08))]), (16,[("Torso",(0,0,0),(0.10,0,-0.08))])],
        "attack": [(1,[("Torso",(0,0,0),(0,0,0))]), (8,[("Torso",(0,-0.12,0.06),(0.30,0,0))]), (16,[("Torso",(0,0.26,0),( -0.24,0,0))]), (24,[("Torso",(0,0,0),(0,0,0))])],
        "hit": [(1,[("Torso",(0,0,0),(0,0,0))]), (5,[("Torso",(0,0.10,-0.06),(-0.25,0,0.16))]), (12,[("Torso",(0,0,0),(0,0,0))])],
        "death": [(1,[("Torso",(0,0,0),(0,0,0))]), (24,[("Torso",(0,0,-0.38),(math.pi/2,0,0.18))])],
    }
    for name, frames in clips.items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        for frame, values in frames:
            key(action, arm, frame, values)
    arm.animation_data.action = None


def build_replacement(root, filename, kind):
    reset_scene()
    stone = material("GroundedStone", (0.16,0.17,0.16), roughness=0.94)
    dark = material("CharredStone", (0.055,0.045,0.04), roughness=0.96)
    iron = material("ConstructIron", (0.10,0.12,0.13), metallic=0.78, roughness=0.52)
    ember = material("EmberCore", (0.42,0.07,0.015), roughness=0.34, emission=(1.0,0.12,0.01,2.3))
    storm = material("StormCore", (0.18,0.30,0.38), roughness=0.30, emission=(0.32,0.68,1.0,1.8))
    frost = material("RimeCore", (0.30,0.58,0.72), roughness=0.24, emission=(0.12,0.42,0.75,1.4))
    moss = material("ConstructMoss", (0.13,0.20,0.085), roughness=0.98)
    bones = [("Root",(0,0,0),(0,0,0.12),None),("Torso",(0,0,0.35),(0,0,0.85),"Root"),
             ("Head",(0,0,0.85),(0,0,1.10),"Torso")]
    if kind in {"ward", "sentinel"}:
        bones += [("Arm.L",(-0.28,0,0.72),(-0.60,0,0.45),"Torso"),("Arm.R",(0.28,0,0.72),(0.60,0,0.45),"Torso"),
                  ("Leg.L",(-0.18,0,0.40),(-0.22,0,0.05),"Root"),("Leg.R",(0.18,0,0.40),(0.22,0,0.05),"Root")]
    arm = armature("Enemy", bones)
    parts = []
    def add(obj, bone="Torso"):
        weight(obj, arm, bone); parts.append(obj)
    if kind == "cinder":
        add(ico("CoalHeart",(0,0,0.58),(0.22,0.20,0.25),ember))
        for i,(x,y,z,s) in enumerate(((-.27,0,.58,.16),(.27,.02,.58,.15),(0,-.24,.66,.14),(0,.25,.50,.13),(-.13,.10,.88,.10))):
            add(ico(f"Coal_{i}",(x,y,z),(s,s*.8,s*1.1),dark))
        for i,x in enumerate((-.18,0,.18)):
            add(cone(f"Ember_{i}",(x,-.03,.88),.06,.008,.32,ember))
    elif kind == "storm":
        add(ico("StormHeart",(0,0,.62),(.20,.20,.23),storm))
        for i,a in enumerate(range(0,360,60)):
            rad=math.radians(a); x,y=.34*math.cos(rad),.34*math.sin(rad)
            add(cone(f"Conductor_{i}",(x,y,.62),.09,.02,.38,iron,rotation=(0.6*math.sin(rad),0.6*math.cos(rad),rad)))
        add(torus("ArcA",(0,0,.62),.38,.025,storm,(math.pi/2,0,0)))
        add(torus("ArcB",(0,0,.62),.29,.020,storm,(0,math.pi/2,0)))
    elif kind == "rime":
        add(ico("FrozenHeart",(0,0,.52),(.24,.22,.22),stone))
        for i,(x,y,h) in enumerate(((-.24,0,.72),(.24,.02,.76),(0,-.18,1.02),(0,.20,.88),(-.12,.10,1.12),(.13,.08,1.04))):
            add(cone(f"Shard_{i}",(x,y,h*.64),.11,.015,h,frost))
    elif kind == "crawler":
        add(ico("RuinShell",(0,0,.38),(.48,.34,.25),stone))
        add(cube("RunePlate",(0,-.30,.40),(.28,.06,.15),iron,bevel=.04),"Head")
        for i,(side,y) in enumerate((( -1,-.22),(-1,0),(-1,.22),(1,-.22),(1,0),(1,.22))):
            add(cylinder(f"Leg_{i}",(side*.48,y,.20),.055,.58,iron,rotation=(0,math.pi/2,side*.45)))
        add(cone("MandibleL",(-.18,-.46,.34),.07,.015,.38,iron,(math.pi/2,0,-.25)),"Head")
        add(cone("MandibleR",(.18,-.46,.34),.07,.015,.38,iron,(math.pi/2,0,.25)),"Head")
    elif kind == "ashmaw":
        add(ico("MawShell",(0,0,.58),(.62,.46,.38),dark))
        add(ico("EmberThroat",(0,-.43,.56),(.25,.10,.22),ember),"Head")
        add(torus("JawRing",(0,-.48,.56),.34,.075,iron,(math.pi/2,0,0)),"Head")
        for i,x in enumerate((-.23,-.08,.08,.23)):
            add(cone(f"MawTooth_{i}",(x,-.58,.62),.055,.008,.28,stone,(math.pi/2,0,0)),"Head")
        for i,(x,y) in enumerate(((-.48,-.20),(.48,-.20),(-.48,.22),(.48,.22))):
            add(cone(f"BasaltLimb_{i}",(x,y,.28),.14,.07,.58,dark,rotation=(0,.65 if x<0 else -.65,0)))
        for i,(x,y,z) in enumerate(((-.34,.02,.92),(0,.12,1.05),(.34,.02,.92))):
            add(cone(f"BackVent_{i}",(x,y,z),.09,.02,.38,ember))
    else:
        scale = 1.0 if kind == "ward" else 1.18
        add(cube("Torso",(0,0,1.20),(.50*scale,.28,.52),stone,bevel=.10))
        add(ico("Core",(0,-.31,1.20),(.16,.06,.17),storm if kind == "ward" else ember))
        add(cube("Head",(0,0,1.88),(.30*scale,.25,.28),iron if kind == "ward" else stone,bevel=.07),"Head")
        add(cube("ArmL",(-.68*scale,0,1.18),(.20,.23,.52),stone,rotation=(0,.08,-.12),bevel=.08),"Arm.L")
        add(cube("ArmR",(.68*scale,0,1.18),(.20,.23,.52),stone,rotation=(0,-.08,.12),bevel=.08),"Arm.R")
        add(cube("LegL",(-.25*scale,0,.43),(.22,.25,.45),stone,bevel=.08),"Leg.L")
        add(cube("LegR",(.25*scale,0,.43),(.22,.25,.45),stone,bevel=.08),"Leg.R")
        add(cube("ShoulderL",(-.55*scale,0,1.60),(.28,.32,.20),iron,rotation=(0,0,.18),bevel=.07))
        add(cube("ShoulderR",(.55*scale,0,1.60),(.28,.32,.20),iron,rotation=(0,0,-.18),bevel=.07))
        if kind == "ward":
            add(torus("WardRune",(0,-.36,1.20),.25,.035,storm,(math.pi/2,0,0)))
        else:
            add(ico("MossL",(-.34,-.20,1.66),(.20,.10,.12),moss))
            add(ico("MossR",(.28,.18,.86),(.17,.11,.10),moss))
    animate(arm)
    output = root / "assets" / "models" / "creatures" / filename
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=str(output), export_format="GLB", use_selection=True,
        export_yup=True, export_materials="EXPORT", export_animations=True,
        export_animation_mode="ACTIONS", export_extra_animations=True, export_apply=False)
    print(f"exported grounded replacement: {filename}")


def main():
    root = root_path()
    patch_creature_materials(root)
    build_kit(root)
    for filename, kind in (("enm_cinder_wisp.glb","cinder"),("enm_storm_mote.glb","storm"),
                           ("enm_rime_shard.glb","rime"),("enm_ruin_crawler.glb","crawler"),
                           ("enm_ward_golem.glb","ward"),("enm_stone_sentinel.glb","sentinel"),
                           ("enm_ash_maw.glb","ashmaw")):
        build_replacement(root, filename, kind)
    print("Enemy identity assets complete.")


if __name__ == "__main__":
    main()
