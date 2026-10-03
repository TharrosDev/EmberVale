"""Keep reviewed retarget choices across adoption without loading an image library or engine."""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from meshy_adopt import patch_import


class ReviewedSilhouetteTests(unittest.TestCase):
    def test_rewrite_preserves_reviewed_true_and_false(self) -> None:
        for value in ("true", "false"):
            with self.subTest(value=value), tempfile.TemporaryDirectory() as directory:
                dest = Path(directory) / "body.glb"
                sidecar = dest.with_suffix(".glb.import")
                sidecar.write_text(
                    'nodes/root_scale=1.0\n_subresources={\n"nodes": {\n'
                    '"PATH:Armature/Skeleton3D": {\n'
                    f'"retarget/rest_fixer/fix_silhouette/enable": {value}\n'
                    '}\n}\n}\ngltf/naming_version=2\ngltf/embedded_image_handling=3\n',
                    encoding="utf-8")

                result = patch_import(dest, 1.0, "reviewed_bonemap.tres")
                text = sidecar.read_text(encoding="utf-8")
                self.assertTrue(result.startswith("patched "), result)
                self.assertIn(f'"retarget/rest_fixer/fix_silhouette/enable": {value},', text)
                self.assertIn('Resource("res://assets/models/animations/reviewed_bonemap.tres")', text)
                self.assertIn("gltf/embedded_image_handling=1", text)
                self.assertIn("nodes/root_scale=1.0", text)
                # Repeating adoption must not lose the preserved choice.
                patch_import(dest, 1.0, "reviewed_bonemap.tres")
                self.assertEqual(text, sidecar.read_text(encoding="utf-8"))

    def test_new_import_defaults_to_disabled(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dest = Path(directory) / "body.glb"
            sidecar = dest.with_suffix(".glb.import")
            sidecar.write_text(
                "nodes/root_scale=1.0\n_subresources={}\ngltf/naming_version=2\n",
                encoding="utf-8")
            patch_import(dest, None, "default_bonemap.tres")
            self.assertIn('"retarget/rest_fixer/fix_silhouette/enable": false,',
                          sidecar.read_text(encoding="utf-8"))

    def test_unreviewed_nested_import_defaults_to_disabled(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dest = Path(directory) / "body.glb"
            sidecar = dest.with_suffix(".glb.import")
            sidecar.write_text(
                'nodes/root_scale=1.0\n_subresources={"nodes": {"PATH:Armature/Skeleton3D": {}}}'
                '\ngltf/naming_version=2\n', encoding="utf-8")
            patch_import(dest, None, "default_bonemap.tres")
            self.assertIn('"retarget/rest_fixer/fix_silhouette/enable": false,',
                          sidecar.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
