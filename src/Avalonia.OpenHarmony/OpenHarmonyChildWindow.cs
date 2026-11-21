using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

internal sealed class OpenHarmonyChildWindow : IWindowImpl, IFramebufferPlatformSurface
{
    private static long s_zOrder;
    private readonly Func<OpenHarmonyChildWindowRequest, Task<bool>> _request;
    private readonly Action<OpenHarmonyChildWindow> _closed;
    private readonly Action<string> _reportError;
    private OpenHarmonyChildWindow? _owner;
    private PixelPoint? _requestedPosition;
    private PixelSize? _requestedFrameSize;
    private Size _initialClientSize;
    private PixelRect? _frame;
    private string? _title;
    private bool _decorated = true;
    private bool _enabled = true;
    private bool _showRequested;
    private volatile bool _avaloniaVisible;
    private volatile bool _visible;
    private bool _focused;
    private bool _active;
    private bool _hasSurfaceGeometry;
    private bool _closingFromPlatform;
    private volatile bool _disposed;
    private volatile bool _framebufferAvailable;
    private Task _pending = Task.CompletedTask;
    private WindowResizeReason _resizeReason;
    private WindowResizeReason _requestedResizeReason;

    internal OpenHarmonyChildWindow(long id, bool isStage, SurfaceTopLevel surface,
        Func<OpenHarmonyChildWindowRequest, Task<bool>> request, Action<OpenHarmonyChildWindow> closed,
        Action<string> reportError)
    {
        Id = id;
        IsStage = isStage;
        Surface = surface;
        _request = request;
        _closed = closed;
        _reportError = reportError;
        _initialClientSize = isStage ? surface.ClientSize : new Size(480, 320);
        _hasSurfaceGeometry = surface.PixelSize.Width > 0 && surface.PixelSize.Height > 0;
        _framebufferAvailable = _hasSurfaceGeometry;
        Surfaces = surface.UsesEgl ? surface.Surfaces : [this];
        surface.Input = DispatchInput;
        surface.Resized = (size, _) => Resized?.Invoke(size, _resizeReason);
    }

    internal long Id { get; }
    internal bool IsStage { get; }
    internal SurfaceTopLevel Surface { get; }
    internal bool AcceptsInput => _enabled && CanRender;
    internal long ZOrder { get; private set; }
    private bool CanRender => _framebufferAvailable && _visible && _avaloniaVisible && !_disposed;
    public IPlatformHandle? Handle => Surface.Handle;
    public Size ClientSize => _hasSurfaceGeometry ? Surface.ClientSize : _initialClientSize;
    public Size? FrameSize => _frame is { } frame
        ? new Size(frame.Width / DesktopScaling, frame.Height / DesktopScaling)
        : ClientSize;
    public double DesktopScaling => !_hasSurfaceGeometry && _owner != null ? _owner.DesktopScaling : Surface.DesktopScaling;
    public double RenderScaling => Surface.RenderScaling;
    public PixelPoint Position => _frame?.Position ?? _requestedPosition ?? default;
    public Size MaxAutoSizeHint => Size.Infinity;
    public Compositor Compositor => Surface.Compositor;
    public IPlatformRenderSurface[] Surfaces { get; }
    bool IPlatformRenderSurface.IsReady => CanRender;
    public WindowTransparencyLevel TransparencyLevel => Surface.TransparencyLevel;
    public AcrylicPlatformCompensationLevels AcrylicCompensationLevels => Surface.AcrylicCompensationLevels;
    public Action<RawInputEventArgs>? Input { get; set; }
    public Action<Rect>? Paint { get => Surface.Paint; set => Surface.Paint = value; }
    public Action<Size, WindowResizeReason>? Resized { get; set; }
    public Action<double>? ScalingChanged { get => Surface.ScalingChanged; set => Surface.ScalingChanged = value; }
    public Action<WindowTransparencyLevel>? TransparencyLevelChanged
    {
        get => Surface.TransparencyLevelChanged;
        set => Surface.TransparencyLevelChanged = value;
    }

    public Action? LostFocus { get => Surface.LostFocus; set => Surface.LostFocus = value; }
    public Action? Closed { get; set; }
    public Action<PixelPoint>? PositionChanged { get; set; }
    public Action? Deactivated { get; set; }
    public Action? Activated { get; set; }
    public Action? GotInputWhenDisabled { get; set; }
    public Func<WindowCloseReason, bool>? Closing { get; set; }
    public Action<WindowState>? WindowStateChanged { get; set; }
    public Action<bool>? ExtendClientAreaToDecorationsChanged { get; set; }
    public bool IsClientAreaExtendedToDecorations => false;
    public bool NeedsManagedDecorations => false;
    public PlatformRequestedDrawnDecoration RequestedDrawnDecorations => 0;
    public Thickness ExtendedMargins => default;
    public Thickness OffScreenMargin => default;
    public PlatformAllowedWindowActions AllowedWindowActions => PlatformAllowedWindowActions.None;
    public bool WindowStateGetterIsUsable => true;
    public WindowState WindowState { get => WindowState.Normal; set { } }

    public void Show(bool activate, bool isDialog)
    {
        if (_disposed)
            return;
        if (IsStage)
        {
            _avaloniaVisible = true;
            RefreshActivation();
            Paint?.Invoke(new Rect(ClientSize));
            return;
        }

        if (_owner is not { IsStage: true, _disposed: false })
        {
            Dispatcher.UIThread.Post(() => CloseAfterFailure("A HarmonyOS child window requires a live WindowStage owner."));
            return;
        }

        var firstShow = !_showRequested;
        _showRequested = true;
        _avaloniaVisible = true;
        if (firstShow)
            _requestedFrameSize = GetFrameRequest(_initialClientSize, DesktopScaling);
        Enqueue(new(Id, OpenHarmonyChildWindowOperation.Show)
        {
            Position = firstShow ? _requestedPosition : null,
            Size = firstShow ? _requestedFrameSize : null,
            Title = _title,
            Modal = isDialog,
            Activate = activate,
            Decorated = _decorated
        });
    }

    public void Hide()
    {
        // Reject native input as soon as Avalonia hides the window.
        if (!IsStage)
            ReportVisible(false);
        _avaloniaVisible = false;
        RefreshActivation();
        if (!IsStage && _showRequested)
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.Hide));
    }

    public void Activate()
    {
        if (IsStage || _showRequested)
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.Activate));
    }

    public void SetParent(IWindowImpl? parent) => _owner = parent as OpenHarmonyChildWindow;

    public void SetEnabled(bool enable)
    {
        if (_enabled && !enable)
        {
            Surface.CancelHardwarePointer();
            LostFocus?.Invoke();
        }
        _enabled = enable;
    }

    public void SetTitle(string? title)
    {
        if (_title == title)
            return;
        _title = title;
        if (!IsStage && _showRequested)
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetTitle) { Title = title });
    }

    public void SetWindowDecorations(WindowDecorations enabled) => _decorated = enabled != WindowDecorations.None;

    public void Resize(Size clientSize, WindowResizeReason reason = WindowResizeReason.Application)
    {
        if (_disposed || IsStage || !double.IsFinite(clientSize.Width) || !double.IsFinite(clientSize.Height)
            || clientSize.Width <= 0 || clientSize.Height <= 0)
            return;
        if (_hasSurfaceGeometry && Surface.ClientSize == clientSize)
            return;

        _initialClientSize = clientSize;
        if (!_showRequested)
            return;

        var frameSize = GetFrameRequest(clientSize, DesktopScaling);
        if (_requestedFrameSize == frameSize)
            return;
        _requestedFrameSize = frameSize;
        _requestedResizeReason = reason;
        Enqueue(new(Id, OpenHarmonyChildWindowOperation.Resize) { Size = frameSize });
    }

    public void Move(PixelPoint point)
    {
        if (_disposed || IsStage || _requestedPosition == point)
            return;

        _requestedPosition = point;
        if (_showRequested)
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.Move) { Position = point });
    }

    public void SetTopmost(bool value) { }
    public void SetIcon(IWindowIconImpl? icon) { }
    public void ShowTaskbarIcon(bool value) { }
    public void CanResize(bool value) { }
    public void SetCanMinimize(bool value) { }
    public void SetCanMaximize(bool value) { }
    public void SetMinMaxSize(Size minSize, Size maxSize) { }
    public void SetExtendClientAreaToDecorationsHint(bool extendIntoClientAreaHint) { }
    public void SetExtendClientAreaTitleBarHeightHint(double titleBarHeight) { }
    public void BeginMoveDrag(PointerPressedEventArgs e) { }
    public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e) { }
    public void SetInputRoot(IInputRoot inputRoot) => Surface.SetInputRoot(inputRoot);
    public Point PointToClient(PixelPoint point) => Surface.PointToClient(point);
    public PixelPoint PointToScreen(Point point) => Surface.PointToScreen(point);
    public void SetCursor(ICursorImpl? cursor) => Surface.SetCursor(cursor);
    public IPopupImpl? CreatePopup() => Surface.CreatePopup();
    public void SetTransparencyLevelHint(IReadOnlyList<WindowTransparencyLevel> levels) =>
        Surface.SetTransparencyLevelHint(levels);
    public void SetFrameThemeVariant(PlatformThemeVariant? themeVariant) => Surface.SetFrameThemeVariant(themeVariant);
    public object? TryGetFeature(Type featureType) => Surface.TryGetFeature(featureType);

    IFramebufferRenderTarget IFramebufferPlatformSurface.CreateFramebufferRenderTarget() =>
        Surface.CreateFramebufferRenderTarget();

    internal void ReportFrame(PixelRect frame)
    {
        if (_disposed || frame.Width <= 0 || frame.Height <= 0)
            return;
        var previous = Position;
        _frame = frame;
        if (_requestedPosition is { } requested && requested == frame.Position)
            _requestedPosition = null;
        if (previous != frame.Position)
            PositionChanged?.Invoke(frame.Position);
    }

    internal void ApplySurfaceGeometry(int width, int height, double scaling,
        WindowResizeReason reason = WindowResizeReason.Unspecified)
    {
        if (_disposed || width <= 0 || height <= 0)
            return;
        _resizeReason = !_hasSurfaceGeometry ? WindowResizeReason.Application
            : reason != WindowResizeReason.Unspecified ? reason
            : Surface.RenderScaling != scaling ? WindowResizeReason.DpiChange
            : _requestedFrameSize is { } requested && _frame?.Size == requested ? _requestedResizeReason
            : WindowResizeReason.Unspecified;
        if (_frame?.Size == _requestedFrameSize)
            _requestedFrameSize = null;
        _hasSurfaceGeometry = true;
        Surface.Resize(width, height, scaling);
        _framebufferAvailable = true;
        RefreshActivation();
        Paint?.Invoke(new Rect(ClientSize));
    }

    internal void ReportSurfaceUnavailable()
    {
        if (_disposed)
            return;
        _framebufferAvailable = false;
        RefreshActivation();
    }

    internal void ReportVisible(bool visible)
    {
        if (_disposed || _visible == visible)
            return;
        _visible = visible;
        RefreshActivation();
        if (visible)
        {
            ZOrder = ++s_zOrder;
            Paint?.Invoke(new Rect(ClientSize));
        }
    }

    internal void ReportFocus(bool focused)
    {
        if (_disposed)
            return;
        _focused = focused;
        RefreshActivation();
    }

    private void RefreshActivation()
    {
        var active = _focused && CanRender;
        if (_active == active)
            return;
        _active = active;
        if (active)
        {
            ZOrder = ++s_zOrder;
            Activated?.Invoke();
        }
        else
        {
            Surface.CancelHardwarePointer();
            LostFocus?.Invoke();
            Deactivated?.Invoke();
        }
    }

    /// <summary>Returns true to cancel a title-bar close before native destruction.</summary>
    internal bool RequestNativeClose()
    {
        if (_disposed)
            return false;
        _closingFromPlatform = true;
        try
        {
            if (Closing?.Invoke(WindowCloseReason.WindowClosing) == true)
                return true;
            Dispose();
            return false;
        }
        finally
        {
            _closingFromPlatform = false;
        }
    }

    internal void ReportDestroyed()
    {
        _closingFromPlatform = true;
        try { Dispose(); }
        finally { _closingFromPlatform = false; }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _framebufferAvailable = false;
        RefreshActivation();
        if (!IsStage && _showRequested && !_closingFromPlatform)
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.Close));
        try
        {
            _closed(this);
        }
        finally
        {
            try { Surface.Dispose(); }
            finally { Closed?.Invoke(); }
        }
    }

    private void DispatchInput(RawInputEventArgs args)
    {
        if (_disposed)
            return;
        if (AcceptsInput || args is RawPointerEventArgs
            {
                Type: RawPointerEventType.CancelCapture or RawPointerEventType.LeaveWindow
            })
            Input?.Invoke(args);
        else if (CanRender && !_enabled)
            GotInputWhenDisabled?.Invoke();
    }

    private PixelSize GetFrameRequest(Size clientSize, double scale)
    {
        var frameWidth = _frame is { } frame ? Math.Max(0, frame.Width - Surface.PixelSize.Width) : 0;
        var frameHeight = _frame is { } current ? Math.Max(0, current.Height - Surface.PixelSize.Height) : 0;
        return new PixelSize(ToPixelCount(clientSize.Width * scale + frameWidth),
            ToPixelCount(clientSize.Height * scale + frameHeight));
    }

    private static int ToPixelCount(double pixels) => (int)Math.Clamp(Math.Ceiling(pixels), 1, int.MaxValue);

    private void Enqueue(OpenHarmonyChildWindowRequest command) => _pending = SendAfterAsync(_pending, command);

    private async Task SendAfterAsync(Task previous, OpenHarmonyChildWindowRequest command)
    {
        await previous.ConfigureAwait(false);
        // Dispatch progress and the failure callback asynchronously, even for a synchronous test transport.
        if (_disposed && command.Operation != OpenHarmonyChildWindowOperation.Close)
            return;
        bool success;
        try
        {
            success = await _request(command).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            _reportError($"HarmonyOS window {Id} {command.Operation}: {error.Message}");
            success = false;
        }
        if (success)
            return;

        if (command.Operation is OpenHarmonyChildWindowOperation.Show or OpenHarmonyChildWindowOperation.Hide)
            Dispatcher.UIThread.Post(() => CloseAfterFailure($"Could not {command.Operation.ToString().ToLowerInvariant()} HarmonyOS window {Id}."));
        else
            _reportError($"HarmonyOS rejected {command.Operation} for window {Id}.");
    }

    private void CloseAfterFailure(string message)
    {
        if (_disposed)
            return;
        _reportError(message);
        Dispose();
    }
}
