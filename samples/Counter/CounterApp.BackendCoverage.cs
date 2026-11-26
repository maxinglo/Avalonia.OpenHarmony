#if OHOS
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Counter;

public sealed partial class CounterApp
{
    private static Control CreateBackendCoverage(Action back)
    {
        var navigation = new WrapPanel();
        var body = new ContentControl();
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var page = new StackPanel { Spacing = 12, Children = { navigation, body, result } };
        var targets = new List<(string Name, Control Element)>();
        var close = new Button { Content = "Back to Counter" };
        close.Click += (_, _) => back();
        navigation.Children.Add(close);
        targets.Add(("Back to Counter", close));

        AddPage("Input checks", CreateHardwareCoverage(Report));
        AddPage("Storage checks", CreateStorageCoverage(page, Report));
        AddPage("Clipboard checks", CreateClipboardCoverage());
        ObserveTopLevel(page, targets.ToArray());
        return page;

        void Report(string message)
        {
            result.Text = message;
            Trace(message);
        }

        void AddPage(string name, (Control Panel, (string Name, Control Element)[] Targets) check)
        {
            var button = new Button { Content = name };
            button.Click += (_, _) => body.Content = check.Panel;
            navigation.Children.Add(button);
            targets.Add((name, button));
            ObserveTopLevel(check.Panel, check.Targets);
            body.Content ??= check.Panel;
        }
    }

    private static (Control Panel, (string Name, Control Element)[] Targets) CreateHardwareCoverage(Action<string> report)
    {
        var instructions = new TextBlock { Text = "Use a mouse, touch, pen, or hardware key here." };
        var pad = new Border
        {
            Child = instructions,
            Background = Brushes.LightSteelBlue,
            Padding = new Thickness(16),
            Height = 120,
            Focusable = true
        };
        var moved = false;
        var hovered = false;
        pad.PointerPressed += (_, args) =>
        {
            pad.Focus();
            moved = false;
            ReportPointer("down", args);
        };
        pad.PointerMoved += (_, args) =>
        {
            if (args.Pointer.Type != PointerType.Pen)
                return;
            if (args.GetCurrentPoint(pad).Properties.IsLeftButtonPressed)
            {
                if (!moved)
                    ReportPointer("move", args);
                moved = true;
            }
            else
            {
                if (!hovered)
                    ReportPointer("hover", args);
                hovered = true;
            }
        };
        pad.PointerReleased += (_, args) =>
        {
            hovered = false;
            ReportPointer("up", args);
        };
        pad.PointerCaptureLost += (_, _) => Trace("Input capture lost");
        pad.AddHandler(InputElement.KeyDownEvent, (_, args) =>
            report($"Input key: {args.Key}, physical {args.PhysicalKey}, modifiers {args.KeyModifiers}"),
            RoutingStrategies.Tunnel, handledEventsToo: true);

        var appearance = new Button { Content = "Read system colors" };
        appearance.Click += (_, _) =>
        {
            var values = Application.Current?.PlatformSettings?.GetColorValues();
            report($"System colors: theme {values?.ThemeVariant}, accent {values?.AccentColor1}");
        };
        var panel = new StackPanel { Spacing = 8, Children = { pad, appearance } };
        return (panel, [("Input pad", pad), ("Read system colors", appearance)]);

        void ReportPointer(string phase, PointerEventArgs args)
        {
            var point = args.GetCurrentPoint(pad);
            report($"Input {args.Pointer.Type} {phase}: button {point.Properties.PointerUpdateKind}, " +
                $"pressure {point.Properties.Pressure:F3}, tilt {point.Properties.XTilt:F1}/{point.Properties.YTilt:F1}, " +
                $"modifiers {args.KeyModifiers}");
        }
    }
}
#endif
