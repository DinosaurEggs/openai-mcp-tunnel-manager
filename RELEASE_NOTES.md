# OpenAI MCP Tunnel Manager v1.0.0

首个正式版本。

## 主要内容

- 增加 Windows 单实例模式：再次启动 EXE 时不创建第二个窗口，而是恢复并置前已运行实例。
- 保留常驻系统托盘，支持从托盘恢复和退出。
- 配置列表简化为 `[已启动/已停止] 配置名称`，支持右键编辑和删除。
- Profile 编辑继续通过 tunnel-client 官方校验提交。
- 优化日志查看性能、自动换行、滚动位置保持和打开日志位置。
- 增加打开配置文件所在位置。
- 修复旧 Runtime 不支持详细 Health 路由时显示 HTTP 404 的兼容问题。
- 主窗口、任务栏、托盘和 EXE 使用统一应用图标。
- EXE Windows 文件版本统一为 1.0.0。
- 增加 GitHub Packages NuGet 包 `OpenAITunnelManager.Windows`。

## Release 资产

- `OpenAITunnelManager.exe`：Windows 单文件 GUI 程序。
- GitHub Packages：`OpenAITunnelManager.Windows` 1.0.0，包内包含同版本 EXE。

发布流水线只有在 Windows `Verify` 和 PyInstaller 打包全部成功后才会执行 Packages 和 Release 发布。
