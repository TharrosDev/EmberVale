#!/usr/bin/env python3
"""Move a cell scene's authored content to the cell's `origin` (2026-09 world rebuild).

    python tools/shift_cell_content.py            # apply to every cell whose origin moved
    python tools/shift_cell_content.py --check    # exit 1 if any scene is not where its spec says

A region spec places a settlement's content frame at a world point (`Cell.origin`) that is usually
NOT its cell's centre — the whole point being that places stop sitting on the streaming lattice. The
generator shifts the spec's own landforms, roads, pads and water by `origin - center`; this tool
shifts the scene to match:

  * every 3D node parented to the cell root or to `Nav` (never `Nav` itself — the bake parents the
    terrain collider into it, and the conformer reads child positions as cell-local);
  * every schedule a node in the scene names, whose destinations are cell-local.

The offset already applied is recorded on the scene root as `metadata/content_offset`, which Godot
preserves on save, so running this twice is a no-op and changing a cell's boundaries moves its
content by exactly the difference.
"""

from __future__ import annotations

import io
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))

META = re.compile(r"^metadata/content_offset = Vector2\(([^,]+), ([^)]+)\)$", re.M)


def region_cells():
    from region_spec_ember import cells as ember
    from region_spec_frostfang import cells as frost
    from region_spec_ashen import cells as ashen
    from region_spec_sunspire import cells as sun
    from region_spec_pale_concord import cells as pale
    return list(ember()) + list(frost()) + list(ashen()) + list(sun()) + list(pale())


def fmt(value: float) -> str:
    value = round(value, 3)
    return str(int(value)) if float(value).is_integer() else str(value)


def shift_scene(text: str, dx: float, dz: float) -> str:
    lines = text.split("\n")
    out = []
    i = 0
    while i < len(lines):
        line = lines[i]
        out.append(line)
        header = re.match(r'^\[node name="[^"]+"(?: type="([^"]+)")?(?: parent="([^"]*)")?(.*)\]$', line)
        if header and header.group(2) in (".", "Nav"):
            node_type = header.group(1)
            instanced = "instance=" in (header.group(3) or "")
            spatial = instanced or (node_type and node_type.endswith("3D") and node_type != "NavigationRegion3D")
            if spatial:
                j = i + 1
                done = False
                while j < len(lines) and not lines[j].startswith("["):
                    m = re.match(r"^transform = Transform3D\((.*)\)$", lines[j])
                    p = re.match(r"^position = Vector3\((.*)\)$", lines[j])
                    if m:
                        # Only the origin changes; the basis is copied verbatim, never re-rounded.
                        v = [x.strip() for x in m.group(1).split(",")]
                        v[9] = fmt(float(v[9]) + dx)
                        v[11] = fmt(float(v[11]) + dz)
                        lines[j] = "transform = Transform3D(" + ", ".join(v) + ")"
                        done = True
                        break
                    if p:
                        v = [x.strip() for x in p.group(1).split(",")]
                        lines[j] = f"position = Vector3({fmt(float(v[0]) + dx)}, {v[1]}, {fmt(float(v[2]) + dz)})"
                        done = True
                        break
                    j += 1
                if not done:
                    out.append(f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {fmt(dx)}, 0, {fmt(dz)})")
        i += 1
    return "\n".join(out)


def set_meta(text: str, ox: float, oz: float) -> str:
    entry = f"metadata/content_offset = Vector2({fmt(ox)}, {fmt(oz)})"
    if META.search(text):
        return META.sub(entry, text, count=1)
    # The root is the first node header without a parent: `type=` for a plain scene, `instance=` for
    # a scene inheriting a base (the dragon lairs inherit roost.tscn).
    root = re.search(r'^\[node name="[^"]+"(?! parent=)[^\]]*\]$', text, re.M)
    end = text.find("\n\n", root.end())
    end = len(text) if end < 0 else end
    return text[:end] + "\n" + entry + text[end:]


def shift_schedule(path: Path, dx: float, dz: float) -> None:
    raw = io.open(path, encoding="utf-8", newline="").read()
    nl = "\r\n" if "\r\n" in raw else "\n"
    text = raw.replace("\r\n", "\n")

    def move(match: re.Match) -> str:
        x, y, z = (float(v) for v in match.group(1).split(","))
        return f"Destination = Vector3({fmt(x + dx)}, {fmt(y)}, {fmt(z + dz)})"

    io.open(path, "w", encoding="utf-8", newline="").write(
        re.sub(r"Destination = Vector3\(([^)]+)\)", move, text).replace("\n", nl))


def schedule_files() -> dict[str, Path]:
    found = {}
    for tres in (ROOT / "data" / "schedules").glob("*.tres"):
        m = re.search(r'^Id = "([^"]+)"', tres.read_text(encoding="utf-8-sig"), re.M)
        if m:
            found[m.group(1)] = tres
    return found


def main(argv: list[str]) -> int:
    check = "--check" in argv
    schedules = schedule_files()
    stale = 0
    for cell in region_cells():
        scene = ROOT / cell.scene.replace("res://", "")
        raw = io.open(scene, encoding="utf-8", newline="").read()
        nl = "\r\n" if "\r\n" in raw else "\n"
        text = raw.replace("\r\n", "\n")
        recorded = META.search(text)
        ox, oz = (float(recorded.group(1)), float(recorded.group(2))) if recorded else (0.0, 0.0)
        tx, tz = cell.offset
        dx, dz = round(tx - ox, 3), round(tz - oz, 3)
        if not (dx or dz):
            continue
        stale += 1
        print(f"{cell.cell_id}: content {'would move' if check else 'moved'} by ({fmt(dx)}, {fmt(dz)})")
        if check:
            continue
        text = set_meta(shift_scene(text, dx, dz), tx, tz)
        io.open(scene, "w", encoding="utf-8", newline="").write(text.replace("\n", nl))
        for sid in re.findall(r'^ScheduleId = "([^"]+)"', text, re.M):
            if sid in schedules:
                shift_schedule(schedules[sid], dx, dz)
    if check:
        print(f"{stale} cell scene(s) out of place")
        return 1 if stale else 0
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
