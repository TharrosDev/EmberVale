#!/usr/bin/env python3
"""Strip a cell's authored ground cover and re-scatter it against the cell's CURRENT layout.

Why this exists
---------------
`dress_cell.py` reads a cell's node positions as keep-outs, so its output is only correct for the
layout it was run against. Phase 44 moved every building in every cell, which left ~800 tufts of
grass standing inside walls and on roads across the realm. This does the three steps that always
follow a re-lay, in one pass and in the right order: delete the old cover, re-scatter, splice the
new stanzas back in at the same place in the file.

    python tools/redress_cell.py <cell.tscn> <style> <first_ext_id> [--seed=N] [--keep=1/2]
                                 [--drop=50_mushrooms,53_rockpath_wide]

Rows naming an ext id the cell does not declare are dropped automatically, and the run says which —
if that list is long, `first_ext_id` is wrong.

⚠️ `--keep` throws away rows. A dense settlement cell can be within a few dozen nodes of the
region's per-cell budget before any dressing at all, and `verge` emits ~100 props (200 nodes). Pass
`--keep=1/2` to take every second row. `--drop` names ext ids the cell does not import — the
scatter offers mushrooms and rock paths that most cells never adopted.
"""

import os
import re
import subprocess
import sys

TOOLS = os.path.dirname(os.path.abspath(__file__))
COVER = re.compile(r"^(Grass|Fern|Clover|Flower[AB]|Pebble|Mushroom|RockPath)\d+$")


def strip(path):
    """Delete every ground-cover node and its Model child; return the line the block started at."""
    lines = open(path, encoding="utf-8").read().split("\n")
    heads = [i for i, l in enumerate(lines) if l.startswith("[node ")]
    names = [l.split('"', 2)[1] for l in (lines[i] for i in heads)]
    kill, anchor = [], None
    for k, i in enumerate(heads):
        end = heads[k + 1] if k + 1 < len(heads) else len(lines)
        if COVER.match(names[k]) or (names[k] == "Model" and k and COVER.match(names[k - 1])):
            kill.append((i, end))
            if anchor is None:
                anchor = i
    for a, b in reversed(kill):
        del lines[a:b]
    open(path, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
    return anchor if anchor is not None else len(lines), len(kill)


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    path, style, first = args[0], args[1], args[2]
    opt = {a.split("=")[0]: a.split("=", 1)[1] for a in argv if "=" in a and a.startswith("--")}
    drop = [d for d in opt.get("--drop", "").split(",") if d]
    keep_n, keep_d = (int(x) for x in opt.get("--keep", "1/1").split("/"))

    anchor, removed = strip(path)

    cmd = [sys.executable, os.path.join(TOOLS, "dress_cell.py"), path, style, first]
    if "--seed" in opt:
        cmd.append(f"--seed={opt['--seed']}")
    table = subprocess.run(cmd, capture_output=True, text=True, check=True).stdout

    # ⚠️ A ROW NAMING AN EXT ID THE CELL DOES NOT DECLARE IS A SCENE THAT WILL NOT LOAD, and the
    # scatter offers twelve species while most cells import eight or nine. dress_cell numbers from
    # first_ext_id, so passing the wrong first id silently emits a hundred references to nothing —
    # and in town_hub the collision is worse than a miss: "53_rockpath_wide" is 53_workbench there,
    # so a bad id plants a joiner's bench in the grass. Declared ids are read from the file rather
    # than listed by hand, because a hand-written list is the thing that goes stale.
    declared = set(re.findall(r'^\[ext_resource[^\]]*id="([^"]+)"',
                              open(path, encoding="utf-8").read(), re.M))
    rows, skipped = [], set()
    for r in table.splitlines():
        if not r.strip() or r.startswith(";"):
            continue
        ext = r.split()[1]
        if ext in drop or ext not in declared:
            skipped.add(ext)
            continue
        rows.append(r)
    if skipped:
        print(f"  skipped species this cell does not declare: {', '.join(sorted(skipped))}")
    rows = [r for n, r in enumerate(rows) if n % keep_d < keep_n]

    stanzas = subprocess.run(
        [sys.executable, os.path.join(TOOLS, "gen_cell_props.py"), "-", "--no-collider", "--parent=."],
        input="\n".join(rows), capture_output=True, text=True, check=True).stdout

    lines = open(path, encoding="utf-8").read().split("\n")
    lines[anchor:anchor] = stanzas.rstrip("\n").split("\n") + [""]
    open(path, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
    print(f"{path}: removed {removed} cover blocks, placed {len(rows)} props at line {anchor + 1}")


if __name__ == "__main__":
    main(sys.argv[1:])
