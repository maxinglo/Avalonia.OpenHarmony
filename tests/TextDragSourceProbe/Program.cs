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

    var multiple = new DataTransfer();
    multiple.Add(DataTransferItem.CreateText("first"));
    multiple.Add(DataTransferItem.CreateText(text));
    multiple.Add(DataTransferItem.CreateText("last"));
    Check(await DragDrop.DoDragDropAsync(press, multiple, DragDropEffects.Copy) == DragDropEffects.Copy,
        "An ordered text offer should complete.");
    var batch = requests[1];
    using var batchPayload = JsonDocument.Parse(batch.Argument);
    Check(batch.Operation == "drag.texts" &&
        batchPayload.RootElement.GetProperty("texts").EnumerateArray().Select(item => item.GetString())
            .SequenceEqual(["first", text, "last"]),
        "Every text item must reach the platform in offer order.");

    var overBudget = new DataTransfer();
    overBudget.Add(DataTransferItem.CreateText(new string('a', 64 * 1024 - 1)));
    overBudget.Add(DataTransferItem.CreateText("中"));
    Check(await DragDrop.DoDragDropAsync(press, overBudget, DragDropEffects.Copy) == DragDropEffects.None,
        "All of the items share the UTF-8 byte budget.");
    var tooMany = new DataTransfer();
    for (var item = 0; item < 33; item++)
        tooMany.Add(DataTransferItem.CreateText("extra"));
    Check(await DragDrop.DoDragDropAsync(press, tooMany, DragDropEffects.Copy) == DragDropEffects.None &&
        requests.Count == 2, "Oversized offers must not reach the bridge.");
});
host.Touch(1, -42, TouchPhase.End, 70, 70);
Console.WriteLine("File offers were rejected before payload reads. Ordered Unicode text and shared limits passed.");

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
