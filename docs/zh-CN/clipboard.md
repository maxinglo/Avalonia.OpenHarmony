<a id="chap-clipboard"></a>

# 系统剪贴板

[英文原文：`docs/clipboard.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/docs/clipboard.md) · [返回目录](../../README.zh-CN.md#contents)

Avalonia 的 `TopLevel.Clipboard` 使用 HarmonyOS Pasteboard 服务，支持纯文本、字符串形式的 `text/html`、经 PixelMap 传递的位图，以及多个有序的数据项。每个数据项可包含多种格式。应用自定义格式可承载字节或 UTF-8 文本，每个值最多 4095 字节。每次操作都先为支持的数据建立快照，使读写能够在工作线程上完成。

对传给 `SetDataAsync` 的数据传输对象，后端在提取可支持的数据后负责释放；`TryGetDataAsync` 返回的快照则由调用方负责释放。`TryGetInProcessDataAsync` 返回 `null`，`FlushAsync` 立即完成。调用 `ClearAsync`、`SetDataAsync(null)` 或传入空的传输对象，均可清空剪贴板。**空字符串仍是一个文本项**。若传入的所有格式都不受支持，原剪贴板内容不变，并报告 `NotSupportedException`。

标准文本和 HTML 不支持内嵌的 U+0000 字符，可改用应用自定义格式。传递文件 URI 则需要能够验证的临时读取授权，以及只读的文件表示。

## 粘贴授权

复制调用系统写入 API。读取剪贴板有两种授权途径：用户在 ArkUI 安全控件 `PasteButton` 上成功执行操作；或应用使用已获批准的配置声明受限权限 `ohos.permission.READ_PASTEBOARD`，并取得用户授权。普通应用可使用可见、无遮挡的 `PasteButton`，不必在清单中声明受限权限。应用进入后台、屏幕关闭或应用退出时，系统会撤销按钮取得的授权。见[剪贴板访问指南](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/get-pastedata-permission-guidelines/)和 [PasteButton 用法](https://developer.huawei.com/consumer/en/doc/HarmonyOS-Guides/pastebutton/)。

通过宿主桥接层将以下操作传给 `OpenHarmonyHost.PasteToFocusedTextBox()`。该方法将 Avalonia 标准粘贴命令投递给当前获得焦点且可编辑的文本框。

```typescript
PasteButton().onClick((_event: ClickEvent, result: PasteButtonOnClickResult) => {
  if (result === PasteButtonOnClickResult.SUCCESS) {
    bridge.requestPaste();
  }
})
```

有同一系统授权时，应用也可以直接调用常规 `IClipboard` 方法。Pasteboard 拒绝读取时会抛出 `UnauthorizedAccessException`，并保持剪贴板内容不变。快捷键和 Avalonia 自定义菜单本身不能授予读取权限。见 [Pasteboard NDK 参考](https://developer.huawei.com/consumer/en/doc/harmonyos-references/capi-oh-pasteboard-h/)。

## 验证

API 26 的 x86_64 和 ARM64 SDK 桩库均导出了所需函数。Windows 测试检查了像素及 Alpha 值、数据项顺序、自定义格式、权限失败和清空操作。PC Counter 使用原生 `PasteButton` 成功读回两个数据项，包括文本、HTML、逐像素一致的位图、应用自定义文本和字节，以及空字节值。清空后再粘贴，结果为空。
