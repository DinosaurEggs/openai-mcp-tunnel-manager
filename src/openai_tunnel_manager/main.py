from __future__ import annotations

import tkinter as tk

from .main_window import MainWindow


def main() -> int:
    root = tk.Tk()
    MainWindow(root)
    root.mainloop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
