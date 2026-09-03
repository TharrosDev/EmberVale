#!/usr/bin/env python3
"""Self-check for the rig-family classifier in tools/assets.py.

    python tools/test_assets.py

The classifier is the one piece of real logic in assets.py - everything else orchestrates a script
that has its own checks. If it mislabels a body, the retarget gate silently stops covering that
body, which is exactly the failure the gate exists to catch. So it gets a test, and the test runs
against the real production assets rather than fixtures: a fixture cannot notice that someone
adopted a humanoid without a bone map.
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import audit_3d
from assets import (ARCHITECTURE, HUMANOID, QUADRUPED, RETARGET_SKELETON, STATIC_PROP, VIEWMODEL,
                    build_manifest, classify, drift, load_manifest)

# One known asset per family. These are load-bearing examples, not a roster: chr_player_base is the
# retargeted body the shared library attaches to, enm_dire_wolf is the quadruped that must NOT be
# forced through it, and fp_arm_right is the viewmodel that is neither.
EXPECTED = {
    "assets/models/characters/chr_player_base.glb": HUMANOID,
    "assets/models/characters/npc_townsman.glb": HUMANOID,
    "assets/models/creatures/enm_dire_wolf.glb": QUADRUPED,
    "assets/models/creatures/mnt_horse.glb": QUADRUPED,
    "assets/models/characters/fp_arm_right.glb": VIEWMODEL,
    "assets/models/characters/fp_arm_left.glb": VIEWMODEL,
}


def test_known_assets() -> None:
    for relative, expected in EXPECTED.items():
        path = audit_3d.ROOT / relative
        assert path.is_file(), f"{relative} is gone; update this test or restore the asset"
        document, _ = audit_3d.read_gltf(path)
        actual = classify(path, document, audit_3d.parse_import(path))["type"]
        assert actual == expected, f"{relative}: expected {expected}, classified {actual}"


def test_families_are_coherent() -> None:
    """Every family's invariant, checked across the whole production set."""
    assets = build_manifest()["assets"]
    by_type: dict[str, list[dict]] = {}
    for asset in assets:
        by_type.setdefault(asset["type"], []).append(asset)

    assert by_type.get(HUMANOID), "no humanoids found at all - the classifier is broken"
    for asset in by_type[HUMANOID]:
        assert asset["bone_map"], f"{asset['id']}: HUMANOID with no bone map"
        assert asset["anim"] == "shared_library", asset["id"]
    # The whole point of separating the families: a quadruped must never carry a humanoid bone map,
    # because that is what would drag it into the shared humanoid animation library.
    for asset in by_type.get(QUADRUPED, []):
        assert not asset["bone_map"], f"{asset['id']}: QUADRUPED with a humanoid bone map"
        assert asset["anim"] == "own_clips", asset["id"]
    for asset in by_type.get(VIEWMODEL, []):
        assert asset["id"].startswith("fp_"), asset["id"]
        assert asset["anim"] == "procedural", asset["id"]
    for asset in by_type.get(ARCHITECTURE, []) + by_type.get(STATIC_PROP, []):
        assert asset["rig"] == "none", f"{asset['id']}: static asset carrying a rig"
    assert "UNREADABLE" not in by_type, \
        f"unparseable models: {[a['id'] for a in by_type['UNREADABLE']]}"


def test_bone_maps_exist() -> None:
    """A bone map named by an .import must be a file, or the retarget silently does not apply."""
    for asset in build_manifest()["assets"]:
        if asset["bone_map"]:
            resource = audit_3d.ROOT / "assets/models/animations" / f"{asset['bone_map']}.tres"
            assert resource.is_file(), f"{asset['id']} names a missing bone map {asset['bone_map']}"


def test_manifest_is_current() -> None:
    problems = drift(build_manifest(), load_manifest())
    assert not problems, ("assets/models/manifest.json is stale; run "
                          f"`python tools/assets.py status --write`:\n  " + "\n  ".join(problems[:5]))


def main() -> int:
    tests = [value for name, value in sorted(globals().items()) if name.startswith("test_")]
    failed = 0
    for test in tests:
        try:
            test()
            print(f"  PASS  {test.__name__}")
        except AssertionError as error:
            print(f"  FAIL  {test.__name__}: {error}")
            failed += 1
    print(f"{len(tests) - failed}/{len(tests)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
