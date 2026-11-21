using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.OpenHarmony;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Runtime.InteropServices;

namespace Counter;

public static partial class Program
{
#if OHOS
    private static readonly Lazy<OpenHarmonyHost> Host = new(() =>
    {
        CounterApp.Trace = Report;
        return new OpenHarmonyHost(AppBuilder.Configure<CounterApp>, Present, Report, GetInputWindowId,
            new OpenHarmonyLauncher(OpenHarmonySystemRequests.RequestAsync), null);
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

    [UnmanagedCallersOnly(EntryPoint = "avaoh_pointer")]
    public static void Pointer(long surface, int phase, int button, double x, double y, double wheelX, double wheelY,
        int modifiers)
    {
        if (Host.IsValueCreated)
            Host.Value.HardwarePointer(surface, phase, button, x, y, wheelX, wheelY, modifiers);
    }

    [UnmanagedCallersOnly(EntryPoint = "avaoh_key")]
    public static int Key(long surface, int down, int code, int modifiers, int unicode, int locks) =>
        Host.IsValueCreated && Host.Value.HardwareKey(surface, down != 0, code, modifiers, unicode, locks) ? 1 : 0;

    [UnmanagedCallersOnly(EntryPoint = "avaoh_input_blur")]
    public static void InputBlur(long surface)
    {
        if (Host.IsValueCreated)
            Host.Value.HardwareInputBlur(surface);
    }

    [LibraryImport("libavalonia_ohos_bridge.so", EntryPoint = "avaoh_present")]
    private static partial void Present(long surface, IntPtr pixels, int width, int height, int rowBytes);

    [LibraryImport("libavalonia_ohos_bridge.so", EntryPoint = "avaoh_report", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void Report(string message);

    [LibraryImport("libavalonia_ohos_bridge.so", EntryPoint = "avaoh_ime_window_id")]
    private static partial int GetInputWindowId();
#else
    public static async Task Main()
    {
        var rendered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clicked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CounterApp.Trace = message =>
        {
            Console.WriteLine(message);
            if (message == "Count: 1")
                clicked.TrySetResult(true);
        };
        var launchRequests = 0;
        var launcher = new OpenHarmonyLauncher((operation, argument) =>
        {
            if (operation != "launcher.uri" || argument != "https://avaloniaui.net/")
                throw new InvalidOperationException("Unexpected launcher request.");
            launchRequests++;
            return Task.FromResult(new OpenHarmonySystemResult(string.Empty, null));
        });
        var host = new OpenHarmonyHost(AppBuilder.Configure<CounterApp>,
            (_, pixels, width, height, rowBytes) =>
            {
                if (pixels != IntPtr.Zero && width == 480 && height == 300 && rowBytes == 1920)
                    rendered.TrySetResult(true);
            }, Console.Error.WriteLine, null, launcher, null);
        host.UpdateSurface(1, 480, 300, 1);
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        host.Touch(1, 0, TouchPhase.Begin, 60, 85);
        host.Touch(1, 0, TouchPhase.End, 60, 85);
        if (!await clicked.Task.WaitAsync(TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException("Touch did not activate the Avalonia button.");
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var comboBox = CounterApp.PopupSelector
                ?? throw new InvalidOperationException("The Counter view did not create its ComboBox.");
            comboBox.IsDropDownOpen = true;
            var root = TopLevel.GetTopLevel(comboBox)
                ?? throw new InvalidOperationException("The ComboBox is not attached to the surface.");
            if (!root.GetVisualDescendants().OfType<OverlayPopupHost>().Any())
                throw new InvalidOperationException("The ComboBox has no in-surface popup host.");
            comboBox.IsDropDownOpen = false;
        });
        var actualLauncher = await Dispatcher.UIThread.InvokeAsync(() =>
            TopLevel.GetTopLevel(CounterApp.PopupSelector)!.Launcher);
        if (!await actualLauncher.LaunchUriAsync(new Uri("https://avaloniaui.net/")) ||
            await actualLauncher.LaunchUriAsync(new Uri("/relative", UriKind.Relative)) ||
            launchRequests != 1)
            throw new InvalidOperationException("The launcher did not handle supported and relative URIs correctly.");
        host.DestroySurface(1);
        Console.WriteLine("An Avalonia surface rendered, received touch, opened an overlay popup, and routed a launcher request.");
    }
#endif
}
