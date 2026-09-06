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
import time
import uuid
from pathlib import Path

from quality_common import ROOT, discover_godot, run_process, write_json, machine_fingerprint
from .contract import SCHEMA, diagnostic, diagnostics_from_log, exit_code
from .changed import changed_paths
from .scenario import validate_plan, resource_path

COMMANDS = ("doctor", "import", "build", "validate", "inspect", "smoke", "scenario", "test",
            "screenshot", "perf", "audit", "all", "world", "assets", "tool", "list", "report", "author")


def parser():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("command", choices=COMMANDS)
    p.add_argument("target", nargs="?", help="scenario JSON, tool name, or asset subcommand")
    p.add_argument("--scene", type=resource_path)
    p.add_argument("--timeout", type=float, default=None, help="hard per-process seconds (including descendants)")
    p.add_argument("--frames", type=int, help="runtime frames (default 120); explicitly overrides specialist capture waits")
    p.add_argument("--seed", type=int, default=12345)
    p.add_argument("--json", action="store_true", help="only the result JSON on stdout")
    p.add_argument("--ndjson", action="store_true", help="stream step events and a final result")
    p.add_argument("--artifacts", type=Path, help="parent directory; a unique run directory is always created")
    p.add_argument("--strict", action="store_true", help="warnings fail the run")
    p.add_argument("--changed-only", action="store_true")
    p.add_argument("--base", help="include changes since this merge base as well as staged/unstaged/untracked")
    p.add_argument("--godot", type=Path)
    p.add_argument("--mode", choices=("fast", "engine", "visual", "performance", "full"), default="engine")
    p.add_argument("--region", choices=("ember_crown", "frostfang_reach"))
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
        self.result = dict(schema=SCHEMA, run_id=run_id, command=args.command, success=False,
                           duration=0, godot_version=None, diagnostics=[], assertions=[], metrics={},
                           artifacts=[], steps=[], machine=machine_fingerprint(),
                           configuration=dict(seed=args.seed, frames=args.frames, timeout=self.timeout,
                                              changed_only=args.changed_only, strict=args.strict))

    def note(self, message):
        if not self.args.json and not self.args.ndjson:
            print(message, flush=True)

    def issue(self, code, message, severity="error", path=None):
        self.result["diagnostics"].append(diagnostic(severity, code, message, path))

    def process(self, name, command, timeout=None, scan=True, expected_errors=()):
        self.note(f"  {name} ...")
        r = run_process(command, cwd=ROOT, timeout=min(timeout or self.timeout, self.timeout), env=self.env)
        label = f"{len(self.result['steps']):02d}-{name}"
        (self.artifacts / f"{label}.stdout.log").write_text(r.stdout, encoding="utf-8")
        (self.artifacts / f"{label}.stderr.log").write_text(r.stderr, encoding="utf-8")
        if r.launch_error:
            code = 2
            self.issue("process.launch", r.launch_error)
        elif r.timed_out:
            code = 3
            self.issue("process.timeout", f"{name} exceeded deadline; process tree terminated")
        elif r.returncode < 0 or r.returncode > 128:
            code = 4
            self.issue("process.crash", f"{name} exited abnormally: {r.returncode}")
        else:
            nested_sdk = len(command) > 1 and Path(str(command[1])).name in {"embervale.py", "assets.py", "world_quality_check.py"}
            code = r.returncode if nested_sdk and r.returncode in {0, 1, 2, 3, 4, 5, 130} else (1 if r.returncode else 0)
        found = diagnostics_from_log(r.output, name) if scan else []
        if r.returncode == 0:
            for item in found:
                if any(re.search(pattern, item["message"]) for pattern in expected_errors):
                    item.update(severity="info", code="fixture.expected_error")
        self.result["diagnostics"].extend(found)
        if code and not found:
            self.issue("process.failed", f"{name}: exit {r.returncode}; see {label}.stderr.log and stdout log")
        step = dict(name=name, command=[str(c) for c in command], duration=r.elapsed_seconds,
                    exit_code=code, process_exit_code=r.returncode,
                    success=code == 0 and not any(d["severity"] == "error" for d in found),
                    stdout=f"{label}.stdout.log", stderr=f"{label}.stderr.log")
        self.result["steps"].append(step)
        self.note(f"  {name}: {'PASS' if step['success'] else 'FAIL'} ({r.elapsed_seconds:.1f}s)")
        if self.args.ndjson:
            print(json.dumps(dict(event="step", **step)), flush=True)
        return r

    def godot(self, name, arguments, user=(), render=False, scan=True):
        if not self.engine:
            raise ValueError("Godot .NET not found; set EMBERVALE_GODOT or --godot")
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
        version = self.result["godot_version"] or ""
        if not re.match(r"4\.7(?:\.|\b)", version) or "mono" not in version.lower():
            self.issue("doctor.godot", f"Godot 4.7 .NET/Mono required; discovered {version or 'nothing'}")
        sdk = self.process("dotnet-sdk", ["dotnet", "--list-sdks"], timeout=30)
        runtimes = self.process("dotnet-runtime", ["dotnet", "--list-runtimes"], timeout=30)
        if not re.search(r"^8\.0\.", sdk.stdout, re.M):
            self.issue("doctor.dotnet", ".NET 8 SDK is not installed")
        if not re.search(r"Microsoft.NETCore.App 8\.0\.", runtimes.stdout):
            self.issue("doctor.runtime", ".NET 8 runtime is not installed")
        for name in ("project.godot", "Embervale.csproj"):
            if not (ROOT / name).is_file():
                self.issue("doctor.project", "Required project file missing", path=name)
        project = (ROOT / "Embervale.csproj").read_text()
        if "net8.0" not in project or "Godot.NET.Sdk/4.7." not in project:
            self.issue("doctor.project", "Project must target Godot 4.7 and net8.0")
        self.process("git", ["git", "--version"], timeout=20)
        from quality_common import discover_blender
        self.result["metrics"]["external_tools"] = {
            "blender": str(discover_blender() or "not installed (optional)"),
            "godot_cli": shutil.which("godot-cli"),
            "meshy": "existing offline adoption pipeline; no remote generation configured",
            "export_presets": (ROOT / "export_presets.cfg").is_file()}
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
        self.result["metrics"][name] = result.get("metrics", {})
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
        self.process("tool-tests", [sys.executable, "-m", "unittest", "discover", "-s", "tools", "-p", "test_*.py"])
        self.process("game-tests", ["dotnet", "test", "tests/Embervale.Tests", "--nologo", "--logger",
                                    "trx;LogFileName=game-tests.trx", "--results-directory", str(self.artifacts / "tests")])

        if self.args.engine_tests:
            self.env["EMBERVALE_RENDER_TESTS"] = "1" if self.args.render else "0"
            self.process("native-protocol-tests", [sys.executable, "tools/sdk_engine_tests.py"])

    def world(self, mode=None, skip=()):
        from world_quality_check import gates, REGIONS
        mode = mode or self.args.mode
        for gate in gates(str(self.engine) if self.engine else None):
            if gate.name in skip or mode not in gate.modes or (mode == "fast" and gate.slow):
                continue
            if not self.engine and any("godot" in p.lower() for p in gate.command[:1]):
                raise ValueError("This world mode needs Godot .NET")
            regions = [self.args.region] if self.args.region else sorted(REGIONS)
            for region in regions if gate.per_region else [None]:
                command = [str(ROOT / REGIONS[region]) if p == "@REGION@" else
                           p.replace("@ARTIFACT@", str(self.artifacts)) for p in gate.command]
                self.process(gate.name + ("-" + region if region else ""), command, timeout=gate.timeout, expected_errors=gate.expected_errors)

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
        self.result["artifacts"] = [str(p.relative_to(self.artifacts)) for p in sorted(self.artifacts.rglob("*")) if p.is_file()]
        self.result["artifacts"].append("summary.json")
        self.result["artifact_directory"] = str(self.artifacts)
        write_json(self.artifacts / "summary.json", self.result)
        if self.args.json:
            print(json.dumps(self.result, ensure_ascii=False))
        elif self.args.ndjson:
            print(json.dumps(dict(event="result", **self.result), ensure_ascii=False))
        else:
            errors = [d for d in self.result["diagnostics"] if d["severity"] == "error"]
            other = [d for d in self.result["diagnostics"] if d["severity"] != "error"]
            for d in errors + other[-10:]:
                origin = " ".join(str(d[k]) for k in ("path", "node") if d.get(k))
                print(f"{d['severity'].upper()} {d['code']}: {d['message']}" + (f" [{origin}]" if origin else ""))
            print(f"{'PASS' if code == 0 else 'FAIL'} {self.args.command} ({self.result['duration']:.1f}s), exit {code}")
            print(f"Evidence: {self.artifacts / 'summary.json'}")
        return code


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    passthrough = []
    if "--" in argv:
        separator = argv.index("--")
        argv, passthrough = argv[:separator], argv[separator + 1:]
    args = parser().parse_args(argv)
    run = None
    try:
        run = Run(args)
        run.note(f"Embervale SDK — {args.command} — {run.artifacts.name}")
        if args.command not in {"list", "tool", "test", "assets"}:
            run.version()
        cmd = args.command
        if (args.write or args.overwrite) and cmd != "author":
            raise ValueError("--write/--overwrite apply only to author")
        if args.changed_only and cmd not in {"validate", "audit", "all"}:
            raise ValueError("--changed-only is supported by validate/audit/all")
        if passthrough and cmd not in {"assets", "tool"}:
            raise ValueError("Arguments after -- are supported only by assets/tool")
        if cmd == "doctor": run.doctor()
        elif cmd == "build": run.build()
        elif cmd == "import": run.import_assets()
        elif cmd == "validate": run.validate()
        elif cmd == "test": run.tests()
        elif cmd == "author": run.author()
        elif cmd in {"inspect", "smoke", "screenshot", "perf", "scenario"}:
            plan = json.loads(Path(args.target).read_text(encoding="utf-8")) if args.target else None
            if cmd == "scenario" and plan is None:
                raise ValueError("scenario requires a JSON plan")
            for resolution in args.resolution or ["1280x720"]:
                run.runtime(cmd, plan, resolution)
        elif cmd == "report":
            if args.target not in {"state", "economy", "worldgen", "lifecycle"}:
                raise ValueError("report requires state, economy, worldgen or lifecycle")
            run.godot(args.target, [], ["--" + args.target])
        elif cmd == "world": run.world()
        elif cmd in {"audit", "all"}:
            run.doctor()
            if exit_code(run.result) == 0:
                run.build()
                run.import_assets()
                run.validate()
                run.tests()
                run.world("engine", skip={"build", "tests", "content"})
                run.process("assets", [sys.executable, "tools/assets.py", "validate"])
                run.runtime("scenario", json.loads((ROOT / "tools/headless/scenarios/new-game.json").read_text()))
                if cmd == "all" and args.render:
                    run.world("visual")
                    run.runtime("perf")
                elif cmd == "all":
                    run.issue("render.not_requested", "Rendering gates require all --render; headless gates were selected", "info")
        elif cmd == "assets":
            run.process("assets", [sys.executable, "tools/assets.py", args.target or "status", *passthrough])
        elif cmd == "list":
            from world_quality_check import gates
            from .scenario import OPERATIONS as runtime_operations, METHODS, PROPERTIES
            from .authoring import OPERATIONS as author_operations, NODE_TYPES, RESOURCE_TYPES
            run.result["metrics"]["capabilities"] = dict(commands=COMMANDS, runtime_operations=sorted(runtime_operations),
                methods=sorted(METHODS), mutable_properties=sorted(PROPERTIES), author_operations=sorted(author_operations),
                node_types=sorted(NODE_TYPES), resource_types=sorted(RESOURCE_TYPES), schema=SCHEMA)
            run.result["metrics"]["gates"] = [dict(name=g.name, description=g.what, modes=g.modes) for g in gates(None)]
            run.result["metrics"]["tools"] = sorted(p.stem for p in (ROOT / "tools").iterdir() if p.suffix in {".py", ".gd"})
            run.note(json.dumps(run.result["metrics"], indent=2))
        elif cmd == "tool":
            if not args.target or not re.fullmatch(r"[a-z0-9_]+", args.target) or args.target in {"embervale", "world_quality_check", "quality_common"}:
                raise ValueError("tool requires an existing specialist tool name (see list)")
            py, gd = ROOT / f"tools/{args.target}.py", ROOT / f"tools/{args.target}.gd"
            if py.is_file():
                run.process(args.target, [sys.executable, str(py), *passthrough])
            elif gd.is_file():
                run.godot(args.target, ["--script", f"res://tools/{gd.name}"], passthrough, render=args.render)
            else:
                raise ValueError("unknown tool")
        return run.finish()
    except KeyboardInterrupt:
        if run:
            run.result["steps"].append(dict(name="interrupted", exit_code=130))
            return run.finish()
        return 130
    except (ValueError, OSError, RuntimeError) as error:
        if run:
            run.issue("configuration.invalid", str(error))
            run.result["steps"].append(dict(name="configuration", exit_code=2))
            return run.finish()
        print(json.dumps(dict(success=False, command=args.command, exit_code=2, error=str(error))))
        return 2
