#!/usr/bin/env python3
"""Correct human GLB material response.

  python tools/patch_human_materials.py [repo-root] [--check]

Existing humanoid GLBs are never imported or re-exported. Only their glTF JSON material factors are
changed; buffers holding geometry, weights, bones and animation remain byte-identical. `--check`
reports what would change and writes nothing.
"""

from __future__ import annotations

import json
import struct
import sys
from pathlib import Path


HUMANS = (
    "npc_adventurer_f.glb", "npc_guild_rep.glb", "npc_hooded.glb", "npc_innkeeper.glb",
    "npc_kael.glb", "npc_merchant_f.glb", "npc_merchant_m.glb", "npc_townsman.glb",
    "npc_townswoman.glb", "npc_vendor.glb", "npc_woman_dress.glb",
)


def read_glb(path: Path) -> tuple[dict, bytes]:
    raw = path.read_bytes()
    if raw[:4] != b"glTF":
        raise ValueError(f"{path} is not GLB")
    _, version, length = struct.unpack_from("<III", raw, 0)
    if version != 2 or length != len(raw):
        raise ValueError(f"invalid GLB header in {path}")
    document = None
    binary = b""
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


def material_response(name: str) -> tuple[float, float]:
    key = name.lower()
    if "gold" in key:
        return 0.9, 0.32
    if "metal" in key:
        return 0.86, 0.42
    if any(word in key for word in ("skin",)):
        return 0.0, 0.68
    if any(word in key for word in ("eye", "pupil")):
        return 0.0, 0.48
    if any(word in key for word in ("hair", "brow", "moustache")):
        return 0.0, 0.8
    if any(word in key for word in ("boot", "shoe", "brown", "leather")):
        return 0.0, 0.72
    return 0.0, 0.86


def patch_humans(root: Path, write: bool) -> None:
    folder = root / "assets" / "models" / "characters"
    for filename in HUMANS:
        path = folder / filename
        if not path.exists():
            print(f"material patch: {filename}: not in the tree, skipped")
            continue
        document, binary = read_glb(path)
        structural = json.dumps({key: document.get(key) for key in
            ("accessors", "animations", "bufferViews", "meshes", "nodes", "skins")}, sort_keys=True)
        changed = 0
        for material in document.get("materials", []):
            pbr = material.setdefault("pbrMetallicRoughness", {})
            metallic, roughness = material_response(material.get("name", ""))
            if pbr.get("metallicFactor") != metallic or pbr.get("roughnessFactor") != roughness:
                pbr["metallicFactor"] = metallic
                pbr["roughnessFactor"] = roughness
                changed += 1
            if filename == "npc_townsman.glb":
                if material.get("name") == "Worker_Yellow":
                    pbr["baseColorFactor"] = [0.18, 0.12, 0.035, 1.0]
                elif material.get("name") == "Worker_Vest":
                    pbr["baseColorFactor"] = [0.20, 0.055, 0.025, 1.0]
        if not write:
            print(f"material patch: {filename}: {changed} material(s) would change")
            continue
        write_glb(path, document, binary)
        verify, verify_binary = read_glb(path)
        verified_structural = json.dumps({key: verify.get(key) for key in
            ("accessors", "animations", "bufferViews", "meshes", "nodes", "skins")}, sort_keys=True)
        if verify_binary != binary or verified_structural != structural:
            raise RuntimeError(f"material patch altered structural payload: {filename}")
        print(f"material patch: {filename}: {changed} material(s), rig payload preserved")


def main() -> None:
    args = [arg for arg in sys.argv[1:] if arg != "--check"]
    root = Path(args[0]).resolve() if args else Path(__file__).resolve().parent.parent
    patch_humans(root, write="--check" not in sys.argv)


if __name__ == "__main__":
    main()
