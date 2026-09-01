#!/usr/bin/env python3
"""Deterministic integrity gate for Embervale's authored architecture kit."""

from __future__ import annotations

import json
import re
import struct
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
PREFABS = {
    "bld_cottage_shuttered.tscn": (1, "solid"),
    "bld_farmhouse_long.tscn": (1, "solid"),
    "bld_shop_awning.tscn": (1, "solid"),
    "bld_townhouse_balcony.tscn": (1, "solid"),
    "bld_workshop_open.tscn": (9, "open"),
    "bld_longhouse_stone.tscn": (1, "solid"),
    "bld_townhouse_wide.tscn": (1, "solid"),
    "bld_inn_courtyard.tscn": (1, "solid"),
    "bld_ruin_house.tscn": (3, "ruined"),
    "bld_ruin_tower.tscn": (3, "ruined"),
    "bld_ashfall_house.tscn": (14, "enterable"),
}
LIVE_EXPECTATIONS = {
    "scenes/regions/ember_crown/town_hub.tscn": (
        "bld_shop_awning.tscn", "bld_townhouse_balcony.tscn", "bld_cottage_shuttered.tscn"),
    "scenes/regions/ember_crown/embermarket.tscn": (
        "bld_shop_awning.tscn", "bld_townhouse_balcony.tscn", "bld_cottage_shuttered.tscn"),
    "scenes/regions/ember_crown/tarn_landing.tscn": ("bld_cottage_shuttered.tscn",),
    "scenes/regions/ember_crown/hollowreach.tscn": ("bld_cottage_shuttered.tscn",),
    "scenes/regions/frostfang_reach/clan_hold.tscn": (
        "bld_longhouse_stone.tscn", "bld_workshop_open.tscn"),
}
RETAINED_GLBS = (
    "bld_blacksmith.glb", "bld_cottage.glb", "bld_house_a.glb", "bld_house_b.glb", "bld_inn.glb")


def fail(message: str, failures: list[str]) -> None:
    failures.append(message)


def glb_document(path: Path) -> dict:
    raw = path.read_bytes()
    if raw[:4] != b"glTF":
        raise ValueError("not GLB")
    _, version, length = struct.unpack_from("<III", raw, 0)
    if version != 2 or length != len(raw):
        raise ValueError("invalid header")
    offset = 12
    while offset < length:
        size, kind = struct.unpack_from("<II", raw, offset)
        if kind == 0x4E4F534A:
            return json.loads(raw[offset + 8:offset + 8 + size].rstrip(b" \0"))
        offset += 8 + size
    raise ValueError("no JSON chunk")


def main() -> int:
    failures: list[str] = []
    props = ROOT / "scenes" / "props"
    for filename, (expected_shapes, mode) in PREFABS.items():
        path = props / filename
        if not path.is_file():
            fail(f"{filename}: missing", failures)
            continue
        text = path.read_text(encoding="utf-8")
        shapes = text.count('type="CollisionShape3D"')
        if shapes != expected_shapes:
            fail(f"{filename}: {shapes} collision shapes, expected {expected_shapes}", failures)
        if "python tools/compose_building.py" not in text:
            fail(f"{filename}: no exact regeneration command", failures)
        ids = re.findall(r'^\[ext_resource .* id="([^"]+)"\]$', text, re.MULTILINE)
        if len(ids) != len(set(ids)):
            fail(f"{filename}: duplicate ext_resource id", failures)
        for resource in re.findall(r'path="res://([^"]+)"', text):
            if not (ROOT / resource).is_file():
                fail(f"{filename}: missing resource {resource}", failures)
        if mode in ("open", "ruined") and "Shape_floor" in text:
            fail(f"{filename}: {mode} shell has an invisible floor collider", failures)
        if mode == "ruined" and "Shape_" + filename[4:-5] in text:
            fail(f"{filename}: ruin uses a whole-shell collider across its breaches", failures)

    for relative, expected in LIVE_EXPECTATIONS.items():
        text = (ROOT / relative).read_text(encoding="utf-8")
        for filename in expected:
            if filename not in text:
                fail(f"{relative}: does not use {filename}", failures)

    architecture = ROOT / "assets" / "models" / "architecture"
    for filename in RETAINED_GLBS:
        try:
            document = glb_document(architecture / filename)
        except (OSError, ValueError, json.JSONDecodeError) as error:
            fail(f"{filename}: unreadable ({error})", failures)
            continue
        for material in document.get("materials", []):
            metallic = material.get("pbrMetallicRoughness", {}).get("metallicFactor", 1.0)
            if metallic > 0.05:
                fail(f"{filename}: {material.get('name', '<unnamed>')} metallic={metallic}", failures)

    if failures:
        print("architecture kit: FAIL")
        for message in failures:
            print("  -", message)
        return 1
    print(f"architecture kit: PASS ({len(PREFABS)} authored prefabs, "
          f"{len(LIVE_EXPECTATIONS)} integrated settlement scenes, "
          f"{len(RETAINED_GLBS)} repaired legacy GLBs)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
