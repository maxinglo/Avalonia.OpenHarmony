# IME composition preview

The HarmonyOS `OH_TextEditorProxy` sends provisional text through its `SetPreviewText` callback.
The backend displays it using the focused Avalonia client's preedit API and uses UTF-16 offsets
for the provisional selection, caret, and surrounding text reported to HarmonyOS. A standard
Avalonia `TextBox` displays the candidate over the selected or explicitly replaced range.
The committed `TextBox.Text` and its data bindings retain their original value while composing.

- An initial range of `-1, -1` uses the selected text or caret. A later `-1, -1` replaces the
  entire active preedit. Explicit ranges use absolute UTF-16 offsets. During preedit, an
  explicit range must stay within the active preview. Equal offsets insert at that position.
- `FinishTextPreview` commits the current candidate as one Avalonia text edit. An
  `InsertText` sent during preedit commits the supplied final text in place of the candidate.
  An empty candidate clears the provisional text. Reset, blur, and external changes clear the
  temporary display. Password fields and clients without an Avalonia `TextPresenter` report
  that preview is unavailable.
- Stock Avalonia 12.1.6 templates bind the presenter's text in both directions. The display
  override pauses the binding briefly when setting a temporary value and restores the
  current source when releasing it. The backend already pins this Avalonia version.
  The override uses .NET 9's `UnsafeAccessor` for Avalonia's binding stop method.
  Keep the Windows probe in the upgrade checks for future Avalonia versions.

The [HarmonyOS IME text editor callback API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/capi-inputmethod-text-editor-proxy-capi-h/)
defines the native surface. The [input method engine API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-inputmethodengine/)
defines the range rules. The [ArkUI text field implementation](https://github.com/openharmony/arkui_ace_engine/blob/master/frameworks/core/components_ng/pattern/text_field/text_field_pattern.cpp)
shows how Finish and Insert handle active preview.

## Windows regression probe

Run from the repository root with the .NET 9 SDK and the Windows DevEco LLVM tools on PATH.

```powershell
$out = 'artifacts/ime-preedit-probe'
New-Item -ItemType Directory -Path $out -Force | Out-Null
clang.exe --target=x86_64-pc-windows-msvc -std=c11 -c tests/ImePreeditProbe/mock.c -o "$out/mock.obj"
lld-link.exe /dll /noentry /nodefaultlib "/out:$out/mock.dll" "$out/mock.obj"
dotnet run --project tests/ImePreeditProbe/ImeProbe.csproj -- "$out/mock.dll"
```

The mock invokes the native ABI while the real Avalonia host renders a SimpleTheme `TextBox`
on Windows. Checks cover focus and attach, regular Unicode text edits, replacement rendering and
caret positions, range validation, one-time commit, undo and redo, reset and empty previews,
password fields, and a two-way model updated externally during preedit.

## HarmonyOS device check

With the Chinese software keyboard active, focus **Type here** in Counter and type a syllable.
Check the visible provisional text before choosing a candidate. The sample's text length log
should show a change when the candidate is accepted. Repeat with a selection and check
replacement, then repeat and move focus to verify that the provisional display clears.
On the API 26 x86_64 Huawei_TripleFold emulator, the Xiaoyi Chinese keyboard offered a
candidate for nu while Avalonia still displayed the empty placeholder. Selecting 奴
and then entering nuli and selecting 努力 produced commits of one and two UTF-16
units. The control displayed 奴努力. This confirms the system candidate bar and final
text delivery. The keyboard did not expose an inline preview in this trial. The native
callback and inline rendering are covered by the Windows probe. A device check for
SetPreviewText, replacement of a selection, and cancellation on blur is still pending.

### Optional callback diagnostics

Build the Counter HAP with `-TraceImePreview` to enable `OpenHarmonyInputDiagnostics.TraceImePreview`.
This adds an `ime-preview` subdirectory under its usual build and package directories.
This variant uses the standard Counter bundle identity and editor, and accepts the same rendering switches.

Filter `AvaloniaOHOS` logs to the current Counter process. `IME preview config` reports the advertised flag and
the SDK setter result. `IME preview set` reports the UTF-16 length, requested range and returned status.
`IME preview finish` reports the pending preedit length and range when processed by the dispatcher.
`IME commit` and Counter's `Text length` record accepted text edits. The preview diagnostics never
report text contents, and sensitive fields skip preview and text length logging.
