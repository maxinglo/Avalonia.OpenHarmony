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
  registration on stage destruction.
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
includes the real NativeAOT and ArkTS bridges. The underlying system child APIs have
been exercised in a standalone ArkTS app on Huawei_TripleFold. The full Avalonia
windowed package requires its own simulator acceptance.

[HarmonyOS WindowStage API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/arkts-apis-window-windowstage/)
