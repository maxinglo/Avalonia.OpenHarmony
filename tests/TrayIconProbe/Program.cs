using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenHarmony;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

var errors = new ConcurrentQueue<string>();
var updates = new List<TrayState>();
var firstShow = new TaskCompletionSource<OpenHarmonySystemResult>(TaskCreationOptions.RunContinuationsAsynchronously);
var failHide = false;
var accepted = new OpenHarmonySystemResult(string.Empty, null);
var options = new OpenHarmonyTrayOptions
{
    Request = (operation, argument) =>
    {
        if (operation == "tray.supported")
            return Task.FromResult(new OpenHarmonySystemResult("true", null));
        Check(operation == "tray.apply", "Unexpected system request.");
        using var data = JsonDocument.Parse(argument);
        var state = data.RootElement;
        var visible = state.GetProperty("visible").GetBoolean();
        updates.Add(new(long.Parse(state.GetProperty("id").GetString()!), visible,
            visible ? state.GetProperty("toolTip").GetString() : null,
            visible ? state.GetProperty("menu").GetString() : null));
        if (updates.Count == 1)
            return firstShow.Task;
        return Task.FromResult(!visible && failHide ? default : accepted);
    }
};
var host = new OpenHarmonyHost(() => AppBuilder.Configure<Application>().With(options),
    (_, _, _, _, _) => { }, errors.Enqueue);
await host.Ready.WaitAsync(TimeSpan.FromSeconds(10));
await Dispatcher.UIThread.InvokeAsync(async () =>
{
    Check(await options.IsSupportedAsync(), "Status bar capability was not reported.");
    var app = Application.Current!;
    var first = new TrayIcon { IsVisible = true, ToolTipText = "First" };
    var clicks = 0;
    first.Clicked += (_, _) => clicks++;
    TrayIcon.SetIcons(app, new TrayIcons { first });
    first.ToolTipText = "Latest";
    await Flush();
    Check(updates is [{ Visible: true, ToolTip: "Latest" }], "The initial setters were not coalesced.");
    var oldId = updates[0].Id;
    first.ToolTipText = "Never published";
    await Flush();
    Check(updates.Count == 1, "An update ran before the first request finished.");

    var replacement = new TrayIcon { IsVisible = true, ToolTipText = "Replacement" };
    replacement.Clicked += (_, _) => clicks += 10;
    TrayIcon.SetIcons(app, new TrayIcons { replacement });
    await Flush();
    Check(updates is [_, { Visible: false }, { Visible: true, ToolTip: "Replacement" }] &&
        updates[1].Id == oldId && updates[2].Id != oldId,
        "Replacement show did not follow the removed owner's hide.");
    firstShow.SetResult(accepted);
    host.ReportTrayEvent(oldId, string.Empty);
    host.ReportTrayEvent(updates[2].Id, string.Empty);
    await Flush();
    Check(clicks == 10, "A stale click was delivered or the active icon did not receive its click.");

    host.ReportTrayEvent(0, string.Empty);
    await Flush();
    await Flush();
    Check(updates.Count == 4 && updates[3] == updates[2], "A new stage did not recover the icon state.");
    using var iconStream = new MemoryStream(Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="));
    using var icon = new Bitmap(iconStream);
    var item = new NativeMenuItem("Icon") { Icon = icon };
    var selected = 0;
    item.Click += (_, _) => selected++;
    replacement.Menu = new NativeMenu { Items = { item } };
    await Flush();
    await Flush();
    using (var menu = JsonDocument.Parse(updates[^1].Menu!))
    {
        var entry = menu.RootElement[0][0];
        Check(Convert.FromBase64String(entry.GetProperty("icon").GetString()!).Length > 0,
            "The menu icon was not encoded.");
        host.ReportTrayEvent(updates[^1].Id, entry.GetProperty("code").GetString()!);
    }
    await Flush();
    Check(selected == 1, "The menu action did not reach Avalonia.");
    item.Icon = null;
    await Flush();
    await Flush();
    using (var menu = JsonDocument.Parse(updates[^1].Menu!))
        Check(!menu.RootElement[0][0].TryGetProperty("icon", out _), "The removed menu icon remained published.");
    var beforeHide = updates.Count;
    failHide = true;
    replacement.IsVisible = false;
    await Flush();
    Check(errors.Count == 1, "A failed hide was not reported.");
    failHide = false;
    TrayIcon.SetIcons(app, null);
    Check(updates.Count == beforeHide + 2 && updates[^2] == updates[^1] && !updates[^1].Visible,
        "Disposal did not retry removal of the possibly active native icon.");
}).WaitAsync(TimeSpan.FromSeconds(10));
Check(errors.TryDequeue(out var failure) && failure == "OpenHarmony could not remove the status bar item." && errors.IsEmpty,
    string.Join(Environment.NewLine, errors));
Console.WriteLine("Status bar description coalescing, replacement, callbacks, stage recovery, and hide failure cleanup passed.");

static async Task Flush() => await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed record TrayState(long Id, bool Visible, string? ToolTip, string? Menu);
