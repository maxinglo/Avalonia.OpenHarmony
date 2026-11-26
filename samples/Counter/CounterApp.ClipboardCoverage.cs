#if OHOS
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Counter;

public sealed partial class CounterApp
{
    private const string ClipboardHtml = "<b>Avalonia — 中文 🎧</b>";
    private const string ClipboardSecondText = "Second clipboard item";
    private const string ClipboardCustomText = "定制\0🎧";
    private static readonly DataFormat<string> ClipboardHtmlFormat = DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly DataFormat<string> ClipboardCustomTextFormat =
        DataFormat.CreateStringApplicationFormat("avalonia.counter.text");
    private static readonly DataFormat<byte[]> ClipboardCustomBytesFormat =
        DataFormat.CreateBytesApplicationFormat("avalonia.counter.bytes");
    private static readonly DataFormat<byte[]> ClipboardEmptyBytesFormat =
        DataFormat.CreateBytesApplicationFormat("avalonia.counter.empty");
    private static readonly DataFormat<object> ClipboardLocalFormat =
        DataFormat.CreateInProcessFormat<object>("avalonia.counter.local");
    private static readonly byte[] ClipboardCustomBytes = [0, 17, 127, 255];
    private static readonly PixelSize ClipboardBitmapSize = new(12, 8);

    private static (Control Panel, (string Name, Control Element)[] Targets) CreateClipboardCoverage()
    {
        var copy = new Button { Content = "Copy rich clipboard sample" };
        var clear = new Button { Content = "Clear clipboard" };
        var receiver = new TextBox
        {
            PlaceholderText = "Focus here, then tap system Paste",
            MinWidth = 230,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        InputMethod.SetIsInputMethodEnabled(receiver, false);
        var preview = new Image { Width = 96, Height = 64, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };

        void Report(string message)
        {
            result.Text = message;
            Trace(message);
        }

        copy.Click += async (_, _) => await RunAsync(async () =>
        {
            if (TopLevel.GetTopLevel(receiver)?.Clipboard is not { } clipboard)
                return;
            using var bitmap = CreateClipboardBitmap();
            var item = DataTransferItem.CreateText(SampleText);
            item.Set(ClipboardHtmlFormat, ClipboardHtml);
            item.Set(DataFormat.Bitmap, bitmap);
            item.Set(ClipboardCustomTextFormat, ClipboardCustomText);
            item.Set(ClipboardCustomBytesFormat, ClipboardCustomBytes);
            item.Set(ClipboardEmptyBytesFormat, []);
            item.Set(ClipboardLocalFormat, new object());
            var transfer = new DataTransfer();
            transfer.Add(item);
            transfer.Add(DataTransferItem.CreateText(ClipboardSecondText));
            await clipboard.SetDataAsync(transfer);
            await clipboard.FlushAsync();
            receiver.Focus();
            Report("Rich clipboard sample copied");
        }, Report);

        clear.Click += async (_, _) => await RunAsync(async () =>
        {
            if (TopLevel.GetTopLevel(receiver)?.Clipboard is not { } clipboard)
                return;
            await clipboard.ClearAsync();
            receiver.Focus();
            Report("Clipboard cleared");
        }, Report);

        void ShowBitmap(Bitmap? bitmap)
        {
            var previous = preview.Source as Bitmap;
            preview.Source = bitmap;
            preview.IsVisible = bitmap is not null;
            previous?.Dispose();
        }

        receiver.PastingFromClipboard += async (_, args) =>
        {
            args.Handled = true;
            await RunAsync(async () =>
            {
                if (TopLevel.GetTopLevel(receiver)?.Clipboard is not { } clipboard)
                    return;
                using var data = await clipboard.TryGetDataAsync();
                if (data is null)
                {
                    ShowBitmap(null);
                    Report("Clipboard empty");
                    return;
                }

                var first = data.Items[0];
                var text = await first.TryGetTextAsync();
                var html = await first.TryGetValueAsync(ClipboardHtmlFormat);
                var bitmap = await data.TryGetBitmapAsync();
                ShowBitmap(bitmap);
                if (text != SampleText || html != ClipboardHtml)
                {
                    Report($"Clipboard pasted items={data.Items.Count} bitmap={bitmap?.PixelSize.ToString() ?? "none"}");
                    return;
                }

                var ordered = data.Items.Count == 2 && await data.Items[1].TryGetTextAsync() == ClipboardSecondText;
                var customText = await first.TryGetValueAsync(ClipboardCustomTextFormat) == ClipboardCustomText;
                var customBytes = (await first.TryGetValueAsync(ClipboardCustomBytesFormat))?.SequenceEqual(ClipboardCustomBytes) == true;
                var empty = await first.TryGetValueAsync(ClipboardEmptyBytesFormat) is { Length: 0 };
                var localExcluded = !first.Contains(ClipboardLocalFormat) && await clipboard.TryGetInProcessDataAsync() is null;
                var pixelsMatch = bitmap is not null && MatchesClipboardBitmap(bitmap);
                Report($"Rich clipboard pasted ordered={ordered} bitmap={pixelsMatch} " +
                    $"customText={customText} customBytes={customBytes} empty={empty} localExcluded={localExcluded}");
            }, Report);
        };

        var panel = new StackPanel
        {
            Spacing = 8,
            Children = { new WrapPanel { Children = { copy, clear } }, receiver, preview, result }
        };
        panel.DetachedFromVisualTree += (_, _) => ShowBitmap(null);
        return (panel, [("Copy rich clipboard sample", copy), ("Clipboard receiver", receiver), ("Clear clipboard", clear)]);
    }

    private static byte[] CreateClipboardPixels()
    {
        ReadOnlySpan<byte> colors = [240, 112, 40, 255, 64, 32, 8, 128, 0, 0, 0, 0, 24, 64, 220, 255];
        var pixels = new byte[ClipboardBitmapSize.Width * ClipboardBitmapSize.Height * 4];
        for (var y = 0; y < ClipboardBitmapSize.Height; y++)
        {
            for (var x = 0; x < ClipboardBitmapSize.Width; x++)
            {
                var color = (x / 3 + y / 2) % 4;
                colors.Slice(color * 4, 4).CopyTo(pixels.AsSpan((y * ClipboardBitmapSize.Width + x) * 4, 4));
            }
        }
        return pixels;
    }

    private static unsafe Bitmap CreateClipboardBitmap()
    {
        fixed (byte* pixels = CreateClipboardPixels())
            return new Bitmap(PixelFormats.Rgba8888, AlphaFormat.Premul, (nint)pixels, ClipboardBitmapSize,
                new Vector(96, 96), ClipboardBitmapSize.Width * 4);
    }

    private static unsafe bool MatchesClipboardBitmap(Bitmap bitmap)
    {
        if (bitmap.PixelSize != ClipboardBitmapSize)
            return false;
        var expected = CreateClipboardPixels();
        var actual = new byte[expected.Length];
        fixed (byte* pixels = actual)
        {
            using var framebuffer = new LockedFramebuffer((nint)pixels, bitmap.PixelSize, bitmap.PixelSize.Width * 4,
                bitmap.Dpi, PixelFormats.Rgba8888, AlphaFormat.Premul, null);
            bitmap.CopyPixels(framebuffer);
        }
        return actual.AsSpan().SequenceEqual(expected);
    }
}
#endif
