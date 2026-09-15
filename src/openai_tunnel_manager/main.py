from __future__ import annotations

import tkinter as tk

from . import __version__
from .compatible_tunnel_client import CompatibleTunnelClient
from .icon_resources import apply_tk_icon, set_windows_app_id
from .single_instance import SingleInstanceGuard
from .tray_main_window import TrayMainWindow


def main() -> int:
    instance = SingleInstanceGuard()
    if not instance.acquire():
        instance.activate_existing()
        instance.close()
        return 0

    try:
        set_windows_app_id()
        root = tk.Tk()
        apply_tk_icon(root)
        window = TrayMainWindow(root, client=CompatibleTunnelClient())
        root.title(f"OpenAI MCP Tunnel Manager v{__version__}")

        # A later EXE launch signals the named Windows event. Route the restore
        # request through the existing UI event queue so Tk is only touched on
        # its owning thread.
        instance.start_activation_listener(
            lambda: window._dispatch_to_ui(window._restore_from_tray)
        )
        root.mainloop()
    finally:
        instance.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
