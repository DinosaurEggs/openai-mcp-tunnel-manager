# WinUI 界面与弹窗验收清单

此清单用于后续所有 UI 修改，避免高 DPI、高缩放、窄窗口和长文本再次引入布局回归。

## 通用规则

- 仅使用 WinUI 3 / Windows App SDK 控件与系统 Picker。
- Responsive 主布局只操作明确命名的 XAML 元素，不依赖 VisualTree 顺序猜测页面结构。
- 全局只保留一套三档响应式规则：Narrow `<650`、Compact `650–999`、Wide `>=1000` epx。
- 页面主布局使用 `Star` / Stretch / 可用空间比例，不用固定像素决定主体宽度。
- 固定值只用于间距、图标、边框等局部视觉尺寸。
- 长路径、长错误文本、长 Profile 内容不能撑宽页面或弹窗。
- 需要路径时使用系统 FilePicker / FolderPicker；路径框只用于展示，不要求用户手工输入。
- `ContentDialog` 内容必须可滚动，不能让标题和操作按钮离开可视区域。
- 在 100% / 125% / 150% / 175% / 200% / 250% 缩放下检查。
- 在 Narrow / Compact / Wide 三种窗口宽度下检查。

## 页面

- [x] 概览：卡片按比例排列，窄宽度自动纵向排列。
- [x] 概览：连接列表中的名称/来源/状态列使用比例布局。
- [x] 连接：Wide / Compact 使用比例双栏；Narrow 自动纵向排列列表与详情。
- [x] 连接：详情字段说明 / 值使用比例列。
- [x] 连接：主操作只保留“启动 / 停止 / 重启 / 编辑 Profile”。
- [x] 连接：“本机偏好 / 密钥、打开配置位置、删除”进入“更多”。
- [x] 连接：Compact / Narrow 时主操作纵向 Stretch，避免按钮挤压。
- [x] 日志：筛选栏按三档断点重排。
- [x] 日志：只保留“立即刷新 / 打开文件位置”，不存在复制可见日志或导出日志入口。
- [x] 诊断：Doctor / Health 在 Wide 双栏、Compact / Narrow 纵向排列。
- [x] 诊断：连接选择器不使用固定宽度。
- [x] 设置：页面内容 Stretch，不使用整个页面固定 MaxWidth。
- [x] 设置：inventory 自动刷新间隔设置已从 XAML 删除，不再仅仅隐藏。
- [x] 设置：高级 Profile / State 目录使用 FolderPicker。
- [x] 设置：高级设置长路径不扩大页面宽度。

## 配置列表交互

- [x] 右键空白区域不显示配置菜单。
- [x] 右键命中配置项时才显示“编辑 / 删除”。
- [x] 右键操作前先切换到实际命中的配置项。
- [x] Profile Import / Export 不属于最终产品，UI 不保留入口或隐藏 Flyout。

## ContentDialog / Picker

- [x] 新建 Profile：中性示例，不使用特定 IDE 名称。
- [x] 新建 Profile：字段 Stretch，内容可滚动。
- [x] 编辑 Profile：YAML / JSON 编辑区按当前窗口高度比例限制并可滚动。
- [x] 编辑 Profile：遇到未知未来 target 类型时禁用 common target 输入，但高级原始文本仍可编辑。
- [x] 本机偏好 / 密钥：内容可滚动、字段 Stretch。
- [x] 删除确认：长名称 / 长文本不使用固定 MaxWidth。
- [x] 错误提示：长错误文本可滚动，不使用固定 MaxWidth。
- [x] tunnel-client.exe：使用 FileOpenPicker。
- [x] 高级 Profile 目录：使用 FolderPicker + 只读路径 + 清除。
- [x] 高级 State 目录：使用 FolderPicker + 只读路径 + 清除。

## 自动门禁

- [x] 源码禁止 WinForms / WPF / Python。
- [x] x64 / ARM64 WinUI publish。
- [x] 发布产物禁止 WinForms / WPF / Python Runtime 残留。
- [x] x64 启动 smoke test。
- [x] 单实例 smoke test。

## 人工视觉验收

CI 可以验证构建、运行和结构，但以下项目仍需要在最终 Artifact 上人工查看：

- [ ] 100 / 125 / 150 / 175 / 200 / 250% DPI 下无裁切或重叠。
- [ ] Narrow `<650` 下连接详情、日志过滤和设置路径输入可完整操作。
- [ ] Compact `650–999` 下连接双栏比例和纵向操作按钮不拥挤。
- [ ] Wide `>=1000` 下信息密度合理，无不必要空白。
- [ ] 长 Profile 名、长路径、长错误信息不会扩大主窗口内容宽度。

> `[x]` 表示代码侧已纳入对应规则；视觉项仍以实际 Windows Artifact 和截图为最终验收依据。
