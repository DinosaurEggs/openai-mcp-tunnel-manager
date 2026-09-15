#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import shutil
import shlex
import signal
import subprocess
import sys
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

STATE = Path(os.environ.get("FAKE_TUNNEL_STATE", Path(tempfile.gettempdir()) / "fake_tunnel_state.json"))
PROFILE_DIR = Path(os.environ.get("FAKE_PROFILE_DIR", STATE.parent / "profiles"))
RUNTIME_DIR = Path(os.environ.get("FAKE_RUNTIME_DIR", STATE.parent / "runtime_profiles"))


def load():
    if not STATE.exists():
        return {"profiles": {}, "runtimes": {}}
    value = json.loads(STATE.read_text(encoding="utf-8"))
    if "profiles" not in value:
        # compatibility with very old fake state
        value = {"profiles": {}, "runtimes": value}
    value.setdefault("profiles", {})
    value.setdefault("runtimes", {})
    return value


def save(data):
    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps(data), encoding="utf-8")


def out(data):
    print(json.dumps(data))


def value(args, flag, default=""):
    return args[args.index(flag) + 1] if flag in args and args.index(flag) + 1 < len(args) else default


def profile_yaml(tunnel_id: str, key_ref: str, kind: str, target: str) -> str:
    if kind == "server_url":
        mcp = f'''mcp:\n  server_urls:\n    - channel: main\n      url: "{target}"\n'''
    else:
        escaped = target.replace('"', '\\"')
        mcp = f'''mcp:\n  commands:\n    - channel: main\n      command: "{escaped}"\n'''
    return f'''config_version: 1\ncontrol_plane:\n  tunnel_id: "{tunnel_id}"\n  api_key: "{key_ref}"\n{mcp}'''


def read_profile(path: str):
    text = Path(path).read_text(encoding="utf-8")
    import re
    tid = re.search(r"(?m)^\s*tunnel_id:\s*[\"']?([^\s\"']+)", text)
    key = re.search(r"(?m)^\s*api_key:\s*[\"']?([^\s\"']+)", text)
    url = re.search(r"(?m)^\s*url:\s*[\"']?(.+?)[\"']?\s*$", text)
    cmd = re.search(r"(?m)^\s*command:\s*[\"']?(.+?)[\"']?\s*$", text)
    return {
        "tunnel_id": tid.group(1) if tid else "",
        "api_key_ref": key.group(1) if key else "env:CONTROL_PLANE_API_KEY",
        "target_kind": "server_url" if url else ("command" if cmd else ""),
        "target_value": (url.group(1) if url else (cmd.group(1) if cmd else "")).strip('"\''),
    }


def ensure_secret(ref: str) -> bool:
    if ref.startswith("env:"):
        return bool(os.environ.get(ref[4:]))
    if ref.startswith("file:"):
        return Path(ref[5:]).is_file()
    return bool(ref)


class HealthHandler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        pass

    def do_GET(self):
        if self.path in {"/healthz", "/readyz"}:
            self.send_response(200); self.end_headers(); self.wfile.write(b"ok"); return
        if self.path.startswith("/health"):
            self.send_response(200); self.send_header("Content-Type", "application/json"); self.end_headers()
            self.wfile.write(json.dumps({"healthy": True, "ready": True, "mcp": {"healthy": True}}).encode()); return
        if self.path == "/ui":
            self.send_response(200); self.end_headers(); self.wfile.write(b"ui"); return
        self.send_response(404); self.end_headers()


def run_foreground(args):
    name = value(args, "--profile")
    data = load()
    entry = data["profiles"].get(name)
    if not entry:
        print("profile not found", file=sys.stderr); return 2
    meta = read_profile(entry["path"])
    if not ensure_secret(meta["api_key_ref"]):
        print("missing runtime key", file=sys.stderr); return 3
    server = ThreadingHTTPServer(("127.0.0.1", 0), HealthHandler)
    base = f"http://127.0.0.1:{server.server_port}"
    url_file = value(args, "--health.url-file")
    if url_file:
        Path(url_file).parent.mkdir(parents=True, exist_ok=True)
        Path(url_file).write_text(base, encoding="utf-8")
    stop = threading.Event()
    def handler(*_):
        stop.set(); server.shutdown()
    signal.signal(signal.SIGTERM, handler)
    signal.signal(signal.SIGINT, handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
    print(f"INFO fake run profile={name}", flush=True)
    while not stop.wait(0.1):
        pass
    server.server_close()
    return 0


def main():
    args = sys.argv[1:]
    if args == ["--version"]:
        print("tunnel-client v0.0.14"); return 0
    if "--help" in args:
        print("help"); return 0
    if not args:
        return 2

    if args[0] == "init":
        name = value(args, "--profile")
        tunnel_id = value(args, "--tunnel-id")
        key_ref = value(args, "--control-plane-api-key-ref", "env:CONTROL_PLANE_API_KEY")
        kind = "server_url" if "--mcp-server-url" in args else "command"
        target = value(args, "--mcp-server-url") or value(args, "--mcp-command")
        if not name or not tunnel_id or not target:
            print("missing init flags", file=sys.stderr); return 2
        data = load()
        if name in data["profiles"]:
            print("profile already exists", file=sys.stderr); return 2
        PROFILE_DIR.mkdir(parents=True, exist_ok=True)
        path = PROFILE_DIR / f"{name}.yaml"
        path.write_text(profile_yaml(tunnel_id, key_ref, kind, target), encoding="utf-8")
        data["profiles"][name] = {"name": name, "path": str(path)}
        save(data)
        print(str(path)); return 0

    if args[:2] == ["profiles", "list"]:
        data = load()
        data["profiles"] = {name: entry for name, entry in data["profiles"].items() if Path(entry.get("path", "")).is_file()}
        save(data)
        out(list(data["profiles"].values())); return 0
    if args[:2] == ["profiles", "add"]:
        name = args[2] if len(args) > 2 else ""
        source = value(args, "--from-file")
        force = "--force" in args
        if not name or not source or not Path(source).is_file():
            print("bad profiles add", file=sys.stderr); return 2
        data = load()
        if name in data["profiles"] and not force:
            print("profile exists", file=sys.stderr); return 2
        meta = read_profile(source)
        if not meta["tunnel_id"] or not meta["target_kind"] or not meta["target_value"]:
            print("invalid profile", file=sys.stderr); return 2
        PROFILE_DIR.mkdir(parents=True, exist_ok=True)
        dest = PROFILE_DIR / f"{name}.yaml"; shutil.copyfile(source, dest)
        data["profiles"][name] = {"name": name, "path": str(dest)}; save(data); print(str(dest)); return 0
    if args[:2] == ["profiles", "edit"]:
        name = args[2] if len(args) > 2 else ""
        profile = load()["profiles"].get(name)
        if not profile:
            print("profile not found", file=sys.stderr); return 2
        editor = os.environ.get("VISUAL") or os.environ.get("EDITOR")
        if not editor:
            print("missing editor", file=sys.stderr); return 2
        cmd = shlex.split(editor, posix=os.name != "nt") + [profile["path"]]
        result = subprocess.run(cmd, check=False)
        if result.returncode != 0:
            print("editor failed", file=sys.stderr); return result.returncode
        # Minimal validation mirroring the fake profile contract.
        meta = read_profile(profile["path"])
        if not meta["tunnel_id"] or not meta["target_kind"] or not meta["target_value"]:
            print("invalid profile", file=sys.stderr); return 2
        print("edited"); return 0

    if args[0] == "doctor":
        profile_path = value(args, "--profile-file")
        if not profile_path:
            name = value(args, "--profile")
            profile_path = load()["profiles"].get(name, {}).get("path", "")
        if not profile_path or not Path(profile_path).is_file() or "--explain" not in args:
            print("missing profile", file=sys.stderr); return 2
        meta = read_profile(profile_path)
        if not ensure_secret(meta["api_key_ref"]):
            print("missing runtime key", file=sys.stderr); return 3
        print("CHECK config ok\nCHECK mcp ok\nDoctor passed"); return 0

    if args[0] == "run":
        return run_foreground(args)

    if args[:2] == ["runtimes", "connect"]:
        alias = value(args, "--alias"); tunnel_id = value(args, "--tunnel-id")
        key_ref = value(args, "--runtime-api-key")
        profile_name = value(args, "--profile", alias)
        target_kind = "server_url" if "--mcp-server-url" in args else "command"
        target = value(args, "--mcp-server-url") or value(args, "--mcp-command")
        if not alias or not tunnel_id or not key_ref or not target:
            print("missing connect flags", file=sys.stderr); return 2
        if not ensure_secret(key_ref):
            print("missing injected key", file=sys.stderr); return 3
        data = load()
        listed = data["profiles"].get(profile_name)
        if listed:
            profile_path = listed["path"]
        else:
            RUNTIME_DIR.mkdir(parents=True, exist_ok=True)
            profile_path = str(RUNTIME_DIR / f"{alias}.yaml")
            Path(profile_path).write_text(profile_yaml(tunnel_id, key_ref, target_kind, target), encoding="utf-8")
        runtime = {
            "alias": alias, "tunnel_id": tunnel_id, "profile_name": profile_name,
            "profile_path": profile_path, "config_path": profile_path,
            "process_running": True, "healthy": True, "ready": True, "runtime_state": "ready", "pid": 4242,
            "health_url": "http://127.0.0.1:54321/healthz", "ui_url": "http://127.0.0.1:54321/ui",
            "process": {"target_kind": target_kind, "target_value": target, "log_path": os.environ.get("FAKE_LOG_PATH", "")},
        }
        data["runtimes"][alias] = runtime; save(data); out(runtime); return 0

    if args[:2] == ["runtimes", "status"]:
        alias = args[2]; data = load(); runtime = data["runtimes"].get(alias)
        if runtime is None:
            if os.environ.get("FAKE_STALE_STATUS") == alias:
                out({"alias": alias, "tunnel_id": "tunnel_0123456789abcdef0123456789abcdef", "runtime_state": "stopped", "process_running": False, "healthy": False, "ready": False, "stale": True, "error": "remote tunnel not found"}); return 2
            print("runtime alias not found", file=sys.stderr); return 1
        out(runtime); return 0

    if args[:2] == ["runtimes", "list"]:
        out({"aliases": list(load()["runtimes"].values())}); return 0

    if args[:2] == ["runtimes", "stop"]:
        alias = args[2]; data = load(); runtime = data["runtimes"].get(alias)
        if not runtime:
            print("runtime alias not found", file=sys.stderr); return 1
        runtime.update({"process_running": False, "healthy": False, "ready": False, "runtime_state": "stopped", "pid": None})
        save(data); out(runtime); return 0

    if args[:2] == ["runtimes", "rm"]:
        alias = args[2]; data = load(); data["runtimes"].pop(alias, None); save(data); out({"removed": alias}); return 0

    print("unsupported: " + " ".join(args), file=sys.stderr); return 2


if __name__ == "__main__":
    raise SystemExit(main())
