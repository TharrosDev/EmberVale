#!/usr/bin/env python3
"""Put a generated static creature mesh onto a skeleton, with clips, and render a pose sheet.

Run with Blender 5.1 (one at a time; it is the only heavy process this tool starts):
  blender --background --factory-startup --python tools/rebind_creature.py -- \
      --mode dragon --id enm_ancient_dragon --src gen/enm_ancient_dragon.refine.glb \
      --out rebound/enm_ancient_dragon.glb --sheet rebound/enm_ancient_dragon_sheet.png --length 22
  blender --background --factory-startup --python tools/rebind_creature.py -- \
      --mode beast --id enm_wolf --src gen/enm_wolf.refine.glb \
      --rig assets/models/creatures/enm_wolf.glb --out rebound/enm_wolf.glb --sheet rebound/enm_wolf_sheet.png

dragon: fits a NEW armature to the mesh (Root, Torso, Neck, Head, Body1..4 tail, Wing1..4.L/R and
four single leg bones: the names gameplay already resolves), skins it with distance weights that are
smoothed over the mesh graph (no bone-heat solve), and authors the eight clips the game plays as
procedural keys. Landmarks are hand-authored per asset in rebind_creature_landmarks.json, in the
SOURCE file's glTF coordinates (x left, y up, z forward); only the left side is authored.
Rungs: A smooth weights, B rigid segments on the same armature, C a six-bone minimal rig.

beast: keeps an existing rigged .glb byte for byte (nodes, skin, inverse binds, every animation) and
replaces only its mesh and material (an unskinned mesh the old body parented to a bone, such as
antlers, is dropped: the new mesh brings its own). The new mesh is warped to the rig (body fit, then each leg
column shifted and scaled until the paw sits on the old paw), and takes its weights from the nearest
vertex of the old mesh in the same body region. Nothing rigged is round-tripped through Blender.

sheet: renders the same contact sheet for any rigged .glb (--src file --sheet png --clips A,B), so
a current asset can be put beside its replacement.

Sheets are rendered in Blender so a result can be judged without Godot: the beast and sheet modes
import the WRITTEN file; the dragon mode renders the scene it has just exported.
"""

from __future__ import annotations

import argparse
import colorsys
import json
import math
import struct
import sys
import tempfile
from pathlib import Path

try:
    import bpy
    import numpy as np
    from mathutils import Euler, Vector
except ImportError:  # --help outside Blender
    bpy = None

HERE = Path(__file__).resolve().parent
FPS = 24
CLIPS = {"Flying_Idle": 36, "Fast_Flying": 20, "Punch": 32, "Headbutt": 36,
         "HitReact": 16, "Death": 16, "Yes": 28, "No": 28}
WINGS = [f"Wing{i}.{s}" for s in "LR" for i in range(1, 5)]
LEGS = ["FrontLeg.L", "FrontLeg.R", "BackLeg.L", "BackLeg.R"]
TAIL = ["Body1", "Body2", "Body3", "Body4"]
MINIMAL = {"Neck": "Head", "Body2": "Body1", "Body3": "Body1", "Body4": "Body1",
           **{w: "Wing1." + w[-1] for w in WINGS if w[4] != "1"}, **{leg: "Torso" for leg in LEGS}}
RADIUS = {"Torso": 1.0, "Neck": 0.6, "Head": 0.75, "Body1": 0.6, "Body2": 0.42, "Body3": 0.32,
          "Body4": 0.26, "leg": 0.5}


# --------------------------------------------------------------------------- glb bytes

COMPONENT = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}
WIDTH = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def read_glb(path):
    raw = Path(path).read_bytes()
    size = struct.unpack_from("<I", raw, 12)[0]
    document = json.loads(raw[20:20 + size])
    offset = 20 + size
    binary = raw[offset + 8:offset + 8 + struct.unpack_from("<I", raw, offset)[0]] if offset < len(raw) else b""
    return document, binary


def write_glb(path, document, binary):
    text = json.dumps(document, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    binary += b"\0" * (-len(binary) % 4)
    Path(path).write_bytes(
        struct.pack("<4sII", b"glTF", 2, 28 + len(text) + len(binary))
        + struct.pack("<I4s", len(text), b"JSON") + text
        + struct.pack("<I4s", len(binary), b"BIN\0") + binary)


def accessor(document, binary, index):
    spec = document["accessors"][index]
    view = document["bufferViews"][spec["bufferView"]]
    kind = np.dtype("<" + COMPONENT[spec["componentType"]])
    width = WIDTH[spec["type"]]
    start = view.get("byteOffset", 0) + spec.get("byteOffset", 0)
    stride = view.get("byteStride") or kind.itemsize * width
    if stride == kind.itemsize * width:
        return np.frombuffer(binary, kind, spec["count"] * width, start).reshape(-1, width).copy()
    return np.stack([np.frombuffer(binary, kind, width, start + i * stride) for i in range(spec["count"])])


def summary(path):
    """What the written file actually contains, re-parsed from disk."""
    document, _ = read_glb(path)
    nodes = document["nodes"]
    parent = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}

    def chain(i):
        names = []
        while i is not None:
            names.append(nodes[i].get("name"))
            i = parent.get(i)
        return "/".join(reversed(names))

    prims = [p for m in document["meshes"] for p in m["primitives"]]
    lo = [min(document["accessors"][p["attributes"]["POSITION"]]["min"][k] for p in prims) for k in range(3)]
    hi = [max(document["accessors"][p["attributes"]["POSITION"]]["max"][k] for p in prims) for k in range(3)]
    skins = document.get("skins", [])
    return {
        "file": str(path), "bytes": Path(path).stat().st_size,
        "triangles": sum(document["accessors"][p["indices"]]["count"] // 3 for p in prims),
        "vertices": sum(document["accessors"][p["attributes"]["POSITION"]]["count"] for p in prims),
        "bind_min": [round(v, 3) for v in lo], "bind_max": [round(v, 3) for v in hi],
        "mesh_nodes": [chain(i) for i, n in enumerate(nodes) if "mesh" in n],
        "bones": [nodes[j]["name"] for j in skins[0]["joints"]] if skins else [],
        "clips": [f"{a['name']} {max(document['accessors'][s['input']]['max'][0] for s in a['samplers']):.3f}s"
                  for a in document.get("animations", [])],
        "images": [i.get("mimeType") for i in document.get("images", [])],
    }


# --------------------------------------------------------------------------- shared numerics

def segment_distance(points, a, b):
    """Distance from every point to segment a-b, and the 0..1 position of the closest point."""
    ab = b - a
    t = np.clip(((points - a) @ ab) / max(float(ab @ ab), 1e-12), 0.0, 1.0)
    return np.linalg.norm(points - (a + t[:, None] * ab), axis=1), t


def weld(points, triangles, eps):
    """Positions that coincide share one graph node, so a UV seam does not stop a weight blend."""
    _, first, inverse = np.unique(np.round(points / eps).astype(np.int64), axis=0,
                                  return_index=True, return_inverse=True)
    inverse = inverse.reshape(-1)
    tri = inverse[triangles]
    edges = np.concatenate([tri[:, [0, 1]], tri[:, [1, 2]], tri[:, [2, 0]]])
    edges = np.unique(np.sort(edges[edges[:, 0] != edges[:, 1]], axis=1), axis=0)
    return first, inverse, edges


def smooth(weights, edges, rounds):
    a, b = edges[:, 0], edges[:, 1]
    degree = np.maximum(np.bincount(np.concatenate([a, b]), minlength=len(weights)), 1)[:, None]
    for _ in range(rounds):
        total = np.zeros_like(weights)
        np.add.at(total, a, weights[b])
        np.add.at(total, b, weights[a])
        weights = 0.5 * weights + 0.5 * total / degree
    return weights


def top_four(weights):
    order = np.argsort(-weights, axis=1)[:, :4]
    kept = np.zeros_like(weights)
    rows = np.arange(len(weights))[:, None]
    kept[rows, order] = weights[rows, order]
    kept[kept < 0.01] = 0.0
    return kept / kept.sum(axis=1, keepdims=True)


def ease(u, points):
    """Piecewise smoothstep through (u, value) pairs."""
    for (u0, v0), (u1, v1) in zip(points, points[1:]):
        if u <= u1:
            t = min(max((u - u0) / (u1 - u0), 0.0), 1.0)
            return v0 + (v1 - v0) * t * t * (3 - 2 * t)
    return points[-1][1]


# --------------------------------------------------------------------------- dragon: landmarks

def auto_landmarks(g):
    """Rough landmarks from extremes of a mesh that faces +Z. Good enough to see what is wrong on
    the sheet's weight panel; every shipped dragon overrides them from the JSON file."""
    lo, hi = g.min(0), g.max(0)
    span = hi - lo
    core = g[np.abs(g[:, 0]) < 0.06 * span[0]]
    snout, tip = core[core[:, 2].argmax()], core[core[:, 2].argmin()]
    left = g[g[:, 0] > 0]
    wing_tip = left[left[:, 0].argmax()]
    low = left[left[:, 1] < lo[1] + 0.06 * span[1]]
    front = low[low[:, 2] > np.median(low[:, 2])].mean(0)
    back = low[low[:, 2] <= np.median(low[:, 2])].mean(0)
    spine_y = lo[1] + 0.48 * span[1]
    hip = np.array([0.0, spine_y, back[2] - 0.03 * span[2]])
    chest = np.array([0.0, spine_y + 0.03 * span[1], front[2]])
    head_base = np.array([0.0, snout[1] + 0.03 * span[1], snout[2] - 0.11 * span[2]])
    return {
        "hip": hip, "chest": chest, "head_base": head_base, "snout": snout,
        "tail": [hip + (tip - hip) * f for f in (0.25, 0.5, 0.75, 1.0)],
        "wing_root": np.array([0.06 * span[0], spine_y + 0.05 * span[1], chest[2]]), "wing_tip": wing_tip,
        "front_leg": [np.array([front[0], spine_y - 0.05 * span[1], front[2] - 0.02 * span[2]]), front],
        "back_leg": [np.array([back[0], spine_y - 0.05 * span[1], back[2] - 0.03 * span[2]]), back],
        "radius": 0.13 * span[1],
        "wing_mask": {"x_min": 0.08 * span[0], "line": [[0.08 * span[0], spine_y + 0.04 * span[1]],
                                                       [0.3 * span[0], spine_y - 0.05 * span[1]]]},
    }


def load_landmarks(path, asset_id, g):
    marks = auto_landmarks(g)
    authored = json.loads(Path(path).read_text()).get(asset_id, {}) if Path(path).exists() else {}
    for key, value in authored.items():
        marks[key] = value if key in ("wing_mask", "radius", "radii", "note") else np.array(value, float)
    marks["authored"] = sorted(authored)
    return marks


# --------------------------------------------------------------------------- dragon: build

def reset_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions, bpy.data.cameras):
        for item in list(block):
            block.remove(item)


def import_static(path):
    reset_scene()
    bpy.ops.import_scene.gltf(filepath=str(path))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for other in [o for o in bpy.context.scene.objects if o != obj]:
        bpy.data.objects.remove(other)
    return obj


def mesh_arrays(mesh):
    points = np.empty(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", points)
    mesh.calc_loop_triangles()
    triangles = np.empty(len(mesh.loop_triangles) * 3, np.int64)
    mesh.loop_triangles.foreach_get("vertices", triangles)
    return points.reshape(-1, 3), triangles.reshape(-1, 3)


def dragon_bones(marks, to_rig, length):
    """name -> (head, tail, parent) in armature space: x left, -y forward, z up, feet on z = 0."""
    def at(p, mirror=False):
        p = np.array(p, float) * ([-1, 1, 1] if mirror else [1, 1, 1])
        return to_rig(p)

    hip, chest = at(marks["hip"]), at(marks["chest"])
    bones = {"Root": (np.zeros(3), np.array([0, 0, 0.03 * length]), None),
             "Torso": (hip, chest, "Root"),
             "Neck": (chest, at(marks["head_base"]), "Torso"),
             "Head": (at(marks["head_base"]), at(marks["snout"]), "Neck")}
    previous, start = "Torso", hip
    for name, point in zip(TAIL, marks["tail"]):
        bones[name] = (start, at(point), previous)
        previous, start = name, at(point)
    for side in "LR":
        mirror = side == "R"
        if "wing" in marks:
            chain = [marks["wing_root"], *marks["wing"], marks["wing_tip"]]
        else:
            chain = [marks["wing_root"] + (marks["wing_tip"] - marks["wing_root"]) * f for f in (0, .25, .5, .75, 1)]
        previous = "Torso"
        for i in range(4):
            bones[f"Wing{i + 1}.{side}"] = (at(chain[i], mirror), at(chain[i + 1], mirror), previous)
            previous = f"Wing{i + 1}.{side}"
        bones[f"FrontLeg.{side}"] = (at(marks["front_leg"][0], mirror), at(marks["front_leg"][1], mirror), "Torso")
        bones[f"BackLeg.{side}"] = (at(marks["back_leg"][0], mirror), at(marks["back_leg"][1], mirror), "Torso")
    return bones


def wing_mask(points, marks, to_rig, scale, sign):
    """Vertices that belong to one wing: outside x_min, above a line drawn in the front view, and
    inside an optional forward/back window. Everything is authored in source coordinates."""
    spec = marks["wing_mask"]
    origin = to_rig(np.zeros(3))
    x = points[:, 0] * sign / scale                      # back to source units
    y = (points[:, 2] - origin[2]) / scale
    z = -(points[:, 1] - origin[1]) / scale
    (x1, y1), (x2, y2) = spec["line"]
    inside = (x > spec["x_min"]) & (y > y1 + (y2 - y1) * (x - x1) / (x2 - x1))
    inside &= (z > spec.get("z_min", -1e9)) & (z < spec.get("z_max", 1e9))
    for box in spec.get("exclude", []):                  # [x0, x1, y0, y1, z0, z1] source units
        inside &= ~((x > box[0]) & (x < box[1]) & (y > box[2]) & (y < box[3]) & (z > box[4]) & (z < box[5]))
    return inside


def dragon_weights(points, triangles, bones, marks, to_rig, scale, rung, rounds):
    names = list(bones)
    column = {n: i for i, n in enumerate(names)}
    radius = marks["radius"] * scale
    radii = {**RADIUS, **marks.get("radii", {})}
    weights = np.zeros((len(points), len(names)))

    body = [n for n in names if n not in WINGS and n != "Root"]
    scaled = []
    for name in body:
        head, tail, _ = bones[name]
        if name in LEGS:                                 # the top of a leg bone sits inside the trunk
            head = head + (tail - head) * 0.3
        distance, _ = segment_distance(points, head, tail)
        scaled.append(distance / (radius * radii["leg" if name in LEGS else name]))
    nearest = np.argmin(np.stack(scaled, axis=1), axis=1)
    weights[np.arange(len(points)), [column[body[i]] for i in nearest]] = 1.0

    for side, sign in (("L", 1.0), ("R", -1.0)):
        inside = wing_mask(points, marks, to_rig, scale, sign)
        chain = [f"Wing{i}.{side}" for i in range(1, 5)]
        best = np.full(len(points), np.inf)
        along = np.zeros(len(points))
        for i, name in enumerate(chain):
            distance, t = segment_distance(points, bones[name][0], bones[name][1])
            closer = distance < best
            best[closer], along[closer] = distance[closer], i + t[closer]
        weights[inside] = 0.0
        for i, name in enumerate(chain):                 # a hat per bone: the membrane curls, never creases
            hat = np.clip(1.0 - np.abs(along - (i + 0.5)), 0.0, 1.0)
            if i == 0:
                hat[along < 0.5] = 1.0
            if i == 3:
                hat[along > 3.5] = 1.0
            weights[inside, column[name]] = hat[inside]

    if rung == "C":
        for source, target in MINIMAL.items():
            weights[:, column[target]] += weights[:, column[source]]
            weights[:, column[source]] = 0.0
    if rung == "B":
        hard = np.zeros_like(weights)
        hard[np.arange(len(points)), weights.argmax(1)] = 1.0
        return names, hard
    first, inverse, edges = weld(points, triangles, 1e-4 * scale)
    return names, top_four(smooth(weights[first], edges, rounds)[inverse])


def build_armature(name, bones, skip=()):
    data = bpy.data.armatures.new(name)
    arm = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for bone_name, (head, tail, parent) in bones.items():
        if bone_name in skip:
            continue
        bone = data.edit_bones.new(bone_name)
        bone.head, bone.tail = head, tail
        while parent in skip:
            parent = bones[parent][2]
        if parent:
            bone.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def skin(obj, arm, names, weights):
    for column, name in enumerate(names):
        if name not in arm.data.bones:
            continue
        group = obj.vertex_groups.new(name=name)
        for index in np.nonzero(weights[:, column] > 0)[0]:
            group.add([int(index)], float(weights[index, column]), "REPLACE")
    obj.modifiers.new("Rig", "ARMATURE").object = arm
    obj.parent = arm


# --------------------------------------------------------------------------- dragon: clips

def dragon_pose(clip, u, size, drop, raised, droop, slump):
    """Every bone's offset from rest at 0..1 through a clip, as [rx, ry, rz, tx, ty, tz] in
    armature axes (x left, -y forward, z up) applied in the parent's moving frame.

    Rest is the generated standing pose. Every clip but Death starts and ends on the same hover
    pose, and every periodic term runs a whole number of cycles, so loops close exactly."""
    pose = {}
    mean = -0.75 * raised                                # beat about level, not about the raised rest

    def add(bone, rx=0.0, ry=0.0, rz=0.0, move=(0.0, 0.0, 0.0)):
        pose[bone] = pose.get(bone, np.zeros(6)) + np.array([rx, ry, rz, *move])

    w = 2 * math.pi * u
    fast = clip == "Fast_Flying"
    c = dict(neck=0.22, head=-0.18, neck_yaw=0.0, head_yaw=0.0, pitch=0.0, forward=0.0, up=0.0, roll=0.0,
             front=0.5, back=0.4, splay=0.0, mean=mean, amp=0.42, sweep=0.0, tail=0.09, sway=1.0)
    if fast:
        c.update(pitch=0.12, neck=0.36, head=-0.44, front=1.0, back=0.85, amp=0.54, sweep=0.18, tail=0.14)
    elif clip == "Punch":                                # rear back, bite and rake forward, recover
        s = ease(u, [(0, 0), (0.3, -1), (0.5, 1), (0.7, 0.3), (1, 0)])
        hit, wind = max(s, 0.0), max(-s, 0.0)
        c.update(neck=0.22 + 0.5 * hit - 0.4 * wind, head=-0.18 + 0.15 * hit + 0.25 * wind,
                 pitch=0.14 * hit - 0.1 * wind, forward=0.05 * hit - 0.02 * wind,
                 front=0.5 - 1.1 * hit + 0.3 * wind, mean=mean + 0.3 * wind - 0.25 * hit)
    elif clip == "Headbutt":                             # chin tucked, crown driven forward
        s = ease(u, [(0, 0), (0.35, -1), (0.5, 1), (0.65, 0.4), (1, 0)])
        hit, wind = max(s, 0.0), max(-s, 0.0)
        c.update(neck=0.22 + 0.45 * hit - 0.35 * wind, head=-0.18 + 0.55 * hit,
                 forward=0.07 * hit - 0.03 * wind, pitch=0.08 * hit, mean=mean + 0.2 * wind)
    elif clip == "HitReact":
        s = ease(u, [(0, 0), (0.25, 1), (1, 0)])
        c.update(pitch=-0.16 * s, forward=-0.03 * s, up=0.01 * s, roll=0.1 * s, neck=0.22 - 0.4 * s,
                 head=-0.18 + 0.25 * s, neck_yaw=0.25 * s, mean=mean + 0.3 * s, front=0.5 + 0.3 * s)
    elif clip == "Yes":
        nod = math.sin(2 * w) * math.sin(math.pi * u)
        c.update(head=-0.18 + 0.3 * nod, neck=0.22 + 0.12 * nod)
    elif clip == "No":
        shake = math.sin(2 * w) * math.sin(math.pi * u)
        c.update(head_yaw=0.42 * shake, neck_yaw=0.16 * shake)
    elif clip == "Death":                                # the wings give out and the body drops
        g = u ** 1.6
        c.update(up=-drop / size * g, roll=0.1 * g, pitch=0.04 * g, neck=0.22 + (slump - 0.22) * g, neck_yaw=0.3 * g,
                 head=-0.18 * (1 - g), front=0.5 - 1.3 * g, back=0.4 + 0.7 * g, splay=0.45 * g,
                 mean=mean + (droop - mean) * g, amp=0.42 * (1 - g), tail=0.09 + 0.1 * g, sway=1 - g)

    bob = (0.008 if fast else 0.012) * c["sway"]
    add("Torso", rx=c["pitch"] + 0.03 * c["sway"] * math.sin(w - 0.4), ry=c["roll"],
        move=(0.0, -c["forward"] * size, (c["up"] - bob * math.sin(w - 0.4)) * size))
    add("Neck", rx=c["neck"] + 0.04 * c["sway"] * math.sin(w + 0.5), rz=c["neck_yaw"])
    add("Head", rx=c["head"] - 0.03 * c["sway"] * math.sin(w + 0.5), rz=c["head_yaw"])
    for i, name in enumerate(TAIL):
        add(name, rx=c["tail"] + 0.05 * c["sway"] * math.sin(w - 0.6 * i),
            rz=0.07 * c["sway"] * math.sin(w - 0.8 * i + 1.0))
    for side, sign in (("L", 1.0), ("R", -1.0)):
        dangle = 0.06 * c["sway"] * math.sin(w - 1.0)
        add(f"FrontLeg.{side}", rx=c["front"] + dangle, ry=-sign * c["splay"])
        add(f"BackLeg.{side}", rx=c["back"] + dangle, ry=-sign * c["splay"])
        for i, share in enumerate((1.0, 0.55, 0.5, 0.45)):   # the beat reaches the tip late
            lift = (c["mean"] if i == 0 else 0.0) + c["amp"] * share * math.sin(w - 0.7 * i)
            add(f"Wing{i + 1}.{side}", ry=-sign * lift,
                rz=sign * (c["sweep"] + 0.08 * c["amp"] * math.cos(w)) if i == 0 else 0.0,
                rx=0.15 * c["amp"] * math.cos(w - 0.7 * i) if i < 2 else 0.0)
    return pose


def author_clips(arm, prefix, **shape):
    arm.animation_data_create()
    rest = {b.name: b.bone.matrix_local.to_3x3() for b in arm.pose.bones}
    still = np.zeros(6)
    for clip, frames in CLIPS.items():
        action = bpy.data.actions.new(f"{prefix}|{clip}")
        action.use_fake_user = True
        arm.animation_data.action = action
        last = {}
        for frame in range(frames + 1):
            pose = dragon_pose(clip, frame / frames, **shape)
            for bone in arm.pose.bones:
                channel = pose.get(bone.name, still)
                basis = rest[bone.name]
                turn = (basis.inverted() @ Euler(channel[:3], "XYZ").to_matrix() @ basis).to_quaternion()
                if bone.name in last and last[bone.name].dot(turn) < 0:
                    turn.negate()
                last[bone.name] = turn.copy()
                bone.rotation_mode = "QUATERNION"
                bone.rotation_quaternion = turn
                bone.location = basis.inverted() @ Vector(channel[3:])
                bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
                bone.keyframe_insert("location", frame=frame, group=bone.name)
    arm.animation_data.action = None
    clear_pose(arm)


def clear_pose(arm):
    for bone in arm.pose.bones:
        bone.location = (0, 0, 0)
        bone.rotation_quaternion = (1, 0, 0, 0)
        bone.rotation_euler = (0, 0, 0)
        bone.scale = (1, 1, 1)


def play(arm, action, frame):
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = action
    if action is not None and getattr(arm.animation_data, "action_slot", None) is None and action.slots:
        arm.animation_data.action_slot = action.slots[0]
    if action is None:
        clear_pose(arm)
    bpy.context.scene.frame_set(int(frame))
    bpy.context.view_layer.update()


def run_dragon(args):
    obj = import_static(args.src)
    obj.name = obj.data.name = "Dragon"
    mesh = obj.data
    points, triangles = mesh_arrays(mesh)
    source = np.stack([points[:, 0], points[:, 2], -points[:, 1]], axis=1)       # back to glTF axes
    marks = load_landmarks(args.landmarks, args.id, source)
    lo, hi = points.min(0), points.max(0)
    scale = args.length / (hi[1] - lo[1]) if args.length else args.height / (hi[2] - lo[2])
    centre_z = 0.5 * (marks["hip"][2] + marks["chest"][2])                       # source z: forward

    def to_rig(p):
        return np.array([p[0], -(p[2] - centre_z), p[1] - lo[2]]) * scale

    points = np.stack([points[:, 0], points[:, 1] + centre_z, points[:, 2] - lo[2]], axis=1) * scale
    mesh.vertices.foreach_set("co", points.ravel())
    mesh.update()
    length = (hi[1] - lo[1]) * scale

    bones = dragon_bones(marks, to_rig, length)
    skip = set()
    if args.rung == "C":                                 # one bone from chest to snout carries neck and head
        skip = set(MINIMAL)
        bones["Head"] = (bones["Neck"][0], bones["Head"][1], "Torso")
    names, weights = dragon_weights(points, triangles, bones, marks, to_rig, scale, args.rung, args.smooth)
    arm = build_armature("CharacterArmature", bones, skip)
    skin(obj, arm, names, weights)
    root = bpy.data.objects.new("RootNode", None)
    bpy.context.collection.objects.link(root)
    arm.parent = root

    # How far the rest wing is raised above level, and how far it must fall to lie on the ground
    # once the body has dropped: both come from the mesh, so the clips suit any generated pose.
    drop = 0.8 * min(bones["FrontLeg.L"][0][2], bones["BackLeg.L"][0][2])
    root = bones["Wing1.L"][0]
    wing = points[wing_mask(points, marks, to_rig, scale, 1.0)].mean(0) - root
    raised = math.atan2(wing[2], wing[0])
    reach_out = float(np.linalg.norm(bones["Wing4.L"][1] - root))
    droop = -raised - math.asin(min(1.0, max(0.0, (root[2] - drop) / reach_out)))
    neck = bones["Neck"][1] - bones["Neck"][0]
    lying = math.asin(min(1.0, max(0.0, (bones["Neck"][0][2] - drop - marks["radius"] * scale)
                                   / float(np.linalg.norm(neck)))))
    slump = math.atan2(neck[2], -neck[1]) + 0.7 * lying        # neck pitch that lays the head on the ground
    bpy.context.scene.render.fps = FPS
    author_clips(arm, "CharacterArmature", size=length, drop=drop, raised=raised, droop=droop, slump=slump)

    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=str(args.out), export_format="GLB", use_selection=True,
        export_yup=True, export_materials="EXPORT", export_animations=True,
        export_animation_mode="ACTIONS", export_extra_animations=True, export_apply=False,
        export_image_format="AUTO", export_optimize_animation_size=False)

    report = summary(args.out)
    report.update(rung=args.rung, wing_raised_deg=round(math.degrees(raised), 1), scale=round(scale, 4), authored_landmarks=marks["authored"],
                  size_m={"wingspan_x": round((hi[0] - lo[0]) * scale, 2),
                          "length": round(length, 2), "height": round((hi[2] - lo[2]) * scale, 2)},
                  head_bone_m=[round(float(v), 2) for v in bones["Head"][0]])
    if args.sheet:
        actions = {a.name.split("|")[-1]: a for a in bpy.data.actions}
        shots = [("rest", None, 0)] + [(f"{clip} {int(at * 100)}%", actions[clip], round(at * CLIPS[clip]))
                                        for clip, at in (("Flying_Idle", 0.25), ("Flying_Idle", 0.75),
                                                         ("Fast_Flying", 0.25), ("Fast_Flying", 0.75),
                                                         ("Punch", 0.3), ("Punch", 0.5), ("Headbutt", 0.5),
                                                         ("HitReact", 0.25), ("Death", 1.0), ("Yes", 0.375),
                                                         ("No", 0.375))]
        palette = np.array([colorsys.hsv_to_rgb((i * 0.381) % 1.0, 0.75, 0.95) for i in range(len(names))])
        paint(mesh, weights @ palette)
        panels = [("weights", lambda: play(arm, None, 0), "VERTEX")]
        panels += [(label, (lambda a=action, f=frame: play(arm, a, f)), "TEXTURE") for label, action, frame in shots]
        render_sheet(args.sheet, panels, points.min(0), points.max(0), args.cell)
    return report


# --------------------------------------------------------------------------- beast

BEAST_KNOTS = (("rump", "Tail1"), ("hip", "BackLeg.L"), ("shoulder", "FrontUpperLeg.L"), ("head", "Ear1.L"))
BEAST_PAWS = (("front_paw", "IKFrontLeg", "FF", "shoulder"), ("hind_paw", "IKBackLeg", "FFB", "hip"))


def soft(value):
    value = np.clip(value, 0.0, 1.0)
    return value * value * (3 - 2 * value)


def beast_warp(points, marks, joint, old):
    """Source mesh (y up, faces +Z) into the rig's bind space.

    1. Body fit: z is remapped piecewise through rump, hip, shoulder, ear base and nose so each
       lands on its bone; height is scaled by the same knots; width by the old body's width.
    2. Tail: bent about its root until it lies along the rig's tail chain, at one even scale.
    3. Legs: each column is sheared so the paw lands on the old paw, nothing moves at the joint."""
    ground = points[:, 1].min()
    old_nose, old_tip = old[old[:, 2].argmax()], old[old[:, 2].argmin()]
    if "rig_tail_tip" in marks:                          # the rig's rearmost vertex is not always its tail
        old_tip = marks["rig_tail_tip"]
    bones = {**dict(BEAST_KNOTS), **marks.get("knots", {})}   # a rig with no ears names another head bone
    knots = [(marks[m][2], joint[b][2], joint[b][1] / (marks[m][1] - ground)) for m, b in bones.items()]
    knots.append((marks["nose"][2], old_nose[2], old_nose[1] / (marks["nose"][1] - ground)))
    knots.sort()
    z_in, z_out, lift = (np.array(column) for column in zip(*knots))
    # Antlers make the widest point of a mesh something other than its body: author "widen" then.
    widen = marks.get("widen") or np.abs(old[:, 0]).max() / np.abs(points[:, 0]).max()

    def fit(p):
        p = np.atleast_2d(np.asarray(p, float))
        z = np.interp(p[:, 2], z_in, z_out)
        below, above = p[:, 2] < z_in[0], p[:, 2] > z_in[-1]
        z[below] = z_out[0] + (p[below, 2] - z_in[0]) * (z_out[1] - z_out[0]) / (z_in[1] - z_in[0])
        z[above] = z_out[-1] + (p[above, 2] - z_in[-1]) * (z_out[-1] - z_out[-2]) / (z_in[-1] - z_in[-2])
        return np.stack([p[:, 0] * widen, (p[:, 1] - ground) * np.interp(p[:, 2], z_in, lift), z], axis=1)

    out = fit(points)

    even = float(lift.mean())                            # one scale for parts that must keep their shape

    (za, ya), (zb, yb) = marks["tail_line"]              # side view: the tail is behind this line
    behind = (za + (points[:, 1] - ya) * (zb - za) / (yb - ya)) - points[:, 2]
    tail = soft(behind / marks.get("tail_blend", 0.08))
    pivot = fit(marks["rump"])[0]
    have, want = marks["tail_tip"] - marks["rump"], old_tip - joint["Tail1"]
    turn = math.atan2(want[1], want[2]) - math.atan2(have[1], have[2])
    turn = ((turn + math.pi) % (2 * math.pi) - math.pi) * tail      # the short way round
    y = out[:, 1] - pivot[1]
    z = out[:, 2] - pivot[2]
    y += tail * (even * (points[:, 1] - marks["rump"][1]) - y)
    z += tail * (even * (points[:, 2] - marks["rump"][2]) - z)
    out[:, 1] = pivot[1] + y * np.cos(turn) + z * np.sin(turn)
    out[:, 2] = pivot[2] + z * np.cos(turn) - y * np.sin(turn)

    reach = marks.get("leg_window", 0.16)                # source units, fore and aft of the paw
    for paw, ankle, toe, top in BEAST_PAWS:
        for side, sign in (("L", 1.0), ("R", -1.0)):
            target = 0.5 * (joint[f"{ankle}.{side}"] + joint[f"{toe}.{side}"])
            mark = marks[paw] * [sign, 1, 1]
            source = fit(mark)[0]
            height = fit(marks[top])[0][1]
            near = soft(1.8 - np.abs(points[:, 2] - mark[2]) / reach) * (points[:, 0] * sign > 0)
            pull = near * soft((height - out[:, 1]) / (0.6 * height)) * (1.0 - tail)
            thick = source[2] + even * (points[:, 2] - mark[2])     # the limb keeps its own thickness
            out[:, 2] += pull * (thick - out[:, 2] + target[2] - source[2])
            out[:, 0] += pull * (target[0] - source[0])
    return out


def vertex_normals(points, triangles, inverse, count, like):
    a, b, c = (points[triangles[:, k]] for k in range(3))
    face = np.cross(b - a, c - a)
    total = np.zeros((count, 3))
    for k in range(3):
        np.add.at(total, inverse[triangles[:, k]], face)
    normal = total[inverse]
    normal /= np.maximum(np.linalg.norm(normal, axis=1, keepdims=True), 1e-12)
    return normal if (normal * like).sum() >= 0 else -normal


def run_beast(args):
    rig, rig_bin = read_glb(args.rig)
    src, src_bin = read_glb(args.src)
    marks = {k: (np.array(v, float) if isinstance(v, list) and k != "rigid" else v)
             for k, v in json.loads(Path(args.landmarks).read_text())[args.id].items()}
    skin = rig["skins"][0]
    names = [rig["nodes"][j]["name"] for j in skin["joints"]]
    bind = np.linalg.inv(accessor(rig, rig_bin, skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1))
    joint = {name: bind[i][:3, 3] for i, name in enumerate(names)}
    node = next(n for n in rig["nodes"] if "skin" in n and "mesh" in n)
    old_points, old_weights = [], []
    for prim in rig["meshes"][node["mesh"]]["primitives"]:
        attrs = prim["attributes"]
        position = accessor(rig, rig_bin, attrs["POSITION"])
        dense = np.zeros((len(position), len(names)))
        np.add.at(dense, (np.arange(len(position))[:, None], accessor(rig, rig_bin, attrs["JOINTS_0"])),
                  accessor(rig, rig_bin, attrs["WEIGHTS_0"]).astype(float))
        old_points.append(position)
        old_weights.append(dense)
    old_points, old_weights = np.concatenate(old_points).astype(float), np.concatenate(old_weights)

    prims = src["meshes"][0]["primitives"]
    if len(prims) != 1:
        raise SystemExit(f"{args.src}: expected one primitive, found {len(prims)}")
    attrs = prims[0]["attributes"]
    points = accessor(src, src_bin, attrs["POSITION"]).astype(float)
    triangles = accessor(src, src_bin, prims[0]["indices"]).reshape(-1, 3).astype(np.int64)
    warped = beast_warp(points, marks, joint, old_points)

    # Weights come from the old mesh: the nearest old vertex on the same side of the body.
    gap = np.linalg.norm(warped[:, None, :] - old_points[None, :, :], axis=2)
    gap += 1e3 * ((warped[:, None, 0] * old_points[None, :, 0] < 0)
                  & (np.abs(warped[:, None, 0]) > 0.03) & (np.abs(old_points[None, :, 0]) > 0.03))
    first, inverse, edges = weld(warped, triangles, 1e-5)
    weights = smooth(old_weights[gap.argmin(1)][first], edges, args.smooth)[inverse]
    # Antlers hang back over the spine, so their nearest old vertex is the back and they would tear
    # off the skull when the neck moves. Everything above an authored side-view line (source z, y)
    # follows one bone rigidly instead.
    for rule in marks.get("rigid", []):
        (za, ya), (zb, yb) = rule["line"]
        above = points[:, 1] > ya + (points[:, 2] - za) * (yb - ya) / (zb - za)
        weights[above] = 0.0
        weights[above, names.index(rule["bone"])] = 1.0
    weights = top_four(weights)
    order = np.argsort(-weights, axis=1)[:, :4]
    rows = np.arange(len(weights))[:, None]
    normals = vertex_normals(warped, triangles, inverse, len(first), accessor(src, src_bin, attrs["NORMAL"]))

    # Rebuild the buffer: the rig's inverse binds and animation data are copied byte for byte.
    document = json.loads(json.dumps(rig))
    binary, views, accessors, moved_views, moved = bytearray(), [], [], {}, {}

    def add_view(data):
        binary.extend(b"\0" * (-len(binary) % 4))
        views.append({"buffer": 0, "byteOffset": len(binary), "byteLength": len(data)})
        binary.extend(data)
        return len(views) - 1

    def keep(index):
        if index not in moved:
            spec = dict(rig["accessors"][index])
            view = rig["bufferViews"][spec["bufferView"]]
            if spec["bufferView"] not in moved_views:
                start = view.get("byteOffset", 0)
                moved_views[spec["bufferView"]] = add_view(rig_bin[start:start + view["byteLength"]])
                if "byteStride" in view:
                    views[-1]["byteStride"] = view["byteStride"]
            spec["bufferView"] = moved_views[spec["bufferView"]]
            accessors.append(spec)
            moved[index] = len(accessors) - 1
        return moved[index]

    def add(array, component, kind, bounds=False):
        array = np.ascontiguousarray(array, dtype="<" + COMPONENT[component])
        spec = {"bufferView": add_view(array.tobytes()), "componentType": component,
                "count": len(array), "type": kind}
        if bounds:
            spec["min"], spec["max"] = array.min(0).tolist(), array.max(0).tolist()
        accessors.append(spec)
        return len(accessors) - 1

    for item in document["skins"]:
        item["inverseBindMatrices"] = keep(item["inverseBindMatrices"])
    for animation in document.get("animations", []):
        for sampler in animation["samplers"]:
            sampler["input"], sampler["output"] = keep(sampler["input"]), keep(sampler["output"])
    primitive = {"attributes": {
        "POSITION": add(warped, 5126, "VEC3", bounds=True), "NORMAL": add(normals, 5126, "VEC3"),
        "TEXCOORD_0": add(accessor(src, src_bin, attrs["TEXCOORD_0"]), 5126, "VEC2"),
        "JOINTS_0": add(order, 5123, "VEC4"), "WEIGHTS_0": add(weights[rows, order], 5126, "VEC4")},
        "indices": add(triangles.reshape(-1, 1), 5125, "SCALAR"), "material": 0}
    for key in ("materials", "textures", "samplers", "images"):
        document[key] = json.loads(json.dumps(src.get(key, [])))
    for image in document["images"]:
        view = src["bufferViews"][image["bufferView"]]
        start = view.get("byteOffset", 0)
        image["bufferView"] = add_view(src_bin[start:start + view["byteLength"]])
    document["materials"][0].setdefault("name", args.id)
    old_mesh = rig["meshes"][node["mesh"]]
    document["meshes"] = [{"name": old_mesh.get("name", args.id), "primitives": [primitive]}]
    for other in document["nodes"]:                      # a bone-parented extra of the old body (a stag's
        if "mesh" in other and "skin" not in other:      # antlers) points at a mesh that no longer exists
            del other["mesh"]
    next(n for n in document["nodes"] if "skin" in n and "mesh" in n)["mesh"] = 0
    document["accessors"], document["bufferViews"] = accessors, views
    document["buffers"] = [{"byteLength": len(binary)}]
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    write_glb(args.out, document, bytes(binary))

    report = summary(args.out)
    report.update(old_bind_min=old_points.min(0).round(3).tolist(), old_bind_max=old_points.max(0).round(3).tolist(),
                  nearest_old_vertex_m={"median": round(float(np.median(gap.min(1))), 3),
                                        "p95": round(float(np.percentile(gap.min(1), 95)), 3),
                                        "max": round(float(gap.min(1).max()), 3)})
    if args.sheet:
        sheet_rigged(args.out, args.sheet, args.clips.split(","), args.cell)
    return report


def sheet_rigged(path, sheet, clips, cell):
    """Import any rigged .glb as written and render rest plus four moments of each named clip."""
    reset_scene()
    try:
        bpy.ops.import_scene.gltf(filepath=str(path), disable_bone_shape=True)
    except TypeError:
        bpy.ops.import_scene.gltf(filepath=str(path))
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    for obj in [o for o in bpy.context.scene.objects if o.type == "MESH" and o.find_armature() != arm]:
        bpy.data.objects.remove(obj)                     # importer helpers such as the bone-shape icosphere
    bpy.context.view_layer.update()
    corners = np.array([obj.matrix_world @ Vector(c) for obj in bpy.context.scene.objects
                        if obj.type == "MESH" for c in obj.bound_box])
    actions = {a.name.split("|")[-1]: a for a in bpy.data.actions}
    panels = [("rest", lambda: play(arm, None, 0), "TEXTURE")]
    for clip in clips:
        if clip not in actions:
            raise SystemExit(f"{path}: no clip {clip}; has {sorted(actions)}")
        action = actions[clip]
        start, end = action.frame_range
        for at in (0.0, 0.25, 0.5, 0.75):
            panels.append((f"{clip} {int(at * 100)}%", (lambda a=action, f=start + (end - start) * at: play(arm, a, f)),
                           "TEXTURE"))
    render_sheet(sheet, panels, corners.min(0), corners.max(0), cell)


# --------------------------------------------------------------------------- sheet

def paint(mesh, colours):
    layer = mesh.color_attributes.new("rebind_debug", "FLOAT_COLOR", "POINT")
    layer.data.foreach_set("color", np.concatenate([colours, np.ones((len(colours), 1))], axis=1).ravel())
    mesh.color_attributes.active_color = layer


def render_sheet(path, panels, lo, hi, cell=(480, 360), views=None):
    """panels: (label, pose callback, 'TEXTURE' | 'VERTEX'). One row holds two panels, three views
    each: front, side and three-quarter. The orange box is 1.8 m tall."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x, scene.render.resolution_y = cell
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.show_cavity = True
    scene.display.shading.show_backface_culling = False
    scene.world = scene.world or bpy.data.worlds.new("Sheet")
    scene.world.color = (0.62, 0.64, 0.68)

    stand = np.array([lo[0] - 0.6, lo[1] - 0.6, 0.9])      # beside the nose, clear of every view
    lo, hi = np.minimum(lo, stand - 0.3), np.maximum(hi, stand + [0.3, 0.3, 0.9])
    span = hi - lo
    centre = Vector((lo + hi) / 2)
    reach = 1.2 * max(span[0], span[1], span[2] * cell[0] / cell[1])
    camera = bpy.data.objects.new("SheetCamera", bpy.data.cameras.new("SheetCamera"))
    camera.data.type, camera.data.ortho_scale = "ORTHO", reach
    camera.data.clip_start, camera.data.clip_end = 0.01, reach * 10
    scene.collection.objects.link(camera)
    scene.camera = camera
    label = bpy.data.objects.new("SheetLabel", bpy.data.curves.new("SheetLabel", "FONT"))
    label.data.size = reach * 0.045
    label.parent = camera
    label.location = (-0.48 * reach, 0.5 * reach * cell[1] / cell[0] - 0.06 * reach, -1.0)
    scene.collection.objects.link(label)
    ink = bpy.data.materials.new("SheetInk")
    ink.diffuse_color = (0.0, 0.0, 0.0, 1.0)
    label.data.materials.append(ink)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=tuple(stand))
    box = bpy.context.active_object
    box.name, box.scale = "Reference_1p8m", (0.45, 0.25, 1.8)
    paint_mat = bpy.data.materials.new("Reference")
    paint_mat.diffuse_color = (1.0, 0.35, 0.0, 1.0)
    box.data.materials.append(paint_mat)

    views = views or [("front", (0, -1, 0)), ("side", (1, 0, 0)), ("3/4", (0.62, -0.62, 0.48))]
    tiles = []
    with tempfile.TemporaryDirectory() as folder:
        for name, pose, colour in panels:
            pose()
            scene.display.shading.color_type = colour
            for view, direction in views:
                direction = Vector(direction).normalized()
                camera.location = centre + direction * reach * 3
                camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
                label.data.body = f"{name} | {view}"
                scene.render.filepath = str(Path(folder) / "tile.png")
                bpy.ops.render.render(write_still=True)
                image = bpy.data.images.load(scene.render.filepath)
                pixels = np.empty(cell[0] * cell[1] * 4, np.float32)
                image.pixels.foreach_get(pixels)
                bpy.data.images.remove(image)
                tiles.append(pixels.reshape(cell[1], cell[0], 4))
    columns = 2 * len(views)
    rows = math.ceil(len(tiles) / columns)
    sheet = np.zeros((rows * cell[1], columns * cell[0], 4), np.float32)
    sheet[..., 3] = 1.0
    for index, tile in enumerate(tiles):                 # image rows run bottom-up
        row, column = divmod(index, columns)
        sheet[(rows - 1 - row) * cell[1]:(rows - row) * cell[1], column * cell[0]:(column + 1) * cell[0]] = tile
    out = bpy.data.images.new("Sheet", columns * cell[0], rows * cell[1], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw, out.file_format = str(Path(path).resolve()), "PNG"
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    out.save()


# --------------------------------------------------------------------------- entry

def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--mode", choices=("dragon", "beast", "sheet"), required=True)
    parser.add_argument("--src", required=True,
                        help="generated static mesh (.glb, faces +Z, y up); sheet mode: any rigged .glb")
    parser.add_argument("--out", help="output .glb (dragon, beast)")
    parser.add_argument("--clips", default="Walk,Attack", help="beast and sheet: clips to render")
    parser.add_argument("--sheet", help="PNG contact sheet to render")
    parser.add_argument("--id", help="landmark key; default is the source file's stem up to the first dot")
    parser.add_argument("--landmarks", default=str(HERE / "rebind_creature_landmarks.json"))
    parser.add_argument("--length", type=float, help="dragon: nose to tail, metres")
    parser.add_argument("--height", type=float, help="dragon: ground to highest point, metres")
    parser.add_argument("--rung", choices=("A", "B", "C"), default="A",
                        help="dragon: A smooth weights, B rigid segments, C minimal six-bone rig")
    parser.add_argument("--smooth", type=int, default=8, help="weight smoothing rounds over the mesh graph")
    parser.add_argument("--cell", type=lambda v: tuple(int(n) for n in v.split("x")), default=(480, 360),
                        help="sheet tile size, WxH")
    parser.add_argument("--rig", help="beast: the existing rigged .glb whose skeleton and clips are kept")
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    args = parser.parse_args(argv)
    if bpy is None:
        parser.error("run inside Blender: blender --background --factory-startup --python tools/rebind_creature.py -- ...")
    args.id = args.id or Path(args.src).name.split(".")[0]
    if args.mode == "sheet":
        if not args.sheet:
            parser.error("--mode sheet needs --sheet")
        sheet_rigged(args.src, args.sheet, args.clips.split(","), args.cell)
        report = summary(args.src)
    elif not args.out:
        parser.error(f"--mode {args.mode} needs --out")
    elif args.mode == "dragon":
        if not (args.length or args.height):
            parser.error("--mode dragon needs --length or --height")
        report = run_dragon(args)
    else:
        if not args.rig:
            parser.error("--mode beast needs --rig")
        report = run_beast(args)
    print("REBIND_REPORT " + json.dumps(report))


if __name__ == "__main__":
    main()
