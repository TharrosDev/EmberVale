#!/usr/bin/env python3
"""Adopt a MegaKit model into `assets/models/`, in whichever of the two shapes fits.

Why this exists
---------------
The four MegaKits ship `.gltf` with a separate `.bin` and shared textures in the pack folder, while
`assets/models/` wants self-contained files. Adopting a model is therefore a CONTAINER change and
nothing else -- no geometry edit, no re-authoring.

Doing that in Blender would be a round-trip, and `ASSET_POLICY.md` records a round-trip as the thing
that destroys bone-parented children. These are static props so nothing is rigged, but the wider
reason still holds: a round-trip re-exports every vertex, normal and UV through another tool's
opinion of them, and this does not. **The buffer is copied byte for byte.**

Two modes, and picking the wrong one is expensive
-------------------------------------------------
    embed   (default) one self-contained `.glb` with its textures inside. Right for a prop that
            owns its textures -- the nature megakit's grass and rocks are 0.8-3 MB each.

    shared  copy the `.gltf` + `.bin` and the textures ALONGSIDE, so many models reference one set.
            Right for a modular kit. ⚠️ The medieval megakit's wall modules carry ~0 MB of geometry
            and SIX shared PBR maps totalling ~17 MB, so embedding twelve of them cost 204 MB of
            the same textures twelve times over. Shared, it is ~45 MB once.

Usage
-----
    python tools/adopt_kit_model.py <source.gltf> <dest.glb> [--scale N]
    python tools/adopt_kit_model.py <source.gltf> <dest.gltf> --shared

`--scale` writes a uniform scale onto the scene's root nodes (embed mode only). Prefer it over
`nodes/root_scale` in the `.import` ONLY when the model is wrong in the file itself; a per-use size
difference belongs in the cell's transform. Neither is a substitute for measuring: measure the
IMPORTED scene in-engine -- accessor bounds ignore node scale and will lie to you.
"""

import json
import argparse
import os
import shutil
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


def share(src, dest):
    """Copy the model and its buffer next to a texture set the whole kit shares.

    Only two things are rewritten: the buffer URI (the `.bin` is renamed with the model) and
    nothing else -- image URIs are left pointing at the plain texture names, so every module in
    the kit resolves to the SAME imported texture. Godot imports a `.gltf` with external images
    exactly as happily as a `.glb`, and imports each PNG once.
    """
    root = os.path.dirname(src)
    out = os.path.dirname(dest)
    stem = os.path.splitext(os.path.basename(dest))[0]
    gltf = json.load(open(src, encoding="utf-8"))

    if len(gltf.get("buffers", [])) != 1:
        raise SystemExit(f"expected exactly 1 buffer in {src}")
    shutil.copyfile(os.path.join(root, gltf["buffers"][0]["uri"]), os.path.join(out, stem + ".bin"))
    gltf["buffers"][0]["uri"] = stem + ".bin"

    shared = 0
    for image in gltf.get("images", []):
        uri = image.get("uri")
        if uri is None:
            continue
        target = os.path.join(out, uri)
        if not os.path.isfile(target):
            shutil.copyfile(os.path.join(root, uri), target)
            shared += 1

    json.dump(gltf, open(dest, "w", encoding="utf-8"), separators=(",", ":"))
    print(f"{os.path.basename(src)} -> {dest}  (+{shared} new shared texture(s))")


# Windows consoles default to cp1252, and every tool here prints the repo's warning glyphs. Without
# this a plain `--help` dies with UnicodeEncodeError before it prints anything useful.
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", help="the pack .gltf to adopt")
    parser.add_argument("dest", help="destination under assets/models/")
    parser.add_argument("--scale", type=float, default=None,
                        help="nodes/root_scale correction to write into the .import")
    parser.add_argument("--shared", action="store_true",
                        help="copy .gltf/.bin/textures alongside so many models share one set")
    options = parser.parse_args()
    if options.shared:
        share(options.source, options.dest)
    else:
        pack(options.source, options.dest, options.scale)
