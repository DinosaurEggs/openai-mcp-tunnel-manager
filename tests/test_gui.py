from __future__ import annotations
import tempfile, time, tkinter as tk, unittest
from pathlib import Path
from unittest.mock import patch
from openai_tunnel_manager.credentials import MemoryCredentialStore
from openai_tunnel_manager.main_window import ExistingProfileEditor, MainWindow, ProfileEditor, SettingsDialog
from openai_tunnel_manager.models import AppSettings, ManagedItem, McpType, ProfilePreference, RuntimeState, RuntimeStatus
from openai_tunnel_manager.settings_store import SettingsStore
from openai_tunnel_manager.tunnel_client import TunnelClient

TID="tunnel_0123456789abcdef0123456789abcdef"

class FakeClient:
    def __init__(self):
        self.binary_path=""; self.running=False; self.connect_calls=0; self.stop_calls=0; self.remove_calls=0; self.shutdown_calls=0; self.log_reads=0
        self.profile_path="/tmp/idea.yaml"
        self.profile_text=(
            'config_version: 1\n'
            'control_plane:\n'
            f'  tunnel_id: "{TID}"\n'
            '  api_key: "env:CONTROL_PLANE_API_KEY"\n'
            'mcp:\n'
            '  server_urls:\n'
            '    - channel: main\n'
            '      url: "http://127.0.0.1:64343/stream"\n'
        )
        self.saved_profile_text=""
        self.items=[ManagedItem(name="idea",profile_name="idea",profile_path=self.profile_path,runtime_alias="idea",runtime_profile_name="idea",runtime_profile_path=self.profile_path,tunnel_id=TID,profile_listed=True)]
    def resolve_binary(self): return "/fake/tunnel-client.exe"
    def capabilities(self): return {"version":"v0.0.14","runtimes":True,"profiles":True,"doctor":True}
    def inventory(self): return list(self.items)
    def status(self,alias):
        if self.running: return RuntimeStatus(alias=alias,state=RuntimeState.READY,process_running=True,healthy=True,ready=True,tunnel_id=TID,pid=10,log_path="/tmp/fake-runtime.log",raw={"process":{"target_kind":"server_url","target_value":"http://127.0.0.1:1"}})
        return RuntimeStatus(alias=alias,state=RuntimeState.STOPPED,tunnel_id=TID,raw={"process":{"target_kind":"server_url","target_value":"http://127.0.0.1:1"}})
    def status_item(self,item): return self.status(item.runtime_alias) if item.runtime_alias else RuntimeStatus(alias=item.name,state=RuntimeState.CONFIGURED,profile_path=item.profile_path)
    def connect_runtime(self,item,secret=None): self.connect_calls+=1; self.running=True; return {"ready":True}
    def start_profile(self,item,secret=None): self.connect_calls+=1; self.running=True; return RuntimeStatus(alias=item.name,state=RuntimeState.READY,process_running=True,healthy=True,ready=True)
    def stop(self,alias): self.stop_calls+=1; self.running=False; return {"stopped":alias}
    def stop_profile(self,name): self.stop_calls+=1; self.running=False
    def remove(self,alias):
        self.remove_calls+=1; self.running=False
        self.items=[i for i in self.items if i.runtime_alias!=alias]
        return {"removed":alias}
    def doctor_item(self,item,secret=None): return "Doctor passed"
    def health_snapshot(self,status): return {}
    def read_log_tail(self,path): self.log_reads+=1; return "INFO startup ok\nDEBUG detail"
    def create_profile(self,spec):
        self.items.append(ManagedItem(name=spec.name,profile_name=spec.name,profile_path=f"/tmp/{spec.name}.yaml",profile_listed=True)); return "created"
    def import_profile(self,name,path): self.items.append(ManagedItem(name=name,profile_name=name,profile_path=path,profile_listed=True)); return "imported"
    def read_profile_text(self,name,expected_path): return self.profile_text
    def save_profile_text(self,name,expected_path,text): self.profile_text=text; self.saved_profile_text=text; return "edited"
    @classmethod
    def profile_metadata_from_text(cls,text): return TunnelClient.profile_metadata_from_text(text)
    def delete_profile(self,name,expected_path): self.items=[i for i in self.items if i.profile_name!=name]
    def shutdown_profile_processes(self): self.shutdown_calls+=1

class GuiTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); d=Path(self.tmp.name)
        self.store=SettingsStore(d/"settings.json")
        # Deliberately no tunnel definitions: inventory must come from tunnel-client.
        self.store.save(AppSettings(binary_path="/fake/tunnel-client.exe",close_to_tray=False,refresh_interval_ms=60000))
        self.creds=MemoryCredentialStore(); self.creds.set("idea","secret-key")
        self.client=FakeClient(); self.root=tk.Tk(); self.root.withdraw()
        self.win=MainWindow(self.root,self.store,self.creds,self.client); self.pump(0.25)
    def tearDown(self):
        try:self.win.quit()
        except tk.TclError:pass
        self.tmp.cleanup()
    def pump(self,seconds=.2):
        end=time.monotonic()+seconds
        while time.monotonic()<end:
            self.root.update(); time.sleep(.01)

    def test_list_comes_from_tunnel_client_not_settings(self):
        self.assertFalse(hasattr(self.win.settings,"tunnels"))
        self.assertEqual(len(self.win.items),1); self.assertEqual(self.win.items[0].name,"idea")
        self.assertIn("idea",self.win.listbox.get(0))

    def test_external_inventory_changes_appear_and_disappear(self):
        self.client.items.append(ManagedItem(name="external",profile_name="external",profile_path="/tmp/external.yaml",profile_listed=True))
        self.win.refresh_inventory(force=True); self.pump(.25)
        self.assertEqual({i.name for i in self.win.items},{"idea","external"})
        self.client.items=[i for i in self.client.items if i.name!="external"]
        self.win.refresh_inventory(force=True); self.pump(.25)
        self.assertEqual([i.name for i in self.win.items],["idea"])

    def test_start_and_status_flow(self):
        self.win.start_selected(); self.pump(.7)
        self.assertEqual(self.client.connect_calls,1); self.assertEqual(self.win.statuses["runtime:idea"].state,RuntimeState.READY)
        self.assertEqual(self.win.field_vars["ready"].get(),"已就绪")

    def test_action_buttons_follow_runtime_state(self):
        self.client.running=False; self.win.refresh_selected(); self.pump(.25)
        self.assertFalse(self.win.start_button.instate(["disabled"]))
        self.assertTrue(self.win.stop_button.instate(["disabled"]))
        self.assertTrue(self.win.restart_button.instate(["disabled"]))
        self.client.running=True; self.win.refresh_selected(); self.pump(.25)
        self.assertTrue(self.win.start_button.instate(["disabled"]))
        self.assertFalse(self.win.stop_button.instate(["disabled"]))
        self.assertFalse(self.win.restart_button.instate(["disabled"]))

    def test_official_ui_button_removed(self):
        self.assertFalse(hasattr(self.win,"ui_button"))
        self.assertNotIn("打开官方 UI",[b.cget("text") for b in self.win.action_buttons])

    def test_manual_start_switches_to_log_tab(self):
        self.client.running=False; self.win.refresh_selected(); self.pump(.2)
        self.win.start_selected(); self.pump(.15)
        self.assertEqual(self.win.notebook.select(),str(self.win.log_frame))

    def test_manual_stop_suppresses_auto_reconnect(self):
        self.win.settings.profile_preferences["idea"]=ProfilePreference(auto_reconnect=True)
        self.client.running=True; self.win.refresh_selected(); self.pump(.2)
        self.win.stop_selected(); self.pump(.8)
        self.assertFalse(self.client.running); self.assertEqual(self.client.connect_calls,0); self.assertIn("runtime:idea",self.win.manual_stopped)

    def test_restart_aborts_if_stop_fails(self):
        self.client.running=True
        self.client.stop=lambda alias: (_ for _ in ()).throw(RuntimeError("stop failed"))
        with patch("openai_tunnel_manager.main_window.messagebox.showerror") as showerror:
            self.win.restart_selected(); self.pump(.5)
        self.assertEqual(self.client.connect_calls,0); self.assertTrue(showerror.called)

    def test_delete_cleans_official_runtime_then_profile(self):
        self.client.running=True
        with patch("openai_tunnel_manager.main_window.messagebox.askyesno",return_value=True):
            self.win.delete_selected(); self.pump(.6)
        self.assertEqual(self.client.stop_calls,1); self.assertEqual(self.client.remove_calls,1)
        self.assertEqual(self.client.items,[]); self.assertIsNone(self.creds.get("idea"))

    def test_delete_runtime_keeps_shared_profile_credential(self):
        p=self.client.profile_path
        self.client.items=[
            ManagedItem(name="r1",profile_name="idea",profile_path=p,runtime_alias="r1",runtime_profile_name="idea",runtime_profile_path=p,profile_listed=True),
            ManagedItem(name="r2",profile_name="idea",profile_path=p,runtime_alias="r2",runtime_profile_name="idea",runtime_profile_path=p,profile_listed=True),
        ]
        self.creds.set("r1","alias-secret")
        self.creds.set("idea","shared-profile-secret")
        self.win._apply_inventory(self.client.items,"runtime:r1")
        with patch("openai_tunnel_manager.main_window.messagebox.askyesno",return_value=True):
            self.win.delete_selected(); self.pump(.6)
        self.assertIsNone(self.creds.get("r1"))
        self.assertEqual(self.creds.get("idea"),"shared-profile-secret")
        self.assertTrue(any(i.runtime_alias=="r2" for i in self.client.items))

    def test_settings_contains_preferences_only(self):
        self.win.settings.profile_preferences["idea"]=ProfilePreference(auto_connect=True)
        self.store.save(self.win.settings); text=Path(self.store.path).read_text(encoding="utf-8")
        self.assertNotIn(TID,text); self.assertNotIn("127.0.0.1:1",text); self.assertNotIn("secret-key",text)

    def test_runtime_inherits_profile_preference_and_secret(self):
        item=ManagedItem(name="runtime-one",profile_name="idea",profile_path=self.client.profile_path,runtime_alias="runtime-one",runtime_profile_name="idea",runtime_profile_path=self.client.profile_path,profile_listed=True)
        self.win.settings.profile_preferences["profile:idea"]=ProfilePreference(auto_connect=True,auto_reconnect=True)
        self.creds.set("idea","profile-secret")
        inherited=self.win._find_preference(item)
        self.assertIsNotNone(inherited); self.assertTrue(inherited.auto_connect); self.assertTrue(inherited.auto_reconnect)
        self.assertEqual(self.win._saved_secret(item),"profile-secret")
        self.assertEqual(self.win._credential_write_id(item),"idea")

    def test_refresh_selection_uses_entity_identity_not_visible_name(self):
        runtime=ManagedItem(name="same",runtime_alias="same",runtime_profile_name="runtime-profile",runtime_profile_path="/tmp/runtime.yaml")
        profile=ManagedItem(name="same",profile_name="same",profile_path="/tmp/same.yaml",profile_listed=True)
        self.win._apply_inventory([runtime,profile],"profile:same")
        self.assertEqual(self.win.selected_item().identity,"profile:same")
        self.win._apply_inventory([runtime,profile],"runtime:same")
        self.assertEqual(self.win.selected_item().identity,"runtime:same")

    def test_ambiguous_visible_name_does_not_override_identity_selection(self):
        runtime=ManagedItem(name="same",runtime_alias="same",runtime_profile_name="runtime-profile",runtime_profile_path="/tmp/runtime.yaml")
        profile=ManagedItem(name="same",profile_name="same",profile_path="/tmp/same.yaml",profile_listed=True)
        # A legacy/non-identity selection token is ambiguous. Do not silently
        # select the last item that happens to share the visible name.
        self.win._apply_inventory([runtime,profile],"same")
        self.assertEqual(self.win.selected_item().identity,"runtime:same")

    def test_close_to_minimize_uses_taskbar_iconify(self):
        self.win.settings.close_to_tray=True
        with patch.object(self.root,"iconify") as iconify:
            self.win._on_close()
        iconify.assert_called_once_with()

    def test_log_search_and_level_filter(self):
        self.win._raw_log="INFO startup ok\nDEBUG noisy detail\nWARN foo problem\nINFO foo recovered"
        self.win.log_search_var.set("foo"); self.win.log_level_var.set("INFO"); self.win._render_log()
        self.assertEqual(self.win.log_text.get("1.0","end-1c"),"INFO foo recovered")

    def test_running_runtime_log_auto_refresh_and_pause(self):
        self.client.running=True
        self.win.refresh_selected(); self.pump(.4)
        self.assertGreaterEqual(self.client.log_reads,1)
        self.assertIn("INFO startup ok",self.win.log_text.get("1.0","end-1c"))
        self.assertIn("fake-runtime.log",self.win.log_path_var.get())
        reads=self.client.log_reads
        self.win.log_auto_refresh_var.set(False)
        self.win.refresh_selected(); self.pump(.35)
        self.assertEqual(self.client.log_reads,reads)
        self.win.refresh_log(); self.pump(.25)
        self.assertEqual(self.client.log_reads,reads+1)

    def test_stopped_runtime_keeps_last_log_available(self):
        self.client.running=True
        self.win.refresh_selected(); self.pump(.4)
        self.assertIn("fake-runtime.log", self.win.log_path_var.get())
        self.win.stop_selected(); self.pump(.7)
        self.assertFalse(self.client.running)
        self.assertIn("fake-runtime.log", self.win.log_path_var.get())
        self.win.refresh_log(); self.pump(.25)
        self.assertIn("INFO startup ok", self.win.log_text.get("1.0", "end-1c"))

    def test_copy_visible_log_uses_filtered_text(self):
        self.win._raw_log="INFO keep\nDEBUG hidden"
        self.win.log_level_var.set("INFO"); self.win._render_log()
        self.win.copy_visible_log(); self.root.update()
        self.assertEqual(self.root.clipboard_get(),"INFO keep")

    def test_profile_editor_valid_save(self):
        dlg=ProfileEditor(self.root); dlg.name.set("valid-alias"); dlg.tunnel_id.set(TID); dlg.kind.set("http"); dlg.target.set("http://127.0.0.1:64343/mcp"); dlg.secret.set("temporary-secret"); dlg._save()
        self.assertIsNotNone(dlg.result); self.assertEqual(dlg.result[0].name,"valid-alias"); self.assertEqual(dlg.result[1],"temporary-secret")

    def test_existing_profile_editor_is_internal_gui_and_preserves_advanced_yaml(self):
        meta=TunnelClient.profile_metadata_from_text(self.client.profile_text)
        dlg=ExistingProfileEditor(self.root,"idea",self.client.profile_path,self.client.profile_text,meta,ProfilePreference(),True)
        dlg.tunnel_id.set("tunnel_11111111111111111111111111111111")
        dlg.target.set("http://127.0.0.1:7000/mcp")
        dlg.raw_text.insert("end","# advanced-comment\n")
        dlg._save()
        self.assertIsNotNone(dlg.result)
        raw=dlg.result[0]
        self.assertIn("tunnel_11111111111111111111111111111111",raw)
        self.assertIn("http://127.0.0.1:7000/mcp",raw)
        self.assertIn("# advanced-comment",raw)

    def test_main_edit_profile_uses_gui_save_not_external_editor(self):
        class AutoEditor:
            def __init__(_self,parent,name,path,text,metadata,pref,has_secret):
                _self.result=(text.replace("64343/stream","7444/mcp"),pref,"",False)
        with patch("openai_tunnel_manager.main_window.ExistingProfileEditor",AutoEditor), patch.object(self.win,"_wait_dialog",lambda _d: None):
            self.win.edit_profile(); self.pump(.8)
        self.assertIn("7444/mcp",self.client.saved_profile_text)

    def test_settings_dialog_save_and_reject_missing(self):
        binary=Path(self.tmp.name)/"tunnel-client.exe"; binary.write_bytes(b"fake")
        dlg=SettingsDialog(self.root,self.win.settings); dlg.binary.set(str(binary)); dlg.refresh_seconds.set(5); dlg._save()
        self.assertEqual(dlg.result["binary_path"],str(binary)); self.assertEqual(dlg.result["refresh_interval_ms"],5000)
        dlg=SettingsDialog(self.root,self.win.settings); dlg.binary.set(str(Path(self.tmp.name)/"missing.exe"))
        with patch("openai_tunnel_manager.main_window.messagebox.showerror") as showerror: dlg._save()
        self.assertIsNone(dlg.result); self.assertTrue(showerror.called); dlg.destroy()

    def test_missing_binary_disables_actions(self):
        class Missing(FakeClient):
            def resolve_binary(self): raise RuntimeError("已配置的 tunnel-client 不存在")
        self.win.client=Missing()
        with patch("openai_tunnel_manager.main_window.messagebox.showwarning") as warning: ok=self.win._ensure_binary(show_warning=True)
        self.assertFalse(ok); self.assertTrue(warning.called); self.assertTrue(all(b.instate(["disabled"]) for b in self.win.action_buttons))

    def test_dialog_center_and_chinese(self):
        self.root.deiconify(); self.root.geometry("900x600+120+90"); self.root.update_idletasks()
        dlg=SettingsDialog(self.root,self.win.settings); dlg.update_idletasks()
        pcx=self.root.winfo_rootx()+self.root.winfo_width()//2; pcy=self.root.winfo_rooty()+self.root.winfo_height()//2
        dcx=dlg.winfo_rootx()+dlg.winfo_width()//2; dcy=dlg.winfo_rooty()+dlg.winfo_height()//2
        self.assertLessEqual(abs(pcx-dcx),8); self.assertLessEqual(abs(pcy-dcy),8); dlg.destroy()
        self.assertEqual(self.win.notebook.tab(0,"text"),"概览"); self.assertEqual(self.win.exit_button.cget("text"),"退出")
        self.root.withdraw()

if __name__ == "__main__": unittest.main()
