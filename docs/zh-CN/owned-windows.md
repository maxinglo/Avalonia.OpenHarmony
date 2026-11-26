<a id="chap-owned-windows"></a>

# HarmonyOS 从属窗口

[英文原文：`docs/owned-windows.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/docs/owned-windows.md) · [返回目录](../../README.zh-CN.md#contents)

应用可通过 `AppBuilder.With(new OpenHarmonyWindowOptions())` 为主 `WindowStage` 启用一个托管的 Avalonia `Window`。原有的 `ISingleViewApplicationLifetime.MainView` 成为这个主窗口的内容。在已挂载的控件上调用 `TopLevel.GetTopLevel(control)` 可取得其所属窗口。普通 Avalonia `Window` 可用 `Show(owner)` 显示非模态从属窗口，或用 `ShowDialog<TResult>(owner)` 显示模态对话框。API 26 也允许在从属窗口下继续嵌套原生子窗口。在自由窗口模式中，不传所有者调用 `Window.Show()` 会在同一 Stage 中创建独立辅助窗口；它与 Stage 归入同一个 Dock 任务，并在 Stage 关闭时一同关闭。见 [HarmonyOS 窗口类型](https://developer.huawei.com/consumer/en/doc/HarmonyOS-Guides/window-type-overview/)。

主窗口及其子窗口分别使用各自的 XComponent 帧缓冲区、输入路由，以及供系统输入法使用的 HarmonyOS 窗口 ID。子窗口的表面可分离、重新连接，而托管窗口仍保留。主窗口的 XComponent 表面被替换时，所有者窗口也继续存活。模态行为由 HarmonyOS 原生窗口系统处理；对话框打开期间，Avalonia 也会禁用所有者窗口的输入。

## HAP 接入

1. 调用 `registerSystemRequests(context, mainWindow, windowStage)`。在 `onWindowStageWillDestroy` 中释放返回的注册对象；随后在 `onWindowStageDestroy` 中调用共享模块 `ChildWindows.ets` 的 `reportStageDestroyed()`。要在 HarmonyOS 无障碍服务启用时提供子窗口语义，把第 4 个可选参数设为 `true`。每个表面单独注册。
2. 在模块的 `main_pages.json` 中加入 `pages/AvaloniaChildWindow`。可复用 ArkTS 目录中已包含该页面，生成的窗口键通过本地存储传递。
3. 从 NativeAOT 应用导出 `avaoh_update_child_surface`、`avaoh_close_child_surface`、`avaoh_report_child_frame`、`avaoh_report_child_event` 和 `avaoh_request_child_close`。键 `0` 用来传递主窗口通知和几何信息，也涵盖第一个 XComponent 表面创建前的事件。Counter 的实现位于 `samples/Counter/Program.Windows.cs`。
4. 若使用扩展主窗口标题栏，参照同一文件再导出 `avaoh_report_title_bar` 和 `avaoh_title_bar_press`。已有宿主不必实现这两个可选函数。若需子窗口安全区与固定键盘遮挡信息，参照 `samples/Counter/Program.Insets.cs` 导出 `avaoh_set_child_avoid_area`。
5. 使用 `-Windowed` 构建 Counter；HAP 位于所选 ABI 和构建模式对应的 `windowed` 子目录。打开模态对话框，试用计数、移动和调整大小命令，再选择 **Return dialog result**，检查主计数器仍能交互。

窗口化宿主默认使用软件帧缓冲区，也可以按 XComponent 启用 EGL。窗口装饰及其可用性取决于设备的窗口模式。若系统在创建时不接受指定的窗口矩形，可改用平台默认位置和大小创建子窗口。见 [EGL 选项](integration.md#chap-integration--skia-egl-rendering)。

## 桌面主窗口状态

在 HarmonyOS 自由窗口模式下，托管主窗口支持 `WindowState.Normal`、`Maximized`、`Minimized` 和 `FullScreen`。设置 `WindowState` 会把原生请求加入队列；读取属性及接收变更通知时，以窗口管理器实际观察到的状态为准，包括通过原生标题栏或 Dock 发起的变化。最大化使用常规最大化布局，全屏使用沉浸式布局。HarmonyOS 的 `recover()` 可恢复已保存的浮动窗口大小和位置。最小化的主窗口可从 Dock 恢复；以编程方式从最小化恢复，则要求 UIAbility 处于前台。见 [HarmonyOS 窗口 API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-window/)。

主窗口浮动时，可用 DIP 指定宽和高来请求新的客户区尺寸，窗口管理器会施加原生外框与尺寸约束。`CanResize` 控制从原生窗口边缘拖动缩放；`CanMinimize` 和 `CanMaximize` 控制对应标题栏按钮。最小化或换用新 XComponent 表面时，主窗口和托管内容保持关联。这些状态和标题栏控件适用于自由窗口模式的主窗口。子窗口仍使用专门的可见性、几何和关闭 API。

在自由窗口模式中，还可通过代码修改主窗口位置、标题、装饰和尺寸限制。设备与宿主具备 `ohos.permission.WINDOW_TOPMOST` 时，`Topmost` 会转发给原生置顶设置。`BeginResizeDrag` 让应用通过 Avalonia 手柄拖动浮动窗口，内部使用 SDK 的调整大小和移动调用；系统边缘拖动沿用原生路径。公开 API 不提供按窗口控制 Dock 可见性或动态设置位图图标的能力。见[窗口 API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-window/)与[置顶权限](https://developer.huawei.com/consumer/en/doc/HarmonyOS-Guides/permissions-for-all/#ohospermissionwindow_topmost)。

窗口化 Counter 通过 **Stage minimize**、**Stage maximize**、**Stage normal**、**Stage fullscreen**、**Stage resize** 和 **Stage caption policy** 发起对应请求。日志保存观察到的状态、位置、客户区和外框尺寸，以及托管与原生窗口标识。`tests/LifecycleProbe` 覆盖托管通知和表面替换；ArkTS 的 `WindowCloseProbe` 通过注入原生行为检查桌面请求路由。

<a id="chap-owned-windows--popup-and-transparency"></a>

## 弹出控件与透明度

Avalonia 的 ComboBox 等弹出控件使用所有者表面内的覆盖层。ArkUI 的原生弹窗 API 接受 ArkUI 构建器，并自行管理焦点、关闭和窗口作用域；使用 Avalonia 的覆盖层能让命中测试和焦点留在同一个窗口。见 [ArkUI 弹窗 API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/ts-universal-attributes-popup/)。

XComponent 报告不透明合成。窗口背景色作用于内容区域；PC 上的 `setWindowContainerColor` 针对主窗口容器，并固定非活动状态透明度。API 26 的 `setWindowContainerModalColor` 要求受限权限 `ohos.permission.SET_WINDOW_ALPHA`，子窗口的形状遮罩又使用固定窗口尺寸。这些 API 无法在宿主窗口及其系统容器之间提供一致、可实时变化的 Alpha 通道。见[窗口 API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-window/)与[受限权限](https://developer.huawei.com/consumer/en/doc/doccenter-capabilities/restricted-permissions/)。

## 扩展主窗口标题栏

在自由窗口模式下，对主窗口的 Avalonia `Window` 设置 `Window.ExtendClientAreaToDecorationsHint = true`，即可在标题栏区域绘制。HarmonyOS 的原生最小化、最大化、还原和关闭按钮仍叠在 Avalonia 表面之上，并遵循窗口原有的按钮策略。见[自由窗口适配指南](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/application-adaptation-freeform-window/)。

通过 `IsExtendedIntoWindowDecorations` 判断扩展是否实际生效。自定义标题栏中，`WindowDecorationMargin.Top` 提供标题高度，`WindowDecorationMargin.Right` 表示须为原生按钮预留的宽度，均以 DIP 为单位。用 `ExtendClientAreaTitleBarHeightHint` 请求自定义高度；后端至少会保留能够容纳系统按钮矩形的高度。默认高度取原生标题行。监听边距变化，可使布局持续对齐系统按钮。

标题栏扩展期间，按钮遵循主窗口实际使用的 Avalonia 主题；关闭扩展即恢复原生按钮颜色模式。见 [HarmonyOS 标题栏按钮样式](https://developer.huawei.com/consumer/cn/doc/harmonyos-references/arkts-apis-window-i/)。

要让浮动主窗口可拖动，在扩展标题栏内划出专门的拖动区域，从该区域的主指针 `PointerPressed` 处理函数调用 `Window.BeginMoveDrag(args)`，之后由 HarmonyOS 接管移动。**不要把可交互控件放进拖动区域。** Counter 的 **Toggle title bar extension** 启用自定义标题栏，**Title action** 演示放在拖动区域旁的可点击 Avalonia 控件。关闭扩展提示后恢复普通系统标题栏。见[窗口布局指南](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/window-layout/)。

## 子窗口策略与原生互操作

从属 `Window` 可设置 `MinWidth`、`MinHeight`、`MaxWidth` 和 `MaxHeight`。后端把这些客户区尺寸换算为物理像素，加上原生边框装饰的尺寸，再调用 `setWindowLimits`。窗口管理器还会施加自身限制。`CanResize` 控制原生边缘拖动；`WindowDecorations` 在创建时决定是否使用标题栏。已有原生标题栏的窗口更改此属性时，后端调用 `setWindowDecorVisible` 并重新应用当前缩放策略。是否生效取决于设备和窗口模式。

Counter 的 **Open constrained window** 打开带有最小、最大尺寸限制且禁用边缘拖动的子窗口，可用其中的按钮切换缩放策略和标题栏。子窗口活动时，**Focus owner** 请求将焦点转给主窗口。见[窗口 API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-window/)。

主窗口已有焦点时，`Activate()` 不执行新的焦点操作。焦点在非模态子窗口上时，可见的主窗口可通过 `WindowStage.shiftAppWindowFocus` 请求取回焦点。有待处理的模态窗口时，焦点保留在模态窗口路径上。见 [WindowStage API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-windowstage/)。

宿主提供原生 ID 后，`TryGetPlatformHandle()` 返回描述符为 `OH_WindowManager window ID` 的句柄。其值是整数，供接受窗口 ID 的 `OH_WindowManager_*` API 使用，并不会因 XComponent 表面被替换而改变。需要时从顶层窗口重新获取，仅在该窗口仍存活时使用。`WindowIcon` 接受 Avalonia 位图、流或路径，可保存 PNG 快照；HarmonyOS 的原生窗口图标则来自应用配置。

## 子窗口安全区与键盘遮挡

每个子窗口观察自身原生窗口的避让区域。后端将避让矩形与当前 XComponent 求交集，通过 `Window.InsetsManager` 和 `Window.InputPane` 以表面本地 DIP 报告结果。子窗口失焦时输入面板关闭；表面被替换时，两项状态均清空，直至获得新的几何信息。

可用性取决于设备和原生窗口模式。在自由窗口的自由浮动模式下，系统报告固定软键盘区域。另一些模式要求子窗口外框与主窗口一致才会提供避让区域。后端请求为模态窗口启用避让；系统拒绝时，窗口仍继续打开。见[窗口 API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-window/)。

在 Windows 上执行 `dotnet run --project tests/ChildInsetsProbe` 可用注入的原生几何数据测试宿主中的窗口。在设备上则让 Counter 子窗口的 TextBox 获得焦点，观察 HiLog 中的 `Input pane` 和 `Safe area`。综合版 API 26 x86_64 Counter 中，子窗口 TextBox 连接输入法后仍保持系统焦点；输入面板报告已打开，但遮挡矩形为空，因为子窗口位于主窗口报告的固定键盘区域上方。Windows 已覆盖子窗口与键盘重叠的几何情况；仍需在会提供子窗口避让区域的设备模式下检查真实重叠。

## 验证

Windows 测试在真实 Avalonia 所有者窗口与子窗口上检查了独立的 Skia 帧、模态输入阻挡、带类型的对话框结果、所有者重新激活、平台发起关闭，以及所有者在表面替换后的存活。x86_64 API 26 Counter HAP 包含实际的 NativeAOT 和 ArkTS 桥接层。在 Huawei_TripleFold 上，主窗口为 3184 × 1872，模态对话框独立呈现 1208 × 863 的帧。点按子窗口会使计数增加；同一操作作用于模态窗口背后的所有者时无效。关闭子窗口后，调用方收到 `1`，所有者恢复输入。

非模态从属窗口也有独立的帧；它移至物理坐标 (1036, 529)，宽度从约 420 DIP 调至 500 DIP，关闭后所有者仍可交互。综合版 Counter 中，子窗口 TextBox 将输入法连接到 WindowManager 报告的子窗口 ID；键盘打开时，焦点仍在该子窗口。

启用无障碍的 HAP 中，两个打开的表面都成功向 ScreenReader 注册，激活主按钮会增加主计数。API 26 x86_64 的实验随后验证了移动过的子窗口也能被激活，并用正式桥接实现复现：单点子窗口计数按钮获得焦点，在空白处双击触发该按钮，子计数变为 1。

修复关闭流程后的 HAP 重新验证了正常模态和非模态路径。模态子窗口独立呈现，阻挡对所有者的触摸，接受一次子窗口点按并返回 `1`。关闭后所有者再次接受点按；随后打开并关闭非模态子窗口，所有者仍可交互。

## 关闭窗口

托管子窗口一请求关闭，就立即停止接收输入；但在 HarmonyOS 确认销毁前，后端保留原生句柄和表面路由。销毁失败时，可解除原生模态状态、隐藏窗口并重试。带类型的对话框结果要等销毁确认才完成。若系统已确认子窗口隐藏或不再模态，所有者可在后台清理完成前恢复输入。Stage 本身属于 UIAbility。关闭它的托管 `Window` 后，同一 Stage 不会因表面大小改变而再次打开；新的 `WindowStage` 会创建新的托管所有者。

关闭回归测试包括显示失败后的清理、销毁多次被拒、重试、回退隐藏与解除模态，以及 Stage 切换期间销毁。Windows Avalonia 测试还检查了模态期间过期的 ScreenReader 操作。这些故障路径通过注入验证；模拟器冒烟测试走的是正常关闭流程。

使用 DevEco API 26 SDK 自带的 TypeScript 编译器运行 ArkTS 服务测试：

```powershell
$typescript = Join-Path $DevEcoRoot 'sdk/default/openharmony/ets/build-tools/ets-loader/node_modules/typescript/lib/typescript.js'
node tests/WindowCloseProbe/CloseProbe.cjs $typescript
```

参考：[HarmonyOS WindowStage API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-windowstage/)。
