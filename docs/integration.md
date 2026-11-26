# Integrate Avalonia in a HAP

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
duplicate of a descriptor to managed code for file and directory operations. [Storage details](storage.md)

Apps can host the HarmonyOS security `PasteButton` alongside the surface. In its success callback, call
`bridge.requestPaste()`. The target NativeAOT library can implement `avaoh_paste` and forward it to
`OpenHarmonyHost.PasteToFocusedTextBox()`. [Clipboard details](clipboard.md)

Apps can opt into receiving plain text and read-only files from system drag and drop on embedded and
owned window surfaces. [Drag and drop integration](drag-and-drop.md)

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

The sample's `Program.Platform.cs` shows the optional NativeAOT exports. The windowed host supports native child
windows owned by its stage. Application-driven foreground and background commands, color accents and system font scale
remain pending.

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

### Accessibility

Counter contains an experimental accessibility option, disabled by default. When that option and the HarmonyOS
accessibility service are enabled, an independent, non-drawing ArkUI node exposes the semantics beside the XComponent.
The Avalonia dispatcher publishes a snapshot of visible automation peers with roles, names, bounds, enabled state,
focus state, and Invoke capability. The bounds use physical pixels relative to the non-drawing CustomNode. ArkUI
applies the host transform when returning screen coordinates. Its queries read the cached snapshot. Invoke is
dispatched back to Avalonia, where the host checks the active surface registration and node again before calling
`IInvokeProvider`.

Set `enableAccessibility: true` on `AvaloniaSurfaceHost` and enable a HarmonyOS accessibility service.
Application libraries opt in by exporting `avaoh_accessibility_configure` and `avaoh_accessibility_invoke`, as Counter
does. Export `avaoh_accessibility_action` to enable scroll callbacks. Forward its `surface`, `registration`, `id`,
and `OpenHarmonyAccessibilityAction` to `OpenHarmonyHost.InvokeAccessibility`. The two original exports continue
to provide button Invoke.

A visible, enabled `ScrollViewer` with a physical viewport advertises the available forward and backward page
actions and publishes its position in physical pixels. Forward uses the vertical axis when it has overflow and
uses the horizontal axis otherwise. A viewport based on logical item units has no pixel offset to publish through
this interface.
The bridge clears the snapshot when accessibility is disabled or the surface is replaced. See the [platform verification matrix](platform-validation.md) for the tested actions and the [HarmonyOS XComponent accessibility guide](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/ndk-accessibility-xcomponent/) for the native provider API.

### Owned windows

Use the optional windowed stage to show native Avalonia child windows and modal dialogs.
[Owned window integration](owned-windows.md)

## Skia EGL rendering

The software framebuffer is the default. An application can opt in to the Skia OpenGL ES renderer
by adding `With(new OpenHarmonyRenderingOptions { UseEgl = true })` to its `AppBuilder`.
The host checks that Skia can draw into the live XComponent window before starting the GPU
compositor. EGL initialization or rendering errors return the affected top level to software.

Run `scripts/Build-Counter.ps1` with `-UseEgl` to enable the same request. The package is placed in the
`egl` subdirectory of the chosen ABI and build mode.
On HarmonyOS, the first compositor frame on each EGL surface produces a
`Presented Avalonia EGL frame` entry under the `AvaloniaOHOS` log tag.
