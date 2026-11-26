# Starting a system text drag

Enable the platform drag source with `AppBuilder.With(new OpenHarmonyTextDragOptions())`, and pass
`enableTextDragSource: true` to the originating `AvaloniaSurfaceHost`. Register the shared ArkTS system request
handler as Counter does. In an Avalonia touch handler, pass the original `PointerPressedEventArgs` to
`DragDrop.DoDragDropAsync` along with a plain-text `DataTransfer` and `DragDropEffects.Copy`.

For owned windows, pass `true` as the sixth argument to
`registerSystemRequests(context, mainWindow, windowStage, enableChildAccessibility, enableChildInboundDrop, true)`.
Counter enables both directions in the main and child surfaces.

The XComponent assigns a new contact identity on each physical touch down and retains the corresponding ArkUI
touch ID while the gesture is eligible. Avalonia carries the identity on the routed pressed event. The ArkUI
handler resolves it in the originating component's UI context and executes the system drag with the original touch
ID, a preview, and one UDMF plain-text record for each text item in offer order. The route accepts up to
32 records and 64 KiB of UTF-8 text in total.
[HarmonyOS DragController](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-uicontext-dragcontroller/)

A touch release, cancellation or focus loss revokes its ability to start a pending drag. Once ArkUI starts the
drag, it owns completion and may take focus across windows. The result returns asynchronously as `Copy` on
success or `None` on rejection, failure or cancellation. Destroying the source releases the Avalonia task.
ArkUI's completion still releases the system drag slot. This route supports touch input and plain-text Copy.

## Outbound file scope

The system source returns `None` when an offer or any item declares `DataFormat.File`. A mixed offer can carry
a file name or path as text, and sending that text alone could report a successful file drag. The bridge checks
the declared formats before reading the payload or making a system drag request. Plain-text offers can complete
with Copy.

Outbound file delivery requires device verification that the system grants read-only access to the offered
objects for the intended time. A controlled API 26 x86_64 emulator check did not establish that guarantee.
[HarmonyOS UDMF URI authorization](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-data-unifieddatachannel/)

## Verification

The Windows control probe used Avalonia's public `DragDrop.DoDragDropAsync` with a routed touch press, validated
the Unicode and per-press identity across the bridge, and checked asynchronous completion and data disposal.
The ArkTS state probe covered released, canceled, unfocused, stale and concurrent gestures, and source destruction.
The Windows `tests/TextDragSourceProbe` regression covers file-only and mixed offer rejection, no reads on the
rejected path, ordered Unicode text transport, and the combined record and byte limits.
The API 26 ARM64 toolchain packaged the ArkTS and C++ bridge in a fixture with zero declared application permissions.
ARM64 drag behavior needs a device check.
DevEco API 26 x86_64 compiled and packaged the native bridge and ArkTS Counter. The Huawei_TripleFold emulator
accepted a touch drag from each of the Counter main and owned windows into an independent ArkTS HAP in system split
screen. The receiver matched the fixed 16 UTF-16 unit fixture and Counter reported Copy in each window. The system
rejected a drag released over a blank area.

For an on-device check, run Counter and a second ArkUI app with a plain-text drop target in split screen.
Drag Counter's **Drag Avalonia sample text** into the destination. Confirm that the receiver sees
`Avalonia — 中文 🎧` and Counter reports **Sample text dragged: Copy**. Release the next drag outside the
target and check that Counter reports **Sample text drag canceled**. The cross-application result requires an
API 26 device or emulator with the system drag service. Counter logs the physical **Drag source** coordinates.
In an owned window, look for **Child drag source** and the **Child** prefix on the completion log.
