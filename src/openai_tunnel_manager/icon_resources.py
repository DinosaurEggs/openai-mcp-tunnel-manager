from __future__ import annotations

import os
import sys
import tkinter as tk
from pathlib import Path

_APP_USER_MODEL_ID = "DinosaurEggs.OpenAITunnelManager"


def asset_path(name: str) -> Path:
    """Return an icon asset path for source and PyInstaller one-file builds."""
    bundle_root = getattr(sys, "_MEIPASS", None)
    if bundle_root:
        return Path(bundle_root) / "openai_tunnel_manager" / "assets" / name
    return Path(__file__).resolve().parent / "assets" / name


def set_windows_app_id() -> None:
    """Give Windows a stable taskbar identity for the packaged application."""
    if os.name != "nt":
        return
    try:
        import ctypes

        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(_APP_USER_MODEL_ID)
    except Exception:
        pass


def load_pil_icon(size: int | None = None):
    """Load the shared application icon for tray/UI use."""
    from PIL import Image

    image = Image.open(asset_path("app_icon.jpg")).convert("RGBA")
    if size:
        image = image.resize((size, size), Image.Resampling.LANCZOS)
    return image


def apply_tk_icon(root: tk.Tk) -> None:
    """Apply the shared icon to the main window and future Toplevel windows."""
    try:
        from PIL import ImageTk

        photo = ImageTk.PhotoImage(load_pil_icon())
        root.iconphoto(True, photo)
        # Tk requires the Python image object to stay alive for the window lifetime.
        root._openai_tunnel_manager_icon = photo  # type: ignore[attr-defined]
    except Exception:
        # Icon loading must never prevent the manager from starting.
        pass
