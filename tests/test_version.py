from __future__ import annotations

import tomllib
import unittest
from pathlib import Path

from openai_tunnel_manager import __version__

ROOT = Path(__file__).resolve().parents[1]


class VersionConsistencyTests(unittest.TestCase):
    def test_release_version_is_consistent(self) -> None:
        project = tomllib.loads((ROOT / "pyproject.toml").read_text(encoding="utf-8"))
        version = str(project["project"]["version"])
        self.assertEqual(version, "1.0.0")
        self.assertEqual(__version__, version)

        readme = (ROOT / "README.md").read_text(encoding="utf-8")
        validation = (ROOT / "VALIDATION.md").read_text(encoding="utf-8")
        release_notes = (ROOT / "RELEASE_NOTES.md").read_text(encoding="utf-8")
        version_info = (ROOT / "tools" / "version_info.txt").read_text(encoding="utf-8")
        package_project = (ROOT / "package" / "OpenAITunnelManager.Package.csproj").read_text(encoding="utf-8")

        self.assertIn(f"## {version}", readme)
        self.assertIn(f"Release: `{version}`", validation)
        self.assertIn(f"v{version}", release_notes)
        self.assertIn("filevers=(1, 0, 0, 0)", version_info)
        self.assertIn("prodvers=(1, 0, 0, 0)", version_info)
        self.assertIn("FileVersion', '1.0.0'", version_info)
        self.assertIn("ProductVersion', '1.0.0'", version_info)
        self.assertIn(f"<Version>{version}</Version>", package_project)

        for text in (readme, validation, release_notes):
            self.assertNotIn("0.3.0", text)


if __name__ == "__main__":
    unittest.main()
