# Avalonia platform interfaces

This matrix describes the API 26 backend for Avalonia 12.1.6. The default host embeds one XComponent.
The optional windowed host adds a stage-owned Avalonia window and native owned windows.

| Avalonia interface | Implementation | Scope |
| --- | --- | --- |
| `IRenderTimer` | HarmonyOS NativeVSync drives the shared Avalonia render loop when available. | The managed timer drives rendering when the native clock is missing, fails to request a frame or stops delivering frames. [NativeVSync API](https://developer.huawei.com/consumer/en/doc/harmonyos-references-V14/_native_vsync-V14/) |
| `IDispatcherImpl` | Avalonia's `ManagedDispatcherImpl` provides the UI dispatcher on the host's dedicated thread. | Native and ArkTS callbacks marshal work to this dispatcher. ArkUI work stays on the ArkUI thread. |
| `IPlatformGraphics` | Optional Skia EGL context and window surface for the embedded view. | Software rendering is the default and the fallback. The windowed host and native children use software. |
| `IPlatformSettings` | Reads the initial system light or dark mode and updates it from HarmonyOS configuration changes. | Avalonia defaults supply interaction metrics. System accents and other system appearance values are pending. |
| `ICursorFactory` | Standard cursor styles and custom bitmap cursors with a pixel hotspot, applied per HarmonyOS window. | Styles use the closest available system shape. Custom images and hidden cursors depend on native input support. |
| `IPlatformIconLoader` | Loads Avalonia window icon data from a file, stream or bitmap. | HAP resources still supply the app icon. The window adapter has no dynamic title-bar icon setter. |
| `INativeControlHostImpl` | Unimplemented. | ArkUI can lay out the XComponent beside other ArkUI components. Embedding a native child inside Avalonia layout needs a host for the child's attachment, bounds, visibility and disposal. |
| `IPlatformHandle` | A native HarmonyOS WindowManager ID is available when the corresponding window is known. | The `OH_WindowManager window ID` descriptor identifies an integer ID for WindowManager APIs. The XComponent surface token and graphics pointer have separate lifetimes. |
| `IPlatformDragSource` | Opt-in touch drag for plain-text records with Copy semantics. The separate incoming path also accepts read-only files. | Desktop mouse initiation, outbound file authorization and Move or Link are pending. [Drag integration](drag-and-drop.md) and [outbound text](text-drag-source.md). |
| `IActivatableLifetime` | Application foreground and background events with `ActivationKind.Background`. | Window focus and application activation are tracked independently. `TryEnterBackground` and `TryLeaveBackground` return false. |
| `IScreenImpl` | Enumerates native displays with bounds, scale, orientation, primary display and working area. | Tracks display and working-area changes, and assigns windows using native geometry. When the system omits the working area, it uses display bounds. [Display Manager API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/capi-oh-display-manager-h/) |
| `IWindowImpl` | Optional stage window and direct native owned and modal windows. Owned windows support movement, resizing, size limits, decorations and close. | Native policies depend on device and window type. Focus transfer targets windows in the same stage. Independent top levels, nested owners and full desktop window-state control are pending. [Owned windows](owned-windows.md) |
| `ITrayIconImpl` | Unimplemented. | Huawei's Desktop Extension Kit has a PC and 2-in-1 status-bar API, with one icon per application and device capability checks. [Status-bar API](https://developer.huawei.com/consumer/en/doc/harmonyos-references/statusbar-extension-manager). |
| `INativeMenuExporter` | Unimplemented. | Avalonia menus render within the view. The PC status-bar context menu belongs to the status-bar item and has a limited menu model. |

See the [platform verification matrix](platform-validation.md) for the devices and probes used to validate these interfaces.
