#!/usr/bin/env python3
"""Correct legacy monolithic-building material response without re-exporting geometry.

The five retained building GLBs came from two approved CC0 packs and all inherited the same 0.4
metallic factor, including plaster, roof tile, timber and stone. This tool changes only glTF JSON
material factors. Geometry buffers and all structural payloads are verified byte-for-byte.
"""

from __future__ import annotations

import json
import struct
import sys
from pathlib import Path


BUILDINGS = (
    "bld_blacksmith.glb",
    "bld_cottage.glb",
    "bld_house_a.glb",
    "bld_house_b.glb",
    "bld_inn.glb",
)
STRUCTURAL_KEYS = ("accessors", "animations", "bufferViews", "meshes", "nodes", "skins")


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


def response(name: str) -> tuple[float, float]:
    key = name.lower()
    if "window" in key or "glass" in key:
        return 0.0, 0.24
    if "roof" in key or "tile" in key:
        return 0.0, 0.84
    if "wood" in key:
        return 0.0, 0.82
    if "plaster" in key or "wall" in key:
        return 0.0, 0.9
    if "stone" in key:
        return 0.0, 0.86
    return 0.0, 0.82


def main() -> None:
    root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path.cwd().resolve()
    folder = root / "assets" / "models" / "architecture"
    for filename in BUILDINGS:
        path = folder / filename
        document, binary = read_glb(path)
        structural = json.dumps({key: document.get(key) for key in STRUCTURAL_KEYS}, sort_keys=True)
        changed = 0
        for material in document.get("materials", []):
            pbr = material.setdefault("pbrMetallicRoughness", {})
            metallic, roughness = response(material.get("name", ""))
            if pbr.get("metallicFactor") != metallic or pbr.get("roughnessFactor") != roughness:
                pbr["metallicFactor"] = metallic
                pbr["roughnessFactor"] = roughness
                changed += 1
        write_glb(path, document, binary)
        verify, verify_binary = read_glb(path)
        verified_structural = json.dumps({key: verify.get(key) for key in STRUCTURAL_KEYS}, sort_keys=True)
        if verify_binary != binary or verified_structural != structural:
            raise RuntimeError(f"material repair altered structural payload: {filename}")
        print(f"{filename}: corrected {changed} material(s); geometry payload preserved")


if __name__ == "__main__":
    main()
