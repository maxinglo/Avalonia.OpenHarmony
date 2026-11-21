#if OHOS
using Avalonia;
using Avalonia.Media;
using Avalonia.OpenHarmony;
using System.Runtime.InteropServices;

namespace Counter;

public static partial class Program
{
    [UnmanagedCallersOnly(EntryPoint = "avaoh_set_avoid_area")]
    public static unsafe void SetAvoidArea(int kind, int* data)
    {
        if (data == null)
            return;
        Host.Value.SetWindowAvoidArea((OpenHarmonyAvoidAreaKind)kind,
            new OpenHarmonyAvoidArea(ReadRect(data), ReadRect(data + 4), ReadRect(data + 8), ReadRect(data + 12)));
    }

    private static unsafe PixelRect ReadRect(int* data) => new(data[0], data[1], data[2], data[3]);

    [UnmanagedCallersOnly(EntryPoint = "avaoh_set_window_inset_properties")]
    public static void SetInsetProperties(int edgeToEdge, int hasColor, uint color) =>
        Host.Value.SetWindowInsetProperties(edgeToEdge != 0, hasColor != 0 ? Color.FromUInt32(color) : null);

    [UnmanagedCallersOnly(EntryPoint = "avaoh_clear_window_insets")]
    public static void ClearInsets()
    {
        if (Host.IsValueCreated)
            Host.Value.ClearWindowInsets();
    }
}
#endif
