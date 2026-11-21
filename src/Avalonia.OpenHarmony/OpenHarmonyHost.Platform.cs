using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

/// <summary>Application and window transitions forwarded by the HarmonyOS host.</summary>
public enum OpenHarmonyLifecycleEvent
{
    WindowHidden = 0,
    WindowShown = 1,
    WindowInactive = 2,
    WindowActive = 3,
    ApplicationBackground = 4,
    ApplicationForeground = 5
}

public sealed partial class OpenHarmonyHost
{
    private static readonly TimeSpan BackTimeout = TimeSpan.FromMilliseconds(150);

    private readonly OpenHarmonyPlatformSettings _settings;
    private readonly OpenHarmonyScreens _screens = new();
    private bool _applicationForeground = true;
    private bool _windowVisible = true;
    private bool _windowFocused = true;
    private bool _rendering;
    private PixelPoint _windowPosition;
    private PixelPoint _surfaceOffset;

    /// <summary>Queues a platform lifecycle change on the Avalonia UI dispatcher.</summary>
    public void SetLifecycle(OpenHarmonyLifecycleEvent transition) => Post(() =>
    {
        switch (transition)
        {
            case OpenHarmonyLifecycleEvent.WindowHidden:
                SetWindowFocus(false);
                _windowVisible = false;
                RefreshRendering();
                break;
            case OpenHarmonyLifecycleEvent.WindowShown:
                _windowVisible = true;
                RefreshRendering();
                break;
            case OpenHarmonyLifecycleEvent.WindowInactive:
                SetWindowFocus(false);
                break;
            case OpenHarmonyLifecycleEvent.WindowActive:
                SetWindowFocus(true);
                break;
            case OpenHarmonyLifecycleEvent.ApplicationBackground:
                SetApplicationForeground(false);
                break;
            case OpenHarmonyLifecycleEvent.ApplicationForeground:
                SetApplicationForeground(true);
                break;
        }
    });

    /// <summary>Reports the platform theme and notifies Avalonia when it changes.</summary>
    public void SetTheme(PlatformThemeVariant theme) => Post(() => _settings.SetTheme(theme));

    /// <summary>Replaces the device's current display list.</summary>
    public void UpdateDisplays(IReadOnlyList<OpenHarmonyDisplayInfo> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var snapshot = displays.ToArray();
        Post(() =>
        {
            var changed = _screens.Replace(snapshot);
            UpdateSurfaceOrigin();
            foreach (var child in _children.Values.ToArray())
                RefreshChildOrigin(child);
            if (changed)
                _screens.NotifyChanged();
        });
    }

    /// <summary>Updates the hosted window's position in pixels relative to its display.</summary>
    public void SetWindowPosition(long displayId, PixelPoint position) => Post(() =>
    {
        _windowPosition = position;
        var changed = _screens.SetHostDisplay(displayId);
        UpdateSurfaceOrigin();
        if (changed)
            _screens.NotifyChanged();
    });

    /// <summary>Updates the XComponent origin in pixels relative to the hosted window.</summary>
    public void SetSurfaceOffset(long surface, PixelPoint offset) => Post(() =>
    {
        if (_childSurfaces.TryGetValue(surface, out var child))
        {
            child.Offset = offset;
            RefreshChildOrigin(child);
            return;
        }
        if ((_stageRoute?.Token ?? _topLevel?.Surface) != surface)
            return;
        _surfaceOffset = offset;
        _hasSurfaceOffset = true;
        UpdateSurfaceOrigin();
        RefreshWindowInsets();
    });

    /// <summary>Asks the current Avalonia view to handle the system back action.</summary>
    public bool RequestBack()
    {
        Dispatcher? dispatcher;
        BackRequest? request = null;
        lock (_dispatchLock)
        {
            dispatcher = _dispatcher;
            if (_stopped || dispatcher is null)
                return false;
            if (!dispatcher.CheckAccess())
            {
                request = new BackRequest();
                Dispatch(dispatcher, () => request.Run(DispatchBack));
            }
        }
        return request is null ? DispatchBack() : request.Wait();
    }

    private bool DispatchBack()
    {
        if (!_rendering)
            return false;
        try
        {
            return _topLevel?.RequestBack() == true;
        }
        catch (Exception exception)
        {
            _reportError(exception.ToString());
            return false;
        }
    }

    private void SetWindowFocus(bool focused)
    {
        if (_windowFocused == focused)
            return;
        _windowFocused = focused;
        if (_stageRoute is { } stage)
            stage.Window.ReportFocus(focused && _applicationForeground);
        else if (!focused)
            _topLevel?.LostFocus?.Invoke();
    }

    private void SetApplicationForeground(bool foreground)
    {
        _applicationForeground = foreground;
        if (!foreground)
            SetWindowFocus(false);
        RefreshRendering();
        foreach (var child in _children.Values.ToArray())
        {
            child.Window.ReportVisible(foreground && child.Visible);
            if (!foreground)
                child.Window.ReportFocus(false);
        }
        _lifetime.ReportForeground(foreground);
    }

    private void RefreshRendering()
    {
        var render = _applicationForeground && _windowVisible;
        if (_stageRoute is { } stage)
        {
            _rendering = render && stage.Token != 0;
            stage.Window.ReportVisible(_rendering);
            return;
        }
        if (_root is null)
            return;
        if (_rendering == render)
            return;
        if (render)
        {
            _root.StartRendering();
            _topLevel?.Paint?.Invoke(new Rect(_topLevel.ClientSize));
        }
        else
        {
            _root.StopRendering();
        }
        _rendering = render;
    }

    private void UpdateSurfaceOrigin()
    {
        if (_topLevel is { } topLevel)
        {
            topLevel.ScreenOrigin = _screens.HostOrigin + _windowPosition + _surfaceOffset;
            _stageRoute?.Window.ReportFrame(new PixelRect(_screens.HostOrigin + _windowPosition, topLevel.PixelSize));
        }
    }

    private sealed class BackRequest
    {
        private const int Started = 1;
        private const int Expired = 2;
        private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _state;

        public void Run(Func<bool> request)
        {
            if (Interlocked.CompareExchange(ref _state, Started, 0) == 0)
                _result.SetResult(request());
        }

        public bool Wait()
        {
            try
            {
                return _result.Task.WaitAsync(BackTimeout).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                // A running handler may already have navigated. Avoid also running ArkUI's default action.
                return Interlocked.CompareExchange(ref _state, Expired, 0) == Started;
            }
        }
    }
}

internal sealed class OpenHarmonyPlatformSettings(PlatformThemeVariant initialTheme) : DefaultPlatformSettings
{
    private volatile PlatformThemeVariant _theme = initialTheme;

    public override PlatformColorValues GetColorValues() => new() { ThemeVariant = _theme };

    public void SetTheme(PlatformThemeVariant theme)
    {
        if (_theme == theme)
            return;
        _theme = theme;
        OnColorValuesChanged(GetColorValues());
    }
}

internal sealed class OpenHarmonyNavigationManager : ISystemNavigationManagerImpl
{
    public event EventHandler<RoutedEventArgs>? BackRequested;

    public bool RequestBack()
    {
        var args = new RoutedEventArgs();
        BackRequested?.Invoke(this, args);
        return args.Handled;
    }
}
