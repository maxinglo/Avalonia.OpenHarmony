using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

[Flags]
public enum OpenHarmonyAccessibilityState : uint
{
    Enabled = 1,
    Focusable = 2,
    InputFocused = 4,
    Invokable = 8
}

/// <summary>One accessible element with bounds relative to the surface, in physical pixels.</summary>
public readonly record struct OpenHarmonyAccessibilityNode(int Id, int ParentId, string Role, string Name,
    PixelRect Bounds, OpenHarmonyAccessibilityState State);

/// <summary>The current semantics of an enabled OpenHarmony surface.</summary>
public readonly record struct OpenHarmonyAccessibilitySnapshot(long Surface, long Registration,
    ReadOnlyCollection<OpenHarmonyAccessibilityNode> Nodes);

internal sealed class OpenHarmonyAccessibilityTree : IDisposable
{
    private const int MaximumNodes = 4_096;
    private const int MaximumDepth = 128;
    private readonly TopLevel _root;
    private readonly long _surface;
    private readonly Action<OpenHarmonyAccessibilitySnapshot> _publish;
    private readonly Action<string> _reportError;
    private readonly ConditionalWeakTable<AutomationPeer, NodeId> _identities = new();
    private Dictionary<int, AutomationPeer> _invokable = new();
    private HashSet<AutomationPeer> _observed = new(ReferenceEqualityComparer.Instance);
    private OpenHarmonyAccessibilityNode[] _latest = [];
    private int _nextId = 1;
    private bool _queued;
    private bool _disposed;

    public OpenHarmonyAccessibilityTree(TopLevel root, long surface, long registration,
        Action<OpenHarmonyAccessibilitySnapshot> publish, Action<string> reportError)
    {
        _root = root;
        _surface = surface;
        Registration = registration;
        _publish = publish;
        _reportError = reportError;
        _root.LayoutUpdated += Invalidate;
        Invalidate(null, EventArgs.Empty);
    }

    public long Registration { get; }

    public void Invoke(int id)
    {
        if (_disposed)
            return;
        Refresh();
        if (_invokable.TryGetValue(id, out var peer) && peer.IsEnabled() && !peer.IsOffscreen())
        {
            peer.GetProvider<IInvokeProvider>()?.Invoke();
            Invalidate(null, EventArgs.Empty);
        }
    }

    private void Invalidate(object? sender, EventArgs args)
    {
        if (_disposed || _queued)
            return;
        _queued = true;
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        _queued = false;
        if (_disposed)
            return;

        try
        {
            var root = ControlAutomationPeer.CreatePeerForElement(_root);
            if (root is null)
                return;

            var nodes = new List<OpenHarmonyAccessibilityNode>();
            var invokable = new Dictionary<int, AutomationPeer>();
            var visited = new HashSet<AutomationPeer>(ReferenceEqualityComparer.Instance);
            var clip = new Rect(_root.ClientSize);
            Add(root, 0, 0, clip, nodes, invokable, visited);
            _invokable = invokable;
            foreach (var peer in _observed)
                if (!visited.Contains(peer)) Observe(peer, false);
            foreach (var peer in visited)
                if (!_observed.Contains(peer)) Observe(peer, true);
            _observed = visited;

            if (!nodes.SequenceEqual(_latest))
            {
                var snapshot = nodes.ToArray();
                _publish(new(_surface, Registration, Array.AsReadOnly(snapshot)));
                _latest = snapshot;
            }
        }
        catch (Exception error)
        {
            _invokable.Clear();
            _reportError(error.ToString());
        }
    }

    private void Add(AutomationPeer peer, int parent, int depth, Rect clip,
        List<OpenHarmonyAccessibilityNode> nodes, Dictionary<int, AutomationPeer> invokable,
        HashSet<AutomationPeer> visited)
    {
        if (visited.Count >= MaximumNodes || depth >= MaximumDepth || !visited.Add(peer))
            return;
        var isRoot = depth == 0;
        var bounds = isRoot ? clip : peer.GetBoundingRectangle().Intersect(clip);
        var pixels = PixelRect.FromRect(bounds, _root.RenderScaling);
        if (pixels.Width > 0 && pixels.Height > 0 && (isRoot || (!peer.IsOffscreen() &&
                     (peer.IsControlElement() || peer.IsContentElement()))))
        {
            int id = isRoot ? 1 : _identities.GetValue(peer, _ => new NodeId(checked(++_nextId))).Value;
            var state = OpenHarmonyAccessibilityState.Enabled;
            if (!isRoot)
            {
                state = peer.IsEnabled() ? OpenHarmonyAccessibilityState.Enabled : 0;
                if (peer.IsKeyboardFocusable()) state |= OpenHarmonyAccessibilityState.Focusable;
                if (peer.HasKeyboardFocus()) state |= OpenHarmonyAccessibilityState.InputFocused;
                if (peer.GetProvider<IInvokeProvider>() is not null)
                {
                    state |= OpenHarmonyAccessibilityState.Invokable;
                    invokable.Add(id, peer);
                }
            }
            nodes.Add(new(id, parent, isRoot ? "Stack" : Role(peer.GetAutomationControlType()),
                isRoot ? "" : peer.GetName() ?? "", pixels, state));
            parent = id;
        }

        foreach (var child in peer.GetChildren())
            Add(child, parent, depth + 1, clip, nodes, invokable, visited);
    }

    private void Observe(AutomationPeer peer, bool observe)
    {
        if (observe)
        {
            peer.ChildrenChanged += Invalidate;
            peer.PropertyChanged += Invalidate;
            if (peer is ControlAutomationPeer control) control.Owner.PropertyChanged += OwnerChanged;
        }
        else
        {
            peer.ChildrenChanged -= Invalidate;
            peer.PropertyChanged -= Invalidate;
            if (peer is ControlAutomationPeer control) control.Owner.PropertyChanged -= OwnerChanged;
        }
    }

    private void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == AutomationProperties.NameProperty || args.Property == InputElement.IsFocusedProperty ||
            args.Property == InputElement.IsEffectivelyEnabledProperty || args.Property == InputElement.FocusableProperty ||
            args.Property == Visual.IsVisibleProperty || args.Property == Visual.BoundsProperty ||
            args.Property == ContentControl.ContentProperty || args.Property == TextBlock.TextProperty)
            Invalidate(sender, args);
    }

    private static string Role(AutomationControlType type) => type switch
    {
        AutomationControlType.Button => "Button",
        AutomationControlType.CheckBox => "Checkbox",
        AutomationControlType.ComboBox => "Select",
        AutomationControlType.Edit => "TextInput",
        AutomationControlType.Hyperlink => "Hyperlink",
        AutomationControlType.Image => "Image",
        AutomationControlType.List => "List",
        AutomationControlType.ListItem => "ListItem",
        AutomationControlType.ProgressBar => "Progress",
        AutomationControlType.RadioButton => "Radio",
        AutomationControlType.ScrollBar => "ScrollBar",
        AutomationControlType.Slider => "Slider",
        AutomationControlType.Tab => "Tabs",
        AutomationControlType.TabItem => "TabContent",
        AutomationControlType.Text => "Text",
        _ => "Stack"
    };

    public void Dispose()
    {
        _disposed = true;
        _root.LayoutUpdated -= Invalidate;
        foreach (var peer in _observed)
            Observe(peer, false);
        _observed.Clear();
        _invokable.Clear();
        try
        {
            _publish(new(_surface, Registration, Array.AsReadOnly(Array.Empty<OpenHarmonyAccessibilityNode>())));
        }
        catch (Exception error)
        {
            _reportError(error.ToString());
        }
    }

    private sealed record NodeId(int Value);
}
