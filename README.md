# OpenAI MCP Tunnel Manager

Windows 上的 `tunnel-client` 可视化管理器。项目以 `tunnel-client` 为 Profile、Runtime、Tunnel ID、MCP target、运行状态、Health URL 和日志路径的唯一事实来源，GUI 不维护第二套 Tunnel 配置。

## 1.0.0

`1.0.0` 是首个正式版本，主要包含：

- Windows 单实例模式：同一登录会话只运行一个程序实例；再次双击 EXE 会唤醒并置前已经运行的窗口，包括从系统托盘恢复。
- 真正的系统托盘图标：程序运行期间始终显示托盘图标，右键菜单提供“打开”和“退出”。
- 配置列表简化为 `[已启动/已停止] 配置名称`，支持右键“编辑 / 删除”。
- Profile 编辑全部在 GUI 中完成，保存时仍交给 `tunnel-client` 官方校验。
- Runtime 启动、停止、重启、诊断以及本机偏好 / Runtime API Key 管理。
- 日志页支持增量刷新、搜索、等级过滤、自动换行、垂直/水平滚动位置保持，以及直接打开日志文件所在位置。
- Health 兼容新旧 Runtime：基础 `/healthz`、`/readyz` 始终按官方状态处理；只有 Runtime 明确声明 `health_details_url` / `mcp_health_url` 时才读取详细健康接口，避免旧 Runtime 的 404 被误报为健康故障。
- EXE、任务栏、窗口和托盘统一使用项目图标。

## 数据来源

界面刷新直接读取：

```text
tunnel-client profiles list --json
tunnel-client runtimes list --json
tunnel-client runtimes status <alias> --json
```

因此，通过命令行创建或修改的 Profile / Runtime alias 会在 GUI 刷新后同步显示。

应用自己的 `settings.json` 只保存 GUI 本机信息，例如：

- `tunnel-client.exe` 路径
- 刷新间隔、关闭按钮行为、Windows 登录启动
- 每个 Profile / Runtime 的启用、自动连接、自动重连偏好
- Runtime API Key 的本机凭据引用

明文 Runtime API Key 存储在 Windows Credential Manager。GUI 不保存 Tunnel ID、MCP URL/command、官方 Profile 内容、Health URL、日志路径或 Runtime 状态。

## 主要功能

- 自动发现现有 Profile / Runtime
- 新建 Profile
- GUI 编辑 Profile
- 删除 Profile / Runtime
- 配置列表右键编辑 / 删除
- Profile / Runtime 启动、停止、重启
- `doctor --explain` 诊断
- 打开配置文件所在位置
- Runtime Health / Ready 状态
- 日志自动刷新、手动刷新、搜索、等级过滤、自动换行
- 打开日志文件所在位置
- 自动连接 / 自动重连
- Runtime API Key 存 Windows Credential Manager
- 系统托盘
- Windows 登录后启动
- Windows 单实例运行

## 单实例行为

程序使用 Windows Named Mutex 保证同一 Windows 登录会话只有一个实例，并使用 Named Event 通知已有实例恢复窗口。

```text
第一次启动
  -> 正常创建窗口和托盘

再次启动 EXE
  -> 不创建第二个 GUI
  -> 通知已有实例
  -> 已有窗口从托盘恢复并置前
  -> 第二个进程立即退出
```

单实例机制不使用固定 TCP 端口，也不依赖 `pywin32`。

## 首次运行

源码运行需要 Python 3.11+。Windows 官方 Python 通常包含 Tkinter。

```powershell
.\run.ps1
```

第一次打开后进入“设置”，选择正在使用的 `tunnel-client.exe`。程序随后直接读取该二进制对应的官方 Profile / Runtime 数据。

如果列表为空，可在同一 PowerShell 环境验证：

```powershell
& "C:\path\to\tunnel-client.exe" profiles list --json
& "C:\path\to\tunnel-client.exe" runtimes list --json
```

如果使用 `TUNNEL_CLIENT_PROFILE_DIR` 或 `TUNNEL_CLIENT_STATE_DIR`，启动 GUI 时也需要继承相同环境变量。

## Profile 编辑

“编辑 Profile”不会打开外部 TXT / 记事本。

- **常用配置**：Tunnel ID、main MCP URL/command。
- **高级配置**：完整 Profile YAML/JSON，在程序内部编辑。
- **本机设置**：启用、自动连接、自动重连、Runtime API Key。

保存流程：

```text
GUI 编辑
 -> 写入临时文件
 -> tunnel-client profiles add <name> --from-file <temp> --force
 -> tunnel-client 校验成功后替换官方 Profile
```

## 日志

启动 Profile / Runtime 后，日志页读取官方 Runtime `log_path` 或 GUI 前台运行日志。

- 大日志只读取受限尾部，避免 GUI 卡顿。
- 自动刷新使用增量追加，尽量减少整段重绘。
- 自动跟随仅影响垂直方向；水平滚动位置由用户控制，刷新不会自动跳到行尾。
- 可搜索并按 `DEBUG / INFO / WARN / ERROR` 过滤。
- 可开启自动换行。
- 可直接打开当前日志文件所在位置。

## Health

基础运行状态使用 tunnel-client 的既有 Health contract：

```text
/healthz
/readyz
```

详细 Health 仅在 `runtimes status --json` 明确提供下面字段时读取：

```text
health_details_url
mcp_health_url
```

旧 Runtime 不提供这些字段时，GUI 会显示“未声明详细健康接口”，不会再无条件请求 `/health?details=true` 或 `/health/mcp` 并产生误导性的 404。

## 构建 Windows EXE

```powershell
.\build.ps1 -TunnelClientPath "F:\Programs\openai-tunnel-client\tunnel-client.exe"
```

构建脚本会先运行完整验证门禁，再使用 PyInstaller 生成：

```text
dist\OpenAITunnelManager.exe
```

EXE 是单文件 Windows GUI 程序，包含项目图标和 Windows `1.0.0` 文件版本信息。

GitHub Actions 使用 `.github/workflows/build-windows.yml` 执行相同的 Windows 验证和打包流程。

## 发布

正式发布提交必须使用：

```text
Release v<版本号> ...
```

GitHub Actions 会先完成 `Verify` 和 Windows EXE 打包。只有这些步骤全部成功后才会：

1. 将 `OpenAITunnelManager.Windows` NuGet 包发布到 GitHub Packages；
2. 创建对应的 `v<版本号>` GitHub Release；
3. 把 `OpenAITunnelManager.exe` 作为 Release 资产上传。

普通 `master` 提交不会自动发布新版本。

## 验证

Windows：

```powershell
.\verify.ps1
```

或直接：

```powershell
python verify.py
```

门禁包含核心逻辑、GUI、真实 fake tunnel-client 集成、Windows Credential Manager、Windows Named Mutex/Event 单实例行为以及版本一致性检查。详细说明见 `VALIDATION.md`。

## 当前边界

- 本工具管理本机 `tunnel-client` Profile / Runtime，不负责 OpenAI 平台上的远程 Tunnel CRUD。
- Profile / Runtime 的官方配置仍由 `tunnel-client` 管理。
- 系统托盘、Windows Credential Manager 和单实例 Named Object 行为以 Windows CI 为最终门禁。
