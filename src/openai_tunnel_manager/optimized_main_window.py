from __future__ import annotations

import os
import subprocess
import tkinter as tk
from pathlib import Path
from tkinter import ttk

from .main_window import MainWindow


class OptimizedMainWindow(MainWindow):
    """MainWindow variant with a responsive, scrollable log viewer."""

    _LOG_FILTER_DEBOUNCE_MS = 140

    def __init__(self, *args, **kwargs) -> None:
        self._log_render_after: str | None = None
        self._last_rendered_text: str | None = None
        self._last_render_raw: str | None = None
        self._last_render_query = ""
        self._last_render_level = "全部"
        super().__init__(*args, **kwargs)

    def _add_log_tab(self) -> tk.Text:
        frame = ttk.Frame(self.notebook, padding=8)
        self.notebook.add(frame, text="日志")
        frame.rowconfigure(2, weight=1)
        frame.columnconfigure(0, weight=1)

        controls = ttk.Frame(frame)
        controls.grid(row=0, column=0, columnspan=2, sticky="ew", pady=(0, 6))
        ttk.Label(controls, text="搜索").pack(side="left")
        self.log_search_var = tk.StringVar()
        search = ttk.Entry(controls, textvariable=self.log_search_var, width=20)
        search.pack(side="left", padx=(5, 10))
        self.log_search_var.trace_add("write", lambda *_: self._schedule_log_render())

        ttk.Label(controls, text="级别").pack(side="left")
        self.log_level_var = tk.StringVar(value="全部")
        levels = ttk.Combobox(
            controls,
            textvariable=self.log_level_var,
            values=("全部", "DEBUG", "INFO", "WARN", "ERROR"),
            state="readonly",
            width=8,
        )
        levels.pack(side="left", padx=(5, 10))
        levels.bind("<<ComboboxSelected>>", lambda _e: self._schedule_log_render())

        self.log_auto_refresh_var = tk.BooleanVar(value=True)
        ttk.Checkbutton(controls, text="自动刷新", variable=self.log_auto_refresh_var).pack(side="left", padx=(0, 8))

        self.log_wrap_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(
            controls,
            text="自动换行",
            variable=self.log_wrap_var,
            command=self._toggle_log_wrap,
        ).pack(side="left", padx=(0, 8))

        ttk.Button(controls, text="立即刷新", command=self.refresh_log).pack(side="left", padx=(0, 6))
        self.open_log_location_button = ttk.Button(
            controls,
            text="打开日志位置",
            command=self.open_log_location,
        )
        self.open_log_location_button.pack(side="right")

        self.log_path_var = tk.StringVar(value="日志文件：-")
        ttk.Label(frame, textvariable=self.log_path_var, wraplength=760).grid(
            row=1,
            column=0,
            columnspan=2,
            sticky="ew",
            pady=(0, 6),
        )

        text = tk.Text(frame, wrap="none", state="disabled", exportselection=False)
        y_scroll = ttk.Scrollbar(frame, orient="vertical", command=text.yview)
        x_scroll = ttk.Scrollbar(frame, orient="horizontal", command=text.xview)
        text.configure(yscrollcommand=y_scroll.set, xscrollcommand=x_scroll.set)

        text.grid(row=2, column=0, sticky="nsew")
        y_scroll.grid(row=2, column=1, sticky="ns")
        x_scroll.grid(row=3, column=0, sticky="ew")

        self.log_vertical_scrollbar = y_scroll
        self.log_horizontal_scrollbar = x_scroll
        self.log_frame = frame
        return text

    def _open_file_location(self, path: str, label: str) -> None:
        raw = str(path or "").strip()
        if not raw:
            self._error(f"当前没有可用的{label}")
            return
        target = Path(raw).expanduser()
        try:
            target = target.resolve(strict=False)
        except OSError:
            pass
        if not target.exists():
            self._error(f"{label}不存在：{target}")
            return
        if os.name != "nt":
            self._error("打开文件所在位置目前仅支持 Windows")
            return
        try:
            if target.is_dir():
                os.startfile(str(target))
            else:
                subprocess.Popen(["explorer.exe", f"/select,{target}"])
        except OSError as exc:
            self._error(f"打开{label}位置失败：{exc}")

    def open_log_location(self) -> None:
        self._open_file_location(self._current_log_path, "日志文件")

    def _schedule_log_render(self) -> None:
        if self._log_render_after:
            try:
                self.root.after_cancel(self._log_render_after)
            except tk.TclError:
                pass
            self._log_render_after = None

        def render() -> None:
            self._log_render_after = None
            self._render_log(force=True)

        try:
            self._log_render_after = self.root.after(self._LOG_FILTER_DEBOUNCE_MS, render)
        except tk.TclError:
            self._log_render_after = None

    def _toggle_log_wrap(self) -> None:
        try:
            self.log_text.configure(wrap="word" if self.log_wrap_var.get() else "none")
        except tk.TclError:
            return

    def _set_raw_log(self, value: str) -> None:
        old_value = self._raw_log
        if value == old_value:
            return

        self._raw_log = value
        query = self.log_search_var.get().casefold().strip() if hasattr(self, "log_search_var") else ""
        level = self.log_level_var.get() if hasattr(self, "log_level_var") else "全部"

        if self._can_append_log_incrementally(old_value, value, query, level):
            self._append_log_delta(value[len(old_value) :], value)
            return

        self._render_log(force=True)

    def _can_append_log_incrementally(self, old_value: str, value: str, query: str, level: str) -> bool:
        return (
            bool(old_value)
            and not query
            and level == "全部"
            and value.startswith(old_value)
            and self._last_rendered_text == old_value
            and self._last_render_raw == old_value
            and self._last_render_query == ""
            and self._last_render_level == "全部"
        )

    def _horizontal_log_position(self) -> float:
        try:
            start, _end = self.log_text.xview()
        except (tk.TclError, ValueError):
            return 0.0
        return start

    def _restore_horizontal_log_position(self, start: float) -> None:
        try:
            self.log_text.xview_moveto(max(0.0, min(1.0, start)))
        except tk.TclError:
            pass

    def _append_log_delta(self, delta: str, full_value: str) -> None:
        if not delta:
            return
        follow_tail = self._is_log_at_bottom()
        horizontal = self._horizontal_log_position()
        try:
            self.log_text.configure(state="normal")
            self.log_text.insert("end-1c", delta)
            self.log_text.configure(state="disabled")
            if follow_tail:
                self.log_text.yview_moveto(1.0)
            self._restore_horizontal_log_position(horizontal)
        except tk.TclError:
            return

        self._last_rendered_text = full_value
        self._last_render_raw = full_value
        self._last_render_query = ""
        self._last_render_level = "全部"

    def _render_log(self, force: bool = False) -> None:
        query = self.log_search_var.get().casefold().strip() if hasattr(self, "log_search_var") else ""
        level = self.log_level_var.get() if hasattr(self, "log_level_var") else "全部"

        if (
            not force
            and self._last_render_raw == self._raw_log
            and self._last_render_query == query
            and self._last_render_level == level
        ):
            return

        if not query and level == "全部":
            rendered = self._raw_log
        else:
            lines: list[str] = []
            for line in self._raw_log.splitlines():
                if query and query not in line.casefold():
                    continue
                if level != "全部" and level not in line.upper():
                    continue
                lines.append(line)
            rendered = "\n".join(lines)

        self._last_render_raw = self._raw_log
        self._last_render_query = query
        self._last_render_level = level

        if rendered == self._last_rendered_text:
            return

        follow_tail = self._is_log_at_bottom()
        horizontal = self._horizontal_log_position()
        try:
            self.log_text.configure(state="normal")
            self.log_text.replace("1.0", "end", rendered)
            self.log_text.configure(state="disabled")
            if follow_tail or self._last_rendered_text is None:
                self.log_text.yview_moveto(1.0)
            self._restore_horizontal_log_position(horizontal)
        except tk.TclError:
            return

        self._last_rendered_text = rendered

    def _is_log_at_bottom(self) -> bool:
        try:
            _start, end = self.log_text.yview()
        except (tk.TclError, ValueError):
            return True
        return end >= 0.995
