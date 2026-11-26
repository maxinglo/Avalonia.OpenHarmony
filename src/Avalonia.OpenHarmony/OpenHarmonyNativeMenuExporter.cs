using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

// Export the tray menu through the system status bar's two-level menu model.
internal sealed class OpenHarmonyNativeMenuExporter(Action<string> update) : INativeMenuExporter, IDisposable
{
    private const int MaximumItems = 20;
    private static long s_nextCode;
    private readonly ConditionalWeakTable<NativeMenuItem, string> _codes = new();
    private readonly HashSet<NativeMenu> _observedMenus = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<NativeMenuItem> _observedItems = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, MenuAction> _actions = new(StringComparer.Ordinal);
    private NativeMenu? _menu;
    private string _snapshot = "[]";
    private bool _updateQueued;
    private bool _disposed;

    public void SetNativeMenu(NativeMenu? menu)
    {
        if (_disposed || ReferenceEquals(_menu, menu))
            return;

        _menu = menu;
        Publish();
    }

    public bool Invoke(string menuCode)
    {
        if (!_actions.TryGetValue(menuCode, out var action) || _menu is null ||
            action.Item.Parent != action.Menu || action.Item.Menu is not null || !CanShow(action.Item))
            return false;

        if (action.Parent is { } parent &&
            (parent.Parent != _menu || parent.Menu != action.Menu || !CanShow(parent)))
            return false;

        ((INativeMenuItemExporterEventsImplBridge)action.Item).RaiseClicked();
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        _menu = null;
        _actions.Clear();
        Unsubscribe();
    }

    private void Publish()
    {
        _updateQueued = false;
        if (_disposed)
            return;

        Unsubscribe();
        _actions.Clear();
        var groups = new List<List<OpenHarmonyNativeMenuEntry>>();
        if (_menu is { } root)
        {
            Observe(root);
            var group = new List<OpenHarmonyNativeMenuEntry>();
            var count = 0;
            foreach (var entry in root.Items)
            {
                if (count == MaximumItems)
                    break;

                if (entry is NativeMenuItemSeparator { IsVisible: true })
                {
                    if (group.Count != 0)
                    {
                        groups.Add(group);
                        group = [];
                    }
                    continue;
                }

                if (entry is not NativeMenuItem item || !CanShow(item))
                    continue;

                if (item.Menu is { } submenu)
                {
                    var children = BuildSubmenu(submenu, item);
                    if (children.Count == 0)
                        continue;

                    group.Add(new(item.Header!, Children: children));
                }
                else
                    group.Add(AddAction(item, root));
                count++;
            }

            if (group.Count != 0)
                groups.Add(group);
        }

        var snapshot = JsonSerializer.Serialize(groups, OpenHarmonyNativeMenuJson.Default.MenuGroups);
        if (!string.Equals(snapshot, _snapshot, StringComparison.Ordinal))
        {
            _snapshot = snapshot;
            update(snapshot);
        }
    }

    private List<OpenHarmonyNativeMenuEntry> BuildSubmenu(NativeMenu submenu, NativeMenuItem parent)
    {
        Observe(submenu);
        var children = new List<OpenHarmonyNativeMenuEntry>();
        foreach (var entry in submenu.Items)
        {
            if (entry is NativeMenuItem { Menu: null } item && item is not NativeMenuItemSeparator && CanShow(item))
                children.Add(AddAction(item, submenu, parent));
            if (children.Count == MaximumItems)
                break;
        }
        return children;
    }

    private OpenHarmonyNativeMenuEntry AddAction(NativeMenuItem item, NativeMenu menu, NativeMenuItem? parent = null)
    {
        var code = _codes.GetValue(item, static _ => Interlocked.Increment(ref s_nextCode)
            .ToString(CultureInfo.InvariantCulture));
        _actions[code] = new(item, menu, parent);
        return new(item.Header!, code, item.ToggleType != MenuItemToggleType.None && item.IsChecked);
    }

    private static bool CanShow(NativeMenuItem item) => item.IsVisible && item.IsEnabled &&
        !string.IsNullOrWhiteSpace(item.Header);

    private void Observe(NativeMenu menu)
    {
        if (!_observedMenus.Add(menu))
            return;

        ((INotifyCollectionChanged)menu.Items).CollectionChanged += OnMenuChanged;
        foreach (var entry in menu.Items)
        {
            if (entry is NativeMenuItem item && _observedItems.Add(item))
                item.PropertyChanged += OnItemChanged;
        }
    }

    private void Unsubscribe()
    {
        foreach (var menu in _observedMenus)
            ((INotifyCollectionChanged)menu.Items).CollectionChanged -= OnMenuChanged;
        foreach (var item in _observedItems)
            item.PropertyChanged -= OnItemChanged;
        _observedMenus.Clear();
        _observedItems.Clear();
    }

    private void OnMenuChanged(object? sender, NotifyCollectionChangedEventArgs args) => QueueUpdate();

    private void OnItemChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == NativeMenuItem.HeaderProperty || args.Property == NativeMenuItem.IsEnabledProperty ||
            args.Property == NativeMenuItem.IsVisibleProperty || args.Property == NativeMenuItem.MenuProperty ||
            args.Property == NativeMenuItem.ToggleTypeProperty || args.Property == NativeMenuItem.IsCheckedProperty)
            QueueUpdate();
    }

    private void QueueUpdate()
    {
        if (_updateQueued || _disposed)
            return;

        _updateQueued = true;
        Dispatcher.UIThread.Post(Publish, DispatcherPriority.Background);
    }

    private sealed record MenuAction(NativeMenuItem Item, NativeMenu Menu, NativeMenuItem? Parent);
}

internal sealed record OpenHarmonyNativeMenuEntry(string Title, string? Code = null, bool Selected = false,
    List<OpenHarmonyNativeMenuEntry>? Children = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<List<OpenHarmonyNativeMenuEntry>>), TypeInfoPropertyName = "MenuGroups")]
internal sealed partial class OpenHarmonyNativeMenuJson : JsonSerializerContext;
