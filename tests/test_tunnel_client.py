from __future__ import annotations
import json, os, shutil, stat, sys, tempfile, unittest
from pathlib import Path
from unittest.mock import patch
from openai_tunnel_manager.models import ManagedItem, McpType, ProfileSpec, RuntimeState
from openai_tunnel_manager.runner import CommandError
from openai_tunnel_manager.tunnel_client import TunnelClient

TID="tunnel_0123456789abcdef0123456789abcdef"

class TunnelClientIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.dir=Path(self.tmp.name)
        source=Path(__file__).with_name("fake_tunnel_client.py")
        if os.name == "nt":
            self.binary=self.dir/"tunnel-client.cmd"
            self.binary.write_text(f'@echo off\r\n"{sys.executable}" "{source}" %*\r\n', encoding="utf-8")
        else:
            self.binary=self.dir/"tunnel-client"; shutil.copy2(source,self.binary); self.binary.chmod(self.binary.stat().st_mode|stat.S_IXUSR)
        self.state=self.dir/"state.json"
        self.env=patch.dict(os.environ,{"FAKE_TUNNEL_STATE":str(self.state),"FAKE_PROFILE_DIR":str(self.dir/"profiles"),"FAKE_RUNTIME_DIR":str(self.dir/"runtime"),"CONTROL_PLANE_API_KEY":"secret-key"},clear=False); self.env.start()
        self.client=TunnelClient(str(self.binary))

    def tearDown(self):
        self.client.shutdown_profile_processes(); self.env.stop(); self.tmp.cleanup()

    def spec(self, name="idea", kind=McpType.HTTP):
        target="http://127.0.0.1:64343/stream" if kind is McpType.HTTP else "python server.py"
        return ProfileSpec(name,TID,kind,target)

    def create(self,name="idea",kind=McpType.HTTP):
        self.client.create_profile(self.spec(name,kind))
        return next(i for i in self.client.inventory() if i.name==name)

    def external_runtime(self, alias="idea", profile="idea", kind="server_url", target="http://127.0.0.1:64343/stream"):
        args=["runtimes","connect","--alias",alias,"--tunnel-id",TID,"--profile",profile,"--runtime-api-key","env:CONTROL_PLANE_API_KEY"]
        args += ["--mcp-server-url",target] if kind=="server_url" else ["--mcp-command",target]
        args.append("--json"); self.client._run(args,timeout=30)
        return next(i for i in self.client.inventory() if i.runtime_alias==alias)

    def test_capabilities(self):
        caps=self.client.capabilities(); self.assertIn("v0.0.14",str(caps["version"])); self.assertTrue(caps["runtimes"] and caps["doctor"] and caps["profiles"])

    def test_inventory_reads_profile_without_any_app_config(self):
        self.client.create_profile(self.spec())
        items=self.client.inventory()
        self.assertEqual(len(items),1); self.assertEqual(items[0].profile_name,"idea"); self.assertTrue(items[0].profile_listed); self.assertFalse(items[0].has_runtime_alias)

    def test_inventory_merges_linked_profile_runtime(self):
        self.create(); item=self.external_runtime()
        self.assertEqual(item.name,"idea"); self.assertTrue(item.has_profile and item.has_runtime_alias and item.profile_listed)
        self.assertEqual(len(self.client.inventory()),1)

    def test_multiple_runtime_aliases_for_one_profile_are_not_collapsed(self):
        self.create("shared")
        self.external_runtime("r1","shared"); self.external_runtime("r2","shared")
        items=self.client.inventory(); aliases={i.runtime_alias for i in items}
        self.assertEqual(aliases,{"r1","r2"}); self.assertEqual(len(items),2)

    def test_runtime_alias_and_unrelated_profile_same_name_stay_distinct(self):
        self.create("same")
        self.create("other")
        self.external_runtime("same", "other")
        items=self.client.inventory()
        identities={i.identity for i in items}
        self.assertIn("runtime:same",identities)
        self.assertIn("profile:same",identities)
        self.assertEqual(len([i for i in items if i.name=="same"]),2)

    def test_same_profile_name_with_different_runtime_profile_path_stays_distinct(self):
        listed=self.create("same")
        custom_dir=self.dir/"custom-runtime"; custom_dir.mkdir()
        custom_path=custom_dir/"same.yaml"
        custom_path.write_text(Path(listed.profile_path).read_text(encoding="utf-8"),encoding="utf-8")
        data=json.loads(self.state.read_text(encoding="utf-8"))
        data["runtimes"]["same"]={
            "alias":"same","tunnel_id":TID,"profile_name":"same",
            "profile_path":str(custom_path),"config_path":str(custom_path),
            "process_running":False,"healthy":False,"ready":False,"runtime_state":"stopped",
            "process":{"target_kind":"server_url","target_value":"http://127.0.0.1:64343/stream"},
        }
        self.state.write_text(json.dumps(data),encoding="utf-8")
        items=self.client.inventory(); identities={i.identity for i in items}
        self.assertEqual(identities,{"profile:same","runtime:same"})
        runtime=next(i for i in items if i.identity=="runtime:same")
        self.assertFalse(runtime.profile_listed)
        self.assertEqual(Path(runtime.profile_path),custom_path)

    def test_profile_foreground_lifecycle(self):
        item=self.create()
        started=self.client.start_profile(item,"secret-key")
        self.assertTrue(started.process_running); self.assertIn(started.state,{RuntimeState.RUNNING,RuntimeState.READY})
        self.client.stop_profile("idea")
        self.assertIs(self.client.profile_status("idea",item.profile_path).state,RuntimeState.CONFIGURED)

    def test_runtime_stop_and_reconnect_from_official_state_profile(self):
        self.create(); item=self.external_runtime()
        self.client.stop(item.runtime_alias)
        payload=self.client.connect_runtime(item,"secret-key")
        self.assertTrue(payload["ready"]); self.assertIs(self.client.status(item.runtime_alias).state,RuntimeState.READY)

    def test_doctor_uses_official_profile(self):
        item=self.create(); self.assertIn("Doctor passed",self.client.doctor_item(item,"secret-key"))

    def test_profile_metadata_http_and_stdio(self):
        http=self.create("http"); meta=self.client.profile_metadata(http.profile_path)
        self.assertEqual(meta["tunnel_id"],TID); self.assertEqual(meta["target_kind"],"server_url"); self.assertIn("64343",meta["target_value"])
        stdio=self.create("stdio",McpType.STDIO); meta=self.client.profile_metadata(stdio.profile_path)
        self.assertEqual(meta["target_kind"],"command"); self.assertEqual(meta["target_value"],"python server.py")

    def test_gui_profile_save_uses_tunnel_client_validator(self):
        item=self.create("editable")
        original=self.client.read_profile_text("editable",item.profile_path)
        updated=original.replace(TID,"tunnel_11111111111111111111111111111111").replace("64343/stream","7555/mcp")
        result=self.client.save_profile_text("editable",item.profile_path,updated)
        self.assertIn("editable.yaml",result)
        saved=Path(item.profile_path).read_text(encoding="utf-8")
        self.assertIn("tunnel_11111111111111111111111111111111",saved)
        self.assertIn("7555/mcp",saved)

    def test_gui_profile_save_rejects_invalid_profile(self):
        item=self.create("invalid-edit")
        with self.assertRaises(CommandError):
            self.client.save_profile_text("invalid-edit",item.profile_path,"config_version: 1\ncontrol_plane: {}\n")
        saved=Path(item.profile_path).read_text(encoding="utf-8")
        self.assertIn(TID,saved)

    def test_import_and_delete_are_official_profile_operations(self):
        source=self.dir/"incoming.yaml"; source.write_text('config_version: 1\ncontrol_plane:\n  tunnel_id: "'+TID+'"\n  api_key: "env:CONTROL_PLANE_API_KEY"\nmcp:\n  commands:\n    - channel: main\n      command: "python mcp.py"\n',encoding="utf-8")
        self.client.import_profile("imported",str(source)); item=next(i for i in self.client.inventory() if i.name=="imported")
        self.assertTrue(Path(item.profile_path).is_file())
        self.client.delete_profile("imported",item.profile_path)
        self.assertFalse(any(i.name=="imported" for i in self.client.inventory()))

    @unittest.skipIf(os.name=="nt", "Windows symlink creation may require Developer Mode/admin privileges")
    def test_delete_profile_unlinks_profile_symlink_not_target(self):
        item=self.create("linked")
        link=Path(item.profile_path)
        target=self.dir/"real-profile.yaml"
        shutil.move(str(link),target)
        link.symlink_to(target)
        self.assertTrue(link.is_symlink()); self.assertTrue(target.is_file())
        self.client.delete_profile("linked",str(link))
        self.assertFalse(link.exists()); self.assertTrue(target.is_file())

    def test_custom_runtime_profile_doctor_uses_profile_file(self):
        item=self.external_runtime("custom","not-listed")
        self.assertFalse(item.profile_listed); self.assertTrue(item.runtime_profile_path)
        self.assertIn("Doctor passed",self.client.doctor_item(item,"secret-key"))

    def test_nonzero_json_stale_status_is_preserved(self):
        with patch.dict(os.environ,{"FAKE_STALE_STATUS":"stale-one"},clear=False): status=self.client.status("stale-one")
        self.assertIs(status.state,RuntimeState.STALE); self.assertIn("remote tunnel not found",status.error)

    def test_read_log_tail_reads_bounded_tail(self):
        log=self.dir/"large.log"
        lines=[f"INFO line {i}" for i in range(10000)]
        log.write_text("\n".join(lines)+"\n",encoding="utf-8")
        text=self.client.read_log_tail(str(log),max_bytes=8192,max_lines=80)
        self.assertIn("INFO line 9999",text)
        self.assertLessEqual(len(text.splitlines()),80)
        self.assertNotIn("INFO line 0\n",text)

    def test_read_log_tail_missing_file_is_clear_error(self):
        with self.assertRaises(RuntimeError) as ctx:
            self.client.read_log_tail(str(self.dir/"missing.log"))
        self.assertIn("日志文件不存在",str(ctx.exception))

    def test_missing_binary(self):
        with self.assertRaises(CommandError): TunnelClient(str(self.dir/"missing.exe")).version()

    def test_explicit_missing_binary_never_falls_back_to_path(self):
        with patch("openai_tunnel_manager.tunnel_client.shutil.which",return_value=str(self.binary)):
            with self.assertRaises(CommandError) as ctx: TunnelClient(str(self.dir/"configured-missing.exe")).resolve_binary()
        self.assertIn("已配置的 tunnel-client 不存在",str(ctx.exception))

if __name__ == "__main__": unittest.main()
