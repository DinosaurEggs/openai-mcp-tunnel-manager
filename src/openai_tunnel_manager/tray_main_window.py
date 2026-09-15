from __future__ import annotations

import os
import threading
import tkinter as tk
from typing import Any, Callable

from .autostart import set_windows_startup
from .icon_resources import load_pil_icon
from .main_window import SettingsDialog
from .optimized_main_window import OptimizedMainWindow

if os.name == "nt":
    try:
        import pystray
    except ImportError:  # pragma: no cover - packaging/runtime guard
        pystray = None
else:  # pragma: no cover - this application targets Windows
    pystray = None


def _walk_widgets(widget: tk.Misc):
    for child in widget.winfo_children():
        yield child
        yield from _walk_widgets(child)


class TraySettingsDialog(SettingsDialog):
    """Settings dialog with tray-specific wording for the existing close preference."""

    def __init__(self, parent: tk.Misc, settings) -> None:
        super().__init__(parent, settings)
        for widget in _walk_widgets(self):
            try:
                if widget.cget("text") == "关闭窗口时最小化":
                    widget.configure(text="点击关闭按钮时最小化到托盘")
                    break
            except (tk.TclError, KeyError):
                continue


class TrayMainWindow(OptimizedMainWindow):
    """Optimized main window with a real Windows notification-area icon."""

    def __init__(self, *args, **kwargs) -> None:
        self._tray_icon: Any | None = None
        self._tray_thread: threading.Thread | None = None
        super().__init__(*args, **kwargs)

    @staticmethod
    def _create_tray_image():
        return load_pil_icon(64)

    def _dispatch_to_ui(self, callback: Callable[[], None]) -> None:
        if self._quitting:
            return

        def invoke(_value: Any) -> None:
            if not self._quitting:
                callback()

        self.async_bridge.events.put(("result", invoke, None))

    def _tray_open_clicked(self, _icon: Any, _item: Any) -> None:
        self._dispatch_to_ui(self._restore_from_tray)

    def _tray_exit_clicked(self, _icon: Any, _item: Any) -> None:
        self._dispatch_to_ui(self.quit)

    def _start_tray(self) -> None:
        if self._tray_icon is not None:
            return
        if os.name != "nt":
            raise RuntimeError("系统托盘功能仅支持 Windows")
        if pystray is None:
            raise RuntimeError("缺少 pystray 运行库")

        menu = pystray.Menu(
            pystray.MenuItem("打开", self._tray_open_clicked, default=True),
            pystray.MenuItem("退出", self._tray_exit_clicked),
        )
        icon = pystray.Icon(
            "OpenAITunnelManager",
            self._create_tray_image(),
            "OpenAI MCP Tunnel Manager",
            menu,
        )
        thread = threading.Thread(
            target=icon.run,
            name="openai-tunnel-manager-tray",
            daemon=True,
        )
        self._tray_icon = icon
        self._tray_thread = thread
        thread.start()

    def _stop_tray(self) -> None:
        icon = self._tray_icon
        self._tray_icon = None
        self._tray_thread = None
        if icon is not None:
            try:
                icon.stop()
            except Exception:
                pass

    def _restore_from_tray(self) -> None:
        if self._quitting:
            return
        try:
            self.root.deiconify()
            self.root.state("normal")
            self.root.lift()
            self.root.focus_force()
            self.status_var.set("已从系统托盘恢复")
        except tk.TclError:
            pass

    def _on_close(self) -> None:
        if not self.settings.close_to_tray:
            self.quit()
            return
        try:
            self._start_tray()
            self.root.withdraw()
            self.status_var.set("已最小化到系统托盘")
        except Exception as exc:
            # Do not unexpectedly terminate the manager if a tray backend fails.
            self.status_var.set(f"系统托盘不可用：{exc}；已最小化到任务栏")
            try:
                self.root.iconify()
            except tk.TclError:
                pass

    def open_settings(self) -> None:
        dlg = TraySettingsDialog(self.root, self.settings)
        self._wait_dialog(dlg)
        if not dlg.result:
            return

        old_start = self.settings.start_with_windows
        for key, value in dlg.result.items():
            setattr(self.settings, key, value)

        if not self.settings.close_to_tray:
            self._stop_tray()

        self.client.binary_path = self.settings.binary_path
        self.store.save(self.settings)
        self._schedule_refresh()

        if old_start != self.settings.start_with_windows:
            try:
                set_windows_startup(self.settings.start_with_windows)
            except Exception as exc:
                self._error(f"修改 Windows 开机启动失败：{exc}")

        if not self._ensure_binary(show_warning=True):
            return
        self.async_bridge.submit(
            "capabilities",
            self.client.capabilities,
            self._show_capabilities,
            self._error,
        )
        self.refresh_inventory(force=True)

    def quit(self) -> None:
        self._stop_tray()
        super().quit()
