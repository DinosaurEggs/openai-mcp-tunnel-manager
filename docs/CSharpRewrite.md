# C# / WinUI 3 architecture

OpenAI MCP Tunnel Manager 1.2.0 使用纯 C# / WinUI 3 实现。

## UI 约束

应用 UI 只允许：

- WinUI 3
- Windows App SDK
- H.NotifyIcon.WinUI（系统托盘）
- 必要的 Win32 / WinRT API

不允许引入 WinForms、WPF、WindowsFormsHost/Integration、PresentationFramework/PresentationCore 或 Python UI/运行时。

GitHub Actions 在测试前执行静态门禁，发现这些依赖或 Python 源码/配置会直接失败。

## 工程分层

```text
OpenAITunnelManager.App
  WinUI Shell / Views / ViewModels
        |
        v
OpenAITunnelManager.Core
  Models / abstractions
        |
        v
OpenAITunnelManager.Infrastructure
  tunnel-client process adapter
  settings
  Windows Credential Manager
  Windows autostart
```

测试工程：

```text
OpenAITunnelManager.Tests
  -> unit / behavior / Windows integration tests

OpenAITunnelManager.FakeTunnelClient
  -> pure C# executable used to verify the real child-process CLI boundary
```

Fake CLI 只属于测试，不进入发布产物。

## Source of truth

`tunnel-client` 是 Profile / Runtime / Tunnel ID / MCP target / Health URL / runtime log path / runtime state 的唯一事实来源。

Manager 的 `config/settings.json` 只保存 UI/本机偏好。API Key 使用 Windows Credential Manager。

Manager 默认继承当前进程的 `TUNNEL_CLIENT_PROFILE_DIR` / `TUNNEL_CLIENT_STATE_DIR`，不会强制把 tunnel-client 官方数据重定向到 Manager 目录。设置页中的目录覆盖为显式 opt-in。

## Manager 本地目录

```text
<app>\config\settings.json
<app>\logs\app.log
<app>\state\foreground\...
<app>\state\temp\...
```

应用日志在 WinUI Application/Window 创建之前就开始记录，因此 XAML、DI、窗口构造和首次 inventory 刷新失败都可诊断。

## tunnel-client 可执行文件管理

设置模型区分两种来源：

- `Managed`：Manager 按架构下载 OpenAI 官方 Release，校验 SHA-256、解压到版本目录并执行 `--version` 后再切换；
- `Custom`：用户选择本地 EXE，Manager 先自动执行 `--version`，验证成功后才保存路径。

托管更新只由用户在设置页点击“下载 / 更新”触发，不在应用启动时自动联网。下载过程向 WinUI 暴露检查、下载、校验、安装阶段以及字节进度；下载阶段可取消。

## CLI adapter

Inventory：

```text
profiles list --json
runtimes list --json
runtimes status <alias> --json
```

Profile：

```text
init ...
profiles add <name> --from-file <temp> --force
```

Runtime：

```text
runtimes connect ... --json
runtimes stop <alias> --json
runtimes rm <alias> --json
```

Profile-only：

```text
run --profile <name> ...
```

诊断：

```text
doctor --profile <name> --explain
# 或 custom runtime profile
doctor --profile-file <path> --explain
```

## 发布

应用采用 unpackaged + self-contained Windows App SDK 发布。`Directory.Build.targets` 明确保证应用自己的 `.pri` 进入 publish 输出，避免 WinUI ResourceDictionary 在启动前发生 `0xC000027B`。

CI 同时发布 `win-x64` / `win-arm64`；x64 还必须通过真实进程启动和第二实例重定向 smoke test，之后才上传 Artifact。
