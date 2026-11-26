<a id="chap-text-drag-source"></a>

# 发起系统拖放

[英文原文：`docs/text-drag-source.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/docs/text-drag-source.md) · [返回目录](../../README.zh-CN.md#contents)

在应用侧使用 `AppBuilder.With(new OpenHarmonyTextDragOptions())`，在表面上设置 `AvaloniaSurfaceHost({ enableTextDragSource: true })`，并注册共享的 ArkTS 系统请求处理器。原生从属窗口还要将第 6 个参数设为 `true`：`registerSystemRequests(context, mainWindow, windowStage, enableChildAccessibility, enableChildInboundDrop, true)`。

调用 `DragDrop.DoDragDropAsync`，传入 Avalonia 控件收到的**原始** `PointerPressedEventArgs`、一个 `DataTransfer` 对象及允许的操作。Counter 给仅用于拖出的 Avalonia `Button` 注册隧道路由处理函数，使普通点击处理不会先消耗手势。ArkUI 表面按对应的按下事件启动系统拖动；触摸路径通过 `DragController` 使用原始 ArkUI 触点 ID。

鼠标与触摸路径都根据待传数据生成尺寸受限的预览。鼠标原生拖动检测若早于数据到达，ArkUI 会短暂等待，并使用紧凑的文本类型卡片。窗口和其他控件不会出现在预览里。见 [HarmonyOS 拖放指南](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-common-events-drag-event/)。

拖动源支持标准文本、`text/html`，以及通过 `text/uri-list` 表示的 HTTP(S) 链接。单次最多 32 条记录，UTF-8 数据最多 64 KiB。接收方可选择 Copy 或 Move；只有发送方声明支持该操作时，才能收到相应结果。拒绝或取消返回 `None`。松开指针、失焦或销毁表面，也会使尚未处理的手势失效。

<a id="chap-text-drag-source--outbound-file-scope"></a>

## 文件拖出的限制

凡声明了 `DataFormat.File` 的数据一律拒绝，包括文件与文本混合的情况。在 API 26 PC 测试中，即使发送方在 ArkUI 拖放记录上声明一次性 READ 策略，独立接收方仍能通过 FileKit 以读写模式打开一个应用私有的测试文件。因此，源端会**先检查格式，再读取任何数据**。接收拖入文件是另一条路径，使用接收方检查过的显式授权策略。见 [UDMF URI 授权](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-data-unifieddatachannel/)。

<a id="chap-text-drag-source--verification"></a>

## 验证

Windows 控件测试从路由触摸按下事件调用公开的 `DragDrop.DoDragDropAsync`，检查 Unicode 数据和每次按下事件的标识在桥接后保持不变，并检查异步完成和数据释放。ArkTS 状态测试覆盖松开、取消、失焦、过期、并发手势及拖动源销毁。Windows 的 `tests/TextDragSourceProbe` 回归测试还覆盖鼠标与触摸手势 ID、读取前拒绝纯文件或混合数据、有序 Unicode 文本、HTML、网页链接、Copy/Move 及记录数和字节数限制。

PC 已验证用鼠标复制纯文本及文本、HTML、网页链接的组合，也覆盖了数据晚于原生拖动检测到达的情况。同一模拟器还将注入的触摸拖动送达了另一应用。Move 由自动化测试覆盖。API 26 ARM64 工具链成功将 ArkTS 和 C++ 桥接层打入一个未声明任何应用权限的测试包；ARM64 上的实际拖动行为仍待设备验证。

DevEco API 26 x86_64 已编译并打包原生桥接层和 ArkTS Counter。Huawei_TripleFold 模拟器在系统分屏模式下，分别从 Counter 主窗口和从属窗口将文本拖入独立 ArkTS HAP。接收方得到完整的 16 个 UTF-16 代码单元；两个窗口均报告 Copy。拖到空白区域则被系统拒绝。

**复测方法：** 在具有系统拖放服务的 API 26 设备或模拟器上，将 Counter 与具有纯文本接收目标的 ArkUI 应用并排或分屏打开。把 **Drag Avalonia sample text** 拖到目标上，确认对方收到 `Avalonia — 中文 🎧`，并且 Counter 报告 **Sample text dragged: Copy**。再次拖动，在目标外松开，应看到 **Sample text drag canceled**。Counter 的 **Drag source** 日志给出物理坐标；从属窗口中查找 **Child drag source** 及带 **Child** 前缀的完成日志。
