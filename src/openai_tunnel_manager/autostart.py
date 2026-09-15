from __future__ import annotations

import os
import sys
from pathlib import Path

APP_NAME = "OpenAITunnelManager"
RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"


def startup_command() -> str:
    if getattr(sys, "frozen", False):
        return f'"{sys.executable}"'
    launcher = Path(__file__).resolve().parents[2] / "launcher.py"
    return f'"{sys.executable}" "{launcher}"'


def set_windows_startup(enabled: bool) -> None:
    if os.name != "nt":
        return
    import winreg
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_SET_VALUE) as key:
        if enabled:
            winreg.SetValueEx(key, APP_NAME, 0, winreg.REG_SZ, startup_command())
        else:
            try:
                winreg.DeleteValue(key, APP_NAME)
            except FileNotFoundError:
                pass
