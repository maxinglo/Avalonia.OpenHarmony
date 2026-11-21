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
  Counter implements these callbacks in `samples/Counter/Program.Windows.cs`.
- Run the Counter build with `-Windowed`. The HAP is placed in the `windowed` subdirectory
  for its ABI and build mode. Use the counter and move/resize commands in the modal dialog,
  then select **Return dialog result** and check the main counter remains interactive.

The windowed host uses software framebuffers and supports child windows directly owned
by the stage. Window decorations and availability depend on the device's window mode.
A device that rejects a frame at creation can create a child with platform-default geometry.
Standalone top levels, owners other than the stage, and desktop controls such as custom
title-bar drags and native min/max constraints need separate support.

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
Insets observation currently follows the stage window. In the accessibility-enabled
HAP, both open surfaces registered successfully under ScreenReader. Activating the
main button through ScreenReader incremented its counter. An API 26 x86_64 experiment
then verified activation in a moved child window. A single touch focused the child
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
