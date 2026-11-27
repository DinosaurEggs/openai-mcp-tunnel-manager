# OpenAI MCP Tunnel Manager v1.3.0

## Manager 自身更新

- 设置页显示当前 Manager 版本，手动检查 GitHub 最新正式 Release。
- 发现新版本时显示版本号和更新说明，确认后下载对应架构的发布包，取消不下载。
- 下载后由独立的 C# Updater 等待 Manager 退出、替换发布文件并重启。保留 config、logs、state 和 tunnel-client，安装失败时尝试恢复已替换的文件。
- 从 1.2.0 首次升级需要手动下载完整发布包；后续版本可通过内置入口更新。Manager 持有的前台连接会随应用退出停止。

## tunnel-client 与 Profile 管理

- 以官方 Full Client v0.0.21 为开发和测试基线。不设最低版本号，不执行旧版兼容性测试矩阵。
- 支持指定正式版本下载、切换已安装版本及重新下载当前版本。
- 支持官方 Profile 模板、更多常用配置字段和完整 YAML / JSON 编辑，保存失败后保留编辑内容。
- 支持 Native Runtime 与完整 Profile 前台运行方式，避免重建 Native Runtime 时丢失高级配置。
- 完善共享关联文件检查、进程生命周期及自动重连协调。
- 本机偏好和凭据按 Profile 路径及 Runtime State Root + alias 隔离，settings schema 升级至 v4。
- 复用官方 CLI 校验，移除全量 Profile 预校验和多余输入限制，支持相对 State Directory。

## 连接、远端管理及诊断

- 连接列表支持搜索、过滤和批量操作。
- 新增远端 Tunnel 与 Admin Profile 管理，支持官方过期 Runtime 清理预览及执行。
- 展示 Control Plane、认证、Residency、远端查询和 Repair Actions 等状态信息。
- 完善日志文件轮转、增量读取、组件过滤、原始 JSON 显示与托盘运行摘要。

## 发布

- 继续采用纯 C# / WinUI 3，提供 Windows x64 和 ARM64 self-contained 发布包，内含对应架构 Updater。
- CI 仅手动触发。正式发布工作流在 master 上重新执行行为测试、官方 CLI 本地契约测试、双架构构建、发布文件检查及 x64 启动和单实例测试。
- 完整手工 UI、真实远端认证与 CRUD、ARM64 原生运行及真实跨版本自动更新仍待实机验收。

## 下载

- `OpenAITunnelManager-win-x64.zip`
- `OpenAITunnelManager-win-arm64.zip`
- GitHub Package：`OpenAITunnelManager.Windows 1.3.0`
