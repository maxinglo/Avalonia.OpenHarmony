using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.OpenHarmony;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

var errors = new ConcurrentQueue<string>();
var requests = new ConcurrentQueue<JsonElement>();
var host = new OpenHarmonyHost(() => AppBuilder.Configure<ProbeApp>().With(new OpenHarmonyWindowOptions
    {
        Request = (_, value) =>
        {
            using var request = JsonDocument.Parse(value);
            requests.Enqueue(request.RootElement.Clone());
            return Task.FromResult(new OpenHarmonySystemResult(string.Empty, null));
        }
    }),
    (_, _, _, _, _) => { }, errors.Enqueue);
await host.Ready.WaitAsync(TimeSpan.FromSeconds(10));
await Dispatcher.UIThread.InvokeAsync(async () =>
{
    var floatingFrame = new PixelRect(48, 32, 810, 640);
    host.ReportChildEvent(0, OpenHarmonyChildWindowEvent.StageCreated);
    host.ReportChildEvent(0, OpenHarmonyChildWindowEvent.StageDesktopAvailable);
    host.ReportChildEvent(0, OpenHarmonyChildWindowEvent.StageNormal);
    host.ReportChildFrame(0, 7, 1, floatingFrame);
    host.UpdateSurface(1, 800, 600, 1);
    await Flush();
    var window = (Window)TopLevel.GetTopLevel(ProbeApp.Main)!;
    Check(window.IsActive, "Initially active window");
    Check(window.Position == floatingFrame.Position && window.FrameSize == new Size(810, 640),
        "The initial desktop stage uses its native frame before the surface attaches");
    window.Width = 820;
    await Flush();
    Check(requests.TryDequeue(out var resize) && resize.GetProperty("operation").GetString() == "resize" &&
        resize.GetProperty("width").GetInt32() == 830 && resize.GetProperty("height").GetInt32() == 640,
        "A width-only resize preserves the native frame height outside the Avalonia surface");
    window.WindowState = WindowState.Maximized;
    window.WindowState = WindowState.Maximized;
    Check(window.WindowState == WindowState.Normal && requests.Count == 1 && requests.TryDequeue(out var request) &&
        request.GetProperty("id").GetInt32() == 0 && request.GetProperty("state").GetString() == "maximized",
        "Repeated managed maximize writes queue one request and retain the observed normal state");
    host.ReportChildFrame(0, 7, 1, new PixelRect(0, 0, 1000, 800));
    host.UpdateSurface(1, 990, 760, 1);
    host.ReportChildEvent(0, OpenHarmonyChildWindowEvent.StageMaximized);
    await Flush();
    Check(window.WindowState == WindowState.Maximized && window.Position == default && window.FrameSize == new Size(1000, 800),
        "The native maximize notification updates the managed state and frame");
    window.WindowState = WindowState.Maximized;
    Check(requests.IsEmpty, "Repeating the observed maximize state sends no additional command");

    host.ReportChildEvent(0, OpenHarmonyChildWindowEvent.StageMinimized);
    host.DestroySurface(1);
    await Flush();
    Check(window.WindowState == WindowState.Minimized && !window.IsActive &&
        ReferenceEquals(window, TopLevel.GetTopLevel(ProbeApp.Main)), "Minimize and surface detach preserve the stage owner");
    host.ReportChildFrame(0, 7, 1, floatingFrame);
    host.UpdateSurface(3, 800, 600, 1);
    host.ReportChildEvent(0, OpenHarmonyChildWindowEvent.StageNormal);
    host.SetLifecycle(OpenHarmonyLifecycleEvent.WindowActive);
    await Flush();
    Check(ReferenceEquals(window, TopLevel.GetTopLevel(ProbeApp.Main)) && window.IsActive && window.WindowState == WindowState.Normal &&
        window.Position == floatingFrame.Position && window.FrameSize == new Size(810, 640),
        "Native restoration observes the same owner and exact floating frame after surface replacement");

    window.ExtendClientAreaTitleBarHeightHint = 46;
    window.ExtendClientAreaToDecorationsHint = true;
    await Flush();
    Check(requests.TryDequeue(out var extend) && extend.GetProperty("operation").GetString() == "setExtendedTitleBar" &&
        extend.GetProperty("extendTitleBar").GetBoolean() && extend.GetProperty("titleBarHeight").GetDouble() == 46 &&
        !window.IsExtendedIntoWindowDecorations,
        "Title bar extension requests use the height hint and wait for the native layout");
    host.ReportTitleBar(true, 46, 140);
    await Flush();
    Check(window.IsExtendedIntoWindowDecorations && window.WindowDecorationMargin == new Thickness(0, 46, 140, 0),
        "The extended title exposes the native reservation through Avalonia");
    host.ReportTitleBar(true, 46, 92);
    await Flush();
    Check(window.IsExtendedIntoWindowDecorations && window.WindowDecorationMargin.Right == 92,
        "The native button reservation can change without toggling extension");
    window.RequestedThemeVariant = ThemeVariant.Dark;
    await Flush();
    Check(requests.TryDequeue(out var darkTitle) && darkTitle.GetProperty("extendTitleBar").GetBoolean() &&
        darkTitle.GetProperty("titleBarTheme").GetString() == "dark",
        "Changing the Avalonia theme updates the native caption theme");
    window.ExtendClientAreaToDecorationsHint = false;
    await Flush();
    Check(requests.TryDequeue(out var restore) && !restore.GetProperty("extendTitleBar").GetBoolean(),
        "Clearing the hint requests the ordinary native title bar");
    host.ReportTitleBar(false, 0, 0);
    await Flush();
    Check(!window.IsExtendedIntoWindowDecorations && window.WindowDecorationMargin == default,
        "Restoring the ordinary title resets decoration margins");

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
