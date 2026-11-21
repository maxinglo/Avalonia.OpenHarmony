using System.Globalization;
using Avalonia.Controls.Platform;
using Avalonia.Media;

namespace Avalonia.OpenHarmony;

internal sealed class OpenHarmonyInsetsManager : InsetsManagerBase, IDisposable
{
    private Func<string, string, Task<OpenHarmonySystemResult>>? _request;
    private Action<string>? _reportError;
    private Task _pending = Task.CompletedTask;
    private bool? _edgeToEdgePreference;
    private bool? _systemBarsVisible;
    private bool _edgeToEdge;
    private Color? _statusBarColor;
    private volatile bool _disposed;

    public override bool DisplayEdgeToEdgePreference
    {
        get => _edgeToEdgePreference ?? _edgeToEdge;
        set
        {
            _edgeToEdgePreference = value;
            Enqueue("window.edgeToEdge", value ? "true" : "false");
        }
    }

    public override bool DisplaysEdgeToEdge => _edgeToEdge;
    public override bool? IsSystemBarVisible
    {
        get => _systemBarsVisible;
        set => Enqueue("window.systemBars", value switch { true => "true", false => "false", _ => "default" });
    }

    public override Color? SystemBarColor
    {
        get => _statusBarColor;
        set => Enqueue("window.statusBarColor", value is { } color
            ? color.ToUInt32().ToString("X8", CultureInfo.InvariantCulture)
            : "default");
    }

    public void SetRequestHandler(Func<string, string, Task<OpenHarmonySystemResult>> request, Action<string> reportError)
    {
        _request = request;
        _reportError = reportError;
    }

    public void Apply(Thickness safeArea, bool? systemBarsVisible, bool edgeToEdge, Color? statusBarColor)
    {
        _systemBarsVisible = systemBarsVisible;
        _edgeToEdge = edgeToEdge;
        _statusBarColor = statusBarColor;
        if (SafeAreaPadding != safeArea)
        {
            SafeAreaPadding = safeArea;
            OnSafeAreaChanged(new SafeAreaChangedArgs(safeArea));
        }
    }

    private void Enqueue(string operation, string argument)
    {
        if (!_disposed && _request is { } request)
            _pending = ApplyAfterAsync(_pending, request, operation, argument);
    }

    private async Task ApplyAfterAsync(Task previous, Func<string, string, Task<OpenHarmonySystemResult>> request,
        string operation, string argument)
    {
        await previous.ConfigureAwait(false);
        if (_disposed)
            return;
        try
        {
            var result = await request(operation, argument).ConfigureAwait(false);
            result.File?.Dispose();
        }
        catch (Exception error)
        {
            _reportError?.Invoke(error.ToString());
        }
    }

    public void Dispose() => _disposed = true;
}

internal sealed class OpenHarmonyInputPane : InputPaneBase
{
    public void Apply(bool isOpen, Rect occluded)
    {
        var state = isOpen ? InputPaneState.Open : InputPaneState.Closed;
        if (State == state && OccludedRect == occluded)
            return;
        var previous = OccludedRect;
        State = state;
        OccludedRect = occluded;
        OnStateChanged(new InputPaneStateEventArgs(state, previous, occluded));
    }
}

internal sealed class OpenHarmonyInsetsState
{
    private readonly OpenHarmonyAvoidArea?[] _areas = new OpenHarmonyAvoidArea?[6];

    public bool EdgeToEdge { get; set; }
    public Color? StatusBarColor { get; set; }

    public bool SetArea(OpenHarmonyAvoidAreaKind kind, OpenHarmonyAvoidArea area)
    {
        var index = (int)kind;
        if (index < 0 || index >= _areas.Length || index == 2 || _areas[index] == area)
            return false;
        _areas[index] = area;
        return true;
    }

    public void Clear()
    {
        Array.Clear(_areas);
        EdgeToEdge = false;
        StatusBarColor = null;
    }

    public void Apply(OpenHarmonyInsetsManager manager, OpenHarmonyInputPane inputPane, PixelRect bounds, double scale)
    {
        var surface = PhysicalRect(bounds);
        var left = 0d;
        var top = 0d;
        var right = 0d;
        var bottom = 0d;
        for (var index = 0; index < _areas.Length; index++)
        {
            if (index == (int)OpenHarmonyAvoidAreaKind.Keyboard || _areas[index] is not { } area)
                continue;
            // Thickness can only express an exclusion meeting a surface edge.
            var leftRect = PhysicalRect(area.Left);
            if (leftRect.X <= surface.X)
                left = Math.Max(left, surface.Intersect(leftRect).Width);
            var topRect = PhysicalRect(area.Top);
            if (topRect.Y <= surface.Y)
                top = Math.Max(top, surface.Intersect(topRect).Height);
            var rightRect = PhysicalRect(area.Right);
            if (rightRect.Right >= surface.Right)
                right = Math.Max(right, surface.Intersect(rightRect).Width);
            var bottomRect = PhysicalRect(area.Bottom);
            if (bottomRect.Bottom >= surface.Bottom)
                bottom = Math.Max(bottom, surface.Intersect(bottomRect).Height);
        }

        var system = _areas[(int)OpenHarmonyAvoidAreaKind.System];
        var navigation = _areas[(int)OpenHarmonyAvoidAreaKind.NavigationIndicator];
        // A window can sit outside the system bars, so empty rectangles are not proof that the bars are hidden.
        bool? visible = HasArea(system) || HasArea(navigation) ? true : null;
        var padding = new Thickness(left / scale, top / scale, right / scale, bottom / scale);
        manager.Apply(padding, visible, EdgeToEdge, StatusBarColor);

        var keyboard = _areas[(int)OpenHarmonyAvoidAreaKind.Keyboard];
        var open = HasArea(keyboard);
        var occluded = open ? KeyboardOcclusion(surface, keyboard.GetValueOrDefault(), scale) : default;
        inputPane.Apply(open, occluded);
    }

    private static Rect KeyboardOcclusion(Rect surface, OpenHarmonyAvoidArea pane, double scale)
    {
        var overlap = default(Rect);
        overlap = Extend(overlap, surface.Intersect(PhysicalRect(pane.Left)));
        overlap = Extend(overlap, surface.Intersect(PhysicalRect(pane.Top)));
        overlap = Extend(overlap, surface.Intersect(PhysicalRect(pane.Right)));
        overlap = Extend(overlap, surface.Intersect(PhysicalRect(pane.Bottom)));
        return overlap.Width > 0 && overlap.Height > 0
            ? new Rect((overlap.X - surface.X) / scale, (overlap.Y - surface.Y) / scale,
                overlap.Width / scale, overlap.Height / scale)
            : default;
    }

    private static Rect Extend(Rect bounds, Rect piece) => piece.Width <= 0 || piece.Height <= 0
        ? bounds
        : bounds.Width <= 0 || bounds.Height <= 0 ? piece : bounds.Union(piece);
    private static bool HasArea(OpenHarmonyAvoidArea? area) => area is { } value
        && (HasArea(value.Left) || HasArea(value.Top) || HasArea(value.Right) || HasArea(value.Bottom));
    private static bool HasArea(PixelRect rect) => rect.Width > 0 && rect.Height > 0;
    private static Rect PhysicalRect(PixelRect rect) =>
        HasArea(rect) ? new Rect(rect.X, rect.Y, rect.Width, rect.Height) : default;
}
