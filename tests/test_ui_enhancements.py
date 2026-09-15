from __future__ import annotations

import os
import tempfile
import time
import tkinter as tk
import unittest
from pathlib import Path
from tkinter import ttk
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

    def _buttons(self) -> list[ttk.Button]:
        buttons: list[ttk.Button] = []

        def walk(widget: tk.Misc) -> None:
            for child in widget.winfo_children():
                if isinstance(child, ttk.Button):
                    buttons.append(child)
                walk(child)

        walk(self.root)
        return buttons

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

    def test_ui_controls_are_reorganized(self) -> None:
        with patch.object(TrayMainWindow, "_start_tray"):
            self.win = TrayMainWindow(self.root, self.store, self.creds, self.client)
        self.pump(0.1)

        buttons = self._buttons()
        texts = [str(button.cget("text")) for button in buttons]
        self.assertIn("诊断", texts)
        self.assertNotIn("Doctor 诊断", texts)
        self.assertIn("打开配置位置", texts)
        self.assertIn("打开日志位置", texts)
        self.assertIn("刷新", texts)
        self.assertNotIn("从 tunnel-client 重新读取", texts)
        self.assertNotIn("导入 Profile", texts)
        self.assertNotIn("导出 Profile", texts)
        self.assertNotIn("复制可见日志", texts)
        self.assertNotIn("导出日志", texts)

        preference = next(button for button in buttons if str(button.cget("text")) == "本机偏好 / 密钥")
        refresh = next(button for button in buttons if str(button.cget("text")) == "刷新")
        toolbar_order = [str(widget.cget("text")) for widget in preference.master.pack_slaves() if isinstance(widget, ttk.Button)]
        self.assertEqual(toolbar_order.index("刷新"), toolbar_order.index("本机偏好 / 密钥") + 1)

        actions_order = [str(widget.cget("text")) for widget in self.win.doctor_button.master.pack_slaves() if isinstance(widget, ttk.Button)]
        self.assertEqual(actions_order.index("打开配置位置"), actions_order.index("诊断") + 1)

    def test_open_location_actions_use_selected_paths(self) -> None:
        with patch.object(TrayMainWindow, "_start_tray"):
            self.win = TrayMainWindow(self.root, self.store, self.creds, self.client)
        self.pump(0.1)

        with patch.object(self.win, "_open_file_location") as open_location:
            self.win.open_config_location()
            open_location.assert_called_once_with(self.client.profile_path, "配置文件")

        self.win._current_log_path = r"C:\temp\manager.log"
        with patch.object(self.win, "_open_file_location") as open_location:
            self.win.open_log_location()
            open_location.assert_called_once_with(r"C:\temp\manager.log", "日志文件")

    @unittest.skipUnless(os.name == "nt", "Windows tray behavior")
    def test_tray_starts_immediately_even_when_close_button_exits(self) -> None:
        with patch.object(TrayMainWindow, "_start_tray") as start_tray:
            self.win = TrayMainWindow(self.root, self.store, self.creds, self.client)
        start_tray.assert_called_once_with()
        self.assertFalse(self.win.settings.close_to_tray)


if __name__ == "__main__":
    unittest.main()
