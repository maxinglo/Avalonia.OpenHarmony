using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Input.TextInput;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Platform.Surfaces;
using Avalonia.Rendering.Composition;

namespace Avalonia.OpenHarmony;

internal sealed unsafe partial class SurfaceTopLevel : ITopLevelImpl, IFramebufferPlatformSurface
{
    private const long MaximumPixels = 16_777_216;
    private readonly OpenHarmonyHost.PresentFrame _present;
    private readonly Action<string> _reportError;
    private readonly TouchDevice _touch = new();
    private readonly IKeyboardDevice _keyboard = AvaloniaLocator.Current.GetRequiredService<IKeyboardDevice>();
    private readonly NativeTextInput? _textInput;
    private readonly ILauncher? _launcher;
    private readonly IStorageProvider? _storageProvider;
    private readonly OpenHarmonyCursorController? _cursorController;
    private readonly OpenHarmonyScreens _screens;
    private readonly OpenHarmonyNavigationManager _navigation = new();
    private readonly OpenHarmonyEgl.Window? _eglWindow;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IInputRoot? _inputRoot;
    private PixelSize _pixelSize;
    private IntPtr _pixels;

    public SurfaceTopLevel(long surface, Compositor compositor, OpenHarmonyHost.PresentFrame present,
        Func<int>? getInputWindowId, Action<string> reportError, OpenHarmonyScreens screens,
        ILauncher? launcher, IStorageProvider? storageProvider, OpenHarmonyEgl.Window? eglWindow)
    {
        Surface = surface;
        Compositor = compositor;
        _present = present;
        _reportError = reportError;
        _launcher = launcher;
        _storageProvider = storageProvider;
        _screens = screens;
        _eglWindow = eglWindow;
        if (getInputWindowId is not null)
        {
            _textInput = new(this, getInputWindowId, reportError);
            _cursorController = new(getInputWindowId);
        }
        Surfaces = eglWindow is null ? [this] : [eglWindow];
    }

    public long Surface { get; }
    internal TopLevel? ControlRoot => _inputRoot?.FocusRoot as TopLevel;
    public Compositor Compositor { get; }
    public IPlatformRenderSurface[] Surfaces { get; }
    public bool UsesEgl => _eglWindow is not null;
    public PixelSize PixelSize => _pixelSize;
    public Size ClientSize => new(_pixelSize.Width / RenderScaling, _pixelSize.Height / RenderScaling);
    internal OpenHarmonyInsetsManager Insets { get; } = new();
    internal OpenHarmonyInputPane InputPane { get; } = new();
    public double RenderScaling { get; private set; } = 1;
    public double DesktopScaling => RenderScaling;
    internal PixelPoint ScreenOrigin { get; set; }
    public IPlatformHandle? Handle => null;
    public Action<RawInputEventArgs>? Input { get; set; }
    public Action<Rect>? Paint { get; set; }
    public Action<Size, WindowResizeReason>? Resized { get; set; }
    public Action<double>? ScalingChanged { get; set; }
    public Action<WindowTransparencyLevel>? TransparencyLevelChanged { get; set; }
    public Action? Closed { get; set; }
    public Action? LostFocus { get; set; }
    public WindowTransparencyLevel TransparencyLevel => WindowTransparencyLevel.None;
    public AcrylicPlatformCompensationLevels AcrylicCompensationLevels => default;

    public void Resize(int width, int height, double scaling)
    {
        if ((long)width * height > MaximumPixels || !double.IsFinite(scaling) || scaling <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Invalid framebuffer geometry.");
        var size = new PixelSize(width, height);
        if (_pixelSize == size && RenderScaling == scaling)
            return;
        if (_pixelSize != size && _eglWindow is null)
        {
            var newBuffer = (IntPtr)NativeMemory.AllocZeroed(checked((nuint)width * (nuint)height * 4));
            NativeMemory.Free((void*)_pixels);
            _pixels = newBuffer;
        }
        _pixelSize = size;
        _eglWindow?.Resize(size, scaling);
        if (RenderScaling != scaling)
        {
            RenderScaling = scaling;
            ScalingChanged?.Invoke(RenderScaling);
        }
        Resized?.Invoke(ClientSize, WindowResizeReason.Unspecified);
        Paint?.Invoke(new Rect(ClientSize));
    }

    public IFramebufferRenderTarget CreateFramebufferRenderTarget() =>
        new FuncFramebufferRenderTarget(
            () =>
            {
                var size = _pixelSize;
                var pixels = _pixels;
                var stride = checked(size.Width * 4);
                return new LockedFramebuffer(pixels, size, stride, new Vector(96 * RenderScaling, 96 * RenderScaling),
                    PixelFormat.Rgba8888, AlphaFormat.Premul,
                    () => _present(Surface, pixels, size.Width, size.Height, stride));
            });

    public void Touch(long contact, TouchPhase phase, double x, double y)
    {
        if (_inputRoot == null || !double.IsFinite(x) || !double.IsFinite(y))
            return;
        var kind = phase switch
        {
            TouchPhase.Begin => RawPointerEventType.TouchBegin,
            TouchPhase.Move => RawPointerEventType.TouchUpdate,
            TouchPhase.End => RawPointerEventType.TouchEnd,
            _ => RawPointerEventType.TouchCancel
        };
        var raw = new RawTouchEventArgs(_touch, (ulong)_clock.ElapsedMilliseconds, _inputRoot, kind,
            new Point(x / RenderScaling, y / RenderScaling), RawInputModifiers.None, contact);
        if (phase == TouchPhase.Begin && contact < 0)
            raw.PlatformInputEventCookie = new OpenHarmonyTextDragGesture(contact);
        Input?.Invoke(raw);
    }

    public void SetInputRoot(IInputRoot inputRoot) => _inputRoot = inputRoot;
    internal void SetInputMethodActive(bool active) => _textInput?.SetInputMethodActive(active);
    internal bool RequestBack() => _navigation.RequestBack();

    internal void InsertText(string text)
    {
        if (_inputRoot is { } root)
            Input?.Invoke(new RawTextInputEventArgs(_keyboard, (ulong)_clock.ElapsedMilliseconds, root, text));
    }

    internal void PressKey(Key key)
    {
        if (_inputRoot is not { } root)
            return;
        var time = (ulong)_clock.ElapsedMilliseconds;
        Input?.Invoke(new RawKeyEventArgs(_keyboard, time, root, RawKeyEventType.KeyDown, key,
            RawInputModifiers.None, PhysicalKey.None, null));
        Input?.Invoke(new RawKeyEventArgs(_keyboard, time, root, RawKeyEventType.KeyUp, key,
            RawInputModifiers.None, PhysicalKey.None, null));
    }
    public Point PointToClient(PixelPoint point) => (point - ScreenOrigin).ToPoint(RenderScaling);
    public PixelPoint PointToScreen(Point point) => PixelPoint.FromPoint(point, RenderScaling) + ScreenOrigin;
    public void SetCursor(ICursorImpl? cursor) =>
        _cursorController?.Apply((cursor as OpenHarmonyCursor)?.Kind ?? StandardCursorType.Arrow);
    public IPopupImpl? CreatePopup() => null;
    public void SetTransparencyLevelHint(IReadOnlyList<WindowTransparencyLevel> levels) { }
    public void SetFrameThemeVariant(PlatformThemeVariant? themeVariant) { }
    public object? TryGetFeature(Type featureType)
    {
        if (featureType == typeof(IClipboard))
            return OpenHarmonyClipboard.Instance;
        if (featureType == typeof(ITextInputMethodImpl))
            return _textInput;
        if (featureType == typeof(ILauncher))
            return _launcher;
        if (featureType == typeof(IStorageProvider))
            return _storageProvider;
        if (featureType == typeof(ISystemNavigationManagerImpl))
            return _navigation;
        if (featureType == typeof(IScreenImpl))
            return _screens;
        if (featureType == typeof(IInsetsManager))
            return Insets;
        if (featureType == typeof(IInputPane))
            return InputPane;
        return null;
    }

    public void Dispose()
    {
        CancelInboundDrag();
        Insets.Dispose();
        _textInput?.Dispose();
        _eglWindow?.Dispose();
        NativeMemory.Free((void*)_pixels);
        _pixels = IntPtr.Zero;
        _touch.Dispose();
        _mouse.Dispose();
        Closed?.Invoke();
    }
}
