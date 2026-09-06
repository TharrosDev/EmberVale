#!/usr/bin/env python3
"""Pull an off-palette colour cast out of a character's baked texture, without touching geometry.

Why this exists
---------------
`npc_hooded` shipped with a KNOWN DEFECT the provenance ledger records: "the cloak drifted
green-teal off palette". `docs/3D_ASSETS.md` sanctions exactly one edit to an existing human GLB --
"JSON material corrections only" -- and that is not enough here, because **the cast is in the
texture, not in a material factor.** The body has ONE material with a base-colour texture and a
white `baseColorFactor`; the only factor-level lever is that white multiplier, and pulling the green
out of the cloak with it would drag the face and the leather down with it.

So the correction has to be in the image, and this is the repeatable script `docs/3D_ASSETS.md`
requires for that ("never adapt anything except through a repeatable script").

What it changes, and what it deliberately does not
--------------------------------------------------
Measured on `npc_hooded_texture_0.png`: 70% of the pixels sit at hue 15-60 (the warm browns, the
leather and the skin, at saturation 0.24-0.30), and a second band at hue 75-150 carries saturation
0.07-0.09 -- a desaturated grey with a green cast. That second band IS the cloak, and the prompt
stem this cast violates is explicit: "Muted desaturated ash-grey and faded-earth-brown palette ...
one small ember-orange accent". Green is off-palette by construction, which is what makes a hue
window a safe selector here rather than a guess.

The transform is therefore narrow on purpose:

    a pixel is corrected only if its hue is inside the cool window AND its saturation is already
    low. Its VALUE is never touched, so every fold, seam and shadow the texture paints survives.

⚠️ **The saturation ceiling is the load-bearing half.** Without it the window would also catch any
genuinely saturated colour that happened to fall in it. With it, the rule reads as what it actually
is: "a surface that is nearly grey already, and is grey in the wrong direction".

⚠️ **The ember-orange accent is safe and must stay that way.** It sits at hue 20-35, nowhere near
the window. Widening the window far enough to reach it would take the one warm colour out of the
palette.

Usage
-----
    python tools/neutralize_palette_cast.py assets/models/characters/npc_hooded.glb
    python tools/neutralize_palette_cast.py <asset.glb> --dry-run

The GLB's embedded PNG is rewritten in place, and any identical sibling `<stem>_texture_N.png` (the
copy Godot's importer extracts) is rewritten with it so the two cannot drift.
"""

from __future__ import annotations

import argparse
import colorsys
import io
import json
import struct
import sys
from pathlib import Path

from PIL import Image

# The cool window, in degrees. Green through cyan: off-palette for this game by construction.
HUE_LOW, HUE_HIGH = 75.0, 200.0

# Only a surface that is ALREADY nearly grey is treated as a cast rather than as a colour.
MAX_SATURATION = 0.25

# What a corrected pixel becomes: the warm neutral the palette is built on, at a fraction of the
# saturation it had. Not zero -- a perfectly grey cloak reads as untextured plastic next to leather
# that still has some colour in it.
TARGET_HUE = 32.0 / 360.0
SATURATION_KEPT = 0.35

STRUCTURAL_KEYS = ("accessors", "animations", "meshes", "nodes", "skins")


def read_glb(path: Path) -> tuple[dict, bytes]:
    raw = path.read_bytes()
    if raw[:4] != b"glTF":
        raise SystemExit(f"{path} is not a glTF binary container")
    document, binary = None, b""
    offset = 12
    while offset < len(raw):
        size, kind = struct.unpack_from("<II", raw, offset)
        payload = raw[offset + 8:offset + 8 + size]
        if kind == 0x4E4F534A:
            document = json.loads(payload.rstrip(b" \0"))
        elif kind == 0x004E4942:
            binary = payload
        offset += 8 + size
    if document is None:
        raise SystemExit(f"{path} has no JSON chunk")
    return document, binary


def write_glb(path: Path, document: dict, binary: bytes) -> None:
    js = json.dumps(document, separators=(",", ":")).encode("utf-8")
    js += b" " * (-len(js) % 4)
    bn = binary + b"\x00" * (-len(binary) % 4)
    body = (struct.pack("<II", len(js), 0x4E4F534A) + js
            + struct.pack("<II", len(bn), 0x004E4942) + bn)
    path.write_bytes(struct.pack("<III", 0x46546C67, 2, 12 + len(body)) + body)


def neutralize(png: bytes) -> tuple[bytes, int, int]:
    """Return (new png bytes, corrected pixel count, total pixel count)."""
    image = Image.open(io.BytesIO(png))
    mode = image.mode if image.mode in ("RGB", "RGBA") else "RGB"
    image = image.convert(mode)
    alpha = image.getchannel("A") if mode == "RGBA" else None
    rgb = image.convert("RGB")

    pixels = list(rgb.get_flattened_data()) if hasattr(rgb, "get_flattened_data") else list(rgb.getdata())
    corrected = 0
    for index, (r, g, b) in enumerate(pixels):
        h, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
        if s > MAX_SATURATION or not (HUE_LOW <= h * 360.0 <= HUE_HIGH):
            continue
        nr, ng, nb = colorsys.hsv_to_rgb(TARGET_HUE, s * SATURATION_KEPT, v)
        pixels[index] = (round(nr * 255), round(ng * 255), round(nb * 255))
        corrected += 1

    out = Image.new("RGB", rgb.size)
    out.putdata(pixels)
    if alpha is not None:
        out = out.convert("RGBA")
        out.putalpha(alpha)

    buffer = io.BytesIO()
    out.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue(), corrected, len(pixels)


def process(path: Path, dry_run: bool) -> int:
    document, binary = read_glb(path)
    images = document.get("images", [])
    if len(images) != 1 or "bufferView" not in images[0]:
        raise SystemExit(f"{path.name}: expected exactly one embedded image, found {len(images)}")

    views = document["bufferViews"]
    image_index = images[0]["bufferView"]
    view = views[image_index]

    # ⚠️ The image is NOT the last region of the buffer -- 75 animation tracks follow it -- so the
    # new PNG cannot simply be appended. The buffer is repacked instead, which is only safe because
    # of what is asserted here: the views are contiguous, in offset order, and never overlap. An
    # ACCESSOR addresses its view by an offset WITHIN the view, so moving whole views is transparent
    # to every one of them; a byteStride or an overlap would not be, which is why both are refused.
    cursor = 0
    for index, other in enumerate(views):
        if other.get("byteStride") is not None:
            raise SystemExit(f"{path.name}: bufferView {index} has a byteStride; repacking is not safe")
        if other.get("byteOffset", 0) < cursor:
            raise SystemExit(f"{path.name}: bufferView {index} overlaps its predecessor; "
                             "repacking is not safe")
        cursor = other.get("byteOffset", 0) + other["byteLength"]

    new_png, corrected, total = neutralize(binary[view.get("byteOffset", 0):
                                                  view.get("byteOffset", 0) + view["byteLength"]])
    share = 100.0 * corrected / total if total else 0.0
    print(f"{path.name}: {corrected}/{total} pixels ({share:.1f}%) pulled off the cool cast")

    if dry_run:
        return 0

    structural = json.dumps({k: document.get(k) for k in STRUCTURAL_KEYS}, sort_keys=True)
    payloads = [new_png if index == image_index
                else binary[other.get("byteOffset", 0):
                            other.get("byteOffset", 0) + other["byteLength"]]
                for index, other in enumerate(views)]

    repacked = bytearray()
    for index, other in enumerate(views):
        repacked += b"\x00" * (-len(repacked) % 4)
        other["byteOffset"] = len(repacked)
        other["byteLength"] = len(payloads[index])
        repacked += payloads[index]

    document["buffers"][0]["byteLength"] = len(repacked)
    write_glb(path, document, bytes(repacked))

    verify, verify_binary = read_glb(path)
    if json.dumps({k: verify.get(k) for k in STRUCTURAL_KEYS}, sort_keys=True) != structural:
        raise RuntimeError(f"{path.name}: structural payload changed; the asset is not safe")
    for index, other in enumerate(verify["bufferViews"]):
        start = other.get("byteOffset", 0)
        if verify_binary[start:start + other["byteLength"]] != payloads[index]:
            raise RuntimeError(f"{path.name}: bufferView {index} did not survive the repack")

    # Godot's importer extracts the embedded texture beside the model. Rewriting the copy keeps the
    # two from drifting -- a stale extracted PNG is what the engine actually renders.
    for sibling in sorted(path.parent.glob(f"{path.stem}_texture_*.png")):
        sibling.write_bytes(new_png)
        print(f"  {sibling.name}: rewritten to match the embedded texture")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("asset", help="the .glb whose embedded base-colour texture to correct")
    parser.add_argument("--dry-run", action="store_true",
                        help="report how much would change and write nothing")
    args = parser.parse_args()
    return process(Path(args.asset), args.dry_run)


for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")


if __name__ == "__main__":
    raise SystemExit(main())
