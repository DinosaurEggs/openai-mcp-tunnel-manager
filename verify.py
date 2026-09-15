from __future__ import annotations

import os
import shutil
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SRC = ROOT / "src"
ENV = os.environ.copy()
ENV["PYTHONPATH"] = str(SRC) + (os.pathsep + ENV["PYTHONPATH"] if ENV.get("PYTHONPATH") else "")


def run(args: list[str], *, gui: bool = False) -> None:
    cmd = [sys.executable, "-m", "unittest", "-v", *args]
    if gui and os.name != "nt":
        # Always isolate Tk GUI tests in a virtual display on Unix. Some CI/container
        # environments expose DISPLAY=:0 even though no X server is reachable.
        xvfb = shutil.which("xvfb-run")
        if not xvfb:
            raise SystemExit("GUI tests require xvfb-run on non-Windows systems.")
        cmd = [xvfb, "-a", *cmd]
    print("\n==>", " ".join(cmd), flush=True)
    completed = subprocess.run(cmd, cwd=ROOT, env=ENV, check=False)
    if completed.returncode != 0:
        raise SystemExit(completed.returncode)


def tunnel_client_test_names() -> list[str]:
    sys.path.insert(0, str(ROOT))
    sys.path.insert(0, str(SRC))
    from tests.test_tunnel_client import TunnelClientIntegrationTests

    return unittest.TestLoader().getTestCaseNames(TunnelClientIntegrationTests)


def main() -> int:
    # Fast pure/core tests in one process.
    run([
        "tests.test_autostart",
        "tests.test_models",
        "tests.test_credentials",
        "tests.test_status_parser",
        "tests.test_settings_store",
        "tests.test_health",
        "tests.test_single_instance",
        "tests.test_version",
    ])

    # Tk tests need a display on Unix-like CI runners.
    run(["tests.test_gui", "tests.test_ui_enhancements"], gui=True)
    run(["tests.test_gui_real_cli"], gui=True)

    # Keep process-level tunnel-client cases isolated. This avoids one long-lived
    # unittest process retaining child-process/HTTP resources across unrelated cases.
    for name in tunnel_client_test_names():
        run([f"tests.test_tunnel_client.TunnelClientIntegrationTests.{name}"])

    # Runs normally on Windows; intentionally skips on other platforms.
    run(["tests.test_windows_credentials"])
    print("\nAll verification groups passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
