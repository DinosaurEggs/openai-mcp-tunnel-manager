from __future__ import annotations
import tempfile, unittest
from pathlib import Path
from openai_tunnel_manager.models import AppSettings, ProfilePreference
from openai_tunnel_manager.settings_store import SettingsStore

class SettingsTests(unittest.TestCase):
    def test_roundtrip_only_app_preferences(self):
        with tempfile.TemporaryDirectory() as td:
            path=Path(td)/"settings.json"; store=SettingsStore(path)
            s=AppSettings(binary_path="C:/tunnel-client.exe", profile_preferences={"idea":ProfilePreference(True, True, True)})
            store.save(s); text=path.read_text(encoding="utf-8")
            self.assertNotIn("tunnel_id", text); self.assertNotIn("mcp_target", text); self.assertNotIn("api_key", text)
            loaded=store.load(); self.assertTrue(loaded.preference("idea").auto_connect)

    def test_broken_preserved(self):
        with tempfile.TemporaryDirectory() as td:
            path=Path(td)/"settings.json"; path.write_text("{broken",encoding="utf-8")
            loaded=SettingsStore(path).load()
            self.assertEqual(loaded.profile_preferences,{})
            self.assertTrue(path.with_suffix(".json.broken").exists())

    def test_old_tunnel_definitions_are_removed_on_load(self):
        with tempfile.TemporaryDirectory() as td:
            path=Path(td)/"settings.json"
            path.write_text('{"binary_path":"x","tunnels":[{"alias":"idea","tunnel_id":"old-private-definition","mcp_target":"http://old","auto_connect":true}]}',encoding="utf-8")
            loaded=SettingsStore(path).load()
            self.assertTrue(loaded.preference("idea").auto_connect)
            rewritten=path.read_text(encoding="utf-8")
            self.assertNotIn("old-private-definition",rewritten); self.assertNotIn("mcp_target",rewritten); self.assertIn('"schema_version": 2',rewritten)

if __name__ == "__main__": unittest.main()
