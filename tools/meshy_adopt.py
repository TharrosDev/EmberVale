#!/usr/bin/env python3
"""Adopt a Meshy .glb export into assets/models/ at a production texture budget.

Meshy ships one 2048x2048 embedded albedo per character. At ~5.6 MB of VRAM each that is
235 MB across a 42-character roster, so every adopted model is repacked with its images
capped (default 1024). Geometry, skin, and animation data are copied through byte-for-byte;
only image bufferViews change, and the buffer is rebuilt compactly so the old bytes go away.

    python tools/meshy_adopt.py <source.glb> <dest.glb> [--max-texture 1024]
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import struct
from pathlib import Path

from PIL import Image

JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942


def read_glb(path: Path) -> tuple[dict, bytes]:
    data = path.read_bytes()
    magic, _version, _length = struct.unpack_from("<III", data, 0)
    if magic != 0x46546C67:
        raise ValueError(f"{path} is not a binary glTF")
    offset, doc, binary = 12, None, b""
    while offset < len(data):
        chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
        body = data[offset + 8 : offset + 8 + chunk_len]
        if chunk_type == JSON_CHUNK:
            doc = json.loads(body)
        elif chunk_type == BIN_CHUNK:
            binary = body
        offset += 8 + chunk_len + (-chunk_len % 4)
    if doc is None:
        raise ValueError(f"{path} has no JSON chunk")
    return doc, binary


def write_glb(path: Path, doc: dict, binary: bytes) -> None:
    doc_bytes = json.dumps(doc, separators=(",", ":")).encode("utf-8")
    doc_bytes += b" " * (-len(doc_bytes) % 4)
    binary += b"\x00" * (-len(binary) % 4)
    total = 12 + 8 + len(doc_bytes) + 8 + len(binary)
    with path.open("wb") as out:
        out.write(struct.pack("<III", 0x46546C67, 2, total))
        out.write(struct.pack("<II", len(doc_bytes), JSON_CHUNK))
        out.write(doc_bytes)
        out.write(struct.pack("<II", len(binary), BIN_CHUNK))
        out.write(binary)


def shrink_images(doc: dict, binary: bytes, max_size: int) -> dict[int, bytes]:
    """Return {bufferView index: replacement bytes} for every oversized embedded image."""
    replacements: dict[int, bytes] = {}
    for image in doc.get("images", []):
        view_index = image.get("bufferView")
        if view_index is None:
            continue
        view = doc["bufferViews"][view_index]
        start = view.get("byteOffset", 0)
        raw = binary[start : start + view["byteLength"]]
        with Image.open(io.BytesIO(raw)) as source:
            if max(source.size) <= max_size:
                continue
            scale = max_size / max(source.size)
            size = (max(1, round(source.width * scale)), max(1, round(source.height * scale)))
            resized = source.convert("RGBA" if "A" in source.getbands() else "RGB")
            resized = resized.resize(size, Image.LANCZOS)
        buffer = io.BytesIO()
        resized.save(buffer, format="PNG", optimize=True)
        replacements[view_index] = buffer.getvalue()
        image["mimeType"] = "image/png"
    return replacements


def normalise_clip_names(doc: dict) -> list[str]:
    """Rename "Armature|running|baselayer" to "Running".

    AnimationClips.Bare() strips "CharacterArmature|", "lib/" and the gendered prefixes, but not
    the "Armature|...|baselayer" wrapper the Meshy API returns -- so the clip would never match the
    'run' alias and locomotion would silently fall through to the shared library. The web exporter
    already emits clean names, so normalising here keeps API-generated models consistent with the
    four the maintainer exported by hand.
    """
    renamed = []
    for animation in doc.get("animations", []):
        name = animation.get("name", "")
        parts = [p for p in name.split("|") if p and p.lower() != "baselayer"]
        if parts and parts[0].lower() == "armature":
            parts = parts[1:]
        if parts:
            clean = parts[-1].capitalize()
            if clean != name:
                animation["name"] = clean
                renamed.append(f"{name} -> {clean}")
    return renamed


def rebuild_buffer(doc: dict, binary: bytes, replacements: dict[int, bytes]) -> bytes:
    """Repack every bufferView in order, substituting the shrunk images."""
    out = bytearray()
    for index, view in enumerate(doc["bufferViews"]):
        if index in replacements:
            payload = replacements[index]
        else:
            start = view.get("byteOffset", 0)
            payload = binary[start : start + view["byteLength"]]
        # byteStride'd views feed vertex attributes; keep 4-byte alignment for all of them.
        out += b"\x00" * (-len(out) % 4)
        view["byteOffset"] = len(out)
        view["byteLength"] = len(payload)
        out += payload
    doc["buffers"] = [{"byteLength": len(out)}]
    return bytes(out)


def build_bone_map(doc: dict) -> tuple[dict[str, str], str]:
    """Map SkeletonProfileHumanoid slots onto this rig by POSITION, never by bone name.

    Meshy's bone names cannot be trusted twice over. The spine is named inverted
    (Hips -> Spine02 -> Spine01 -> Spine, so "Spine" is the TOP one), and the names are sometimes
    attached to the wrong joints outright: npc_innkeeper came back as
    Hips -> neck -> Spine02 -> Spine -> {shoulders, Head1 -> Head}, where the bone called "neck"
    is the lowest spine joint and the one called "Head1" is the neck. The geometry was fine; only
    the labels were scrambled. Reading the hierarchy instead survives both, and survives whatever
    the next rig decides to call things.

    The torso is walked from Hips to the joint that carries the shoulders -- that joint is
    UpperChest, whatever it is called, and the joints below it become Chest and Spine. From there
    the non-shoulder branch is Neck then Head.
    """
    nodes = doc["nodes"]
    joints = {nodes[j].get("name") for j in doc["skins"][0]["joints"]}
    by_name = {node.get("name"): i for i, node in enumerate(nodes)}
    mapping: dict[str, str] = {}
    if "Hips" not in by_name:
        return mapping, "norig"
    mapping["Hips"] = "Hips"

    def named_children(index):
        return [c for c in nodes[index].get("children", []) if nodes[c].get("name") in joints]

    leg_roots = {"LeftUpLeg", "RightUpLeg"}
    shoulders = {"LeftShoulder", "RightShoulder"}

    torso, current = [], by_name["Hips"]
    while True:
        ups = [c for c in named_children(current) if nodes[c].get("name") not in leg_roots]
        carries_shoulders = any(nodes[c].get("name") in shoulders for c in ups)
        if carries_shoulders or not ups:
            break
        torso.append(current)
        current = ups[0]
    # `current` now carries the shoulders; `torso` holds the joints below it, Hips first.
    torso = torso[1:]  # drop Hips itself
    spine_slots = ["UpperChest", "Chest", "Spine"]
    for slot, index in zip(spine_slots, [current] + list(reversed(torso))):
        mapping[slot] = nodes[index].get("name")

    neck_branch = [c for c in named_children(current) if nodes[c].get("name") not in shoulders]
    if neck_branch:
        mapping["Neck"] = nodes[neck_branch[0]].get("name")
        head = named_children(neck_branch[0])
        if head:
            mapping["Head"] = nodes[head[0]].get("name")

    for side in ("Left", "Right"):
        for slot, bone in ((f"{side}Shoulder", f"{side}Shoulder"),
                           (f"{side}UpperArm", f"{side}Arm"),
                           (f"{side}LowerArm", f"{side}ForeArm"),
                           (f"{side}Hand", f"{side}Hand"),
                           (f"{side}UpperLeg", f"{side}UpLeg"),
                           (f"{side}LowerLeg", f"{side}Leg"),
                           (f"{side}Foot", f"{side}Foot"),
                           (f"{side}Toes", f"{side}ToeBase")):
            if bone in joints:
                mapping[slot] = bone

    mapping = {k: v for k, v in mapping.items() if v in joints}
    # Key the signature on the RESOLVED NAMES, not the rig's shape. Two rigs can both have three
    # spine joints and still need different maps -- npc_innkeeper's spine is (neck, Spine02, Spine)
    # where everyone else's is (Spine02, Spine01, Spine). Keying on shape alone made them collide
    # on one file and the last writer silently won.
    digest = hashlib.sha256(repr(sorted(mapping.items())).encode("utf-8")).hexdigest()[:8]
    spine_count = sum(1 for s in spine_slots if s in mapping)
    return mapping, f"s{spine_count}_{digest}"


PROFILE_SLOTS = [
    "Root", "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "LeftEye", "RightEye", "Jaw",
    "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
    "LeftThumbMetacarpal", "LeftThumbProximal", "LeftThumbDistal",
    "LeftIndexProximal", "LeftIndexIntermediate", "LeftIndexDistal",
    "LeftMiddleProximal", "LeftMiddleIntermediate", "LeftMiddleDistal",
    "LeftRingProximal", "LeftRingIntermediate", "LeftRingDistal",
    "LeftLittleProximal", "LeftLittleIntermediate", "LeftLittleDistal",
    "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
    "RightThumbMetacarpal", "RightThumbProximal", "RightThumbDistal",
    "RightIndexProximal", "RightIndexIntermediate", "RightIndexDistal",
    "RightMiddleProximal", "RightMiddleIntermediate", "RightMiddleDistal",
    "RightRingProximal", "RightRingIntermediate", "RightRingDistal",
    "RightLittleProximal", "RightLittleIntermediate", "RightLittleDistal",
    "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes",
    "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes",
]


def write_bone_map(mapping: dict[str, str], signature: str, root: Path) -> Path:
    """Write (or reuse) the BoneMap for this rig shape. Shapes repeat, so files stay few."""
    path = root / "assets" / "models" / "animations" / f"bonemap_meshy_{signature}.tres"
    lines = ['[gd_resource type="BoneMap" load_steps=2 format=3]', "",
             f"; Meshy auto-rig shape '{signature}' -> SkeletonProfileHumanoid. Generated by",
             "; tools/meshy_adopt.py from the rig's real hierarchy -- do not hand-edit.",
             ";",
             "; The spine is walked, never sorted by name: Meshy names it INVERTED",
             "; (Hips -> Spine02 -> Spine01 -> Spine), and the chain length varies per model.",
             "; No fingers, eyes or jaw in any Meshy rig.", "",
             '[sub_resource type="SkeletonProfileHumanoid" id="SkeletonProfileHumanoid_1"]', "",
             "[resource]", 'profile = SubResource("SkeletonProfileHumanoid_1")']
    for slot in PROFILE_SLOTS:
        lines.append(f'bone_map/{slot} = "{mapping.get(slot, "")}"')
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return path


RETARGET_BLOCK = """_subresources={
"nodes": {
"PATH:Armature/Skeleton3D": {
"retarget/bone_map": Resource("res://assets/models/animations/{bonemap}"),
"retarget/bone_renamer/rename_bones": true,
"retarget/bone_renamer/unique_node/make_unique": true,
"retarget/bone_renamer/unique_node/skeleton_name": "GeneralSkeleton",
"retarget/remove_tracks/except_bone_transform": false,
"retarget/remove_tracks/unimportant_positions": true,
"retarget/remove_tracks/unmapped_bones": false,
"retarget/rest_fixer/apply_node_transforms": true,
"retarget/rest_fixer/fix_silhouette/enable": false,
"retarget/rest_fixer/fix_silhouette/filter": [],
"retarget/rest_fixer/fix_silhouette/threshold": 15.0,
"retarget/rest_fixer/keep_global_rest_on_leftovers": true,
"retarget/rest_fixer/normalize_position_tracks": true,
"retarget/rest_fixer/overwrite_axis": true,
"retarget/rest_fixer/reset_all_bone_poses_after_import": true,
"retarget/rest_fixer/retarget_global_rest": false
}
}
}"""


def patch_import(dest: Path, root_scale: float | None, bonemap: str) -> str:
    """Point the model's .import at bonemap_meshy so the 46-clip shared library attaches.

    The _subresources key is a node path into the imported scene that starts at the scene root's
    FIRST CHILD, not at the root -- Godot names the root after the file, so anchoring on the root
    never matches. Getting it wrong is silent: the model imports fine, keeps its raw 24 Meshy bone
    names, never matches "GeneralSkeleton", and the actor T-poses with no log and no error.
    tools/meshy_rig_probe.gd is the gate that turns that into a hard failure.
    """
    sidecar = dest.with_suffix(dest.suffix + ".import")
    if not sidecar.exists():
        return "no .import yet (run a Godot import pass, then re-run with --patch-import)"
    text = sidecar.read_text(encoding="utf-8")
    block = RETARGET_BLOCK.replace("{bonemap}", bonemap)
    # _subresources is always the last multi-line param before the gltf/ block, and it appears both
    # as "{}" and as a nested dict -- anchoring on what follows it covers each form with one rule.
    text, count = re.subn(r"_subresources=.*?(?=\ngltf/naming_version)",
                          block, text, count=1, flags=re.S)
    if count != 1:
        return f"FAILED to locate _subresources in {sidecar.name}"
    # 1 = extract the embedded albedo to a sidecar .png, which then gets VRAM-compressed by its own
    # importer. Mode 3 (embed uncompressed) ships the texture straight to VRAM at full cost, and
    # boss_iron_king inherited it from its predecessor -- normalise so the roster is consistent.
    text = re.sub(r"gltf/embedded_image_handling=\d+", "gltf/embedded_image_handling=1", text, count=1)
    # Reset root_scale unless told otherwise. A replacement inherits its predecessor's .import, and
    # per-model scale corrections belong to the model they were measured against -- npc_woman_dress
    # carried 0.384 for a Quaternius body and would have imported the Meshy replacement at 38% with
    # nothing in any log to say so. Meshy rigs at a stated height, so 1.0 is right by default; pass
    # --root-scale for a deliberate correction (boss_iron_king fills its 2.6 m capsule at 1.53).
    scale = 1.0 if root_scale is None else root_scale
    text, scaled = re.subn(r"nodes/root_scale=[0-9.]+", f"nodes/root_scale={scale}", text, count=1)
    if scaled != 1:
        return f"FAILED to locate nodes/root_scale in {sidecar.name}"
    sidecar.write_text(text, encoding="utf-8")
    return f"patched {sidecar.name} (PATH:Armature/Skeleton3D)"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("dest", type=Path)
    parser.add_argument("--max-texture", type=int, default=1024)
    parser.add_argument("--root-scale", type=float, default=None,
                        help="override nodes/root_scale in the .import")
    parser.add_argument("--strip-animations", action="store_true",
                        help="drop the model's own clips so every slot resolves from the shared "
                             "46-clip library (use when Meshy's locomotion clip is deformed)")
    parser.add_argument("--patch-import", action="store_true",
                        help="point the sibling .import at bonemap_meshy.tres")
    args = parser.parse_args()

    doc, binary = read_glb(args.source)
    if args.strip_animations:
        # Meshy sometimes ships a sound mesh on a mis-labelled rig, and its own clip then drives
        # the wrong joints -- npc_innkeeper's head flattened into a slab. The bind pose is fine and
        # the retarget is fine, so dropping the clip lets the slot fall through to anim_library.res
        # rather than costing a regeneration.
        dropped = len(doc.pop("animations", []))
        print(f"  dropped {dropped} model clip(s); slots resolve from the shared library")
    renamed = normalise_clip_names(doc)
    replacements = shrink_images(doc, binary, args.max_texture)
    binary = rebuild_buffer(doc, binary, replacements)
    args.dest.parent.mkdir(parents=True, exist_ok=True)
    write_glb(args.dest, doc, binary)

    mapping, signature = build_bone_map(doc)
    bonemap_path = write_bone_map(mapping, signature, Path(__file__).resolve().parent.parent)

    before = args.source.stat().st_size
    after = args.dest.stat().st_size
    print(f"{args.source.name} -> {args.dest} : {before//1024} KB -> {after//1024} KB "
          f"({len(replacements)} texture(s) capped at {args.max_texture})")
    for entry in renamed:
        print(f"  clip {entry}")
    print(f"  rig shape '{signature}' -> {bonemap_path.name} ({len(mapping)} slots mapped)")
    if args.patch_import:
        print("  " + patch_import(args.dest, args.root_scale, bonemap_path.name))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
