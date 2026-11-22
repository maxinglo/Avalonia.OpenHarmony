using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

[Flags]
public enum OpenHarmonyAccessibilityState : uint
{
    Enabled = 1,
    Focusable = 2,
    InputFocused = 4,
    Invokable = 8,
    Scrollable = 16,
    ScrollForward = 32,
    ScrollBackward = 64
}

/// <summary>An action requested by the accessibility service.</summary>
public enum OpenHarmonyAccessibilityAction
{
    Invoke = 0,
    ScrollForward = 1,
    ScrollBackward = 2
}

/// <summary>One accessible element with bounds relative to the surface, in physical pixels.</summary>
public readonly record struct OpenHarmonyAccessibilityNode(int Id, int ParentId, string Role, string Name,
    PixelRect Bounds, OpenHarmonyAccessibilityState State)
{
    /// <summary>The offset along the primary scroll axis, in physical pixels.</summary>
    public int ScrollOffset { get; init; }
}

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
    private Dictionary<int, AutomationPeer> _actionable = new();
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

    public void Invoke(int id, OpenHarmonyAccessibilityAction action)
    {
        if (_disposed)
            return;
        Refresh();
        if (!_actionable.TryGetValue(id, out var peer) || !peer.IsEnabled() || peer.IsOffscreen())
            return;

        if (action == OpenHarmonyAccessibilityAction.Invoke)
        {
            peer.GetProvider<IInvokeProvider>()?.Invoke();
        }
        else
        {
            OpenHarmonyAccessibilityState direction = action switch
            {
                OpenHarmonyAccessibilityAction.ScrollForward => OpenHarmonyAccessibilityState.ScrollForward,
                OpenHarmonyAccessibilityAction.ScrollBackward => OpenHarmonyAccessibilityState.ScrollBackward,
                _ => 0
            };
            if (direction == 0 || GetScrollPosition(peer) is not { } position ||
                (position.Directions & direction) == 0)
                return;
            var amount = action == OpenHarmonyAccessibilityAction.ScrollForward
                ? ScrollAmount.LargeIncrement : ScrollAmount.LargeDecrement;
            position.Peer.Scroll(position.Vertical ? ScrollAmount.NoAmount : amount,
                position.Vertical ? amount : ScrollAmount.NoAmount);
        }
        Invalidate(null, EventArgs.Empty);
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
            var actionable = new Dictionary<int, AutomationPeer>();
            var visited = new HashSet<AutomationPeer>(ReferenceEqualityComparer.Instance);
            var clip = new Rect(_root.ClientSize);
            Add(root, 0, 0, clip, nodes, actionable, visited);
            _actionable = actionable;
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
            _actionable.Clear();
            _reportError(error.ToString());
        }
    }

    private void Add(AutomationPeer peer, int parent, int depth, Rect clip,
        List<OpenHarmonyAccessibilityNode> nodes, Dictionary<int, AutomationPeer> actionable,
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
            var scrollOffset = 0;
            if (!isRoot)
            {
                state = peer.IsEnabled() ? OpenHarmonyAccessibilityState.Enabled : 0;
                if (peer.IsKeyboardFocusable()) state |= OpenHarmonyAccessibilityState.Focusable;
                if (peer.HasKeyboardFocus()) state |= OpenHarmonyAccessibilityState.InputFocused;
                if (peer.GetProvider<IInvokeProvider>() is not null)
                    state |= OpenHarmonyAccessibilityState.Invokable;
                if ((state & OpenHarmonyAccessibilityState.Enabled) != 0 && GetScrollPosition(peer) is { } scroll)
                {
                    state |= OpenHarmonyAccessibilityState.Scrollable | scroll.Directions;
                    scrollOffset = (int)Math.Clamp(Math.Round(scroll.Offset * _root.RenderScaling), 0, int.MaxValue);
                }
                if ((state & (OpenHarmonyAccessibilityState.Invokable | OpenHarmonyAccessibilityState.Scrollable)) != 0)
                    actionable.Add(id, peer);
            }
            nodes.Add(new(id, parent, isRoot ? "Stack" : Role(peer.GetAutomationControlType()),
                isRoot ? "" : peer.GetName() ?? "", pixels, state) { ScrollOffset = scrollOffset });
            parent = id;
        }

        foreach (var child in peer.GetChildren())
            Add(child, parent, depth + 1, clip, nodes, actionable, visited);
    }

    private static ScrollPosition? GetScrollPosition(AutomationPeer peer)
    {
        if (peer is not ScrollViewerAutomationPeer scroll ||
            scroll.Owner.Presenter is not ScrollContentPresenter presenter ||
            presenter.Child is ILogicalScrollable { IsLogicalScrollEnabled: true })
            return null;

        var vertical = presenter.CanVerticallyScroll && scroll.VerticallyScrollable;
        if (!vertical && !(presenter.CanHorizontallyScroll && scroll.HorizontallyScrollable))
            return null;

        var owner = scroll.Owner;
        var viewport = vertical ? owner.Viewport.Height : owner.Viewport.Width;
        var maximum = (vertical ? owner.Extent.Height : owner.Extent.Width) - viewport;
        var offset = vertical ? owner.Offset.Y : owner.Offset.X;
        if (!(viewport > 0) || !(maximum > 0) || !double.IsFinite(maximum) || !double.IsFinite(offset))
            return null;

        var directions = offset < maximum ? OpenHarmonyAccessibilityState.ScrollForward : 0;
        if (offset > 0)
            directions |= OpenHarmonyAccessibilityState.ScrollBackward;
        return new(scroll, vertical, Math.Clamp(offset, 0, maximum), directions);
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
            args.Property == ContentControl.ContentProperty || args.Property == TextBlock.TextProperty ||
            args.Property == ScrollViewer.OffsetProperty || args.Property == ScrollViewer.ExtentProperty ||
            args.Property == ScrollViewer.ViewportProperty)
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
        AutomationControlType.ScrollViewer => "Scroll",
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
        _actionable.Clear();
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

    private readonly record struct ScrollPosition(ScrollViewerAutomationPeer Peer, bool Vertical, double Offset,
        OpenHarmonyAccessibilityState Directions);
}
