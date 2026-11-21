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

    public Window AcquireWindow(long token, PixelSize size, double scaling, Action<Exception> fallback)
    {
        var handle = AcquireNativeWindow(token);
        if (handle == 0)
            throw new InvalidOperationException("The XComponent NativeWindow is unavailable for EGL.");

        var window = new Window(new NativeWindowReference(handle), size, scaling, fallback);
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

    internal sealed class Window(NativeWindowReference window, PixelSize size, double scaling, Action<Exception> fallback) :
        IGlPlatformSurface, EglGlPlatformSurface.IEglWindowGlPlatformSurfaceInfoWithWaitPolicy, IDisposable
    {
        private bool _logFirstFrame;
        public PixelSize Size { get; private set; } = size;
        public double Scaling { get; private set; } = scaling;
        public bool IsReady => !window.IsClosed;
        public bool SkipWaits => true;
        public nint Handle => window.DangerousGetHandle();

        public void EnableFrameLogging() => _logFirstFrame = true;

        private void FramePresented(PixelSize frameSize)
        {
            if (!_logFirstFrame)
                return;
            _logFirstFrame = false;
            Report($"Presented Avalonia EGL frame {frameSize.Width}x{frameSize.Height}");
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
                return new RenderTarget(window, target, egl, FramePresented, fallback);
            }
            catch (Exception error)
            {
                if (retained)
                    window.DangerousRelease();
                fallback(error);
                throw;
            }
        }

        public void Dispose() => window.Dispose();

        private sealed class RenderTarget(NativeWindowReference window, IGlPlatformSurfaceRenderTarget target,
            EglInterface egl, Action<PixelSize> presented, Action<Exception> fallback) : IGlPlatformSurfaceRenderTarget
        {
            private bool _disposed;
            public PlatformRenderTargetState State => target.State;

            public IGlPlatformSurfaceRenderingSession BeginDraw(IRenderTarget.RenderTargetSceneInfo sceneInfo)
            {
                try
                {
                    return new RenderingSession(target.BeginDraw(sceneInfo), egl, presented, fallback);
                }
                catch (Exception error)
                {
                    if (!window.IsClosed)
                        fallback(error);
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
                    window.DangerousRelease();
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
