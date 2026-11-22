# Platform verification

The Counter sample logs under `AvaloniaOHOS`. Its text field logs only lengths and decreases. It reports a fixed
success message when it receives the sample clipboard value. The log also reports control positions and display scale.
Input diagnostics identify IME attachment, keyboard status, commits and deletion requests. Hardware-key diagnostics
report whether Avalonia handled the event and the length of any text it carried. Applications can enable these
logs with `OpenHarmonyInputDiagnostics`.

| Capability | Verified | Huawei_TripleFold API 26 check |
| --- | --- | --- |
| CPU frames and touch | The combined x86_64 emulator HAP presented a 3184 × 1872 CPU frame and reported Count: 1 after a tap. | Open Counter and tap **Add one**. Look for the native frame and `Count: 1`. |
| Skia EGL | The x86_64 HAP presented an Avalonia EGL frame at 3184 × 1872. The Counter retained its count through backgrounding. It also incremented through the Huawei_TripleFold full, single, double and full-width sequence in the same process. Windows exercised software fallback. | Install the `-UseEgl` HAP and check `Presented Avalonia EGL frame`. Tap Add one before and after changing the fold state. [DevEco fold controls](https://developer.huawei.com/consumer/en/doc/doccenter-deveco-studio/ide-emulator-command-line/) |
| In-surface popups | The Windows Counter opened an Avalonia `OverlayPopupHost`. The combined x86_64 emulator HAP opened the ComboBox and selected index 1. | Open the selector and choose the second option. Look for `ComboBox opened` and `ComboBox selected: 1`. |
| System IME | On x86_64 the Counter opened the system keyboard when the input method app was initially absent. Earlier device checks received multiple soft-key commits. Inserting + changed the TextBox length from 14 to 15 UTF-16 units. A Windows C ABI probe exercised deletion, selection and detach. A separate mock verified recovery and cancellation of the bounded retry for error 12800008. | Focus **Type here** and tap a soft key. Confirm `IME commit` and the TextBox length change. Soft-keyboard Backspace still needs a device check. |
| System clipboard | A Windows C ABI probe exercised Avalonia TextBox copy and paste, Unicode, HTML, multiple records and read denial. On the x86_64 emulator, the security button returned authorization status 0. The copied sample reached the Avalonia TextBox as 16 UTF-16 units. | **Copy sample text**, focus **Type here**, and tap the visible ArkUI Paste button. Look for `Sample text copied` and `Sample text pasted`. Repeat with text copied in another app. [Clipboard scope](clipboard.md) |
| Mouse and keyboard | Windows Avalonia controls handled mouse, wheel, scaled coordinates, shortcuts, Latin text, NumLock and blur. On the emulator, a hardware Backspace deleted the trailing emoji in a single action (16 to 14 UTF-16 units). | Move and click with the mouse, wheel over Counter, then type with a physical keyboard. See `Wheel:` and length logs. Check standard text and hand cursor styles. [Hardware input scope](hardware-input.md) |
| File and folder picker | Windows probes validated temporary URI preservation, file descriptor ownership, Unicode, stream read/write, cancellation and capability flags. The x86_64 emulator saved and reopened the same 24-byte file through the system picker. | **Save sample text**, **Open text file**, and select that file. Each should report 24 bytes. **Choose folder** reports the directory entry count when that capability is available. [Storage scope](storage.md) |
| Link and file launcher | The window service bridge and ArkTS launcher build against API 26. The Counter smoke test exercises the launcher request and relative-URI rejection. | **Open Avalonia website** and observe the system browser or matching handler. File launch requires an accessible file and an installed handler for its type. |
| Lifecycle, Back, theme and DPI | A Windows probe covered frame pause/resume, activation, initial and changed appearance, Back handling and timeout, and display coordinates. The emulator reported an initial scale of 2.88. XComponent resize reads the current scale in ArkTS. | Background and resume Counter, switch system color mode, and resize or fold the device. Check the theme and `DPI scale:` logs when the system reports changes. A Back event logs `System Back received`. |
| Safe area and input pane | The x86_64 EGL HAP reported a fixed keyboard occlusion of about 1107 × 232 DIP and cleared it on close. Toggling immersive layout reported edge-to-edge with safe-area padding from system bars. Windows exercised movement, scaling, and space already excluded by ArkUI. | Focus the editor and then dismiss the fixed system keyboard. Check `Input pane: Open` and `Input pane: Closed`. Verify `Safe area` and the window layout when toggling immersive mode. |
| Chinese IME candidates and inline preview | Chinese selections 奴 and 努力 arrived in the same Avalonia TextBox on the API 26 x86_64 emulator. The optional callback trace confirmed that the Xiaoyi input method accepted a preview-capable editor and used InsertText for the observed candidate without calling SetPreviewText or FinishTextPreview. The Windows ABI probe exercised inline rendering, binding, selection and undo. | Use the Chinese soft keyboard and select a candidate. To test native inline display, repeat with an input method that sends preview callbacks. [Composition verification](ime-preedit.md) |
| Inbound system drag and drop | In API 26 x86_64 system split screen, a separate ArkTS application dropped Unicode text and a read-only file onto both the Counter main view and a native owned window. Each accepted a 10 UTF-16 unit fixture and read the 29-byte file. The source reported successful Copy for all four drops. | Open the Counter and the companion drag source app in split screen. Drop the two fixtures onto the target in each window. [Drag setup and scope](drag-and-drop.md) |
| Outbound system text drag | On API 26 x86_64, touch drags from Avalonia main and owned windows reached the other ArkTS app with all 16 UTF-16 units unchanged. ArkTS and Avalonia reported Copy. Releasing over an empty area returned rejection. | In system split screen, hold **Drag Avalonia sample text**, move to the other app's text drop target, and release. Repeat from the owned window and over an empty area. [Outbound text setup and scope](text-drag-source.md) |
| Scoped storage and bookmarks | Windows probes checked authorization, bookmark token recovery and scoped directory operations. A dedicated API 26 x86_64 probe read the same 24 bytes after process restart. The system reported successful revocation, and a third process could still open the file. | Verify grant and revocation on an opted-in physical target. Directory move and delete also need device checks. [Storage scope](storage.md) |
| Accessibility | HarmonyOS ScreenReader activated the opt-in Counter main button in the API 26 x86_64 production bridge build. After moving and focusing a native child window, a single touch focused its counter and a double touch on a blank area invoked that button exactly once. The same run independently incremented the main counter. Windows checked Arc names, roles, bounds and states, including rejection of stale owner actions during modality. The Windows scroll probe exercised forward and backward paging, horizontal fallback, scaled pixel offsets, and rejection of stale and blocked modal actions. The native scroll bridge linked for both API 26 ABIs. | Enable the sample accessibility option and system ScreenReader. Focus Add one and use the system activation gesture. In the windowed Counter, move an owned window and repeat on its counter. For system scroll validation, constrain the Counter so its content overflows and check that paging moves content and stops at each edge. |
| Owned windows and modal dialogs | The opt-in x86_64 native Counter presented a separate Avalonia dialog frame, rejected owner touch while modal, accepted child touch, returned 1 from ShowDialog, and restored owner input after close. The modeless window moved, resized from about 420 to 500 DIP wide, and closed. In the integrated HAP, IME attachment and WindowManager focus both identified the child window. The final close-fix HAP repeated both normal open and close flows. Rejected native closure and retries were verified with fault injection. | Run the `-Windowed` sample and test **Open modal dialog** and **Open owned window**. [Owned window integration](owned-windows.md) |

The default embedded host and AudioSynx use one application view with Avalonia menus and dropdowns. A separate EGL
rotation check and independent native windows require further platform verification. The outbound system source
accepts plain text and rejects file offers. [Outbound file scope](text-drag-source.md#outbound-file-scope)

## ScreenReader scroll check

Build the Counter with `-Abi x86_64 -Windowed -ScreenReaderScroll` on API 26. This stages a separate HAP with
accessibility enabled, a 280 vp Avalonia surface that gives the Counter a vertically overflowing viewport, and
native logs for scroll requests and published offsets. Use `-StageOnly` to open the staged project in DevEco
Studio for signing.

1. Enable ScreenReader, open Counter and tap **Add one** once to focus it. The `AvaloniaOHOS` log should include
   `Accessibility scroll position` at offset `0 px` with `forward=1 backward=0`.
2. Check which gestures are assigned to forward and backward scroll under **Settings > Accessibility >
   ScreenReader > More settings > Custom gestures**. If left-then-right is assigned to forward scroll, hold
   the mouse button over the Counter surface, move left and then right without releasing, and release.
   Reverse the movement for backward scroll if assigned. The system also supports two-finger scrolling.
   In an instrumented UiTest, use `Driver.injectMultiPointerAction` with two parallel vertical touch paths.
3. Confirm an `Accessibility scroll request` log with `direction=forward` followed by a larger offset for the
   same surface and node. The sample should reveal controls from further down the page. Repeat to reach
   `forward=0`, then scroll backward until the offset returns to `0 px` and `backward=0`. Observe any speech
   feedback separately. A position change alone can come from normal touch input.

The API 26 Huawei_TripleFold run published the initial scroll offset and updated it after
ordinary touch scrolling. The installed standalone UiTest runner returned no Driver for
the continuous gesture test. A system scroll action reaching the native callback remains
to be verified on a device with a working gesture path.

[ScreenReader gestures](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-universal-attributes-accessibility/)
· [Forward and backward gestures](https://consumer.huawei.com/cn/support/content/zh-cn16030881/)
· [UiTest pointer paths](https://developer.huawei.com/consumer/en/doc/harmonyos-references/js-apis-uitest/)
