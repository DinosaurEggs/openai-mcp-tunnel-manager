from __future__ import annotations

import tkinter as tk

from .tray_main_window import TrayMainWindow


def main() -> int:
    root = tk.Tk()
    TrayMainWindow(root)
    root.mainloop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
