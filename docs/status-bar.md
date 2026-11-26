# Status bar icons on HarmonyOS PCs

Opt in through `AppBuilder.With(new OpenHarmonyTrayOptions())`. Register a `StatusBar` from
`ets/services/StatusBar.ets` in the ArkTS UIAbility and pass a callback to the final
`registerSystemRequests` parameter, as in Counter's `EntryAbility.ets`. This enables the
Huawei Desktop Extension Kit for that HAP. Call `StatusBar.stop()` when destroying the window stage
and unregister the system request handler. After registering a new stage, call `bridge.reportTrayEvent(0n, '')`
to republish any active icon.

Export `avaoh_report_tray_event(long id, byte* menuCode, int length)` from the NativeAOT application.
Decode its UTF-8 menu code while the callback is on the stack and call
`OpenHarmonyHost.ReportTrayEvent`. Counter's `Program.Tray.cs` shows the complete export. The host
delivers icon clicks and menu commands on the Avalonia dispatcher.

Wait for `OpenHarmonyTrayOptions.IsSupportedAsync()` before offering tray controls. It checks the ArkTS
system capability and calls the API 26 capability query when available. Use the standard Avalonia
`TrayIcon.SetIcons`, `IsVisible`, `ToolTipText`, `Icon`, `Clicked` and `Command` APIs.
The first attached icon holds the HAP's single system slot until detached or disposed.
Without a custom icon or tooltip it uses the HAP icon and ability label. For a custom icon, the bridge
decodes the supplied PNG at status bar size and uses that image for both wallpaper variants.

A `NativeMenu` set on the tray icon becomes the system right-click menu. It supports separators as
groups, one submenu level, and checked items. The system permits up to 20 entries at each level.
Only visible, enabled items with a title are exported. HarmonyOS controls menu presentation and
provides its own app exit action. The export covers menu text and commands. Avalonia item icons,
keyboard gestures and menu opening events are outside this export. The current API 26 window interface
exposes native title buttons without an app-defined system window menu. Apps can present a window menu
inside the Avalonia view through `NativeMenuBar`. [HarmonyOS Window API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-window/)

Counter offers show, update and hide controls. Click the icon in the system status bar to confirm the
Avalonia click handler and pick a custom right-click item to confirm the menu callback.
The PC system can display the icon directly in the Dock or in its overflow panel. On the API 26 x86_64
MateBook Pro emulator, Counter showed the icon, received a left click, and ran Avalonia commands from the
system menu and submenu. The system displayed the updated hover text and removed and restored the item
as its visibility changed. [PC acceptance](platform-validation.md#desktop-integrations)

[Huawei status bar development guide](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/statusbar-extension-guide/)
 · [Desktop Extension Kit statusBarManager API](https://developer.huawei.com/consumer/en/doc/doccenter-references/api/statusbar-extension-manager/)
