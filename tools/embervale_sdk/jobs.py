"""Background jobs: a detached run that outlives the shell that started it, polled through files.

    artifacts/jobs/<id>/job.json       what was asked: argv, the command line, the estimate
    artifacts/jobs/<id>/state.json     queued | running | done | cancelled, pids, exit code
    artifacts/jobs/<id>/progress.json  written by the SDK run inside the job: step, counts, ETA
    artifacts/jobs/<id>/output.log     the job's stdout and stderr

`start()` spawns the supervisor (this module run with -m) detached; the supervisor waits for the
heavy lock, so jobs queue instead of overlapping, runs the command, and records how it ended.
A job is sized for the 6-8 minute bake and the 9 minute engine suite: start it, then poll.
"""
from __future__ import annotations

import datetime as dt
import json
import os
import subprocess
import sys
import time
import uuid
from pathlib import Path

from quality_common import ROOT, pid_alive, write_json
from . import costs, heavy

JOBS = "artifacts/jobs"
TOOLS = Path(__file__).resolve().parents[1]
STILL_RUNNING = 75       # `job wait` exit code when the job has not finished within --max
DEAD = 4                 # the supervisor vanished without recording a result
QUEUE_SECONDS = 6 * 3600
MAX_SECONDS = 4 * 3600
ACTIVE = ("starting", "queued", "running")


def jobs_root(root: Path = ROOT) -> Path:
    return Path(root) / JOBS


def read_json(path: Path) -> dict:
    try:
        data = json.loads(Path(path).read_text(encoding="utf-8"))
        return data if isinstance(data, dict) else {}
    except (OSError, ValueError):
        return {}


def job_estimate(argv, history=None) -> float | None:
    """Seconds this command line took before (or its seed), or None when nothing is known."""
    key = "job:" + " ".join(argv)
    history = history if history is not None else costs.load()
    return history.get(key, costs.SEED.get(key))


def start(argv, sdk: bool, root: Path = ROOT, python: str = sys.executable) -> dict:
    """Creates the job directory and launches its supervisor detached. Returns job.json's content."""
    if not argv:
        raise ValueError("job start needs a command, e.g. `job start world --mode engine`")
    root = Path(root)
    identifier = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + uuid.uuid4().hex[:6]
    directory = jobs_root(root) / identifier
    directory.mkdir(parents=True)
    (directory / ".gdignore").touch()
    command = [python, str(TOOLS / "embervale.py"), *argv] if sdk else list(argv)
    info = dict(id=identifier, argv=list(argv), command=command, sdk=sdk, created=time.time(),
                eta_seconds=job_estimate(argv, costs.load(root)), directory=str(directory), cwd=str(root))
    write_json(directory / "job.json", info)
    write_json(directory / "state.json", dict(state="starting"))
    environment = dict(os.environ, PYTHONPATH=str(TOOLS), PYTHONUNBUFFERED="1")
    with open(directory / "supervisor.log", "wb") as log:
        options = dict(cwd=root, env=environment, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT)
        supervisor = [python, "-m", "embervale_sdk.jobs", str(directory)]
        if os.name == "nt":
            detached = 0x00000008 | subprocess.CREATE_NEW_PROCESS_GROUP   # DETACHED_PROCESS
            try:   # leave the launching shell's job object, so closing that shell does not end the run
                process = subprocess.Popen(supervisor, creationflags=detached | 0x01000000, **options)
            except OSError:   # the shell's job object does not permit breakaway
                process = subprocess.Popen(supervisor, creationflags=detached, **options)
        else:
            process = subprocess.Popen(supervisor, start_new_session=True, **options)
    process.returncode = 0   # detached on purpose: it outlives this process, so there is nothing to reap or warn about
    # Its own file: job.json is being read by the supervisor right now and must not be replaced under it.
    (directory / "supervisor.pid").write_text(str(process.pid), encoding="utf-8")
    return dict(info, pid=process.pid)


def write_state(directory: Path, state: dict) -> None:
    """state.json is polled by `job status` while the supervisor rewrites it; write_json retries
    the replace a reader can briefly block on Windows."""
    write_json(Path(directory) / "state.json", state)


def supervise(directory: Path) -> int:
    """The detached process: queue on the heavy lock, run the command, record the outcome."""
    directory = Path(directory)
    info = read_json(directory / "job.json")
    state = dict(state="queued", pid=os.getpid(), queued=time.time())
    write_state(directory, state)
    what = f"job {info.get('id')}: {' '.join(info.get('argv', []))}"
    blocker = heavy.acquire(what, wait=QUEUE_SECONDS, poll=3.0)
    if blocker:
        state.update(state="done", exit_code=2, ended=time.time(),
                     error=f"heavy lock still held after {QUEUE_SECONDS}s by pid {blocker.get('pid')}: {blocker.get('what')}")
        write_state(directory, state)
        return 2
    code = 1
    try:
        environment = dict(os.environ, EMBERVALE_JOB_DIR=str(directory), PYTHONUNBUFFERED="1")
        environment[heavy.HELD] = "1"
        state.update(state="running", started=time.time())
        with open(directory / "output.log", "wb") as log:
            try:
                child = subprocess.Popen(info["command"], cwd=info.get("cwd") or ROOT, env=environment,
                                         stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT,
                                         creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
            except OSError as error:
                state.update(state="done", exit_code=2, ended=time.time(), error=f"could not launch: {error}")
                write_state(directory, state)
                return 2
            state["child"] = child.pid
            write_state(directory, state)
            try:
                code = child.wait(timeout=MAX_SECONDS)
            except subprocess.TimeoutExpired:
                kill_tree(child.pid)
                code = 3
                state["error"] = f"exceeded {MAX_SECONDS}s"
    finally:
        heavy.release()
    ended = time.time()
    state.update(state="done", exit_code=code, ended=ended)
    write_state(directory, state)
    if code == 0:
        try:
            root = Path(info.get("cwd") or ROOT)
            history = costs.load(root)
            costs.record(history, "job:" + " ".join(info.get("argv", [])), ended - state["started"])
            costs.save(history, root)
        except OSError:
            pass
    return code


def kill_tree(pid: int) -> None:
    if os.name == "nt":
        subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True, timeout=20)
    else:
        import signal
        try:
            os.killpg(pid, signal.SIGKILL)
        except (ProcessLookupError, PermissionError):
            pass


def cancel(directory: Path) -> bool:
    """Ends a live job and everything under it. False when it had already finished."""
    directory = Path(directory)
    current = view(directory)
    if current["state"] not in ACTIVE:
        return False
    for pid in (current.get("pid"), current.get("child")):
        if pid:
            kill_tree(int(pid))
    if current.get("pid"):
        heavy.release(pid=int(current["pid"]))
    state = read_json(directory / "state.json")
    state.update(state="cancelled", ended=time.time(), exit_code=130)
    write_state(directory, state)
    return True


def find(identifier: str | None, root: Path = ROOT) -> Path:
    """A job directory by id, unique prefix or suffix; the newest when no id is given."""
    from .compact import runs
    directories = runs(jobs_root(root))
    if not directories:
        raise ValueError("no jobs yet; start one with `job start <command>`")
    if not identifier:
        return directories[0]
    matches = [d for d in directories if d.name == identifier] or \
              [d for d in directories if d.name.startswith(identifier) or d.name.endswith(identifier)]
    if len(matches) != 1:
        raise ValueError(f"{'no' if not matches else 'more than one'} job matches {identifier!r} (see `job list`)")
    return matches[0]


def view(directory: Path) -> dict:
    """Everything known about a job, merged: job.json, state.json and the run's progress.json.
    A job whose supervisor is gone without a result reads as state `dead`."""
    directory = Path(directory)
    merged = dict(read_json(directory / "job.json"))
    state = read_json(directory / "state.json")
    merged.update(state)
    merged["progress"] = read_json(directory / "progress.json")
    merged.setdefault("id", directory.name)
    merged.setdefault("state", "starting")
    if not merged.get("pid"):   # before the supervisor has written its first state
        try:
            merged["pid"] = int((directory / "supervisor.pid").read_text(encoding="utf-8"))
        except (OSError, ValueError):
            merged["pid"] = 0
    if merged["state"] == "starting" and not merged["pid"] and time.time() - merged.get("created", 0) < 15:
        return merged   # `job start` is between spawning the supervisor and recording its pid
    if merged["state"] in ACTIVE and not pid_alive(int(merged.get("pid") or 0)):
        # The supervisor writes `done` as its last act; re-read once before calling it dead.
        state = read_json(directory / "state.json")
        merged.update(state)
        if merged["state"] in ACTIVE:
            merged["state"] = "dead"
            merged["exit_code"] = DEAD
    return merged


def status_line(job: dict, now: float | None = None) -> str:
    """One line for a job view: what it is doing, how far along, and how long is left."""
    now = now if now is not None else time.time()
    identifier, state, progress = job.get("id"), job.get("state"), job.get("progress") or {}
    what = " ".join(job.get("argv", []))[:80]
    if state in ("starting", "queued"):
        holder = heavy.holder() if state == "queued" else None
        waiting = f" behind pid {holder.get('pid')} ({holder.get('what')})" if holder else ""
        return f"QUEUED {identifier} {costs.clock(now - job.get('queued', job.get('created', now)))}{waiting}: {what}"
    if state == "running":
        elapsed = now - job.get("started", now)
        line = f"RUNNING {identifier} {costs.clock(elapsed)}"
        remaining = None
        if progress and not progress.get("done"):
            total = progress.get("steps_total")
            line += f" step {progress.get('steps_done', 0) + (1 if progress.get('step') else 0)}" + (f"/{total}" if total else "")
            if progress.get("step"):
                line += f" {progress['step']} {costs.clock(now - (progress.get('step_started') or now))}"
            line += f" fails={progress.get('failed', 0)}"
            if total:
                remaining = progress.get("eta_seconds", 0) - (now - progress.get("updated", now))
        if remaining is None and job.get("eta_seconds"):
            remaining = job["eta_seconds"] - elapsed
        if remaining is not None:
            line += f" eta={costs.clock(remaining)}" if remaining > 0 else " eta=overdue"
        return f"{line}: {what}"
    duration = costs.clock((job.get("ended") or now) - (job.get("started") or job.get("created") or now))
    code = job.get("exit_code")
    if state == "done":
        line = f"DONE {'PASS' if code == 0 else 'FAIL'} {identifier} exit={code} {duration}"
        if job.get("error"):
            line += f" ({job['error']})"
        if progress.get("artifact_directory"):
            from .compact import short_path
            line += f" evidence={short_path(progress['artifact_directory'])}"
        return f"{line}: {what}"
    if state == "cancelled":
        return f"CANCELLED {identifier} after {duration}: {what}"
    return f"DEAD {identifier} supervisor gone, last step {progress.get('step') or '?'}; see output.log: {what}"


def tail(path: Path, lines: int = 20) -> list[str]:
    """The last lines of a file that may still be growing."""
    try:
        with open(path, "rb") as handle:
            handle.seek(0, os.SEEK_END)
            size = handle.tell()
            handle.seek(max(0, size - 64 * 1024))
            text = handle.read().decode("utf-8", errors="replace")
    except OSError:
        return []
    return text.splitlines()[-max(1, lines):]


if __name__ == "__main__":
    raise SystemExit(supervise(Path(sys.argv[1])))
