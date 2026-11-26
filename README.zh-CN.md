# Avalonia.OpenHarmony 中文文档

原项目：[maxinglo/Avalonia.OpenHarmony](https://github.com/maxinglo/Avalonia.OpenHarmony) · 英文来源：`main` 分支 · 翻译日期：2026-11-27。

本译本涵盖 README、12 篇接入与验证文档、第三方软件说明及许可证。复现示例时，按钮名称、日志、命令和 API 标识符请按原文使用。验证记录是原项目报告的结果，不是译者重新运行的测试。

<a id="contents"></a>

## 目录

1. [项目说明与 Counter 快速入门](#chap-readme)
2. [在 HAP 中接入 Avalonia](docs/zh-CN/integration.md#chap-integration)
3. [Avalonia 平台接口对照表](docs/zh-CN/platform-capabilities.md#chap-platform-capabilities)
4. [HarmonyOS 从属窗口](docs/zh-CN/owned-windows.md#chap-owned-windows)
5. [嵌入原生 ArkUI 控件](docs/zh-CN/native-controls.md#chap-native-controls)
6. [HarmonyOS PC 状态栏图标](docs/zh-CN/status-bar.md#chap-status-bar)
7. [硬件输入](docs/zh-CN/hardware-input.md#chap-hardware-input)
8. [输入法组合输入与行内预览](docs/zh-CN/ime-preedit.md#chap-ime-preedit)
9. [系统剪贴板](docs/zh-CN/clipboard.md#chap-clipboard)
10. [HarmonyOS 文档存储](docs/zh-CN/storage.md#chap-storage)
11. [接收拖入的文本和文件](docs/zh-CN/drag-and-drop.md#chap-drag-and-drop)
12. [发起系统拖放](docs/zh-CN/text-drag-source.md#chap-text-drag-source)
13. [平台验证记录](docs/zh-CN/platform-validation.md#chap-platform-validation)
14. [第三方软件说明](docs/zh-CN/third-party.md#chap-third-party)
15. [MIT 许可证（中文参考译文）](docs/zh-CN/license.md#chap-license)

<a id="chap-readme"></a>

## 项目说明与 Counter 快速入门

[英文原文：`README.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/README.md) · [返回目录](#contents)

Avalonia.OpenHarmony 是一个独立的平台后端，用来在 HarmonyOS API 26 的 ArkUI `XComponent` 中运行 Avalonia 12.1.6。

**关于 AI 维护的说明：** 本仓库几乎所有代码都由 AI 智能体编写和维护。

后端支持嵌入单个 Avalonia 视图，默认使用软件渲染，也可启用 Skia EGL。应用还可以创建原生从属窗口和模态窗口。后端集成了系统输入、剪贴板、文档选择器、拖放、窗口避让和应用生命周期。各接口能做什么、有哪些限制，见[平台接口对照表](docs/zh-CN/platform-capabilities.md#chap-platform-capabilities)；在哪些设备或模拟器上做过哪些测试，见[平台验证记录](docs/zh-CN/platform-validation.md#chap-platform-validation)。

### 运行 Counter 示例

构建环境：PowerShell 7、.NET SDK 9.0.321 或更高版本，以及安装了 HarmonyOS API 26 Release SDK 的 DevEco Studio 26.0.0.865。在本仓库根目录执行：

```powershell
git clone https://gitcode.com/OpenHarmonyPCDeveloper/ohos_net-opensource ../ohos_net-opensource
git -C ../ohos_net-opensource checkout 9114affc54e9d7d94246708b841ec7f037119ea6
./scripts/Build-Counter.ps1 -DevEcoRoot 'C:/Program Files/Huawei/DevEco Studio' -OpenHarmonyNetRoot ../ohos_net-opensource -Abi x86_64
```

默认构建生成 `artifacts/packages/x86_64/debug/AvaloniaCounter-unsigned.hap`。使用 `hdc install` 将其安装到本地 API 26 x86_64 模拟器，打开 Counter，点击 **Add one**（加一）。

| 构建参数 | 用途 |
| --- | --- |
| `-Windowed` | 启用原生从属窗口。 |
| `-UseEgl` | 使用 GPU 渲染。 |
| `-Abi arm64-v8a` | 构建 ARM64 版本。 |
| `-StageOnly` | 仅准备工程文件，随后可在 DevEco Studio 中打开工程并签名。 |

如果 OHPM 在 Windows 上无法创建符号链接，请启用 Windows 开发者模式，或使用具有创建符号链接权限的终端运行构建。

只需在 Windows 上检查帧缓冲区渲染和触摸输入时，不必构建 HAP，运行以下命令即可：

```powershell
dotnet run --project samples/Counter/Counter.csproj
```

### 接入自己的应用

在 NativeAOT 应用中引用后端项目，把 C++ 桥接库和 ArkTS 组件放入 HAP。[HAP 接入指南](docs/zh-CN/integration.md#chap-integration)介绍宿主回调、可选的平台服务和生命周期。

开发工作在功能分支上进行，通过拉取请求合入 `main`。验证通过的提交依次进入 `preview` 和 `stable`。分发条款和第三方声明见 [LICENSE](docs/zh-CN/license.md#license-en) 和[第三方软件说明](docs/zh-CN/third-party.md#chap-third-party)。
