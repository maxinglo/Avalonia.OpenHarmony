# Starting a system drag

Opt in with `AppBuilder.With(new OpenHarmonyTextDragOptions())` and
`AvaloniaSurfaceHost({ enableTextDragSource: true })`. Register the shared ArkTS system request handler.
For native owned windows, also set the sixth argument of
`registerSystemRequests(context, mainWindow, windowStage, enableChildAccessibility, enableChildInboundDrop, true)`.

Call `DragDrop.DoDragDropAsync` with the original `PointerPressedEventArgs` from an Avalonia control,
a `DataTransfer`, and the allowed effects. Counter uses a routed tunnel handler on a drag-only Avalonia
`Button` so its normal click handler does not consume the gesture. The ArkUI surface starts the system
drag using the matching press. Touch uses `DragController` with the original ArkUI touch ID. Both
paths use a bounded preview generated from the offered payload. If mouse drag detection starts
before the offer arrives, ArkUI waits briefly and uses a compact text-type card. The window and
other controls stay out of the preview. [HarmonyOS drag guide](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-common-events-drag-event/)

The source supports standard text, `text/html` and HTTP(S) links via `text/uri-list`. Its limit is
32 records and 64 KiB of UTF-8 data. Copy and Move are available to the receiving app. The sender
receives the selected effect only if it offered that effect. A rejected or canceled drag returns
`None`. Releasing the press, losing focus, or destroying the surface also invalidates pending gestures.

## Outbound file scope

The source rejects every offer that declares `DataFormat.File`, including mixed offers. In the API 26
PC check, an independent receiver opened a synthetic app-private file in read-write mode through
FileKit despite the sender declaring a one-time READ policy on the ArkUI drag record. The source
checks the formats before reading any payload. Inbound file access uses the receiver's explicit
grant policy. [HarmonyOS UDMF URI authorization](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-data-unifieddatachannel/)

## Verification

The Windows control probe used Avalonia's public `DragDrop.DoDragDropAsync` with a routed touch press, validated
the Unicode and per-press identity across the bridge, and checked asynchronous completion and data disposal.
The ArkTS state probe covered released, canceled, unfocused, stale and concurrent gestures, and source destruction.
The Windows `tests/TextDragSourceProbe` regression covers mouse and touch gesture IDs, file-only and
mixed offer rejection before reads, ordered Unicode text, HTML, web links, Copy/Move handling, and
record and byte limits.
PC testing verified mouse Copy with text and with combined text, HTML and web links, including offers
that arrived after native drag detection. The same emulator delivered an injected touch drag to the
other app. Move is covered by the automated probes.
The API 26 ARM64 toolchain packaged the ArkTS and C++ bridge in a fixture with zero declared application permissions.
ARM64 drag behavior needs a device check.
DevEco API 26 x86_64 compiled and packaged the native bridge and ArkTS Counter. The Huawei_TripleFold emulator
accepted a touch drag from each of the Counter main and owned windows into an independent ArkTS HAP in system split
screen. The receiver matched the fixed 16 UTF-16 unit fixture and Counter reported Copy in each window. The system
rejected a drag released over a blank area.

For an on-device check, run Counter and a second ArkUI app with a plain-text drop target in adjacent windows
or split screen.
Drag Counter's **Drag Avalonia sample text** into the destination. Confirm that the receiver sees
`Avalonia — 中文 🎧` and Counter reports **Sample text dragged: Copy**. Release the next drag outside the
target and check that Counter reports **Sample text drag canceled**. The cross-application result requires an
API 26 device or emulator with the system drag service. Counter logs the physical **Drag source** coordinates.
In an owned window, look for **Child drag source** and the **Child** prefix on the completion log.
