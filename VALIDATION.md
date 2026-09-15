# Validation record

Release: `0.3.0`
Date: `2026-09-15`

This record describes the checks performed on the source package before delivery.

## Automated checks

The complete test suite contains **70 tests**. The process-level tunnel-client tests are executed in deterministic groups in this delivery environment because long single commands can exceed the harness transport timeout.

- Non-tunnel-client core/GUI/process group: **49 passed**.
- TunnelClient integration group: **20 passed** (all methods covered in exhaustive groups).
- Native Windows Credential Manager group: **1 skipped on Linux** by design.
- Total: **70 tests; 69 passed; 0 failed; 1 skipped on Linux**.

On Windows, `verify.ps1` / `build.ps1` execute the Credential Manager test instead of skipping it.

Additional gates:

- `python -m compileall` for `src`, `tests`, and `launcher.py`.
- Tkinter launcher smoke under Xvfb; GUI must remain alive during the smoke interval and emit no stderr.
- Production dependency audit: runtime imports use Python standard library only.
- Secret scan for OpenAI-key-shaped literals.
- Unsafe execution scan for `shell=True`, `os.system`, `eval`, and `exec` in production sources.
- External editor invocation scan: no Notepad / `$EDITOR` / `$VISUAL` launch path in production behavior.
- Source ZIP cleanliness and CRC validation.
- Fresh ZIP extraction followed by the same grouped test gate, compile check, static scans, and GUI smoke.

## tunnel-client source-of-truth audit

Verified behavior:

- Inventory comes from `tunnel-client profiles list --json` and `tunnel-client runtimes list --json`.
- Runtime state comes from `tunnel-client runtimes status <alias> --json`.
- Existing profiles appear when application settings contain no tunnel definitions.
- Profiles/runtimes added or removed externally are reflected on refresh.
- App settings persist GUI-local preferences only; Tunnel ID, MCP target, Health URL, log path, runtime status and official Profile contents are not app-owned configuration.
- Profile creation uses `tunnel-client init`.
- Profile import uses `profiles add --from-file`.
- GUI Profile editing saves through `profiles add <name> --from-file <temp> --force`, using tunnel-client validation before replacement.
- Doctor uses the official listed Profile or Runtime profile file.
- Runtime reconnect reconstructs required values from official Runtime/Profile data.
- Same-name Profile/Runtime entities stay distinct internally.
- Custom profile-dir paths are respected; same names alone do not cause false merges.
- Multiple Runtime aliases referencing one Profile are retained independently.
- Shared Profile credentials are not deleted when removing only one Runtime alias.
- Profile symlink deletion removes the named symlink entry, not its target.

## GUI editor coverage

Verified behavior:

- Existing Profile edit opens a centered internal GUI window; no external TXT/Notepad editor is invoked.
- Editor contains Common / Advanced / Local settings tabs.
- Common fields safely update recognized Tunnel ID and main MCP URL/command values.
- Advanced YAML/JSON remains available inside the GUI so unknown fields and comments are preserved.
- Local page manages enabled/auto-connect/auto-reconnect and optional Runtime API Key storage.
- Invalid edited Profile content is rejected by tunnel-client and the original Profile remains unchanged.

## Runtime action coverage

Verified behavior:

- The “Open official UI” action is absent.
- Stopped/configured state: Start enabled, Stop and Restart disabled.
- Starting/in-flight operation: conflicting runtime actions are disabled.
- Running/Ready state: Start disabled, Stop and Restart enabled.
- Manual Stop suppresses auto-reconnect.
- Restart aborts if Stop fails.

## Runtime log coverage

Verified behavior:

- Runtime log path is read from official runtime status (`log_path` and supported nested equivalents).
- GUI foreground `run --profile` stdout/stderr is written to a Runtime log file.
- Manual Start automatically switches to the Log tab.
- Started Profile log content is visible in the GUI through a real child-process integration test.
- GUI and core use the single `read_log_tail()` API; the old missing-method failure (`TunnelClient` has no `tail_log`) is eliminated and no duplicate log-tail API remains.
- Log reading runs in a background worker.
- Tail reads are bounded by bytes and line count.
- Missing log files return a clear Chinese error.
- Auto refresh can be paused; manual refresh remains available.
- Search and DEBUG/INFO/WARN/ERROR filtering work on the in-memory tail.
- Copy-visible-log and export actions are covered.
- After Stop, the last known log path remains available and recent log content can still be viewed.

## Integration coverage

The process-level fake `tunnel-client` verifies:

- `profiles list/add`, `init`, `doctor`, and Profile lifecycle.
- `runtimes connect/list/status/stop/rm` and JSON parsing.
- Runtime API key delivery through environment references rather than plaintext CLI secrets.
- Ready, starting, stopped, stale, and non-zero-with-valid-JSON status handling.
- `/healthz` URL normalization before detailed Health requests.
- GUI startup with a pre-existing tunnel-client Profile and no app-side tunnel inventory.
- External Runtime creation appearing after GUI refresh.
- GUI Start -> child process -> Ready.
- GUI Start -> foreground log file -> visible log page.
- Chinese labels, centered dialogs and strict missing-binary handling.

## Windows gate

`build.ps1` runs the full test gate before PyInstaller. On Windows, `test_windows_credentials.py` performs a real write/read/delete round-trip against Windows Credential Manager. Packaging aborts on any required test failure.

## Environment limitation

The delivery environment is Linux, so a Windows `.exe` is not falsely marked as locally verified. The source package, Windows build script, and Windows GitHub Actions workflow are included; the Windows build path performs its own verification before packaging.
