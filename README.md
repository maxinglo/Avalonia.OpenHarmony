# Avalonia.OpenHarmony

Avalonia.OpenHarmony is an independent adapter for hosting Avalonia 12.1.6 views in a HarmonyOS API 26 `XComponent`.
Avalonia lays out, styles and draws the controls. ArkTS supplies the surface lifecycle and touch events. A C++ N-API
module presents Skia's CPU-rendered framebuffer through the HarmonyOS NativeWindow API.

The Counter sample uses Avalonia's Simple theme, a text block and a button. Tapping **Add one** updates the label and
writes the new count to hilog.

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
   `libavalonia_ohos_bridge.so`. Use the sample's N-API declarations and `XComponentController` as a starting point.
   Call `configureFiles(this.context.filesDir)` before creating the surface so Fontconfig can find HarmonyOS system
   fonts.
4. Include the managed shared object and the graphics libraries prepared by `scripts/PrepareOpenHarmonyGraphics.ps1` in
   `entry/libs/<abi>`. Include the applicable licensing files for the backend, runtime, SDK C++ library and graphics
   dependencies in the packaged resources.

`OpenHarmonyHost` runs on its own Avalonia dispatcher. Surface and touch callbacks can arrive on the ArkTS thread and
are queued to that dispatcher. The frame callback receives a premultiplied RGBA8888 framebuffer whose address remains
valid for the duration of the callback. Surface IDs remain strings in ArkTS, and C++ assigns independent monotonic
identifiers to surface instances. Touch coordinates sent to the C# host are physical pixels.

## Compatibility

The backend uses the available Avalonia 12.1.6 implementation assemblies and exact package versions. Its initial scope
is a single embedded Avalonia view, software rendering, surface recreation, resizing and touch. Platform integration for
native text input, system clipboard, pointing devices, accessibility, multiple windows and accelerated rendering is
pending. Applications own their service and settings lifetimes.

The graphics preparation script pins and checks the two NuGet native asset packages and the auxiliary libraries supplied
by the pinned OpenHarmony .NET checkout. HarmonyOS supplies `libc.so`. See the distribution and license details
in [THIRD-PARTY.md](THIRD-PARTY.md).

## Emulator verification

The API 26 x86_64 Counter HAP was installed and launched on the Huawei_TripleFold local emulator. The native bridge presented an Avalonia frame and tapping **Add one** updated the counter to 1.

## Repository workflow

Develop features and fixes on separate branches and review them in pull requests to main. Promote selected main commits to preview in a release pull request. Update stable after preview validation. Use a hotfix branch for urgent stable fixes and bring the fix back to main.
