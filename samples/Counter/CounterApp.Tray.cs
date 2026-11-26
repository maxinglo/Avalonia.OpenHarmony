#if OHOS
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Counter;

public sealed partial class CounterApp
{
    private (Control Panel, (string Name, Control Element)[] Targets) CreateTrayControls()
    {
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void Report(string message)
        {
            status.Text = message;
            Trace(message);
        }

        var show = new Button { Content = "Show status bar icon", IsEnabled = false };
        var update = new Button { Content = "Update status bar icon", IsEnabled = false };
        var panel = new StackPanel
        {
            Spacing = 4,
            Children = { new WrapPanel { Children = { show, update } }, status }
        };
        TrayIcon? tray = null;
        var attachment = 0;
        var updates = 0;
        var clicks = 0;
        panel.AttachedToVisualTree += async (_, _) =>
        {
            var currentAttachment = ++attachment;
            await RunAsync(async () =>
            {
                var supported = await Program.TrayOptions.IsSupportedAsync();
                if (currentAttachment != attachment)
                    return;
                if (!supported)
                {
                    Report("HarmonyOS status bar unavailable");
                    return;
                }
                tray = new TrayIcon
                {
                    Icon = CreateTrayIcon(alternate: false),
                    ToolTipText = "Avalonia Counter",
                    Menu = CreateTrayMenu(Report),
                    IsVisible = false
                };
                tray.Clicked += (_, _) => Report($"Status bar icon clicked: {++clicks}");
                TrayIcon.SetIcons(this, new TrayIcons { tray });
                show.IsEnabled = true;
                Report("HarmonyOS status bar available");
            }, Report);
        };
        panel.DetachedFromVisualTree += (_, _) =>
        {
            attachment++;
            TrayIcon.SetIcons(this, null);
            tray?.Dispose();
            tray = null;
            show.IsEnabled = false;
            update.IsEnabled = false;
            show.Content = "Show status bar icon";
        };
        show.Click += (_, _) =>
        {
            if (tray is null)
                return;
            tray.IsVisible = !tray.IsVisible;
            show.Content = tray.IsVisible ? "Hide status bar icon" : "Show status bar icon";
            update.IsEnabled = tray.IsVisible;
            Report($"Status bar visibility requested: {tray.IsVisible}");
        };
        update.Click += (_, _) =>
        {
            if (tray is null)
                return;
            tray.Icon = CreateTrayIcon(alternate: ++updates % 2 != 0);
            tray.ToolTipText = $"Avalonia Counter ({updates})";
            Report($"Status bar update requested: {updates}");
        };
        return (panel, [("Show status bar icon", show), ("Update status bar icon", update)]);
    }

    private static unsafe WindowIcon CreateTrayIcon(bool alternate)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(24, 24), new Vector(96, 96),
            PixelFormat.Rgba8888, AlphaFormat.Premul);
        using (var frame = bitmap.Lock())
        {
            for (var y = 0; y < frame.Size.Height; y++)
            {
                var row = new Span<uint>((void*)(frame.Address + y * frame.RowBytes), frame.Size.Width);
                row.Clear();
                var color = alternate ? 0xff218a12u : 0xffb93565u;
                for (var x = 3; x < row.Length - 3; x++)
                {
                    if (y >= 3 && y < frame.Size.Height - 3)
                        row[x] = (y >= 9 && y < 15 && x >= 9 && x < 15) ? 0xffffffff : color;
                }
            }
        }
        return new WindowIcon(bitmap);
    }
}
#endif
