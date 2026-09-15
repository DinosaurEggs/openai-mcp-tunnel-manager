# WinUI 界面与弹窗验收清单

此清单用于后续所有 UI 修改，避免高 DPI、高缩放、窄窗口和长文本再次引入布局回归。

## 通用规则

- 仅使用 WinUI 3 / Windows App SDK 控件与系统 Picker。
- 页面主布局使用 `Star` / Stretch / 可用空间比例，不用固定像素决定主体宽度。
- 固定值只用于间距、图标、边框等局部视觉尺寸。
- 长路径、长错误文本、长 Profile 内容不能撑宽页面或弹窗。
- 需要路径时使用系统 FilePicker / FolderPicker；路径框只用于展示，不要求用户手工输入。
- `ContentDialog` 内容必须可滚动，不能让标题和操作按钮离开可视区域。
- 在 100% / 125% / 150% / 175% / 200% / 250% 缩放下检查。
- 在窄、中、宽三种窗口宽度下检查。

## 页面

- [x] 概览：卡片按比例排列，窄宽度自动纵向排列。
- [x] 概览：连接列表中的名称/来源/状态列改为比例布局。
- [x] 连接：配置列表 / 详情按比例布局。
- [x] 连接：详情字段说明 / 值使用比例列。
- [x] 连接：操作按钮在空间不足时改为纵向 Stretch。
- [x] 日志：筛选栏按比例布局，窄宽度自动换行。
- [x] 诊断：Doctor / Health 使用比例布局。
- [x] 诊断：连接选择器不使用固定宽度。
- [x] 设置：页面内容 Stretch，不使用整个页面固定 MaxWidth。
- [x] 设置：废弃的 inventory 自动刷新设置隐藏。
- [x] 设置：高级 Profile / State 目录使用 FolderPicker。
- [x] 设置：高级设置长路径不扩大页面宽度。

## 配置列表交互

- [x] 右键空白区域不显示配置菜单。
- [x] 右键命中配置项时才显示“编辑 / 删除”。
- [x] 右键操作前先切换到实际命中的配置项。

## ContentDialog / Picker

- [x] 新建 Profile：中性示例，不再使用特定 IDE 名称。
- [x] 新建 Profile：字段 Stretch，内容可滚动。
- [x] 编辑 Profile：移除固定 300/650 像素高度。
- [x] 编辑 Profile：YAML / JSON 编辑区按当前窗口高度比例限制并可滚动。
- [x] 本机偏好 / 密钥：内容可滚动、字段 Stretch。
- [x] 删除确认：长名称 / 长文本不使用固定 MaxWidth。
- [x] 错误提示：长错误文本可滚动，不使用固定 MaxWidth。
- [x] 导入 Profile：中性名称提示，使用 FileOpenPicker。
- [x] 导出 Profile：使用 FileSavePicker。
- [x] 高级 Profile 目录：使用 FolderPicker + 只读路径 + 清除。
- [x] 高级 State 目录：使用 FolderPicker + 只读路径 + 清除。

## 自动门禁

- [x] 源码禁止 WinForms / WPF / Python。
- [x] x64 / ARM64 WinUI publish。
- [x] 发布产物禁止 WinForms / WPF / Python Runtime 残留。
- [x] x64 启动 smoke test。
- [x] 单实例 smoke test。

> 此清单中的 UI 项表示代码侧已经纳入统一布局规则；每次用户反馈具体显示问题时，仍应以实际截图和最新 Artifact 复核。
