# OpenAI MCP Tunnel Manager

Windows 上的 `tunnel-client` 可视化管理器，使用 C# / WinUI 3 开发，不包含 Python、WinForms 或 WPF UI 组件。

当前版本：**1.2.0**

## 技术基线

- C# 14 / .NET 10
- WinUI 3 / Windows App SDK 2.4
- CommunityToolkit.Mvvm
- Microsoft.Extensions.Hosting
- H.NotifyIcon.WinUI
- WinUIEdit / Scintilla
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

**没有 Runtime 状态后台周期轮询，也没有 inventory 自动刷新。** 外部命令行修改 Profile / Runtime 后，点击 GUI 的“刷新”同步。

Manager 自己启动并持有的 Profile 前台进程通过 `Process.Exited` 事件发现退出并触发自动重连。外部 Runtime 不做后台探测；其状态只在启动、手动刷新和显式操作时同步。

唯一保留的周期刷新是日志 tail：日志标签当前可见时，每 1 秒增量读取当前日志文件。暂停输出只暂停控制台渲染，不停止文件读取。

`tunnel-client --version` 和 capability probe 按可执行文件完整路径与最后修改时间缓存，不随每次刷新重复执行。

## Manager 数据目录

Manager 自身配置、日志和状态文件固定保存在 `OpenAITunnelManager.exe` 所在目录，不写 `%LOCALAPPDATA%` / `%APPDATA%`，目录不可写时也不会自动回退。

```text
OpenAITunnelManager.exe
├─ tunnel-client\
│  └─ versions\
│     └─ vX.Y.Z\
│        └─ tunnel-client.exe
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
      └─ tunnel-client-update\
```

因此发布目录必须可写。

首次启动时，Manager 会让用户选择“托管版本”或“自定义版本”。选择托管版本并点击下载后会先进入设置页，再显示下载进度；选择自定义版本后会自动执行 `tunnel-client.exe --version` 验证，验证失败不会保存错误路径。

托管模式只在用户点击“下载”或“更新”时访问 GitHub，不在应用启动时自动检查新版本。Manager 会按当前架构精确选择官方 `windows-amd64` / `windows-arm64` Release 资产，校验 Release digest 与 `SHA256SUMS.txt`，并在新版本通过 `--version` 验证后才切换。更新失败时继续使用当前已安装版本。

托管版本按版本号安装到 `tunnel-client\\versions\\<version>`。正常更新保留当前版本和一个旧版本；设置页“清理已下载版本”会删除当前未使用的其他托管版本。

`config/settings.json` 只保存 Manager 本机偏好：

- tunnel-client 来源（自动管理或自定义）；
- 自定义 `tunnel-client.exe` 路径；
- 当前自动管理版本；
- 关闭到托盘；
- Windows 登录启动；
- Profile / Runtime 的 enabled、auto-connect、auto-reconnect；
- 可选的 Profile / State 目录覆盖。

历史 settings 中的 `refreshIntervalMs` / `refresh_interval_ms` 只用于兼容迁移；读取旧配置后会自动重写并移除此字段。

Runtime API Key 明文只进入 Windows Credential Manager，不写入 `settings.json`、Profile 或应用日志。

默认不会覆盖 `TUNNEL_CLIENT_PROFILE_DIR` / `TUNNEL_CLIENT_STATE_DIR`。只有用户在高级设置中主动选择目录时，才向 `tunnel-client` 子进程传递覆盖值。

## 界面结构

左侧主导航只有：

```text
概览
连接
设置
```

日志和诊断不是独立主页面，而是当前连接详情内的标签：

```text
连接
├─ 常规
├─ 日志
└─ 诊断
```

连接生命周期操作栏固定在标签上方：启动、停止、重启、编辑 Profile、更多。执行启动等操作不会自动切换页面或标签。

概览是纯 Dashboard，不显示连接实体列表。它包含系统状态、四张运行统计卡、`tunnel-client` 信息和 Manager 本机状态。旧的全局底部状态条已删除。

设置页分为“常规设置”和“高级设置”。`tunnel-client` 使用紧凑设置项：当前状态、来源下拉框、一个主操作按钮和“更多”菜单；只有下载/安装过程中才临时显示进度条。主按钮在托管模式下按状态显示“下载 / 更新 / 重试 / 取消”，自定义模式下显示“重新选择”。高级设置始终显示，不使用 Expander；Profile / State 目录使用只读路径框和 WinUI `FolderPicker`。

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
- WinUIEdit / Scintilla 原生日志控制台：JSON `level` 精确解析、UTF-8 增量读取、搜索/正则、等级过滤、Follow Tail、暂停/继续、清空、复制、导出、自动换行与重复折叠；
- 停止后保留最后已知日志路径用于继续读取和“打开文件位置”；
- 打开配置 / 日志文件所在位置；
- 纯 WinUI 系统托盘；
- Windows 登录启动；
- 托管 / 自定义 tunnel-client 来源切换；托管下载带进度、取消、SHA-256 校验、版本回退与旧版本清理；
- 自定义 tunnel-client 选择后自动验证，验证成功前不写入配置；
- Windows App SDK `AppInstance` 单实例。

以下功能已从最终产品**直接删除**，不保留隐藏控件或隐藏后端：

- Profile Import / Export；
- inventory 周期轮询；
- Runtime status 周期轮询；
- 可配置刷新间隔字段 / UI；
- 独立 Logs / Diagnostics 主页面；
- 旧的大字符串 `RawLog / VisibleLog` 日志渲染链路。

## Profile 新建 / 编辑

新建与编辑共用一个 WinUI Profile 编辑器，对话框包含：

```text
基本 | 高级配置
```

- Header / Footer 固定；
- 中间内容区独立滚动；
- 基本页包含基本信息、运行选项和凭据；
- 高级页使用 WinUIEdit / Scintilla 编辑完整 YAML / JSON，并按内容启用 YAML / JSON 语法高亮；
- 对话框打开后宽度稳定，不随 TextBox focus / scrollbar 状态重新缩放；
- 编辑模式打开时基本页滚动位置归零。

保存流程：

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

Manager 持有该前台子进程及 stdout/stderr 日志 writer。异常退出通过 `Process.Exited` 立即通知 ViewModel；手动停止时不会误触发自动重连。

所有短生命周期 `tunnel-client` 命令统一通过一个 process runner 执行；调用方取消或命令超时时终止整个子进程树。

## 自动重连

自动重连只针对 Manager 能事件化发现的 Profile 前台进程退出，以及显式操作后确认的停止状态。退避序列：

```text
1s -> 2s -> 5s -> 10s -> 30s -> 60s -> 60s ...
```

延迟结束后重新按 connection Identity 获取当前对象，避免使用旧快照。没有为了自动重连而恢复后台 Runtime status 轮询。

## 日志

Runtime 使用 `runtimes status --json` 返回的官方 `log_path`；纯 Profile 使用 Manager 前台进程日志。

日志 UI 使用 WinUIEdit / Scintilla 原生控件，不依赖 WebView2、Node/npm 或浏览器前端：

- 日志文件按 UTF-8 增量解码，跨 chunk 字符不会被截断；
- 每行按 JSON 解析，等级**只读取 `level` 字段**，不会再从 `message` 或整行文本猜测 ERROR / WARN / INFO；
- 支持 `trace/debug/info/warn/warning/error/fatal/critical` 映射，无法解析的行归为 Unknown；
- 默认显示 `时间 LEVEL [logger] message`，原始 JSON 仍保存在内存结构中；
- 初次只 tail 最近约 2 MiB，后续按文件 offset 增量读取，并处理文件截断 / 轮转；
- Clear All 只清控制台与内存缓冲，并把读取游标推进到当前 EOF，不 truncate 正在写入的日志文件；
- 最多缓存约 30,000 条 / 8 MiB，并保留最多 8 个连接的日志游标；
- 默认 Follow Tail；用户向上滚动后停止抢滚动位置，并显示新增日志数量；点击“滚动到底部”恢复跟随；
- “暂停输出”期间仍持续读取/解析日志，恢复后一次性重放当前缓冲；
- 支持 TRACE / DEBUG / INFO / WARN / ERROR 等级过滤，其中 ERROR 包含 Fatal；
- 支持上一项 / 下一项搜索、区分大小写和 C++11 正则表达式；
- 支持自动换行、连续重复日志折叠、复制、全选和保存当前控制台；
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

只有一套响应式控制器：

```text
Narrow   < 650 epx
Compact  650–999 epx
Wide     >= 1000 epx
```

- 大的页面/分栏使用 `Star` 比例；卡片高度、按钮、Padding 等局部视觉尺寸可使用数值；
- 概览：Wide 四张统计卡 `25% / 25% / 25% / 25%`，Compact `2×2`，Narrow 单列；页面占满 NavigationView 剩余内容区域，不使用固定最大内容宽度；
- 连接：Wide `30% / 70%`，Compact 约 `36% / 64%`，Narrow 上下 `30% / 70%`；
- 生命周期按钮只在 Narrow 纵向排列，Compact / Wide 保持横向；
- 日志筛选：Wide 单行，Compact 两列重排，Narrow 单列；
- 诊断：Wide 双栏，Compact / Narrow 纵向；
- 设置：tunnel-client 紧凑设置项在窄屏下自动换行，下载进度只在操作期间出现；高级目录选择控件继续按断点重排。

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
