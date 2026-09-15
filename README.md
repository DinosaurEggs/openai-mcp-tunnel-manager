# OpenAI MCP Tunnel Manager

Windows 上的 `tunnel-client` 可视化管理器。此分支是完整的 C# / WinUI 3 重写版，不包含 Python、WinForms 或 WPF UI 组件。

## 技术基线

- C# 14
- .NET 10
- WinUI 3 / Windows App SDK 2.4
- CommunityToolkit.Mvvm
- Microsoft.Extensions.Hosting
- H.NotifyIcon.WinUI
- YamlDotNet
- xUnit v3 / Microsoft Testing Platform

应用目标框架为 `net10.0-windows10.0.19041.0`，发布为 unpackaged、self-contained 的 `win-x64` / `win-arm64`。

## 数据来源与刷新模型

`tunnel-client` 始终是以下数据的唯一事实来源：

- Profile
- Runtime alias
- Tunnel ID
- MCP URL / command
- Runtime 状态
- Health URL
- Runtime 日志路径

静态 inventory 只在应用启动、用户手动刷新以及显式 CRUD / lifecycle 操作后读取：

```text
tunnel-client profiles list --json
tunnel-client runtimes list --json
```

运行期间由轻量 `RuntimeMonitor` 每 5 秒只查询已知连接状态：

```text
tunnel-client runtimes status <alias> --json
```

因此不会恢复旧版对完整 inventory 的周期轮询。命令行侧新增、删除或修改 Profile / Runtime 后，点击 GUI 的“刷新”即可同步；已知 Runtime 的运行状态与自动重连则由 RuntimeMonitor 独立维护。

`tunnel-client --version` 和 capability probe 按可执行文件完整路径与最后修改时间缓存，不随每次刷新重复执行。

GUI 不保存第二套 Tunnel 配置。

## Manager 数据目录

Manager 自身的配置、日志和状态文件**固定保存在 `OpenAITunnelManager.exe` 所在目录**，不会写入 `%LOCALAPPDATA%`、`%APPDATA%` 或其他用户目录，也不会在目录不可写时自动回退到其他位置。

```text
OpenAITunnelManager.exe
├─ config\
│  └─ settings.json
├─ logs\
│  ├─ app.log
│  ├─ app.log.1
│  ├─ app.log.2
│  └─ app.log.3
└─ state\
   ├─ foreground\
   └─ temp\
```

因此发布目录必须可写；如果程序所在目录没有写权限，目录初始化会失败，而不是把数据静默迁移到别处。

`config/settings.json` 只保存 Manager 本机偏好：

- `tunnel-client.exe` 路径
- 关闭到托盘
- Windows 登录启动
- Profile / Runtime 的 enabled、auto-connect、auto-reconnect
- 可选的 Profile / State 目录覆盖

旧 settings 中的 `refreshIntervalMs` 仍可读取以保持兼容，但当前 UI 不再提供 inventory 自动刷新间隔设置，也不依赖该字段驱动 RuntimeMonitor。

Runtime API Key 明文只进入 Windows Credential Manager，不写入 `settings.json`、Profile 或应用日志。

默认情况下应用**不会覆盖** `TUNNEL_CLIENT_PROFILE_DIR` / `TUNNEL_CLIENT_STATE_DIR`，会继承启动环境并让 `tunnel-client` 自己决定官方数据目录。只有用户在设置页显式填写目录覆盖时才传递覆盖值。

## 首次运行

首次启动进入“设置”，使用 WinUI 文件选择器选择正在使用的 `tunnel-client.exe`。保存后立即验证版本和能力并刷新官方 Profile / Runtime。

显式配置的路径不存在时不会偷偷 fallback 到其他 `tunnel-client`。

## 主要功能

- 读取现有 Profile / Runtime
- Profile 与 Runtime alias 使用独立实体 Identity，同名也不会错误合并
- 新建 Profile
- GUI 内编辑 Profile，不打开外部文本编辑器
- 常用字段编辑后保留高级 YAML / JSON 内容
- 未识别的未来 MCP target 类型仍允许在高级编辑器修改，不会被强制转换成 HTTP target
- 保存 Profile 时调用 `tunnel-client profiles add ... --force` 做官方校验
- 删除 Runtime / Profile，含共享 Profile 路径保护和路径二次核验
- Profile-only 前台运行
- Runtime / Profile 启动、停止、重启
- Stop 失败时 Restart 不继续 Start
- enabled / auto-connect / auto-reconnect
- 轻量 RuntimeMonitor + 1/2/5/10/30/60 秒自动重连退避
- Runtime Ready 稳定 20 秒后才重置重连退避
- 手动 Stop / 关闭自动重连 / 删除配置 / 应用退出都会取消待执行的重连任务
- Runtime API Key 使用 Windows Credential Manager，并支持 Runtime → Profile 凭据继承
- `doctor --explain`
- Health / Ready 与新旧 Runtime 兼容
- 日志受限尾读、增量刷新、搜索、等级过滤、自动换行
- 日志垂直自动跟随时保留用户水平滚动位置
- 停止后保留最后已知日志路径
- 打开配置文件 / 日志文件所在位置
- 纯 WinUI 系统托盘：打开 / 退出
- 关闭到系统托盘
- Windows 登录启动，并检测指向旧 EXE 的失效启动项
- Windows App SDK `AppInstance` 单实例；再次启动恢复并置前已有窗口
- EXE、窗口和托盘统一应用图标

以下旧功能已按 Python 1.0.0 最终产品范围删除，不再保留隐藏后端：

- Profile 导入 / 导出
- 复制可见日志
- 导出日志
- 完整 inventory 周期轮询及其可配置刷新间隔 UI

## Profile 编辑

编辑全部在 WinUI 对话框内完成，包括完整高级 Profile 文本。保存流程：

```text
WinUI 编辑
  -> <程序目录>\state\temp 临时 Profile
  -> tunnel-client profiles add <name> --from-file <temp> --force
  -> 官方校验成功
  -> 刷新 inventory
```

若校验失败，官方 Profile 保持原样。

## Runtime 与纯 Profile 启动

已有 Runtime alias 时，根据官方 Runtime/Profile 数据重建 `runtimes connect` 参数，包括 Alias、Tunnel ID、Profile、Profile 目录、MCP target 与 API key ref。

只有 Profile、没有 Runtime alias 时，Manager 使用：

```text
tunnel-client run --profile <name>
```

前台子进程信息和运行日志仅作为 Manager 本次进程的运行状态管理，不成为新的 Tunnel 配置事实来源。前台 stdout/stderr 使用持久 `StreamWriter` 写入，不再每行重新打开日志文件。

所有短生命周期 `tunnel-client` 命令统一通过一个 process runner 执行；调用方取消、命令超时和应用退出时都会终止整个子进程树。真正退出应用前会等待 RuntimeMonitor 与 Manager 拥有的前台 Profile 完成清理。

## 自动重连

RuntimeMonitor 每 5 秒检查已知连接状态，最多并发 4 个状态查询。需要重连时使用：

```text
1s -> 2s -> 5s -> 10s -> 30s -> 60s -> 60s ...
```

延迟结束后重新按 connection Identity 获取当前对象，不使用旧快照。`connect` 命令成功只表示重连命令已提交；必须由后续监控确认 Runtime `Ready`，并持续稳定 20 秒后才重置退避计数。

## 日志

Runtime 使用 `runtimes status --json` 返回的官方 `log_path`。纯 Profile 使用 Manager 前台进程日志。

- 大文件只读取受限尾部
- 后续刷新按文件 offset 增量读取
- 大量突发追加时重新 tail，避免一次性分配巨大字符串
- 每个连接独立缓存，不串日志
- 支持 `DEBUG / INFO / WARN / ERROR` 过滤和文本搜索
- 支持自动换行
- 自动跟随只改变纵向位置，横向滚动由用户控制
- 可直接打开当前日志文件所在位置
- Manager 自身 `app.log` 按约 5 MiB × 3 份备份滚动

## Health

基础运行状态采用 `tunnel-client` 官方状态中的 Health / Ready 结果。详细接口只有 Runtime 明确返回以下字段时才请求：

```text
health_details_url
mcp_health_url
```

未声明时显示“未声明详细健康接口”，不会对旧 Runtime 猜测新路由。

所有 Manager 主动发出的 Health 请求：

- 只允许 HTTP(S) loopback 地址；
- 禁止自动 HTTP 重定向，避免 loopback 通过 30x 跳转到远端；
- 使用流式读取；
- 单个详细 Health 响应最大 1 MiB；
- 默认 3 秒超时。

## 单实例与托盘

单实例使用 Windows App SDK `AppInstance.FindOrRegisterForKey` / `RedirectActivationToAsync`。

```text
第一次启动
  -> 创建 WinUI 窗口和托盘

再次启动
  -> 重定向激活到第一实例
  -> 已有窗口从托盘恢复并置前
  -> 第二进程退出
```

托盘使用 WinUI 专用实现，不依赖 WinForms/WPF。选择“退出”或在关闭到托盘关闭时真正退出程序，会先等待 Manager 后台任务和前台子进程清理完成。

## 响应式 UI

主界面使用一套具名 WinUI 布局控制器，不再通过 VisualTree 顺序猜测页面结构，也不存在第二套 competing responsive 逻辑。

```text
Narrow   < 650 epx
Compact  650–999 epx
Wide     >= 1000 epx
```

连接页在窄窗口纵向堆叠列表 / 详情；日志筛选栏和诊断面板随断点重排。连接主操作只保留“启动 / 停止 / 重启 / 编辑”，本机偏好、打开配置位置和删除进入“更多”。

## 构建

Windows：

```powershell
dotnet restore .\OpenAITunnelManager.slnx
dotnet test .\tests\OpenAITunnelManager.Tests\OpenAITunnelManager.Tests.csproj -c Release
dotnet publish .\src\OpenAITunnelManager.App\OpenAITunnelManager.App.csproj `
  -c Release `
  -p:Platform=x64 `
  -r win-x64 `
  --self-contained true
```

GitHub Actions 会执行：

```text
纯 WinUI / 无 Python 源码检查
-> C# 行为测试
-> C# fake tunnel-client 真实进程集成测试
-> Windows Credential Manager 测试
-> x64 / ARM64 publish
-> PRI / WinUI Runtime / 图标完整性检查
-> 发布目录 WinForms / WPF / Python 残留检查
-> x64 实际启动 smoke test
-> 单实例重定向 smoke test
-> 上传 Artifact
```

任何一步失败都不会上传对应的最终构建产物。

详细功能对照见 `docs/FEATURE_PARITY.md`。
