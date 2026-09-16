# WinUI 界面与弹窗验收清单

此清单用于后续所有 UI 修改，避免高 DPI、高缩放、窄窗口和长文本再次引入布局回归。

## 通用规则

- 仅使用 WinUI 3 / Windows App SDK 控件与系统 Picker。
- Responsive 主布局只操作明确命名的 XAML 元素，不依赖 VisualTree 顺序猜测页面结构。
- 全局只保留一套三档响应式规则：Narrow `<650`、Compact `650–999`、Wide `>=1000` epx。
- 页面主体使用 `Star` / Stretch / 可用空间比例；固定值只用于局部视觉尺寸和 Narrow 列表高度等明确场景。
- 长路径、长错误文本、长 Profile 内容不能撑宽页面或弹窗。
- 需要路径时使用系统 FilePicker / FolderPicker；目录路径框只读。
- `ContentDialog` 内容必须可滚动。
- 在 100% / 125% / 150% / 175% / 200% / 250% 缩放下检查。
- 在 Narrow / Compact / Wide 三种窗口宽度下检查。

## 主导航与页面

- [x] 主导航只保留“概览 / 连接 / 设置”。
- [x] 日志与诊断不是独立主页面，位于连接详情的 `TabView`。
- [x] 连接详情标签顺序固定为“常规 / 日志 / 诊断”。
- [x] 启动连接后不自动切换页面或标签。
- [x] 全局底部状态条已删除，`StatusMessage` 放入概览。

## 概览

- [x] Wide 模式限制最大可读宽度，窗口最大化后内容不会无限拉伸。
- [x] Wide：统计卡片三列。
- [x] Compact：统计卡片两列，最后单卡可跨两列。
- [x] Narrow：统计卡片单列。
- [x] 连接摘要使用名称 / 状态主行与来源副行，避免放大后多列间距失控。
- [x] 长名称、版本、来源文本可裁切或换行，不扩大页面宽度。

## 连接

- [x] Wide / Compact 使用比例双栏；Narrow 自动纵向排列列表与详情。
- [x] 详情字段说明 / 值使用比例列。
- [x] 主操作只保留“启动 / 停止 / 重启 / 编辑 Profile / 更多”。
- [x] “本机偏好 / 密钥、打开配置位置、删除”进入“更多”。
- [x] Compact / Narrow 时主操作纵向 Stretch，避免按钮挤压。
- [x] 列表选择不再被 Runtime 定时 status 刷新周期替换对象。
- [x] 手动“刷新”仍允许重新读取完整 inventory / status。

## 日志标签

- [x] 保留“立即刷新”和“自动刷新”。
- [x] 自动刷新只在日志标签当前可见且开关启用时每 1 秒增量读取。
- [x] 筛选栏按三档断点重排。
- [x] 保留日志搜索、等级过滤、自动换行。
- [x] 不存在复制可见日志或导出日志入口。
- [x] 日志文本横向滚动不会因自动跟随被强制重置。

## 诊断标签

- [x] Doctor / Health 在 Wide 双栏、Compact / Narrow 纵向排列。
- [x] 使用当前连接，不再提供独立页面的第二个连接选择器。
- [x] “详细 Health / 开始诊断”保留在诊断标签顶部操作区。

## 设置

- [x] 页面内容 Stretch，不使用整个页面固定 MaxWidth。
- [x] 保存设置按钮位于标题右上角。
- [x] 不显示 `settings.json` 文件路径。
- [x] “常规设置 / 高级设置”使用静态小标题。
- [x] 高级设置不使用 Expander，不存在展开后改变页面整体尺寸的问题。
- [x] inventory / Runtime 自动刷新间隔设置与配置字段均删除。
- [x] Profile / State 目录使用 FolderPicker。
- [x] 目录路径框只读，并提供“选择目录 / 清除”。
- [x] 高级设置长路径使用 Star 列和 `MinWidth=0`，不会扩大页面宽度。

## 配置列表交互

- [x] 右键空白区域不显示配置菜单。
- [x] 右键命中配置项时才显示“编辑 / 删除”。
- [x] 右键操作前先切换到实际命中的配置项。
- [x] Profile Import / Export 不属于最终产品，UI 不保留入口。

## ContentDialog / Picker

- [x] 新建 Profile：字段 Stretch，内容可滚动。
- [x] 编辑 Profile：YAML / JSON 编辑区按窗口高度限制并可滚动。
- [x] 未知未来 target：common target 输入禁用，高级原始文本仍可编辑。
- [x] 本机偏好 / 密钥：内容可滚动、字段 Stretch。
- [x] 删除确认与错误提示支持长文本。
- [x] tunnel-client.exe 使用 FileOpenPicker。
- [x] Profile / State 目录使用 FolderPicker + 只读路径 + 清除。

## 自动门禁

- [x] 源码禁止 WinForms / WPF / Python。
- [x] x64 / ARM64 WinUI publish。
- [x] 发布产物禁止 WinForms / WPF / Python Runtime 残留。
- [x] x64 启动 smoke test。
- [x] 单实例 smoke test。

## 人工视觉验收

CI 可以验证构建、启动和基础结构，但以下项目仍需在最终 Windows Artifact 上人工查看：

- [ ] 100 / 125 / 150 / 175 / 200 / 250% DPI 下无裁切或重叠。
- [ ] Narrow `<650` 下连接详情、日志过滤和设置路径控件可完整操作。
- [ ] Compact `650–999` 下连接双栏比例和纵向操作按钮不拥挤。
- [ ] Wide `>=1000` 下概览最大化后内容宽度、卡片密度和留白合理。
- [ ] 长 Profile 名、长路径、长错误信息不会扩大主窗口内容宽度。
- [ ] 日志自动刷新时切换连接、搜索和水平滚动体验正常。

> `[x]` 表示代码侧已纳入对应规则；视觉项仍以实际 Windows Artifact 和截图为最终验收依据。
