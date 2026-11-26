# Owned HarmonyOS windows

An application can opt into a managed Avalonia `Window` for its main `WindowStage` with
`AppBuilder.With(new OpenHarmonyWindowOptions())`. Its existing
`ISingleViewApplicationLifetime.MainView` becomes the content of the stage window.
Call `TopLevel.GetTopLevel(control)` on an attached control to get that owner. A standard
Avalonia `Window` can call `Show(owner)` or `ShowDialog<TResult>(owner)`.

The stage and its child windows have separate XComponent framebuffers, input routes, and
HarmonyOS window IDs for the system keyboard. A child surface can detach and attach again
while preserving its managed window. The owner stays alive across replacement of the
stage's XComponent surface. HarmonyOS handles native window modality; Avalonia also
disables its owner's input while the dialog is open.

## HAP integration

- Call `registerSystemRequests(context, mainWindow, windowStage)` and dispose the returned
  registration in `onWindowStageWillDestroy`. Call `reportStageDestroyed()` from the shared
  `ChildWindows.ets` module in the subsequent `onWindowStageDestroy` callback.
- Pass `true` as the optional fourth argument to publish child window accessibility while
  a HarmonyOS accessibility service is active. Each surface has its own registration.
- Include `pages/AvaloniaChildWindow` in the module's `main_pages.json`. The reusable ArkTS
  tree includes this page and transfers the generated window key using local storage.
- Export `avaoh_update_child_surface`, `avaoh_close_child_surface`, `avaoh_report_child_frame`,
  `avaoh_report_child_event`, and `avaoh_request_child_close` from the NativeAOT application.
  Key `0` carries stage notifications and geometry, including events before the first
  XComponent surface is created. Counter implements these callbacks in `samples/Counter/Program.Windows.cs`.
- For extended stage title bars, also export `avaoh_report_title_bar` and `avaoh_title_bar_press`
  as in the same Counter file. These two exports are optional for existing hosts.
- To provide child safe area and fixed-keyboard occlusion, export `avaoh_set_child_avoid_area`
  as in `samples/Counter/Program.Insets.cs`.
- Run the Counter build with `-Windowed`. The HAP is placed in the `windowed` subdirectory
  for its ABI and build mode. Use the counter and move/resize commands in the modal dialog,
  then select **Return dialog result** and check the main counter remains interactive.

The windowed host uses software framebuffers and supports child windows directly owned
by the stage. Window decorations and availability depend on the device's window mode.
A device that rejects a frame at creation can create a child with platform-default geometry.
Standalone top levels, owners other than the stage, custom child-window move drags,
and custom resize drags need separate support.

## Desktop stage window state

In HarmonyOS freeform window mode, the managed stage supports `WindowState.Normal`,
`Maximized`, `Minimized`, and `FullScreen`. Assigning `WindowState` queues a native request.
The getter and property notifications follow the window manager’s observed state, including
changes made with the native title bar and Dock. The two expanded states use the normal maximized
layout and the immersive full-screen layout respectively. HarmonyOS `recover()` restores its saved
floating size and position. A minimized stage can return from the Dock. HarmonyOS restricts
programmatic restoration from minimization to a foreground UIAbility.
[HarmonyOS Window API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-window/)

Stage width and height in DIP can request a new client size when the stage is floating. The window
manager applies the native frame and size restrictions. `CanResize` controls native edge dragging.
`CanMinimize` and `CanMaximize` control the corresponding native caption buttons. The stage and its
managed content remain attached across a minimize or a new XComponent surface. These state and
caption controls apply to the main stage in freeform window mode. Child windows continue to follow
the dedicated child-window visibility, geometry and close APIs.

In the windowed Counter, the **Stage minimize**, **Stage maximize**, **Stage normal**,
**Stage fullscreen**, **Stage resize**, and **Stage caption policy** controls issue those requests.
Counter logs observed state, position, client and frame sizes, and both the managed and native
window identities. `tests/LifecycleProbe` covers managed notification and surface replacement;
the ArkTS `WindowCloseProbe` also checks desktop request routing with injected native behavior.

## Extended stage title bar

In freeform window mode, set `Window.ExtendClientAreaToDecorationsHint = true` on the
stage's Avalonia window to draw into the title area. HarmonyOS keeps the native minimize,
maximize, restore, and close controls over the Avalonia surface. The native caption
controls follow the window's existing policy.
[HarmonyOS freeform window guide](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/application-adaptation-freeform-window/)

Use `IsExtendedIntoWindowDecorations` to detect whether extension took effect. In the
custom title area, `WindowDecorationMargin.Top` gives the title height and
`WindowDecorationMargin.Right` gives the width to reserve for the native controls,
both in DIP. Set `ExtendClientAreaTitleBarHeightHint` to request a custom title height.
The backend keeps enough height for the system button rectangle. The default height
uses the native title row. Observe margin changes to keep the layout aligned with the
system controls.

The caption buttons follow the stage's actual Avalonia theme while the title bar is
extended. Turning the extension off restores the native button color mode.
[HarmonyOS caption button style](https://developer.huawei.com/consumer/cn/doc/harmonyos-references/arkts-apis-window-i/)

Call `Window.BeginMoveDrag(args)` from the primary `PointerPressed` handler of a drag
region inside the extended title area of a floating stage. Keep interactive controls
outside that region. HarmonyOS takes over the window move. In Counter, **Toggle title
bar extension** enables the custom title and **Title action** demonstrates an interactive
Avalonia control beside the drag region. Toggling the hint off restores the ordinary
system title bar.
[HarmonyOS window layout guide](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/window-layout/)

## Child window policies and interop

An owned `Window` can set `MinWidth`, `MinHeight`, `MaxWidth`, and `MaxHeight`.
The backend converts those client dimensions to physical pixels and adds the native
frame decoration size before calling `setWindowLimits`. The native window manager
applies its own limits as well. `CanResize` controls native edge dragging.
`WindowDecorations` selects the title bar at creation. When the window has a native
title bar, changes to this property call `setWindowDecorVisible` and reapply the
current resizing policy. These operations depend on the device and window mode.
The Counter's **Open constrained window** fixture starts with minimum and maximum
dimensions and edge dragging disabled. Its buttons toggle the resize policy and the title bar.
**Focus owner** requests focus for the main stage while the owned window is active.
[HarmonyOS Window API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-window/)

When the stage already has focus, `Activate()` needs no new focus operation. A visible stage can
request focus from a modeless child that currently holds it through `WindowStage.shiftAppWindowFocus`.
Pending modal windows keep focus in the modal route. [HarmonyOS WindowStage API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-windowstage/)

`TryGetPlatformHandle()` exposes the WindowManager ID after the host supplies it, with
the descriptor `OH_WindowManager window ID`. Its value is an integer identifier for
OpenHarmony `OH_WindowManager_*` APIs that accept a window ID. The ID survives replacement
of the XComponent surface. Reacquire it from the top level when needed and use it only
while that window remains live.

`WindowIcon` accepts Avalonia bitmaps, streams, and paths and saves a PNG snapshot.
HarmonyOS supplies the native window icon from the application configuration.

## Child safe areas and keyboard occlusion

Each child observes the avoid areas of its own native window. The backend intersects those
rectangles with the active XComponent and reports the result in surface-local DIP through
`Window.InsetsManager` and `Window.InputPane`. The input pane closes when the child loses
focus. Surface replacement clears both features until fresh geometry arrives.

HarmonyOS availability depends on the device and native window mode. In free-floating
mode under a freeform window, the OS reports the fixed soft keyboard area. For some other
modes, a child must match the main window's frame for the OS to supply areas. The backend
asks modal windows to enable avoidance and continues opening the window if HarmonyOS
rejects that request. [HarmonyOS Window API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-window/)

On Windows, run `dotnet run --project tests/ChildInsetsProbe` to exercise the hosted
Avalonia windows with injected native geometry. For a device check, focus the Counter
child's TextBox and follow the `Input pane` and `Safe area` messages in HiLog.

In the combined API 26 x86_64 Counter build, the owned window kept system focus when its
TextBox attached to the native input method. Its input pane reported open with an empty
occlusion rectangle. The owned window stood above the fixed keyboard area reported for the
main window. An overlapping fixed keyboard in a window mode that supplies child avoid areas
still needs a device check. The Windows probe covers overlapping child geometry.

## Verification

A Windows probe exercised a real Avalonia owner and child with separate Skia frames,
modal input blocking, a typed dialog result, owner reactivation, platform-driven close,
and survival of the owner when its surface is replaced. The x86_64 API 26 Counter HAP
includes the real NativeAOT and ArkTS bridges. On Huawei_TripleFold, the Avalonia modal dialog
presented a separate 1208 × 863 frame while the owner presented at 3184 × 1872. A real tap
incremented the child counter. The same interaction on the owner had no effect while the
dialog was open. Closing the child returned 1 to its caller and restored the owner input.
A modeless owned window also presented a separate frame. It moved to physical position
1036,529, resized from about 420 to 500 DIP wide, closed, and left the owner interactive.

In the integrated Counter, the child TextBox attached the HarmonyOS input method to the
child window ID reported by WindowManager. The keyboard opened while that window held focus.
In the accessibility-enabled
HAP, both open surfaces registered successfully under ScreenReader. Activating the
main button through ScreenReader incremented its counter. An API 26 x86_64 experiment
then verified activation in a moved child window. The same result was reproduced with the production bridge. A single touch focused the child
counter button. Double-touching a blank area invoked that button and changed the
child count to 1.

The final close-fix HAP repeated the normal modal and modeless flows. The modal child
presented its own frame, ignored owner touches, accepted one child tap, and returned 1.
The owner accepted another tap after the modal close. The modeless child then opened,
closed, and left the owner interactive.

## Window closure

A child stops accepting input as soon as its managed window requests closure. The
backend keeps its native handle and surface routing until HarmonyOS confirms destruction.
If destruction fails, it can release native modality, hide the window and retry.
A typed dialog result completes after destruction is confirmed. If the system confirms
the child is hidden or no longer modal, the owner can accept input while cleanup
continues. The Stage itself belongs to UIAbility. Closing its managed Window prevents
that same stage from reopening during a surface resize. A new WindowStage starts a
new managed owner.

The closure regression probes cover failed show cleanup, repeated destroy rejection,
retry, fallback hide and modality, and destruction during a stage change. The Windows
Avalonia probe also exercises stale ScreenReader actions during modality. These failure
paths were verified by injection. The emulator smoke test used normal window closure.
Run the
ArkTS service probe with the TypeScript compiler supplied by the DevEco API 26 SDK.

```powershell
$typescript = Join-Path $DevEcoRoot 'sdk/default/openharmony/ets/build-tools/ets-loader/node_modules/typescript/lib/typescript.js'
node tests/WindowCloseProbe/CloseProbe.cjs $typescript
```

[HarmonyOS WindowStage API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-windowstage/)
