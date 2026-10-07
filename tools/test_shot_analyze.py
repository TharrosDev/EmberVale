"""Tests for tools/shot_analyze.py and its make_contact_sheet shim. Images are drawn here; no engine."""
import contextlib
import io
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

try:
    from PIL import Image, ImageDraw
    import shot_analyze
except ImportError:  # Pillow is a tool dependency, not a project one
    Image = None

TOOLS = Path(__file__).resolve().parent


def scene(path, size=(640, 360), box=None, colour=(200, 60, 40), ground=(70, 110, 60)):
    """A sky-over-ground frame with an optional coloured box, so it is neither flat nor black."""
    image = Image.new("RGB", size, (120, 150, 200))
    draw = ImageDraw.Draw(image)
    draw.rectangle([0, size[1] // 2, size[0], size[1]], fill=ground)
    if box:
        draw.rectangle(box, fill=colour)
    image.save(path)
    return path


def cli(*argv):
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        code = shot_analyze.main([str(a) for a in argv])
    return code, [json.loads(line) for line in out.getvalue().splitlines()]


@unittest.skipIf(Image is None, "Pillow is not installed")
class StatsTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory(prefix="shot analyze ")
        self.dir = Path(self.folder.name)

    def tearDown(self):
        self.folder.cleanup()

    def test_black_flat_and_magenta_frames_are_flagged_and_a_scene_is_not(self):
        scene(self.dir / "a_scene.png")
        Image.new("RGB", (640, 360), (0, 0, 0)).save(self.dir / "b_black.png")
        Image.new("RGB", (640, 360), (90, 90, 90)).save(self.dir / "c_flat.png")
        scene(self.dir / "d_magenta.png", box=[100, 100, 300, 250], colour=(255, 0, 255))
        results = {r["image"]: r for r in shot_analyze.analyze(shot_analyze.images_in(self.dir))}
        self.assertEqual([], results["a_scene.png"]["flags"])
        self.assertEqual(["flat", "black"], results["b_black.png"]["flags"])
        self.assertEqual(["flat"], results["c_flat.png"]["flags"])
        self.assertEqual(["magenta"], results["d_magenta.png"]["flags"])
        self.assertGreater(results["d_magenta.png"]["magenta_pct"], 10)
        self.assertEqual([640, 360], results["a_scene.png"]["resolution"])

    def test_a_repeated_frame_names_the_first_one(self):
        scene(self.dir / "01.png", box=[10, 10, 60, 60])
        scene(self.dir / "02.png", box=[10, 10, 60, 60])
        scene(self.dir / "03.png", box=[300, 10, 360, 60])
        flags = [r["flags"] for r in shot_analyze.analyze(shot_analyze.images_in(self.dir))]
        self.assertEqual([[], ["same_as:01.png"], []], flags)

    def test_generated_files_are_not_inputs(self):
        for name in ("shot.png", "shot.film.png", "shot.diff.png", "sheet.png", "sheet_02.png"):
            scene(self.dir / name)
        self.assertEqual(["shot.png"], [p.name for p in shot_analyze.images_in(self.dir)])

    def test_cli_prints_one_line_per_image_and_strict_fails_on_flags(self):
        scene(self.dir / "ok.png")
        Image.new("RGB", (320, 180), (0, 0, 0)).save(self.dir / "black.png")
        code, lines = cli("stats", self.dir)
        self.assertEqual(0, code)
        self.assertEqual(3, len(lines))
        self.assertEqual(dict(summary=True, directory=str(self.dir), images=2, flagged=["black.png"]), lines[-1])
        code, lines = cli("stats", self.dir, "--flagged-only", "--strict")
        self.assertEqual(1, code)
        self.assertEqual(["black.png", None], [line.get("image") for line in lines])
        self.assertEqual(2, cli("stats", self.dir / "missing")[0])


@unittest.skipIf(Image is None, "Pillow is not installed")
class DiffTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory(prefix="shot diff ")
        self.new = Path(self.folder.name) / "new"
        self.old = Path(self.folder.name) / "old"
        self.new.mkdir()
        self.old.mkdir()

    def tearDown(self):
        self.folder.cleanup()

    def test_a_moved_prop_is_boxed_where_it_changed(self):
        scene(self.old / "view.png")
        scene(self.new / "view.png", box=[400, 40, 479, 119])
        result = shot_analyze.diff_images(self.new / "view.png", self.old / "view.png",
                                          triptych=self.new / "diff" / "view.diff.png")
        self.assertTrue(result["changed"])
        self.assertEqual(1, len(result["boxes"]))
        x, y, w, h = result["boxes"][0]
        self.assertTrue(390 <= x <= 400 and 32 <= y <= 40, result["boxes"])
        self.assertTrue(80 <= w <= 96 and 80 <= h <= 96, result["boxes"])
        self.assertLess(result["changed_pct"], 6)
        with Image.open(result["triptych"]) as sheet:
            self.assertEqual((480 * 3 + 8, 270), sheet.size)

    def test_an_unchanged_frame_writes_no_triptych(self):
        scene(self.old / "view.png", box=[10, 10, 90, 90])
        scene(self.new / "view.png", box=[10, 10, 90, 90])
        result = shot_analyze.diff_images(self.new / "view.png", self.old / "view.png", triptych=self.new / "d.png")
        self.assertEqual((False, 0.0, []), (result["changed"], result["changed_pct"], result["boxes"]))
        self.assertNotIn("triptych", result)
        self.assertFalse((self.new / "d.png").exists())

    def test_a_tone_shift_counts_unless_rows_are_normalized(self):
        scene(self.old / "view.png", box=[400, 40, 479, 119])
        with Image.open(self.old / "view.png") as image:
            image.point(lambda v: min(255, v + 30)).save(self.new / "view.png")
        plain = shot_analyze.diff_images(self.new / "view.png", self.old / "view.png")
        normalized = shot_analyze.diff_images(self.new / "view.png", self.old / "view.png", row_normalize=True)
        self.assertGreater(plain["changed_pct"], 90)
        self.assertEqual(0.0, normalized["changed_pct"])

    def test_a_baseline_at_another_resolution_is_resized_not_refused(self):
        scene(self.old / "view.png", size=(320, 180))
        scene(self.new / "view.png", size=(640, 360))
        result = shot_analyze.diff_images(self.new / "view.png", self.old / "view.png")
        self.assertTrue(result["resized_baseline"])
        self.assertLess(result["changed_pct"], 5)

    def test_cli_reports_changed_and_missing_images_only(self):
        for name in ("same.png", "moved.png"):
            scene(self.old / name)
        scene(self.new / "same.png")
        scene(self.new / "moved.png", box=[100, 200, 180, 280])
        scene(self.new / "new.png")
        code, lines = cli("diff", self.new, "--baseline", self.old, "--strict")
        self.assertEqual(1, code)
        self.assertEqual(["moved.png", "new.png", None], [line.get("image") for line in lines])
        self.assertTrue(lines[1]["missing_baseline"])
        self.assertEqual((["moved.png"], ["new.png"], 3), (lines[-1]["changed"], lines[-1]["missing_baseline"], lines[-1]["images"]))
        self.assertTrue((self.new / "diff" / "moved.diff.png").is_file())
        self.assertEqual(0, cli("diff", self.new, "--baseline", self.new)[0])

    def test_boxes_are_connected_groups_largest_first(self):
        deltas = [[0.0] * 8 for _ in range(6)]
        for x, y in ((0, 0), (1, 0), (1, 1), (6, 4), (3, 3)):
            deltas[y][x] = 50.0
        deltas[5][7] = 50.0  # touches (6,4) only diagonally: its own group
        self.assertEqual([[0, 0, 2, 2], [3, 3, 1, 1], [6, 4, 1, 1]], shot_analyze.changed_boxes(deltas, limit=3))
        self.assertEqual(4, len(shot_analyze.changed_boxes(deltas)))


@unittest.skipIf(Image is None, "Pillow is not installed")
class SheetTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory(prefix="shot sheet ")
        self.dir = Path(self.folder.name)
        self.paths = [scene(self.dir / f"{i:02d}-state.png", box=[i * 20, 10, i * 20 + 40, 60]) for i in range(5)]

    def tearDown(self):
        self.folder.cleanup()

    def test_one_sheet_holds_a_small_run(self):
        written = shot_analyze.make_sheets(self.paths, self.dir / "sheet.png", flagged={"02-state.png"})
        self.assertEqual([self.dir / "sheet.png"], written)
        with Image.open(written[0]) as sheet:
            self.assertEqual(4 * 320, sheet.width)
            self.assertEqual(2 * (180 + 22), sheet.height)
            self.assertEqual((255, 59, 59), sheet.getpixel((2 * 320 + 1, 1)), "a flagged cell is outlined in red")
            self.assertNotEqual((255, 59, 59), sheet.getpixel((1, 1)))

    def test_a_large_run_is_paged(self):
        written = shot_analyze.make_sheets(self.paths, self.dir / "sheet.png", per_sheet=2)
        self.assertEqual(["sheet_01.png", "sheet_02.png", "sheet_03.png"], [p.name for p in written])
        self.assertEqual([], shot_analyze.make_sheets([], self.dir / "none.png"))

    def test_thumbs_and_sheet_commands(self):
        code, lines = cli("thumbs", self.dir, "--width", 160)
        self.assertEqual((0, 5), (code, lines[-1]["thumbs"]))
        with Image.open(self.dir / "00-state.thumb.jpg") as thumb:
            self.assertEqual((160, 90), thumb.size)
        code, lines = cli("sheet", self.dir, self.dir / "sheet.png", "--per-sheet", 0)
        self.assertEqual((0, 5, [str(self.dir / "sheet.png")]), (code, lines[-1]["images"], lines[-1]["sheets"]))

    def test_the_old_contact_sheet_command_still_works(self):
        out = self.dir / "out" / "contact.png"
        result = subprocess.run([sys.executable, str(TOOLS / "make_contact_sheet.py"), str(self.dir), str(out),
                                 "--columns", "5", "--thumb-width", "200"], capture_output=True, text=True, cwd=TOOLS)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("(5 images)", result.stdout)
        with Image.open(out) as sheet:
            self.assertEqual(5 * 200, sheet.width)


if __name__ == "__main__":
    unittest.main()
