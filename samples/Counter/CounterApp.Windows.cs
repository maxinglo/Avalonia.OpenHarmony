using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Counter;

public sealed partial class CounterApp
{
    private static Control ChildWindowButtons()
    {
        var open = new Button { Content = "Open owned window" };
        open.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(open) is Window owner)
                MakeChild("Owned window", false).Show(owner);
        };
        var dialog = new Button { Content = "Open modal dialog" };
        dialog.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(dialog) is Window owner)
            {
                var result = await MakeChild("Modal dialog", true).ShowDialog<int>(owner);
                Trace($"Dialog result: {result}");
            }
        };
        return new WrapPanel { Children = { open, dialog } };
    }

    private static Window MakeChild(string title, bool modal)
    {
        var window = new Window
        {
            Title = title,
            Width = 420,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var counter = new Button { Content = "Child count: 0" };
        var number = 0;
        counter.Click += (_, _) =>
        {
            var label = $"Child count: {++number}";
            counter.Content = label;
            Trace(label);
        };
        var editor = new TextBox { PlaceholderText = "Type in child window", MaxWidth = 400 };
        var dropTarget = CounterDropTarget.Create(editor, message => Trace($"Child {message}"));
        var move = new Button { Content = "Move child" };
        move.Click += (_, _) => window.Position += new PixelPoint(48, 24);
        var resize = new Button { Content = "Resize child" };
        resize.Click += (_, _) => window.Width = window.Width < 480 ? 500 : 420;
        var close = new Button { Content = modal ? "Return dialog result" : "Close child" };
        close.Click += (_, _) =>
        {
            if (modal)
                window.Close(number);
            else
                window.Close();
        };
        var content = new Border
        {
            Padding = new Thickness(12),
            Child = new ScrollViewer
            {
                Content = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Spacing = 8,
                    Children = { counter, editor, dropTarget, new WrapPanel { Children = { move, resize, close } } }
                }
            }
        };
        window.Content = content;
        ObserveTopLevel(content, ("Child drop target", dropTarget));
        window.Opened += (_, _) => Trace("Avalonia child opened");
        window.PositionChanged += (_, e) => Trace($"Avalonia child position: {e.Point}");
        window.Resized += (_, e) => Trace($"Avalonia child size: {e.ClientSize}");
        window.Closed += (_, _) => Trace("Avalonia child closed");
        return window;
    }
}
