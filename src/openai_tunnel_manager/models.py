from __future__ import annotations

from dataclasses import asdict, dataclass, field
from enum import Enum
import re
from urllib.parse import urlsplit
from typing import Any


class McpType(str, Enum):
    HTTP = "http"
    STDIO = "stdio"


class RuntimeState(str, Enum):
    CONFIGURED = "Configured"
    STOPPED = "Stopped"
    STARTING = "Starting"
    RUNNING = "Running"
    READY = "Ready"
    ERROR = "Error"
    STALE = "Stale"
    UNKNOWN = "Unknown"


@dataclass(slots=True)
class ProfileSpec:
    """Ephemeral input used only to ask tunnel-client to create a real profile."""

    name: str
    tunnel_id: str
    mcp_type: McpType = McpType.HTTP
    mcp_target: str = ""

    def __post_init__(self) -> None:
        if isinstance(self.mcp_type, str):
            self.mcp_type = McpType(self.mcp_type)
        self.name = self.name.strip()
        self.tunnel_id = self.tunnel_id.strip()
        self.mcp_target = self.mcp_target.strip()

    def validate(self) -> list[str]:
        errors: list[str] = []
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,127}", self.name):
            errors.append("Profile 名称必须以字母或数字开头，只能包含字母、数字、.、_、-，最长 128 字符")
        if not re.fullmatch(r"tunnel_[0-9a-f]{32}", self.tunnel_id):
            errors.append("Tunnel ID 必须是 tunnel_ 加 32 个小写十六进制字符")
        if not self.mcp_target:
            errors.append("MCP 地址/命令不能为空")
        if self.mcp_type is McpType.HTTP and self.mcp_target:
            try:
                parsed = urlsplit(self.mcp_target)
                if parsed.scheme not in {"http", "https"} or not parsed.hostname:
                    raise ValueError
            except ValueError:
                errors.append("HTTP MCP 地址必须是有效的 http:// 或 https:// URL")
        if self.mcp_type is McpType.STDIO and len(self.mcp_target) > 4096:
            errors.append("STDIO 命令最长 4096 字符")
        return errors


# Backward-compatible import name for tests/code from <=0.1.2. It is no longer persisted.
TunnelConfig = ProfileSpec


@dataclass(slots=True)
class ProfilePreference:
    auto_connect: bool = False
    auto_reconnect: bool = False
    enabled: bool = True

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "ProfilePreference":
        return cls(
            auto_connect=bool(data.get("auto_connect", False)),
            auto_reconnect=bool(data.get("auto_reconnect", False)),
            enabled=bool(data.get("enabled", True)),
        )


@dataclass(slots=True)
class AppSettings:
    binary_path: str = ""
    close_to_tray: bool = True
    start_with_windows: bool = False
    refresh_interval_ms: int = 4000
    profile_preferences: dict[str, ProfilePreference] = field(default_factory=dict)

    def preference(self, name: str) -> ProfilePreference:
        key = name.strip()
        pref = self.profile_preferences.get(key)
        if pref is None:
            pref = ProfilePreference()
            self.profile_preferences[key] = pref
        return pref

    def to_dict(self) -> dict[str, Any]:
        return {
            "schema_version": 2,
            "binary_path": self.binary_path,
            "close_to_tray": self.close_to_tray,
            "start_with_windows": self.start_with_windows,
            "refresh_interval_ms": self.refresh_interval_ms,
            "profile_preferences": {name: pref.to_dict() for name, pref in sorted(self.profile_preferences.items())},
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "AppSettings":
        preferences: dict[str, ProfilePreference] = {}
        raw_prefs = data.get("profile_preferences", {})
        if isinstance(raw_prefs, dict):
            for name, raw in raw_prefs.items():
                if isinstance(raw, dict) and str(name).strip():
                    preferences[str(name).strip()] = ProfilePreference.from_dict(raw)

        # One-way migration from <=0.1.2: keep only app-specific behavior flags.
        # Tunnel ID/MCP target are deliberately discarded because tunnel-client owns them.
        old_tunnels = data.get("tunnels", [])
        if isinstance(old_tunnels, list):
            for raw in old_tunnels:
                if not isinstance(raw, dict):
                    continue
                name = str(raw.get("alias", "")).strip()
                if not name or name in preferences:
                    continue
                preferences[name] = ProfilePreference(
                    auto_connect=bool(raw.get("auto_connect", False)),
                    auto_reconnect=bool(raw.get("auto_reconnect", False)),
                    enabled=bool(raw.get("enabled", True)),
                )

        try:
            refresh = max(1500, int(data.get("refresh_interval_ms", 4000)))
        except (TypeError, ValueError):
            refresh = 4000
        return cls(
            binary_path=str(data.get("binary_path", "")),
            close_to_tray=bool(data.get("close_to_tray", True)),
            start_with_windows=bool(data.get("start_with_windows", False)),
            refresh_interval_ms=refresh,
            profile_preferences=preferences,
        )


@dataclass(slots=True)
class ManagedItem:
    """Merged read-only view of tunnel-client profile inventory and runtime alias inventory."""

    name: str
    profile_name: str = ""
    profile_path: str = ""
    runtime_alias: str = ""
    runtime_profile_name: str = ""
    runtime_profile_path: str = ""
    tunnel_id: str = ""
    raw_runtime: dict[str, Any] = field(default_factory=dict)
    profile_listed: bool = False

    @property
    def identity(self) -> str:
        # Runtime aliases and named profiles are separate tunnel-client namespaces.
        # Keep them distinct even when their visible names collide.
        if self.runtime_alias:
            return f"runtime:{self.runtime_alias}"
        if self.profile_name:
            return f"profile:{self.profile_name}"
        return f"item:{self.name}"

    @property
    def credential_id(self) -> str:
        return self.runtime_alias or self.profile_name or self.name

    @property
    def has_profile(self) -> bool:
        return bool(self.profile_name and self.profile_path)

    @property
    def has_runtime_alias(self) -> bool:
        return bool(self.runtime_alias)

    @property
    def source_text(self) -> str:
        if self.has_profile and self.has_runtime_alias:
            return "Profile + 运行实例"
        if self.has_runtime_alias:
            return "运行实例"
        return "Profile 配置"


@dataclass(slots=True)
class RuntimeStatus:
    alias: str
    state: RuntimeState = RuntimeState.UNKNOWN
    process_running: bool = False
    healthy: bool = False
    ready: bool = False
    tunnel_id: str = ""
    pid: int | None = None
    profile_path: str = ""
    health_url: str = ""
    ui_url: str = ""
    log_path: str = ""
    error: str = ""
    raw: dict[str, Any] = field(default_factory=dict)
