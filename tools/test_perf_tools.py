"""Unit tests for the perf comparer, the analytics reader and the two SDK perf commands (no engine)."""
import argparse
import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import analytics
import perf_compare
from embervale_sdk import cli
from embervale_sdk.commands import REGISTRY, perf_report, vfxperf


def write(directory, name, payload):
    path = Path(directory) / name
    path.write_text(json.dumps(payload), encoding="utf-8")
    return path


def session(**facts):
    """An EMBERVALE_RESULT file as --perf-report writes it."""
    base = dict(suite="session", mode="new-game", region="region.ember_crown", frames=1200, seconds=20.0,
                frame_ms_p50=10.0, frame_ms_p95=14.0, frame_ms_max=30.0, hitches_gt33=0, draw_calls=900,
                static_mb=400.0, violations=0, log_errors=0, adapter="Test GPU", headless=False)
    base.update(facts)
    return dict(schema=1, gate="perf-report", ok=True, exit_code=0, elapsed_ms=1, facts=base, failures=[], warnings=[])


class FlattenAndRules(unittest.TestCase):
    def test_flatten_keeps_only_finite_numbers_under_dotted_keys(self):
        flat = perf_compare.flatten({"a": {"p95": 2, "name": "x", "list": [1, 2]}, "ok": True, "b": 1.5,
                                     "bad": float("nan")})
        self.assertEqual(flat, {"a.p95": 2.0, "b": 1.5})

    def test_body_is_the_facts_of_a_result_file_and_the_document_otherwise(self):
        self.assertEqual(perf_compare.body(session())["frames"], 1200)
        self.assertEqual(perf_compare.body({"frames": 3, "facts": "not a result"})["frames"], 3)

    def test_rules_by_key_name(self):
        expected = {
            "frame_ms_p95": "lower", "frameMs.p95": "lower", "steadyMs.frames": "info", "frames": "info",
            "seconds": "info", "over_budget_seconds": "lower", "world_worst_second_ms": "lower",
            "elapsed_ms": "info", "draw_calls": "lower", "peakDrawCalls": "lower", "static_memory_bytes": "lower",
            "video_mb": "lower", "orphan_nodes": "lower", "gc_gen2": "lower", "violations": "lower",
            "casts": "higher", "active_cells": "info", "regions.ember.cell_count": "info", "someNewThing": "info",
            # An id used as a key must not decide: the metric's own name does.
            "castsBySpell.spell.ball_lightning": "higher", "firstUseMaxMsBySpell.spell.sunfall": "lower",
            "regions.ember.cell_ms.lighthouse_seed_w": "lower",
        }
        for key, direction in expected.items():
            self.assertEqual(perf_compare.rule_for(key)[0], direction, key)

    def test_every_default_rule_is_well_formed(self):
        for pattern, direction, floor in perf_compare.RULES:
            self.assertEqual(pattern, pattern.lower())
            self.assertIn(direction, ("lower", "higher", "info"))
            self.assertGreaterEqual(floor, 0)


class Compare(unittest.TestCase):
    def test_regression_needs_both_the_tolerance_and_the_floor(self):
        base = {"frame_ms_p95": 14.0, "frame_ms_p50": 1.0, "draw_calls": 900.0, "casts": 40.0, "frames": 1200.0}
        now = {"frame_ms_p95": 17.0,    # +21%, +3 ms: a regression
               "frame_ms_p50": 1.4,     # +40% but only 0.4 ms: under the floor
               "draw_calls": 905.0,     # within tolerance
               "casts": 20.0,           # fewer casts is worse
               "frames": 5.0,           # informational
               "hitches_gt33": 3.0}     # not in the baseline
        result = perf_compare.compare(now, base, 0.10)
        self.assertEqual([e["key"] for e in result["regress"]], ["casts", "frame_ms_p95"])
        self.assertEqual(result["regress"][1]["percent"], 21.4)
        self.assertEqual((result["same"], result["info"], result["new"], result["missing"]), (2, 1, 1, 0))

    def test_improvement_and_a_zero_baseline(self):
        result = perf_compare.compare({"frame_ms_p95": 10.0, "violations": 1.0}, {"frame_ms_p95": 14.0, "violations": 0.0})
        self.assertEqual([e["key"] for e in result["improve"]], ["frame_ms_p95"])
        self.assertEqual([e["key"] for e in result["regress"]], ["violations"])

    def test_median_values_uses_keys_every_run_has(self):
        merged = perf_compare.median_values([{"a": 1.0, "b": 9.0}, {"a": 5.0, "b": 9.0}, {"a": 2.0}])
        self.assertEqual(merged, {"a": 2.0})

    def test_identity_and_machine_key(self):
        self.assertEqual(perf_compare.identity(session()), ("session", "new-game-region.ember_crown"))
        vfx = {"suite": "--vfxperf", "tier": "ultra", "view": "wide", "reducedMotion": True}
        self.assertEqual(perf_compare.identity(vfx), ("vfxperf", "ultra-wide-reduced"))
        self.assertEqual(perf_compare.identity({"x": 1}, "C:/runs/performance.json"), ("perf", "performance"))
        self.assertEqual(perf_compare.identity(session(), key="My Key!"), ("session", "my-key"))
        one, other = perf_compare.machine_id(session()), perf_compare.machine_id(session(adapter="Other GPU"))
        self.assertRegex(one, r"^[0-9a-f]{8}$")
        self.assertNotEqual(one, other)


class Baselines(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        patcher = mock.patch.object(perf_compare, "BASELINES", Path(self.directory.name) / "baselines")
        patcher.start()
        self.addCleanup(patcher.stop)

    def run_main(self, *argv):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = perf_compare.main([str(a) for a in argv])
        return code, out.getvalue().strip().splitlines()

    def test_no_baseline_then_update_then_compare(self):
        current = write(self.directory.name, "run.json", session())
        code, lines = self.run_main(current)
        self.assertEqual(code, 0)
        self.assertIn("NO BASELINE", lines[0])
        self.assertEqual(self.run_main(current, "--require-baseline")[0], 2)

        code, lines = self.run_main(current, "--update")
        self.assertEqual(code, 0)
        self.assertIn("UPDATED", lines[0])
        stored = list((Path(self.directory.name) / "baselines/session").glob("new-game-region.ember_crown.*.json"))
        self.assertEqual(len(stored), 1)
        self.assertEqual(json.loads(stored[0].read_text())["values"]["frame_ms_p95"], 14.0)

        code, lines = self.run_main(current)
        self.assertEqual((code, len(lines)), (0, 1))
        self.assertIn("0 regress, 0 improve", lines[0])

        slower = write(self.directory.name, "slow.json", session(frame_ms_p95=20.0, hitches_gt33=9))
        code, lines = self.run_main(slower)
        self.assertEqual(code, perf_compare.EXIT_REGRESSION)
        self.assertEqual(lines[0], "REGRESS frame_ms_p95 14.00 -> 20.00 +42.9% (limit 10%)")
        self.assertTrue(lines[1].startswith("REGRESS hitches_gt33 0.00 -> 9.00"))
        self.assertIn("2 regress", lines[-1])

    def test_json_output_is_one_line(self):
        current = write(self.directory.name, "run.json", session())
        self.run_main(current, "--update")
        code, lines = self.run_main(write(self.directory.name, "slow.json", session(frame_ms_p95=30.0)), "--json")
        self.assertEqual(len(lines), 1)
        document = json.loads(lines[0])
        self.assertEqual((code, document["exit_code"]), (5, 5))
        self.assertEqual(document["verdicts"][0]["regress"][0]["key"], "frame_ms_p95")

    def test_update_refuses_a_failed_or_stale_run(self):
        failed = session()
        failed["ok"] = False
        code, lines = self.run_main(write(self.directory.name, "failed.json", failed), "--update")
        self.assertEqual(code, 2)
        self.assertIn("REFUSED", lines[0])
        stale = write(self.directory.name, "stale.json", session(stale_binary=True))
        self.assertEqual(self.run_main(stale, "--update")[0], 2)
        self.assertEqual(self.run_main(stale, "--update", "--force")[0], 0)

    def test_a_baseline_from_another_machine_is_incomparable_not_a_regression(self):
        current = write(self.directory.name, "run.json", session(frame_ms_p95=99.0))
        foreign = write(self.directory.name, "foreign.json", dict(machine_id="deadbeef", values={"frame_ms_p95": 1.0}))
        code, lines = self.run_main(current, "--baseline", foreign)
        self.assertEqual(code, 0)
        self.assertIn("INCOMPARABLE", lines[0])
        self.assertEqual(self.run_main(current, "--baseline", foreign, "--force")[0], 5)

    def test_two_raw_runs_compare_directly_and_repeats_take_the_median(self):
        old = write(self.directory.name, "old.json", {"p95_frame_ms": 10.0, "frames": 120})
        runs = [write(self.directory.name, f"r{i}.json", {"p95_frame_ms": value, "frames": 120})
                for i, value in enumerate((10.2, 30.0, 10.4))]
        self.assertEqual(self.run_main(*runs, "--baseline", old, "--median")[0], 0)   # median 10.4
        self.assertEqual(self.run_main(runs[1], "--baseline", old)[0], 5)

    def test_a_run_with_too_few_frames_is_not_judged(self):
        # The measured case: one stalled run got 19 frames in 5 s and a p50 of 254 ms against 7 ms.
        old = write(self.directory.name, "old.json", {"frame_ms_p50": 7.34, "frames": 600})
        stalled = write(self.directory.name, "stalled.json", {"frame_ms_p50": 254.56, "frames": 19})
        code, lines = self.run_main(stalled, "--baseline", old)
        self.assertEqual(code, 0)
        self.assertIn("INCOMPARABLE: 19 frames", lines[0])
        self.assertEqual(self.run_main(stalled, "--baseline", old, "--require-baseline")[0], 2)
        self.assertEqual(self.run_main(stalled, "--baseline", old, "--force")[0], 5)
        self.assertEqual(self.run_main(stalled, "--baseline", old, "--min-frames", "10")[0], 5)

        # A short baseline is as useless as a short run, and one is never recorded.
        fine = write(self.directory.name, "fine.json", {"frame_ms_p50": 7.0, "frames": 600})
        code, lines = self.run_main(fine, "--baseline", stalled)
        self.assertEqual(code, 0)
        self.assertIn("INCOMPARABLE: 19 frames (the baseline", lines[0])
        code, lines = self.run_main(stalled, "--update")
        self.assertEqual(code, 2)
        self.assertIn("REFUSED", lines[0])

        # A document that does not say how many frames it sampled is judged as before.
        self.assertIsNone(perf_compare.too_few_frames({"cell_ms.a": 3.0}))
        self.assertEqual(perf_compare.frame_count({"frameMs.frames": 240.0, "steadyMs.frames": 12.0}), 240.0)

    def test_unreadable_input_is_exit_2(self):
        bad = Path(self.directory.name) / "bad.json"
        bad.write_text("{", encoding="utf-8")
        self.assertEqual(self.run_main(bad)[0], 2)
        self.assertEqual(self.run_main(Path(self.directory.name) / "absent.json")[0], 2)


NEW_SESSION = [
    {"type": "session_start", "t": 100.0, "pt": 0.0, "args": "--new-game", "automation": True, "headless": True},
    {"type": "quest_start", "t": 101.0, "pt": 1.0, "quest": "quest.intro", "region": "region.ember_crown"},
    {"type": "quest_start", "t": 102.0, "pt": 2.0, "quest": "quest.lost"},
    {"type": "death", "t": 110.0, "pt": 10.0, "entity": "enemy.goblin", "killer": "player"},
    {"type": "death", "t": 111.0, "pt": 11.0, "entity": "enemy.goblin", "killer": "player"},
    {"type": "death", "t": 112.0, "pt": 12.0, "entity": "enemy.wolf", "killer": "companion.kael"},
    {"type": "death", "t": 120.0, "pt": 20.0, "entity": "player", "player": True, "killer": "enemy.troll",
     "region": "region.ember_crown"},
    {"type": "gold", "t": 121.0, "pt": 21.0, "delta": 40, "total": 40, "source": "pickup"},
    {"type": "gold", "t": 122.0, "pt": 22.0, "delta": -15, "total": 25, "source": "menu"},
    {"type": "level_up", "t": 123.0, "pt": 23.0, "level": 3},
    {"type": "quest_complete", "t": 190.0, "pt": 61.0, "quest": "quest.intro", "seconds": 60.0},
    {"type": "session_end", "t": 200.0, "pt": 70.0, "player_deaths": 1, "damage": [
        {"source": "player", "target": "enemy.goblin", "hits": 12, "total": 340.5},
        {"source": "enemy.troll", "target": "player", "hits": 3, "total": 90.0},
        {"source": "enemy.goblin", "target": "companion.kael", "hits": 2, "total": 11.0}]},
]

OLD_SESSION = [  # before pt, seconds, session_start and session_end existed
    {"type": "quest_start", "t": 1000.0, "quest": "quest.intro"},
    {"type": "death", "t": 1010.0, "entity": "Player", "killer": ""},
    {"type": "quest_complete", "t": 1100.0, "quest": "quest.intro"},
]


class Analytics(unittest.TestCase):
    def test_summary_of_a_new_session(self):
        summary = analytics.summarize([NEW_SESSION])
        self.assertEqual((summary["sessions"], summary["automation"], summary["rows"]), (1, 1, len(NEW_SESSION)))
        self.assertEqual(summary["play_seconds"], 70.0)
        self.assertEqual(summary["player_deaths"], 1)
        self.assertEqual(summary["deaths_by_killer"], {"enemy.troll": 1})
        self.assertEqual(summary["kills"], {"enemy.goblin": 2})
        self.assertEqual(summary["damage_dealt"], {"enemy.goblin": {"total": 340.5, "hits": 12}})
        self.assertEqual(summary["damage_taken"], {"enemy.troll": {"total": 90.0, "hits": 3}})
        self.assertEqual((summary["gold_in"], summary["gold_out"]), ({"pickup": 40}, {"menu": 15}))
        self.assertEqual(summary["quests"]["quest.intro"], dict(started=1, completed=1, failed=0, median_seconds=60.0))
        self.assertIsNone(summary["quests"]["quest.lost"]["median_seconds"])
        self.assertEqual(summary["max_level"], 3)
        self.assertFalse(summary["wall_clock_quests"])

    def test_old_sessions_still_count_and_mark_wall_clock_quest_times(self):
        summary = analytics.summarize([OLD_SESSION, NEW_SESSION, []])
        self.assertEqual(summary["sessions"], 2)
        self.assertEqual(summary["player_deaths"], 2)
        self.assertEqual(summary["deaths_by_killer"], {"unknown": 1, "enemy.troll": 1})
        self.assertEqual(summary["quests"]["quest.intro"]["median_seconds"], 80.0)  # median of 100 and 60
        self.assertTrue(summary["wall_clock_quests"])
        self.assertIn("median ~80 s", analytics.render(summary))

    def test_render_is_compact_and_names_unfinished_quests(self):
        text = analytics.render(analytics.summarize([NEW_SESSION]))
        self.assertLessEqual(len(text.splitlines()), 10)
        self.assertIn("deaths: player 1 by enemy.troll 1 in region.ember_crown 1", text)
        self.assertIn("gold: +40 -15 net +25 | in: pickup 40 | out: menu 15", text)
        self.assertIn("damage taken: 90 (enemy.troll 90/3 hits)", text)
        self.assertIn("unfinished: quest.lost", text)

    def test_cli_reads_a_directory_and_survives_a_damaged_line(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory) / "analytics"
            folder.mkdir()
            lines = [json.dumps(row) for row in NEW_SESSION]
            (folder / "session_1_1_1.jsonl").write_text("\n".join(lines) + "\n{\"type\": \"dea", encoding="utf-8")
            (folder / "notes.txt").write_text("not a session", encoding="utf-8")
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                code = analytics.main(["summary", directory, "--json"])
            self.assertEqual(code, 0)
            document = json.loads(out.getvalue())
            self.assertEqual((document["files"], document["rows"]), (1, len(NEW_SESSION)))
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(analytics.main(["summary", "--dir", str(Path(directory) / "empty")]), 2)

    def test_default_directory_follows_an_isolated_user_dir(self):
        with mock.patch.dict("os.environ", {"EMBERVALE_USER_DIR": "X:/run/user"}):
            self.assertEqual(analytics.default_directory(), Path("X:/run/user") / "analytics")


class SdkCommands(unittest.TestCase):
    def test_a_run_with_nothing_to_compare_says_so_and_asserts_nothing(self):
        def filed(verdict):
            run = mock.Mock()
            run.result = dict(assertions=[])
            perf_report.file_verdict(run, dict(suite="session", key="k", machine="m", **verdict), "perf_report")
            return run
        none = filed(dict(status="no-baseline", baseline="tests/performance_baselines/m/k.json"))
        self.assertIn("NO BASELINE", none.brief.call_args.args[0])      # default output, not --verbose only
        code, message, severity = none.issue.call_args.args
        self.assertEqual((code, severity), ("perf_report.not_compared", "warning"))
        self.assertIn("nothing was compared", message)
        self.assertEqual(none.result["assertions"], [])                 # no "no perf regression" pass
        apart = filed(dict(status="incomparable", reason="different resolution"))
        self.assertIn("different resolution", apart.issue.call_args.args[1])
        ok = filed(dict(status="ok", regress=[], improve=[], same=3, info=0, new=0, missing=0, tolerance=0.1))
        ok.issue.assert_not_called()
        self.assertEqual([(a["name"], a["success"]) for a in ok.result["assertions"]],
                         [("no perf regression (k)", True)])

    def test_both_commands_are_discovered_with_their_own_flags(self):
        self.assertIn("perf-report", REGISTRY)
        self.assertIn("vfxperf", REGISTRY)
        self.assertTrue(REGISTRY["perf-report"].passthrough)
        args = cli.parser("perf-report").parse_args(["perf-report", "--seconds", "5", "--repro", "swarm", "--repeat", "2"])
        self.assertEqual((args.seconds, args.repro, args.repeat, args.update_baseline), (5.0, "swarm", 2, False))
        args = cli.parser("vfxperf").parse_args(["vfxperf", "--tiers", "ultra", "--cast-seconds", "4"])
        self.assertEqual((args.tiers, args.cast_seconds, args.view), ("ultra", 4.0, "wide"))

    def test_perf_report_game_arguments_baseline_name_and_summary(self):
        args = argparse.Namespace(seconds=20.0, warmup=3.0, repro="tools/repro/swarm.txt")
        self.assertEqual(perf_report.game_arguments(args, "R.json", ["--exec=x"]),
                         ["--new-game", "--perf-report=20", "--warmup=3", "--report=R.json",
                          "--repro=tools/repro/swarm.txt", "--exec=x"])
        # The baseline is named by perf_compare from the facts, so the standalone tool finds it too.
        document = {"gate": "perf-report", "facts": {"suite": "session", "mode": "new-game", "region": "region.ember_crown", "plan": "swarm"}}
        self.assertEqual(perf_compare.identity(document), ("session", "new-game-region.ember_crown-swarm"))
        line = perf_report.summary_line(0, {"facts": {"frames": 714, "seconds": 5, "headless": True, "frame_ms_p95": 8.94}})
        self.assertIn("714 frames in 5s headless", line)
        self.assertIn("p95 8.94", line)

    def test_vfxperf_tiers_and_safety_deadline(self):
        self.assertEqual(vfxperf.parse_tiers(" Ultra, medium "), ["ultra", "medium"])
        for bad in ("", "ultra,ultra", "extreme"):
            with self.assertRaises(ValueError):
                vfxperf.parse_tiers(bad)
        self.assertEqual(vfxperf.safety_seconds(["a", "b", "c"], 20.0), 225)


if __name__ == "__main__":
    unittest.main()
