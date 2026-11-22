using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.OpenHarmony;
using Avalonia.Threading;

var errors = new ConcurrentQueue<string>();
var closeReply = new TaskCompletionSource<OpenHarmonySystemResult>();
OpenHarmonyWindowOptions options = new()
{
    Request = (_, value) =>
    {
        using var request = JsonDocument.Parse(value);
        return request.RootElement.GetProperty("operation").GetString() == "close"
            ? closeReply.Task : Task.FromResult(new OpenHarmonySystemResult(string.Empty, null));
    }
};
var host = new OpenHarmonyHost(() => AppBuilder.Configure<ProbeApp>().With(options),
    (_, _, _, _, _) => { }, errors.Enqueue);
await host.Ready.WaitAsync(TimeSpan.FromSeconds(10));
await Dispatcher.UIThread.InvokeAsync(() => RunAsync(host)).WaitAsync(TimeSpan.FromSeconds(10));
Check(errors.IsEmpty, string.Join(Environment.NewLine, errors));

static async Task RunAsync(OpenHarmonyHost host)
{
    const long stageToken = 1;
    const long firstToken = 2;
    const long secondToken = 3;
    const long replacementToken = 4;
    var stageInset = new OpenHarmonyAvoidArea(default, new PixelRect(0, 0, 1000, 30), default, default);
    var childInset = new OpenHarmonyAvoidArea(default, new PixelRect(0, 0, 500, 24), default, default);
    var keyboard = new OpenHarmonyAvoidArea(default, default, default, new PixelRect(20, 300, 300, 160));
    host.UpdateSurface(stageToken, 1000, 720, 2);
    host.SetSurfaceOffset(stageToken, default);
    host.SetWindowAvoidArea(OpenHarmonyAvoidAreaKind.System, stageInset);
    await FlushAsync();
    var stage = (Window)TopLevel.GetTopLevel(ProbeApp.Main)!;
    Check(stage.InsetsManager!.SafeAreaPadding.Top == 15, "Stage inset initial value");

    var first = new Window();
    first.Show(stage);
    var second = new Window();
    second.Show(stage);
    var firstInsets = first.InsetsManager!;
    var firstPane = first.InputPane!;
    var secondPane = second.InputPane!;
    Attach(1, firstToken, new PixelPoint(20, 12));
    Attach(2, secondToken, new PixelPoint(20, 12));
    host.ReportChildEvent(1, OpenHarmonyChildWindowEvent.Focused);
    host.SetChildAvoidArea(firstToken, OpenHarmonyAvoidAreaKind.Cutout, childInset);
    host.SetChildAvoidArea(firstToken, OpenHarmonyAvoidAreaKind.Keyboard, keyboard);
    host.SetChildAvoidArea(secondToken, OpenHarmonyAvoidAreaKind.Keyboard, keyboard);
    await FlushAsync();
    Check(firstInsets.SafeAreaPadding == new Thickness(0, 6, 0, 0), "Child safe area uses its surface offset and scale");
    Check(firstPane.State == InputPaneState.Open && firstPane.OccludedRect == new Rect(0, 144, 150, 56),
        "Child pane reports only the overlap in surface DIP");
    Check(secondPane.State == InputPaneState.Closed && secondPane.OccludedRect == default &&
        stage.InputPane!.State == InputPaneState.Closed && stage.InsetsManager.SafeAreaPadding.Top == 15,
        "Device inset events are isolated by window and focus");

    host.UpdateChildSurface(1, firstToken, 400, 288, 1);
    await FlushAsync();
    Check(firstPane.State == InputPaneState.Open && firstPane.OccludedRect == default &&
        firstInsets.SafeAreaPadding.Top == 12, "Viewport shrink keeps the pane open with no overlap and reapplies scale");

    host.ReportChildEvent(1, OpenHarmonyChildWindowEvent.Unfocused);
    host.SetChildAvoidArea(firstToken, OpenHarmonyAvoidAreaKind.Keyboard, keyboard);
    host.ReportChildEvent(2, OpenHarmonyChildWindowEvent.Focused);
    host.SetChildAvoidArea(secondToken, OpenHarmonyAvoidAreaKind.Keyboard, keyboard);
    await FlushAsync();
    Check(firstPane.State == InputPaneState.Closed && secondPane.State == InputPaneState.Open &&
        firstInsets.SafeAreaPadding.Top == 12, "A focus switch clears stale keyboard data while preserving safe padding");
    host.SetLifecycle(OpenHarmonyLifecycleEvent.ApplicationBackground);
    await FlushAsync();
    Check(secondPane.State == InputPaneState.Closed, "Backgrounding clears the focused child pane");

    host.SetLifecycle(OpenHarmonyLifecycleEvent.ApplicationForeground);
    host.ReportChildEvent(1, OpenHarmonyChildWindowEvent.Focused);
    Attach(1, replacementToken, new PixelPoint(20, 12));
    host.SetChildAvoidArea(firstToken, OpenHarmonyAvoidAreaKind.Cutout, childInset);
    host.SetChildAvoidArea(firstToken, OpenHarmonyAvoidAreaKind.Keyboard, keyboard);
    host.CloseChildSurface(1, firstToken);
    await FlushAsync();
    Check(firstInsets.SafeAreaPadding == default && firstPane.State == InputPaneState.Closed,
        "Surface replacement clears the snapshot and ignores callbacks from the old token");
    host.SetChildAvoidArea(replacementToken, OpenHarmonyAvoidAreaKind.Cutout, childInset);
    host.SetChildAvoidArea(replacementToken, OpenHarmonyAvoidAreaKind.Keyboard, keyboard);
    await FlushAsync();
    Check(firstInsets.SafeAreaPadding.Top == 6 && firstPane.State == InputPaneState.Open, "Replacement surface receives live data");
    host.CloseChildSurface(1, replacementToken);
    host.SetChildAvoidArea(replacementToken, OpenHarmonyAvoidAreaKind.Cutout, childInset);
    await FlushAsync();
    Check(firstInsets.SafeAreaPadding == default && firstPane.State == InputPaneState.Closed,
        "Surface detach clears both features");

    host.ReportChildEvent(2, OpenHarmonyChildWindowEvent.Focused);
    host.SetChildAvoidArea(secondToken, OpenHarmonyAvoidAreaKind.Cutout, childInset);
    host.SetChildAvoidArea(secondToken, OpenHarmonyAvoidAreaKind.Keyboard, keyboard);
    await FlushAsync();
    Check(secondPane.State == InputPaneState.Open, "Existing child resumes after the application returns");
    var secondInsets = second.InsetsManager!;
    second.Close();
    Check(secondPane.State == InputPaneState.Closed, "Managed close clears the pane before the native close reply");
    host.SetChildAvoidArea(secondToken, OpenHarmonyAvoidAreaKind.Keyboard, keyboard);
    host.ReportChildEvent(2, OpenHarmonyChildWindowEvent.Destroyed);
    await FlushAsync();
    Check(secondInsets.SafeAreaPadding == default && secondPane.State == InputPaneState.Closed,
        "Destroying the child clears safe area and stops late callbacks");

    void Attach(int key, long token, PixelPoint offset)
    {
        host.UpdateChildSurface(key, token, 400, 400, 2);
        host.SetSurfaceOffset(token, offset);
        host.ReportChildEvent(key, OpenHarmonyChildWindowEvent.Shown);
    }
}

static async Task FlushAsync() => await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
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
