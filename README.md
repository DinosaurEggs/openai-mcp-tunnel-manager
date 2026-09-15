# OpenAI MCP Tunnel Manager

A new C# / WinUI 3 implementation of the Windows desktop manager for `tunnel-client`.

This branch is a clean rewrite. It does not carry Python source, Python tests, Python packaging files, legacy build scripts, or legacy documentation from the previous implementation.

## Technology baseline

- C# 14
- .NET 10
- WinUI 3
- Windows App SDK 2.4
- CommunityToolkit.Mvvm
- Microsoft.Extensions.Hosting
- YamlDotNet

## Architecture

```text
OpenAITunnelManager.App
  -> OpenAITunnelManager.Core
  -> OpenAITunnelManager.Infrastructure
       -> tunnel-client.exe
```

`tunnel-client` is the source of truth for Profile, Runtime, Tunnel ID, MCP target, runtime state, health endpoints, and log paths. The GUI does not maintain a second copy of those objects.

## Current milestone

The first milestone implements the new Connections workspace and reads live data from:

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

Start/restart, Credential Manager, logs, diagnostics, settings, tray integration, autostart, and single-instance activation will be added as the rewrite progresses.

## Build

```powershell
dotnet restore .\OpenAITunnelManager.slnx
dotnet build .\src\OpenAITunnelManager.App\OpenAITunnelManager.App.csproj -c Release -p:Platform=x64 -r win-x64
```

During early development, point the application at `tunnel-client.exe` with `TUNNEL_CLIENT_PATH`, or place the executable next to the built application.
