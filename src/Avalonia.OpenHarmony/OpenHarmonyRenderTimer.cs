using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Rendering;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

internal sealed unsafe class OpenHarmonyRenderTimer : IRenderTimer, IDisposable
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FallbackInterval = TimeSpan.FromSeconds(1.0 / 60);
    private static readonly ConcurrentDictionary<nint, OpenHarmonyRenderTimer> Pending = new();
    private static long s_requestId;
    private static readonly Lazy<NativeVSync?> Native = new(NativeVSync.TryLoad);

    private readonly Dispatcher _dispatcher;
    private readonly Action<string> _report;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private volatile Action<TimeSpan>? _tick;
    private nint _vsync;
    private nint _pendingRequest;
    private long _firstNativeTimestamp;
    private TimeSpan _firstNativeElapsed;
    private TimeSpan _lastTick;
    private bool _fallback;
    private bool _disposed;

    public OpenHarmonyRenderTimer(Dispatcher dispatcher, Action<string> report)
    {
        _dispatcher = dispatcher;
        _report = report;
        _timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher);
        _timer.Tick += OnTimer;
    }

    public bool RunsInBackground => false;

    public Action<TimeSpan>? Tick
    {
        get => _tick;
        set
        {
            _tick = value;
            _dispatcher.Post(Update, DispatcherPriority.Render);
        }
    }

    private void Update()
    {
        if (_disposed)
            return;

        if (_tick is null)
        {
            CancelRequest();
            _timer.Stop();
            return;
        }

        if (_fallback)
        {
            _timer.Start();
            return;
        }

        if (_pendingRequest != 0)
            return;

        if (Native.Value is not { } native)
        {
            UseFallback();
            return;
        }

        _vsync = _vsync != 0 ? _vsync : native.Create();
        if (_vsync == 0)
        {
            UseFallback("could not create a connection");
            return;
        }

        // A late callback gets an opaque request ID so it cannot access a freed managed handle.
        _pendingRequest = (nint)Interlocked.Increment(ref s_requestId);
        Pending[_pendingRequest] = this;
        var status = native.RequestFrame(_vsync, &OnNativeFrame, _pendingRequest);
        if (status != 0)
        {
            UseFallback($"frame request failed ({status})");
            return;
        }

        _timer.Interval = FrameTimeout;
        _timer.Start();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNativeFrame(long timestamp, nint request)
    {
        if (Pending.TryRemove(request, out var timer))
        {
            var elapsed = timer._clock.Elapsed;
            timer._dispatcher.Post(() => timer.DeliverNativeFrame(request, timestamp, elapsed), DispatcherPriority.Render);
        }
    }

    private void DeliverNativeFrame(nint request, long timestamp, TimeSpan elapsed)
    {
        if (_pendingRequest != request)
            return;

        _pendingRequest = 0;
        _timer.Stop();
        if (_tick is null)
            return;

        if (timestamp <= 0)
        {
            UseFallback("received an invalid frame timestamp");
            return;
        }

        if (_firstNativeTimestamp == 0)
        {
            _firstNativeTimestamp = timestamp;
            _firstNativeElapsed = elapsed;
            _report("OpenHarmony NativeVSync received its first frame");
        }

        DeliverTick(_firstNativeElapsed + TimeSpan.FromTicks((timestamp - _firstNativeTimestamp) / 100));
        Update();
    }

    private void OnTimer(object? sender, EventArgs args)
    {
        if (_tick is null)
        {
            Update();
            return;
        }

        if (!_fallback)
            UseFallback("timed out waiting for a frame");

        DeliverTick(_clock.Elapsed);
    }

    private void DeliverTick(TimeSpan timestamp)
    {
        if (timestamp > _lastTick)
            _lastTick = timestamp;
        _tick?.Invoke(_lastTick);
    }

    private void UseFallback(string? reason = null)
    {
        _fallback = true;
        ReleaseNativeVSync();
        _timer.Stop();
        _timer.Interval = FallbackInterval;
        if (_tick is not null)
            _timer.Start();
        if (reason is not null)
            _report($"OpenHarmony NativeVSync {reason}. Using the UI render timer.");
    }

    private void CancelRequest()
    {
        if (_pendingRequest != 0)
        {
            Pending.TryRemove(_pendingRequest, out _);
            _pendingRequest = 0;
        }
    }

    private void ReleaseNativeVSync()
    {
        CancelRequest();
        if (_vsync != 0)
        {
            Native.Value!.Destroy(_vsync);
            _vsync = 0;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _tick = null;
        _timer.Stop();
        ReleaseNativeVSync();
    }

    private sealed class NativeVSync(nint create, nint destroy, nint requestFrame)
    {
        public nint Create()
        {
            var name = "Avalonia"u8;
            fixed (byte* pointer = name)
                return ((delegate* unmanaged[Cdecl]<byte*, uint, nint>)create)(pointer, (uint)name.Length);
        }

        public void Destroy(nint connection) => ((delegate* unmanaged[Cdecl]<nint, void>)destroy)(connection);

        public int RequestFrame(nint connection, delegate* unmanaged[Cdecl]<long, nint, void> callback, nint request) =>
            ((delegate* unmanaged[Cdecl]<nint, delegate* unmanaged[Cdecl]<long, nint, void>, nint, int>)requestFrame)
                (connection, callback, request);

        public static NativeVSync? TryLoad()
        {
            if (!NativeLibrary.TryLoad("libnative_vsync.so", out var library))
                return null;

            if (NativeLibrary.TryGetExport(library, "OH_NativeVSync_Create", out var create) &&
                NativeLibrary.TryGetExport(library, "OH_NativeVSync_Destroy", out var destroy) &&
                NativeLibrary.TryGetExport(library, "OH_NativeVSync_RequestFrame", out var requestFrame))
                return new(create, destroy, requestFrame);

            NativeLibrary.Free(library);
            return null;
        }
    }
}
