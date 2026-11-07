from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent

class ReleaseLayoutTests(unittest.TestCase):
    def test_qr_runtime_and_license_are_in_packager_and_verifier(self):
        for name in ("Prepare-Release.ps1", "Verify-Release.ps1"):
            text = (ROOT / name).read_text(encoding="utf-8-sig")
            for required in ("'QRCoder.dll'", "'QRCoder-LICENSE.txt'"):
                self.assertIn(required, text, f"{name} is missing {required}")

    def test_updater_helper_keeps_required_runtime_dependency(self):
        text = (ROOT.parent / "ClipBridge" / "Windows" / "SelfUpdate.cs").read_text(encoding="utf-8-sig")
        for name in ("QRCoder.dll", "ClipBridge.deps.json"):
            self.assertIn(f'"{name}"', text, f"standalone updater does not preserve {name}")

    def test_qr_runtime_and_license_are_in_installer(self):
        text = (ROOT / "ClipBridge-Setup.iss").read_text(encoding="utf-8-sig")
        for name in ("QRCoder.dll", "QRCoder-LICENSE.txt"):
            self.assertIn(f'Source: "{{#BuildDir}}\\{name}"', text)

if __name__ == "__main__":
    unittest.main()