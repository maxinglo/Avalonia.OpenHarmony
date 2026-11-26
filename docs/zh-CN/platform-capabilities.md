<a id="chap-platform-capabilities"></a>

# Avalonia 平台接口对照表

[英文原文：`docs/platform-capabilities.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/docs/platform-capabilities.md) · [返回目录](../../README.zh-CN.md#contents)

本表描述 Avalonia 12.1.6 在 HarmonyOS API 26 上的后端。默认宿主嵌入一个 XComponent。可选的窗口化宿主在 `WindowStage` 中提供一个 Avalonia 主窗口，并支持原生从属窗口。

| 接口 | 已实现的功能 | 适用范围与限制 |
| --- | --- | --- |
| `IRenderTimer` | 可用时，由 HarmonyOS NativeVSync 驱动共享的 Avalonia 渲染循环。 | 原生时钟不可用、申请下一帧失败或不再送达帧回调时，回退到托管计时器。[NativeVSync API](https://developer.huawei.com/consumer/en/doc/harmonyos-references-V14/_native_vsync-V14/) |
| `IDispatcherImpl` | `ManagedDispatcherImpl` 在宿主专用线程上提供 Avalonia UI 调度器。 | 原生和 ArkTS 回调将任务投递到该调度器；ArkUI 的操作仍在 ArkUI 线程执行。 |
| `IPlatformGraphics` | 嵌入视图、窗口化主窗口和从属窗口可选用 Skia EGL。 | PC Counter 的主窗口和子窗口都成功呈现了帧。托管测试验证：某一窗口的渲染路径失败后，会保留该 `Window` 并切回软件渲染。[渲染接入](integration.md#chap-integration--skia-egl-rendering) |
| `IPlatformSettings` | 提供系统深浅色模式和强调色。 | PC Counter 已通过 Avalonia 读到当前强调色。SDK 手势识别阈值与 Avalonia 平台指标描述的操作不同，因此仍使用 Avalonia 默认交互参数。 |
| `ICursorFactory` | 按 HarmonyOS 窗口应用标准光标，或带像素热点的自定义位图光标。 | 标准样式映射为系统最接近的形状；自定义和隐藏光标依赖原生输入支持。 |
| `IPlatformIconLoader` | 从文件、流或位图读取 Avalonia 窗口图标数据。 | 应用图标仍由 HAP 资源提供；窗口适配器没有动态设置标题栏图标的接口。 |
| `INativeControlHostImpl` | 通过 `NativeControlHost` 和 `OpenHarmonyNativeControlHandle` 嵌入 ArkUI NDK 控件。 | PC Counter 已验证点击、矩形裁剪与命中测试、隐藏、释放和重建。原生控件位于 Avalonia 表面上方。[原生 ArkUI 控件](native-controls.md#chap-native-controls) |
| `IPlatformHandle` | 确定对应的窗口后，可取得其原生 WindowManager ID。 | 描述符 `OH_WindowManager window ID` 表示供 WindowManager API 使用的整数。它与 XComponent 表面令牌、图形指针各有独立的生命周期。 |
| `IPlatformDragSource` | 可启用鼠标或触摸，把文本、HTML 和网页链接从 Avalonia 控件拖出。 | PC Counter 的按钮通过紧凑预览向独立应用发送了数据；拖入文件的 READ、WRITE 授权符合预期。文件拖出返回 `None`：即使显式只授予 READ，独立 PC 接收方仍能打开测试文件进行写入。[拖入](drag-and-drop.md#chap-drag-and-drop) · [拖出](text-drag-source.md#chap-text-drag-source) |
| `IActivatableLifetime` | 以 `ActivationKind.Background` 报告应用前后台事件。 | 窗口焦点与应用激活状态分别跟踪。UIAbility 的后台请求是异步的，Stage 宿主恢复前台亦受限，因此 `TryEnterBackground` 和 `TryLeaveBackground` 均返回 `false`。[UIAbilityContext](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-inner-application-uiabilitycontext/) |
| `IScreenImpl` | 枚举显示器边界、缩放、方向、主屏标记和工作区。 | PC Counter 报告了实际显示器与工作区；多显示器路由使用注入的快照验证。无工作区时使用显示器边界；无法确定位置的非主显示器不列出。[Display Manager API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/capi-oh-display-manager-h/) |
| `IWindowImpl` | 支持自由窗口模式的主窗口操作、扩展标题栏、原生从属与嵌套窗口，以及同一 Stage 内的独立辅助窗口。 | PC Counter 验证了位置、标题、置顶、系统窗口装饰、自定义缩放手柄、嵌套模态结果，以及独立窗口缩放、最大化、最小化和还原。不能动态设置单个窗口的位图图标或 Dock 任务可见性。辅助窗口共享 Stage 的任务。[窗口接入](owned-windows.md#chap-owned-windows) |
| `ITrayIconImpl` | 通过华为 Desktop Extension Kit 按需提供 `TrayIcon`。 | PC Counter 验证了状态栏注册、左键点击、更新图标和悬停提示、隐藏及重新显示。每个应用只有一个系统位置，并须先查询设备支持情况。[状态栏](status-bar.md#chap-status-bar) |
| `INativeMenuExporter` | 将 `TrayIcon.Menu` 及位图图标导出到原生状态栏右键菜单。 | PC Counter 显示了菜单项图标，并从原生菜单执行了 Avalonia 命令。支持根级分组、一级子菜单，每级最多 20 项；没有窗口菜单、禁用项、快捷键或菜单打开事件字段。窗口内菜单可用 `NativeMenuBar` 绘制。[状态栏](status-bar.md#chap-status-bar) |
| `ITopLevelImpl` | 在 Avalonia 覆盖层中显示弹出控件；XComponent 按不透明表面呈现。 | ArkUI 弹窗需要原生构建器。窗口透明度 API 作用于不同图层，受设备状态和权限限制，因此保留 Avalonia 覆盖层，并报告为不透明合成。[弹出控件与透明度](owned-windows.md#chap-owned-windows--popup-and-transparency) |
| `ITextInputMethodImpl` | 原生文本输入、选区、预编辑，以及全选、剪切、复制、粘贴。 | PC 编辑器成功连接并接受键盘输入。Windows 原生 ABI 测试验证了真实 `TextBox` 的编辑命令、隐私保护和预编辑。系统粘贴需要授权；原生 TextConfig 没有输入建议、首字母大写、大小写或语言区域偏好的设置方法。私有命令需与输入法约定协议。[输入法说明](ime-preedit.md#chap-ime-preedit) |
| `IClipboard` | 通过系统剪贴板处理文本、HTML、位图和应用自定义格式。 | PC Counter 使用安全粘贴按钮验证了两个多格式数据项的写入和读回，以及清空后的空结果。文件 URI 的临时授权仍需独立验证。[剪贴板](clipboard.md#chap-clipboard) |
| `IStorageProvider` | 原生文件和目录选择、MIME 类型筛选、查询已授权的公共目录。 | PC Counter 通过 MIME 筛选保存并重新打开了 24 字节样本，取得保存类型索引及修改时间。打开文件时 SDK 不返回类型索引；直接查询公共目录需要单独授权。[文档存储](storage.md#chap-storage) |
| `ILauncher` | 网页、自定义深度链接、`mailto`，以及文件或目录 URI 处理程序。 | 未注册的链接返回不可用；测试用系统镜像没有已安装的处理程序来打开被测邮件 URI。文件或目录能否打开，取决于是否装有对应系统类型的处理程序。 |
| 硬件输入 | 鼠标、手指触摸、笔尖与悬停、键盘、滚轮、触控板滚动。 | PC Counter 收到鼠标、触摸和 F13 事件；托管测试验证了笔压、倾角与修饰键。模拟器注入的笔事件实际是普通触摸，真实笔输入和悬停仍需相应硬件验证。[硬件输入](hardware-input.md#chap-hardware-input) |

各接口使用的设备和测试程序见[平台验证记录](platform-validation.md#chap-platform-validation)。
