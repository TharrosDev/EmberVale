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
# The 2026-10 world overhaul moved the outdoor world's towers, walls and waystones to the generated
# set (scenes/props/lm_*.tscn). These are the models it replaced everywhere: a cell scene that names
# one again has been dressed from the old kit. bld_ruin_tower.tscn stays an authored prefab above (it
# is on disk and still composes); it is the PLACING of it that is retired. Models kept on purpose in a
# few places (prp_ruin_pillar, prp_ruin_wall, prp_gate_palisade, the yard clutter) are not listed.
RETIRED_IN_CELLS = (
    "props/bld_ruin_tower.tscn", "architecture/bld_castle_fortress.glb", "props/prp_watch_tower.glb",
    "props/prp_warden_post.glb", "props/prp_bell_tower.glb", "props/prp_arena_wall.glb",
    "props/prp_waystone.glb", "props/prp_beast_antler_ring.tscn", "props/prp_beast_stake.tscn",
    "props/prp_lamp_post.glb", "props/prp_well.glb")


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

    # The generated set: every wrapper a district row can place exists, is solid and names a model
    # that is on disk; and no cell has gone back to a model the set replaced.
    import sys
    sys.path.insert(0, str(ROOT / "tools"))
    from compose_district import BOXED, COMPOSED, LOOSE
    wrappers = 0
    for kind, (scene, _) in sorted(COMPOSED.items()):
        path = ROOT / scene.replace("res://", "")
        if not path.is_file():
            fail(f"district kind '{kind}': {scene} is missing", failures)
            continue
        if "/lm_" not in scene:
            continue
        wrappers += 1
        text = path.read_text(encoding="utf-8")
        if 'type="CollisionShape3D"' not in text:
            fail(f"{path.name}: a generated landmark with no collider", failures)
        for resource in re.findall(r'path="res://([^"]+)"', text):
            if not (ROOT / resource).is_file():
                fail(f"{path.name}: missing resource {resource}", failures)
    for kind, model in sorted([(k, v[0]) for k, v in BOXED.items()] + list(LOOSE.items())):
        if not (ROOT / model.replace("res://", "")).is_file():
            fail(f"district kind '{kind}': {model} is missing", failures)
    for scene in sorted((ROOT / "scenes" / "regions").rglob("*.tscn")):
        text = scene.read_text(encoding="utf-8")
        for retired in RETIRED_IN_CELLS:
            if f'/{retired}"' in text:
                fail(f"{scene.relative_to(ROOT).as_posix()}: places {retired}, which the generated set replaced", failures)

    if failures:
        print("architecture kit: FAIL")
        for message in failures:
            print("  -", message)
        return 1
    print(f"architecture kit: PASS ({len(PREFABS)} authored prefabs, "
          f"{len(LIVE_EXPECTATIONS)} integrated settlement scenes, "
          f"{len(RETAINED_GLBS)} repaired legacy GLBs, {wrappers} generated landmark wrappers)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
