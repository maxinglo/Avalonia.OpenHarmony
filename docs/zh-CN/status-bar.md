<a id="chap-status-bar"></a>

# HarmonyOS PC 状态栏图标

[英文原文：`docs/status-bar.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/docs/status-bar.md) · [返回目录](../../README.zh-CN.md#contents)

通过 `AppBuilder.With(new OpenHarmonyTrayOptions())` 启用托盘支持。参照 Counter 的 `EntryAbility.ets`，在 ArkTS UIAbility 中注册 `ets/services/StatusBar.ets` 提供的 `StatusBar`，并向 `registerSystemRequests` 的最后一个参数传入回调，由此为 HAP 接入华为 Desktop Extension Kit。销毁 `WindowStage` 时调用 `StatusBar.stop()` 并注销系统请求处理器。注册新的 Stage 后，调用 `bridge.reportTrayEvent(0n, '')`，重新发布仍处于活动状态的图标。

从 NativeAOT 应用导出 `avaoh_report_tray_event(long id, byte* menuCode, int length)`。在回调返回前解码 UTF-8 菜单标识，再调用 `OpenHarmonyHost.ReportTrayEvent`。完整导出实现见 Counter 的 `Program.Tray.cs`。图标点击和菜单命令由宿主投递到 Avalonia 调度器。

向用户提供托盘控件前，先等待 `OpenHarmonyTrayOptions.IsSupportedAsync()` 完成。它检查 ArkTS 系统能力，并在 API 26 能力查询可用时调用该查询。随后使用标准的 Avalonia API：`TrayIcon.SetIcons`、`IsVisible`、`ToolTipText`、`Icon`、`Clicked` 和 `Command`。

一个 HAP 只有一个系统图标位置，第一个挂载的图标占用它，直到该图标被移除或释放。未指定自定义图标或提示文字时，分别使用 HAP 图标和 UIAbility 标签。对于自定义图标，桥接层按状态栏尺寸解码 PNG，两种壁纸外观均使用同一图像。

托盘图标上的 `NativeMenu` 会成为系统右键菜单。支持以根级分隔符分组、一级子菜单、勾选项和位图图标；每一级最多 20 项。只有**可见、已启用且有标题**的条目才会导出。系统负责菜单呈现，并提供自己的应用退出操作。原生子菜单是平面的，没有禁用项、键盘快捷键或菜单打开事件字段。API 26 的窗口接口提供原生标题栏按钮，不提供应用自定义的系统窗口菜单；窗口内菜单可用 `NativeMenuBar` 绘制。见 [HarmonyOS 窗口 API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-window/)。

Counter 提供显示、更新和隐藏控件。左键点击系统状态栏图标可检查 Avalonia 点击处理；从右键菜单选择自定义项可检查菜单回调。PC 系统可能把图标直接放在 Dock 中，也可能放在其折叠面板内。在 API 26 x86_64 MateBook Pro 模拟器上，Counter 成功显示图标、收到左键点击，并从原生菜单和子菜单执行 Avalonia 命令。系统显示了更新后的悬停文字，隐藏再显示时，系统条目也随之移除并恢复。

参考：[PC 验收步骤](platform-validation.md#chap-platform-validation--desktop-integrations) · [华为状态栏开发指南](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/statusbar-extension-guide/) · [Desktop Extension Kit 的 statusBarManager API](https://developer.huawei.com/consumer/en/doc/doccenter-references/api/statusbar-extension-manager/)
