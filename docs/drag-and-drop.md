# Receiving text and file drops

An application can opt in with `AvaloniaSurfaceHost({ enableInboundDrop: true })` and forward the optional
`avaoh_drag` NativeAOT export as the Counter sample does. Set `DragDrop.AllowDrop` on Avalonia controls that receive
the drop and use the standard Avalonia routed handlers.

For owned windows, enable the fifth argument of
`registerSystemRequests(context, mainWindow, windowStage, enableChildAccessibility, true)`.
The host routes each callback by its physical XComponent token. Closing, hiding, replacing a surface or moving a
drag into a different window releases the prior drag target. An owned modal window receives drops while its owner
is disabled.

The XComponent advertises plain text and file records. Enter and move events expose the formats in the OS summary.
Text and files become available during Drop. The host transforms ArkUI window coordinates to the XComponent's
physical coordinates before Avalonia hit testing. Drag-over and drop effects use Copy, and the OS receives the target's
decision inside the ArkUI callback. Run asynchronous work after the handler's synchronous decision. A callback that
waits for ArkUI while ArkUI waits for the dispatcher would block both threads.

The receiver accepts UDMF plain text and readable regular files represented by `general.file-uri` or
`general.file` records. The file path must be a `file://` URI. FileKit checks system authorization on Drop and opens
the file with read-only and nonblocking flags while the grant is active. The managed file retains a duplicate descriptor so it remains readable
after the source's temporary grant ends. The target should dispose each `IStorageFile` it keeps. The retained object
provides read-only streams without requesting a persistent grant. The bridge limits one drop to 32 transferred items
and a 1 MiB UTF-8 payload, including metadata and text. [ArkUI drag and drop](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-common-events-drag-event/) · [UDMF authorization](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-data-unifieddatachannel/)

## NativeAOT export

```c
int32_t avaoh_drag(int64_t surface, int32_t phase, double xPx, double yPx,
    int32_t modifiers, int32_t formats, const char* payloadUtf8, int32_t payloadLength);
```

Phases from 0 through 3 are enter, over, leave, and drop. Modifier bits match Avalonia's
`RawInputModifiers.KeyboardMask`. Enter and over use a bit mask of 1 for text and 2 for files, with an empty UTF-8
payload. Drop passes JSON with `texts` and `files` arrays. Each file entry contains its `uri`, borrowed
`descriptor` and `length`. A result of 1 indicates Copy. The receiver must retain any descriptor it will keep before
returning. The ArkTS host closes each descriptor it opened after the native call returns.

## Verification

Windows probes exercised Unicode text, preview formats, effect and URI rejection, read-only descriptor retention,
independent stream offsets, and cancellation while the dispatcher was busy. The owned-window probe verified
cross-window leave, stale token rejection after surface replacement, and modality and background handling.
The API 26 native bridge compiled for x86_64 and arm64-v8a. The x86_64 Counter includes the real NativeAOT export
and ArkTS receiver.

In split screen, drag Unicode text or a readable file from another application onto **Drop text or files here**.
Counter writes the text length or bytes read to hilog. The launch logs include the current physical drop target
coordinates. The windowed Counter also exposes a separate drop target in each owned window.

## Starting a drag

The touch drag source sends plain text to the system with a Copy effect. Its opt-in is independent of receiving
drops. [Starting a system text drag](text-drag-source.md)
