#!/usr/bin/env python3
"""Permanent Embervale 3D inventory, audit, and report generator.

This tool never modifies a production asset (it also owns the texture budget rules that
`assets.py audit-weight` reports against and `assets.py adopt` writes). It combines byte-level glTF inspection,
repository usage/import analysis, optional Blender inspection, and optional Godot imported-scene
measurements. Reports are deterministic apart from the recorded run metadata.

Typical use:
    python tools/audit_3d.py
    python tools/audit_3d.py --render all
    python tools/audit_3d.py --render selected --asset assets/models/characters/chr_player_base.glb
    python tools/audit_3d.py --static-only
"""

from __future__ import annotations

import argparse
import csv
import datetime as dt
import hashlib
import json
import math
import os
import re
import shutil
import struct
import subprocess
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_REPORT = ROOT / "reports" / "3d" / "session-1-foundation"
MODEL_EXTENSIONS = {".glb", ".gltf"}
TEXT_EXTENSIONS = {".cs", ".gd", ".tscn", ".tres", ".godot", ".md", ".json", ".py"}
IGNORED_PARTS = {".git", ".godot", "bin", "obj", "artifacts", "reports", "__pycache__"}
EXPECTED_PREFIX = {
    "animations": "anim_", "architecture": ("bld_", "mod_"), "characters": ("chr_", "npc_", "fp_"),
    "creatures": ("enm_", "boss_", "mnt_"), "props": "prp_", "weapons": "wpn_", "world": "prp_",
}


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def rel(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()


def read_glb_json(path: Path) -> tuple[dict[str, Any], bytes]:
    raw = path.read_bytes()
    if len(raw) < 20 or raw[:4] != b"glTF":
        raise ValueError("not a glTF 2 GLB")
    _, version, length = struct.unpack_from("<III", raw, 0)
    if version != 2 or length > len(raw):
        raise ValueError(f"invalid GLB header (version={version}, declared={length}, actual={len(raw)})")
    offset, document, binary = 12, None, b""
    while offset + 8 <= length:
        size, kind = struct.unpack_from("<II", raw, offset)
        payload = raw[offset + 8:offset + 8 + size]
        if kind == 0x4E4F534A:
            document = json.loads(payload.rstrip(b" \x00").decode("utf-8"))
        elif kind == 0x004E4942:
            binary = payload
        offset += 8 + size
    if document is None:
        raise ValueError("GLB has no JSON chunk")
    return document, binary


def read_gltf(path: Path) -> tuple[dict[str, Any], bytes]:
    if path.suffix.lower() == ".glb":
        return read_glb_json(path)
    document = json.loads(path.read_text(encoding="utf-8"))
    buffers = document.get("buffers", [])
    binary = b""
    if len(buffers) == 1 and isinstance(buffers[0].get("uri"), str) and not buffers[0]["uri"].startswith("data:"):
        binary = (path.parent / buffers[0]["uri"]).read_bytes()
    return document, binary


def accessor_count(document: dict[str, Any], index: int | None) -> int:
    if index is None:
        return 0
    accessors = document.get("accessors", [])
    return int(accessors[index].get("count", 0)) if 0 <= index < len(accessors) else 0


def image_payload(document: dict[str, Any], binary: bytes, model_path: Path, image: dict[str, Any]) -> bytes:
    view_index = image.get("bufferView")
    if isinstance(view_index, int):
        views = document.get("bufferViews", [])
        if 0 <= view_index < len(views):
            view = views[view_index]
            start = int(view.get("byteOffset", 0))
            return binary[start:start + int(view.get("byteLength", 0))]
    uri = image.get("uri")
    if isinstance(uri, str) and not uri.startswith("data:"):
        candidate = model_path.parent / uri
        if candidate.is_file():
            return candidate.read_bytes()
    return b""


def png_jpeg_size(raw: bytes) -> list[int] | None:
    if raw.startswith(b"\x89PNG\r\n\x1a\n") and len(raw) >= 24:
        return list(struct.unpack(">II", raw[16:24]))
    if raw.startswith(b"\xff\xd8"):
        offset = 2
        while offset + 9 < len(raw):
            if raw[offset] != 0xFF:
                offset += 1
                continue
            marker = raw[offset + 1]
            if marker in {0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF}:
                height, width = struct.unpack(">HH", raw[offset + 5:offset + 9])
                return [width, height]
            if offset + 4 > len(raw):
                break
            offset += 2 + struct.unpack(">H", raw[offset + 2:offset + 4])[0]
    return None


def root_transform(document: dict[str, Any]) -> dict[str, Any]:
    scenes = document.get("scenes", [])
    scene_index = int(document.get("scene", 0))
    roots = scenes[scene_index].get("nodes", []) if 0 <= scene_index < len(scenes) else []
    nodes = document.get("nodes", [])
    records = []
    for index in roots:
        node = nodes[index]
        scale = node.get("scale", [1.0, 1.0, 1.0])
        translation = node.get("translation", [0.0, 0.0, 0.0])
        records.append({"node": node.get("name", str(index)), "translation": translation,
                        "rotation": node.get("rotation", [0.0, 0.0, 0.0, 1.0]), "scale": scale,
                        "matrix": node.get("matrix"), "negative": math.prod(scale) < 0})
    return {"roots": records, "has_negative": any(item["negative"] for item in records)}


def inspect_gltf(path: Path) -> dict[str, Any]:
    record: dict[str, Any] = {"path": rel(path), "category": path.parent.name, "file_bytes": path.stat().st_size,
                              "file_sha256": sha256(path), "errors": []}
    try:
        document, binary = read_gltf(path)
        meshes = document.get("meshes", [])
        primitives = [primitive for mesh in meshes for primitive in mesh.get("primitives", [])]
        attrs = [primitive.get("attributes", {}) for primitive in primitives]
        record.update({
            "mesh_count": len(meshes), "primitive_count": len(primitives),
            "vertex_count": sum(accessor_count(document, item.get("POSITION")) for item in attrs),
            "triangle_count": sum(accessor_count(document, primitive.get("indices")) // 3 if primitive.get("mode", 4) == 4
                                  else 0 for primitive in primitives),
            "material_count": len(document.get("materials", [])), "texture_count": len(document.get("textures", [])),
            "has_uv0": bool(attrs) and all("TEXCOORD_0" in item for item in attrs),
            "has_normals": bool(attrs) and all("NORMAL" in item for item in attrs),
            "has_tangents": bool(attrs) and all("TANGENT" in item for item in attrs),
            "skin_count": len(document.get("skins", [])),
            "bone_count": max((len(skin.get("joints", [])) for skin in document.get("skins", [])), default=0),
            "animation_count": len(document.get("animations", [])),
            "animation_clips": [animation.get("name", f"animation_{i}") for i, animation in enumerate(document.get("animations", []))],
            "root_transform": root_transform(document),
            "gltf_generator": document.get("asset", {}).get("generator", ""),
        })
        textures = []
        for index, image in enumerate(document.get("images", [])):
            payload = image_payload(document, binary, path, image)
            textures.append({"index": index, "name": image.get("name") or image.get("uri") or f"image_{index}",
                             "bytes": len(payload), "resolution": png_jpeg_size(payload),
                             "sha256": hashlib.sha256(payload).hexdigest() if payload else None})
        record["textures"] = textures
        record["texture_bytes"] = sum(item["bytes"] for item in textures)
        record["max_texture_resolution"] = max((max(item["resolution"]) for item in textures if item["resolution"]), default=0)
        materials = []
        for index, material in enumerate(document.get("materials", [])):
            pbr = material.get("pbrMetallicRoughness", {})
            materials.append({"index": index, "name": material.get("name", f"material_{index}"),
                              "metallic": float(pbr.get("metallicFactor", 1.0)),
                              "roughness": float(pbr.get("roughnessFactor", 1.0)),
                              "has_metallic_roughness_texture": "metallicRoughnessTexture" in pbr,
                              "base_color": pbr.get("baseColorFactor", [1, 1, 1, 1]),
                              "double_sided": bool(material.get("doubleSided", False)),
                              "alpha_mode": material.get("alphaMode", "OPAQUE")})
        record["materials"] = materials
    except Exception as exc:
        record["errors"].append(str(exc))
    return record


def parse_import(path: Path) -> dict[str, Any]:
    import_path = Path(str(path) + ".import")
    if not import_path.is_file():
        return {"present": False}
    text = import_path.read_text(encoding="utf-8", errors="replace")
    wanted = ("nodes/apply_root_scale", "nodes/root_scale", "meshes/ensure_tangents", "meshes/generate_lods",
              "meshes/create_shadow_meshes", "meshes/light_baking", "meshes/force_disable_compression",
              "animation/import", "animation/fps", "animation/trimming", "animation/remove_immutable_tracks",
              "animation/import_rest_as_RESET", "import_script/path")
    result: dict[str, Any] = {"present": True}
    for key in wanted:
        match = re.search(rf"(?m)^{re.escape(key)}=(.+)$", text)
        if match:
            raw = match.group(1).strip()
            result[key] = raw.strip('"') if raw.startswith('"') else ({"true": True, "false": False}.get(raw, float(raw) if re.fullmatch(r"-?\d+(\.\d+)?", raw) else raw))
    result["has_bone_map"] = "retarget/bone_map" in text or "bone_map" in text
    # The retarget's two identifying facts, which tools/assets.py classifies rig families from.
    bone_map = re.search(r'"retarget/bone_map":\s*Resource\("([^"]+)"\)', text)
    result["bone_map"] = Path(bone_map.group(1)).stem if bone_map else None
    skeleton = re.search(r'"retarget/bone_renamer/unique_node/skeleton_name":\s*"([^"]+)"', text)
    result["skeleton_name"] = skeleton.group(1) if skeleton else None
    result["subresource_count"] = len(re.findall(r"(?m)^\w.+?=\{", text))
    return result


# --------------------------------------------------------------------------- texture weight
#
# Every texture a model uses is a .png beside it (gltf/embedded_image_handling=1 extracts an
# embedded image to <model>_texture_N.png), so its video-memory cost is decided entirely by that
# png's .import. The budget below is data/rendering/VisualContract.json's three caps (prop 512,
# hero 1024, player/boss 2048) applied by class and by map role; docs/3D_ASSETS.md "Texture weight"
# is the prose. `python tools/assets.py audit-weight` reports against it and `adopt` writes it.
#
# The numbers are longest-edge caps written as process/size_limit. A trim sheet tiles across a whole
# building and is read at arm's length, so it is a hero surface (1024), not a prop.
# Creatures that are DELIBERATELY one mesh in another coat: copy -> the model it was written from
# (tools/meshy_prep_static.py --tint / --lighten). Identical geometry between two creatures is
# otherwise a critical flag, because it has always meant a stand-in that was never replaced. A
# family listed here is reported as info instead; anything else that matches still fails loudly.
RECOLOURED_COPIES: dict[str, str] = {
    "assets/models/creatures/enm_dire_wolf.glb": "assets/models/creatures/enm_wolf.glb",
    "assets/models/creatures/enm_frost_stalker.glb": "assets/models/creatures/enm_wolf.glb",
}

TEXTURE_BUDGET: dict[str, dict[str, int]] = {
    "player_boss":       {"base": 2048, "normal": 2048, "orm": 1024},
    "character":         {"base": 1024, "normal": 1024, "orm": 512},
    "architecture_trim": {"base": 1024, "normal": 1024, "orm": 512},
    "prop_trim":         {"base": 1024, "normal": 512, "orm": 512},
    "nature_hero":       {"base": 1024, "normal": 512, "orm": 512},
    "world_hero":        {"base": 2048, "normal": 1024, "orm": 1024},
    "world_landmark":    {"base": 1024, "normal": 1024, "orm": 512},
    "prop":              {"base": 512, "normal": 512, "orm": 512},
}
# Trees and rock faces the player walks up to; every other nature texture is a small prop.
NATURE_HERO = ("T_Nature_Leaves", "T_Nature_LeafBroadleaf", "T_Nature_BarkBroadleaf",
               "T_Nature_BarkDead", "T_Nature_Rocks")
# ⚠️ DATA TEXTURES STAY LOSSLESS AND UNMIPPED, ON PURPOSE. A palette is flat swatches and gradient
# strips sampled at a point: block compression bands the gradient, and a mip level averages a
# swatch with its neighbour (the grass strip goes orange at distance). The player region mask is
# read with filter_linear by player_body.gdshader, and a blurred mask smears hair over clothing.
LOSSLESS = ("T_Prop_Colormap", "T_Nature_Grass")
# assets/models/world/ is the generated outdoor set: one model, one atlas per map role, extracted to
# <model>_BaseColor/_Normal/_ORM.png (tools/meshy_prep_static.py names the images), so the class is
# read from the MODEL's name. Three giants the player stands under get a 2048 albedo; every other
# landmark, the trees and the two placed 10 m+ rock and ice pieces get 1024; scatter is a prop.
WORLD_HERO = ("prp_lm_iron_citadel", "prp_lm_colossus", "prp_lm_godhall")
WORLD_LANDMARK = ("prp_lm_", "prp_tree_", "prp_pine_", "prp_rock_outcrop", "prp_ice_wall")
# A dragon fills the screen the way a boss does, whatever its file is called.
DRAGONS = ("enm_ancient_dragon", "enm_ash_dragon", "enm_wild_dragon", "enm_frost_drake")
VRAM_COMPRESSED = "2"


def texture_role(stem: str) -> str:
    lowered = stem.lower()
    if lowered.endswith("_normal"):
        return "normal"
    if lowered.endswith(("_orm", "_roughness", "_metallic", "_ao")):
        return "orm"
    return "base"


def texture_group(path: Path) -> str:
    """The budget class of one texture, from its folder and name alone (never authored)."""
    stem, folder = path.stem, path.parent.name
    if stem in LOSSLESS or stem.endswith("_mask") or "colormap" in stem.lower():
        return "lossless"
    if folder in ("characters", "creatures"):
        return "player_boss" if stem.startswith(("chr_player", "boss_") + DRAGONS) else "character"
    if folder == "world":
        if stem.startswith(WORLD_HERO):
            return "world_hero"
        return "world_landmark" if stem.startswith(WORLD_LANDMARK) else "prop"
    if stem.startswith(NATURE_HERO):
        return "nature_hero"
    if stem.startswith("T_Trim_"):
        return "prop_trim"
    # A BaseColor/Normal/ORM set in the architecture folder is a building trim sheet; a lone
    # texture there (the vine leaf card) is a prop.
    if folder == "architecture" and (texture_role(stem) != "base" or stem.endswith("_BaseColor")):
        return "architecture_trim"
    return "prop"


def import_params(import_path: Path) -> dict[str, str]:
    """The raw `key=value` lines of a .import's [params] section."""
    params: dict[str, str] = {}
    section = ""
    for line in import_path.read_text(encoding="utf-8").splitlines():
        if line.startswith("["):
            section = line
        elif section == "[params]" and "=" in line:
            key, value = line.split("=", 1)
            params[key] = value
    return params


def png_uses_alpha(path: Path, header: bytes) -> bool:
    """Whether the importer will keep an alpha channel (BC3) or drop it (BC1).

    Godot decides from the pixels, not the file's colour type: an RGBA png whose alpha is 255
    everywhere compresses as BC1. Reading pixels needs Pillow; without it an alpha channel is
    counted as used, which over-estimates and never under-reports.
    """
    if len(header) < 26 or header[25] not in (4, 6):
        return False
    try:
        from PIL import Image
    except ImportError:
        return True
    with Image.open(path) as image:
        return image.getchannel("A").getextrema()[0] < 255


def texture_vram_bytes(width: int, height: int, params: dict[str, str], uses_alpha: bool) -> int:
    """Estimated video memory of one imported texture.

    BC1 is 0.5 byte per pixel, BC3 (alpha) and RGTC (normal maps) 1, and anything not VRAM
    compressed is 4: the renderer uploads RGB8 as RGBA8. A mip chain adds a third.
    """
    limit, longest = int(params.get("process/size_limit", "0")), max(width, height)
    if 0 < limit < longest:
        width, height = max(1, width * limit // longest), max(1, height * limit // longest)
    if params.get("compress/mode") == VRAM_COMPRESSED:
        per_pixel = 1.0 if uses_alpha or params.get("compress/normal_map") == "1" else 0.5
    else:
        per_pixel = 4.0
    mips = 4 / 3 if params.get("mipmaps/generate") == "true" else 1.0
    return int(width * height * per_pixel * mips)


def texture_targets(path: Path, width: int, height: int) -> dict[str, str]:
    """The [params] values this texture's class requires. A key not listed is left alone."""
    group, role = texture_group(path), texture_role(path.stem)
    if group == "lossless":
        # detect_3d/compress_to=1 would let the editor flip these to VRAM + mips on first 3D use.
        return {"mipmaps/generate": "false", "detect_3d/compress_to": "0"}
    targets = {"compress/mode": VRAM_COMPRESSED, "mipmaps/generate": "true", "detect_3d/compress_to": "0"}
    if role == "normal":
        # What the editor's own normal-map detection writes: RGTC, and no roughness limiter.
        targets.update({"compress/normal_map": "1", "roughness/mode": "1",
                        "roughness/src_normal": f'"res://{rel(path)}"'})
    cap = TEXTURE_BUDGET[group][role]
    if max(width, height) > cap:
        targets["process/size_limit"] = str(cap)
    return targets


def texture_weight() -> list[dict[str, Any]]:
    """One record per texture under assets/models: its class, its cost and what is off budget."""
    records = []
    for import_path in sorted((ROOT / "assets" / "models").rglob("*.png.import")):
        path = import_path.with_suffix("")
        with path.open("rb") as stream:
            header = stream.read(32)
        size = png_jpeg_size(header)
        if size is None:
            continue
        params = import_params(import_path)
        targets = texture_targets(path, size[0], size[1])
        records.append({
            "path": rel(path), "group": texture_group(path), "role": texture_role(path.stem),
            "size": size, "params": params,
            "bytes": texture_vram_bytes(size[0], size[1], params, png_uses_alpha(path, header)),
            "problems": [f"{key}={params.get(key)} (want {value})"
                         for key, value in targets.items() if params.get(key) != value],
            "targets": targets,
        })
    return records


def world_texture_duplicates(records: list[dict[str, Any]]) -> dict[str, list[str]]:
    """World models that embed an image another world model also embeds, byte for byte.

    ⚠️ THIS IS THE SHARED-TEXTURE RULE FOR assets/models/world/, AND IT ACCEPTS AN EMBEDDED ATLAS
    ON PURPOSE. share_nature_textures.py externalises the kit's images because many kit props use
    one pack texture. A generated world model is the opposite case: its atlas is unwrapped for that
    mesh alone, so embedding it is the single copy and there is no family to share with. What must
    still never ship is the SAME atlas inside two files (a rescaled or retinted copy written as a
    second model): that is two imported textures for one image, so it fails here and the fix is an
    instance scale or a scatter Tint on the one model.
    """
    owners: dict[str, list[str]] = defaultdict(list)
    for record in records:
        if record.get("category") == "world":
            for digest in sorted({item["sha256"] for item in record.get("textures", []) if item.get("sha256")}):
                owners[digest].append(record["path"])
    result: dict[str, list[str]] = defaultdict(list)
    for paths in owners.values():
        for path in paths:
            result[path] += [other for other in paths if other != path and other not in result[path]]
    return {path: others for path, others in result.items() if others}


def patch_texture_import(text: str, targets: dict[str, str]) -> str:
    """Return a png's .import text with `targets` applied. Pure: the caller writes the file.

    ⚠️ Only the VALUE of an existing `key=value` line is replaced. Godot answers an .import it
    cannot parse by reimporting with defaults and says nothing, so a missing key is an error here
    rather than a line appended in a guessed syntax.
    """
    for key, value in targets.items():
        pattern = re.compile(rf"(?m)^{re.escape(key)}=.*$")
        if not pattern.search(text):
            raise ValueError(f"{key} is not in the [params] section")
        text = pattern.sub(lambda _match, line=f"{key}={value}": line, text, count=1)
    # A texture leaving lossless names one .ctex in [remap]; a VRAM one names an s3tc and an etc2
    # file (project.godot imports both families). Writing the form Godot itself writes points
    # [remap] at files that do not exist yet, which forces the reimport, and leaves the committed
    # file byte-identical to the one the engine saves afterwards.
    single = re.search(r'(?m)^path="(res://\.godot/imported/[^"]+)\.ctex"$', text)
    if single and targets.get("compress/mode") == VRAM_COMPRESSED:
        s3tc, etc2 = f"{single.group(1)}.s3tc.ctex", f"{single.group(1)}.etc2.ctex"
        text = text.replace(single.group(0), f'path.s3tc="{s3tc}"\npath.etc2="{etc2}"')
        text = text.replace('metadata={\n"vram_texture": false\n}',
                            'metadata={\n"imported_formats": ["s3tc_bptc", "etc2_astc"],\n"vram_texture": true\n}')
        text = re.sub(r"(?m)^dest_files=\[.*\]$", lambda _match: f'dest_files=["{s3tc}", "{etc2}"]', text, count=1)
    return text


def load_manifest() -> list[dict[str, Any]]:
    path = ROOT / "assets" / "library" / "manifest.json"
    if not path.is_file():
        return []
    payload = json.loads(path.read_text(encoding="utf-8"))
    if isinstance(payload, list):
        return payload
    for key in ("models", "assets", "entries"):
        if isinstance(payload.get(key), list):
            return payload[key]
    if isinstance(payload, dict):
        flattened = []
        for pack, entries in payload.items():
            if isinstance(entries, list):
                flattened.extend([{**item, "pack": pack} for item in entries if isinstance(item, dict)])
        return flattened
    return []


def provenance_for(record: dict[str, Any], credits: str, manifest: list[dict[str, Any]]) -> dict[str, Any]:
    stem = Path(record["path"]).stem.lower()
    words = {part for part in re.split(r"[_\-\s]+", stem) if len(part) > 2 and part not in {"prp", "enm", "npc", "chr", "bld", "mod", "wpn"}}
    matches = []
    for item in manifest:
        blob = json.dumps(item, sort_keys=True).lower()
        score = sum(word in blob for word in words)
        if score >= max(1, len(words) - 1):
            matches.append(item)
    credit_lines = [line.strip() for line in credits.splitlines() if stem in line.lower()]
    licence = "CC0" if matches or credit_lines or "quaternius" in record.get("gltf_generator", "").lower() else "UNRESOLVED"
    source = "assets/library/manifest.json candidate" if matches else ("assets/CREDITS.md historical record" if credit_lines else "unresolved")
    return {"source": source, "licence": licence, "manifest_candidates": matches[:5], "credit_mentions": credit_lines[:5]}


# The derived production manifest names every asset by path, so counting it as a *use* of one makes
# every model look referenced. That is not cosmetic: `architecture-no-collision` is a critical flag
# that only fires on a used asset, so four unreferenced wall modules were reported as shipped
# architecture with no collider.
NOT_A_USAGE = {"assets/models/manifest.json"}


def repository_texts() -> dict[str, str]:
    result = {}
    for path in ROOT.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in TEXT_EXTENSIONS or any(part in IGNORED_PARTS for part in path.parts):
            continue
        if rel(path) in NOT_A_USAGE:
            continue
        try:
            result[rel(path)] = path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            pass
    return result


def usage_for(model_path: str, texts: dict[str, str]) -> dict[str, Any]:
    resource_path = "res://" + model_path
    basename = Path(model_path).name
    hits = []
    count = 0
    for source, text in texts.items():
        exact = text.count(resource_path)
        if exact:
            capsule_heights = [float(value) for value in re.findall(r"CapsuleHeight\s*=\s*([0-9.]+)", text)]
            capsule_radii = [float(value) for value in re.findall(r"CapsuleRadius\s*=\s*([0-9.]+)", text)]
            hits.append({"path": source, "count": exact, "has_collision_nodes": "CollisionShape3D" in text or "StaticBody3D" in text,
                         "capsule_heights": capsule_heights, "capsule_radii": capsule_radii})
            count += exact
    return {"count": count, "files": hits, "basename_mentions": sum(text.count(basename) for text in texts.values())}


def discover_executable(name: str, candidates: list[Path]) -> Path | None:
    from quality_common import discover_blender, discover_godot
    if name == "blender": return discover_blender()
    if name == "godot": return discover_godot()
    found = shutil.which(name)
    if found:
        return Path(found)
    return next((path for path in candidates if path.is_file()), None)


def run_external(command: list[str], timeout: int) -> tuple[bool, str]:
    from quality_common import run_process
    completed = run_process(command, cwd=ROOT, timeout=timeout)
    return completed.returncode == 0, completed.output or completed.launch_error or ""


def load_json_if(path: Path) -> dict[str, Any]:
    if not path.is_file():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def flags_for(record: dict[str, Any]) -> list[dict[str, str]]:
    flags: list[dict[str, str]] = []
    def add(code: str, severity: str, detail: str) -> None:
        flags.append({"code": code, "severity": severity, "detail": detail})
    if record["errors"]: add("parse-error", "critical", "; ".join(record["errors"]))
    if not record.get("has_normals", True): add("missing-normals", "high", "one or more primitives have no NORMAL attribute")
    if record.get("texture_count", 0) and not record.get("has_uv0", True): add("missing-uv", "high", "textured asset has a primitive without UV0")
    if record.get("material_count", 0) and not record.get("has_tangents", True) and not record["import"].get("meshes/ensure_tangents", False):
        add("missing-tangents", "medium", "no tangents in payload and Godot tangent generation is disabled")
    if record.get("max_texture_resolution", 0) > 4096: add("oversized-texture", "high", f"largest embedded texture is {record['max_texture_resolution']} px")
    elif record.get("max_texture_resolution", 0) > 2048: add("large-texture", "medium", f"largest embedded texture is {record['max_texture_resolution']} px")
    if record.get("material_count", 0) > 12: add("excessive-materials", "high", f"{record['material_count']} materials")
    elif record.get("material_count", 0) > 6: add("many-materials", "medium", f"{record['material_count']} materials")
    if record.get("triangle_count", 0) > 100_000: add("very-high-triangles", "high", f"{record['triangle_count']:,} triangles")
    elif record.get("category") == "props" and record.get("triangle_count", 0) > 25_000: add("expensive-prop", "medium", f"simple-prop category has {record['triangle_count']:,} triangles")
    transform = record.get("root_transform", {})
    if transform.get("has_negative"): add("negative-root-scale", "high", "root transform has negative determinant")
    for root in transform.get("roots", []):
        if any(abs(float(v)) > 0.05 for v in root.get("translation", [])) and record.get("skin_count", 0):
            add("rig-root-translation", "high", f"rigged root {root['node']} has translation {root['translation']}")
    blender = record.get("blender", {})
    dimensions = blender.get("dimensions") or record.get("godot", {}).get("dimensions")
    if dimensions:
        largest, smallest = max(dimensions), min(dimensions)
        if largest > 100 or (largest < 0.01 and largest > 0): add("extreme-scale", "high", f"world dimensions {dimensions}")
        if smallest > 0 and largest / smallest > 1000: add("extreme-proportions", "medium", f"world dimensions {dimensions}")
    base = blender.get("aabb_min", [0, 0, 0])[2] if blender.get("aabb_min") else None
    if base is not None and abs(base) > max(0.1, (dimensions or [1,1,1])[2] * 0.05):
        add("ground-offset", "high", f"lowest rendered point is Z={base:.3f} m in Blender")
    if blender.get("negative_transform_count", 0): add("negative-transform", "high", f"{blender['negative_transform_count']} object transforms have negative determinant")
    path_words = record["path"].lower()
    path_semantic = next((word for word in ("skin", "wood", "cloth", "stone") if word in path_words), None)
    if path_semantic is None and any(word in path_words for word in ("waystone", "boulder", "rock", "glacier")): path_semantic="stone"
    for material in record.get("materials", []):
        name=material.get("name","").lower()
        semantic=next((word for word in ("skin", "wood", "cloth", "stone") if word in name), None)
        if semantic is None and any(word in name for word in ("rock", "plaster")): semantic="stone"
        if semantic is None: semantic=path_semantic
        metallic=float(material.get("metallic",0))
        if semantic and metallic>0.25:
            if material.get("has_metallic_roughness_texture"):
                add(f"metallic-{semantic}-risk", "medium", f"{material.get('name')} uses metallic multiplier {metallic:.2f} with a metallic/roughness texture; inspect channel values")
            else:
                add(f"metallic-{semantic}", "high", f"{material.get('name')} sets metallic factor {metallic:.2f} without a metallic texture")
            break
    if record.get("skin_count", 0) and record.get("animation_count", 0) == 0 and record["category"] in {"characters", "creatures"}:
        add("rig-missing-local-animation", "medium", "rigged actor has no local clips; verify shared-library resolution")
    if record["category"] == "architecture" and record["usage"]["count"] and not any(item["has_collision_nodes"] for item in record["usage"]["files"]):
        add("architecture-no-collision", "critical", "used architecture has no collision node in any direct usage file")
    if record["usage"]["count"] >= 50:
        add("excessive-reuse", "medium", f"direct resource path appears {record['usage']['count']} times; visually inspect repetition and HLOD impact")
    godot_record = record.get("godot", {})
    if godot_record.get("bounds_reliable", True) and godot_record.get("dimensions"):
        collision_height = godot_record["dimensions"][1]
        collision_source = "Godot imported"
    else:
        collision_height = (record.get("blender", {}).get("dimensions") or [0, 0, 0])[2]
        collision_source = "Blender evaluated"
    authored_heights = [height for use in record["usage"]["files"] for height in use.get("capsule_heights", [])]
    if record["category"] in {"characters", "creatures"} and collision_height > 0 and authored_heights:
        worst = max(abs(height - collision_height) / max(collision_height, 0.001) for height in authored_heights)
        if worst > 0.35:
            add("collision-render-mismatch", "high", f"{collision_source} render height {collision_height:.2f} m vs authored capsule height(s) {authored_heights}")
    if not record["import"].get("present"): add("missing-import-config", "medium", "no committed .import sidecar")
    elif record["import"].get("nodes/root_scale", 1.0) != 1.0: add("nonunit-import-scale", "info", f"measured import correction is {record['import'].get('nodes/root_scale')}; do not normalize blindly")
    if not record["provenance"]["licence"] or record["provenance"]["licence"] == "UNRESOLVED": add("unresolved-provenance", "high", "no confident manifest/CREDITS provenance match")
    prefix = EXPECTED_PREFIX.get(record["category"])
    if prefix and not record["path"].split("/")[-1].startswith(prefix): add("inconsistent-name", "low", f"filename does not use expected {prefix} prefix")
    return flags


def recommendation(record: dict[str, Any]) -> str:
    codes = {item["code"] for item in record["flags"]}
    if record["usage"]["count"] == 0 and record["category"] != "animations": return "REPLACE" if codes & {"unresolved-provenance", "parse-error"} else "KEEP"
    if codes & {"parse-error", "architecture-no-collision"}: return "IMPROVE"
    if codes & {"metallic-skin", "metallic-wood", "metallic-cloth", "metallic-stone", "ground-offset", "extreme-scale", "negative-transform", "rig-root-translation"}: return "IMPROVE"
    if codes & {"very-high-triangles", "excessive-materials", "oversized-texture"}: return "IMPROVE"
    return "KEEP"


def md_table(headers: list[str], rows: list[list[Any]]) -> str:
    def clean(value: Any) -> str: return str(value).replace("|", "\\|").replace("\n", " ")
    return "| " + " | ".join(headers) + " |\n| " + " | ".join("---" for _ in headers) + " |\n" + "\n".join("| " + " | ".join(clean(v) for v in row) + " |" for row in rows) + "\n"


def write_reports(records: list[dict[str, Any]], output: Path, metadata: dict[str, Any]) -> None:
    output.mkdir(parents=True, exist_ok=True)
    render_files = sorted((output / "renders").glob("*.png")) if (output / "renders").is_dir() else []
    metadata["render_file_count"] = len(render_files)
    findings = [{"path": item["path"], "recommendation": item["recommendation"], **flag}
                for item in records for flag in item["flags"]]
    (output / "inventory.json").write_text(json.dumps({"schema_version": 1, "metadata": metadata, "assets": records}, indent=2), encoding="utf-8")
    (output / "findings.json").write_text(json.dumps({"schema_version": 1, "metadata": metadata, "findings": findings}, indent=2), encoding="utf-8")
    columns = ["path", "category", "file_bytes", "mesh_count", "vertex_count", "triangle_count", "primitive_count", "material_count", "texture_count", "texture_bytes", "skin_count", "bone_count", "animation_count", "usage_count", "recommendation", "flag_count"]
    with (output / "inventory.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=columns); writer.writeheader()
        for item in records:
            writer.writerow({**{key: item.get(key, "") for key in columns}, "usage_count": item["usage"]["count"], "recommendation": item["recommendation"], "flag_count": len(item["flags"])})

    severity_order = {"critical": 0, "high": 1, "medium": 2, "low": 3, "info": 4}
    findings.sort(key=lambda item: (severity_order.get(item["severity"], 9), -next(r["usage"]["count"] for r in records if r["path"] == item["path"]), item["path"], item["code"]))
    categories = Counter(item["category"] for item in records)
    recs = Counter(item["recommendation"] for item in records)
    overview = ["# Embervale 3D audit", "", "This folder records a point-in-time production-model audit for its containing work session.", "",
                "## Scope and run", "", f"- Production assets audited: **{len(records)}** (`assets/models/**/*.glb|gltf`)",
                f"- Categories: {', '.join(f'{k} {v}' for k,v in sorted(categories.items()))}",
                f"- Findings: {len(findings)} ({', '.join(f'{k} {v}' for k,v in sorted(Counter(f['severity'] for f in findings).items()))})",
                f"- Recommendations: {', '.join(f'{k} {v}' for k,v in sorted(recs.items()))}",
                f"- Blender: {metadata.get('blender', 'not run')}", f"- Godot imported-scene probe: {metadata.get('godot', 'not run')}", "",
                f"- Committed diagnostic render files: **{len(render_files)}**", "",
                "## Read next", "", "1. `prioritized-findings.md` — ordered worklist.", "2. `production-inventory.md` — complete human-readable inventory.",
                "3. `visual-qa-index.md` — truthful Blender views and sampled rig poses.",
                "4. Domain reports (`materials`, `scale-origin`, `rig-animation`, `collision`, `texture-performance`, `duplicates`).",
                "5. `inventory.json` and `findings.json` for automation.", "", "Production assets were inspected only; the audit does not rewrite them."]
    (output / "README.md").write_text("\n".join(overview) + "\n", encoding="utf-8")

    inventory_rows = [[r["path"], r["usage"]["count"], r.get("mesh_count",0), f"{r.get('triangle_count',0):,}", r.get("material_count",0), r.get("texture_count",0), r.get("bone_count",0), r.get("animation_count",0), r["recommendation"], len(r["flags"])] for r in records]
    (output / "production-inventory.md").write_text("# Production 3D inventory\n\n" + md_table(["Asset", "Uses", "Meshes", "Triangles", "Materials", "Textures", "Bones", "Clips", "Recommendation", "Flags"], inventory_rows), encoding="utf-8")

    priority_rows = [[f["severity"].upper(), f["path"], f["code"], f["detail"], f["recommendation"]] for f in findings]
    (output / "prioritized-findings.md").write_text("# Prioritized model-quality findings\n\nAutomated flags are triage evidence, not authorization to alter an asset. Confirm with visual QA and dependent tracing.\n\n" + md_table(["Severity", "Asset", "Finding", "Evidence", "Action"], priority_rows), encoding="utf-8")

    exact_groups = defaultdict(list); geometry_groups = defaultdict(list); texture_groups = defaultdict(list)
    for r in records:
        exact_groups[r["file_sha256"]].append(r["path"])
        gh = r.get("blender", {}).get("geometry_sha256")
        if gh: geometry_groups[gh].append(r["path"])
        for tex in r.get("textures", []):
            if tex.get("sha256"): texture_groups[tex["sha256"]].append(f"{r['path']}::{tex['name']}")
    duplicate_sections = ["# Duplicate analysis", "", "Exact payload duplicates compare source files; geometry duplicates compare normalized evaluated meshes from Blender; texture duplicates compare decoded source payload bytes.", ""]
    for title, groups in (("Exact model payloads", exact_groups), ("Geometry", geometry_groups), ("Textures", texture_groups)):
        duplicate_sections += [f"## {title}", ""]
        duplicated = [items for items in groups.values() if len(items) > 1]
        duplicate_sections.append(md_table(["Count", "Members"], [[len(items), "<br>".join(items)] for items in sorted(duplicated, key=lambda x:(-len(x),x))]) if duplicated else "No duplicates detected.\n")
    (output / "duplicate-analysis.md").write_text("\n".join(duplicate_sections), encoding="utf-8")

    material_rows = [[r["path"], r.get("material_count",0), max((m.get("metallic",0) for m in r.get("materials",[])), default=0), ", ".join(m.get("name","") for m in r.get("materials",[])), ", ".join(f["code"] for f in r["flags"] if f["code"].startswith("metallic-") or "material" in f["code"])] for r in records]
    (output / "materials-analysis.md").write_text("# Materials analysis\n\n" + md_table(["Asset", "Count", "Max metallic", "Materials", "Flags"], material_rows), encoding="utf-8")

    scale_rows=[]
    for r in records:
        godot_source=r.get("godot",{}); source=godot_source if godot_source.get("bounds_reliable",True) and godot_source.get("dimensions") else r.get("blender",{}); scale_rows.append([r["path"], source.get("dimensions"), source.get("aabb_min"), source.get("aabb_max"), r["import"].get("nodes/root_scale",1), r.get("root_transform",{}).get("has_negative",False), ", ".join(f["code"] for f in r["flags"] if any(k in f["code"] for k in ("scale","offset","root","transform")))])
    (output / "scale-origin-analysis.md").write_text("# Scale and origin analysis\n\nGodot imported-scene dimensions take precedence when available. Import scale corrections are recorded, never automatically normalized.\n\n" + md_table(["Asset", "World dimensions m", "AABB min", "AABB max", "Import scale", "Negative root", "Flags"], scale_rows), encoding="utf-8")

    rig_rows=[[r["path"],r.get("skin_count",0),r.get("bone_count",0),r.get("animation_count",0),"<br>".join(r.get("animation_clips",[])),r["import"].get("has_bone_map",False),", ".join(f["code"] for f in r["flags"] if "rig" in f["code"] or "animation" in f["code"])] for r in records if r.get("skin_count",0) or r.get("animation_count",0)]
    (output / "rig-animation-analysis.md").write_text("# Rig and animation analysis\n\nClip names list payload clips; `inventory.json` also carries imported Godot clip names when the probe succeeds. Gameplay slot resolution must still be validated by project tests.\n\n" + md_table(["Asset", "Skins", "Bones", "Payload clips", "Clip names", "Bone map", "Flags"], rig_rows), encoding="utf-8")

    collision_rows=[[r["path"],r["usage"]["count"],sum(1 for f in r["usage"]["files"] if f["has_collision_nodes"]),r.get("godot",{}).get("collision_node_count",0),", ".join(f["code"] for f in r["flags"] if "collision" in f["code"])] for r in records]
    (output / "collision-analysis.md").write_text("# Collision analysis\n\nCollision and render geometry are intentionally assessed separately. A direct-usage collision count is a repository heuristic; inspect the listed usage files before changing shared resources.\n\n" + md_table(["Asset", "Direct uses", "Usage files with collision", "Imported collision nodes", "Flags"], collision_rows), encoding="utf-8")

    perf_rows=[[r["path"],f"{r.get('file_bytes',0)/1048576:.2f}",f"{r.get('triangle_count',0):,}",r.get("primitive_count",0),r.get("material_count",0),r.get("texture_count",0),f"{r.get('texture_bytes',0)/1048576:.2f}",r.get("max_texture_resolution",0),", ".join(f["code"] for f in r["flags"] if any(k in f["code"] for k in ("texture","triangle","material","prop")))] for r in sorted(records,key=lambda x:x.get("triangle_count",0),reverse=True)]
    (output / "texture-performance-analysis.md").write_text("# Texture and performance analysis\n\n" + md_table(["Asset", "File MiB", "Triangles", "Primitives", "Materials", "Textures", "Texture MiB", "Max px", "Flags"], perf_rows), encoding="utf-8")

    rec_rows=[[r["recommendation"],r["path"],r["usage"]["count"],", ".join(f["code"] for f in r["flags"])] for r in sorted(records,key=lambda x:(x["recommendation"],-x["usage"]["count"],x["path"]))]
    (output / "recommendations.md").write_text("# KEEP / IMPROVE / KITBASH / REPLACE / CUSTOM BUILD recommendations\n\nThese are conservative Session 1 triage recommendations. `KITBASH` and `CUSTOM BUILD` remain available categories but are not assigned automatically; they require visual/design judgment in an overhaul session.\n\n" + md_table(["Recommendation", "Asset", "Uses", "Basis"], rec_rows), encoding="utf-8")

    render_groups=defaultdict(list)
    for path in render_files: render_groups[path.stem.split("__",1)[0]].append(path)
    visual=["# Visual QA render index", "", "These PNGs were rendered from the actual production assets by Blender. A selected set exercises the renderer and records representative high-priority assets; run `--render all` for a complete image batch or `--render selected` after an important model change.", ""]
    for stem, paths in sorted(render_groups.items()):
        visual += [f"## {stem}", "", " · ".join(f"[{path.stem.split('__',1)[-1]}](renders/{path.name})" for path in paths), ""]
    if not render_groups: visual.append("No renders were requested for this run.\n")
    (output / "visual-qa-index.md").write_text("\n".join(visual),encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--output", type=Path, default=DEFAULT_REPORT)
    parser.add_argument("--static-only", action="store_true", help="skip Blender and Godot probes")
    parser.add_argument("--render", choices=("none", "selected", "all"), default="none")
    parser.add_argument("--asset", action="append", default=[], help="repo-relative asset for selected rendering")
    parser.add_argument("--render-size", type=int, default=320)
    parser.add_argument("--pose", action="append", choices=("idle", "movement", "attack", "equipment"), default=[],
                        help="also render a matching rig action from the front three-quarter view")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        assert png_jpeg_size(b"\x89PNG\r\n\x1a\n" + b"\0"*8 + struct.pack(">II", 7, 11)) == [7,11]
        models = ROOT / "assets" / "models"
        assert texture_group(models / "creatures" / "boss_x_texture_0.png") == "player_boss"
        assert texture_group(models / "creatures" / "enm_x_texture_0.png") == "character"
        assert texture_group(models / "characters" / "chr_player_base_mask.png") == "lossless"
        assert texture_group(models / "architecture" / "T_Brick_Roughness.png") == "architecture_trim"
        assert texture_group(models / "architecture" / "T_VineLeaf_png.png") == "prop"
        assert texture_group(models / "props" / "T_Trim_Metal_ORM.png") == "prop_trim"
        assert texture_group(models / "props" / "T_Nature_BarkDead_Normal.png") == "nature_hero"
        assert texture_group(models / "props" / "T_Nature_Mushrooms.png") == "prop"
        assert texture_group(models / "world" / "prp_lm_iron_citadel_BaseColor.png") == "world_hero"
        assert texture_group(models / "world" / "prp_lm_colossus_head_Normal.png") == "world_hero"
        assert texture_group(models / "world" / "prp_lm_monolith_a_ORM.png") == "world_landmark"
        assert texture_group(models / "world" / "prp_tree_oak_a_BaseColor.png") == "world_landmark"
        assert texture_group(models / "world" / "prp_rock_boulder_a_BaseColor.png") == "prop"
        assert texture_group(models / "creatures" / "enm_ancient_dragon_BaseColor.png") == "player_boss"
        assert texture_role("prp_lm_monolith_a_ORM") == "orm" and texture_role("prp_lm_monolith_a_Normal") == "normal"
        assert "process/size_limit" not in texture_targets(models / "world" / "prp_lm_godhall_fragment_BaseColor.png", 2048, 2048)
        assert texture_targets(models / "world" / "prp_lm_arch_BaseColor.png", 2048, 2048)["process/size_limit"] == "1024"
        own = [{"path": name, "category": "world", "textures": [{"sha256": digest}]}
               for name, digest in (("a", "1"), ("b", "2"), ("c", "1"))]
        assert world_texture_duplicates(own) == {"a": ["c"], "c": ["a"]}
        assert not world_texture_duplicates([{**item, "category": "props"} for item in own])
        compressed = {"compress/mode": "2", "mipmaps/generate": "true", "process/size_limit": "1024", "compress/normal_map": "0"}
        assert texture_vram_bytes(2048, 2048, compressed, False) == 1024 * 1024 * 2 // 3
        assert texture_vram_bytes(1024, 512, {"compress/mode": "0", "mipmaps/generate": "false"}, False) == 1024 * 512 * 4
        sample = '[remap]\n\npath="res://.godot/imported/a.png-0f.ctex"\nmetadata={\n"vram_texture": false\n}\n\n[deps]\n\ndest_files=["res://.godot/imported/a.png-0f.ctex"]\n\n[params]\n\ncompress/mode=0\nprocess/size_limit=0\n'
        patched = patch_texture_import(sample, {"compress/mode": "2", "process/size_limit": "512"})
        assert 'path.s3tc="res://.godot/imported/a.png-0f.s3tc.ctex"\npath.etc2="res://.godot/imported/a.png-0f.etc2.ctex"\n' in patched
        assert "compress/mode=2\nprocess/size_limit=512\n" in patched and '"vram_texture": true' in patched
        assert patch_texture_import(patched, {"compress/mode": "2", "process/size_limit": "512"}) == patched
        print("audit_3d self-test: PASS"); return 0
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=True)
    models = sorted(path for path in (ROOT / "assets" / "models").rglob("*") if path.suffix.lower() in MODEL_EXTENSIONS)
    texts = repository_texts(); credits = (ROOT / "assets" / "CREDITS.md").read_text(encoding="utf-8", errors="replace")
    manifest = load_manifest(); records = []
    for path in models:
        record = inspect_gltf(path); record["import"] = parse_import(path); record["usage"] = usage_for(record["path"], texts)
        record["provenance"] = provenance_for(record, credits, manifest); records.append(record)

    metadata: dict[str, Any] = {"generated_utc": dt.datetime.now(dt.timezone.utc).isoformat(), "root": str(ROOT), "model_count": len(records), "blender": "not run", "godot": "not run"}
    if not args.static_only:
        blender = discover_executable("blender", [Path(r"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"), Path(r"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe")])
        if blender:
            blender_json = output / "blender-inspection.json"
            command = [str(blender), "--background", "--factory-startup", "--python", str(ROOT / "tools" / "blender_model_qa.py"), "--", "--root", str(ROOT), "--output", str(blender_json), "--render", args.render, "--render-size", str(args.render_size)]
            for asset in args.asset: command += ["--asset", asset]
            for pose in args.pose: command += ["--pose", pose]
            ok, log = run_external(command, timeout=7200); (output / "blender.log").write_text(log, encoding="utf-8")
            metadata["blender"] = f"{blender} ({'PASS' if ok else 'FAIL'})"
            by_path = load_json_if(blender_json).get("assets", {})
            for record in records: record["blender"] = by_path.get(record["path"], {})
        else: metadata["blender"] = "BLOCKED: executable not found"
        godot = discover_executable("godot", [Path(r"C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe")])
        if godot:
            godot_json = output / "godot-inspection.json"
            ok, log = run_external([str(godot), "--headless", "--path", str(ROOT), "--script", "res://tools/model_audit_probe.gd", "--", "--output", str(godot_json)], timeout=1800)
            (output / "godot.log").write_text(log, encoding="utf-8"); metadata["godot"] = f"{godot} ({'PASS' if ok else 'FAIL'})"
            by_path = load_json_if(godot_json).get("assets", {})
            for record in records: record["godot"] = by_path.get(record["path"], {})
        else: metadata["godot"] = "BLOCKED: executable not found"
    for record in records: record["flags"] = flags_for(record); record["recommendation"] = recommendation(record)
    hash_groups=defaultdict(list)
    for record in records: hash_groups[record["file_sha256"]].append(record["path"])
    for record in records:
        duplicates=hash_groups[record["file_sha256"]]
        if len(duplicates)>1:
            record["flags"].append({"code":"duplicate-payload","severity":"high","detail":"byte-identical to " + ", ".join(p for p in duplicates if p != record["path"])})
            record["recommendation"]="IMPROVE"
    geometry_groups=defaultdict(list)
    for record in records:
        geometry_hash=record.get("blender",{}).get("geometry_sha256")
        if geometry_hash: geometry_groups[geometry_hash].append(record["path"])
    for record in records:
        geometry_hash=record.get("blender",{}).get("geometry_sha256")
        duplicates=geometry_groups.get(geometry_hash, [])
        if geometry_hash and len(duplicates)>1 and len(hash_groups[record["file_sha256"]])==1:
            if {RECOLOURED_COPIES.get(p, p) for p in duplicates} == {RECOLOURED_COPIES.get(record["path"], record["path"])}:
                record["flags"].append({"code":"duplicate-geometry","severity":"info","detail":"declared recolour family: " + ", ".join(p for p in duplicates if p != record["path"])})
                continue
            severity="critical" if record["category"]=="creatures" else "high"
            record["flags"].append({"code":"duplicate-geometry","severity":severity,"detail":"evaluated geometry is identical to " + ", ".join(p for p in duplicates if p != record["path"])})
            record["recommendation"]="IMPROVE"
    duplicated = world_texture_duplicates(records)
    for record in records:
        if record["path"] in duplicated:
            record["flags"].append({"code":"duplicate-world-texture","severity":"critical","detail":"embeds an image byte-identical to one in " + ", ".join(duplicated[record["path"]])})
            record["recommendation"]="IMPROVE"
    write_reports(records, output, metadata)
    print(f"3D audit complete: {len(records)} assets, {sum(len(r['flags']) for r in records)} findings -> {output}")
    for path, others in sorted(duplicated.items()): print(f"FAIL {path}: embeds the same texture as {', '.join(others)}; keep one model and scale or tint the instance")
    return 0 if all(not r["errors"] for r in records) and not duplicated else 1


if __name__ == "__main__":
    raise SystemExit(main())
