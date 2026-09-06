"""Tool regression tests; run via embervale.py test. No engine, renderer or network required."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

from quality_common import run_process, discover_godot, write_json
from embervale_sdk.contract import diagnostics_from_log, exit_code
from embervale_sdk.scenario import validate_plan, resource_path
from embervale_sdk.changed import changed_paths
from embervale_sdk.authoring import validate_author_plan


class ProcessTests(unittest.TestCase):
    def test_stdout_stderr_and_space_unicode_arguments(self):
        result = run_process([sys.executable, "-c", "import sys; print(sys.argv[1]); print('bad', file=sys.stderr)", "a space λ"], timeout=10)
        self.assertEqual(0, result.returncode, result.launch_error)
        self.assertEqual("a space λ", result.stdout.strip())
        self.assertEqual("bad", result.stderr.strip())

    def test_launch_failure(self):
        result = run_process(["embervale-does-not-exist-binary"], timeout=2)
        self.assertEqual(127, result.returncode)
        self.assertTrue(result.launch_error)

    def test_timeout_keeps_partial_output(self):
        before = time.monotonic()
        result = run_process([sys.executable, "-u", "-c", "import time; print('alive'); time.sleep(60)"], timeout=2)
        self.assertTrue(result.timed_out)
        self.assertIn("alive", result.stdout)
        self.assertLess(time.monotonic() - before, 8)

    def test_child_cleanup_after_parent_completes(self):
        with tempfile.TemporaryDirectory(prefix="Embervale process ") as folder:
            sentinel = Path(folder) / "child-survived.txt"
            child = "import time,pathlib; time.sleep(1.5); pathlib.Path(" + repr(str(sentinel)) + ").write_text('leak')"
            parent = "import subprocess,sys; subprocess.Popen([sys.executable,'-c'," + repr(child) + "],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)"
            result = run_process([sys.executable, "-c", parent], timeout=5)
            self.assertEqual(0, result.returncode, result.launch_error)
            time.sleep(2)
            self.assertFalse(sentinel.exists(), "child escaped process ownership")

    def test_child_cleanup_on_timeout(self):
        result = run_process([sys.executable, "-u", "-c",
            "import subprocess,sys,time; subprocess.Popen([sys.executable,'-c','import time;time.sleep(60)']); print('spawned'); time.sleep(60)"], timeout=3)
        self.assertTrue(result.timed_out)
        self.assertIn("spawned", result.stdout)

    def test_parent_deadline_propagates(self):
        env = dict(os.environ, EMBERVALE_DEADLINE=str(time.time() - 1))
        result = run_process([sys.executable, "-c", "raise Exception('must not run')"], timeout=5, env=env)
        self.assertTrue(result.timed_out)
        self.assertNotIn("Exception", result.stderr)

    def test_invalid_explicit_engine_does_not_fall_back(self):
        with patch.dict(os.environ, EMBERVALE_GODOT="missing-not-a-godot.exe"):
            self.assertIsNone(discover_godot())


class ContractTests(unittest.TestCase):
    def test_nested_assertion_exit_is_preserved(self):
        self.assertEqual(5, exit_code(dict(steps=[{"exit_code": 5}], assertions=[], diagnostics=[])))

    def test_world_source_identity_survives_git_line_ending_conversion(self):
        from world_bake import source_digest, digest
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "source.cs"
            path.write_bytes(b"first\nsecond\n")
            source, raw = source_digest(path), digest(path)
            path.write_bytes(b"first\r\nsecond\r\n")
            self.assertEqual(source, source_digest(path))
            self.assertNotEqual(raw, digest(path))
            path.write_bytes(b"first\r\nCHANGED\r\n")
            self.assertNotEqual(source, source_digest(path))

    def test_runtime_default_does_not_override_specialist_capture_wait(self):
        from embervale_sdk.cli import Run, parser
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, {}, clear=True):
            with patch("embervale_sdk.cli.discover_godot", return_value=None):
                run = Run(parser().parse_args(["world", "--artifacts", folder]))
                self.assertEqual(120, run.args.frames)
                self.assertNotIn("EMBERVALE_FRAMES", run.env)
                run = Run(parser().parse_args(["world", "--frames", "12", "--artifacts", folder]))
                self.assertEqual("12", run.env["EMBERVALE_FRAMES"])

    def test_repeated_warnings_retain_occurrence_count(self):
        ds = diagnostics_from_log("WARNING: repeated\nWARNING: repeated\nWARNING: repeated", "runtime")
        self.assertEqual(1, len(ds))
        self.assertEqual(3, ds[0]["count"])

    def test_zero_errors_not_a_failure(self):
        self.assertEqual([], diagnostics_from_log("Build succeeded.\n0 errors\n0 warnings", "build"))

    def test_runtime_error_even_with_zero_exit(self):
        ds = diagnostics_from_log("SCRIPT ERROR: Parse Error at res://tools/broken.gd:3", "runtime")
        self.assertEqual("error", ds[0]["severity"])
        self.assertIn("broken.gd", ds[0]["path"])
        self.assertEqual(1, exit_code(dict(steps=[], diagnostics=ds, assertions=[])))

    def test_exit_priority(self):
        result = dict(steps=[], diagnostics=[], assertions=[dict(success=False)])
        self.assertEqual(5, exit_code(result))
        result["steps"] = [dict(exit_code=3)]
        self.assertEqual(3, exit_code(result))

    def test_strict_only_changes_warning_status(self):
        result = dict(steps=[], diagnostics=[dict(severity="warning")], assertions=[])
        self.assertEqual(0, exit_code(result))
        self.assertEqual(1, exit_code(result, strict=True))

    def test_atomic_json(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "summary.json"
            write_json(path, {"success": False, "message": "λ"})
            self.assertEqual("λ", json.loads(path.read_text(encoding="utf-8"))["message"])
            self.assertFalse(path.with_suffix(".json.tmp").exists())


class ScenarioTests(unittest.TestCase):
    def plan(self, step):
        return validate_plan(dict(steps=[step]))

    def test_no_eval_or_arbitrary_method(self):
        for step in [{"op": "eval", "code": "quit()"},
                     {"op": "call_method", "node": "/root/Main", "method": "free"},
                     {"op": "call_method", "node": "/root/Main", "method": "call"}]:
            with self.assertRaises(ValueError): self.plan(step)

    def test_no_script_assignment(self):
        with self.assertRaises(ValueError):
            self.plan(dict(op="set_property", node="/root/Main", property="script", value="bad"))

    def test_no_resource_path_traversal(self):
        for path in ("res://../outside.tscn", "C:/scene.tscn", "user://scene.tscn", "res://a\\b.tscn"):
            with self.assertRaises(ValueError): resource_path(path)

    def test_invalid_vectors_and_capture_names(self):
        for value in ([1, 2], [1, 2, float("nan")], "Vector3(0,0,0)"):
            with self.assertRaises(ValueError):
                self.plan(dict(op="set_property", node="/root/Main", property="position", value=value))
        with self.assertRaises(ValueError): self.plan(dict(op="capture_screenshot", name="../escape"))

    def test_finite_waits(self):
        for frames in (-1, 100001, "3", True):
            with self.assertRaises(ValueError): self.plan(dict(op="wait_frames", frames=frames))

    def test_valid_scenarios(self):
        for path in (Path(__file__).parent / "headless/scenarios").glob("*.json"):
            validate_plan(json.loads(path.read_text()))


class ChangedTests(unittest.TestCase):
    def test_staged_unstaged_untracked_deleted_and_spaces(self):
        with tempfile.TemporaryDirectory(prefix="Embervale git ") as folder:
            root = Path(folder)
            def git(*args):
                result = run_process(["git", *args], timeout=10, cwd=root)
                self.assertEqual(0, result.returncode, result.output)
            git("init", "-q")
            git("config", "user.email", "sdk-test@example.invalid")
            git("config", "user.name", "SDK tests")
            for name in ("staged.tscn", "changed.tres", "deleted.tres"):
                (root / name).write_text("before")
            git("add", ".")
            git("commit", "-qm", "fixture")
            (root / "staged.tscn").write_text("after")
            git("add", "staged.tscn")
            (root / "changed.tres").write_text("after")
            (root / "deleted.tres").unlink()
            (root / "space name.tscn").write_text("new")
            paths, full = changed_paths(root=root)
            self.assertEqual(["changed.tres", "deleted.tres", "space name.tscn", "staged.tscn"], paths)
            self.assertFalse(full)
            (root / "global.cs").write_text("new")
            self.assertTrue(changed_paths(root=root)[1])


class AuthoringTests(unittest.TestCase):
    def test_publication_backup_and_concurrent_edit_guard(self):
        from embervale_sdk.cli import Run, parser
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            plan = root / "plan.json"
            plan.write_text('{"operations": []}')
            target = root / "scenes/fixture.tscn"
            target.parent.mkdir()
            target.write_text("original")
            args = parser().parse_args(["author", str(plan), "--write", "res://scenes/fixture.tscn", "--overwrite"])
            with patch("embervale_sdk.cli.ROOT", root), patch("embervale_sdk.cli.discover_godot", return_value=None):
                run = Run(args)
                def stage(*unused):
                    staged = run.artifacts / "authored.tscn"
                    staged.write_text("replacement")
                    run.result["metrics"]["author"] = {"staged_path": str(staged)}
                with patch.object(run, "runtime", side_effect=stage):
                    run.author()
                self.assertEqual("replacement", target.read_text())
                self.assertEqual("original", (run.artifacts / "before.tscn").read_text())
                def concurrent(*unused):
                    stage()
                    target.write_text("someone else's edit")
                with patch.object(run, "runtime", side_effect=concurrent):
                    with self.assertRaisesRegex(ValueError, "changed during authoring"):
                        run.author()
                self.assertEqual("someone else's edit", target.read_text())
                args.overwrite = False
                with self.assertRaisesRegex(ValueError, "overwrite is required"):
                    run.author()

    def test_workshop_plan(self):
        validate_author_plan(json.loads((Path(__file__).parent / "headless/examples/workshop.json").read_text()))

    def test_no_script_class_or_script_property(self):
        for operation in [dict(op="create_node", id="x", type="EditorScript"),
                          dict(op="customize", target="root", properties={"script": "bad"}),
                          dict(op="create_resource", id="x", source="res://src/Bad.cs")]:
            with self.assertRaises(ValueError):
                validate_author_plan(dict(operations=[operation]))

    def test_no_removing_root_or_escaping_parent(self):
        for operation in [dict(op="remove_node", target="root"),
                          dict(op="create_node", id="x", parent="../elsewhere")]:
            with self.assertRaises(ValueError):
                validate_author_plan(dict(operations=[operation]))

    def test_unique_ids(self):
        with self.assertRaises(ValueError):
            validate_author_plan(dict(operations=[dict(op="create_node", id="x"), dict(op="create_node", id="x")]))


if __name__ == "__main__":
    unittest.main()
