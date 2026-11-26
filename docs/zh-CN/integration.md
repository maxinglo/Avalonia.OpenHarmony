<a id="chap-integration"></a>

# 在 HAP 中接入 Avalonia

[英文原文：`docs/integration.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/docs/integration.md) · [返回目录](../../README.zh-CN.md#contents)

1. 在 NativeAOT 应用中引用 `src/Avalonia.OpenHarmony/Avalonia.OpenHarmony.csproj`。按常规方式构建 `AppBuilder`，将创建它的工厂函数以及原生帧回调、错误回调传给 `OpenHarmonyHost`。在 `Application.OnFrameworkInitializationCompleted` 中设置 `ISingleViewApplicationLifetime.MainView`。
2. 使用 `UnmanagedCallersOnly` 从 NativeAOT 库导出 `avaoh_resize`、`avaoh_destroy` 和 `avaoh_touch`，并将调用转发给宿主。示例的 `Program.cs` 给出了函数签名，以及与之配套的 `avaoh_present`、`avaoh_report` 导入声明。
3. 将 CMake 缓存变量 `AVALONIA_OHOS_APP_LIBRARY` 设为 NativeAOT 共享库的文件名，使用 `add_subdirectory` 引入 `src/Avalonia.OpenHarmony.Native`。生成的 ArkTS 原生模块为 `libavalonia_ohos_bridge.so`。把 `src/Avalonia.OpenHarmony.ArkTS` 的内容复制到 HAP 模块的 `src/main` 目录。从 `ets/components/AvaloniaSurfaceHost.ets` 导入 `AvaloniaSurfaceHost`，让它占用 ArkUI 入口页上分配给 Avalonia 的区域。创建渲染表面（surface）**之前**调用 `configureFiles(this.context.filesDir)`，以便 Fontconfig 找到 HarmonyOS 系统字体。
4. 将托管应用生成的共享库和 `scripts/PrepareOpenHarmonyGraphics.ps1` 准备的图形库放入 `entry/libs/<abi>`。打包资源中还应包含后端、运行时、SDK 的 C++ 库及图形依赖项各自适用的许可文件。

`OpenHarmonyHost` 使用独立的 Avalonia 调度器。表面和触摸回调可能来自 ArkTS 线程；宿主会将任务放入调度器队列。帧回调接收预乘 Alpha 的 RGBA8888 帧缓冲区，地址只在本次回调期间有效。ArkTS 中的表面 ID 保持为字符串，C++ 则为每个表面实例另行分配单调递增的标识符。发送给 C# 宿主的触摸坐标以物理像素为单位。

## 系统服务

加载表面所在页面之前，用 `registerSystemRequests(this.context)` 注册窗口的 ArkTS 系统请求处理器。其实现位于 `ets/services/SystemRequests.ets`。销毁 `WindowStage` 时注销处理器，使挂起的请求得以结束。用 `OpenHarmonySystemRequests.RequestAsync` 对接 `OpenHarmonyLauncher` 与 `OpenHarmonyStorageProvider`，并将二者传给 `OpenHarmonyHost`。它们支持打开 HTTP(S) 链接、按文件关联启动应用，以及调用系统文档选择器。

所选文件保留其原始授权 URI。桥接层会复制文件描述符，并将副本的所有权交给托管代码，用于文件和目录操作。见[文档存储](storage.md#chap-storage)。

可以在表面旁放置 HarmonyOS 安全粘贴按钮 `PasteButton`。在成功回调里调用 `bridge.requestPaste()`；目标 NativeAOT 库可实现 `avaoh_paste`，将请求转发到 `OpenHarmonyHost.PasteToFocusedTextBox()`。见[系统剪贴板](clipboard.md#chap-clipboard)。

嵌入视图和从属窗口均可按需接收系统拖入的纯文本与只读文件。格式和可写文件的授权条件见[接收系统拖放](drag-and-drop.md#chap-drag-and-drop)。HarmonyOS PC 还可显示 Avalonia 托盘图标及其原生右键菜单，见[状态栏图标](status-bar.md#chap-status-bar)。

## 系统输入法

将提供原生窗口 ID 的函数作为宿主构造函数的第 4 个参数，即可启用 HarmonyOS 输入法。在加载 XComponent 页面前，从 ArkTS 调用 `configureInputMethod(windowStage.getMainWindowSync().getWindowProperties().id)`。Counter 通过 `avaoh_ime_window_id` 读取 ID。Avalonia 文本客户端获得焦点时，后端连接原生编辑器；失焦或表面销毁时断开连接。

后端将已提交的 UTF-16 文本、删除操作、选区变化和回车转发给 Avalonia。普通文本框会向输入法提供插入点和周围文本；启用 Avalonia 的敏感文本或密码选项后，不再共享周围文本。见 [HarmonyOS 原生输入法指南](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/use-inputmethod-in-custom-edit-box-ndk/)。

## 窗口生命周期、主题与显示器

Counter 的 `PlatformWindow.ets` 转发窗口显示、隐藏和焦点变化；UIAbility 单独转发应用进入前台或后台的事件。窗口隐藏或 UIAbility 进入后台时，Avalonia 暂停嵌入式渲染器，但保留视图。窗口不再活动时清除输入焦点。应用可通过 `Application.TryGetFeature<IActivatableLifetime>()` 订阅 `Background` 类型的激活状态变化。见 [HarmonyOS 窗口生命周期](https://developer.huawei.com/consumer/en/doc/HarmonyOS-Guides/window-lifecycle/)。

UIAbility 的颜色模式配置决定初始主题，后续变化会更新 Avalonia 的 `IPlatformSettings`。应用要跟随系统主题，应使用 `ThemeVariant.Default`。Native Display Manager 提供显示器的主屏标记、边界、像素密度和方向。结合宿主窗口和 XComponent 的偏移量，可在屏幕坐标与客户区坐标之间转换。系统无法报告可用工作区时，以显示器边界代替。见 [Native Display Manager](https://developer.huawei.com/consumer/en/doc/harmonyos-references/capi-oh-display-manager-h/)。

ArkUI 入口页将同步回调 `onBackPress` 转给 `TopLevel.BackRequested`。处理函数可设置 `Handled` 以覆盖系统默认返回行为。处理函数应尽快返回，不要在 Avalonia 线程上同步等待 ArkUI。若请求在 150 ms 内尚未开始处理，则交还 ArkUI 执行默认导航；若已开始，桥接层会在到达时限时将请求标记为已处理，以免导航两次。见 [ArkUI 入口页生命周期](https://developer.huawei.com/consumer/en/doc/harmonyos-references/ts-custom-component-lifecycle/)。

可选 NativeAOT 导出函数见示例 `Program.Platform.cs`。窗口化宿主支持隶属于其 `WindowStage` 的原生子窗口。本篇英文原文将应用主动切换前后台、系统强调色和系统字体缩放列为待办。**译注：** [平台接口对照表](platform-capabilities.md#chap-platform-capabilities)及[验证记录](platform-validation.md#chap-platform-validation)已单独记载 PC 端成功读取系统强调色；不要把这三项都理解为尚未实现。

## 安全区与输入面板

共享的 ArkTS 宿主先读取窗口初始避让区域，再订阅 `avoidAreaChange`。系统栏或屏幕挖孔与 XComponent 实际客户区重叠的部分，才计入 Avalonia 的安全区内边距。先用窗口本地物理像素计算交集，再按表面的渲染比例换算；ArkUI 已避让的空间不重复计算。见 [HarmonyOS 沉浸式布局](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/immersive-window-feature/)。

`TopLevel.InputPane` 使用 Avalonia 客户区坐标报告固定键盘及其遮挡范围。ArkUI 若已缩小表面避开键盘，键盘可以是打开状态，而遮挡矩形为空。只有高度、没有位置的通知不足以推断遮挡范围；浮动键盘不产生避让边距。通知报告收到的几何信息，不提供键盘动画曲线。见 [HarmonyOS 键盘避让](https://developer.huawei.com/consumer/en/doc/HarmonyOS-Guides/window-faqs/)。

可选的控制命令复用 ArkTS 系统请求服务。把主窗口传给 `registerSystemRequests(context, mainWindow)`，保留其清理回调，再调用 `host.UseInsetsCommands(OpenHarmonySystemRequests.RequestAsync)`。应用可请求沉浸式布局、控制状态栏与三键导航栏的可见性，或设置状态栏背景。Avalonia 从系统回读实际窗口模式、系统栏预留区域和状态栏颜色。系统报告预留区域为零时，栏的可见性为“未知”，并不等于“隐藏”。可见性和颜色命令取决于设备类型和窗口模式；布局以 XComponent 的实际几何信息为准。见 [HarmonyOS 窗口系统栏](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-window/)。

## 无障碍

Counter 的无障碍选项属于实验功能，默认关闭。该选项与系统无障碍服务同时启用后，XComponent 旁一个独立、不参与绘制的 ArkUI 节点会向系统提供无障碍语义。Avalonia 调度器为可见的自动化对等对象生成快照，包含角色、名称、边界、启用状态、焦点状态和可否执行 `Invoke`。边界以相对于该 `CustomNode` 的物理像素表示。ArkUI 查询缓存的快照，并在返回屏幕坐标时应用宿主变换。`Invoke` 操作调度回 Avalonia；宿主重新核对当前表面的注册信息和节点后，才调用 `IInvokeProvider`。

在 `AvaloniaSurfaceHost` 上设置 `enableAccessibility: true`，并启用 HarmonyOS 无障碍服务。应用库可参照 Counter 导出 `avaoh_accessibility_configure` 和 `avaoh_accessibility_invoke`。要支持滚动，再导出 `avaoh_accessibility_action`，将 `surface`、`registration`、`id` 和 `OpenHarmonyAccessibilityAction` 转发到 `OpenHarmonyHost.InvokeAccessibility`。仅有前两个导出函数时，按钮的 `Invoke` 仍可工作。

可见、已启用且按物理长度计量视口的 `ScrollViewer` 会公布可用的向前、向后翻页操作，并以物理像素报告位置。向前翻页优先使用有溢出的纵轴，否则使用横轴。按逻辑项目数计量的视口没有可通过此接口报告的像素偏移。关闭无障碍或替换表面时，桥接层清空快照。已测操作见[平台验证记录](platform-validation.md#chap-platform-validation)；原生提供程序 API 见 [XComponent 无障碍指南](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/ndk-accessibility-xcomponent/)。

## 从属窗口

启用窗口化 `WindowStage` 后，即可显示原生 Avalonia 子窗口和模态对话框。见[从属窗口接入](owned-windows.md#chap-owned-windows)。

<a id="chap-integration--skia-egl-rendering"></a>

## Skia EGL 渲染

默认使用软件帧缓冲区。要启用 Skia 的 OpenGL ES 渲染器，在 `AppBuilder` 上添加 `With(new OpenHarmonyRenderingOptions { UseEgl = true })`。嵌入视图、窗口化主窗口和原生从属窗口均可使用。每个窗口的渲染路径绑定自己的当前 XComponent；若其中一个窗口的 EGL 路径失败，只将该窗口切回软件渲染，保留原 Avalonia 窗口。创建新表面后可再次尝试 EGL。

构建 Counter 时，向 `scripts/Build-Counter.ps1` 传入 `-UseEgl` 或 `-Windowed -UseEgl`。对应的包分别写入 `egl` 或 `windowed/egl` 子目录。在 HarmonyOS 的 `AvaloniaOHOS` 日志标签下，`Presented Avalonia EGL frame` 条目会包含表面令牌。
