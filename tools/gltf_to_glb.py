#!/usr/bin/env python3
"""Pack a `.gltf` + `.bin` + sidecar `.png`s into a single self-contained `.glb`.

Why this exists
---------------
The four MegaKits ship `.gltf` with a separate `.bin` and shared textures in the pack folder, while
`assets/models/` is `.glb` with images embedded. Adopting a model therefore needs a container
change and nothing else -- no geometry edit, no re-authoring.

Doing that in Blender would be a round-trip, and `ASSET_POLICY.md` records a round-trip as the thing
that destroys bone-parented children. These are static props so nothing is rigged, but the wider
reason still holds: a round-trip re-exports every vertex, normal and UV through another tool's
opinion of them, and this does not. **The buffer is copied byte for byte.** The only edits are the
ones a GLB container requires: sidecar URIs become bufferViews, and the single buffer loses its URI.

Usage
-----
    python tools/gltf_to_glb.py <source.gltf> <dest.glb> [--scale N]

`--scale` writes a uniform scale onto the scene's root nodes. Prefer it over `nodes/root_scale` in
the `.import` ONLY when the model is wrong in the file itself; a per-use size difference belongs in
the cell's transform. Neither is a substitute for measuring: run the model through the in-engine
measure, not a viewport and not this script's arithmetic.
"""

import json
import os
import struct
import sys


def pad(data: bytes, fill: bytes) -> bytes:
    """GLB requires every chunk to be 4-byte aligned."""
    return data + fill * (-len(data) % 4)


def pack(src: str, dest: str, scale: float | None = None) -> None:
    root = os.path.dirname(src)
    gltf = json.load(open(src, encoding="utf-8"))

    buffers = gltf.get("buffers", [])
    if len(buffers) != 1:
        raise SystemExit(f"expected exactly 1 buffer, found {len(buffers)} in {src}")

    blob = bytearray(open(os.path.join(root, buffers[0]["uri"]), "rb").read())
    del buffers[0]["uri"]

    # Each sidecar image becomes a bufferView appended to the same buffer. Appending (rather than
    # rebuilding) is what keeps every existing accessor offset valid without touching one of them.
    for image in gltf.get("images", []):
        uri = image.pop("uri", None)
        if uri is None:
            continue
        path = os.path.join(root, uri)
        if not os.path.isfile(path):
            raise SystemExit(f"missing texture {uri} referenced by {src}")
        raw = open(path, "rb").read()
        blob += b"\x00" * (-len(blob) % 4)
        gltf.setdefault("bufferViews", []).append(
            {"buffer": 0, "byteOffset": len(blob), "byteLength": len(raw)}
        )
        blob += raw
        image["bufferView"] = len(gltf["bufferViews"]) - 1
        image["mimeType"] = "image/png" if uri.lower().endswith(".png") else "image/jpeg"

    buffers[0]["byteLength"] = len(blob)

    if scale is not None:
        for index in gltf["scenes"][gltf.get("scene", 0)]["nodes"]:
            node = gltf["nodes"][index]
            if "matrix" in node:
                raise SystemExit(f"root node {index} uses a matrix; --scale would be ambiguous")
            have = node.get("scale", [1.0, 1.0, 1.0])
            node["scale"] = [v * scale for v in have]

    js = pad(json.dumps(gltf, separators=(",", ":")).encode("utf-8"), b" ")
    bn = pad(bytes(blob), b"\x00")
    body = (
        struct.pack("<II", len(js), 0x4E4F534A) + js
        + struct.pack("<II", len(bn), 0x004E4942) + bn
    )
    open(dest, "wb").write(struct.pack("<III", 0x46546C67, 2, 12 + len(body)) + body)
    print(f"{os.path.basename(src)} -> {dest}  ({len(bn) / 1024:.0f} KB binary)")


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    flags = {a.split("=")[0]: a.split("=")[1] for a in sys.argv[1:] if "=" in a and a.startswith("--")}
    if len(args) != 2:
        raise SystemExit(__doc__)
    pack(args[0], args[1], float(flags["--scale"]) if "--scale" in flags else None)
