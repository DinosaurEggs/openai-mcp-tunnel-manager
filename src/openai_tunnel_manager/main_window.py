from __future__ import annotations

import json
import os
import queue
import re
import shutil
import tkinter as tk
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from tkinter import filedialog, messagebox, simpledialog, ttk
from typing import Any, Callable

from .autostart import set_windows_startup
from .credentials import CredentialStore, WindowsCredentialStore
from .models import AppSettings, ManagedItem, McpType, ProfilePreference, ProfileSpec, RuntimeState, RuntimeStatus
from .settings_store import SettingsStore
from .tunnel_client import TunnelClient


_RUNTIME_STATE_ZH = {
    RuntimeState.CONFIGURED: "仅配置",
    RuntimeState.STOPPED: "已停止",
    RuntimeState.STARTING: "正在启动",
    RuntimeState.RUNNING: "运行中",
    RuntimeState.READY: "已就绪",
    RuntimeState.ERROR: "错误",
    RuntimeState.STALE: "状态失效",
    RuntimeState.UNKNOWN: "未知",
}


def _runtime_state_text(state: RuntimeState) -> str:
    return _RUNTIME_STATE_ZH.get(state, state.value)


def _source_text(item: ManagedItem) -> str:
    if item.profile_listed and item.has_runtime_alias:
        return "Profile + 运行实例"
    if item.has_runtime_alias:
        return "运行实例"
    return "Profile 配置"


def _replace_yaml_section_scalar(text: str, section: str, key: str, value: str) -> str:
    """Replace a direct scalar under a top-level YAML section while preserving other text."""
    lines = text.splitlines(keepends=True)
    section_index = None
    section_indent = 0
    for i, line in enumerate(lines):
        stripped = line.strip()
        if not stripped or line.lstrip().startswith("#"):
            continue
        indent = len(line) - len(line.lstrip(" "))
        if indent == 0 and stripped == f"{section}:":
            section_index = i
            section_indent = indent
            break
    if section_index is None:
        raise ValueError(f"高级配置中缺少 {section}: 段，无法安全应用常用设置")
    end = len(lines)
    for i in range(section_index + 1, len(lines)):
        line = lines[i]
        stripped = line.strip()
        if not stripped or line.lstrip().startswith("#"):
            continue
        indent = len(line) - len(line.lstrip(" "))
        if indent <= section_indent:
            end = i
            break
    pattern = re.compile(rf"^(\s*){re.escape(key)}\s*:")
    for i in range(section_index + 1, end):
        m = pattern.match(lines[i])
        if m:
            nl = "\n" if lines[i].endswith("\n") else ""
            lines[i] = f"{m.group(1)}{key}: {json.dumps(value, ensure_ascii=False)}{nl}"
            return "".join(lines)
    indent = "  "
    nl = "\n" if text.endswith("\n") or lines else ""
    lines.insert(section_index + 1, f"{indent}{key}: {json.dumps(value, ensure_ascii=False)}{nl}")
    return "".join(lines)


def _replace_yaml_main_mcp_target(text: str, target_kind: str, value: str) -> str:
    """Replace the documented main MCP URL/command entry without touching other channels."""
    lines = text.splitlines(keepends=True)
    section = ""
    mode = ""
    channel = "main"
    wanted_mode = "server_url" if target_kind == "server_url" else "command"
    wanted_key = "url" if wanted_mode == "server_url" else "command"
    for i, line in enumerate(lines):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        indent = len(line) - len(line.lstrip(" "))
        stripped = line.strip()
        if indent == 0 and stripped.endswith(":"):
            section = stripped[:-1]
            mode = ""
            channel = "main"
            continue
        if section != "mcp":
            continue
        if stripped == "server_urls:":
            mode, channel = "server_url", "main"
            continue
        if stripped == "commands:":
            mode, channel = "command", "main"
            continue
        m = re.match(r"-?\s*channel\s*:\s*(.+)$", stripped)
        if m and mode:
            channel = TunnelClient._yaml_scalar(m.group(1))
            continue
        if mode == wanted_mode and channel == "main":
            m = re.match(rf"{wanted_key}\s*:\s*(.+)$", stripped)
            if m:
                prefix = line[: len(line) - len(line.lstrip(" "))]
                nl = "\n" if line.endswith("\n") else ""
                lines[i] = f"{prefix}{wanted_key}: {json.dumps(value, ensure_ascii=False)}{nl}"
                return "".join(lines)
    raise ValueError("没有找到可安全修改的 main MCP 绑定；请直接在“高级配置”页修改完整 YAML")


def _apply_common_profile_fields(text: str, initial: dict[str, str], tunnel_id: str, target_value: str) -> str:
    """Apply only changed common fields to YAML/JSON while preserving all unknown fields."""
    if not text.strip():
        raise ValueError("Profile 内容不能为空")
    try:
        raw = json.loads(text)
    except json.JSONDecodeError:
        raw = None
    changed_tunnel = tunnel_id != initial.get("tunnel_id", "")
    changed_target = target_value != initial.get("target_value", "")
    if isinstance(raw, dict):
        if changed_tunnel:
            cp = raw.get("control_plane")
            if not isinstance(cp, dict):
                raise ValueError("高级配置中缺少 control_plane 对象")
            cp["tunnel_id"] = tunnel_id
        if changed_target:
            mcp = raw.get("mcp")
            if not isinstance(mcp, dict):
                raise ValueError("高级配置中缺少 mcp 对象")
            key = "server_urls" if initial.get("target_kind") == "server_url" else "commands"
            value_key = "url" if key == "server_urls" else "command"
            entries = mcp.get(key)
            if not isinstance(entries, list):
                raise ValueError("没有找到可安全修改的 main MCP 绑定")
            entry = next((x for x in entries if isinstance(x, dict) and str(x.get("channel", "main")) == "main"), None)
            if not isinstance(entry, dict):
                raise ValueError("没有找到可安全修改的 main MCP 绑定")
            entry[value_key] = target_value
        return json.dumps(raw, ensure_ascii=False, indent=2) + "\n"
    if changed_tunnel:
        text = _replace_yaml_section_scalar(text, "control_plane", "tunnel_id", tunnel_id)
    if changed_target:
        kind = initial.get("target_kind", "")
        if kind not in {"server_url", "command"}:
            raise ValueError("当前 Profile 的 main MCP 绑定不是常用格式，请在“高级配置”页修改")
        text = _replace_yaml_main_mcp_target(text, kind, target_value)
    return text


def _center_window(window: tk.Misc, parent: tk.Misc | None = None) -> None:
    """Center a Tk/Toplevel window over its parent, falling back to the screen."""
    window.update_idletasks()
    width = window.winfo_width() if window.winfo_width() > 1 else window.winfo_reqwidth()
    height = window.winfo_height() if window.winfo_height() > 1 else window.winfo_reqheight()
    if parent is not None:
        try:
            parent.update_idletasks()
            pw = parent.winfo_width() if parent.winfo_width() > 1 else parent.winfo_reqwidth()
            ph = parent.winfo_height() if parent.winfo_height() > 1 else parent.winfo_reqheight()
            x = parent.winfo_x() + (pw - width) // 2
            y = parent.winfo_y() + (ph - height) // 2
        except tk.TclError:
            parent = None
    if parent is None:
        x = (window.winfo_screenwidth() - width) // 2
        y = (window.winfo_screenheight() - height) // 2
    sw, sh = window.winfo_screenwidth(), window.winfo_screenheight()
    x = max(0, min(x, max(0, sw - width)))
    y = max(0, min(y, max(0, sh - height)))
    window.geometry(f"+{x}+{y}")


class AsyncBridge:
    def __init__(self, root: tk.Misc, max_workers: int = 4) -> None:
        self.root = root
        self.pool = ThreadPoolExecutor(max_workers=max_workers, thread_name_prefix="tunnel-manager")
        self.events: queue.Queue[tuple[str, Callable[..., None] | None, Any]] = queue.Queue()
        self.busy: set[str] = set()
        self._closed = False
        self._pump_after: str | None = self.root.after(50, self._pump)

    def submit(
        self,
        key: str,
        fn: Callable[[], Any],
        on_result: Callable[[Any], None] | None = None,
        on_error: Callable[[str], None] | None = None,
    ) -> bool:
        if key in self.busy or self._closed:
            return False
        self.busy.add(key)

        def run() -> None:
            try:
                value = fn()
            except Exception as exc:
                self.events.put(("error", on_error, str(exc).strip() or exc.__class__.__name__))
            else:
                self.events.put(("result", on_result, value))
            finally:
                self.events.put(("done", None, key))

        self.pool.submit(run)
        return True

    def _pump(self) -> None:
        if self._closed:
            return
        try:
            while True:
                kind, callback, value = self.events.get_nowait()
                if kind == "done":
                    self.busy.discard(str(value))
                elif callback:
                    callback(value)
        except queue.Empty:
            pass
        self._pump_after = self.root.after(50, self._pump)

    def close(self) -> None:
        self._closed = True
        if self._pump_after:
            try:
                self.root.after_cancel(self._pump_after)
            except tk.TclError:
                pass
            self._pump_after = None
        self.pool.shutdown(wait=False, cancel_futures=True)


class ProfileEditor(tk.Toplevel):
    """Create a real tunnel-client profile. The GUI never persists these fields itself."""

    def __init__(self, parent: tk.Misc, has_secret: bool = False) -> None:
        super().__init__(parent)
        self.withdraw()
        self.result: tuple[ProfileSpec, str, ProfilePreference] | None = None
        self.title("新建 tunnel-client Profile")
        self.resizable(True, False)
        self.transient(parent)

        body = ttk.Frame(self, padding=14)
        body.grid(sticky="nsew")
        body.columnconfigure(1, weight=1)
        self.name = tk.StringVar()
        self.tunnel_id = tk.StringVar()
        self.kind = tk.StringVar(value=McpType.HTTP.value)
        self.target = tk.StringVar()
        self.secret = tk.StringVar()
        self.auto_connect = tk.BooleanVar(value=False)
        self.auto_reconnect = tk.BooleanVar(value=False)
        self.enabled = tk.BooleanVar(value=True)

        rows = [
            ("Profile 名称", ttk.Entry(body, textvariable=self.name)),
            ("Tunnel ID", ttk.Entry(body, textvariable=self.tunnel_id)),
        ]
        for r, (label, widget) in enumerate(rows):
            ttk.Label(body, text=label).grid(row=r, column=0, sticky="w", padx=(0, 10), pady=5)
            widget.grid(row=r, column=1, sticky="ew", pady=5)
        ttk.Label(body, text="MCP 类型").grid(row=2, column=0, sticky="w", padx=(0, 10), pady=5)
        ttk.Combobox(body, textvariable=self.kind, values=(McpType.HTTP.value, McpType.STDIO.value), state="readonly").grid(
            row=2, column=1, sticky="ew", pady=5
        )
        ttk.Label(body, text="MCP 地址 / 命令").grid(row=3, column=0, sticky="w", padx=(0, 10), pady=5)
        ttk.Entry(body, textvariable=self.target).grid(row=3, column=1, sticky="ew", pady=5)
        ttk.Label(body, text="Runtime API Key").grid(row=4, column=0, sticky="w", padx=(0, 10), pady=5)
        ttk.Entry(body, textvariable=self.secret, show="●").grid(row=4, column=1, sticky="ew", pady=5)
        ttk.Label(
            body,
            text="可留空并使用系统环境变量；填写后只保存到 Windows 凭据管理器",
            foreground="#666666",
        ).grid(row=5, column=1, sticky="w", pady=(0, 8))
        ttk.Checkbutton(body, text="程序启动后自动连接", variable=self.auto_connect).grid(row=6, column=1, sticky="w")
        ttk.Checkbutton(body, text="异常停止后自动重连", variable=self.auto_reconnect).grid(row=7, column=1, sticky="w")
        ttk.Checkbutton(body, text="启用此配置", variable=self.enabled).grid(row=8, column=1, sticky="w")

        buttons = ttk.Frame(body)
        buttons.grid(row=9, column=0, columnspan=2, sticky="e", pady=(14, 0))
        ttk.Button(buttons, text="取消", command=self.destroy).pack(side="right", padx=(8, 0))
        ttk.Button(buttons, text="创建", command=self._save).pack(side="right")
        self.protocol("WM_DELETE_WINDOW", self.destroy)
        _center_window(self, parent)
        self.deiconify()
        self.grab_set()
        self.focus_set()

    def _save(self) -> None:
        try:
            spec = ProfileSpec(
                name=self.name.get(),
                tunnel_id=self.tunnel_id.get(),
                mcp_type=McpType(self.kind.get()),
                mcp_target=self.target.get(),
            )
        except ValueError as exc:
            messagebox.showerror("配置无效", str(exc), parent=self)
            return
        errors = spec.validate()
        if errors:
            messagebox.showerror("配置无效", "\n".join(errors), parent=self)
            return
        pref = ProfilePreference(
            auto_connect=self.auto_connect.get(),
            auto_reconnect=self.auto_reconnect.get(),
            enabled=self.enabled.get(),
        )
        self.result = (spec, self.secret.get(), pref)
        self.destroy()


# Compatibility for tests/imports from previous versions.
TunnelEditor = ProfileEditor


class ExistingProfileEditor(tk.Toplevel):
    """Edit an existing tunnel-client profile entirely inside the GUI."""

    def __init__(
        self,
        parent: tk.Misc,
        name: str,
        path: str,
        text: str,
        metadata: dict[str, str],
        pref: ProfilePreference,
        has_secret: bool,
    ) -> None:
        super().__init__(parent)
        self.withdraw()
        self.result: tuple[str, ProfilePreference, str, bool] | None = None
        self.initial_metadata = dict(metadata)
        self.title(f"编辑 Profile - {name}")
        self.geometry("780x620")
        self.minsize(680, 520)
        self.transient(parent)
        self.rowconfigure(0, weight=1)
        self.columnconfigure(0, weight=1)

        notebook = ttk.Notebook(self)
        notebook.grid(row=0, column=0, sticky="nsew", padx=12, pady=(12, 6))

        common = ttk.Frame(notebook, padding=14)
        common.columnconfigure(1, weight=1)
        notebook.add(common, text="常用配置")
        self.tunnel_id = tk.StringVar(value=metadata.get("tunnel_id", ""))
        self.target = tk.StringVar(value=metadata.get("target_value", ""))
        target_kind = metadata.get("target_kind", "")
        type_text = "HTTP URL" if target_kind == "server_url" else "STDIO 命令" if target_kind == "command" else "高级/未识别"
        rows = [
            ("Profile 名称", name),
            ("Profile 文件", path),
            ("MCP 类型", type_text),
            ("API Key 引用", metadata.get("api_key_ref", "") or "-"),
        ]
        for row, (label, value) in enumerate(rows):
            ttk.Label(common, text=label).grid(row=row, column=0, sticky="nw", padx=(0, 12), pady=6)
            ttk.Label(common, text=value, wraplength=560).grid(row=row, column=1, sticky="w", pady=6)
        ttk.Label(common, text="Tunnel ID").grid(row=4, column=0, sticky="w", padx=(0, 12), pady=6)
        ttk.Entry(common, textvariable=self.tunnel_id).grid(row=4, column=1, sticky="ew", pady=6)
        ttk.Label(common, text="MCP 地址 / 命令").grid(row=5, column=0, sticky="w", padx=(0, 12), pady=6)
        target_entry = ttk.Entry(common, textvariable=self.target)
        target_entry.grid(row=5, column=1, sticky="ew", pady=6)
        if target_kind not in {"server_url", "command"}:
            target_entry.state(["disabled"])
        ttk.Label(
            common,
            text="这里只编辑最常用字段；多通道、代理、证书、Health、日志等完整配置请在“高级配置”页修改。",
            foreground="#666666",
            wraplength=620,
        ).grid(row=6, column=0, columnspan=2, sticky="w", pady=(12, 0))

        advanced = ttk.Frame(notebook, padding=10)
        advanced.rowconfigure(1, weight=1)
        advanced.columnconfigure(0, weight=1)
        notebook.add(advanced, text="高级配置")
        ttk.Label(
            advanced,
            text="完整 tunnel-client Profile YAML/JSON。保存时由 tunnel-client 完整校验后再覆盖官方 Profile。",
            foreground="#666666",
        ).grid(row=0, column=0, sticky="w", pady=(0, 8))
        text_frame = ttk.Frame(advanced)
        text_frame.grid(row=1, column=0, sticky="nsew")
        text_frame.rowconfigure(0, weight=1)
        text_frame.columnconfigure(0, weight=1)
        self.raw_text = tk.Text(text_frame, wrap="none", undo=True, font=("Consolas", 10))
        yscroll = ttk.Scrollbar(text_frame, orient="vertical", command=self.raw_text.yview)
        xscroll = ttk.Scrollbar(text_frame, orient="horizontal", command=self.raw_text.xview)
        self.raw_text.configure(yscrollcommand=yscroll.set, xscrollcommand=xscroll.set)
        self.raw_text.grid(row=0, column=0, sticky="nsew")
        yscroll.grid(row=0, column=1, sticky="ns")
        xscroll.grid(row=1, column=0, sticky="ew")
        self.raw_text.insert("1.0", text)

        local = ttk.Frame(notebook, padding=14)
        local.columnconfigure(1, weight=1)
        notebook.add(local, text="本机设置")
        self.enabled = tk.BooleanVar(value=pref.enabled)
        self.auto_connect = tk.BooleanVar(value=pref.auto_connect)
        self.auto_reconnect = tk.BooleanVar(value=pref.auto_reconnect)
        self.secret = tk.StringVar()
        self.delete_secret = tk.BooleanVar(value=False)
        ttk.Checkbutton(local, text="启用此配置", variable=self.enabled).grid(row=0, column=0, columnspan=2, sticky="w", pady=3)
        ttk.Checkbutton(local, text="程序启动后自动连接", variable=self.auto_connect).grid(row=1, column=0, columnspan=2, sticky="w", pady=3)
        ttk.Checkbutton(local, text="异常停止后自动重连", variable=self.auto_reconnect).grid(row=2, column=0, columnspan=2, sticky="w", pady=3)
        ttk.Label(local, text="Runtime API Key").grid(row=3, column=0, sticky="w", padx=(0, 10), pady=(12, 5))
        ttk.Entry(local, textvariable=self.secret, show="●").grid(row=3, column=1, sticky="ew", pady=(12, 5))
        ttk.Label(
            local,
            text="已保存，留空表示保持不变" if has_secret else "未在本程序保存；填写后存入 Windows 凭据管理器",
            foreground="#666666",
        ).grid(row=4, column=1, sticky="w")
        if has_secret:
            ttk.Checkbutton(local, text="删除本程序保存的密钥", variable=self.delete_secret).grid(row=5, column=1, sticky="w", pady=(5, 0))

        buttons = ttk.Frame(self, padding=(12, 6, 12, 12))
        buttons.grid(row=1, column=0, sticky="ew")
        ttk.Button(buttons, text="取消", command=self.destroy).pack(side="right", padx=(8, 0))
        ttk.Button(buttons, text="保存", command=self._save).pack(side="right")
        self.protocol("WM_DELETE_WINDOW", self.destroy)
        _center_window(self, parent)
        self.deiconify()
        self.grab_set()
        self.focus_set()

    def _save(self) -> None:
        tunnel_id = self.tunnel_id.get().strip()
        target = self.target.get().strip()
        kind = self.initial_metadata.get("target_kind", "")
        initial_tunnel = self.initial_metadata.get("tunnel_id", "")
        initial_target = self.initial_metadata.get("target_value", "")
        if tunnel_id != initial_tunnel and tunnel_id and not re.fullmatch(r"tunnel_[0-9a-f]{32}", tunnel_id):
            messagebox.showerror("配置无效", "Tunnel ID 必须是 tunnel_ 加 32 个小写十六进制字符", parent=self)
            return
        if target != initial_target and kind in {"server_url", "command"}:
            validation_tid = tunnel_id or "tunnel_00000000000000000000000000000000"
            mcp_type = McpType.HTTP if kind == "server_url" else McpType.STDIO
            spec = ProfileSpec("validate", validation_tid, mcp_type, target)
            errors = [e for e in spec.validate() if not e.startswith("Tunnel ID")]
            if errors:
                messagebox.showerror("配置无效", "\n".join(errors), parent=self)
                return
        raw = self.raw_text.get("1.0", "end-1c")
        try:
            raw = _apply_common_profile_fields(raw, self.initial_metadata, tunnel_id, target)
        except ValueError as exc:
            messagebox.showerror("无法应用常用配置", str(exc), parent=self)
            return
        pref = ProfilePreference(self.auto_connect.get(), self.auto_reconnect.get(), self.enabled.get())
        self.result = (raw, pref, self.secret.get(), self.delete_secret.get())
        self.destroy()


class PreferenceDialog(tk.Toplevel):
    def __init__(self, parent: tk.Misc, name: str, pref: ProfilePreference, has_secret: bool) -> None:
        super().__init__(parent)
        self.withdraw()
        self.result: tuple[ProfilePreference, str, bool] | None = None
        self.title(f"本机偏好 / 密钥 - {name}")
        self.transient(parent)
        body = ttk.Frame(self, padding=14)
        body.grid(sticky="nsew")
        body.columnconfigure(1, weight=1)
        self.enabled = tk.BooleanVar(value=pref.enabled)
        self.auto_connect = tk.BooleanVar(value=pref.auto_connect)
        self.auto_reconnect = tk.BooleanVar(value=pref.auto_reconnect)
        self.secret = tk.StringVar()
        self.delete_secret = tk.BooleanVar(value=False)

        ttk.Checkbutton(body, text="启用此配置", variable=self.enabled).grid(row=0, column=0, columnspan=2, sticky="w", pady=3)
        ttk.Checkbutton(body, text="程序启动后自动连接", variable=self.auto_connect).grid(row=1, column=0, columnspan=2, sticky="w", pady=3)
        ttk.Checkbutton(body, text="异常停止后自动重连", variable=self.auto_reconnect).grid(row=2, column=0, columnspan=2, sticky="w", pady=3)
        ttk.Label(body, text="Runtime API Key").grid(row=3, column=0, sticky="w", padx=(0, 10), pady=(10, 5))
        ttk.Entry(body, textvariable=self.secret, show="●").grid(row=3, column=1, sticky="ew", pady=(10, 5))
        ttk.Label(
            body,
            text="已保存，留空表示保持不变" if has_secret else "未在本程序中保存；可继续使用 profile 的 env:/file: 引用",
            foreground="#666666",
        ).grid(row=4, column=1, sticky="w")
        if has_secret:
            ttk.Checkbutton(body, text="删除本程序保存的密钥", variable=self.delete_secret).grid(row=5, column=1, sticky="w", pady=(5, 0))
        buttons = ttk.Frame(body)
        buttons.grid(row=6, column=0, columnspan=2, sticky="e", pady=(14, 0))
        ttk.Button(buttons, text="取消", command=self.destroy).pack(side="right", padx=(8, 0))
        ttk.Button(buttons, text="保存", command=self._save).pack(side="right")
        self.protocol("WM_DELETE_WINDOW", self.destroy)
        _center_window(self, parent)
        self.deiconify()
        self.grab_set()
        self.focus_set()

    def _save(self) -> None:
        self.result = (
            ProfilePreference(self.auto_connect.get(), self.auto_reconnect.get(), self.enabled.get()),
            self.secret.get(),
            self.delete_secret.get(),
        )
        self.destroy()


class SettingsDialog(tk.Toplevel):
    def __init__(self, parent: tk.Misc, settings: AppSettings) -> None:
        super().__init__(parent)
        self.withdraw()
        self.result: dict[str, Any] | None = None
        self.title("设置")
        self.transient(parent)
        body = ttk.Frame(self, padding=14)
        body.grid(sticky="nsew")
        body.columnconfigure(1, weight=1)
        self.binary = tk.StringVar(value=settings.binary_path)
        self.minimize_on_close = tk.BooleanVar(value=settings.close_to_tray)
        self.start_windows = tk.BooleanVar(value=settings.start_with_windows)
        self.refresh_seconds = tk.IntVar(value=max(2, settings.refresh_interval_ms // 1000))

        ttk.Label(body, text="tunnel-client.exe").grid(row=0, column=0, sticky="w", padx=(0, 10), pady=5)
        row = ttk.Frame(body)
        row.grid(row=0, column=1, sticky="ew")
        row.columnconfigure(0, weight=1)
        ttk.Entry(row, textvariable=self.binary).grid(row=0, column=0, sticky="ew")
        ttk.Button(row, text="浏览…", command=self._browse).grid(row=0, column=1, padx=(8, 0))
        ttk.Label(body, text="留空时自动搜索程序目录与 PATH", foreground="#666666").grid(row=1, column=1, sticky="w")
        ttk.Checkbutton(body, text="关闭窗口时最小化", variable=self.minimize_on_close).grid(row=2, column=1, sticky="w", pady=(8, 2))
        ttk.Checkbutton(body, text="Windows 登录后启动", variable=self.start_windows).grid(row=3, column=1, sticky="w", pady=2)
        ttk.Label(body, text="刷新间隔（秒）").grid(row=4, column=0, sticky="w", padx=(0, 10), pady=5)
        ttk.Spinbox(body, from_=2, to=60, textvariable=self.refresh_seconds, width=8).grid(row=4, column=1, sticky="w", pady=5)
        buttons = ttk.Frame(body)
        buttons.grid(row=5, column=0, columnspan=2, sticky="e", pady=(14, 0))
        ttk.Button(buttons, text="取消", command=self.destroy).pack(side="right", padx=(8, 0))
        ttk.Button(buttons, text="保存", command=self._save).pack(side="right")
        self.protocol("WM_DELETE_WINDOW", self.destroy)
        _center_window(self, parent)
        self.deiconify()
        self.grab_set()
        self.focus_set()

    def _browse(self) -> None:
        path = filedialog.askopenfilename(parent=self, title="选择 tunnel-client.exe", filetypes=[("tunnel-client", "tunnel-client.exe"), ("所有文件", "*.*")])
        if path:
            self.binary.set(path)

    def _save(self) -> None:
        path = self.binary.get().strip()
        if path and not Path(path).expanduser().is_file():
            messagebox.showerror("文件不存在", "找不到指定的 tunnel-client.exe，请重新选择。", parent=self)
            return
        try:
            seconds = max(2, min(60, int(self.refresh_seconds.get())))
        except (ValueError, tk.TclError):
            messagebox.showerror("设置无效", "刷新间隔必须是 2 到 60 秒之间的整数。", parent=self)
            return
        self.result = {
            "binary_path": path,
            "close_to_tray": self.minimize_on_close.get(),
            "start_with_windows": self.start_windows.get(),
            "refresh_interval_ms": seconds * 1000,
        }
        self.destroy()


class MainWindow:
    def __init__(
        self,
        root: tk.Tk,
        store: SettingsStore | None = None,
        credentials: CredentialStore | None = None,
        client: TunnelClient | None = None,
    ) -> None:
        self.root = root
        self.store = store or SettingsStore()
        self.settings = self.store.load()
        if credentials is not None:
            self.credentials = credentials
        elif os.name == "nt":
            try:
                self.credentials = WindowsCredentialStore()
            except Exception:
                self.credentials = None
        else:
            self.credentials = None
        self.client = client or TunnelClient(self.settings.binary_path)
        self.client.binary_path = self.settings.binary_path
        self.async_bridge = AsyncBridge(root)
        self.items: list[ManagedItem] = []
        self.statuses: dict[str, RuntimeStatus] = {}
        self.manual_stopped: set[str] = set()
        self.reconnect_attempts: dict[str, int] = {}
        self._after_ids: set[str] = set()
        self._refresh_after: str | None = None
        self._log_after: str | None = None
        self._quitting = False
        self._initial_auto_connect_done = False
        self._raw_log = ""
        self._current_log_path = ""
        self._log_paths: dict[str, str] = {}
        self._log_cache: dict[str, str] = {}
        self._operation_busy: set[str] = set()
        self.binary_ok = False

        self.root.title("OpenAI MCP Tunnel Manager")
        self.root.geometry("1060x690")
        self.root.minsize(880, 580)
        self._build_ui()
        _center_window(self.root)
        self.root.protocol("WM_DELETE_WINDOW", self._on_close)
        self.binary_ok = self._ensure_binary(show_warning=False)
        if self.binary_ok:
            self.refresh_inventory()
        self._schedule_refresh()
        self._schedule_log_refresh()

    def _after(self, delay_ms: int, callback: Callable[[], None]) -> str:
        holder: dict[str, str] = {}
        def wrapped() -> None:
            ident = holder.get("id")
            if ident:
                self._after_ids.discard(ident)
            if not self._quitting:
                callback()
        ident = self.root.after(delay_ms, wrapped)
        holder["id"] = ident
        self._after_ids.add(ident)
        return ident

    def _build_ui(self) -> None:
        outer = ttk.Frame(self.root, padding=8)
        outer.pack(fill="both", expand=True)
        outer.columnconfigure(1, weight=1)
        outer.rowconfigure(1, weight=1)

        toolbar = ttk.Frame(outer)
        toolbar.grid(row=0, column=0, columnspan=2, sticky="ew", pady=(0, 8))
        self.profile_buttons: list[ttk.Button] = []
        for text, cmd in [
            ("新建 Profile", self.add_profile),
            ("编辑 Profile", self.edit_profile),
            ("删除", self.delete_selected),
            ("导入 Profile", self.import_profile),
            ("导出 Profile", self.export_selected),
            ("本机偏好 / 密钥", self.edit_preferences),
        ]:
            button = ttk.Button(toolbar, text=text, command=cmd)
            button.pack(side="left", padx=(0, 6))
            self.profile_buttons.append(button)
        ttk.Button(toolbar, text="设置", command=self.open_settings).pack(side="right", padx=(6, 0))
        self.exit_button = ttk.Button(toolbar, text="退出", command=self.quit)
        self.exit_button.pack(side="right")

        left = ttk.Frame(outer, width=270)
        left.grid(row=1, column=0, sticky="nsew", padx=(0, 8))
        left.rowconfigure(1, weight=1)
        left.columnconfigure(0, weight=1)
        ttk.Label(left, text="tunnel-client 配置 / 运行实例", font=("TkDefaultFont", 10, "bold")).grid(row=0, column=0, sticky="w", pady=(0, 6))
        self.listbox = tk.Listbox(left, width=34, exportselection=False)
        self.listbox.grid(row=1, column=0, sticky="nsew")
        self.listbox.bind("<<ListboxSelect>>", lambda _e: self._selection_changed())
        ttk.Button(left, text="从 tunnel-client 重新读取", command=lambda: self.refresh_inventory(force=True)).grid(row=2, column=0, sticky="ew", pady=(7, 0))

        right = ttk.Frame(outer)
        right.grid(row=1, column=1, sticky="nsew")
        right.columnconfigure(0, weight=1)
        right.rowconfigure(2, weight=1)

        head = ttk.Frame(right)
        head.grid(row=0, column=0, sticky="ew")
        head.columnconfigure(0, weight=1)
        self.title_var = tk.StringVar(value="请选择配置")
        self.state_var = tk.StringVar(value="未知")
        ttk.Label(head, textvariable=self.title_var, font=("TkDefaultFont", 14, "bold")).grid(row=0, column=0, sticky="w")
        ttk.Label(head, textvariable=self.state_var).grid(row=0, column=1, sticky="e")

        actions = ttk.Frame(right)
        actions.grid(row=1, column=0, sticky="ew", pady=(8, 8))
        self.start_button = ttk.Button(actions, text="启动", command=self.start_selected)
        self.stop_button = ttk.Button(actions, text="停止", command=self.stop_selected)
        self.restart_button = ttk.Button(actions, text="重启", command=self.restart_selected)
        self.doctor_button = ttk.Button(actions, text="Doctor 诊断", command=self.doctor_selected)
        self.action_buttons = [self.start_button, self.stop_button, self.restart_button, self.doctor_button]
        for b in self.action_buttons:
            b.pack(side="left", padx=(0, 6))
        self._set_actions(False)

        self.notebook = ttk.Notebook(right)
        self.notebook.grid(row=2, column=0, sticky="nsew")
        self.overview = ttk.Frame(self.notebook, padding=12)
        self.notebook.add(self.overview, text="概览")
        self.overview.columnconfigure(1, weight=1)
        self.field_vars: dict[str, tk.StringVar] = {}
        fields = [
            ("source", "来源"), ("profile", "Profile"), ("profile_path", "Profile 路径"),
            ("runtime", "Runtime Alias"), ("tunnel", "Tunnel ID"), ("mcp", "MCP Target"),
            ("process", "进程"), ("health", "健康"), ("ready", "Ready"), ("pid", "PID"),
            ("health_url", "Health URL"),
        ]
        for row_i, (key, label) in enumerate(fields):
            ttk.Label(self.overview, text=label).grid(row=row_i, column=0, sticky="nw", padx=(0, 12), pady=4)
            var = tk.StringVar(value="-")
            self.field_vars[key] = var
            ttk.Label(self.overview, textvariable=var, wraplength=650).grid(row=row_i, column=1, sticky="nw", pady=4)

        self.health_text = self._add_text_tab("健康状态 / JSON")
        self.diag_text = self._add_text_tab("诊断")
        self.log_text = self._add_log_tab()

        self.status_var = tk.StringVar(value="正在读取 tunnel-client…")
        ttk.Label(outer, textvariable=self.status_var, anchor="w").grid(row=2, column=0, columnspan=2, sticky="ew", pady=(7, 0))

    def _add_text_tab(self, name: str) -> tk.Text:
        frame = ttk.Frame(self.notebook, padding=8)
        self.notebook.add(frame, text=name)
        frame.rowconfigure(0, weight=1)
        frame.columnconfigure(0, weight=1)
        text = tk.Text(frame, wrap="word", state="disabled")
        text.grid(row=0, column=0, sticky="nsew")
        return text

    def _add_log_tab(self) -> tk.Text:
        frame = ttk.Frame(self.notebook, padding=8)
        self.notebook.add(frame, text="日志")
        frame.rowconfigure(2, weight=1)
        frame.columnconfigure(0, weight=1)
        controls = ttk.Frame(frame)
        controls.grid(row=0, column=0, sticky="ew", pady=(0, 6))
        ttk.Label(controls, text="搜索").pack(side="left")
        self.log_search_var = tk.StringVar()
        search = ttk.Entry(controls, textvariable=self.log_search_var, width=20)
        search.pack(side="left", padx=(5, 10))
        self.log_search_var.trace_add("write", lambda *_: self._render_log())
        ttk.Label(controls, text="级别").pack(side="left")
        self.log_level_var = tk.StringVar(value="全部")
        levels = ttk.Combobox(controls, textvariable=self.log_level_var, values=("全部", "DEBUG", "INFO", "WARN", "ERROR"), state="readonly", width=8)
        levels.pack(side="left", padx=(5, 10))
        levels.bind("<<ComboboxSelected>>", lambda _e: self._render_log())
        self.log_auto_refresh_var = tk.BooleanVar(value=True)
        ttk.Checkbutton(controls, text="自动刷新", variable=self.log_auto_refresh_var).pack(side="left", padx=(0, 8))
        ttk.Button(controls, text="立即刷新", command=self.refresh_log).pack(side="left", padx=(0, 6))
        ttk.Button(controls, text="复制可见日志", command=self.copy_visible_log).pack(side="right", padx=(6, 0))
        ttk.Button(controls, text="导出日志", command=self.export_log).pack(side="right")
        self.log_path_var = tk.StringVar(value="日志文件：-")
        ttk.Label(frame, textvariable=self.log_path_var, wraplength=760).grid(row=1, column=0, sticky="w", pady=(0, 6))
        text = tk.Text(frame, wrap="none", state="disabled")
        text.grid(row=2, column=0, sticky="nsew")
        self.log_frame = frame
        return text

    @staticmethod
    def _set_text(widget: tk.Text, value: str) -> None:
        widget.configure(state="normal")
        widget.delete("1.0", "end")
        widget.insert("1.0", value)
        widget.configure(state="disabled")

    def _set_raw_log(self, value: str) -> None:
        self._raw_log = value
        self._render_log()

    def _render_log(self) -> None:
        query = self.log_search_var.get().casefold().strip() if hasattr(self, "log_search_var") else ""
        level = self.log_level_var.get() if hasattr(self, "log_level_var") else "全部"
        lines: list[str] = []
        for line in self._raw_log.splitlines():
            if query and query not in line.casefold():
                continue
            if level != "全部" and level not in line.upper():
                continue
            lines.append(line)
        self._set_text(self.log_text, "\n".join(lines))
        try:
            self.log_text.see("end")
        except tk.TclError:
            pass

    def refresh_log(self) -> None:
        item = self.selected_item()
        if not item:
            self._set_raw_log("")
            self.log_path_var.set("日志文件：-")
            return
        key = self._item_key(item)
        status = self.statuses.get(key)
        path = (status.log_path if status else "") or self._log_paths.get(key, "")
        if not path:
            self.log_path_var.set("日志文件：当前没有可用日志")
            self._set_raw_log(self._log_cache.get(key, ""))
            return
        self._current_log_path = path
        self._log_paths[key] = path
        self.log_path_var.set(f"日志文件：{path}")
        identity = item.identity

        def done(text: str) -> None:
            current = self.selected_item()
            if current and current.identity == identity and self._current_log_path == path:
                self._log_cache[key] = text
                self._set_raw_log(text)

        def failed(message: str) -> None:
            current = self.selected_item()
            if current and current.identity == identity and self._current_log_path == path:
                self._set_raw_log(f"读取日志失败：{message}")

        self.async_bridge.submit(f"log:{identity}", lambda: self.client.read_log_tail(path), done, failed)

    def copy_visible_log(self) -> None:
        try:
            text = self.log_text.get("1.0", "end-1c")
            self.root.clipboard_clear()
            self.root.clipboard_append(text)
            self.status_var.set("已复制当前可见日志")
        except tk.TclError as exc:
            self._error(f"复制日志失败：{exc}")

    def export_log(self) -> None:
        item = self.selected_item()
        initial = f"{item.name if item else 'tunnel-client'}.log"
        path = filedialog.asksaveasfilename(parent=self.root, title="导出日志", initialfile=initial, defaultextension=".log", filetypes=[("日志", "*.log"), ("文本", "*.txt"), ("所有文件", "*.*")])
        if not path:
            return
        try:
            Path(path).write_text(self._raw_log, encoding="utf-8")
        except OSError as exc:
            self._error(f"导出日志失败：{exc}")

    def _schedule_refresh(self) -> None:
        if self._refresh_after:
            try:
                self.root.after_cancel(self._refresh_after)
            except tk.TclError:
                pass
        self._refresh_after = self.root.after(self.settings.refresh_interval_ms, self._periodic_refresh)

    def _periodic_refresh(self) -> None:
        self._refresh_after = None
        if self.binary_ok:
            self.refresh_inventory()
        self._schedule_refresh()

    def _schedule_log_refresh(self) -> None:
        if self._log_after:
            try:
                self.root.after_cancel(self._log_after)
            except tk.TclError:
                pass
        self._log_after = self.root.after(1000, self._periodic_log_refresh)

    def _periodic_log_refresh(self) -> None:
        self._log_after = None
        if self.binary_ok and self.log_auto_refresh_var.get():
            self.refresh_log()
        self._schedule_log_refresh()

    def selected_item(self) -> ManagedItem | None:
        selection = self.listbox.curselection()
        if not selection:
            return None
        index = int(selection[0])
        return self.items[index] if 0 <= index < len(self.items) else None

    # Compatibility for callers/tests from <=0.1.2.
    selected_config = selected_item

    def _item_key(self, item: ManagedItem) -> str:
        return item.identity

    def _preference_keys(self, item: ManagedItem) -> list[str]:
        keys = [self._item_key(item)]
        # A runtime can start referring to an existing named Profile after the
        # GUI has already stored local preferences for that Profile. Treat the
        # Profile preference as the runtime default without duplicating any
        # tunnel-client configuration. Runtime-specific preferences still win.
        for name in (item.profile_name, item.runtime_profile_name):
            if name:
                keys.append(f"profile:{name}")
        for name in (item.name, item.runtime_alias, item.profile_name, item.runtime_profile_name):
            if name:
                keys.append(name)
        return list(dict.fromkeys(keys))

    def _find_preference(self, item: ManagedItem) -> ProfilePreference | None:
        for key in self._preference_keys(item):
            pref = self.settings.profile_preferences.get(key)
            if pref is not None:
                return pref
        return None

    def _preference(self, item: ManagedItem) -> ProfilePreference:
        key = self._item_key(item)
        pref = self.settings.profile_preferences.get(key)
        if pref is not None:
            return pref
        inherited = self._find_preference(item)
        if inherited is not None:
            # Copy inherited defaults into a runtime-specific entry only when a
            # mutable preference is requested. This preserves a shared Profile
            # preference for other runtimes that may reference the same Profile.
            copied = ProfilePreference(
                auto_connect=inherited.auto_connect,
                auto_reconnect=inherited.auto_reconnect,
                enabled=inherited.enabled,
            )
            self.settings.profile_preferences[key] = copied
            self.store.save(self.settings)
            return copied
        return self.settings.preference(key)

    def _credential_ids(self, item: ManagedItem) -> list[str]:
        # Prefer a listed Profile name because it survives a later transition
        # from profile-only to runtime-backed display. Runtime-only aliases keep
        # their own credential namespace. Older alias-based entries remain
        # readable through the fallback candidates.
        ids: list[str] = []
        if item.profile_listed and item.profile_name:
            ids.append(item.profile_name)
        for value in (item.credential_id, item.runtime_alias, item.profile_name, item.runtime_profile_name, item.name):
            if value:
                ids.append(value)
        return list(dict.fromkeys(ids))

    def _credential_write_id(self, item: ManagedItem) -> str:
        return self._credential_ids(item)[0]

    def refresh_inventory(self, select_name: str | None = None, *, force: bool = False) -> None:
        if not self.binary_ok and not self._ensure_binary(show_warning=force):
            return
        selected = self.selected_item()
        previous = select_name or (selected.identity if selected else None)
        self.status_var.set("正在从 tunnel-client 读取 Profile 和 Runtime…")

        def done(items: list[ManagedItem]) -> None:
            self._apply_inventory(items, previous)
            self.status_var.set(f"已从 tunnel-client 读取 {len(items)} 个配置/运行实例")

        def failed(message: str) -> None:
            self.status_var.set("读取 tunnel-client 配置失败")
            if force:
                self._error(message)
            else:
                self._set_text(self.diag_text, "无法读取 tunnel-client Profile/Runtime：\n" + message)

        self.async_bridge.submit("inventory", self.client.inventory, done, failed)

    def _apply_inventory(self, items: list[ManagedItem], select_name: str | None = None) -> None:
        self.items = items
        live = {self._item_key(i) for i in items}
        self.statuses = {k: v for k, v in self.statuses.items() if k in live}
        self.listbox.delete(0, "end")
        selected_index: int | None = None
        fallback_matches: set[str] = set()
        if select_name and not select_name.startswith(("profile:", "runtime:", "item:")):
            for item in items:
                if select_name in {item.name, item.profile_name, item.runtime_alias}:
                    fallback_matches.add(item.identity)

        for idx, item in enumerate(items):
            status = self.statuses.get(self._item_key(item))
            state = status.state if status else (RuntimeState.UNKNOWN if item.has_runtime_alias else RuntimeState.CONFIGURED)
            self.listbox.insert("end", self._list_label(item, state))
            if select_name == item.identity or (len(fallback_matches) == 1 and item.identity in fallback_matches):
                selected_index = idx
        if items:
            idx = selected_index if selected_index is not None else 0
            self.listbox.selection_set(idx)
            self.listbox.activate(idx)
            self._selection_changed()
        else:
            self.title_var.set("没有配置")
            self.state_var.set("-")
            self._clear_overview()
            self._set_actions(False)
            self._set_text(self.diag_text, "tunnel-client 当前没有 Profile 或 Runtime alias。\n\n可点击“新建 Profile”，或在命令行用 tunnel-client init / runtimes connect 创建。")
        if not self._initial_auto_connect_done:
            self._initial_auto_connect_done = True
            self._after(100, self._auto_connect)

    def _list_label(self, item: ManagedItem, state: RuntimeState) -> str:
        return f"{item.name}   [{_source_text(item)}]   {_runtime_state_text(state)}"

    def _update_list_label(self, item: ManagedItem, status: RuntimeStatus) -> None:
        for idx, candidate in enumerate(self.items):
            if candidate.identity == item.identity:
                self.listbox.delete(idx)
                self.listbox.insert(idx, self._list_label(item, status.state))
                self.listbox.selection_set(idx)
                break

    def _selection_changed(self) -> None:
        item = self.selected_item()
        if not item:
            self._set_actions(False)
            return
        key = self._item_key(item)
        self._current_log_path = self._log_paths.get(key, "")
        self._set_raw_log(self._log_cache.get(key, ""))
        self.log_path_var.set(f"日志文件：{self._current_log_path}" if self._current_log_path else "日志文件：-")
        self.title_var.set(item.name)
        cached = self.statuses.get(key)
        if cached:
            self._render_status(item, cached)
        else:
            configured = RuntimeStatus(alias=item.name, state=RuntimeState.UNKNOWN if item.has_runtime_alias else RuntimeState.CONFIGURED)
            self._render_status(item, configured)
        self._update_action_states(item, cached)
        self.refresh_selected()

    def _set_actions(self, enabled: bool) -> None:
        item = self.selected_item() if enabled else None
        status = self.statuses.get(self._item_key(item)) if item else None
        self._update_action_states(item, status, binary_available=enabled)

    def _update_action_states(
        self,
        item: ManagedItem | None = None,
        status: RuntimeStatus | None = None,
        *,
        binary_available: bool | None = None,
    ) -> None:
        available = self.binary_ok if binary_available is None else binary_available
        profile_state = "normal" if available else "disabled"
        for button in self.profile_buttons:
            button.configure(state=profile_state)

        if not available or item is None:
            for button in self.action_buttons:
                button.configure(state="disabled")
            return

        key = self._item_key(item)
        current = status or self.statuses.get(key)
        state = current.state if current else (RuntimeState.UNKNOWN if item.has_runtime_alias else RuntimeState.CONFIGURED)
        running = bool(current and current.process_running) or state in {RuntimeState.RUNNING, RuntimeState.READY}
        starting = state is RuntimeState.STARTING
        busy = key in self._operation_busy

        can_start = bool(item.has_runtime_alias or item.profile_name)
        self.start_button.configure(state="normal" if can_start and not running and not starting and not busy else "disabled")
        self.stop_button.configure(state="normal" if running and not busy else "disabled")
        self.restart_button.configure(state="normal" if running and not busy else "disabled")
        can_doctor = bool(item.profile_name or item.profile_path or item.runtime_profile_path)
        self.doctor_button.configure(state="normal" if can_doctor and not busy else "disabled")

    def _set_operation_busy(self, item: ManagedItem, busy: bool) -> None:
        key = self._item_key(item)
        if busy:
            self._operation_busy.add(key)
        else:
            self._operation_busy.discard(key)
        current = self.selected_item()
        if current and current.identity == item.identity:
            self._update_action_states(current, self.statuses.get(key))

    def _ensure_binary(self, *, show_warning: bool = False) -> bool:
        self.client.binary_path = self.settings.binary_path
        try:
            path = self.client.resolve_binary()
        except Exception as exc:
            self.binary_ok = False
            self._set_actions(False)
            self.status_var.set("未找到 tunnel-client.exe，请打开设置选择正确文件")
            self._set_text(self.diag_text, f"tunnel-client 不可用：\n{exc}")
            if show_warning:
                messagebox.showwarning("找不到 tunnel-client.exe", f"无法执行操作：\n\n{exc}\n\n请打开“设置”选择正确的 tunnel-client.exe。", parent=self.root)
            return False
        self.binary_ok = True
        self.status_var.set(f"tunnel-client：{path}")
        if self.selected_item():
            self._set_actions(True)
        else:
            for button in self.profile_buttons:
                button.configure(state="normal")
        return True

    def _clear_overview(self) -> None:
        for var in self.field_vars.values():
            var.set("-")
        self._set_text(self.health_text, "")
        self._current_log_path = ""
        self.log_path_var.set("日志文件：-")
        self._set_raw_log("")

    def _render_status(self, item: ManagedItem, status: RuntimeStatus, health_snapshot: dict[str, Any] | None = None) -> None:
        self.state_var.set(_runtime_state_text(status.state))
        raw_process = status.raw.get("process", {}) if isinstance(status.raw, dict) else {}
        target = ""
        if isinstance(raw_process, dict):
            target = str(raw_process.get("target_value", "")).strip()
        self.field_vars["source"].set(_source_text(item))
        self.field_vars["profile"].set(item.profile_name or item.runtime_profile_name or "-")
        self.field_vars["profile_path"].set(status.profile_path or item.profile_path or item.runtime_profile_path or "-")
        self.field_vars["runtime"].set(item.runtime_alias or "-")
        self.field_vars["tunnel"].set(status.tunnel_id or item.tunnel_id or "-")
        self.field_vars["mcp"].set(target or "-")
        self.field_vars["process"].set("运行中" if status.process_running else ("未运行" if status.state is not RuntimeState.UNKNOWN else "未知"))
        self.field_vars["health"].set("正常" if status.healthy else ("异常/未知" if status.process_running else "-"))
        self.field_vars["ready"].set("已就绪" if status.ready else ("未就绪" if status.process_running else "-"))
        self.field_vars["pid"].set(str(status.pid) if status.pid is not None else "-")
        self.field_vars["health_url"].set(status.health_url or "-")
        if health_snapshot is not None:
            import json
            self._set_text(self.health_text, json.dumps(health_snapshot, ensure_ascii=False, indent=2))
        elif not status.health_url:
            self._set_text(self.health_text, "当前没有可用的本地 Health URL。")
        key = self._item_key(item)
        if status.log_path:
            self._log_paths[key] = status.log_path
        self._current_log_path = self._log_paths.get(key, "")
        self.log_path_var.set(f"日志文件：{self._current_log_path}" if self._current_log_path else "日志文件：-")
        if self._current_log_path and self.log_auto_refresh_var.get():
            self.refresh_log()
        elif key in self._log_cache:
            self._set_raw_log(self._log_cache[key])
        if status.error:
            self.status_var.set(f"{item.name}：{status.error}")
        self._update_list_label(item, status)
        current = self.selected_item()
        if current and current.identity == item.identity:
            self._update_action_states(current, status)

    def _saved_secret(self, item: ManagedItem) -> str | None:
        if not self.credentials:
            return None
        try:
            for credential_id in self._credential_ids(item):
                secret = self.credentials.get(credential_id)
                if secret:
                    return secret
            return None
        except Exception as exc:
            raise RuntimeError(f"读取 Windows 凭据失败：{exc}") from exc

    def refresh_selected(self) -> None:
        item = self.selected_item()
        if item:
            self._refresh_item(item)

    def _refresh_item(self, item: ManagedItem) -> None:
        key = f"status:{self._item_key(item)}"
        def work() -> tuple[RuntimeStatus, dict[str, Any]]:
            status = self.client.status_item(item)
            snapshot = self.client.health_snapshot(status) if status.health_url else {}
            return status, snapshot
        def done(value: tuple[RuntimeStatus, dict[str, Any]]) -> None:
            status, snapshot = value
            self.statuses[self._item_key(item)] = status
            current = self.selected_item()
            if current and current.identity == item.identity:
                self._render_status(current, status, snapshot)
            self._maybe_auto_reconnect(item, status)
        self.async_bridge.submit(key, work, done, lambda msg: self._status_error(item, msg))

    def _status_error(self, item: ManagedItem, message: str) -> None:
        status = RuntimeStatus(alias=item.name, state=RuntimeState.ERROR, error=message)
        self.statuses[self._item_key(item)] = status
        current = self.selected_item()
        if current and current.identity == item.identity:
            self._render_status(current, status)

    def start_selected(self) -> None:
        item = self.selected_item()
        if item:
            self._start_item(item, manual=True)

    def _start_item(self, item: ManagedItem, *, manual: bool = False) -> None:
        if not self._ensure_binary(show_warning=manual):
            return
        pref = self._preference(item)
        if not pref.enabled:
            if manual:
                self._error("此配置已在“本机偏好 / 密钥”中禁用")
            return
        self.manual_stopped.discard(self._item_key(item))
        try:
            secret = self._saved_secret(item)
        except Exception as exc:
            if manual:
                self._error(str(exc))
            return
        self._set_operation_busy(item, True)
        if manual:
            self.notebook.select(self.log_frame)
            if not self._raw_log:
                self._set_raw_log("正在等待 tunnel-client 运行日志…")
        self.status_var.set(f"正在启动 {item.name}…")
        starting = RuntimeStatus(alias=item.name, state=RuntimeState.STARTING)
        self.statuses[self._item_key(item)] = starting
        self._render_status(item, starting)

        def work() -> Any:
            if item.has_runtime_alias:
                status = self.client.status(item.runtime_alias)
                if status.process_running and status.ready:
                    return status
                return self.client.connect_runtime(item, secret)
            return self.client.start_profile(item, secret)
        def done(_value: Any) -> None:
            self._set_operation_busy(item, False)
            self.reconnect_attempts.pop(self._item_key(item), None)
            self.status_var.set(f"{item.name} 启动命令已完成，正在核验状态…")
            self._after(250, lambda: self._refresh_item(item))
        def failed(message: str) -> None:
            self._set_operation_busy(item, False)
            self._status_error(item, message)
            if manual:
                self._error(message)
        if not self.async_bridge.submit(f"start:{self._item_key(item)}", work, done, failed):
            self._set_operation_busy(item, False)

    def stop_selected(self) -> None:
        item = self.selected_item()
        if not item or not self._ensure_binary(show_warning=True):
            return
        self.manual_stopped.add(self._item_key(item))
        self.reconnect_attempts.pop(self._item_key(item), None)
        self._set_operation_busy(item, True)
        def work() -> None:
            if item.has_runtime_alias:
                status = self.client.status(item.runtime_alias)
                if status.process_running:
                    self.client.stop(item.runtime_alias)
            elif item.profile_name:
                self.client.stop_profile(item.profile_name)
        def done(_value: Any) -> None:
            self._set_operation_busy(item, False)
            previous = self.statuses.get(self._item_key(item))
            stopped = RuntimeStatus(
                alias=item.name,
                state=RuntimeState.STOPPED,
                process_running=False,
                tunnel_id=previous.tunnel_id if previous else item.tunnel_id,
                profile_path=previous.profile_path if previous else item.profile_path,
                log_path=(previous.log_path if previous else self._log_paths.get(self._item_key(item), "")),
            )
            self.statuses[self._item_key(item)] = stopped
            current = self.selected_item()
            if current and current.identity == item.identity:
                self._render_status(current, stopped)
            self.status_var.set(f"{item.name} 已停止")
            self._after(150, lambda: self._refresh_item(item))
        def failed(message: str) -> None:
            self._set_operation_busy(item, False)
            self._error(message)
        if not self.async_bridge.submit(f"stop:{self._item_key(item)}", work, done, failed):
            self._set_operation_busy(item, False)

    def restart_selected(self) -> None:
        item = self.selected_item()
        if not item or not self._ensure_binary(show_warning=True):
            return
        self.manual_stopped.discard(self._item_key(item))
        try:
            secret = self._saved_secret(item)
        except Exception as exc:
            self._error(str(exc)); return
        self._set_operation_busy(item, True)
        def work() -> Any:
            if item.has_runtime_alias:
                status = self.client.status(item.runtime_alias)
                if status.process_running:
                    self.client.stop(item.runtime_alias)
                elif status.state is RuntimeState.ERROR:
                    raise RuntimeError(f"无法确认当前 Runtime 状态，取消重启：{status.error or '未知错误'}")
                return self.client.connect_runtime(item, secret)
            if item.profile_name:
                self.client.stop_profile(item.profile_name)
                return self.client.start_profile(item, secret)
            raise RuntimeError("该项目没有可启动的 Profile/Runtime")
        def done(_value: Any) -> None:
            self._set_operation_busy(item, False)
            starting = RuntimeStatus(alias=item.name, state=RuntimeState.STARTING)
            self.statuses[self._item_key(item)] = starting
            current = self.selected_item()
            if current and current.identity == item.identity:
                self._render_status(current, starting)
            self._after(200, lambda: self._refresh_item(item))
        def failed(message: str) -> None:
            self._set_operation_busy(item, False)
            self._error(message)
        if not self.async_bridge.submit(f"restart:{self._item_key(item)}", work, done, failed):
            self._set_operation_busy(item, False)

    def doctor_selected(self) -> None:
        item = self.selected_item()
        if not item or not self._ensure_binary(show_warning=True):
            return
        try:
            secret = self._saved_secret(item)
        except Exception as exc:
            self._error(str(exc)); return
        self.notebook.select(2)
        self._set_text(self.diag_text, "正在运行 tunnel-client doctor --explain …")
        self.async_bridge.submit(
            f"doctor:{self._item_key(item)}",
            lambda: self.client.doctor_item(item, secret),
            lambda text: self._set_text(self.diag_text, text),
            lambda msg: self._set_text(self.diag_text, "诊断失败：\n" + msg),
        )

    def _wait_dialog(self, dialog: tk.Toplevel) -> None:
        self.root.wait_window(dialog)

    def add_profile(self) -> None:
        if not self._ensure_binary(show_warning=True):
            return
        dlg = ProfileEditor(self.root)
        self._wait_dialog(dlg)
        if not dlg.result:
            return
        spec, secret, pref = dlg.result
        def work() -> str:
            return self.client.create_profile(spec)
        def done(_text: str) -> None:
            self.settings.profile_preferences[f"profile:{spec.name}"] = pref
            if secret:
                if not self.credentials:
                    self._error("Profile 已创建，但当前无法使用 Windows 凭据管理器保存密钥。")
                else:
                    try:
                        self.credentials.set(spec.name, secret)
                    except Exception as exc:
                        self._error(f"Profile 已创建，但保存密钥失败：{exc}")
            self.store.save(self.settings)
            self.refresh_inventory(f"profile:{spec.name}", force=True)
        self.async_bridge.submit(f"create-profile:{spec.name}", work, done, self._error)

    # Compatibility alias
    add_tunnel = add_profile

    def edit_profile(self) -> None:
        item = self.selected_item()
        if not item or not item.profile_name:
            self._error("当前项目没有可编辑的 tunnel-client Profile")
            return
        if not item.profile_listed:
            self._error("此 Runtime 使用的是内部运行 Profile，不在 profiles list 中；当前不能直接编辑。")
            return
        if not self._ensure_binary(show_warning=True):
            return
        name = item.profile_name
        path = item.profile_path
        identity = item.identity
        try:
            has_secret = bool(self._saved_secret(item))
        except Exception:
            has_secret = False
        pref = self._preference(item)
        self.status_var.set(f"正在读取 Profile：{name}")

        def loaded(text: str) -> None:
            metadata = self.client.profile_metadata_from_text(text)
            dlg = ExistingProfileEditor(self.root, name, path, text, metadata, pref, has_secret)
            self._wait_dialog(dlg)
            if not dlg.result:
                self.status_var.set("已取消编辑")
                return
            raw, updated_pref, secret, delete_secret = dlg.result
            self.status_var.set(f"正在通过 tunnel-client 校验并保存：{name}")

            def done(_value: str) -> None:
                self.settings.profile_preferences[self._item_key(item)] = updated_pref
                credential_error = ""
                try:
                    if self.credentials:
                        if delete_secret:
                            for credential_id in self._credential_ids(item):
                                self.credentials.delete(credential_id)
                        elif secret:
                            self.credentials.set(self._credential_write_id(item), secret)
                    elif secret:
                        raise RuntimeError("Windows 凭据管理器不可用")
                except Exception as exc:
                    credential_error = str(exc)
                self.store.save(self.settings)
                self.refresh_inventory(identity, force=True)
                if credential_error:
                    self._error(f"Profile 已保存，但本机密钥保存失败：{credential_error}")
                else:
                    self.status_var.set(f"Profile 已保存：{name}")

            self.async_bridge.submit(
                f"save-profile:{name}",
                lambda: self.client.save_profile_text(name, path, raw),
                done,
                self._error,
            )

        self.async_bridge.submit(
            f"load-profile:{name}",
            lambda: self.client.read_profile_text(name, path),
            loaded,
            self._error,
        )

    edit_tunnel = edit_profile

    def edit_preferences(self) -> None:
        item = self.selected_item()
        if not item:
            return
        try:
            has_secret = bool(self._saved_secret(item))
        except Exception:
            has_secret = False
        pref = self._preference(item)
        dlg = PreferenceDialog(self.root, item.name, pref, has_secret)
        self._wait_dialog(dlg)
        if not dlg.result:
            return
        updated, secret, delete_secret = dlg.result
        self.settings.profile_preferences[self._item_key(item)] = updated
        try:
            if self.credentials:
                if delete_secret:
                    # Remove every compatible key for this selected entity so an
                    # older alias/profile fallback cannot silently resurrect it.
                    for credential_id in self._credential_ids(item):
                        self.credentials.delete(credential_id)
                elif secret:
                    self.credentials.set(self._credential_write_id(item), secret)
            elif secret:
                raise RuntimeError("Windows 凭据管理器不可用")
        except Exception as exc:
            self._error(str(exc)); return
        self.store.save(self.settings)
        self.status_var.set(f"{item.name} 的本机偏好已保存")

    def delete_selected(self) -> None:
        item = self.selected_item()
        if not item:
            return
        if not self._ensure_binary(show_warning=True):
            return
        profile_note = ""
        if item.profile_listed:
            profile_note = "\n- 删除 tunnel-client profiles list 中的 Profile 文件（若未被其他 Runtime 共用）"
        if not messagebox.askyesno(
            "删除 tunnel-client 配置",
            f"确定删除 {item.name}？\n\n将执行：\n- 停止并移除对应 Runtime alias（如果有）{profile_note}\n- 删除本程序保存的本机偏好和 Runtime API Key\n\n不会删除 OpenAI 平台上的远程 Tunnel。",
            parent=self.root,
        ):
            return

        def work() -> bool:
            profile_deleted = False
            fresh = self.client.inventory()
            current = next((x for x in fresh if x.identity == item.identity), None)
            if current is None and item.has_runtime_alias:
                raise RuntimeError("列表已发生变化，请刷新后重试")
            target = current or item
            shared_profile = bool(target.profile_name and any(
                x.identity != target.identity and x.profile_name == target.profile_name for x in fresh
            ))
            if target.has_runtime_alias:
                status = self.client.status(target.runtime_alias)
                if status.process_running:
                    self.client.stop(target.runtime_alias)
                try:
                    self.client.remove(target.runtime_alias)
                except Exception:
                    # Re-check: if alias disappeared concurrently, cleanup can continue.
                    after = self.client.inventory()
                    if any(x.runtime_alias == target.runtime_alias for x in after):
                        raise
            if target.profile_listed and target.profile_name and target.profile_path and not shared_profile:
                self.client.delete_profile(target.profile_name, target.profile_path)
                profile_deleted = True
            return profile_deleted

        def done(profile_deleted: bool) -> None:
            try:
                if self.credentials:
                    if profile_deleted:
                        credential_ids = self._credential_ids(item)
                    else:
                        # Removing only a Runtime alias must not delete a shared
                        # Profile credential that another Runtime can still use.
                        credential_ids = [item.runtime_alias] if item.runtime_alias else [self._credential_write_id(item)]
                    for credential_id in credential_ids:
                        if credential_id:
                            self.credentials.delete(credential_id)
            except Exception as exc:
                self._error(f"tunnel-client 配置已清理，但删除 Windows 凭据失败：{exc}")
            self.settings.profile_preferences.pop(self._item_key(item), None)
            self.settings.profile_preferences.pop(item.name, None)
            if profile_deleted and item.profile_name:
                self.settings.profile_preferences.pop(f"profile:{item.profile_name}", None)
                self.settings.profile_preferences.pop(item.profile_name, None)
            self.statuses.pop(self._item_key(item), None)
            self.store.save(self.settings)
            self.refresh_inventory(force=True)

        self.async_bridge.submit(f"delete:{self._item_key(item)}", work, done, self._error)

    delete_tunnel = delete_selected

    def import_profile(self) -> None:
        if not self._ensure_binary(show_warning=True):
            return
        path = filedialog.askopenfilename(parent=self.root, title="导入 tunnel-client Profile", filetypes=[("YAML", "*.yaml *.yml"), ("所有文件", "*.*")])
        if not path:
            return
        default_name = Path(path).stem
        name = simpledialog.askstring("Profile 名称", "导入为哪个 Profile 名称？", initialvalue=default_name, parent=self.root)
        if not name:
            return
        self.async_bridge.submit(
            f"import:{name}",
            lambda: self.client.import_profile(name, path),
            lambda _v: self.refresh_inventory(f"profile:{name}", force=True),
            lambda msg: self._error(f"导入失败：{msg}"),
        )

    import_config = import_profile

    def export_selected(self) -> None:
        item = self.selected_item()
        if not item:
            return
        source = item.profile_path or item.runtime_profile_path
        if not source or not Path(source).is_file():
            self._error("当前项目没有可导出的官方 Profile 文件")
            return
        initial = f"{item.profile_name or item.name}.yaml"
        path = filedialog.asksaveasfilename(parent=self.root, title="导出 tunnel-client Profile", initialfile=initial, defaultextension=".yaml", filetypes=[("YAML", "*.yaml"), ("所有文件", "*.*")])
        if not path:
            return
        try:
            shutil.copyfile(source, path)
            self.status_var.set(f"已导出官方 Profile：{path}")
        except OSError as exc:
            self._error(f"导出失败：{exc}")

    def open_settings(self) -> None:
        dlg = SettingsDialog(self.root, self.settings)
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
        self.async_bridge.submit("capabilities", self.client.capabilities, self._show_capabilities, self._error)
        self.refresh_inventory(force=True)

    def _show_capabilities(self, caps: dict[str, Any]) -> None:
        lines = [
            f"版本：{caps.get('version') or '未知'}",
            f"Runtime 管理：{'支持' if caps.get('runtimes') else '不支持'}",
            f"Profile 管理：{'支持' if caps.get('profiles') else '不支持'}",
            f"Doctor：{'支持' if caps.get('doctor') else '不支持'}",
        ]
        self._set_text(self.diag_text, "\n".join(lines))

    def _auto_connect(self) -> None:
        for item in list(self.items):
            pref = self._find_preference(item)
            if pref and pref.enabled and pref.auto_connect and self._item_key(item) not in self.manual_stopped:
                self._start_item(item, manual=False)

    def _maybe_auto_reconnect(self, item: ManagedItem, status: RuntimeStatus) -> None:
        pref = self._find_preference(item)
        if not pref or not pref.enabled or not pref.auto_reconnect or self._item_key(item) in self.manual_stopped:
            return
        if status.process_running or status.state in {RuntimeState.CONFIGURED, RuntimeState.STARTING, RuntimeState.UNKNOWN}:
            return
        attempts = self.reconnect_attempts.get(self._item_key(item), 0)
        delays = [1000, 2000, 5000, 10000, 30000, 60000]
        if attempts >= len(delays):
            self.status_var.set(f"{item.name} 自动重连已暂停，请运行 Doctor 检查")
            return
        self.reconnect_attempts[self._item_key(item)] = attempts + 1
        self.status_var.set(f"{item.name} 将在 {delays[attempts] // 1000} 秒后自动重连")
        self._after(delays[attempts], lambda: self._start_item(item, manual=False))

    def _error(self, message: str) -> None:
        self.status_var.set(message)
        messagebox.showerror("错误", message, parent=self.root)

    def _on_close(self) -> None:
        if self.settings.close_to_tray:
            # There is deliberately no tray dependency in the verified standard-
            # library build. Keep the window reachable from the Windows taskbar.
            self.root.iconify()
        else:
            self.quit()

    def quit(self) -> None:
        if self._quitting:
            return
        self._quitting = True
        if self._refresh_after:
            try:
                self.root.after_cancel(self._refresh_after)
            except tk.TclError:
                pass
            self._refresh_after = None
        if self._log_after:
            try:
                self.root.after_cancel(self._log_after)
            except tk.TclError:
                pass
            self._log_after = None
        for ident in list(self._after_ids):
            try:
                self.root.after_cancel(ident)
            except tk.TclError:
                pass
        self._after_ids.clear()
        try:
            self.client.shutdown_profile_processes()
        except Exception:
            pass
        self.async_bridge.close()
        try:
            self.root.destroy()
        except tk.TclError:
            pass
