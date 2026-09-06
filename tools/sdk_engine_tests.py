"""Native protocol integration tests. Explicit: embervale.py test --engine-tests [--render]."""
import json
import os
import shutil
from pathlib import Path
import sys
import tempfile
import unittest

from quality_common import ROOT, discover_godot, run_process

FIXTURE = "res://tests/headless/ProtocolFixture.tscn"


class NativeProtocolTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="sdk-integration-", dir=os.environ.get("EMBERVALE_ARTIFACTS"))
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)

    def sdk(self, command, expected=0):
        result = run_process([sys.executable, str(ROOT / "tools/embervale.py"), *command,
                              "--json", "--artifacts", str(Path(os.environ.get("EMBERVALE_ARTIFACTS", ROOT / "artifacts/headless")) / "native-tests")], timeout=90)
        self.assertEqual(expected, result.returncode, result.output[-12000:])
        parsed = json.loads(result.stdout)
        self.assertEqual(expected == 0, parsed["success"])
        self.assertTrue((Path(parsed["artifact_directory"]) / "summary.json").is_file())
        return parsed

    def plan(self, steps):
        path = self.folder / "plan.json"
        path.write_text(json.dumps(dict(scene=FIXTURE, steps=steps)), encoding="utf-8")
        return str(path)

    def test_control_round_trip(self):
        node = "/root/Fixture/Subject"
        steps = [dict(op="assert", node=node),
                 dict(op="set_property", node=node, property="position", value=[1, 2, 3]),
                 dict(op="get_property", node=node, property="position"),
                 dict(op="assert", node=node, property="position", comparison="eq", value=[1, 2, 3]),
                 dict(op="call_method", node=node, method="hide"),
                 dict(op="assert", node=node, property="visible", comparison="eq", value=False),
                 dict(op="call_method", node=node, method="show"),
                 dict(op="wait_frames", frames=2), dict(op="scene_tree"), dict(op="collect_logs"),
                 dict(op="collect_metrics"), dict(op="load_scene", scene=FIXTURE),
                 dict(op="assert", node=node, property="position", comparison="eq", value=[0, 0, 0]),
                 dict(op="stop")]
        result = self.sdk(["scenario", self.plan(steps), "--frames", "2"])
        self.assertEqual(4, len(result["assertions"]))

    def test_failed_assertion_preserves_evidence(self):
        result = self.sdk(["scenario", self.plan([dict(op="assert", node="/root/Fixture/Missing")]), "--frames", "1"], expected=5)
        self.assertTrue(any(p.endswith(".state.json") for p in result["artifacts"]))
        self.assertTrue(any(not a["success"] for a in result["assertions"]))

    def test_missing_scene_reports_failure(self):
        result = self.sdk(["smoke", "--scene", "res://tests/headless/missing.tscn", "--frames", "1"], expected=1)
        self.assertTrue(any(d["severity"] == "error" for d in result["diagnostics"]))

    def test_rejects_unsafe_method(self):
        result = self.sdk(["scenario", self.plan([dict(op="call_method", node="/root/Fixture", method="free")])], expected=2)
        self.assertFalse(any(s["name"].startswith("scenario-") for s in result["steps"]))

    def test_resource_inspection(self):
        result = self.sdk(["inspect", "--resource", FIXTURE])
        self.assertTrue(any(p.endswith(".resource.json") for p in result["artifacts"]))

    def test_perf_budget_is_an_assertion(self):
        result = self.sdk(["perf", "--scene", FIXTURE, "--frames", "10", "--max-frame-ms", "0"], expected=5)
        self.assertTrue(any(a["name"] == "p95 frame budget" for a in result["assertions"]))

    def test_author_roundtrip_and_live_build(self):
        plan = json.loads((ROOT / "tools/headless/examples/workshop.json").read_text())
        path = self.folder / "author.json"
        path.write_text(json.dumps(plan))
        authored = self.sdk(["author", str(path)])
        staged = Path(authored["artifact_directory"]) / "authored.tscn"
        self.assertTrue(staged.is_file())
        # The in-engine author command already reloaded and instantiated this saved scene.
        metric = next(v for v in authored["metrics"].values() if isinstance(v, dict) and "staged_path" in v)
        self.assertTrue(metric["success"])
        self.assertEqual(len(plan["operations"]), metric["operations"])
        live = self.plan([dict(op="build", parent="/root/Fixture", plan=plan),
                          dict(op="assert", node="/root/Fixture/SdkWorkshop/Floor/FloorCollision"),
                          dict(op="assert", node="/root/Fixture/SdkWorkshop/Crate", property="position", comparison="eq", value=[0, 0.5, 0])])
        self.sdk(["scenario", live, "--frames", "2"])

    def test_author_unknown_property_is_not_silently_accepted(self):
        path = self.folder / "author-invalid.json"
        path.write_text(json.dumps(dict(operations=[dict(op="customize", target="root", properties={"does_not_exist": 1})])))
        result = self.sdk(["author", str(path)], expected=1)
        self.assertFalse((Path(result["artifact_directory"]) / "authored.tscn").exists())

    def test_author_customizes_exported_csharp_resource(self):
        authored = self.sdk(["author", str(ROOT / "tools/headless/examples/weapon.json")])
        staged = Path(authored["artifact_directory"]) / "authored.tres"
        # Keep inspection project-local even when the caller stores artifacts outside ROOT.
        with tempfile.NamedTemporaryFile(suffix=".tres", dir=ROOT / "tests/headless", delete=False) as temporary:
            fixture = Path(temporary.name)
        self.addCleanup(fixture.unlink, missing_ok=True)
        shutil.copyfile(staged, fixture)
        inspected = self.sdk(["inspect", "--resource", "res://" + fixture.relative_to(ROOT).as_posix()])
        dump = next(Path(inspected["artifact_directory"]) / p for p in inspected["artifacts"] if p.endswith(".resource.json"))
        properties = json.loads(dump.read_text())["properties"]
        self.assertEqual("SDK Training Sword", properties["DisplayName"])
        self.assertEqual(14, properties["BaseDamage"])
        self.assertEqual(10, properties["StaminaCost"])

    @unittest.skipUnless(os.environ.get("EMBERVALE_RENDER_TESTS") == "1", "render tests require --render")
    def test_screenshot_diff_and_failure_capture(self):
        first = self.sdk(["screenshot", "--scene", FIXTURE, "--resolution", "320x240", "--frames", "2"])
        png = next(Path(first["artifact_directory"]) / p for p in first["artifacts"] if p.endswith(".capture.png"))
        self.sdk(["screenshot", "--scene", FIXTURE, "--resolution", "320x240", "--frames", "2", "--baseline", str(png), "--threshold", "0.01"])
        moved = self.plan([dict(op="set_property", node="/root/Fixture/Subject", property="position", value=[2, 0, 0])])
        diff = self.sdk(["screenshot", moved, "--resolution", "320x240", "--frames", "2", "--baseline", str(png), "--threshold", "0.001"], expected=1)
        self.assertTrue(any(p.endswith(".diff.png") for p in diff["artifacts"]))
        failed = self.sdk(["scenario", self.plan([dict(op="assert", node="/root/Fixture/Missing")]), "--render", "--resolution", "320x240"], expected=5)
        self.assertTrue(any(p.endswith(".failure.png") for p in failed["artifacts"]))


if __name__ == "__main__":
    if not discover_godot():
        raise SystemExit("Godot required for native protocol tests")
    unittest.main()
