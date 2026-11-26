<a id="chap-ime-preedit"></a>

# 输入法组合输入与行内预览

[英文原文：`docs/ime-preedit.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/docs/ime-preedit.md) · [返回目录](../../README.zh-CN.md#contents)

HarmonyOS 的 `OH_TextEditorProxy` 通过 `SetPreviewText` 回调发送尚未提交的文本。后端用当前获得焦点的 Avalonia 文本客户端的预编辑 API 显示它。向 HarmonyOS 报告临时选区、插入点和周围文本时，偏移量以 UTF-16 代码单元计。标准 Avalonia `TextBox` 会在选中区域或显式指定的替换范围上显示候选文本。**组合输入期间，已提交的 `TextBox.Text` 和对应的数据绑定仍保持原值。**

- **范围。** 首次收到 `-1, -1` 时，使用当前选区或插入点。预编辑进行中再次收到 `-1, -1` 时，替换全部预编辑文本。显式范围使用绝对 UTF-16 偏移；预编辑期间，显式范围必须位于当前预览内。起止偏移相同表示在该位置插入。
- **提交与取消。** `FinishTextPreview` 将当前候选文本作为一次 Avalonia 编辑提交。预编辑期间收到 `InsertText` 时，改为用传入的最终文本替换候选文本并提交。空候选文本清除临时文本；重置、失焦或外部修改也会清除临时显示。密码框和没有 Avalonia `TextPresenter` 的客户端会报告预览不可用。
- **数据绑定。** Avalonia 12.1.6 自带模板对 presenter 的文本使用双向绑定。显示临时值时，后端短暂暂停绑定；结束临时显示时，恢复当前数据源的值。后端已锁定该 Avalonia 版本，并使用 .NET 9 的 `UnsafeAccessor` 访问 Avalonia 停止绑定的方法。以后升级 Avalonia 时，应保留 Windows 回归测试。

原生编辑器将全选、剪切、复制、粘贴命令路由给获得焦点的 Avalonia 客户端。剪切要求控件可编辑且有选区；复制和剪切会遵守密码与 PIN 的隐私限制。粘贴使用 HarmonyOS 剪贴板授权。输入类型、回车动作及占位提示经原生文本配置传递。HarmonyOS 没有与 Avalonia 的输入建议、大小写、首字母大写或语言区域偏好相对应的设置方法。私有命令需要输入法与应用约定协议。

[文本编辑器回调 API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/capi-inputmethod-text-editor-proxy-capi-h/)定义原生接口；[输入法引擎 API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-inputmethodengine/)定义范围规则；[ArkUI 文本框实现](https://github.com/openharmony/arkui_ace_engine/blob/master/frameworks/core/components_ng/pattern/text_field/text_field_pattern.cpp)展示 Finish 和 Insert 如何处理已有预览。

## Windows 回归测试

安装 .NET 9 SDK，将 Windows 版 DevEco 的 LLVM 工具加入 PATH，在仓库根目录执行：

```powershell
$out = 'artifacts/ime-preedit-probe'
New-Item -ItemType Directory -Path $out -Force | Out-Null
clang.exe --target=x86_64-pc-windows-msvc -std=c11 -c tests/ImePreeditProbe/mock.c -o "$out/mock.obj"
lld-link.exe /dll /noentry /nodefaultlib "/out:$out/mock.dll" "$out/mock.obj"
dotnet run --project tests/ImePreeditProbe/ImeProbe.csproj -- "$out/mock.dll"
```

模拟库调用真实原生 ABI，Avalonia 宿主则在 Windows 上渲染采用 SimpleTheme 的真实 `TextBox`。测试覆盖焦点与连接、常规 Unicode 编辑、替换预览的渲染与插入点、范围合法性、只提交一次、撤销与重做、重置和空预览、密码框、剪贴板编辑命令，以及预编辑期间从外部更新双向绑定模型。

## HarmonyOS 设备检查

启用中文软键盘，点击 Counter 的 **Type here** 输入框，输入一个音节。选词前观察是否有行内预览；接受候选词后，示例日志中的文本长度应有变化。选中已有文本后再试一次，检查替换；再重复一次并移走焦点，检查临时显示是否清除。

在 API 26 x86_64 Huawei_TripleFold 模拟器上，输入 `nu` 后，小艺输入法显示候选字，但 Avalonia 文本框仍显示空值占位提示。选“奴”，再输入 `nuli` 并选“努力”，两次分别提交了 1 和 2 个 UTF-16 代码单元，文本框最终显示“奴努力”。这验证了**系统候选栏和选词上屏**，并不证明该输入法向应用发送了行内预览。原生回调与行内渲染已有 Windows 测试覆盖；设备上对 `SetPreviewText`、选区替换及失焦取消的验证仍待完成。

#### 可选的回调诊断

构建 Counter HAP 时加入 `-TraceImePreview` 可启用 `OpenHarmonyInputDiagnostics.TraceImePreview`。常规构建目录和打包目录下会增加 `ime-preview` 子目录。该变体使用标准 Counter 的包标识和编辑器，支持相同的渲染选项。

将 `AvaloniaOHOS` 日志过滤到当前 Counter 进程。`IME preview config` 显示声明的支持标志与 SDK 设置结果；`IME preview set` 显示 UTF-16 长度、请求范围和返回状态；`IME preview finish` 显示调度器处理时待提交预编辑文本的长度和范围。`IME commit` 和 Counter 的 `Text length` 记录已接受的编辑。预览诊断不记录文本内容，敏感字段也不记录预览和文本长度。

同一模拟器的后续测试记录到一次成功的配置，`supported=True`。小艺软键盘输入 `nu` 后选择“奴”，产生一次 `InsertText` 提交，文本框长度为 1；同一 Counter 进程对此次输入记录到的 `SetPreviewText` 和 `FinishTextPreview` 回调次数均为零。
