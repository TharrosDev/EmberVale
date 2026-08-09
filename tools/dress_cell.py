#!/usr/bin/env python3
"""Scatter nature-megakit ground cover into a region cell and print the `.tscn` stanzas.

Why this exists
---------------
`gen_cell_props.py` expands a prop TABLE. This writes the table, because ground cover is 100-200
props per cell and nobody is hand-placing that. It reads the cell's OWN node positions out of the
`.tscn` and treats them as keep-outs, so the scatter cannot land a fern inside a building or a
tuft of grass on a merchant's head without anyone noticing.

⚠️ It prints to stdout like `gen_cell_props.py` and for the same reason: the `.tscn` stays the
authored artefact. Pipe it, read it, paste it.

Usage
-----
    python tools/dress_cell.py <cell.tscn> <style> <first_ext_id> [--seed N]

Styles, because a cell's dressing is a judgement about the PLACE and not a setting:

    meadow     open wilderness - grass everywhere, flowers, ferns and mushrooms under the pines
    verge      a road or a settlement - grass at the margins only, nothing on the thoroughfare
    shore      a waterfront - wispy grass near the water, pebbles on the strand, ferns inland
    industrial a worked site - no meadow. Mushrooms and pebbles in the damp corners, grass at the
               fenceline where nobody walks
    edges      a dense or a deliberately clear cell - a thin band of grass hard against the
               boundary and nothing at all in the middle

⚠️ **`edges` exists for the arena.** A combat floor must stay legible; scenery a player mistakes
for cover is worse than a bare floor. Do not "improve" the arena by giving it a meadow.
"""

import math
import random
import re
import sys

YAWS = [0, 30, 90, 180, 270]

# What a node's name implies about how much room it needs. Checked longest-first.
FOOTPRINT = [
    (("building", "house", "cottage", "tower", "shed", "hut", "chandlery", "store",
      "gazebo", "harbourshed", "minehead", "dockcomplex"), 7.0),
    (("stall", "jetty", "wall", "fence", "cart", "tent", "rack", "well", "waystone",
      "bell", "gate", "pillar", "ruin", "boulder", "rocks", "oreseam", "timber"), 3.0),
    (("pine", "tree"), 3.0),
    (("lamp", "brazier", "fire", "coals", "bench", "crate", "barrel", "sacks", "hay",
      "cauldron", "locker", "stand", "station", "vent", "spawn", "notice", "banner",
      "relic", "deed", "chest", "vault", "marker"), 1.6),
]
DEFAULT_FOOTPRINT = 2.0          # an NPC, a pickup, anything unrecognised - nothing lands on a person

# style -> (grass patches, per-patch range, flower clumps, fern clumps, mushroom clumps,
#           clover clumps, pebble count, band) where band limits placement to a radius fraction.
STYLES = {
    "meadow":     dict(patches=22, tufts=(4, 7), flowers=3, ferns=4, shrooms=3, clover=3,
                       pebbles=10, band=(0.00, 1.00)),
    "verge":      dict(patches=14, tufts=(3, 5), flowers=2, ferns=2, shrooms=1, clover=2,
                       pebbles=8, band=(0.55, 1.00)),
    "shore":      dict(patches=16, tufts=(4, 6), flowers=2, ferns=3, shrooms=2, clover=2,
                       pebbles=14, band=(0.00, 1.00)),
    "industrial": dict(patches=9, tufts=(3, 5), flowers=0, ferns=2, shrooms=3, clover=1,
                       pebbles=12, band=(0.60, 1.00)),
    "edges":      dict(patches=8, tufts=(3, 5), flowers=0, ferns=0, shrooms=0, clover=0,
                       pebbles=4, band=(0.80, 1.00)),
}

SPECIES = {
    "grass_short": 13, "grass_tall": 14, "grass_wispy": 15, "clover": 16,
    "flowers_a": 17, "flowers_b": 18, "fern": 19, "mushrooms": 20,
    "pebble_a": 21, "pebble_b": 22, "rockpath_wide": 23, "rockpath_small": 24,
}

NODE_RE = re.compile(r'^\[node name="([^"]+)"[^\]]*\]$')
XFORM_RE = re.compile(r"^transform = Transform3D\(([^)]*)\)$")


def read_cell(path):
    """Every node's (name, x, z) plus the floor's half-extent, straight out of the .tscn."""
    nodes, half = [], 25.0
    pending = None
    for line in open(path, encoding="utf-8"):
        line = line.strip()
        m = NODE_RE.match(line)
        if m:
            pending = m.group(1)
            continue
        m = XFORM_RE.match(line)
        if m and pending:
            v = [float(x) for x in m.group(1).split(",")]
            nodes.append((pending, v[9], v[11]))
            pending = None
        if line.startswith("size = Vector3(") and half == 25.0:
            v = [float(x) for x in line[len("size = Vector3("):-1].split(",")]
            if v[0] > 20:                       # the floor slab, not a collider
                half = min(v[0], v[2]) / 2.0
    return nodes, half


def footprint(name):
    low = name.lower()
    for words, radius in FOOTPRINT:
        if any(w in low for w in words):
            return radius
    return DEFAULT_FOOTPRINT


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    if len(args) != 3:
        raise SystemExit(__doc__)
    path, style, first_id = args[0], args[1], int(args[2])
    if style not in STYLES:
        raise SystemExit(f"unknown style {style!r}; pick one of {sorted(STYLES)}")
    seed = next((int(a.split("=")[1]) for a in argv if a.startswith("--seed=")), 38)
    random.seed(seed)

    cfg = STYLES[style]
    nodes, half = read_cell(path)
    keepout = [(x, z, footprint(n)) for n, x, z in nodes]
    edge = half - 1.5
    lo, hi = cfg["band"][0] * edge, cfg["band"][1] * edge

    def free(x, z, pad=0.0):
        r = math.hypot(x, z)
        if max(abs(x), abs(z)) > edge or not (lo <= r <= hi):
            return False
        return all((x - kx) ** 2 + (z - kz) ** 2 >= (kr + pad) ** 2 for kx, kz, kr in keepout)

    rows = []

    def spots_for(count, pad):
        """`count` patch centres, spread evenly around the cell rather than bunched.

        ⚠️ A BANDED STYLE MUST BE SAMPLED BY ANGLE, NOT ONLY BY REJECTION. Drawing (x, z) uniformly
        in the square and discarding anything outside the band hands every patch to whichever arc
        happens to be free: town_hub's buildings sit north and west, so the first pass put ALL of
        its ground cover in the east field and left three quarters of the verge bare.

        ⚠️ ...but the ring pass alone is not enough either. embermarket's ring is nearly solid with
        houses, fences and trees, and walking it at fixed angles delivered 10 props where 48 were
        wanted. So the even sweep is a PREFERENCE and rejection sampling tops up whatever it could
        not place. Both failures were silent - the table simply came out short.
        """
        if count <= 0:
            return []                       # a style that asks for none of a species (edges: no
                                            # flowers, no clover) must not divide by it below
        out = []
        if cfg["band"][0] > 0.0:
            # Sweep the WHOLE circle before choosing. Returning as soon as `count` was reached
            # walked only the first arc and left the rest of the ring bare - which is what
            # town_hub looked like on the second attempt, bunched along its north edge. Collect
            # every angle that works, then thin evenly so the survivors stay spread.
            steps = max(8, count * 3)
            ring = []
            for i in range(steps):
                a = (i / float(steps)) * math.tau + random.uniform(-0.10, 0.10)
                for _ in range(16):
                    r = random.uniform(lo, hi)
                    x, z = r * math.cos(a), r * math.sin(a)
                    if free(x, z, pad):
                        ring.append((x, z))
                        break
            if len(ring) > count:
                step = len(ring) / float(count)
                ring = [ring[int(i * step)] for i in range(count)]
            out = ring
        tries = 0
        while len(out) < count and tries < 8000:
            tries += 1
            x, z = random.uniform(-edge, edge), random.uniform(-edge, edge)
            if free(x, z, pad):
                out.append((x, z))
        return out

    ids = {k: f"{first_id + i}_{k}" for i, k in enumerate(SPECIES)}

    def add(prefix, count, species, spread, pad, clumps=None, per=1):
        placed = 0
        spots = clumps if clumps is not None else spots_for(count, 2.0)
        for cx, cz in spots:
            for _ in range(per):
                x = cx + random.uniform(-spread, spread)
                z = cz + random.uniform(-spread, spread)
                if not free(x, z, pad):
                    continue
                placed += 1
                s = species() if callable(species) else species
                rows.append("%-16s %-18s %7.2f %7.2f  -  0  %d"
                            % (f"{prefix}{placed}", ids[s], x, z, random.choice(YAWS)))
        return placed

    grass = lambda: random.choices(
        ["grass_short", "grass_tall", "grass_wispy"], weights=[5, 3, 2])[0]

    # ⚠️ A BANDED STYLE MUST BE SAMPLED BY ANGLE, NOT BY REJECTION. Drawing (x, z) uniformly in the
    # square and throwing away anything outside the band gives every patch to whichever arc happens
    # to be unoccupied: town_hub's buildings sit north and west, so the first pass put ALL of its
    # ground cover in the east field and left three quarters of the verge bare. Walking the ring at
    # even angles spreads them the way a verge actually runs.
    centres = spots_for(cfg["patches"], 2.2)
    n = add("Grass", 0, grass, 1.3, 0.3, clumps=centres, per=random.randint(*cfg["tufts"]))

    # Ferns and mushrooms are shade species: they belong UNDER the pines, so they clear the
    # trunk and not the canopy. Applying the pine's full keep-out to them deletes them silently,
    # which is exactly what happened on wilds_north's second pass.
    pines = [(x, z) for nm, x, z in nodes if "pine" in nm.lower() or "tree" in nm.lower()]
    random.shuffle(pines)
    pineset = {(round(x, 3), round(z, 3)) for x, z in pines}
    saved = list(keepout)
    keepout[:] = [(x, z, r * 0.22 if (round(x, 3), round(z, 3)) in pineset else r)
                  for x, z, r in keepout]

    # ⚠️ Ferns and mushrooms draw from the same pines rather than splitting the list. Slicing it
    # (shade[:ferns] then shade[ferns:]) starves whichever comes second the moment a cell has
    # fewer pines than clumps -- wilds_west has two pines and got 3 ferns and ZERO mushrooms.
    # A pine can shelter both, which is also what a real one does.
    def under(count, spread):
        return [(x + random.uniform(-spread, spread), z + random.uniform(-spread, spread))
                for x, z in (pines * 3)[:count]] if pines else []

    f = add("Fern", 0, "fern", 1.6, 0.4, clumps=under(cfg["ferns"], 1.8), per=3)
    m = add("Mushroom", 0, "mushrooms", 1.1, 0.15, clumps=under(cfg["shrooms"], 1.5), per=3)
    keepout[:] = saved

    c = add("Clover", cfg["clover"], "clover", 1.8, 0.2, per=7)
    fa = add("FlowerA", cfg["flowers"], "flowers_a", 1.8, 0.3, per=4)
    fb = add("FlowerB", max(0, cfg["flowers"] - 1), "flowers_b", 1.6, 0.3, per=4)
    p = add("Pebble", cfg["pebbles"], lambda: random.choice(["pebble_a", "pebble_b"]),
            0.6, 0.15, per=2)

    print("\n".join(rows))
    print(f"\n; {style}: {n} grass, {c} clover, {fa + fb} flowers, {f} ferns, "
          f"{m} mushrooms, {p} pebbles = {len(rows)}", file=sys.stderr)
    print(";EXT " + " ".join(f"{ids[k]}={k}" for k in SPECIES), file=sys.stderr)


if __name__ == "__main__":
    main(sys.argv[1:])
