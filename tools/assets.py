#!/usr/bin/env python3
"""The one entry point for Embervale 3D asset work.

    python tools/assets.py status                     # what exists, what family, what drifted
    python tools/assets.py validate                   # every hard gate, in the order they need
    python tools/assets.py adopt SRC DEST             # source model -> validated production asset
    python tools/assets.py audit                      # full Blender + Godot inspection
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
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))

import audit_3d
from quality_common import ROOT, command_text, discover_godot, run_process, write_json

MODELS = ROOT / "assets" / "models"
MANIFEST = MODELS / "manifest.json"
RUNS = ROOT / "reports" / "3d" / "runs"
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
        record["status"] = "active" if refs else "unreferenced"
        assets.append(record)
    return {"schema_version": 1,
            "generated_by": "python tools/assets.py status --write",
            "assets": assets}


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
    artifacts = RUNS / dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    artifacts.mkdir(parents=True, exist_ok=True)
    failures, blocked = [], []
    # A fresh clone has no .godot/imported, so every engine gate would fail on "cannot load
    # resource" - which reads as broken art rather than an unimported checkout. "Could not check"
    # and "checked and it is broken" must never look alike.
    imported = (ROOT / ".godot" / "imported").is_dir()
    print("-" * 78)
    for gate in gates:
        if gate.needs == "godot" and (engine is None or not imported):
            reason = ("no Godot: set EMBERVALE_GODOT" if engine is None else
                      "assets not imported yet: godot --headless --path . --import")
            print(f"  {gate.name:<16} BLOCKED   0.0s  {gate.what}")
            print(f"      {reason}")
            blocked.append(gate.name)
            continue
        result = run_process(gate.command, timeout=gate.timeout, cwd=ROOT)
        (artifacts / f"{gate.name}.log").write_text(result.output, encoding="utf-8")
        if result.launch_error:
            status, detail = "BLOCKED", f"could not start: {result.launch_error}"
        elif result.timed_out:
            status, detail = "TIMEOUT", f"exceeded {gate.timeout}s"
        elif result.returncode != 0:
            status, detail = "FAIL", f"exit code {result.returncode}"
        else:
            status, detail = "PASS", ""
        print(f"  {gate.name:<16} {status:<9} {result.elapsed_seconds:4.1f}s  {gate.what}")
        if detail:
            lines = [line for line in result.output.splitlines() if line.strip()]
            for line in (lines if verbose else lines[-15:]):
                print("      " + line)
            print(f"      reproduce: {command_text(gate.command)}")
        if status in ("FAIL", "TIMEOUT"):
            failures.append(gate.name)
        elif status == "BLOCKED":
            blocked.append(gate.name)
    print("-" * 78)
    print(f"logs: {artifacts}")
    if blocked:
        print(f"BLOCKED: {', '.join(blocked)} (could not check - not the same as broken)")
        return 2
    if failures:
        print(f"FAILED: {', '.join(failures)}")
        return 1
    print("all gates passed")
    return 0


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
    result = subprocess.run(command, cwd=ROOT, check=False)
    return result.returncode


BUILD_TARGETS = {
    "npc-kit": ["tools/build_npc_kit.py"],
    "enemy-identity": ["tools/build_enemy_identity_assets.py"],
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
    blender = shutil.which("blender") or next(
        (str(path) for path in (
            Path(r"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"),
            Path(r"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe")) if path.is_file()),
        None)
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
        result = subprocess.run(step, cwd=ROOT, check=False)
        if result.returncode != 0:
            print(f"assets build: step failed ({result.returncode}); "
                  f"the remaining steps did NOT run, so the tree is half-built.", file=sys.stderr)
            return 1
    print("built. Now run: python tools/assets.py validate")
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
    if subprocess.run(step, cwd=ROOT, check=False).returncode != 0:
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
    print("  -> godot --headless --import")
    run_process([str(engine), "--headless", "--path", ".", "--import"], timeout=1800, cwd=ROOT)

    write_json(MANIFEST, build_manifest())
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
        print("      Identity pieces bolt on via EnemyVisualKit; see docs/3D_ASSETS.md.")

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
    status.set_defaults(func=cmd_status)

    validate = sub.add_parser("validate", help="every hard gate, in the required order")
    validate.add_argument("--verbose", "-v", action="store_true")
    validate.set_defaults(func=cmd_validate)

    adopt = sub.add_parser("adopt", help="source model -> validated production asset")
    adopt.add_argument("source")
    adopt.add_argument("dest", help="repo-relative destination, e.g. assets/models/characters/npc_x.glb")
    adopt.add_argument("--kit", action="store_true", help="adopt a MegaKit .gltf instead of a Meshy .glb")
    adopt.add_argument("--shared", action="store_true", help="kit only: share textures rather than embed")
    adopt.add_argument("--root-scale", type=float, default=None)
    adopt.add_argument("--strip-animations", action="store_true")
    adopt.set_defaults(func=cmd_adopt)

    audit = sub.add_parser("audit", help="full Blender + Godot inspection and report")
    audit.add_argument("--output", type=Path, default=None)
    audit.add_argument("--render", choices=("none", "selected", "all"), default="none")
    audit.set_defaults(func=cmd_audit)

    build = sub.add_parser("build", help="Blender rebuild plus its mandatory follow-up")
    build.add_argument("target", choices=sorted(BUILD_TARGETS) + ["anim-library"])
    build.set_defaults(func=cmd_build)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
