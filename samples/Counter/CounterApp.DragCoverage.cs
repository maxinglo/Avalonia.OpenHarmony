#if OHOS
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Counter;

public sealed partial class CounterApp
{
    private const string WritableDragFileName = "avalonia-counter-writable.txt";
    private const string WritableDragFileContent = "Writable drag fixture\n";
    private static readonly DataFormat<string> DragHtmlFormat = DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly DataFormat<string> DragUriListFormat = DataFormat.CreateStringPlatformFormat("text/uri-list");

    private static Control CreateTextDragSource(Action<string> report)
    {
        var source = CreateDragSource("Drag Avalonia sample text", () =>
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(SampleText));
            return data;
        }, DragDropEffects.Copy, effect => report(effect == DragDropEffects.Copy
            ? "Sample text dragged: Copy"
            : "Sample text drag canceled"));
        return source;
    }

    private static (Control Panel, (string Name, Control Element)[] Targets) CreateDragCoverage(Action<string> report)
    {
        var rich = CreateDragSource("Drag HTML and hyperlink", () =>
        {
            var item = DataTransferItem.CreateText(SampleText);
            item.Set(DragHtmlFormat, "<strong>Avalonia — 中文 🎧</strong>");
            item.Set(DragUriListFormat, "https://avaloniaui.net/");
            var data = new DataTransfer();
            data.Add(item);
            return data;
        }, DragDropEffects.Copy, effect => report($"Rich drag result: {effect}"));

        var moveOffered = true;
        var move = CreateDragSource("Drag movable text", () =>
        {
            if (!moveOffered)
                return null;
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText("Avalonia move fixture"));
            return data;
        }, DragDropEffects.Copy | DragDropEffects.Move, effect =>
        {
            if (effect == DragDropEffects.Move)
                moveOffered = false;
            report($"Movable text result: {effect}");
        });
        var reset = new Button { Content = "Reset movable text" };
        reset.Click += (_, _) => moveOffered = true;

        var writeTarget = CreateWritableDropTarget(report);
        var panel = new WrapPanel
        {
            Children = { rich, move, reset, writeTarget }
        };
        return (panel, [("Rich drag source", rich), ("Movable text", move), ("Writable drop target", writeTarget)]);
    }

    private static Button CreateDragSource(string label, Func<IDataTransfer?> offer,
        DragDropEffects allowedEffects, Action<DragDropEffects> report)
    {
        var source = new Button
        {
            Background = Brushes.RoyalBlue,
            Content = label,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12)
        };
        // Handle the press before Button treats it as a click while keeping the native drag gesture active.
        source.AddHandler(InputElement.PointerPressedEvent, async (_, args) =>
        {
            if (args.Pointer.Type != PointerType.Touch &&
                (args.Pointer.Type != PointerType.Mouse ||
                 args.GetCurrentPoint(source).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed))
                return;

            args.Handled = true;
            if (offer() is not { } data)
                return;

            report(await DragDrop.DoDragDropAsync(args, data, allowedEffects));
        }, RoutingStrategies.Tunnel);
        source.Click += (_, _) => Trace($"Drag source button clicked: {label}");
        return source;
    }

    private static Border CreateWritableDropTarget(Action<string> report)
    {
        var target = new Border
        {
            Padding = new Thickness(12),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = "Append to writable drag fixture" }
        };
        DragDrop.SetAllowDrop(target, true);
        DragDrop.AddDragOverHandler(target, (_, args) => args.DragEffects =
            args.DataTransfer.Formats.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        DragDrop.AddDropHandler(target, (_, args) =>
        {
            var files = args.DataTransfer.TryGetFiles();
            args.DragEffects = DragDropEffects.None;
            if (files is null)
                return;

            if (files.Length != 1 || files[0] is not IStorageFile file || file.Name != WritableDragFileName)
            {
                foreach (var item in files)
                    item.Dispose();
                return;
            }

            args.DragEffects = DragDropEffects.Copy;
            _ = RunAsync(async () =>
            {
                using (file)
                {
                    var expected = Encoding.UTF8.GetBytes(WritableDragFileContent);
                    var buffer = new byte[expected.Length + 1];
                    await using var reader = await file.OpenReadAsync();
                    if (await reader.ReadAtLeastAsync(buffer, buffer.Length, false) != expected.Length ||
                        !buffer.AsSpan(0, expected.Length).SequenceEqual(expected))
                    {
                        report("Writable drag fixture content differs");
                        return;
                    }
                    try
                    {
                        await using var writer = await file.OpenWriteAsync();
                        writer.Seek(0, SeekOrigin.End);
                        await writer.WriteAsync("Counter wrote this marker\n"u8.ToArray());
                        await writer.FlushAsync();
                        report("Writable drag fixture updated");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        report("Writable drag fixture opened read-only");
                    }
                }
            }, report);
        });
        return target;
    }
}
#endif
