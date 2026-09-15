from __future__ import annotations

import os
import tempfile
import time
import tkinter as tk
import unittest
from pathlib import Path
from unittest.mock import patch

from openai_tunnel_manager.credentials import MemoryCredentialStore
from openai_tunnel_manager.models import AppSettings
from openai_tunnel_manager.optimized_main_window import OptimizedMainWindow
from openai_tunnel_manager.settings_store import SettingsStore
from openai_tunnel_manager.tray_main_window import TrayMainWindow
from tests.test_gui import FakeClient


class UiEnhancementTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.store = SettingsStore(Path(self.tmp.name) / "settings.json")
        self.store.save(
            AppSettings(
                binary_path="/fake/tunnel-client.exe",
                close_to_tray=False,
                refresh_interval_ms=60000,
            )
        )
        self.creds = MemoryCredentialStore()
        self.client = FakeClient()
        self.root = tk.Tk()
        self.root.withdraw()
        self.win = None

    def tearDown(self) -> None:
        if self.win is not None:
            try:
                self.win.quit()
            except tk.TclError:
                pass
        else:
            try:
                self.root.destroy()
            except tk.TclError:
                pass
        self.tmp.cleanup()

    def pump(self, seconds: float = 0.2) -> None:
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            self.root.update()
            time.sleep(0.01)

    def test_log_refresh_preserves_horizontal_scroll_position(self) -> None:
        self.win = OptimizedMainWindow(self.root, self.store, self.creds, self.client)
        self.pump(0.1)

        first = "INFO " + ("A" * 1200)
        self.win._set_raw_log(first)
        self.root.update_idletasks()
        self.assertLessEqual(self.win.log_text.xview()[0], 0.001)

        self.win.log_text.xview_moveto(0.35)
        self.root.update_idletasks()
        before_append = self.win.log_text.xview()[0]

        self.win._set_raw_log(first + "\n" + ("B" * 1200))
        self.root.update_idletasks()
        self.assertAlmostEqual(self.win.log_text.xview()[0], before_append, delta=0.01)

        self.win.log_text.xview_moveto(0.2)
        self.root.update_idletasks()
        before_replace = self.win.log_text.xview()[0]

        self.win._set_raw_log("WARN " + ("C" * 1200))
        self.root.update_idletasks()
        self.assertAlmostEqual(self.win.log_text.xview()[0], before_replace, delta=0.01)

    @unittest.skipUnless(os.name == "nt", "Windows tray behavior")
    def test_tray_starts_immediately_even_when_close_button_exits(self) -> None:
        with patch.object(TrayMainWindow, "_start_tray") as start_tray:
            self.win = TrayMainWindow(self.root, self.store, self.creds, self.client)
        start_tray.assert_called_once_with()
        self.assertFalse(self.win.settings.close_to_tray)


if __name__ == "__main__":
    unittest.main()
