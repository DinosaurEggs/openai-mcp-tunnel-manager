# Python 1.0.0 → WinUI 3 功能对照

此表用于验证 C# / WinUI 3 重写版的最终产品行为。`tunnel-client` 仍是 Profile / Runtime 唯一事实来源；Python 1.0.0 中已明确移除的功能不作为重写目标，也不保留隐藏 UI 或隐藏后端。

| Python 1.0.0 / 最终约束 | WinUI 3 实现 | 自动门禁 |
|---|---|---|
| `tunnel-client` 是 Profile / Runtime 唯一事实来源 | `TunnelClientService` / `TunnelClientOperations` | fake CLI 集成测试 |
| 外部 Profile / Runtime 变化手动刷新后同步 | 启动、手动刷新、CRUD、lifecycle 时读取 inventory | 集成测试 |
| 不恢复 inventory 周期轮询 | 无后台 inventory timer | 源码 + ViewModel |
| 不恢复 Runtime status 周期轮询 | 状态只在启动、刷新和显式操作时查询 | ViewModel + CI |
| Manager-owned 前台 Profile 异常退出可发现 | `Process.Exited` 事件 | process lifecycle 实现 |
| Runtime / Profile 同名仍是不同实体 | `runtime:<alias>` / `profile:<name>` | 单元 + 集成测试 |
| 一个 Profile 可被多个 Runtime alias 共用 | 不折叠多个 runtime | 集成测试 |
| custom Runtime Profile 与同名 listed Profile 分离 | Profile path 严格匹配后才合并 | 集成测试 |
| 设置 tunnel-client.exe | WinUI `FileOpenPicker` | explicit path 测试 |
| 显式错误路径不 fallback | `TunnelClientOptions.ResolveExecutablePath` | 行为测试 |
| Manager 配置不保存 Tunnel ID / target / API Key | schema 2 `config/settings.json` | 设置测试 |
| 旧 Python settings / tunnels[] 单向迁移 | 保留本机偏好、清除旧 Tunnel 定义 | 兼容测试 |
| 旧刷新间隔配置不继续保留 | 读取 `refreshIntervalMs` / `refresh_interval_ms` 后重写并移除 | 兼容测试 |
| 损坏 settings 保留 `.broken` | `JsonSettingsStore` | 兼容测试 |
| Manager 配置 / 日志 / 状态只写程序目录 | `<EXE>\config` / `<EXE>\logs` / `<EXE>\state` | AppDataPaths + smoke |
| 新建 Profile | `tunnel-client init`；高级文本修改时 `profiles add --from-file --force` | CLI 集成测试 |
| GUI 编辑 Profile | 共享 WinUI `ProfileEditorControl` + ContentDialog | Profile 文档测试 |
| 高级 YAML / JSON 保留 | `ProfileDocumentEditor` 只更新 common fields | 行为测试 |
| 未知未来 target 不被 common editor 破坏 | common target 禁用，高级文本仍可编辑 | 文档逻辑 |
| 保存走官方 validator | 临时文件 + `profiles add --force` | CLI 集成测试 |
| validator 失败不破坏原文件 | 官方操作失败后不覆盖 | CLI 集成测试 |
| 删除前再次核对 Profile path | `VerifyProfileEntryAsync` | CLI 集成测试 |
| 共享 Profile 删除保护 | 规范化 Profile path 判断 | ViewModel |
| Runtime start 重建官方参数 | `runtimes connect` | CLI 集成测试 |
| Profile-only 前台运行 | `tunnel-client run --profile` | CLI 集成测试 |
| Stop | Runtime stop / foreground process stop | CLI 集成测试 |
| Restart = Stop 成功后 Start | `RestartAsync` | 安全集成测试 |
| Stop 失败不得继续 Start | 异常立即中断 | connect-count 测试 |
| 删除正在运行配置先停止 | Runtime + foreground Profile 均先停止 | 安全集成测试 |
| Runtime remove | `runtimes rm` | CLI 集成测试 |
| Runtime API Key 不写配置 | Windows Credential Manager | Windows roundtrip |
| Runtime → Profile secret fallback | credential fallback 顺序 | ViewModel |
| enabled / auto-connect | `ProfilePreference` + 初始化启动 | ViewModel |
| auto-reconnect | 前台 Profile 退出事件 + 1/2/5/10/30/60 秒退避 | `RuntimeEvents` |
| 不为 auto-reconnect 恢复轮询 | 外部 Runtime 不后台探测 | ViewModel |
| 重连延迟后不使用旧对象 | 按 Identity 重新解析当前连接 | ViewModel |
| 手动 Stop 抑制 auto-reconnect | `_manualStopped` + CTS | ViewModel |
| 关闭 AutoReconnect / 删除 / 退出取消重连 | per-identity CTS + lifetime CTS | ViewModel |
| Start / Stop / Restart 按状态启用 | command gate | ViewModel |
| 所有短生命周期 CLI 统一进程管理 | `TunnelClientProcessRunner` | 安全集成测试 |
| caller cancellation 杀进程树 | `Kill(entireProcessTree: true)` | cancellation 测试 |
| command timeout 杀进程树 | 同一 runner timeout 路径 | timeout 测试 |
| capability 不随刷新重复探测 | path + LastWriteTimeUtc cache | 实现约束 |
| UTF-8 增量日志读取 | 流式 Decoder + byte offset | ANSI viewer |
| 日志大文件有界 | 约 30k 行 / 8 MiB 字符，初始 tail 约 2 MiB | parser / stress tests |
| 日志自动刷新保留 | 日志标签可见时 1 秒 `DispatcherQueueTimer` | WinUI shell |
| 日志切换不串数据 | 最多 8 个 identity cursor | ANSI viewer |
| ANSI 终端颜色 | 16 / 256 / TrueColor SGR | parser tests |
| 中文 / Unicode / Emoji | UTF-8 + WinUI font fallback | parser + UI |
| 日志搜索 / 等级过滤 | 纯文本过滤 + debounce | ANSI viewer |
| 自动换行 | 虚拟化行控件 wrapping | WinUI shell |
| 大量日志 UI 性能 | `ListView + ItemsStackPanel` 虚拟化 | WinUI shell |
| 日志页不显示文件路径 | 路径控件和布局行不存在 | WinUI XAML |
| 停止后保留最后日志路径 | `_lastLogPaths`，仅用于继续读取 / 打开文件位置 | ViewModel |
| 打开日志 / 配置位置 | Explorer `/select` | WinUI shell |
| 前台日志不逐行 reopen | 每个 Profile 持久 `StreamWriter` | 集成测试 |
| Manager 日志有界滚动 | app.log 约 5 MiB × 3 | 实现约束 |
| 基础 Health / Ready | runtime status / foreground health endpoints | Health tests |
| 详细 Health 仅使用声明 URL | `health_details_url` / `mcp_health_url` | Health tests |
| 非 loopback Health 拒绝 | URI loopback guard | Health tests |
| loopback 30x 不可跳远端 | `AllowAutoRedirect=false` | redirect 测试 |
| Health 响应有上限 | 流式读取，最大 1 MiB | oversized 测试 |
| `doctor --explain` | listed Profile / custom `--profile-file` | CLI 集成测试 |
| Windows 登录启动 | HKCU Run | Windows service |
| stale autostart 不误报 enabled | 注册值匹配当前 EXE | Windows service |
| 系统托盘 | H.NotifyIcon.WinUI | x64 startup smoke |
| 真正退出先等待清理 | await reconnect CTS + foreground shutdown | WinUI shell |
| Windows 单实例 | WinAppSDK AppInstance | CI single-instance smoke |
| EXE / 窗口 / 托盘统一图标 | AppIcon.ico / png | publish gate |
| 应用启动 / 异常日志 | `<EXE>\logs\app.log` | startup smoke |
| 不强制覆盖 tunnel-client Profile / State | 默认继承，仅显式 override | 行为测试 |
| 主导航只保留概览 / 连接 / 设置 | 日志、诊断嵌入连接页 TabView | x64 startup smoke |
| 连接详情标签 | 常规 / 日志 / 诊断 | WinUI XAML |
| 生命周期按钮固定在 Tab 上方 | 启动 / 停止 / 重启 / 编辑 Profile / 更多 | WinUI XAML |
| 启动连接后不跳页 | 生命周期操作不修改导航 / Tab | WinUI shell |
| 删除全局底部状态条 | StatusMessage 展示在概览 | WinUI XAML |
| 概览纯 Dashboard | 无连接实体列表 | WinUI XAML |
| 概览大布局按比例分配 | Wide 4×25%，Compact 2×2，Narrow 单列 | responsive controller |
| 连接大布局按比例分配 | Wide 30/70，Compact 9/16，Narrow 上下 30/70 | responsive controller |
| Compact 生命周期栏不浪费高度 | 仅 Narrow 纵向，Compact / Wide 横向 | responsive controller |
| 设置高级区域不折叠 | 静态 XAML 小标题 + 设置项 | WinUI XAML |
| 高级目录使用 FolderPicker | 只读路径 + 选择 / 清除 | WinUI shell |
| 保存设置在页头 | Narrow 自动换行布局 | responsive controller |
| Responsive 只有一套规则 | `MainWindow.Responsive.cs` | UI audit |
| Profile Import / Export 删除 | UI / ViewModel / operations / tests 无入口 | 源码检查 |
| 复制可见日志 / 导出日志删除 | UI / handlers / backend 无入口 | 源码检查 |
| 旧字符串日志链路删除 | 无 `RawLog / VisibleLog`、无旧 Logs ViewModel 文件 | 源码检查 |
| Runtime polling 残留命名删除 | 事件生命周期为 `ConnectionsViewModel.RuntimeEvents.cs` | 源码检查 |
| 无 Python / WinForms / WPF | 纯 C# + WinUI 3 | CI 静态 + Artifact |

## 发布门禁

最终 Artifact 只有以下阶段全部成功后才上传：

1. 纯 WinUI / 无 Python 源码检查；
2. xUnit 行为、兼容与安全测试；
3. fake tunnel-client 真实子进程集成测试；
4. cancellation / timeout、Health 安全、程序目录存储测试；
5. Windows Credential Manager 集成测试；
6. x64 / ARM64 self-contained publish；
7. EXE、PRI、Microsoft.UI.Xaml 和图标完整性检查；
8. 发布目录不得包含 WinForms / WPF Runtime DLL 或 Python 残留；
9. x64 实际启动 smoke test；
10. 第二实例重定向并退出 smoke test。
