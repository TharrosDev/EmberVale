#!/usr/bin/env python3
"""Shared process, Godot-discovery and artifact helpers for Embervale quality tools."""

from __future__ import annotations

import json
import os
import platform
import shutil
import subprocess
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Sequence

ROOT = Path(__file__).resolve().parent.parent
GODOT_ENV_VARS = ("EMBERVALE_GODOT", "GODOT")


def discover_godot() -> Path | None:
    candidates: list[str] = []
    for name in GODOT_ENV_VARS:
        if value := os.environ.get(name):
            candidates.append(value)
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
                env: dict[str, str] | None = None) -> ProcessResult:
    """Run a tool with a hard timeout and kill its whole process tree when it hangs."""
    cmd = [str(part) for part in command]
    started = time.monotonic()
    try:
        process = subprocess.Popen(
            cmd, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
            encoding="utf-8", errors="replace", env=env,
            creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0,
            start_new_session=os.name != "nt")
    except (FileNotFoundError, OSError) as error:
        return ProcessResult(cmd, 127, time.monotonic() - started, "", "", launch_error=str(error))
    try:
        stdout, stderr = process.communicate(timeout=timeout)
        return ProcessResult(cmd, process.returncode, time.monotonic() - started, stdout, stderr)
    except subprocess.TimeoutExpired:
        if os.name == "nt":
            subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                           capture_output=True, check=False)
        else:
            import signal
            os.killpg(process.pid, signal.SIGKILL)
        stdout, stderr = process.communicate()
        return ProcessResult(cmd, 124, time.monotonic() - started, stdout, stderr, timed_out=True)


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
