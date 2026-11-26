<a id="chap-drag-and-drop"></a>

# 接收拖入的文本和文件

[英文原文：`docs/drag-and-drop.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/docs/drag-and-drop.md) · [返回目录](../../README.zh-CN.md#contents)

在 `AvaloniaSurfaceHost({ enableInboundDrop: true })` 中开启接收，并参照 Counter 转发可选的 NativeAOT 导出函数 `avaoh_drag`。在接收拖放的 Avalonia 控件上设置 `DragDrop.AllowDrop`，用标准 Avalonia 路由事件处理拖放。

对从属窗口，还须启用注册函数的第 5 个参数：`registerSystemRequests(context, mainWindow, windowStage, enableChildAccessibility, true)`。宿主使用实际 XComponent 实例的令牌分发回调。表面关闭、隐藏、被替换，或拖动进入另一窗口时，释放原拖放目标。模态从属窗口可以接收拖放，其所有者窗口此时禁用输入。

XComponent 声明可接收纯文本、HTML、网页链接和文件。进入和移动阶段只公开系统摘要中的格式，直到放下（Drop）时才实际读取数据。命中测试之前，宿主将 ArkUI 窗口坐标换算为相对于 XComponent 的物理像素坐标。默认操作是复制（Copy）。对于不含文件的数据，按住 Shift 且 Avalonia 处理函数接受移动（Move）时，才可移动。系统在 ArkUI 回调中取得目标的决定，因此异步操作应在作出该决定之后进行。

文件路径必须是指向普通文件的 `file://` URI。放下时由 FileKit 检查临时系统授权。**只有同时具备明确的 WRITE 策略、并成功以读写模式打开文件，才提供可写流。** 否则，在获得读取授权时按只读方式打开。接收端保留描述符副本，使其在回调返回后仍然有效。用完后须释放每个保留的 `IStorageFile`。单次拖入最多 32 项，UTF-8 数据最多 1 MiB。见 [ArkUI 拖放](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-common-events-drag-event/)与 [UDMF 授权](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-data-unifieddatachannel/)。

## NativeAOT 导出函数

```c
int32_t avaoh_drag(int64_t surface, int32_t phase, double xPx, double yPx,
    int32_t modifiers, int32_t formats, const char* payloadUtf8, int32_t payloadLength);
```

`phase` 的 0、1、2、3 分别表示进入、悬停移动、离开和放下。修饰键位与 `RawInputModifiers.KeyboardMask` 一致。V1 宿主收到包含 `texts` 和 `files` 的 UTF-8 JSON。转发扩展指针导出函数的宿主会收到有序 `items`，其中包含更丰富的数据及文件授权。Counter 的导出函数调用 `OpenHarmonyHost.ReceiveDragEffect`，返回 Avalonia 选定的操作。原生调用返回后，ArkTS 宿主关闭它借出的描述符。

## 验证

Windows 测试覆盖 Unicode 文本、预览格式、拒绝不支持的操作或 URI、只读描述符保留、流之间相互独立的偏移量，以及调度器繁忙时取消。窗口测试覆盖跨窗口离开、表面替换后拒绝旧令牌，以及模态和后台状态处理。API 26 原生桥接层已针对 x86_64 和 arm64-v8a 编译；x86_64 Counter 集成了实际的 NativeAOT 导出函数及 ArkTS 接收端。

PC 环境中，独立 ArkUI 测试应用和 Counter 通过鼠标交换了文本、HTML 和网页链接。Counter 成功读取只授予 READ 的文件；源应用授予 WRITE 后，它也成功写入了第二个文件。源应用仅在第二个文件中看到写入标记。独立测试页面位于 `tests/TextDragSourceProbe/HarmonyDragPeer.ets`。

## 发起拖放

允许向外拖出和允许接收拖入是两个独立的开关。详见[发起系统拖放](text-drag-source.md#chap-text-drag-source)。
