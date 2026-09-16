# OpenAI MCP Tunnel Manager v1.1.0

## 版本说明

1.1.0 将 C# / WinUI 3 实现作为 OpenAI MCP Tunnel Manager 的正式主线版本。该实现独立开发，并直接替代此前的 Python 主程序。

## 主要内容

- 使用 C# 14、.NET 10、WinUI 3 / Windows App SDK 构建 Windows 桌面应用。
- 提供 x64 与 ARM64 self-contained 发布产物。
- 支持 Profile / Runtime 的读取、创建、编辑、删除、启动、停止与重启。
- 支持 enabled、auto-connect、auto-reconnect 与前台 Profile 退出事件重连。
- Runtime API Key 使用 Windows Credential Manager 保存。
- 提供高性能 ANSI 日志查看器，支持 UTF-8、Unicode / Emoji、16/256/TrueColor、搜索、等级过滤、自动刷新与自动换行。
- 提供 Doctor / Health 诊断，并限制 Health 请求为 loopback HTTP(S)。
- 支持系统托盘、Windows 登录启动与 Windows App SDK 单实例激活。
- 使用 Narrow / Compact / Wide 三档响应式布局。
- 发布源码与产物均不包含 Python、WinForms 或 WPF 运行时组件。

## CI 门禁

正式构建必须通过：

- 纯 WinUI / 无 Python 源码检查；
- 行为、兼容、安全与 CLI 集成测试；
- x64 / ARM64 self-contained publish；
- 发布文件完整性检查；
- x64 启动 smoke test；
- 单实例 activation smoke test。
