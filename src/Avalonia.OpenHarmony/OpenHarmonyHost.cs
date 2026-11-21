using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Embedding;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
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
    private bool _stopped;
    private EmbeddableControlRoot? _root;
    private SurfaceTopLevel? _topLevel;

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
        IStorageProvider? storageProvider)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _present = present ?? throw new ArgumentNullException(nameof(present));
        _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        _getInputWindowId = getInputWindowId;
        _launcher = launcher;
        _storageProvider = storageProvider;
        _settings = new OpenHarmonyPlatformSettings(initialTheme);
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
        if (_root?.FocusManager?.GetFocusedElement() is TextBox { IsReadOnly: false } textBox)
            textBox.Paste();
    });

    public void UpdateSurface(long surface, int width, int height, double scaling) =>
        Post(() =>
        {
            if (surface <= 0 || width <= 0 || height <= 0)
                return;
            if (_topLevel?.Surface != surface)
            {
                if (_topLevel != null && _topLevel.Surface > surface)
                    return;

                CloseSurface();
                var compositor = AvaloniaLocator.Current.GetRequiredService<Compositor>();
                _topLevel = new SurfaceTopLevel(surface, compositor, _present, _getInputWindowId, _reportError,
                    _screens, _launcher, _storageProvider);
                try
                {
                    _topLevel.Resize(width, height, scaling);
                    _root = new EmbeddableControlRoot(_topLevel);
                    _root.Content = _lifetime.MainView;
                    _root.Prepare();
                    UpdateSurfaceOrigin();
                    RefreshRendering();
                }
                catch
                {
                    CloseSurface();
                    throw;
                }
                return;
            }
            _topLevel.Resize(width, height, scaling);
        });

    public void DestroySurface(long surface) =>
        Post(() =>
        {
            if (_topLevel?.Surface == surface)
                CloseSurface();
        });

    private void CloseSurface()
    {
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
    }

    public void Touch(long surface, long contact, TouchPhase phase, double x, double y) =>
        Post(() =>
        {
            if (_topLevel?.Surface == surface)
                _topLevel.Touch(contact, phase, x, y);
        });

    /// <summary>Queues hardware pointer input. Position is in physical pixels and wheel deltas are in Avalonia wheel units.</summary>
    public void HardwarePointer(long surface, int phase, int button, double x, double y,
        double wheelX, double wheelY, int modifiers) =>
        Post(() =>
        {
            if (_topLevel?.Surface == surface)
                _topLevel.HardwarePointer(phase, button, x, y, wheelX, wheelY, (RawInputModifiers)modifiers);
        });

    /// <summary>Queues an ArkUI hardware key and reports whether its key code or printable text is supported.</summary>
    public bool HardwareKey(long surface, bool down, int keyCode, int modifiers, int unicode, int locks)
    {
        var key = OpenHarmonyKeyInterop.Map(keyCode, (locks & 1) != 0);
        var keyModifiers = (RawInputModifiers)modifiers & RawInputModifiers.KeyboardMask;
        var text = OpenHarmonyKeyInterop.Text(key, unicode, keyModifiers);
        if (key == default && text == null)
            return false;

        Post(() =>
        {
            if (_topLevel?.Surface == surface)
                _topLevel.HardwareKey(down, key, keyModifiers, text);
        });
        return true;
    }

    public void HardwareInputBlur(long surface) =>
        Post(() =>
        {
            if (_topLevel?.Surface == surface)
            {
                _topLevel.CancelHardwarePointer();
                _root?.FocusManager.Focus(null);
            }
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
    }

    private void InitializePlatform()
    {
        var locator = AvaloniaLocator.CurrentMutable;
        locator.Bind<IPlatformSettings>().ToConstant(_settings);
        locator.Bind<IActivatableLifetime>().ToConstant(_lifetime);
        locator.Bind<IKeyboardDevice>().ToSingleton<KeyboardDevice>();
        locator.Bind<PlatformHotkeyConfiguration>().ToSingleton<PlatformHotkeyConfiguration>();
        locator.Bind<ICursorFactory>().ToSingleton<CursorFactory>();
        locator.Bind<IRenderLoop>().ToConstant(RenderLoop.FromTimer(new UiThreadRenderTimer(60)));
        locator.Bind<IWindowingPlatform>().ToConstant(new WindowingPlatform(this));
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

    private sealed class WindowingPlatform(OpenHarmonyHost host) : IWindowingPlatform
    {
        public IWindowImpl CreateWindow() => throw new PlatformNotSupportedException("OpenHarmony currently hosts one embedded view.");
        public IWindowImpl CreateEmbeddableWindow() => CreateWindow();
        public ITopLevelImpl CreateEmbeddableTopLevel() =>
            host._topLevel ?? throw new InvalidOperationException("The XComponent surface is not available.");
        public ITrayIconImpl? CreateTrayIcon() => null;
        public void GetWindowsZOrder(ReadOnlySpan<IWindowImpl> windows, Span<long> zOrder) => zOrder.Clear();
    }

    private sealed class CursorFactory : ICursorFactory
    {
        public ICursorImpl GetCursor(StandardCursorType cursorType) => new OpenHarmonyCursor(cursorType);
        public ICursorImpl CreateCursor(Bitmap cursor, PixelPoint hotSpot) => new OpenHarmonyCursor(StandardCursorType.Arrow);
    }
}

public enum TouchPhase
{
    Begin,
    Move,
    End,
    Cancel
}
