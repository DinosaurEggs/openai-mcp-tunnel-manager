from __future__ import annotations

import json
import os
from pathlib import Path

from .models import AppSettings


def app_data_dir() -> Path:
    if os.name == "nt":
        base = Path(os.environ.get("LOCALAPPDATA") or os.environ.get("APPDATA") or Path.home())
        return base / "OpenAITunnelManager"
    return Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config")) / "openai-tunnel-manager"


class SettingsStore:
    def __init__(self, path: Path | None = None) -> None:
        self.path = path or app_data_dir() / "settings.json"

    def load(self) -> AppSettings:
        if not self.path.exists():
            return AppSettings()
        try:
            data = json.loads(self.path.read_text(encoding="utf-8"))
            if not isinstance(data, dict):
                raise ValueError("settings root must be an object")
            settings = AppSettings.from_dict(data)
            # <=0.1.2 duplicated tunnel definitions in app settings. Migrate them
            # immediately so tunnel-client remains the only persisted source of truth.
            if "tunnels" in data or int(data.get("schema_version", 0) or 0) < 2:
                self.save(settings)
            return settings
        except Exception:
            backup = self.path.with_suffix(self.path.suffix + ".broken")
            try:
                if not backup.exists():
                    backup.write_bytes(self.path.read_bytes())
            except OSError:
                pass
            return AppSettings()

    def save(self, settings: AppSettings) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        tmp = self.path.with_suffix(self.path.suffix + ".tmp")
        payload = json.dumps(settings.to_dict(), ensure_ascii=False, indent=2)
        tmp.write_text(payload, encoding="utf-8")
        os.replace(tmp, self.path)
