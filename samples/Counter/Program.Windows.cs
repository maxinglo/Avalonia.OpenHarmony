#if OHOS
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.OpenHarmony;

namespace Counter;

public static partial class Program
{
    [UnmanagedCallersOnly(EntryPoint = "avaoh_update_child_surface")]
    public static void UpdateChildSurface(int key, long token, int width, int height, double scaling)
    {
        if (Host.IsValueCreated)
            Host.Value.UpdateChildSurface(key, token, width, height, scaling);
    }

    [UnmanagedCallersOnly(EntryPoint = "avaoh_close_child_surface")]
    public static void CloseChildSurface(int key, long token)
    {
        if (Host.IsValueCreated)
            Host.Value.CloseChildSurface(key, token);
    }

    [UnmanagedCallersOnly(EntryPoint = "avaoh_report_child_frame")]
    public static void ReportChildFrame(int key, int windowId, long displayId, int x, int y, int width, int height)
    {
#if OHOS_COUNTER_WINDOWS
        if (key == 0 || Host.IsValueCreated)
#else
        if (Host.IsValueCreated)
#endif
            Host.Value.ReportChildFrame(key, windowId, displayId, new PixelRect(x, y, width, height));
    }

    [UnmanagedCallersOnly(EntryPoint = "avaoh_report_child_event")]
    public static void ReportChildEvent(int key, int state)
    {
#if OHOS_COUNTER_WINDOWS
        if (key == 0 || Host.IsValueCreated)
#else
        if (Host.IsValueCreated)
#endif
            Host.Value.ReportChildEvent(key, (OpenHarmonyChildWindowEvent)state);
    }

    [UnmanagedCallersOnly(EntryPoint = "avaoh_request_child_close")]
    public static int RequestChildClose(int key) => Host.IsValueCreated && Host.Value.RequestChildClose(key) ? 1 : 0;
}
#endif
