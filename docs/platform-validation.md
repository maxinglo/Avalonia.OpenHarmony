# Platform verification

See the [Avalonia interface matrix](platform-capabilities.md) for the current implementation scope.

## API 26 PC acceptance

Use one x86_64 MateBook Pro emulator throughout the run. Verify `const.ohos.apiversion=26`, `x86_64` in
`const.product.cpu.abilist`, and `const.product.devicetype=2in1` through HDC. Test the windowed HAP first, then install
the EGL HAP on the same emulator. Use each run's `Control positions` and the current WindowManager frames for input.

### Desktop integrations

| Interface | Device acceptance |
| --- | --- |
| `INativeControlHostImpl` | Click the ArkUI button inside Counter and observe the Avalonia count change. Move it against the viewport edge and compare the visible bounds with the viewport. A click in the clipped area leaves the native count unchanged. Hide, detach, and reattach it and observe native visibility, disposal, and creation. |
| `ITrayIconImpl` | **Show status bar icon** adds an item to the OS status bar or its overflow. Left-clicking it raises `TrayIcon.Clicked`. **Update status bar icon** changes the icon and hover text. Hiding removes the item, and showing restores the native item. Check the capability query before exercising the controls. |
| `INativeMenuExporter` | Right-click the Counter item in the system tray. Click **Add one from tray** and a child of **Tray menu options**. Verify command dispatch and updated selection in the reopened native menu. This check exercises `TrayIcon.Menu` export. |
| `IWindowImpl` | Compare the system frame and Avalonia window state after programmatic maximize, minimize, fullscreen, and normal requests. Toggle title bar extension and compare the XComponent top with the native frame top. Click **Title action**, drag the custom title, and exercise native maximize, restore, minimize and close. Verify Dock restore keeps the same window and preserves its title layout and content state. |

On the PC image, the status bar overflow is opened by `DockStatusBarComponent_arrowbutton` in the desktop
layout. Take a fresh layout after opening it to locate the test icon. Move and click in one HDC command, for
example `hdc shell uinput -M -m X Y -i 150 -c 1` for right-click. The system menu is drawn in a separate window.

The windowed Counter embedded a real ArkUI button in the same process as Avalonia. The button's original width
was 320 px. After moving it, the system reported a 189 px visible region. Only clicks inside that region incremented
the native counter. Showing it again preserved its count. Detaching disposed the native node and reattaching
created a new node at count 0. The Avalonia counter also accepted input.

The capability query returned true. Counter received a left click on the system item and a root menu command from
the native right-click window. Two native submenu selections toggled the selected property on and off. The system
accepted icon and menu updates, displayed `Avalonia Counter (1)` on hover, and removed and restored the item on
hide and show. The independent ArkTS probe exercised the same native left and right callback routes before Counter.

WindowManager reported 3120 × 1955 px for the maximized stage and 3120 × 2080 px for fullscreen. A width-only
resize changed the floating frame from 2090 × 1394 px to 1178 × 1394 px. Recovering from fullscreen returned to
the exact 1178 × 1394 px frame at (515, 281) after the system transition settled. Counter toggled the native
maximize and minimize title buttons. The programmatic and native-button minimize paths both preserved the native
window and Avalonia owner. Reopening the ability and restoring from the Dock returned to the same native frame.
The main counter continued from 1 to 2 in the same process.

With title bar extension enabled, the Counter's XComponent started at the native frame
top, 70 px above its ordinary origin at 1.90 scale. The 44 DIP custom header reserved
140 DIP for the system buttons. **Title action** received a mouse click, and dragging the
empty title area moved the same native window by 190 × 120 px. Native maximize and
restore kept the extended title, and native minimize followed by Dock restore retained
the process, window ID and floating frame. Disabling extension returned the surface
to its ordinary origin. Re-enabling followed by the native close button removed the
window from WindowManager.

An isolated AudioSynx Arc debug HAP on the same emulator reused its shipping Avalonia
view and enabled the extended title bar. With the native 37 DIP title height and
140 DIP button reservation, the AudioSynx brand and theme icon centers were within
2 px of all three native button centers in floating and maximized windows. Clicking
the theme control left the frame in place. Screenshots showed dark glyphs on the
light background and light glyphs on the dark background for all three buttons.
Dragging the header moved the window by 174 × 112 px. Native maximize and restore
preserved the extended title, and Dock restore after native minimization returned
to the same floating frame and window ID. The native close button removed the window.

The AudioSynx Arc HAP installed under its regular bundle ID repeated the extended frame,
174 × 112 px title drag, native window actions and light/dark caption contrast checks.

After restoring the stage, the original modal flow still blocked the owner, returned 1 and let the owner advance
to count 3 after closing. The embedded EGL variant received NativeVSync, presented a 2090 × 1213 frame and
accepted clicks in both the Avalonia and ArkUI counters. The Windows smoke and managed lifecycle, child insets,
text drag, tray, and ArkTS close probes also passed.

### Rendering and owned-window regression

| Check | Pass condition |
| --- | --- |
| Software, VSync and dispatcher | The `-Windowed` HAP receives the first NativeVSync callback, presents a software frame, and increments the main counter after a click. A local callback probe exercises timeout and unavailable-library fallback. |
| Native owned windows | A modeless child has its own WindowManager frame. Its counter responds, **Move child** changes its position, **Resize child** changes its size, and closing it restores the owner. |
| Modality | The modal child blocks a click on the owner, accepts one child counter click, and returns 1. The same owner accepts another click after the dialog closes. |
| Native ID and sizing | **Open constrained window** reports distinct stage and child WindowManager IDs that match the system. Compare the reported limits with the actual native window size. Exercise the edge-resize toggle and title bar in the window modes that expose them. |
| Pointer routing | Hover the bitmap, hidden, editor and default cursor targets with mouse input. Look for `32x24 hotspot=(7,5)`, `16x16`, `Text` and `Default` in the native result messages. For visual verification, compare the rendered point against the hotspot on a visible system pointer. |
| EGL and lifecycle | The `-UseEgl` HAP presents an EGL frame. Minimize using the native PC title bar and reopen Counter. The same process reports background and foreground events and increments from 1 to 2. |

On MateBook Pro, both HAPs received NativeVSync and presented a 2090 × 1213 frame. The windowed run moved the
modeless child by 48 × 24 px and resized its native frame from 798 × 570 to 950 × 570 px. The modal returned 1 and
the owner accepted input again. The stage and child handle IDs matched WindowManager. **Focus owner** transferred
WindowManager focus from the still-open constrained child to the stage.

With the title bar visible, the constrained child stayed at 798 × 602 px with edge resize disabled. The same
drag reached the system limit of 987 px wide with edge resize enabled. Hiding and restoring the title bar
changed the native frame height limits by 70 px. Counter reported one 3120 × 2080 display, a 3120 × 1955 work area
and 1.90 scale. The native cursor calls accepted the bitmap, transparent bitmap, text and default cursor.
The EGL run retained its process and count through native minimization and restoration. On this image the native
modal keyboard-avoidance request was denied, and the dialog remained usable.

Use `dotnet run --project tests/LifecycleProbe` for both window-focus event orders and synthetic two-display snapshots.
The [cursor probe](hardware-input.md#verification) checks pixel format, alpha, exact hotspot and native resource release.
The [text drag probe](text-drag-source.md#verification) checks ordered records and payload bounds. PC single-display
results cover the active display only. Native multi-monitor hot-plug and cross-application delivery of several text records
require their own device checks.

### API 26 PC service checks

| Capability | Device result | Additional coverage |
| --- | --- | --- |
| Stage and auxiliary windows | Programmatic move and title changes reached WindowManager. The topmost stage stayed above the independent app after that app received focus. Disabling topmost let the independent app move above it. The custom resize grip moved the stage edge by 155 × 75 px. A nested native modal returned `1`. An independent 420 × 300 DIP window opened at 798 × 570 px, maximized to 3120 × 1955 px, restored its starting frame, minimized, and reopened with the same window ID. | Managed probes exercised retained limits, window routing, and per-surface EGL fallback. |
| Native tray menu image | The native menu displayed the purple bitmap with a white plus next to **Add one from tray**. Clicking the native menu item dispatched the Avalonia command. | The tray probe covers changes to icons and menu entries. |
| Pointer and appearance | Real PC input delivered F13 with the same logical and physical key. Avalonia read the system emphasis color. The emulator's stylus injection arrived as ordinary touch. | Managed pointer routing checked stylus type, pressure, X/Y tilt and modifiers. Real stylus, hover and multiple attached displays need hardware. |
| Rich clipboard | The secure PasteButton returned two ordered items containing text, HTML, a bitmap and application data. The check included exact alpha, binary bytes and an empty value. Clearing the pasteboard yielded an empty result. | The Windows ABI probe checked denied paste and pixel buffer lifetime. |
| File picker and launch requests | Counter saved and reopened a 24-byte sample using the plain-text MIME filter. The system exposed modification time and the save filter index. Direct known-folder lookup had no grant. An unregistered URI returned unavailable, and the PC image showed no handler for the tested mail link. | Storage and launcher probes check cancellation, directory grants, and the shape of file and folder launch requests. |
| Text editor | The Counter editor attached to the PC window, advertised inline preview through the native API and accepted a hardware key. | A Windows ABI probe checked preedit callbacks, native editing commands and password guards in an Avalonia TextBox. |
| Cross-application drag | Counter Avalonia buttons sent four plain and two rich offers to a separate ArkUI app in a mouse test. Four offers reached ArkUI after native drag detection and still completed as Copy. A plain-text preview measured 292 × 79 px and the generic card measured 121 × 79 px. Injected touch also delivered text with a compact preview. Drag-only buttons did not raise Click, and dragging an ordinary button produced no drop. Inbound, FileKit denied writing to the READ-only fixture and the sender observed the marker after a WRITE-enabled drop. | Move has automated coverage. Outbound files are rejected because the PC receiver could open a test file for writing even with an explicit READ policy. [Outbound file scope](text-drag-source.md#outbound-file-scope) |

## Huawei_TripleFold checks

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

The default embedded host uses one application view with Avalonia menus and dropdowns. A separate EGL rotation
check remains open. The current PC drag source also supports HTML and web links. [Outbound file scope](text-drag-source.md#outbound-file-scope)

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
