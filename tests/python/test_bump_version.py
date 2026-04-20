import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


def _load_module():
    root = Path(__file__).resolve().parents[2]
    script_path = root / "bump-version.py"
    spec = importlib.util.spec_from_file_location("bump_version_script", script_path)
    module = importlib.util.module_from_spec(spec)
    assert spec is not None and spec.loader is not None
    spec.loader.exec_module(module)
    return module


class BumpVersionScriptTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.mod = _load_module()

    def test_parse_version_parses_semver_and_v_prefix(self):
        self.assertEqual((1, 2, 3), self.mod.parse_version("1.2.3"))
        self.assertEqual((2, 0, 9), self.mod.parse_version("v2.0.9"))

    def test_parse_version_rejects_invalid_format(self):
        with self.assertRaises(ValueError):
            self.mod.parse_version("1.2")
        with self.assertRaises(ValueError):
            self.mod.parse_version("1.2.3.4")

    def test_bump_version_major_minor_patch(self):
        self.assertEqual("2.0.0", self.mod.bump_version("1.9.9", "major"))
        self.assertEqual("1.10.0", self.mod.bump_version("1.9.9", "minor"))
        self.assertEqual("1.9.10", self.mod.bump_version("1.9.9", "patch"))

    def test_bump_version_rejects_unknown_bump_type(self):
        with self.assertRaises(ValueError):
            self.mod.bump_version("1.2.3", "invalid")

    def test_update_version_json_and_read_canonical_version_round_trip(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            shared = root / "versions" / "shared"
            shared.mkdir(parents=True, exist_ok=True)
            version_file = shared / "version.json"
            version_file.write_text(json.dumps({"major": 0, "minor": 1, "patch": 0}))

            self.mod.update_version_json(root, "3.4.5")
            self.assertEqual("3.4.5", self.mod.read_canonical_version(root))

            data = json.loads(version_file.read_text())
            self.assertEqual(3, data["major"])
            self.assertEqual(4, data["minor"])
            self.assertEqual(5, data["patch"])

    def test_format_version_formats_semver_tuple(self):
        self.assertEqual("7.8.9", self.mod.format_version(7, 8, 9))

    def test_read_canonical_version_raises_when_missing(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(FileNotFoundError):
                self.mod.read_canonical_version(Path(tmp))

    def test_update_project_godot_updates_semver_fields(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            project_file = root / "project.godot"
            project_file.write_text(
                '[application]\n'
                'config/version="0.1.0"\n'
                "config/version_major=0\n"
                "config/version_minor=1\n"
                "config/version_patch=0\n"
            )

            self.mod.update_project_godot(root, "2.3.4")
            updated = project_file.read_text()
            self.assertIn('config/version="2.3.4"', updated)
            self.assertIn("config/version_major=2", updated)
            self.assertIn("config/version_minor=3", updated)
            self.assertIn("config/version_patch=4", updated)

    def test_update_export_presets_updates_windows_android_and_ios_fields(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            presets = root / "export_presets.cfg"
            presets.write_text(
                'application/file_version="0.1.0.0"\n'
                'application/product_version="0.1.0.0"\n'
                "package/version=100\n"
                'application/short_version="0.1.0"\n'
                'application/version="100"\n'
            )

            self.mod.update_export_presets(root, "1.2.3")
            updated = presets.read_text()
            self.assertIn('application/file_version="1.2.3.0"', updated)
            self.assertIn('application/product_version="1.2.3.0"', updated)
            self.assertIn("package/version=10203", updated)
            self.assertIn('application/short_version="1.2.3"', updated)
            self.assertIn('application/version="10203"', updated)

    def test_update_android_manifest_and_gradle(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            android = root / "versions" / "android"
            android.mkdir(parents=True, exist_ok=True)

            manifest = android / "AndroidManifest.xml"
            manifest.write_text(
                '<manifest android:versionCode="100" android:versionName="0.1.0"></manifest>'
            )
            gradle = android / "build.gradle"
            gradle.write_text('versionCode 100\nversionName "0.1.0"\n')

            self.mod.update_android_manifest(root, "1.2.3")
            self.mod.update_android_gradle(root, "1.2.3")

            self.assertIn('android:versionCode="10203"', manifest.read_text())
            self.assertIn('android:versionName="1.2.3"', manifest.read_text())
            self.assertIn("versionCode 10203", gradle.read_text())
            self.assertIn('versionName "1.2.3"', gradle.read_text())

    def test_update_snapcraft_plist_and_packaging_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)

            linux = root / "versions" / "linux"
            (linux / "debian").mkdir(parents=True, exist_ok=True)
            windows = root / "versions" / "windows"
            windows.mkdir(parents=True, exist_ok=True)
            macos = root / "versions" / "macos"
            macos.mkdir(parents=True, exist_ok=True)
            ios = root / "versions" / "ios"
            ios.mkdir(parents=True, exist_ok=True)

            (linux / "snapcraft.yaml").write_text("name: cordite\nversion: 0.1.0\n")
            (linux / "debian" / "control").write_text("Package: cordite\nVersion: 0.1.0\n")
            (windows / "AppxManifest.xml").write_text('<Identity Version="0.1.0.0" />')
            (windows / "inno-setup.iss").write_text('#define MyAppVersion "0.1.0"\n')
            (windows / "CorditeWars.wxs").write_text("-out dist\\windows\\CorditeWars_0.1.0.msi")

            plist_template = (
                "<plist><dict>"
                "<key>CFBundleShortVersionString</key><string>0.1.0</string>"
                "<key>CFBundleVersion</key><string>100</string>"
                "</dict></plist>"
            )
            (macos / "Info.plist").write_text(plist_template)
            (ios / "ios-info.plist").write_text(plist_template)

            self.mod.update_snapcraft_yaml(root, "1.2.3")
            self.mod.update_plist(root, "1.2.3")
            self.mod.update_appxmanifest(root, "1.2.3")
            self.mod.update_deb_control(root, "1.2.3")
            self.mod.update_inno_setup(root, "1.2.3")
            self.mod.update_wix(root, "1.2.3")

            self.assertIn("version: 1.2.3", (linux / "snapcraft.yaml").read_text())
            self.assertIn("Version: 1.2.3", (linux / "debian" / "control").read_text())
            self.assertIn('Version="1.2.3.0"', (windows / "AppxManifest.xml").read_text())
            self.assertIn('"1.2.3"', (windows / "inno-setup.iss").read_text())
            self.assertIn("CorditeWars_1.2.3.msi", (windows / "CorditeWars.wxs").read_text())

            self.assertIn("CFBundleShortVersionString</key><string>1.2.3</string>", (macos / "Info.plist").read_text())
            self.assertIn("CFBundleVersion</key><string>10203</string>", (macos / "Info.plist").read_text())
            self.assertIn("CFBundleShortVersionString</key><string>1.2.3</string>", (ios / "ios-info.plist").read_text())
            self.assertIn("CFBundleVersion</key><string>10203</string>", (ios / "ios-info.plist").read_text())


if __name__ == "__main__":
    unittest.main()
