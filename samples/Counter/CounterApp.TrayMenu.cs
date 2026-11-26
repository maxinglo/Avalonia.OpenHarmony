#if OHOS
using Avalonia.Controls;

namespace Counter;

public sealed partial class CounterApp
{
    private static NativeMenu CreateTrayMenu(Action<string> report)
    {
        var count = 0;
        var increment = new NativeMenuItem("Add one from tray");
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
}
#endif
