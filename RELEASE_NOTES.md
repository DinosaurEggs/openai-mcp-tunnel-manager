# OpenAI MCP Tunnel Manager v1.2.0

## 版本说明

1.2.0 重点完善 tunnel-client 生命周期管理、日志控制台和高级配置编辑体验，并继续保持纯 C# / WinUI 3、x64 / ARM64 self-contained 发布。

## tunnel-client 管理

- 设置 schema 升级到 v3，明确区分托管版本（Managed）和自定义版本（Custom）。
- 设置页改为紧凑单行布局：当前状态、来源下拉、一个主操作按钮和“更多”菜单。
- 托管版本未安装时显示“下载”，已安装时显示“更新”；只有用户点击“更新”时才检查远端新版本，应用启动不再自动联网检查。
- 首次启动选择托管版本并点击下载后，先进入设置页，再显示实际下载进度。
- 下载过程显示检查、下载、校验、安装阶段，并在可获取 Content-Length 时显示字节进度和百分比；下载阶段支持取消。
- 按当前架构精确选择 OpenAI 官方 Windows amd64 / arm64 Release 资产。
- 校验 Release digest 与 SHA256SUMS.txt，安全解压，并在新 EXE 通过 `tunnel-client.exe --version` 后才切换当前版本。
- 更新失败或取消时继续保留并使用旧版本。
- 正常更新保留当前版本和一个回退版本；“清理已下载版本”可删除当前未使用的托管版本。
- 自定义 EXE 选择后自动执行版本验证；验证失败不会覆盖现有有效配置，并可直接重新选择。
- “更多”菜单提供打开文件位置和托管版本清理入口。

## 日志控制台

- 日志页切换为 WinUIEdit / Scintilla 原生大文本控件，不依赖 WebView2、xterm.js 或 npm。
- JSON 日志等级只读取 `level` 字段，不再从 message 或整行文本猜测等级。
- 支持 UTF-8 增量 tail、文件截断 / 轮转、30,000 条 / 8 MiB 有界缓冲。
- 支持 Follow Tail；用户向上滚动后保持当前位置，并累计新日志数量。
- 支持暂停 / 恢复、Clear All、TRACE / DEBUG / INFO / WARN / ERROR 过滤。
- 支持上一项 / 下一项搜索、大小写、正则、自动换行、连续重复日志折叠、复制、全选和保存控制台。
- Clear All 只清空控制台和内存缓冲，并把读取游标推进到当前 EOF，不 truncate 正在写入的日志文件。

## Profile 高级配置

- 高级配置编辑器改为 WinUIEdit / Scintilla。
- 根据内容自动使用 YAML / JSON 语法高亮。
- 保留既有 Profile 文本同步、官方校验和保存流程。
- 获得 Scintilla 原生选择、滚动和 Undo / Redo 能力。

## 兼容与发布

- 保留历史 settings 的单向迁移，并继续移除旧 refresh interval 字段。
- 继续使用 Windows Credential Manager 保存 Runtime API Key。
- 继续保持纯 WinUI 3，无 Python、WinForms 或 WPF Runtime。
- x64 / ARM64 self-contained 发布均校验 WinUIEditor.dll / WinUIEditor.pri 和 WinUI Runtime 文件。
- x64 继续执行真实启动 smoke test 和单实例 activation smoke test。

## 发布产物

- `OpenAITunnelManager-win-x64.zip`
- `OpenAITunnelManager-win-arm64.zip`
- GitHub Package：`OpenAITunnelManager.Windows 1.2.0`
