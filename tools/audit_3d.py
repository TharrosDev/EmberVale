#!/usr/bin/env python3
"""Permanent Embervale 3D inventory, audit, and report generator.

This tool never modifies a production asset. It combines byte-level glTF inspection,
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
    "creatures": ("enm_", "boss_", "mnt_"), "props": "prp_", "weapons": "wpn_",
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
    result["subresource_count"] = len(re.findall(r"(?m)^\w.+?=\{", text))
    return result


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


def repository_texts() -> dict[str, str]:
    result = {}
    for path in ROOT.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in TEXT_EXTENSIONS or any(part in IGNORED_PARTS for part in path.parts):
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
    found = shutil.which(name)
    if found:
        return Path(found)
    return next((path for path in candidates if path.is_file()), None)


def run_external(command: list[str], timeout: int) -> tuple[bool, str]:
    try:
        completed = subprocess.run(command, cwd=ROOT, text=True, stdout=subprocess.PIPE,
                                   stderr=subprocess.STDOUT, timeout=timeout, check=False)
        return completed.returncode == 0, completed.stdout
    except (OSError, subprocess.TimeoutExpired) as exc:
        return False, str(exc)


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
            severity="critical" if record["category"]=="creatures" else "high"
            record["flags"].append({"code":"duplicate-geometry","severity":severity,"detail":"evaluated geometry is identical to " + ", ".join(p for p in duplicates if p != record["path"])})
            record["recommendation"]="IMPROVE"
    write_reports(records, output, metadata)
    print(f"3D audit complete: {len(records)} assets, {sum(len(r['flags']) for r in records)} findings -> {output}")
    return 0 if all(not r["errors"] for r in records) else 1


if __name__ == "__main__":
    raise SystemExit(main())
