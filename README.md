# Avalonia.OpenHarmony

Avalonia.OpenHarmony is an independent adapter for hosting Avalonia 12.1.6 views in a HarmonyOS API 26 `XComponent`.
Avalonia lays out, styles and draws the controls. ArkTS supplies the surface lifecycle and touch events. A C++ N-API
module presents Skia's CPU-rendered framebuffer through the HarmonyOS NativeWindow API.

The Counter sample uses Avalonia's Simple theme, a button and a text box. Tapping **Add one** updates the label and
writes the new count to hilog. The **Type here** field exercises the system keyboard integration.

## Build the Counter sample

Use Windows with PowerShell 7, .NET SDK 9.0.321 or later, and DevEco Studio 26.0.0.865 with the API 26 Release SDK. If
OHPM reports a Windows symlink error, enable Windows Developer Mode or run the build shell with the Windows symlink
privilege.

Check out the pinned OpenHarmony .NET toolchain and run the build from this repository.

```powershell
git clone https://gitcode.com/OpenHarmonyPCDeveloper/ohos_net-opensource ../ohos_net-opensource
git -C ../ohos_net-opensource checkout 9114affc54e9d7d94246708b841ec7f037119ea6
./scripts/Build-Counter.ps1 -DevEcoRoot 'C:/Program Files/Huawei/DevEco Studio' -OpenHarmonyNetRoot ../ohos_net-opensource -Abi x86_64
```

The build verifies the SDK, compiles the NativeAOT library, prepares the native graphics dependencies and checks the HAP
contents. It writes the unsigned HAP to `artifacts/packages/x86_64/debug/AvaloniaCounter-unsigned.hap`. Use
`-Abi arm64-v8a` for the corresponding ARM64 build. Use `-StageOnly` to prepare a DevEco project under
`artifacts/build/<abi>/Hap` and open it in DevEco Studio for signing and installation.
Both methods stage the original license and notice texts described in [THIRD-PARTY.md](THIRD-PARTY.md).

On Windows, run the same managed backend through a smoke check of framebuffer rendering and touch dispatch.

```powershell
dotnet run --project samples/Counter/Counter.csproj
```

## Use the backend in another HAP

1. Reference `src/Avalonia.OpenHarmony/Avalonia.OpenHarmony.csproj` from the NativeAOT application. Build an
   `AppBuilder` as usual and pass its factory to `OpenHarmonyHost` along with the native frame and error callbacks. Set
   `ISingleViewApplicationLifetime.MainView` in `Application.OnFrameworkInitializationCompleted`.
2. Expose `avaoh_resize`, `avaoh_destroy`, and `avaoh_touch` from the NativeAOT library using `UnmanagedCallersOnly`.
   Forward them to the host. The sample's `Program.cs` shows the signatures and the matching `avaoh_present` and
   `avaoh_report` imports.
3. Set the CMake cache variable `AVALONIA_OHOS_APP_LIBRARY` to your NativeAOT shared object filename and include
   `src/Avalonia.OpenHarmony.Native` with `add_subdirectory`. The resulting ArkTS module is
   `libavalonia_ohos_bridge.so`. Stage the contents of `src/Avalonia.OpenHarmony.ArkTS` into the HAP module's
   `src/main` directory. Import `AvaloniaSurfaceHost` from `ets/components/AvaloniaSurfaceHost.ets` and give it
   the area allocated to Avalonia in your ArkUI entry page. Call `configureFiles(this.context.filesDir)` before
   creating the surface so Fontconfig can find HarmonyOS system fonts.
4. Include the managed shared object and the graphics libraries prepared by `scripts/PrepareOpenHarmonyGraphics.ps1` in
   `entry/libs/<abi>`. Include the applicable licensing files for the backend, runtime, SDK C++ library and graphics
   dependencies in the packaged resources.

`OpenHarmonyHost` runs on its own Avalonia dispatcher. Surface and touch callbacks can arrive on the ArkTS thread and
are queued to that dispatcher. The frame callback receives a premultiplied RGBA8888 framebuffer whose address remains
valid for the duration of the callback. Surface IDs remain strings in ArkTS, and C++ assigns independent monotonic
identifiers to surface instances. Touch coordinates sent to the C# host are physical pixels.

### System services

Register the window's ArkTS system request handler with `registerSystemRequests(this.context)` before loading the
surface page. The handler is provided in `ets/services/SystemRequests.ets`. Unregister it during window-stage
destruction so pending requests complete. Pass an `OpenHarmonyLauncher` and an `OpenHarmonyStorageProvider` to
`OpenHarmonyHost` using `OpenHarmonySystemRequests.RequestAsync`. The providers offer HTTP(S) links, file associations,
and the system document picker. A picked file retains its original authorization URI. The bridge transfers an owned
duplicate of a descriptor to managed code for file and directory operations. [Storage details](docs/storage.md)

Apps can host the HarmonyOS security `PasteButton` alongside the surface. In its success callback, call
`bridge.requestPaste()`. The target NativeAOT library can implement `avaoh_paste` and forward it to
`OpenHarmonyHost.PasteToFocusedTextBox()`. [Clipboard details](docs/clipboard.md)

### System input method

Pass a native window ID provider as the fourth constructor argument to enable the HarmonyOS input method. Call
`configureInputMethod(windowStage.getMainWindowSync().getWindowProperties().id)` from ArkTS before loading the
XComponent page. The Counter application reads the ID through `avaoh_ime_window_id`. The backend attaches a native
editor when an Avalonia text client gains focus and detaches on focus loss or surface destruction.

The backend forwards committed UTF-16 text, deletion, selection changes, and Enter to Avalonia. It sends the caret and
surrounding text to the input method for standard fields. Avalonia's sensitive-text and password options suppress
surrounding-text
sharing. [HarmonyOS native input method guide](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/use-inputmethod-in-custom-edit-box-ndk/)

### Window lifecycle, theme and displays

The Counter's `PlatformWindow.ets` forwards window show, hide and focus changes. The ability forwards application
foreground and background separately. Avalonia pauses the embedded renderer while the window is hidden or the ability
is backgrounded. It keeps the view alive and clears input focus when the window becomes inactive. Applications can
subscribe to `Application.TryGetFeature<IActivatableLifetime>()` for activation changes of kind
`Background`. [HarmonyOS window lifecycle](https://developer.huawei.com/consumer/en/doc/HarmonyOS-Guides/window-lifecycle/)

The ability's color-mode configuration supplies the initial theme and updates Avalonia `IPlatformSettings` when it
changes. Use `ThemeVariant.Default` in an application that follows the system. Displays come from the Native Display
Manager, including their primary flag, bounds, density and orientation. The host window and XComponent offsets allow
screen and client coordinate conversion. Display bounds serve as the working-area fallback when the system cannot report
the available
area. [HarmonyOS Native Display Manager](https://developer.huawei.com/consumer/en/doc/harmonyos-references/capi-oh-display-manager-h/)

The ArkUI entry page forwards its synchronous `onBackPress` callback to `TopLevel.BackRequested`. The event handler
can set `Handled` to override the system action. Keep handlers brief and avoid waiting for ArkUI from the Avalonia
thread. A request that cannot begin within 150 ms returns to ArkUI for default navigation. If a handler has already
begun, the bridge consumes the request at its deadline to avoid a second
navigation. [ArkUI entry page lifecycle](https://developer.huawei.com/consumer/en/doc/harmonyos-references/ts-custom-component-lifecycle/)

The sample's `Program.Platform.cs` shows the optional NativeAOT exports. Avalonia secondary windows, application-driven
foreground and background commands, color accents and system font scale remain pending.

### Safe areas and input pane

The shared ArkTS host reads initial window avoidance data and subscribes to `avoidAreaChange`. Avalonia receives safe
area padding for system bars and cutouts that overlap the actual XComponent client area. The intersection is computed
in window-local physical pixels and then converted using the surface's rendering scale. Space already excluded by
ArkUI contributes zero
padding. [HarmonyOS immersive layout and avoidance coordinates](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/immersive-window-feature/)

`TopLevel.InputPane` reports the fixed keyboard and its occlusion in Avalonia client coordinates. The fixed keyboard
can remain open with an empty occlusion when ArkUI resizes the surface to avoid it. The platform does not infer an
occlusion from a height-only notification, and a floating keyboard does not contribute an inset. Notifications report
the received geometry without a keyboard animation
curve. [HarmonyOS keyboard avoidance](https://developer.huawei.com/consumer/en/doc/HarmonyOS-Guides/window-faqs/)

The optional commands travel through the existing ArkTS system request service. Pass the main window to
`registerSystemRequests(context, mainWindow)`, retain its cleanup callback, and use
`host.UseInsetsCommands(OpenHarmonySystemRequests.RequestAsync)`. The preferences request immersive layout and
visibility for the status and three-button navigation bars, or set the status bar background. Avalonia reads the
resulting window mode, reserved system-bar area, and status bar color from HarmonyOS. System-bar visibility reads as
unknown when HarmonyOS reports zero reserved area. Visibility and color commands are limited to supporting device types
and window modes. Layout follows the actual XComponent
geometry. [HarmonyOS window system bars](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-window/)

### Owned windows

Use the optional windowed stage to show native Avalonia child windows and modal dialogs.
[Owned window integration](docs/owned-windows.md)

## Compatibility

The backend uses the available Avalonia 12.1.6 implementation assemblies and exact package versions. Its scope is a
single embedded Avalonia view with software rendering or opt-in Skia EGL, resizing and in-surface popups, touch, mouse and hardware
keyboard input, optional system text input, clipboard, and system file and link operations. Applications own their
service and settings lifetimes. The [platform verification matrix](docs/platform-validation.md) distinguishes
device checks, local probes and remaining platform work.

The graphics preparation script pins and checks the two NuGet native asset packages and the auxiliary libraries supplied
by the pinned OpenHarmony .NET checkout. HarmonyOS supplies `libc.so`. See the distribution and license details
in [THIRD-PARTY.md](THIRD-PARTY.md).

## Emulator verification

The API 26 x86_64 Counter HAP was installed and launched on the Huawei_TripleFold local emulator. The native bridge
presented 3184 × 1872 Avalonia frames in both the software and EGL builds. Tapping **Add one** updated the counter
to 1. The sample selected a ComboBox item, copied text to the system clipboard, pasted through the ArkUI security
button, and saved and reopened a 24-byte file through the system picker.

### Input method verification

The IME sample builds as an API 26 x86_64 HAP with NativeAOT. A Windows probe used a C ABI test library that enforced
the SDK's 15 editor callbacks. It verified touch focus, window ID propagation, UTF-16 insertion and deletion, selection
replacement, caret and surrounding-text queries, and detachment when the surface closed.

On the Huawei_TripleFold emulator, the sample rendered and focused its text box. The native attach call returned
`12800008` (`IME_ERR_IMMS`). The emulator listed `com.huawei.hmos.inputmethod/InputService` as the selected x86_64
system input method. In a separate ArkTS app, self-drawn binding returned `12800009`. Its standard ArkUI `TextInput`
gained focus, the keyboard windows stayed at 0 × 0, and the keyboard process was absent during the check.
System apps and a standard unsigned ArkUI TextInput sample later opened the keyboard successfully on the same
emulator. The standard sample received nine characters through the system keyboard. A later cold launch of the
Avalonia Counter also displayed the keyboard and did not reproduce `12800008`. The EGL package received multiple
system keyboard commits in the Avalonia TextBox. A hardware Backspace removed the sample's trailing emoji in one
action. Soft-keyboard Backspace still needs device verification.

## Repository workflow

Develop features and fixes on separate branches and review them in pull requests to main. Promote selected main commits
to preview in a release pull request. Update stable after preview validation. Use a hotfix branch for urgent stable
fixes and bring the fix back to main.

# Skia EGL rendering

The software framebuffer is the default. An application can opt in to the Skia OpenGL ES renderer
by adding `With(new OpenHarmonyRenderingOptions { UseEgl = true })` to its `AppBuilder`.
The host checks that Skia can draw into the live XComponent window before starting the GPU
compositor. EGL initialization or rendering errors return the affected top level to software.

Run `scripts/Build-Counter.ps1` with `-UseEgl` to enable the same request. The package is placed in the
`egl` subdirectory of the chosen ABI and build mode.
On HarmonyOS, the first compositor frame on each EGL surface produces a
`Presented Avalonia EGL frame` entry under the `AvaloniaOHOS` log tag.
