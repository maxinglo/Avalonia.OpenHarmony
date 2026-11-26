# Receiving text and file drops

An application can opt in with `AvaloniaSurfaceHost({ enableInboundDrop: true })` and forward the optional
`avaoh_drag` NativeAOT export as the Counter sample does. Set `DragDrop.AllowDrop` on Avalonia controls that receive
the drop and use the standard Avalonia routed handlers.

For owned windows, enable the fifth argument of
`registerSystemRequests(context, mainWindow, windowStage, enableChildAccessibility, true)`.
The host routes each callback by its physical XComponent token. Closing, hiding, replacing a surface or moving a
drag into a different window releases the prior drag target. An owned modal window receives drops while its owner
is disabled.

The XComponent advertises plain text, HTML, web links and files. Enter and move events expose the formats in the
OS summary, and Drop materializes them. The host transforms ArkUI window coordinates to physical coordinates
relative to the XComponent before Avalonia hit testing. Copy is the default. A non-file offer can use Move when
Shift is held and the Avalonia handler accepts Move. The system receives the target's decision in the ArkUI
callback, so asynchronous work belongs after that decision.

The file path must be a `file://` URI for a regular file. FileKit checks the temporary system grant on Drop.
An explicit WRITE policy and a successful read-write open are both required to expose a writable stream.
Otherwise the receiver opens the file read-only, if authorized. It retains a duplicate descriptor across the
callback. Dispose each retained `IStorageFile` when finished. The bridge limits a drop to 32 items and a 1 MiB
UTF-8 payload.
[ArkUI drag and drop](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-common-events-drag-event/)
· [UDMF authorization](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-data-unifieddatachannel/)

## NativeAOT export

```c
int32_t avaoh_drag(int64_t surface, int32_t phase, double xPx, double yPx,
    int32_t modifiers, int32_t formats, const char* payloadUtf8, int32_t payloadLength);
```

Phases from 0 through 3 are enter, over, leave, and drop. Modifier bits match Avalonia's
`RawInputModifiers.KeyboardMask`. V1 hosts receive UTF-8 JSON with `texts` and `files`. Hosts that
forward the extended pointer export receive ordered `items` with richer data and file grants. Counter's
export calls `OpenHarmonyHost.ReceiveDragEffect` and returns the selected Avalonia effect. The ArkTS host
closes its borrowed descriptors after the native call returns.

## Verification

Windows probes exercised Unicode text, preview formats, effect and URI rejection, read-only descriptor retention,
independent stream offsets, and cancellation while the dispatcher was busy. The owned-window probe verified
cross-window leave, stale token rejection after surface replacement, and modality and background handling.
The API 26 native bridge compiled for x86_64 and arm64-v8a. The x86_64 Counter includes the real NativeAOT export
and ArkTS receiver.

On PC, an independent ArkUI test app and Counter exchanged data using the mouse. Counter received text,
HTML and a web link. It read a READ-only file fixture and wrote a second fixture when the source granted
WRITE. The source observed the marker in the writable sample only. The independent test page is
`tests/TextDragSourceProbe/HarmonyDragPeer.ets`.

## Starting a drag

The source opt-in is independent of receiving drops. [Starting a system drag](text-drag-source.md)
