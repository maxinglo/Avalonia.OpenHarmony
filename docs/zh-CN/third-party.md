<a id="chap-third-party"></a>

# 第三方软件说明

[英文原文：`THIRD-PARTY.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/THIRD-PARTY.md) · [返回目录](../../README.zh-CN.md#contents)

Counter HAP 将下列文档打包在 `resources/rawfile/licenses/` 和 `resources/rawfile/graphics-licenses/` 下，可离线查阅。

- **Avalonia 12.1.6** 提供控件、渲染器集成、文本整形和 Simple 主题。其 MIT 许可证与声明也涵盖来自同一源码修订版本的 Avalonia.HarfBuzz、Avalonia.Remote.Protocol、Avalonia.Skia 和 Avalonia.Themes.Simple 包。[Avalonia 许可证](https://github.com/AvaloniaUI/Avalonia/blob/cf8e564674700348947e93ab19105f27afe859a9/licence.md) · [Avalonia 声明](https://github.com/AvaloniaUI/Avalonia/blob/cf8e564674700348947e93ab19105f27afe859a9/NOTICE.md)
- **MicroCom.Runtime 0.11.6** 提供 Avalonia 引用的 COM 互操作类型。[MicroCom MIT 许可证](https://github.com/kekekeks/MicroCom/blob/76785efcafd91b5902fd19dd11145f6dd655b7b4/LICENSE)
- **Counter 及其 C++ 桥接层** 按 Avalonia.OpenHarmony 仓库的 [MIT 许可证](license.md#license-en)分发。
- **.NET 运行时。** Counter 使用固定在提交 `9114affc54e9d7d94246708b841ec7f037119ea6` 的 OpenHarmony .NET 运行时包，以及由 SDK 解析选用的 Microsoft .NET 运行时包。仓库随附的 OpenHarmony CoreLib 将自身标识为 .NET 9.0.2。.NET 9.0.2 原版 MIT 许可证和第三方声明，与两个经过测试的 Microsoft .NET 9.0.23 Linux musl 运行时包中的对应文件逐字节相同。若文件不同，构建过程会同时收录 Microsoft 包自身的声明。[OpenHarmony .NET](https://gitcode.com/OpenHarmonyPCDeveloper/ohos_net-opensource) · [.NET 许可证](https://github.com/dotnet/runtime/blob/v9.0.2/LICENSE.TXT) · [.NET 第三方声明](https://github.com/dotnet/runtime/blob/v9.0.2/THIRD-PARTY-NOTICES.TXT)
- **SkiaSharp 3.119.4 与 HarfBuzzSharp 8.3.1.3** 提供光栅图形和文本整形。它们的 Linux 原生资源包与相应托管绑定附带相同的 MIT 许可文件，因此 HAP 中的图形许可文件同时涵盖两种形式。图形准备脚本适配 musl ELF 依赖，并一并准备 Fontconfig 及其辅助库。参见 `scripts/graphics-packages.lock.json` 和 `scripts/graphics-licenses/`。[SkiaSharp 与 HarfBuzzSharp 源码](https://github.com/mono/SkiaSharp)
- **DevEco Native SDK** 提供与 N-API 桥接层一起打包的 `libc++_shared.so`。HAP 包含 SDK 原版 `llvm/NOTICE`，其中载有 LLVM 与 libc++ 条款。系统库由 HarmonyOS 提供。[HarmonyOS C++ 运行时文档](https://developer.huawei.com/consumer/en/doc/harmonyos-references/cpp/)
