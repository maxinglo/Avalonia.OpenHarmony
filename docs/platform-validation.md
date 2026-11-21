# Platform verification

The Counter sample logs under `AvaloniaOHOS`. Its text field logs only lengths and decreases. It reports a fixed
success message when it receives the sample clipboard value. The log also reports control positions and display scale.
Input diagnostics identify IME attachment, keyboard status, commits and deletion requests. Hardware-key diagnostics
report whether Avalonia handled the event and the length of any text it carried. Applications can enable these
logs with `OpenHarmonyInputDiagnostics`.

| Capability | Verified | Huawei_TripleFold API 26 check |
| --- | --- | --- |
| CPU frames and touch | The combined x86_64 emulator HAP presented a 3184 × 1872 CPU frame and reported Count: 1 after a tap. | Open Counter and tap **Add one**. Look for the native frame and `Count: 1`. |
| Skia EGL | The opt-in x86_64 HAP presented an actual Avalonia EGL frame at 3184 × 1872 and responded to touch. Windows exercised software fallback. | Install the `-UseEgl` HAP, then check `Presented Avalonia EGL frame` after launch, background and resumption, and surface resize. |
| In-surface popups | The Windows Counter opened an Avalonia `OverlayPopupHost`. The combined x86_64 emulator HAP opened the ComboBox and selected index 1. | Open the selector and choose the second option. Look for `ComboBox opened` and `ComboBox selected: 1`. |
| System IME | The x86_64 emulator opened the system keyboard and received multiple soft-key commits. Inserting + changed the TextBox length from 14 to 15 UTF-16 units. A Windows C ABI probe exercised deletion, selection and detach. | Focus **Type here** and tap a soft key. Confirm `IME commit` and the TextBox length change. Soft-keyboard Backspace still needs a device check. |
| System clipboard | A Windows C ABI probe exercised Avalonia TextBox copy and paste, Unicode, HTML, multiple records and read denial. On the x86_64 emulator, the security button returned authorization status 0. The copied sample reached the Avalonia TextBox as 16 UTF-16 units. | **Copy sample text**, focus **Type here**, and tap the visible ArkUI Paste button. Look for `Sample text copied` and `Sample text pasted`. Repeat with text copied in another app. [Clipboard scope](clipboard.md) |
| Mouse and keyboard | Windows Avalonia controls handled mouse, wheel, scaled coordinates, shortcuts, Latin text, NumLock and blur. On the emulator, a hardware Backspace deleted the trailing emoji in a single action (16 to 14 UTF-16 units). | Move and click with the mouse, wheel over Counter, then type with a physical keyboard. See `Wheel:` and length logs. Check standard text and hand cursor styles. [Hardware input scope](hardware-input.md) |
| File and folder picker | Windows probes validated temporary URI preservation, file descriptor ownership, Unicode, stream read/write, cancellation and capability flags. The x86_64 emulator saved and reopened the same 24-byte file through the system picker. | **Save sample text**, **Open text file**, and select that file. Each should report 24 bytes. **Choose folder** reports the directory entry count when that capability is available. [Storage scope](storage.md) |
| Link and file launcher | The window service bridge and ArkTS launcher build against API 26. The Counter smoke test exercises the launcher request and relative-URI rejection. | **Open Avalonia website** and observe the system browser or matching handler. File launch requires an accessible file and an installed handler for its type. |
| Lifecycle, Back, theme and DPI | A Windows probe covered frame pause/resume, activation, initial and changed appearance, Back handling and timeout, and display coordinates. The emulator reported an initial scale of 2.88. XComponent resize reads the current scale in ArkTS. | Background and resume Counter, switch system color mode, and resize or fold the device. Check the theme and `DPI scale:` logs when the system reports changes. A Back event logs `System Back received`. |

The initial scope is one embedded application view. Applications can open Avalonia menus and dropdowns in the same
surface. Native Avalonia windows require a separate window and surface per instance. Native accessibility needs
XComponent provider registration and an Avalonia automation peer mapping. EGL resumption and surface replacement
still need device checks. Safe-area and keyboard insets, composition preview, cross-application drag and drop,
and persistent file bookmarks require their own implementations and device checks.
