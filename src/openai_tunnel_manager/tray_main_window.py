from __future__ import annotations

import os
import threading
import tkinter as tk
from tkinter import ttk
from typing import Any, Callable

from .autostart import set_windows_startup
from .icon_resources import load_pil_icon
from .main_window import SettingsDialog
from .models import RuntimeState
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
    """Optimized main window with a persistent Windows notification-area icon."""

    def __init__(self, *args, **kwargs) -> None:
        self._tray_icon: Any | None = None
        self._tray_thread: threading.Thread | None = None
        super().__init__(*args, **kwargs)

        if os.name == "nt":
            try:
                self._start_tray()
            except Exception as exc:
                self.status_var.set(f"系统托盘不可用：{exc}")

    def _build_ui(self) -> None:
        super()._build_ui()

        # Remove legacy Profile import/export controls from the toolbar.
        kept_profile_buttons: list[ttk.Button] = []
        preference_button: ttk.Button | None = None
        for button in self.profile_buttons:
            text = str(button.cget("text"))
            if text in {"导入 Profile", "导出 Profile"}:
                button.destroy()
                continue
            kept_profile_buttons.append(button)
            if text == "本机偏好 / 密钥":
                preference_button = button
        self.profile_buttons = kept_profile_buttons

        # Remove the old large refresh button below the list.
        for widget in list(_walk_widgets(self.root)):
            if isinstance(widget, ttk.Button) and str(widget.cget("text")) == "从 tunnel-client 重新读取":
                widget.destroy()

        self._style_config_sidebar()

        # Put the compact refresh action directly beside local preferences.
        if preference_button is not None:
            self.refresh_button = ttk.Button(
                preference_button.master,
                text="刷新",
                command=lambda: self.refresh_inventory(force=True),
            )
            self.refresh_button.pack(side="left", padx=(0, 6), after=preference_button)
            self.profile_buttons.append(self.refresh_button)

        self.doctor_button.configure(text="诊断")
        self.config_location_button = ttk.Button(
            self.doctor_button.master,
            text="打开配置位置",
            command=self.open_config_location,
            state="disabled",
        )
        self.config_location_button.pack(side="left", padx=(0, 6), after=self.doctor_button)
        self.action_buttons.append(self.config_location_button)

    def _style_config_sidebar(self) -> None:
        """Make the configuration list read like a compact desktop sidebar."""
        sidebar = self.listbox.master
        try:
            sidebar.configure(padding=(12, 10, 10, 10), width=310)
        except tk.TclError:
            pass

        self.config_list_title: ttk.Label | None = None
        for widget in sidebar.winfo_children():
            if isinstance(widget, ttk.Label):
                try:
                    if str(widget.cget("text")) == "tunnel-client 配置 / 运行实例":
                        widget.configure(text="配置列表", font=("Segoe UI", 10, "bold"))
                        self.config_list_title = widget
                        break
                except tk.TclError:
                    continue

        self.listbox.configure(
            width=36,
            activestyle="none",
            relief="flat",
            borderwidth=0,
            highlightthickness=0,
            selectborderwidth=0,
            font=("Segoe UI", 10),
        )
        self.listbox.grid_configure(row=1, column=0, sticky="nsew", pady=(5, 0))

        sidebar.columnconfigure(0, weight=1)
        sidebar.columnconfigure(1, weight=0)
        self.config_list_scrollbar = ttk.Scrollbar(sidebar, orient="vertical", command=self.listbox.yview)
        self.config_list_scrollbar.grid(row=1, column=1, sticky="ns", pady=(5, 0), padx=(5, 0))
        self.listbox.configure(yscrollcommand=self.config_list_scrollbar.set)

    def _list_label(self, item, state: RuntimeState) -> str:
        started = state in {RuntimeState.STARTING, RuntimeState.RUNNING, RuntimeState.READY}
        status = "已启动" if started else "已停止"
        return f"  [{status}]  {item.name}"

    def _profile_path_for_item(self, item) -> str:
        status = self.statuses.get(self._item_key(item))
        return (
            (status.profile_path if status else "")
            or item.profile_path
            or item.runtime_profile_path
            or ""
        )

    def open_config_location(self) -> None:
        item = self.selected_item()
        if not item:
            self._error("请先选择一个配置")
            return
        self._open_file_location(self._profile_path_for_item(item), "配置文件")

    def _update_action_states(self, *args, **kwargs) -> None:
        super()._update_action_states(*args, **kwargs)
        if not hasattr(self, "config_location_button"):
            return
        item = self.selected_item()
        can_open = bool(self.binary_ok and item and self._profile_path_for_item(item))
        self.config_location_button.configure(state="normal" if can_open else "disabled")

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
