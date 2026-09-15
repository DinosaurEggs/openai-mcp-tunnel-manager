# Python 1.0.0 → WinUI 3 功能对照

此表用于验证 C# / WinUI 3 重写版的行为等价。UI 信息架构可以不同，但数据来源、操作结果和关键边界必须保持一致。

| Python 1.0.0 行为 | WinUI 3 实现 | 自动门禁 |
|---|---|---|
| `tunnel-client` 是 Profile/Runtime 唯一事实来源 | `TunnelClientService` / `TunnelClientOperations` | C# fake CLI 集成测试 |
| 外部 Profile/Runtime 变化刷新后同步 | 每次 inventory 重新读取官方 CLI | 集成测试 |
| Runtime/Profile 同名仍是不同实体 | `runtime:<alias>` / `profile:<name>` | 单元 + 集成测试 |
| 一个 Profile 可被多个 Runtime alias 共用 | 不折叠多个 runtime | 集成测试 |
| custom Runtime Profile 与同名 listed Profile 分离 | Profile path 严格匹配后才合并 | 集成测试 |
| 设置 tunnel-client.exe | WinUI FileOpenPicker + `settings.json` | missing/explicit path 测试 |
| 显式错误路径不 fallback | `TunnelClientOptions.ResolveExecutablePath` | 行为测试 |
| Manager 配置不保存 Tunnel ID/MCP target/API Key | schema 2 `config/settings.json` | 设置测试 |
| 旧 Python settings/tunnels[] 单向迁移 | 保留本机偏好、清除旧 Tunnel 定义 | 兼容测试 |
| 损坏 settings 保留 `.broken` | `JsonSettingsStore` | 兼容测试 |
| 新建 Profile | `tunnel-client init` | CLI 集成测试 |
| GUI 编辑 Profile | WinUI ContentDialog | Profile 文档测试 |
| 高级 YAML/JSON 保留 | `ProfileDocumentEditor` 只更新 common fields | 行为测试 |
| 保存走官方 validator | 临时文件 + `profiles add --force` | CLI 集成测试 |
| validator 失败不破坏原文件 | 官方操作失败后不覆盖 | CLI 集成测试 |
| 删除前再次核对 Profile path | `VerifyProfileEntryAsync` | CLI 集成测试 |
| 删除 symlink 不解析 target | `File.Delete` 官方 entry path | 实现约束 |
| Runtime start/reconnect 重建官方参数 | `runtimes connect` | CLI 集成测试 |
| Profile-only 前台运行 | `tunnel-client run --profile` | CLI 集成测试 |
| Stop | Runtime stop / foreground process stop | CLI 集成测试 |
| Restart = Stop 成功后 Start | `RestartAsync` | 安全集成测试 |
| Stop 失败不得继续 Start | 异常立即中断 | connect-count 安全测试 |
| 删除正在运行的配置先停止 | Runtime + foreground Profile 均先停止 | 安全集成测试 |
| Runtime remove | `runtimes rm` | CLI 集成测试 |
| 共享 Profile 删除保护 | 其他 Runtime 仍使用时不删 Profile/共享 credential | ViewModel 逻辑 + 集成边界 |
| Runtime API Key 不写配置 | Windows Credential Manager | Windows native roundtrip |
| Runtime → Profile secret fallback | Credential ID fallback 顺序 | 行为逻辑 |
| enabled | ProfilePreference | ViewModel |
| auto-connect | 初始化后启动 enabled 配置 | ViewModel |
| auto-reconnect | 1/2/5/10/30/60 秒退避 | ViewModel |
| 手动 Stop 抑制 auto-reconnect | `_manualStopped` | ViewModel |
| Start/Stop/Restart 按状态启用 | `CanStart/CanStop/CanRestart` | ViewModel command gate |
| 手动 Start 打开日志页 | Start command + WinUI page switch | WinUI shell |
| bounded log tail | 最大字节/行限制 | 行为测试 |
| 增量日志刷新 | per-connection offset cursor | ViewModel log service |
| 日志切换不串数据 | per-identity cache | ViewModel log service |
| 日志搜索 | case-insensitive filter | ViewModel |
| DEBUG/INFO/WARN/ERROR 过滤 | level filter | ViewModel |
| 自动换行 | WinUI TextBox wrapping | WinUI shell |
| 自动跟随仅影响垂直位置 | 保存/恢复 HorizontalOffset | WinUI shell |
| 停止后保留最后日志路径 | `_lastLogPaths` | ViewModel |
| 打开日志位置 | Explorer `/select` | WinUI shell |
| 打开配置位置 | Explorer `/select` | WinUI shell |
| 基础 Health/Ready | 官方 runtime status / foreground `/healthz`,`/readyz` | Health tests |
| 详细 Health 仅使用声明 URL | `health_details_url` / `mcp_health_url` | Health tests |
| 非 loopback Health 拒绝 | URI loopback guard | Health tests |
| `doctor --explain` | listed Profile / custom `--profile-file` | CLI 集成测试 |
| Windows 登录启动 | HKCU Run | Windows service |
| 系统托盘始终存在 | H.NotifyIcon.WinUI | x64 startup smoke + shell |
| 托盘打开/退出 | WinUI MenuFlyout command | WinUI shell |
| 关闭到托盘 | AppWindow Closing + native hide | WinUI shell |
| Windows 单实例 | WinAppSDK AppInstance | CI secondary-instance smoke |
| 第二实例恢复已有窗口 | activation redirect + foreground | CI smoke |
| EXE/窗口/托盘统一图标 | AppIcon.ico/png | publish file gate |
| 应用启动/异常日志 | `<program>\logs\app.log` | startup smoke |
| Manager 本机配置/日志不写 AppData | `AppContext.BaseDirectory` | settings/log implementation |
| 不强制覆盖 tunnel-client Profile/State 目录 | 默认继承环境，仅显式 override | 行为测试 |
| 无 Python / WinForms / WPF | 纯 C# + WinUI 3 | CI 静态硬门禁 |

## 发布门禁

最终 Artifact 只有以下阶段全部成功后才上传：

1. 纯 WinUI / 无 Python 源码检查；
2. xUnit 行为与兼容测试；
3. C# fake tunnel-client 真实子进程集成测试；
4. Windows Credential Manager 集成测试；
5. x64 / ARM64 self-contained publish；
6. EXE、PRI、Microsoft.UI.Xaml 和图标完整性检查；
7. x64 实际启动 5 秒 smoke test；
8. 第二实例重定向并退出 smoke test。
