#!/usr/bin/env python3
"""Prove Session 3's human GLB edits changed materials, not rig/geometry payloads."""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
HUMANS = (
    "npc_adventurer_f.glb", "npc_guild_rep.glb", "npc_hooded.glb", "npc_innkeeper.glb",
    "npc_kael.glb", "npc_merchant_f.glb", "npc_merchant_m.glb", "npc_townsman.glb",
    "npc_townswoman.glb", "npc_vendor.glb", "npc_woman_dress.glb",
)
STRUCTURAL_KEYS = ("scenes", "scene", "nodes", "meshes", "skins", "animations", "accessors",
                   "bufferViews", "buffers")


def read_glb(payload: bytes) -> tuple[dict, bytes]:
    magic, version, length = struct.unpack_from("<4sII", payload, 0)
    if magic != b"glTF" or version != 2 or length != len(payload):
        raise ValueError("not a canonical glTF 2 GLB")
    offset = 12
    chunks: dict[bytes, bytes] = {}
    while offset < length:
        size, kind = struct.unpack_from("<I4s", payload, offset)
        offset += 8
        chunks[kind] = payload[offset:offset + size]
        offset += size
    return json.loads(chunks[b"JSON"].rstrip(b" \0")), chunks.get(b"BIN\0", b"")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="reports/3d/session-03-npc-handoff/rig-preservation")
    parser.add_argument("--base", default="HEAD", help="Git revision containing the source humans")
    args = parser.parse_args()
    output = ROOT / args.output
    output.mkdir(parents=True, exist_ok=True)
    rows = []
    for name in HUMANS:
        relative = f"assets/models/characters/{name}"
        before_bytes = subprocess.check_output(["git", "show", f"{args.base}:{relative}"], cwd=ROOT)
        after_bytes = (ROOT / relative).read_bytes()
        before, before_bin = read_glb(before_bytes)
        after, after_bin = read_glb(after_bytes)
        structure_ok = all(before.get(key) == after.get(key) for key in STRUCTURAL_KEYS)
        binary_ok = before_bin == after_bin
        rows.append((relative, structure_ok, binary_ok, len(after.get("skins", [])),
                     len(after.get("animations", [])), hashlib.sha256(after_bin).hexdigest()))
    lines = [
        "# NPC rig and geometry preservation", "",
        f"Compared the working assets byte-for-byte with `{args.base}`.",
        "Materials are intentionally excluded from structural comparison.", "",
        "| Asset | Structure | BIN payload | Skins | Animations | BIN SHA-256 |",
        "| --- | --- | --- | ---: | ---: | --- |",
    ]
    for relative, structure_ok, binary_ok, skins, animations, digest in rows:
        lines.append(f"| `{relative}` | {'PASS' if structure_ok else 'FAIL'} | "
                     f"{'PASS' if binary_ok else 'FAIL'} | {skins} | {animations} | `{digest}` |")
    passed = all(row[1] and row[2] for row in rows)
    lines += ["", f"Result: **{'PASS' if passed else 'FAIL'}** ({len(rows)} production humans)."]
    (output / "README.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"NPC rig preservation: {'PASS' if passed else 'FAIL'} ({len(rows)} assets) -> {output}")
    raise SystemExit(0 if passed else 1)


if __name__ == "__main__":
    main()
