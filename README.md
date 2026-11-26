# Avalonia.OpenHarmony

[简体中文](README.zh-CN.md)

Avalonia.OpenHarmony is an independent backend for hosting Avalonia 12.1.6 in a HarmonyOS API 26 ArkUI `XComponent`.

**AI maintenance disclosure.** AI agents write and maintain almost all of this repository's code.

The backend supports a single embedded view, software rendering, optional Skia EGL, and native owned or modal windows.
It integrates system input, clipboard, document pickers, drag and drop, window insets and application lifecycle.
See the [Avalonia interface matrix](docs/platform-capabilities.md) for each interface's scope and the
[verification matrix](docs/platform-validation.md) for device evidence.

## Run Counter

Use PowerShell 7, .NET SDK 9.0.321 or later, and DevEco Studio 26.0.0.865 with the HarmonyOS API 26 Release SDK.

```powershell
git clone https://gitcode.com/OpenHarmonyPCDeveloper/ohos_net-opensource ../ohos_net-opensource
git -C ../ohos_net-opensource checkout 9114affc54e9d7d94246708b841ec7f037119ea6
./scripts/Build-Counter.ps1 -DevEcoRoot 'C:/Program Files/Huawei/DevEco Studio' -OpenHarmonyNetRoot ../ohos_net-opensource -Abi x86_64
```

The default build writes `artifacts/packages/x86_64/debug/AvaloniaCounter-unsigned.hap`. Install it on a local API 26
x86_64 emulator with `hdc install`, open Counter and tap **Add one**. Use `-Windowed` for native owned windows or
`-UseEgl` for GPU rendering. Use `-Abi arm64-v8a` for an ARM64 build or `-StageOnly` to open the staged project in DevEco
Studio for signing. If OHPM cannot create symlinks on Windows, enable Developer Mode or run with the symlink privilege.

```powershell
dotnet run --project samples/Counter/Counter.csproj
```

The .NET command runs the Windows framebuffer and touch smoke test without a HAP.

## Integrate

Reference the backend project from a NativeAOT application and stage its C++ bridge and ArkTS components in your HAP.
The guide covers the host callbacks, optional platform services and lifecycle. [HAP integration guide](docs/integration.md)

Develop on feature branches and submit pull requests to `main`. Promote verified commits through `preview` and `stable`.
See [LICENSE](LICENSE) and [THIRD-PARTY.md](THIRD-PARTY.md) for distribution terms and third-party notices.
