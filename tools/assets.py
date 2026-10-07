#!/usr/bin/env python3
"""The one entry point for Embervale 3D asset work.

    python tools/assets.py status                     # what exists, what family, what drifted
    python tools/assets.py validate                   # every hard gate, in the order they need
    python tools/assets.py adopt SRC DEST             # source model -> validated production asset
    python tools/assets.py adopt-batch --plan P --source-dir D   # many static models, one import pass
    python tools/assets.py audit                      # full Blender + Godot inspection
    python tools/assets.py audit-weight               # estimated texture video memory by class
    python tools/assets.py build TARGET               # Blender rebuild + its mandatory follow-up

The contract these commands enforce is docs/3D_ASSETS.md. Read that; you do not need to know
which of the twenty scripts under tools/ implements which step, and you should not have to.

⚠️ IT ORCHESTRATES; IT DOES NOT VALIDATE. Every rule lives in the tool that owns it - the glTF
parsing in audit_3d.py, the retarget gate in meshy_rig_probe.gd, the shared-texture rule in
share_nature_textures.py. A check implemented here as well as there is how two validators start
disagreeing, and the one nobody runs is always the correct one.

WHY THIS EXISTS
---------------
Every step below already existed and worked. What did not exist was any way to know the order.
Rebuilding environment assets silently corrupts the rock atlas unless share_nature_textures.py
then repair_architecture_materials.py run straight afterwards; adopting a Meshy body silently
T-poses the actor unless the retarget probe runs and passes. Both facts lived in source comments
and a report folder. They are encoded here now.

EXIT CODES: 0 passed - 1 a gate failed - 2 the harness could not run a requested gate (no Godot,
no Blender), which is deliberately NOT the same as a failure.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import shutil
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))

import audit_3d
from quality_common import ROOT, command_text, discover_godot, discover_blender, run_process, write_json

MODELS = ROOT / "assets" / "models"
MANIFEST = MODELS / "manifest.json"
RUNS = Path(os.environ["EMBERVALE_ARTIFACTS"]) / "assets" if os.environ.get("EMBERVALE_ARTIFACTS") else ROOT / "reports" / "3d" / "runs"
MODEL_EXTENSIONS = {".glb", ".gltf"}

# The retarget's marker. CharacterAnimationComponent.AddSharedLibrary attaches the shared 46-clip
# library only when the imported Skeleton3D is literally named this, so it is also what separates
# a HUMANOID from every other rigged thing in the repo.
RETARGET_SKELETON = "GeneralSkeleton"

HUMANOID, QUADRUPED, VIEWMODEL, ARCHITECTURE, STATIC_PROP, ANIMATION = (
    "HUMANOID", "QUADRUPED", "VIEWMODEL", "ARCHITECTURE", "STATIC_PROP", "ANIMATION")


# --------------------------------------------------------------------------- classification

def classify(path: Path, document: dict[str, Any], import_config: dict[str, Any]) -> dict[str, str]:
    """The rig family of one model, derived only from the file and its .import sidecar.

    Nothing here is authored: the five families already existed in the assets, they had just never
    been named. Keep it that way - a family that needs a human to declare it is a family that will
    be declared wrong.
    """
    name, folder = path.name, path.parent.name
    skinned = bool(document.get("skins"))
    # An animation source carries a skeleton and a bone map but is not a character: it has no mesh
    # at all (tools/strip_anim_glb.py takes it out), and nothing in the game instantiates one. Left
    # in HUMANOID it would be counted as part of the cast and swept by every character gate.
    if name.startswith("anim_"):
        return {"type": ANIMATION, "rig": "general_skeleton" if skinned else "none",
                "anim": "library_source"}
    if name.startswith("fp_"):
        return {"type": VIEWMODEL, "rig": "native" if skinned else "none", "anim": "procedural"}
    if skinned:
        retargeted = (import_config.get("bone_map")
                      and import_config.get("skeleton_name") == RETARGET_SKELETON)
        if retargeted:
            return {"type": HUMANOID, "rig": "general_skeleton", "anim": "shared_library"}
        return {"type": QUADRUPED, "rig": "native", "anim": "own_clips"}
    if folder == "architecture" or name.startswith(("bld_", "mod_")):
        return {"type": ARCHITECTURE, "rig": "none", "anim": "none"}
    return {"type": STATIC_PROP, "rig": "none", "anim": "none"}


def measured_heights() -> dict[str, float]:
    """Real world-space heights, from a local audit run of THIS working tree.

    Deliberately NOT computed here. A skinned mesh's raw AABB is bind-space and can be hundreds of
    metres (docs/3D_ASSETS.md, HUMANOID), so the only truthful numbers come from Blender's
    evaluated geometry - which audit_3d.py already owns. This reads its output rather than growing
    a second, worse implementation.

    ⚠️ It reads reports/3d/runs/ only, never an archived session report. Those were captured
    against the assets of their day: the last committed one still measures chr_player_base at a
    body that was replaced after it ran. A stale height is worse than no height, because a capsule
    gets authored against it. Null here means "run `python tools/assets.py audit`", and that is an
    honest answer.
    """
    candidates = sorted(RUNS.rglob("blender-inspection.json"),
                        key=lambda path: path.stat().st_mtime, reverse=True)
    for candidate in candidates:
        try:
            assets = json.loads(candidate.read_text(encoding="utf-8")).get("assets", {})
        except (OSError, json.JSONDecodeError):
            continue
        found = {path: round(float(record["dimensions"][2]), 3)
                 for path, record in assets.items()
                 if len(record.get("dimensions") or []) == 3}
        if found:
            return found
    return {}


def build_manifest() -> dict[str, Any]:
    """Derive the whole production manifest from what is on disk. Runs in about two seconds."""
    texts = audit_3d.repository_texts()   # already excludes the manifest itself
    heights = measured_heights()
    assets = []
    for path in sorted(p for p in MODELS.rglob("*") if p.suffix.lower() in MODEL_EXTENSIONS):
        relative = audit_3d.rel(path)
        record: dict[str, Any] = {"id": path.stem, "path": "res://" + relative}
        try:
            document, _ = audit_3d.read_gltf(path)
        except (ValueError, OSError) as error:
            record.update({"type": "UNREADABLE", "rig": "none", "anim": "none",
                           "error": str(error), "status": "broken"})
            assets.append(record)
            continue
        import_config = audit_3d.parse_import(path)
        record.update(classify(path, document, import_config))
        record["bone_map"] = import_config.get("bone_map")
        record["root_scale"] = import_config.get("nodes/root_scale", 1.0)
        # The effective in-game height: Godot applies nodes/root_scale on import, so the raw
        # measurement is not what the player stands next to. mnt_horse measures 4.76 m and is a
        # normal horse, because its armature carries a 100x scale that root_scale=0.5 corrects.
        raw_height = heights.get(relative)
        record["height_m"] = (round(raw_height * float(record["root_scale"]), 3)
                              if raw_height is not None else None)
        refs = audit_3d.usage_for(relative, texts)["count"]
        record["refs"] = refs
        record["status"] = ("active" if refs or referenced_by_name(relative)
                            else "unreferenced")
        assets.append(record)
    return {"schema_version": 1,
            "generated_by": "python tools/assets.py status --write",
            "assets": assets}


# ⚠️ A REFERENCE COUNT FOUND BY SEARCHING FOR A LITERAL PATH CANNOT SEE A COMPUTED ONE, and
# `compose_building.py` computes most of the architecture kit's names. It asks for a roof by size
# (`roof_{wide*2}x{deep*2}`) and for the optional pieces by flag (`stairs_exterior` under --stairs,
# `roof_dormer` under --dormer), so the string "mod_roof_6x10" appears nowhere in the repository
# even though deleting the file breaks every 6x10 shell the composer can build.
#
# This bit once: an audit read `mod_stairs_exterior` and `mod_roof_6x10` as dead and proposed
# deleting both. `src/Core/ModelAssets.cs` carries the same warning for gameplay paths, and for the
# same reason -- it keeps every path a complete literal so this counter can see it. The composer
# cannot do that, because the whole point of it is that the name is derived from the geometry.
COMPOSED_BY_PATTERN = (
    re.compile(r"^mod_roof_\d+x\d+$"),
    re.compile(r"^mod_gable_\d+$"),
    re.compile(r"^mod_(stairs_exterior|roof_dormer|roof_awning|roof_supports|vine)$"),
)

# The same blind spot, one directory over: `tools/build_meshy_anim_library.gd` reads its whole
# SOURCE_DIR and takes each clip's gameplay slot from the FILENAME, so all 25 animation sources are
# referenced by the folder rather than by name. They are the inputs to `anim_meshy.res`, which is
# what the game actually loads -- deleting one silently drops a gameplay slot out of the shared
# library, which is `docs/3D_ASSETS.md`'s quietest failure: the actor just stands in its bind pose.
DIRECTORY_CONSUMED = ("assets/models/animations/meshy/",)


def referenced_by_name(relative: str) -> bool:
    """True when a tool derives this file's name, or reads its whole folder, instead of naming it."""
    if any(relative.replace("\\", "/").startswith(prefix) for prefix in DIRECTORY_CONSUMED):
        return True
    stem = Path(relative).stem
    return any(pattern.match(stem) for pattern in COMPOSED_BY_PATTERN)


def load_manifest() -> dict[str, Any] | None:
    if not MANIFEST.is_file():
        return None
    try:
        return json.loads(MANIFEST.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return None


def drift(current: dict[str, Any], committed: dict[str, Any] | None) -> list[str]:
    """What the committed manifest gets wrong about the assets on disk."""
    if committed is None:
        return ["assets/models/manifest.json is missing; run: python tools/assets.py status --write"]
    was = {item["id"]: item for item in committed.get("assets", [])}
    now = {item["id"]: item for item in current["assets"]}
    problems = [f"{asset_id}: on disk but not in the manifest" for asset_id in sorted(now - was.keys())]
    problems += [f"{asset_id}: in the manifest but not on disk" for asset_id in sorted(was.keys() - now.keys())]
    for asset_id in sorted(now.keys() & was.keys()):
        for field in ("path", "type", "rig", "anim", "bone_map", "root_scale", "status"):
            if now[asset_id].get(field) != was[asset_id].get(field):
                problems.append(
                    f"{asset_id}.{field}: manifest says {was[asset_id].get(field)!r}, "
                    f"disk says {now[asset_id].get(field)!r}")
    return problems


# --------------------------------------------------------------------------- gates

@dataclass
class Gate:
    name: str
    what: str
    command: list[str]
    timeout: int = 900
    needs: str = ""          # "godot" or "blender" - a missing one BLOCKS, it does not FAIL


def validate_gates(engine: str | None, humanoids: list[str]) -> list[Gate]:
    """The hard gates, in the only order that works.

    Ordering is the whole point of this list. The static audit has to see the files before the
    engine probes report on the imported result, and the texture check has to run before the
    architecture check reads the materials it shares.
    """
    engine = engine or "godot"
    gates = [
        Gate("static-audit", "every production glTF parses, and no new critical flag",
             [sys.executable, "tools/audit_3d.py", "--static-only",
              "--output", str(RUNS / "validate")], timeout=1200),
        Gate("textures", "shared nature textures are still shared, not re-embedded",
             [sys.executable, "tools/share_nature_textures.py", "--check"]),
        Gate("architecture", "building prefabs, collision modes, materials and callers agree",
             [sys.executable, "tools/check_architecture_kit.py"]),
    ]
    if humanoids:
        # ⚠️ THE GATE THAT MATTERS MOST. A humanoid whose retarget did not run T-poses in game with
        # no log and no error - it is invisible to the compiler, the tests and --validate.
        gates.insert(1, Gate(
            "rig", f"all {len(humanoids)} humanoids retargeted to {RETARGET_SKELETON}",
            [engine, "--headless", "--path", ".", "--script", "res://tools/meshy_rig_probe.gd", "--"]
            + [arg for asset in humanoids for arg in ("--asset", asset)],
            needs="godot"))
        gates.insert(3, Gate(
            "shared-textures", "one Texture2D per nature family reaches the engine",
            [engine, "--headless", "--path", ".", "--script", "res://tools/nature_texture_probe.gd"],
            needs="godot"))
    return gates


def run_gates(gates: list[Gate], engine: str | None, verbose: bool) -> int:
    from embervale_sdk.cli import Run, parser
    args = parser().parse_args(["assets", "validate", "--artifacts", str(RUNS),
                                "--timeout", str(max((g.timeout for g in gates), default=900))])
    run = Run(args)
    run.version()
    imported = (ROOT / ".godot/imported").is_dir()
    for gate in gates:
        if gate.needs == "godot" and (engine is None or not imported):
            run.issue("assets.prerequisite", "Godot and imported assets required: " + gate.name)
            run.result["steps"].append(dict(name=gate.name, exit_code=2))
            continue
        run.process(gate.name, gate.command, timeout=gate.timeout)
    return run.finish()


# --------------------------------------------------------------------------- commands

def cmd_status(args: argparse.Namespace) -> int:
    current = build_manifest()
    if args.write:
        write_json(MANIFEST, current)
        print(f"wrote {audit_3d.rel(MANIFEST)} ({len(current['assets'])} assets)")
        return 0

    families: dict[str, list[dict[str, Any]]] = {}
    for asset in current["assets"]:
        families.setdefault(asset["type"], []).append(asset)
    if args.json:
        # One line, the whole drift list: the table below caps drift at 20 lines.
        problems = drift(current, load_manifest())
        print(json.dumps({
            "ok": not problems, "assets": len(current["assets"]),
            "families": {family: len(assets) for family, assets in sorted(families.items())},
            "unreferenced": sorted(a["id"] for a in current["assets"] if a["status"] == "unreferenced"),
            "drift": problems}))
        return 1 if problems else 0
    print(f"Embervale 3D assets - {len(current['assets'])} production models")
    print("-" * 78)
    for family in (HUMANOID, QUADRUPED, VIEWMODEL, ARCHITECTURE, STATIC_PROP, ANIMATION, "UNREADABLE"):
        assets = families.get(family)
        if not assets:
            continue
        unreferenced = sum(1 for a in assets if a["status"] == "unreferenced")
        rigs = sorted({a["rig"] for a in assets})
        print(f"  {family:<13} {len(assets):>4}   rig={'/'.join(rigs):<16} "
              f"anim={'/'.join(sorted({a['anim'] for a in assets})):<16}"
              + (f"  ({unreferenced} unreferenced)" if unreferenced else ""))
        if args.verbose:
            for asset in assets:
                height = f"{asset['height_m']:.2f}m" if asset.get("height_m") else "-"
                print(f"      {asset['id']:<28} scale={asset['root_scale']:<6} {height:>7} "
                      f"refs={asset['refs']:<4} {asset['bone_map'] or ''}")
    print("-" * 78)
    problems = drift(current, load_manifest())
    if problems:
        print(f"MANIFEST DRIFT ({len(problems)}):")
        for problem in problems[:20]:
            print(f"  {problem}")
        if len(problems) > 20:
            print(f"  ... and {len(problems) - 20} more")
        print("  fix with: python tools/assets.py status --write")
        return 1
    print("manifest matches disk")
    print("contract: docs/3D_ASSETS.md")
    return 0


def cmd_validate(args: argparse.Namespace) -> int:
    current = build_manifest()
    problems = drift(current, load_manifest())
    print(f"Embervale 3D validate - {len(current['assets'])} models")
    if problems:
        print("-" * 78)
        print(f"  {'manifest':<16} {'FAIL':<9}  0.0s  the committed manifest matches disk")
        for problem in problems[:10]:
            print("      " + problem)
        print("      reproduce: python tools/assets.py status --write")
        print("-" * 78)
        print("FAILED: manifest")
        return 1
    humanoids = [a["path"] for a in current["assets"] if a["type"] == HUMANOID]
    engine_path = discover_godot()
    engine = str(engine_path) if engine_path else None
    return run_gates(validate_gates(engine, humanoids), engine, args.verbose)


def cmd_audit(args: argparse.Namespace) -> int:
    output = args.output or RUNS / dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    command = [sys.executable, "tools/audit_3d.py", "--output", str(output)]
    if args.render != "none":
        command += ["--render", args.render]
    print(f"full audit -> {output}")
    result = run_process(command, cwd=ROOT, timeout=1800)
    print(result.output or result.launch_error or "")
    return result.returncode


def apply_texture_budget() -> list[str]:
    """Write every off-budget texture's .import to its class budget; returns the paths changed.

    The budget and the text edit both belong to audit_3d.py. This is only the file write, kept
    here because audit_3d.py never modifies a production asset.
    """
    changed = []
    for record in audit_3d.texture_weight():
        if not record["problems"]:
            continue
        sidecar = ROOT / (record["path"] + ".import")
        text = sidecar.read_text(encoding="utf-8")
        sidecar.write_text(audit_3d.patch_texture_import(text, record["targets"]),
                           encoding="utf-8", newline="\n")
        changed.append(record["path"])
    return changed


def cmd_audit_weight(args: argparse.Namespace) -> int:
    """Estimated texture video memory per budget class, and everything off its budget.

    Pure python: it reads png headers and .import sidecars, so it runs anywhere in a second. It is
    an estimate of what the importer will produce, not a measurement of a running game.
    """
    if args.fix:
        for path in apply_texture_budget():
            print(f"  fixed {path}.import")
    records = audit_3d.texture_weight()
    megabytes = 1024 * 1024
    print(f"Embervale texture weight - {len(records)} textures under assets/models")
    print("-" * 78)
    for group in sorted({record["group"] for record in records}):
        members = [record for record in records if record["group"] == group]
        print(f"  {group:<18} {len(members):>3} textures  {sum(r['bytes'] for r in members) / megabytes:>8.1f} MB")
    print(f"  {'TOTAL':<18} {len(records):>3} textures  {sum(r['bytes'] for r in records) / megabytes:>8.1f} MB")
    print("-" * 78)
    if args.verbose:
        for record in sorted(records, key=lambda r: -r["bytes"]):
            print(f"  {record['bytes'] / megabytes:>6.2f} MB  {'x'.join(map(str, record['size'])):<10} "
                  f"limit={record['params'].get('process/size_limit', '?'):<5} {record['path']}")
        print("-" * 78)
    problems = [f"{record['path']} ({'x'.join(map(str, record['size']))} {record['group']}/{record['role']}): "
                + ", ".join(record["problems"]) for record in records if record["problems"]]
    # Automatic LODs and shadow meshes are the two mesh import settings every model must carry.
    for path in sorted(p for p in MODELS.rglob("*") if p.suffix.lower() in MODEL_EXTENSIONS):
        config = audit_3d.parse_import(path)
        missing = [key for key in ("meshes/generate_lods", "meshes/create_shadow_meshes")
                   if config.get(key) is not True]
        if missing:
            problems.append(f"{audit_3d.rel(path)}: {', '.join(missing)} is not true")
    if problems:
        print(f"OFF BUDGET ({len(problems)}):")
        for problem in problems:
            print(f"  {problem}")
        print("  fix textures with: python tools/assets.py audit-weight --fix   (then a Godot import pass)")
        return 1 if args.check else 0
    print("every texture is within its class budget; every model has LODs and shadow meshes")
    print("contract: docs/3D_ASSETS.md (Texture weight)")
    return 0


BUILD_TARGETS = {
    "primitive-creatures": ["tools/build_enemy_identity_assets.py"],
    "environment": ["tools/build_environment_assets.py"],
    "player-weapons": ["tools/build_player_weapon_assets.py"],
}


def cmd_build(args: argparse.Namespace) -> int:
    """Run a Blender authoring script and then the follow-up it cannot skip.

    ⚠️ THIS ORDER IS NOT A PREFERENCE. Blender's glTF exporter re-embeds the shared rock atlas and
    resets material factors on every write, so an export followed by neither of these ships a
    duplicated 200 MB texture set and metallic plaster. That was a comment in one script's header;
    it is a sequence here.
    """
    blender_path = discover_blender()
    blender = str(blender_path) if blender_path else None
    if args.target == "anim-library":
        engine = discover_godot()
        if engine is None:
            print("assets build anim-library: needs Godot. Set EMBERVALE_GODOT.", file=sys.stderr)
            return 2
        steps = [[str(engine), "--headless", "--path", ".", "--script",
                  "res://tools/extract_anim_library.gd"]]
    else:
        if blender is None:
            print("assets build: Blender not found; put it on PATH.", file=sys.stderr)
            return 2
        steps = [[blender, "--background", "--factory-startup", "--python",
                  BUILD_TARGETS[args.target][0], "--", str(ROOT)],
                 [sys.executable, "tools/share_nature_textures.py"],
                 [sys.executable, "tools/repair_architecture_materials.py"]]
    for step in steps:
        print(f"  -> {command_text(step)}")
        result = run_process(step, cwd=ROOT, timeout=1800)
        print(result.output or result.launch_error or "")
        if result.returncode != 0:
            print(f"assets build: step failed ({result.returncode}); "
                  f"the remaining steps did NOT run, so the tree is half-built.", file=sys.stderr)
            return 1
    print("built. Now run: python tools/assets.py validate")
    return 0


def import_and_budget(engine: Path) -> int:
    """One import pass, the class texture budget, the pass that applies it, then the manifest.

    However many models were just written, this is the whole engine cost of adopting them.
    """
    print("  -> godot --headless --import")
    imported = run_process([str(engine), "--headless", "--path", ".", "--import"], timeout=1800, cwd=ROOT)
    from embervale_sdk.contract import diagnostics_from_log
    if imported.returncode or any(d["severity"] == "error" for d in diagnostics_from_log(imported.output, "import")):
        print(imported.output or imported.launch_error)
        print("assets: import failed; manifest and rig checks were not advanced.")
        return 1

    # The first import is what creates a new texture's .import (and extracts an embedded image to
    # <model>_<image name>.png), so the class budget can only be written now, and a second pass is
    # what applies it. Without this a fresh 2048 atlas ships uncapped until someone audits it.
    budgeted = apply_texture_budget()
    if budgeted:
        print(f"  -> texture budget written to {len(budgeted)} .import file(s); importing again")
        imported = run_process([str(engine), "--headless", "--path", ".", "--import"], timeout=1800, cwd=ROOT)
        if imported.returncode:
            print(imported.output or imported.launch_error)
            print("assets: the texture-budget reimport failed.")
            return 1

    write_json(MANIFEST, build_manifest())
    return 0


# What the engine itself writes for a static model, less the uid, the imported path and [deps],
# which it fills in on the first import while keeping every value here. Written beside a NEW model
# so that pass is already the right one: LODs and shadow meshes on, no collision generated from the
# visual mesh (_subresources is empty and no node carries a -col suffix), embedded images extracted
# to .png (1) so audit-weight can budget them. An existing sidecar is never overwritten.
STATIC_IMPORT = """[remap]

importer="scene"
importer_version=1
type="PackedScene"

[params]

nodes/root_type=""
nodes/root_name=""
nodes/root_script=null
mesh_library/use_node_names_as_mesh_names=false
array_mesh/deduplicate_surfaces=true
nodes/apply_root_scale=true
nodes/root_scale=1.0
nodes/import_as_skeleton_bones=false
nodes/use_name_suffixes=true
nodes/use_node_type_suffixes=true
meshes/ensure_tangents=true
meshes/generate_lods=true
meshes/create_shadow_meshes=true
meshes/light_baking=1
meshes/lightmap_texel_size=0.2
meshes/force_disable_compression=false
skins/use_named_skins=true
animation/import=true
animation/fps=30
animation/trimming=false
animation/remove_immutable_tracks=true
animation/import_rest_as_RESET=false
import_script/path=""
materials/extract=0
materials/extract_format=0
materials/extract_path=""
_subresources={}
gltf/naming_version=2
gltf/embedded_image_handling=1
gltf/texture_map_mode=1
"""

# The plan kinds that are a static prop. A dragon or a beast is rebound onto a rig first and an NPC
# body goes through `adopt`, so none of those is this command's to write.
STATIC_KINDS = ("nature", "landmark", "prop")


def cmd_adopt_batch(args: argparse.Namespace) -> int:
    """Many static models in, ONE import pass owed.

    `adopt` launches the engine twice per model. A tier of forty props is one prep each (pure
    python, tools/meshy_prep_static.py owns it) and then the same two passes for all of them, so
    this writes every .glb and its sidecar and leaves the engine work to --import or to the
    printed commands.
    """
    import tempfile
    import meshy_prep_static

    jobs: list[tuple[Path, Path, dict[str, Any] | None]] = []   # source, folder/name, prep options
    skipped: list[str] = []
    if args.plan:
        if args.source_dir is None:
            print("assets adopt-batch: --plan needs --source-dir", file=sys.stderr)
            return 2
        for item in json.loads(args.plan.read_text(encoding="utf-8")):
            if (args.only and item["id"] not in args.only) or (args.priority and item.get("priority") not in args.priority):
                continue
            if item.get("kind") not in STATIC_KINDS:
                skipped.append(f"{item['id']}: a {item.get('kind')} is not a static prop")
                continue
            source = args.source_dir / f"{item['id']}.{args.stage}.glb"
            if not source.is_file():
                skipped.append(f"{item['id']}: no {source.name} yet")
                continue
            # An item's optional "prep" object overrides the defaults (origin, yaw, length, offset).
            options = {"height": item["heightMetres"], **item.get("prep", {})}
            if "length" in options:
                options.pop("height")
            jobs.append((source, Path(item["folder"]) / f"{item['id']}.glb", options))
    if args.sources and not args.folder:
        print("assets adopt-batch: prepared sources need --folder", file=sys.stderr)
        return 2
    jobs += [(Path(source), Path(args.folder) / Path(source).name, None) for source in args.sources]
    if not jobs and not args.run_import:
        print("assets adopt-batch: nothing to adopt" + "".join(f"\n  {line}" for line in skipped), file=sys.stderr)
        return 2

    root = Path(tempfile.mkdtemp(prefix="adopt-batch-")) if args.dry_run else MODELS
    failed: list[str] = []
    for source, relative, options in jobs:
        dest, real = root / relative, MODELS / relative
        # A plan id can be the name of a model the game already loads (wpn_sword_iron). Writing over
        # it is a replacement with its own checks (grip offset, the scenes that scale it), so it is
        # never a side effect of taking a whole tier: it has to be asked for.
        if real.is_file() and not args.replace:
            if not args.dry_run:
                failed.append(f"{source.name}: {relative.as_posix()} already exists; pass --replace to overwrite it")
                continue
            print(f"!! {relative.as_posix()} already exists: the real run refuses it without --replace")
        group = audit_3d.texture_group(real.with_name(f"{real.stem}_BaseColor.png"))
        try:
            if options is None:
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, dest)
                report = meshy_prep_static.measure(dest)
                report["textures"] = [f"{i['name']}: {i['format']} {i['size'][0]}x{i['size'][1]}" for i in report["images"]]
            else:
                report = meshy_prep_static.prepare(source, dest, max_texture=audit_3d.TEXTURE_BUDGET[group], **options)
        except (ValueError, OSError, KeyError) as error:
            failed.append(f"{source.name}: {error}")
            continue
        print(meshy_prep_static.describe(report).replace(dest.name, relative.as_posix(), 1) + f"\n  class   {group}")
        sidecar = Path(str(real) + ".import")
        if sidecar.is_file():
            inherited = audit_3d.parse_import(real).get("nodes/root_scale", 1.0)
            print(f"  import  kept the existing {sidecar.name}"
                  + (f"\n  !! it carries nodes/root_scale={inherited}; the new file is already at real size" if inherited != 1.0 else ""))
        elif not args.dry_run:
            sidecar.write_text(STATIC_IMPORT, encoding="utf-8", newline="\n")
            print(f"  import  wrote {sidecar.name}")
    if args.dry_run:
        shutil.rmtree(root, ignore_errors=True)
    print("-" * 78)
    for line in skipped:
        print(f"  skipped {line}")
    for line in failed:
        print(f"  FAILED  {line}")
    print(f"{len(jobs) - len(failed)} of {len(jobs)} model(s) " + ("would be written (dry run, nothing kept)" if args.dry_run else "written"))
    if failed or args.dry_run:
        return 1 if failed else 0

    engine = discover_godot()
    if args.run_import:
        if engine is None:
            print("assets adopt-batch: --import needs Godot. Set EMBERVALE_GODOT.", file=sys.stderr)
            return 2
        if import_and_budget(engine):
            return 1
        print("imported, budgeted and manifested. Now run: python tools/assets.py validate")
        return 0
    godot = command_text([str(engine or "godot"), "--headless", "--path", ".", "--import"])
    print("NOT imported yet. In this order, one engine launch at a time:")
    print(f"  1. {godot}")
    print("  2. python tools/assets.py audit-weight --fix")
    print(f"  3. {godot}")
    print("  4. python tools/assets.py status --write")
    print("  5. python tools/assets.py validate")
    print("  6. python tools/assets.py audit-weight --check")
    print("steps 1 to 4 as one command: python tools/assets.py adopt-batch --import")
    return 0


def cmd_adopt(args: argparse.Namespace) -> int:
    """Source model in, validated production asset out.

    The point of this command is that adopting a humanoid is one step and not six. The retarget
    probe at the end is not optional: an unretargeted body imports cleanly, compiles, passes the
    tests, passes --validate, and then T-poses in front of the player.
    """
    source, dest = Path(args.source), Path(args.dest)
    if not source.is_file():
        print(f"assets adopt: no such source {source}", file=sys.stderr)
        return 2
    if not dest.is_absolute():
        dest = ROOT / dest
    replacing = dest.is_file()

    if args.kit:
        step = [sys.executable, "tools/adopt_kit_model.py", str(source), str(dest)]
        if args.root_scale is not None:
            step += [f"--scale={args.root_scale}"]
        if args.shared:
            step += ["--shared"]
    else:
        step = [sys.executable, "tools/meshy_adopt.py", str(source), str(dest), "--patch-import"]
        if args.root_scale is not None:
            step += ["--root-scale", str(args.root_scale)]
        if args.strip_animations:
            step += ["--strip-animations"]
    print(f"  -> {command_text(step)}")
    adopted = run_process(step, cwd=ROOT, timeout=1800)
    if adopted.returncode != 0:
        # ⚠️ run_process CAPTURES both streams, so a failing step used to print nothing at all and
        # the command looked like it had simply decided not to work. The child's own message is the
        # only thing that says which of the two adoption paths was the wrong one.
        print(adopted.output.rstrip(), file=sys.stderr)
        return 1

    # ⚠️ A replacement inherits its predecessor's .import, including its root_scale. npc_woman_dress
    # carried 0.384 and a replacement would have imported at 38% of its authored size.
    if replacing and args.root_scale is None:
        inherited = audit_3d.parse_import(dest).get("nodes/root_scale", 1.0)
        if inherited != 1.0:
            print(f"  !! {dest.name} inherited nodes/root_scale={inherited} from the model it "
                  f"replaces. Confirm that is still right, or re-run with --root-scale.")

    engine = discover_godot()
    if engine is None:
        print("  !! Godot not found - the asset was adopted but NOT imported or rig-checked.")
        print("     Set EMBERVALE_GODOT, then: python tools/assets.py validate")
        return 2
    if import_and_budget(engine):
        return 1
    entry = next((a for a in load_manifest()["assets"] if a["id"] == dest.stem), None)
    if entry is None:
        print(f"assets adopt: {dest.stem} did not reach the manifest", file=sys.stderr)
        return 1
    print(f"  {dest.stem}: {entry['type']} rig={entry['rig']} anim={entry['anim']} "
          f"scale={entry['root_scale']}")

    if entry["type"] == HUMANOID:
        probe = [str(engine), "--headless", "--path", ".", "--script",
                 "res://tools/meshy_rig_probe.gd", "--", "--asset", entry["path"]]
        print(f"  -> {command_text(probe)}")
        result = run_process(probe, timeout=600, cwd=ROOT)
        print("\n".join("      " + line for line in result.output.splitlines() if line.strip()))
        if result.returncode != 0:
            print("assets adopt: THE RETARGET DID NOT RUN. This model will T-pose in game.",
                  file=sys.stderr)
            return 1
    elif not entry["bone_map"] and entry["type"] == QUADRUPED:
        print("      QUADRUPED: keeps its own rig and its own clips - no retarget, by design.")

    print("\nadopted. Commit the .glb, its .import and manifest.json together, then:")
    print("  python tools/assets.py validate")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    status = sub.add_parser("status", help="what exists, what family, what drifted")
    status.add_argument("--write", action="store_true", help="regenerate assets/models/manifest.json")
    status.add_argument("--verbose", "-v", action="store_true", help="list every asset")
    status.add_argument("--json", action="store_true", help="one JSON line: counts, unreferenced ids, all drift")
    status.set_defaults(func=cmd_status)

    validate = sub.add_parser("validate", help="every hard gate, in the required order")
    validate.add_argument("--verbose", "-v", action="store_true")
    validate.set_defaults(func=cmd_validate)

    adopt = sub.add_parser("adopt", help="source model -> validated production asset")
    adopt.add_argument("source")
    adopt.add_argument("dest", help="repo-relative destination, e.g. assets/models/characters/npc_x.glb")
    adopt.add_argument("--kit", action="store_true",
                       help="the source is a STATIC model (.gltf or .glb) rather than a rigged Meshy body")
    adopt.add_argument("--shared", action="store_true", help="kit only: share textures rather than embed")
    adopt.add_argument("--root-scale", type=float, default=None)
    adopt.add_argument("--strip-animations", action="store_true")
    adopt.set_defaults(func=cmd_adopt)

    batch = sub.add_parser("adopt-batch", help="many static models -> assets/models/<folder>/, one import pass")
    batch.add_argument("sources", nargs="*", help="already prepared .glb files, copied as they are (needs --folder)")
    batch.add_argument("--folder", help="assets/models/<folder> for the prepared sources")
    batch.add_argument("--plan", type=Path,
                       help="generation plan json; every static item whose file exists is prepared to its "
                            "heightMetres and class texture cap and written to its folder")
    batch.add_argument("--source-dir", type=Path, help="where the plan's <id>.<stage>.glb files are")
    batch.add_argument("--stage", choices=("refine", "preview"), default="refine")
    batch.add_argument("--only", nargs="+", default=[], metavar="ID", help="plan ids to take")
    batch.add_argument("--priority", type=int, action="append", default=[], help="plan priority to take; repeatable")
    batch.add_argument("--dry-run", action="store_true", help="prepare into a temp folder, report, keep nothing")
    batch.add_argument("--replace", action="store_true",
                       help="allow writing over a model that already exists in assets/models (refused otherwise)")
    batch.add_argument("--import", dest="run_import", action="store_true",
                       help="then run the import pass, the texture budget, its reimport and the manifest write")
    batch.set_defaults(func=cmd_adopt_batch)

    audit = sub.add_parser("audit", help="full Blender + Godot inspection and report")
    audit.add_argument("--output", type=Path, default=None)
    audit.add_argument("--render", choices=("none", "selected", "all"), default="none")
    audit.set_defaults(func=cmd_audit)

    weight = sub.add_parser("audit-weight", help="estimated texture video memory by budget class")
    weight.add_argument("--check", action="store_true", help="exit 1 when anything is off budget")
    weight.add_argument("--fix", action="store_true", help="write the class budget into each .import")
    weight.add_argument("--verbose", "-v", action="store_true", help="list every texture, heaviest first")
    weight.set_defaults(func=cmd_audit_weight)

    build = sub.add_parser("build", help="Blender rebuild plus its mandatory follow-up")
    build.add_argument("target", choices=sorted(BUILD_TARGETS) + ["anim-library"])
    build.set_defaults(func=cmd_build)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
