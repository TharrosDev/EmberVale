"""Tests for the SDK `console` command (tools/embervale_sdk/commands/console.py). No engine required."""
import json
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

from embervale_sdk.cli import COMMANDS, main, parser
from embervale_sdk.commands import console


def arguments(*argv):
    return parser("console").parse_args(["console", *argv])


class FakeRun:
    def __init__(self, artifacts):
        self.artifacts = artifacts
        self.result = dict(metrics={}, assertions=[], diagnostics=[])
        self.notes = []

    def note(self, message):
        self.notes.append(message)

    def issue(self, code, message, severity="error", path=None):
        self.result["diagnostics"].append(dict(code=code, message=message, severity=severity))


class ConsoleCommandTests(unittest.TestCase):
    def test_registered_with_its_own_flags(self):
        self.assertIn("console", COMMANDS)
        args = arguments("pos; inv", "--stop-on-fail", "--exec-timeout", "30", "--quiet")
        self.assertEqual(("pos; inv", True, 30.0, True), (args.target, args.stop_on_fail, args.exec_timeout, args.quiet))
        with self.assertRaises(SystemExit), patch("sys.stderr"):
            parser("smoke").parse_args(["smoke", "--fixture", "x"])

    def test_script_comes_from_exactly_one_place(self):
        self.assertEqual("pos", console.script_text(arguments("pos")))
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "s.txt"
            path.write_text("tp out\nassert player.safe eq false\n", encoding="utf-8")
            self.assertIn("tp out", console.script_text(arguments("--file", str(path))))
            with self.assertRaises(ValueError):
                console.script_text(arguments("pos", "--file", str(path)))
            path.write_text("  \n", encoding="utf-8")
            with self.assertRaises(ValueError):
                console.script_text(arguments("--file", str(path)))
        with self.assertRaises(ValueError):
            console.script_text(arguments())

    def test_new_game_session_arguments(self):
        with tempfile.TemporaryDirectory() as folder:
            user = console.session_arguments(arguments("pos", "--exec-timeout", "60", "--stop-on-fail"),
                                             folder, Path("s.txt"), Path("r.json"))
        self.assertEqual(["--new-game", "--exec-file=s.txt", "--exec-timeout=60", "--report=r.json",
                          "--quit-after=90", "--exec-stop-on-fail"], user)
        self.assertNotIn("--exec-verbose", user)
        with self.assertRaises(ValueError):
            console.session_arguments(arguments("pos", "--exec-timeout", "0"), ".", Path("s"), Path("r"))

    def test_fixture_slot_is_copied_into_the_isolated_user_directory(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            slot = root / "fixtures" / "mid_game"
            slot.mkdir(parents=True)
            (slot / "save.json").write_text("{}", encoding="utf-8")
            (slot / "header.json").write_text("{}", encoding="utf-8")
            user_dir = root / "user"
            user_dir.mkdir()
            user = console.session_arguments(arguments("pos", "--fixture", str(slot), "--game-log"),
                                             user_dir, Path("s.txt"), Path("r.json"))
            self.assertEqual(["--play", "--slot=mid_game"], user[:2])
            self.assertIn("--exec-verbose", user)
            self.assertNotIn("--new-game", user)
            self.assertTrue((user_dir / "saves" / "mid_game" / "save.json").is_file())
            self.assertTrue((slot / "save.json").is_file(), "the fixture itself is untouched")

            (root / "empty").mkdir()
            with self.assertRaises(ValueError):
                console.session_arguments(arguments("pos", "--fixture", str(root / "empty")), user_dir, Path("s"), Path("r"))
            bad = root / "bad name"
            bad.mkdir()
            (bad / "save.json").write_text("{}", encoding="utf-8")
            with self.assertRaises(ValueError):
                console.session_arguments(arguments("pos", "--fixture", str(bad)), user_dir, Path("s"), Path("r"))

    def test_results_are_read_and_folded(self):
        with tempfile.TemporaryDirectory() as folder:
            results = Path(folder) / "console" / "result.ndjson"
            results.parent.mkdir()
            results.write_text("\n".join([
                json.dumps(dict(i=1, cmd="pos", ok=True, frame=10, out="ember_crown / hub\nmore", data=dict(x=1.5))),
                "not json",
                json.dumps(dict(i=2, cmd="assert enemies.count ge 3", ok=False, frame=11, out="enemies.count=0")),
                json.dumps(dict(event="result", ok=False, steps=2, ran=2, failed=1, invariants=0)),
            ]), encoding="utf-8")
            statements, summary = console.read_results(results)
            self.assertEqual(2, len(statements))
            self.assertEqual(1, summary["failed"])

            run = FakeRun(folder)
            console.fold(run, statements, summary)
            metrics = run.result["metrics"]["console"]
            self.assertEqual((2, 1, True), (metrics["statements"], metrics["failed"], metrics["complete"]))
            self.assertEqual(dict(x=1.5), metrics["steps"][0]["data"])
            self.assertEqual([False], [a["success"] for a in run.result["assertions"]])
            self.assertEqual("enemies.count=0", run.result["assertions"][0]["actual"])
            self.assertEqual(["ok   1 pos -> ember_crown / hub …", "FAIL 2 assert enemies.count ge 3 -> enemies.count=0"], run.notes)
            self.assertEqual([], run.result["diagnostics"])

            quiet = FakeRun(folder)
            console.fold(quiet, statements, summary, quiet=True)
            self.assertEqual(1, len(quiet.notes))

    def test_a_run_without_a_result_line_is_incomplete(self):
        with tempfile.TemporaryDirectory() as folder:
            self.assertEqual(([], None), console.read_results(Path(folder) / "missing.ndjson"))
            run = FakeRun(folder)
            console.fold(run, [], None)
            self.assertEqual(["console.incomplete"], [d["code"] for d in run.result["diagnostics"]])

    def test_long_lines_are_cut(self):
        line = console.line_for(dict(i=1, cmd="flag list", ok=True, out="x" * 500), width=80)
        self.assertEqual(80, len(line))
        self.assertTrue(line.endswith("…"))
        self.assertEqual("ok   3 heal", console.line_for(dict(i=3, cmd="heal", ok=True, out="")))

    def test_scenario_console_operation_is_validated(self):
        from embervale_sdk.scenario import validate_plan
        validate_plan(dict(steps=[dict(op="console", line="give item.currency.gold 50")]))
        for bad in (dict(op="console"), dict(op="console", line="  "), dict(op="console", line="a\nb"),
                    dict(op="console", line="x" * 401), dict(op="console", line=7)):
            with self.assertRaises(ValueError):
                validate_plan(dict(steps=[bad]))
        driver = (Path(__file__).parent / "headless" / "driver.gd").read_text(encoding="utf-8")
        self.assertIn('"console":', driver)
        self.assertIn('"ExecuteJson"', driver)

    def test_end_to_end_with_a_stubbed_engine(self):
        """The whole command with the engine launch replaced: the script file, the arguments and the exit code."""
        seen = {}

        def fake_godot(self, name, arguments, user=(), render=False, scan=True):
            seen.update(name=name, engine=list(arguments), user=list(user), render=render)
            out = Path(self.artifacts) / "console"
            out.mkdir()
            (out / "result.ndjson").write_text("\n".join([
                json.dumps(dict(i=1, cmd="tp out", ok=True, frame=5, out="teleporting")),
                json.dumps(dict(i=2, cmd="assert player.safe eq false", ok=False, frame=90, out="player.safe=true")),
                json.dumps(dict(event="result", ok=False, steps=2, ran=2, failed=1, invariants=0)),
            ]), encoding="utf-8")
            self.result["steps"].append(dict(name=name, exit_code=1))
            return types.SimpleNamespace(stdout="", returncode=1)

        with tempfile.TemporaryDirectory() as folder, patch("embervale_sdk.cli.Run.godot", fake_godot), \
                patch("embervale_sdk.cli.discover_godot", return_value=None), patch("sys.stdout", MagicMock()):
            code = main(["console", "tp out; assert player.safe eq false", "--json", "--artifacts", folder])
            run_dir = next(Path(folder).iterdir())
            summary = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
            self.assertEqual("tp out; assert player.safe eq false", (run_dir / "console-script.txt").read_text(encoding="utf-8"))
        self.assertEqual(5, code)
        self.assertEqual("console", seen["name"])
        self.assertEqual(["--fixed-fps", "60"], seen["engine"])
        self.assertEqual("--new-game", seen["user"][0])
        self.assertTrue(seen["user"][1].startswith("--exec-file=") and seen["user"][1].endswith("console-script.txt"))
        self.assertFalse(seen["render"])
        self.assertEqual(1, summary["metrics"]["console"]["failed"])
        self.assertFalse(summary["success"])


if __name__ == "__main__":
    unittest.main()
