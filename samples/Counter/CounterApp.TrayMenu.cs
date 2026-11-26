#if OHOS
using Avalonia.Controls;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Counter;

public sealed partial class CounterApp
{
    private static Bitmap? s_trayMenuIcon;

    private static NativeMenu CreateTrayMenu(Action<string> report)
    {
        var count = 0;
        var increment = new NativeMenuItem("Add one from tray") { Icon = s_trayMenuIcon ??= CreateTrayMenuIcon() };
        increment.Click += (_, _) => report($"Tray menu count: {++count}");

        var selected = new NativeMenuItem("Selected option") { ToggleType = MenuItemToggleType.CheckBox };
        selected.Click += (_, _) =>
        {
            selected.IsChecked = !selected.IsChecked;
            report($"Tray menu selected: {selected.IsChecked}");
        };
        var nested = new NativeMenuItem("Tray menu options") { Menu = new() { selected } };
        return new NativeMenu { increment, new NativeMenuItemSeparator(), nested };
    }

    private static unsafe Bitmap CreateTrayMenuIcon()
    {
        var bitmap = new WriteableBitmap(new PixelSize(24, 24), new Vector(96, 96),
            PixelFormat.Rgba8888, AlphaFormat.Premul);
        using var frame = bitmap.Lock();
        for (var y = 0; y < frame.Size.Height; y++)
        {
            var row = new Span<uint>((void*)(frame.Address + y * frame.RowBytes), frame.Size.Width);
            row.Clear();
            for (var x = 2; x < 22; x++)
            {
                if (y >= 2 && y < 22)
                    row[x] = (x >= 10 && x < 14 && y >= 6 && y < 18) ||
                        (y >= 10 && y < 14 && x >= 6 && x < 18) ? 0xffffffffu : 0xffb93565u;
            }
        }
        return bitmap;
    }
}
#endif
