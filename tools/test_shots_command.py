"""Tests for the SDK `shots` command (tools/embervale_sdk/commands/shots.py). No engine: the game
launch is replaced by a function that writes what a harness would."""
import json
import re
import tempfile
import types
import unittest
from pathlib import Path

from embervale_sdk.cli import parser
from embervale_sdk.commands import shots

try:
    from PIL import Image
except ImportError:
    Image = None

ROOT = Path(__file__).resolve().parents[1]


def parse(*argv):
    return parser("shots").parse_args(["shots", *argv])


class FakeRun:
    """The parts of cli.Run the command touches."""

    def __init__(self, folder, harness=None, run_id="run-1"):
        self.artifacts = Path(folder) / "artifacts" / run_id
        (self.artifacts / "user").mkdir(parents=True)
        self.env, self.timeout, self.notes, self.briefs, self.issues, self.calls = {}, 900, [], [], [], []
        self.result = dict(metrics={})
        self.harness = harness

    def note(self, message):
        self.notes.append(message)

    def brief(self, line):
        self.briefs.append(line)

    def issue(self, code, message, severity="error", path=None):
        self.issues.append((code, message, severity))

    def godot(self, name, arguments, user=(), render=False, scan=True):
        self.calls.append(types.SimpleNamespace(name=name, arguments=list(arguments), user=list(user), render=render))
        return types.SimpleNamespace(output=self.harness(self) if self.harness else "")

    def shots_line(self):
        # brief(), not note(): the line must be in the default output, not only under --verbose.
        lines = [n for n in self.briefs if n.startswith("SHOTS ")]
        return json.loads(lines[-1][6:]) if lines else None


def write_run(run, folder, shots_, failures=()):
    """Writes a manifest and (with Pillow) images the way a harness run leaves them."""
    directory = run.artifacts / folder
    directory.mkdir(parents=True)
    records = []
    for index, (name, problem, flags) in enumerate(shots_):
        record = dict(name=name, selected=True, ok=problem is None)
        if problem:
            record["problem"] = problem
        else:
            record.update(file=name + ".png", frame=100 + index)
            if Image is not None:
                image = Image.new("RGB", (640, 360), (40 + index * 60, 90, 120))
                image.paste((200, 200, 40), (index * 100, 50, index * 100 + 80, 130))
                image.save(directory / (name + ".png"))
        if flags:
            record["flags"] = flags
        records.append(record)
    manifest = dict(schema=1, suite="--" + folder, ok=not failures and all(r["ok"] for r in records), seconds=4.2,
                    registered=len(records), captured=sum(1 for r in records if "file" in r),
                    failed=[r["name"] for r in records if "problem" in r], focus_lost=[], failures=list(failures),
                    shots=records)
    (directory / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    return directory


class ArgumentTests(unittest.TestCase):
    def test_suite_table_matches_the_game(self):
        source = (ROOT / "src/Bootstrap/SessionHarnesses.cs").read_text(encoding="utf-8")
        capture_flags = {match.group(1) for line in source.splitlines() if "Capture: false" not in line
                         for match in [re.search(r'^\s*new\("(--[a-z-]+)",', line)] if match}
        self.assertEqual(capture_flags, set(shots.SESSION_SUITES.values()))
        self.assertNotIn("--vfxperf", shots.SUITES.values())
        shell = (ROOT / "src/Bootstrap/GameShellController.cs").read_text(encoding="utf-8")
        for flag in shots.TITLE_SUITES.values():
            self.assertIn(f'"{flag}"', shell)

    def test_a_session_suite_starts_a_new_game_and_passes_the_filter(self):
        args = parse("panelshots", "--only", "0?-map*", "--no-thumbs")
        self.assertEqual(["--panelshots", "--new-game", "--only=0?-map*", "--no-thumbs"],
                         shots.user_arguments(args, "panelshots"))
        self.assertEqual(["--shellshots", "--list"], shots.user_arguments(parse("shellshots", "--list"), "shellshots"))

    def test_a_save_replaces_the_new_game(self):
        self.assertEqual(["--hudshots", "--slot=fixture"], shots.user_arguments(parse("hudshots"), "hudshots", "fixture"))

    def test_input_suites_drive_components_unless_asked_for_real_buttons(self):
        self.assertEqual(["--spellshots", "--new-game", "--film=16x3", "--direct-input"],
                         shots.user_arguments(parse("spellshots", "--film", "16x3"), "spellshots"))
        self.assertEqual(["--camshots", "--new-game", "--film"],
                         shots.user_arguments(parse("camshots", "--film", "--input-route"), "camshots"))

    def test_a_world_view_becomes_flags(self):
        args = parse("shot", "--cell", "ember_crown.embermarket", "--yaw", "90", "--pitch", "-20", "--hour", "19.5",
                     "--at=-40,12", "--hud", "--region", "ember_crown", "--name", "market")
        self.assertEqual(["--shot", "--name=market", "--cell=ember_crown.embermarket", "--at=-40,12", "--yaw=90",
                          "--pitch=-20", "--hour=19.5", "--hud", "--region=ember_crown", "--new-game", "--direct-input"],
                         shots.user_arguments(args, "shot"))

    def test_a_spec_file_is_the_shot_argument(self):
        with tempfile.TemporaryDirectory() as folder:
            spec = Path(folder) / "views.json"
            spec.write_text("[]")
            user = shots.user_arguments(parse("shot", "--spec", str(spec)), "shot")
            self.assertEqual(f"--shot={spec.resolve()}", user[0])

    def test_environment_carries_resolution_scale_and_spell_options(self):
        args = parse("spellshots", "--resolution", "1280x800", "--ui-scale", "1.5", "--spell-filter", "fire",
                     "--spell-view", "tp", "--tier", "low")
        self.assertEqual(dict(EMBERVALE_RES="1280x800", EMBERVALE_SHOT_UISCALE="1.5", EMBERVALE_SPELLSHOTS_FILTER="fire",
                              EMBERVALE_SPELLSHOTS_VIEW="tp", EMBERVALE_SPELLSHOTS_TIER="low"), shots.environment(args))
        self.assertEqual({}, shots.environment(parse("hudshots", "--spell-view", "both")))

    def test_result_line_is_the_last_one_and_survives_log_prefixes(self):
        output = 'noise\nEMBERVALE_RESULT {"gate":"a"}\n[INFO] EMBERVALE_RESULT {"gate":"b","facts":{"shots":["x"]}}\nbye'
        self.assertEqual("b", shots.result_line(output)["gate"])
        self.assertIsNone(shots.result_line("nothing here"))
        self.assertIsNone(shots.result_line("EMBERVALE_RESULT {broken"))


class RunTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory(prefix="shots command ")

    def tearDown(self):
        self.folder.cleanup()

    def test_list_needs_no_engine(self):
        run = FakeRun(self.folder.name)
        shots.run(run, parse("list"), [])
        self.assertEqual([], run.calls)
        self.assertIn("panelshots", run.shots_line()["suites"])

    def test_unknown_suite_is_a_usage_error(self):
        with self.assertRaises(ValueError):
            shots.run(FakeRun(self.folder.name), parse("vfxperf"), [])

    def test_a_suite_runs_at_a_fixed_step_under_a_foreground_timeout(self):
        run = FakeRun(self.folder.name, lambda r: write_run(r, "hudshots", [("00-exploration", None, None)]) and "")
        shots.run(run, parse("hudshots", "--resolution", "1280x800", "--no-analyze"), [])
        call = run.calls[0]
        self.assertEqual(("shots-hudshots", True), (call.name, call.render))
        self.assertEqual(["--fixed-fps", "60", "--resolution", "1280x800"], call.arguments)
        self.assertEqual(shots.DEFAULT_TIMEOUT, run.timeout)
        self.assertEqual("1280x800", run.env["EMBERVALE_RES"])
        line = run.shots_line()
        self.assertEqual((True, 1, []), (line["ok"], line["captured"], line["failed"]))
        self.assertEqual([], run.issues)
        self.assertEqual(line, run.result["metrics"]["shots"])
        pointer = json.loads((run.artifacts.parent / "latest-shots-hudshots.json").read_text())
        self.assertEqual(str(run.artifacts / "hudshots"), pointer["dir"])

    def test_an_explicit_timeout_is_kept(self):
        run = FakeRun(self.folder.name, lambda r: "")
        run.timeout = 60
        shots.run(run, parse("hudshots", "--timeout", "60"), [])
        self.assertEqual(60, run.timeout)

    def test_failed_and_flagged_shots_are_named(self):
        def harness(r):
            write_run(r, "panelshots", [("00-map", None, ["same_as:zz"]), ("01-journal", "'01-journal' prerequisite failed: no journal", None)],
                      failures=["2 problem(s)"])
            return ""
        run = FakeRun(self.folder.name, harness)
        shots.run(run, parse("panelshots", "--no-analyze"), [])
        line = run.shots_line()
        self.assertEqual((False, ["01-journal"], {"00-map": ["same_as:zz"]}), (line["ok"], line["failed"], line["flagged"]))
        self.assertEqual(["shots.failed", "shots.failed"], [code for code, _, _ in run.issues])

    def test_no_manifest_is_reported_with_the_games_own_reason(self):
        out = 'EMBERVALE_RESULT {"gate":"shots","ok":false,"failures":["--only matched no shot: jurnal"]}'
        run = FakeRun(self.folder.name, lambda r: out)
        shots.run(run, parse("panelshots", "--only", "jurnal"), [])
        self.assertEqual(["shots.failed", "shots.incomplete"], [code for code, _, _ in run.issues])
        self.assertIn("jurnal", run.issues[0][1])
        self.assertIsNone(run.shots_line())

    def test_list_reads_the_names_from_the_result_line(self):
        out = 'EMBERVALE_RESULT {"gate":"shots-list","facts":{"suite":"--hudshots","count":2,"shots":["a","b"]}}'
        run = FakeRun(self.folder.name, lambda r: out)
        shots.run(run, parse("hudshots", "--list"), [])
        self.assertFalse(run.calls[0].render)
        self.assertEqual(["a", "b"], run.shots_line()["shots"])
        empty = FakeRun(self.folder.name + "2", lambda r: "no line")
        shots.run(empty, parse("hudshots", "--list"), [])
        self.assertEqual("shots.incomplete", empty.issues[0][0])

    def test_a_title_state_asked_through_shot_runs_the_title_suite(self):
        run = FakeRun(self.folder.name, lambda r: "")
        shots.run(run, parse("shot", "--ui", "shellshots/04-settings"), [])
        self.assertEqual(["--shellshots", "--only=04-settings"], run.calls[0].user)

    def test_a_save_folder_is_copied_into_the_isolated_user_directory(self):
        slot = Path(self.folder.name) / "fixture"
        slot.mkdir()
        (slot / "save.json").write_text("{}")
        run = FakeRun(self.folder.name, lambda r: "")
        shots.run(run, parse("hudshots", "--save", str(slot)), [])
        self.assertTrue((run.artifacts / "user/saves/fixture/save.json").is_file())
        self.assertIn("--slot=fixture", run.calls[0].user)
        with self.assertRaises(ValueError):
            shots.run(FakeRun(self.folder.name + "b"), parse("hudshots", "--save", self.folder.name), [])

    @unittest.skipIf(Image is None, "Pillow is not installed")
    def test_sheets_are_built_and_the_previous_run_is_the_baseline(self):
        first = FakeRun(self.folder.name, lambda r: write_run(r, "hudshots", [("00-a", None, None), ("01-b", None, ["black"])]) and "")
        shots.run(first, parse("hudshots"), [])
        line = first.shots_line()
        self.assertEqual([str(first.artifacts / "hudshots" / "sheet.png")], line["sheets"])
        self.assertNotIn("changed", line)

        second = FakeRun(self.folder.name, None, "run-2")

        def changed(r):
            directory = write_run(r, "hudshots", [("00-a", None, None), ("01-b", None, None)])
            with Image.open(directory / "01-b.png") as image:
                image = image.copy()
            image.paste((255, 255, 255), (400, 200, 520, 300))
            image.save(directory / "01-b.png")
            return ""
        second.harness = changed
        shots.run(second, parse("hudshots", "--diff-last"), [])
        line = second.shots_line()
        self.assertEqual(str(first.artifacts / "hudshots"), line["baseline"])
        self.assertEqual(["01-b.png"], list(line["changed"]))
        self.assertTrue(Path(line["changed"]["01-b.png"]["triptych"]).is_file())
        self.assertEqual(1, len(line["changed"]["01-b.png"]["boxes"]))


if __name__ == "__main__":
    unittest.main()
