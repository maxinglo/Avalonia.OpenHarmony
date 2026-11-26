# Native ArkUI controls

`AvaloniaSurfaceHost` supplies an ArkUI `ContentSlot` above its XComponent. Avalonia's `NativeControlHost` places ArkUI NDK nodes in that slot in the embedded view, windowed stage, and owned windows. Empty parts of the overlay leave pointer input with the XComponent. Each native node receives ArkUI's own input, rendering, and focus. [ArkUI and NDK component integration](https://developer.huawei.com/consumer/en/doc/HarmonyOS-Guides/ndk-access-the-arkts-page/)

An application can subclass `NativeControlHost` and return `OpenHarmonyNativeControlHandle` from `CreateNativeControlCore`. The `factory` receives the fully mounted ArkUI parent on the ArkUI UI thread. Create the child with the public `ArkUI_NativeNodeAPI_1` and return it unattached. The platform adds it to the parent and later removes it. An optional destroy callback unregisters app-owned callbacks and disposes the child on ArkUI's thread. The platform calls `disposeNode` when the destroy callback is omitted.

```csharp
protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent) =>
    OpenHarmonyNativeControlHandle.Create(parent, CreateArkUINode, DestroyArkUINode, this);
```

The node is created after the surface is available. Use `Post` to queue node work on ArkUI after attachment. ArkUI events that modify Avalonia controls should post to `Dispatcher.UIThread`. Both directions are asynchronous, so an ArkUI callback can return while Avalonia processes its work.

The platform clips to the XComponent and the Avalonia host's laid-out rectangle. Passing the Avalonia host as the `owner` also applies rectangular ancestor bounds and explicit clips. Transformed and irregular clips use bounding rectangles. An ArkUI child is composited above the XComponent, so Avalonia controls and in-surface overlays occupy the layer below it.

The [Counter example](../samples/Counter/CounterApp.NativeControls.cs) embeds an ArkUI `Button`. Its label increments in ArkUI and an Avalonia status line receives the same click count. The sample can move the button through a clip boundary, change visibility, and remove and recreate it. [Native button implementation](../samples/Counter/Hap/entry/src/main/cpp/counter_native_control.cpp)

In the API 26 x86_64 MateBook Pro emulator, the stage's native button started at 320 physical pixels wide. Moving it 266 pixels right left 189 pixels visible. Clicks reached the native button before and after the move. Hiding and showing it preserved the node and its count. Detaching disposed the node, and reattaching created a fresh one that accepted clicks again. The Avalonia counter also accepted input through the same stage's XComponent.
