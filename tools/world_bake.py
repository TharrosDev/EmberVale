#!/usr/bin/env python3
"""Build and verify Embervale's committed production-world package.

    python tools/world_bake.py --bake                 # rebake only the regions whose inputs changed
    python tools/world_bake.py --bake --full          # rebake every region
    python tools/world_bake.py --bake --region ember_crown   # also force the named region(s)
    python tools/world_bake.py --check                # CI: name every stale/missing/unexpected artifact

A region's signature covers the shared inputs (world code, biomes, models, shaders) plus its own
region resource and cell scenes, so editing one realm rebakes one realm. A change to a shared input
still rebakes them all: nothing here can prove which cell uses which model.

The source fingerprint is the authority. It covers world specifications, generated region inputs,
cell scenes and the code that turns them into terrain/scatter/navigation. Output hashes make partial
or hand-edited bakes fail even when their manifest was not updated.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

from quality_common import ROOT, require_godot, run_process, write_json

BAKE_ROOT = ROOT / "data" / "world_bake"
MANIFEST = BAKE_ROOT / "manifest.json"

SOURCE_GLOBS = (
    "tools/gen_regions.py",
    "tools/region_spec_*.py",
    "data/regions/*.tres",
    "data/world_gen/*.tres",
    "data/biomes/**/*.tres",
    "data/world/**/*.tres",
    "scenes/regions/**/*.tscn",
    "src/World/*.cs",
    "src/Bootstrap/HeadlessWorldBake.cs",
    "src/Combat/CombatLayers.cs",
    # Models and world shaders only. UI art, fonts and audio never reach a prepared cell, and
    # hashing their import sidecars made every new icon a thirty-minute rebake.
    "assets/models/**/*.import",
    "assets/models/**/*.tres",
    "assets/shaders/world/*",
)

REGION_SCENES = "scenes/regions/"


def files_for(patterns: tuple[str, ...]) -> list[Path]:
    return sorted({path for pattern in patterns for path in ROOT.glob(pattern) if path.is_file()},
                  key=lambda path: path.relative_to(ROOT).as_posix())


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_hashes() -> dict[str, str]:
    return {path.relative_to(ROOT).as_posix(): source_digest(path) for path in files_for(SOURCE_GLOBS)}


def source_digest(path: Path) -> str:
    # Git checks C#/Python out with platform-native endings. Source identity must survive
    # that checkout conversion; binary artifacts retain exact byte hashes via digest().
    payload = path.read_bytes()
    if path.suffix in {".cs", ".py", ".tres", ".tscn", ".import", ".gdshader", ".gdshaderinc"}:
        payload = payload.replace(b"\r\n", b"\n")
    return hashlib.sha256(payload).hexdigest()


def aggregate(hashes: dict[str, str]) -> str:
    payload = "".join(f"{name}\0{value}\n" for name, value in sorted(hashes.items()))
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def region_outputs() -> dict[str, tuple[Path, set[Path]]]:
    """Region id -> (its data/regions resource, every artifact its bake writes)."""
    regions: dict[str, tuple[Path, set[Path]]] = {}
    for path in sorted((ROOT / "data" / "regions").glob("*.tres")):
        text = path.read_text(encoding="utf-8")
        root = text.split("[resource]", 1)[-1]
        match = re.search(r'^Id = "([^"]+)"', root, re.MULTILINE)
        if match is None:
            continue
        region_id = match.group(1)
        region_slug = region_id.replace(".", "_").replace(":", "_")
        outputs = {BAKE_ROOT / "regions" / f"{region_slug}.res"}
        cell_prefix = region_id.removeprefix("region.") + "."
        for cell_id in re.findall(r'^Id = "([a-z0-9_]+\.[a-z0-9_]+)"', text, re.MULTILINE):
            if not cell_id.startswith(cell_prefix):
                continue
            cell_slug = cell_id.replace(".", "_").replace(":", "_")
            outputs.add(BAKE_ROOT / "cells" / region_slug / f"{cell_slug}.scn")
        regions[region_id] = (path, outputs)
    return regions


def expected_outputs() -> set[Path]:
    return {output for _, outputs in region_outputs().values() for output in outputs}


def region_signatures(sources: dict[str, str]) -> dict[str, str]:
    """One signature per region: the shared inputs plus that region's own resource and scenes.

    A scene folder belongs to the regions whose resource names a scene in it; a folder nobody names
    stays shared, so an unrecognised layout costs a full bake rather than a stale cell. Region specs
    are left out on purpose: the generated resource carries their effect and --bake refuses to run
    while gen_regions.py --check reports drift.
    """
    regions = region_outputs()
    resources = {path.relative_to(ROOT).as_posix(): region for region, (path, _) in regions.items()}
    folders: dict[str, set[str]] = {}
    for region, (path, _) in regions.items():
        for folder in re.findall(r'res://scenes/regions/([^/"]+)/', path.read_text(encoding="utf-8")):
            folders.setdefault(folder, set()).add(region)

    shared: dict[str, str] = {}
    own: dict[str, dict[str, str]] = {region: {} for region in regions}
    for name, value in sources.items():
        if re.fullmatch(r"tools/region_spec_[^/]*\.py", name):
            continue
        owners: set[str] = set()
        if name in resources:
            owners = {resources[name]}
        elif name.startswith(REGION_SCENES) and "/" in name[len(REGION_SCENES):]:
            owners = folders.get(name[len(REGION_SCENES):].split("/", 1)[0], set())
        if owners:
            for region in owners:
                own[region][name] = value
        else:
            shared[name] = value
    return {region: aggregate({**shared, **mine}) for region, mine in own.items()}


def stale_regions(manifest: dict, signatures: dict[str, str]) -> list[str]:
    """Regions that must be rebaked: a changed signature, or an output that is missing or edited."""
    recorded_regions: dict[str, str] = manifest.get("regions", {})
    recorded_outputs: dict[str, str] = manifest.get("outputs", {})
    stale: list[str] = []
    for region, (_, outputs) in region_outputs().items():
        intact = all(
            output.is_file() and recorded_outputs.get(output.relative_to(ROOT).as_posix()) == digest(output)
            for output in outputs)
        if recorded_regions.get(region) != signatures[region] or not intact:
            stale.append(region)
    return stale


def current_outputs() -> set[Path]:
    if not BAKE_ROOT.exists():
        return set()
    return {path for path in BAKE_ROOT.rglob("*") if path.is_file() and path != MANIFEST}


def check() -> int:
    problems: list[str] = []
    if not MANIFEST.is_file():
        print("world bake is stale: missing data/world_bake/manifest.json")
        return 1
    try:
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"world bake is stale: unreadable manifest: {error}")
        return 1

    sources = source_hashes()
    signature = aggregate(sources)
    recorded_sources = manifest.get("sources", {})
    for name in sorted(set(sources) | set(recorded_sources)):
        if name not in recorded_sources:
            problems.append(f"new source requires bake: {name}")
        elif name not in sources:
            problems.append(f"removed source requires bake: {name}")
        elif sources[name] != recorded_sources[name]:
            problems.append(f"changed source requires bake: {name}")
    if manifest.get("source_signature") != signature:
        problems.append("source signature does not match current inputs")

    expected = expected_outputs()
    actual = current_outputs()
    for path in sorted(expected - actual):
        problems.append(f"missing output: {path.relative_to(ROOT).as_posix()}")
    for path in sorted(actual - expected):
        problems.append(f"unexpected output: {path.relative_to(ROOT).as_posix()}")

    recorded_outputs: dict[str, str] = manifest.get("outputs", {})
    for path in sorted(expected & actual):
        name = path.relative_to(ROOT).as_posix()
        if recorded_outputs.get(name) != digest(path):
            problems.append(f"modified output: {name}")
    for name in sorted(set(recorded_outputs) - {p.relative_to(ROOT).as_posix() for p in expected}):
        problems.append(f"manifest lists obsolete output: {name}")

    if problems:
        print("world bake is stale:")
        for problem in problems:
            print(f"  - {problem}")
        print("regenerate with: python tools/world_bake.py --bake")
        return 1

    print(f"world bake current: {len(expected)} artifacts, source {signature[:12]}")
    return 0


def bake(full: bool = False, forced: tuple[str, ...] = ()) -> int:
    before = source_hashes()
    signature = aggregate(before)
    signatures = region_signatures(before)
    unknown = [name for name in forced if _region_id(name) not in signatures]
    if unknown:
        print(f"unknown region(s): {', '.join(unknown)}; known: {', '.join(sorted(signatures))}", file=sys.stderr)
        return 2
    try:
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        manifest = {}
    targets = sorted(signatures) if full else sorted(
        set(stale_regions(manifest, signatures)) | {_region_id(name) for name in forced})
    obsolete = current_outputs() - expected_outputs()
    if not targets and not obsolete and manifest.get("source_signature") == signature:
        print("world bake: every region is current; nothing to do")
        return check()
    print(f"world bake: {len(targets)} of {len(signatures)} region(s) to prepare: {', '.join(targets) or 'none'}")
    generation = run_process(
        [sys.executable, "tools/gen_regions.py", "--check"], cwd=ROOT, timeout=120)
    print(generation.output, end="")
    if generation.returncode != 0:
        print("region resources are stale; run python tools/gen_regions.py before baking", file=sys.stderr)
        return generation.returncode

    build = run_process(
        ["dotnet", "build", "-p:EmbervaleTooling=false"], timeout=600, cwd=ROOT)
    if build.returncode != 0:
        print(build.output, file=sys.stderr)
        return build.returncode

    for path in sorted(obsolete):
        path.unlink()
    if targets:
        engine = require_godot()
        result = run_process(
            [str(engine), "--headless", "--path", str(ROOT), "--", "--world-bake",
             f"--world-bake-signature={signature}",
             "--world-bake-regions=" + ",".join(f"{region}={signatures[region]}" for region in targets)],
            timeout=5400, cwd=ROOT)  # ~27 min for the 52-cell Ember Crown alone
        print(result.output, end="")
        if result.returncode != 0:
            return result.returncode

    after = source_hashes()
    if after != before:
        print("world inputs changed while the bake was running; outputs were not manifested", file=sys.stderr)
        return 1

    expected = expected_outputs()
    missing = expected - current_outputs()
    if missing:
        for path in sorted(missing):
            print(f"missing output after bake: {path.relative_to(ROOT).as_posix()}", file=sys.stderr)
        return 1

    sources = after
    outputs = {path.relative_to(ROOT).as_posix(): digest(path) for path in sorted(expected)}
    write_json(MANIFEST, {
        "schema": 2,
        "source_signature": aggregate(sources),
        "regions": signatures,
        "sources": sources,
        "outputs": outputs,
    })
    print(f"wrote {len(outputs)} world artifacts and {MANIFEST.relative_to(ROOT)}")
    return check()


def _region_id(name: str) -> str:
    return name if name.startswith("region.") else f"region.{name}"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--bake", action="store_true")
    mode.add_argument("--check", action="store_true")
    parser.add_argument("--full", action="store_true", help="with --bake: prepare every region")
    parser.add_argument("--region", action="append", default=[],
                        help="with --bake: also prepare this region (id or slug); repeatable")
    args = parser.parse_args()
    return bake(args.full, tuple(args.region)) if args.bake else check()


if __name__ == "__main__":
    raise SystemExit(main())
