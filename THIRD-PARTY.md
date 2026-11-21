# Third-party software

- Avalonia 12.1.6 provides the controls, renderer integration, and the implementation assemblies used by the backend. It
  is licensed under MIT. [Avalonia source and license](https://github.com/AvaloniaUI/Avalonia/tree/12.1.6)
- The Counter sample uses the .NET NativeAOT toolchain and the OpenHarmony .NET fork at commit
  `9114affc54e9d7d94246708b841ec7f037119ea6`. [OpenHarmony .NET source and notices](https://gitcode.com/OpenHarmonyPCDeveloper/ohos_net-opensource)
- SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.3 provide raster graphics and text shaping. The graphics preparation script
  stages the matching musl native assets and rewrites their dynamic libc dependency to the HarmonyOS library name. The
  script also stages the Fontconfig and auxiliary native libraries needed on HarmonyOS. See
  `scripts/graphics-packages.lock.json` for pinned asset information and `scripts/graphics-licenses/` for the
  redistributed notices. [SkiaSharp source and licenses](https://github.com/mono/SkiaSharp)
- The DevEco Native SDK and its headers, tools, and system libraries are supplied separately with DevEco
  Studio. [HarmonyOS developer documentation](https://developer.huawei.com/consumer/en/doc/)
