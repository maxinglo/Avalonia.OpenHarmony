#if OHOS
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.OpenHarmony;

namespace Counter;

public static partial class Program
{
    internal static readonly OpenHarmonyTrayOptions TrayOptions = new();

    [UnmanagedCallersOnly(EntryPoint = "avaoh_report_tray_event")]
    public static unsafe void ReportTrayEvent(long id, byte* menuCode, int length)
    {
        if (Host.IsValueCreated && length is >= 0 and <= 256 && (length == 0 || menuCode != null))
            Host.Value.ReportTrayEvent(id, Encoding.UTF8.GetString(new ReadOnlySpan<byte>(menuCode, length)));
    }
}
#endif
