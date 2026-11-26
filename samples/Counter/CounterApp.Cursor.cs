#if OHOS
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Counter;

public sealed partial class CounterApp
{
    private static (Button Bitmap, Button Hidden) CreateCursorTargets()
    {
        var bitmapTarget = new Button { Content = "Bitmap cursor" };
        bitmapTarget.AttachedToVisualTree += (_, _) => bitmapTarget.Cursor = CreateBitmapCursor();
        var hiddenTarget = new Button { Content = "Hidden cursor" };
        hiddenTarget.AttachedToVisualTree += (_, _) => hiddenTarget.Cursor = new Cursor(StandardCursorType.None);
        foreach (var target in new[] { bitmapTarget, hiddenTarget })
        {
            target.DetachedFromVisualTree += (_, _) =>
            {
                target.Cursor?.Dispose();
                target.ClearValue(InputElement.CursorProperty);
            };
        }

        return (bitmapTarget, hiddenTarget);
    }

    private static unsafe Cursor CreateBitmapCursor()
    {
        var hotSpot = new PixelPoint(7, 5);
        using var bitmap = new WriteableBitmap(new PixelSize(32, 24), new Vector(96, 96),
            PixelFormat.Rgba8888, AlphaFormat.Premul);
        using (var frame = bitmap.Lock())
        {
            for (var y = 0; y < frame.Size.Height; y++)
            {
                var row = new Span<uint>((void*)(frame.Address + y * frame.RowBytes), frame.Size.Width);
                row.Clear();
                for (var x = 0; x < row.Length; x++)
                {
                    if (x == hotSpot.X || y == hotSpot.Y)
                        row[x] = 0xff21bcff;
                }
                if (y == hotSpot.Y)
                    row[hotSpot.X] = 0xffffffff;
            }
        }

        return new Cursor(bitmap, hotSpot);
    }
}
#endif
