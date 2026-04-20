import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


def _load_module():
    root = Path(__file__).resolve().parents[2]
    script_path = root / "generate-checksums.py"
    spec = importlib.util.spec_from_file_location("generate_checksums_script", script_path)
    module = importlib.util.module_from_spec(spec)
    assert spec is not None and spec.loader is not None
    spec.loader.exec_module(module)
    return module


class GenerateChecksumsScriptTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.mod = _load_module()

    def test_compute_sha256_matches_known_hash(self):
        with tempfile.TemporaryDirectory() as tmp:
            file_path = Path(tmp) / "artifact.bin"
            payload = b"cordite-test-payload"
            file_path.write_bytes(payload)

            expected = hashlib.sha256(payload).hexdigest()
            self.assertEqual(expected, self.mod.compute_sha256(file_path))

    def test_generate_checksums_creates_txt_and_json_outputs(self):
        with tempfile.TemporaryDirectory() as tmp:
            build_dir = Path(tmp)
            (build_dir / "windows").mkdir(parents=True)
            (build_dir / "linux").mkdir(parents=True)

            exe_path = build_dir / "windows" / "CorditeWars.exe"
            linux_path = build_dir / "linux" / "CorditeWars"
            exe_path.write_bytes(b"windows-binary")
            linux_path.write_bytes(b"linux-binary")

            checksums = self.mod.generate_checksums(build_dir, output_format="both")

            self.assertEqual(2, len(checksums))
            self.assertIn("windows/CorditeWars.exe", checksums)
            self.assertIn("linux/CorditeWars", checksums)
            self.assertTrue((build_dir / "checksums.txt").exists())
            self.assertTrue((build_dir / "checksums.json").exists())

            json_data = json.loads((build_dir / "checksums.json").read_text())
            self.assertEqual("sha256", json_data["algorithm"])
            self.assertEqual(2, len(json_data["artifacts"]))

    def test_verify_checksums_json_passes_and_detects_tamper(self):
        with tempfile.TemporaryDirectory() as tmp:
            build_dir = Path(tmp)
            (build_dir / "android").mkdir(parents=True)
            apk_path = build_dir / "android" / "game.apk"
            apk_path.write_bytes(b"apk-v1")

            self.mod.generate_checksums(build_dir, output_format="json")
            checksums_json = build_dir / "checksums.json"

            self.assertTrue(self.mod.verify_checksums(checksums_json, build_dir))

            apk_path.write_bytes(b"apk-v2")
            self.assertFalse(self.mod.verify_checksums(checksums_json, build_dir))

    def test_generate_checksums_returns_empty_when_no_artifacts(self):
        with tempfile.TemporaryDirectory() as tmp:
            build_dir = Path(tmp)
            checksums = self.mod.generate_checksums(build_dir, output_format="both")
            self.assertEqual({}, checksums)
            self.assertFalse((build_dir / "checksums.txt").exists())
            self.assertFalse((build_dir / "checksums.json").exists())

    def test_verify_checksums_json_defaults_to_parent_directory(self):
        with tempfile.TemporaryDirectory() as tmp:
            build_dir = Path(tmp)
            (build_dir / "windows").mkdir(parents=True)
            exe_path = build_dir / "windows" / "CorditeWars.exe"
            exe_path.write_bytes(b"exe-v1")

            self.mod.generate_checksums(build_dir, output_format="json")
            checksums_json = build_dir / "checksums.json"
            self.assertTrue(self.mod.verify_checksums(checksums_json))

    def test_verify_checksums_txt_passes_and_handles_missing_file(self):
        with tempfile.TemporaryDirectory() as tmp:
            build_dir = Path(tmp)
            (build_dir / "linux").mkdir(parents=True)
            binary_path = build_dir / "linux" / "CorditeWars"
            payload = b"linux-v1"
            binary_path.write_bytes(payload)

            checksum = hashlib.sha256(payload).hexdigest()
            checksums_txt = build_dir / "checksums.txt"
            checksums_txt.write_text(f"{checksum}  linux/CorditeWars\n")
            self.assertTrue(self.mod.verify_checksums(checksums_txt, build_dir))

            binary_path.unlink()
            self.assertFalse(self.mod.verify_checksums(checksums_txt, build_dir))

    def test_verify_checksums_txt_ignores_invalid_lines(self):
        with tempfile.TemporaryDirectory() as tmp:
            build_dir = Path(tmp)
            checksums_txt = build_dir / "checksums.txt"
            checksums_txt.write_text("invalid-line-without-path\n\n")
            self.assertTrue(self.mod.verify_checksums(checksums_txt, build_dir))


if __name__ == "__main__":
    unittest.main()
