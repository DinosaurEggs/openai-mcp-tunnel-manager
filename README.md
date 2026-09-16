# OpenAI MCP Tunnel Manager

Windows 上的 `tunnel-client` 可视化管理器。`rewrite/csharp-winui3` 是完整的 C# / WinUI 3 重写版，不包含 Python、WinForms 或 WPF UI 组件。

## 技术基线

- C# 14 / .NET 10
- WinUI 3 / Windows App SDK 2.4
- CommunityToolkit.Mvvm
- Microsoft.Extensions.Hosting
- H.NotifyIcon.WinUI
- YamlDotNet
- xUnit v3 / Microsoft Testing Platform

目标框架为 `net10.0-windows10.0.19041.0`，发布为 unpackaged、self-contained 的 `win-x64` / `win-arm64`。

## 数据来源与刷新模型

`tunnel-client` 始终是 Profile、Runtime alias、Tunnel ID、MCP target、Runtime 状态、Health URL 和 Runtime 日志路径的唯一事实来源。GUI 不保存第二套 Tunnel 定义。

完整 inventory 只在以下时机读取：

- 应用启动；
- 用户点击“刷新”；
- 新建、编辑、删除 Profile / Runtime 后；
- 启动、停止、重启、自动重连等显式 lifecycle 操作后。

```text
tunnel-client profiles list --json
tunnel-client runtimes list --json
```

**没有 Runtime 状态后台周期轮询，也没有 inventory 自动刷新。** 外部命令行修改 Profile / Runtime 后，点击 GUI 的“刷新”即可同步。

Manager 自己启动并持有的 Profile 前台进程通过 `Process.Exited` 事件发现异常退出，并据此触发自动重连，不依赖定时 status 查询。外部 Runtime 不做后台探测；其状态在启动、手动刷新和显式操作时同步。

唯一保留的 UI 周期刷新是“日志 → 自动刷新”：日志标签可见且开关启用时，每 1 秒增量读取当前日志文件。

`tunnel-client --version` 和 capability probe 按可执行文件完整路径与最后修改时间缓存，不随每次刷新重复执行。

## Manager 数据目录

Manager 自身配置、日志和状态文件固定保存在 `OpenAITunnelManager.exe` 所在目录，不写 `%LOCALAPPDATA%` / `%APPDATA%`，也不会在目录不可写时自动回退。

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

因此发布目录必须可写。

`config/settings.json` 只保存 Manager 本机偏好：

- `tunnel-client.exe` 路径；
- 关闭到托盘；
- Windows 登录启动；
- Profile / Runtime 的 enabled、auto-connect、auto-reconnect；
- 可选的 Profile / State 目录覆盖。

历史 settings 中的 `refreshIntervalMs` / `refresh_interval_ms` 仅用于兼容迁移；读取旧配置后会自动重写并移除此字段。

Runtime API Key 明文只进入 Windows Credential Manager，不写入 `settings.json`、Profile 或应用日志。

默认不会覆盖 `TUNNEL_CLIENT_PROFILE_DIR` / `TUNNEL_CLIENT_STATE_DIR`。只有用户在高级设置中主动选择目录时，才向 `tunnel-client` 子进程传递覆盖值。

## 界面结构

左侧主导航：

```text
概览
连接
设置
```

日志和诊断不再是独立主页面，而是当前连接详情内的标签：

```text
连接
├─ 常规
├─ 日志
└─ 诊断
```

启动连接后不会自动跳转到日志或其他页面；用户当前所在页面和标签保持不变。

概览包含当前状态信息、统计卡片和连接摘要。旧的全局底部状态条已删除。Wide 模式下概览内容限制最大可读宽度，Compact / Narrow 模式按断点重排，避免窗口放大后卡片与列表被过度拉伸。

设置页分为“常规设置”和“高级设置”。高级设置始终显示，不使用 Expander；Profile / State 目录为只读路径框，并通过 WinUI `FolderPicker` 选择或清除。保存设置按钮位于页面右上角，不显示 settings.json 文件路径。

## 主要功能

- 读取现有 Profile / Runtime；
- Profile / Runtime alias 独立 Identity，同名不会错误合并；
- 新建和 GUI 内编辑 Profile；
- 常用字段编辑后保留高级 YAML / JSON；
- 未识别的未来 MCP target 仍可在高级原始文本中编辑；
- 保存 Profile 使用 `profiles add ... --force` 做官方校验；
- 删除 Runtime / Profile，含共享 Profile 保护和路径二次核验；
- Profile-only 前台运行；
- Runtime / Profile 启动、停止、重启；
- enabled / auto-connect / auto-reconnect；
- 前台 Profile 退出事件 + 1/2/5/10/30/60 秒自动重连退避；
- 手动 Stop、关闭自动重连、删除配置、应用退出都会取消待执行重连；
- Runtime API Key 使用 Windows Credential Manager，并支持 Runtime → Profile 凭据查找；
- `doctor --explain`；
- Health / Ready 与新旧 Runtime 兼容；
- 日志受限尾读、增量刷新、搜索、等级过滤、自动刷新、自动换行；
- 停止后保留最后已知日志路径；
- 打开配置 / 日志文件所在位置；
- 纯 WinUI 系统托盘；
- Windows 登录启动；
- Windows App SDK `AppInstance` 单实例。

以下功能已从最终产品删除，不保留隐藏后端：

- Profile Import / Export；
- 复制可见日志；
- 导出日志；
- inventory 周期轮询；
- Runtime status 周期轮询；
- 可配置刷新间隔字段 / UI。

## Profile 编辑

编辑全部在 WinUI 对话框内完成。保存流程：

```text
WinUI 编辑
  -> <程序目录>\state\temp 临时 Profile
  -> tunnel-client profiles add <name> --from-file <temp> --force
  -> 官方校验成功
  -> 刷新 inventory
```

校验失败时不会覆盖原 Profile。

## Runtime 与 Profile-only 启动

已有 Runtime alias 时，根据官方 Runtime / Profile 数据重建 `runtimes connect` 参数，包括 Alias、Tunnel ID、Profile、Profile 目录、MCP target 与 API key ref。

只有 Profile、没有 Runtime alias 时，Manager 使用：

```text
tunnel-client run --profile <name>
```

Manager 持有该前台子进程及其 stdout/stderr 日志 writer。异常退出通过 `Process.Exited` 立即通知 ViewModel；手动停止时先把进程从受监控集合移除，因此不会误触发自动重连。

所有短生命周期 `tunnel-client` 命令统一通过一个 process runner 执行；调用方取消或命令超时时终止整个子进程树。

## 自动重连

自动重连只针对 Manager 能够事件化发现的 Profile 前台进程异常退出，以及显式操作后确认的停止状态。退避序列：

```text
1s -> 2s -> 5s -> 10s -> 30s -> 60s -> 60s ...
```

延迟结束后重新按 connection Identity 获取当前对象，避免使用旧快照。没有为了自动重连而恢复后台 Runtime status 轮询。

## 日志

Runtime 使用 `runtimes status --json` 返回的官方 `log_path`；纯 Profile 使用 Manager 前台进程日志。

- 大文件只读取受限尾部；
- 后续刷新按文件 offset 增量读取；
- 大量突发追加时重新 tail；
- 每个连接独立缓存；
- 支持 `DEBUG / INFO / WARN / ERROR` 和文本搜索；
- 支持自动刷新开关，默认每 1 秒增量读取；
- 支持自动换行；
- 自动跟随仅改变纵向位置，保留水平滚动位置；
- Manager 自身 `app.log` 按约 5 MiB × 3 份备份滚动。

## Health

基础运行状态采用官方 Runtime status 或 Manager 持有的前台 Profile Health / Ready。详细接口只有 Runtime 明确返回 `health_details_url` / `mcp_health_url` 时才请求。

主动 Health 请求：

- 只允许 HTTP(S) loopback；
- 禁止自动重定向；
- 使用流式读取；
- 单个响应最大 1 MiB；
- 默认 3 秒超时。

## 响应式 UI

只有一套具名 WinUI 布局控制器：

```text
Narrow   < 650 epx
Compact  650–999 epx
Wide     >= 1000 epx
```

- 概览：Narrow 单列、Compact 双列、Wide 三列并限制最大内容宽度；
- 连接：Wide / Compact 双栏，Narrow 为列表 + 详情纵向布局；
- 日志标签：筛选控件随断点重排；
- 诊断标签：Wide 双栏，Compact / Narrow 纵向；
- 设置：长路径使用 Star 列 + `MinWidth=0`，不会撑宽窗口。

## 构建与 CI

```powershell
dotnet restore .\OpenAITunnelManager.slnx
dotnet test .\tests\OpenAITunnelManager.Tests\OpenAITunnelManager.Tests.csproj -c Release
dotnet publish .\src\OpenAITunnelManager.App\OpenAITunnelManager.App.csproj `
  -c Release `
  -p:Platform=x64 `
  -r win-x64 `
  --self-contained true
```

GitHub Actions 门禁：

```text
纯 WinUI / 无 Python 源码检查
-> 行为、兼容和安全测试
-> fake tunnel-client 真实子进程集成测试
-> Windows Credential Manager 测试
-> x64 / ARM64 publish
-> 发布文件与 Runtime 检查
-> x64 实际启动 smoke test
-> 单实例重定向 smoke test
-> Artifact
```

详细功能对照见 `docs/FEATURE_PARITY.md`，UI 验收规则见 `docs/UI_AUDIT.md`。
