#!/usr/bin/env python3
"""Generate assets/models/characters/chr_player_base_mask.png, the region mask the player body shader reads.

The player body is ONE mesh with ONE baked Meshy atlas (chr_player_base_texture_0.png), so appearance
cannot swap materials per part; it tints by region instead. The mask is a same-size RGB image:

    R = skin   (face, neck, hands, arms: the pink islands of the atlas)
    G = hair   (hair islands, plus the dark beard/brow pixels of the face island)
    B = eyes   (the dark iris/lash pixels around the two eyes; they also take the ember glow)

Clothing and gear are all-zero, so only skin/hair/eyes ever change colour.

How it is derived (Pillow only, no numpy). The atlas is a set of UV islands; the mesh says which island belongs
to the head (triangles skinned to Head/head_end/headfront):
  * a pixel looks like skin when it is light pink (bright red, clear red-over-blue lead);
  * an island is a SKIN island when enough of it looks like skin; a clothing island that merely contains a few
    pink-ish highlights stays zero;
  * a head island that is not skin is HAIR as a whole; the head's skin island has hair only where the pixel is very
    dark (beard, brows);
  * the eyes are two small discs at fixed atlas coordinates (the atlas is a fixed asset), limited to dark pixels.
Masks are dilated 1 px into the gutter between islands so bilinear/mip sampling never shows an untinted fringe.

It prints the average colour of each region in the atlas; those are the reference colours in
src/Appearance/AppearanceRules.cs. If the model or texture is replaced, re-run and LOOK at --preview first.

Usage:  python tools/gen_player_mask.py [--preview PATH]
"""

import json
import os
import struct
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GLB = os.path.join(ROOT, "assets", "models", "characters", "chr_player_base.glb")
TEXTURE = os.path.join(ROOT, "assets", "models", "characters", "chr_player_base_texture_0.png")
OUT = os.path.join(ROOT, "assets", "models", "characters", "chr_player_base_mask.png")

HEAD_JOINTS = ("Head", "head_end", "headfront")
# Atlas pixel centres of the two eyes (found by eye on the 1024 atlas) and the disc radius around them.
EYE_CENTRES = ((76, 924), (545, 882))
EYE_RADIUS = 13
# An island is skin when at least this fraction of its pixels look like skin.
SKIN_ISLAND_FRACTION = 0.35
GUTTER = 0


def smoothstep(lo, hi, x):
    t = min(1.0, max(0.0, (x - lo) / (hi - lo)))
    return t * t * (3.0 - 2.0 * t)


def read_glb(path):
    data = open(path, "rb").read()
    json_len = struct.unpack("<I", data[12:16])[0]
    doc = json.loads(data[20:20 + json_len])
    blob = data[20 + json_len + 8:]
    return doc, blob


def accessor(doc, blob, index):
    acc = doc["accessors"][index]
    view = doc["bufferViews"][acc["bufferView"]]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[acc["type"]]
    fmt = {5126: "f", 5121: "B", 5123: "H"}[acc["componentType"]]
    size = acc["count"] * width * struct.calcsize(fmt)
    return list(struct.iter_unpack("<" + fmt * width, blob[start:start + size]))


def islands(doc, blob):
    """Returns (triangles, island_of_triangle, head_islands): the UV triangles, their island index (triangles
    sharing a vertex are one island) and the set of islands whose triangles are mostly skinned to the head."""
    prim = doc["meshes"][0]["primitives"][0]
    names = [doc["nodes"][i]["name"] for i in doc["skins"][0]["joints"]]
    uv = accessor(doc, blob, prim["attributes"]["TEXCOORD_0"])
    joints = accessor(doc, blob, prim["attributes"]["JOINTS_0"])
    weights = accessor(doc, blob, prim["attributes"]["WEIGHTS_0"])
    index = [i[0] for i in accessor(doc, blob, prim["indices"])]
    on_head = []
    for j, w in zip(joints, weights):
        strongest = max(range(4), key=lambda k: w[k])
        on_head.append(names[j[strongest]] in HEAD_JOINTS)

    parent = list(range(len(uv)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    tris = [index[t:t + 3] for t in range(0, len(index), 3)]
    for a, b, c in tris:
        parent[find(b)] = find(a)
        parent[find(c)] = find(a)

    ids = {}
    tri_island = []
    head_votes = {}
    for tri in tris:
        root = find(tri[0])
        isl = ids.setdefault(root, len(ids))
        tri_island.append(isl)
        votes = head_votes.setdefault(isl, [0, 0])
        votes[0] += sum(on_head[i] for i in tri)
        votes[1] += 3
    head = {isl for isl, (h, n) in head_votes.items() if h * 2 > n}
    polys = [[(uv[i][0], uv[i][1]) for i in tri] for tri in tris]
    return polys, tri_island, head


def build_mask():
    tex = Image.open(TEXTURE).convert("RGB")
    size = tex.size
    doc, blob = read_glb(GLB)
    polys, tri_island, head_islands = islands(doc, blob)

    # Island id map, 0 = no island (gutter), otherwise index + 1.
    island_map = Image.new("I", size, 0)
    draw = ImageDraw.Draw(island_map)
    for poly, isl in zip(polys, tri_island):
        # PIL addresses a pixel by its centre coordinate and a texel's centre is +0.5, hence the half-pixel shift; the
        # outline too, or the one-pixel cracks between adjacent triangles stay empty and the mask is perforated.
        draw.polygon([(u * size[0] - 0.5, v * size[1] - 0.5) for u, v in poly], fill=isl + 1, outline=isl + 1)

    px = tex.load()
    imap = island_map.load()
    count = {}
    skin_like = {}
    score = {}
    for y in range(size[1]):
        for x in range(size[0]):
            r, g, b = px[x, y]
            # Light pink: bright red and a clear red-over-blue lead. Leather is darker; denim-grey cloth has
            # almost no red lead.
            s = smoothstep(115, 150, r) * smoothstep(28, 48, r - b)
            score[(x, y)] = s
            isl = imap[x, y]
            if isl:
                count[isl] = count.get(isl, 0) + 1
                skin_like[isl] = skin_like.get(isl, 0) + (1 if s > 0.5 else 0)

    skin_islands = {isl for isl, n in count.items() if skin_like.get(isl, 0) >= SKIN_ISLAND_FRACTION * n}
    head_ids = {i + 1 for i in head_islands}
    face_islands = skin_islands & head_ids
    hair_islands = head_ids - skin_islands

    eye_zone = Image.new("L", size, 0)
    zone = ImageDraw.Draw(eye_zone)
    for cx, cy in EYE_CENTRES:
        zone.ellipse((cx - EYE_RADIUS, cy - EYE_RADIUS, cx + EYE_RADIUS, cy + EYE_RADIUS), fill=255)

    skin = Image.new("L", size)
    hair = Image.new("L", size)
    eyes = Image.new("L", size)
    zpx = eye_zone.load()
    spx, hrpx, epx = skin.load(), hair.load(), eyes.load()
    for y in range(size[1]):
        for x in range(size[0]):
            isl = imap[x, y]
            if not isl:
                continue
            r, g, b = px[x, y]
            luma = (r + g + b) / 3.0
            dark = 1.0 - smoothstep(70, 120, luma)
            eye = (zpx[x, y] / 255.0) * dark if isl in face_islands else 0.0
            s = score[(x, y)] if isl in skin_islands else 0.0
            if isl in hair_islands:
                hr = 1.0
            elif isl in face_islands:
                hr = (1.0 - smoothstep(55, 85, luma)) * (1.0 - s)  # beard stubble and brows
            else:
                hr = 0.0
            spx[x, y] = int(round(s * (1.0 - eye) * (1.0 - hr) * 255))
            hrpx[x, y] = int(round(hr * (1.0 - eye) * 255))
            epx[x, y] = int(round(eye * 255))

    # Dilate into the gutter only (pixels no triangle covers), so island interiors keep their exact values.
    covered = Image.new("L", size, 0)
    cover_draw = ImageDraw.Draw(covered)
    for poly in polys:
        cover_draw.polygon([(u * size[0] - 0.5, v * size[1] - 0.5) for u, v in poly], fill=255, outline=255)
    outside = ImageChops.invert(covered)
    channels = []
    for channel in (skin, hair, eyes):
        grown = channel.filter(ImageFilter.MaxFilter(2 * GUTTER + 1)) if GUTTER else channel
        channels.append(Image.composite(grown, channel, outside))
    mask = Image.merge("RGB", tuple(channels))
    # Reference colours come from the island interiors, not the dilated gutter (which copies the atlas's padding).
    stats = region_averages(tex, Image.merge("RGB", (skin, hair, eyes)))
    return mask, tex, stats


def region_averages(tex, mask):
    tp, mp = tex.load(), mask.load()
    acc = [[0.0, 0.0, 0.0, 0] for _ in range(3)]
    for y in range(tex.size[1]):
        for x in range(tex.size[0]):
            for c in range(3):
                if mp[x, y][c] > 127:
                    a = acc[c]
                    a[0] += tp[x, y][0]
                    a[1] += tp[x, y][1]
                    a[2] += tp[x, y][2]
                    a[3] += 1
    return {name: ([round(v / a[3] / 255, 3) for v in a[:3]], a[3]) for name, a in zip(("skin", "hair", "eyes"), acc)}


def preview(mask, tex, path):
    """Side-by-side: the atlas, and the atlas blended with the mask (red skin, green hair, blue eyes)."""
    blend = Image.blend(tex, mask, 0.65)
    out = Image.new("RGB", (tex.size[0] * 2, tex.size[1]))
    out.paste(tex, (0, 0))
    out.paste(blend, (tex.size[0], 0))
    out.save(path)


def main():
    args = sys.argv[1:]
    mask, tex, stats = build_mask()
    mask.save(OUT)
    print(f"wrote {os.path.relpath(OUT, ROOT)} {mask.size}")
    for name, (rgb, pixels) in stats.items():
        print(f"  {name}: {pixels} px, average sRGB {rgb}")
    if "--preview" in args:
        target = args[args.index("--preview") + 1]
        preview(mask, tex, target)
        print(f"wrote preview {target}")


if __name__ == "__main__":
    main()
