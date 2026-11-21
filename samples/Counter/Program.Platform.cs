#if OHOS
using Avalonia;
using Avalonia.OpenHarmony;
using Avalonia.Platform;
using System.Runtime.InteropServices;

namespace Counter;

public static partial class Program
{
    private static PlatformThemeVariant _initialTheme = PlatformThemeVariant.Light;

    [UnmanagedCallersOnly(EntryPoint = "avaoh_set_lifecycle")]
    public static void SetLifecycle(int transition) =>
        Host.Value.SetLifecycle((OpenHarmonyLifecycleEvent)transition);

    [UnmanagedCallersOnly(EntryPoint = "avaoh_set_theme")]
    public static void SetTheme(int dark)
    {
        _initialTheme = dark != 0 ? PlatformThemeVariant.Dark : PlatformThemeVariant.Light;
        if (Host.IsValueCreated)
            Host.Value.SetTheme(_initialTheme);
    }

    [UnmanagedCallersOnly(EntryPoint = "avaoh_set_displays")]
    public static void SetDisplays(nint displays, int count) => Host.Value.UpdateNativeDisplays(displays, count);

    [UnmanagedCallersOnly(EntryPoint = "avaoh_set_window_position")]
    public static void SetWindowPosition(long display, int x, int y) =>
        Host.Value.SetWindowPosition(display, new PixelPoint(x, y));

    [UnmanagedCallersOnly(EntryPoint = "avaoh_set_surface_offset")]
    public static void SetSurfaceOffset(long surface, int x, int y) =>
        Host.Value.SetSurfaceOffset(surface, new PixelPoint(x, y));

    [UnmanagedCallersOnly(EntryPoint = "avaoh_request_back")]
    public static int RequestBack() => Host.IsValueCreated && Host.Value.RequestBack() ? 1 : 0;
}
#endif
