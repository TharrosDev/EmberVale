#!/usr/bin/env python3
"""Correct legacy material response without re-exporting geometry.

The five retained building GLBs came from two approved CC0 packs and all inherited the same 0.4
metallic factor, including plaster, roof tile, timber and stone. This tool changes only glTF JSON
material factors. Geometry buffers and all structural payloads are verified byte-for-byte.

⚠️ THE SAME 0.4 IS ON 121 PROP MATERIALS AND IT IS THE WHOLE ENVIRONMENT (Session 6). Wood, hay,
sacking, bark, leaves, cooking soup and fire all ship at 40% metallic with no metallic texture,
which `docs/ART_STYLE.md` forbids outright — the entire dressed world carries a faint sheen no
hand-painted surface should have, and it is the "excessive gloss" the visual-QA checklist names.

⚠️ AND IT IS WRONG IN BOTH DIRECTIONS, WHICH IS WHY 0.4 SURVIVED SO LONG. It is a compromise
value: too high for wood and too low for iron, so nothing looks obviously broken and everything
looks slightly off. Correcting only the wood leaves the brazier's ironwork and the relic's gold
reading as painted plastic. `response()` therefore drives real metal UP as well as everything
else DOWN.

⚠️ A MATERIAL WITH A metallicRoughnessTexture IS NOT THIS DEFECT AND MUST BE LEFT ALONE. The
shared-texture props (`prp_workbench.gltf` and its twenty siblings) carry real ORM maps; a
factor written over one of those overrides a map that was authored correctly.

    python tools/repair_architecture_materials.py [repo_root]
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
    """The (metallic, roughness) a material of this name should have.

    Ordered most specific first. Metal is tested before wood so `Metal_Light` does not fall
    through to the wood branch, and before stone so an ore seam's gold is not read as rock.
    """
    key = name.lower()
    # Real metal, driven UP. Cold worked iron in a dying world: metallic, but never a mirror.
    if "gold" in key:
        return 1.0, 0.30
    if "bell" in key:
        return 1.0, 0.38
    if "metal" in key:
        return 1.0, 0.45
    if "window" in key or "glass" in key:
        return 0.0, 0.24
    if "roof" in key or "tile" in key:
        return 0.0, 0.84
    if "wood" in key or "bark" in key or "timber" in key:
        return 0.0, 0.82
    if "plaster" in key or "wall" in key:
        return 0.0, 0.9
    if "stone" in key or "rock" in key or "marble" in key:
        return 0.0, 0.86
    # Soft and fibrous: hay, sacking, cloth, leather, foliage. These read worst at 0.4 metallic
    # because a matte surface with a specular sheen has no real-world referent at all.
    if any(part in key for part in ("hay", "bag", "fabric", "cloth", "leather", "leaves", "flower")):
        return 0.0, 0.95
    # Fire, embers and cooking liquid are emissive/wet, not metal; keep them non-metallic and
    # let the material's own albedo and any VFX carry them.
    if "fire" in key or "soup" in key:
        return 0.0, 0.55
    return 0.0, 0.82


def repair(path: Path) -> int:
    """Rewrite one GLB's material factors in place. Returns how many it corrected."""
    document, binary = read_glb(path)
    structural = json.dumps({key: document.get(key) for key in STRUCTURAL_KEYS}, sort_keys=True)
    changed = 0
    for material in document.get("materials", []):
        pbr = material.setdefault("pbrMetallicRoughness", {})
        if "metallicRoughnessTexture" in pbr:
            continue  # an authored ORM map owns this surface; a factor would override it
        metallic, roughness = response(material.get("name", ""))
        if pbr.get("metallicFactor") != metallic or pbr.get("roughnessFactor") != roughness:
            pbr["metallicFactor"] = metallic
            pbr["roughnessFactor"] = roughness
            changed += 1
    if changed == 0:
        return 0
    write_glb(path, document, binary)
    verify, verify_binary = read_glb(path)
    verified = json.dumps({key: verify.get(key) for key in STRUCTURAL_KEYS}, sort_keys=True)
    if verify_binary != binary or verified != structural:
        raise RuntimeError(f"material repair altered structural payload: {path.name}")
    return changed


def main() -> None:
    root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path.cwd().resolve()
    models = root / "assets" / "models"

    for filename in BUILDINGS:
        changed = repair(models / "architecture" / filename)
        print(f"{filename}: corrected {changed} material(s); geometry payload preserved")

    # Props are swept by folder rather than listed: the defect is the pack default, so every
    # GLB adopted from those packs has it and a hand-kept list would go stale on the next
    # adoption. The `.gltf` props are untouched — their ORM maps are caught by repair().
    total = 0
    for path in sorted((models / "props").glob("*.glb")):
        changed = repair(path)
        total += changed
        if changed:
            print(f"{path.name}: corrected {changed} material(s); geometry payload preserved")
    print(f"props: {total} material(s) corrected across the folder")


if __name__ == "__main__":
    main()
