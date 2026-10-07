"""Background jobs for runs longer than a foreground shell allows.

    job start world --mode engine          start detached; prints the id and the estimate
    job start tool world_bake -- --bake    any SDK command line; a non-SDK command runs as given
    job status [ID]                        one line: state, step, elapsed, ETA
    job wait [ID] --max 540                block until done (exit = the job's) or --max (exit 75)
    job tail [ID] [--lines 20] [--step]    the job's output, or the step in flight with --step
    job list                               recent jobs, newest first
    job cancel [ID]                        end it and everything under it

ID is a job id, or any unique start or end of one; without it the newest job is meant. Everything
after `job start` belongs to the job, so flags for `job` itself go before `start`. Jobs take the
heavy-run lock: a second job queues behind the first instead of overlapping it.
"""
import json
import time

from .. import jobs
from . import REGISTRY

HELP = "start, poll, tail and cancel detached background runs (bake, engine suite) with progress and ETA"
LIGHT = True
VERSION = False
PASSTHROUGH = True
ACTIONS = ("start", "status", "wait", "tail", "list", "cancel")


def arguments(parser):
    parser.add_argument("identifier", nargs="?", help="job id or a unique prefix/suffix (default: the newest job)")
    parser.add_argument("--max", type=float, default=540, help="wait: give up after this many seconds (exit 75)")
    parser.add_argument("--lines", type=int, default=20, help="tail: how many lines")
    parser.add_argument("--step", action="store_true", help="tail: the log of the step in flight, not the job output")


def emit(args, job, extra=()):
    if args.json or args.json_full:
        keep = ("id", "state", "exit_code", "argv", "eta_seconds", "created", "started", "ended", "error", "directory")
        print(json.dumps({**{k: job.get(k) for k in keep}, "progress": job.get("progress") or None}))
    else:
        print(jobs.status_line(job))
        for line in extra:
            print(line)


def run(run, args, passthrough):
    action = args.target
    if action not in ACTIONS:
        raise ValueError("job needs one of: " + ", ".join(ACTIONS))
    if action == "start":
        info = jobs.start(passthrough, sdk=bool(passthrough) and passthrough[0] in REGISTRY)
        if args.json or args.json_full:
            print(json.dumps({k: info.get(k) for k in ("id", "pid", "eta_seconds", "directory", "argv")}))
        else:
            from ..compact import short_path
            from ..costs import clock
            eta = f" eta={clock(info['eta_seconds'])}" if info.get("eta_seconds") else ""
            print(f"JOB {info['id']} pid={info['pid']}{eta} dir={short_path(info['directory'])} "
                  f"(poll: job status | job wait --max 540)")
        return 0
    if action == "list":
        from ..compact import runs
        for directory in runs(jobs.jobs_root())[:max(1, args.lines)]:
            emit(args, jobs.view(directory))
        return 0
    directory = jobs.find(args.identifier)
    if action == "cancel":
        cancelled = jobs.cancel(directory)
        emit(args, jobs.view(directory))
        return 0 if cancelled else 1
    if action == "tail":
        job = jobs.view(directory)
        source = (job.get("progress") or {}).get("log") if args.step else None
        for line in jobs.tail(source or directory / "output.log", args.lines):
            print(line)
        return 0
    job = jobs.view(directory)
    if action == "wait":
        deadline = time.monotonic() + max(0, args.max)
        while job["state"] in jobs.ACTIVE and time.monotonic() < deadline:
            time.sleep(2)
            job = jobs.view(directory)
        if job["state"] in jobs.ACTIVE:
            emit(args, job)
            return jobs.STILL_RUNNING
        # A finished job's own output ends with its failures and verdict: that is the result.
        emit(args, job, jobs.tail(directory / "output.log", 15))
        return int(job.get("exit_code") if job.get("exit_code") is not None else jobs.DEAD)
    emit(args, job)   # status: 0 whatever the job's state; the line says it
    return 0
