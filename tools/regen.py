#!/usr/bin/env python3
"""One ordered entry point for every generator: is anything generated out of date, and fix it.

    python tools/regen.py --check                 run every generator's check, in dependency order
    python tools/regen.py --check --only gen_items gen_recipes
    python tools/regen.py --fix                   regenerate what drifted, then prove a fixed point
    python tools/regen.py --stale                 hash only: whose inputs or outputs changed
    python tools/regen.py --list                  the registry, in order

Output is failures only, then one line `REGEN {json}`. --json prints the full result object
instead; -v prints every entry. Each generator's full output is in artifacts/regen/<name>.log.
Exit 0 clean, 1 when a generator would change a file or a check fails, 2 when a generator crashed,
3 when another regen holds the lock.

This file owns no generation rule. Each entry runs the generator's own entry point; a generator
with no --check (gen_appearance, gen_player_mask) is checked by running it, comparing the files it
owns and putting the old bytes back. Entries run one at a time because six of them rewrite blocks
of data/locale/strings.csv.

A passing entry is remembered in artifacts/regen/cache.json by a hash of everything it reads and
writes plus its command, and reported `cached` until one of those changes. CI and any run that
must not trust the cache pass --no-cache.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import shutil
import sys
import time
from dataclasses import dataclass
from pathlib import Path

from quality_common import ROOT, run_process, write_json

STATE = ROOT / "artifacts" / "regen"
LOCK_STALE_SECONDS = 900
#: files larger than this are identified by size and mtime instead of content.
BIG_FILE = 1_000_000

LOCALE = "data/locale/strings.csv"
DATA = ("data/**/*.tres", LOCALE)
ITEM_TOOLS = ("tools/items/*.py",)
REGION_SCENES = ("scenes/regions/**/*.tscn", "data/regions/*.tres")


@dataclass(frozen=True)
class Entry:
    name: str
    what: str
    #: arguments after the python executable; None when the entry cannot check or cannot write.
    check: tuple[str, ...] | None
    write: tuple[str, ...] | None
    #: everything the generator reads or writes; a change to any of it invalidates the cache.
    watch: tuple[str, ...]
    #: files the writer owns. Required when `check` is None: they are snapshotted and restored.
    owns: tuple[str, ...] = ()
    #: importable modules / executables it needs; missing ones make the entry `skipped`.
    needs: tuple[str, ...] = ()
    timeout: int = 300


def generator(name: str, what: str, script: str, watch: tuple[str, ...], **extra) -> Entry:
    return Entry(name, what, (script, "--check"), (script,), (script, *watch), **extra)


#: Dependency order. gen_recipes prices outputs from the item files gen_items writes and gen_loot
#: reads both, so the catalogue chain must stay in this order.
REGISTRY: tuple[Entry, ...] = (
    Entry("catalogue", "the item catalogue validates", ("tools/items/catalogue.py",), None,
          ITEM_TOOLS),
    generator("gen_items", "items, weapons, sets, unique effects, shop stock",
              "tools/gen_items.py", (*DATA, *ITEM_TOOLS, "src/**/ItemValidator.Content.cs")),
    generator("gen_recipes", "recipes, recipe names, trainer list, commission safety",
              "tools/gen_recipes.py", (*DATA, *ITEM_TOOLS, "src/Core/GameIds*.cs", "src/Economy/*.cs")),
    generator("gen_loot", "affixes, tier pools, boss chests, family tier rows",
              "tools/items/gen_loot.py", (*DATA, *ITEM_TOOLS)),
    generator("gen_perks", "perk resources and their locale block", "tools/gen_perks.py", DATA),
    Entry("gen_appearance", "appearance options, race allow-lists, locale block",
          None, ("tools/gen_appearance.py",), ("tools/gen_appearance.py", *DATA),
          owns=("data/appearance/*.tres", "data/races/*.tres", LOCALE)),
    generator("gen_campaign", "main-story quests, dialogue, locale blocks, flag graph",
              "tools/gen_campaign.py",
              (*DATA, "tools/campaign/**/*.py", "data/story/*.json"), timeout=600),
    Entry("campaign_selftest", "the campaign generator's own tests",
          ("tools/campaign/selftest.py",), None,
          (*DATA, "tools/gen_campaign.py", "tools/campaign/**/*.py", "data/story/*.json"), timeout=600),
    generator("gen_map_locations", "map locations, locale block, scene markers",
              "tools/gen_map_locations.py",
              (*DATA, "tools/map_locations_*.py", "scenes/regions/**/*.tscn")),
    generator("gen_regions", "region resources from their specs", "tools/gen_regions.py",
              ("tools/region_spec_*.py", "data/regions/*.tres")),
    generator("compose_district", "street plans composed into their cells",
              "tools/compose_district.py", ("tools/district_layouts.py", *REGION_SCENES)),
    generator("new_cell_scenes", "every region cell has a scene", "tools/new_cell_scenes.py",
              REGION_SCENES),
    generator("shift_cell_content", "settlement content stands at its content origin",
              "tools/shift_cell_content.py", ("tools/region_spec_*.py", *REGION_SCENES)),
    generator("gen_ground_cover", "procedural grass and fern meshes", "tools/gen_ground_cover.py",
              ("assets/models/world/**/*.glb",)),
    generator("gen_spell_sfx", "synthesised spell cues", "tools/gen_spell_sfx.py",
              ("assets/audio/sfx/spell/*",), needs=("numpy", "ffmpeg")),
    Entry("gen_player_mask", "player body region mask",
          None, ("tools/gen_player_mask.py",),
          ("tools/gen_player_mask.py", "assets/models/characters/chr_player_base*"),
          owns=("assets/models/characters/chr_player_base_mask.png",), needs=("PIL",)),
    Entry("check_hit_zones", "enemy hit zones cover their meshes",
          ("tools/check_hit_zones.py", "--check"), None,
          ("tools/check_hit_zones.py", "data/enemies/*.tres", "assets/models/creatures/*.glb")),
    Entry("world_atlas", "atlas bands, hooks, secrecy, doc table",
          ("tools/world_atlas.py", "--check"), None,
          ("tools/world_atlas.py", "tools/region_spec_*.py", LOCALE, "data/map_locations/*.tres",
           "docs/WORLD_ATLAS.md")),
)


# ------------------------------------------------------------------------------------- hashing

_GLOBS: dict[tuple, list[Path]] = {}


def files_for(root: Path, patterns: tuple[str, ...]) -> list[Path]:
    """Files matching the patterns. Walking data/ on Windows costs most of a second and eight
    entries share it, so one walk per pattern is kept until a generator runs (run_check and run_write forget it)."""
    found: set[Path] = set()
    for pattern in patterns:
        key = (root, pattern)
        if key not in _GLOBS:
            _GLOBS[key] = [path for path in root.glob(pattern) if path.is_file()]
        found.update(_GLOBS[key])
    return sorted(found)


_TOKENS: dict[tuple, str] = {}


def file_token(path: Path) -> str:
    stat = path.stat()
    if stat.st_size > BIG_FILE:
        return f"big:{stat.st_size}:{stat.st_mtime_ns}"
    key = (path, stat.st_size, stat.st_mtime_ns)
    if key not in _TOKENS:
        # Line endings differ by checkout; the same content must hash the same on every machine.
        _TOKENS[key] = hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
    return _TOKENS[key]


def tree_hashes(root: Path, patterns: tuple[str, ...]) -> dict[str, str]:
    return {path.relative_to(root).as_posix(): file_token(path) for path in files_for(root, patterns)}


def signature(root: Path, entry: Entry) -> str:
    digest = hashlib.sha256(repr((entry.check, entry.write)).encode("utf-8"))
    for name, token in tree_hashes(root, entry.watch).items():
        digest.update(f"{name}\0{token}\n".encode("utf-8"))
    return digest.hexdigest()


def missing_needs(entry: Entry) -> list[str]:
    missing = []
    for need in entry.needs:
        present = shutil.which(need) is not None if need == "ffmpeg" else importlib.util.find_spec(need) is not None
        if not present:
            missing.append(need)
    return missing


# ------------------------------------------------------------------------------------- running

CRASH_MARKERS = ("Traceback (most recent call last)", "SyntaxError:", "ModuleNotFoundError:")
DRIFT_WORDS = ("DRIFT", "WOULD CHANGE", "differ", "out of date", "stale", "FAIL", "---", "error", "Error")


def classify(returncode: int, output: str, timed_out: bool, checks_only: bool) -> str:
    """ok | DRIFT (a writer would change a file) | FAIL (a pure check failed) | ERROR (crashed)."""
    if timed_out or returncode in (124, 127) or any(marker in output for marker in CRASH_MARKERS):
        return "ERROR"
    if returncode == 0:
        return "ok"
    # Every writer here exits 1 for "a file would change"; any other code is a rule it enforces
    # (gen_regions exits 2 on a lattice error), which regenerating cannot fix.
    return "DRIFT" if returncode == 1 and not checks_only else "FAIL"


def first_reason(output: str) -> str:
    lines = [line.strip() for line in output.splitlines() if line.strip()]
    for line in lines:
        if any(word in line for word in DRIFT_WORDS) and not line.lower().startswith("warning"):
            return line[:200]
    return lines[-1][:200] if lines else ""


def execute(root: Path, arguments: tuple[str, ...], timeout: int):
    return run_process([sys.executable, *arguments], timeout=timeout, cwd=root)


def snapshot(root: Path, patterns: tuple[str, ...]) -> dict[Path, bytes]:
    return {path: path.read_bytes() for path in files_for(root, patterns)}


def restore(root: Path, patterns: tuple[str, ...], before: dict[Path, bytes]) -> None:
    for path in files_for(root, patterns):
        if path not in before:
            path.unlink()
    for path, payload in before.items():
        if not path.is_file() or path.read_bytes() != payload:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(payload)


def run_check(root: Path, entry: Entry, run=execute) -> dict:
    """Run one entry's check. A writer with no --check runs for real and is then put back."""
    started = time.monotonic()
    if entry.check is not None:
        result = run(root, entry.check, entry.timeout)
        _GLOBS.clear()
        status = classify(result.returncode, result.output, result.timed_out, entry.write is None)
        changed: list[str] = []
        output = result.output
    else:
        before = snapshot(root, entry.owns)
        try:
            result = run(root, entry.write, entry.timeout)
            _GLOBS.clear()
            after = snapshot(root, entry.owns)
        finally:
            _GLOBS.clear()
            restore(root, entry.owns, before)
            _GLOBS.clear()
        changed = sorted(path.relative_to(root).as_posix() for path in set(before) | set(after)
                         if before.get(path) != after.get(path))
        output = result.output
        if result.returncode != 0 or result.timed_out:
            status = "ERROR"
        else:
            status = "DRIFT" if changed else "ok"
            if changed:
                output += "\nWOULD CHANGE " + ", ".join(changed[:5])
    return {"name": entry.name, "status": status, "seconds": round(time.monotonic() - started, 1),
            "reason": "" if status == "ok" else first_reason(output), "changed": changed,
            "output": output}


def run_write(root: Path, entry: Entry, run=execute) -> dict:
    started = time.monotonic()
    before = tree_hashes(root, entry.watch)
    result = run(root, entry.write, entry.timeout)
    _GLOBS.clear()
    # The memo is keyed by size and mtime; a same-size rewrite inside one clock tick would read as
    # unchanged, so nothing hashed before a writer ran is trusted after it.
    _TOKENS.clear()
    after = tree_hashes(root, entry.watch)
    changed = sorted(name for name in set(before) | set(after) if before.get(name) != after.get(name))
    crashed = result.timed_out or any(marker in result.output for marker in CRASH_MARKERS)
    status = "ERROR" if crashed or (result.returncode != 0 and not changed) else "fixed"
    return {"name": entry.name, "status": status, "seconds": round(time.monotonic() - started, 1),
            "reason": first_reason(result.output) if status == "ERROR" else "",
            "changed": changed, "output": result.output}


class Lock:
    """A directory lock: mkdir is atomic, and two lanes regenerating at once lose locale blocks."""

    def __init__(self, path: Path):
        self.path = path
        self.held = False

    def acquire(self) -> bool:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        try:
            self.path.mkdir()
        except FileExistsError:
            try:
                age = time.time() - self.path.stat().st_mtime
            except OSError:
                age = 0
            if age < LOCK_STALE_SECONDS:
                return False
            # A crashed run left it behind.
        self.held = True
        return True

    def release(self) -> None:
        if self.held:
            try:
                self.path.rmdir()
            except OSError:
                pass
            self.held = False


def load_cache(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def select(names: list[str] | None, registry: tuple[Entry, ...] = REGISTRY) -> list[Entry]:
    if not names:
        return list(registry)
    known = {entry.name for entry in registry}
    unknown = [name for name in names if name not in known]
    if unknown:
        # Exit 2, not 1: 1 means drift, and a typo in --only must not read as drift.
        print(f"regen: unknown generator(s): {', '.join(unknown)}; known: {', '.join(sorted(known))}", file=sys.stderr)
        raise SystemExit(2)
    return [entry for entry in registry if entry.name in names]


def regen(mode: str, entries: list[Entry], root: Path = ROOT, state: Path = STATE,
          use_cache: bool = True, run=execute) -> dict:
    """mode: check | fix | stale. Returns the result object printed as REGEN {json}."""
    started = time.monotonic()
    cache_path = state / "cache.json"
    cache = load_cache(cache_path) if use_cache else {}
    rows: list[dict] = []

    def log(row: dict) -> None:
        output = row.pop("output", None)
        if output is not None:
            state.mkdir(parents=True, exist_ok=True)
            (state / f"{row['name']}.log").write_text(output, encoding="utf-8")
        rows.append(row)

    def check_one(entry: Entry, trust_cache: bool) -> dict:
        missing = missing_needs(entry)
        if missing:
            return {"name": entry.name, "status": "skipped", "seconds": 0.0,
                    "reason": "missing " + ", ".join(missing), "changed": []}
        fingerprint = signature(root, entry)
        if trust_cache and cache.get(entry.name) == fingerprint:
            return {"name": entry.name, "status": "cached", "seconds": 0.0, "reason": "", "changed": []}
        if mode == "stale":
            return {"name": entry.name, "status": "stale", "seconds": 0.0,
                    "reason": "inputs or outputs changed since the last passing check", "changed": []}
        row = run_check(root, entry, run)
        if row["status"] == "ok":
            cache[entry.name] = signature(root, entry)
        else:
            cache.pop(entry.name, None)
        return row

    for entry in entries:
        row = check_one(entry, use_cache)
        if mode == "fix" and row["status"] == "DRIFT" and entry.write is not None:
            log({**row, "name": entry.name + ".check"})
            rows.pop()
            row = run_write(root, entry, run)
        log(row)

    not_idempotent: list[str] = []
    if mode == "fix":
        # A second, uncached pass over everything proves the tree is at a fixed point: a writer
        # that keeps changing its own output, or one that disturbed an earlier generator, shows here.
        for entry in entries:
            if missing_needs(entry):
                continue
            again = run_check(root, entry, run)
            again.pop("output", None)
            if again["status"] == "ok":
                cache[entry.name] = signature(root, entry)
            else:
                cache.pop(entry.name, None)
                not_idempotent.append(entry.name)
                for row in rows:
                    if row["name"] == entry.name:
                        row["status"] = "NOT-IDEMPOTENT" if row["status"] == "fixed" else again["status"]
                        row["reason"] = again["reason"]

    if use_cache and mode != "stale":
        write_json(cache_path, cache)

    def named(*statuses: str) -> list[str]:
        return [row["name"] for row in rows if row["status"] in statuses]

    errors = named("ERROR")
    drift = named("DRIFT", "NOT-IDEMPOTENT", "stale")
    failed = named("FAIL")
    return {
        "ok": not (errors or drift or failed),
        "mode": mode,
        "exit_code": 2 if errors else 1 if (drift or failed) else 0,
        "entries": rows,
        "drift": drift, "failed": failed, "errors": errors,
        "fixed": named("fixed"), "skipped": named("skipped"), "cached": len(named("cached")),
        "not_idempotent": not_idempotent,
        "seconds": round(time.monotonic() - started, 1),
        "logs": state.relative_to(root).as_posix() if state.is_relative_to(root) else str(state),
    }


def report(result: dict, as_json: bool, verbose: bool) -> None:
    if as_json:
        print(json.dumps(result))
        return
    for row in result["entries"]:
        if not verbose and row["status"] in ("ok", "cached"):
            continue
        changed = f"  {len(row['changed'])} file(s): {', '.join(row['changed'][:3])}" if row["changed"] else ""
        reason = f"  {row['reason']}" if row["reason"] else ""
        print(f"{row['name']:<20} {row['status']:<8} {row['seconds']:>5.1f}s{changed}{reason}")
    compact = {key: value for key, value in result.items() if key != "entries"}
    print("REGEN " + json.dumps(compact))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check", action="store_true")
    mode.add_argument("--fix", action="store_true")
    mode.add_argument("--stale", action="store_true")
    mode.add_argument("--list", action="store_true")
    parser.add_argument("--only", nargs="+", metavar="NAME")
    parser.add_argument("--no-cache", action="store_true", help="run every check even if nothing changed")
    parser.add_argument("--json", action="store_true")
    parser.add_argument("-v", "--verbose", action="store_true", help="print passing entries too")
    args = parser.parse_args(argv)
    entries = select(args.only)
    if args.list:
        for entry in entries:
            modes = "/".join(word for word, present in (("check", entry.check), ("write", entry.write)) if present)
            print(f"{entry.name:<20} {modes:<11} {entry.what}")
        return 0
    chosen = "check" if args.check else "fix" if args.fix else "stale"
    lock = Lock(STATE / ".lock")
    if chosen != "stale" and not lock.acquire():
        print("REGEN " + json.dumps({"ok": False, "exit_code": 3,
                                     "error": f"another regen holds {lock.path.relative_to(ROOT).as_posix()}"}))
        return 3
    try:
        result = regen(chosen, entries, use_cache=not args.no_cache)
    finally:
        lock.release()
    report(result, args.json, args.verbose)
    return result["exit_code"]


if __name__ == "__main__":
    raise SystemExit(main())
