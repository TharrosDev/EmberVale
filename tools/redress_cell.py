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

    rows = [r for r in table.splitlines()
            if r.strip() and not r.startswith(";") and not any(d in r for d in drop)]
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
