using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using Avalonia.Themes.Simple;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Counter;

public sealed partial class CounterApp : Application
{
    private const string SampleText = "Avalonia — 中文 🎧";
    private static readonly FilePickerFileType TextFiles = new("Text documents")
    {
        Patterns = ["*.txt"],
        MimeTypes = ["text/plain"]
    };

    internal static Action<string> Trace { get; set; } = Console.WriteLine;
    internal static ComboBox? PopupSelector { get; private set; }

    public override void Initialize()
    {
        Styles.Add(new SimpleTheme());
        ActualThemeVariantChanged += (_, _) => Trace($"Theme: {ActualThemeVariant}");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (this.TryGetFeature<IActivatableLifetime>() is { } activation)
        {
            activation.Activated += (_, args) => Trace($"Application activated: {args.Kind}");
            activation.Deactivated += (_, args) => Trace($"Application deactivated: {args.Kind}");
        }

        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
        {
            var text = new TextBlock { Text = "Count: 0", FontSize = 22 };
            var button = new Button { Content = "Add one", Padding = new Thickness(20, 12) };
            var count = 0;
            button.Click += (_, _) =>
            {
                text.Text = $"Count: {++count}";
                Trace(text.Text);
            };

            var editor = new TextBox
            {
                PlaceholderText = "Type here",
                MinWidth = 240,
                MaxWidth = 400,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var previousLength = 0;
            editor.TextChanged += (_, _) =>
            {
                var length = editor.Text?.Length ?? 0;
                Trace(length < previousLength
                    ? $"Text shortened: {previousLength} -> {length}"
                    : $"Text length: {length}");
                previousLength = length;
                if (editor.Text == SampleText)
                    Trace("Sample text pasted");
            };
            var picker = new ComboBox
            {
                ItemsSource = new[] { "First option", "Second option", "Third option" },
                SelectedIndex = 0,
                MinWidth = 240
            };
            picker.DropDownOpened += (_, _) => Trace("ComboBox opened");
            picker.SelectionChanged += (_, _) => Trace($"ComboBox selected: {picker.SelectedIndex}");
            PopupSelector = picker;

            var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            void ReportStatus(string message)
            {
                status.Text = message;
                Trace(message);
            }

            var copy = new Button { Content = "Copy sample text" };
            copy.Click += async (_, _) =>
            {
                var clipboard = TopLevel.GetTopLevel(editor)?.Clipboard;
                if (clipboard is null)
                {
                    ReportStatus("Clipboard unavailable");
                    return;
                }
                await RunAsync(async () =>
                {
                    await clipboard.SetTextAsync(SampleText);
                    editor.Focus();
                    ReportStatus("Sample text copied");
                }, ReportStatus);
            };

            var open = new Button { Content = "Open text file" };
            open.Click += async (_, _) => await RunAsync(() => OpenFileAsync(editor, ReportStatus), ReportStatus);
            var save = new Button { Content = "Save sample text" };
            save.Click += async (_, _) => await RunAsync(() => SaveFileAsync(editor, ReportStatus), ReportStatus);
            var folder = new Button { Content = "Choose folder" };
            folder.Click += async (_, _) => await RunAsync(() => OpenFolderAsync(editor, ReportStatus), ReportStatus);
            var link = new Button { Content = "Open Avalonia website" };
            link.Click += async (_, _) => await RunAsync(async () =>
            {
                var launcher = TopLevel.GetTopLevel(editor)?.Launcher;
                var opened = launcher is not null && await launcher.LaunchUriAsync(new Uri("https://avaloniaui.net/"));
                ReportStatus(opened ? "Website launch accepted" : "Website launch unavailable");
            }, ReportStatus);

            var immersive = new Button { Content = "Toggle immersive layout" };
            immersive.Click += (_, _) =>
            {
                if (TopLevel.GetTopLevel(editor)?.InsetsManager is not { } insets)
                    return;
                insets.DisplayEdgeToEdgePreference = !insets.DisplayEdgeToEdgePreference;
                ReportStatus($"Immersive layout requested: {insets.DisplayEdgeToEdgePreference}");
            };

            var dropTarget = CounterDropTarget.Create(editor, ReportStatus);
#if OHOS
            var dragSource = CreateTextDragSource(ReportStatus);
            var (bitmapCursor, hiddenCursor) = CreateCursorTargets();
#endif
            var content = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "Avalonia on HarmonyOS", FontSize = 27 },
                    button,
                    text,
                    editor,
                    dropTarget,
#if OHOS
                    dragSource,
#endif
                    picker,
#if OHOS
                    new WrapPanel { Children = { bitmapCursor, hiddenCursor } },
#endif
                    new WrapPanel { Children = { copy, open, save, folder, link, immersive } },
                    status
                }
            };
#if OHOS_COUNTER_WINDOWS
            var (windowButtons, windowTargets) = ChildWindowButtons();
            content.Children.Add(windowButtons);
#endif
            var border = new Border
            {
                Child = new ScrollViewer { Content = content },
                Padding = new Thickness(24)
            };
            border[!Border.BackgroundProperty] = new DynamicResourceExtension("ThemeBackgroundBrush");
            border.AddHandler(InputElement.PointerWheelChangedEvent, (_, args) =>
                Trace($"Wheel: {args.Delta.X:F2} {args.Delta.Y:F2}"),
                RoutingStrategies.Tunnel, handledEventsToo: true);
            ObserveTopLevel(border, [("Count button", button), ("Editor", editor), ("Drop target", dropTarget), ("ComboBox", picker),
                ("Copy", copy), ("Open", open), ("Save", save), ("Folder", folder), ("Website", link),
#if OHOS
                ("Drag source", dragSource), ("Bitmap cursor", bitmapCursor), ("Hidden cursor", hiddenCursor),
#endif
                ("Immersive", immersive),
#if OHOS_COUNTER_WINDOWS
                .. windowTargets
#endif
            ]);
            var surface = new Border { Child = border };
            TopLevel.SetAutoSafeAreaPadding(surface, true);
            lifetime.MainView = surface;
        }
        base.OnFrameworkInitializationCompleted();
    }

#if OHOS
    private static Control CreateTextDragSource(Action<string> report)
    {
        var label = new TextBlock
        {
            Text = "Drag Avalonia sample text",
            Margin = new Thickness(12),
            Foreground = Avalonia.Media.Brushes.White
        };
        var source = new Border
        {
            Background = Avalonia.Media.Brushes.RoyalBlue,
            Child = label,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(6)
        };
        source.PointerPressed += async (_, args) =>
        {
            if (args.Pointer.Type != PointerType.Touch)
                return;
            args.Handled = true;
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(SampleText));
            var result = await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Copy);
            report(result == DragDropEffects.Copy ? "Sample text dragged: Copy" : "Sample text drag canceled");
        };
        return source;
    }
#endif

    private static void ObserveTopLevel(Control content, params (string Name, Control Element)[] targets)
    {
        TopLevel? current = null;
        string? lastLayout = null;
        void ScaleChanged(object? sender, EventArgs args) => Trace($"DPI scale: {current?.RenderScaling:F2}");
        void PositionChanged(object? sender, PixelPointEventArgs args) => QueuePositions();
        void BackRequested(object? sender, RoutedEventArgs args) => Trace("System Back received");
        void ScreensChanged(object? sender, EventArgs args)
        {
            if (current?.Screens is not { ScreenCount: > 0 } screens)
                return;
            var active = screens.ScreenFromTopLevel(current);
            Trace($"Displays: {screens.ScreenCount}. Active bounds: {active?.Bounds}. " +
                $"Work area: {active?.WorkingArea}. Scale: {active?.Scaling:F2}");
        }
        void SafeAreaChanged(object? sender, SafeAreaChangedArgs args) => ReportSafeArea();
        void InputPaneChanged(object? sender, InputPaneStateEventArgs args) => ReportInputPane();
        void ReportSafeArea()
        {
            if (current?.InsetsManager is { } insets)
                Trace($"Safe area: {insets.SafeAreaPadding}. Edge to edge: {insets.DisplaysEdgeToEdge}");
        }
        void ReportInputPane()
        {
            if (current?.InputPane is { } pane)
                Trace($"Input pane: {pane.State}. Occlusion: {pane.OccludedRect}");
        }
        content.AttachedToVisualTree += (_, _) =>
        {
            current = TopLevel.GetTopLevel(content);
            if (current is not null)
            {
                current.ScalingChanged += ScaleChanged;
                current.BackRequested += BackRequested;
                if (current is Window window)
                    window.PositionChanged += PositionChanged;
                if (current.Screens is { } screens)
                    screens.Changed += ScreensChanged;
                if (current.InsetsManager is { } insets)
                    insets.SafeAreaChanged += SafeAreaChanged;
                if (current.InputPane is { } pane)
                    pane.StateChanged += InputPaneChanged;
                ScaleChanged(null, EventArgs.Empty);
                ScreensChanged(null, EventArgs.Empty);
                ReportSafeArea();
                ReportInputPane();
            }
        };
        content.DetachedFromVisualTree += (_, _) =>
        {
            if (current is not null)
            {
                current.ScalingChanged -= ScaleChanged;
                current.BackRequested -= BackRequested;
                if (current is Window window)
                    window.PositionChanged -= PositionChanged;
                if (current.Screens is { } screens)
                    screens.Changed -= ScreensChanged;
                if (current.InsetsManager is { } insets)
                    insets.SafeAreaChanged -= SafeAreaChanged;
                if (current.InputPane is { } pane)
                    pane.StateChanged -= InputPaneChanged;
            }
            current = null;
            lastLayout = null;
        };
        void ReportPositions()
        {
            if (current is null || targets.Any(target => target.Element.Bounds.Width <= 0))
                return;
            var positions = string.Join(" | ", targets.Select(target =>
            {
                var size = target.Element.Bounds.Size;
                var center = target.Element.PointToScreen(new Point(size.Width / 2, size.Height / 2));
                return $"{target.Name} {center.X},{center.Y}";
            }));
            if (positions != lastLayout)
            {
                lastLayout = positions;
                Trace($"Control positions: {positions}");
            }
        }
        void QueuePositions() => Dispatcher.UIThread.Post(ReportPositions, DispatcherPriority.Background);
        content.LayoutUpdated += (_, _) => QueuePositions();
    }

    private static async Task OpenFileAsync(Control control, Action<string> report)
    {
        var storage = TopLevel.GetTopLevel(control)?.StorageProvider;
        if (storage?.CanOpen != true)
        {
            report("File picker unavailable");
            return;
        }
        var selection = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a text file",
            FileTypeFilter = [TextFiles]
        });
        using var file = selection.FirstOrDefault();
        if (file is null)
        {
            report("File picker closed");
            return;
        }
        await using var stream = await file.OpenReadAsync();
        var buffer = new byte[1024];
        var read = await stream.ReadAsync(buffer);
        report($"Read {read} bytes");
    }

    private static async Task SaveFileAsync(Control control, Action<string> report)
    {
        var storage = TopLevel.GetTopLevel(control)?.StorageProvider;
        if (storage?.CanSave != true)
        {
            report("Save picker unavailable");
            return;
        }
        using var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save sample text",
            SuggestedFileName = "avalonia-harmonyos.txt",
            FileTypeChoices = [TextFiles]
        });
        if (file is null)
        {
            report("Save picker closed");
            return;
        }
        await using var stream = await file.OpenWriteAsync();
        var bytes = System.Text.Encoding.UTF8.GetBytes(SampleText);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
        report($"Wrote {bytes.Length} bytes");
    }

    private static async Task OpenFolderAsync(Control control, Action<string> report)
    {
        var storage = TopLevel.GetTopLevel(control)?.StorageProvider;
        if (storage?.CanPickFolder != true)
        {
            report("Folder picker unavailable");
            return;
        }
        var selection = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a folder" });
        using var folder = selection.FirstOrDefault();
        if (folder is null)
        {
            report("Folder picker closed");
            return;
        }
        var count = 0;
        await foreach (var item in folder.GetItemsAsync())
        {
            using (item)
                count++;
        }
        report($"Read {count} directory entries");
    }

    private static async Task RunAsync(Func<Task> action, Action<string> report)
    {
        try { await action(); }
        catch (Exception error) { report($"Platform action failed: {error.GetType().Name}"); }
    }
}
