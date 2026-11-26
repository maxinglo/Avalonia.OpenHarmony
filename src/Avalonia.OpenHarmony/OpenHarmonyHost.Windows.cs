using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

/// <summary>Opts into an Avalonia Window for the HarmonyOS stage and native child windows with that owner.</summary>
public sealed class OpenHarmonyWindowOptions
{
    public Func<string, string, Task<OpenHarmonySystemResult>> Request { get; init; }
        = OpenHarmonySystemRequests.RequestAsync;
}

/// <summary>Native lifecycle notifications for a window created under the application stage.</summary>
public enum OpenHarmonyChildWindowEvent
{
    Shown,
    Hidden,
    Focused,
    Unfocused,
    Destroyed,
    StageCreated,
    ModalityReleased,
    StageDesktopAvailable,
    StageDesktopUnavailable,
    StageNormal,
    StageMinimized,
    StageMaximized,
    StageFullScreen
}

public sealed partial class OpenHarmonyHost
{
    private readonly Dictionary<int, WindowRoute> _children = new();
    private readonly Dictionary<long, WindowRoute> _childSurfaces = new();
    private int _nextChildId;
    private WindowRoute? _stageRoute;
    private Window? _stageRoot;
    private bool _stageCanOpen = true;
    private bool _stageDesktop;
    private WindowState _stageWindowState;
    private PixelSize? _stageFrameSize;
    private long _closedStageToken;

    private WindowRoute CreateWindowRoute(int id, bool isStage, OpenHarmonyWindowOptions options)
    {
        var route = new WindowRoute();
        var surface = new SurfaceTopLevel(id, AvaloniaLocator.Current.GetRequiredService<Compositor>(),
            (_, pixels, width, height, rowBytes) =>
            {
                var token = Volatile.Read(ref route.Token);
                if (token != 0 && !route.Window.IsCloseRequested)
                    _present(token, pixels, width, height, rowBytes);
            }, isStage ? _getInputWindowId : () => route.PlatformId, _reportError,
            _screens, _launcher, _storageProvider, null);
        surface.SetNativeControlSurface(0);
        surface.SetInputMethodActive(isStage && IsInputMethodWindowActive);
        route.Window = new OpenHarmonyChildWindow(id, isStage, surface,
            request => SendWindowRequest(options, request), OnManagedWindowClosed, _reportError);
        return route;
    }

    private OpenHarmonyChildWindow CreateOwnedWindow()
    {
        var options = AvaloniaLocator.Current.GetService<OpenHarmonyWindowOptions>();
        if (options is null || _stageRoute is null)
            throw new PlatformNotSupportedException("OpenHarmony child windows require an active windowed stage.");
        var id = checked(++_nextChildId);
        var route = CreateWindowRoute(id, false, options);
        _children.Add(id, route);
        return route.Window;
    }

    private void UpdateStageSurface(OpenHarmonyWindowOptions options, long token, int width, int height, double scaling)
    {
        if (!_stageCanOpen)
        {
            _closedStageToken = Math.Max(_closedStageToken, token);
            return;
        }
        if (_stageRoute is null)
        {
            if (token <= _closedStageToken)
                return;
            var route = CreateWindowRoute(0, true, options);
            _stageRoute = route;
            route.Window.ReportStageDesktop(_stageDesktop);
            route.Window.ReportWindowState(_stageWindowState);
            _topLevel = route.Window.Surface;
            if (_requestInsetsCommand is { } command)
                _topLevel.Insets.SetRequestHandler(command, _reportError);
            UpdateRouteSurface(route, token, width, height, scaling);
            UpdateSurfaceOrigin();
            RefreshWindowInsets();
            _stageRoot = new Window(route.Window)
            {
                WindowDecorations = WindowDecorations.None,
                Content = _lifetime.MainView
            };
            _stageRoot.Show();
            route.Window.ReportFocus(_applicationForeground && _windowFocused);
            RefreshRendering();
            return;
        }
        if (UpdateRouteSurface(_stageRoute, token, width, height, scaling))
        {
            UpdateSurfaceOrigin();
            RefreshWindowInsets();
            RefreshRendering();
        }
    }

    private bool UpdateRouteSurface(WindowRoute route, long token, int width, int height, double scaling)
    {
        if (token <= 0 || (token != route.Token && token <= route.LatestToken))
            return false;
        if (token != route.Token)
            CancelInboundDrag(route.Token);
        Volatile.Write(ref route.Token, token);
        route.Window.Surface.SetNativeControlSurface(token);
        route.LatestToken = token;
        route.Window.ApplySurfaceGeometry(width, height, scaling);
        return true;
    }

    private bool DetachStageSurface(long token)
    {
        if (_stageRoute is not { } route)
            return false;
        if (route.Token == token)
        {
            CancelInboundDrag(token);
            CloseAccessibility(token);
            Volatile.Write(ref route.Token, 0);
            route.Window.Surface.SetNativeControlSurface(0);
            route.Window.Surface.SetInputMethodActive(false);
            _surfaceOffset = default;
            _hasSurfaceOffset = false;
            route.Window.ReportSurfaceUnavailable();
            UpdateSurfaceOrigin();
            RefreshWindowInsets();
            RefreshRendering();
        }
        return true;
    }

    /// <summary>Registers the XComponent surface for an existing Avalonia child window.</summary>
    public void UpdateChildSurface(int key, long token, int width, int height, double scaling) => Post(() =>
    {
        if (!_children.TryGetValue(key, out var route) || route.Window.IsCloseRequested)
            return;
        var previousToken = route.Token;
        if (!UpdateRouteSurface(route, token, width, height, scaling))
            return;
        if (previousToken != 0 && previousToken != token)
        {
            CloseAccessibility(previousToken);
            _childSurfaces.Remove(previousToken);
            ResetChildInsets(route);
        }
        _childSurfaces[token] = route;
        RefreshChildInsets(route);
        RefreshChildInputMethod(route);
    });

    /// <summary>Detaches an XComponent without closing its child window.</summary>
    public void CloseChildSurface(int key, long token) => Post(() =>
    {
        if (!_children.TryGetValue(key, out var route) || route.Token != token)
            return;
        CancelInboundDrag(token);
        CloseAccessibility(token);
        _childSurfaces.Remove(token);
        Volatile.Write(ref route.Token, 0);
        route.Window.Surface.SetNativeControlSurface(0);
        ResetChildInsets(route);
        RefreshChildInsets(route);
        RefreshChildInputMethod(route);
        route.Window.ReportSurfaceUnavailable();
    });

    /// <summary>Reports the stage (key 0) or child frame in display-local physical coordinates.</summary>
    public void ReportChildFrame(int key, int windowId, long displayId, PixelRect frame) => Post(() =>
    {
        if (key == 0)
        {
            if (_stageCanOpen && _stageDesktop && frame.Width > 0 && frame.Height > 0)
            {
                _stageFrameSize = frame.Size;
                SetWindowPositionCore(displayId, frame.Position);
            }
            return;
        }
        if (!_children.TryGetValue(key, out var route))
            return;
        route.PlatformId = windowId;
        route.DisplayId = displayId;
        route.Frame = frame;
        RefreshChildOrigin(route);
        RefreshChildInputMethod(route);
    });

    /// <summary>Reports native child lifecycle or stage window state (key 0).</summary>
    public void ReportChildEvent(int key, OpenHarmonyChildWindowEvent state) => Post(() =>
    {
        if (key == 0)
        {
            ReportStageEvent(state);
            return;
        }
        if (!_children.TryGetValue(key, out var route))
            return;
        switch (state)
        {
            case OpenHarmonyChildWindowEvent.Shown:
                route.Visible = true;
                route.Window.ReportVisible(_applicationForeground);
                break;
            case OpenHarmonyChildWindowEvent.Hidden:
                route.Visible = false;
                route.Window.ReportNativeHidden();
                route.Focused = false;
                break;
            case OpenHarmonyChildWindowEvent.Focused:
                route.Focused = _applicationForeground;
                break;
            case OpenHarmonyChildWindowEvent.Unfocused:
                route.Focused = false;
                break;
            case OpenHarmonyChildWindowEvent.Destroyed:
                route.Window.ReportDestroyed();
                return;
            case OpenHarmonyChildWindowEvent.ModalityReleased:
                route.Window.ReportModalityReleased();
                break;
        }
        RefreshChildInputMethod(route);
        CancelUnavailableInboundDrag();
    });

    private void ReportStageEvent(OpenHarmonyChildWindowEvent state)
    {
        switch (state)
        {
            case OpenHarmonyChildWindowEvent.StageCreated:
                _stageCanOpen = true;
                return;
            case OpenHarmonyChildWindowEvent.Destroyed:
                DestroyStage();
                return;
        }
        if (!_stageCanOpen)
            return;
        if (state == OpenHarmonyChildWindowEvent.StageDesktopAvailable)
        {
            _stageDesktop = true;
            _stageRoute?.Window.ReportStageDesktop(true);
            return;
        }

        var observed = state switch
        {
            OpenHarmonyChildWindowEvent.StageDesktopUnavailable or OpenHarmonyChildWindowEvent.StageNormal => WindowState.Normal,
            OpenHarmonyChildWindowEvent.StageMaximized => WindowState.Maximized,
            OpenHarmonyChildWindowEvent.StageMinimized => WindowState.Minimized,
            OpenHarmonyChildWindowEvent.StageFullScreen => WindowState.FullScreen,
            _ => (WindowState?)null
        };
        if (!_stageDesktop || observed is null)
            return;
        if (state == OpenHarmonyChildWindowEvent.StageDesktopUnavailable)
        {
            _stageDesktop = false;
            _stageFrameSize = null;
            _stageRoute?.Window.ReportStageDesktop(false);
            UpdateSurfaceOrigin();
        }
        _stageWindowState = observed.Value;
        _stageRoute?.Window.ReportWindowState(observed.Value);
        RefreshRendering();
    }

    /// <summary>Returns true if Avalonia cancels the native title-bar close. Call off the ArkTS UI thread.</summary>
    public bool RequestChildClose(int key)
    {
        Dispatcher? dispatcher;
        lock (_dispatchLock)
            dispatcher = _dispatcher;
        if (dispatcher is null)
            return false;

        bool Query() => _children.TryGetValue(key, out var route) && route.Window.RequestNativeClose();
        try
        {
            if (dispatcher.CheckAccess())
                return Query();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            return dispatcher.InvokeAsync(Query, DispatcherPriority.Input, timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (Exception error)
        {
            _reportError(error.ToString());
            return true;
        }
    }

    private void DestroyStage()
    {
        _stageCanOpen = false;
        _stageDesktop = false;
        _stageWindowState = WindowState.Normal;
        _stageFrameSize = null;
        foreach (var child in _children.Values.ToArray())
            child.Window.ReportDestroyed();
        _stageRoute?.Window.ReportDestroyed();
    }

    private void OnManagedWindowClosed(OpenHarmonyChildWindow window)
    {
        if (window.IsStage)
        {
            _stageCanOpen = false;
            if (_stageRoute is { } stage)
            {
                _closedStageToken = Math.Max(_closedStageToken, stage.LatestToken);
                if (stage.Token > 0)
                {
                    CancelInboundDrag(stage.Token);
                    CloseAccessibility(stage.Token);
                }
            }
            foreach (var child in _children.Values.ToArray())
                child.Window.Dispose();
            if (_stageRoot is { } root)
                root.Content = null;
            _stageRoot = null;
            _stageRoute = null;
            _topLevel = null;
            _rendering = false;
            _surfaceOffset = default;
            _hasSurfaceOffset = false;
            return;
        }
        if (_children.Remove(checked((int)window.Id), out var route) && route.Token != 0)
        {
            CancelInboundDrag(route.Token);
            CloseAccessibility(route.Token);
            _childSurfaces.Remove(route.Token);
            Volatile.Write(ref route.Token, 0);
            ResetChildInsets(route);
            RefreshChildInsets(route);
        }
    }

    private void RefreshChildOrigin(WindowRoute route)
    {
        if (route.Frame.Width <= 0 || route.Frame.Height <= 0)
            return;
        var position = route.Frame.Position + _screens.OriginOfDisplay(route.DisplayId);
        route.Window.Surface.ScreenOrigin = position + route.Offset;
        route.Window.ReportFrame(new PixelRect(position, route.Frame.Size));
    }

    private SurfaceTopLevel? FindInputSurface(long token)
    {
        if (_stageRoute is { } stage && stage.Token == token && stage.Window.AcceptsInput)
            return stage.Window.Surface;
        if (_childSurfaces.TryGetValue(token, out var child) && child.Window.AcceptsInput)
            return child.Window.Surface;
        return _stageRoute is null && _topLevel?.Surface == token ? _topLevel : null;
    }

    private SurfaceTopLevel? FindHostedSurface(long token)
    {
        if (_stageRoute is { } stage && stage.Token == token)
            return stage.Window.Surface;
        if (_childSurfaces.TryGetValue(token, out var child) && !child.Window.IsCloseRequested)
            return child.Window.Surface;
        return _stageRoute is null && _topLevel?.Surface == token ? _topLevel : null;
    }

    private void RefreshChildInputMethod(WindowRoute route)
    {
        var active = IsChildInputMethodActive(route);
        if (!active && route.Insets.SetArea(OpenHarmonyAvoidAreaKind.Keyboard, default))
            RefreshChildInsets(route);
        route.Window.Surface.SetInputMethodActive(active);
        route.Window.ReportFocus(active);
    }

    private bool IsChildInputMethodActive(WindowRoute route) => !route.Window.IsCloseRequested &&
        _applicationForeground && route.Focused && route.Visible && route.Token != 0;

    private IInputElement? FocusedWindowElement()
    {
        if (_stageRoot?.FocusManager?.GetFocusedElement() is { } stageElement)
            return stageElement;
        return _root?.FocusManager?.GetFocusedElement();
    }

    private static async Task<bool> SendWindowRequest(OpenHarmonyWindowOptions options, OpenHarmonyChildWindowRequest request)
    {
        using var data = new MemoryStream();
        using (var json = new Utf8JsonWriter(data))
        {
            json.WriteStartObject();
            json.WriteNumber("id", request.Id);
            json.WriteString("operation", request.Operation switch
            {
                OpenHarmonyChildWindowOperation.Show => "show",
                OpenHarmonyChildWindowOperation.Hide => "hide",
                OpenHarmonyChildWindowOperation.Close => "close",
                OpenHarmonyChildWindowOperation.Activate => "activate",
                OpenHarmonyChildWindowOperation.Resize => "resize",
                OpenHarmonyChildWindowOperation.Move => "move",
                OpenHarmonyChildWindowOperation.SetTitle => "setTitle",
                OpenHarmonyChildWindowOperation.SetDecorations => "setDecorations",
                OpenHarmonyChildWindowOperation.SetResizable => "setResizable",
                OpenHarmonyChildWindowOperation.SetLimits => "setLimits",
                OpenHarmonyChildWindowOperation.SetState => "setState",
                OpenHarmonyChildWindowOperation.SetCaptionButtons => "setCaptionButtons",
                OpenHarmonyChildWindowOperation.SetExtendedTitleBar => "setExtendedTitleBar",
                _ => throw new ArgumentOutOfRangeException(nameof(request))
            });
            if (request.Position is { } position)
            {
                json.WriteNumber("x", position.X);
                json.WriteNumber("y", position.Y);
            }
            if (request.Size is { } size)
            {
                json.WriteNumber("width", size.Width);
                json.WriteNumber("height", size.Height);
            }
            json.WriteString("title", request.Title);
            json.WriteBoolean("modal", request.Modal);
            json.WriteBoolean("activate", request.Activate);
            json.WriteBoolean("decorated", request.Decorated);
            if (request.Resizable is { } resizable)
                json.WriteBoolean("resizable", resizable);
            if (request.State is { } state)
                json.WriteString("state", state.ToString().ToLowerInvariant());
            if (request.Operation == OpenHarmonyChildWindowOperation.SetCaptionButtons)
            {
                json.WriteBoolean("canMinimize", request.CanMinimize);
                json.WriteBoolean("canMaximize", request.CanMaximize);
            }
            if (request.Operation == OpenHarmonyChildWindowOperation.SetExtendedTitleBar)
            {
                json.WriteBoolean("extendTitleBar", request.ExtendTitleBar);
                json.WriteNumber("titleBarHeight", request.TitleBarHeight);
                if (request.TitleBarTheme is { } theme)
                    json.WriteString("titleBarTheme", theme == PlatformThemeVariant.Dark ? "dark" : "light");
            }
            if (request.Limits is { } limits)
            {
                json.WriteStartObject("limits");
                if (limits.MinWidth is { } minWidth)
                    json.WriteNumber("minWidth", minWidth);
                if (limits.MinHeight is { } minHeight)
                    json.WriteNumber("minHeight", minHeight);
                if (limits.MaxWidth is { } maxWidth)
                    json.WriteNumber("maxWidth", maxWidth);
                if (limits.MaxHeight is { } maxHeight)
                    json.WriteNumber("maxHeight", maxHeight);
                json.WriteEndObject();
            }
            json.WriteEndObject();
        }

        var result = await options.Request("window.child", Encoding.UTF8.GetString(data.ToArray())).ConfigureAwait(false);
        result.File?.Dispose();
        return result.Value is not null;
    }

    private sealed class WindowRoute
    {
        public OpenHarmonyChildWindow Window = null!;
        public long Token;
        public long LatestToken;
        public int PlatformId;
        public long DisplayId;
        public PixelRect Frame;
        public PixelPoint Offset;
        public bool HasOffset;
        public readonly OpenHarmonyInsetsState Insets = new();
        public bool Visible;
        public bool Focused;
    }
}
