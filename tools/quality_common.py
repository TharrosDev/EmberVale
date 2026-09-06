#!/usr/bin/env python3
"""Shared process, Godot-discovery and artifact helpers for Embervale quality tools."""

from __future__ import annotations

import json
import os
import platform
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Sequence

ROOT = Path(__file__).resolve().parent.parent
GODOT_ENV_VARS = ("EMBERVALE_GODOT", "GODOT")

# Windows consoles default to cp1252, and every tool here prints the repo's warning glyphs. Without
# this a plain `--help` dies with UnicodeEncodeError before it prints anything useful.
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")



def discover_godot() -> Path | None:
    candidates: list[str] = []
    for name in GODOT_ENV_VARS:
        if value := os.environ.get(name):
            path = Path(value).expanduser()
            return path.resolve() if path.is_file() else None
    for command in ("godot", "godot4", "godot-mono"):
        if found := shutil.which(command):
            candidates.append(found)
    if os.name == "nt":
        candidates.extend(str(path) for path in sorted((Path.home() / "Downloads").glob(
            "Godot_v*-stable_mono_win64/**/Godot*_console.exe"), reverse=True))
    for candidate in candidates:
        path = Path(candidate).expanduser()
        if path.is_file():
            return path.resolve()
    return None


def require_godot() -> Path:
    engine = discover_godot()
    if engine is None:
        names = " or ".join(f"{name}=<console executable>" for name in GODOT_ENV_VARS)
        raise RuntimeError(f"Godot .NET console executable not found. Set {names}, or put godot on PATH.")
    return engine


@dataclass
class ProcessResult:
    command: list[str]
    returncode: int
    elapsed_seconds: float
    stdout: str
    stderr: str
    timed_out: bool = False
    launch_error: str | None = None

    @property
    def output(self) -> str:
        return self.stdout + self.stderr


def run_process(command: Sequence[str], *, timeout: float, cwd: Path = ROOT,
                env: dict[str, str] | None = None, input: str | None = None) -> ProcessResult:
    """Own the process tree, capture both streams, and bound nested execution by one deadline.

    Windows Job Objects clean descendants on normal completion, timeout and cancellation.
    POSIX children run in a dedicated session. Every SDK subprocess also writes raw logs here,
    including Blender and asset pipeline grandchildren.
    """
    from process_tree import ProcessTree
    import uuid
    cmd = [str(part) for part in command]
    started = time.monotonic()
    child_env = dict(os.environ if env is None else env)
    child_env.setdefault("PYTHONIOENCODING", "utf-8")
    child_env.setdefault("PYTHONUTF8", "1")
    deadline = min(time.time() + timeout, float(child_env.get("EMBERVALE_DEADLINE", "inf")))
    child_env["EMBERVALE_DEADLINE"] = str(deadline)
    remaining = deadline - time.time()
    if remaining <= 0:
        return ProcessResult(cmd, 124, 0, "", "Parent deadline expired", timed_out=True)
    process = None
    tree = None
    result = None
    try:
        tree = ProcessTree()
        process = subprocess.Popen(
            cmd, cwd=cwd, stdin=subprocess.PIPE if input is not None else subprocess.DEVNULL,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
            encoding="utf-8", errors="replace", env=child_env,
            creationflags=(subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_NO_WINDOW) if os.name == "nt" else 0,
            start_new_session=os.name != "nt")
        tree.assign(process)
        try:
            stdout, stderr = process.communicate(input=input, timeout=remaining)
            result = ProcessResult(cmd, process.returncode, time.monotonic() - started, stdout, stderr)
        except subprocess.TimeoutExpired:
            tree.kill(process)
            stdout, stderr = process.communicate(timeout=10)
            result = ProcessResult(cmd, 124, time.monotonic() - started, stdout, stderr, timed_out=True)
        except KeyboardInterrupt:
            tree.kill(process)
            process.communicate(timeout=10)
            raise
    except OSError as error:
        if process is not None:
            process.kill()
            process.communicate(timeout=10)
        result = ProcessResult(cmd, 127, time.monotonic() - started, "", "", launch_error=str(error))
    finally:
        if tree is not None:
            if process is not None and os.name != "nt":
                tree.kill(process)
            tree.close()
    if folder := child_env.get("EMBERVALE_ARTIFACTS"):
        directory = Path(folder) / "processes"
        directory.mkdir(parents=True, exist_ok=True)
        label = uuid.uuid4().hex[:12]
        (directory / (label + ".stdout.log")).write_text(result.stdout, encoding="utf-8")
        (directory / (label + ".stderr.log")).write_text(result.stderr, encoding="utf-8")
        write_json(directory / (label + ".json"), {"command": cmd, "returncode": result.returncode,
                   "duration": result.elapsed_seconds, "timed_out": result.timed_out,
                   "launch_error": result.launch_error})
    return result


def discover_blender() -> Path | None:
    """Same discovery for doctor, asset builds and audit; no MCP requirement."""
    explicit = os.environ.get("EMBERVALE_BLENDER")
    if explicit:
        path = Path(explicit).expanduser()
        return path.resolve() if path.is_file() else None
    if found := shutil.which("blender"):
        return Path(found)
    if os.name == "nt":
        candidates = sorted(Path("C:/Program Files/Blender Foundation").glob("Blender */blender.exe"), reverse=True)
        return next((p for p in candidates if p.is_file()), None)
    return None


def command_text(command: Sequence[str]) -> str:
    return subprocess.list2cmdline([str(part) for part in command])


def machine_fingerprint() -> dict[str, str]:
    return {"platform": platform.platform(), "machine": platform.machine(),
            "python": platform.python_version()}


def write_json(path: Path, payload: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(path)


def legacy_run(command, *, timeout=600, cwd=ROOT, input=None, check=False,
               capture_output=False, text=True, encoding="utf-8", stdout=None, stderr=None):
    """Compatibility for older specialist tools, now with the same ownership and deadlines."""
    result = run_process(command, timeout=timeout, cwd=cwd, input=input)
    if check and result.returncode:
        raise subprocess.CalledProcessError(result.returncode, command, result.stdout, result.stderr)
    return result


def legacy_check_output(command, **kwargs):
    return legacy_run(command, check=True, **kwargs).stdout
