#!/usr/bin/env python3
"""Build and verify Embervale's committed production-world package.

    python tools/world_bake.py --plan                 # what would bake and why; no engine, no build
    python tools/world_bake.py --bake                 # rebake only the regions whose inputs changed
    python tools/world_bake.py --bake --full          # rebake every region
    python tools/world_bake.py --bake --region ember_crown   # also force the named region(s)
    python tools/world_bake.py --bake --resume        # after a timeout or a kill: keep finished regions
    python tools/world_bake.py --status               # n/total, current cell, ETA of the running bake
    python tools/world_bake.py --check                # CI: name every stale/missing/unexpected artifact

A full bake outlives a ten-minute foreground command. Start `--bake` in the background and poll
`--status` (exit 3 running, 0 done, 1 failed/timeout/interrupted, 2 nothing recorded), or run
`--bake --max-minutes 9` and then `--bake --resume`. Progress is read from the output files as the
engine saves them, into artifacts/world_bake/status.json; the engine log is written to
artifacts/world_bake/bake.log and only its error lines are printed. The last line of --bake and
--plan is `WORLD_BAKE {json}`.

A region's signature covers the shared inputs (world code, biomes, models, shaders) plus its own
region resource and cell scenes, so editing one realm rebakes one realm. A change to a shared input
still rebakes them all: nothing here can prove which cell uses which model. Resume works at the
same granularity: a region is kept only when the interrupted run finished it and its inputs and
output bytes are unchanged since.

The source fingerprint is the authority. It covers world specifications, generated region inputs,
cell scenes and the code that turns them into terrain/scatter/navigation. Output hashes make partial
or hand-edited bakes fail even when their manifest was not updated.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import threading
import time
from pathlib import Path

from quality_common import ROOT, require_godot, run_process, write_json

BAKE_ROOT = ROOT / "data" / "world_bake"
MANIFEST = BAKE_ROOT / "manifest.json"

STATE = ROOT / "artifacts" / "world_bake"
STATUS = STATE / "status.json"
JOURNAL = STATE / "journal.json"
BAKE_LOG = STATE / "bake.log"

#: Measured 2026-10-07: a full six-region bake of 151 artifacts takes 6-8 minutes. Used for the
#: ETA until a bake on this checkout has recorded its own rate in status.json.
DEFAULT_SECONDS_PER_ARTIFACT = 2.8
#: A status file still saying "running" this long after its last refresh belongs to a dead bake.
HEARTBEAT_SECONDS = 30
PREPARE_SECONDS = 900
LINE_CAP = 10

SOURCE_GLOBS = (
    "tools/gen_regions.py",
    "tools/region_spec_*.py",
    "data/regions/*.tres",
    "data/world_gen/*.tres",
    "data/biomes/**/*.tres",
    "data/world/**/*.tres",
    "scenes/regions/**/*.tscn",
    # Wrapper scenes a cell instances (shrine_waystone, lm_*): their colliders feed the cell navmesh.
    "scenes/props/*.tscn",
    "src/World/*.cs",
    "src/Bootstrap/HeadlessWorldBake.cs",
    "src/Combat/CombatLayers.cs",
    # Models and world shaders only. UI art, fonts and audio never reach a prepared cell, and
    # hashing their import sidecars made every new icon a thirty-minute rebake.
    "assets/models/**/*.import",
    # The model bytes themselves. A sidecar does not change when a .glb is replaced in place, so
    # without these a new mesh under an old name left every prepared cell that scatters it stale.
    "assets/models/**/*.glb",
    "assets/models/**/*.gltf",
    "assets/models/**/*.bin",
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
    if path.suffix in {".cs", ".py", ".gd", ".tres", ".tscn", ".import", ".gdshader", ".gdshaderinc"}:
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


def region_inputs(sources: dict[str, str]) -> tuple[dict[str, str], dict[str, dict[str, str]]]:
    """(inputs every region shares, each region's own inputs).

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
    return shared, own


def region_signatures(sources: dict[str, str]) -> dict[str, str]:
    """One signature per region: the shared inputs plus that region's own resource and scenes."""
    shared, own = region_inputs(sources)
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


def read_json(path: Path) -> dict:
    try:
        loaded = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    return loaded if isinstance(loaded, dict) else {}


def write_quietly(path: Path, payload: dict) -> None:
    try:
        write_json(path, payload)
    except OSError:
        pass  # a reader holding the file open on Windows; the next tick writes again


def capped(lines: list[str], limit: int = LINE_CAP) -> list[str]:
    """At most `limit` lines per (kind, directory) group, each group ending with what was left out.

    A model pass changes hundreds of sources; listing each one buries the answer."""
    groups: dict[tuple[str, str], list[str]] = {}
    for line in lines:
        kind, _, name = line.partition(": ")
        groups.setdefault((kind, name.rsplit("/", 1)[0] if "/" in name else ""), []).append(line)
    out: list[str] = []
    for (kind, directory), members in groups.items():
        out.extend(members[:limit])
        if len(members) > limit:
            out.append(f"{kind}: ... {len(members) - limit} more under {directory or '.'}")
    return out


def some(names: list[str], limit: int = 3) -> str:
    return ", ".join(names[:limit]) + (f" (+{len(names) - limit} more)" if len(names) > limit else "")


# ------------------------------------------------------------------------------------- resume

def resumable(journal: dict, signatures: dict[str, str]) -> set[str]:
    """Regions an interrupted bake finished: same inputs now as then, and every output it wrote is
    still the byte-for-byte file it wrote."""
    trusted: set[str] = set()
    for region, entry in (journal.get("regions") or {}).items():
        if signatures.get(region) != entry.get("signature"):
            continue
        outputs: dict[str, str] = entry.get("outputs") or {}
        if outputs and all((ROOT / name).is_file() and digest(ROOT / name) == value
                           for name, value in outputs.items()):
            trusted.add(region)
    return trusted


def journal_region(journal: dict, region: str, signature: str, outputs: set[Path]) -> None:
    journal.setdefault("regions", {})[region] = {
        "signature": signature,
        "outputs": {path.relative_to(ROOT).as_posix(): digest(path) for path in sorted(outputs)},
    }


# ------------------------------------------------------------------------------------- plan

def plan(manifest: dict, sources: dict[str, str], journal: dict | None = None,
         rate: float = DEFAULT_SECONDS_PER_ARTIFACT) -> dict:
    """What a bake would do and why, without starting one."""
    shared, own = region_inputs(sources)
    signatures = {region: aggregate({**shared, **mine}) for region, mine in own.items()}
    recorded_sources: dict[str, str] = manifest.get("sources", {})
    recorded_regions: dict[str, str] = manifest.get("regions", {})
    recorded_outputs: dict[str, str] = manifest.get("outputs", {})

    def changed(names) -> list[str]:
        return sorted(name for name in names if recorded_sources.get(name) != sources.get(name))

    removed = sorted(name for name in recorded_sources if name not in sources
                     and not re.fullmatch(r"tools/region_spec_[^/]*\.py", name))
    trusted = resumable(journal or {}, signatures)
    regions = []
    for region, (_, outputs) in sorted(region_outputs().items()):
        names = sorted(output.relative_to(ROOT).as_posix() for output in outputs)
        missing = [name for name in names if not (ROOT / name).is_file()]
        modified = [name for name in names if name not in missing
                    and recorded_outputs.get(name) != digest(ROOT / name)]
        stale = recorded_regions.get(region) != signatures[region] or bool(missing or modified)
        state = "current" if not stale else "resumable" if region in trusted else "stale"
        regions.append({
            "region": region,
            "state": state,
            "artifacts": len(outputs),
            "cells": len(outputs) - 1,
            "eta_seconds": round(len(outputs) * rate) if state == "stale" else 0,
            "own_changed": changed(own[region]) if state == "stale" else [],
            "missing_outputs": len(missing),
            "modified_outputs": len(modified),
        })
    to_bake = [row for row in regions if row["state"] == "stale"]
    return {
        "regions": regions,
        "shared_changed": changed(shared),
        "removed_sources": removed,
        "obsolete_outputs": sorted(p.relative_to(ROOT).as_posix() for p in current_outputs() - expected_outputs()),
        "to_bake": [row["region"] for row in to_bake],
        "resumable": [row["region"] for row in regions if row["state"] == "resumable"],
        "artifacts": sum(row["artifacts"] for row in to_bake),
        "eta_seconds": sum(row["eta_seconds"] for row in to_bake),
        "seconds_per_artifact": round(rate, 2),
    }


def print_plan(result: dict, as_json: bool) -> None:
    if as_json:
        print(json.dumps(result))
        return
    if result["shared_changed"]:
        print(f"shared inputs changed ({len(result['shared_changed'])}), every region rebakes: "
              f"{some(result['shared_changed'])}")
    if result["removed_sources"]:
        print(f"inputs removed ({len(result['removed_sources'])}): {some(result['removed_sources'])}")
    for row in result["regions"]:
        if row["state"] == "current":
            continue
        why = []
        if row["own_changed"]:
            why.append(f"own inputs {len(row['own_changed'])}: {some(row['own_changed'])}")
        if row["missing_outputs"]:
            why.append(f"{row['missing_outputs']} output(s) missing")
        if row["modified_outputs"]:
            why.append(f"{row['modified_outputs']} output(s) not as manifested")
        if row["state"] == "resumable":
            why.append("finished by the interrupted run; --resume keeps it")
        print(f"{row['region']:<26} {row['state']:<9} {row['cells']:>3} cells  ~{row['eta_seconds']}s"
              + ("  " + "; ".join(why) if why else ""))
    if result["obsolete_outputs"]:
        print(f"obsolete outputs to delete ({len(result['obsolete_outputs'])}): {some(result['obsolete_outputs'])}")
    print("WORLD_BAKE " + json.dumps({"plan": True, **{key: result[key] for key in (
        "to_bake", "resumable", "artifacts", "eta_seconds", "seconds_per_artifact")}}))


def recorded_rate() -> float:
    rate = read_json(STATUS).get("seconds_per_artifact")
    return float(rate) if isinstance(rate, (int, float)) and rate > 0 else DEFAULT_SECONDS_PER_ARTIFACT


def cmd_plan(as_json: bool, resume: bool) -> int:
    print_plan(plan(read_json(MANIFEST), source_hashes(), read_json(JOURNAL) if resume else None,
                    recorded_rate()), as_json)
    return 0


# ------------------------------------------------------------------------------------- progress

def progress(expected: dict[str, set[Path]], started: float, now: float,
             fallback_rate: float = DEFAULT_SECONDS_PER_ARTIFACT) -> dict:
    """n/total, the artifact written last, and an ETA, from output files newer than the bake.

    The engine saves each cell as it finishes and each region's resource after its last cell, so
    counting files written since `started` needs no cooperation from the engine."""
    total = sum(len(outputs) for outputs in expected.values())
    done = 0
    latest: tuple[float, str, Path] | None = None
    regions_done: list[str] = []
    for region, outputs in expected.items():
        for path in outputs:
            try:
                modified = path.stat().st_mtime
            except OSError:
                continue
            if modified < started - 1:
                continue
            done += 1
            if latest is None or modified > latest[0]:
                latest = (modified, region, path)
            if path.parent.name == "regions":
                regions_done.append(region)
    elapsed = max(0.0, now - started)
    rate = elapsed / done if done else fallback_rate
    return {
        "done": done,
        "total": total,
        "region": latest[1] if latest else "",
        "artifact": latest[2].stem if latest else "",
        "regions_done": sorted(regions_done),
        "elapsed_seconds": round(elapsed),
        "eta_seconds": round(rate * (total - done)),
        "seconds_per_artifact": round(rate, 2),
    }


class Watcher(threading.Thread):
    """Refreshes status.json while the engine runs and journals each region as it completes."""

    def __init__(self, expected: dict[str, set[Path]], signatures: dict[str, str], started: float,
                 base: dict, rate: float, journal: dict, interval: float = 2.0):
        super().__init__(daemon=True)
        self.expected, self.signatures, self.started = expected, signatures, started
        self.base, self.rate, self.journal, self.interval = base, rate, journal, interval
        self.stop = threading.Event()
        self._settling: dict[str, tuple] = {}

    def tick(self) -> dict:
        snapshot = progress(self.expected, self.started, time.time(), self.rate)
        for region in snapshot["regions_done"]:
            if region in self.journal["regions"]:
                continue
            # The region resource is written last. Journal the region once its files have stopped
            # changing between two ticks, so a half-written file is never hashed as the finished one.
            try:
                state = tuple((p.stat().st_size, p.stat().st_mtime_ns) for p in sorted(self.expected[region]))
            except OSError:
                continue
            if self._settling.get(region) == state:
                journal_region(self.journal, region, self.signatures[region], self.expected[region])
                write_quietly(JOURNAL, self.journal)
            else:
                self._settling[region] = state
        write_quietly(STATUS, {**self.base, **snapshot, "updated": time.time()})
        return snapshot

    def run(self) -> None:
        while not self.stop.wait(self.interval):
            self.tick()


def status_line(status: dict, now: float) -> tuple[str, str, int]:
    """(one line for a poller, the effective state, exit code: 0 done, 3 running, 2 none, else 1)."""
    if not status:
        return "bake: no status recorded (no bake has run from this checkout)", "none", 2
    state = status.get("state", "unknown")
    # "preparing" covers the generation check and the dotnet build, which refresh nothing.
    allowed = HEARTBEAT_SECONDS if state == "running" else PREPARE_SECONDS
    if state in ("running", "preparing") and now - float(status.get("updated", 0)) > allowed:
        state = "interrupted"
    line = f"bake {state} {status.get('done', 0)}/{status.get('total', 0)}"
    if status.get("artifact"):
        line += f" {status.get('region', '')} {status['artifact']}"
    line += f" elapsed {status.get('elapsed_seconds', 0)}s"
    if state in ("running", "preparing"):
        return line + f" eta {status.get('eta_seconds', 0)}s", state, 3
    if state in ("interrupted", "timeout"):
        line += "  resume with: python tools/world_bake.py --bake --resume"
    elif status.get("error"):
        line += f"  {status['error']}"
    return line, state, 0 if state == "done" else 1


def cmd_status(as_json: bool) -> int:
    status = read_json(STATUS)
    line, state, code = status_line(status, time.time())
    print(json.dumps({**status, "state": state, "exit_code": code}) if as_json else line)
    return code


# ------------------------------------------------------------------------------------- check

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
        print(f"world bake is stale ({len(problems)} problem(s)):")
        for problem in capped(problems):
            print(f"  - {problem}")
        stale = stale_regions(manifest, region_signatures(sources))
        if stale:
            print(f"would rebake {len(stale)} region(s): {', '.join(stale)}; "
                  "details: python tools/world_bake.py --plan")
        print("regenerate with: python tools/world_bake.py --bake")
        return 1

    print(f"world bake current: {len(expected)} artifacts, source {signature[:12]}")
    return 0


# ------------------------------------------------------------------------------------- bake

def error_lines(output: str, limit: int = 20) -> list[str]:
    lines = [line for line in output.splitlines() if "ERROR" in line]
    more = [f"... {len(lines) - limit} more error line(s) in {BAKE_LOG.relative_to(ROOT).as_posix()}"]
    return lines[:limit] + (more if len(lines) > limit else [])


RESULT_KEYS = ("state", "exit_code", "done", "total", "elapsed_seconds", "seconds_per_artifact",
               "targets", "resumed", "tooling", "log", "error")


def finish(status: dict, state: str, code: int, error: str = "") -> int:
    status.update(state=state, exit_code=code, updated=time.time())
    if error:
        status["error"] = error
    write_quietly(STATUS, status)
    print("WORLD_BAKE " + json.dumps({key: status[key] for key in RESULT_KEYS
                                      if status.get(key) not in (None, "", [])}))
    return code


def bake(full: bool = False, forced: tuple[str, ...] = (), resume: bool = False,
         max_minutes: float = 90.0, restore_tooling: bool = False) -> int:
    before = source_hashes()
    signature = aggregate(before)
    signatures = region_signatures(before)
    unknown = [name for name in forced if _region_id(name) not in signatures]
    if unknown:
        print(f"unknown region(s): {', '.join(unknown)}; known: {', '.join(sorted(signatures))}", file=sys.stderr)
        return 2
    manifest = read_json(MANIFEST)
    targets = sorted(signatures) if full else sorted(
        set(stale_regions(manifest, signatures)) | {_region_id(name) for name in forced})
    kept_journal = read_json(JOURNAL) if resume and not full else {}
    forced_ids = {_region_id(name) for name in forced}
    resumed = sorted((resumable(kept_journal, signatures) & set(targets)) - forced_ids)
    targets = [region for region in targets if region not in resumed]
    obsolete = current_outputs() - expected_outputs()
    if not targets and not resumed and not obsolete and manifest.get("source_signature") == signature:
        print("world bake: every region is current; nothing to do")
        return check()
    print(f"world bake: {len(targets)} of {len(signatures)} region(s) to prepare: {', '.join(targets) or 'none'}"
          + (f"; kept from the interrupted run: {', '.join(resumed)}" if resumed else ""))

    outputs_by_region = {region: outputs for region, (_, outputs) in region_outputs().items()}
    expected_now = {region: outputs_by_region[region] for region in targets}
    total = sum(len(outputs) for outputs in expected_now.values())
    rate = recorded_rate()
    status = {"state": "preparing", "pid": os.getpid(), "started": time.time(), "targets": targets,
              "resumed": resumed, "done": 0, "total": total, "elapsed_seconds": 0,
              "eta_seconds": round(rate * total), "seconds_per_artifact": round(rate, 2),
              "log": BAKE_LOG.relative_to(ROOT).as_posix(), "tooling": False, "updated": time.time()}
    write_quietly(STATUS, status)

    generation = run_process(
        [sys.executable, "tools/gen_regions.py", "--check"], cwd=ROOT, timeout=120)
    if generation.returncode != 0:
        print(generation.output, end="")
        print("region resources are stale; run python tools/gen_regions.py before baking", file=sys.stderr)
        return finish(status, "failed", generation.returncode, "region resources are stale")

    # The bake runs the shipping code path, so the Debug assembly is rebuilt WITHOUT the capture
    # harnesses. Every --*shots flag is absent from that assembly until the solution is rebuilt.
    status["updated"] = time.time()
    write_quietly(STATUS, status)
    build = run_process(
        ["dotnet", "build", "-p:EmbervaleTooling=false"], timeout=600, cwd=ROOT)
    if build.returncode != 0:
        print(build.output, file=sys.stderr)
        return finish(status, "failed", build.returncode, "dotnet build failed")

    for path in sorted(obsolete):
        path.unlink()
    journal = {"regions": {region: entry for region, entry in (kept_journal.get("regions") or {}).items()
                           if region in resumed}}
    write_quietly(JOURNAL, journal)
    if targets:
        engine = require_godot()
        started = time.time()
        status.update(state="running", started=started, updated=started)
        write_quietly(STATUS, status)
        watcher = Watcher(expected_now, signatures, started, status, rate, journal)
        watcher.start()
        try:
            result = run_process(
                [str(engine), "--headless", "--path", str(ROOT), "--", "--world-bake",
                 f"--world-bake-signature={signature}",
                 "--world-bake-regions=" + ",".join(f"{region}={signatures[region]}" for region in targets)],
                timeout=max_minutes * 60, cwd=ROOT)
        except BaseException:
            watcher.stop.set()
            watcher.join(timeout=10)
            watcher.tick()
            status.update(watcher.tick())
            finish(status, "interrupted", 130, "interrupted")
            raise
        watcher.stop.set()
        watcher.join(timeout=10)
        watcher.tick()
        status.update(watcher.tick())  # twice: the second tick journals a region the first saw settle
        BAKE_LOG.parent.mkdir(parents=True, exist_ok=True)
        BAKE_LOG.write_text(result.output, encoding="utf-8")
        for line in error_lines(result.output):
            print(line)
        if result.timed_out:
            return finish(status, "timeout", 124, f"stopped after {max_minutes:g} min")
        if result.returncode != 0:
            return finish(status, "failed", result.returncode, f"engine exited {result.returncode}")

    after = source_hashes()
    if after != before:
        print("world inputs changed while the bake was running; outputs were not manifested", file=sys.stderr)
        return finish(status, "failed", 1, "inputs changed during the bake")

    expected = expected_outputs()
    missing = expected - current_outputs()
    if missing:
        for path in sorted(missing)[:LINE_CAP]:
            print(f"missing output after bake: {path.relative_to(ROOT).as_posix()}", file=sys.stderr)
        return finish(status, "failed", 1, f"{len(missing)} output(s) missing after the bake")

    sources = after
    outputs = {path.relative_to(ROOT).as_posix(): digest(path) for path in sorted(expected)}
    write_json(MANIFEST, {
        "schema": 2,
        "source_signature": aggregate(sources),
        "regions": signatures,
        "sources": sources,
        "outputs": outputs,
    })
    JOURNAL.unlink(missing_ok=True)
    print(f"wrote {len(outputs)} world artifacts and {MANIFEST.relative_to(ROOT)}")
    if restore_tooling:
        rebuild = run_process(["dotnet", "build", "Embervale.sln", "--nologo"], timeout=600, cwd=ROOT)
        status["tooling"] = rebuild.returncode == 0
        if rebuild.returncode != 0:
            print(rebuild.output[-2000:], file=sys.stderr)
    if not status["tooling"]:
        print("NOTE the Debug assembly now has no capture harnesses: run `dotnet build Embervale.sln` "
              "before any --*shots or tooling run, or bake with --restore-tooling")
    code = check()
    return finish(status, "done" if code == 0 else "failed", code)


def _region_id(name: str) -> str:
    return name if name.startswith("region.") else f"region.{name}"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--bake", action="store_true")
    mode.add_argument("--check", action="store_true")
    mode.add_argument("--plan", action="store_true", help="what --bake would do and why; no engine, no build")
    mode.add_argument("--status", action="store_true", help="progress of the running or last bake")
    parser.add_argument("--full", action="store_true", help="with --bake: prepare every region")
    parser.add_argument("--region", action="append", default=[],
                        help="with --bake: also prepare this region (id or slug); repeatable")
    parser.add_argument("--resume", action="store_true",
                        help="with --bake/--plan: keep regions an interrupted bake already finished")
    parser.add_argument("--max-minutes", type=float, default=90.0,
                        help="with --bake: stop the engine after this long (then --bake --resume)")
    parser.add_argument("--restore-tooling", action="store_true",
                        help="with --bake: rebuild the solution with harnesses once the bake is manifested")
    parser.add_argument("--json", action="store_true", help="with --plan/--status: one JSON object")
    args = parser.parse_args(argv)
    if args.plan:
        return cmd_plan(args.json, args.resume)
    if args.status:
        return cmd_status(args.json)
    if args.bake:
        return bake(args.full, tuple(args.region), args.resume, args.max_minutes, args.restore_tooling)
    return check()


if __name__ == "__main__":
    raise SystemExit(main())
