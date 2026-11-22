using Avalonia.Media;

namespace Avalonia.OpenHarmony;

/// <summary>The window avoidance areas provided by HarmonyOS.</summary>
public enum OpenHarmonyAvoidAreaKind
{
    System = 0,
    Cutout = 1,
    Keyboard = 3,
    NavigationIndicator = 4,
    FloatingNavigation = 5
}

/// <summary>One window avoidance area in physical pixels relative to the window.</summary>
public readonly record struct OpenHarmonyAvoidArea(PixelRect Left, PixelRect Top, PixelRect Right, PixelRect Bottom);

public sealed partial class OpenHarmonyHost
{
    private readonly OpenHarmonyInsetsState _windowInsets = new();
    private Func<string, string, Task<OpenHarmonySystemResult>>? _requestInsetsCommand;
    private bool _hasSurfaceOffset;

    /// <summary>Connects the insets properties to the optional ArkTS window command handler.</summary>
    public void UseInsetsCommands(Func<string, string, Task<OpenHarmonySystemResult>> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Post(() =>
        {
            _requestInsetsCommand = request;
            _topLevel?.Insets.SetRequestHandler(request, _reportError);
        });
    }

    /// <summary>Reports an avoidance area in window-local physical pixels.</summary>
    public void SetWindowAvoidArea(OpenHarmonyAvoidAreaKind kind, OpenHarmonyAvoidArea area) => Post(() =>
    {
        if (_windowInsets.SetArea(kind, area))
            RefreshWindowInsets();
    });

    /// <summary>Reports the current child surface's avoidance area in window-local physical pixels.</summary>
    public void SetChildAvoidArea(long surface, OpenHarmonyAvoidAreaKind kind, OpenHarmonyAvoidArea area) => Post(() =>
    {
        if (!_childSurfaces.TryGetValue(surface, out var child) || child.Window.IsCloseRequested)
            return;
        if (kind == OpenHarmonyAvoidAreaKind.Keyboard && !IsChildInputMethodActive(child))
            return;
        if (child.Insets.SetArea(kind, area))
            RefreshChildInsets(child);
    });

    /// <summary>Reports the window's current immersive layout and status bar background.</summary>
    public void SetWindowInsetProperties(bool edgeToEdge, Color? statusBarColor) => Post(() =>
    {
        _windowInsets.EdgeToEdge = edgeToEdge;
        _windowInsets.StatusBarColor = statusBarColor;
        RefreshWindowInsets();
    });

    /// <summary>Clears the window snapshot when its stage is destroyed.</summary>
    public void ClearWindowInsets() => Post(() =>
    {
        _windowInsets.Clear();
        RefreshWindowInsets();
    });

    private void RefreshWindowInsets()
    {
        if (_topLevel is { } topLevel)
        {
            var surface = _hasSurfaceOffset ? new PixelRect(_surfaceOffset, topLevel.PixelSize) : default;
            _windowInsets.Apply(topLevel.Insets, topLevel.InputPane, surface, topLevel.RenderScaling);
        }
    }

    private static void RefreshChildInsets(WindowRoute route)
    {
        var surface = route.Window.Surface;
        var bounds = route.HasOffset ? new PixelRect(route.Offset, surface.PixelSize) : default;
        route.Insets.Apply(surface.Insets, surface.InputPane, bounds, surface.RenderScaling);
    }

    private static void ResetChildInsets(WindowRoute route)
    {
        route.Offset = default;
        route.HasOffset = false;
        route.Insets.Clear();
    }
}
