using Avalonia;
using Avalonia.OpenHarmony;
using System.Runtime.InteropServices;

namespace Counter;

public static partial class Program
{
#if OHOS
    private static readonly Lazy<OpenHarmonyHost> Host = new(() =>
    {
        CounterApp.Trace = Report;
        return new OpenHarmonyHost(AppBuilder.Configure<CounterApp>, Present, Report);
    });

    [UnmanagedCallersOnly(EntryPoint = "avaoh_resize")]
    public static void Resize(long surface, int width, int height, double scaling) =>
        Host.Value.UpdateSurface(surface, width, height, scaling);

    [UnmanagedCallersOnly(EntryPoint = "avaoh_destroy")]
    public static void Destroy(long surface)
    {
        if (Host.IsValueCreated)
            Host.Value.DestroySurface(surface);
    }

    [UnmanagedCallersOnly(EntryPoint = "avaoh_touch")]
    public static void Touch(long surface, long contact, int phase, double x, double y)
    {
        if (Host.IsValueCreated)
            Host.Value.Touch(surface, contact, (TouchPhase)phase, x, y);
    }

    [LibraryImport("libavalonia_ohos_bridge.so", EntryPoint = "avaoh_present")]
    private static partial void Present(long surface, IntPtr pixels, int width, int height, int rowBytes);

    [LibraryImport("libavalonia_ohos_bridge.so", EntryPoint = "avaoh_report", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void Report(string message);
#else
    public static async Task Main()
    {
        var rendered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clicked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CounterApp.Trace = message =>
        {
            Console.WriteLine(message);
            clicked.TrySetResult(message == "Count: 1");
        };
        var host = new OpenHarmonyHost(AppBuilder.Configure<CounterApp>,
            (_, pixels, width, height, rowBytes) =>
            {
                if (pixels != IntPtr.Zero && width == 480 && height == 300 && rowBytes == 1920)
                    rendered.TrySetResult(true);
            }, Console.Error.WriteLine);
        host.UpdateSurface(1, 480, 300, 1);
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        host.Touch(1, 0, TouchPhase.Begin, 60, 85);
        host.Touch(1, 0, TouchPhase.End, 60, 85);
        if (!await clicked.Task.WaitAsync(TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException("Touch did not activate the Avalonia button.");
        host.DestroySurface(1);
        Console.WriteLine("An Avalonia surface rendered and received touch.");
    }
#endif
}
