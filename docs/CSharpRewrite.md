# C# / WinUI 3 rewrite

This branch contains the C# rewrite of OpenAI MCP Tunnel Manager.

## Baseline

- .NET 10 / C# 14
- WinUI 3 on Windows App SDK 2.4
- CommunityToolkit.Mvvm
- Microsoft.Extensions.Hosting
- YamlDotNet
- unpackaged, Windows App SDK self-contained deployment

## Architecture

```text
OpenAITunnelManager.App
  -> OpenAITunnelManager.Core
  -> OpenAITunnelManager.Infrastructure
       -> tunnel-client.exe
```

`tunnel-client` remains the source of truth. The application does not persist a second copy of Profile, Runtime, Tunnel ID, MCP target, health, or log-path data.

The first migration milestone implements the new Connections workspace and reads live data from:

```text
tunnel-client --version
tunnel-client profiles list --json
tunnel-client runtimes list --json
tunnel-client runtimes status <alias> --json
```

Runtime stop is wired through:

```text
tunnel-client runtimes stop <alias> --json
```

Profile metadata is read from the official profile path returned by `tunnel-client`. Start/restart are intentionally disabled until Credential Manager and the full `runtimes connect` reconstruction path are migrated.

## Build

Requirements: Visual Studio 2026 with Windows application development tooling, or the .NET 10 SDK on Windows.

```powershell
dotnet restore .\OpenAITunnelManager.slnx
dotnet build .\src\OpenAITunnelManager.App\OpenAITunnelManager.App.csproj -c Release -p:Platform=x64 -r win-x64
```

Set `TUNNEL_CLIENT_PATH` to point to `tunnel-client.exe`, or place `tunnel-client.exe` next to the application executable. A settings page will replace this temporary bootstrap mechanism later in the migration.
