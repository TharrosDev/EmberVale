#!/usr/bin/env python3
"""How well an archetype's hit zones cover the model they are authored against.

    python tools/check_hit_zones.py            # every data/enemies/*.tres that has HitZones
    python tools/check_hit_zones.py --check    # exit 1 when a body falls under the floors below

Hit zones are capsules written as numbers in a .tres, in metres from the actor's feet, and nothing
ties them to the mesh: ContentValidator checks that a radius is positive, not that the head zone is
where the head is. This reads both files and says how much of each body part's REST-POSE vertices
sit inside at least one zone, and how much of what a sword can reach (below 2.2 m) does.

It reads the zones from the .tres and the vertices from the .glb, so it is a check of what is
committed rather than of a second copy of the numbers. Pure Python, no engine.

Limits, on purpose:
  * rest pose only. A dragon's head drops four metres in its bite and the zone does not follow.
  * the model's skinned mesh must already be in metres with an identity root (the generated
    creatures are; an old kit rig with a 100x armature is reported as skipped, not as a failure).
  * coverage is by vertex count, so a dense head counts for more than a wing membrane of two
    hundred big triangles. Read the per-part lines, not just the total.
"""
from __future__ import annotations

import argparse
import json
import math
import re
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REACH = 2.2          # metres: about what a standing swing touches
FLOOR_ALL = 0.60     # --check: share of all vertices inside a zone
FLOOR_REACH = 0.70   # --check: share of the vertices below REACH inside a zone
COMPONENT = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}
WIDTH = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def read_glb(path: Path) -> tuple[dict, bytes]:
    raw = path.read_bytes()
    size = struct.unpack_from("<I", raw, 12)[0]
    document = json.loads(raw[20:20 + size])
    offset = 20 + size
    return document, raw[offset + 8:offset + 8 + struct.unpack_from("<I", raw, offset)[0]]


def accessor(document: dict, binary: bytes, index: int) -> list[tuple]:
    spec = document["accessors"][index]
    view = document["bufferViews"][spec["bufferView"]]
    code, width = COMPONENT[spec["componentType"]], WIDTH[spec["type"]]
    stride = view.get("byteStride") or struct.calcsize(code) * width
    start = view.get("byteOffset", 0) + spec.get("byteOffset", 0)
    return [struct.unpack_from(f"<{width}{code}", binary, start + i * stride) for i in range(spec["count"])]


def part_of(bone: str) -> str:
    name = bone.lower()
    for part, keys in (("wings", ("wing",)), ("legs", ("leg", "foot", "paw")), ("tail", ("tail", "body")),
                       ("head", ("head", "jaw")), ("neck", ("neck",))):
        if any(key in name for key in keys):
            return part
    return "torso"


def model_parts(path: Path, scale: float) -> dict[str, list[tuple[float, float, float]]] | str:
    """Rest-pose vertices by body part, in the ACTOR's space (the factory turns a model half a turn
    about Y, so x and z negate), or a reason this model cannot be measured from its bytes."""
    document, binary = read_glb(path)
    skins = document.get("skins", [])
    if not skins:
        return "no skin"
    for node in document["nodes"]:
        if "skin" not in node and any(abs(v - 1.0) > 1e-3 for v in node.get("scale", [1, 1, 1])):
            return "a node above the mesh carries a scale: bind space is not metres"
    names = [document["nodes"][joint].get("name", "") for joint in skins[0]["joints"]]
    parts: dict[str, list[tuple[float, float, float]]] = {}
    for mesh in document["meshes"]:
        for primitive in mesh["primitives"]:
            attributes = primitive["attributes"]
            if "JOINTS_0" not in attributes:
                continue
            for point, joints, weights in zip(accessor(document, binary, attributes["POSITION"]),
                                              accessor(document, binary, attributes["JOINTS_0"]),
                                              accessor(document, binary, attributes["WEIGHTS_0"])):
                bone = names[joints[max(range(4), key=lambda i: weights[i])]]
                parts.setdefault(part_of(bone), []).append((-point[0] * scale, point[1] * scale, -point[2] * scale))
    return parts or "no skinned vertices"


def read_zones(text: str) -> list[dict]:
    zones = []
    for block in re.split(r"\n(?=\[)", text):
        if not block.startswith("[sub_resource") or 'Id = "' not in block or "Radius" not in block:
            continue

        def number(key: str, default: float = 0.0) -> float:
            found = re.search(rf"^{key} = ([-0-9.e]+)", block, re.M)
            return float(found.group(1)) if found else default

        def vector(key: str) -> tuple[float, float, float]:
            found = re.search(rf"^{key} = Vector3\(([^)]*)\)", block, re.M)
            return tuple(float(part) for part in found.group(1).split(",")) if found else (0.0, 0.0, 0.0)

        zones.append({"id": re.search(r'^Id = "([^"]*)"', block, re.M).group(1), "offset": vector("Offset"),
                      "radius": number("Radius", 0.5), "height": number("Height"),
                      "axis": capsule_axis(vector("RotationDegrees"))})
    return zones


def capsule_axis(degrees: tuple[float, float, float]) -> tuple[float, float, float]:
    """Where Godot's Basis.FromEuler (YXZ order) sends the capsule's own +Y."""
    x, y, z = (math.radians(value) for value in degrees)
    v = (-math.sin(z), math.cos(z), 0.0)                                              # about Z
    v = (v[0], v[1] * math.cos(x) - v[2] * math.sin(x), v[1] * math.sin(x) + v[2] * math.cos(x))   # about X
    return (v[0] * math.cos(y) + v[2] * math.sin(y), v[1], -v[0] * math.sin(y) + v[2] * math.cos(y))  # about Y


def inside(point: tuple[float, float, float], zone: dict) -> bool:
    v = tuple(point[i] - zone["offset"][i] for i in range(3))
    radius, height = zone["radius"], zone["height"]
    if height > radius * 2:                     # the factory's own rule for capsule versus sphere
        half = height / 2 - radius
        along = max(-half, min(half, sum(a * b for a, b in zip(v, zone["axis"]))))
        v = tuple(v[i] - zone["axis"][i] * along for i in range(3))
    return v[0] * v[0] + v[1] * v[1] + v[2] * v[2] <= radius * radius


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("archetypes", nargs="*", type=Path, help="default: every data/enemies/*.tres with HitZones")
    parser.add_argument("--check", action="store_true", help="fail under the coverage floors")
    args = parser.parse_args()
    files = args.archetypes or sorted((ROOT / "data" / "enemies").glob("*.tres"))
    failed = []
    for path in files:
        text = path.read_text(encoding="utf-8").replace("\r\n", "\n")
        zones = read_zones(text)
        model = re.search(r'^ModelPath = "res://([^"]+)"', text, re.M)
        if not zones or not model:
            continue
        scale = re.search(r"^ModelScale = ([0-9.]+)", text, re.M)
        parts = model_parts(ROOT / model.group(1), float(scale.group(1)) if scale else 1.0)
        print(f"{path.name}  ({model.group(1)})")
        if isinstance(parts, str):
            print(f"  skipped: {parts}")
            continue
        for zone in zones:
            shape = "capsule" if zone["height"] > zone["radius"] * 2 else "sphere"
            print("  zone %-6s %-7s at (%.2f, %.2f, %.2f) r %.2f h %.2f axis (%.2f, %.2f, %.2f)"
                  % (zone["id"], shape, *zone["offset"], zone["radius"], zone["height"], *zone["axis"]))
        everything = [point for points in parts.values() for point in points]
        for part in ("head", "neck", "torso", "legs", "wings", "tail"):
            points = parts.get(part, [])
            if not points:
                continue
            counts = {zone["id"]: sum(1 for point in points if inside(point, zone)) for zone in zones}
            covered = sum(1 for point in points if any(inside(point, zone) for zone in zones))
            print("  part %-6s %5.1f%% of %5d vertices   %s" % (
                part, 100.0 * covered / len(points), len(points),
                "  ".join(f"{name} {100 * count // len(points)}%" for name, count in counts.items() if count)))
        low = [point for point in everything if point[1] < REACH]
        share_all = sum(1 for point in everything if any(inside(point, zone) for zone in zones)) / len(everything)
        share_low = sum(1 for point in low if any(inside(point, zone) for zone in zones)) / max(1, len(low))
        print("  all vertices %.1f%% inside a zone; below %.1f m %.1f%%" % (100 * share_all, REACH, 100 * share_low))
        if share_all < FLOOR_ALL or share_low < FLOOR_REACH:
            failed.append(path.name)
    if args.check and failed:
        print("FAIL: zones do not fit the model: " + ", ".join(failed), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
