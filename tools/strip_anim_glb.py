#!/usr/bin/env python3
"""Strip the body out of a Meshy animation .glb, leaving the armature and its clips.

⚠️ WHY THIS EXISTS. `meshy_animate` returns a whole animated CHARACTER — the full skinned mesh and
its 2048x2048 albedo — once per clip. A 24-clip set is 190 MB of the same townsman standing in 24
poses, and `meshy_adopt.py`'s texture cap only gets that to 39 MB. None of it is wanted: the shared
animation library is built from the CLIPS, and the bodies that play them are already in the repo.
This repo has paid for that mistake once already, when twelve wall modules shipped 204 MB of the same
textures twelve times over (docs/3D_ASSETS.md).

What is kept, and why each is load-bearing:

  * every node, so the joint hierarchy the retarget walks is intact;
  * `skins`, because Godot builds its Skeleton3D from `skins[].joints` — drop this and there is no
    skeleton to retarget onto and the clips address nothing;
  * `animations`, the point of the exercise;
  * only the accessors those two reference, and only the bufferViews those accessors reference.

What goes: meshes, materials, textures, images, samplers, and the `mesh` key on every node.

    python tools/strip_anim_glb.py <source.glb> <dest.glb>
"""
from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path

JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942
MAGIC = 0x46546C67


def read_glb(path: Path) -> tuple[dict, bytes]:
    data = path.read_bytes()
    magic, _version, _length = struct.unpack_from("<III", data, 0)
    if magic != MAGIC:
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
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as out:
        out.write(struct.pack("<III", MAGIC, 2, total))
        out.write(struct.pack("<II", len(doc_bytes), JSON_CHUNK))
        out.write(doc_bytes)
        out.write(struct.pack("<II", len(binary), BIN_CHUNK))
        out.write(binary)


def strip(doc: dict, binary: bytes) -> tuple[dict, bytes]:
    # 1. Which accessors survive: every animation sampler's input and output, plus each skin's
    #    inverse bind matrices.
    keep_accessors: set[int] = set()
    for animation in doc.get("animations", []):
        for sampler in animation.get("samplers", []):
            keep_accessors.add(sampler["input"])
            keep_accessors.add(sampler["output"])
    for skin in doc.get("skins", []):
        if "inverseBindMatrices" in skin:
            keep_accessors.add(skin["inverseBindMatrices"])

    # 2. Renumber the accessors, keeping their original order so the file stays diffable.
    order = sorted(keep_accessors)
    accessor_remap = {old: new for new, old in enumerate(order)}
    accessors = [doc["accessors"][i] for i in order]

    # 3. Which bufferViews those accessors need, renumbered the same way.
    keep_views = sorted({a["bufferView"] for a in accessors if "bufferView" in a})
    view_remap = {old: new for new, old in enumerate(keep_views)}

    out = bytearray()
    views = []
    for old in keep_views:
        view = dict(doc["bufferViews"][old])
        start = view.get("byteOffset", 0)
        payload = binary[start : start + view["byteLength"]]
        out += b"\x00" * (-len(out) % 4)
        view["byteOffset"] = len(out)
        view["byteLength"] = len(payload)
        view["buffer"] = 0
        # A byteStride belongs to vertex attributes; nothing surviving here is one, and leaving a
        # stride on a tightly-packed animation accessor makes Godot read past the end of it.
        view.pop("byteStride", None)
        views.append(view)
        out += payload

    for accessor in accessors:
        if "bufferView" in accessor:
            accessor["bufferView"] = view_remap[accessor["bufferView"]]

    for animation in doc.get("animations", []):
        for sampler in animation.get("samplers", []):
            sampler["input"] = accessor_remap[sampler["input"]]
            sampler["output"] = accessor_remap[sampler["output"]]
    for skin in doc.get("skins", []):
        if "inverseBindMatrices" in skin:
            skin["inverseBindMatrices"] = accessor_remap[skin["inverseBindMatrices"]]

    # 4. The body itself. Nodes keep their transforms and their skin, and lose their geometry.
    for node in doc.get("nodes", []):
        node.pop("mesh", None)

    for key in ("meshes", "materials", "textures", "images", "samplers"):
        doc.pop(key, None)

    doc["accessors"] = accessors
    doc["bufferViews"] = views
    doc["buffers"] = [{"byteLength": len(out)}]
    return doc, bytes(out)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("dest", type=Path)
    args = parser.parse_args()

    doc, binary = read_glb(args.source)
    before = args.source.stat().st_size
    clips = [a.get("name", "?") for a in doc.get("animations", [])]
    joints = len(doc.get("nodes", []))

    doc, binary = strip(doc, binary)
    write_glb(args.dest, doc, binary)
    after = args.dest.stat().st_size

    print(f"{args.source.name} -> {args.dest.name}: "
          f"{before // 1024} KB -> {after // 1024} KB "
          f"({joints} nodes, clips {clips})")
    if not clips:
        print("  WARNING: no animations survived — this file is useless to the library", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
