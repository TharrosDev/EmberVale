#!/usr/bin/env python3
"""Adopt a Meshy .glb export into assets/models/ at a production texture budget.

Meshy ships one 2048x2048 embedded albedo per character. At ~5.6 MB of VRAM each that is
235 MB across a 42-character roster, so every adopted model is repacked with its images
capped (default 1024). Geometry, skin, and animation data are copied through byte-for-byte;
only image bufferViews change, and the buffer is rebuilt compactly so the old bytes go away.

    python tools/meshy_adopt.py <source.glb> <dest.glb> [--max-texture 1024]
"""
from __future__ import annotations

import argparse
import io
import json
import re
import struct
from pathlib import Path

from PIL import Image

JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942


def read_glb(path: Path) -> tuple[dict, bytes]:
    data = path.read_bytes()
    magic, _version, _length = struct.unpack_from("<III", data, 0)
    if magic != 0x46546C67:
        raise ValueError(f"{path} is not a binary glTF")
    offset, doc, binary = 12, None, b""
    while offset < len(data):
        chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
        body = data[offset + 8 : offset + 8 + chunk_len]
        if chunk_type == JSON_CHUNK:
            doc = json.loads(body)
        elif chunk_type == BIN_CHUNK:
            binary = body
        offset += 8 + chunk_len + (-chunk_len % 4)
    if doc is None:
        raise ValueError(f"{path} has no JSON chunk")
    return doc, binary


def write_glb(path: Path, doc: dict, binary: bytes) -> None:
    doc_bytes = json.dumps(doc, separators=(",", ":")).encode("utf-8")
    doc_bytes += b" " * (-len(doc_bytes) % 4)
    binary += b"\x00" * (-len(binary) % 4)
    total = 12 + 8 + len(doc_bytes) + 8 + len(binary)
    with path.open("wb") as out:
        out.write(struct.pack("<III", 0x46546C67, 2, total))
        out.write(struct.pack("<II", len(doc_bytes), JSON_CHUNK))
        out.write(doc_bytes)
        out.write(struct.pack("<II", len(binary), BIN_CHUNK))
        out.write(binary)


def shrink_images(doc: dict, binary: bytes, max_size: int) -> dict[int, bytes]:
    """Return {bufferView index: replacement bytes} for every oversized embedded image."""
    replacements: dict[int, bytes] = {}
    for image in doc.get("images", []):
        view_index = image.get("bufferView")
        if view_index is None:
            continue
        view = doc["bufferViews"][view_index]
        start = view.get("byteOffset", 0)
        raw = binary[start : start + view["byteLength"]]
        with Image.open(io.BytesIO(raw)) as source:
            if max(source.size) <= max_size:
                continue
            scale = max_size / max(source.size)
            size = (max(1, round(source.width * scale)), max(1, round(source.height * scale)))
            resized = source.convert("RGBA" if "A" in source.getbands() else "RGB")
            resized = resized.resize(size, Image.LANCZOS)
        buffer = io.BytesIO()
        resized.save(buffer, format="PNG", optimize=True)
        replacements[view_index] = buffer.getvalue()
        image["mimeType"] = "image/png"
    return replacements


def rebuild_buffer(doc: dict, binary: bytes, replacements: dict[int, bytes]) -> bytes:
    """Repack every bufferView in order, substituting the shrunk images."""
    out = bytearray()
    for index, view in enumerate(doc["bufferViews"]):
        if index in replacements:
            payload = replacements[index]
        else:
            start = view.get("byteOffset", 0)
            payload = binary[start : start + view["byteLength"]]
        # byteStride'd views feed vertex attributes; keep 4-byte alignment for all of them.
        out += b"\x00" * (-len(out) % 4)
        view["byteOffset"] = len(out)
        view["byteLength"] = len(payload)
        out += payload
    doc["buffers"] = [{"byteLength": len(out)}]
    return bytes(out)


RETARGET_BLOCK = """_subresources={
"nodes": {
"PATH:Armature/Skeleton3D": {
"retarget/bone_map": Resource("res://assets/models/animations/bonemap_meshy.tres"),
"retarget/bone_renamer/rename_bones": true,
"retarget/bone_renamer/unique_node/make_unique": true,
"retarget/bone_renamer/unique_node/skeleton_name": "GeneralSkeleton",
"retarget/remove_tracks/except_bone_transform": false,
"retarget/remove_tracks/unimportant_positions": true,
"retarget/remove_tracks/unmapped_bones": false,
"retarget/rest_fixer/apply_node_transforms": true,
"retarget/rest_fixer/fix_silhouette/enable": false,
"retarget/rest_fixer/fix_silhouette/filter": [],
"retarget/rest_fixer/fix_silhouette/threshold": 15.0,
"retarget/rest_fixer/keep_global_rest_on_leftovers": true,
"retarget/rest_fixer/normalize_position_tracks": true,
"retarget/rest_fixer/overwrite_axis": true,
"retarget/rest_fixer/reset_all_bone_poses_after_import": true,
"retarget/rest_fixer/retarget_global_rest": false
}
}
}"""


def patch_import(dest: Path, root_scale: float | None) -> str:
    """Point the model's .import at bonemap_meshy so the 46-clip shared library attaches.

    The _subresources key is a node path into the imported scene that starts at the scene root's
    FIRST CHILD, not at the root -- Godot names the root after the file, so anchoring on the root
    never matches. Getting it wrong is silent: the model imports fine, keeps its raw 24 Meshy bone
    names, never matches "GeneralSkeleton", and the actor T-poses with no log and no error.
    tools/meshy_rig_probe.gd is the gate that turns that into a hard failure.
    """
    sidecar = dest.with_suffix(dest.suffix + ".import")
    if not sidecar.exists():
        return "no .import yet (run a Godot import pass, then re-run with --patch-import)"
    text = sidecar.read_text(encoding="utf-8")
    block = RETARGET_BLOCK
    # _subresources is always the last multi-line param before the gltf/ block, and it appears both
    # as "{}" and as a nested dict -- anchoring on what follows it covers each form with one rule.
    text, count = re.subn(r"_subresources=.*?(?=\ngltf/naming_version)",
                          block, text, count=1, flags=re.S)
    if count != 1:
        return f"FAILED to locate _subresources in {sidecar.name}"
    # 1 = extract the embedded albedo to a sidecar .png, which then gets VRAM-compressed by its own
    # importer. Mode 3 (embed uncompressed) ships the texture straight to VRAM at full cost, and
    # boss_iron_king inherited it from its predecessor -- normalise so the roster is consistent.
    text = re.sub(r"gltf/embedded_image_handling=\d+", "gltf/embedded_image_handling=1", text, count=1)
    if root_scale is not None:
        text = re.sub(r"nodes/root_scale=[0-9.]+", f"nodes/root_scale={root_scale}", text, count=1)
    sidecar.write_text(text, encoding="utf-8")
    return f"patched {sidecar.name} (PATH:Armature/Skeleton3D)"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("dest", type=Path)
    parser.add_argument("--max-texture", type=int, default=1024)
    parser.add_argument("--root-scale", type=float, default=None,
                        help="override nodes/root_scale in the .import")
    parser.add_argument("--patch-import", action="store_true",
                        help="point the sibling .import at bonemap_meshy.tres")
    args = parser.parse_args()

    doc, binary = read_glb(args.source)
    replacements = shrink_images(doc, binary, args.max_texture)
    binary = rebuild_buffer(doc, binary, replacements)
    args.dest.parent.mkdir(parents=True, exist_ok=True)
    write_glb(args.dest, doc, binary)

    before = args.source.stat().st_size
    after = args.dest.stat().st_size
    print(f"{args.source.name} -> {args.dest} : {before//1024} KB -> {after//1024} KB "
          f"({len(replacements)} texture(s) capped at {args.max_texture})")
    if args.patch_import:
        print("  " + patch_import(args.dest, args.root_scale))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
