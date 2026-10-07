#!/usr/bin/env python3
"""Write the procedural ground-cover meshes: a grass clump and a fern rosette.

Neither is Quaternius or Meshy. Both are deterministic pure-python glTF 2.0 binaries (stdlib
only, no Blender, no Godot) built to the same contract as the shipping `prp_grass_*` meshes so
they drop into the existing `WorldBiomeScatter` layers unchanged:

    prp_grass_clump_a.glb   44 tapered, curved blades spread over a 0.6 m root patch, widths crossed so
                            the planes interleave. 126 triangles, 0.70 m tall, origin at the base.
    prp_fern_clump_a.glb    12 broad arching fronds in three rings, five V-folded segments each.
                            216 triangles, 0.80 m tall, origin at the base.

What the scatter path reads from a mesh, and what is matched here:
  * `WorldBiomeScatter.WeatherMaterial` turns an opaque StandardMaterial3D into world_scatter's
    ShaderMaterial and gives it wind when the SCENE PATH contains "grass" or "fern", so the file
    names carry those words. Wind weight is VERTEX.y, not a vertex attribute: the root must sit at
    y = 0 and the tips above it, which the origin-at-base rule already gives.
  * The shader multiplies albedo texture * COLOR_0 * the layer's per-instance tint, and applies the
    layer `Saturation` to the TEXTURE only. So, like the shipping grass, the hue lives in the
    shared `T_Nature_Grass.png` gradient column (UV u ~ 0.19, v 1 = dark base green, v 0 = pale
    tip) and COLOR_0 is a GREY ramp, dark at the root to white at the tip, that adds the root
    shading. Baking a green into COLOR_0 would make `Saturation = 0.35` (dry grass) do nothing.
  * Material is double sided and opaque, as the shipping grass is (the shader is cull_disabled).
  * Grass normals are exactly +Y, as the shipping grass: every blade shades like the ground under
    it. Fern normals are blended 65% toward +Y, as the shipping fern's mostly-up normals are.

Usage:
    python tools/gen_ground_cover.py                 # write both into artifacts/meshy-overhaul/procedural/
    python tools/gen_ground_cover.py --check         # build in memory, re-parse, assert; writes nothing
    python tools/gen_ground_cover.py --out DIR --tex-uri ../props/T_Nature_Grass.png

The texture is referenced by relative URI (default `T_Nature_Grass.png`, which resolves when the
files sit in assets/models/props/ next to it). Use --tex-uri when installing elsewhere.
"""
from __future__ import annotations

import argparse
import json
import math
import random
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUT = ROOT / "artifacts" / "meshy-overhaul" / "procedural"
PROPS = ROOT / "assets" / "models" / "props"
# The layout each build is compared with is the installed copy of the same mesh: the retired models
# these two were first modelled on are gone, and what must not drift is the attribute layout the
# scatter shader already draws.
REFERENCE_GRASS = PROPS / "prp_grass_clump_a.glb"
REFERENCE_FERN = PROPS / "prp_fern_clump_a.glb"

GRASS_HEIGHT = 1.00
GRASS_BUDGET = 320
FERN_HEIGHT = 0.80
FERN_BUDGET = 250
UP = (0.0, 1.0, 0.0)

# ----------------------------------------------------------------------------------------------
# vector helpers
# ----------------------------------------------------------------------------------------------


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def mul(a, k):
    return (a[0] * k, a[1] * k, a[2] * k)


def norm(a):
    n = math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]) or 1.0
    return (a[0] / n, a[1] / n, a[2] / n)


def lerp(a, b, t):
    return a + (b - a) * t


class Mesh:
    """Flat vertex streams plus a triangle list; one primitive, one material."""

    def __init__(self) -> None:
        self.pos: list[tuple[float, float, float]] = []
        self.nrm: list[tuple[float, float, float]] = []
        self.col: list[float] = []   # grey ramp, written as COLOR_0 rgb (a = 1)
        self.uv: list[tuple[float, float]] = []
        self.tri: list[tuple[int, int, int]] = []

    def vert(self, p, n, grey, uv) -> int:
        self.pos.append(p)
        self.nrm.append(n)
        self.col.append(grey)
        self.uv.append(uv)
        return len(self.pos) - 1


def rescale_to_height(mesh: Mesh, height: float) -> None:
    """Uniformly scale so the highest vertex sits exactly at `height` (the root stays at y = 0)."""
    k = height / max(p[1] for p in mesh.pos)
    mesh.pos = [mul(p, k) for p in mesh.pos]


# ----------------------------------------------------------------------------------------------
# grass clump
# ----------------------------------------------------------------------------------------------

BLADES = 44
BLADE_ROWS = 4                      # rows of two verts, then a single tip vertex: 2*4 - 1 = 7 tris
BLADE_WIDTH = (1.0, 0.93, 0.78, 0.50)   # taper, as a fraction of the root half-width
BLADE_T = (0.0, 0.25, 0.5, 0.75)        # row heights as a fraction of the blade
# A tuft, not a spike: the roots spread over a patch about as wide as the tuft is tall, so one
# instance covers the ground the way the grass it replaced did. The root is shaded, not black: the
# open fan shows its lower blades, and a black root reads as a dark spike on pale ground.
TUFT_RADIUS = 0.30
ROOT_SHADE = 0.34


def build_grass(seed: int = 7) -> Mesh:
    rng = random.Random(seed)
    m = Mesh()
    for i in range(BLADES):
        # golden-angle fan so the blades never stack, root radius packed toward the middle
        phi = i * 2.399963 + rng.uniform(-0.25, 0.25)
        radius = TUFT_RADIUS * math.sqrt((i + 0.5) / BLADES)
        base = (radius * math.cos(phi), 0.0, radius * math.sin(phi))
        # central blades stand tallest, outer blades are shorter and lean further out
        rim = radius / TUFT_RADIUS
        height = rng.uniform(0.70, 1.0) * lerp(1.0, 0.45, rim)
        lean = rng.uniform(0.10, 0.22) + 0.30 * rim
        psi = phi + rng.uniform(-0.45, 0.45)
        lean_dir = (math.cos(psi), 0.0, math.sin(psi))
        # width axis: horizontal, twisted off the lean plane so neighbouring blades cross
        twist = psi + math.pi / 2 + rng.uniform(-0.6, 0.6)
        side = (math.cos(twist), 0.0, math.sin(twist))
        half = rng.uniform(0.038, 0.062)
        bright = rng.uniform(0.82, 1.0)
        v_tip = rng.uniform(0.02, 0.28)     # shorter blades stop short of the palest texture row

        def at(t: float):
            # quadratic bend: straight at the root, curving over toward the tip
            return add(base, add(mul(lean_dir, lean * height * t * t), (0.0, height * t, 0.0)))

        def shade(t: float) -> float:
            return min(1.0, (ROOT_SHADE + (1.0 - ROOT_SHADE) * t ** 1.2) * bright) if t < 1.0 else 1.0

        def v_of(t: float) -> float:
            return 0.99 - (0.99 - v_tip) * t

        rows = []
        for t, w in zip(BLADE_T, BLADE_WIDTH):
            c = at(t)
            g = shade(t)
            rows.append((
                m.vert(add(c, mul(side, -half * w)), UP, g, (0.18, v_of(t))),
                m.vert(add(c, mul(side, half * w)), UP, g, (0.20, v_of(t))),
            ))
        tip = m.vert(at(1.0), UP, 1.0, (0.19, v_tip))
        for (l0, r0), (l1, r1) in zip(rows, rows[1:]):
            m.tri.append((l0, r0, r1))
            m.tri.append((l0, r1, l1))
        m.tri.append((rows[-1][0], rows[-1][1], tip))
    rescale_to_height(m, GRASS_HEIGHT)
    return m


# ----------------------------------------------------------------------------------------------
# fern rosette
# ----------------------------------------------------------------------------------------------

FRONDS = 12
FROND_ROWS = 5                              # rows of three verts (edge, rib, edge), then a tip
FROND_WIDTH = (0.50, 1.0, 0.76, 0.92, 0.46)  # half-width profile, a notch after the first lobe
FROND_FOLD = 0.28                           # edges ride this fraction of their width above the rib
# per ring: (launch angle off vertical, length, extra droop along the frond), radians
FERN_RINGS = ((0.22, 0.78, 0.75), (0.55, 0.80, 1.05), (0.92, 0.68, 1.10))


def build_fern(seed: int = 11) -> Mesh:
    rng = random.Random(seed)
    m = Mesh()
    for i in range(FRONDS):
        theta0, length, droop = FERN_RINGS[i % 3]
        theta0 += rng.uniform(-0.10, 0.10)
        length *= rng.uniform(0.88, 1.08)
        alpha = i * 2.399963 + rng.uniform(-0.15, 0.15)
        out = (math.cos(alpha), 0.0, math.sin(alpha))
        side0 = (-math.sin(alpha), 0.0, math.cos(alpha))
        roll = rng.uniform(-0.2, 0.2)
        wmax = rng.uniform(0.060, 0.075)
        bright = rng.uniform(0.80, 1.0)
        centre = mul(out, 0.03)

        verts, step = [], length / FROND_ROWS
        for r in range(FROND_ROWS + 1):
            s = r / FROND_ROWS
            theta = theta0 + droop * s ** 1.3
            # tangent and the upper-surface normal, both in the frond's vertical plane
            tang = (math.sin(theta) * out[0], math.cos(theta), math.sin(theta) * out[2])
            n0 = (-math.cos(theta) * out[0], math.sin(theta), -math.cos(theta) * out[2])
            cr, sr = math.cos(roll), math.sin(roll)
            side = add(mul(side0, cr), mul(n0, sr))
            n = add(mul(n0, cr), mul(side0, -sr))
            grey = min(1.0, (0.10 + 0.90 * s ** 1.2) * bright)
            v = 0.985 - 0.50 * s
            if r == FROND_ROWS:
                verts.append((m.vert(centre, norm(add(mul(n, 0.35), mul(UP, 0.65))), grey, (0.19, v)),))
                break
            w = wmax * FROND_WIDTH[r]
            lift = mul(n, FROND_FOLD * w)
            row = []
            for u, offset, tilt in ((0.18, -w, 0.2), (0.19, 0.0, 0.0), (0.20, w, -0.2)):
                p = add(centre, add(mul(side, offset), lift if offset else (0.0, 0.0, 0.0)))
                shading = norm(add(mul(add(n, mul(side, tilt)), 0.35), mul(UP, 0.65)))
                row.append(m.vert(p, shading, grey, (u, v)))
            verts.append(tuple(row))
            centre = add(centre, mul(tang, step))
        for (l0, m0, r0), (l1, m1, r1) in zip(verts[:-2], verts[1:-1]):
            m.tri += [(l0, m0, m1), (l0, m1, l1), (m0, r0, r1), (m0, r1, m1)]
        (l, mid, r), (tip,) = verts[-2], verts[-1]
        m.tri += [(l, mid, tip), (mid, r, tip)]
    rescale_to_height(m, FERN_HEIGHT)
    return m


# ----------------------------------------------------------------------------------------------
# glTF writer
# ----------------------------------------------------------------------------------------------

FLOAT, USHORT = 5126, 5123
ARRAY_BUFFER, ELEMENT_ARRAY_BUFFER = 34962, 34963


def to_glb(mesh: Mesh, node_name: str, material: dict, tex_uri: str, image_name: str) -> bytes:
    """Serialise to a binary glTF laid out like the shipping grass: one primitive, attributes
    COLOR_0 (ushort normalized vec4), POSITION, NORMAL, TEXCOORD_0 (float), ushort indices, one
    material with one external base-colour texture."""
    n = len(mesh.pos)
    if n > 65535:
        raise ValueError("ushort indices cannot address this mesh")
    colour = b"".join(struct.pack("<4H", g16, g16, g16, 65535)
                      for g16 in (round(min(1.0, max(0.0, g)) * 65535) for g in mesh.col))
    streams = [
        ("COLOR_0", colour, USHORT, "VEC4", True, ARRAY_BUFFER, None),
        ("POSITION", b"".join(struct.pack("<3f", *p) for p in mesh.pos), FLOAT, "VEC3", False, ARRAY_BUFFER, mesh.pos),
        ("NORMAL", b"".join(struct.pack("<3f", *v) for v in mesh.nrm), FLOAT, "VEC3", False, ARRAY_BUFFER, None),
        ("TEXCOORD_0", b"".join(struct.pack("<2f", *t) for t in mesh.uv), FLOAT, "VEC2", False, ARRAY_BUFFER, None),
    ]
    flat = [i for tri in mesh.tri for i in tri]
    streams.append(("indices", struct.pack(f"<{len(flat)}H", *flat), USHORT, "SCALAR", False, ELEMENT_ARRAY_BUFFER, None))

    blob, views, accessors, attributes = bytearray(), [], [], {}
    for name, data, ctype, kind, normalized, target, bounds in streams:
        views.append({"buffer": 0, "byteOffset": len(blob), "byteLength": len(data), "target": target})
        blob += data
        blob += b"\0" * (-len(blob) % 4)
        acc = {"bufferView": len(views) - 1, "componentType": ctype, "count": len(data) // _width(ctype, kind), "type": kind}
        if normalized:
            acc["normalized"] = True
        if bounds:
            # As float32, the values actually stored: a strict glTF validator compares them exactly.
            stored = [struct.unpack("<3f", struct.pack("<3f", *p)) for p in bounds]
            acc["min"] = [min(p[a] for p in stored) for a in range(3)]
            acc["max"] = [max(p[a] for p in stored) for a in range(3)]
        accessors.append(acc)
        if name != "indices":
            attributes[name] = len(accessors) - 1

    doc = {
        "asset": {"version": "2.0", "generator": "tools/gen_ground_cover.py"},
        "scene": 0,
        "scenes": [{"name": "Scene", "nodes": [0]}],
        "nodes": [{"mesh": 0, "name": node_name}],
        "materials": [material],
        "meshes": [{"name": node_name, "primitives": [{
            "attributes": attributes, "indices": len(accessors) - 1, "material": 0}]}],
        "textures": [{"sampler": 0, "source": 0}],
        "images": [{"name": image_name, "uri": tex_uri}],
        "samplers": [{"magFilter": 9729, "minFilter": 9987}],
        "bufferViews": views,
        "accessors": accessors,
        "buffers": [{"byteLength": len(blob)}],
    }
    js = json.dumps(doc, separators=(",", ":")).encode()
    js += b" " * (-len(js) % 4)
    body = struct.pack("<II", len(js), 0x4E4F534A) + js + struct.pack("<II", len(blob), 0x004E4942) + bytes(blob)
    return struct.pack("<III", 0x46546C67, 2, 12 + len(body)) + body


def _width(ctype: int, kind: str) -> int:
    return {FLOAT: 4, USHORT: 2}[ctype] * {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[kind]


def material(name: str, roughness: float) -> dict:
    return {"name": name, "doubleSided": True, "pbrMetallicRoughness": {
        "baseColorTexture": {"index": 0}, "metallicFactor": 0.0, "roughnessFactor": roughness}}


# name -> (builder, node name, material, image name, triangle budget, height)
ASSETS = {
    "prp_grass_clump_a": (build_grass, "Grass_Clump_A", material("Grass", 0.82), "Grass", GRASS_BUDGET, GRASS_HEIGHT),
    "prp_fern_clump_a": (build_fern, "Fern_Clump_A", material("Fern", 0.90), "Grass", FERN_BUDGET, FERN_HEIGHT),
}


def build_all(tex_uri: str) -> dict[str, bytes]:
    return {
        f"{name}.glb": to_glb(builder(), node, mat, tex_uri, image)
        for name, (builder, node, mat, image, _budget, _height) in ASSETS.items()
    }


# ----------------------------------------------------------------------------------------------
# re-parse and verify
# ----------------------------------------------------------------------------------------------


def parse_glb(data: bytes) -> dict:
    """Read a .glb back into plain python: the document plus decoded attribute lists."""
    magic, _version, length = struct.unpack_from("<III", data, 0)
    if magic != 0x46546C67 or length != len(data):
        raise ValueError("not a binary glTF, or the header length is wrong")
    (jlen, jtype) = struct.unpack_from("<II", data, 12)
    doc = json.loads(data[20:20 + jlen])
    boff = 20 + jlen
    (blen, btype) = struct.unpack_from("<II", data, boff)
    if jtype != 0x4E4F534A or btype != 0x004E4942:
        raise ValueError("chunk order is not JSON then BIN")
    blob = data[boff + 8:boff + 8 + blen]
    prim = doc["meshes"][0]["primitives"][0]

    def read(index: int) -> list[tuple]:
        a = doc["accessors"][index]
        v = doc["bufferViews"][a["bufferView"]]
        width = _width(a["componentType"], a["type"])
        fmt = "<" + {FLOAT: "f", USHORT: "H"}[a["componentType"]] * (width // {FLOAT: 4, USHORT: 2}[a["componentType"]])
        return [struct.unpack_from(fmt, blob, v.get("byteOffset", 0) + k * width) for k in range(a["count"])]

    out = {"doc": doc, "prim": prim, "attrs": {k: read(i) for k, i in prim["attributes"].items()}}
    flat = [x[0] for x in read(prim["indices"])]
    out["tris"] = [tuple(flat[i:i + 3]) for i in range(0, len(flat), 3)]
    out["bytes"] = len(data)
    return out


def layout(parsed: dict) -> dict:
    """The attribute layout in kind: names, component types, normalization, material flags."""
    doc, prim = parsed["doc"], parsed["prim"]
    acc = doc["accessors"]
    return {
        "attributes": {k: (acc[i]["type"], acc[i]["componentType"], bool(acc[i].get("normalized")))
                       for k, i in sorted(prim["attributes"].items())},
        "indices": acc[prim["indices"]]["componentType"],
        "doubleSided": doc["materials"][0].get("doubleSided", False),
        "alphaMode": doc["materials"][0].get("alphaMode", "OPAQUE"),
        "textured": "baseColorTexture" in doc["materials"][0]["pbrMetallicRoughness"],
        "external_image": "uri" in doc["images"][0],
    }


def verify(filename: str, data: bytes, budget: int, height: float, reference: Path) -> list[str]:
    """Return a list of problems (empty = pass) and print the measured facts."""
    p = parse_glb(data)
    problems: list[str] = []
    pos, nrm, col, uv = (p["attrs"][k] for k in ("POSITION", "NORMAL", "COLOR_0", "TEXCOORD_0"))
    tris = p["tris"]
    lo = [min(v[a] for v in pos) for a in range(3)]
    hi = [max(v[a] for v in pos) for a in range(3)]
    mean_ny = sum(v[1] for v in nrm) / len(nrm)
    grey = [c[0] / 65535 for c in col]
    print(f"  {filename}: {len(data)} bytes, {len(tris)} tris, {len(pos)} verts")
    print(f"    bounds min=({lo[0]:.3f}, {lo[1]:.3f}, {lo[2]:.3f}) max=({hi[0]:.3f}, {hi[1]:.3f}, {hi[2]:.3f})"
          f"  footprint {hi[0] - lo[0]:.2f} x {hi[2] - lo[2]:.2f} m")
    print(f"    mean normal.y {mean_ny:.3f}, COLOR_0 grey {min(grey):.2f}..{max(grey):.2f}, "
          f"UV u {min(t[0] for t in uv):.3f}..{max(t[0] for t in uv):.3f} v {min(t[1] for t in uv):.3f}..{max(t[1] for t in uv):.3f}")

    if len(tris) >= budget:
        problems.append(f"{len(tris)} triangles, budget is under {budget}")
    if abs(hi[1] - height) > 0.005 or lo[1] < -1e-6:
        problems.append(f"height {lo[1]:.3f}..{hi[1]:.3f}, want 0..{height}: origin must be at the base")
    if not any(w in filename for w in ("grass", "fern")):
        problems.append("file name lacks 'grass'/'fern', so the scatter shader would give it no wind")
    if any(abs(math.sqrt(sum(a * a for a in v)) - 1.0) > 1e-3 for v in nrm):
        problems.append("a normal is not unit length")
    if min(min(t) for t in uv) < 0 or max(max(t) for t in uv) > 1:
        problems.append("UVs leave the 0..1 range")
    if any(c[3] != 65535 or not (c[0] == c[1] == c[2]) for c in col):
        problems.append("COLOR_0 is not an opaque grey ramp")
    if max(i for t in tris for i in t) >= len(pos):
        problems.append("an index is out of range")
    for t in tris:
        a, b, c = (pos[i] for i in t)
        ab = [b[k] - a[k] for k in range(3)]
        ac = [c[k] - a[k] for k in range(3)]
        cross = (ab[1] * ac[2] - ab[2] * ac[1], ab[2] * ac[0] - ab[0] * ac[2], ab[0] * ac[1] - ab[1] * ac[0])
        if math.sqrt(sum(x * x for x in cross)) < 1e-7:
            problems.append(f"degenerate triangle {t}")
            break
    declared = p["doc"]["accessors"][p["prim"]["attributes"]["POSITION"]]
    if any(abs(declared["min"][a] - lo[a]) > 1e-5 or abs(declared["max"][a] - hi[a]) > 1e-5 for a in range(3)):
        problems.append("POSITION accessor min/max do not match the data")

    if reference.exists():
        want, got = layout(parse_glb(reference.read_bytes())), layout(p)
        # the shipping fern is an alpha-masked card; ours is opaque geometry, so alphaMode may differ
        same = {k: want[k] == got[k] for k in want}
        if "fern" in filename:
            same.pop("alphaMode")
        bad = [k for k, ok in same.items() if not ok]
        print(f"    layout vs {reference.name}: {'identical in kind' if not bad else 'DIFFERS in ' + ', '.join(bad)}")
        if bad:
            problems.append(f"layout differs from {reference.name} in {', '.join(bad)}")
    else:
        print(f"    layout vs {reference.name}: reference not on disk, skipped")
    return problems


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT, help="output directory (default: %(default)s)")
    ap.add_argument("--tex-uri", default="T_Nature_Grass.png", help="relative URI of the gradient texture")
    ap.add_argument("--check", action="store_true", help="build in memory and verify; write nothing")
    args = ap.parse_args()

    built = build_all(args.tex_uri)
    if built != build_all(args.tex_uri):
        print("FAIL: two builds differ, the generator is not deterministic")
        return 1
    references = {"prp_grass_clump_a.glb": REFERENCE_GRASS, "prp_fern_clump_a.glb": REFERENCE_FERN}
    problems: list[str] = []
    for filename, data in built.items():
        name = filename[:-4]
        _b, _n, _m, _i, budget, height = ASSETS[name]
        problems += [f"{filename}: {p}" for p in verify(filename, data, budget, height, references[filename])]

    if problems:
        print("FAIL")
        for p in problems:
            print(f"  - {p}")
        return 1
    if args.check:
        print("ok (nothing written)")
        return 0
    args.out.mkdir(parents=True, exist_ok=True)
    for filename, data in built.items():
        (args.out / filename).write_bytes(data)
    print(f"wrote {len(built)} files to {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
