# OpenAI MCP Tunnel Manager

Windows 上的 `tunnel-client` 可视化管理器。运行时只使用 Python 标准库；可用 PyInstaller 打包为不需要安装 Python 的目录版程序。

## 0.3.0 的核心原则

`tunnel-client` 是 Profile、Runtime alias、Tunnel ID、MCP target、运行状态、Health URL 和日志路径的唯一事实来源。GUI 不维护第二套 Tunnel 配置清单。

界面刷新直接读取：

```text
tunnel-client profiles list --json
tunnel-client runtimes list --json
tunnel-client runtimes status <alias> --json
```

因此，已经通过 `tunnel-client init --profile ...` 创建的 Profile，或通过 `tunnel-client runtimes connect ...` 创建的 Runtime alias，在选择正确的 `tunnel-client.exe` 后会自动显示。外部命令行新增/删除配置后，手动刷新或周期刷新即可同步。

应用自己的 `settings.json` 只保存 GUI 本机信息：

- `tunnel-client.exe` 路径
- 刷新间隔、关闭时最小化、Windows 登录启动
- 每个 Profile/Runtime 的自动连接、自动重连、启用状态
- Runtime API Key 的凭据引用（明文 Key 存 Windows Credential Manager）

不会保存 Tunnel ID、MCP URL/command、官方 Profile 内容、Health URL、日志路径或 Runtime 状态。

## 0.3.0 主要改进

- “编辑 Profile”完全在程序 GUI 中完成，不再打开 TXT、记事本或系统 `$EDITOR`。
- 编辑窗口包含“常用配置 / 高级配置 / 本机设置”三个页签；高级 YAML/JSON 也在应用窗口内编辑。
- 保存编辑内容时通过 `tunnel-client profiles add <name> --from-file <temp> --force` 让 tunnel-client 自己完整校验，校验失败不会覆盖原 Profile。
- 删除“打开官方 UI”按钮，日常操作全部留在本程序界面。
- Runtime 按钮严格跟随状态：未运行时“停止/重启”灰色；点击启动后“启动”立即灰色；运行后“停止/重启”可用。
- 启动后自动切到“日志”页，并持续读取 tunnel-client 官方 Runtime `log_path` 或 GUI 前台 `run --profile` 日志。
- 日志支持自动刷新/暂停、立即刷新、搜索、等级过滤、复制和导出；停止后仍可查看最近一次日志。
- 修复旧日志页调用不存在接口导致 `AttributeError: 'TunnelClient' object has no attribute 'tail_log'` 的问题；日志读取统一使用单一 `read_log_tail()` 接口。
- 中文界面、父窗口居中弹窗、严格 `tunnel-client.exe` 路径检查继续保留。

## 界面中的对象

官方 Profile 和 Runtime alias 是两个概念。界面将 `profiles list` 与 `runtimes list` 合并成实时视图：

- 只有 Profile：显示为 Profile，启动时执行 `run --profile <name>`，前台进程由 GUI 生命周期管理。
- 已有 Runtime alias：状态/启停使用官方 `runtimes status/connect/stop/rm`。
- Runtime 引用了 `profiles list` 中的 Profile：显示关联关系，但配置仍从官方 Profile/Runtime 读取。
- Runtime 使用自定义 `--profile-dir` 时，结合实际 Profile 路径判断关联关系；名称相同也不会误合并。
- 多个 Runtime alias 可引用同一个 Profile，不会被折叠。
- Runtime alias 与 Profile 同名时内部仍使用独立身份键，不会串状态或本机偏好。

## 主要功能

- 自动发现现有 tunnel-client Profile / Runtime
- 从 tunnel-client 手动重新读取
- 新建 Profile：调用官方 `tunnel-client init`
- 编辑 Profile：内部 GUI 编辑 + tunnel-client 校验提交
- 导入 Profile：调用官方 `profiles add --from-file`
- 导出官方 Profile YAML
- Profile / Runtime 启动、停止、重启
- `doctor --explain`
- Runtime Health / Ready 状态
- 启动后实时日志查看、搜索、等级过滤、复制、导出
- 自动连接 / 自动重连
- Runtime API Key 存 Windows Credential Manager
- `tunnel-client.exe` 严格路径检查
- 中文界面与居中弹窗
- 关闭窗口时最小化到 Windows 任务栏

## 首次运行

源码运行需要 Python 3.11+，Windows 官方 Python 通常自带 Tkinter。

```powershell
.\run.ps1
```

第一次打开后进入“设置”，选择正在使用的 `tunnel-client.exe`。程序会直接扫描这个二进制对应的官方 Profile / Runtime 数据。

如果列表为空，可在同一个 PowerShell 环境中验证：

```powershell
& "C:\path\to\tunnel-client.exe" profiles list --json
& "C:\path\to\tunnel-client.exe" runtimes list --json
```

GUI 与命令行必须使用同一个 `tunnel-client.exe`，并继承相同的 tunnel-client Profile/State 环境变量。如果你使用了 `TUNNEL_CLIENT_PROFILE_DIR` 或 `TUNNEL_CLIENT_STATE_DIR`，启动 GUI 时也必须继承它们。

## 编辑 Profile

“编辑 Profile”不会再打开外部文本编辑器。

- **常用配置**：Tunnel ID、可识别的 main MCP URL/command。
- **高级配置**：完整 Profile YAML/JSON，在程序自己的 GUI 文本页中编辑，用于多通道、代理、证书等高级/未知字段。
- **本机设置**：启用状态、自动连接、自动重连、Runtime API Key。

保存流程：GUI 内容写入临时文件 → `tunnel-client profiles add <name> --from-file <temp> --force` → tunnel-client 校验成功后替换官方 Profile。程序不会直接绕过 tunnel-client 校验裸写 Profile。

## 启动 / 停止 / 重启按钮

- 配置未运行：`启动`可用，`停止`和`重启`禁用。
- 正在启动：运行操作临时锁定，避免重复提交。
- 已运行 / Ready：`启动`禁用，`停止`和`重启`可用。
- 正在停止 / 重启：冲突操作临时锁定。

## 日志查看

启动 Profile/Runtime 后，手动点击“启动”会自动切换到“日志”页。

- Runtime 日志路径从官方 `runtimes status --json` 的 `log_path`（含支持的嵌套形式）读取。
- GUI 管理的 `run --profile` 会把 stdout/stderr 写入自己的 Runtime 日志文件，并通过同一个日志页显示。
- 自动刷新可关闭，关闭后“立即刷新”仍可用。
- 支持文本搜索与 `DEBUG / INFO / WARN / ERROR` 过滤。
- 支持复制当前可见日志和导出文件。
- 大日志只读取受限尾部，避免阻塞 GUI。
- 停止后保留最近一次已知日志路径和缓存，可继续查看本次运行日志。

## 删除语义

删除前会重新读取官方 inventory：

- Runtime alias：先停止活动进程，再执行官方 `runtimes rm`。
- 列出的 Profile：只有未被其他 Runtime 共用时才删除 Profile 文件。
- Profile 是符号链接时只删除链接本身，不删除链接目标。
- 不删除 OpenAI 平台上的远程 Tunnel。
- 删除一个 Runtime 不会误删其他 Runtime 仍依赖的共享 Profile 凭据。

## Runtime API Key

Windows 下使用原生 Credential Manager (`CredWriteW` / `CredReadW` / `CredDeleteW`)。应用配置文件不含明文密钥。

## 构建 Windows 版本

```powershell
.\build.ps1 -TunnelClientPath "F:\Programs\openai-tunnel-client\tunnel-client.exe"
```

构建脚本先执行测试门禁，再用 PyInstaller 生成目录版。PyInstaller 只用于构建，应用运行时没有第三方 Python 包依赖。

也可以使用 `.github/workflows/build-windows.yml` 在 Windows Runner 构建。

## 验证

Linux/macOS：

```bash
./verify.sh
```

Windows：

```powershell
.\verify.ps1
```

详细验证结果见 `VALIDATION.md`。

## 当前限制

- 当前没有真正的系统托盘图标；“关闭窗口时最小化”是最小化到任务栏。
- OpenAI 平台 Tunnel CRUD 不属于本工具范围。
- Windows Credential Manager 的真实 round-trip 只能在 Windows 门禁中验证。
