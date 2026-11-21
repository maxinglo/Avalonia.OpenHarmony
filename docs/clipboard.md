# System clipboard

Avalonia's `TopLevel.Clipboard` uses the HarmonyOS Pasteboard service. It supports plain text, string-valued `text/html`, and multiple text items. An item can contain both text and HTML. HTML copied by another application can supply its own plain-text fallback. Each operation snapshots its supported data so clipboard reads and writes can complete on a worker thread.

The backend disposes the transfer passed to `SetDataAsync` after materializing its supported data. Callers own the snapshot returned by `TryGetDataAsync`. The backend returns null from `TryGetInProcessDataAsync`, and `FlushAsync` completes immediately. Clear the clipboard via `ClearAsync`, `SetDataAsync(null)`, or an empty transfer. An empty string is a text item. Unsupported-only transfers leave the prior system contents intact and report `NotSupportedException`. File items, bitmaps, application-defined formats, and embedded U+0000 are outside the supported formats.

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

The x86_64 and ARM64 API 26 SDK stubs export all imported functions. A Windows probe exercises the real Avalonia `TextBox` through a C ABI fixture. It covers copy and focused paste, an empty string versus clear, 1.44 MB of Unicode text, HTML and multiple items, denied reads, rejected writes, concurrent clearing, and native handle cleanup. Confirmation of cross-app copy and the security button grant requires a running API 26 target.
