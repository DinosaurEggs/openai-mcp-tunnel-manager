import unittest
from openai_tunnel_manager.models import RuntimeState
from openai_tunnel_manager.tunnel_client import parse_runtime_status

class StatusParserTests(unittest.TestCase):
    def test_ready(self):
        s=parse_runtime_status("idea", {"tunnel_id":"tunnel_0123456789abcdef0123456789abcdef","process_running":True,"healthy":True,"ready":True,"runtime":{"pid":55},"profile_path":"C:/a.yaml","health_url":"http://127.0.0.1:1"})
        self.assertIs(s.state, RuntimeState.READY); self.assertEqual(s.pid,55); self.assertEqual(s.ui_url,"http://127.0.0.1:1/ui")

    def test_ready_official_healthz_url_derives_correct_ui_base(self):
        s=parse_runtime_status("idea", {"process_running":True,"healthy":True,"ready":True,"health_url":"http://127.0.0.1:54321/healthz"})
        self.assertEqual(s.ui_url,"http://127.0.0.1:54321/ui")

    def test_stale(self):
        self.assertIs(parse_runtime_status("idea", {"runtime_state":"stale_alias","process_running":False}).state, RuntimeState.STALE)
        self.assertIs(parse_runtime_status("idea", {"stale":True,"runtime_state":"stopped","process_running":False}).state, RuntimeState.STALE)

    def test_official_starting_state(self):
        self.assertIs(parse_runtime_status("idea", {"runtime_state":"starting","process_running":True,"healthy":False,"ready":False}).state, RuntimeState.STARTING)

    def test_nested(self):
        s=parse_runtime_status("idea", {"process_running":True,"healthy":True,"ready":False,"runtime":{"profile_file":"C:/p.yaml","admin":{"ui_url":"http://x/ui"},"logs":{"log_file":"C:/x.log"}}})
        self.assertEqual(s.profile_path,"C:/p.yaml"); self.assertEqual(s.ui_url,"http://x/ui"); self.assertEqual(s.log_path,"C:/x.log")

if __name__ == "__main__": unittest.main()
