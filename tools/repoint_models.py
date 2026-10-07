#!/usr/bin/env python3
"""Swap the model a scene node instances, one node at a time, and retire models nobody uses.

    python tools/repoint_models.py apply rows.json --dry-run     # unified diff, writes nothing
    python tools/repoint_models.py apply rows.json               # rewrite the scenes
    python tools/repoint_models.py refs prp_ruin_pillar.glb      # every referrer of a model
    python tools/repoint_models.py delete prp_a.glb prp_b.glb    # refuses while a referrer remains

A repoint is per NODE, never per file path: the same old .glb is a landmark in one cell and a
tome stand in the next, so `rows.json` is a list of

    {"scene": "scenes/regions/ember_crown/arena.tscn",   # res:// or repo-relative
     "node": "Nav/GatePillarN",                          # node path, or a bare name if unique
     "new_path": "res://scenes/props/lm_monolith_a.tscn",
     "scale": 1.0,                                        # optional, ABSOLUTE uniform scale
     "y_offset": 0.0,                                     # optional, ADDED to the origin's y
     "collider": "remove"}                                # optional, see below

`node` names either the instancing node itself or the Node3D that wraps it (the usual
`Thing / Model + Collider` trio, in which case its `Model` child is repointed). Only the
`instance=ExtResource(...)` of that one node changes: its name, parent, index, groups, script,
other properties and every child (lights, interactables, map pins) stay byte for byte. `scale`
and `y_offset` edit the PLACEMENT's transform and keep its rotation and position: the wrapping
node when there is one (so the collider beside the model follows), else the instancing node.

A `Dx_*` node is written by tools/compose_district.py from tools/district_layouts.py and is
rewritten on its next run: the tool repoints it if asked and warns that the layout row is the
real edit.

`collider` acts on the StaticBody3D that sits beside the model (a child of the wrapping node):
"keep" (default, and the tool prints what it kept so it can be refitted), "remove" (use when
`new_path` is a wrapper scene that brings its own), or {"box": [w, h, d]} / {"cylinder": [r, h]}
with an optional "y" for the shape centre (default h / 2). Sizes are in the placement's local
space, so a placement scaled 0.5 halves them, exactly as it does for a wrapper's own collider.

ext_resource entries follow the repo convention (path only, no uid): one is added when the new
path is not in the scene yet, and the old one is dropped when its last user is gone.

`refs` and `delete` match the bare file name as well as the res:// path, in scenes, resources,
region specs, C# and tools, because housing decor and the generators name models by file name.
In code (C#, python, gdscript, json) a bare stem (`prp_ruin_pillar` with no extension) also counts
unless --allow-stem is given, since a path can be assembled from it.
Generated outputs (the model manifest, data/world_bake) are not referrers: they are rebuilt.
"""

from __future__ import annotations

import argparse
import difflib
import fnmatch
import io
import json
import math
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LF, CRLF = chr(10), chr(13) + chr(10)

HEADER = re.compile(r"^\[(gd_scene|ext_resource|sub_resource|node|connection|editable)\b.*\]\s*$")
ATTR = r'\b{}="([^"]*)"'
INSTANCE = re.compile(r'instance=ExtResource\("([^"]+)"\)')
TRANSFORM = re.compile(r"^transform = Transform3D\(([^)]*)\)\s*$")

# Where a model can be named. Text files only; generated outputs and vendored sources are skipped.
SCAN_DIRS = ("scenes", "data", "src", "tools", "tests", "assets", "addons")
SCAN_FILES = ("project.godot",)
SCAN_SUFFIXES = {".tscn", ".tres", ".cs", ".py", ".gd", ".json", ".cfg", ".gdshader", ".csv"}
SCAN_SKIP = ("data/world_bake/*", "assets/models/manifest.json", "assets/library/*", "tests/visual_baselines/*",
             "tools/__pycache__/*", "tools/repoint_models.py", "*/bin/*", "*/obj/*")


def fmt(v: float) -> str:
    v = round(v, 4)
    return str(int(v)) if float(v).is_integer() else str(v)


def parent_of(path: str) -> str:
    return path.rsplit("/", 1)[0] if "/" in path else "."


def attr(header: str, name: str) -> str | None:
    found = re.search(ATTR.format(name), header)
    return found.group(1) if found else None


class Scene:
    """A .tscn as lines, with just enough structure to edit one node safely."""

    def __init__(self, text: str):
        self.lines = text.split(LF)

    def text(self) -> str:
        return LF.join(self.lines)

    def headers(self, kind: str | None = None) -> list[int]:
        out = []
        for i, line in enumerate(self.lines):
            found = HEADER.match(line)
            if found and (kind is None or found.group(1) == kind):
                out.append(i)
        return out

    def prop_end(self, start: int) -> int:
        """Index one past the block's own lines: its header and the property lines under it. A blank
        line or a `;` comment ends it, so a comment written above the NEXT block is never taken along."""
        for i in range(start + 1, len(self.lines)):
            if not self.lines[i].strip() or self.lines[i].startswith(";") or HEADER.match(self.lines[i]):
                return i
        return len(self.lines)

    def after(self, start: int) -> int:
        """prop_end plus the one blank line that separates blocks."""
        end = self.prop_end(start)
        return end + 1 if end < len(self.lines) and not self.lines[end].strip() else end

    def node_path(self, index: int) -> str:
        header = self.lines[index]
        parent = attr(header, "parent")
        name = attr(header, "name")
        if parent is None:
            return "."
        return name if parent == "." else f"{parent}/{name}"

    def nodes(self) -> dict[str, int]:
        return {self.node_path(i): i for i in self.headers("node")}

    def find_node(self, wanted: str) -> int:
        nodes = self.nodes()
        if wanted in nodes:
            return nodes[wanted]
        named = [i for path, i in nodes.items() if path.rsplit("/", 1)[-1] == wanted]
        if len(named) == 1:
            return named[0]
        raise ValueError(f"node '{wanted}' " + ("is ambiguous: give its full path" if named else "not found"))

    def uses(self, kind: str, ident: str) -> int:
        return self.text().count(f'{kind}("{ident}")')

    def ext_id_for(self, path: str) -> str | None:
        for i in self.headers("ext_resource"):
            if attr(self.lines[i], "path") == path:
                return attr(self.lines[i], "id")
        return None

    def ext_path(self, ident: str) -> str | None:
        for i in self.headers("ext_resource"):
            if attr(self.lines[i], "id") == ident:
                return attr(self.lines[i], "path")
        return None

    def unique_id(self, kind: str, base: str) -> str:
        taken = {attr(self.lines[i], "id") for i in self.headers(kind)}
        ident, n = base, 2
        while ident in taken:
            ident, n = f"{base}_{n}", n + 1
        return ident

    def add_ext(self, path: str) -> str:
        ident = self.unique_id("ext_resource", "rp_" + re.sub(r"\W", "_", Path(path).stem))
        line = f'[ext_resource type="PackedScene" path="{path}" id="{ident}"]'
        existing = self.headers("ext_resource")
        if existing:
            self.lines.insert(existing[-1] + 1, line)
        else:
            self.lines[1:1] = ["", line]
        return ident

    def add_sub(self, kind: str, base: str, props: list[str]) -> str:
        ident = self.unique_id("sub_resource", base)
        block = [f'[sub_resource type="{kind}" id="{ident}"]', *props, ""]
        existing = self.headers("sub_resource")
        at = self.after(existing[-1]) if existing else self.headers("node")[0]
        self.lines[at:at] = block
        return ident

    def drop_if_unused(self, kind: str, ident: str) -> bool:
        call = "ExtResource" if kind == "ext_resource" else "SubResource"
        if self.uses(call, ident):
            return False
        for i in self.headers(kind):
            if attr(self.lines[i], "id") == ident:
                end = self.after(i) if kind == "sub_resource" else i + 1
                del self.lines[i:end]
                return True
        return False

    def remove_subtree(self, path: str) -> list[str]:
        """Deletes a node and everything under it. Returns the SubResource ids they used."""
        used: list[str] = []
        for i in reversed(self.headers("node")):
            here = self.node_path(i)
            if here == path or here.startswith(path + "/"):
                end = self.after(i)
                used += re.findall(r'SubResource\("([^"]+)"\)', LF.join(self.lines[i:end]))
                del self.lines[i:end]
        return used

    def resource_count(self) -> int:
        return len(self.headers("ext_resource")) + len(self.headers("sub_resource"))

    def fix_load_steps(self, before: int) -> None:
        """Moves load_steps by the resources added or dropped (it is a progress hint; many are stale)."""
        found = re.search(r"load_steps=(\d+)", self.lines[0])
        if found:
            steps = int(found.group(1)) + self.resource_count() - before
            self.lines[0] = self.lines[0].replace(found.group(0), f"load_steps={steps}")


def set_transform(scene: Scene, index: int, scale: float | None, y_offset: float | None) -> None:
    values = [1.0, 0, 0, 0, 1.0, 0, 0, 0, 1.0, 0, 0, 0]
    at = None
    for i in range(index + 1, scene.prop_end(index)):
        found = TRANSFORM.match(scene.lines[i])
        if found:
            values, at = [float(v) for v in found.group(1).split(",")], i
    if scale is not None:
        # Basis is stored row-major; a column is one local axis. Rescale each axis, keep its direction.
        for column in range(3):
            length = math.sqrt(sum(values[row * 3 + column] ** 2 for row in range(3))) or 1.0
            for row in range(3):
                values[row * 3 + column] *= scale / length
    if y_offset is not None:
        values[10] += y_offset
    line = "transform = Transform3D(" + ", ".join(fmt(v) for v in values) + ")"
    if at is None:
        scene.lines.insert(index + 1, line)
    else:
        scene.lines[at] = line


def describe_shape(scene: Scene, ident: str) -> str:
    for i in scene.headers("sub_resource"):
        if attr(scene.lines[i], "id") == ident:
            props = "; ".join(l for l in scene.lines[i + 1:scene.prop_end(i)] if l.strip())
            return f'{attr(scene.lines[i], "type")} {props}'
    return "?"


def apply_row(scene: Scene, row: dict, root: Path, allow_missing: bool) -> list[str]:
    notes: list[str] = []
    new_path = row["new_path"]
    if not new_path.startswith("res://") or not new_path.endswith((".glb", ".gltf", ".tscn")):
        raise ValueError(f"new_path '{new_path}' must be a res:// .glb or .tscn")
    if not allow_missing and not (root / new_path[len("res://"):]).is_file():
        raise ValueError(f"new_path '{new_path}' does not exist (pass --allow-missing to repoint ahead of adoption)")

    index = scene.find_node(row["node"])
    path = scene.node_path(index)
    owner = path
    if not INSTANCE.search(scene.lines[index]):
        model = scene.nodes().get(f"{path}/Model")
        if model is None or not INSTANCE.search(scene.lines[model]):
            raise ValueError(f"node '{path}' instances no scene and has no instanced 'Model' child")
        index, path = model, f"{path}/Model"
    elif path.rsplit("/", 1)[-1] == "Model":
        owner = parent_of(path)

    old_id = INSTANCE.search(scene.lines[index]).group(1)
    old_path = scene.ext_path(old_id)
    if old_path == new_path:
        # Done on an earlier run. Skipping the whole row keeps a re-run from adding y_offset twice.
        return [f"{path}: already {new_path}, row skipped"]
    new_id = scene.ext_id_for(new_path) or scene.add_ext(new_path)
    index = scene.find_node(path)
    scene.lines[index] = scene.lines[index].replace(f'instance=ExtResource("{old_id}")',
                                                    f'instance=ExtResource("{new_id}")')
    dropped = scene.drop_if_unused("ext_resource", old_id)
    notes.append(f"{path}: {old_path} -> {new_path}" + (" (old ext_resource dropped)" if dropped else ""))

    if row.get("scale") is not None or row.get("y_offset") is not None:
        set_transform(scene, scene.find_node(owner), row.get("scale"), row.get("y_offset"))
    if owner.rsplit("/", 1)[-1].startswith("Dx_"):
        notes.append(f"  WARNING {owner} is generated by compose_district.py; change district_layouts.py "
                     "(and COMPOSED/BOXED) or the next compose run undoes this")

    # A node under the instance with neither a type nor an instance is an override of a node that
    # lives inside the OLD model; it will not find its target in the new one.
    for other, i in scene.nodes().items():
        header = scene.lines[i]
        if other.startswith(path + "/") and attr(header, "type") is None and not INSTANCE.search(header):
            notes.append(f"  WARNING {other} overrides a node inside the old model; check or remove it")
    for i in scene.headers("editable"):
        if attr(scene.lines[i], "path") == path:
            notes.append(f"  WARNING [editable path=\"{path}\"] is set; overrides inside it may not apply")

    nodes = scene.nodes()
    bodies = [p for p, i in nodes.items()
              if p != "." and parent_of(p) == owner and attr(scene.lines[i], "type") == "StaticBody3D"]
    collider = row.get("collider", "keep")
    if collider == "keep":
        for body in bodies:
            shapes: list[str] = []
            for child, i in nodes.items():
                if child.startswith(body + "/"):
                    shapes += re.findall(r'SubResource\("([^"]+)"\)', LF.join(scene.lines[i:scene.prop_end(i)]))
            notes.append(f"  collider kept: {body} [" + ", ".join(describe_shape(scene, s) for s in shapes) + "]")
    elif collider == "remove":
        for body in bodies:
            for ident in set(scene.remove_subtree(body)):
                scene.drop_if_unused("sub_resource", ident)
            notes.append(f"  collider removed: {body}")
    elif isinstance(collider, dict) and len({"box", "cylinder"} & set(collider)) == 1:
        if "box" in collider:
            w, h, d = collider["box"]
            kind, props = "BoxShape3D", [f"size = Vector3({fmt(w)}, {fmt(h)}, {fmt(d)})"]
        else:
            r, h = collider["cylinder"]
            kind, props = "CylinderShape3D", [f"radius = {fmt(r)}", f"height = {fmt(h)}"]
        if bodies:
            # Keep the body node itself (name, layers); replace the shapes under it with the one fitted shape.
            for child in [p for p in scene.nodes() if p.startswith(bodies[0] + "/")]:
                for old in set(scene.remove_subtree(child)):
                    scene.drop_if_unused("sub_resource", old)
        owner_name = attr(scene.lines[scene.nodes()[owner]], "name") or "Root"
        ident = scene.add_sub(kind, "RpShape_" + re.sub(r"\W", "_", owner_name), props)
        if bodies:
            body = bodies[0]
            at = scene.after(scene.find_node(body))
        else:
            body = "Collider" if owner == "." else f"{owner}/Collider"
            at = scene.after(scene.find_node(path))
            scene.lines[at:at] = [f'[node name="Collider" type="StaticBody3D" parent="{owner}"]', ""]
            at += 2
        scene.lines[at:at] = [f'[node name="Shape" type="CollisionShape3D" parent="{body}"]',
                              f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, {fmt(collider.get('y', h * 0.5))}, 0)",
                              f'shape = SubResource("{ident}")', ""]
        notes.append(f"  collider set: {body} [{kind} {'; '.join(props)}]")
    else:
        raise ValueError('collider must be "keep", "remove", {"box": [w, h, d]} or {"cylinder": [r, h]}')
    return notes


def scene_file(root: Path, name: str) -> Path:
    return root / (name[len("res://"):] if name.startswith("res://") else name)


def apply(args) -> int:
    root = Path(args.root).resolve()
    rows = json.loads(Path(args.rows).read_text(encoding="utf-8"))
    by_scene: dict[str, list[dict]] = {}
    for row in rows:
        by_scene.setdefault(row["scene"].replace("\\", "/").removeprefix("res://"), []).append(row)
    results, errors = [], 0
    for name, scene_rows in by_scene.items():
        path = scene_file(root, name)
        if not path.is_file():
            print(f"ERROR {name}: scene not found")
            errors += 1
            continue
        # Scenes are LF on disk (.gitattributes); never carry a stray CRLF forward (compose_district.py).
        original = io.open(path, encoding="utf-8", newline="").read().replace(CRLF, LF)
        scene = Scene(original)
        before = scene.resource_count()
        print(f"{path.relative_to(root).as_posix()}")
        for row in scene_rows:
            try:
                for note in apply_row(scene, row, root, args.allow_missing):
                    print(f"  {note}")
            except (ValueError, KeyError) as error:
                print(f"  ERROR {row.get('node')}: {error}")
                errors += 1
        scene.fix_load_steps(before)
        results.append((path, original, scene.text()))
    if errors:
        print(f"{errors} error(s): nothing written")
        return 2
    changed = 0
    for path, original, new in results:
        if new == original:
            continue
        changed += 1
        if args.dry_run:
            label = path.relative_to(root).as_posix()
            sys.stdout.writelines(difflib.unified_diff(original.splitlines(True), new.splitlines(True),
                                                       f"a/{label}", f"b/{label}", n=2))
        else:
            io.open(path, "w", encoding="utf-8", newline="\n").write(new)
    print(f"{len(rows)} row(s), {changed} scene(s) " + ("would change (dry run)" if args.dry_run else "rewritten"))
    return 0


def scan_files(root: Path, ignore: list[str]) -> list[Path]:
    files = [root / f for f in SCAN_FILES if (root / f).is_file()]
    for folder in SCAN_DIRS:
        files += [p for p in (root / folder).rglob("*") if p.suffix in SCAN_SUFFIXES and p.is_file()]
    skip = SCAN_SKIP + tuple(ignore)
    return [p for p in files if not any(fnmatch.fnmatch(p.relative_to(root).as_posix(), g) for g in skip)]


def referrers(root: Path, names: list[str], ignore: list[str], allow_stem: bool) -> dict[str, list[tuple[str, int, str, str]]]:
    """name -> [(file, line number, kind, line)], kind one of path / bare / stem / note."""
    found: dict[str, list] = {name: [] for name in names}
    patterns = {name: re.compile(r"(?<!\w)" + re.escape(Path(name).stem) + r"(\.glb|\.gltf)?(?![\w])") for name in names}
    for path in scan_files(root, ignore):
        text = path.read_text(encoding="utf-8", errors="ignore")
        for name in names:
            if Path(name).stem not in text:
                continue
            for number, line in enumerate(text.split(LF), 1):
                for match in patterns[name].finditer(line):
                    if not match.group(1):
                        # `prp_x.png`, `prp_x.tscn` and the like are other files, not this model.
                        # In a scene or resource a bare stem is an ext_resource id or a node name: Godot
                        # names a model by path there, and that line is already counted.
                        if allow_stem or line[match.end():match.end() + 1] == "." or path.suffix in (".tscn", ".tres"):
                            continue
                        kind = "stem"
                    else:
                        kind = "path" if line[:match.start()].endswith("/") else "bare"
                    if line.lstrip().startswith((";", "#", "//", "*")):
                        kind = "note"  # a comment mentions it; shown, never blocking
                    found[name].append((path.relative_to(root).as_posix(), number, kind, line.strip()[:160]))
    return found


def resolve_models(root: Path, names: list[str]) -> dict[str, list[Path]]:
    """A model may be given as a bare name; find the file(s) it means under assets/models."""
    out = {}
    for name in names:
        direct = scene_file(root, name)
        if direct.is_file():
            out[Path(name).name] = [direct]
        else:
            out[Path(name).name] = sorted((root / "assets" / "models").rglob(Path(name).name))
    return out


def refs(args) -> int:
    root = Path(args.root).resolve()
    models = resolve_models(root, args.models)
    found = referrers(root, list(models), args.ignore, args.allow_stem)
    for name, hits in found.items():
        where = ", ".join(p.relative_to(root).as_posix() for p in models[name]) or "NOT ON DISK"
        live = [h for h in hits if h[2] != "note"]
        print(f"{name} ({where}): {len(live)} reference(s) in {len({h[0] for h in live})} file(s)")
        for file, number, kind, line in hits:
            print(f"  {kind:4} {file}:{number}: {line}")
    return 0


def delete(args) -> int:
    root = Path(args.root).resolve()
    models = resolve_models(root, args.models)
    found = referrers(root, list(models), args.ignore, args.allow_stem)
    refused = 0
    for name, files in models.items():
        hits = [h for h in found[name] if h[2] != "note"]
        if hits:
            refused += 1
            users = sorted({h[0] for h in hits})
            print(f"REFUSED {name}: {len(hits)} reference(s) in {len(users)} file(s): " + ", ".join(users[:6])
                  + (" ..." if len(users) > 6 else ""))
            continue
        if not files:
            print(f"MISSING {name}: no such file under assets/models")
            refused += 1
            continue
        for file in files:
            for target in (file, file.with_name(file.name + ".import")):
                if target.is_file():
                    print(("would delete " if args.dry_run else "deleted ") + target.relative_to(root).as_posix())
                    if not args.dry_run:
                        target.unlink()
            textures = sorted(p.name for p in file.parent.glob(file.stem + "_*.png"))
            if textures:
                print(f"  left in place (check by hand, the prefix can belong to another model): {', '.join(textures)}")
    return 1 if refused else 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split(LF + LF)[0])
    parser.add_argument("--root", default=str(ROOT), help=argparse.SUPPRESS)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("apply", help="repoint the nodes listed in a JSON table")
    p.add_argument("rows")
    p.add_argument("--dry-run", action="store_true", help="print a unified diff and write nothing")
    p.add_argument("--allow-missing", action="store_true", help="accept a new_path that is not on disk yet")
    p.set_defaults(run=apply)
    for name, run in (("refs", refs), ("delete", delete)):
        p = sub.add_parser(name, help="list referrers" if name == "refs" else "delete models that have no referrer")
        p.add_argument("models", nargs="+", help="file name or path of a .glb")
        p.add_argument("--ignore", action="append", default=[], metavar="GLOB",
                       help="repo-relative glob of referrer files to disregard (a retired generator)")
        p.add_argument("--allow-stem", action="store_true", help="do not count a bare stem without extension")
        if name == "delete":
            p.add_argument("--dry-run", action="store_true")
        p.set_defaults(run=run)
    args = parser.parse_args(argv)
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")  # scene comments carry non-cp1252 text
    return args.run(args)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
