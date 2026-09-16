# OpenAI MCP Tunnel Manager v1.1.1

## 版本说明

1.1.1 是 C# / WinUI 3 主线的维护版本，重点精简发布包中的本地化资源，仅保留简体中文和英文。

## 主要变更

- .NET satellite resource 仅保留英文与简体中文。
- WinUI 3 原生 MUI 资源仅保留英文（`en-*`）与简体中文（`zh-CN`）。
- CI 新增发布后语言资源裁剪与校验，防止其他语言目录重新进入正式产物。
- x64 / ARM64 self-contained 发布均继续通过运行时文件完整性检查。
- x64 继续通过启动 smoke test 与单实例 activation smoke test。
- 不改变 Profile、Runtime、日志、诊断、托盘等现有业务功能。

## 发布产物

- `OpenAITunnelManager-win-x64.zip`
- `OpenAITunnelManager-win-arm64.zip`
- GitHub Package：`OpenAITunnelManager.Windows 1.1.1`
