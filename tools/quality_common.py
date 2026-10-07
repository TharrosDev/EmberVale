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
                env: dict[str, str] | None = None, input: str | None = None,
                stdout_path: Path | None = None, stderr_path: Path | None = None) -> ProcessResult:
    """Own the process tree, capture both streams, and bound nested execution by one deadline.

    Windows Job Objects clean descendants on normal completion, timeout and cancellation.
    POSIX children run in a dedicated session. Every SDK subprocess also writes raw logs here,
    including Blender and asset pipeline grandchildren.

    With stdout_path/stderr_path the child writes straight into those files, so a long step can be
    tailed while it runs and its output survives the parent being killed; the result still carries
    the text. Without them both streams are held in memory, as before.
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
    sinks = {}

    def collected(piped_out, piped_err):
        """The two streams as text, whichever of memory or file each one went to."""
        texts = []
        for key, piped, path in (("stdout", piped_out, stdout_path), ("stderr", piped_err, stderr_path)):
            if key in sinks:
                sinks[key].flush()
                texts.append(Path(path).read_text(encoding="utf-8", errors="replace"))
            else:
                texts.append(piped or "")
        return texts

    try:
        for key, path in (("stdout", stdout_path), ("stderr", stderr_path)):
            if path is not None:
                Path(path).parent.mkdir(parents=True, exist_ok=True)
                sinks[key] = open(path, "wb")
        tree = ProcessTree()
        process = subprocess.Popen(
            cmd, cwd=cwd, stdin=subprocess.PIPE if input is not None else subprocess.DEVNULL,
            stdout=sinks.get("stdout", subprocess.PIPE), stderr=sinks.get("stderr", subprocess.PIPE), text=True,
            encoding="utf-8", errors="replace", env=child_env,
            creationflags=(subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_NO_WINDOW) if os.name == "nt" else 0,
            start_new_session=os.name != "nt")
        tree.assign(process)
        try:
            stdout, stderr = collected(*process.communicate(input=input, timeout=remaining))
            result = ProcessResult(cmd, process.returncode, time.monotonic() - started, stdout, stderr)
        except subprocess.TimeoutExpired:
            tree.kill(process)
            stdout, stderr = collected(*process.communicate(timeout=10))
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
        for sink in sinks.values():
            sink.close()
        if tree is not None:
            if process is not None and os.name != "nt":
                tree.kill(process)
            tree.close()
    # A step that streamed to its own log already has its evidence on disk; do not store it twice.
    if (folder := child_env.get("EMBERVALE_ARTIFACTS")) and not sinks:
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


def memory_megabytes() -> tuple[int, int] | None:
    """(free, total) physical memory in MB, or None where it cannot be read."""
    try:
        if os.name == "nt":
            import ctypes

            class Status(ctypes.Structure):
                _fields_ = [("length", ctypes.c_ulong), ("load", ctypes.c_ulong)] + [
                    (name, ctypes.c_ulonglong) for name in ("total", "free", "total_page", "free_page",
                                                            "total_virtual", "free_virtual", "extended")]
            status = Status()
            status.length = ctypes.sizeof(Status)
            if not ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)):
                return None
            return status.free // 2**20, status.total // 2**20
        fields = dict(line.split(":", 1) for line in Path("/proc/meminfo").read_text().splitlines() if ":" in line)
        return int(fields["MemAvailable"].split()[0]) // 1024, int(fields["MemTotal"].split()[0]) // 1024
    except (OSError, KeyError, ValueError, AttributeError):
        return None


def parse_tasklist(text: str) -> list[tuple[str, int, int]]:
    """(image name, pid, working-set MB) rows from `tasklist /FO CSV /NH`. The memory column is
    locale-formatted ("2,100,123 K", "2.100.123 K"), so only its digits are read."""
    import csv
    rows = []
    for record in csv.reader(text.splitlines()):
        if len(record) >= 5 and record[1].strip().isdigit():
            digits = "".join(ch for ch in record[4] if ch.isdigit())
            rows.append((record[0], int(record[1]), int(digits or 0) // 1024))
    return rows


def process_table() -> list[tuple[str, int, int]]:
    """Running processes as (name, pid, MB); empty where the platform tool is missing."""
    if os.name == "nt":
        result = run_process(["tasklist", "/FO", "CSV", "/NH"], timeout=20, env=dict(os.environ, EMBERVALE_ARTIFACTS=""))
        return parse_tasklist(result.stdout) if result.returncode == 0 else []
    result = run_process(["ps", "-eo", "comm=,pid=,rss="], timeout=20, env=dict(os.environ, EMBERVALE_ARTIFACTS=""))
    rows = []
    for line in result.stdout.splitlines() if result.returncode == 0 else []:
        parts = line.split()
        if len(parts) >= 3 and parts[-2].isdigit() and parts[-1].isdigit():
            rows.append((" ".join(parts[:-2]), int(parts[-2]), int(parts[-1]) // 1024))
    return rows


def pid_alive(pid: int) -> bool:
    """Whether a process with this id is running. A recycled id reads as alive; callers only use
    this to clear a lock whose owner is certainly gone."""
    if pid <= 0:
        return False
    if os.name == "nt":
        import ctypes
        kernel = ctypes.WinDLL("kernel32")
        kernel.OpenProcess.restype = ctypes.c_void_p
        handle = kernel.OpenProcess(0x1000, False, int(pid))  # PROCESS_QUERY_LIMITED_INFORMATION
        if not handle:
            return False
        code = ctypes.c_ulong()
        alive = bool(kernel.GetExitCodeProcess(ctypes.c_void_p(handle), ctypes.byref(code))) and code.value == 259
        kernel.CloseHandle(ctypes.c_void_p(handle))
        return alive
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def process_started(pid: int) -> int | None:
    """When this process was created, as an opaque number: a later process that reuses the id has
    a different one. None when it cannot be read (the process is gone, or the platform cannot say)."""
    if pid <= 0:
        return None
    if os.name == "nt":
        import ctypes
        from ctypes import wintypes
        kernel = ctypes.WinDLL("kernel32")
        kernel.OpenProcess.restype = ctypes.c_void_p
        handle = kernel.OpenProcess(0x1000, False, int(pid))  # PROCESS_QUERY_LIMITED_INFORMATION
        if not handle:
            return None
        times = [wintypes.FILETIME() for _ in range(4)]
        read = kernel.GetProcessTimes(ctypes.c_void_p(handle), *(ctypes.byref(item) for item in times))
        kernel.CloseHandle(ctypes.c_void_p(handle))
        return (times[0].dwHighDateTime << 32 | times[0].dwLowDateTime) if read else None
    try:   # field 22 of /proc/<pid>/stat, counted after the parenthesised command name
        return int(Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()[19])
    except (OSError, ValueError, IndexError):
        return None


def same_process(pid: int, born: int | None) -> bool:
    """Whether `pid` is alive AND is the process that recorded `born` (its process_started).
    A lock or job record is only as good as this: a bare id is reused by Windows within minutes,
    and a reused id would read as a live owner, or be killed as one. With no recorded birth time
    (an older record, or a platform that cannot tell) the id alone decides."""
    if not pid_alive(pid):
        return False
    if born is None:
        return True
    return process_started(pid) in (None, born)


def write_json(path: Path, payload: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    # Windows refuses to replace a file another process has open, and progress, state and summary
    # files are polled while they are rewritten. A reader holds one for microseconds: retry briefly.
    for attempt in range(20):
        try:
            temporary.replace(path)
            return
        except PermissionError:
            if attempt == 19:
                raise
            time.sleep(0.05)


def legacy_run(command, *, timeout=600, cwd=ROOT, input=None, check=False,
               capture_output=False, text=True, encoding="utf-8", stdout=None, stderr=None):
    """Compatibility for older specialist tools, now with the same ownership and deadlines."""
    result = run_process(command, timeout=timeout, cwd=cwd, input=input)
    if check and result.returncode:
        raise subprocess.CalledProcessError(result.returncode, command, result.stdout, result.stderr)
    return result


def legacy_check_output(command, **kwargs):
    return legacy_run(command, check=True, **kwargs).stdout
