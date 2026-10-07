"""How long a step takes: measured seeds, corrected by what this checkout actually recorded.

Estimates feed `verify --plan`, the progress file's ETA and `job status`. A passing step updates
artifacts/.durations.json (an exponential average), so the numbers follow the machine.
"""
from __future__ import annotations

import json
from pathlib import Path

from quality_common import ROOT, write_json

HISTORY = "artifacts/.durations.json"

# Seconds, measured on the maintainer machine 2026-10-07. Step names are the SDK's own.
SEED = {
    "build": 20, "auto-build": 20, "tests": 19, "game-tests": 19, "tool-tests": 25,
    "content": 18, "lifecycle": 38, "save-reload": 31, "traversal": 50, "story": 60,
    "shipping-assembly": 30, "negative": 2400, "assets": 40, "import": 120, "docs-lines": 1,
    "godot-version": 1,
    # Whole command lines, for a background job that has no step plan yet.
    "job:world --mode fast": 50, "job:world --mode engine": 550, "job:world": 550,
    "job:tool world_bake -- --bake": 420, "job:assets validate": 40, "job:test": 45, "job:build": 20,
}
ENGINE_STEP = 10   # the engine suite is 550 s over about fifty launches once the named ones are taken out
PYTHON_STEP = 1


def load(root: Path = ROOT) -> dict:
    try:
        data = json.loads((Path(root) / HISTORY).read_text(encoding="utf-8"))
        return {k: float(v) for k, v in data.items() if isinstance(v, (int, float))}
    except (OSError, ValueError, AttributeError):
        return {}


def save(history: dict, root: Path = ROOT) -> None:
    write_json(Path(root) / HISTORY, {k: round(v, 1) for k, v in sorted(history.items())})


def record(history: dict, name: str, seconds: float) -> None:
    """Folds one passing duration into the history, half old and half new."""
    history[name] = seconds if name not in history else (history[name] + seconds) / 2


def estimate(name: str, history: dict | None = None, engine: bool = False) -> float:
    """Seconds for a step: recorded history, else the seed, else a default by kind. A per-region
    step (`seams-ember_crown`) falls back to its gate's entry."""
    history = history or {}
    for key in (name, name.rsplit("-", 1)[0] if "-" in name else name):
        if key in history:
            return history[key]
    for key in (name, name.rsplit("-", 1)[0] if "-" in name else name):
        if key in SEED:
            return float(SEED[key])
    return float(ENGINE_STEP if engine else PYTHON_STEP)


def clock(seconds: float) -> str:
    """Compact duration: 41s, 7m10s, 1h02m."""
    seconds = max(0, int(round(seconds)))
    if seconds < 60:
        return f"{seconds}s"
    if seconds < 3600:
        return f"{seconds // 60}m{seconds % 60:02d}s"
    return f"{seconds // 3600}h{seconds % 3600 // 60:02d}m"
