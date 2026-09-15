# Validation record

Release: `1.0.0`
Date: `2026-09-15`

This record describes the release gates for OpenAI MCP Tunnel Manager 1.0.0. The authoritative executable gate is `python verify.py` on Windows, followed by PyInstaller packaging in `.github/workflows/build-windows.yml`.

## Required gates

A release is not published unless all of the following complete successfully in the same Windows GitHub Actions job:

- Runtime dependencies install successfully.
- `python verify.py` completes with no required test failures.
- The application icon is generated successfully.
- PyInstaller produces the single-file `dist/OpenAITunnelManager.exe`.
- GitHub Package creation succeeds.
- GitHub Packages publication succeeds (duplicate versions are treated idempotently).
- Only then may the GitHub Release be created and the EXE uploaded.

Release publishing is additionally restricted to `master` push commits whose message starts with `Release v` and whose declared version matches the project version.

## Version consistency

`tests.test_version` verifies that release metadata agrees across:

- `src/openai_tunnel_manager/__init__.py`
- `pyproject.toml`
- `README.md`
- `VALIDATION.md`
- `RELEASE_NOTES.md`
- `tools/version_info.txt`
- `package/OpenAITunnelManager.Package.csproj`

The PyInstaller build consumes `tools/version_info.txt`, so Windows Explorer file properties report version 1.0.0 for the EXE.

## Windows single-instance gate

`tests.test_single_instance` runs a real Windows named-object round trip:

1. A primary guard acquires a unique Named Mutex.
2. A second guard using the same name must fail to become primary.
3. The second guard signals the shared Named Event.
4. The primary listener must receive that activation signal.
5. All Windows handles and the listener thread are closed cleanly.

The production startup path creates the single-instance guard before Tk, so a second EXE launch does not flash a second GUI. The activation callback is routed through the existing UI event queue before restoring/focusing the Tk window.

## tunnel-client source-of-truth audit

Verified architecture:

- Inventory comes from `tunnel-client profiles list --json` and `tunnel-client runtimes list --json`.
- Runtime state comes from `tunnel-client runtimes status <alias> --json`.
- External Profile / Runtime changes are reflected after refresh.
- Application settings persist GUI-local preferences only.
- Profile creation and editing continue to use tunnel-client validation.
- Runtime reconnect derives required values from official Runtime / Profile data.
- Same-name Profile / Runtime entities stay distinct internally.
- Custom profile directories and shared Profile references remain supported.

## GUI coverage

The Windows GUI gate covers, among other behavior:

- Internal Profile editing rather than external TXT/Notepad editing.
- State-sensitive Start / Stop / Restart actions.
- Configuration list status labels and right-click Edit / Delete behavior.
- Configuration and log "open location" actions.
- Persistent Windows system tray behavior.
- Log auto-refresh, filtering, incremental rendering and horizontal scroll preservation.
- Health-detail compatibility for both newer and older tunnel-client runtimes.

## Health compatibility

Base health continues to use the established `/healthz` and `/readyz` contract.

Detailed health is requested only when Runtime status advertises `health_details_url` and/or `mcp_health_url`. Older runtimes that omit these fields are reported as not supporting detailed health instead of being shown as failed because `/health` routes return HTTP 404.

## Credential gate

On Windows, `tests.test_windows_credentials` performs a real Windows Credential Manager write/read/delete round trip. Packaging stops if the required Windows gate fails.

## Packaging and publication

The release artifact is a single-file Windows GUI executable with the project icon and Windows file/product version resource.

The GitHub Packages artifact is a NuGet package named `OpenAITunnelManager.Windows` containing the same `OpenAITunnelManager.exe` under `tools/` plus the project README. The GitHub Release contains the directly downloadable EXE.
