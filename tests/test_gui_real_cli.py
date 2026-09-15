from __future__ import annotations
import os, shutil, stat, sys, tempfile, time, tkinter as tk, unittest
from pathlib import Path
from unittest.mock import patch
from openai_tunnel_manager.credentials import MemoryCredentialStore
from openai_tunnel_manager.main_window import MainWindow
from openai_tunnel_manager.models import AppSettings, ProfileSpec, McpType, RuntimeState
from openai_tunnel_manager.settings_store import SettingsStore
from openai_tunnel_manager.tunnel_client import TunnelClient

TID="tunnel_0123456789abcdef0123456789abcdef"

class GuiRealCliIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); d=Path(self.tmp.name); source=Path(__file__).with_name("fake_tunnel_client.py")
        if os.name=="nt":
            binary=d/"tunnel-client.cmd"; binary.write_text(f'@echo off\r\n"{sys.executable}" "{source}" %*\r\n',encoding="utf-8")
        else:
            binary=d/"tunnel-client"; shutil.copy2(source,binary); binary.chmod(binary.stat().st_mode|stat.S_IXUSR)
        self.env=patch.dict(os.environ,{"FAKE_TUNNEL_STATE":str(d/"state.json"),"FAKE_PROFILE_DIR":str(d/"profiles"),"FAKE_RUNTIME_DIR":str(d/"runtime"),"CONTROL_PLANE_API_KEY":"secret-key"},clear=False); self.env.start()
        self.client=TunnelClient(str(binary))
        # Create using tunnel-client itself BEFORE GUI starts. App settings contain no tunnel list.
        self.client.create_profile(ProfileSpec("idea",TID,McpType.HTTP,"http://127.0.0.1:64343/stream"))
        self.store=SettingsStore(d/"settings.json"); self.store.save(AppSettings(binary_path=str(binary),close_to_tray=False,refresh_interval_ms=60000))
        self.creds=MemoryCredentialStore(); self.creds.set("idea","secret-key")
        self.root=tk.Tk(); self.root.withdraw(); self.win=MainWindow(self.root,self.store,self.creds,self.client)
        self.assertTrue(self.pump_until(lambda:len(self.win.items)==1))
    def tearDown(self):
        try:self.win.quit()
        except tk.TclError:pass
        # MainWindow closes the executor without waiting so the real app can exit promptly.
        # Tests must join any in-flight CLI task before deleting the temporary .cmd launcher;
        # otherwise Windows can transiently fail cleanup with WinError 32 (file in use).
        try:self.win.async_bridge.pool.shutdown(wait=True,cancel_futures=True)
        except Exception:pass
        self.env.stop(); self.tmp.cleanup()
    def pump_until(self,predicate,timeout=4.0):
        end=time.monotonic()+timeout
        while time.monotonic()<end:
            self.root.update(); time.sleep(.01)
            if predicate(): return True
        return False

    def test_existing_tunnel_client_profile_is_visible_with_empty_app_settings(self):
        self.assertEqual(self.win.items[0].profile_name,"idea"); self.assertIn("idea",self.win.listbox.get(0))

    def test_profile_button_to_real_fake_subprocess_ready(self):
        self.win.start_selected()
        self.assertTrue(self.pump_until(lambda:self.win.statuses.get("profile:idea") is not None and self.win.statuses["profile:idea"].state is RuntimeState.READY,6.0))
        self.assertEqual(self.win.field_vars["process"].get(),"运行中"); self.assertEqual(self.win.field_vars["health"].get(),"正常")

    def test_started_profile_log_is_visible_in_gui(self):
        self.win.start_selected()
        self.assertTrue(self.pump_until(lambda:self.win.statuses.get("profile:idea") is not None and self.win.statuses["profile:idea"].state is RuntimeState.READY,6.0))
        self.assertTrue(self.pump_until(lambda:"INFO fake run profile=idea" in self.win.log_text.get("1.0","end-1c"),4.0))
        self.assertIn("runtime.log",self.win.log_path_var.get())
        self.assertTrue(self.pump_until(lambda:"fake run profile=idea" in self.win.log_text.get("1.0","end-1c"),3.0))
        self.assertIn("runtime.log",self.win.log_path_var.get())

    def test_external_runtime_added_after_gui_start_appears_on_refresh(self):
        self.client._run(["runtimes","connect","--alias","runtime-one","--tunnel-id",TID,"--profile","idea","--runtime-api-key","env:CONTROL_PLANE_API_KEY","--mcp-server-url","http://127.0.0.1:64343/stream","--json"])
        self.win.refresh_inventory(force=True)
        self.assertTrue(self.pump_until(lambda:any(i.runtime_alias=="runtime-one" for i in self.win.items)))

if __name__ == "__main__": unittest.main()
