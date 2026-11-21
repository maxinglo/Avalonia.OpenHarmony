using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Simple;

namespace Counter;

public sealed class CounterApp : Application
{
    internal static Action<string> Trace { get; set; } = Console.WriteLine;

    public override void Initialize()
    {
        Styles.Add(new SimpleTheme());
        RequestedThemeVariant = ThemeVariant.Light;
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

            lifetime.MainView = new Border
            {
                Background = Brushes.White,
                Padding = new Thickness(24),
                Child = new StackPanel
                {
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Avalonia on HarmonyOS",
                            FontSize = 27,
                            Foreground = Brushes.Black
                        },
                        button,
                        text
                    }
                }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
