from __future__ import annotations

import tkinter as tk

from .compatible_tunnel_client import CompatibleTunnelClient
from .icon_resources import apply_tk_icon, set_windows_app_id
from .tray_main_window import TrayMainWindow


def main() -> int:
    set_windows_app_id()
    root = tk.Tk()
    apply_tk_icon(root)
    TrayMainWindow(root, client=CompatibleTunnelClient())
    root.mainloop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
