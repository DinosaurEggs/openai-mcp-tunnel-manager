from __future__ import annotations

import sys
from pathlib import Path

# Makes the source checkout runnable without installation while remaining safe for PyInstaller.
if not getattr(sys, "frozen", False):
    src = Path(__file__).resolve().parent / "src"
    if src.is_dir():
        sys.path.insert(0, str(src))

from openai_tunnel_manager.main import main

if __name__ == "__main__":
    raise SystemExit(main())
