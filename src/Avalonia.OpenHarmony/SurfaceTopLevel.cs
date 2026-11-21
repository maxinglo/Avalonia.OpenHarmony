using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Input.TextInput;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Platform.Surfaces;
using Avalonia.Rendering.Composition;

namespace Avalonia.OpenHarmony;

internal sealed unsafe class SurfaceTopLevel : ITopLevelImpl, IFramebufferPlatformSurface
{
    private const long MaximumPixels = 16_777_216;
    private readonly OpenHarmonyHost.PresentFrame _present;
    private readonly TouchDevice _touch = new();
    private readonly IKeyboardDevice _keyboard = AvaloniaLocator.Current.GetRequiredService<IKeyboardDevice>();
    private readonly NativeTextInput? _textInput;
    private readonly ILauncher? _launcher;
    private readonly IStorageProvider? _storageProvider;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IInputRoot? _inputRoot;
    private PixelSize _pixelSize;
    private IntPtr _pixels;

    public SurfaceTopLevel(long surface, Compositor compositor, OpenHarmonyHost.PresentFrame present,
        Func<int>? getInputWindowId, Action<string> reportError, ILauncher? launcher, IStorageProvider? storageProvider)
    {
        Surface = surface;
        Compositor = compositor;
        _present = present;
        _launcher = launcher;
        _storageProvider = storageProvider;
        if (getInputWindowId is not null)
            _textInput = new(this, getInputWindowId, reportError);
        Surfaces = [this];
    }

    public long Surface { get; }
    public Compositor Compositor { get; }
    public IPlatformRenderSurface[] Surfaces { get; }
    public Size ClientSize => new(_pixelSize.Width / RenderScaling, _pixelSize.Height / RenderScaling);
    public double RenderScaling { get; private set; } = 1;
    public double DesktopScaling => RenderScaling;
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
        if (_pixelSize != size)
        {
            var newBuffer = (IntPtr)NativeMemory.AllocZeroed(checked((nuint)width * (nuint)height * 4));
            NativeMemory.Free((void*)_pixels);
            _pixels = newBuffer;
            _pixelSize = size;
        }
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
        Input?.Invoke(new RawTouchEventArgs(_touch, (ulong)_clock.ElapsedMilliseconds, _inputRoot, kind,
            new Point(x / RenderScaling, y / RenderScaling), RawInputModifiers.None, contact));
    }

    public void SetInputRoot(IInputRoot inputRoot) => _inputRoot = inputRoot;

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
    public Point PointToClient(PixelPoint point) => point.ToPoint(RenderScaling);
    public PixelPoint PointToScreen(Point point) => PixelPoint.FromPoint(point, RenderScaling);
    public void SetCursor(ICursorImpl? cursor) { }
    public IPopupImpl? CreatePopup() => null;
    public void SetTransparencyLevelHint(IReadOnlyList<WindowTransparencyLevel> levels) { }
    public void SetFrameThemeVariant(PlatformThemeVariant? themeVariant) { }
    public object? TryGetFeature(Type featureType)
    {
        if (featureType == typeof(ITextInputMethodImpl))
            return _textInput;
        if (featureType == typeof(ILauncher))
            return _launcher;
        if (featureType == typeof(IStorageProvider))
            return _storageProvider;
        return null;
    }

    public void Dispose()
    {
        _textInput?.SetClient(null);
        NativeMemory.Free((void*)_pixels);
        _pixels = IntPtr.Zero;
        _touch.Dispose();
        Closed?.Invoke();
    }
}
