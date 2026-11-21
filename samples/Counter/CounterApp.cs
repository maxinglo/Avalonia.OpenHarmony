using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using Avalonia.Themes.Simple;

namespace Counter;

public sealed class CounterApp : Application
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
            var link = new Button { Content = "Open Avalonia website" };
            link.Click += async (_, _) => await RunAsync(async () =>
            {
                var launcher = TopLevel.GetTopLevel(editor)?.Launcher;
                var opened = launcher is not null && await launcher.LaunchUriAsync(new Uri("https://avaloniaui.net/"));
                ReportStatus(opened ? "Website launch accepted" : "Website launch unavailable");
            }, ReportStatus);

            var content = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "Avalonia on HarmonyOS", FontSize = 27 },
                    button,
                    text,
                    editor,
                    picker,
                    new WrapPanel { Children = { copy, open, save, link } },
                    status
                }
            };
            var border = new Border
            {
                Child = new ScrollViewer { Content = content },
                Padding = new Thickness(24)
            };
            border[!Border.BackgroundProperty] = new DynamicResourceExtension("ThemeBackgroundBrush");
            border.AddHandler(InputElement.PointerWheelChangedEvent, (_, args) =>
                Trace($"Wheel: {args.Delta.X:F2} {args.Delta.Y:F2}"),
                RoutingStrategies.Tunnel, handledEventsToo: true);
            ObserveTopLevel(border);
            lifetime.MainView = border;
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void ObserveTopLevel(Control content)
    {
        TopLevel? current = null;
        void ScaleChanged(object? sender, EventArgs args) => Trace($"DPI scale: {current?.RenderScaling:F2}");
        content.AttachedToVisualTree += (_, _) =>
        {
            current = TopLevel.GetTopLevel(content);
            if (current is not null)
            {
                current.ScalingChanged += ScaleChanged;
                ScaleChanged(null, EventArgs.Empty);
            }
        };
        content.DetachedFromVisualTree += (_, _) =>
        {
            if (current is not null)
                current.ScalingChanged -= ScaleChanged;
            current = null;
        };
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

    private static async Task RunAsync(Func<Task> action, Action<string> report)
    {
        try { await action(); }
        catch (Exception error) { report($"Platform action failed: {error.GetType().Name}"); }
    }
}
