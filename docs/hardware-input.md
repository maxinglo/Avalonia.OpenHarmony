# Hardware input

The Counter sample connects its focusable ArkUI `XComponent` to the Avalonia mouse and keyboard devices. It sends
device coordinates in physical pixels. The backend divides them by the surface scale before hit testing. Mouse motion,
five buttons, hover exit, horizontal and vertical scrolling, and focus loss follow Avalonia's normal event path.

Hardware key down and up events carry the ArkUI code and the Alt, Ctrl, Shift, and Meta states. The backend translates
regular editing, navigation, and function keys, including the numeric keypad. It emits basic Latin text from the SDK's
`KeyEvent.unicode` after an unhandled key down. ArkUI calls `onKeyEvent` after giving the input method a chance to
consume
the key. Layout composition and other text rely on the system input method. The keyboard event guide describes the
ordering and the supported Unicode
range. [ArkUI keyboard events](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-interaction-development-guide-keyboard/)

The backend applies standard cursor styles through the current HarmonyOS window ID supplied to `OpenHarmonyHost`.
Custom bitmap and hidden cursors fall back to the default arrow. Wheel input preserves the system sensitivity, with
one Avalonia wheel unit per 15 degrees of rotation. Touchpad scroll distance uses 48 vp per wheel
unit. [ArkUI mouse events](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-interaction-development-guide-mouse/) · [Native pointer style](https://developer.huawei.com/consumer/en/doc/harmonyos-references/capi-oh-input-manager-h/)

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
| HarmonyOS device delivery | Physical mouse, wheel, keyboard, and cursor display require device verification |
