#!/usr/bin/env python3
"""Build and verify Embervale's committed production-world package.

    python tools/world_bake.py --bake   # regenerate prepared regions/cells and manifest
    python tools/world_bake.py --check  # CI: name every stale/missing/unexpected artifact

The source fingerprint is the authority. It covers world specifications, generated region inputs,
cell scenes and the code that turns them into terrain/scatter/navigation. Output hashes make partial
or hand-edited bakes fail even when their manifest was not updated.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
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
    "assets/**/*.import",
    "assets/**/*.tres",
    "assets/shaders/world/*",
)


def files_for(patterns: tuple[str, ...]) -> list[Path]:
    return sorted({path for pattern in patterns for path in ROOT.glob(pattern) if path.is_file()},
                  key=lambda path: path.relative_to(ROOT).as_posix())


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_hashes() -> dict[str, str]:
    return {path.relative_to(ROOT).as_posix(): digest(path) for path in files_for(SOURCE_GLOBS)}


def aggregate(hashes: dict[str, str]) -> str:
    payload = "".join(f"{name}\0{value}\n" for name, value in sorted(hashes.items()))
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def expected_outputs() -> set[Path]:
    expected: set[Path] = set()
    for path in sorted((ROOT / "data" / "regions").glob("*.tres")):
        text = path.read_text(encoding="utf-8")
        root = text.split("[resource]", 1)[-1]
        match = re.search(r'^Id = "([^"]+)"', root, re.MULTILINE)
        if match is None:
            continue
        region_id = match.group(1)
        region_slug = region_id.replace(".", "_").replace(":", "_")
        expected.add(BAKE_ROOT / "regions" / f"{region_slug}.res")
        cell_prefix = region_id.removeprefix("region.") + "."
        for cell_id in re.findall(r'^Id = "([a-z0-9_]+\.[a-z0-9_]+)"', text, re.MULTILINE):
            if not cell_id.startswith(cell_prefix):
                continue
            cell_slug = cell_id.replace(".", "_").replace(":", "_")
            expected.add(BAKE_ROOT / "cells" / region_slug / f"{cell_slug}.scn")
    return expected


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


def bake() -> int:
    before = source_hashes()
    signature = aggregate(before)
    generation = subprocess.run(
        [sys.executable, "tools/gen_regions.py", "--check"], cwd=ROOT, check=False)
    if generation.returncode != 0:
        print("region resources are stale; run python tools/gen_regions.py before baking", file=sys.stderr)
        return generation.returncode

    build = run_process(
        ["dotnet", "build", "-p:EmbervaleTooling=false"], timeout=600, cwd=ROOT)
    if build.returncode != 0:
        print(build.output, file=sys.stderr)
        return build.returncode

    engine = require_godot()
    result = run_process(
        [str(engine), "--headless", "--path", str(ROOT), "--", "--world-bake",
         f"--world-bake-signature={signature}"],
        timeout=1800, cwd=ROOT)
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
        "schema": 1,
        "source_signature": aggregate(sources),
        "sources": sources,
        "outputs": outputs,
    })
    print(f"wrote {len(outputs)} world artifacts and {MANIFEST.relative_to(ROOT)}")
    return check()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--bake", action="store_true")
    mode.add_argument("--check", action="store_true")
    args = parser.parse_args()
    return bake() if args.bake else check()


if __name__ == "__main__":
    raise SystemExit(main())
