using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.OpenHarmony;
using Avalonia.Platform;
using Avalonia.Threading;

var errors = new ConcurrentQueue<string>();
var host = new OpenHarmonyHost(() => AppBuilder.Configure<ProbeApp>().With(new OpenHarmonyWindowOptions
    { Request = (_, _) => Task.FromResult(new OpenHarmonySystemResult(string.Empty, null)) }),
    (_, _, _, _, _) => { }, errors.Enqueue);
await host.Ready.WaitAsync(TimeSpan.FromSeconds(10));
await Dispatcher.UIThread.InvokeAsync(async () =>
{
    host.UpdateSurface(1, 800, 600, 1);
    await Flush();
    var window = (Window)TopLevel.GetTopLevel(ProbeApp.Main)!;
    Check(window.IsActive, "Initially active window");
    var lifetime = (IActivatableLifetime)Application.Current!.ApplicationLifetime!;
    var activated = 0;
    var deactivated = 0;
    lifetime.Activated += (_, e) => { Check(e.Kind == ActivationKind.Background, "Foreground kind"); activated++; };
    lifetime.Deactivated += (_, e) => { Check(e.Kind == ActivationKind.Background, "Background kind"); deactivated++; };
    host.SetLifecycle(OpenHarmonyLifecycleEvent.ApplicationBackground);
    await Flush();
    Check(!window.IsActive && deactivated == 1, "Background deactivates window and lifetime");
    host.SetLifecycle(OpenHarmonyLifecycleEvent.WindowActive);
    await Flush();
    Check(!window.IsActive, "Window remains inactive in background");
    host.SetLifecycle(OpenHarmonyLifecycleEvent.ApplicationForeground);
    await Flush();
    Check(window.IsActive && activated == 1, "Foreground restores previously reported window focus");
    host.SetLifecycle(OpenHarmonyLifecycleEvent.ApplicationForeground);
    await Flush();
    Check(activated == 1, "Duplicate foreground does not duplicate lifetime event");
    host.SetLifecycle(OpenHarmonyLifecycleEvent.ApplicationBackground);
    host.SetLifecycle(OpenHarmonyLifecycleEvent.ApplicationForeground);
    await Flush();
    Check(!window.IsActive, "Foreground without window focus does not activate window");
    host.SetLifecycle(OpenHarmonyLifecycleEvent.WindowActive);
    await Flush();
    Check(window.IsActive && activated == 2 && deactivated == 2, "Later window focus activates window once");

    OpenHarmonyDisplayInfo[] displays = [
        new OpenHarmonyDisplayInfo(1, "First", new PixelRect(0, 0, 1000, 800),
            new PixelRect(0, 0, 1000, 760), 1, ScreenOrientation.Landscape, true),
        new OpenHarmonyDisplayInfo(2, "Second", new PixelRect(1000, 0, 1000, 800),
            new PixelRect(1000, 0, 1000, 800), 1, ScreenOrientation.Landscape, false)
    ];
    host.UpdateDisplays(displays);
    host.SetWindowPosition(1, default);
    await Flush();
    var child = new Window();
    child.Show(window);
    host.UpdateChildSurface(1, 2, 100, 100, 1);
    host.ReportChildFrame(1, 2, 1, new PixelRect(950, 0, 400, 400));
    host.ReportChildEvent(1, OpenHarmonyChildWindowEvent.Shown);
    await Flush();
    Check(window.Screens.ScreenFromWindow(child)?.DisplayName == "Second",
        "Cross-display window selection uses the native frame instead of the smaller surface");
    var second = window.Screens.All[1];
    var changes = 0;
    window.Screens.Changed += (_, _) => changes++;
    host.UpdateDisplays(displays);
    await Flush();
    Check(changes == 0, "Unchanged display snapshots do not raise an event");
    displays[1] = displays[1] with { WorkingArea = new PixelRect(1000, 0, 1000, 750), Scaling = 1.5 };
    host.UpdateDisplays(displays);
    await Flush();
    Check(changes == 1 && ReferenceEquals(second, window.Screens.All[1]) &&
        second.WorkingArea.Height == 750 && second.Scaling == 1.5,
        "Work area and scaling changes update the existing screen before notification");
    host.UpdateDisplays([displays[0]]);
    await Flush();
    Check(changes == 2 && window.Screens.ScreenCount == 1 && second.Bounds == default,
        "Removing a display clears its old screen and raises an event");
}).WaitAsync(TimeSpan.FromSeconds(10));
Check(errors.IsEmpty, string.Join(Environment.NewLine, errors));

static async Task Flush() => await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine(message);
}

public sealed class ProbeApp : Application
{
    public static Control Main { get; } = new Border();
    public override void OnFrameworkInitializationCompleted()
    {
        ((ISingleViewApplicationLifetime)ApplicationLifetime!).MainView = Main;
        base.OnFrameworkInitializationCompleted();
    }
}
