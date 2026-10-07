#!/usr/bin/env python3
"""Apply the hand-placed nature swap: nature.json through tools/repoint_models.py, then nature_extra.json.

    python tools/overhaul_rows/apply_nature.py --dry-run     # report, write nothing
    python tools/overhaul_rows/apply_nature.py               # rewrite the scenes

Both steps are safe to run again: a row whose node already instances its new scene is skipped by
repoint_models.py, and every op below sets an absolute value or does nothing when it is already done.
So this can be re-run on a tree where another lane's edits to the same cell had to win a merge.

nature_extra.json holds what a repoint row cannot say:

    {"op": "scale",  "scene", "node", "scale": [x, y, z], "y": 0.0}
        Absolute length of each local axis of that exact node (rotation and position kept); the
        optional "y" is the absolute origin height. Used where the scale is not uniform (the ice
        walls are fitted so the wrapper's collider covers the footprint the old box did), and where
        a `Thing / Model` trio carries other children (a lantern bowl, a map pin, a dialogue) so the
        Model is sized and the Thing is left alone.
    {"op": "delete", "scene", "node"}
        Removes the node, its children and any resource only they used (the stepping-stone paths:
        there is no replacement for them in the generated set).
    {"op": "add",    "scene", "parent", "name", "path", "transform": [12 numbers], "groups", "props"}
        A new instance (the elder oaks and crag spires). Skipped when the node already exists.
"""
from __future__ import annotations

import argparse
import io
import json
import math
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
sys.path.insert(0, str(HERE.parent))
import repoint_models as rm  # noqa: E402


def set_axes(scene: rm.Scene, index: int, scale: list[float], y: float | None) -> None:
    values, at = [1.0, 0, 0, 0, 1.0, 0, 0, 0, 1.0, 0, 0, 0], None
    for i in range(index + 1, scene.prop_end(index)):
        found = rm.TRANSFORM.match(scene.lines[i])
        if found:
            values, at = [float(v) for v in found.group(1).split(",")], i
    for column in range(3):
        length = math.sqrt(sum(values[row * 3 + column] ** 2 for row in range(3))) or 1.0
        if abs(length - scale[column]) < 0.001:
            continue  # already there: do not let four-decimal rounding walk the basis on a re-run
        for row in range(3):
            values[row * 3 + column] *= scale[column] / length
    if y is not None:
        values[10] = y
    line = "transform = Transform3D(" + ", ".join(rm.fmt(v) for v in values) + ")"
    if at is None:
        scene.lines.insert(index + 1, line)
    else:
        scene.lines[at] = line


def delete(scene: rm.Scene, path: str) -> bool:
    removed: list[str] = []
    for i in reversed(scene.headers("node")):
        here = scene.node_path(i)
        if here == path or here.startswith(path + "/"):
            end = scene.after(i)
            removed += scene.lines[i:end]
            del scene.lines[i:end]
    text = rm.LF.join(removed)
    for ident in set(re.findall(r'SubResource\("([^"]+)"\)', text)):
        scene.drop_if_unused("sub_resource", ident)
    for ident in set(re.findall(r'ExtResource\("([^"]+)"\)', text)):
        scene.drop_if_unused("ext_resource", ident)
    return bool(removed)


def add(scene: rm.Scene, op: dict) -> bool:
    path = op["name"] if op["parent"] == "." else f'{op["parent"]}/{op["name"]}'
    if path in scene.nodes():
        return False
    if op["parent"] != "." and op["parent"] not in scene.nodes():
        raise ValueError(f'parent \'{op["parent"]}\' not found')
    ident = scene.ext_id_for(op["path"]) or scene.add_ext(op["path"])
    groups = ""
    if op.get("groups"):
        groups = " groups=[" + ", ".join(f'"{g}"' for g in op["groups"]) + "]"
    block = [f'[node name="{op["name"]}" parent="{op["parent"]}" instance=ExtResource("{ident}"){groups}]',
             "transform = Transform3D(" + ", ".join(rm.fmt(v) for v in op["transform"]) + ")"]
    block += [f"{key} = {value}" for key, value in op.get("props", {}).items()]
    # After the last node, before any [connection] or [editable] section.
    last = scene.headers("node")[-1]
    at = scene.after(last)
    if scene.lines[at - 1].strip():
        block = [""] + block
    scene.lines[at:at] = block + [""]
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--allow-missing", action="store_true",
                        help="repoint to a model that is not on disk yet (passed to repoint_models.py)")
    args = parser.parse_args()

    status = rm.apply(argparse.Namespace(root=str(ROOT), rows=str(HERE / "nature.json"), dry_run=args.dry_run,
                                         allow_missing=args.allow_missing))
    if status:
        return status

    ops = json.loads((HERE / "nature_extra.json").read_text(encoding="utf-8"))
    by_scene: dict[str, list[dict]] = {}
    for op in ops:
        by_scene.setdefault(op["scene"], []).append(op)
    errors = 0
    results: list[tuple[Path, str, str]] = []
    for name, scene_ops in by_scene.items():
        path = ROOT / name
        original = io.open(path, encoding="utf-8", newline="").read().replace(rm.CRLF, rm.LF)
        scene = rm.Scene(original)
        before = scene.resource_count()
        for op in scene_ops:
            try:
                if op["op"] == "scale":
                    set_axes(scene, scene.nodes()[op["node"]], op["scale"], op.get("y"))
                elif op["op"] == "delete":
                    delete(scene, op["node"])
                elif op["op"] == "add":
                    add(scene, op)
                else:
                    raise ValueError(f'unknown op \'{op["op"]}\'')
            except (KeyError, ValueError) as error:
                print(f"ERROR {name} {op.get('node') or op.get('name')}: {error}")
                errors += 1
        scene.fix_load_steps(before)
        if scene.text() != original:
            results.append((path, name, scene.text()))
    if errors:
        print(f"{errors} error(s): no extra op written")
        return 2
    for path, name, text in results:
        if args.dry_run:
            print(f"would change: {name}")
        else:
            io.open(path, "w", encoding="utf-8", newline="\n").write(text)
    print(f"{len(ops)} extra op(s), {len(results)} scene(s) " + ("would change (dry run)" if args.dry_run else "rewritten"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
