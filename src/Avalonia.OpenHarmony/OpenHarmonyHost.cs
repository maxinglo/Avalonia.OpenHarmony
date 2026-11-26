using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Embedding;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

/// <summary>Hosts one Avalonia view on the application UI dispatcher and an external framebuffer.</summary>
public sealed partial class OpenHarmonyHost
{
    public delegate void PresentFrame(long surface, IntPtr pixels, int width, int height, int rowBytes);

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _dispatchLock = new();
    private readonly Queue<Action> _pending = new();
    private readonly PresentFrame _present;
    private readonly Action<string> _reportError;
    private readonly Func<int>? _getInputWindowId;
    private readonly ILauncher? _launcher;
    private readonly IStorageProvider? _storageProvider;
    private readonly SingleViewLifetime _lifetime = new();
    private Dispatcher? _dispatcher;
    private OpenHarmonyRenderTimer? _renderTimer;
    private bool _stopped;
    private EmbeddableControlRoot? _root;
    private SurfaceTopLevel? _topLevel;
    private OpenHarmonyEgl? _egl;
    private Compositor? _eglCompositor;
    private bool _eglUnavailable;

    public OpenHarmonyHost(Func<AppBuilder> configure, PresentFrame present, Action<string> reportError)
        : this(configure, present, reportError, null)
    {
    }

    public OpenHarmonyHost(Func<AppBuilder> configure, PresentFrame present, Action<string> reportError,
        Func<int>? getInputWindowId)
        : this(configure, present, reportError, getInputWindowId, PlatformThemeVariant.Light)
    {
    }

    public OpenHarmonyHost(Func<AppBuilder> configure, PresentFrame present, Action<string> reportError,
        Func<int>? getInputWindowId, ILauncher? launcher, IStorageProvider? storageProvider)
        : this(configure, present, reportError, getInputWindowId, PlatformThemeVariant.Light, launcher, storageProvider)
    {
    }

    public OpenHarmonyHost(Func<AppBuilder> configure, PresentFrame present, Action<string> reportError,
        Func<int>? getInputWindowId, PlatformThemeVariant initialTheme)
        : this(configure, present, reportError, getInputWindowId, initialTheme, null, null)
    {
    }

    public OpenHarmonyHost(Func<AppBuilder> configure, PresentFrame present, Action<string> reportError,
        Func<int>? getInputWindowId, PlatformThemeVariant initialTheme, ILauncher? launcher,
        IStorageProvider? storageProvider, uint? initialAccent = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _present = present ?? throw new ArgumentNullException(nameof(present));
        _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        _getInputWindowId = getInputWindowId;
        _launcher = launcher;
        _storageProvider = storageProvider;
        _settings = new OpenHarmonyPlatformSettings(initialTheme, initialAccent);
        new Thread(() => Run(configure))
        {
            IsBackground = true,
            Name = "Avalonia OpenHarmony UI"
        }.Start();
    }

    public Task Ready => _ready.Task;

    /// <summary>Pastes into the focused Avalonia text box after a successful ArkUI PasteButton authorization.</summary>
    public void PasteToFocusedTextBox() => Post(() =>
    {
        if (FocusedWindowElement() is TextBox { IsReadOnly: false } textBox)
            textBox.Paste();
    });

    public void UpdateSurface(long surface, int width, int height, double scaling) =>
        Post(() =>
        {
            if (surface <= 0 || width <= 0 || height <= 0)
                return;
            if (AvaloniaLocator.Current.GetService<OpenHarmonyWindowOptions>() is { } windowOptions)
            {
                UpdateStageSurface(windowOptions, surface, width, height, scaling);
                return;
            }
            if (_topLevel?.Surface != surface)
            {
                if (_topLevel != null && _topLevel.Surface > surface)
                    return;

                CloseSurface();
                OpenSurface(surface, width, height, scaling);
                return;
            }
            _topLevel.Resize(width, height, scaling);
            RefreshWindowInsets();
        });

    private void OpenSurface(long surface, int width, int height, double scaling)
    {
        var compositor = AvaloniaLocator.Current.GetRequiredService<Compositor>();
        OpenHarmonyEgl.Window? eglWindow = null;
        if (!_eglUnavailable && AvaloniaLocator.Current.GetService<OpenHarmonyRenderingOptions>()?.UseEgl == true)
        {
            try
            {
                _egl ??= new OpenHarmonyEgl();
                eglWindow = _egl.AcquireWindow(surface, new PixelSize(width, height), scaling,
                    error => Post(() => FallBackToSoftware(surface, error)));
                compositor = _eglCompositor ??= new Compositor(_egl.Graphics);
            }
            catch (Exception error)
            {
                eglWindow?.Dispose();
                eglWindow = null;
                ReportSoftwareFallback(error);
            }
        }

        try
        {
            _topLevel = new SurfaceTopLevel(surface, compositor, _present, _getInputWindowId, _reportError,
                _screens, _launcher, _storageProvider, eglWindow);
            _topLevel.SetInputMethodActive(IsInputMethodWindowActive);
            if (_requestInsetsCommand is { } command)
                _topLevel.Insets.SetRequestHandler(command, _reportError);
            _topLevel.Resize(width, height, scaling);
            UpdateSurfaceOrigin();
            RefreshWindowInsets();
            _root = new EmbeddableControlRoot(_topLevel);
            _root.Content = _lifetime.MainView;
            _root.Prepare();
            RefreshRendering();
        }
        catch
        {
            CloseSurface();
            eglWindow?.Dispose();
            throw;
        }
    }

    private void FallBackToSoftware(long surface, Exception error)
    {
        if (_topLevel is not { UsesEgl: true } topLevel || topLevel.Surface != surface)
            return;

        ReportSoftwareFallback(error);
        var size = topLevel.PixelSize;
        var scaling = topLevel.RenderScaling;
        var offset = _surfaceOffset;
        var hasOffset = _hasSurfaceOffset;
        CloseSurface();
        _surfaceOffset = offset;
        _hasSurfaceOffset = hasOffset;
        OpenSurface(surface, size.Width, size.Height, scaling);
    }

    private void ReportSoftwareFallback(Exception error)
    {
        _eglUnavailable = true;
        _reportError($"OpenHarmony EGL initialization or rendering failed. Using software: {error.Message}");
    }

    public void DestroySurface(long surface) =>
        Post(() =>
        {
            if (!DetachStageSurface(surface) && _topLevel?.Surface == surface)
                CloseSurface();
        });

    private void CloseSurface()
    {
        if (_topLevel is { } topLevel)
        {
            CancelInboundDrag(topLevel.Surface);
            CloseAccessibility(topLevel.Surface);
        }
        if (_root != null)
        {
            if (_rendering)
                _root.StopRendering();
            _root.Content = null;
            _root.Dispose();
        }
        else
        {
            _topLevel?.Dispose();
        }
        _root = null;
        _topLevel = null;
        _rendering = false;
        _surfaceOffset = default;
        _hasSurfaceOffset = false;
    }

    public void Touch(long surface, long contact, TouchPhase phase, double x, double y) =>
        Post(() => FindInputSurface(surface)?.Touch(contact, phase, x, y));

    /// <summary>Queues touch or pen input with the physical position and ArkUI pointer details.</summary>
    public void Touch(long surface, long contact, TouchPhase phase, double x, double y, bool pen, int modifiers,
        double pressure, double tiltX, double tiltY) => Post(() =>
    {
        if (FindInputSurface(surface) is not { } target)
            return;
        if (pen)
            target.Pen(contact, phase, x, y, (RawInputModifiers)modifiers, pressure, tiltX, tiltY);
        else if (phase <= TouchPhase.Cancel)
            target.Touch(contact, phase, x, y, (RawInputModifiers)modifiers);
    });

    /// <summary>Queues hardware pointer input. Position is in physical pixels and wheel deltas are in Avalonia wheel units.</summary>
    public void HardwarePointer(long surface, int phase, int button, double x, double y,
        double wheelX, double wheelY, int modifiers) =>
        HardwarePointer(surface, phase, button, x, y, wheelX, wheelY, modifiers, 0);

    /// <summary>Queues hardware pointer input with an ArkUI primary mouse-press drag token when available.</summary>
    public void HardwarePointer(long surface, int phase, int button, double x, double y,
        double wheelX, double wheelY, int modifiers, long dragGesture) =>
        Post(() => FindInputSurface(surface)?.HardwarePointer(phase, button, x, y, wheelX, wheelY,
            (RawInputModifiers)modifiers, dragGesture));

    /// <summary>Queues an ArkUI hardware key and reports whether its key code or printable text is supported.</summary>
    public bool HardwareKey(long surface, bool down, int keyCode, int modifiers, int unicode, int locks)
    {
        var key = OpenHarmonyKeyInterop.Map(keyCode, (locks & 1) != 0);
        var keyModifiers = (RawInputModifiers)modifiers & RawInputModifiers.KeyboardMask;
        var text = OpenHarmonyKeyInterop.Text(key, unicode, keyModifiers);
        if (key == default && text == null)
            return false;

        Post(() => FindInputSurface(surface)?.HardwareKey(down, key, keyModifiers, text));
        return true;
    }

    public void HardwareInputBlur(long surface) =>
        Post(() =>
        {
            var target = FindInputSurface(surface);
            if (target is null)
                return;
            target.CancelHardwarePointer();
            target.CancelPenInput();
            if (_root is { } root && target == _topLevel)
                root.FocusManager.Focus(null);
            else
                target.LostFocus?.Invoke();
        });

    private void Run(Func<AppBuilder> configure)
    {
        try
        {
            var dispatcher = Dispatcher.UIThread;
            Dispatcher.InitializeUIThreadDispatcher(new ManagedDispatcherImpl(null));
            _lifetime.MainViewChanged += view =>
            {
                if (_root != null)
                    _root.Content = view;
                if (_stageRoot != null)
                    _stageRoot.Content = view;
            };

            configure()
                .UseStandardRuntimePlatformSubsystem()
                .UseSkia()
                .UseHarfBuzz()
                .UseWindowingSubsystem(InitializePlatform, "OpenHarmony")
                .SetupWithLifetime(_lifetime);

            lock (_dispatchLock)
            {
                _dispatcher = dispatcher;
                while (_pending.TryDequeue(out var action))
                    Dispatch(dispatcher, action);
            }
            _ready.SetResult();
            dispatcher.MainLoop(CancellationToken.None);
        }
        catch (Exception exception)
        {
            lock (_dispatchLock)
            {
                _stopped = true;
                _dispatcher = null;
                _pending.Clear();
            }
            _ready.TrySetException(exception);
            _reportError(exception.ToString());
        }
        finally
        {
            _trayIcon?.Dispose();
            _renderTimer?.Dispose();
        }
    }

    private void InitializePlatform()
    {
        var locator = AvaloniaLocator.CurrentMutable;
        locator.Bind<IPlatformSettings>().ToConstant(_settings);
        locator.Bind<IActivatableLifetime>().ToConstant(_lifetime);
        locator.Bind<IKeyboardDevice>().ToSingleton<KeyboardDevice>();
        locator.Bind<PlatformHotkeyConfiguration>().ToSingleton<PlatformHotkeyConfiguration>();
        if (locator.GetService<OpenHarmonyTextDragOptions>() is { } textDrag)
            locator.Bind<IPlatformDragSource>().ToConstant(new OpenHarmonyTextDragSource(textDrag, _reportError));
        locator.Bind<ICursorFactory>().ToSingleton<OpenHarmonyCursorFactory>();
        locator.Bind<IPlatformIconLoader>().ToSingleton<OpenHarmonyIconLoader>();
        _renderTimer = new OpenHarmonyRenderTimer(Dispatcher.UIThread, _reportError);
        locator.Bind<IRenderLoop>().ToConstant(RenderLoop.FromTimer(_renderTimer));
        locator.Bind<IWindowingPlatform>().ToConstant(new OpenHarmonyChildWindowingPlatform(CreateOwnedWindow,
            () => _topLevel ?? throw new InvalidOperationException("The XComponent surface is not available."),
            CreateTrayIcon));
        var windowedEgl = locator.GetService<OpenHarmonyWindowOptions>() is not null &&
            locator.GetService<OpenHarmonyRenderingOptions>()?.UseEgl == true;
        if (windowedEgl)
        {
            var skia = locator.GetRequiredService<IPlatformRenderInterface>();
            locator.Bind<IPlatformRenderInterface>().ToConstant(new OpenHarmonyWindowedSkia(skia));
        }
        locator.Bind<IPlatformGraphics?>().ToFunc(() => windowedEgl
            ? _windowedGraphics is { Enabled: true } ? _egl?.Graphics : null
            : _topLevel is { UsesEgl: true } ? _egl?.Graphics : null);
        locator.Bind<Compositor>().ToConstant(new Compositor(null));
    }

    private void Post(Action action)
    {
        lock (_dispatchLock)
        {
            if (_stopped)
                return;
            if (_dispatcher is { } dispatcher)
                Dispatch(dispatcher, action);
            else
                _pending.Enqueue(action);
        }
    }

    private void Dispatch(Dispatcher dispatcher, Action action) =>
        dispatcher.Post(() =>
        {
            try { action(); }
            catch (Exception exception) { _reportError(exception.ToString()); }
        }, DispatcherPriority.Input);

    private sealed class SingleViewLifetime : ISingleViewApplicationLifetime, IActivatableLifetime
    {
        private Control? _mainView;
        private bool? _reportedForeground;
        public event Action<Control?>? MainViewChanged;
        public event EventHandler<ActivatedEventArgs>? Activated;
        public event EventHandler<ActivatedEventArgs>? Deactivated;
        public Control? MainView
        {
            get => _mainView;
            set
            {
                _mainView = value;
                MainViewChanged?.Invoke(value);
            }
        }

        public void ReportForeground(bool foreground)
        {
            if (_reportedForeground == foreground)
                return;
            _reportedForeground = foreground;
            var args = new ActivatedEventArgs(ActivationKind.Background);
            if (foreground)
                Activated?.Invoke(this, args);
            else
                Deactivated?.Invoke(this, args);
        }

        public bool TryEnterBackground() => false;
        public bool TryLeaveBackground() => false;
    }

}

public enum TouchPhase
{
    Begin,
    Move,
    End,
    Cancel,
    PenHover,
    PenLeave
}
