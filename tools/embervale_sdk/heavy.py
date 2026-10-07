"""The heavy-run lock: one engine run at a time on this machine.

Two Godot runs at once is what exhausts 14 GB, so anything in the SDK that launches the engine
holds this lock, across checkouts and worktrees (it lives in the temp directory, not the project).
A lock whose owner process is gone is stale and is taken over. A nested SDK call inherits its
parent's lock through EMBERVALE_HEAVY_HELD and does not take it again.
"""
from __future__ import annotations

import json
import os
import tempfile
import time
from pathlib import Path

from quality_common import pid_alive

HELD = "EMBERVALE_HEAVY_HELD"


def lock_path() -> Path:
    return Path(os.environ.get("EMBERVALE_HEAVY_LOCK") or Path(tempfile.gettempdir()) / "embervale-heavy.lock")


def holder(path: Path | None = None) -> dict | None:
    """Who holds the lock, or None when it is free (a dead owner's file is removed)."""
    path = path or lock_path()
    try:
        info = json.loads(path.read_text(encoding="utf-8"))
        pid = int(info["pid"])
    except FileNotFoundError:
        return None
    except (OSError, ValueError, KeyError, TypeError):
        info, pid = {}, 0
        try:   # a file caught half-written belongs to a live writer; an old unreadable one is debris
            if time.time() - path.stat().st_mtime < 5:
                return dict(pid=0, what="(lock being written)")
        except OSError:
            return None
    if pid_alive(pid):
        return info
    path.unlink(missing_ok=True)
    return None


def acquire(what: str, path: Path | None = None, wait: float = 0, poll: float = 2.0, pid: int | None = None) -> dict | None:
    """Takes the lock. Returns None on success, or the current holder when it is still held after
    `wait` seconds."""
    path = path or lock_path()
    deadline = time.monotonic() + wait
    while True:
        try:
            descriptor = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        except FileExistsError:
            current = holder(path)
            if current is None:
                continue
            if time.monotonic() >= deadline:
                return current
            time.sleep(poll)
            continue
        with os.fdopen(descriptor, "w", encoding="utf-8") as handle:
            json.dump(dict(pid=pid or os.getpid(), what=what, since=int(time.time())), handle)
        return None


def release(path: Path | None = None, pid: int | None = None) -> None:
    """Drops the lock if this process (or `pid`) owns it."""
    path = path or lock_path()
    try:
        if int(json.loads(path.read_text(encoding="utf-8"))["pid"]) == (pid or os.getpid()):
            path.unlink(missing_ok=True)
    except (OSError, ValueError, KeyError, TypeError):
        pass
