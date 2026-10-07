"""The `gate` SDK command: argument assembly, report merging and the run flow, without the engine."""
import json
import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))

from embervale_sdk.commands import gate  # noqa: E402


class GateArgumentTests(unittest.TestCase):
    def test_the_mode_flag_report_and_seed_are_added(self):
        self.assertEqual(["--story", "--story-mission=27", "--report=out/story.json", "--seed=7"],
                         gate.user_arguments("story", ["--story-mission=27"], "out/story.json", 7))

    def test_a_mode_flag_the_caller_gave_is_not_repeated(self):
        arguments = gate.user_arguments("arena", ["--arena=enemy.goblin", "--trials=5"], "a.json", 1)
        self.assertEqual(["--arena=enemy.goblin", "--trials=5", "--report=a.json", "--seed=1"], arguments)

    def test_a_report_mode_gets_no_seed_and_the_callers_report_and_seed_win(self):
        self.assertEqual(["--state", "--ids=quests", "--report=s.json"],
                         gate.user_arguments("state", ["--ids=quests"], "s.json", 9))
        self.assertEqual(["--lifecycle", "--report=mine.json", "--seed=3"],
                         gate.user_arguments("lifecycle", ["--report=mine.json", "--seed=3"], "l.json", 9))

    def test_only_the_arena_fixes_the_frame_rate(self):
        self.assertEqual(["--fixed-fps", "60"], gate.ENGINE_ARGUMENTS["arena"])
        self.assertNotIn("story", gate.ENGINE_ARGUMENTS)


class GateReportTests(unittest.TestCase):
    def test_facts_become_metrics_and_warnings_become_diagnostics(self):
        result = dict(metrics={}, diagnostics=[])
        code = gate.merge_report(result, "validate", dict(
            exit_code=1, facts=dict(issues=2, partial=True), failures=["[items/Items] x"],
            warnings=["filtered run (--only/--skip): not the gate."]))
        self.assertIsNone(code)
        self.assertEqual(dict(issues=2, partial=True), result["metrics"]["validate"])
        self.assertEqual([("warning", "gate.validate", "filtered run (--only/--skip): not the gate.")],
                         [(d["severity"], d["code"], d["message"]) for d in result["diagnostics"]])

    def test_a_refused_run_is_a_configuration_error(self):
        self.assertEqual(2, gate.merge_report(dict(metrics={}, diagnostics=[]), "arena", dict(exit_code=2)))


class FakeRun:
    def __init__(self, folder, write, exit_code=0):
        self.artifacts = Path(folder)
        self.result = dict(metrics={}, diagnostics=[], steps=[])
        self.calls = []
        self.issues = []
        self._write = write
        self._exit_code = exit_code

    def godot(self, name, arguments, user=()):
        self.calls.append((name, list(arguments), list(user)))
        self.result["steps"].append(dict(name=name, exit_code=self._exit_code))
        if self._write is not None:
            (self.artifacts / f"{name}.json").write_text(json.dumps(self._write), encoding="utf-8")

    def issue(self, code, message):
        self.issues.append(code)

    def brief(self, line):
        self.result.setdefault("brief", []).append(line)


class GateRunTests(unittest.TestCase):
    def test_the_compact_output_carries_the_matchup_rows_and_the_report_warnings(self):
        with tempfile.TemporaryDirectory() as folder:
            row = dict(enemy="enemy.goblin", trials=1, wins=0, losses=1, ttk_s=dict(p50=None))
            run = FakeRun(folder, dict(exit_code=0, facts=dict(matchups=[row]), warnings=["no ground"]))
            gate.run(run, types.SimpleNamespace(target="arena", seed=7), [])
            brief = run.result["brief"]
            self.assertIn("enemy.goblin: win 0/1 loss 1", brief[1])
            self.assertIn("ttk p50 -s", brief[1])
            self.assertEqual("  warning: no ground", brief[-1])

    def test_the_arena_runs_at_a_fixed_frame_rate_and_its_facts_are_merged(self):
        with tempfile.TemporaryDirectory() as folder:
            run = FakeRun(folder, dict(exit_code=0, facts=dict(matchups=[dict(enemy="enemy.goblin", wins=3)]), warnings=[]))
            gate.run(run, types.SimpleNamespace(target="arena", seed=5), ["--arena=enemy.goblin"])
            name, engine, user = run.calls[0]
            self.assertEqual(("arena", ["--fixed-fps", "60"]), (name, engine))
            self.assertEqual("--arena=enemy.goblin", user[0])
            self.assertIn("--seed=5", user)
            self.assertEqual(3, run.result["metrics"]["arena"]["matchups"][0]["wins"])
            self.assertEqual(0, run.result["steps"][-1]["exit_code"])

    def test_a_refusal_marks_the_step_as_a_configuration_error(self):
        with tempfile.TemporaryDirectory() as folder:
            run = FakeRun(folder, dict(exit_code=2, facts={}, warnings=[]), exit_code=1)
            gate.run(run, types.SimpleNamespace(target="validate", seed=5), ["--only=itmes"])
            self.assertEqual(2, run.result["steps"][-1]["exit_code"])

    def test_a_clean_exit_without_a_report_is_reported(self):
        with tempfile.TemporaryDirectory() as folder:
            run = FakeRun(folder, None)
            gate.run(run, types.SimpleNamespace(target="state", seed=5), [])
            self.assertEqual(["gate.no_report"], run.issues)

    def test_an_unknown_mode_is_a_usage_error(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ValueError):
                gate.run(FakeRun(folder, None), types.SimpleNamespace(target="nonsense", seed=5), [])

    def test_the_command_is_registered_and_takes_passthrough(self):
        from embervale_sdk import commands
        from embervale_sdk.cli import COMMANDS
        self.assertIn("gate", COMMANDS)
        self.assertTrue(commands.REGISTRY["gate"].passthrough)
        with patch("sys.stderr"):
            from embervale_sdk.cli import parser
            self.assertEqual("arena", parser("gate").parse_args(["gate", "arena"]).target)


if __name__ == "__main__":
    unittest.main()
