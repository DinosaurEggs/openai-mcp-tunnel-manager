from __future__ import annotations

import json
import urllib.error
import urllib.parse
import urllib.request
from typing import Any

from .models import RuntimeStatus
from .tunnel_client import TunnelClient, _is_loopback_http_url, _normalize_health_base_url


def _find_string(value: Any, key: str) -> str:
    if isinstance(value, dict):
        for current_key, current_value in value.items():
            if str(current_key).lower() == key and isinstance(current_value, str) and current_value.strip():
                return current_value.strip()
        for current_value in value.values():
            found = _find_string(current_value, key)
            if found:
                return found
    elif isinstance(value, list):
        for current_value in value:
            found = _find_string(current_value, key)
            if found:
                return found
    return ""


def _resolve_health_endpoint(base: str, value: str) -> str:
    raw = str(value or "").strip()
    if not raw:
        return ""
    parsed = urllib.parse.urlsplit(raw)
    if parsed.scheme:
        return raw
    return urllib.parse.urljoin(base.rstrip("/") + "/", raw.lstrip("/"))


def _read_health_json(url: str, timeout: float) -> dict[str, Any]:
    if not _is_loopback_http_url(url):
        return {"error": "拒绝访问非 loopback Health URL", "url": url}
    req = urllib.request.Request(url, headers={"Accept": "application/json"}, method="GET")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as response:
            raw = response.read(1024 * 1024)
            ctype = response.headers.get_content_charset() or "utf-8"
            text = raw.decode(ctype, errors="replace")
            try:
                return json.loads(text)
            except json.JSONDecodeError:
                return {"status": response.status, "body": text}
    except urllib.error.HTTPError as exc:
        try:
            body = exc.read(1024 * 1024).decode("utf-8", errors="replace")
        except Exception:
            body = ""
        result: dict[str, Any] = {"error": f"HTTP {exc.code}: {exc.reason}", "status": exc.code}
        if body:
            result["body"] = body
        return result
    except (urllib.error.URLError, TimeoutError, OSError) as exc:
        return {"error": str(exc)}


class CompatibleTunnelClient(TunnelClient):
    """TunnelClient with health-detail compatibility across runtime versions."""

    def health_snapshot(self, status: RuntimeStatus, timeout: float = 3.0) -> dict[str, Any]:
        base = _normalize_health_base_url(status.health_url)
        if not base:
            return {}
        if not _is_loopback_http_url(base):
            return {"error": "拒绝访问非 loopback Health URL", "health_url": base}

        # Liveness/readiness are part of the long-standing health contract and
        # are already reported by `runtimes status --json`. Do not re-probe them
        # merely to render the details page.
        result: dict[str, Any] = {
            "healthz": {"ok": bool(status.healthy), "url": base + "/healthz"},
            "readyz": {"ok": bool(status.ready), "url": base + "/readyz"},
        }

        # Newer tunnel-client runtimes advertise these URLs only when the
        # component-detail routes are actually supported. Older runtimes omit
        # them, even though /healthz and /readyz continue to work.
        details_url = _resolve_health_endpoint(base, _find_string(status.raw, "health_details_url"))
        mcp_url = _resolve_health_endpoint(base, _find_string(status.raw, "mcp_health_url"))

        if details_url:
            result["details"] = _read_health_json(details_url, timeout)
        else:
            result["details"] = {
                "supported": False,
                "message": "当前 tunnel-client Runtime 未声明详细健康接口，已显示基础健康状态。",
            }

        if mcp_url:
            result["mcp"] = _read_health_json(mcp_url, timeout)
        else:
            result["mcp"] = {
                "supported": False,
                "message": "当前 tunnel-client Runtime 未声明 MCP 组件健康接口。",
            }
        return result
