#!/usr/bin/env python3
"""Apply a layout spec to a region cell `.tscn`, in place.

Why this exists
---------------
Phase 44 rebuilds the physical layout of every explorable cell. A cell is 1,000-2,500 lines of
`.tscn` in which the *composition* — where each stall, house, wall and lamp stands — is a single
`transform =` line per node, surrounded by component properties, colliders, particles and comments
that must survive untouched. Rewriting the files by hand retypes the parts that are correct in
order to change the parts that are not; this moves only the transform.

⚠️ It edits the file IN PLACE and the `.tscn` stays the authored artefact. This is not a build
step: run it once, read the diff, commit the result.

Usage
-----
    python tools/relayout_cell.py scenes/regions/ember_crown/embermarket.tscn layout.txt

Spec lines (blank lines and `#` comments ignored):

    pos    NAME  x y z          move a node, KEEPING its authored basis (rotation + scale)
    move   NAME  x y z yaw      move a node and REPLACE its basis with a pure yaw (degrees)
    scale  NAME  x y z yaw sx sy sz   as `move`, with a scaled basis
    del    NAME                 delete the node and every descendant
    rename OLD NEW              rename a node, repointing every child's parent path

NAME is the node's `name=` as written in the file. It must be unique among nodes of that name at
that level — a name used by several nodes (every "Model", "Col", "Shape") is rejected rather than
guessed at, because moving the wrong one is silent.
"""

import math
import re
import sys

NODE_RE = re.compile(r'^\[node name="([^"]+)"(?:\s+type="([^"]+)")?(?:\s+parent="([^"]+)")?')


def basis(yaw_deg, sx=1.0, sy=1.0, sz=1.0):
    """Godot Transform3D basis columns for a Y rotation, written row-major as the .tscn does."""
    c, s = math.cos(math.radians(yaw_deg)), math.sin(math.radians(yaw_deg))
    vals = [c * sx, 0.0, s * sx, 0.0, sy, 0.0, -s * sz, 0.0, c * sz]
    return ", ".join(num(v) for v in vals)


def num(v):
    v = round(float(v), 4) + 0.0
    return str(int(v)) if v == int(v) else str(v)


def parse_blocks(lines):
    """[(start, end, name, parent_path)] — one entry per [node ...] header, end exclusive."""
    heads = [i for i, l in enumerate(lines) if l.startswith("[node ")]
    out = []
    for k, i in enumerate(heads):
        m = NODE_RE.match(lines[i])
        if not m:
            raise SystemExit(f"line {i + 1}: unparsable node header: {lines[i]!r}")
        name, _type, parent = m.group(1), m.group(2), m.group(3)
        end = heads[k + 1] if k + 1 < len(heads) else len(lines)
        path = name if parent in (None, ".") else f"{parent}/{name}"
        out.append([i, end, name, path, parent])
    return out


def find(blocks, name):
    hits = [b for b in blocks if b[2] == name]
    if not hits:
        raise SystemExit(f"no node named {name!r}")
    if len(hits) > 1:
        raise SystemExit(f"{name!r} is ambiguous ({len(hits)} nodes) — rename one first")
    return hits[0]


def set_transform(lines, block, text):
    start, end = block[0], block[1]
    for i in range(start + 1, end):
        if lines[i].startswith("transform = "):
            lines[i] = text
            return
    # No authored transform (an unmoved instance) — insert one directly under the header.
    lines.insert(start + 1, text)


def apply(path, spec_path):
    lines = open(path, encoding="utf-8").read().split("\n")
    ops = []
    for raw in open(spec_path, encoding="utf-8"):
        raw = raw.split("#", 1)[0].strip()
        if raw:
            ops.append(raw.split())

    for op in ops:
        blocks = parse_blocks(lines)
        kind = op[0]

        if kind == "del":
            b = find(blocks, op[1])
            prefix = b[3] + "/"
            victims = [c for c in blocks if c[3] == b[3] or c[3].startswith(prefix)]
            for c in sorted(victims, key=lambda c: -c[0]):
                del lines[c[0]:c[1]]

        elif kind == "rename":
            old, new = op[1], op[2]
            b = find(blocks, old)
            lines[b[0]] = lines[b[0]].replace(f'name="{old}"', f'name="{new}"', 1)
            old_path, new_path = b[3], (b[3].rsplit("/", 1)[0] + "/" + new if "/" in b[3] else new)
            # ⚠️ MATCH THE WHOLE PATH SEGMENT, NEVER A PREFIX. Renaming PillarN with a bare prefix
            # test also rewrote PillarNE's children to "Nav/AvenueAE" — a parent path pointing at no
            # node, which Godot loads as a scene silently missing every collider under it. Only
            # `parent="OLD"` and `parent="OLD/...` are this node's.
            for i, l in enumerate(lines):
                if not l.startswith("[node "):
                    continue
                for suffix in ('"', "/"):
                    if f'parent="{old_path}{suffix}' in l:
                        lines[i] = l.replace(f'parent="{old_path}{suffix}',
                                             f'parent="{new_path}{suffix}', 1)
                        break

        elif kind == "pos":
            b = find(blocks, op[1])
            x, y, z = (num(v) for v in op[2:5])
            for i in range(b[0] + 1, b[1]):
                if lines[i].startswith("transform = Transform3D("):
                    parts = lines[i][len("transform = Transform3D("):-1].split(",")
                    keep = ", ".join(p.strip() for p in parts[:9])
                    lines[i] = f"transform = Transform3D({keep}, {x}, {y}, {z})"
                    break
            else:
                set_transform(lines, b, f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {x}, {y}, {z})")

        elif kind in ("move", "scale"):
            b = find(blocks, op[1])
            x, y, z, yaw = (float(v) for v in op[2:6])
            s = [float(v) for v in op[6:9]] if kind == "scale" else [1.0, 1.0, 1.0]
            set_transform(
                lines, b,
                f"transform = Transform3D({basis(yaw, *s)}, {num(x)}, {num(y)}, {num(z)})")

        else:
            raise SystemExit(f"unknown op {kind!r}")

    open(path, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
    print(f"{path}: {len(ops)} ops applied")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    apply(sys.argv[1], sys.argv[2])
