# WinUI 界面与弹窗验收清单

适用于 OpenAI MCP Tunnel Manager 1.1.0 及后续 WinUI 3 版本。

此清单用于后续所有 UI 修改，避免高 DPI、高缩放、窄窗口、长文本和大日志再次引入布局回归。

## 通用规则

- 仅使用 WinUI 3 / Windows App SDK 控件与系统 Picker。
- 全局只保留一套三档响应式控制器：Narrow `<650`、Compact `650–999`、Wide `>=1000` epx。
- 页面主体、左右分栏、Dashboard 主区域使用 `Star` / Stretch / 可用空间比例。
- 卡片高度、按钮、Padding、字号、圆角等局部视觉尺寸可以使用数值，避免窗口放大后组件无限膨胀。
- 已决定删除的功能直接删除 XAML、事件、ViewModel / backend 和响应式残留，不使用 `Collapsed`、零高度行或空实现代替删除。
- 长路径、长错误文本、长 Profile 内容不能撑宽页面或弹窗。
- 需要路径时使用系统 FilePicker / FolderPicker；目录路径框只读。
- ContentDialog 的正文区域可滚动，Header / Footer 保持稳定。
- 在 100% / 125% / 150% / 175% / 200% / 250% 缩放下检查。
- 在 Narrow / Compact / Wide 三种窗口宽度下检查。

## 主导航与页面

- [x] 主导航只保留“概览 / 连接 / 设置”。
- [x] 日志与诊断不是独立主页面，位于连接详情 `TabView`。
- [x] 连接详情标签顺序固定为“常规 / 日志 / 诊断”。
- [x] 启动连接后不自动切换页面或标签。
- [x] 全局底部状态条已删除，`StatusMessage` 放入概览。

## 概览

- [x] 页面占满 NavigationView 剩余内容区域，不使用固定 `MaxWidth` 控制大布局。
- [x] Wide：四张统计卡按 `25% / 25% / 25% / 25%` 分配。
- [x] Compact：四张统计卡为 `2×2`。
- [x] Narrow：四张统计卡单列。
- [x] 统计卡自身高度、Padding 等保持局部数值约束，不随窗口高度比例放大。
- [x] `tunnel-client` / Manager 信息区占满主内容宽度，长版本和路径可裁切。
- [x] 概览不显示连接实体列表。

## 连接

- [x] Wide / Compact 使用比例双栏；Narrow 使用列表 + 详情上下布局。
- [x] Wide 约 `30% / 70%`；Compact 约 `36% / 64%`；Narrow 上下约 `30% / 70%`。
- [x] 详情字段说明 / 值使用比例列。
- [x] 主操作只保留“启动 / 停止 / 重启 / 编辑 Profile / 更多”。
- [x] “本机偏好 / 密钥、打开配置位置、删除”进入“更多”。
- [x] 只有 Narrow 主操作纵向 Stretch；Compact / Wide 保持横向。
- [x] 列表选择不再被 Runtime 周期 status 刷新替换对象。
- [x] 手动“刷新”仍允许重新读取完整 inventory / status。

## 日志标签

- [x] 使用 WinUIEdit / Scintilla 原生 `LogConsoleControl`，不依赖 WebView2 / npm / 浏览器前端。
- [x] 旧 `RawLog / VisibleLog` 和 ANSI/ListView 自研渲染链路已删除。
- [x] 日志标签可见时每 1 秒增量 tail；不再提供“自动刷新”开关。
- [x] “暂停输出”只暂停控制台渲染，文件读取与结构化 buffer 继续推进。
- [x] Clear All 清屏后从当前文件 EOF 继续读取，不删除或 truncate 实际日志文件。
- [x] JSON 等级只读取 `level` 字段，不从 message / 原始文本猜测。
- [x] 筛选栏按三档断点重排。
- [x] 支持 TRACE / DEBUG / INFO / WARN / ERROR 等级过滤。
- [x] 支持上一项 / 下一项、大小写和正则搜索。
- [x] 支持 Follow Tail；用户向上滚动后保持 viewport，并显示新增日志数量。
- [x] 支持自动换行、连续重复日志折叠。
- [x] 支持复制、全选、保存当前控制台和打开日志文件位置。
- [x] 使用有界结构化 buffer，长日志不会无限占用内存。
- [x] 日志页文件路径控件和对应布局行保持删除。

## 诊断标签

- [x] Doctor / Health 在 Wide 双栏、Compact / Narrow 纵向排列。
- [x] 使用当前连接，不再提供独立页面的第二个连接选择器。
- [x] “详细 Health / 开始诊断”保留在诊断标签顶部操作区。

## 设置

- [x] 页面内容 Stretch，不使用整个页面固定 MaxWidth。
- [x] 保存设置按钮位于标题操作区；Narrow 时自动换到下一行。
- [x] 不显示 `settings.json` 文件路径，保存状态也不回显该路径。
- [x] “常规设置 / 高级设置”使用静态小标题。
- [x] 高级设置不使用 Expander。
- [x] inventory / Runtime 自动刷新间隔设置与 UI 已删除；旧字段仅在配置迁移时识别并清除。
- [x] Profile / State 目录使用 FolderPicker。
- [x] 目录路径框只读，并提供“选择目录 / 清除”。
- [x] Narrow 时目录路径独占一行，两个按钮在下一行；Compact / Wide 恢复 Star + Auto + Auto。

## Profile 新建 / 编辑弹窗

- [x] 新建与编辑共用 `ProfileEditorControl`。
- [x] 标签为“基本 / 高级配置”。
- [x] ContentDialog 只负责居中与外层约束，单一 Host Grid 负责编辑区尺寸。
- [x] Editor 本身只 Stretch，不再同时锁 `Width / MinWidth / MaxWidth`。
- [x] 基本页只有正文 ScrollViewer；Footer 不随正文滚动。
- [x] 高级编辑器使用 WinUIEdit / Scintilla，占剩余 `*` 高度，并支持 YAML / JSON 语法高亮。
- [x] 编辑模式打开时基本页滚动位置归零。
- [x] TextBox focus / clear button / scrollbar 状态不会导致对话框宽度重新收缩。
- [x] 未知未来 target：common target 输入禁用，高级原始文本仍可编辑。
- [x] Runtime API Key 删除必须显式操作；留空保持现有凭据。

## 配置列表交互

- [x] 右键空白区域不显示配置菜单。
- [x] 右键命中配置项时才显示“编辑 / 删除”。
- [x] 右键操作前先切换到实际命中的配置项。
- [x] Profile Import / Export 不属于最终产品，UI 与 backend 均不保留。

## 删除功能清理原则

以下能力已经是“源码删除”，不是隐藏：

- [x] Profile Import / Export。
- [x] 独立 Logs / Diagnostics 主页面。
- [x] 全局底部状态条。
- [x] Runtime / inventory 周期轮询。
- [x] 可配置刷新间隔 UI / runtime setting。
- [x] 旧字符串日志 ViewModel 文件与 `RawLog / VisibleLog` 链路。
- [x] 旧 supplemental layout controller；响应式规则已合并回 `MainWindow.Responsive.cs`。
- [x] 旧 RuntimeMonitor 文件；事件生命周期已迁移到 `ConnectionsViewModel.RuntimeEvents.cs`。

## 自动门禁

- [x] 源码禁止 WinForms / WPF / Python。
- [x] x64 / ARM64 WinUI publish。
- [x] 发布产物禁止 WinForms / WPF / Python Runtime 残留。
- [x] x64 启动 smoke test。
- [x] 单实例 smoke test。
- [x] JSON level parser 与增量 tail / Clear cursor 包含自动测试。

## 人工视觉验收

CI 可以验证构建、启动和基础结构，但以下项目仍需在最终 Windows Artifact 上人工查看：

- [ ] 100 / 125 / 150 / 175 / 200 / 250% DPI 下无裁切或重叠。
- [ ] Narrow `<650` 下连接详情、日志过滤和设置路径控件可完整操作。
- [ ] Compact `650–999` 下连接双栏比例和横向操作栏不拥挤。
- [ ] Wide `>=1000` 下 Dashboard 四卡比例、信息区宽度和留白合理。
- [ ] 新建 / 编辑 Profile 弹窗在不同 DPI 下居中，基本页从顶部显示，高级编辑器滚动条位于内容边界内。
- [ ] 长 Profile 名、长路径、长错误信息不会扩大主窗口内容宽度。
- [ ] 日志持续 tail 时切换连接、搜索、Follow Tail、暂停/恢复和水平滚动体验正常。

> `[x]` 表示代码侧已纳入对应规则；视觉项仍以实际 Windows Artifact 和截图为最终验收依据。
