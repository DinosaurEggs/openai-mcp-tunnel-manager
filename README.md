# OpenAI MCP Tunnel Manager

Windows desktop manager for `tunnel-client`, implemented with C# and WinUI 3.

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

`tunnel-client` is the source of truth for Profile, Runtime, Tunnel ID, MCP target, runtime state, health endpoints, and log paths. The application stores only its own local UI preferences and credential references.

## Current implementation

The Connections workspace reads live data from:

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

The application is being built around five workspaces: Overview, Connections, Logs, Diagnostics, and Settings.

## Build

```powershell
dotnet restore .\OpenAITunnelManager.slnx
dotnet build .\src\OpenAITunnelManager.App\OpenAITunnelManager.App.csproj -c Release -p:Platform=x64 -r win-x64
```

During development, set `TUNNEL_CLIENT_PATH` to the full path of `tunnel-client.exe`, or place the executable next to the application.
