"""Tests for the SDK's agent tooling: compact output, timeouts, cache, parallel gates, the heavy
lock, background jobs, log triage, verify, focused tests, doctor parts, the negative journal and
artifact pruning. No engine, renderer or network required."""
import contextlib
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import time
import types
import unittest
from unittest.mock import Mock, patch

from quality_common import ProcessResult, parse_tasklist, pid_alive, run_process, write_json
from embervale_sdk import cache, compact, costs, heavy, jobs, triage
from embervale_sdk.cli import Run, main, parser
from embervale_sdk.contract import diagnostics_from_log
from embervale_sdk.testing import python_report, trx_report
from world_quality_check import Gate, REGIONS, gates

PY = sys.executable


def make_run(folder, arguments=("world",), engine=None):
    """A real Run in a temp directory that touches nothing under the project's artifacts/."""
    with patch("embervale_sdk.cli.discover_godot", return_value=engine):
        run = Run(parser(arguments[0]).parse_args([*arguments, "--artifacts", str(folder)]))
    run.history = {}
    run.cache = cache.GateCache(Path(folder))
    run.tree = dict(entries={"src": "1"}, changed={})
    run.read_tree = lambda: run.tree   # the temp folder is not a checkout; the fake tree never changes
    return run


def finish(run):
    out = io.StringIO()
    with patch("embervale_sdk.cli.costs.save"), contextlib.redirect_stdout(out):
        code = run.finish()
    return code, out.getvalue()


def python_gate(name, code="pass", **options):
    return Gate(name, "fixture", [PY, "-c", code], **options)


class TimeoutTests(unittest.TestCase):
    def limit(self, arguments, step_timeout):
        with tempfile.TemporaryDirectory() as folder:
            run = make_run(folder, arguments)
            result = ProcessResult(["x"], 0, 0.1, "", "")
            with patch("embervale_sdk.cli.run_process", return_value=result) as launched:
                run.execute("step", "00-step", ["x"], timeout=step_timeout)
            return launched.call_args.kwargs["timeout"]

    def test_a_gate_keeps_its_own_longer_timeout(self):
        self.assertEqual(1800, self.limit(["world"], 1800))  # was clamped to the 900 s default

    def test_default_applies_when_the_step_names_none(self):
        self.assertEqual(900, self.limit(["world"], None))

    def test_an_explicit_timeout_caps_and_also_sets_the_default(self):
        self.assertEqual(60, self.limit(["world", "--timeout", "60"], 1800))
        self.assertEqual(1200, self.limit(["world", "--timeout", "1200"], None))
        self.assertEqual(600, self.limit(["world", "--timeout", "1200"], 600))

    def test_registry_long_gates_still_declare_long_timeouts(self):
        declared = {gate.name: gate.timeout for gate in gates(None)}
        self.assertEqual((1800, 3600, 1200), (declared["traversal"], declared["negative"], declared["story"]))


class StreamingTests(unittest.TestCase):
    def test_output_goes_to_the_files_and_to_the_result(self):
        with tempfile.TemporaryDirectory() as folder:
            out, err = Path(folder) / "o.log", Path(folder) / "sub" / "e.log"
            result = run_process([PY, "-c", "import sys; print('to out λ'); print('to err', file=sys.stderr)"],
                                 timeout=20, stdout_path=out, stderr_path=err)
            self.assertEqual(("to out λ", "to err"), (result.stdout.strip(), result.stderr.strip()))
            self.assertEqual("to out λ", out.read_text(encoding="utf-8").strip())
            self.assertEqual("to err", err.read_text(encoding="utf-8").strip())

    def test_a_timed_out_step_leaves_its_partial_log(self):
        with tempfile.TemporaryDirectory() as folder:
            out = Path(folder) / "o.log"
            result = run_process([PY, "-u", "-c", "import time; print('alive'); time.sleep(60)"], timeout=2, stdout_path=out)
            self.assertTrue(result.timed_out)
            self.assertIn("alive", result.stdout)
            self.assertIn("alive", out.read_text(encoding="utf-8"))


class CompactTests(unittest.TestCase):
    RESULT = dict(schema=1, run_id="r", command="world", success=False, exit_code=1, duration=125.4,
                  artifact_directory="X:/runs/r", assertions=[dict(name="p95", success=False, expected=5, actual=9)],
                  steps=[dict(name="build", exit_code=0, success=True, stdout="00-build.stdout.log"),
                         dict(name="content", exit_code=1, success=False, stdout="01-content.stdout.log"),
                         dict(name="tests", exit_code=0, success=True, cached=True)],
                  diagnostics=[dict(severity="error", code="process.content", message="ERROR: broken shop " + "x" * 400, count=3),
                               dict(severity="error", code="process.content", message="ERROR: second"),
                               dict(severity="error", code="configuration.invalid", message="bad flag"),
                               dict(severity="warning", code="process.build", message="WARNING: w", count=4)])

    def test_failures_carry_first_error_and_log_and_the_rest_is_counts(self):
        c = compact.compact(self.RESULT)
        self.assertEqual((3, 1, 4), (c["steps"], c["cached"], c["warnings"]))
        self.assertEqual(1, len(c["failed"]))
        row = c["failed"][0]
        self.assertEqual(("content", 1, 4), (row["step"], row["exit_code"], row["errors"]))
        self.assertLessEqual(len(row["message"]), compact.MESSAGE)
        self.assertTrue(row["log"].endswith("01-content.stdout.log"))
        self.assertEqual(["configuration.invalid"], [e["code"] for e in c["errors"]])
        self.assertEqual("p95", c["assertions_failed"][0]["name"])
        self.assertLess(len(json.dumps(c)), 1500)

    def test_text_is_failures_then_one_verdict_line(self):
        lines = compact.text(self.RESULT).splitlines()
        self.assertTrue(lines[0].startswith("FAIL content exit=1 ERROR: broken shop"))
        self.assertTrue(lines[-1].startswith("FAIL world 3 steps (1 cached) 2m05s exit=1 warnings=4 evidence="))

    def test_a_passing_run_is_one_line(self):
        result = dict(self.RESULT, success=True, exit_code=0, diagnostics=[], assertions=[],
                      steps=[dict(name="build", exit_code=0, success=True)], label="world/fast")
        self.assertEqual(1, len(compact.text(result).splitlines()))
        self.assertTrue(compact.text(result).startswith("PASS world/fast 1 steps 2m05s evidence="))

    def test_many_failures_are_bounded(self):
        steps = [dict(name=f"g{i}", exit_code=1, success=False) for i in range(50)]
        c = compact.compact(dict(self.RESULT, steps=steps, diagnostics=[]))
        self.assertEqual((compact.MAX_FAILED, 30), (len(c["failed"]), c["failed_more"]))

    def test_run_output_modes(self):
        with tempfile.TemporaryDirectory() as folder:
            run = make_run(folder, ["world", "--json"])
            run.process("ok", [PY, "-c", "print('fine')"])
            run.process("bad", [PY, "-c", "import sys; print('ERROR: it broke'); sys.exit(1)"])
            run.brief("note from the command")
            code, printed = finish(run)
            data = json.loads(printed)
            self.assertEqual((1, False, 2, "bad"), (code, data["success"], data["steps"], data["failed"][0]["step"]))
            self.assertEqual("ERROR: it broke", data["failed"][0]["message"])
            self.assertNotIn("artifacts", data)   # the file list stays in summary.json
            summary = json.loads((run.artifacts / "summary.json").read_text(encoding="utf-8"))
            self.assertIn("summary.txt", summary["artifacts"])
            self.assertNotIn("partial", summary)
            text = (run.artifacts / "summary.txt").read_text(encoding="utf-8")
            self.assertIn("note from the command", text)
            self.assertIn("FAIL bad exit=1 ERROR: it broke", text)
            self.assertEqual("fine", (run.artifacts / "00-ok.stdout.log").read_text(encoding="utf-8").strip())

    def test_a_killed_run_leaves_a_partial_summary_and_progress(self):
        with tempfile.TemporaryDirectory() as folder:
            run = make_run(folder)
            run.planned, run.plan_known = ["one", "two"], True
            run.process("one", [PY, "-c", "pass"])
            partial = json.loads((run.artifacts / "summary.json").read_text(encoding="utf-8"))
            self.assertEqual((True, None, 1), (partial["partial"], partial["success"], len(partial["steps"])))
            progress = json.loads((run.artifacts / "progress.json").read_text(encoding="utf-8"))
            self.assertEqual((1, 2, 0, False), (progress["steps_done"], progress["steps_total"], progress["failed"], progress["done"]))
            self.assertTrue(compact.text(partial).splitlines()[-1].startswith("PARTIAL world 1 steps"))

    def test_last_reads_the_newest_run_without_running(self):
        from embervale_sdk.commands import last
        with tempfile.TemporaryDirectory() as folder:
            for run_id, ok in (("20260101T000000-aaaaaaaaaa", False), ("20260102T000000-bbbbbbbbbb", True)):
                write_json(Path(folder) / run_id / "summary.json", dict(self.RESULT, run_id=run_id, success=ok,
                           exit_code=0 if ok else 1, artifact_directory=str(Path(folder) / run_id)))
            def shown(arguments):
                out = io.StringIO()
                with contextlib.redirect_stdout(out):
                    code = last.run(None, parser("last").parse_args(["last", *arguments, "--artifacts", folder]), [])
                return code, out.getvalue()
            self.assertIn("20260102T000000-bbbbbbbbbb", shown([])[1])
            code, text = shown(["--failed", "--json"])
            self.assertEqual((0, "20260101T000000-aaaaaaaaaa"), (code, json.loads(text)["run_id"]))
            self.assertEqual(2, len([l for l in shown(["2"])[1].splitlines() if "evidence=" in l]))
        with tempfile.TemporaryDirectory() as folder:
            self.assertEqual(2, shown([])[0])

    def test_bulky_runtime_metrics_are_counted_not_copied(self):
        with tempfile.TemporaryDirectory() as folder:
            run = make_run(folder, ["validate"], engine=Path("godot-test.exe"))
            def fake_engine(name, *unused, **unused_too):
                write_json(run.artifacts / f"{name}.result.json", dict(metrics=dict(
                    dependency_graph={"a": ["b"], "c": []}, selected_paths=["a", "b", "c"], samples_ms=[1.0, 2.0], scanned=3)))
            with patch.object(run, "godot", side_effect=fake_engine):
                run.runtime("validate")
            metrics = next(iter(run.result["metrics"].values()))
            self.assertEqual(dict(dependency_graph_count=2, selected_paths_count=3, samples_ms_count=2, scanned=3), metrics)


class ContractResultTests(unittest.TestCase):
    LINE = 'EMBERVALE_RESULT {"schema":1,"gate":"validate","ok":false,"exit_code":1,"facts":{"issues":2,"ids":["a","b"]},' \
           '"failures":["shop.x has no stock","quest.y names a missing item"],"warnings":["slow"]}'

    def test_a_quiet_gates_failures_become_error_diagnostics_once(self):
        from embervale_sdk.contract import parse_result, result_brief
        self.assertEqual("RESULT validate FAILED issues=2 ids=2 items", result_brief(parse_result("noise\n" + self.LINE)))
        found = diagnostics_from_log(self.LINE, "content")
        self.assertEqual([("error", "process.content", "shop.x has no stock"),
                          ("error", "process.content", "quest.y names a missing item")],
                         [(d["severity"], d["code"], d["message"]) for d in found])
        # The same failure already printed as an [ERROR] line is not counted twice.
        echoed = diagnostics_from_log("[ERROR] validate: shop.x has no stock\n" + self.LINE, "content")
        self.assertEqual(["[ERROR] validate: shop.x has no stock", "quest.y names a missing item"],
                         [d["message"] for d in echoed])
        self.assertIsNone(parse_result("EMBERVALE_RESULT {broken"))
        self.assertEqual([], diagnostics_from_log('EMBERVALE_RESULT {"gate":"state","ok":true,"failures":[]}', "state"))

    def test_a_failed_tool_is_quoted_not_pointed_at(self):
        from embervale_sdk.contract import first_words
        self.assertEqual("bake is stale: | - a.cs | - b.cs | +1 lines", first_words("\nbake is stale:\n  - a.cs\n  - b.cs\nfix it\n"))
        self.assertEqual("", first_words("\n\n"))


class ContractNoiseTests(unittest.TestCase):
    def test_mcp_plugin_lines_are_neither_errors_nor_warnings(self):
        self.assertEqual([], diagnostics_from_log("ERROR: [McpPlugin] relay is down\nWARNING: [McpPlugin] retrying", "editor"))


class CacheTests(unittest.TestCase):
    STATE = dict(entries={"src": "1", "tools": "2", "docs": "3", ".github": "4"}, changed={"src/A.cs": "h1"})

    def test_key_follows_inputs_and_ignores_what_no_gate_reads(self):
        base = cache.key(self.STATE, (), ["cmd"])
        self.assertEqual(base, cache.key(dict(self.STATE, entries=dict(self.STATE["entries"], **{".github": "9"})), (), ["cmd"]))
        self.assertNotEqual(base, cache.key(dict(self.STATE, changed={"src/A.cs": "h2"}), (), ["cmd"]))
        self.assertNotEqual(base, cache.key(dict(self.STATE, entries=dict(self.STATE["entries"], docs="9")), (), ["cmd"]))
        self.assertNotEqual(base, cache.key(self.STATE, (), ["other"]))
        scoped = cache.key(self.STATE, ("src",), ["cmd"])
        self.assertEqual(scoped, cache.key(dict(self.STATE, entries=dict(self.STATE["entries"], tools="9"),
                                                changed={"src/A.cs": "h1", "tools/x.py": "n"}), ("src",), ["cmd"]))
        self.assertNotEqual(scoped, cache.key(dict(self.STATE, changed={"src/A.cs": "h2"}), ("src",), ["cmd"]))

    def test_tree_state_sees_committed_changed_untracked_and_deleted(self):
        with tempfile.TemporaryDirectory(prefix="Embervale cache ") as folder:
            root = Path(folder)
            def git(*arguments):
                self.assertEqual(0, run_process(["git", *arguments], timeout=20, cwd=root).returncode)
            git("init", "-q")
            git("config", "user.email", "sdk-test@example.invalid")
            git("config", "user.name", "SDK tests")
            (root / "src").mkdir()
            for name in ("src/a.cs", "src/gone.cs", "top.txt"):
                (root / name).write_text("one")
            git("add", ".")
            git("commit", "-qm", "fixture")
            clean = cache.tree_state(root)
            self.assertEqual(({"src", "top.txt"}, {}), (set(clean["entries"]), clean["changed"]))
            (root / "src/a.cs").write_text("two")
            (root / "src/gone.cs").unlink()
            (root / "new file.txt").write_text("new")
            dirty = cache.tree_state(root)
            self.assertEqual({"src/a.cs", "src/gone.cs", "new file.txt"}, set(dirty["changed"]))
            self.assertEqual("absent", dirty["changed"]["src/gone.cs"])
            first = dirty["changed"]["src/a.cs"]
            (root / "src/a.cs").write_text("three")
            self.assertNotEqual(first, cache.tree_state(root)["changed"]["src/a.cs"])
        with tempfile.TemporaryDirectory() as folder:
            self.assertIsNone(cache.tree_state(Path(folder)))

    def test_a_pass_is_reused_and_a_failure_never_is(self):
        with tempfile.TemporaryDirectory() as folder:
            counter = Path(folder) / "count.txt"
            code = f"import pathlib; p = pathlib.Path({str(counter)!r}); p.write_text(p.read_text() + 'x' if p.exists() else 'x')"
            good, bad = python_gate("good", code), python_gate("bad", code + "; raise SystemExit(1)")
            first = make_run(folder)
            first.run_gates([(good, None), (bad, None)])
            self.assertEqual("xx", counter.read_text())
            second = make_run(folder)
            second.run_gates([(good, None), (bad, None)])
            self.assertEqual("xxx", counter.read_text())   # only the failing gate ran again
            steps = {s["name"]: s for s in second.result["steps"]}
            self.assertTrue(steps["good"]["cached"] and steps["good"]["success"])
            self.assertEqual(first.result["run_id"], steps["good"]["cached_run"])
            self.assertFalse(steps["bad"]["success"])
            third = make_run(folder, ["world", "--no-cache"])
            third.run_gates([(good, None)])
            self.assertEqual("xxxx", counter.read_text())
            changed = make_run(folder)
            changed.tree = dict(entries={"src": "2"}, changed={})
            changed.run_gates([(good, None)])
            self.assertEqual("xxxxx", counter.read_text())   # an input changed: the old pass does not count

    def test_gates_judged_by_eye_and_no_build_runs_are_not_cached(self):
        registry = {gate.name: gate for gate in gates(None)}
        self.assertFalse(any(registry[name].cacheable for name in ("visuals", "performance", "environment-route")))
        self.assertTrue(registry["content"].cacheable)
        self.assertEqual((), registry["tests"].inputs)   # the unit suite reads data/ and docs/
        self.assertIn("src", registry["build"].inputs)
        with tempfile.TemporaryDirectory() as folder:
            self.assertIsNone(make_run(folder, ["world", "--no-build"]).gate_key(registry["content"], ["x"]))
            self.assertIsNone(make_run(folder).gate_key(registry["visuals"], ["x"]))
            self.assertIsNotNone(make_run(folder).gate_key(registry["content"], ["x"]))


class ParallelGateTests(unittest.TestCase):
    def test_neighbouring_python_gates_overlap_and_are_recorded_in_order(self):
        with tempfile.TemporaryDirectory() as folder:
            names = ("generation", "world-bake", "atlas")   # three gates the registry marks parallel
            fixture = [(python_gate(name, "import time; time.sleep(1.2); print('done')"), None) for name in names]
            self.assertTrue(all(gate.parallel for gate, _ in fixture))
            run = make_run(folder)
            started = time.monotonic()
            run.run_gates(fixture)
            elapsed = time.monotonic() - started
            self.assertEqual(list(names), [s["name"] for s in run.result["steps"]])
            self.assertTrue(all(s["success"] for s in run.result["steps"]))
            self.assertLess(elapsed, 3.2, "three 1.2 s gates should overlap")
            self.assertEqual(["00-generation.stdout.log", "01-world-bake.stdout.log", "02-atlas.stdout.log"],
                             [s["stdout"] for s in run.result["steps"]])
            self.assertEqual([], run.planned)

    def test_parallel_one_is_serial_and_engine_gates_never_batch(self):
        registry = gates("godot-test.exe")
        self.assertFalse(any(gate.parallel for gate in registry if gate.command[0] == "godot-test.exe"))
        self.assertFalse(any(gate.parallel for gate in registry if gate.name in ("build", "tests", "shipping-assembly", "negative")))
        with tempfile.TemporaryDirectory() as folder:
            run = make_run(folder, ["world", "--parallel", "1"])
            run.execute = Mock(side_effect=AssertionError("serial runs go through process"))
            run.process = Mock()
            run.run_gates([(python_gate("generation"), None), (python_gate("atlas"), None)])
            self.assertEqual(["generation", "atlas"], [c.args[0] for c in run.process.call_args_list])


class HeavyLockTests(unittest.TestCase):
    def test_acquire_refuse_release_and_stale_takeover(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "heavy.lock"
            self.assertIsNone(heavy.acquire("first", path))
            self.assertEqual("first", heavy.holder(path)["what"])
            blocker = heavy.acquire("second", path, wait=0)
            self.assertEqual((os.getpid(), "first"), (blocker["pid"], blocker["what"]))
            heavy.release(path, pid=os.getpid() + 1)   # not the owner: stays
            self.assertIsNotNone(heavy.holder(path))
            heavy.release(path)
            self.assertIsNone(heavy.holder(path))
            dead = run_process([PY, "-c", "import os; print(os.getpid())"], timeout=20)
            path.write_text(json.dumps(dict(pid=int(dead.stdout), what="gone")))
            self.assertIsNone(heavy.acquire("third", path))   # the dead owner's lock is taken over
            self.assertEqual("third", heavy.holder(path)["what"])

    def test_pid_alive(self):
        self.assertTrue(pid_alive(os.getpid()))
        self.assertFalse(pid_alive(0))

    def test_an_engine_launch_refuses_while_another_run_holds_the_lock(self):
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, EMBERVALE_HEAVY_LOCK=str(Path(folder) / "l")):
            os.environ.pop(heavy.HELD, None)
            run = make_run(folder, ["smoke", "--no-build"], engine=Path("godot-test.exe"))
            run.env.pop(heavy.HELD, None)
            run.process = Mock()
            heavy.acquire("someone else")   # held by this (live) process, as another run would
            with self.assertRaisesRegex(ValueError, "heavy lock"):
                run.godot("blocked", [])
            run.process.assert_not_called()
            heavy.release()
            run.godot("first", [])
            run.godot("second", [])
            self.assertEqual("1", run.env[heavy.HELD])   # nested SDK calls inherit instead of re-locking
            self.assertTrue(heavy.holder())
            finish(run)
            self.assertIsNone(heavy.holder())
            nested = make_run(folder, ["smoke", "--no-build"], engine=Path("godot-test.exe"))
            nested.env[heavy.HELD] = "1"
            nested.process = Mock()
            heavy.acquire("the parent run")
            nested.godot("inside", [])   # does not raise: the parent holds it for us
            heavy.release()

    def test_engine_tools_are_guarded_and_read_only_calls_are_not(self):
        with tempfile.TemporaryDirectory() as folder:
            launcher, pure = Path(folder) / "launcher.py", Path(folder) / "pure.py"
            launcher.write_text("from quality_common import discover_godot\n")
            pure.write_text("print('no engine here')\n")
            run = make_run(folder)
            run.ensure_fresh, run.ensure_heavy = Mock(), Mock()
            run.guard_tool(pure, ["--bake"])
            run.guard_tool(launcher, ["--check"])
            run.guard_tool(launcher, ["status"])
            run.ensure_fresh.assert_not_called()
            run.guard_tool(launcher, ["--bake"])
            run.ensure_fresh.assert_called_once()
            run.ensure_heavy.assert_called_once()


class JobTests(unittest.TestCase):
    def test_status_lines(self):
        now = 1000.0
        base = dict(id="J1", argv=["world", "--mode", "engine"], created=900.0)
        running = dict(base, state="running", started=940.0, eta_seconds=550, progress=dict(
            steps_done=30, steps_total=58, step="traversal", step_started=980.0, failed=1, eta_seconds=200, updated=990.0))
        self.assertEqual("RUNNING J1 1m00s step 31/58 traversal 20s fails=1 eta=3m10s: world --mode engine",
                         jobs.status_line(running, now))
        self.assertEqual("RUNNING J1 1m00s eta=8m10s: world --mode engine",
                         jobs.status_line(dict(base, state="running", started=940.0, eta_seconds=550, progress={}), now))
        done = dict(base, state="done", started=940.0, ended=990.0, exit_code=0, progress=dict(artifact_directory="X:/r"))
        self.assertTrue(jobs.status_line(done, now).startswith("DONE PASS J1 exit=0 50s evidence="))
        self.assertTrue(jobs.status_line(dict(done, exit_code=1), now).startswith("DONE FAIL J1 exit=1"))
        self.assertTrue(jobs.status_line(dict(base, state="dead", progress=dict(step="bake")), now).startswith("DEAD J1"))
        with patch("embervale_sdk.jobs.heavy.holder", return_value=dict(pid=7, what="job J0")):
            self.assertEqual("QUEUED J1 1m40s behind pid 7 (job J0): world --mode engine",
                             jobs.status_line(dict(base, state="queued", queued=900.0), now))

    def test_estimate_prefers_history_then_seed(self):
        self.assertEqual(550, jobs.job_estimate(["world", "--mode", "engine"], {}))
        self.assertEqual(9, jobs.job_estimate(["world", "--mode", "engine"], {"job:world --mode engine": 9}))
        self.assertIsNone(jobs.job_estimate(["never", "seen"], {}))

    def test_everything_after_job_start_belongs_to_the_job(self):
        seen = {}
        def fake_start(argv, sdk):
            seen.update(argv=argv, sdk=sdk)
            return dict(id="J", pid=1, eta_seconds=None, directory="d", argv=argv)
        with patch("embervale_sdk.commands.job.jobs.start", side_effect=fake_start), contextlib.redirect_stdout(io.StringIO()) as out:
            self.assertEqual(0, main(["--json", "job", "start", "tool", "world_bake", "--timeout", "1200", "--", "--bake"]))
            self.assertEqual((["tool", "world_bake", "--timeout", "1200", "--", "--bake"], True), (seen["argv"], seen["sdk"]))
            self.assertEqual("J", json.loads(out.getvalue())["id"])
            main(["job", "start", "--", "dotnet", "--info"])
            self.assertEqual((["dotnet", "--info"], False), (seen["argv"], seen["sdk"]))

    def wait(self, directory, seconds=40):
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            job = jobs.view(directory)
            if job["state"] not in jobs.ACTIVE:
                return job
            time.sleep(0.3)
        self.fail(f"job still {job['state']}: {(Path(directory) / 'supervisor.log').read_text(errors='replace')}")

    def test_a_detached_job_runs_reports_and_frees_the_lock(self):
        with tempfile.TemporaryDirectory(prefix="Embervale jobs ", ignore_cleanup_errors=True) as folder, \
                patch.dict(os.environ, EMBERVALE_HEAVY_LOCK=str(Path(folder) / "heavy.lock")):
            root = Path(folder)
            info = jobs.start([PY, "-c", "import os; print('job says hi'); print(os.environ['EMBERVALE_HEAVY_HELD'])"], sdk=False, root=root)
            directory = Path(info["directory"])
            job = self.wait(directory)
            self.assertEqual(("done", 0), (job["state"], job["exit_code"]))
            self.assertEqual(["job says hi", "1"], jobs.tail(directory / "output.log", 5))
            self.assertIsNone(heavy.holder())
            self.assertEqual(directory, jobs.find(None, root))
            self.assertEqual(directory, jobs.find(info["id"][-6:], root))
            self.assertIn("job:", next(iter(costs.load(root))))
            failing = jobs.start([PY, "-c", "raise SystemExit(3)"], sdk=False, root=root)
            self.assertEqual(3, self.wait(failing["directory"])["exit_code"])
            with self.assertRaises(ValueError):
                jobs.find("no-such-job", root)

    def test_a_second_job_queues_and_cancel_ends_a_job(self):
        with tempfile.TemporaryDirectory(prefix="Embervale jobs ", ignore_cleanup_errors=True) as folder, \
                patch.dict(os.environ, EMBERVALE_HEAVY_LOCK=str(Path(folder) / "heavy.lock")):
            root = Path(folder)
            heavy.acquire("a foreground engine run")
            queued = jobs.start([PY, "-c", "print('ran')"], sdk=False, root=root)
            directory = Path(queued["directory"])
            deadline = time.monotonic() + 20
            while jobs.view(directory)["state"] != "queued" and time.monotonic() < deadline:
                time.sleep(0.2)
            self.assertEqual("queued", jobs.view(directory)["state"])
            self.assertFalse((directory / "output.log").exists())   # it has not started the command
            self.assertTrue(jobs.cancel(directory))
            self.assertEqual("cancelled", jobs.view(directory)["state"])
            self.assertFalse(jobs.cancel(directory))
            self.assertEqual("a foreground engine run", heavy.holder()["what"])   # cancelling did not take our lock
            heavy.release()
            self.assertIsNone(heavy.holder())

    def test_a_vanished_supervisor_reads_as_dead(self):
        with tempfile.TemporaryDirectory() as folder:
            directory = Path(folder) / "20260101T000000-abcdef"
            gone = int(run_process([PY, "-c", "import os; print(os.getpid())"], timeout=20).stdout)
            write_json(directory / "job.json", dict(id=directory.name, argv=["x"], pid=gone))
            write_json(directory / "state.json", dict(state="running", pid=gone, started=time.time()))
            job = jobs.view(directory)
            self.assertEqual(("dead", jobs.DEAD), (job["state"], job["exit_code"]))

    def test_run_reports_progress_into_its_job_directory_only(self):
        with tempfile.TemporaryDirectory() as folder:
            job_directory = Path(folder) / "job"
            job_directory.mkdir()
            with patch.dict(os.environ, EMBERVALE_JOB_DIR=str(job_directory)):
                run = make_run(folder)
            self.assertNotIn("EMBERVALE_JOB_DIR", run.env)   # a nested SDK call must not report as the job
            run.process("one", [PY, "-c", "pass"])
            finish(run)
            progress = json.loads((job_directory / "progress.json").read_text(encoding="utf-8"))
            self.assertEqual((True, 0, 1), (progress["done"], progress["exit_code"], progress["steps_done"]))


class PolledFileTests(unittest.TestCase):
    def test_write_json_retries_a_replace_blocked_by_a_reader(self):
        with tempfile.TemporaryDirectory() as folder:
            path, real, calls = Path(folder) / "state.json", Path.replace, []
            def blocked_twice(self_path, target):
                calls.append(target)
                if len(calls) < 3:
                    raise PermissionError(5, "Access is denied")
                return real(self_path, target)
            with patch.object(Path, "replace", blocked_twice):
                write_json(path, dict(state="done"))
            self.assertEqual((3, "done"), (len(calls), json.loads(path.read_text())["state"]))

    def test_a_job_between_spawn_and_pid_is_starting_not_dead(self):
        with tempfile.TemporaryDirectory() as folder:
            directory = Path(folder) / "20260101T000000-abcdef"
            write_json(directory / "job.json", dict(id=directory.name, argv=["x"], created=time.time()))
            write_json(directory / "state.json", dict(state="starting"))
            self.assertEqual("starting", jobs.view(directory)["state"])
            write_json(directory / "job.json", dict(id=directory.name, argv=["x"], created=time.time() - 60))
            self.assertEqual("dead", jobs.view(directory)["state"])


class CostTests(unittest.TestCase):
    def test_estimate_falls_back_from_history_to_seed_to_kind(self):
        self.assertEqual(3, costs.estimate("content", {"content": 3}))
        self.assertEqual(18, costs.estimate("content", {}))
        self.assertEqual(7, costs.estimate("seams-ember_crown", {"seams": 7}))
        self.assertEqual(costs.ENGINE_STEP, costs.estimate("melee", {}, engine=True))
        self.assertEqual(costs.PYTHON_STEP, costs.estimate("atlas", {}))

    def test_history_round_trip_and_average(self):
        with tempfile.TemporaryDirectory() as folder:
            history = costs.load(Path(folder))
            costs.record(history, "build", 20)
            costs.record(history, "build", 30)
            costs.save(history, Path(folder))
            self.assertEqual({"build": 25.0}, costs.load(Path(folder)))

    def test_clock(self):
        self.assertEqual(["41s", "7m10s", "1h02m"], [costs.clock(s) for s in (41, 430, 3725)])


class TriageTests(unittest.TestCase):
    LOG = "\n".join([
        "Godot Engine v4.7.1.stable.mono",
        "[ERROR] (OnInitialize) TelegraphComponent failed on <Node3D#4410> at (1.5, 2, -3)",
        "   at Embervale.Combat.TelegraphComponent.OnInitialize() in C:\\src\\Combat\\TelegraphComponent.cs:line 41",
        "[ERROR] (OnInitialize) TelegraphComponent failed on <Node3D#9912> at (7, 0.25, 12)",
        "ERROR: Condition \"!is_inside_tree()\" is true. Returning: Transform3D()",
        "   at: get_global_transform (scene/3d/node_3d.cpp:343)",
        "WARNING: slot save_audit_0123456789abcdef0123456789abcdef is slow (212 ms)",
        "WARNING: slot save_audit_fedcba9876543210fedcba9876543210 is slow (3 ms)",
        "ERROR: [McpPlugin] relay is down",
        "WARNING: ObjectDB instances leaked at exit (run with --verbose for details).",
        "STALE_BINARY Embervale.dll was built before src/A.cs changed",
        'EMBERVALE_RESULT {"schema":1,"gate":"state","ok":true,"facts":{"regions":6}}',
        "0 errors, 3 warnings in the build",
    ])

    def test_groups_counts_first_line_and_origin(self):
        report = triage.triage(self.LOG, triage.known_noise())
        self.assertEqual([("error", 2, 2), ("error", 1, 5), ("warning", 2, 7)],
                         [(g["severity"], g["count"], g["line"]) for g in report["groups"]])
        self.assertEqual("TelegraphComponent.OnInitialize TelegraphComponent.cs:41", report["groups"][0]["origin"])
        self.assertEqual("get_global_transform (scene/3d/node_3d.cpp:343)", report["groups"][1]["origin"])
        self.assertIn("#4410", report["groups"][0]["message"])   # the first raw example is kept
        self.assertEqual(2, report["noise"])
        self.assertEqual("state", report["result"]["gate"])
        self.assertEqual([11, 12], [m["line"] for m in report["markers"]])

    def test_render_is_one_line_per_group_and_a_totals_line(self):
        lines = triage.render(triage.triage(self.LOG, triage.known_noise()), source="x.log")
        self.assertTrue(lines[0].startswith("E x2 L2 [ERROR] (OnInitialize)"))
        self.assertTrue(lines[0].endswith("| TelegraphComponent.OnInitialize TelegraphComponent.cs:41"))
        self.assertEqual("LOGS errors=2(3) warnings=1(2) noise=2 lines=13 source=x.log", lines[-1])
        self.assertEqual(1, len([l for l in triage.render(triage.triage(self.LOG), top=1) if l.startswith(("E ", "W "))]))

    def test_raw_keeps_noise_and_distinct_messages_stay_distinct(self):
        self.assertEqual(0, triage.triage(self.LOG, ())["noise"])
        report = triage.triage("ERROR: shop.embercrown has no stock\nERROR: shop.frostfang has no stock")
        self.assertEqual(2, len(report["groups"]))

    def test_logs_command_reads_a_file_or_the_newest_run(self):
        from embervale_sdk.commands import logs
        with tempfile.TemporaryDirectory() as folder:
            run_directory = Path(folder) / "20260101T000000-aaaaaaaaaa"
            run_directory.mkdir()
            (run_directory / "content.godot.log").write_text(self.LOG, encoding="utf-8")
            (run_directory / "00-content.stdout.log").write_text("ERROR: only in the step log", encoding="utf-8")
            clean = Path(folder) / "clean.log"
            clean.write_text("WARNING: one thing\nall good\n", encoding="utf-8")
            def shown(arguments):
                out = io.StringIO()
                with contextlib.redirect_stdout(out):
                    code = logs.run(None, parser("logs").parse_args(["logs", *arguments, "--artifacts", folder]), [])
                return code, out.getvalue()
            code, text = shown([])
            self.assertEqual(1, code)
            self.assertIn("errors=2(3)", text)
            self.assertNotIn("only in the step log", text)   # the engine log is the source when there is one
            self.assertEqual(0, shown([str(clean)])[0])
            raw = json.loads(shown([str(run_directory), "--json", "--raw", "--errors-only"])[1])
            self.assertEqual((0, 3), (raw["noise"], len(raw["groups"])))   # --raw lists the McpPlugin error too
            with self.assertRaises(ValueError):
                shown([str(Path(folder) / "missing.log")])

    def test_a_log_call_and_its_engine_echo_are_one_group_with_the_callers_origin(self):
        # Shapes copied from a real environment_route.godot.log and arena.godot.log.
        log = "\n".join([
            "WARNING: [WARN]  (_Process) World performance budget exceeded in 'region.ember_crown': frame ms 20.06 > 16.67.",
            "   at: void Embervale.Core.Diagnostics.Log.Warn(string, string) (res://src/Core/Diagnostics/Log.cs:51)",
            "   C# backtrace (most recent call first):",
            "       [0] void Godot.GD.PushWarning(string) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/GD.cs:396)",
            "       [1] void Embervale.Core.Diagnostics.Log.Warn(string, string) (C:\\Users\\x\\src\\Core\\Diagnostics\\Log.cs:51)",
            "       [2] void Embervale.World.WorldPerformanceMonitor._Process(double) (C:\\Users\\x\\src\\World\\WorldPerformanceMonitor.cs:133)",
            "       [3] bool Godot.Node.InvokeGodotClassMethod(Godot.NativeInterop.godot_string_name&) (/root/godot/modules/x/Node.cs:9)",
            "[WARN]  (_Process) World performance budget exceeded in 'region.ember_crown': frame ms 20.06 > 16.67.",
            "WARNING: [WARN]  (_Process) World performance budget exceeded in 'region.ember_crown': frame ms 28.49 > 16.67.",
            "   at: void Embervale.Core.Diagnostics.Log.Warn(string, string) (res://src/Core/Diagnostics/Log.cs:51)",
            "[WARN]  (_Process) World performance budget exceeded in 'region.ember_crown': frame ms 28.49 > 16.67.",
            "ERROR: System.ObjectDisposedException: Cannot access a disposed object.",
            "Object name: 'Embervale.Enemies.EnemyEntity'.",
            "   at Godot.GodotObject.GetPtr(GodotObject instance) in /root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/GodotObject.base.cs:line 93",
            "   at Embervale.Player.CameraFramingLayer.TrackTarget(IEntity target, Single dt) in C:\\Users\\x\\src\\Player\\CameraFramingLayer.cs:line 115",
            "   at Embervale.Player.PlayerCameraRig.Tick(Double delta) in C:\\Users\\x\\src\\Player\\PlayerCameraRig.cs:line 416",
            "WARNING: Navigation region synchronization had 2 edge error(s).",
            'EMBERVALE_RESULT {"schema":1,"gate":"arena","ok":false,"facts":{"trials":1},"failures":["enemy.goblin: nothing fought"]}',
        ])
        report = triage.triage(log, triage.known_noise())
        self.assertEqual([("error", 1, "CameraFramingLayer.TrackTarget CameraFramingLayer.cs:115"),
                          ("warning", 2, "WorldPerformanceMonitor._Process WorldPerformanceMonitor.cs:133")],
                         [(g["severity"], g["count"], g["origin"]) for g in report["groups"]])
        self.assertTrue(report["groups"][1]["message"].startswith("[WARN]"))
        self.assertEqual(1, report["noise"])   # the documented navigation edge-sync warning
        self.assertEqual("RESULT arena FAILED trials=1 failures=1: enemy.goblin: nothing fought", report["markers"][0]["text"])
        # An engine echo whose plain line never reached the log still counts.
        self.assertEqual(1, triage.triage("WARNING: [WARN] alone")["groups"][0]["count"])

    def test_known_noise_file_is_valid_regexes(self):
        import re
        patterns = triage.known_noise()
        self.assertTrue(patterns)
        for pattern in patterns:
            re.compile(pattern)


class VerifyTests(unittest.TestCase):
    def setUp(self):
        from embervale_sdk.commands import verify
        self.verify = verify
        self.registry = gates(None)
        self.names = [gate.name for gate in self.registry]

    def plan(self, *paths):
        return self.verify.plan(list(paths), self.registry, REGIONS)

    def test_every_rule_names_a_real_gate_and_every_gate_is_reachable(self):
        named = {name for _, names in self.verify.RULES for name in names}
        self.assertEqual(set(), named - set(self.names), "a rule names a gate the registry does not have")
        self.assertEqual(set(), self.verify.EXCLUDED - set(self.names))
        self.assertEqual(set(), set(self.names) - named - self.verify.EXCLUDED,
                         "a registry gate no rule can select: add it to RULES or to EXCLUDED")
        self.assertEqual(set(), named & self.verify.EXCLUDED)

    def test_csharp_always_builds_tests_and_validates(self):
        selection = self.plan("src/Npc/ScheduleComponent.cs")
        self.assertEqual(["build", "tests", "content"], selection["gates"])
        self.assertEqual(([], [], None), (selection["extra"], selection["unmapped"], selection["regions"]))

    def test_system_folders_add_their_probes_in_registry_order(self):
        selection = self.plan("src/Magic/SpellcastingComponent.cs", "src/Save/SaveManager.cs")
        for name in ("build", "tests", "content", "magic-core", "magic-status", "save-audit", "save-reload", "lifecycle"):
            self.assertIn(name, selection["gates"])
        self.assertNotIn("melee", selection["gates"])
        self.assertEqual(sorted(selection["gates"], key=self.names.index), selection["gates"])

    def test_a_gates_own_script_selects_it(self):
        self.assertEqual(["melee"], self.plan("tools/melee_probe.gd")["gates"])
        self.assertEqual(["atlas"], self.plan("tools/world_atlas.py")["gates"])
        self.assertNotIn("negative", self.plan("tools/negative_tests.py")["gates"])   # forty minutes: advised, not run
        self.assertTrue(any("negative battery" in a for a in self.plan("tools/negative_tests.py")["advice"]))

    def test_region_files_narrow_per_region_gates(self):
        one = self.plan("tools/region_spec_ember_crown.py", "data/regions/EmberCrown.tres")
        self.assertEqual(["ember_crown"], one["regions"])
        self.assertTrue({"generation", "seams", "layout", "composition"} <= set(one["gates"]))
        self.assertEqual(["ember_crown", "sunspire"], self.plan("data/regions/EmberCrown.tres", "data/regions/Sunspire.tres")["regions"])
        self.assertIsNone(self.plan("tools/region_spec_ember_crown.py", "tools/gen_regions.py")["regions"])

    def test_docs_tools_ignored_and_unmapped(self):
        self.assertEqual((["docs-lines"], []), (self.plan("docs/NOW.md")["extra"], self.plan("docs/NOW.md")["gates"]))
        self.assertEqual(["tool-tests"], self.plan("tools/embervale_sdk/cli.py", "tools/quality_common.py")["extra"])
        ignored = self.plan(".github/workflows/ci.yml", "reports/3d/x.csv", "docs/img/shot.png", ".gitignore")
        self.assertEqual(([], [], []), (ignored["gates"], ignored["extra"], ignored["unmapped"]))
        self.assertEqual(["tools/some_new_probe.gd", "strange/file.bin"],
                         self.plan("tools/some_new_probe.gd", "strange/file.bin")["unmapped"])
        other = self.plan("tools/some_new_generator.py")   # a Python tool with no rule: the unit suite
        self.assertEqual((["tool-tests"], []), (other["extra"], other["unmapped"]))
        self.assertEqual(["generators"], self.plan("tools/gen_perks.py")["gates"])
        self.assertIn("generators", self.plan("data/perks/x.tres")["gates"])
        self.assertTrue(any("no gate sees pixels" in a for a in self.plan("src/UI/GameHud.cs")["advice"]))

    def test_estimates_count_regions_and_use_measured_seeds(self):
        selection = self.plan("data/regions/EmberCrown.tres", "src/Core/Log.cs")
        rows = dict(self.verify.estimates(selection, self.registry, REGIONS, {}))
        self.assertEqual((20, 19, 18, 38, 31), tuple(rows[n] for n in ("build", "tests", "content", "lifecycle", "save-reload")))
        self.assertEqual(costs.PYTHON_STEP, rows["seams"])   # one region, not six
        everywhere = dict(self.verify.estimates(self.plan("tools/gen_regions.py"), self.registry, REGIONS, {}))
        self.assertEqual(6 * costs.PYTHON_STEP, everywhere["seams"])

    def test_plan_prints_and_runs_nothing(self):
        out = io.StringIO()
        with patch("embervale_sdk.changed.changed_paths", return_value=(["src/Magic/Spell.cs", "tools/odd.gd"], True)), \
                patch("embervale_sdk.cli.Run", side_effect=AssertionError("--plan must not create a run")), \
                patch("embervale_sdk.commands.verify.costs.load", return_value={}), contextlib.redirect_stdout(out):
            self.assertEqual(0, main(["verify", "--plan", "--budget", "15"]))
        lines = out.getvalue().splitlines()
        self.assertTrue(lines[0].startswith("PLAN 8 steps ~1m47s for 2 changed file(s): build 20s, tests 19s, content 18s, magic-core 10s"))
        self.assertEqual("SKIP over --budget 15s: build, tests, content", lines[1])
        self.assertEqual("UNMAPPED 1: tools/odd.gd", lines[2])

    def test_run_executes_the_selection_and_reports_unmapped_as_a_warning(self):
        with tempfile.TemporaryDirectory() as folder:
            (Path(folder) / "long.md").write_text("ok\n" + "x" * 2001 + "\n", encoding="utf-8")
            ran = []
            def fake_gates(self_run, items):
                ran.extend(gate.name + ("-" + region if region else "") for gate, region in items)
            def fake_process(self_run, name, command, **_):
                # Never the real thing: `tool-tests` is this suite, and running it from inside itself
                # is a process chain that never ends.
                ran.append(name)
                self_run.add_step(dict(name=name, command=[], duration=0, exit_code=0, success=True))
            out = io.StringIO()
            with patch("embervale_sdk.changed.changed_paths", return_value=(["tools/world_atlas.py", "long.md", "odd/x.bin"], False)), \
                    patch("embervale_sdk.cli.Run.run_gates", fake_gates), patch("embervale_sdk.cli.Run.process", fake_process), \
                    patch("embervale_sdk.cli.discover_godot", return_value=None), \
                    patch("embervale_sdk.commands.verify.ROOT", Path(folder)), patch("embervale_sdk.cli.costs.save"), \
                    contextlib.redirect_stdout(out):
                code = main(["verify", "--json", "--artifacts", folder])
            data = json.loads(out.getvalue())
            self.assertEqual(["atlas", "tool-tests"], ran)
            self.assertEqual((1, "docs-lines", 1), (code, data["failed"][0]["step"], data["warnings"]))
            self.assertIn("long.md:2 is 2001 characters", data["failed"][0]["message"])
            self.assertTrue(any(line.startswith("UNMAPPED 1: odd/x.bin") for line in data["brief"]))


class FocusedTestTests(unittest.TestCase):
    def test_python_failures_are_named_with_their_first_message(self):
        text = "\n".join([
            "======================================================================",
            "FAIL: test_clock (test_sdk_tools.CostTests.test_clock)",
            "----------------------------------------------------------------------",
            "Traceback (most recent call last):",
            '  File "x.py", line 3, in test_clock',
            "    self.assertEqual(1, 2)",
            "AssertionError: 1 != 2",
            "",
            "======================================================================",
            "ERROR: test_old (test_sdk.ContractTests)",
            "----------------------------------------------------------------------",
            "Traceback (most recent call last):",
            "ValueError: boom",
            "",
            "Ran 44 tests in 1.2s",
            "FAILED (failures=1, errors=1)"])
        report = python_report(text)
        self.assertEqual(44, report["ran"])
        self.assertEqual([("test_sdk_tools.CostTests.test_clock", "AssertionError: 1 != 2"),
                          ("test_sdk.ContractTests.test_old", "ValueError: boom")], report["failures"])
        self.assertEqual(dict(ran=3, failures=[]), python_report("...\nRan 3 tests in 0.1s\n\nOK"))

    def test_trx_failures_and_counts(self):
        trx = """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testName="Embervale.Tests.A.Passes" outcome="Passed" />
    <UnitTestResult testName="Embervale.Tests.A.Breaks" outcome="Failed">
      <Output><ErrorInfo><Message>Assert.Equal() Failure
Expected: 1</Message><StackTrace>at X</StackTrace></ErrorInfo></Output>
    </UnitTestResult>
  </Results>
  <ResultSummary outcome="Failed"><Counters total="3" executed="2" passed="1" failed="1" /></ResultSummary>
</TestRun>"""
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "game-tests.trx"
            path.write_text(trx, encoding="utf-8")
            report = trx_report(path)
            self.assertEqual((1, 1, 1), (report["passed"], report["failed"], report["skipped"]))
            self.assertEqual([("Embervale.Tests.A.Breaks", "Assert.Equal() Failure")], report["failures"])
            self.assertIsNone(trx_report(Path(folder) / "missing.trx"))

    def steps(self, arguments):
        with tempfile.TemporaryDirectory() as folder:
            run = make_run(folder, ["test", *arguments])
            run.process = Mock(return_value=Mock(stdout="", stderr="Ran 2 tests in 0.1s\n\nOK"))
            run.tests()
            return {c.args[0]: c.args[1] for c in run.process.call_args_list}

    def test_suite_selection_and_filters(self):
        self.assertEqual(["tool-tests", "game-tests"], list(self.steps([])))
        self.assertEqual(["tool-tests"], list(self.steps(["--only", "tool"])))
        focused = self.steps(["--filter", "FullyQualifiedName~HeadlessTooling"])
        self.assertEqual(["game-tests"], list(focused))
        self.assertEqual(["--filter", "FullyQualifiedName~HeadlessTooling"], focused["game-tests"][-2:])
        python = self.steps(["--py", "FreshnessTests"])
        self.assertEqual(["tool-tests"], list(python))
        self.assertEqual(["-k", "FreshnessTests"], python["tool-tests"][-2:])
        self.assertEqual(["tool-tests", "game-tests"], list(self.steps(["--py", "A", "--filter", "B"])))

    def test_failed_tests_become_named_diagnostics(self):
        with tempfile.TemporaryDirectory() as folder:
            run = make_run(folder, ["test", "--only", "tool"])
            run.process = Mock(return_value=Mock(stdout="", stderr="FAIL: test_x (pkg.Case.test_x)\nAssertionError: no\nRan 1 test in 0s"))
            run.tests()
            failed = [d for d in run.result["diagnostics"] if d["code"] == "test.failed"]
            self.assertEqual([("pkg.Case.test_x", "pkg.Case.test_x: AssertionError: no")], [(d["path"], d["message"]) for d in failed])
            self.assertEqual(dict(tool=dict(ran=1, failed=1)), run.result["metrics"]["tests"])


class DoctorTests(unittest.TestCase):
    def test_tasklist_rows_survive_locale_number_formats(self):
        text = '"Godot_v4.7.1-stable_mono_win64.exe","4312","Console","1","2,100,224 K"\n' \
               '"dotnet.exe","88","Console","1","512.000 K"\n"System Idle Process","0","Services","0","8 K"\n\ngarbage'
        self.assertEqual([("Godot_v4.7.1-stable_mono_win64.exe", 4312, 2051), ("dotnet.exe", 88, 500), ("System Idle Process", 0, 0)],
                         parse_tasklist(text))

    def test_mcp_check_reports_blocked_without_a_config(self):
        import godot_mcp_check
        with tempfile.TemporaryDirectory() as folder, patch("godot_mcp_check.ROOT", Path(folder)):
            result = godot_mcp_check.check()
            self.assertEqual(("blocked", 2), (result["status"], result["exit_code"]))
            (Path(folder) / ".mcp.json").write_text(json.dumps({"mcpServers": {"ai-game-developer": {"url": "https://ai-game.dev/x"}}}))
            self.assertEqual(("fail", 1), (godot_mcp_check.check()["status"], godot_mcp_check.check()["exit_code"]))

    def test_doctor_rows(self):
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, EMBERVALE_HEAVY_LOCK=str(Path(folder) / "l")):
            run = make_run(folder, ["doctor"], engine=Path("godot-test.exe"))
            run.result["godot_version"] = "4.7.1.stable.mono.official"
            outputs = {"dotnet-sdk": "8.0.404 [C:/dotnet/sdk]", "dotnet-runtime": "Microsoft.NETCore.App 8.0.11 [x]",
                       "git": "## lane/sdk...origin/main [ahead 2]\n M tools/a.py\n?? tools/b.py"}
            run.process = Mock(side_effect=lambda name, *a, **k: Mock(stdout=outputs[name], returncode=0))
            table = [("Godot_v4.7.1-stable_mono_win64_console.exe", 10, 2100), ("dotnet.exe", 11, 900), ("dotnet.exe", 12, 900)]
            with patch("quality_common.memory_megabytes", return_value=(3000, 14000)), \
                    patch("quality_common.process_table", return_value=table), \
                    patch("embervale_sdk.cli.stale_reason", return_value="src/A.cs is newer"), \
                    patch("godot_mcp_check.check", return_value=dict(status="blocked", detail="godot-cli is not on PATH")):
                run.doctor()
            rows = {r["key"]: r for r in run.result["metrics"]["doctor"]}
            self.assertEqual(("ok", "ok", "warn", "warn", "warn", "warn", "ok", "info"),
                             tuple(rows[k]["level"] for k in ("godot", "dotnet", "binary", "memory", "godot-processes", "build-servers", "heavy-lock", "mcp")))
            self.assertIn("pid 10 2100MB", rows["godot-processes"]["value"])
            self.assertIn("2 dotnet process(es), 1800 MB", rows["build-servers"]["value"])
            self.assertEqual("lane/sdk...origin/main [ahead 2], 2 changed", rows["git"]["value"])
            self.assertEqual([], [d for d in run.result["diagnostics"] if d["severity"] == "error"])
            self.assertTrue(run.result["brief"][-1].startswith("ok: godot dotnet project"))
            self.assertTrue(any(line.startswith("warn memory: 3000 MB free of 14000 MB") for line in run.result["brief"]))

    def test_doctor_fails_on_low_memory_wrong_engine_and_a_leftover_journal(self):
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, EMBERVALE_HEAVY_LOCK=str(Path(folder) / "l")):
            run = make_run(folder, ["doctor"])
            run.process = Mock(return_value=Mock(stdout="", returncode=1))
            (Path(folder) / "artifacts/negative-journal").mkdir(parents=True)
            (Path(folder) / "artifacts/negative-journal/journal.json").write_text("{}")
            for name in ("project.godot", "Embervale.csproj"):
                (Path(folder) / name).write_text("net8.0 Godot.NET.Sdk/4.7.0")
            with patch("quality_common.memory_megabytes", return_value=(900, 14000)), patch("quality_common.process_table", return_value=[]), \
                    patch("embervale_sdk.cli.ROOT", Path(folder)), patch("embervale_sdk.cli.stale_reason", return_value=None), \
                    patch("godot_mcp_check.check", side_effect=RuntimeError("no cli")):
                run.doctor()
            codes = sorted(d["code"] for d in run.result["diagnostics"] if d["severity"] == "error")
            self.assertEqual(["doctor.dotnet", "doctor.godot", "doctor.memory", "doctor.negative-journal"], codes)
            self.assertEqual(2, run.result["steps"][-1]["exit_code"])


class NegativeJournalTests(unittest.TestCase):
    def test_an_interrupted_mutation_is_restored_from_disk(self):
        import negative_tests
        with tempfile.TemporaryDirectory() as folder:
            repo, journal = Path(folder) / "repo", Path(folder) / "journal"
            (repo / "data/shops").mkdir(parents=True)
            shop, other = repo / "data/shops/A.tres", repo / "data/shops/B.tres"
            shop.write_bytes(b"BuyMarkup = 1.5\r\n")
            other.write_bytes(b"untouched")
            saved = {"data/shops/A.tres": shop.read_bytes(), "data/shops/B.tres": other.read_bytes()}
            negative_tests.write_journal(saved, {"data/shops/A.tres"}, journal)
            shop.write_bytes(b"BuyMarkup = 0.5\r\n")          # the mutation; then the process is killed
            other.write_bytes(b"edited by the user afterwards")
            self.assertEqual(["data/shops/A.tres"], negative_tests.recover(journal, repo))
            self.assertEqual(b"BuyMarkup = 1.5\r\n", shop.read_bytes())
            self.assertEqual(b"edited by the user afterwards", other.read_bytes())   # only the mutated file is put back
            self.assertFalse(journal.exists())
            self.assertEqual([], negative_tests.recover(journal, repo))

    def test_a_journal_without_its_index_restores_nothing(self):
        import negative_tests
        with tempfile.TemporaryDirectory() as folder:
            journal = Path(folder) / "journal"
            journal.mkdir()
            (journal / "0.bin").write_bytes(b"half written")
            self.assertEqual([], negative_tests.recover(journal, Path(folder)))
            self.assertFalse(journal.exists())


class CleanTests(unittest.TestCase):
    def test_keeps_the_newest_and_recent_failures(self):
        from embervale_sdk.commands import clean
        with tempfile.TemporaryDirectory() as folder:
            parent, now = Path(folder), time.time()
            def make(day, ok, age_days, job=False):
                directory = parent / f"202601{day:02d}T000000-aaaaaaaa"
                if job:
                    write_json(directory / "job.json", {})
                    write_json(directory / "state.json", dict(state="done", exit_code=0 if ok else 1))
                else:
                    write_json(directory / "summary.json", dict(success=ok))
                os.utime(directory, (now - age_days * 86400,) * 2)
                return directory
            old_pass, old_fail = make(1, True, 30), make(2, False, 30)
            recent_fail, recent_pass, job_pass = make(3, False, 2), make(4, True, 2), make(5, True, 2, job=True)
            newest = make(6, True, 0)
            (parent / "perf-baseline").mkdir()
            (parent / "notes.txt").write_text("keep")
            doomed = clean.candidates(parent, 1, None, now)
            self.assertEqual({old_pass, old_fail, recent_pass, job_pass}, set(doomed))
            self.assertNotIn(newest, doomed)
            self.assertNotIn(recent_fail, doomed)   # a failure's evidence stays a week
            self.assertEqual({old_pass, old_fail}, set(clean.candidates(parent, 1, 10, now)))
            self.assertEqual([], clean.candidates(parent, 20, None, now))

    def test_only_run_id_directories_are_runs(self):
        with tempfile.TemporaryDirectory() as folder:
            for name in ("20260101T000000-abcdef", "20260102T000000-0123456789", "perf-baseline", "native-tests", "2026-not-a-run"):
                (Path(folder) / name).mkdir()
            self.assertEqual(["20260102T000000-0123456789", "20260101T000000-abcdef"], [p.name for p in compact.runs(Path(folder))])


class RunCommandTests(unittest.TestCase):
    def setUp(self):
        from embervale_sdk.commands import run as run_command
        self.command = run_command

    def launch(self, arguments, passthrough=(), stdout=""):
        with tempfile.TemporaryDirectory() as folder:
            run = make_run(folder, ["run", *arguments], engine=Path("godot-test.exe"))
            run.godot = Mock(return_value=Mock(stdout=stdout, returncode=0))
            self.command.run(run, run.args, list(passthrough))
            return run, run.godot.call_args

    def test_harness_names_come_from_the_games_own_table(self):
        names = self.command.harnesses()
        for name in ("hudshots", "panelshots", "spellshots", "vfxperf", "combat-shots", "combatshots"):
            self.assertIn(name, names)
        self.assertEqual([], self.command.harnesses(Path("no-such-root")))

    def test_headless_gate_and_its_result_line(self):
        line = 'noise\nEMBERVALE_RESULT {"schema":1,"gate":"state","ok":true,"facts":{"regions":6,"region_ids":["a","b"]},"failures":[]}\n'
        run, call = self.launch(["state"], stdout=line)
        self.assertEqual(("state", [], ["--state"]), call.args[:3])
        self.assertFalse(call.kwargs["render"])
        self.assertEqual(["RESULT state ok regions=6 region_ids=2 items"], run.result["brief"])
        self.assertEqual(6, run.result["metrics"]["gate_result"]["facts"]["regions"])

    def test_a_failed_report_with_exit_zero_is_an_error(self):
        line = 'EMBERVALE_RESULT {"gate":"story","ok":false,"failures":["act 2 did not start"]}'
        run, _ = self.launch(["story"], stdout=line)
        self.assertIn("GATE-FAIL act 2 did not start", run.result["brief"])
        self.assertEqual(["run.result"], [d["code"] for d in run.result["diagnostics"]])

    def test_session_harness_renders_and_starts_a_fresh_isolated_game(self):
        run, call = self.launch(["hudshots", "--env", "EMBERVALE_RES=1920x1080"], ["--quit-after=60"])
        self.assertEqual(["--hudshots", "--quit-after=60", "--new-game"], call.args[2])
        self.assertTrue(call.kwargs["render"])
        self.assertEqual("1920x1080", run.env["EMBERVALE_RES"])
        self.assertTrue(run.env["EMBERVALE_USER_DIR"].endswith("user"))
        _, call = self.launch(["hudshots"], ["--new-game", "--slot=probe"])
        self.assertEqual(["--hudshots", "--new-game", "--slot=probe"], call.args[2])
        run, call = self.launch(["hudshots", "--save", "real"])
        self.assertEqual(["--hudshots"], call.args[2])
        self.assertNotIn("EMBERVALE_USER_DIR", run.env)
        _, call = self.launch(["shellshots"])
        self.assertEqual((["--shellshots"], True), (call.args[2], call.kwargs["render"]))   # the title needs no session

    def test_a_copied_save_directory_and_refusals(self):
        with tempfile.TemporaryDirectory() as source:
            (Path(source) / "saves/slot1").mkdir(parents=True)
            (Path(source) / "saves/slot1/save.json").write_text("{}")
            with tempfile.TemporaryDirectory() as folder:
                run = make_run(folder, ["run", "hudshots", "--save", source], engine=Path("godot-test.exe"))
                run.godot = Mock(return_value=Mock(stdout="", returncode=0))
                self.command.run(run, run.args, [])
                self.assertTrue((run.artifacts / "user/saves/slot1/save.json").is_file())
                self.assertEqual(["--hudshots"], run.godot.call_args.args[2])
        for arguments in (["no-such-flag"], ["hudshots", "--save", "no-such-dir"], ["state", "--env", "PATH=x"],
                          ["state", "--env", "EMBERVALE_USER_DIR=x"], [], ["bad name"]):
            with self.assertRaises(ValueError, msg=arguments):
                self.launch(arguments)
        _, call = self.launch(["arena-probe", "--headless"])
        self.assertEqual((["--arena-probe"], False), (call.args[2], call.kwargs["render"]))


class ListAndRegistryTests(unittest.TestCase):
    def test_new_commands_are_registered_and_light_ones_make_no_run_directory(self):
        from embervale_sdk.cli import REGISTRY
        for name in ("verify", "job", "last", "logs", "run", "clean"):
            self.assertIn(name, REGISTRY)
        self.assertTrue(all(REGISTRY[name].light for name in ("verify", "job", "last", "logs", "clean", "list")))
        self.assertFalse(REGISTRY["run"].light or REGISTRY["world"].light)
        with tempfile.TemporaryDirectory() as folder:
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                self.assertEqual(0, main(["list", "--artifacts", folder]))
                self.assertEqual(2, main(["last", "--artifacts", folder]))
                self.assertEqual(2, main(["logs", "--artifacts", folder]))   # a light command's usage error is exit 2
            self.assertEqual([], list(Path(folder).iterdir()))
            lines = out.getvalue().splitlines()
            self.assertTrue(lines[0].startswith("commands: doctor "))
            self.assertLess(len(lines), 12)
            self.assertIn("story", next(l for l in lines if l.startswith("gates first reached in engine")))

    def test_list_json_is_the_whole_inventory(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            main(["list", "--json"])
        data = json.loads(out.getvalue())
        self.assertIn("wait_frames", data["capabilities"]["runtime_operations"])
        self.assertIn("verify", data["commands"])
        self.assertTrue(any(g["name"] == "story" and "engine" in g["modes"] for g in data["gates"]))

    def test_module_template_still_registers_with_the_light_flag(self):
        from embervale_sdk import commands
        module = types.ModuleType("embervale_sdk.commands.sample_light")
        module.LIGHT = True
        module.run = lambda run, args, passthrough: 7 if run is None else 0
        entry = commands.register_module(module)
        try:
            self.assertTrue(entry.light)
            self.assertEqual(7, main(["sample-light"]))
        finally:
            del commands.REGISTRY["sample-light"]



class CacheHonestyTests(unittest.TestCase):
    def test_a_pass_whose_inputs_changed_while_it_ran_is_not_cached(self):
        with tempfile.TemporaryDirectory() as folder:
            run = make_run(folder)
            states = iter([dict(entries={"src": "1"}, changed={}),                     # before the gate
                           dict(entries={"src": "1"}, changed={"src/a.cs": "edited"})])  # after it
            run.tree = None
            run.read_tree = lambda: next(states)
            run.run_gates([(python_gate("slow"), None)])
            self.assertTrue(run.result["steps"][-1]["success"])
            self.assertEqual({}, cache.GateCache(Path(folder)).entries)   # the pass proved another tree
            steady = make_run(folder)
            steady.run_gates([(python_gate("slow"), None)])
            self.assertIn("slow", cache.GateCache(Path(folder)).entries)

    def test_a_cached_pass_brings_its_warnings_back_so_strict_still_fails(self):
        with tempfile.TemporaryDirectory() as folder:
            noisy = python_gate("noisy", "print('WARNING: mesh has no LOD')")
            first = make_run(folder)
            first.run_gates([(noisy, None)])
            self.assertEqual(0, finish(first)[0])
            strict = make_run(folder, ["world", "--strict"])
            strict.run_gates([(noisy, None)])
            self.assertTrue(strict.result["steps"][-1]["cached"])
            self.assertEqual(["WARNING: mesh has no LOD"],
                             [d["message"] for d in strict.result["diagnostics"] if d["severity"] == "warning"])
            self.assertNotEqual(0, finish(strict)[0])


class OwnerIdentityTests(unittest.TestCase):
    def test_a_recycled_pid_is_not_the_owner(self):
        from quality_common import process_started, same_process
        born = process_started(os.getpid())
        self.assertIsNotNone(born)
        self.assertEqual(born, process_started(os.getpid()))
        self.assertTrue(same_process(os.getpid(), born))
        self.assertTrue(same_process(os.getpid(), None))       # an older record: the id alone decides
        self.assertFalse(same_process(os.getpid(), born + 1))  # alive, but some other process's record
        self.assertFalse(same_process(0, None))

    def test_a_lock_left_by_a_process_whose_pid_was_reused_is_taken_over(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "heavy.lock"
            # This pid is alive, but the record says its owner was born at another time: a hard-killed
            # holder whose id the system handed to someone else.
            path.write_text(json.dumps(dict(pid=os.getpid(), born=1, what="killed long ago", since=0)))
            self.assertIsNone(heavy.holder(path))
            self.assertIsNone(heavy.acquire("next", path))
            self.assertEqual("next", heavy.holder(path)["what"])

    def test_clearing_a_stale_lock_never_removes_a_fresh_one(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "heavy.lock"
            # Another waiter already replaced the stale file this one judged; its lock must survive.
            self.assertIsNone(heavy.acquire("the faster waiter", path))
            heavy.clear_stale(path, json.dumps(dict(pid=1, what="the stale one")))
            self.assertEqual("the faster waiter", heavy.holder(path)["what"])
            self.assertEqual([path.name], [item.name for item in Path(folder).iterdir()])

    def test_cancel_does_not_kill_a_stranger_that_reused_the_pid(self):
        with tempfile.TemporaryDirectory() as folder:
            directory = Path(folder)
            write_json(directory / "job.json", dict(id="J", argv=["x"]))
            write_json(directory / "state.json", dict(state="running", pid=os.getpid(), born=1, child=os.getpid(), child_born=1))
            with patch("embervale_sdk.jobs.kill_tree") as kill:
                self.assertEqual("dead", jobs.view(directory)["state"])   # its supervisor is gone
                self.assertFalse(jobs.cancel(directory))
                kill.assert_not_called()


class EngineOnlyWhenNeededTests(unittest.TestCase):
    def test_a_pure_python_run_starts_no_engine_and_takes_no_lock(self):
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, EMBERVALE_HEAVY_LOCK=str(Path(folder) / "l")):
            os.environ.pop(heavy.HELD, None)
            run = make_run(folder, ["world", "--no-build"], engine=Path("godot-test.exe"))
            run.env.pop(heavy.HELD, None)
            heavy.acquire("an engine run in another checkout")
            run.run_gates([(python_gate("pure"), None)])     # not refused, although the lock is held
            self.assertEqual(["pure"], [step["name"] for step in run.result["steps"]])
            self.assertIsNone(run.result["godot_version"])
            heavy.release()
            run.process = Mock(return_value=Mock(stdout="4.7.1.stable.mono\n", returncode=0))
            run.godot("launch", [])
            self.assertEqual("godot-version", run.process.call_args_list[0].args[0])
            self.assertEqual("4.7.1.stable.mono", run.result["godot_version"])
            run.godot("again", [])
            self.assertEqual(3, run.process.call_count)       # the version is read once
            finish(run)

    def test_an_sdk_job_leaves_the_heavy_lock_to_the_command_inside_it(self):
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, EMBERVALE_HEAVY_LOCK=str(Path(folder) / "l")):
            os.environ.pop(heavy.HELD, None)
            directory = Path(folder) / "job"
            directory.mkdir()
            show = "import os; print(os.environ.get('EMBERVALE_HEAVY_HELD'), os.environ.get('EMBERVALE_HEAVY_WAIT'))"
            write_json(directory / "job.json", dict(id="J", argv=["test", "--only", "tool"], sdk=True,
                                                    command=[PY, "-c", show], cwd=folder))
            heavy.acquire("an engine run")        # a pure-Python SDK job neither waits for it nor holds it
            with patch("embervale_sdk.jobs.costs.save"):
                self.assertEqual(0, jobs.supervise(directory))
            self.assertEqual([f"None {jobs.QUEUE_SECONDS}"], jobs.tail(directory / "output.log", 5))
            self.assertEqual("an engine run", heavy.holder()["what"])
            heavy.release()


if __name__ == "__main__":
    unittest.main()
