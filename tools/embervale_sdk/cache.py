"""Gate result cache: a gate that PASSED on exactly these inputs is not run again.

The key is the committed tree (per top-level entry) plus the content of every changed, staged or
untracked file, so any edit to an input changes it. Only passes are stored, never a failure.
Ignored files (.godot/, artifacts/) are not inputs: the stale-build guard owns the assembly, and a
missing import cache is `doctor`'s finding. `--no-cache` skips the lookup.
"""
from __future__ import annotations

import hashlib
import json
import time
from pathlib import Path

from quality_common import ROOT, run_process, write_json

STORE = "artifacts/.gate-cache.json"
# Top-level entries no gate reads. Docs stay inputs: the unit suite reads some of them.
NEVER_INPUT = (".github", ".claude")


def tree_state(root: Path = ROOT) -> dict | None:
    """{"entries": {top-level name: object id}, "changed": {path: content digest}} or None when
    this is not a git checkout (then nothing is cached)."""
    root = Path(root)
    listing = run_process(["git", "ls-tree", "HEAD"], timeout=30, cwd=root)
    status = run_process(["git", "status", "--porcelain=v1", "-z", "--untracked-files=all"], timeout=60, cwd=root)
    if listing.returncode or status.returncode:
        return None
    entries = {}
    for line in listing.stdout.splitlines():
        meta, _, name = line.partition("\t")
        if name:
            entries[name] = meta.split()[-1]
    changed = {}
    tokens = [t for t in status.stdout.split("\0") if t]
    index = 0
    while index < len(tokens):
        code, path = tokens[index][:2], tokens[index][3:]
        paths = [path]
        if "R" in code or "C" in code:   # the next token is the path it was renamed or copied from
            index += 1
            if index < len(tokens):
                paths.append(tokens[index])
        for item in paths:
            file = root / item
            changed[item.replace("\\", "/")] = (hashlib.sha256(file.read_bytes()).hexdigest()
                                               if file.is_file() else "absent")
        index += 1
    return dict(entries=entries, changed=changed)


def key(state: dict, inputs=(), extra=()) -> str:
    """The cache key for a gate. `inputs` narrows it to those top-level entries; empty means the
    whole tree apart from NEVER_INPUT."""
    def counts(path):
        top = path.split("/", 1)[0]
        return top in inputs if inputs else top not in NEVER_INPUT
    payload = [sorted((n, s) for n, s in state["entries"].items() if counts(n)),
               sorted((p, d) for p, d in state["changed"].items() if counts(p)),
               [str(e) for e in extra]]
    return hashlib.sha256(json.dumps(payload).encode("utf-8")).hexdigest()


class GateCache:
    def __init__(self, root: Path = ROOT):
        self.path = Path(root) / STORE
        self.entries = self.read()

    def read(self) -> dict:
        try:
            entries = json.loads(self.path.read_text(encoding="utf-8"))
            return entries if isinstance(entries, dict) else {}
        except (OSError, ValueError):
            return {}

    def get(self, name: str, cache_key: str) -> dict | None:
        entry = self.entries.get(name)
        return entry if isinstance(entry, dict) and entry.get("key") == cache_key else None

    def put(self, name: str, cache_key: str, duration: float, run: str) -> None:
        self.entries = self.read()   # another run may have stored a pass since this one started
        self.entries[name] = dict(key=cache_key, duration=round(duration, 1), run=run, passed_at=int(time.time()))
        write_json(self.path, self.entries)
