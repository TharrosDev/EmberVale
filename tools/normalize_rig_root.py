#!/usr/bin/env python3
"""Give a rigged glTF the same root-node shape every working body in this pack already has.

The problem
-----------
`chr_player_base.glb` could not be retargeted. Compared against a body that retargets cleanly:

    npc_townsman   RootNode  T=[0,0,0]        R=identity  S=[1,1,1]
                   Armature  T=[0,0,0]        R=-90deg X  S=[100,100,100]
                   root bone t=[0,-0.00072,0]

    chr_player     RootNode  T=[0,4.8237,0]   R=identity  S=[0.9161]*3    <-- the anomaly
                   Armature  T=[0,0,0]        R=-90deg X  S=[100,100,100]
                   root bone t=[0,0.00725,-0.05264]                       <-- and its counterweight

The root bone's -0.05264 on Z, carried through the armature's -90 degree X rotation and x91.61 of
scale, lands at -4.822 in world Y - which `RootNode`'s +4.8237 cancels almost exactly. **Two errors
that cancel.** The model therefore renders correctly today, and Godot's retarget rest-fixer breaks it
in all four of its flag combinations because every one of them redistributes exactly those numbers:

    apply_node_transforms=true,  overwrite_axis=true   -> sinks 4.8 m
    apply_node_transforms=true,  overwrite_axis=false  -> sinks 4.8 m
    apply_node_transforms=false, overwrite_axis=true   -> spine shears, head 45 cm off-axis
    apply_node_transforms=false, overwrite_axis=false  -> bones land right, MESH stays at y -4.9

⚠️ A Blender round-trip is not available: `ASSET_POLICY.md` records that it destroys bone-parented
children, and this model carries **17** of them. Hence a surgical edit of the glTF.

What it does
------------
Collapses the cancellation instead of moving it around:

* the scene root becomes **exact identity** — its uniform scale is folded into the armature node, so
  the TOTAL scale reaching the bones is unchanged (0.9161 x 100 == 1 x 91.61);
* its translation is folded into the **root bone**, expressed in the bone's own space, so the world
  pose is bit-for-bit what it was;
* ⚠️ every animation keyframe that translates the root bone gets the same offset — this model's 24
  animations all drive the root bone, so fixing only the rest pose would be undone the moment
  anything played.

Rotation and scale keyframes are untouched, because neither the rotation nor the total scale changes.
Vertices and inverse bind matrices are never touched at all.

⚠️ Refuses a non-uniform ancestor scale: it does not commute with rotation, so it cannot be folded
into a TRS keyframe stream without shearing every pose.

Usage
-----
    python tools/normalize_rig_root.py <model.glb> [--out=other.glb]
"""

import json
import os
import struct
import sys

FLOAT = 5126


def read_glb(path):
    raw = open(path, "rb").read()
    if struct.unpack("<I", raw[:4])[0] != 0x46546C67:
        raise SystemExit(f"{path} is not a GLB")
    gltf = binc = None
    off = 12
    while off < len(raw):
        length, kind = struct.unpack("<II", raw[off:off + 8])
        chunk = raw[off + 8: off + 8 + length]
        if kind == 0x4E4F534A:
            gltf = json.loads(chunk)
        elif kind == 0x004E4942:
            binc = bytearray(chunk)
        off += 8 + length
    return gltf, binc


def write_glb(path, gltf, binc):
    js = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    js += b" " * (-len(js) % 4)
    bn = bytes(binc) + b"\x00" * (-len(binc) % 4)
    body = (struct.pack("<II", len(js), 0x4E4F534A) + js
            + struct.pack("<II", len(bn), 0x004E4942) + bn)
    open(path, "wb").write(struct.pack("<III", 0x46546C67, 2, 12 + len(body)) + body)


def qconj(q):
    return [-q[0], -q[1], -q[2], q[3]]


def qrot(q, v):
    x, y, z, w = q
    tx, ty, tz = 2 * (y * v[2] - z * v[1]), 2 * (z * v[0] - x * v[2]), 2 * (x * v[1] - y * v[0])
    return [v[0] + w * tx + y * tz - z * ty,
            v[1] + w * ty + z * tx - x * tz,
            v[2] + w * tz + x * ty - y * tx]


def accessor_vec3(gltf, binc, index):
    acc = gltf["accessors"][index]
    if acc["componentType"] != FLOAT or acc["type"] != "VEC3":
        raise SystemExit(f"accessor {index} is not a float VEC3")
    view = gltf["bufferViews"][acc["bufferView"]]
    stride = view.get("byteStride", 12)
    if stride != 12:
        raise SystemExit(f"accessor {index} is interleaved (stride {stride}); refusing to guess")
    base = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    vals = [list(struct.unpack_from("<3f", binc, base + i * 12)) for i in range(acc["count"])]

    def write(new):
        for i, v in enumerate(new):
            struct.pack_into("<3f", binc, base + i * 12, *v)
        if "min" in acc:
            acc["min"] = [min(v[k] for v in new) for k in range(3)]
            acc["max"] = [max(v[k] for v in new) for k in range(3)]

    return vals, write


def normalize(src, dest):
    gltf, binc = read_glb(src)
    nodes = gltf["nodes"]
    joints = set(gltf["skins"][0]["joints"])
    roots = [j for j in joints if not any(j in nodes[k].get("children", []) for k in joints)]
    if len(roots) != 1:
        raise SystemExit(f"expected exactly 1 root bone, found {len(roots)}")
    root_bone = roots[0]

    scene_roots = gltf["scenes"][gltf.get("scene", 0)]["nodes"]
    if len(scene_roots) != 1:
        raise SystemExit(f"expected exactly 1 scene root, found {len(scene_roots)}")
    top = nodes[scene_roots[0]]

    t_top = top.get("translation", [0.0, 0.0, 0.0])
    s_top = top.get("scale", [1.0, 1.0, 1.0])
    if top.get("rotation", [0, 0, 0, 1]) != [0, 0, 0, 1]:
        raise SystemExit("the scene root carries a rotation; not handled")
    if abs(s_top[0] - s_top[1]) > 1e-4 or abs(s_top[1] - s_top[2]) > 1e-4:
        raise SystemExit(f"scene root scale {s_top} is non-uniform; refusing")
    if t_top == [0.0, 0.0, 0.0] and abs(s_top[0] - 1.0) < 1e-6:
        print("  already normal - nothing to do")
        return False

    # The armature is the scene root's child that leads to the bones.
    def leads_to_bone(i, seen=None):
        if i == root_bone:
            return True
        return any(leads_to_bone(c) for c in nodes[i].get("children", []))

    arm_idx = [c for c in top.get("children", []) if leads_to_bone(c)]
    if len(arm_idx) != 1:
        raise SystemExit(f"expected 1 armature under the scene root, found {len(arm_idx)}")
    arm = nodes[arm_idx[0]]
    arm_rot = arm.get("rotation", [0.0, 0.0, 0.0, 1.0])
    arm_scale = arm.get("scale", [1.0, 1.0, 1.0])
    if arm.get("translation", [0, 0, 0]) not in ([0, 0, 0], [0.0, 0.0, 0.0]):
        raise SystemExit("the armature carries a translation; not handled")

    total = s_top[0] * arm_scale[0]
    print(f"  scene root  T={[round(v, 4) for v in t_top]}  S={round(s_top[0], 4)}")
    print(f"  armature    S={round(arm_scale[0], 4)}  ->  total scale reaching the bones {total:.4f}")

    # Every sibling of the armature (the skinned mesh nodes) carries the SAME rotation and scale in
    # this pack's exports, and a skinned mesh node's transform is ignored per the glTF spec anyway.
    # They are scaled to match so the file stays self-consistent for any other reader.
    for c in top.get("children", []):
        n = nodes[c]
        s = n.get("scale", [1.0, 1.0, 1.0])
        n["scale"] = [v * s_top[0] for v in s]

    top.pop("translation", None)
    top.pop("scale", None)
    top.pop("rotation", None)
    top.pop("matrix", None)

    # Fold the removed translation into the root bone, expressed in the bone's own space:
    #   world = T_top + R_arm * (total * t)   ->   t' = t + (R_arm^-1 * T_top) / total
    delta = [v / total for v in qrot(qconj(arm_rot), t_top)]
    bone = nodes[root_bone]
    bt = bone.get("translation", [0.0, 0.0, 0.0])
    bone["translation"] = [bt[k] + delta[k] for k in range(3)]
    print(f"  root bone '{bone.get('name')}'  t {[round(v, 5) for v in bt]}"
          f" -> {[round(v, 5) for v in bone['translation']]}")

    # ⚠️ Several animations SHARE one output accessor, so each is rewritten exactly once.
    moved, seen = 0, set()
    for anim in gltf.get("animations", []):
        for ch in anim["channels"]:
            if ch["target"]["node"] != root_bone or ch["target"]["path"] != "translation":
                continue
            acc = anim["samplers"][ch["sampler"]]["output"]
            if acc in seen:
                continue
            seen.add(acc)
            vals, write = accessor_vec3(gltf, binc, acc)
            write([[v[k] + delta[k] for k in range(3)] for v in vals])
            moved += 1
    print(f"  offset {moved} root-bone translation sampler(s) by {[round(v, 5) for v in delta]}")

    write_glb(dest, gltf, binc)
    print(f"  -> {dest}")
    return True


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(args) != 1:
        raise SystemExit(__doc__)
    out = next((a.split("=", 1)[1] for a in sys.argv[1:] if a.startswith("--out=")), args[0])
    print(os.path.basename(args[0]))
    normalize(args[0], out)
