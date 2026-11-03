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

    def test_workflows_stop_on_each_failed_build_and_test_command(self):
        workflows = ROOT.parent / ".github" / "workflows"
        for filename, first_step in (("preflight.yml", "Build application and smoke-test projects"),
                                     ("release.yml", "Build test projects")):
            text = (workflows / filename).read_text(encoding="utf-8-sig")
            section = text.split(f"- name: {first_step}", 1)[1].split("- name:", 1)[0]
            lines = [line.strip() for line in section.splitlines() if line.strip()]
            commands = [i for i, line in enumerate(lines) if line.startswith(("dotnet ", "python "))]
            self.assertGreaterEqual(len(commands), 3, filename)
            for index in commands:
                with self.subTest(workflow=filename, command=lines[index]):
                    self.assertIn("if ($LASTEXITCODE -ne 0)", lines[index + 1] if index + 1 < len(lines) else "")
if __name__ == "__main__":
    unittest.main()