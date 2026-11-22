# Third-party software

The Counter HAP provides these documents offline under `resources/rawfile/licenses/` and
`resources/rawfile/graphics-licenses/`.

- Avalonia 12.1.6 provides controls, renderer integration, text shaping and the Simple theme. Its MIT license and notices
  also cover the Avalonia.HarfBuzz, Avalonia.Remote.Protocol, Avalonia.Skia and Avalonia.Themes.Simple packages from the
  same source revision. [Avalonia license](https://github.com/AvaloniaUI/Avalonia/blob/cf8e564674700348947e93ab19105f27afe859a9/licence.md)
  [Avalonia notices](https://github.com/AvaloniaUI/Avalonia/blob/cf8e564674700348947e93ab19105f27afe859a9/NOTICE.md)
- MicroCom.Runtime 0.11.6 supplies COM interop types referenced by Avalonia. [MicroCom MIT license](https://github.com/kekekeks/MicroCom/blob/76785efcafd91b5902fd19dd11145f6dd655b7b4/LICENSE)
- The Counter and its C++ bridge are distributed under the Avalonia.OpenHarmony repository's [MIT license](LICENSE).
- The Counter uses the OpenHarmony .NET runtime pack at commit `9114affc54e9d7d94246708b841ec7f037119ea6` and the
  Microsoft .NET runtime pack resolved by the SDK. The vendored OpenHarmony CoreLib identifies itself as .NET 9.0.2.
  The original .NET 9.0.2 MIT license and third-party notices also match the files in both tested Microsoft .NET 9.0.23
  Linux musl runtime packs byte for byte. The build includes the Microsoft pack's own notices when the files differ.
  [OpenHarmony .NET](https://gitcode.com/OpenHarmonyPCDeveloper/ohos_net-opensource)
  [.NET license](https://github.com/dotnet/runtime/blob/v9.0.2/LICENSE.TXT)
  [.NET notices](https://github.com/dotnet/runtime/blob/v9.0.2/THIRD-PARTY-NOTICES.TXT)
- SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.3 provide raster graphics and text shaping. Their Linux native asset packages
  carry the same MIT license files as the corresponding managed bindings, so the HAP's graphics license files cover
  both forms. The graphics preparation script adapts the musl ELF dependencies and stages Fontconfig with its auxiliary
  libraries. See `scripts/graphics-packages.lock.json` and `scripts/graphics-licenses/`.
  [SkiaSharp and HarfBuzzSharp source](https://github.com/mono/SkiaSharp)
- The DevEco Native SDK supplies `libc++_shared.so`, which is packaged with the N-API bridge. The HAP includes the SDK's
  original `llvm/NOTICE` with the LLVM and libc++ terms. HarmonyOS supplies the system libraries.
  [HarmonyOS C++ runtime documentation](https://developer.huawei.com/consumer/en/doc/harmonyos-references/cpp/)
