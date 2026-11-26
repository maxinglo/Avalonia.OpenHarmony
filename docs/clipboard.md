# System clipboard

Avalonia's `TopLevel.Clipboard` uses the HarmonyOS Pasteboard service. It supports plain text, string-valued `text/html`, bitmaps via PixelMap, and ordered items. An item can contain more than one format. Application formats can carry bytes or UTF-8 text, up to 4095 bytes per value. Each operation snapshots its supported data so clipboard reads and writes can complete on a worker thread.

The backend disposes the transfer passed to `SetDataAsync` after materializing its supported data. Callers own the snapshot returned by `TryGetDataAsync`. The backend returns null from `TryGetInProcessDataAsync`, and `FlushAsync` completes immediately. Clear the clipboard via `ClearAsync`, `SetDataAsync(null)`, or an empty transfer. An empty string is a text item. Unsupported-only transfers leave the prior system contents intact and report `NotSupportedException`. Embedded U+0000 is excluded from standard text and HTML and can be carried by an application format. File URIs require a verifiable temporary read grant and a read-only file representation.

## Authorize paste

Copy uses the system write API. Reading clipboard contents requires a successful user gesture on ArkUI's security `PasteButton` or the restricted `ohos.permission.READ_PASTEBOARD` permission in an approved profile with a user grant. A visible, unobscured `PasteButton` can authorize normal applications without a restricted manifest entry. The platform revokes the button's grant when the app moves to the background, the screen turns off, or the app exits. [HarmonyOS clipboard access guide](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/get-pastedata-permission-guidelines/) · [Using PasteButton](https://developer.huawei.com/consumer/en/doc/HarmonyOS-Guides/pastebutton/)

Send the following action through the host bridge to `OpenHarmonyHost.PasteToFocusedTextBox()`. That method queues Avalonia's normal paste command on the currently focused editable text box.

```typescript
PasteButton().onClick((_event: ClickEvent, result: PasteButtonOnClickResult) => {
  if (result === PasteButtonOnClickResult.SUCCESS) {
    bridge.requestPaste();
  }
})
```

Application code can call the regular `IClipboard` methods with the same system grant. A read denied by the Pasteboard service raises `UnauthorizedAccessException` and leaves the clipboard unchanged. A shortcut or Avalonia custom menu does not grant permission by itself. [Pasteboard NDK reference](https://developer.huawei.com/consumer/en/doc/harmonyos-references/capi-oh-pasteboard-h/)

## Verification

The x86_64 and ARM64 API 26 SDK stubs export all imported functions. The Windows clipboard probe checks pixel and alpha values, item order, application formats, permission failure and clear. The PC Counter used the native PasteButton and recovered both items, text, HTML, identical bitmap pixels, application text and bytes, and an empty byte value. Clearing and pasting then produced an empty result.
