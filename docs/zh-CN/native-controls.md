<a id="chap-native-controls"></a>

# 嵌入原生 ArkUI 控件

[英文原文：`docs/native-controls.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/docs/native-controls.md) · [返回目录](../../README.zh-CN.md#contents)

`AvaloniaSurfaceHost` 在 XComponent 上方提供 ArkUI 的 `ContentSlot`。嵌入视图、窗口化主窗口和从属窗口中的 `NativeControlHost` 都可以把 ArkUI NDK 节点放到这个插槽里。覆盖层的空白部分仍将指针输入交给 XComponent；每个原生节点的输入、渲染和焦点则由 ArkUI 负责。见 [ArkUI 与 NDK 组件集成](https://developer.huawei.com/consumer/en/doc/HarmonyOS-Guides/ndk-access-the-arkts-page/)。

应用可继承 `NativeControlHost`，从 `CreateNativeControlCore` 返回 `OpenHarmonyNativeControlHandle`。`factory` 会在 ArkUI 的 UI 线程上收到已挂载完毕的父节点。用公开的 `ArkUI_NativeNodeAPI_1` 创建子节点，返回时**不要自行挂载**：平台负责将其加入父节点并在稍后移除。可选的销毁回调用于在 ArkUI 线程上注销应用注册的回调并释放子节点。未提供销毁回调时，平台调用 `disposeNode`。

```csharp
protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent) =>
    OpenHarmonyNativeControlHandle.Create(parent, CreateArkUINode, DestroyArkUINode, this);
```

表面可用后才会创建节点。挂载后，用 `Post` 将节点操作投递到 ArkUI；反过来，ArkUI 事件中需要修改 Avalonia 控件时，请投递到 `Dispatcher.UIThread`。两个方向都是异步的：ArkUI 回调可自行返回，不必等待 Avalonia 处理任务。

平台按 XComponent 和 Avalonia 宿主的实际布局矩形裁剪。将 Avalonia 宿主作为 `owner` 传入，也会应用祖先节点的矩形边界和显式裁剪。变换后的或不规则的裁剪区域按外接矩形处理。ArkUI 子节点在 XComponent 上方合成，因此 Avalonia 控件及表面内的覆盖层都位于它下方。

[Counter 示例](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/samples/Counter/CounterApp.NativeControls.cs)嵌入了原生 ArkUI `Button`：ArkUI 更新按钮标签中的计数，Avalonia 状态行收到同一点击计数。示例可以将按钮移过裁剪边界、切换可见性，或移除后重建。见[原生按钮实现](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/samples/Counter/Hap/entry/src/main/cpp/counter_native_control.cpp)。

在 API 26 x86_64 MateBook Pro 模拟器上，按钮初始宽度为 320 物理像素；向右移动 266 像素后，仍有 189 像素可见。移动前后点击均能送达按钮。隐藏后再显示，节点和计数保留；从宿主分离后节点被释放，重新挂载则创建新的、能响应点击的节点。Avalonia 计数器也能通过同一主窗口的 XComponent 接收输入。
