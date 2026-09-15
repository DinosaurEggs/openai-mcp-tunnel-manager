from __future__ import annotations
import json, threading, unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from openai_tunnel_manager.compatible_tunnel_client import CompatibleTunnelClient
from openai_tunnel_manager.models import RuntimeStatus
from openai_tunnel_manager.tunnel_client import TunnelClient, _is_loopback_http_url, _normalize_health_base_url

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        payload = {"ok": True, "path": self.path}
        body = json.dumps(payload).encode()
        self.send_response(200); self.send_header("Content-Type","application/json"); self.send_header("Content-Length",str(len(body))); self.end_headers(); self.wfile.write(body)
    def log_message(self, format, *args): pass

class HealthTests(unittest.TestCase):
    def test_loopback_guard(self):
        self.assertTrue(_is_loopback_http_url("http://127.0.0.1:1234"))
        self.assertTrue(_is_loopback_http_url("http://localhost:1234"))
        self.assertTrue(_is_loopback_http_url("http://[::1]:1234"))
        self.assertFalse(_is_loopback_http_url("https://example.com"))
        self.assertFalse(_is_loopback_http_url("file:///tmp/a"))

    def test_official_status_healthz_url_is_normalized_to_base(self):
        self.assertEqual(_normalize_health_base_url("http://127.0.0.1:54321/healthz"), "http://127.0.0.1:54321")
        self.assertEqual(_normalize_health_base_url("http://127.0.0.1:54321/readyz"), "http://127.0.0.1:54321")

    def test_health_snapshot(self):
        server=ThreadingHTTPServer(("127.0.0.1",0),Handler)
        t=threading.Thread(target=server.serve_forever,daemon=True); t.start()
        try:
            status=RuntimeStatus(alias="x",health_url=f"http://127.0.0.1:{server.server_address[1]}/healthz")
            snap=TunnelClient().health_snapshot(status)
            self.assertTrue(snap["details"]["ok"]); self.assertIn("details=true",snap["details"]["path"])
            self.assertEqual(snap["mcp"]["path"],"/health/mcp")
        finally:
            server.shutdown(); server.server_close(); t.join(timeout=2)

    def test_compatible_snapshot_prefers_advertised_urls(self):
        server=ThreadingHTTPServer(("127.0.0.1",0),Handler)
        t=threading.Thread(target=server.serve_forever,daemon=True); t.start()
        try:
            base=f"http://127.0.0.1:{server.server_address[1]}"
            status=RuntimeStatus(
                alias="x",
                health_url=base+"/healthz",
                healthy=True,
                ready=True,
                raw={
                    "health_details_url": base+"/custom-details",
                    "mcp_health_url": base+"/custom-mcp",
                },
            )
            snap=CompatibleTunnelClient().health_snapshot(status)
            self.assertEqual(snap["details"]["path"], "/custom-details")
            self.assertEqual(snap["mcp"]["path"], "/custom-mcp")
            self.assertTrue(snap["healthz"]["ok"])
            self.assertTrue(snap["readyz"]["ok"])
        finally:
            server.shutdown(); server.server_close(); t.join(timeout=2)

    def test_compatible_snapshot_does_not_probe_new_routes_for_old_runtime(self):
        status=RuntimeStatus(
            alias="x",
            health_url="http://127.0.0.1:54321/healthz",
            healthy=True,
            ready=True,
            raw={},
        )
        snap=CompatibleTunnelClient().health_snapshot(status)
        self.assertEqual(snap["healthz"]["ok"],True)
        self.assertEqual(snap["readyz"]["ok"],True)
        self.assertFalse(snap["details"]["supported"])
        self.assertFalse(snap["mcp"]["supported"])
        self.assertIn("未声明",snap["details"]["message"])

    def test_non_loopback_rejected_without_request(self):
        snap=TunnelClient().health_snapshot(RuntimeStatus(alias="x",health_url="https://example.com"))
        self.assertIn("拒绝",snap["error"])

if __name__ == "__main__": unittest.main()
