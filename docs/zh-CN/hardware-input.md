<a id="chap-hardware-input"></a>

# 硬件输入

[英文原文：`docs/hardware-input.md`](https://github.com/maxinglo/Avalonia.OpenHarmony/blob/main/docs/hardware-input.md) · [返回目录](../../README.zh-CN.md#contents)

Counter 将可获取焦点的 ArkUI `XComponent` 连接到 Avalonia 的鼠标和键盘设备。设备坐标按物理像素传递；命中测试前，后端将其除以表面缩放系数。鼠标移动、五个鼠标键、悬停离开、水平或垂直滚动以及失焦事件均走 Avalonia 标准事件路径。

硬件按键的按下和抬起事件携带 ArkUI 键码及 Alt、Ctrl、Shift、Meta 状态。系统转发时，后端会映射编辑键、导航键、F1–F24、小键盘、媒体播放控制和音量键。若按下事件未被处理，后端从 SDK 的 `KeyEvent.unicode` 生成基本拉丁文本。ArkUI 先给输入法处理按键的机会，再调用 `onKeyEvent`。依赖键盘布局的组合输入及其他文本由系统输入法处理。事件顺序及 Unicode 范围见 [ArkUI 键盘事件](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-interaction-development-guide-keyboard/)。

后端用传给 `OpenHarmonyHost` 的当前 HarmonyOS 窗口 ID 设置标准或自定义位图光标，并向 InputKit 传递原始像素尺寸及热点。自定义位图最大为 256 × 256 像素，热点必须位于图像内。`StandardCursorType.None` 使用窗口专属的透明位图。自定义光标无效或不可用时回退到默认箭头。InputKit 可能在跨越原生区域边界时重置光标；收到鼠标输入后，后端会重新应用 Avalonia 选择的光标。见[原生指针样式](https://developer.huawei.com/consumer/en/doc/harmonyos-references/capi-oh-input-manager-h/)。

滚轮输入保留系统灵敏度：每转动 15 度对应 1 个 Avalonia 滚轮单位。触控板每滚动 48 vp 对应 1 个滚轮单位。见 [ArkUI 鼠标事件](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-interaction-development-guide-mouse/)。

## 触控笔和手指触摸

宿主转发手指事件时会保留键盘修饰键。ArkUI 报告 `SourceTool.Pen` 时，事件使用 Avalonia 笔设备，包含笔尖按下、移动、抬起、悬停、压力及 X/Y 倾角。抬起、取消或失去输入时清除接触状态。这里使用的 ArkUI 回调提供笔尖和悬停数据；笔身按钮、橡皮擦及笔杆旋转角需要能报告这些属性的设备和输入路径。见 [ArkUI 触摸与笔输入](https://developer.huawei.com/consumer/en/doc/harmonyos-guides/arkts-interaction-development-guide-touch-screen/)和[悬停事件](https://developer.huawei.com/consumer/en/doc/harmonyos-references/ts-universal-events-hover/)。

PC Counter 收到鼠标和触摸事件，F13 的逻辑键与物理键均报告为 F13。该模拟器的 `uinput -S` 送达的是普通触摸事件，与公开注入工具的说明一致。托管 Counter 测试检查笔类型、压力、倾角及 Shift 状态在 Avalonia 命中测试后仍被保留。验证设备上的笔路径，需要能够保留工具类型的真实笔输入。见 [OpenHarmony 输入注入](https://github.com/openharmony/docs/blob/master/zh-cn/application-dev/dfx/uinput.md)。

## 应用导出函数

原生桥接层在应用的 NativeAOT 库中查找以下可选的 `UnmanagedCallersOnly` 导出函数。托管侧的转发方法见 Counter 示例。

| 导出函数 | 参数（依次列出） |
| --- | --- |
| `avaoh_pointer` | `int64_t surface`, `int32_t phase`, `int32_t button`, `double x`, `double y`, `double wheelX`, `double wheelY`, `int32_t modifiers` |
| `avaoh_key` | `int64_t surface`, `int32_t down`, `int32_t code`, `int32_t modifiers`, `int32_t unicode`, `int32_t locks` |
| `avaoh_input_blur` | `int64_t surface` |

指针阶段 0–4 依次为移动、按下、释放、离开、滚动。按钮码 0–5 依次为无按钮、左键、右键、中键、后退键、前进键。滚动增量使用 Avalonia 滚轮单位，负值表示向下或向右。修饰位与 `RawInputModifiers` 一致，包括当前按住的鼠标键。按键导出函数返回 `int32_t`：`1` 表示支持该事件，`0` 表示留给 ArkUI。`locks` 的第 0 位表示 NumLock。

<a id="chap-hardware-input--verification"></a>

## 验证

| 检查项 | 结果 |
| --- | --- |
| API 26 x86_64 ArkTS 和 C++ 桥接层 | 已使用 DevEco 工具链编译、打包。 |
| 托管应用导出 | 启用 HarmonyOS 入口点后编译成功。 |
| Windows 上的 Avalonia 输入 | 托管测试覆盖缩放坐标下的命中测试、鼠标按钮、可双轴滚动的 `ScrollViewer`、键盘文本与快捷键、失焦和触摸。 |
| Windows 光标 ABI | `tests/CursorProbe` 配合原生模拟库检查位图转换、物理热点、透明与标准光标切换，以及资源释放。 |
| API 26 MateBook Pro x86_64 | 注入的鼠标移动依次选中热点为 (7, 5) 的 32 × 24 位图、16 × 16 透明位图、文本光标和默认光标。对主窗口 WindowManager ID 的 InputKit 调用均返回 0。可见光标及热点位置仍可用物理设备核验。 |

在 Windows 上将 DevEco 的 LLVM 工具加入 PATH，再从仓库根目录执行：

```powershell
$out = 'artifacts/build/cursor-probe'
New-Item -ItemType Directory -Path $out -Force | Out-Null
clang.exe --target=x86_64-pc-windows-msvc -std=c11 -c tests/CursorProbe/mock.c -o "$out/mock.obj"
lld-link.exe /dll /noentry /nodefaultlib "/out:$out/mock.dll" "$out/mock.obj"
dotnet run --project tests/CursorProbe/CursorProbe.csproj -- "$out/mock.dll"
```
