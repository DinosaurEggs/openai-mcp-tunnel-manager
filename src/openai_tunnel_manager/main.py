from __future__ import annotations

import tkinter as tk

from .optimized_main_window import OptimizedMainWindow


def main() -> int:
    root = tk.Tk()
    OptimizedMainWindow(root)
    root.mainloop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
