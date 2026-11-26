# Hardware input

The Counter sample connects its focusable ArkUI `XComponent` to the Avalonia mouse and keyboard devices. It sends
device coordinates in physical pixels. The backend divides them by the surface scale before hit testing. Mouse motion,
five buttons, hover exit, horizontal and vertical scrolling, and focus loss follow Avalonia's normal event path.

Hardware key down and up events carry the ArkUI code and the Alt, Ctrl, Shift, and Meta states. The backend translates
editing, navigation, F1–F24, numeric keypad, media transport and volume keys when the OS forwards them. It emits
basic Latin text from the SDK's
`KeyEvent.unicode` after an unhandled key down. ArkUI calls `onKeyEvent` after giving the input method a chance to
consume
the key. Layout composition and other text rely on the system input method. The keyboard event guide describes the
ordering and the supported Unicode
range. [ArkUI keyboard events](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-interaction-development-guide-keyboard/)

The backend applies standard and custom bitmap cursors through the current HarmonyOS window ID supplied to
`OpenHarmonyHost`. The bitmap's original pixel dimensions and hotspot are passed to InputKit. Bitmaps up to
256 × 256 pixels with an in-bounds hotspot are supported. `StandardCursorType.None` uses a transparent per-window
bitmap. An invalid or unavailable custom cursor falls back to the default arrow. InputKit can reset a custom cursor
at a native region boundary. Mouse input refreshes the cursor selected by Avalonia.
[Native pointer style](https://developer.huawei.com/consumer/en/doc/harmonyos-references/capi-oh-input-manager-h/)

Wheel input preserves the system sensitivity, with one Avalonia wheel unit per 15 degrees of rotation. Touchpad
scroll distance uses 48 vp per wheel unit.
[ArkUI mouse events](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-interaction-development-guide-mouse/)

## Pen and touch

The host forwards finger events with keyboard modifiers. When ArkUI reports `SourceTool.Pen`, the event uses
Avalonia's pen device with tip press, move, release, hover, pressure and X/Y tilt. The contact is cleared on
release, cancellation or input loss. The ArkUI callbacks used here supply tip and hover data. Barrel buttons,
an eraser tool and pen twist need a device and input path that report those properties.
[ArkUI touch and stylus input](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-interaction-development-guide-touch-screen/)
· [ArkUI hover input](https://developer.huawei.com/consumer/en/doc/harmonyos-references/ts-universal-events-hover/)

The PC Counter received mouse and touch and reported F13 as both the logical and physical key.
Its emulator's `uinput -S` delivered ordinary touch events, consistent with the public injection tool's
documented behavior. The managed Counter probe checks that pen type, pressure, tilt and Shift survive Avalonia
hit testing. Device verification of the stylus route requires pen input that preserves the tool type.
[OpenHarmony input injection](https://github.com/openharmony/docs/blob/master/zh-cn/application-dev/dfx/uinput.md)

## Application exports

The native bridge looks up these optional `UnmanagedCallersOnly` exports in the application's NativeAOT library. The
Counter sample shows their managed forwarding methods.

| Export             | Arguments                                                                                                                           |
|--------------------|-------------------------------------------------------------------------------------------------------------------------------------|
| `avaoh_pointer`    | `int64_t surface`, `int32_t phase`, `int32_t button`, `double x`, `double y`, `double wheelX`, `double wheelY`, `int32_t modifiers` |
| `avaoh_key`        | `int64_t surface`, `int32_t down`, `int32_t code`, `int32_t modifiers`, `int32_t unicode`, `int32_t locks`                          |
| `avaoh_input_blur` | `int64_t surface`                                                                                                                   |

Pointer phases from 0 through 4 represent move, press, release, leave, and scroll. Button codes from 0 through 5
represent no button, left, right, middle, back, and forward. Rotary deltas use Avalonia wheel units, with negative
values scrolling down or to the right. Modifier bits match Avalonia's `RawInputModifiers`, including the held mouse
buttons. The key export returns `int32_t` with value 1 for supported events and 0 to leave an event with ArkUI. Bit 0
in `locks` indicates NumLock.

## Verification

| Check | Result |
| --- | --- |
| API 26 x86_64 ArkTS and C++ bridge | Compiled and packaged with the DevEco toolchain |
| Managed application exports | Compiled with the HarmonyOS entry points enabled |
| Avalonia input on Windows | The managed probe exercised scaled pointer hit testing, mouse buttons, a two-axis `ScrollViewer`, keyboard text and shortcuts, focus loss, and touch |
| Cursor ABI on Windows | `tests/CursorProbe` checks bitmap conversion, the physical hotspot, transparent and standard cursor transitions, and resource disposal against a native mock |
| API 26 MateBook Pro x86_64 | Injected mouse moves into Counter selected the 32 × 24 bitmap at hotspot (7, 5), transparent 16 × 16 bitmap, text cursor and default cursor. Each InputKit call returned 0 for the stage's WindowManager ID. A physical-device check can verify the visible pointer and hotspot. |

Run the cursor probe from the repository root with the Windows DevEco LLVM tools on PATH.

```powershell
$out = 'artifacts/build/cursor-probe'
New-Item -ItemType Directory -Path $out -Force | Out-Null
clang.exe --target=x86_64-pc-windows-msvc -std=c11 -c tests/CursorProbe/mock.c -o "$out/mock.obj"
lld-link.exe /dll /noentry /nodefaultlib "/out:$out/mock.dll" "$out/mock.obj"
dotnet run --project tests/CursorProbe/CursorProbe.csproj -- "$out/mock.dll"
```
