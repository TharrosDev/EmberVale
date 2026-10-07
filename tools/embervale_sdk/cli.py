"""Canonical command orchestration. Standard library only; no editor or MCP required."""
from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import hashlib
import os
import re
import shutil
import sys
import threading
import time
import uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from quality_common import ROOT, discover_godot, run_process, write_json, machine_fingerprint
from .contract import SCHEMA, diagnostic, diagnostics_from_log, exit_code, first_words
from .changed import changed_paths
from .scenario import validate_plan, resource_path
from .freshness import stale_reason
from . import commands, compact, costs, heavy
from . import cache as gate_cache
from .commands import REGISTRY

# Arguments that mean a specialist tool is only reading: no rebuild and no heavy lock for those.
READ_ONLY_ARGUMENTS = {"--check", "--list", "--help", "-h", "status"}
# Gates that launch the engine from inside a Python tool, so the command line does not show it.
ENGINE_INSIDE = {"negative"}
# `job start <command line>`: everything after these two words belongs to the job, flags included.
REST_AFTER = {("job", "start")}


class BuildFailed(RuntimeError):
    """The automatic rebuild of a stale binary failed; its step already carries the diagnostics."""


def parser(command=None):
    """The argument parser. Shared flags are accepted by every command; `command` adds that
    command's own flags (the ones its registry entry declares) for the real parse."""
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("command", choices=tuple(REGISTRY))
    p.add_argument("target", nargs="?", help="scenario JSON, tool name, or asset subcommand")
    p.add_argument("--scene", type=resource_path)
    p.add_argument("--timeout", type=float, default=None, help="hard per-process seconds (including descendants)")
    p.add_argument("--frames", type=int, help="runtime frames (default 120); explicitly overrides specialist capture waits")
    p.add_argument("--seed", type=int, default=12345)
    p.add_argument("--json", action="store_true", help="only the compact result JSON on stdout (failures, counts, evidence path)")
    p.add_argument("--json-full", action="store_true", help="the whole result JSON on stdout (what summary.json holds)")
    p.add_argument("--ndjson", action="store_true", help="stream step events and a final compact result")
    p.add_argument("-v", "--verbose", action="store_true", help="print every step as it runs, not only failures")
    p.add_argument("--no-cache", action="store_true", help="world/verify: run a gate even if it passed on these exact inputs")
    p.add_argument("--parallel", type=int, default=4, help="world/verify: pure-Python gates run at once (1 = serial)")
    p.add_argument("--wait-lock", type=float, default=0, metavar="SEC",
                   help="wait this long for another engine run to finish, instead of exiting 2")
    p.add_argument("--artifacts", type=Path, help="parent directory; a unique run directory is always created")
    p.add_argument("--strict", action="store_true", help="warnings fail the run")
    p.add_argument("--changed-only", action="store_true")
    p.add_argument("--base", help="include changes since this merge base as well as staged/unstaged/untracked")
    p.add_argument("--godot", type=Path)
    p.add_argument("--mode", choices=("fast", "engine", "visual", "performance", "full"), default="engine")
    p.add_argument("--gate", action="append", help="world: run only this named gate; repeat for a focused regression set")
    p.add_argument("--region", choices=("ember_crown", "frostfang_reach", "ashen_wilds", "sunspire", "pale_concord", "celestial"))
    p.add_argument("--engine-tests", action="store_true", help="include native protocol integration tests")
    p.add_argument("--render", action="store_true", help="use a rendering display (required for screenshot evidence)")
    p.add_argument("--resolution", action="append", help="WIDTHxHEIGHT; repeat for a capture set")
    p.add_argument("--resource", help="inspect one project resource without instantiating a game")
    p.add_argument("--node", help="inspect one absolute node path rather than the whole tree")
    p.add_argument("--max-warnings", type=int, help="maximum warning occurrences before failing")
    p.add_argument("--camera", help="absolute node path to an existing Camera3D")
    p.add_argument("--position", help="camera x,y,z")
    p.add_argument("--rotation", help="camera Euler degrees x,y,z")
    p.add_argument("--viewpoint", help="name from tools/headless/viewpoints.json")
    p.add_argument("--baseline", type=Path, help="existing PNG for screenshot or metrics JSON for perf")
    p.add_argument("--threshold", type=float, default=0.05, help="image normalized mean error / relative perf regression")
    p.add_argument("--max-frame-ms", type=float, help="fail perf when sampled p95 exceeds this budget")
    p.add_argument("--reimport", action="store_true", help="force the editor to scan/reimport all assets")
    p.add_argument("--write", help="author: publish the validated scene/resource to res://scenes/... or res://data/...")
    p.add_argument("--overwrite", action="store_true", help="author: permit replacement, with backup and concurrent-edit check")
    p.add_argument("--no-build", action="store_true", help="do not rebuild a stale Embervale.dll before launching the engine")
    entry = REGISTRY.get(command)
    if entry and entry.arguments:
        entry.arguments(p.add_argument_group(f"{command} options"))
    return p


class Run:
    def __init__(self, args):
        self.args = args
        explicit_frames = args.frames is not None
        if args.frames is None:
            args.frames = 120
        self.started = time.monotonic()
        config_path = ROOT / "tools/headless/config.json"
        config = json.loads(config_path.read_text()) if config_path.exists() else {}
        self.timeout = args.timeout if args.timeout is not None else config.get("timeout", 900)
        if not math.isfinite(self.timeout) or self.timeout <= 0 or not 0 <= args.frames <= 100000 or not math.isfinite(args.threshold) or args.threshold < 0:
            raise ValueError("timeout must be positive, frames 0..100000, threshold nonnegative")
        if args.godot:
            os.environ["EMBERVALE_GODOT"] = str(args.godot.resolve())
        self.engine = discover_godot()
        run_id = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + uuid.uuid4().hex[:10]
        parent = (args.artifacts or ROOT / "artifacts/headless").resolve()
        parent.mkdir(parents=True, exist_ok=True)
        self.artifacts = parent / run_id
        self.artifacts.mkdir()
        (self.artifacts / ".gdignore").touch()
        (self.artifacts / "user").mkdir()
        self.env = dict(os.environ, EMBERVALE_ARTIFACTS=str(self.artifacts),
                        EMBERVALE_USER_DIR=str(self.artifacts / "user"),
                        EMBERVALE_SEED=str(args.seed),
                        PYTHONUNBUFFERED="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
        if explicit_frames:
            self.env["EMBERVALE_FRAMES"] = str(args.frames)
        self.fresh_checked = False
        self.lock = threading.Lock()
        self.step_index = 0           # the next step's log number
        self.planned = []             # names of the steps still to run, when the command knows them
        self.plan_known = False
        self.engine_steps = set()
        self.history = costs.load()
        self.history_changed = False
        self.cache = gate_cache.GateCache()
        self.tree = None              # the cache's view of the working tree, read once per run
        self.heavy_held = False
        job = os.environ.get("EMBERVALE_JOB_DIR")
        self.job_directory = Path(job) if job and Path(job).is_dir() else None
        self.env.pop("EMBERVALE_JOB_DIR", None)   # a nested SDK call must not report as the job
        self.result = dict(schema=SCHEMA, run_id=run_id, command=args.command, success=False,
                           duration=0, godot_version=None, diagnostics=[], assertions=[], metrics={},
                           artifacts=[], steps=[], machine=machine_fingerprint(),
                           configuration=dict(seed=args.seed, frames=args.frames, timeout=self.timeout,
                                              changed_only=args.changed_only, strict=args.strict))

    def note(self, message):
        """A progress line. Only with --verbose: by default a run prints its failures and one verdict."""
        if self.args.verbose and not (self.args.json or self.args.ndjson or self.args.json_full):
            print(message, flush=True)

    def issue(self, code, message, severity="error", path=None):
        self.result["diagnostics"].append(diagnostic(severity, code, message, path))

    def brief(self, line):
        """A line of the command's own product, shown in the compact output above the verdict."""
        self.result.setdefault("brief", []).append(line)

    def process(self, name, command, timeout=None, scan=True, expected_errors=()):
        label = self.reserve(name)
        self.progress(name, label)
        return self.record(self.execute(name, label, command, timeout, scan, expected_errors))

    def reserve(self, name):
        with self.lock:
            label = f"{self.step_index:02d}-{name}"
            self.step_index += 1
        return label

    def execute(self, name, label, command, timeout=None, scan=True, expected_errors=()):
        """Runs one step and returns its outcome without touching the result, so pure-Python gates
        can run side by side; record() files the outcome."""
        # The step's own limit stands (a gate may need longer than the default); an explicit
        # --timeout is the only thing that caps it.
        limit = timeout or self.timeout
        if self.args.timeout is not None:
            limit = min(limit, self.args.timeout)
        r = run_process(command, cwd=ROOT, timeout=limit, env=self.env,
                        stdout_path=self.artifacts / f"{label}.stdout.log",
                        stderr_path=self.artifacts / f"{label}.stderr.log")
        issues = []
        if r.launch_error:
            code = 2
            issues.append(("process.launch", f"{name}: {r.launch_error}"))
        elif r.timed_out:
            code = 3
            issues.append(("process.timeout", f"{name} exceeded deadline ({limit:.0f}s); process tree terminated"))
        elif r.returncode < 0 or r.returncode > 128:
            code = 4
            issues.append(("process.crash", f"{name} exited abnormally: {r.returncode}"))
        else:
            nested_sdk = len(command) > 1 and Path(str(command[1])).name in {"embervale.py", "assets.py", "world_quality_check.py"}
            code = r.returncode if nested_sdk and r.returncode in {0, 1, 2, 3, 4, 5, 130} else (1 if r.returncode else 0)
        found = diagnostics_from_log(r.output, name) if scan else []
        if r.returncode == 0:
            for item in found:
                if any(re.search(pattern, item["message"]) for pattern in expected_errors):
                    item.update(severity="info", code="fixture.expected_error")
        if code and not found:
            said = first_words(r.stderr) or first_words(r.stdout) or f"no output; see {label}.stderr.log"
            issues.append(("process.failed", f"{name}: exit {r.returncode}: {said}"))
        step = dict(name=name, command=[str(c) for c in command], duration=r.elapsed_seconds,
                    exit_code=code, process_exit_code=r.returncode,
                    success=code == 0 and not any(d["severity"] == "error" for d in found),
                    stdout=f"{label}.stdout.log", stderr=f"{label}.stderr.log")
        return r, step, found, issues

    def record(self, outcome):
        r, step, found, issues = outcome
        for code, message in issues:
            self.issue(code, message)
        self.result["diagnostics"].extend(found)
        self.add_step(step)
        if step["success"]:
            costs.record(self.history, step["name"], step["duration"])
            self.history_changed = True
        return r

    def add_step(self, step):
        self.result["steps"].append(step)
        if step["name"] in self.planned:
            self.planned.remove(step["name"])
        verdict = "CACHED" if step.get("cached") else ("PASS" if step.get("success") else "FAIL")
        self.note(f"  {step['name']}: {verdict} ({step.get('duration', 0):.1f}s)")
        if self.args.ndjson:
            print(json.dumps(dict(event="step", **step)), flush=True)
        self.progress()

    def progress(self, current=None, label=None, done=False):
        """Writes the pollable progress file (and the job's copy), and between steps a partial
        summary.json, so a run killed at a time limit still says how far it got."""
        steps = self.result["steps"]
        if self.planned:
            eta = sum(costs.estimate(n, self.history, n in self.engine_steps) for n in self.planned)
        else:
            eta = costs.estimate(current, self.history) if current else 0
        now = time.time()
        payload = dict(run_id=self.result["run_id"], command=self.result.get("label") or self.args.command,
                       pid=os.getpid(), step=current, step_started=now if current else None,
                       log=str(self.artifacts / f"{label}.stdout.log") if label else None,
                       steps_done=len(steps), steps_total=len(steps) + len(self.planned) if self.plan_known else None,
                       failed=sum(1 for s in steps if not s.get("success", not s.get("exit_code"))),
                       eta_seconds=round(eta), updated=now, artifact_directory=str(self.artifacts),
                       done=done, exit_code=self.result.get("exit_code") if done else None)
        try:
            for folder in (self.artifacts, self.job_directory):
                if folder is not None:
                    write_json(folder / "progress.json", payload)
            if current is None and not done:
                write_json(self.artifacts / "summary.json", dict(
                    self.result, partial=True, success=None, artifact_directory=str(self.artifacts),
                    duration=time.monotonic() - self.started))
        except OSError:
            pass   # progress is a convenience; a locked file must not fail the run

    def ensure_heavy(self):
        """One engine run at a time on this machine (see heavy.py). A nested SDK call inherits the
        lock; anything else waits --wait-lock seconds and then refuses."""
        if not hasattr(self, "env") or self.heavy_held or self.env.get(heavy.HELD):
            return
        current = heavy.acquire(f"{self.args.command} {self.result['run_id']}", wait=self.args.wait_lock)
        if current:
            raise ValueError(f"another engine run holds the heavy lock (pid {current.get('pid')}: {current.get('what')}). "
                             "Wait for it (`job wait`), pass --wait-lock SEC, or queue this with `job start`")
        self.heavy_held = True
        self.env[heavy.HELD] = "1"

    def guard_tool(self, script, arguments):
        """A Python tool that launches the engine itself gets the same stale-build guard and heavy
        lock as a direct launch, unless its arguments say it is only reading."""
        try:
            launches = bool(re.search(r"discover_godot|require_godot", Path(script).read_text(encoding="utf-8", errors="replace")))
        except OSError:
            launches = False
        if launches and not READ_ONLY_ARGUMENTS.intersection(arguments):
            self.ensure_fresh()
            self.ensure_heavy()

    def ensure_fresh(self):
        """Stale-binary guard: once per run, before the first engine launch, rebuild when a source
        file is newer than the assembly the engine would load. --no-build opts out."""
        if getattr(self, "fresh_checked", False) or getattr(self.args, "no_build", False):
            return
        self.fresh_checked = True
        reason = stale_reason(ROOT)
        if not reason:
            return
        self.note(f"  stale binary ({reason}); building first. --no-build skips this.")
        self.result["configuration"]["auto_build"] = reason
        if self.process("auto-build", ["dotnet", "build", "Embervale.sln", "--nologo"]).returncode != 0:
            raise BuildFailed("the automatic build failed; the engine was not launched on a stale binary")

    def godot(self, name, arguments, user=(), render=False, scan=True):
        if not self.engine:
            raise ValueError("Godot .NET not found; set EMBERVALE_GODOT or --godot")
        self.ensure_fresh()
        self.ensure_heavy()
        command = [str(self.engine), "--path", str(ROOT), "--log-file", str(self.artifacts / f"{name}.godot.log")]
        if not render:
            command += ["--headless"]
        command += list(arguments)
        if user:
            command += ["--", *user]
        return self.process(name, command, scan=scan)

    def version(self):
        if self.engine:
            r = self.process("godot-version", [str(self.engine), "--version"], timeout=20)
            self.result["godot_version"] = r.stdout.strip()

    def doctor(self):
        """The checks that actually bite, one row each: ok, info, warn or fail. A fail is an error
        diagnostic (exit 2); a warn is advice. Rows land in metrics.doctor and the compact output
        lists everything that is not ok."""
        from quality_common import discover_blender, memory_megabytes, process_table
        rows = []

        def row(level, key, value, code=None):
            rows.append(dict(level=level, key=key, value=str(value)))
            if level == "fail":
                self.issue(code or f"doctor.{key}", f"{key}: {value}")

        version = self.result["godot_version"] or ""
        if not re.match(r"4\.7(?:\.|\b)", version) or "mono" not in version.lower():
            row("fail", "godot", f"Godot 4.7 .NET/Mono required; discovered {version or 'nothing'} "
                                 "(set EMBERVALE_GODOT or --godot to the _console.exe)")
        else:
            row("ok", "godot", f"{version} {self.engine}")
        sdk = self.process("dotnet-sdk", ["dotnet", "--list-sdks"], timeout=30)
        runtimes = self.process("dotnet-runtime", ["dotnet", "--list-runtimes"], timeout=30)
        if not re.search(r"^8\.0\.", sdk.stdout, re.M):
            row("fail", "dotnet", ".NET 8 SDK is not installed")
        elif not re.search(r"Microsoft.NETCore.App 8\.0\.", runtimes.stdout):
            row("fail", "dotnet", ".NET 8 runtime is not installed", "doctor.runtime")
        else:
            row("ok", "dotnet", re.search(r"^8\.0\.\S+", sdk.stdout, re.M).group(0))
        missing = [name for name in ("project.godot", "Embervale.csproj") if not (ROOT / name).is_file()]
        project = (ROOT / "Embervale.csproj").read_text() if not missing else ""
        if missing:
            row("fail", "project", f"required project file missing: {', '.join(missing)}")
        elif "net8.0" not in project or "Godot.NET.Sdk/4.7." not in project:
            row("fail", "project", "Project must target Godot 4.7 and net8.0")
        else:
            row("ok", "project", "net8.0, Godot.NET.Sdk 4.7")

        stale = stale_reason(ROOT)
        row("warn" if stale else "ok", "binary", f"stale: {stale} (the SDK rebuilds before an engine launch; "
            "a raw godot launch would not)" if stale else "Embervale.dll is newer than every source")
        memory = memory_megabytes()
        if memory:
            free, total = memory
            row("fail" if free < 1500 else "warn" if free < 4000 else "ok", "memory",
                f"{free} MB free of {total} MB" + (" (an engine run needs about 3 GB; a bake more)" if free < 4000 else ""))
        table = process_table()
        engines = [(n, p, m) for n, p, m in table if n.lower().startswith("godot")]
        servers = [(n, p, m) for n, p, m in table if re.match(r"(?i)(dotnet|vbcscompiler|msbuild|testhost)", n)]
        other = [(n, p, m) for n, p, m in table if re.match(r"(?i)(gamedev-mcp-server|blender)", n)]
        if engines:
            row("warn", "godot-processes", ", ".join(f"{n} pid {p} {m}MB" for n, p, m in engines)
                + " (an editor is fine; a leftover headless run is not)")
        else:
            row("ok", "godot-processes", "none running")
        server_mb = sum(m for _, _, m in servers)
        row("warn" if server_mb > 1500 else "ok", "build-servers",
            f"{len(servers)} dotnet process(es), {server_mb} MB" + (" (`dotnet build-server shutdown` frees them)" if server_mb > 1500 else ""))
        if other:
            row("info", "helpers", ", ".join(f"{n} pid {p} {m}MB" for n, p, m in other))
        held = heavy.holder()
        row("warn" if held else "ok", "heavy-lock", f"held by pid {held.get('pid')}: {held.get('what')}" if held else "free")
        imported = ROOT / ".godot/imported"
        has_import = imported.is_dir() and next(imported.iterdir(), None) is not None
        row("ok" if has_import else "warn", "import-cache",
            "present" if has_import else "missing: run `embervale.py import` before any engine command")
        journal = ROOT / "artifacts/negative-journal/journal.json"
        if journal.is_file():
            row("fail", "negative-journal", "an interrupted negative battery left mutated data: run "
                                            "`python tools/negative_tests.py --restore`")
        git = self.process("git", ["git", "status", "--porcelain=v1", "-b"], timeout=30)
        if git.returncode == 0:
            lines = git.stdout.splitlines()
            row("ok", "git", f"{lines[0][3:] if lines else '?'}, {max(0, len(lines) - 1)} changed")
        run_count = len(compact.runs(ROOT / "artifacts/headless"))
        row("warn" if run_count > 60 else "ok", "artifacts",
            f"{run_count} run directories" + (" (`embervale.py clean` prunes them)" if run_count > 60 else ""))
        try:
            from godot_mcp_check import check as mcp_check
            mcp = mcp_check(timeout=15)
            row("ok" if mcp["status"] == "pass" else "info", "mcp", f"{mcp['status']}: {mcp['detail']}")
        except Exception as error:   # the MCP is optional tooling; its check must not fail doctor
            row("info", "mcp", f"not checked: {error}")
        row("ok", "python", sys.executable)

        self.result["metrics"]["doctor"] = rows
        self.result["metrics"]["external_tools"] = {
            "blender": str(discover_blender() or "not installed (optional)"),
            "godot_cli": shutil.which("godot-cli"),
            "meshy": "existing offline adoption pipeline; no remote generation configured",
            "export_presets": (ROOT / "export_presets.cfg").is_file()}
        for item in rows:
            if item["level"] != "ok":
                self.brief(f"{item['level']} {item['key']}: {item['value']}")
        self.brief("ok: " + " ".join(item["key"] for item in rows if item["level"] == "ok"))
        if not version or any(d["severity"] == "error" for d in self.result["diagnostics"]):
            self.result["steps"].append(dict(name="prerequisites", exit_code=2))

    def build(self):
        self.process("build", ["dotnet", "build", "Embervale.sln", "--nologo"])

    def import_assets(self):
        if self.args.reimport:
            cache = (ROOT / ".godot/imported").resolve()
            expected = ROOT.resolve() / ".godot/imported"
            if cache != expected or cache.is_symlink():
                raise ValueError("Refusing reimport: imported cache resolves outside the project")
            if cache.exists():
                shutil.rmtree(cache)
        self.godot("import", ["--editor", "--import"])

    def runtime(self, command, plan=None, resolution="1280x720"):
        args = self.args
        if not re.fullmatch(r"[1-9]\d{1,3}x[1-9]\d{1,3}", resolution):
            raise ValueError("resolution must be WIDTHxHEIGHT (10..9999)")
        author_plan = None
        if command == "author":
            from .authoring import validate_author_plan
            author_plan = validate_author_plan(plan)
            plan = dict(steps=[])
        plan = validate_plan(plan or dict(steps=[]))
        name = f"{command}-{len(self.result['steps']):02d}-{resolution}"
        request = dict(command=command, scene=args.scene or plan.get("scene", "res://scenes/Main.tscn"),
                       frames=args.frames, seed=args.seed, artifacts=str(self.artifacts),
                       name=name, steps=plan["steps"], resolution=[int(v) for v in resolution.split("x")],
                       baseline=str(args.baseline.resolve()) if args.baseline else "", threshold=args.threshold)
        if author_plan is not None:
            request["author_plan"] = author_plan
        if args.changed_only and command == "validate":
            paths, full = changed_paths(args.base)
            request.update(changed=paths, full=full)
            self.result["configuration"]["changed_paths"] = paths
            self.result["configuration"]["full_fallback"] = full
        if args.resource:
            if not args.resource.startswith("res://") or ".." in args.resource[6:].split("/") or "\\" in args.resource:
                raise ValueError("resource must be a project-local res:// path")
            request["resource"] = args.resource
        if args.node:
            if not args.node.startswith("/root/") or ".." in args.node.split("/"):
                raise ValueError("node must be an absolute /root/ path")
            request["node"] = args.node
        if args.viewpoint:
            views = json.loads((ROOT / "tools/headless/viewpoints.json").read_text())
            if args.viewpoint not in views:
                raise ValueError(f"unknown viewpoint: {args.viewpoint}")
            request.update(views[args.viewpoint])
        if args.camera:
            request["camera"] = args.camera
        for key in ("position", "rotation"):
            if value := getattr(args, key):
                vector = [float(v) for v in value.split(",")]
                if len(vector) != 3 or not all(math.isfinite(v) for v in vector):
                    raise ValueError(f"{key} requires three finite numbers x,y,z")
                request[key] = vector
        req_path = self.artifacts / f"{name}.request.json"
        write_json(req_path, request)
        render = args.render or command == "screenshot"
        if any(s["op"] == "capture_screenshot" for s in plan["steps"]):
            render = True
        self.godot(name, ["--fixed-fps", "60", "--resolution", resolution, "--script",
                          "res://tools/headless/driver.gd"], ["--request", str(req_path)], render=render)
        report = self.artifacts / f"{name}.result.json"
        if not report.exists():
            self.issue("runtime.incomplete", "Godot did not finish its protocol; inspect the Godot log", path=str(report))
            return
        result = json.loads(report.read_text(encoding="utf-8"))
        self.result["diagnostics"].extend(result.get("diagnostics", []))
        self.result["assertions"].extend(result.get("assertions", []))
        metrics = result.get("metrics", {})
        # The whole-project dependency graph, the scanned path list and the raw frame samples
        # stay in this step's own result file; the summary carries their sizes.
        for bulky in ("dependency_graph", "selected_paths", "samples_ms"):
            if isinstance(metrics.get(bulky), (list, dict)):
                metrics[bulky + "_count"] = len(metrics.pop(bulky))
        self.result["metrics"][name] = metrics
        if command == "perf":
            metrics = result["metrics"]
            limit = args.max_frame_ms
            if args.baseline:
                previous = json.loads(args.baseline.read_text())
                baseline = previous.get("p95_frame_ms")
                if baseline is None:
                    raise ValueError("perf baseline must be a metrics.json containing p95_frame_ms")
                limit = baseline * (1 + args.threshold)
            if limit is not None:
                actual = metrics["p95_frame_ms"]
                self.result["assertions"].append(dict(name="p95 frame budget", success=actual <= limit,
                                                       expected=limit, actual=actual))

    def author(self):
        if not self.args.target:
            raise ValueError("author requires a JSON construction plan")
        destination, previous_hash = None, None
        if self.args.write:
            value = self.args.write
            if "\\" in value or not ((value.startswith("res://scenes/") and value.endswith(".tscn")) or (value.startswith("res://data/") and value.endswith(".tres"))) or value.startswith("res://data/regions/"):
                raise ValueError("author writes scenes/*.tscn or data/*.tres; generated data/regions is protected")
            destination = (ROOT / value[6:]).resolve()
            if not destination.is_relative_to(ROOT.resolve()) or ".." in value[6:].split("/"):
                raise ValueError("author destination escapes the project")
            relative = destination.relative_to(ROOT.resolve())
            if relative.parts[0] not in {"scenes", "data"} or relative.parts[:2] == ("data", "regions"):
                raise ValueError("author destination resolves outside the authoring directories")
            if destination.exists():
                if not self.args.overwrite:
                    raise ValueError("destination exists; --overwrite is required")
                previous_hash = hashlib.sha256(destination.read_bytes()).hexdigest()
        plan = json.loads(Path(self.args.target).read_text(encoding="utf-8"))
        self.runtime("author", plan)
        if exit_code(self.result) != 0:
            return
        metrics = next((m for m in self.result["metrics"].values() if isinstance(m, dict) and "staged_path" in m), None)
        if not metrics:
            self.issue("author.incomplete", "Author did not provide a staged artifact")
            return
        staged = Path(metrics["staged_path"])
        if not staged.resolve().is_relative_to(self.artifacts) or not staged.is_file():
            raise ValueError("author returned an invalid staged path")
        if destination:
            if staged.suffix != destination.suffix:
                raise ValueError("destination extension does not match the authored resource")
            actual_hash = hashlib.sha256(destination.read_bytes()).hexdigest() if destination.exists() else None
            if actual_hash != previous_hash:
                raise ValueError("destination changed during authoring; refusing overwrite")
            destination.parent.mkdir(parents=True, exist_ok=True)
            if destination.exists():
                shutil.copy2(destination, self.artifacts / ("before" + destination.suffix))
            temporary = destination.with_name(destination.name + ".sdk-" + self.result["run_id"] + ".tmp")
            try:
                shutil.copyfile(staged, temporary)
                temporary.replace(destination)
            finally:
                temporary.unlink(missing_ok=True)
            self.result["metrics"]["published"] = dict(path=self.args.write, previous_sha256=previous_hash,
                                                      sha256=hashlib.sha256(destination.read_bytes()).hexdigest())

    def validate(self):
        self.runtime("validate")
        # Cross-database semantic invariants are global, even in changed-only mode.
        self.godot("content", [], ["--validate"])

    def tests(self):
        """Both suites, or a focused part: --only tool|game, --filter EXPR (xUnit), --py PATTERN
        (unittest -k). A filter for one suite alone runs only that suite. Failed tests are named
        in the result with their first message line (code test.failed)."""
        from .testing import python_report, trx_report
        only, expression, pattern = (getattr(self.args, n, None) for n in ("only", "filter", "py"))
        tool = only == "tool" or (only is None and not (expression and not pattern))
        game = only == "game" or (only is None and not (pattern and not expression))
        counts = {}
        if tool:
            command = [sys.executable, "-m", "unittest", "discover", "-s", "tools", "-p", "test_*.py"]
            # unittest prints failures as `ERROR: name (id)`; they are extracted by name below
            # rather than scanned as anonymous log errors.
            r = self.process("tool-tests", command + (["-k", pattern] if pattern else []), scan=False)
            report = python_report(r.stderr + r.stdout)
            for test, message in report["failures"]:
                self.issue("test.failed", f"{test}: {message}", path=test)
            counts["tool"] = dict(ran=report["ran"], failed=len(report["failures"]))
        if game:
            trx = self.artifacts / "tests" / "game-tests.trx"
            command = ["dotnet", "test", "tests/Embervale.Tests", "--nologo", "--logger",
                       "trx;LogFileName=game-tests.trx", "--results-directory", str(trx.parent)]
            self.process("game-tests", command + (["--filter", expression] if expression else []))
            report = trx_report(trx)
            if report:
                for test, message in report["failures"]:
                    self.issue("test.failed", f"{test}: {message}", path=test)
                counts["game"] = {k: report[k] for k in ("passed", "failed", "skipped")}
        self.result["metrics"]["tests"] = counts
        self.brief("tests " + "; ".join(f"{suite}: " + ", ".join(f"{v} {k}" for k, v in c.items() if v is not None)
                                        for suite, c in counts.items()))

        if self.args.engine_tests:
            self.env["EMBERVALE_RENDER_TESTS"] = "1" if self.args.render else "0"
            # Every test in there launches the engine through a nested SDK call: build once and
            # hold the heavy lock here, so those calls inherit both.
            self.ensure_fresh()
            self.ensure_heavy()
            self.process("native-protocol-tests", [sys.executable, "tools/sdk_engine_tests.py"])

    def world(self, mode=None, skip=()):
        from world_quality_check import gates, REGIONS
        mode = mode or self.args.mode
        registry = gates(str(self.engine) if self.engine else None)
        selected = set(self.args.gate or ())
        if selected:
            if self.args.command != "world":
                raise ValueError("--gate applies only to the world command")
            available = {gate.name for gate in registry if mode in gate.modes and not (mode == "fast" and gate.slow)}
            unavailable = selected - available
            if unavailable:
                raise ValueError(f"Unavailable gates for {mode}: {', '.join(sorted(unavailable))}")
        items = []
        for gate in registry:
            if selected and gate.name not in selected:
                continue
            if gate.name in skip or mode not in gate.modes or (mode == "fast" and gate.slow):
                continue
            regions = [self.args.region] if self.args.region else sorted(REGIONS)
            items += [(gate, region) for region in (regions if gate.per_region else [None])]
        if self.args.command == "world" and hasattr(self, "result"):
            self.result["label"] = f"world/{mode}"
        self.run_gates(items)

    def run_gates(self, items):
        """Runs (gate, region) pairs in the given order. Neighbouring pure-Python gates run side by
        side (--parallel), a gate that already passed on these exact inputs is reused from the
        cache (--no-cache), and the steps still to come feed the progress file's ETA."""
        from world_quality_check import REGIONS
        prepared = []
        for gate, region in items:
            command = [str(ROOT / REGIONS[region]) if p == "@REGION@" else
                       p.replace("@ARTIFACT@", str(self.artifacts)) for p in gate.command]
            prepared.append((gate, gate.name + ("-" + region if region else ""), command))
        self.planned = getattr(self, "planned", []) + [name for _, name, _ in prepared]
        self.plan_known = True
        self.engine_steps = {name for gate, name, _ in prepared if self.launches_engine(gate)}
        batch = []
        for gate, name, command in prepared:
            if gate.parallel and self.args.parallel > 1:
                batch.append((gate, name, command))
                continue
            self.run_batch(batch)
            self.run_gate(gate, name, command)
        self.run_batch(batch)

    def launches_engine(self, gate):
        return bool(self.engine) and gate.command[:1] == [str(self.engine)]

    def run_gate(self, gate, name, command):
        if not self.engine and any("godot" in p.lower() for p in gate.command[:1]):
            raise ValueError("This world mode needs Godot .NET")
        key = self.gate_key(gate, command)
        if self.reuse(name, command, key):
            return
        if self.launches_engine(gate) or gate.name in ENGINE_INSIDE:
            # These gates launch the engine themselves, not through Run.godot. After the
            # `build` gate the freshness check finds nothing to do.
            self.ensure_fresh()
            self.ensure_heavy()
        self.process(name, command, timeout=gate.timeout, expected_errors=gate.expected_errors)
        self.remember(name, key)

    def run_batch(self, batch):
        """A run of neighbouring pure-Python gates: executed together, recorded in registry order."""
        todo = []
        for gate, name, command in batch:
            key = self.gate_key(gate, command)
            if not self.reuse(name, command, key):
                todo.append((gate, name, command, key))
        batch.clear()
        if len(todo) == 1:
            gate, name, command, key = todo[0]
            self.process(name, command, timeout=gate.timeout, expected_errors=gate.expected_errors)
            self.remember(name, key)
        elif todo:
            labels = [self.reserve(name) for _, name, _, _ in todo]
            self.progress(todo[0][1], labels[0])
            with ThreadPoolExecutor(max_workers=min(self.args.parallel, len(todo))) as pool:
                futures = [pool.submit(self.execute, name, label, command, gate.timeout, True, gate.expected_errors)
                           for (gate, name, command, _), label in zip(todo, labels)]
                for (_, name, _, key), future in zip(todo, futures):
                    self.record(future.result())
                    self.remember(name, key)

    def gate_key(self, gate, command):
        """The cache key for this gate now, or None when it must simply run: no cache in play, a
        gate judged by eye, --no-build (the assembly may not match the sources), not a git checkout."""
        if getattr(self, "cache", None) is None or not gate.cacheable or self.args.no_build:
            return None
        if self.tree is None:
            self.tree = gate_cache.tree_state(ROOT) or False
        if not self.tree:
            return None
        return gate_cache.key(self.tree, gate.inputs, [*command, self.env.get("EMBERVALE_SEED"), self.env.get("EMBERVALE_FRAMES")])

    def reuse(self, name, command, key):
        """Records a cached pass instead of running the gate. False when there is none to reuse."""
        entry = self.cache.get(name, key) if key and not self.args.no_cache else None
        if not entry:
            return False
        self.add_step(dict(name=name, command=[str(c) for c in command], duration=0.0, exit_code=0,
                           process_exit_code=0, success=True, cached=True, cached_run=entry.get("run"),
                           cached_duration=entry.get("duration")))
        return True

    def remember(self, name, key):
        """Stores a pass. A failure is never cached."""
        step = self.result["steps"][-1] if key and self.result["steps"] else None
        if step and step["name"] == name and step.get("success") and step.get("exit_code") == 0:
            try:
                self.cache.put(name, key, step["duration"], self.result["run_id"])
            except OSError:
                pass   # an unwritable cache costs a rerun later; it must not fail a gate that passed

    def finish(self):
        grouped = {}
        for item in self.result["diagnostics"]:
            key = (item["severity"], item["code"], item.get("path"), item.get("node"), item["message"])
            if key in grouped:
                grouped[key]["count"] += item.get("count", 1)
            else:
                grouped[key] = dict(item, count=item.get("count", 1))
        self.result["diagnostics"] = list(grouped.values())
        self.result["duration"] = time.monotonic() - self.started
        warning_count = sum(d.get("count", 1) for d in self.result["diagnostics"] if d["severity"] == "warning")
        self.result["metrics"]["warning_count"] = warning_count
        if self.args.max_warnings is not None and warning_count > self.args.max_warnings:
            self.issue("warnings.budget", f"{warning_count} warnings exceed budget {self.args.max_warnings}")
        code = exit_code(self.result, self.args.strict)
        self.result.update(success=code == 0, exit_code=code)
        if self.heavy_held:
            heavy.release()
            self.heavy_held = False
        if self.history_changed:
            try:
                costs.save({**costs.load(), **self.history})
            except OSError:
                pass
        self.planned = []
        self.result["artifacts"] = sorted({str(p.relative_to(self.artifacts)) for p in self.artifacts.rglob("*") if p.is_file()}
                                          | {"summary.json", "summary.txt"})
        self.result["artifact_directory"] = str(self.artifacts)
        write_json(self.artifacts / "summary.json", self.result)
        summary = compact.text(self.result)
        (self.artifacts / "summary.txt").write_text(summary + "\n", encoding="utf-8")
        self.progress(done=True)
        # Compact by default: failures with their logs and one verdict line. The whole result is
        # summary.json, or --json-full.
        if self.args.json_full:
            print(json.dumps(self.result, ensure_ascii=False))
        elif self.args.json:
            print(json.dumps(compact.compact(self.result), ensure_ascii=False))
        elif self.args.ndjson:
            print(json.dumps(dict(event="result", **compact.compact(self.result)), ensure_ascii=False))
        else:
            print(summary)
        return code


def _runtime(run, args, passthrough):
    cmd = args.command
    plan = json.loads(Path(args.target).read_text(encoding="utf-8")) if args.target else None
    if cmd == "scenario" and plan is None:
        raise ValueError("scenario requires a JSON plan")
    for resolution in args.resolution or ["1280x720"]:
        run.runtime(cmd, plan, resolution)


def _audit(run, args, passthrough):
    cmd = args.command
    run.doctor()
    if exit_code(run.result) == 0:
        run.build()
        run.import_assets()
        run.validate()
        run.tests()
        run.world("engine", skip={"build", "tests", "content"})
        run.guard_tool(ROOT / "tools/assets.py", ["validate"])
        run.process("assets", [sys.executable, "tools/assets.py", "validate"])
        run.runtime("scenario", json.loads((ROOT / "tools/headless/scenarios/new-game.json").read_text()))
        if cmd == "all" and args.render:
            run.world("visual")
            run.runtime("perf")
        elif cmd == "all":
            run.issue("render.not_requested", "Rendering gates require all --render; headless gates were selected", "info")


def _assets(run, args, passthrough):
    arguments = [args.target or "status", *passthrough]
    run.guard_tool(ROOT / "tools/assets.py", arguments)
    run.process("assets", [sys.executable, "tools/assets.py", *arguments])


def _list(run, args, passthrough):
    """What exists. Names only by default; `list --json` is the whole inventory with descriptions."""
    from world_quality_check import gates
    from .scenario import OPERATIONS as runtime_operations, METHODS, PROPERTIES
    from .authoring import OPERATIONS as author_operations, NODE_TYPES, RESOURCE_TYPES
    registry = gates(None)
    tools = sorted(p.stem for p in (ROOT / "tools").iterdir() if p.suffix in {".py", ".gd"})
    if args.json or args.json_full:
        print(json.dumps(dict(
            capabilities=dict(commands=tuple(REGISTRY), runtime_operations=sorted(runtime_operations),
                              methods=sorted(METHODS), mutable_properties=sorted(PROPERTIES),
                              author_operations=sorted(author_operations), node_types=sorted(NODE_TYPES),
                              resource_types=sorted(RESOURCE_TYPES), schema=SCHEMA),
            commands={name: entry.help for name, entry in REGISTRY.items()},
            gates=[dict(name=g.name, description=g.what, modes=g.modes) for g in registry], tools=tools)))
        return 0
    print("commands: " + " ".join(REGISTRY))
    shown = set()
    for mode in ("fast", "engine", "visual", "performance", "full"):
        names = [g.name for g in registry if mode in g.modes and not (mode == "fast" and g.slow) and g.name not in shown]
        shown.update(names)
        if names:
            print(f"gates first reached in {mode}: " + " ".join(names))
    print(f"tools: {len(tools)} under tools/ (`list --json` names them, with gate descriptions and the scenario protocol)")
    return 0


def _tool(run, args, passthrough):
    if not args.target or not re.fullmatch(r"[a-z0-9_]+", args.target) or args.target in {"embervale", "world_quality_check", "quality_common"}:
        raise ValueError("tool requires an existing specialist tool name (see list)")
    py, gd = ROOT / f"tools/{args.target}.py", ROOT / f"tools/{args.target}.gd"
    if py.is_file():
        run.guard_tool(py, passthrough)
        run.process(args.target, [sys.executable, str(py), *passthrough])
    elif gd.is_file():
        run.godot(args.target, ["--script", f"res://tools/{gd.name}"], passthrough, render=args.render)
    else:
        raise ValueError("unknown tool")


# The original commands. A new one does not go here: it is a module under commands/ (see its docstring).
commands.register("doctor", lambda run, args, passthrough: run.doctor())
commands.register("import", lambda run, args, passthrough: run.import_assets())
commands.register("build", lambda run, args, passthrough: run.build())
commands.register("validate", lambda run, args, passthrough: run.validate())
for _name in ("inspect", "smoke", "scenario", "screenshot", "perf"):
    commands.register(_name, _runtime)
def _test_arguments(group):
    group.add_argument("--only", choices=("tool", "game"), help="run one suite: the Python tool tests or the xUnit game tests")
    group.add_argument("--filter", help="xUnit filter expression, e.g. FullyQualifiedName~HeadlessTooling (game suite only)")
    group.add_argument("--py", help="unittest -k pattern, e.g. FreshnessTests (tool suite only)")


commands.register("test", lambda run, args, passthrough: run.tests(), version=False, arguments=_test_arguments)
commands.register("audit", _audit)
commands.register("all", _audit)
commands.register("world", lambda run, args, passthrough: run.world())
commands.register("assets", _assets, version=False, passthrough=True)
commands.register("tool", _tool, version=False, passthrough=True)
commands.register("list", _list, version=False, light=True)
commands.register("author", lambda run, args, passthrough: run.author())
commands.discover()
COMMANDS = tuple(REGISTRY)


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    passthrough = []
    # A command's own flags are only known once the command is: it is the first registered name.
    first = next((i for i, a in enumerate(argv) if a in REGISTRY), None)
    if first is not None and tuple(argv[first:first + 2]) in REST_AFTER:
        argv, passthrough = argv[:first + 2], argv[first + 2:]
        if passthrough[:1] == ["--"]:
            passthrough = passthrough[1:]
    elif "--" in argv:
        separator = argv.index("--")
        argv, passthrough = argv[:separator], argv[separator + 1:]
    args = parser(argv[first] if first is not None and first < len(argv) else None).parse_args(argv)
    entry = REGISTRY[args.command]
    if not entry.light:
        return run_command(args, passthrough, entry)
    try:
        return int(entry.run(None, args, passthrough) or 0)
    except KeyboardInterrupt:
        return 130
    except (ValueError, OSError, RuntimeError) as error:
        print(json.dumps(dict(success=False, command=args.command, exit_code=2, error=str(error))))
        return 2


def run_command(args, passthrough, entry, body=None):
    """One full run: its directory, the command (or `body` in its place), the summary and the exit
    code. A light command calls this when it turns out to need a real run."""
    run = None
    try:
        run = Run(args)
        run.note(f"Embervale SDK — {args.command} — {run.artifacts.name}")
        if entry.version:
            run.version()
        cmd = args.command
        if (args.write or args.overwrite) and cmd != "author":
            raise ValueError("--write/--overwrite apply only to author")
        if args.changed_only and cmd not in {"validate", "audit", "all"}:
            raise ValueError("--changed-only is supported by validate/audit/all")
        if args.gate and cmd != "world":
            raise ValueError("--gate applies only to the world command")
        if passthrough and not entry.passthrough:
            raise ValueError("Arguments after -- are supported only by " + "/".join(n for n, c in REGISTRY.items() if c.passthrough))
        (body or entry.run)(run, args, passthrough)
        return run.finish()
    except KeyboardInterrupt:
        if run:
            run.result["steps"].append(dict(name="interrupted", exit_code=130))
            return run.finish()
        return 130
    except BuildFailed as error:
        run.issue("build.stale", str(error))
        return run.finish()
    except (ValueError, OSError, RuntimeError) as error:
        if run:
            run.issue("configuration.invalid", str(error))
            run.result["steps"].append(dict(name="configuration", exit_code=2))
            return run.finish()
        print(json.dumps(dict(success=False, command=args.command, exit_code=2, error=str(error))))
        return 2
