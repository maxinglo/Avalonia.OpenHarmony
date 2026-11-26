using System.Runtime.InteropServices;
using System.Text;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.OpenGL.Surfaces;
using Avalonia.Platform;
using SkiaSharp;

namespace Avalonia.OpenHarmony;

internal sealed class OpenHarmonyEgl
{
    private const int EglSuccess = 0x3000;
    private const int GlFramebufferBinding = 0x8CA6;
    private const uint GlRgba8 = 0x8058;
    private static readonly Lazy<nint> EglLibrary = new(() => NativeLibrary.Load("libEGL.so"));

    public OpenHarmonyEgl()
    {
        Graphics = new EglPlatformGraphics(new EglDisplay(new EglDisplayCreationOptions
        {
            Egl = new EglInterface(GetAddress),
            SupportsMultipleContexts = true,
            GlVersions = [new(GlProfileType.OpenGLES, 3, 0), new(GlProfileType.OpenGLES, 2, 0)]
        }));
    }

    public IPlatformGraphics Graphics { get; }

    internal sealed class WindowedGraphics(IPlatformGraphics egl, Action<Exception> fallback) : IPlatformGraphics
    {
        private int _disabled;
        public bool Enabled => Volatile.Read(ref _disabled) == 0;
        public bool UsesSharedContext => false;
        public IPlatformGraphicsContext GetSharedContext() => throw new NotSupportedException();

        public IPlatformGraphicsContext CreateContext()
        {
            if (Enabled)
            {
                try
                {
                    return new WindowedContext(this, egl.CreateContext());
                }
                catch (Exception error)
                {
                    Disable(error);
                }
            }
            return new WindowedContext(this, null);
        }

        public void Disable(Exception error)
        {
            if (Interlocked.Exchange(ref _disabled, 1) == 0)
                fallback(error);
        }
    }

    internal sealed class WindowedContext(WindowedGraphics graphics, IPlatformGraphicsContext? egl)
        : IPlatformGraphicsContext
    {
        public bool IsLost
        {
            get
            {
                if (egl is null)
                    return false;
                if (!graphics.Enabled && egl is EglContext { IsLost: false } native)
                    native.NotifyContextLost();
                return !graphics.Enabled || egl.IsLost;
            }
        }
        public object? TryGetFeature(Type featureType) => egl?.TryGetFeature(featureType);

        public IDisposable EnsureCurrent()
        {
            if (egl is null)
                return this;
            try
            {
                return egl.EnsureCurrent();
            }
            catch (Exception error) when (error is not PlatformGraphicsContextLostException)
            {
                graphics.Disable(error);
                throw;
            }
        }

        internal IPlatformRenderInterfaceContext CreateBackend(IPlatformRenderInterface skia)
        {
            var software = skia.CreateBackendContext(null);
            if (egl is null)
                return software;
            try
            {
                return new OpenHarmonyWindowedSkia.Backend(skia.CreateBackendContext(egl), software);
            }
            catch (Exception error)
            {
                graphics.Disable(error);
                return software;
            }
        }

        public void Dispose() => egl?.Dispose();
    }

    public Window AcquireWindow(long token, PixelSize size, double scaling, Action<Exception> fallback)
    {
        var handle = AcquireNativeWindow(token);
        if (handle == 0)
            throw new InvalidOperationException("The XComponent NativeWindow is unavailable for EGL.");

        var window = new Window(new NativeWindowReference(handle), token, size, scaling, fallback);
        try
        {
            ValidateWindow(window);
            window.EnableFrameLogging();
            return window;
        }
        catch
        {
            window.Dispose();
            throw;
        }
    }

    private void ValidateWindow(Window window)
    {
        using var context = (IGlContext)Graphics.CreateContext();
        using var current = context.EnsureCurrent();
        using var functions = GRGlInterface.CreateGles(context.GlInterface.GetProcAddress)
            ?? throw new InvalidOperationException("Skia could not resolve OpenGL ES.");
        using var gpu = GRContext.CreateGl(functions)
            ?? throw new InvalidOperationException("Skia could not initialize a GPU context.");
        using var target = window.CreateGlRenderTarget(context);
        using var session = target.BeginDraw(default);
        context.GlInterface.GetIntegerv(GlFramebufferBinding, out var framebuffer);
        using var nativeTarget = new GRBackendRenderTarget(session.Size.Width, session.Size.Height,
            context.SampleCount, context.StencilSize, new GRGlFramebufferInfo((uint)framebuffer, GlRgba8));
        using var skiaSurface = SKSurface.Create(gpu, nativeTarget,
            session.IsYFlipped ? GRSurfaceOrigin.TopLeft : GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
            ?? throw new InvalidOperationException("Skia could not render to the XComponent EGL surface.");
        skiaSurface.Canvas.Clear(SKColors.Transparent);
        gpu.Flush();
    }

    private static unsafe nint GetAddress(string name)
    {
        if (NativeLibrary.TryGetExport(EglLibrary.Value, name, out var symbol))
            return symbol;

        var getProc = (delegate* unmanaged<byte*, nint>)NativeLibrary.GetExport(EglLibrary.Value, "eglGetProcAddress");
        var utf8 = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* pointer = utf8)
            return getProc(pointer);
    }

    [DllImport("libavalonia_ohos_bridge.so", EntryPoint = "avaoh_acquire_egl_window")]
    private static extern nint AcquireNativeWindow(long token);

    [DllImport("libavalonia_ohos_bridge.so", EntryPoint = "avaoh_release_egl_window")]
    private static extern void ReleaseNativeWindow(nint window);

    [DllImport("libavalonia_ohos_bridge.so", EntryPoint = "avaoh_report")]
    private static extern void Report([MarshalAs(UnmanagedType.LPUTF8Str)] string message);

    internal sealed class NativeWindowReference : SafeHandle
    {
        public NativeWindowReference(nint value) : base(0, true) => SetHandle(value);
        public override bool IsInvalid => handle == 0;

        protected override bool ReleaseHandle()
        {
            ReleaseNativeWindow(handle);
            return true;
        }
    }

    internal sealed class Window(NativeWindowReference window, long token, PixelSize size, double scaling,
        Action<Exception> fallback) :
        IGlPlatformSurface, EglGlPlatformSurface.IEglWindowGlPlatformSurfaceInfoWithWaitPolicy, IDisposable
    {
        private bool _disposed;
        private bool _logFirstFrame;
        internal Func<bool>? CanRender { get; set; }
        public PixelSize Size { get; private set; } = size;
        public double Scaling { get; private set; } = scaling;
        public bool IsReady => !_disposed && (CanRender?.Invoke() ?? true);
        public bool SkipWaits => true;
        public nint Handle => window.DangerousGetHandle();

        public void EnableFrameLogging() => _logFirstFrame = true;

        private void FramePresented(PixelSize frameSize)
        {
            if (!_logFirstFrame)
                return;
            _logFirstFrame = false;
            Report($"Presented Avalonia EGL frame {frameSize.Width}x{frameSize.Height} (surface {token})");
        }

        public void Resize(PixelSize newSize, double newScaling)
        {
            Size = newSize;
            Scaling = newScaling;
        }

        public IGlPlatformSurfaceRenderTarget CreateGlRenderTarget(IGlContext context)
        {
            var retained = false;
            try
            {
                window.DangerousAddRef(ref retained);
                var egl = ((EglContext)context).Display.EglInterface;
                var target = new EglGlPlatformSurface(this).CreateGlRenderTarget(context);
                return new RenderTarget(this, target, egl);
            }
            catch (Exception error)
            {
                if (retained)
                    window.DangerousRelease();
                ReportFailure(error);
                throw;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            window.Dispose();
        }

        private void ReleaseRenderTarget() => window.DangerousRelease();

        private void ReportFailure(Exception error)
        {
            if (!_disposed)
                fallback(error);
        }

        private sealed class RenderTarget(Window owner, IGlPlatformSurfaceRenderTarget target,
            EglInterface egl) : IGlPlatformSurfaceRenderTarget
        {
            private bool _disposed;
            public PlatformRenderTargetState State => owner._disposed
                ? PlatformRenderTargetState.Corrupted
                : owner.IsReady ? target.State : PlatformRenderTargetState.NotReadyWillWakeupRenderLoop;

            public IGlPlatformSurfaceRenderingSession BeginDraw(IRenderTarget.RenderTargetSceneInfo sceneInfo)
            {
                try
                {
                    return new RenderingSession(target.BeginDraw(sceneInfo), egl, owner.FramePresented, owner.ReportFailure);
                }
                catch (Exception error)
                {
                    owner.ReportFailure(error);
                    throw;
                }
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                try
                {
                    target.Dispose();
                }
                finally
                {
                    owner.ReleaseRenderTarget();
                }
            }
        }

        private sealed class RenderingSession(IGlPlatformSurfaceRenderingSession session, EglInterface egl,
            Action<PixelSize> presented, Action<Exception> fallback) : IGlPlatformSurfaceRenderingSession
        {
            public IGlContext Context => session.Context;
            public PixelSize Size => session.Size;
            public double Scaling => session.Scaling;
            public bool IsYFlipped => session.IsYFlipped;

            public void Dispose()
            {
                var size = session.Size;
                try
                {
                    egl.GetError();
                    session.Dispose();
                    var result = egl.GetError();
                    if (result != EglSuccess)
                        throw new OpenGlException($"EGL frame presentation failed (0x{result:X}).");
                    presented(size);
                }
                catch (Exception error)
                {
                    fallback(error);
                    throw;
                }
            }
        }
    }
}
