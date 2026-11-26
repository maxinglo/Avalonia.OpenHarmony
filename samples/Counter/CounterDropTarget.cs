using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Counter;

internal static class CounterDropTarget
{
    private static readonly DataFormat<string> HtmlFormat = DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly DataFormat<string> UriFormat = DataFormat.CreateStringPlatformFormat("text/uri-list");

    public static Control Create(TextBox editor, Action<string> report)
    {
        var target = new Border
        {
            Padding = new Thickness(12),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = "Drop text or files here", TextWrapping = TextWrapping.Wrap }
        };
        DragDrop.SetAllowDrop(target, true);
        DragDrop.AddDragOverHandler(target, (_, args) =>
        {
            var formats = args.DataTransfer.Formats;
            args.DragEffects = ChooseEffect(args, formats.Contains(DataFormat.Text) || formats.Contains(HtmlFormat) ||
                formats.Contains(UriFormat), formats.Contains(DataFormat.File));
        });
        DragDrop.AddDropHandler(target, (_, args) =>
        {
            var text = args.DataTransfer.TryGetText();
            var html = args.DataTransfer.TryGetValue(HtmlFormat);
            var link = args.DataTransfer.TryGetValue(UriFormat);
            var files = args.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().ToArray() ?? [];
            args.DragEffects = ChooseEffect(args, text is not null || html is not null || link is not null, files.Length > 0);
            if (text is not null)
            {
                editor.Text = text;
                report($"Dropped text length: {text.Length}");
            }
            if (html is not null)
                report($"Dropped HTML length: {html.Length}");
            if (link is not null)
                report($"Dropped hyperlink length: {link.Length}");
            if (args.DragEffects == DragDropEffects.Move)
                report("Drop selected: Move");
            if (files.Length > 0)
                _ = ReadFilesAsync(files, report);
        });
        return target;
    }

    private static DragDropEffects ChooseEffect(DragEventArgs args, bool hasText, bool hasFiles)
    {
        if (!hasText && !hasFiles)
            return DragDropEffects.None;
        if (!hasFiles && (args.KeyModifiers & KeyModifiers.Shift) != 0 && (args.DragEffects & DragDropEffects.Move) != 0)
            return DragDropEffects.Move;
        return DragDropEffects.Copy;
    }

    private static async Task ReadFilesAsync(IStorageFile[] files, Action<string> report)
    {
        var buffer = new byte[256];
        foreach (var file in files)
        {
            using (file)
            {
                try
                {
                    await using var stream = await file.OpenReadAsync();
                    var bytes = await stream.ReadAsync(buffer);
                    report($"Dropped file read: {bytes} bytes");
                }
                catch (Exception)
                {
                    report("Could not read a dropped file");
                }
            }
        }
    }
}
