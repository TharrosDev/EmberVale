#!/usr/bin/env python3
"""Write scenes/props/lm_<id>.tscn: a generated model, primitive colliders and the detail material.

    python tools/make_landmark_wrapper.py prp_lm_monolith_a prp_lm_arch   # named assets
    python tools/make_landmark_wrapper.py --all                           # every table id on disk
    python tools/make_landmark_wrapper.py --all --check                   # exit 1 if a wrapper is stale
    python tools/make_landmark_wrapper.py --all --dry-run                 # print, write nothing
    python tools/make_landmark_wrapper.py --slices prp_lm_arch            # measure, to fill SHAPES

A wrapper is what a cell instances instead of the bare .glb (precedent: shrine_waystone.tscn), so
every placement of a landmark shares one fitted collider and one material setup:

    LmMonolithA (Node3D, landmark_detail.gd: albedo_tint, detail_scale, detail_strength)
      Model     (the production .glb from tools/meshy_prep_static.py, base-centred, real size)
      Collider  (StaticBody3D, default layer 1 = CombatLayers.WorldStatic, like every static prop)
        Shape, Shape2 ...  (BoxShape3D / CylinderShape3D only. Never a trimesh, never -convcol.)

The collider shapes come from SHAPES below, written in NORMALISED bounds coordinates so a re-rolled
or re-scaled model keeps its fit: x and z are fractions of the bounds' width and depth measured from
the bounds' centre (-0.5 .. 0.5), y is a fraction of the height from the base (0 .. 1), and a
cylinder's radius is a fraction of the SMALLER of width and depth. `--slices` prints a model in
exactly those units. An id with no entry gets one box fitted to the whole bounds.

uid: the ext_resource lines carry a path and no uid, which is this repo's convention for prop
scenes. Godot resolves a path-only reference at load time, so the wrapper can be written before the
model has ever been imported; it only has to be imported before something LOADS the wrapper (the
first `--import` does both in the right order). Godot adds uids itself if the scene is ever saved
from the editor, and nothing depends on that.

Pure Python plus Pillow (through meshy_prep_static). Deterministic: same model, same bytes.
"""

from __future__ import annotations

import argparse
import io
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))

from meshy_prep_static import measure, read_glb, read_vectors  # noqa: E402

LF = chr(10)
DETAIL_SCRIPT = "res://assets/shaders/world/landmark_detail.gd"
# The script's own defaults: a property equal to its default is not written, as Godot would not.
DEFAULT_SCALE, DEFAULT_STRENGTH = 0.8, 1.0

FULL_BOX = ("box", 0.0, 0.0, 1.0, 1.0, 0.0, 1.0)
# Stone brighter than TINT_ABOVE (sRGB luminance of the albedo) reads as plaster in daylight;
# --slices suggests the grey tint that brings it down to TINT_TARGET. The tint multiplies in linear.
TINT_ABOVE, TINT_TARGET = 0.45, 0.40


def to_linear(v: float) -> float:
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def to_srgb(v: float) -> float:
    return v * 12.92 if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055


def box(cx: float, cz: float, w: float, d: float, y0: float = 0.0, y1: float = 1.0, yaw: float = 0.0) -> tuple:
    return ("box", cx, cz, w, d, y0, y1, yaw)


def cyl(cx: float, cz: float, r: float, y0: float = 0.0, y1: float = 1.0) -> tuple:
    return ("cyl", cx, cz, r, y0, y1)


# id -> shapes (normalised, see the module docstring), tint (a Godot Color that multiplies the
# albedo; pale marble and the colossi are deliberately left a little lighter than TINT_TARGET),
# detail (strength of the stone grain; 0 = no script, the imported material is left alone) and
# scale (metres across one lump of grain). Filled by looking at the generated models
# (artifacts/meshy-overhaul/gen/_refines.png, _p2.png) and at `--slices` of each file.
SHAPES: dict[str, dict] = {
    # --- landmarks. GREY is the weathered-grey tint for stone that came back near-white.
    # Shaft on a low ring of kerb stones.
    "prp_lm_waystone": {"shapes": [cyl(0, 0, 0.5, 0, 0.08), cyl(0, 0.02, 0.29, 0.08, 1)], "tint": (0.73, 0.73, 0.73)},
    "prp_lm_monolith_a": {"shapes": [box(0, 0, 0.95, 0.95)]},
    # Standing shaft on its base block at -x, and the fallen drum lying across +x.
    "prp_lm_column_broken": {"shapes": [box(-0.25, 0.05, 0.5, 0.9, 0, 0.2), cyl(-0.24, 0.05, 0.25, 0.2, 1),
                                        box(0.33, 0, 0.125, 0.85, 0, 0.1, -27)], "tint": (0.7, 0.72, 0.74)},
    # An L: the long wall along x, a return wall along z at -x and its stub. The hole in the long
    # wall is a 0.6 m sill up, so it stays closed.
    "prp_lm_wall_ruin": {"shapes": [box(0, 0.2, 0.92, 0.22, 0, 0.9), box(-0.4, -0.1, 0.14, 0.62, 0, 0.9),
                                    box(-0.3, -0.36, 0.3, 0.12, 0, 0.9)], "tint": (0.68, 0.68, 0.68)},
    # Solid: the modelled gateway is a blind tunnel 2.7 m wide and the plan has nothing enterable.
    "prp_lm_gate_tower": {"shapes": [box(0, 0, 0.86, 0.82)], "tint": (0.9, 0.9, 0.9)},
    # The tower, and the rubble heaped round its foot as a low kerb.
    "prp_lm_ruin_tower": {"shapes": [cyl(0, 0, 0.39), cyl(0, 0, 0.47, 0, 0.05)], "tint": (0.86, 0.86, 0.86)},
    # Walk-under: two legs and the lintel. The opening is the middle half of the width, to half height.
    "prp_lm_arch": {"shapes": [box(-0.35, -0.01, 0.2, 0.76, 0, 0.55), box(0.35, -0.01, 0.2, 0.76, 0, 0.55),
                               box(0, -0.01, 0.9, 0.76, 0.5, 1)]},
    # Rubble skirt, then the curtain walls and towers as one hull.
    "prp_lm_iron_citadel": {"shapes": [box(0, 0, 0.97, 0.97, 0, 0.05), box(0, 0, 0.9, 0.9, 0, 0.12),
                                       box(0, 0, 0.76, 0.76)]},
    # Plinth and legs, shoulders, head. The raised arm is 20 m up and gets nothing.
    "prp_lm_colossus_a": {"shapes": [box(-0.02, -0.22, 0.72, 0.55, 0, 0.45), box(-0.01, -0.2, 0.98, 0.6, 0.45, 0.75),
                                     box(-0.02, -0.15, 0.36, 0.45, 0.75, 1)], "tint": (0.92, 0.92, 0.92)},
    # Walk-through bones: tail, four planted feet, the foreclaws under the skull, and the rib cage
    # held clear of the ground. The wings and neck are overhead and get nothing.
    "prp_lm_dragon_skeleton": {"shapes": [box(0, -0.36, 0.1, 0.3, 0, 0.2),
                                          box(-0.3, -0.1, 0.22, 0.28, 0, 0.35), box(0.3, -0.1, 0.22, 0.28, 0, 0.35),
                                          box(-0.28, 0.3, 0.2, 0.22, 0, 0.3), box(0.28, 0.3, 0.2, 0.22, 0, 0.3),
                                          box(0, 0.44, 0.4, 0.12, 0, 0.2), box(-0.02, 0.02, 0.34, 0.4, 0.2, 0.5)]},
    "prp_lm_wall_rampart": {"shapes": [box(0, 0, 1, 0.75)]},
    # Rubble skirt, the face and shoulders, the crown.
    "prp_lm_colossus_head": {"shapes": [box(0, 0, 0.95, 0.95, 0, 0.1), box(-0.02, 0, 0.72, 0.72, 0, 0.6),
                                        box(0.03, 0, 0.45, 0.55, 0.6, 1)], "tint": (0.89, 0.89, 0.89)},
    # Plinth (2 m, not climbable), the two colonnades and the end columns. The floor between is open.
    "prp_lm_godhall_fragment": {"shapes": [box(0, 0, 1, 0.97, 0, 0.07), box(0, -0.38, 0.98, 0.14, 0.07, 1),
                                           box(0, 0.38, 0.98, 0.14, 0.07, 1), box(-0.46, 0, 0.08, 0.4, 0.07, 1),
                                           box(0.46, 0, 0.08, 0.4, 0.07, 1)], "tint": (0.73, 0.73, 0.73)},
    # Dais, seat block, tall back.
    "prp_lm_ash_throne": {"shapes": [box(0, 0, 1, 1, 0, 0.06), box(0, -0.05, 0.82, 0.62, 0.06, 0.42),
                                     box(0, -0.42, 0.85, 0.16, 0.06, 1)]},
    # Two planted tusks on their stones and the crossing overhead.
    "prp_lm_bone_arch": {"shapes": [box(-0.39, 0, 0.22, 1, 0, 0.45), box(0.39, 0, 0.22, 1, 0, 0.45),
                                    box(0, 0, 0.5, 0.6, 0.85, 1)]},
    "prp_lm_bell_tower": {"shapes": [box(0, -0.08, 0.88, 0.78)]},
    # Not generated when this table was filled: one fitted box until somebody has looked at it.
    "prp_lm_palisade_gate": {},
    # --- placed nature. A tree is its trunk; the canopy is overhead. No stone grain on bark and leaves.
    "prp_tree_oak_a": {"shapes": [cyl(-0.01, -0.03, 0.09, 0, 0.45)], "detail": 0.0},
    "prp_tree_dead_a": {"shapes": [cyl(0.02, 0.14, 0.12, 0, 0.5)], "detail": 0.0},
    "prp_pine_fir_a": {"shapes": [cyl(-0.01, -0.02, 0.06, 0, 0.5)], "detail": 0.0},
    # Measured on the PREVIEW, which came back as two palms with one trunk reaching the ground.
    "prp_tree_palm_a": {"shapes": [cyl(0.33, 0.04, 0.04, 0, 0.6)], "detail": 0.0},
    "prp_rock_boulder_a": {"shapes": [cyl(0, 0, 0.48, 0, 0.85)]},
    "prp_rock_rubble_a": {"shapes": [cyl(0, 0, 0.42, 0, 0.6)]},
    "prp_rock_crag_a": {"shapes": [box(0, 0, 0.9, 0.9, 0, 0.4), box(0.03, -0.02, 0.55, 0.55, 0.4, 0.9)]},
    "prp_rock_outcrop_a": {"shapes": [box(0, 0, 0.95, 0.95)]},
    # Ice takes a finer, fainter grain than stone.
    "prp_ice_spire_a": {"shapes": [cyl(0, 0, 0.45, 0, 0.45), cyl(-0.07, 0.05, 0.2, 0.45, 0.95)], "detail": 0.5, "scale": 0.5},
    "prp_ice_wall_a": {"shapes": [box(0, 0, 0.9, 0.85, 0, 0.5), box(-0.02, -0.03, 0.75, 0.55, 0.5, 0.97)],
                       "detail": 0.5, "scale": 0.5},
}


def entry(asset_id: str) -> dict:
    found = SHAPES.get(asset_id, {})
    tree = asset_id.startswith(("prp_tree_", "prp_pine_"))
    return {"shapes": found.get("shapes", [FULL_BOX]), "tint": found.get("tint", (1.0, 1.0, 1.0)),
            "detail": found.get("detail", 0.0 if tree else DEFAULT_STRENGTH),
            "scale": found.get("scale", DEFAULT_SCALE)}


def fmt(v: float) -> str:
    v = round(v, 3)
    return str(int(v)) if float(v).is_integer() else str(v)


def wrapper_name(asset_id: str) -> str:
    """prp_lm_monolith_a -> lm_monolith_a; prp_tree_oak_a -> lm_tree_oak_a."""
    stem = asset_id.removeprefix("prp_")
    return stem if stem.startswith("lm_") else "lm_" + stem


def node_name(asset_id: str) -> str:
    return "".join(part.capitalize() for part in wrapper_name(asset_id).split("_"))


def fit(shape: tuple, low: list[float], high: list[float]) -> tuple[str, list[str], str]:
    """A normalised shape against real bounds -> (resource type, property lines, node transform)."""
    size = [high[axis] - low[axis] for axis in range(3)]
    centre_x, centre_z = (low[0] + high[0]) / 2, (low[2] + high[2]) / 2
    kind, cx, cz = shape[0], shape[1], shape[2]
    x, z = centre_x + cx * size[0], centre_z + cz * size[2]
    if kind == "box":
        w, d, y0, y1 = shape[3:7]
        yaw = math.radians(shape[7]) if len(shape) > 7 else 0.0
        props = [f"size = Vector3({fmt(w * size[0])}, {fmt((y1 - y0) * size[1])}, {fmt(d * size[2])})"]
        resource = "BoxShape3D"
    elif kind == "cyl":
        r, y0, y1 = shape[3:6]
        yaw = 0.0
        props = [f"radius = {fmt(r * min(size[0], size[2]))}", f"height = {fmt((y1 - y0) * size[1])}"]
        resource = "CylinderShape3D"
    else:
        raise ValueError(f"unknown shape kind '{kind}'")
    if not 0.0 <= y0 < y1 <= 1.0:
        raise ValueError(f"shape {shape}: y range must satisfy 0 <= y0 < y1 <= 1")
    y = low[1] + (y0 + y1) / 2 * size[1]
    c, s = math.cos(yaw), math.sin(yaw)
    # rotation_degrees.y = yaw. A .tscn stores the basis by ROWS; the local axes are its columns,
    # x=(c,0,-s) and z=(s,0,c), so the first row reads (c, 0, s) and the third (-s, 0, c).
    transform = f"Transform3D({fmt(c)}, 0, {fmt(s)}, 0, 1, 0, {fmt(-s)}, 0, {fmt(c)}, {fmt(x)}, {fmt(y)}, {fmt(z)})"
    return resource, props, transform


def build(asset_id: str, model_res: str, low: list[float], high: list[float], settings: dict) -> str:
    scripted = settings["detail"] > 0.0 or tuple(settings["tint"]) != (1.0, 1.0, 1.0)
    fitted = [fit(shape, low, high) for shape in settings["shapes"]]
    lines = [f"[gd_scene load_steps={2 + int(scripted) + len(fitted)} format=3]", "",
             f'[ext_resource type="PackedScene" path="{model_res}" id="1_model"]']
    if scripted:
        lines.append(f'[ext_resource type="Script" path="{DETAIL_SCRIPT}" id="2_detail"]')
    lines.append("")
    for index, (resource, props, _) in enumerate(fitted, 1):
        lines += [f'[sub_resource type="{resource}" id="Shape_{index}"]', *props, ""]
    size = [high[axis] - low[axis] for axis in range(3)]
    lines += [f"; Generated by tools/make_landmark_wrapper.py from {Path(model_res).name} "
              f"({fmt(size[0])} x {fmt(size[1])} x {fmt(size[2])} m).",
              "; Edit SHAPES in that tool and run it again; a hand edit here is overwritten.",
              f'[node name="{node_name(asset_id)}" type="Node3D"]']
    if scripted:
        lines.append('script = ExtResource("2_detail")')
        if tuple(settings["tint"]) != (1.0, 1.0, 1.0):
            lines.append("albedo_tint = Color(" + ", ".join(fmt(v) for v in settings["tint"]) + ", 1)")
        if settings["scale"] != DEFAULT_SCALE:
            lines.append(f"detail_scale = {fmt(settings['scale'])}")
        if settings["detail"] != DEFAULT_STRENGTH:
            lines.append(f"detail_strength = {fmt(settings['detail'])}")
    lines += ["", '[node name="Model" parent="." instance=ExtResource("1_model")]', "",
              '[node name="Collider" type="StaticBody3D" parent="."]']
    for index, (_, _, transform) in enumerate(fitted, 1):
        lines += ["", f'[node name="Shape{index if index > 1 else ""}" type="CollisionShape3D" parent="Collider"]',
                  f"transform = {transform}", f'shape = SubResource("Shape_{index}")']
    return LF.join(lines) + LF


def albedo_sample(doc: dict, binary: bytes) -> tuple[float, float, float] | None:
    """Mean sRGB base colour at the vertices' own UVs: what the surface shows, not the atlas padding."""
    from PIL import Image

    total, count = [0.0, 0.0, 0.0], 0
    for mesh in doc.get("meshes", []):
        for primitive in mesh["primitives"]:
            material = doc.get("materials", [{}])[primitive.get("material", 0)] if doc.get("materials") else {}
            slot = material.get("pbrMetallicRoughness", {}).get("baseColorTexture")
            if slot is None or "TEXCOORD_0" not in primitive["attributes"]:
                continue
            view = doc["bufferViews"][doc["images"][doc["textures"][slot["index"]]["source"]]["bufferView"]]
            start = view.get("byteOffset", 0)
            with Image.open(io.BytesIO(binary[start:start + view["byteLength"]])) as picture:
                pixels = picture.convert("RGB")
                width, height = pixels.size
                for u, v in read_vectors(doc, binary, primitive["attributes"]["TEXCOORD_0"], 2):
                    r, g, b = pixels.getpixel((int(u % 1.0 * (width - 1)), int(v % 1.0 * (height - 1))))
                    total[0] += r
                    total[1] += g
                    total[2] += b
                    count += 1
    return tuple(c / count / 255.0 for c in total) if count else None


def slices(path: Path, bands: int) -> None:
    """Footprint per height band in the table's units, to write a SHAPES entry from."""
    doc, binary = read_glb(path)
    points: list[tuple[float, ...]] = []
    for index in sorted({p["attributes"]["POSITION"] for m in doc["meshes"] for p in m["primitives"]}):
        points += read_vectors(doc, binary, index, 3)
    low = [min(p[a] for p in points) for a in range(3)]
    high = [max(p[a] for p in points) for a in range(3)]
    size = [high[a] - low[a] for a in range(3)]
    mid = [(low[a] + high[a]) / 2 for a in range(3)]
    print(f"{path.name}: {size[0]:.2f} x {size[1]:.2f} x {size[2]:.2f} m, base y {low[1]:.3f}, {len(points)} vertices")
    print("  y0-y1      cx     cz      w      d   r(cyl)  verts")
    for band in range(bands):
        y0, y1 = band / bands, (band + 1) / bands
        inside = [p for p in points if y0 <= (p[1] - low[1]) / size[1] <= y1]
        if not inside:
            print(f"  {y0:.2f}-{y1:.2f}  (no vertex)")
            continue
        x0, x1 = min(p[0] for p in inside), max(p[0] for p in inside)
        z0, z1 = min(p[2] for p in inside), max(p[2] for p in inside)
        radius = max(x1 - x0, z1 - z0) / 2 / min(size[0], size[2])
        print(f"  {y0:.2f}-{y1:.2f}  {((x0 + x1) / 2 - mid[0]) / size[0]:5.2f}  {((z0 + z1) / 2 - mid[2]) / size[2]:5.2f}"
              f"  {(x1 - x0) / size[0]:5.2f}  {(z1 - z0) / size[2]:5.2f}  {radius:6.2f}  {len(inside):5d}")
    colour = albedo_sample(doc, binary)
    if colour:
        luminance = 0.2126 * colour[0] + 0.7152 * colour[1] + 0.0722 * colour[2]
        print(f"  albedo at the vertices (sRGB mean): {colour[0]:.2f}, {colour[1]:.2f}, {colour[2]:.2f}; luminance {luminance:.2f}")
        if luminance > TINT_ABOVE:
            grey = to_srgb(to_linear(TINT_TARGET) / to_linear(luminance))
            print(f"  near-white: tint ({grey:.2f}, {grey:.2f}, {grey:.2f}) brings it to luminance {TINT_TARGET}")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split(LF + LF)[0])
    parser.add_argument("ids", nargs="*", help="asset ids (prp_lm_monolith_a) or .glb paths")
    parser.add_argument("--all", action="store_true", help="every SHAPES id whose model is in --models-dir")
    parser.add_argument("--models-dir", type=Path, default=ROOT / "assets" / "models" / "world")
    parser.add_argument("--out", type=Path, default=ROOT / "scenes" / "props")
    parser.add_argument("--res-dir", help="res:// folder the scene names the model in "
                                          "(default: --models-dir, which must then be inside the project)")
    parser.add_argument("--check", action="store_true", help="write nothing; exit 1 if a wrapper is missing or stale")
    parser.add_argument("--dry-run", action="store_true", help="print the scenes, write nothing")
    parser.add_argument("--slices", action="store_true", help="print each model's footprint per height band")
    parser.add_argument("--bands", type=int, default=10)
    args = parser.parse_args(argv)
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    models_dir = args.models_dir.resolve()
    ids = list(args.ids)
    if args.all:
        ids += [asset_id for asset_id in SHAPES if (models_dir / f"{asset_id}.glb").is_file() and asset_id not in ids]
    if not ids:
        parser.error("name at least one asset id, or pass --all")
    if args.res_dir is None:
        try:
            args.res_dir = "res://" + models_dir.relative_to(ROOT).as_posix()
        except ValueError:
            parser.error("--models-dir is outside the project: pass --res-dir (res://assets/models/world)")

    problems = 0
    for name in ids:
        path = Path(name) if name.endswith(".glb") else models_dir / f"{name}.glb"
        asset_id = path.stem
        if not path.is_file():
            print(f"MISSING {asset_id}: {path} (adopt the model first)")
            problems += 1
            continue
        if args.slices:
            slices(path, args.bands)
            continue
        report = measure(path)
        if abs(report["min"][1]) > 0.02:
            print(f"WARNING {asset_id}: base is at y={report['min'][1]:.3f}, not 0. Was it prepared?")
        try:
            text = build(asset_id, f"{args.res_dir.rstrip('/')}/{path.name}", report["min"], report["max"], entry(asset_id))
        except ValueError as error:
            print(f"ERROR {asset_id}: {error}")
            problems += 1
            continue
        target = args.out / f"{wrapper_name(asset_id)}.tscn"
        fitted = "table" if asset_id in SHAPES else "DEFAULT one box"
        if args.dry_run:
            print(f"--- {target.name} ({fitted})" + LF + text)
        elif args.check:
            current = io.open(target, encoding="utf-8", newline="").read() if target.is_file() else None
            if current != text:
                print(f"STALE {target.name}" if current is not None else f"MISSING {target.name}")
                problems += 1
        else:
            args.out.mkdir(parents=True, exist_ok=True)
            io.open(target, "w", encoding="utf-8", newline="\n").write(text)
            print(f"wrote {target} ({len(entry(asset_id)['shapes'])} shape(s), {fitted})")
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
