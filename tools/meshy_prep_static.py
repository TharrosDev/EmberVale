#!/usr/bin/env python3
"""Turn a Meshy static .glb into a production .glb: real size, base origin, plain material.

Meshy text-to-3D returns a mesh about two units tall centred on the origin, with the albedo wired
into emission as well and (pbr off) no roughness authored. Every one of those is a per-instance
correction somebody would otherwise repeat in a scene, so this bakes them into the file once:

    geometry   node transforms, a uniform scale to --height (or --length along the longest
               horizontal axis), an optional --yaw and the origin are written INTO the vertices.
               Normals and tangents are rotated with them. The node carries no transform.
    material   emission and the specular/ior extensions are dropped; an atlas with no
               metallicRoughness texture gets metallic 0 and --roughness. A material that has the
               map, or no texture at all, keeps its own factors.
    textures   re-encoded as PNG at --max-texture and named by role. Godot extracts an embedded
               image to <model>_<image name>.png, so the role IS the name: prp_x.glb gives
               prp_x_BaseColor.png, prp_x_Normal.png and prp_x_ORM.png, which is what
               audit_3d.texture_role reads.

    python tools/meshy_prep_static.py gen/prp_tree_oak_a.refine.glb out/prp_tree_oak_a.glb --height 12
    python tools/meshy_prep_static.py wpn_sword_iron.glb wpn_dagger_iron.glb --height 0.45 --origin keep
    python tools/meshy_prep_static.py enm_wolf.glb enm_dire_wolf.glb --tint 0.55,0.5,0.5
    python tools/meshy_prep_static.py enm_wolf.glb enm_frost_stalker.glb --lighten 0.35 --desaturate 0.6

A RIGGED source (the wolf copies above) is recoloured only: its geometry, nodes, materials and
image names are left exactly as they are, because a skinned mesh cannot be rescaled from here
without its skeleton. Pure Python plus Pillow; the written file is parsed back and checked.
"""
from __future__ import annotations

import argparse
import io
import math
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from meshy_adopt import read_glb, rebuild_buffer, write_glb

FLOAT = 5126
ORIGINS = ("base", "footprint", "keep")
ROLES = ("base", "normal", "orm")
IMAGE_NAMES = {"base": "BaseColor", "normal": "Normal", "orm": "ORM"}


class PrepError(ValueError):
    """The source is not something this tool can prepare; the message says why."""


# --------------------------------------------------------------------------- matrices (3x4 rows)

IDENTITY = [[1.0, 0.0, 0.0, 0.0], [0.0, 1.0, 0.0, 0.0], [0.0, 0.0, 1.0, 0.0]]


def multiply(a: list[list[float]], b: list[list[float]]) -> list[list[float]]:
    return [[sum(a[r][k] * b[k][c] for k in range(3)) + (a[r][3] if c == 3 else 0.0)
             for c in range(4)] for r in range(3)]


def node_matrix(node: dict) -> list[list[float]]:
    if "matrix" in node:
        m = node["matrix"]            # glTF stores it column-major
        return [[m[0 + r], m[4 + r], m[8 + r], m[12 + r]] for r in range(3)]
    x, y, z, w = node.get("rotation", [0.0, 0.0, 0.0, 1.0])
    sx, sy, sz = node.get("scale", [1.0, 1.0, 1.0])
    tx, ty, tz = node.get("translation", [0.0, 0.0, 0.0])
    return [
        [(1 - 2 * (y * y + z * z)) * sx, (2 * (x * y - z * w)) * sy, (2 * (x * z + y * w)) * sz, tx],
        [(2 * (x * y + z * w)) * sx, (1 - 2 * (x * x + z * z)) * sy, (2 * (y * z - x * w)) * sz, ty],
        [(2 * (x * z - y * w)) * sx, (2 * (y * z + x * w)) * sy, (1 - 2 * (x * x + y * y)) * sz, tz],
    ]


def determinant(m: list[list[float]]) -> float:
    return (m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1])
            - m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0])
            + m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0]))


def normal_matrix(m: list[list[float]]) -> list[list[float]]:
    """The cofactor matrix: the inverse transpose up to a positive factor, which normalising drops."""
    return [[m[(r + 1) % 3][(c + 1) % 3] * m[(r + 2) % 3][(c + 2) % 3]
             - m[(r + 1) % 3][(c + 2) % 3] * m[(r + 2) % 3][(c + 1) % 3] for c in range(3)]
            for r in range(3)]


def yaw_matrix(degrees: float) -> list[list[float]]:
    c, s = math.cos(math.radians(degrees)), math.sin(math.radians(degrees))
    return [[c, 0.0, s, 0.0], [0.0, 1.0, 0.0, 0.0], [-s, 0.0, c, 0.0]]


# --------------------------------------------------------------------------- accessors

def span(doc: dict, index: int, width: int) -> tuple[int, int, int]:
    """(first byte, stride, count) of a float accessor; anything this tool cannot rewrite raises."""
    accessor = doc["accessors"][index]
    if accessor.get("componentType") != FLOAT or "sparse" in accessor or "bufferView" not in accessor:
        raise PrepError(f"accessor {index} is not plain float data (quantised or sparse)")
    view = doc["bufferViews"][accessor["bufferView"]]
    start = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    return start, view.get("byteStride") or width * 4, accessor["count"]


def read_vectors(doc: dict, binary: bytes, index: int, width: int) -> list[tuple[float, ...]]:
    start, stride, count = span(doc, index, width)
    return [struct.unpack_from(f"<{width}f", binary, start + i * stride) for i in range(count)]


def write_vectors(doc: dict, binary: bytearray, index: int, values: list[tuple[float, ...]]) -> None:
    width = len(values[0])
    start, stride, _ = span(doc, index, width)
    for i, value in enumerate(values):
        struct.pack_into(f"<{width}f", binary, start + i * stride, *value)


def bounds(points: list[tuple[float, ...]]) -> tuple[list[float], list[float]]:
    return ([min(p[axis] for p in points) for axis in range(3)],
            [max(p[axis] for p in points) for axis in range(3)])


def triangle_count(doc: dict) -> int:
    total = 0
    for mesh in doc.get("meshes", []):
        for primitive in mesh["primitives"]:
            source = primitive.get("indices", primitive["attributes"]["POSITION"])
            total += doc["accessors"][source]["count"] // 3
    return total


# --------------------------------------------------------------------------- geometry

def mesh_nodes(doc: dict) -> list[tuple[int, list[list[float]]]]:
    """(mesh index, world matrix) for every node of the one scene that draws a mesh."""
    found: list[tuple[int, list[list[float]]]] = []

    def walk(index: int, parent: list[list[float]]) -> None:
        node = doc["nodes"][index]
        world = multiply(parent, node_matrix(node))
        if "mesh" in node:
            found.append((node["mesh"], world))
        for child in node.get("children", []):
            walk(child, world)

    for root in doc["scenes"][0].get("nodes", []):
        walk(root, IDENTITY)
    return found


def bake_geometry(doc: dict, binary: bytearray, asset_id: str, height: float | None,
                  length: float | None, yaw: float, origin: str,
                  offset: tuple[float, float, float], allow_multi_mesh: bool) -> None:
    placed = mesh_nodes(doc)
    if not placed:
        raise PrepError("the scene draws no mesh")
    if len(placed) > 1 and not allow_multi_mesh:
        raise PrepError(f"{len(placed)} mesh nodes; a Meshy static export has one "
                        f"(pass --allow-multi-mesh if the extra ones are wanted)")
    if len({mesh for mesh, _ in placed}) != len(placed):
        raise PrepError("a mesh is instanced by more than one node; it cannot be baked once")

    # accessor -> the matrix its mesh is drawn with. Two meshes may share an accessor only when
    # they also share a transform, otherwise one of them would be baked wrong.
    owners: dict[str, dict[int, list[list[float]]]] = {"POSITION": {}, "NORMAL": {}, "TANGENT": {}}
    for mesh, world in placed:
        if determinant(world) <= 0:
            raise PrepError("a node transform mirrors or flattens the mesh")
        for primitive in doc["meshes"][mesh]["primitives"]:
            if primitive.get("mode", 4) != 4 or "targets" in primitive:
                raise PrepError("only plain triangle primitives without morph targets are supported")
            if "POSITION" not in primitive["attributes"]:
                raise PrepError("a primitive has no POSITION")
            for semantic, table in owners.items():
                index = primitive["attributes"].get(semantic)
                if index is None:
                    continue
                if table.setdefault(index, world) != world:
                    raise PrepError("one accessor is drawn with two different transforms")

    turn = yaw_matrix(yaw)
    positions = {index: [tuple(row[0] * p[0] + row[1] * p[1] + row[2] * p[2] + row[3] for row in m)
                         for p in read_vectors(doc, binary, index, 3)]
                 for index, world in owners["POSITION"].items() for m in [multiply(turn, world)]}
    everything = [p for points in positions.values() for p in points]
    low, high = bounds(everything)
    size = [high[axis] - low[axis] for axis in range(3)]
    if min(size[0], size[2]) <= 0 or size[1] <= 0:
        raise PrepError(f"degenerate bounds {size}")

    scale = 1.0
    if height is not None:
        scale = height / size[1]
    elif length is not None:
        scale = length / max(size[0], size[2])
    if origin == "keep":
        pivot = [0.0, 0.0, 0.0]
    else:
        centre = everything
        if origin == "footprint":
            # Where it meets the ground, not the middle of its box: a lopsided canopy otherwise
            # puts the trunk a metre off the point a collider or a scatter transform is given.
            centre = [p for p in everything if p[1] <= low[1] + 0.05 * size[1]]
        c_low, c_high = bounds(centre)
        pivot = [(c_low[0] + c_high[0]) / 2, low[1], (c_low[2] + c_high[2]) / 2]

    for index, points in positions.items():
        moved = [tuple((p[axis] - pivot[axis]) * scale + offset[axis] for axis in range(3))
                 for p in points]
        write_vectors(doc, binary, index, moved)
        # Bounds come from the float32 values just stored, so a strict validator agrees with them.
        stored = read_vectors(doc, binary, index, 3)
        doc["accessors"][index]["min"], doc["accessors"][index]["max"] = bounds(stored)

    for semantic in ("NORMAL", "TANGENT"):
        for index, world in owners[semantic].items():
            width = 3 if semantic == "NORMAL" else 4
            m = multiply(turn, world)
            rotate = normal_matrix(m) if semantic == "NORMAL" else m
            turned = []
            for value in read_vectors(doc, binary, index, width):
                v = [row[0] * value[0] + row[1] * value[1] + row[2] * value[2] for row in rotate[:3]]
                norm = math.sqrt(sum(c * c for c in v))
                # A zero-length source normal stays zero; the importer regenerates what it needs.
                v = [c / norm for c in v] if norm > 1e-12 else list(value[:3])
                turned.append(tuple(v) + tuple(value[3:]))
            write_vectors(doc, binary, index, turned)

    meshes = [mesh for mesh, _ in placed]
    doc["nodes"] = [{"name": asset_id if len(meshes) == 1 else f"{asset_id}_{order}", "mesh": mesh}
                    for order, mesh in enumerate(meshes)]
    doc["scenes"] = [{"name": asset_id, "nodes": list(range(len(meshes)))}]
    doc["scene"] = 0
    for mesh in meshes:
        doc["meshes"][mesh]["name"] = asset_id
    for key in ("cameras", "extensions"):
        doc.pop(key, None)


# --------------------------------------------------------------------------- materials, textures

def texture_slots(material: dict) -> list[tuple[str, dict]]:
    """(role, texture reference) pairs of one material, strongest role first."""
    pbr = material.get("pbrMetallicRoughness", {})
    slots = [("base", pbr.get("baseColorTexture")), ("normal", material.get("normalTexture")),
             ("orm", pbr.get("metallicRoughnessTexture")), ("orm", material.get("occlusionTexture"))]
    return [(role, reference) for role, reference in slots if reference]


def plain_materials(doc: dict, asset_id: str, roughness: float) -> None:
    materials = doc.get("materials", [])
    for order, material in enumerate(materials):
        material["name"] = f"M_{asset_id}" if len(materials) == 1 else f"M_{asset_id}_{order}"
        # Meshy wires the albedo into emission at full strength, which is why an unprepared prop
        # glows at night; the specular/ior extensions come along from its Blender export.
        for key in ("emissiveTexture", "emissiveFactor", "extensions"):
            material.pop(key, None)
        pbr = material.setdefault("pbrMetallicRoughness", {})
        # Only an atlas with no roughness map gets the default. A material with no texture at all
        # was authored by hand (the kit weapons), and its factors are the whole material.
        if "baseColorTexture" in pbr and "metallicRoughnessTexture" not in pbr:
            pbr["metallicFactor"], pbr["roughnessFactor"] = 0.0, roughness
    doc.pop("extensionsUsed", None)


def prune_images(doc: dict) -> None:
    """Drop textures no material uses, images no texture uses and their bufferViews.

    Dropping emission orphans a texture entry, and an orphaned image is still extracted to a .png
    by the importer. Every index that pointed past a dropped entry is renumbered here.
    """
    used = sorted({reference["index"] for material in doc.get("materials", [])
                   for _, reference in texture_slots(material)})
    remap = {old: new for new, old in enumerate(used)}
    for material in doc.get("materials", []):
        for _, reference in texture_slots(material):
            reference["index"] = remap[reference["index"]]
    doc["textures"] = [doc.get("textures", [])[old] for old in used]

    used = sorted({texture["source"] for texture in doc["textures"]})
    remap = {old: new for new, old in enumerate(used)}
    for texture in doc["textures"]:
        texture["source"] = remap[texture["source"]]
    doc["images"] = [doc.get("images", [])[old] for old in used]

    views = sorted({holder["bufferView"] for holder in doc.get("accessors", []) + doc["images"]
                    if "bufferView" in holder})
    remap = {old: new for new, old in enumerate(views)}
    for holder in doc.get("accessors", []) + doc["images"]:
        if "bufferView" in holder:
            holder["bufferView"] = remap[holder["bufferView"]]
    doc["bufferViews"] = [doc["bufferViews"][old] for old in views]
    for key in ("textures", "images"):
        if not doc[key]:
            del doc[key]
    if "textures" not in doc:
        doc.pop("samplers", None)


def image_roles(doc: dict, emission_is_colour: bool) -> dict[int, str]:
    """image index -> base / normal / orm, from how the materials use it."""
    roles: dict[int, str] = {}
    for material in doc.get("materials", []):
        slots = texture_slots(material)
        if emission_is_colour and material.get("emissiveTexture"):
            slots.append(("base", material["emissiveTexture"]))
        for role, reference in slots:
            roles.setdefault(doc["textures"][reference["index"]]["source"], role)
    return roles


def recolour(image, lighten: float, desaturate: float):
    """Blend toward the pixel's own grey, then toward white. A colour factor can only darken."""
    from PIL import Image

    alpha = image.getchannel("A") if "A" in image.getbands() else None
    rgb = image.convert("RGB")
    if desaturate:
        rgb = Image.blend(rgb, rgb.convert("L").convert("RGB"), desaturate)
    if lighten:
        rgb = Image.blend(rgb, Image.new("RGB", rgb.size, (255, 255, 255)), lighten)
    if alpha is not None:
        rgb.putalpha(alpha)
    return rgb


def encode_images(doc: dict, binary: bytes, roles: dict[int, str], caps: dict[str, int],
                  lighten: float, desaturate: float, rename: bool) -> tuple[dict[int, bytes], list[str]]:
    """Every embedded image as a PNG within its role's cap: ({bufferView: bytes}, report lines)."""
    from PIL import Image

    replacements: dict[int, bytes] = {}
    lines: list[str] = []
    taken: set[str] = set()
    for index, image in enumerate(doc.get("images", [])):
        if "bufferView" not in image:
            raise PrepError(f"image {index} is an external file; only embedded images are prepared")
        role = roles.get(index, "base")
        view = doc["bufferViews"][image["bufferView"]]
        start = view.get("byteOffset", 0)
        with Image.open(io.BytesIO(binary[start:start + view["byteLength"]])) as source:
            before, was = source.size, source.format
            picture = source.convert("RGBA" if "A" in source.getbands() else "RGB")
        # An alpha channel that is 255 everywhere only doubles the file; the importer ignores it.
        if picture.mode == "RGBA" and picture.getchannel("A").getextrema()[0] == 255:
            picture = picture.convert("RGB")
        cap = caps[role]
        if max(picture.size) > cap:
            ratio = cap / max(picture.size)
            picture = picture.resize((max(1, round(picture.width * ratio)),
                                      max(1, round(picture.height * ratio))), Image.LANCZOS)
        if role == "base" and (lighten or desaturate):
            picture = recolour(picture, lighten, desaturate)
        packed = io.BytesIO()
        picture.save(packed, format="PNG", optimize=True)
        replacements[image["bufferView"]] = packed.getvalue()
        image["mimeType"] = "image/png"
        if rename:
            name = IMAGE_NAMES[role]
            # A second image of one role keeps the role as its SUFFIX, which is what the audit reads.
            image["name"] = name if name not in taken else f"{index}_{name}"
            taken.add(image["name"])
        lines.append(f"{image.get('name', index)}: {was} {before[0]}x{before[1]} -> PNG "
                     f"{picture.width}x{picture.height} ({len(packed.getvalue()) // 1024} KB)")
    return replacements, lines


# --------------------------------------------------------------------------- the whole job

def prepare(source: Path, dest: Path, *, asset_id: str | None = None, height: float | None = None,
            length: float | None = None, yaw: float = 0.0, origin: str = "base",
            offset: tuple[float, float, float] = (0.0, 0.0, 0.0),
            max_texture: int | dict[str, int] = 1024, roughness: float = 0.8,
            tint: tuple[float, float, float] | None = None, lighten: float = 0.0,
            desaturate: float = 0.0, allow_multi_mesh: bool = False) -> dict:
    """Write `dest` from `source` and return what was measured in the written file."""
    asset_id = asset_id or dest.stem
    caps = max_texture if isinstance(max_texture, dict) else dict.fromkeys(ROLES, max_texture)
    doc, raw = read_glb(source)
    binary = bytearray(raw)
    if len(doc.get("scenes", [])) != 1:
        raise PrepError(f"{len(doc.get('scenes', []))} scenes; expected exactly one")
    if doc.get("extensionsRequired"):
        raise PrepError(f"requires {doc['extensionsRequired']}; export plain glTF instead")
    if len(doc.get("buffers", [])) != 1 or "uri" in doc["buffers"][0]:
        raise PrepError("expected one embedded buffer")
    triangles = triangle_count(doc)

    rigged = bool(doc.get("skins") or doc.get("animations"))
    if rigged:
        if height is not None or length is not None or yaw or origin != "base" or any(offset):
            raise PrepError("the source is rigged: it can be recoloured, not rescaled or moved")
        roles = image_roles(doc, emission_is_colour=True)
    else:
        bake_geometry(doc, binary, asset_id, height, length, yaw, origin, offset, allow_multi_mesh)
        plain_materials(doc, asset_id, roughness)
        prune_images(doc)
        roles = image_roles(doc, emission_is_colour=False)
    if tint:
        for material in doc.get("materials", []):
            pbr = material.setdefault("pbrMetallicRoughness", {})
            factor = pbr.get("baseColorFactor", [1.0, 1.0, 1.0, 1.0])
            pbr["baseColorFactor"] = [factor[c] * tint[c] for c in range(3)] + [factor[3]]
            # A rigged Meshy body still emits its albedo, and that half ignores the colour factor.
            if "emissiveTexture" in material:
                glow = material.get("emissiveFactor", [0.0, 0.0, 0.0])
                material["emissiveFactor"] = [glow[c] * tint[c] for c in range(3)]

    replacements, texture_lines = encode_images(doc, bytes(binary), roles, caps, lighten, desaturate,
                                                rename=not rigged)
    packed = rebuild_buffer(doc, bytes(binary), replacements)
    doc.setdefault("asset", {})["generator"] = "Embervale meshy_prep_static"
    dest.parent.mkdir(parents=True, exist_ok=True)
    # Written beside the destination and moved over it only once the re-parse passes: a failed
    # prep must never leave a half-checked file, or delete the production model it was replacing.
    staged = dest.with_name(dest.name + ".tmp")
    write_glb(staged, doc, packed)
    try:
        report = measure(staged)
        report["textures"] = texture_lines
        problems = [] if rigged else verify(report, height, length, origin, offset, caps)
        if report["triangles"] != triangles:
            problems.append(f"triangle count changed: {triangles} -> {report['triangles']}")
        if problems:
            raise PrepError(f"{dest.name} failed its own re-parse: " + "; ".join(problems))
        staged.replace(dest)
    finally:
        staged.unlink(missing_ok=True)
    report["path"] = str(dest)
    return report


def measure(path: Path) -> dict:
    """Bounds, counts and texture sizes read back from a written file's own bytes."""
    from PIL import Image

    doc, binary = read_glb(path)
    points: list[tuple[float, ...]] = []
    declared_ok, normals_off = True, 0
    for index in sorted({p["attributes"]["POSITION"] for m in doc.get("meshes", []) for p in m["primitives"]}):
        own = read_vectors(doc, binary, index, 3)
        low, high = bounds(own)
        accessor = doc["accessors"][index]
        declared_ok &= accessor.get("min") == low and accessor.get("max") == high
        points += own
    for index in sorted({p["attributes"]["NORMAL"] for m in doc.get("meshes", []) for p in m["primitives"]
                         if "NORMAL" in p["attributes"]}):
        normals_off += sum(1 for n in read_vectors(doc, binary, index, 3)
                           if abs(math.sqrt(n[0] ** 2 + n[1] ** 2 + n[2] ** 2) - 1.0) > 1e-3)
    images = []
    for image in doc.get("images", []):
        view = doc["bufferViews"][image["bufferView"]]
        start = view.get("byteOffset", 0)
        with Image.open(io.BytesIO(binary[start:start + view["byteLength"]])) as picture:
            images.append({"name": image.get("name", ""), "format": picture.format, "size": picture.size})
    low, high = bounds(points)
    return {"path": str(path), "bytes": path.stat().st_size, "triangles": triangle_count(doc),
            "vertices": len(points), "min": low, "max": high,
            "size": [high[axis] - low[axis] for axis in range(3)],
            "declared_bounds_match": declared_ok, "non_unit_normals": normals_off,
            "has_normals": all("NORMAL" in p["attributes"] for m in doc["meshes"] for p in m["primitives"]),
            "has_uv": all("TEXCOORD_0" in p["attributes"] for m in doc["meshes"] for p in m["primitives"]),
            "images": images, "materials": doc.get("materials", []),
            "root_nodes": [doc["nodes"][i] for i in doc["scenes"][0]["nodes"]]}


def verify(report: dict, height: float | None, length: float | None, origin: str,
           offset: tuple[float, float, float], caps: dict[str, int]) -> list[str]:
    """What the written static file gets wrong; empty when it is what was asked for."""
    problems = []
    tolerance = 1e-4 * max(1.0, max(report["size"]))
    if height is not None and abs(report["size"][1] - height) > tolerance:
        problems.append(f"height {report['size'][1]:.5f} is not {height}")
    if height is None and length is not None and abs(max(report["size"][0], report["size"][2]) - length) > tolerance:
        problems.append(f"length {max(report['size'][0], report['size'][2]):.5f} is not {length}")
    if origin != "keep":
        if abs(report["min"][1] - offset[1]) > tolerance:
            problems.append(f"base is at y={report['min'][1]:.5f}, not {offset[1]}")
        if origin == "base":
            for axis in (0, 2):
                middle = (report["min"][axis] + report["max"][axis]) / 2 - offset[axis]
                if abs(middle) > tolerance:
                    problems.append(f"axis {axis} is centred on {middle:.5f}, not 0")
    if not report["declared_bounds_match"]:
        problems.append("an accessor's min/max does not match its vertices")
    if any(key in node for node in report["root_nodes"] for key in ("translation", "rotation", "scale", "matrix")):
        problems.append("a node still carries a transform")
    for material in report["materials"]:
        if "emissiveTexture" in material or "emissiveFactor" in material:
            problems.append(f"{material.get('name')} still emits")
    for image in report["images"]:
        # Named by role above; an image of an unknown name is held to the loosest cap.
        role = next((r for r, name in IMAGE_NAMES.items() if image["name"].endswith(name)), None)
        if image["format"] != "PNG" or max(image["size"]) > (caps[role] if role else max(caps.values())):
            problems.append(f"image {image['name']} is {image['format']} {image['size']}")
    return problems


def describe(report: dict) -> str:
    low, high, size = report["min"], report["max"], report["size"]
    lines = [f"{Path(report['path']).name}: {report['triangles']:,} triangles, {report['vertices']:,} "
             f"vertices, {report['bytes'] // 1024} KB",
             f"  bounds  x {low[0]:.3f}..{high[0]:.3f}  y {low[1]:.3f}..{high[1]:.3f}  "
             f"z {low[2]:.3f}..{high[2]:.3f}   size {size[0]:.3f} x {size[1]:.3f} x {size[2]:.3f} m"]
    lines += [f"  texture {line}" for line in report["textures"]] or ["  texture none (untextured source)"]
    for material in report["materials"]:
        pbr = material.get("pbrMetallicRoughness", {})
        lines.append(f"  material {material.get('name')}: metallic {pbr.get('metallicFactor', 1.0)} "
                     f"roughness {pbr.get('roughnessFactor', 1.0)}"
                     + (" + metallicRoughness map" if "metallicRoughnessTexture" in pbr else "")
                     + (f" colour factor {[round(c, 3) for c in pbr['baseColorFactor'][:3]]}"
                        if "baseColorFactor" in pbr else ""))
    if not report["has_normals"]:
        lines.append("  !! no NORMAL attribute: the static audit will flag missing-normals")
    if report["images"] and not report["has_uv"]:
        lines.append("  !! textured but no TEXCOORD_0")
    if report["non_unit_normals"]:
        lines.append(f"  !! {report['non_unit_normals']} normal(s) are not unit length (zero in the source)")
    return "\n".join(lines)


def triple(text: str) -> tuple[float, float, float]:
    values = tuple(float(part) for part in text.split(","))
    if len(values) != 3:
        raise argparse.ArgumentTypeError("expected three comma-separated numbers")
    return values


def main() -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", type=Path)
    parser.add_argument("dest", type=Path)
    size = parser.add_mutually_exclusive_group()
    size.add_argument("--height", type=float, help="metres tall after the bake")
    size.add_argument("--length", type=float, help="metres along the longest horizontal axis instead")
    parser.add_argument("--yaw", type=float, default=0.0, help="degrees about +Y, baked before measuring")
    parser.add_argument("--origin", choices=ORIGINS, default="base",
                        help="base: centre of the bounds at ground level (default). footprint: centre of "
                             "the lowest 5%% (a trunk, not its canopy). keep: scale about the source origin")
    parser.add_argument("--offset", type=triple, default=(0.0, 0.0, 0.0), metavar="X,Y,Z",
                        help="metres added after the bake (a weapon's grip below the origin)")
    parser.add_argument("--max-texture", type=int, default=1024, help="longest edge of every image")
    parser.add_argument("--roughness", type=float, default=0.8,
                        help="roughness factor when no metallicRoughness texture exists")
    parser.add_argument("--tint", type=triple, metavar="R,G,B",
                        help="multiply baseColorFactor (linear 0-1); darkens a copy for free")
    parser.add_argument("--lighten", type=float, default=0.0, help="0-1 blend of the albedo toward white")
    parser.add_argument("--desaturate", type=float, default=0.0, help="0-1 blend of the albedo toward grey")
    parser.add_argument("--id", dest="asset_id", help="asset id for node and material names (default: dest stem)")
    parser.add_argument("--allow-multi-mesh", action="store_true")
    args = parser.parse_args()
    options = {key: value for key, value in vars(args).items() if key not in ("source", "dest")}
    try:
        report = prepare(args.source, args.dest, **options)
    except PrepError as error:
        print(f"meshy_prep_static: {args.source.name}: {error}", file=sys.stderr)
        return 1
    print(describe(report))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
