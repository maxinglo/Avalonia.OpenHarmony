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
    private static readonly TimeSpan NativeCloseGrace = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan CloseRequestTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan[] CloseRetryDelays =
        [TimeSpan.Zero, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(450)];
    private static long s_zOrder;
    private readonly Func<OpenHarmonyChildWindowRequest, Task<bool>> _request;
    private readonly Action<OpenHarmonyChildWindow> _closed;
    private readonly Action<string> _reportError;
    private readonly Func<PixelPoint?, double> _getStartupScaling;
    private readonly IPlatformRenderSurface[] _softwareSurfaces;
    private OpenHarmonyChildWindow? _owner;
    private OpenHarmonyChildWindow? _closingModalOwner;
    private int _closingModalChildren;
    private PixelPoint? _requestedPosition;
    private PixelSize? _requestedFrameSize;
    private PixelSize? _requestedStageClientSize;
    private Size _initialClientSize;
    private Size _minClientSize;
    private Size _maxClientSize = Size.Infinity;
    private PixelRect? _frame;
    private string? _title;
    private bool _titleSet;
    private bool _decorated = true;
    private bool _resizable = true;
    private bool _freeform;
    private bool _stagePolicyInitialized;
    private bool _independent;
    private bool _childCanMaximize;
    private bool? _topmost;
    private bool _canMinimize = true;
    private bool _canMaximize = true;
    private bool _extendTitleBar;
    private double _titleBarHeight = -1;
    private PlatformThemeVariant? _titleBarTheme;
    private bool _dispatchingTitleBarPress;
    private bool _moveRequested;
    private RawPointerEventArgs? _dispatchingPress;
    private ResizeGrip? _resizeGrip;
    private OpenHarmonyChildWindowRequest? _nextGripResize;
    private bool _gripResizePending;
    private WindowState _windowState;
    private WindowState? _requestedWindowState;
    private int _stateRequestVersion;
    private bool _enabled = true;
    private bool _showRequested;
    private bool _shownModally;
    private volatile bool _avaloniaVisible;
    private volatile bool _visible;
    private bool _focused;
    private bool _active;
    private bool _hasSurfaceGeometry;
    private bool _closingFromPlatform;
    private bool _closeAttemptInProgress;
    private volatile bool _closeRequested;
    private volatile bool _disposed;
    private volatile bool _framebufferAvailable;
    private Task _pending = Task.CompletedTask;
    private WindowResizeReason _resizeReason;
    private WindowResizeReason _requestedResizeReason;

    internal OpenHarmonyChildWindow(long id, bool isStage, SurfaceTopLevel surface,
        Func<OpenHarmonyChildWindowRequest, Task<bool>> request, Action<OpenHarmonyChildWindow> closed,
        Action<string> reportError, Func<PixelPoint?, double> getStartupScaling)
    {
        Id = id;
        IsStage = isStage;
        Surface = surface;
        _request = request;
        _closed = closed;
        _reportError = reportError;
        _getStartupScaling = getStartupScaling;
        _title = isStage ? Window.TitleProperty.GetDefaultValue(typeof(Window)) : null;
        _initialClientSize = isStage ? surface.ClientSize : new Size(480, 320);
        _hasSurfaceGeometry = surface.PixelSize.Width > 0 && surface.PixelSize.Height > 0;
        _framebufferAvailable = _hasSurfaceGeometry;
        _softwareSurfaces = [this];
        surface.Input = DispatchInput;
        surface.Resized = (size, _) => Resized?.Invoke(size, _resizeReason);
    }

    internal long Id { get; }
    internal bool IsStage { get; }
    internal SurfaceTopLevel Surface { get; }
    internal bool AcceptsInput => _enabled && _closingModalChildren == 0 && CanRender;
    internal bool IsCloseRequested => _closeRequested;
    internal long ZOrder { get; private set; }
    internal bool CanRender => _framebufferAvailable && _visible && _avaloniaVisible && !_closeRequested && !_disposed;
    public IPlatformHandle? Handle => _closeRequested ? null : Surface.Handle;
    public Size ClientSize => _hasSurfaceGeometry ? Surface.ClientSize : _initialClientSize;
    public Size? FrameSize => _frame is { } frame
        ? new Size(frame.Width / DesktopScaling, frame.Height / DesktopScaling)
        : ClientSize;
    public double DesktopScaling => _hasSurfaceGeometry || IsStage ? Surface.DesktopScaling
        : _owner?.DesktopScaling ?? _getStartupScaling(_requestedPosition);
    public double RenderScaling => _hasSurfaceGeometry ? Surface.RenderScaling : DesktopScaling;
    public PixelPoint Position => _frame?.Position ?? _requestedPosition ?? default;
    public Size MaxAutoSizeHint => Size.Infinity;
    public Compositor Compositor => Surface.Compositor;
    public IPlatformRenderSurface[] Surfaces => Surface.UsesEgl ? Surface.Surfaces : _softwareSurfaces;
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
    public bool IsClientAreaExtendedToDecorations { get; private set; }
    public bool NeedsManagedDecorations => false;
    public PlatformRequestedDrawnDecoration RequestedDrawnDecorations => 0;
    public Thickness ExtendedMargins { get; private set; }
    public Thickness OffScreenMargin => default;
    public PlatformAllowedWindowActions AllowedWindowActions => !_freeform || !IsStage && (!_showRequested || _shownModally)
        ? PlatformAllowedWindowActions.None
        : (IsStage || _childCanMaximize ? PlatformAllowedWindowActions.Fullscreen : PlatformAllowedWindowActions.None)
            | (_canMaximize && (IsStage || _childCanMaximize) ? PlatformAllowedWindowActions.Maximize : PlatformAllowedWindowActions.None)
            | (_canMinimize ? PlatformAllowedWindowActions.Minimize : PlatformAllowedWindowActions.None);
    public Action<PlatformAllowedWindowActions>? AllowedWindowActionsChanged { get; set; }
    public bool WindowStateGetterIsUsable => true;
    public WindowState WindowState
    {
        get => _windowState;
        set
        {
            if (!IsStage && !_showRequested)
            {
                _requestedWindowState = value;
                return;
            }
            RequestWindowState(value);
        }
    }

    private void RequestWindowState(WindowState value)
    {
        if (!_freeform || value == (_requestedWindowState ?? _windowState) || !IsStage && _shownModally)
            return;
        if ((!IsStage && value is (WindowState.Maximized or WindowState.FullScreen) && !_childCanMaximize) ||
            (value == WindowState.Maximized && !_canMaximize) || (value == WindowState.Minimized && !_canMinimize))
            return;
        _requestedWindowState = value;
        Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetState)
        {
            State = value,
            StateRequestVersion = ++_stateRequestVersion
        });
    }

    public void Show(bool activate, bool isDialog)
    {
        if (_closeRequested || _disposed)
            return;
        if (IsStage)
        {
            var firstStageShow = !_showRequested;
            if (_showRequested && !_avaloniaVisible && _freeform)
                Enqueue(new(Id, OpenHarmonyChildWindowOperation.Show));
            _showRequested = true;
            _avaloniaVisible = true;
            if (firstStageShow && _freeform)
                ApplyStagePolicy();
            RefreshActivation();
            Paint?.Invoke(new Rect(ClientSize));
            return;
        }

        var firstShow = !_showRequested;
        var independent = _owner is null && !isDialog;
        if ((independent && !_freeform && firstShow) || _owner is { _disposed: true } or { _closeRequested: true } ||
            (_owner is null && !independent))
        {
            Dispatcher.UIThread.Post(() => CloseAfterFailure("HarmonyOS requires a live owner for dialogs and for windows outside freeform mode."));
            return;
        }

        if (!firstShow && independent != _independent)
        {
            Dispatcher.UIThread.Post(() => CloseAfterFailure("A HarmonyOS window cannot change between owned and independent after creation."));
            return;
        }
        _independent = independent;
        _showRequested = true;
        _shownModally = isDialog;
        _avaloniaVisible = true;
        if (firstShow)
        {
            _childCanMaximize = !isDialog && _canMaximize;
            _requestedFrameSize = GetFrameRequest(_initialClientSize, DesktopScaling);
        }
        Enqueue(new(Id, OpenHarmonyChildWindowOperation.Show)
        {
            OwnerId = _owner?.Id,
            Independent = independent,
            Position = firstShow ? _requestedPosition : null,
            Size = firstShow ? _requestedFrameSize : null,
            Title = _title,
            Modal = isDialog,
            Activate = activate,
            Decorated = _decorated,
            Resizable = _resizable ? null : false,
            Limits = GetWindowLimits(DesktopScaling),
            CanMinimize = _canMinimize,
            CanMaximize = _childCanMaximize && _canMaximize
        }, _owner is { IsStage: false } owner ? owner._pending : null);
        AllowedWindowActionsChanged?.Invoke(AllowedWindowActions);
        if (firstShow && _requestedWindowState is { } desiredState)
        {
            _requestedWindowState = null;
            RequestWindowState(desiredState);
        }
    }

    public void Hide()
    {
        // Reject native input as soon as Avalonia hides the window.
        Surface.CancelInboundDrag();
        if (!IsStage)
            ReportVisible(false);
        _avaloniaVisible = false;
        RefreshActivation();
        if (_showRequested && (!IsStage || _freeform))
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
            Surface.CancelInboundDrag();
            Surface.CancelHardwarePointer();
            LostFocus?.Invoke();
        }
        _enabled = enable;
        RefreshNativeControlInput();
    }

    public void SetTitle(string? title)
    {
        if (_title == title)
            return;
        _title = title;
        _titleSet = true;
        if (_showRequested && (!IsStage || _freeform))
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetTitle) { Title = title });
    }

    public void SetWindowDecorations(WindowDecorations enabled)
    {
        var decorated = enabled != WindowDecorations.None;
        if (_decorated == decorated)
            return;
        _decorated = decorated;
        if (_showRequested && (!IsStage || _freeform))
            SendDecorations();
    }

    private void SendDecorations() => Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetDecorations)
    {
        Decorated = _decorated,
        Resizable = _resizable,
        Limits = GetWindowLimits(DesktopScaling) ?? default(OpenHarmonyWindowLimits)
    });

    public void Resize(Size clientSize, WindowResizeReason reason = WindowResizeReason.Application)
    {
        if (_closeRequested || _disposed || !double.IsFinite(clientSize.Width) || !double.IsFinite(clientSize.Height)
            || clientSize.Width <= 0 || clientSize.Height <= 0)
            return;
        if (_hasSurfaceGeometry && Surface.ClientSize == clientSize)
            return;

        if (IsStage)
        {
            if (!_freeform || !_showRequested || WindowState != WindowState.Normal)
                return;
            var pixels = new PixelSize(ToPixelCount(clientSize.Width * DesktopScaling),
                ToPixelCount(clientSize.Height * DesktopScaling));
            if (pixels == Surface.PixelSize || pixels == _requestedStageClientSize)
                return;
            _requestedStageClientSize = pixels;
            _requestedResizeReason = reason;
            // Include the native chrome and any host layout outside the Avalonia surface.
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.Resize)
            {
                Size = GetFrameRequest(clientSize, DesktopScaling),
                StageClientSize = pixels
            });
            return;
        }

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
        if (_closeRequested || _disposed || (IsStage && (!_freeform || !_showRequested || WindowState != WindowState.Normal)) ||
            (_requestedPosition ?? _frame?.Position) == point)
            return;

        _requestedPosition = point;
        if (_showRequested)
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.Move) { Position = point });
    }

    public void SetTopmost(bool value)
    {
        if (_topmost == value || _topmost is null && !value)
            return;
        _topmost = value;
        if (IsStage && _showRequested && _freeform)
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetTopmost) { Topmost = value });
    }
    public void SetIcon(IWindowIconImpl? icon) { }
    public void ShowTaskbarIcon(bool value) { }
    public void CanResize(bool value)
    {
        if (_resizable == value)
            return;
        _resizable = value;
        if (_showRequested && (!IsStage || _freeform))
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetResizable) { Resizable = value });
    }
    public void SetCanMinimize(bool value)
    {
        if (_canMinimize == value)
            return;
        _canMinimize = value;
        SetCaptionButtons();
        if (_freeform)
            AllowedWindowActionsChanged?.Invoke(AllowedWindowActions);
    }

    public void SetCanMaximize(bool value)
    {
        if (_canMaximize == value)
            return;
        _canMaximize = value;
        SetCaptionButtons();
        if (_freeform)
            AllowedWindowActionsChanged?.Invoke(AllowedWindowActions);
    }

    private void SetCaptionButtons()
    {
        if (_freeform && _showRequested && (IsStage || _independent))
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetCaptionButtons)
            {
                CanMinimize = _canMinimize,
                CanMaximize = _canMaximize && (IsStage || _childCanMaximize)
            });
    }
    public void SetMinMaxSize(Size minSize, Size maxSize)
    {
        if (_minClientSize == minSize && _maxClientSize == maxSize)
            return;
        _minClientSize = minSize;
        _maxClientSize = maxSize;
        if (_showRequested && (!IsStage || _freeform))
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetLimits) { Limits = GetWindowLimits(DesktopScaling) ?? default(OpenHarmonyWindowLimits) });
    }
    public void SetExtendClientAreaToDecorationsHint(bool extendIntoClientAreaHint)
    {
        if (_extendTitleBar == extendIntoClientAreaHint)
            return;
        _extendTitleBar = extendIntoClientAreaHint;
        RequestTitleBarLayout();
    }

    public void SetExtendClientAreaTitleBarHeightHint(double titleBarHeight)
    {
        var height = double.IsFinite(titleBarHeight) && titleBarHeight >= 0 ? titleBarHeight : -1;
        if (_titleBarHeight == height)
            return;
        _titleBarHeight = height;
        if (_extendTitleBar)
            RequestTitleBarLayout();
    }

    private void RequestTitleBarLayout()
    {
        if (IsStage && _freeform && _showRequested)
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetExtendedTitleBar)
            {
                ExtendTitleBar = _extendTitleBar,
                TitleBarHeight = _titleBarHeight,
                TitleBarTheme = _titleBarTheme
            });
    }

    internal void ReportTitleBar(bool extended, double height, double buttonInset)
    {
        if (_closeRequested || _disposed)
            return;
        var margins = extended ? new Thickness(0, height, buttonInset, 0) : default;
        if (IsClientAreaExtendedToDecorations == extended && ExtendedMargins == margins)
            return;
        IsClientAreaExtendedToDecorations = extended;
        ExtendedMargins = margins;
        ExtendClientAreaToDecorationsChanged?.Invoke(extended);
    }

    public void BeginMoveDrag(PointerPressedEventArgs e)
    {
        if (_dispatchingTitleBarPress && IsClientAreaExtendedToDecorations && AcceptsInput)
            _moveRequested = true;
    }

    internal bool DispatchTitleBarPress(bool mouse, long contact, double x, double y, RawInputModifiers modifiers,
        Func<bool> acceptMove)
    {
        _dispatchingTitleBarPress = true;
        try
        {
            if (mouse)
                Surface.HardwarePointer(1, 1, x, y, 0, 0, modifiers);
            else
                Surface.Touch(contact, TouchPhase.Begin, x, y, modifiers);

            if (!_moveRequested || !acceptMove())
                return false;
            if (mouse)
                Surface.CancelHardwarePointer();
            else
                Surface.Touch(contact, TouchPhase.Cancel, x, y);
            return true;
        }
        finally
        {
            _moveRequested = false;
            _dispatchingTitleBarPress = false;
        }
    }
    public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (!_freeform || !_resizable || !AcceptsInput || WindowState != WindowState.Normal ||
            _dispatchingPress is not { } press || _frame is not { } frame || !Enum.IsDefined(edge))
            return;

        _resizeGrip = new(edge, press.Device, press.RawPointerId, Surface.PointToScreen(press.Position), frame);
    }
    public void SetInputRoot(IInputRoot inputRoot) => Surface.SetInputRoot(inputRoot);
    public Point PointToClient(PixelPoint point) => Surface.PointToClient(point);
    public PixelPoint PointToScreen(Point point) => Surface.PointToScreen(point);
    public void SetCursor(ICursorImpl? cursor) => Surface.SetCursor(cursor);
    public IPopupImpl? CreatePopup() => Surface.CreatePopup();
    public void SetTransparencyLevelHint(IReadOnlyList<WindowTransparencyLevel> levels) =>
        Surface.SetTransparencyLevelHint(levels);
    public void SetFrameThemeVariant(PlatformThemeVariant? themeVariant)
    {
        if (_titleBarTheme == themeVariant)
            return;
        _titleBarTheme = themeVariant;
        if (_extendTitleBar)
            RequestTitleBarLayout();
    }
    public object? TryGetFeature(Type featureType) => Surface.TryGetFeature(featureType);

    IFramebufferRenderTarget IFramebufferPlatformSurface.CreateFramebufferRenderTarget() =>
        Surface.CreateFramebufferRenderTarget(() => CanRender);

    internal void ReportFreeform(bool enabled)
    {
        if (_freeform == enabled || _closeRequested || _disposed)
            return;
        _freeform = enabled;
        AllowedWindowActionsChanged?.Invoke(AllowedWindowActions);
        if (!enabled)
        {
            ReportTitleBar(false, 0, 0);
            _requestedWindowState = null;
            ReportWindowState(WindowState.Normal);
            return;
        }
        if (_showRequested && IsStage)
            ApplyStagePolicy();
        else if (_showRequested && _independent)
            SetCaptionButtons();
    }

    private void ApplyStagePolicy()
    {
        if (_titleSet)
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetTitle) { Title = _title });
        if (!_decorated || _stagePolicyInitialized)
            SendDecorations();
        else
        {
            if (!_resizable)
                Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetResizable) { Resizable = false });
            if (GetWindowLimits(DesktopScaling) is { } limits)
                Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetLimits) { Limits = limits });
        }
        if (!_canMaximize || !_canMinimize || _stagePolicyInitialized)
            SetCaptionButtons();
        if (_topmost is { } topmost)
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetTopmost) { Topmost = topmost });
        if (_extendTitleBar)
            RequestTitleBarLayout();
        _stagePolicyInitialized = true;
    }

    internal void ReportWindowState(WindowState state)
    {
        if (_windowState == state || _closeRequested || _disposed)
            return;
        _windowState = state;
        if (state != WindowState.Normal)
            CancelResizeGrip();
        if (_requestedWindowState == state)
            _requestedWindowState = null;
        _requestedStageClientSize = null;
        WindowStateChanged?.Invoke(state);
    }

    internal void ReportFrame(PixelRect frame)
    {
        if (_closeRequested || _disposed || frame.Width <= 0 || frame.Height <= 0)
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
        if (_closeRequested || _disposed || width <= 0 || height <= 0)
            return;
        _resizeReason = !_hasSurfaceGeometry ? WindowResizeReason.Application
            : reason != WindowResizeReason.Unspecified ? reason
            : Surface.RenderScaling != scaling ? WindowResizeReason.DpiChange
            : _gripResizePending ? WindowResizeReason.User
            : IsStage && _requestedStageClientSize == new PixelSize(width, height) ? _requestedResizeReason
            : _requestedFrameSize is { } requested && _frame?.Size == requested ? _requestedResizeReason
            : WindowResizeReason.Unspecified;
        if (IsStage && Surface.PixelSize != new PixelSize(width, height))
            _requestedStageClientSize = null;
        if (_frame?.Size == _requestedFrameSize)
            _requestedFrameSize = null;
        var previousScaling = DesktopScaling;
        _hasSurfaceGeometry = true;
        Surface.Resize(width, height, scaling);
        if (_showRequested && (!IsStage || _freeform) && previousScaling != scaling && GetWindowLimits(scaling) is { } limits)
            Enqueue(new(Id, OpenHarmonyChildWindowOperation.SetLimits) { Limits = limits });
        _framebufferAvailable = true;
        RefreshActivation();
        Paint?.Invoke(new Rect(ClientSize));
    }

    internal void ReportSurfaceUnavailable()
    {
        if (_disposed)
            return;
        _framebufferAvailable = false;
        if (Surface.UsesEgl)
        {
            Surface.SetEglWindow(null);
            Paint?.Invoke(new Rect(ClientSize));
        }
        Surface.CancelInboundDrag();
        RefreshActivation();
    }

    internal void ReportVisible(bool visible)
    {
        if (_closeRequested || _disposed || _visible == visible)
            return;
        _visible = visible;
        if (!visible)
            Surface.CancelInboundDrag();
        RefreshActivation();
        if (visible)
        {
            ZOrder = ++s_zOrder;
            Paint?.Invoke(new Rect(ClientSize));
        }
    }

    internal void ReportFocus(bool focused)
    {
        if (_closeRequested || _disposed)
            return;
        _focused = focused;
        RefreshActivation();
    }

    internal void ReportNativeHidden()
    {
        ReportVisible(false);
        // A failed native destroy can leave the window alive but no longer visible or modal.
        ReportModalityReleased();
    }

    internal void ReportModalityReleased() => ReleaseClosingModalOwner();

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
        if (_closeRequested || _disposed)
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

    internal void ReportDestroyed() => FinishClose();

    public void Dispose()
    {
        if (_disposed)
            return;
        if (IsStage || !_showRequested)
        {
            FinishClose();
            return;
        }

        if (!_closeRequested)
        {
            _closeRequested = true;
            Surface.CancelInboundDrag();
            if (_shownModally && _owner is { } owner)
            {
                // Avalonia clears Owner after Dispose returns, before async native destruction.
                _closingModalOwner = owner;
                owner._closingModalChildren++;
                owner.RefreshNativeControlInput();
            }
            Surface.SetInputMethodActive(false);
            RefreshActivation();
            Surface.InputPane.Apply(false, default);
        }

        if (!_closeAttemptInProgress)
        {
            _closeAttemptInProgress = true;
            _pending = CloseNativeAfterAsync(_pending, _closingFromPlatform);
        }
    }

    private void FinishClose()
    {
        if (_disposed)
            return;
        _closeRequested = true;
        _disposed = true;
        _framebufferAvailable = false;
        RefreshActivation();
        ReleaseClosingModalOwner();
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

    private void ReleaseClosingModalOwner()
    {
        if (_closingModalOwner is not { } owner)
            return;
        _closingModalOwner = null;
        owner._closingModalChildren--;
        owner.RefreshNativeControlInput();
    }

    private void RefreshNativeControlInput() => Surface.SetNativeControlInputEnabled(_enabled && _closingModalChildren == 0);

    private void DispatchInput(RawInputEventArgs args)
    {
        if (_disposed)
            return;
        if (AcceptsInput || args is RawPointerEventArgs
            {
                Type: RawPointerEventType.CancelCapture or RawPointerEventType.LeaveWindow
            } || args is RawDragEvent { Type: RawDragEventType.DragLeave })
        {
            if (args is not RawPointerEventArgs pointer)
            {
                Input?.Invoke(args);
                return;
            }
            RouteResizeGrip(pointer);
            if (pointer.Type is not (RawPointerEventType.LeftButtonDown or RawPointerEventType.TouchBegin))
            {
                Input?.Invoke(args);
                return;
            }
            var previous = _dispatchingPress;
            _dispatchingPress = pointer;
            try { Input?.Invoke(args); }
            finally { _dispatchingPress = previous; }
        }
        else if (CanRender && !_enabled)
            GotInputWhenDisabled?.Invoke();
    }

    private void RouteResizeGrip(RawPointerEventArgs pointer)
    {
        if (_resizeGrip is not { } grip || !ReferenceEquals(grip.Device, pointer.Device) || grip.Id != pointer.RawPointerId)
            return;
        switch (pointer.Type)
        {
            case RawPointerEventType.CancelCapture:
            case RawPointerEventType.TouchCancel:
            case RawPointerEventType.LeftButtonDown:
            case RawPointerEventType.TouchBegin:
                CancelResizeGrip();
                return;
            case RawPointerEventType.LeftButtonUp:
            case RawPointerEventType.TouchEnd:
                _resizeGrip = null;
                return;
            case RawPointerEventType.Move when pointer.InputModifiers.HasFlag(RawInputModifiers.LeftMouseButton):
            case RawPointerEventType.TouchUpdate:
                break;
            default:
                return;
        }

        var offset = Surface.PointToScreen(pointer.Position) - grip.Origin;
        var fromLeft = grip.Edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest;
        var fromTop = grip.Edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast;
        var width = grip.Frame.Width + (fromLeft ? -offset.X :
            grip.Edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast ? offset.X : 0);
        var height = grip.Frame.Height + (fromTop ? -offset.Y :
            grip.Edge is WindowEdge.South or WindowEdge.SouthWest or WindowEdge.SouthEast ? offset.Y : 0);
        _nextGripResize = new OpenHarmonyChildWindowRequest(Id, OpenHarmonyChildWindowOperation.ResizeFromGrip)
        {
            Size = new PixelSize(Math.Max(width, 1), Math.Max(height, 1)),
            Position = new PixelPoint(fromLeft ? grip.Frame.Right : grip.Frame.X, fromTop ? grip.Frame.Bottom : grip.Frame.Y),
            ResizeFromLeft = fromLeft,
            ResizeFromTop = fromTop
        };
        SendNextGripResize();
    }

    private void CancelResizeGrip()
    {
        _resizeGrip = null;
        _nextGripResize = null;
    }

    private void SendNextGripResize()
    {
        if (_gripResizePending || _nextGripResize is not { } request || !AcceptsInput || !_freeform || !_resizable)
            return;
        _nextGripResize = null;
        _gripResizePending = true;
        Enqueue(request);
    }

    private readonly record struct ResizeGrip(WindowEdge Edge, IInputDevice Device, long Id, PixelPoint Origin, PixelRect Frame);

    private PixelSize GetFrameRequest(Size clientSize, double scale)
    {
        var frameWidth = _frame is { } frame ? Math.Max(0, frame.Width - Surface.PixelSize.Width) : 0;
        var frameHeight = _frame is { } current ? Math.Max(0, current.Height - Surface.PixelSize.Height) : 0;
        return new PixelSize(ToPixelCount(clientSize.Width * scale + frameWidth),
            ToPixelCount(clientSize.Height * scale + frameHeight));
    }

    private static int ToPixelCount(double pixels) => (int)Math.Clamp(Math.Ceiling(pixels), 1, int.MaxValue);

    private OpenHarmonyWindowLimits? GetWindowLimits(double scaling)
    {
        var minWidth = _minClientSize.Width > 0 ? ToPixelCount(_minClientSize.Width * scaling) : (int?)null;
        var minHeight = _minClientSize.Height > 0 ? ToPixelCount(_minClientSize.Height * scaling) : (int?)null;
        var maxWidth = GetMaximum(_maxClientSize.Width, minWidth);
        var maxHeight = GetMaximum(_maxClientSize.Height, minHeight);
        return minWidth.HasValue || minHeight.HasValue || maxWidth.HasValue || maxHeight.HasValue
            ? new(minWidth, minHeight, maxWidth, maxHeight) : null;

        int? GetMaximum(double size, int? minimum) => double.IsFinite(size)
            ? (int)Math.Clamp(Math.Floor(size * scaling), minimum ?? 1, int.MaxValue) : null;
    }

    private void Enqueue(OpenHarmonyChildWindowRequest command, Task? ownerPending = null)
    {
        if (!_closeRequested && !_disposed)
            _pending = SendAfterAsync(ownerPending is null ? _pending : Task.WhenAll(_pending, ownerPending), command);
    }

    private async Task SendAfterAsync(Task previous, OpenHarmonyChildWindowRequest command)
    {
        await previous.ConfigureAwait(false);
        if (_closeRequested || _disposed)
            return;
        var accepted = await SendAsync(command).ConfigureAwait(false);
        if (command.Operation == OpenHarmonyChildWindowOperation.ResizeFromGrip)
            Dispatcher.UIThread.Post(() =>
            {
                _gripResizePending = false;
                if (!accepted)
                    CancelResizeGrip();
                SendNextGripResize();
            }, DispatcherPriority.Input);
        if (command.Operation == OpenHarmonyChildWindowOperation.SetState)
            Dispatcher.UIThread.Post(() =>
            {
                if (command.StateRequestVersion == _stateRequestVersion)
                    _requestedWindowState = null;
            }, DispatcherPriority.Input);
        if (command.Operation == OpenHarmonyChildWindowOperation.Move)
            Dispatcher.UIThread.Post(() =>
            {
                if (_requestedPosition == command.Position)
                    _requestedPosition = null;
            }, DispatcherPriority.Input);
        if (accepted)
            return;
        if (IsStage && command.Operation == OpenHarmonyChildWindowOperation.Resize)
            Dispatcher.UIThread.Post(() =>
            {
                if (_requestedStageClientSize == command.StageClientSize)
                    _requestedStageClientSize = null;
            }, DispatcherPriority.Input);

        if (!IsStage && command.Operation is (OpenHarmonyChildWindowOperation.Show or OpenHarmonyChildWindowOperation.Hide))
            Dispatcher.UIThread.Post(() => CloseAfterFailure($"Could not {command.Operation.ToString().ToLowerInvariant()} HarmonyOS window {Id}."));
        else
            _reportError($"HarmonyOS rejected {command.Operation} for window {Id}.");
    }

    private async Task CloseNativeAfterAsync(Task previous, bool titleBarClose)
    {
        await previous.ConfigureAwait(false);
        // Give windowWillClose time to return and the system time to send WINDOW_DESTROYED.
        if (titleBarClose)
            await Task.Delay(NativeCloseGrace).ConfigureAwait(false);
        foreach (var delay in CloseRetryDelays)
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay).ConfigureAwait(false);
            if (_disposed)
                return;
            if (await SendAsync(new(Id, OpenHarmonyChildWindowOperation.Close)).ConfigureAwait(false))
            {
                Dispatcher.UIThread.Post(FinishClose, DispatcherPriority.Input);
                return;
            }
        }
        Dispatcher.UIThread.Post(() =>
        {
            _closeAttemptInProgress = false;
            if (!_disposed)
                _reportError($"HarmonyOS window {Id} did not confirm destruction. Close can be requested again.");
        }, DispatcherPriority.Input);
    }

    private async Task<bool> SendAsync(OpenHarmonyChildWindowRequest command)
    {
        try
        {
            var pending = _request(command);
            if (command.Operation == OpenHarmonyChildWindowOperation.Close)
                pending = pending.WaitAsync(CloseRequestTimeout);
            return await pending.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            _reportError($"HarmonyOS window {Id} {command.Operation}: {error.Message}");
            return false;
        }
    }

    private void CloseAfterFailure(string message)
    {
        if (_closeRequested || _disposed)
            return;
        _reportError(message);
        Dispose();
    }
}
