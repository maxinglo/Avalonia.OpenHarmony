using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Avalonia.OpenHarmony;

internal sealed class OpenHarmonyCursor(StandardCursorType kind) : ICursorImpl
{
    public StandardCursorType Kind { get; } = kind;

    public void Dispose() { }
}

internal sealed class OpenHarmonyCursorFactory : ICursorFactory
{
    private const int MaximumDimension = 256;
    private static readonly PixelSize HiddenSize = new(16, 16);
    private static readonly byte[] HiddenPixels = new byte[HiddenSize.Width * HiddenSize.Height * 4];

    public ICursorImpl GetCursor(StandardCursorType cursorType) => cursorType == StandardCursorType.None
        ? new OpenHarmonyBitmapCursor(HiddenPixels, HiddenSize, default)
        : new OpenHarmonyCursor(cursorType);

    public unsafe ICursorImpl CreateCursor(Bitmap bitmap, PixelPoint hotSpot)
    {
        var size = bitmap.PixelSize;
        if (size.Width is <= 0 or > MaximumDimension || size.Height is <= 0 or > MaximumDimension ||
            hotSpot.X < 0 || hotSpot.X >= size.Width || hotSpot.Y < 0 || hotSpot.Y >= size.Height)
            return new OpenHarmonyCursor(StandardCursorType.Arrow);

        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        fixed (byte* address = pixels)
        {
            using var target = new LockedFramebuffer((nint)address, size, stride, bitmap.Dpi,
                PixelFormats.Rgba8888, AlphaFormat.Premul, null);
            bitmap.CopyPixels(target);
        }
        return new OpenHarmonyBitmapCursor(pixels, size, hotSpot);
    }
}

internal sealed class OpenHarmonyBitmapCursor(byte[] pixels, PixelSize size, PixelPoint hotSpot) : ICursorImpl
{
    private readonly object _sync = new();
    private NativeCursor? _native;
    private bool _disposed;

    internal PixelSize Size => size;
    internal PixelPoint HotSpot => hotSpot;

    internal int? Apply(int window)
    {
        lock (_sync)
        {
            if (_disposed)
                return null;
            _native ??= NativeCursor.Create(pixels, size, hotSpot);
            return _native?.Apply(window);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _native?.Dispose();
            _native = null;
        }
    }
}

internal sealed partial class OpenHarmonyCursorController(Func<int> getWindowId, OpenHarmonyInputDiagnostics? diagnostics)
{
    private ICursorImpl? _requested;
    private (int Window, ICursorImpl? Cursor) _last;
    private (int Window, ICursorImpl? Cursor, int? Result) _lastReported;
    private long _lastCustomApply;
    private bool _standardUnavailable;
    private bool _customUnavailable;

    public void SetCursor(ICursorImpl? cursor)
    {
        _requested = cursor;
        Apply();
    }

    public void Invalidate() => _last = default;

    public void RefreshCustomCursor()
    {
        // ArkUI can reset the shape as the pointer crosses native regions. Refresh on mouse input
        // at most once per frame while retaining immediate updates when the Avalonia cursor changes.
        Apply(force: _requested is OpenHarmonyBitmapCursor && !_customUnavailable &&
            Environment.TickCount64 - _lastCustomApply >= 16);
    }

    private void Apply(bool force = false)
    {
        var window = getWindowId();
        var requested = (window, _requested);
        if (window <= 0 || (!force && _last == requested))
            return;
        if (_requested is OpenHarmonyBitmapCursor bitmap && !_customUnavailable)
        {
            try
            {
                _lastCustomApply = Environment.TickCount64;
                var result = bitmap.Apply(window);
                Report(window, result);
                if (result == 0)
                {
                    _last = requested;
                    return;
                }
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
            {
                _customUnavailable = true;
            }
        }

        if (_standardUnavailable)
            return;
        var style = Map((_requested as OpenHarmonyCursor)?.Kind ?? StandardCursorType.Arrow);
        try
        {
            var result = SetPointerStyle(window, style);
            if (_requested is not OpenHarmonyBitmapCursor)
                Report(window, result, style);
            if (result == 0)
                _last = requested;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            _standardUnavailable = true;
        }
    }

    private void Report(int window, int? result, PointerStyle style = default)
    {
        var state = (window, _requested, result);
        if (diagnostics is null || _lastReported == state)
            return;
        _lastReported = state;
        var description = _requested is OpenHarmonyBitmapCursor bitmap
            ? $"bitmap {bitmap.Size.Width}x{bitmap.Size.Height} hotspot=({bitmap.HotSpot.X},{bitmap.HotSpot.Y})"
            : style.ToString();
        diagnostics.Report($"Cursor: window={window} {description} result={result?.ToString() ?? "unavailable"}");
    }

    private static PointerStyle Map(StandardCursorType kind) => kind switch
    {
        StandardCursorType.Ibeam => PointerStyle.Text,
        StandardCursorType.Wait => PointerStyle.Loading,
        StandardCursorType.Cross => PointerStyle.Cross,
        StandardCursorType.UpArrow or StandardCursorType.TopSide => PointerStyle.North,
        StandardCursorType.SizeWestEast => PointerStyle.WestEast,
        StandardCursorType.SizeNorthSouth => PointerStyle.NorthSouth,
        StandardCursorType.SizeAll or StandardCursorType.DragMove => PointerStyle.Move,
        StandardCursorType.No => PointerStyle.Forbidden,
        StandardCursorType.Hand or StandardCursorType.DragLink => PointerStyle.PointingHand,
        StandardCursorType.AppStarting => PointerStyle.Running,
        StandardCursorType.Help => PointerStyle.Help,
        StandardCursorType.BottomSide => PointerStyle.South,
        StandardCursorType.LeftSide => PointerStyle.West,
        StandardCursorType.RightSide => PointerStyle.East,
        StandardCursorType.TopLeftCorner => PointerStyle.NorthWest,
        StandardCursorType.TopRightCorner => PointerStyle.NorthEast,
        StandardCursorType.BottomLeftCorner => PointerStyle.SouthWest,
        StandardCursorType.BottomRightCorner => PointerStyle.SouthEast,
        StandardCursorType.DragCopy => PointerStyle.Copy,
        _ => PointerStyle.Default
    };

    // Values match the SDK's Input_PointerStyle in oh_pointer_style.h.
    private enum PointerStyle
    {
        Default = 0, East = 1, West = 2, South = 3, North = 4,
        WestEast = 5, NorthSouth = 6, NorthEast = 7, NorthWest = 8, SouthEast = 9, SouthWest = 10,
        Cross = 13, Copy = 14, Forbidden = 15, PointingHand = 19, Help = 20, Move = 21,
        Text = 26, Loading = 42, Running = 43
    }

    [LibraryImport("libohinput.so", EntryPoint = "OH_Input_SetPointerStyle")]
    private static partial int SetPointerStyle(int windowId, PointerStyle style);
}

internal sealed unsafe partial class NativeCursor : SafeHandle
{
    private const string InputLibrary = "libohinput.so";
    private const string PixelMapLibrary = "libpixelmap.so";
    private const int Rgba8888 = 3;
    private const int PremultipliedAlpha = 2;
    private nint _pixelMap;
    private nint _config;

    private NativeCursor(nint pixelMap, nint cursor, nint config) : base(nint.Zero, ownsHandle: true)
    {
        _pixelMap = pixelMap;
        _config = config;
        SetHandle(cursor);
    }

    public override bool IsInvalid => handle == nint.Zero;

    internal static NativeCursor? Create(byte[] pixels, PixelSize size, PixelPoint hotSpot)
    {
        nint options = 0, pixelMap = 0, cursor = 0, config = 0;
        try
        {
            if (CreateOptions(out options) != 0 ||
                SetWidth(options, (uint)size.Width) != 0 || SetHeight(options, (uint)size.Height) != 0 ||
                SetPixelFormat(options, Rgba8888) != 0 || SetSourcePixelFormat(options, Rgba8888) != 0 ||
                SetAlphaType(options, PremultipliedAlpha) != 0)
                return null;
            fixed (byte* address = pixels)
            {
                if (CreatePixelMap(address, (nuint)pixels.Length, options, out pixelMap) != 0)
                    return null;
            }
            cursor = CreateCustomCursor(pixelMap, hotSpot.X, hotSpot.Y);
            if (cursor == 0)
                return null;
            config = CreateConfig(false);
            if (config == 0)
                return null;

            var native = new NativeCursor(pixelMap, cursor, config);
            pixelMap = cursor = config = 0;
            return native;
        }
        finally
        {
            Release(ref pixelMap, ref cursor, ref config);
            if (options != 0)
                ReleaseOptions(options);
        }
    }

    internal int Apply(int window) => SetCustomCursor(window, handle, _config);

    protected override bool ReleaseHandle()
    {
        Release(ref _pixelMap, ref handle, ref _config);
        return true;
    }

    private static void Release(ref nint pixelMap, ref nint cursor, ref nint config)
    {
        if (config != 0)
            DestroyConfig(ref config);
        if (cursor != 0)
            DestroyCustomCursor(ref cursor);
        if (pixelMap != 0)
            DestroyPixelMap(ref pixelMap);
    }

    [LibraryImport(InputLibrary, EntryPoint = "OH_Input_CustomCursor_Create")]
    private static partial nint CreateCustomCursor(nint pixelMap, int anchorX, int anchorY);
    [LibraryImport(InputLibrary, EntryPoint = "OH_Input_CustomCursor_Destroy")]
    private static partial void DestroyCustomCursor(ref nint cursor);
    [LibraryImport(InputLibrary, EntryPoint = "OH_Input_CursorConfig_Create")]
    private static partial nint CreateConfig([MarshalAs(UnmanagedType.I1)] bool followSystem);
    [LibraryImport(InputLibrary, EntryPoint = "OH_Input_CursorConfig_Destroy")]
    private static partial void DestroyConfig(ref nint config);
    [LibraryImport(InputLibrary, EntryPoint = "OH_Input_SetCustomCursor")]
    private static partial int SetCustomCursor(int window, nint cursor, nint config);

    [LibraryImport(PixelMapLibrary, EntryPoint = "OH_PixelmapInitializationOptions_Create")]
    private static partial int CreateOptions(out nint options);
    [LibraryImport(PixelMapLibrary, EntryPoint = "OH_PixelmapInitializationOptions_Release")]
    private static partial int ReleaseOptions(nint options);
    [LibraryImport(PixelMapLibrary, EntryPoint = "OH_PixelmapInitializationOptions_SetWidth")]
    private static partial int SetWidth(nint options, uint width);
    [LibraryImport(PixelMapLibrary, EntryPoint = "OH_PixelmapInitializationOptions_SetHeight")]
    private static partial int SetHeight(nint options, uint height);
    [LibraryImport(PixelMapLibrary, EntryPoint = "OH_PixelmapInitializationOptions_SetPixelFormat")]
    private static partial int SetPixelFormat(nint options, int format);
    [LibraryImport(PixelMapLibrary, EntryPoint = "OH_PixelmapInitializationOptions_SetSrcPixelFormat")]
    private static partial int SetSourcePixelFormat(nint options, int format);
    [LibraryImport(PixelMapLibrary, EntryPoint = "OH_PixelmapInitializationOptions_SetAlphaType")]
    private static partial int SetAlphaType(nint options, int alphaType);
    [LibraryImport(PixelMapLibrary, EntryPoint = "OH_PixelmapNative_CreatePixelmap")]
    private static partial int CreatePixelMap(byte* pixels, nuint length, nint options, out nint pixelMap);
    [LibraryImport(PixelMapLibrary, EntryPoint = "OH_PixelmapNative_Destroy")]
    private static partial int DestroyPixelMap(ref nint pixelMap);
}
