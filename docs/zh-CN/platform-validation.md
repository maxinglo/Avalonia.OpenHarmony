<a id="chap-platform-validation"></a>

# 平台验证记录

[英文原文：`docs/platform-validation.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/docs/platform-validation.md) · [返回目录](../../README.zh-CN.md#contents)

各接口的实现范围见[平台接口对照表](platform-capabilities.md#chap-platform-capabilities)。以下测试环境、结果与待验证项来自项目文档。

## API 26 PC 验收

整轮测试使用同一台 x86_64 MateBook Pro 模拟器。通过 HDC 确认 `const.ohos.apiversion=26`、`const.product.cpu.abilist` 包含 `x86_64`，且 `const.product.devicetype=2in1`。先测试窗口化 HAP，再在同一模拟器上安装 EGL HAP。注入输入时，使用本轮日志中的 `Control positions` 和 WindowManager 报告的当前窗口矩形。

<a id="chap-platform-validation--desktop-integrations"></a>

#### 桌面集成

| 接口 | 验收方法 |
| --- | --- |
| `INativeControlHostImpl` | 点击 Counter 里的 ArkUI 按钮，观察 Avalonia 中的计数变化。将按钮移到视口边缘，对照可见范围和视口，确认点击被裁掉的部分不会增加原生计数。隐藏、分离并重新挂载，观察原生控件的可见性、销毁和重建。 |
| `ITrayIconImpl` | 先检查设备能力。选择 **Show status bar icon**，系统状态栏或折叠区应出现一项；左键点击应触发 `TrayIcon.Clicked`。**Update status bar icon** 应更换图标与悬停文字。隐藏时条目消失，再显示时恢复。 |
| `INativeMenuExporter` | 在系统托盘中右键点击 Counter，选择 **Add one from tray**，再选择 **Tray menu options** 下的子项。检查命令是否送达，并重新打开菜单验证选中状态已更新。本项检查 `TrayIcon.Menu` 的导出。 |
| `IWindowImpl` | 依次用代码请求最大化、最小化、全屏与普通状态，对照系统外框和 Avalonia 窗口状态。切换标题栏扩展，比较 XComponent 与原生窗口的顶部位置。试用 **Title action**、拖动自定义标题，操作原生最大化、还原、最小化和关闭按钮；从 Dock 恢复后须是同一窗口，标题布局与内容状态保留。 |

PC 镜像中，桌面布局的 `DockStatusBarComponent_arrowbutton` 打开状态栏折叠区。打开后重新获取布局来定位图标。在同一条 HDC 命令中移动并点击，例如右击用 `hdc shell uinput -M -m X Y -i 150 -c 1`。系统菜单在独立窗口中绘制。

**控件与托盘。** 窗口化 Counter 在同一进程内嵌入了真实的 ArkUI 按钮。原始宽度 320 px，移动后系统报告可见宽度 189 px。只有点在可见区域才会增加原生计数。隐藏后再显示可保留计数；分离会释放节点，重新挂载会创建从 0 开始的新节点。Avalonia 计数器也能接受输入。托盘能力查询返回 `true`；Counter 收到系统图标上的左键事件，以及原生右键菜单的根级命令。两次选择原生子菜单项使选中状态先开启再关闭。系统接受了图标和菜单更新，悬停提示显示 `Avalonia Counter (1)`，隐藏和再次显示时条目也随之移除、恢复。独立 ArkTS 测试在 Counter 之前验证了同一套原生左键、右键回调。

**窗口外框与状态。** WindowManager 报告最大化后的主窗口为 3120 × 1955 px，全屏为 3120 × 2080 px。仅修改宽度可将浮动窗口由 2090 × 1394 px 调为 1178 × 1394 px。从全屏还原并等待系统过渡结束，窗口准确回到 (515, 281)，尺寸仍为 1178 × 1394 px。Counter 成功切换原生最大化、最小化按钮的可用策略。无论通过代码还是原生按钮最小化，都保留了原生窗口和 Avalonia 所有者。重新打开 UIAbility 或从 Dock 恢复，都回到同一原生外框；主计数在同一进程内从 1 增至 2。

**扩展标题栏。** 启用扩展后，Counter 的 XComponent 从原生窗口顶部开始绘制；缩放为 1.90 时，比原始起点高 70 px。自定义标题栏高 44 DIP，为系统按钮预留 140 DIP。**Title action** 收到鼠标点击；拖动空白标题区域使同一原生窗口移动 190 × 120 px。原生最大化和还原保留扩展标题栏；最小化后从 Dock 恢复，进程、窗口 ID 和浮动外框均不变。关闭扩展后表面回到原位置；再启用扩展并点击原生关闭按钮，WindowManager 中的该窗口被移除。

**AudioSynx Arc 交叉检查。** 同一模拟器上，一个隔离的调试 HAP 复用了该应用发布版的 Avalonia 视图，并启用扩展标题栏。使用原生标题高度 37 DIP、按钮预留宽度 140 DIP 时，在浮动和最大化状态下，品牌标识及主题图标的中心与三个原生按钮中心的垂直对齐误差均不超过 2 px。点击主题控件不会移动窗口；截图中三个按钮在浅色背景上使用深色图形，在深色背景上使用浅色图形。拖动标题使窗口移动 174 × 112 px。最大化和还原均保留扩展标题栏，最小化后从 Dock 恢复时，窗口 ID 和浮动外框不变。原生关闭按钮成功移除窗口。使用常规包标识安装的 AudioSynx Arc HAP 也复核了扩展外框、174 × 112 px 的拖动、原生窗口操作和明暗主题按钮对比度。

主窗口恢复后，原有模态流程仍可阻挡所有者输入，返回 `1`，关闭后所有者计数可增至 3。嵌入式 EGL 版收到 NativeVSync，呈现 2090 × 1213 的帧；Avalonia 与 ArkUI 计数器均响应点击。Windows 冒烟测试，以及托管生命周期、子窗口避让、文本拖动、托盘和 ArkTS 关闭测试均通过。

#### 渲染与从属窗口回归

| 检查项 | 通过条件 |
| --- | --- |
| 软件渲染、VSync 与调度器 | `-Windowed` HAP 收到首个 NativeVSync 回调，呈现软件渲染帧，点击后主计数增加。本地回调测试覆盖超时与缺少原生库时的回退。 |
| 原生从属窗口 | 非模态子窗口有独立的 WindowManager 外框，计数器响应输入；**Move child** 改变位置，**Resize child** 改变尺寸；关闭后所有者恢复操作。 |
| 模态 | 子窗口阻挡对所有者的点击，自身接受一次点击并返回 `1`。对话框关闭后，同一所有者能再次接受点击。 |
| 原生 ID 与尺寸限制 | **Open constrained window** 报告不同的 Stage 与子窗口 WindowManager ID，且均与系统一致。对照报告的限制与实际原生窗口尺寸，在支持的模式中切换边缘缩放及标题栏。 |
| 指针路由 | 用鼠标依次悬停位图光标、隐藏光标、文本编辑器和默认光标目标；在原生结果中查找 `32x24 hotspot=(7,5)`、`16x16`、`Text`、`Default`。要做视觉验证，需在系统指针可见时对照绘制位置和热点。 |
| EGL 与生命周期 | `-UseEgl` HAP 呈现 EGL 帧。用 PC 原生标题栏最小化，再重新打开 Counter；同一进程报告后台和前台事件，计数从 1 增至 2。 |

在 MateBook Pro 模拟器上，两种 HAP 均收到 NativeVSync 并呈现 2090 × 1213 的帧。窗口化版本将非模态子窗口移动了 48 × 24 px，原生外框由 798 × 570 调至 950 × 570 px。模态窗口返回 `1` 后，所有者恢复输入。主窗口和子窗口的句柄 ID 与 WindowManager 一致。点击 **Focus owner** 可在受限子窗口保持打开时，将系统焦点转回主窗口。

标题栏可见时，禁用边缘缩放，受限子窗口保持 798 × 602 px；启用后执行相同拖动，宽度达到系统限制 987 px。隐藏、恢复标题栏使原生窗口高度限制变化 70 px。Counter 报告一个 3120 × 2080 的显示器、3120 × 1955 的工作区以及 1.90 的缩放系数。系统接受了位图、透明位图、文本和默认光标的原生调用。EGL 版在最小化和恢复后保留进程与计数。该镜像拒绝了原生模态键盘避让请求，但对话框仍可使用。

运行 `dotnet run --project tests/LifecycleProbe` 可检查两种窗口焦点事件顺序以及模拟的双显示器快照。[光标测试](hardware-input.md#chap-hardware-input--verification)检查像素格式、Alpha、热点位置与原生资源释放；[文本拖动测试](text-drag-source.md#chap-text-drag-source--verification)检查记录顺序及传输大小限制。PC 单屏结果仅覆盖当前活动显示器；原生多显示器热插拔及多条文本记录的跨应用交付仍需各自进行设备检查。

#### API 26 PC 系统服务检查

| 功能 | PC 环境中的结果 | 补充覆盖与限制 |
| --- | --- | --- |
| 主窗口与辅助窗口 | 代码发起的移动和标题修改送达 WindowManager。另一独立应用获得焦点后，置顶主窗口仍在其上；取消置顶后，另一应用可升至其上。自定义缩放手柄使窗口边缘移动 155 × 75 px。嵌套模态窗口返回 `1`。独立的 420 × 300 DIP 窗口以 798 × 570 px 打开，可最大化至 3120 × 1955 px、还原为原始外框，最小化后重新打开仍使用原窗口 ID。 | 托管测试覆盖约束保留、窗口路由和单个表面的 EGL 回退。 |
| 原生托盘菜单图像 | **Add one from tray** 旁显示了带白色加号的紫色位图；点击原生菜单项执行了 Avalonia 命令。 | 托盘测试覆盖图标及菜单项更新。 |
| 指针与外观 | PC 输入的 F13 逻辑键与物理键一致；Avalonia 读到了系统强调色。模拟器注入的笔事件以普通触摸送达。 | 托管测试检查笔类型、压力、X/Y 倾角、修饰键。真实触控笔、悬停及多台外接显示器仍需硬件。 |
| 多格式剪贴板 | 安全粘贴按钮按顺序返回两个数据项，包含文本、HTML、位图和应用自定义数据。检查了精确的 Alpha 值、二进制字节以及空值。清空剪贴板后读回空结果。 | Windows ABI 测试检查粘贴遭拒及像素缓冲区的生命周期。 |
| 文件选择与启动请求 | 通过纯文本 MIME 筛选保存并重新打开 24 字节样本。系统返回修改时间与保存类型索引。直接查询常用目录未获授权。未注册的 URI 报告不可用，PC 镜像中没有可打开测试邮件链接的处理程序。 | 存储和启动器测试检查取消、目录授权，以及文件和目录启动请求的结构。 |
| 文本编辑器 | Counter 编辑器连接到 PC 窗口，通过原生 API 声明支持行内预览，并接收到硬件按键。 | Windows ABI 测试在 Avalonia TextBox 上检查预编辑回调、原生编辑命令及密码保护。 |
| 跨应用拖放 | Counter 的 Avalonia 按钮通过鼠标向独立 ArkUI 应用送达 4 次纯文本和 2 次多格式数据；其中 4 次数据晚于原生拖动检测到达，仍以 Copy 完成。纯文本预览为 292 × 79 px，通用卡片为 121 × 79 px。注入的触摸也以紧凑预览完成文本拖放。仅用于拖动的按钮未触发 Click，拖动普通按钮也未触发 Drop。对于拖入的文件，FileKit 拒绝写入只读样本；授予 WRITE 的样本则被写入，发送方可见标记。 | Move 由自动化测试覆盖。即使显式只授予 READ，PC 接收方仍能以写模式打开测试文件，因此拒绝所有文件拖出。[文件拖出限制](text-drag-source.md#chap-text-drag-source--outbound-file-scope) |

## Huawei_TripleFold 验证

Counter 使用日志标签 `AvaloniaOHOS`。文本框只记录长度及长度减少情况，不记录正文；收到示例剪贴板值时，报告固定的成功消息。日志还包含控件位置和显示缩放。输入诊断报告输入法连接、键盘状态、文本提交和删除请求；硬件按键诊断报告 Avalonia 是否处理事件及附带文本的长度。应用可通过 `OpenHarmonyInputDiagnostics` 启用这些日志。

| 能力 | 项目已验证的内容 | API 26 复测方法或待验证项 |
| --- | --- | --- |
| CPU 渲染与触摸 | 综合版 x86_64 模拟器 HAP 呈现了 3184 × 1872 的 CPU 帧，点按后报告 `Count: 1`。 | 打开 Counter，点 **Add one**，查看原生帧和 `Count: 1`。 |
| Skia EGL | x86_64 HAP 呈现了 3184 × 1872 的 EGL 帧；切后台后保持计数。在同一进程中依次切换完整、单屏、双屏、全宽折叠状态，计数仍能递增。Windows 验证了软件回退。 | 安装 `-UseEgl` HAP，检查 `Presented Avalonia EGL frame`。折叠状态变化前后分别点击 **Add one**。[DevEco 折叠控制](https://developer.huawei.com/consumer/en/doc/doccenter-deveco-studio/ide-emulator-command-line/) |
| 表面内弹出控件 | Windows Counter 打开了 `OverlayPopupHost`；综合版 x86_64 HAP 打开 ComboBox 并选中索引 1。 | 打开选择框，选择第 2 项，查找 `ComboBox opened` 和 `ComboBox selected: 1`。 |
| 系统输入法 | 输入法应用最初未运行时，x86_64 Counter 也能打开系统键盘。较早的设备测试收到多次软键盘提交；输入 `+` 后，TextBox 长度从 14 增至 15 个 UTF-16 代码单元。Windows C ABI 测试覆盖删除、选区和断开；另有模拟测试覆盖错误 `12800008` 的有限次数重试及取消。 | 点击 **Type here**，点一个软键，确认 `IME commit` 和文本长度变化。软键盘退格仍待设备检查。 |
| 系统剪贴板 | Windows C ABI 测试覆盖 TextBox 复制粘贴、Unicode、HTML、多条记录及拒绝读取。模拟器上安全按钮返回授权状态 0，示例文本以 16 个 UTF-16 代码单元粘贴到 TextBox。 | 选择 **Copy sample text**，让 **Type here** 获得焦点，再点击可见的 ArkUI 粘贴按钮，查找 `Sample text copied` 和 `Sample text pasted`。可用其他应用复制的文本再次检查。[剪贴板](clipboard.md#chap-clipboard) |
| 鼠标与键盘 | Windows Avalonia 控件处理了鼠标、滚轮、缩放坐标、快捷键、拉丁文本、NumLock 与失焦。模拟器中，一次硬件 Backspace 完整删除末尾的 emoji，长度从 16 变为 14 个 UTF-16 代码单元。 | 用鼠标移动、点击，在 Counter 上滚动，然后用物理键盘输入。检查 `Wheel:` 及长度日志，并检查文本与手形光标。[硬件输入](hardware-input.md#chap-hardware-input) |
| 文件与目录选择 | Windows 验证了临时 URI 保留、描述符所有权、Unicode、流读写、取消及能力标记。x86_64 模拟器通过系统选择器保存、重新打开同一份 24 字节文件。 | 先选 **Save sample text**，再选 **Open text file** 并打开同一文件，均应报告 24 字节。支持目录选择时，**Choose folder** 报告目录条目数。[文档存储](storage.md#chap-storage) |
| 链接与文件启动 | 窗口服务桥接层及 ArkTS 启动器可针对 API 26 构建；Counter 冒烟测试覆盖启动请求及相对 URI 拒绝。 | 选择 **Open Avalonia website**，观察系统浏览器或匹配的处理程序是否打开。启动文件需要文件可访问且有对应类型的处理程序。 |
| 生命周期、返回、主题与 DPI | Windows 测试覆盖帧暂停与恢复、激活、初始及后续外观、返回键处理和超时、显示坐标。模拟器初始缩放为 2.88。XComponent 调整大小时，ArkTS 读取当前缩放。 | 将 Counter 切到后台再恢复、切换系统颜色模式、调整设备尺寸或折叠。在系统报告变化时检查主题与 `DPI scale:` 日志；返回事件会记录 `System Back received`。 |
| 安全区与输入面板 | x86_64 EGL HAP 报告固定键盘遮挡约 1107 × 232 DIP，键盘关闭后清空。切换沉浸布局后报告边到边显示及系统栏安全区内边距。Windows 已覆盖移动、缩放及 ArkUI 已避让的空间。 | 让编辑器获取焦点，再关闭固定键盘，检查 `Input pane: Open`、`Input pane: Closed`。切换沉浸模式，检查 `Safe area` 与窗口布局。 |
| 中文候选与行内预览 | 同一 TextBox 收到“奴”和“努力”。可选回调跟踪确认小艺输入法接受了支持预览的编辑器；本次选词调用 `InsertText`，没有调用 `SetPreviewText` 或 `FinishTextPreview`。Windows ABI 测试覆盖行内渲染、绑定、选区和撤销。 | 用中文软键盘选词。要验证原生行内显示，须换用会发送预览回调的输入法重测。[组合输入验证](ime-preedit.md#chap-ime-preedit) |
| 系统拖入 | x86_64 系统分屏下，独立 ArkTS 应用向 Counter 主窗口及原生从属窗口分别拖入 Unicode 文本和只读文件。每个窗口均收到 10 个 UTF-16 代码单元的文本，并读到 29 字节文件；源端对 4 次操作均报告 Copy 成功。 | 分屏打开 Counter 与配套的拖动源应用，在两个窗口的接收目标上分别放下两种样本。[拖入接入](drag-and-drop.md#chap-drag-and-drop) |
| 系统文本拖出 | API 26 x86_64 上，从主窗口及从属窗口触摸拖出的 16 个 UTF-16 代码单元完整到达另一 ArkTS 应用。两端均报告 Copy。拖到空白区域则遭拒。 | 分屏长按 **Drag Avalonia sample text**，移至另一应用的文本接收目标并松开。再从子窗口重复，并尝试在空白区域松开。[拖出接入](text-drag-source.md#chap-text-drag-source) |
| 限定范围存储与书签 | Windows 测试覆盖授权、书签令牌恢复和授权目录内的操作。专用 API 26 x86_64 测试在进程重启后读回同样的 24 字节。系统报告撤销成功，但第 3 个进程仍可打开文件。 | 需在已启用相应功能的目标真机上验证授权与撤销；目录移动、删除也待设备检查。[存储范围](storage.md#chap-storage) |
| 无障碍 | 正式桥接构建中，HarmonyOS ScreenReader 激活了启用无障碍的主按钮。移动原生子窗口并让其获得焦点后，单点可聚焦其计数按钮，在空白处双击会恰好激活一次；同一轮也独立增加了主计数。Windows 测试检查 Arc 的名称、角色、边界和状态，及模态期间拒绝来自所有者的过期操作。滚动测试覆盖前后翻页、回退到横向、按比例换算像素偏移，并拒绝过期或被模态阻挡的操作。原生滚动桥接层已为两个 API 26 ABI 完成链接。 | 启用示例无障碍和系统 ScreenReader，聚焦 **Add one** 并执行系统激活手势。窗口化版本中，移动子窗口后再次操作其计数器。验证系统滚动时限制可视高度，让内容溢出，检查能否翻页并停在两端。 |
| 从属窗口与模态对话框 | 窗口化 x86_64 Counter 独立呈现对话框帧，模态期间拒绝所有者触摸、接受子窗口触摸，`ShowDialog` 返回 `1`，关闭后所有者恢复输入。非模态窗口可移动、宽度由约 420 调至 500 DIP，并正常关闭。综合 HAP 的输入法连接与 WindowManager 焦点均指向子窗口。修复关闭流程后的 HAP 重新通过两类正常开关流程。原生关闭遭拒及重试使用故障注入验证。 | 运行 `-Windowed` 示例，测试 **Open modal dialog** 和 **Open owned window**。[从属窗口接入](owned-windows.md#chap-owned-windows) |

默认嵌入式宿主使用单个应用视图，菜单和下拉框均由 Avalonia 提供。单独的 EGL 旋转测试仍未完成。当前 PC 拖动源还支持 HTML 和网页链接；文件的限制见[文件拖出范围](text-drag-source.md#chap-text-drag-source--outbound-file-scope)。

## ScreenReader 滚动检查

在 API 26 上用 `-Abi x86_64 -Windowed -ScreenReaderScroll` 构建 Counter。它会另行准备一个 HAP：启用无障碍，Avalonia 表面高 280 vp，使 Counter 的内容垂直溢出，并记录原生滚动请求及发布的偏移。要在 DevEco Studio 打开已准备的工程并签名，可加 `-StageOnly`。

1. 启用 ScreenReader、打开 Counter，单点 **Add one** 使其获得焦点。`AvaloniaOHOS` 日志中应出现 `Accessibility scroll position`，偏移为 `0 px`，且 `forward=1 backward=0`。
2. 在 **Settings > Accessibility > ScreenReader > More settings > Custom gestures**（设置 → 无障碍 → 屏幕朗读 → 更多设置 → 自定义手势）中确认向前和向后滚动各自绑定的手势。例如“先左后右”对应向前滚动，就在 Counter 表面按住鼠标，不松开，向左再向右，最后松开；若反向手势绑定了向后滚动，则反向操作。系统也支持双指滚动。使用带测试插桩的 UiTest 时，通过 `Driver.injectMultiPointerAction` 注入两条平行的纵向触摸路径。
3. 先确认日志出现含 `direction=forward` 的 `Accessibility scroll request`，再确认**同一表面、同一节点**报告的偏移增大，并能看到页面下方的控件。重复直到 `forward=0`，再向后滚动，直到偏移回到 `0 px` 且 `backward=0`。朗读反馈另行观察。**仅有位置变化不能证明无障碍滚动已触发**，因为普通触摸也会改变偏移。

API 26 Huawei_TripleFold 测试已发布初始偏移，并在普通触摸滚动后更新偏移。已安装的独立 UiTest 运行器在连续手势测试中未返回 Driver。系统滚动操作能否到达原生回调，仍须在手势路径可用的设备上验证。

参考：[ScreenReader 手势](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-universal-attributes-accessibility/) · [向前与向后手势](https://consumer.huawei.com/cn/support/content/zh-cn16030881/) · [UiTest 指针路径](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-uitest/)
