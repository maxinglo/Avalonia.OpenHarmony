using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Counter;

internal static class CounterDropTarget
{
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
            args.DragEffects = args.DataTransfer.Formats.Contains(DataFormat.Text) ||
                args.DataTransfer.Formats.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        DragDrop.AddDropHandler(target, (_, args) =>
        {
            var text = args.DataTransfer.TryGetText();
            var files = args.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().ToArray() ?? [];
            args.DragEffects = text is not null || files.Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            if (text is not null)
            {
                editor.Text = text;
                report($"Dropped text length: {text.Length}");
            }
            if (files.Length > 0)
                _ = ReadFilesAsync(files, report);
        });
        return target;
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
