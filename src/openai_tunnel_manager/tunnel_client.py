from __future__ import annotations

import ipaddress
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

from .models import ManagedItem, McpType, ProfileSpec, RuntimeState, RuntimeStatus
from .runner import CommandError, CommandRunner


DEFAULT_RUNTIME_KEY_REF = "env:CONTROL_PLANE_API_KEY"


def _first(data: dict[str, Any], keys: Iterable[str], default: Any = None) -> Any:
    for key in keys:
        if key in data and data[key] is not None:
            return data[key]
    return default


def _recursive_string_by_keys(value: Any, keys: set[str]) -> str:
    if isinstance(value, dict):
        for k, v in value.items():
            if str(k).lower() in keys and isinstance(v, str) and v:
                return v
        for v in value.values():
            found = _recursive_string_by_keys(v, keys)
            if found:
                return found
    elif isinstance(value, list):
        for v in value:
            found = _recursive_string_by_keys(v, keys)
            if found:
                return found
    return ""


def _recursive_int_by_keys(value: Any, keys: set[str]) -> int | None:
    if isinstance(value, dict):
        for k, v in value.items():
            if str(k).lower() in keys:
                try:
                    return int(v)
                except (TypeError, ValueError):
                    pass
        for v in value.values():
            found = _recursive_int_by_keys(v, keys)
            if found is not None:
                return found
    elif isinstance(value, list):
        for v in value:
            found = _recursive_int_by_keys(v, keys)
            if found is not None:
                return found
    return None


def _normalize_health_base_url(url: str) -> str:
    value = url.strip()
    if not value:
        return ""
    for suffix in ("/healthz", "/readyz"):
        if value.endswith(suffix):
            value = value[: -len(suffix)]
    return value.rstrip("/")


def _normalized_entry_path(path: str) -> str:
    """Normalize a filesystem entry path without resolving symlinks.

    Profile symlinks are an officially supported tunnel-client configuration
    surface.  Using Path.resolve() here would collapse the profile entry to its
    target and could make a delete operation remove the target file instead of
    the named profile entry itself.
    """
    if not path:
        return ""
    return os.path.normcase(os.path.abspath(os.path.expanduser(path)))


def _same_profile_entry(left: str, right: str) -> bool:
    """Return whether two paths represent the same profile entry/content."""
    if not left or not right:
        return False
    if _normalized_entry_path(left) == _normalized_entry_path(right):
        return True
    try:
        return os.path.samefile(left, right)
    except OSError:
        return False


def _is_loopback_http_url(url: str) -> bool:
    try:
        parsed = urllib.parse.urlsplit(url)
        if parsed.scheme not in {"http", "https"} or not parsed.hostname:
            return False
        host = parsed.hostname.lower().strip("[]")
        if host == "localhost":
            return True
        return ipaddress.ip_address(host).is_loopback
    except (ValueError, ipaddress.AddressValueError):
        return False


def parse_runtime_status(alias: str, payload: dict[str, Any]) -> RuntimeStatus:
    process_running = bool(_first(payload, ["process_running", "running"], False))
    healthy = bool(_first(payload, ["healthy"], False))
    ready = bool(_first(payload, ["ready"], False))
    runtime_state_text = str(_first(payload, ["runtime_state", "state"], "")).lower()
    stale = bool(payload.get("stale", False)) or runtime_state_text in {"stale", "stale_alias"}

    if stale:
        state = RuntimeState.STALE
    elif ready and process_running:
        state = RuntimeState.READY
    elif process_running and healthy:
        state = RuntimeState.RUNNING
    elif process_running:
        state = RuntimeState.STARTING
    elif runtime_state_text in {"stopped", "disconnected", "not_running", "missing_profile"}:
        state = RuntimeState.STOPPED
    elif runtime_state_text in {"error", "failed"}:
        state = RuntimeState.ERROR
    else:
        state = RuntimeState.STOPPED if payload else RuntimeState.UNKNOWN

    health_url = _recursive_string_by_keys(payload, {"health_url", "health_base_url"})
    ui_url = _recursive_string_by_keys(payload, {"ui_url", "admin_ui_url"})
    if not ui_url and health_url:
        ui_url = _normalize_health_base_url(health_url) + "/ui"
    log_path = _recursive_string_by_keys(
        payload, {"log_path", "log_file", "log_file_path", "runtime_log", "logs_path", "logs"}
    )
    profile_path = _recursive_string_by_keys(payload, {"profile_path", "profile_file"})
    pid = _recursive_int_by_keys(payload, {"pid", "process_id"})

    return RuntimeStatus(
        alias=alias,
        state=state,
        process_running=process_running,
        healthy=healthy,
        ready=ready,
        tunnel_id=str(_first(payload, ["tunnel_id"], "")),
        pid=pid,
        profile_path=profile_path,
        health_url=health_url,
        ui_url=ui_url,
        log_path=log_path,
        raw=payload,
    )


@dataclass(slots=True)
class _ForegroundProfile:
    name: str
    process: subprocess.Popen[Any]
    health_url_file: Path
    log_path: Path


class TunnelClient:
    def __init__(self, binary_path: str = "", runner: CommandRunner | None = None) -> None:
        self.binary_path = binary_path
        self.runner = runner or CommandRunner()
        self._foreground: dict[str, _ForegroundProfile] = {}

    def resolve_binary(self) -> str:
        configured = self.binary_path.strip()
        if configured:
            path = Path(configured).expanduser()
            if path.is_file():
                return str(path.resolve())
            raise CommandError(f"已配置的 tunnel-client 不存在：{configured}。请在设置中重新选择 tunnel-client.exe。")

        if getattr(sys, "frozen", False):
            base = Path(sys.executable).resolve().parent
        else:
            base = Path.cwd()
        candidates = [str(base / "tunnel-client.exe"), str(base / "tunnel-client")]
        found = shutil.which("tunnel-client")
        if found:
            candidates.append(found)
        for candidate in candidates:
            path = Path(candidate).expanduser()
            if path.is_file():
                return str(path.resolve())
        raise CommandError("未找到 tunnel-client。请在设置中选择完整 tunnel-client.exe。")

    def _run(
        self,
        args: list[str],
        *,
        env: dict[str, str] | None = None,
        timeout: float = 45.0,
        check: bool = True,
    ):
        return self.runner.run(self.resolve_binary(), args, env=env, timeout=timeout, check=check)

    @staticmethod
    def _secret_env(secret_ref: str, secret: str | None) -> dict[str, str] | None:
        if not secret:
            return None
        ref = secret_ref.strip()
        if ref.startswith("env:") and len(ref) > 4:
            return {ref[4:]: secret}
        # Secret file refs are already self-contained and should not be replaced.
        return None

    @staticmethod
    def _profile_secret_ref(path: str) -> str:
        if not path:
            return DEFAULT_RUNTIME_KEY_REF
        try:
            text = Path(path).read_text(encoding="utf-8")
        except OSError:
            return DEFAULT_RUNTIME_KEY_REF
        # Generated runtime profiles may be JSON (valid YAML); init profiles are YAML.
        try:
            raw = json.loads(text)
        except json.JSONDecodeError:
            raw = None
        if isinstance(raw, dict):
            cp = raw.get("control_plane")
            if isinstance(cp, dict):
                value = cp.get("api_key")
                if isinstance(value, str) and (value.startswith("env:") or value.startswith("file:")):
                    return value.strip()
        match = re.search(r"(?m)^\s*api_key\s*:\s*['\"]?((?:env:|file:)[^\s#'\"]+)", text)
        return match.group(1).strip() if match else DEFAULT_RUNTIME_KEY_REF

    @staticmethod
    def _yaml_scalar(value: str) -> str:
        value = value.strip()
        if not value:
            return ""
        if value[0:1] == value[-1:] and value[0:1] in {"\"", "'"}:
            if value.startswith('"'):
                try:
                    parsed = json.loads(value)
                    if isinstance(parsed, str):
                        return parsed
                except json.JSONDecodeError:
                    pass
            return value[1:-1].replace("''", "'")
        return value.split(" #", 1)[0].strip()

    @classmethod
    def profile_metadata(cls, path: str) -> dict[str, str]:
        """Read only the fields needed to operate an official tunnel-client profile."""
        if not path:
            return {"tunnel_id": "", "api_key_ref": DEFAULT_RUNTIME_KEY_REF, "target_kind": "", "target_value": ""}
        try:
            text = Path(path).read_text(encoding="utf-8")
        except OSError:
            return {"tunnel_id": "", "api_key_ref": DEFAULT_RUNTIME_KEY_REF, "target_kind": "", "target_value": ""}
        return cls.profile_metadata_from_text(text)

    @classmethod
    def profile_metadata_from_text(cls, text: str) -> dict[str, str]:
        """Parse the common profile fields from YAML/JSON text without owning the config.

        Unknown/advanced fields are intentionally ignored and therefore remain untouched by
        the GUI's raw editor.
        """
        result = {"tunnel_id": "", "api_key_ref": DEFAULT_RUNTIME_KEY_REF, "target_kind": "", "target_value": ""}
        try:
            raw = json.loads(text)
        except json.JSONDecodeError:
            raw = None
        if isinstance(raw, dict):
            cp = raw.get("control_plane")
            if isinstance(cp, dict):
                result["tunnel_id"] = str(cp.get("tunnel_id", "")).strip()
                ref = cp.get("api_key")
                if isinstance(ref, str) and ref.strip():
                    result["api_key_ref"] = ref.strip()
            mcp = raw.get("mcp")
            if isinstance(mcp, dict):
                for entry in mcp.get("server_urls", []) if isinstance(mcp.get("server_urls"), list) else []:
                    if isinstance(entry, dict) and str(entry.get("channel", "main")) == "main" and entry.get("url"):
                        result["target_kind"] = "server_url"
                        result["target_value"] = str(entry["url"])
                        return result
                for entry in mcp.get("commands", []) if isinstance(mcp.get("commands"), list) else []:
                    if isinstance(entry, dict) and str(entry.get("channel", "main")) == "main" and entry.get("command"):
                        result["target_kind"] = "command"
                        result["target_value"] = str(entry["command"])
                        return result
            return result

        section = ""
        mcp_mode = ""
        channel = "main"
        for raw_line in text.splitlines():
            if not raw_line.strip() or raw_line.lstrip().startswith("#"):
                continue
            indent = len(raw_line) - len(raw_line.lstrip(" "))
            stripped = raw_line.strip()
            if indent == 0 and stripped.endswith(":"):
                section = stripped[:-1]
                mcp_mode = ""
                continue
            if section == "control_plane":
                m = re.match(r"tunnel_id\s*:\s*(.+)$", stripped)
                if m:
                    result["tunnel_id"] = cls._yaml_scalar(m.group(1))
                    continue
                m = re.match(r"api_key\s*:\s*(.+)$", stripped)
                if m:
                    value = cls._yaml_scalar(m.group(1))
                    if value:
                        result["api_key_ref"] = value
                    continue
            if section == "mcp":
                if stripped == "server_urls:":
                    mcp_mode, channel = "server_url", "main"
                    continue
                if stripped == "commands:":
                    mcp_mode, channel = "command", "main"
                    continue
                m = re.match(r"-?\s*channel\s*:\s*(.+)$", stripped)
                if m and mcp_mode:
                    channel = cls._yaml_scalar(m.group(1))
                    continue
                if mcp_mode == "server_url" and channel == "main":
                    m = re.match(r"url\s*:\s*(.+)$", stripped)
                    if m:
                        result["target_kind"] = "server_url"
                        result["target_value"] = cls._yaml_scalar(m.group(1))
                        return result
                if mcp_mode == "command" and channel == "main":
                    m = re.match(r"command\s*:\s*(.+)$", stripped)
                    if m:
                        result["target_kind"] = "command"
                        result["target_value"] = cls._yaml_scalar(m.group(1))
                        return result
        return result

    def version(self) -> str:
        return self._run(["--version"], timeout=10).stdout.strip()

    def capabilities(self) -> dict[str, bool | str]:
        result: dict[str, bool | str] = {"version": "", "runtimes": False, "doctor": False, "profiles": False}
        try:
            result["version"] = self.version()
        except CommandError:
            return result
        for name, args in {
            "runtimes": ["runtimes", "--help"],
            "doctor": ["doctor", "--help"],
            "profiles": ["profiles", "--help"],
        }.items():
            try:
                self._run(args, timeout=10)
                result[name] = True
            except CommandError:
                result[name] = False
        return result

    def list_profiles(self) -> list[dict[str, str]]:
        result = self._run(["profiles", "list", "--json"], timeout=20)
        try:
            payload = json.loads(result.stdout)
        except json.JSONDecodeError as exc:
            raise CommandError("profiles list 返回的内容不是有效 JSON", stdout=result.stdout, stderr=result.stderr) from exc
        if not isinstance(payload, list):
            raise CommandError("profiles list JSON 根节点不是数组", stdout=result.stdout, stderr=result.stderr)
        entries: list[dict[str, str]] = []
        for item in payload:
            if not isinstance(item, dict):
                continue
            name = str(item.get("name", "")).strip()
            path = str(item.get("path", "")).strip()
            if name and path:
                entries.append({"name": name, "path": path})
        return entries

    def list_runtimes(self) -> dict[str, Any]:
        return self._run(["runtimes", "list", "--json"], timeout=20).json()

    def inventory(self) -> list[ManagedItem]:
        """Read tunnel-client profiles/runtime aliases and build a live display view.

        Runtime aliases are kept as distinct rows so multiple aliases may safely refer to
        the same profile. Profiles not referenced by any runtime are added as profile-only
        rows. Nothing from this inventory is persisted by the GUI.
        """
        profiles = self.list_profiles()
        runtimes = self.list_runtimes()
        profile_by_name = {p["name"]: p for p in profiles}
        linked_profiles: set[str] = set()
        items: list[ManagedItem] = []

        aliases = runtimes.get("aliases", []) if isinstance(runtimes, dict) else []
        if not isinstance(aliases, list):
            aliases = []
        for raw in aliases:
            if not isinstance(raw, dict):
                continue
            alias = str(raw.get("alias", "")).strip()
            if not alias:
                continue
            runtime_profile_name = str(raw.get("profile_name", "")).strip()
            runtime_profile_path = str(raw.get("profile_path", raw.get("config_path", ""))).strip()
            listed = profile_by_name.get(runtime_profile_name) if runtime_profile_name else None
            # A runtime can use --profile-dir and therefore have the same
            # profile *name* as an unrelated profile in the default profile
            # directory.  When the runtime exposes its profile path, require
            # the paths to refer to the same entry/content before merging.
            if listed and runtime_profile_path and not _same_profile_entry(listed["path"], runtime_profile_path):
                listed = None
            profile_name = runtime_profile_name if runtime_profile_name else ""
            profile_path = listed["path"] if listed else runtime_profile_path
            if listed:
                linked_profiles.add(runtime_profile_name)
            items.append(ManagedItem(
                name=alias,
                profile_name=profile_name,
                profile_path=profile_path,
                runtime_alias=alias,
                runtime_profile_name=runtime_profile_name,
                runtime_profile_path=runtime_profile_path,
                tunnel_id=str(raw.get("tunnel_id", "")).strip(),
                raw_runtime=dict(raw),
                profile_listed=bool(listed),
            ))

        for profile in profiles:
            if profile["name"] in linked_profiles:
                continue
            items.append(ManagedItem(
                name=profile["name"],
                profile_name=profile["name"],
                profile_path=profile["path"],
                profile_listed=True,
            ))
        return sorted(items, key=lambda x: (x.name.casefold(), x.runtime_alias.casefold(), x.profile_name.casefold()))

    def create_profile(self, spec: ProfileSpec) -> str:
        errors = spec.validate()
        if errors:
            raise ValueError("；".join(errors))
        args = [
            "init",
            "--profile", spec.name,
            "--tunnel-id", spec.tunnel_id,
            "--control-plane-api-key-ref", DEFAULT_RUNTIME_KEY_REF,
        ]
        if spec.mcp_type is McpType.HTTP:
            args.extend(["--mcp-server-url", spec.mcp_target])
        else:
            args.extend(["--mcp-command", spec.mcp_target])
        return self._run(args, timeout=30).stdout.strip()

    def import_profile(self, name: str, source_path: str) -> str:
        name = name.strip()
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,127}", name):
            raise ValueError("导入文件名不是有效的 Profile 名称")
        return self._run(["profiles", "add", name, "--from-file", source_path], timeout=30).stdout.strip()

    def read_profile_text(self, name: str, expected_path: str) -> str:
        """Read a listed profile after re-checking its tunnel-client-owned path."""
        matches = [p for p in self.list_profiles() if p["name"] == name]
        if len(matches) != 1:
            raise RuntimeError(f"无法确认 tunnel-client Profile {name} 的唯一文件，请刷新列表")
        actual_path = matches[0]["path"]
        if _normalized_entry_path(actual_path) != _normalized_entry_path(expected_path):
            raise RuntimeError("Profile 路径已发生变化，请刷新列表")
        try:
            return Path(actual_path).read_text(encoding="utf-8")
        except OSError as exc:
            raise RuntimeError(f"读取 Profile 失败：{exc}") from exc

    def save_profile_text(self, name: str, expected_path: str, text: str) -> str:
        """Validate and replace GUI-edited profile text through tunnel-client itself.

        `profiles add --from-file --force` runs the same full profile validator as
        `profiles edit`, without depending on VISUAL/EDITOR parsing. This is robust
        when the application or Windows user path contains spaces.
        """
        self.read_profile_text(name, expected_path)
        if not text.strip():
            raise ValueError("Profile 内容不能为空")
        with tempfile.TemporaryDirectory(prefix="openai-tunnel-manager-edit-") as td:
            payload = Path(td) / "profile.yaml"
            payload.write_text(text, encoding="utf-8", newline="")
            return self._run(
                ["profiles", "add", name, "--from-file", str(payload), "--force"],
                timeout=30,
            ).stdout.strip()

    def delete_profile(self, name: str, expected_path: str) -> None:
        matches = [p for p in self.list_profiles() if p["name"] == name]
        if not matches:
            return
        if len(matches) != 1:
            raise RuntimeError(f"无法确认 tunnel-client Profile {name} 的唯一文件，取消删除")
        actual_path = matches[0]["path"]
        if _normalized_entry_path(actual_path) != _normalized_entry_path(expected_path):
            raise RuntimeError("Profile 路径已发生变化，取消删除并请先刷新列表")
        # Delete the profile directory entry itself.  Do not resolve symlinks:
        # tunnel-client explicitly supports named profile symlinks.
        actual = Path(actual_path).expanduser()
        if not (actual.is_file() or actual.is_symlink()):
            raise RuntimeError("Profile 文件已不存在，请刷新列表")
        actual.unlink()

    def status(self, alias: str) -> RuntimeStatus:
        try:
            payload = self._run(["runtimes", "status", alias, "--json"], timeout=20).json()
            return parse_runtime_status(alias, payload)
        except CommandError as exc:
            stdout = exc.stdout.strip()
            if stdout:
                try:
                    payload = json.loads(stdout)
                except json.JSONDecodeError:
                    payload = None
                if isinstance(payload, dict) and any(
                    key in payload for key in ("alias", "runtime_state", "process_running", "healthy", "ready", "stale")
                ):
                    status = parse_runtime_status(alias, payload)
                    payload_error = _first(payload, ["error", "remote_error"], "")
                    if payload_error:
                        status.error = str(payload_error)
                    return status
            text = f"{exc.stderr}\n{exc.stdout}\n{exc}".lower()
            missing_markers = ("not found", "unknown alias", "no runtime alias", "does not exist", "is not known")
            if any(marker in text for marker in missing_markers):
                return RuntimeStatus(alias=alias, state=RuntimeState.STOPPED, error=str(exc))
            return RuntimeStatus(alias=alias, state=RuntimeState.ERROR, error=str(exc))

    def status_item(self, item: ManagedItem) -> RuntimeStatus:
        if item.runtime_alias:
            return self.status(item.runtime_alias)
        if item.profile_name:
            return self.profile_status(item.profile_name, item.profile_path)
        return RuntimeStatus(alias=item.name, state=RuntimeState.UNKNOWN)

    @staticmethod
    def _target_from_status(status: RuntimeStatus) -> tuple[str, str]:
        process = status.raw.get("process", {}) if isinstance(status.raw, dict) else {}
        if isinstance(process, dict):
            kind = str(process.get("target_kind", "")).strip()
            value = str(process.get("target_value", "")).strip()
            if kind and value:
                return kind, value
        return "", ""

    def connect_runtime(self, item: ManagedItem, secret: str | None = None) -> dict[str, Any]:
        if not item.runtime_alias:
            raise ValueError("该项目没有 tunnel-client Runtime alias")
        status = self.status(item.runtime_alias)
        profile_name = str(status.raw.get("profile_name", "")).strip() or item.runtime_profile_name or item.profile_name or item.runtime_alias
        profile_dir = str(status.raw.get("profile_dir", "")).strip() or str(item.raw_runtime.get("profile_dir", "")).strip()
        profile_path = status.profile_path or item.runtime_profile_path or item.profile_path
        metadata = self.profile_metadata(profile_path)
        tunnel_id = status.tunnel_id or item.tunnel_id or metadata["tunnel_id"]
        if not tunnel_id:
            raise RuntimeError("官方 Runtime 状态/Profile 中缺少 Tunnel ID，无法安全重建 connect 命令")
        kind, target = self._target_from_status(status)
        if not target:
            kind, target = metadata["target_kind"], metadata["target_value"]
        if not target:
            raise RuntimeError("官方 Runtime 状态/Profile 中缺少 main MCP target；请用 tunnel-client 修复该 Runtime 配置")
        secret_ref = metadata["api_key_ref"] or self._profile_secret_ref(profile_path)
        args = [
            "runtimes", "connect",
            "--alias", item.runtime_alias,
            "--tunnel-id", tunnel_id,
            "--profile", profile_name,
            "--runtime-api-key", secret_ref,
        ]
        if profile_dir:
            args.extend(["--profile-dir", profile_dir])
        normalized_kind = kind.strip().lower().replace("-", "_")
        if normalized_kind in {"server_url", "mcp_server_url", "url", "http", "https"}:
            args.extend(["--mcp-server-url", target])
        elif normalized_kind in {"command", "mcp_command", "stdio"}:
            args.extend(["--mcp-command", target])
        else:
            # Status schemas have changed across tunnel-client revisions. When the
            # process target kind is unknown, prefer the documented official profile
            # binding instead of guessing from an application-side cache.
            fallback_kind = metadata["target_kind"]
            fallback_target = metadata["target_value"]
            if fallback_kind == "server_url" and fallback_target:
                args.extend(["--mcp-server-url", fallback_target])
            elif fallback_kind == "command" and fallback_target:
                args.extend(["--mcp-command", fallback_target])
            else:
                raise RuntimeError(f"不支持的 Runtime MCP target 类型：{kind}")
        args.append("--json")
        return self._run(args, env=self._secret_env(secret_ref, secret), timeout=90).json()

    def stop(self, alias: str) -> dict[str, Any]:
        return self._run(["runtimes", "stop", alias, "--json"], timeout=30).json()

    def remove(self, alias: str) -> dict[str, Any]:
        return self._run(["runtimes", "rm", alias, "--json"], timeout=30).json()

    def doctor_item(self, item: ManagedItem, secret: str | None = None) -> str:
        args = ["doctor"]
        profile_path = item.profile_path or item.runtime_profile_path
        if item.profile_name and item.profile_listed:
            args.extend(["--profile", item.profile_name])
        elif profile_path:
            args.extend(["--profile-file", profile_path])
        else:
            raise RuntimeError("该项目没有可供 doctor 使用的 tunnel-client Profile")
        args.append("--explain")
        secret_ref = self._profile_secret_ref(profile_path)
        return self._run(args, env=self._secret_env(secret_ref, secret), timeout=60).stdout

    def start_profile(self, item: ManagedItem, secret: str | None = None) -> RuntimeStatus:
        name = item.profile_name
        if not name:
            raise RuntimeError("该项目没有 tunnel-client Profile")
        current = self._foreground.get(name)
        if current and current.process.poll() is None:
            return self.profile_status(name, item.profile_path)

        work_dir = Path(tempfile.mkdtemp(prefix="openai-tunnel-manager-"))
        health_file = work_dir / "health.url"
        log_path = work_dir / "runtime.log"
        profile_path = item.profile_path
        secret_ref = self._profile_secret_ref(profile_path)
        full_env = os.environ.copy()
        extra = self._secret_env(secret_ref, secret)
        if extra:
            full_env.update(extra)
        args = [
            self.resolve_binary(), "run", "--profile", name,
            "--health.listen-addr", "127.0.0.1:0",
            "--health.url-file", str(health_file),
        ]
        creationflags = 0
        startupinfo = None
        if os.name == "nt":
            creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
            startupinfo = subprocess.STARTUPINFO()
            startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        log_handle = log_path.open("ab")
        try:
            proc = subprocess.Popen(
                args,
                stdin=subprocess.DEVNULL,
                stdout=log_handle,
                stderr=subprocess.STDOUT,
                env=full_env,
                creationflags=creationflags,
                startupinfo=startupinfo,
            )
        finally:
            log_handle.close()
        self._foreground[name] = _ForegroundProfile(name, proc, health_file, log_path)

        deadline = time.monotonic() + 12.0
        while time.monotonic() < deadline:
            if proc.poll() is not None:
                break
            if health_file.is_file() and health_file.read_text(encoding="utf-8", errors="replace").strip():
                break
            time.sleep(0.05)
        return self.profile_status(name, profile_path)

    def profile_status(self, name: str, profile_path: str = "") -> RuntimeStatus:
        record = self._foreground.get(name)
        if record is None:
            return RuntimeStatus(alias=name, state=RuntimeState.CONFIGURED, profile_path=profile_path)
        code = record.process.poll()
        if code is not None:
            return RuntimeStatus(
                alias=name,
                state=RuntimeState.ERROR,
                profile_path=profile_path,
                log_path=str(record.log_path),
                error=f"Profile 前台进程已退出，退出码 {code}",
            )
        health_url = ""
        try:
            if record.health_url_file.is_file():
                health_url = record.health_url_file.read_text(encoding="utf-8", errors="replace").strip()
        except OSError:
            pass
        healthy = self._probe_bool(health_url, "/healthz") if health_url else False
        ready = self._probe_bool(health_url, "/readyz") if health_url else False
        state = RuntimeState.READY if ready else (RuntimeState.RUNNING if healthy else RuntimeState.STARTING)
        return RuntimeStatus(
            alias=name,
            state=state,
            process_running=True,
            healthy=healthy,
            ready=ready,
            pid=record.process.pid,
            profile_path=profile_path,
            health_url=health_url,
            ui_url=_normalize_health_base_url(health_url) + "/ui" if health_url else "",
            log_path=str(record.log_path),
            raw={"mode": "gui_foreground_profile", "profile_name": name},
        )

    @staticmethod
    def _probe_bool(base_or_url: str, suffix: str, timeout: float = 1.0) -> bool:
        base = _normalize_health_base_url(base_or_url)
        if not base or not _is_loopback_http_url(base):
            return False
        try:
            with urllib.request.urlopen(base + suffix, timeout=timeout) as response:
                return 200 <= response.status < 300
        except (urllib.error.URLError, TimeoutError, OSError):
            return False

    def stop_profile(self, name: str) -> None:
        record = self._foreground.get(name)
        if record is None:
            return
        proc = record.process
        if proc.poll() is None:
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait(timeout=3)
        self._foreground.pop(name, None)

    def shutdown_profile_processes(self) -> None:
        for name in list(self._foreground):
            try:
                self.stop_profile(name)
            except Exception:
                pass

    @staticmethod
    def read_log_tail(path: str, *, max_bytes: int = 512 * 1024, max_lines: int = 5000) -> str:
        """Read a bounded tail of a tunnel-client runtime log file.

        The log path comes from tunnel-client runtime status (or the GUI-managed
        foreground profile process). Reading is deliberately bounded so a large
        long-running log cannot freeze the GUI or consume unbounded memory.
        """
        value = str(path or "").strip()
        if not value:
            return ""
        max_bytes = max(4096, min(int(max_bytes), 4 * 1024 * 1024))
        max_lines = max(1, min(int(max_lines), 20000))
        log_path = Path(value).expanduser()
        try:
            if not log_path.is_file():
                raise RuntimeError(f"日志文件不存在：{log_path}")
            with log_path.open("rb") as handle:
                handle.seek(0, os.SEEK_END)
                size = handle.tell()
                start = max(0, size - max_bytes)
                handle.seek(start)
                data = handle.read(max_bytes)
        except OSError as exc:
            raise RuntimeError(f"读取日志文件失败：{exc}") from exc
        if start > 0:
            # The first bytes may be the middle of a UTF-8 code point or line.
            # Drop through the first newline and decode only complete tail lines.
            newline = data.find(b"\n")
            if newline >= 0:
                data = data[newline + 1 :]
        text = data.decode("utf-8", errors="replace")
        lines = text.splitlines()
        if len(lines) > max_lines:
            lines = lines[-max_lines:]
        return "\n".join(lines)

    def health_snapshot(self, status: RuntimeStatus, timeout: float = 3.0) -> dict[str, Any]:
        base = _normalize_health_base_url(status.health_url)
        if not base:
            return {}
        if not _is_loopback_http_url(base):
            return {"error": "拒绝访问非 loopback Health URL", "health_url": base}
        result: dict[str, Any] = {}
        for name, suffix in (("details", "/health?details=true"), ("mcp", "/health/mcp")):
            url = base + suffix
            req = urllib.request.Request(url, headers={"Accept": "application/json"}, method="GET")
            try:
                with urllib.request.urlopen(req, timeout=timeout) as response:
                    raw = response.read(1024 * 1024)
                    ctype = response.headers.get_content_charset() or "utf-8"
                    text = raw.decode(ctype, errors="replace")
                    try:
                        result[name] = json.loads(text)
                    except json.JSONDecodeError:
                        result[name] = {"status": response.status, "body": text}
            except (urllib.error.URLError, TimeoutError, OSError) as exc:
                result[name] = {"error": str(exc)}
        return result

