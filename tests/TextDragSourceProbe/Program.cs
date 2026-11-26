using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.OpenHarmony;
using Avalonia.Threading;

var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var requests = new List<(string Operation, string Argument)>();
var systemEffect = "copy";
var host = new OpenHarmonyHost(
    () => AppBuilder.Configure<ProbeApp>().With(new OpenHarmonyTextDragOptions
    {
        Request = (operation, argument) =>
        {
            requests.Add((operation, argument));
            return Task.FromResult(new OpenHarmonySystemResult(systemEffect, null));
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
        new([DataFormat.Text, DataFormat.File], [DataFormat.Text]),
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
host.HardwarePointer(1, 1, 1, 70, 70, 0, 0, (int)RawInputModifiers.LeftMouseButton, -43);
var mousePress = await ProbeApp.MousePressed.Task.WaitAsync(TimeSpan.FromSeconds(5));
await Dispatcher.UIThread.InvokeAsync(async () =>
{
    var offer = new DataTransfer();
    offer.Add(DataTransferItem.CreateText("Mouse drag"));
    Check(await DragDrop.DoDragDropAsync(mousePress, offer, DragDropEffects.Copy) == DragDropEffects.Copy,
        "A primary mouse press from the native drag gesture should be accepted.");
    using var argument = JsonDocument.Parse(requests[^1].Argument);
    Check(argument.RootElement.GetProperty("gesture").GetInt64() == -43,
        "The native mouse gesture must reach the system request.");

    var item = DataTransferItem.CreateText("Accessible description");
    item.Set(ProbeApp.Html, "<strong>中文</strong>");
    item.Set(ProbeApp.Hyperlink, "https://avaloniaui.net/");
    var rich = new DataTransfer();
    rich.Add(item);
    systemEffect = "move";
    Check(await DragDrop.DoDragDropAsync(mousePress, rich, DragDropEffects.Copy | DragDropEffects.Move) == DragDropEffects.Move,
        "A successful system Move must reach a source that allows Move.");
    using var richArgument = JsonDocument.Parse(requests[^1].Argument);
    var richItem = richArgument.RootElement.GetProperty("items")[0];
    Check(requests[^1].Operation == "drag.data" && richArgument.RootElement.GetProperty("effects").GetInt32() == 3 &&
        richItem.GetProperty("html").GetString() == "<strong>中文</strong>" &&
        richItem.GetProperty("link").GetString() == "https://avaloniaui.net/",
        "HTML and hyperlink alternatives must travel with the same item and allowed effects.");

    var copyOnly = new DataTransfer();
    copyOnly.Add(DataTransferItem.CreateText("copy only"));
    Check(await DragDrop.DoDragDropAsync(mousePress, copyOnly, DragDropEffects.Copy) == DragDropEffects.None,
        "A destination's Move may not be reported to a Copy-only source.");
    var linkOnly = new DataTransfer();
    linkOnly.Add(DataTransferItem.CreateText("link only"));
    var lastCount = requests.Count;
    Check(await DragDrop.DoDragDropAsync(mousePress, linkOnly, DragDropEffects.Link) == DragDropEffects.None &&
        requests.Count == lastCount, "The HarmonyOS effect set cannot offer Link.");
});
host.HardwarePointer(1, 2, 1, 70, 70, 0, 0, 0);

const int shift = (int)RawInputModifiers.Shift;
const int richMoveFormats = 1 | 4 | 8 | 16;
const string drop = """{"items":[{"text":"Received text","html":"<em>中文</em>","link":"https://avaloniaui.net/"}]}""";
Check(host.ReceiveDragEffect(1, 0, 70, 70, shift, richMoveFormats, "") == DragDropEffects.Copy,
    "An unprocessed enter should still prefer Copy even when Move is allowed.");
Check(host.ReceiveDragEffect(1, 1, 70, 70, shift, richMoveFormats, "") == DragDropEffects.Move,
    "A target's Move choice must be reflected while a rich drag is over it.");
Check(host.ReceiveDragEffect(1, 3, 70, 70, shift, 16, drop) == DragDropEffects.Move &&
    ProbeApp.Dropped == ("Received text", "<em>中文</em>", "https://avaloniaui.net/"),
    "A rich drop must deliver the formats together and preserve the target's Move choice.");
Check(host.ReceiveDragEffect(1, 3, 70, 70, shift, 16 | 2, drop) == DragDropEffects.None,
    "The bridge must reject a target's Move if the source declared a file even when the file was unavailable.");
Check(!host.ReceiveDrag(1, 3, 70, 70, shift, 16, drop),
    "The original Copy-only entry point must never grant a Move.");
Console.WriteLine("Touch and mouse gestures, Unicode bounds, rich data, outbound file rejection, and Move boundaries passed.");

static void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}

public sealed class ProbeApp : Application
{
    public static readonly DataFormat<string> Html = DataFormat.CreateStringPlatformFormat("text/html");
    public static readonly DataFormat<string> Hyperlink = DataFormat.CreateStringPlatformFormat("text/uri-list");
    public static (string? Text, string? Html, string? Link) Dropped { get; private set; }
    public static TaskCompletionSource<PointerPressedEventArgs> Pressed { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static TaskCompletionSource<PointerPressedEventArgs> MousePressed { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override void OnFrameworkInitializationCompleted()
    {
        var root = new Border { Background = Brushes.Blue };
        DragDrop.SetAllowDrop(root, true);
        DragDrop.AddDragOverHandler(root, (_, args) =>
        {
            CheckRichPreview(args);
            args.DragEffects = DragDropEffects.Move;
        });
        DragDrop.AddDropHandler(root, (_, args) =>
        {
            var item = args.DataTransfer.Items.Single();
            Dropped = (item.TryGetText(), item.TryGetValue(Html), item.TryGetValue(Hyperlink));
            args.DragEffects = DragDropEffects.Move;
        });
        root.PointerPressed += (_, args) =>
        {
            if (args.Pointer.Type == PointerType.Mouse)
                MousePressed.TrySetResult(args);
            else
                Pressed.TrySetResult(args);
        };
        ((ISingleViewApplicationLifetime)ApplicationLifetime!).MainView = root;
        base.OnFrameworkInitializationCompleted();
    }

    private static void CheckRichPreview(DragEventArgs args)
    {
        if (args.DataTransfer.TryGetValue(Html) is not null || args.DataTransfer.TryGetValue(Hyperlink) is not null)
            throw new InvalidOperationException("The preview must expose only the format names.");
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
