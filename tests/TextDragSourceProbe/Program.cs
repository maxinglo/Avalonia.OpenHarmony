using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenHarmony;
using Avalonia.Threading;

var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var requests = new List<(string Operation, string Argument)>();
var host = new OpenHarmonyHost(
    () => AppBuilder.Configure<ProbeApp>().With(new OpenHarmonyTextDragOptions
    {
        Request = (operation, argument) =>
        {
            requests.Add((operation, argument));
            return Task.FromResult(new OpenHarmonySystemResult("copy", null));
        }
    }),
    (_, _, _, _, _) => ready.TrySetResult(), Console.Error.WriteLine);
host.UpdateSurface(1, 480, 300, 2);
await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
host.Touch(1, -42, TouchPhase.Begin, 70, 70);
var press = await ProbeApp.Pressed.Task.WaitAsync(TimeSpan.FromSeconds(5));

await Dispatcher.UIThread.InvokeAsync(async () =>
{
    ProbeOffer[] rejected =
    [
        new([DataFormat.Text, DataFormat.File], [DataFormat.Text, DataFormat.File]),
        new([DataFormat.File], [DataFormat.File]),
        new([DataFormat.Text], [DataFormat.Text, DataFormat.File])
    ];
    foreach (var offer in rejected)
    {
        Check(await DragDrop.DoDragDropAsync(press, offer, DragDropEffects.Copy) == DragDropEffects.None,
            "File offers must be refused.");
        Check(offer.Reads == 0 && offer.Disposals == 1, "A refused offer must be disposed without reading it.");
    }
    Check(requests.Count == 0, "No file offer may reach ArkUI.");

    const string text = "Avalonia — 中文 🎧";
    var plain = new ProbeOffer([DataFormat.Text], [DataFormat.Text], text);
    Check(await DragDrop.DoDragDropAsync(press, plain, DragDropEffects.Copy) == DragDropEffects.Copy,
        "Plain text should still complete.");
    var request = requests.Single();
    using var payload = JsonDocument.Parse(request.Argument);
    Check(request.Operation == "drag.text" &&
        payload.RootElement.GetProperty("text").GetString() == text &&
        payload.RootElement.GetProperty("gesture").GetInt64() == -42,
        "The platform request must preserve Unicode and the originating gesture.");
    Check(plain.Disposals == 1, "The platform owns disposal of the accepted text transfer.");
});
host.Touch(1, -42, TouchPhase.End, 70, 70);
Console.WriteLine("File-only and mixed offers were rejected before payload reads. Plain text reached the system bridge.");

static void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}

public sealed class ProbeApp : Application
{
    public static TaskCompletionSource<PointerPressedEventArgs> Pressed { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override void OnFrameworkInitializationCompleted()
    {
        var root = new Border { Background = Brushes.Blue };
        root.PointerPressed += (_, args) => Pressed.TrySetResult(args);
        ((ISingleViewApplicationLifetime)ApplicationLifetime!).MainView = root;
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class ProbeOffer(IReadOnlyList<DataFormat> formats, IReadOnlyList<DataFormat> itemFormats, string? text = null)
    : IDataTransfer, IDataTransferItem
{
    public IReadOnlyList<DataFormat> Formats => formats;
    IReadOnlyList<DataFormat> IDataTransferItem.Formats => itemFormats;
    public IReadOnlyList<IDataTransferItem> Items => [this];
    public int Reads { get; private set; }
    public int Disposals { get; private set; }

    public object? TryGetRaw(DataFormat format)
    {
        Reads++;
        return format == DataFormat.Text ? text : throw new InvalidOperationException("File data was read.");
    }

    public void Dispose() => Disposals++;
}
