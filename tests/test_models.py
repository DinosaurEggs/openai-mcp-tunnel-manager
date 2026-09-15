from __future__ import annotations
import unittest
from openai_tunnel_manager.models import AppSettings, McpType, ProfileSpec, RuntimeState

class ModelTests(unittest.TestCase):
    def test_validate_http(self):
        spec = ProfileSpec("idea", "tunnel_0123456789abcdef0123456789abcdef", McpType.HTTP, "http://127.0.0.1:64343/mcp")
        self.assertEqual(spec.validate(), [])

    def test_validate_stdio(self):
        spec = ProfileSpec("idea", "tunnel_0123456789abcdef0123456789abcdef", McpType.STDIO, "python server.py")
        self.assertEqual(spec.validate(), [])

    def test_reject_bad_values(self):
        errors = " ".join(ProfileSpec("bad alias!", "bad", McpType.HTTP, "not-url").validate())
        self.assertIn("Profile", errors); self.assertIn("32", errors); self.assertIn("http://", errors)

    def test_settings_do_not_persist_tunnel_definition(self):
        raw = AppSettings().to_dict()
        self.assertNotIn("tunnels", raw)
        self.assertNotIn("tunnel_id", str(raw))
        self.assertNotIn("mcp_target", str(raw))

    def test_old_settings_migrate_only_preferences(self):
        settings = AppSettings.from_dict({"tunnels": [{"alias":"idea", "tunnel_id":"secret-definition", "mcp_target":"http://old", "auto_connect":True}]})
        self.assertTrue(settings.preference("idea").auto_connect)
        serialized = str(settings.to_dict())
        self.assertNotIn("secret-definition", serialized)
        self.assertNotIn("http://old", serialized)

    def test_configured_state_exists(self):
        self.assertEqual(RuntimeState.CONFIGURED.value, "Configured")

if __name__ == "__main__": unittest.main()
