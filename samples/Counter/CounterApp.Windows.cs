using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace Counter;

public sealed partial class CounterApp
{
    private static (Control Buttons, (string Name, Control Element)[] Targets) ChildWindowButtons()
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
        var policies = new Button { Content = "Open constrained window" };
        policies.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(policies) is not Window owner)
                return;
            if (owner.TryGetPlatformHandle() is { } handle)
                Trace($"Stage native handle {handle.HandleDescriptor}: {handle.Handle}");
            MakeChild("Constrained child", false, constrained: true).Show(owner);
        };
        var (stageButtons, stageTargets) = StageWindowButtons();
        return (new StackPanel { Spacing = 8, Children = { stageButtons, new WrapPanel { Children = { open, dialog, policies } } } },
            [.. stageTargets, ("Open owned window", open), ("Open modal dialog", dialog), ("Open constrained window", policies)]);
    }

    private static (Control Buttons, (string Name, Control Element)[] Targets) StageWindowButtons()
    {
        var panel = new WrapPanel();
        var targets = new List<(string, Control)>();
        Add("Stage minimize", window => SetState(window, WindowState.Minimized));
        Add("Stage maximize", window => SetState(window, WindowState.Maximized));
        Add("Stage normal", window => SetState(window, WindowState.Normal));
        Add("Stage fullscreen", window => SetState(window, WindowState.FullScreen));
        Add("Stage resize", window =>
        {
            var width = window.ClientSize.Width > 650 ? 620 : 720;
            Trace($"Stage resize requested: width {width}");
            window.Width = width;
        });
        Add("Stage caption policy", window =>
        {
            window.CanMinimize = !window.CanMinimize;
            window.CanMaximize = window.CanMinimize;
            Trace($"Stage caption policy requested: minimize {window.CanMinimize}, maximize {window.CanMaximize}");
        });
        Add("Toggle title bar extension", window =>
        {
            window.ExtendClientAreaTitleBarHeightHint = 44;
            window.ExtendClientAreaToDecorationsHint = !window.ExtendClientAreaToDecorationsHint;
        });

        Window? owner = null;
        WindowState? lastState = null;
        panel.AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(panel) is not Window window)
                return;
            owner = window;
            lastState = window.WindowState;
            window.PropertyChanged += StateChanged;
            window.PositionChanged += PositionChanged;
            window.Resized += Resized;
            Report(window, "attached");
        };
        panel.DetachedFromVisualTree += (_, _) =>
        {
            if (owner is not { } window)
                return;
            window.PropertyChanged -= StateChanged;
            window.PositionChanged -= PositionChanged;
            window.Resized -= Resized;
            Report(window, "detached");
            owner = null;
        };
        return (panel, targets.ToArray());

        void Add(string name, Action<Window> action)
        {
            var button = new Button { Content = name };
            button.Click += (_, _) =>
            {
                if (TopLevel.GetTopLevel(button) is Window window)
                    action(window);
            };
            panel.Children.Add(button);
            targets.Add((name, button));
        }

        static void SetState(Window window, WindowState state)
        {
            Trace($"Stage state requested: {state}");
            window.WindowState = state;
        }

        void StateChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Window.WindowStateProperty && owner is { } window && lastState != window.WindowState)
            {
                lastState = window.WindowState;
                Report(window, "state changed");
            }
        }

        void PositionChanged(object? sender, PixelPointEventArgs args)
        {
            if (owner is { } window)
                Report(window, "moved");
        }

        void Resized(object? sender, WindowResizedEventArgs args)
        {
            if (owner is { } window)
                Report(window, $"resized ({args.Reason})");
        }

        static void Report(Window window, string change)
        {
            Trace($"Stage {change}: owner {System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(window)}, " +
                $"native {window.TryGetPlatformHandle()?.Handle}, {window.WindowState}, " +
                $"position {window.Position}, frame {window.FrameSize}, client {window.ClientSize}");
        }
    }

    private static Control WithCustomTitleBar(Control content)
    {
        var action = new Button { Content = "Title action", VerticalAlignment = VerticalAlignment.Center };
        var clicks = 0;
        action.Click += (_, _) => Trace($"Title action clicked: {++clicks}");
        var drag = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(12, 0),
            Child = new TextBlock { Text = "Avalonia title", VerticalAlignment = VerticalAlignment.Center }
        };
        drag.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(drag).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(drag) is Window window)
            {
                window.BeginMoveDrag(args);
                args.Handled = true;
            }
        };
        DockPanel.SetDock(action, Dock.Left);
        var header = new Border
        {
            IsVisible = false,
            Child = new DockPanel { Children = { action, drag } }
        };
        header[!Border.BackgroundProperty] = new DynamicResourceExtension("ThemeBackgroundBrush");
        DockPanel.SetDock(header, Dock.Top);
        ObserveTopLevel(header, ("Title drag", drag), ("Title action", action));
        Window? owner = null;
        header.AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(header) is not Window window)
                return;
            owner = window;
            window.PropertyChanged += OnWindowPropertyChanged;
            Update(window);
        };
        header.DetachedFromVisualTree += (_, _) =>
        {
            if (owner is { } window)
                window.PropertyChanged -= OnWindowPropertyChanged;
            owner = null;
        };
        return new DockPanel { Children = { header, content } };

        void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (owner is { } window && (args.Property == Window.IsExtendedIntoWindowDecorationsProperty ||
                args.Property == Window.WindowDecorationMarginProperty))
                Update(window);
        }

        void Update(Window window)
        {
            var margin = window.WindowDecorationMargin;
            header.IsVisible = window.IsExtendedIntoWindowDecorations;
            header.Height = margin.Top;
            header.Padding = new Thickness(12, 0, margin.Right, 0);
            Trace($"Title bar extension: {header.IsVisible}, height {margin.Top}, right {margin.Right}");
        }
    }

    private static Window MakeChild(string title, bool modal, bool constrained = false)
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
#if OHOS
        var dragSource = CreateTextDragSource(message => Trace($"Child {message}"));
#endif
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
        var controls = new WrapPanel { Children = { move, resize, close } };
        List<(string Name, Control Element)> targets =
            [("Child count", counter), ("Child editor", editor), ("Child drop target", dropTarget),
                ("Move child", move), ("Resize child", resize), ("Close child", close)];
#if OHOS
        targets.Add(("Child drag source", dragSource));
#endif
        if (constrained)
        {
            window.MinWidth = 360;
            window.MinHeight = 280;
            window.MaxWidth = 520;
            window.MaxHeight = 400;
            window.CanResize = false;
            var toggleResize = new Button { Content = "Enable edge resize" };
            toggleResize.Click += (_, _) =>
            {
                window.CanResize = !window.CanResize;
                toggleResize.Content = window.CanResize ? "Disable edge resize" : "Enable edge resize";
                Trace($"Constrained child CanResize: {window.CanResize}");
            };
            var toggleTitleBar = new Button { Content = "Hide title bar" };
            toggleTitleBar.Click += (_, _) =>
            {
                var decorated = window.WindowDecorations == WindowDecorations.Full;
                window.WindowDecorations = decorated ? WindowDecorations.None : WindowDecorations.Full;
                toggleTitleBar.Content = decorated ? "Show title bar" : "Hide title bar";
            };
            var focusOwner = new Button { Content = "Focus owner" };
            focusOwner.Click += (_, _) =>
            {
                if (window.Owner is { } owner)
                {
                    Trace("Constrained child requested owner focus");
                    owner.Activate();
                }
            };
            controls.Children.Add(toggleResize);
            controls.Children.Add(toggleTitleBar);
            controls.Children.Add(focusOwner);
            targets.Add(("Toggle edge resize", toggleResize));
            targets.Add(("Toggle title bar", toggleTitleBar));
            targets.Add(("Focus owner", focusOwner));
            window.Activated += (_, _) =>
            {
                if (window.TryGetPlatformHandle() is { } handle)
                    Trace($"Constrained child native handle {handle.HandleDescriptor}: {handle.Handle}");
            };
            window.Deactivated += (_, _) => Trace("Constrained child deactivated");
        }
        var content = new Border
        {
            Padding = new Thickness(12),
            Child = new ScrollViewer
            {
                Content = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Spacing = 8,
                    Children =
                    {
                        counter,
                        editor,
                        dropTarget,
#if OHOS
                        dragSource,
#endif
                        controls
                    }
                }
            }
        };
        window.Content = content;
        ObserveTopLevel(content, targets.ToArray());
        window.Opened += (_, _) => Trace("Avalonia child opened");
        window.PositionChanged += (_, e) => Trace($"Avalonia child position: {e.Point}");
        window.Resized += (_, e) => Trace($"Avalonia child size: {e.ClientSize}");
        window.Closed += (_, _) => Trace("Avalonia child closed");
        return window;
    }
}
