from __future__ import annotations

import json
import os
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Any


class CommandError(RuntimeError):
    def __init__(self, message: str, *, returncode: int | None = None, stdout: str = "", stderr: str = "") -> None:
        super().__init__(message)
        self.returncode = returncode
        self.stdout = stdout
        self.stderr = stderr


@dataclass(slots=True)
class CommandResult:
    args: list[str]
    returncode: int
    stdout: str
    stderr: str

    def json(self) -> dict[str, Any]:
        text = self.stdout.strip()
        try:
            value = json.loads(text)
        except json.JSONDecodeError as exc:
            raise CommandError(
                "tunnel-client 返回的内容不是有效 JSON",
                returncode=self.returncode,
                stdout=self.stdout,
                stderr=self.stderr,
            ) from exc
        if not isinstance(value, dict):
            raise CommandError("tunnel-client JSON 根节点不是对象", stdout=self.stdout, stderr=self.stderr)
        return value


class CommandRunner:
    def run(
        self,
        executable: str,
        args: list[str],
        *,
        env: dict[str, str] | None = None,
        timeout: float = 45.0,
        check: bool = True,
    ) -> CommandResult:
        cmd = [executable, *args]
        full_env = os.environ.copy()
        if env:
            full_env.update(env)
        creationflags = 0
        startupinfo = None
        if os.name == "nt":
            creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
            startupinfo = subprocess.STARTUPINFO()
            startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        try:
            cp = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                env=full_env,
                timeout=timeout,
                creationflags=creationflags,
                startupinfo=startupinfo,
            )
        except FileNotFoundError as exc:
            raise CommandError(f"找不到 tunnel-client: {executable}") from exc
        except subprocess.TimeoutExpired as exc:
            raise CommandError(
                f"命令执行超时（{timeout:g} 秒）",
                stdout=exc.stdout or "",
                stderr=exc.stderr or "",
            ) from exc
        result = CommandResult(cmd, cp.returncode, cp.stdout, cp.stderr)
        if check and cp.returncode != 0:
            detail = (cp.stderr or cp.stdout).strip()
            raise CommandError(
                detail or f"tunnel-client 退出码 {cp.returncode}",
                returncode=cp.returncode,
                stdout=cp.stdout,
                stderr=cp.stderr,
            )
        return result
